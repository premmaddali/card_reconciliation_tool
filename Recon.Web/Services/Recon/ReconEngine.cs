using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Staging;

namespace Recon.Web.Services.Recon;

public record ReconRunResult(int ReconRunId, int ProcessorCount, int MatchedCount, int ExceptionCount, decimal AverageConfidence);
public record ReconProgress(string Stage, int Processed, int Total, int Matched);

public interface IReconEngine
{
    /// <summary>Runs one function (ATM/POS/Interchange → GL) over a processor batch and a GL batch.</summary>
    Task<ReconRunResult> RunAsync(ReconFunction function, int processorBatchId, int glBatchId,
        string periodKey, DateOnly? periodStart = null, DateOnly? periodEnd = null, CancellationToken ct = default,
        IProgress<ReconProgress>? progress = null, Func<ReconProgress, Task>? beforeAmbiguousPass = null);
}

/// <summary>
/// Weighted-confidence matcher. Amount and date are primary; reference and channel add weight.
/// Each processor transaction is scored against candidate GL cardholder-settlement rows of the same
/// currency; the best available candidate above the threshold is assigned (greedy, highest score first).
/// </summary>
public class ReconEngine(IDbContextFactory<AppDbContext> dbFactory, IOptions<MatchWeights> weights) : IReconEngine
{
    private readonly MatchWeights _w = weights.Value;

    // ---- in-memory row shapes ------------------------------------------------
    private sealed class GlRow
    {
        public int Id;
        public string? AcctNum;
        public int Ccy;
        public decimal Amount;      // abs TranAmt
        public DateOnly? Date;
        public string? Reference;   // TranParticular2
        public bool IsCash;         // TranParticular starts CASH
        public bool IsPos;          // starts POS
        public bool Used;
    }

    private sealed class ProcRow
    {
        public int Id;
        public string? AccountNumber;
        public StagingSource Source;
        public int? OriginalCcy;
        public decimal OriginalAmount; // abs transaction amount, retained for traceability
        public decimal? BillingAmount;
        public int? BillingCcy;
        public decimal? SettlementAmount;
        public int? SettlementCcy;
        public int? Ccy;              // currency used to reconcile against the GL
        public decimal Amount;         // amount used to reconcile against the GL
        public string? FxBasis;        // null for transaction-currency matches
        public DateOnly? Date;
        public string? Reference;
        public bool IsCash;
        public bool IsPos;
    }

    public async Task<ReconRunResult> RunAsync(ReconFunction function, int processorBatchId, int glBatchId,
        string periodKey, DateOnly? periodStart = null, DateOnly? periodEnd = null, CancellationToken ct = default,
        IProgress<ReconProgress>? progress = null, Func<ReconProgress, Task>? beforeAmbiguousPass = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var run = new ReconRun
        {
            Function = function, PeriodKey = periodKey, PeriodStart = periodStart, PeriodEnd = periodEnd,
            Status = ReconRunStatus.Running,
            ProcessorBatchId = processorBatchId, GlBatchId = glBatchId, StartedDate = DateTime.UtcNow
        };
        db.ReconRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            var gl = await LoadGlAsync(db, glBatchId, function, ct);
            var proc = await LoadProcessorAsync(db, processorBatchId, function, ct);
            progress?.Report(new("Preparing match candidates…", 0, proc.Count, 0));

            var (matches, exceptions) = await MatchAsync(run.ReconRunId, function, proc, gl, progress, beforeAmbiguousPass, ct);

            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.ReconMatches.AddRange(matches);
            db.ReconExceptions.AddRange(exceptions);
            db.ReconPassSummaries.AddRange(BuildSummaries(run.ReconRunId, matches));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.AutoDetectChangesEnabled = true;

            var matchedRows = matches.Where(m => m.MatchStatus == MatchStatus.Matched).ToList();
            run.ProcessorCount = proc.Count;
            run.MatchedCount = matchedRows.Count;
            run.ExceptionCount = exceptions.Count;
            // Confidence is meaningful only for matched rows; unmatched rows carry 0 and would skew it.
            run.AverageConfidence = matchedRows.Count > 0 ? Math.Round(matchedRows.Average(m => m.Confidence), 2) : 0m;
            run.Status = ReconRunStatus.Completed;
            run.CompletedDate = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return new ReconRunResult(run.ReconRunId, proc.Count, matchedRows.Count, exceptions.Count, run.AverageConfidence);
        }
        catch
        {
            run.Status = ReconRunStatus.Failed;
            run.CompletedDate = DateTime.UtcNow;
            db.ChangeTracker.AutoDetectChangesEnabled = true;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    // ---- loading -------------------------------------------------------------
    private static async Task<List<GlRow>> LoadGlAsync(AppDbContext db, int glBatchId, ReconFunction fn, CancellationToken ct)
    {
        // Cardholder-settlement accounts only; currency comes from the seeded account map.
        var accts = await db.GlSettlementAccounts
            .Where(a => a.IsActive && a.Role == GlAccountRole.CardholderSettlement)
            .ToDictionaryAsync(a => a.AcctNum, a => a.Ccy, ct);

        var raw = await db.StgGlEntries
            .Where(g => g.ImportBatchId == glBatchId && g.AcctNum != null && accts.Keys.Contains(g.AcctNum))
            .Select(g => new { g.StgGlEntryId, g.AcctNum, g.TranAmt, g.TranDate, g.TranParticular, g.TranParticular2 })
            .ToListAsync(ct);

        var rows = new List<GlRow>(raw.Count);
        foreach (var g in raw)
        {
            var part = (g.TranParticular ?? "").TrimStart().ToUpperInvariant();
            var isCash = part.StartsWith("CASH");
            var isPos = part.StartsWith("POS");
            // Channel scope: ATM function → CASH rows; POS → POS rows; Interchange → all.
            if (fn == ReconFunction.AtmToGl && !isCash) continue;
            if (fn == ReconFunction.PosToGl && !isPos) continue;
            rows.Add(new GlRow
            {
                Id = g.StgGlEntryId,
                AcctNum = g.AcctNum,
                // These cardholder accounts are denominated by card currency, but their GL amounts
                // are posted in MUR.  Do not use the account denomination to compare amounts.
                Ccy = 480,
                Amount = Math.Abs(g.TranAmt ?? 0m),
                Date = g.TranDate,
                Reference = g.TranParticular2?.Trim(),
                IsCash = isCash,
                IsPos = isPos
            });
        }
        return rows;
    }

    private static async Task<List<ProcRow>> LoadProcessorAsync(AppDbContext db, int batchId, ReconFunction fn, CancellationToken ct)
    {
        var settlementCurrencies = await db.GlSettlementAccounts
            .Where(a => a.IsActive && a.Role == GlAccountRole.CardholderSettlement)
            .Select(a => a.Ccy)
            .ToHashSetAsync(ct);

        if (fn is ReconFunction.AtmToGl or ReconFunction.PosToGl)
        {
            var rows = await db.StgDebitSettlements
                .Where(x => x.ImportBatchId == batchId)
                .Select(x => new { x.StgDebitSettlementId, x.Channel, x.CardNumberHashed, x.CardNumber, x.TxnCcy, x.TxnAmount, x.BillingCcy, x.BillingAmount, x.SettlementCcy, x.SettlementAmount, x.ProcessingDate, x.TransactionDate, x.ReferenceNumber })
                .ToListAsync(ct);
            return rows.Select(x =>
            {
                var recon = ReconciliationValue(x.TxnCcy, x.TxnAmount, x.SettlementCcy, x.SettlementAmount, x.BillingCcy, x.BillingAmount, settlementCurrencies);
                return new ProcRow
                {
                Id = x.StgDebitSettlementId,
                AccountNumber = x.CardNumberHashed ?? Mask(x.CardNumber),
                Source = x.Channel == SettlementChannel.Atm ? StagingSource.DebitSettlementAtm : StagingSource.DebitSettlementPos,
                OriginalCcy = x.TxnCcy,
                OriginalAmount = Math.Abs(x.TxnAmount ?? 0m),
                BillingAmount = x.BillingAmount is null ? null : Math.Abs(x.BillingAmount.Value),
                BillingCcy = x.BillingCcy,
                SettlementAmount = x.SettlementAmount is null ? null : Math.Abs(x.SettlementAmount.Value),
                SettlementCcy = x.SettlementCcy,
                Ccy = recon.Ccy,
                Amount = recon.Amount,
                FxBasis = recon.FxBasis,
                Date = x.ProcessingDate ?? x.TransactionDate,
                Reference = x.ReferenceNumber?.Trim(),
                IsCash = x.Channel == SettlementChannel.Atm,
                IsPos = x.Channel == SettlementChannel.Pos
                };
            }).ToList();
        }

        var ir = await db.StgInterchangeFees
            .Where(x => x.ImportBatchId == batchId)
            .Select(x => new { x.StgInterchangeFeeId, x.CardAccountNumber, x.CardNumberHashed, x.TxnCcy, x.TxnAmount, x.BillingCcy, x.BillingAmount, x.SettlementCcy, x.SettlementAmount, x.ProcessingDate, x.TransactionDate, x.ReferenceNumber, x.TransactionWording })
            .ToListAsync(ct);
        return ir.Select(x =>
        {
            var w = (x.TransactionWording ?? "").ToUpperInvariant();
            var cash = w.Contains("WITHDRAWAL") || w.Contains("CASH");
            var recon = ReconciliationValue(x.TxnCcy, x.TxnAmount, x.SettlementCcy, x.SettlementAmount, x.BillingCcy, x.BillingAmount, settlementCurrencies);
            return new ProcRow
            {
                Id = x.StgInterchangeFeeId,
                AccountNumber = x.CardAccountNumber ?? x.CardNumberHashed,
                Source = StagingSource.InterchangeFee,
                OriginalCcy = x.TxnCcy,
                OriginalAmount = Math.Abs(x.TxnAmount ?? 0m),
                BillingAmount = x.BillingAmount is null ? null : Math.Abs(x.BillingAmount.Value),
                BillingCcy = x.BillingCcy,
                SettlementAmount = x.SettlementAmount is null ? null : Math.Abs(x.SettlementAmount.Value),
                SettlementCcy = x.SettlementCcy,
                Ccy = recon.Ccy,
                Amount = recon.Amount,
                FxBasis = recon.FxBasis,
                Date = x.ProcessingDate ?? (x.TransactionDate.HasValue ? DateOnly.FromDateTime(x.TransactionDate.Value) : null),
                Reference = x.ReferenceNumber?.Trim(),
                IsCash = cash,
                IsPos = !cash
            };
        }).ToList();
    }

    // ---- matching ------------------------------------------------------------
    private async Task<(List<ReconMatch> matches, List<ReconException> exceptions)> MatchAsync(
        int runId, ReconFunction fn, List<ProcRow> proc, List<GlRow> gl, IProgress<ReconProgress>? progress,
        Func<ReconProgress, Task>? beforeAmbiguousPass, CancellationToken ct)
    {
        var window = fn == ReconFunction.AtmToGl ? _w.AtmDateToleranceDays : _w.DateToleranceDays;

        // Index GL by (currency, amount-in-cents) and by reference.
        var byAmount = new Dictionary<(int, long), List<GlRow>>();
        var byRef = new Dictionary<string, List<GlRow>>();
        foreach (var g in gl)
        {
            var key = (g.Ccy, Cents(g.Amount));
            (byAmount.TryGetValue(key, out var l1) ? l1 : byAmount[key] = new()).Add(g);
            if (!string.IsNullOrEmpty(g.Reference))
                (byRef.TryGetValue(g.Reference, out var l2) ? l2 : byRef[g.Reference] = new()).Add(g);
        }

        var matches = new List<ReconMatch>(proc.Count);
        var exceptions = new List<ReconException>();
        var matchedProc = new HashSet<int>();
        var matchedCount = 0;

        void RecordAssignment(ProcRow p, GlRow g, decimal score, string breakdown, bool amountOk)
        {
            var status = amountOk ? MatchStatus.Matched : MatchStatus.AmountMismatch;
            if (status == MatchStatus.Matched) matchedCount++;
            matches.Add(new ReconMatch
            {
                ReconRunId = runId,
                ReferenceNumber = p.Reference,
                ProcessorAccountNumber = p.AccountNumber,
                GlAccountNumber = g.AcctNum,
                MatchStatus = status,
                Confidence = Math.Round(score, 2),
                ScoreBreakdown = p.FxBasis is null ? breakdown : $"{breakdown} ({p.FxBasis} currency)",
                ProcessorStagingId = p.Id,
                ProcessorSource = p.Source,
                ProcessorAmount = p.OriginalAmount,
                ProcessorCcy = p.OriginalCcy,
                BillingAmount = p.BillingAmount,
                BillingCcy = p.BillingCcy,
                SettlementAmount = p.SettlementAmount,
                SettlementCcy = p.SettlementCcy,
                ReconciliationAmount = p.Amount,
                ProcessorDate = p.Date,
                GlStagingId = g.Id,
                GlAmount = g.Amount,
                GlCcy = g.Ccy,
                GlDate = g.Date,
                Ccy = p.Ccy
            });
            if (status == MatchStatus.AmountMismatch)
                exceptions.Add(Exc(runId, p, MatchStatus.AmountMismatch, score,
                    $"Reference matched but amount differs: processor {p.Amount:N2} vs GL {g.Amount:N2}.", g.Amount, g.AcctNum));
        }

        // Fast pass: a reference is safe to assign immediately only when it occurs exactly once
        // in both sources.  Duplicates deliberately remain for the scored pass below.
        var procByRef = proc.Where(p => !string.IsNullOrEmpty(p.Reference))
            .GroupBy(p => p.Reference!).ToDictionary(g => g.Key, g => g.ToList());
        var uniqueResolved = 0;
        foreach (var (reference, procRows) in procByRef)
        {
            if (procRows.Count != 1 || !byRef.TryGetValue(reference, out var glRows) || glRows.Count != 1)
                continue;

            var p = procRows[0];
            var g = glRows[0];
            var (score, breakdown, amountOk) = Score(p, g, window);
            g.Used = true;
            matchedProc.Add(p.Id);
            RecordAssignment(p, g, score, breakdown, amountOk);
            uniqueResolved++;
            if (uniqueResolved % 500 == 0)
            {
                progress?.Report(new("Resolving unique reference records…", uniqueResolved, proc.Count, matchedCount));
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
        var uniqueProgress = new ReconProgress("Unique-reference matching complete", uniqueResolved, proc.Count, matchedCount);
        progress?.Report(uniqueProgress);
        if (beforeAmbiguousPass is not null)
        {
            ct.ThrowIfCancellationRequested();
            await beforeAmbiguousPass(uniqueProgress);
            ct.ThrowIfCancellationRequested();
        }

        // Scored pass: only unresolved rows enter candidate generation.  This is where duplicate,
        // ambiguous and reference-less records are evaluated.
        var pairs = new List<(decimal score, string breakdown, bool amountOk, bool refMatch, ProcRow p, GlRow g)>();
        var evaluated = uniqueResolved;
        foreach (var p in proc)
        {
            if (matchedProc.Contains(p.Id)) continue;
            var seen = new HashSet<GlRow>();
            IEnumerable<GlRow> candidates = [];
            if (p.Ccy is int ccy && byAmount.TryGetValue((ccy, Cents(p.Amount)), out var byAmt))
                candidates = byAmt;
            if (!string.IsNullOrEmpty(p.Reference) && byRef.TryGetValue(p.Reference, out var byRf))
                candidates = candidates.Concat(byRf);

            foreach (var g in candidates)
            {
                if (!seen.Add(g)) continue;
                var (score, breakdown, amountOk) = Score(p, g, window);
                var refMatch = !string.IsNullOrEmpty(p.Reference) && p.Reference == g.Reference;
                if (score > 0) pairs.Add((score, breakdown, amountOk, refMatch, p, g));
            }
            evaluated++;
            if (evaluated % 500 == 0 || evaluated == proc.Count)
            {
                progress?.Report(new("Evaluating transaction records…", evaluated, proc.Count, 0));
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        pairs.Sort((a, b) => b.score.CompareTo(a.score));

        var assigned = uniqueResolved;
        foreach (var (score, breakdown, amountOk, refMatch, p, g) in pairs)
        {
            if (g.Used || matchedProc.Contains(p.Id)) continue;
            // Accept when amount ties and confidence clears the threshold, OR when the reference is an
            // exact identity match (even if the amount differs — that surfaces as an amount mismatch).
            var accept = (amountOk && score >= _w.MatchThreshold) || refMatch;
            if (!accept) continue;

            g.Used = true;
            matchedProc.Add(p.Id);
            RecordAssignment(p, g, score, breakdown, amountOk);
            assigned++;
            if (assigned % 500 == 0)
            {
                progress?.Report(new("Assigning best matches…", proc.Count, proc.Count, matchedCount));
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        // Processor rows with no assignment.
        foreach (var p in proc)
        {
            if (matchedProc.Contains(p.Id)) continue;
            matches.Add(new ReconMatch
            {
                ReconRunId = runId,
                ReferenceNumber = p.Reference,
                ProcessorAccountNumber = p.AccountNumber,
                MatchStatus = MatchStatus.UnmatchedProcessor,
                Confidence = 0m,
                ProcessorStagingId = p.Id,
                ProcessorSource = p.Source,
                ProcessorAmount = p.OriginalAmount,
                ProcessorCcy = p.OriginalCcy,
                BillingAmount = p.BillingAmount,
                BillingCcy = p.BillingCcy,
                SettlementAmount = p.SettlementAmount,
                SettlementCcy = p.SettlementCcy,
                ReconciliationAmount = p.Amount,
                ProcessorDate = p.Date,
                Ccy = p.Ccy
            });
            exceptions.Add(Exc(runId, p, MatchStatus.UnmatchedProcessor, 0m,
                $"No GL cardholder-settlement entry found for processor transaction {p.Reference} ({p.Amount:N2}{(p.FxBasis is null ? "" : $", {p.FxBasis} currency")}).", null, null));
        }

        // GL cardholder rows no processor transaction claimed.
        foreach (var g in gl)
        {
            if (g.Used) continue;
            exceptions.Add(new ReconException
            {
                ReconRunId = runId,
                ReferenceNumber = Cap(g.Reference),
                GlAccountNumber = g.AcctNum,
                ExceptionType = MatchStatus.UnmatchedGl,
                Ccy = g.Ccy,
                Confidence = 0m,
                ExpectedAmount = g.Amount,
                TxnDate = g.Date,
                Narrative = $"GL entry {g.Reference} ({g.Amount:N2}) has no matching processor transaction.",
                CreatedBy = CurrentUserService.SystemUserId,
                CreatedDate = DateTime.UtcNow
            });
        }

        progress?.Report(new("Finalising results…", proc.Count, proc.Count, matchedCount));
        return (matches, exceptions);
    }

    private (decimal score, string breakdown, bool amountOk) Score(ProcRow p, GlRow g, int window)
    {
        decimal score = 0m;
        var parts = new List<string>();

        var amountOk = Math.Abs(p.Amount - g.Amount) <= _w.AmountTolerance;
        if (amountOk) { score += _w.Amount; parts.Add($"amount agrees"); }

        if (p.Date is { } pd && g.Date is { } gd)
        {
            var dd = Math.Abs(pd.DayNumber - gd.DayNumber);
            if (dd == 0) { score += _w.Date; parts.Add("date agrees"); }
            else if (dd <= window)
            {
                var d = Math.Round(_w.Date * (1 - (decimal)dd / window), 1);
                if (d > 0) { score += d; parts.Add("date is close"); }
            }
        }

        if (!string.IsNullOrEmpty(p.Reference) && p.Reference == g.Reference)
        {
            score += _w.Reference;
            parts.Add("reference agrees");
            if (score < _w.ReferenceIdentityConfidence)
            {
                score = _w.ReferenceIdentityConfidence;
                parts.Add("exact reference baseline");
            }
            // An exact reference establishes the 75% identity baseline; a corroborating amount
            // must increase confidence rather than being swallowed by that baseline.
            if (amountOk)
                score = Math.Max(score, _w.ReferenceIdentityConfidence + _w.Amount);
        }

        var channelOk = (p.IsCash && g.IsCash) || (p.IsPos && g.IsPos);
        if (channelOk) { score += _w.Channel; parts.Add("channel agrees"); }

        return (Math.Min(100m, score), string.Join(", ", parts), amountOk);
    }

    private static ReconException Exc(int runId, ProcRow p, MatchStatus type, decimal conf, string narrative, decimal? glAmount, string? glAccount) => new()
    {
        ReconRunId = runId,
        ReferenceNumber = p.Reference,
        ProcessorAccountNumber = p.AccountNumber,
        GlAccountNumber = glAccount,
        ExceptionType = type,
        Ccy = p.Ccy,
        Confidence = Math.Round(conf, 2),
        ExpectedAmount = glAmount,
            ActualAmount = p.Amount,
        Difference = glAmount is null ? null : p.Amount - glAmount,
        TxnDate = p.Date,
        Narrative = narrative,
        CreatedBy = CurrentUserService.SystemUserId,
        CreatedDate = DateTime.UtcNow
    };

    // GL settlement currencies; every other transaction currency collapses into one "Other" bucket (0).
    private static int CcyBucket(int? ccy) => ccy is 480 or 840 or 978 ? ccy.Value : 0;

    private static List<ReconPassSummary> BuildSummaries(int runId, List<ReconMatch> matches)
    {
        var list = new List<ReconPassSummary>();

        // Daily per (bucketed) currency.
        foreach (var g in matches.GroupBy(m => (Ccy: CcyBucket(m.Ccy), Day: m.ProcessorDate)))
            list.Add(Summarise(runId, g.Key.Ccy, g.Key.Day, g));

        // Monthly per (bucketed) currency (Day = null).
        foreach (var g in matches.GroupBy(m => CcyBucket(m.Ccy)))
            list.Add(Summarise(runId, g.Key, null, g));

        return list;
    }

    private static ReconPassSummary Summarise(int runId, int ccy, DateOnly? day, IEnumerable<ReconMatch> rows)
    {
        var r = rows.ToList();
        var matched = r.Where(x => x.MatchStatus == MatchStatus.Matched).ToList();
        var exceptions = r.Where(x => x.MatchStatus != MatchStatus.Matched).ToList();
        return new ReconPassSummary
        {
            ReconRunId = runId,
            Ccy = ccy,
            Day = day,
            MatchedCount = matched.Count,
            MatchedAmount = matched.Sum(x => x.ReconciliationAmount ?? x.ProcessorAmount ?? 0m),
            ExceptionCount = exceptions.Count,
            ExceptionAmount = exceptions.Sum(x => x.ReconciliationAmount ?? x.ProcessorAmount ?? 0m),
            AverageConfidence = matched.Count > 0 ? Math.Round(matched.Average(x => x.Confidence), 2) : 0m
        };
    }

    private static long Cents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static string? Mask(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber)) return null;
        var value = cardNumber.Trim();
        return value.Length <= 4 ? "****" : $"****{value[^4..]}";
    }

    /// <summary>
    /// The BankOne cardholder-settlement GL holds the billing amount (including when the account
    /// itself carries a USD/EUR label). Its currency label is not dependable for the posted value,
    /// which is presented as MUR on the reconciliation. Fall back only when billing is absent.
    /// </summary>
    private static (int? Ccy, decimal Amount, string? FxBasis) ReconciliationValue(
        int? txnCcy, decimal? txnAmount, int? settlementCcy, decimal? settlementAmount,
        int? billingCcy, decimal? billingAmount, ISet<int> settlementCurrencies)
    {
        if (billingAmount.HasValue)
            return (480, Math.Abs(billingAmount.Value), "billing");

        if (txnCcy is int ccy && settlementCurrencies.Contains(ccy))
            return (ccy, Math.Abs(txnAmount ?? 0m), null);

        if (settlementCcy is int settleCcy && settlementCurrencies.Contains(settleCcy) && settlementAmount.HasValue)
            return (settleCcy, Math.Abs(settlementAmount.Value), "settlement");

        if (billingCcy is int billCcy && settlementCurrencies.Contains(billCcy) && billingAmount.HasValue)
            return (billCcy, Math.Abs(billingAmount.Value), "billing");

        return (txnCcy, Math.Abs(txnAmount ?? 0m), null);
    }

    /// <summary>Reference/particular fields are stored in a 32-char column; guard against longer text.</summary>
    private static string? Cap(string? s) => s is { Length: > 32 } ? s[..32] : s;
}
