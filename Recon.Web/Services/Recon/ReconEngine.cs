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
    /// <summary>Runs one function (ATM/POS/Interchange → GL) over a processor batch and a GL batch.
    /// <paramref name="schemeProfileId"/> tags the run with the owning scheme (null = legacy).</summary>
    Task<ReconRunResult> RunAsync(ReconFunction function, int processorBatchId, int glBatchId,
        string periodKey, DateOnly? periodStart = null, DateOnly? periodEnd = null, CancellationToken ct = default,
        IProgress<ReconProgress>? progress = null, Func<ReconProgress, Task>? beforeAmbiguousPass = null, int? schemeProfileId = null,
        bool applyAmountTolerance = false, decimal amountTolerance = 0m, bool enforcePostingPeriod = true,
        ReconAnalysisInputs? analysis = null);
}

/// <summary>
/// Weighted-confidence matcher. Amount and date are primary; the adapter-defined match key and channel add weight.
/// Each processor transaction is scored against candidate GL cardholder-settlement rows of the same
/// currency; a candidate is assigned only when it is the clear best choice on both sides.
/// </summary>
public class ReconEngine(IDbContextFactory<AppDbContext> dbFactory, IOptions<MatchWeights> weights) : IReconEngine
{
    private readonly MatchWeights _w = weights.Value;

    private sealed record CandidatePair(decimal Score, string Breakdown, bool AmountOk, bool RefMatch,
        ProcRow Processor, GlRow Gl, bool ToleranceApplied);

    // Keep only the best two pairs per node. Counts retain ambiguity evidence without storing
    // the complete candidate graph, which can be very large for common settlement amounts.
    private sealed class CandidateChoices
    {
        public int Count;
        public CandidatePair? Best;
        public CandidatePair? RunnerUp;
        public void Consider(CandidatePair pair)
        {
            Count++;
            static int Compare(CandidatePair a, CandidatePair b) =>
                a.RefMatch != b.RefMatch ? a.RefMatch.CompareTo(b.RefMatch) : a.Score.CompareTo(b.Score);
            if (Best is null || Compare(pair, Best) > 0) { RunnerUp = Best; Best = pair; }
            else if (RunnerUp is null || Compare(pair, RunnerUp) > 0) RunnerUp = pair;
        }
    }

    // ---- in-memory row shapes ------------------------------------------------
    private sealed class GlRow
    {
        public int Id;
        public string? AcctNum;
        public int Ccy;
        public decimal Amount;      // abs TranAmt
        public DateOnly? Date;
        public DateOnly? ValueDate;
        public DateTime? PostingDateTime;
        public string? Reference;   // adapter-defined standard match key
        public string? CreditReference; // Mastercard credit key: TranParticular2 after its twelve-character posting prefix
        public string? CardNumber;
        public string? Description;
        public ReconSettlementLeg Leg;
        public bool IsCash;         // TranParticular starts CASH
        public bool IsPos;          // starts POS
        public bool Used;
    }

    private sealed class ProcRow
    {
        public int Id;
        public string? AccountNumber;
        public string? CardNumber;
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
        public DateTime? TransactionDateTime;
        public string? ProductType;
        public string? Merchant;
        public string? Description;
        public string? Reference;
        public string? MicrofilmReference;
        public string? MatchKey;
        public string? TransactionCode;
        public ReconSettlementLeg Leg;
        public bool IsCash;
        public bool IsPos;
    }

    public async Task<ReconRunResult> RunAsync(ReconFunction function, int processorBatchId, int glBatchId,
        string periodKey, DateOnly? periodStart = null, DateOnly? periodEnd = null, CancellationToken ct = default,
        IProgress<ReconProgress>? progress = null, Func<ReconProgress, Task>? beforeAmbiguousPass = null, int? schemeProfileId = null,
        bool applyAmountTolerance = false, decimal amountTolerance = 0m, bool enforcePostingPeriod = true,
        ReconAnalysisInputs? analysis = null)
    {
        if (function == ReconFunction.AtmToGl)
            throw new InvalidOperationException("ATM-to-GL reconciliation is not available: Mastercard currently has no ATM General Ledger. Use Network ↔ ATM reconciliation instead.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (_w.AmountTolerance < 0 || _w.MinimumScoreGap <= 0 || amountTolerance < 0 || _w.DateToleranceDays is < 0 or > 365)
            throw new InvalidOperationException("Amount tolerance must be nonnegative and the candidate score gap must be positive.");
        await MatchingPeriod.ValidateAsync(db, function, processorBatchId, glBatchId, periodKey, periodStart, periodEnd, schemeProfileId, ct, enforcePostingPeriod);

        var run = new ReconRun
        {
            Function = function, PeriodKey = periodKey, PeriodStart = periodStart, PeriodEnd = periodEnd,
            Status = ReconRunStatus.Running,
            ProcessorBatchId = processorBatchId, GlBatchId = glBatchId, StartedDate = DateTime.UtcNow,
            SchemeProfileId = schemeProfileId,
            AnalysisOfRunId = analysis?.SourceRunId,
            PeriodGranularity = analysis?.Granularity ?? PeriodGranularity.Month,
            ApplyAmountTolerance = applyAmountTolerance && amountTolerance > _w.AmountTolerance,
            AmountTolerance = applyAmountTolerance && amountTolerance > _w.AmountTolerance ? amountTolerance : null
        };
        db.ReconRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            await using var assignmentTransaction = await db.Database.BeginTransactionAsync(ct);
            await GlAssignmentLock.AcquireAsync(db, ct);
            var gl = analysis is null ? await LoadGlAsync(db, glBatchId, function, ct) : new List<GlRow>();
            if (analysis is not null)
                foreach (var batch in analysis.GlBatchIds)
                    gl.AddRange(await LoadGlLegacyAsync(db, batch, function, ct));
            if (analysis is not null || !await db.NormalizedGlEntries.AnyAsync(x => x.ImportBatchId == glBatchId, ct))
            {
                var originalId = analysis?.SourceRunId;
                var reserved = (await db.ReconMatches.Where(x => x.ReconRun.AnalysisOfRunId == null && x.ReconRunId != originalId
                    && x.MatchedAcrossPeriods && x.GlStagingId != null && x.ReconRun.ProcessorBatchId != processorBatchId)
                    .Select(x => x.GlStagingId!.Value).ToListAsync(ct)).ToHashSet();
                gl.RemoveAll(x => reserved.Contains(x.Id));
            }
            var proc = analysis is null ? await LoadProcessorAsync(db, processorBatchId, function, ct) : new List<ProcRow>();
            if (analysis is not null)
                foreach (var batch in analysis.ProcessorBatchIds)
                    proc.AddRange(await LoadProcessorLegacyAsync(db, batch, function, ct));
            progress?.Report(new("Preparing match candidates…", 0, proc.Count, 0));

            var effectiveAmountTolerance = run.AmountTolerance ?? _w.AmountTolerance;
            var (matches, exceptions) = await MatchAsync(run.ReconRunId, function, proc, gl, effectiveAmountTolerance, progress, beforeAmbiguousPass, ct);

            // Phase 2: Carry forward manual resolutions and comments from prior runs of this batch/period
            await CarryForwardResolutionsAsync(db, run, exceptions, ct);

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

            await assignmentTransaction.CommitAsync(ct);
            return new ReconRunResult(run.ReconRunId, proc.Count, matchedRows.Count, exceptions.Count, run.AverageConfidence);
        }
        catch
        {
            db.ChangeTracker.AutoDetectChangesEnabled = true;
            db.ChangeTracker.Clear();
            var failedRun = await db.ReconRuns.FindAsync([run.ReconRunId], ct);
            if (failedRun is not null)
            {
                failedRun.Status = ReconRunStatus.Failed;
                failedRun.CompletedDate = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            throw;
        }
    }

    // ---- loading -------------------------------------------------------------
    /// <summary>
    /// Loads GL rows from the normalized platform model when the batch has been projected;
    /// falls back to the legacy BankOne loader otherwise. Channel scope by function is applied
    /// identically in both paths.
    /// </summary>
    private static async Task<List<GlRow>> LoadGlAsync(AppDbContext db, int glBatchId, ReconFunction fn, CancellationToken ct)
    {
        var normalized = await LoadGlNormalizedAsync(db, glBatchId, fn, ct);
        if (normalized is not null) return normalized;
        return await LoadGlLegacyAsync(db, glBatchId, fn, ct);
    }

    private static async Task<List<GlRow>?> LoadGlNormalizedAsync(AppDbContext db, int glBatchId, ReconFunction fn, CancellationToken ct)
    {
        var any = await db.NormalizedGlEntries.AnyAsync(g => g.ImportBatchId == glBatchId, ct);
        if (!any) return null;

        var raw = await db.NormalizedGlEntries
            .Where(g => g.ImportBatchId == glBatchId)
            .Select(g => new { g.NormalizedGlEntryId, g.SourceStagingId, g.SourceTable, g.Account, g.Currency, g.Amount, g.PostingDate, g.PostingDateTime, g.Reference, g.Description, g.SettlementLeg, g.IsCash, g.IsPos })
            .ToListAsync(ct);

        var nativeCurrencies = await db.StgGlEntries.Where(g => g.ImportBatchId == glBatchId)
            .Select(g => new { g.StgGlEntryId, g.TranCcyCode }).ToDictionaryAsync(g => g.StgGlEntryId, g => g.TranCcyCode, ct);
        var nativeCards = await db.StgGlEntries.Where(g => g.ImportBatchId == glBatchId)
            .Select(g => new { g.StgGlEntryId, g.TranRmks, g.TranParticular, g.ValueDate }).ToDictionaryAsync(g => g.StgGlEntryId, g => g, ct);

        var rows = new List<GlRow>(raw.Count);
        foreach (var g in raw)
        {
            // Channel scope: ATM function → CASH rows; POS → POS rows; Interchange → all.
            if (fn == ReconFunction.AtmToGl && !g.IsCash) continue;
            if (fn == ReconFunction.PosToGl && !g.IsPos) continue;
            rows.Add(new GlRow
            {
                Id = checked((int)g.NormalizedGlEntryId),
                AcctNum = g.Account,
                CardNumber = g.SourceTable == nameof(StgGlEntry) && g.SourceStagingId is int cardId && nativeCards.TryGetValue(cardId, out var remarks) ? CardIdentity.Normalize(remarks.TranRmks) : null,
                Ccy = g.SourceTable == nameof(StgGlEntry) && g.SourceStagingId is int nativeId && nativeCurrencies.TryGetValue(nativeId, out var nativeCurrency)
                    ? MatchingEvidence.GlCurrency(nativeCurrency) : g.Currency,
                Leg = (ReconSettlementLeg)g.SettlementLeg,
                Amount = g.Amount,
                Date = g.PostingDate,
                ValueDate = g.SourceTable == nameof(StgGlEntry) && g.SourceStagingId is int valueId && nativeCards.TryGetValue(valueId, out var valueRow)
                    ? valueRow.ValueDate : null,
                PostingDateTime = g.PostingDateTime,
                Reference = g.Reference,
                CreditReference = MastercardCreditGlKey(g.Reference),
                Description = g.SourceTable == nameof(StgGlEntry) && g.SourceStagingId is int descriptionId && nativeCards.TryGetValue(descriptionId, out var native)
                    ? CombineDescription(native.TranParticular, native.TranRmks) : g.Description,
                IsCash = g.IsCash,
                IsPos = g.IsPos
            });
        }
        return rows;
    }

    private static async Task<List<GlRow>> LoadGlLegacyAsync(AppDbContext db, int glBatchId, ReconFunction fn, CancellationToken ct)
    {
        // Both bank-account legs are loaded. Each interchange record is routed to exactly one
        // settlement leg: credit-card rows use the intermediary account; debit-card rows use
        // the cardholder account.
        var accts = await db.GlSettlementAccounts
            .Where(a => a.IsActive && (a.Role == GlAccountRole.CardholderSettlement || a.Role == GlAccountRole.IntermediarySettlement))
            .ToDictionaryAsync(a => a.AcctNum, a => a.Role == GlAccountRole.CardholderSettlement ? ReconSettlementLeg.DebitCardholder : ReconSettlementLeg.CreditIntermediary, ct);

        var raw = await db.StgGlEntries
            .Where(g => g.ImportBatchId == glBatchId && g.AcctNum != null && accts.Keys.Contains(g.AcctNum))
            .Select(g => new { g.StgGlEntryId, g.AcctNum, g.TranAmt, g.TranCcyCode, g.TranDate, g.ValueDate, g.EntryDate, g.VfdDate, g.TranParticular, g.TranParticular2, g.TranRmks })
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
                CardNumber = CardIdentity.Normalize(g.TranRmks),
                // Compare the currency attached to the posted amount, not account denomination.
                Ccy = MatchingEvidence.GlCurrency(g.TranCcyCode),
                Leg = accts[g.AcctNum!],
                Amount = Math.Abs(g.TranAmt ?? 0m),
                Date = g.TranDate,
                ValueDate = g.ValueDate,
                PostingDateTime = g.EntryDate ?? g.VfdDate,
                Reference = g.TranParticular2?.Trim(),
                CreditReference = MastercardCreditGlKey(g.TranParticular2),
                Description = CombineDescription(g.TranParticular, g.TranRmks),
                IsCash = isCash,
                IsPos = isPos
            });
        }
        return rows;
    }

    private static async Task<List<ProcRow>> LoadProcessorAsync(AppDbContext db, int batchId, ReconFunction fn, CancellationToken ct)
    {
        var normalized = await LoadProcessorNormalizedAsync(db, batchId, fn, ct);
        if (normalized is not null) return normalized;
        return await LoadProcessorLegacyAsync(db, batchId, fn, ct);
    }

    private static async Task<List<ProcRow>?> LoadProcessorNormalizedAsync(AppDbContext db, int batchId, ReconFunction fn, CancellationToken ct)
    {
        var any = await db.NormalizedTransactions.AnyAsync(t => t.ImportBatchId == batchId, ct);
        if (!any) return null;

        var settlementCurrencies = await db.GlSettlementAccounts
            .Where(a => a.IsActive && a.Role == GlAccountRole.CardholderSettlement)
            .Select(a => a.Ccy)
            .ToHashSetAsync(ct);

        var rows = await db.NormalizedTransactions
            .Where(t => t.ImportBatchId == batchId)
            .Select(t => new { t.NormalizedTransactionId, t.Processor, t.SourceStagingId, t.MaskedCardNumber, t.CardType, t.Merchant, t.Currency, t.Amount, t.BillingAmount, t.BillingCurrency, t.SettlementAmount, t.SettlementCurrency, t.PostingDate, t.TransactionDate, t.TransactionDateTime, t.Reference, t.TraceNumber, t.Description, t.TransactionType, t.IsCash, t.IsPos, t.SettlementLeg })
            .ToListAsync(ct);

        // BankOne marks On-Us ATM withdrawals with Origin code 0.  They are deliberately
        // retained in staging for review, but have no corresponding GL posting and must not
        // become reconciliation exceptions.  Read the staging value here so this also works
        // for normalized batches projected before this rule was introduced.
        var onUsAtmStagingIds = fn == ReconFunction.InterchangeToGl
            ? await db.StgInterchangeFees.Where(x => x.ImportBatchId == batchId && x.OrigineCode != null && x.OrigineCode.Trim() == "0")
                .Select(x => x.StgInterchangeFeeId).ToHashSetAsync(ct)
            : [];

        var result = new List<ProcRow>(rows.Count);
        var nativeTransactions = await db.StgInterchangeFees.Where(t => t.ImportBatchId == batchId)
            .Select(t => new { t.StgInterchangeFeeId, t.CardAccountEntityId, t.ProductType, t.BillingAmount, t.BillingCcy, t.IssMarkupAmount })
            .ToDictionaryAsync(t => t.StgInterchangeFeeId, ct);
        foreach (var x in rows)
        {
            if (x.SourceStagingId is int stagingId && onUsAtmStagingIds.Contains(stagingId)) continue;
            // Channel scope mirrors the legacy loader: ATM function → cash rows only; POS → POS only.
            if (fn == ReconFunction.AtmToGl && !x.IsCash) continue;
            if (fn == ReconFunction.PosToGl && !x.IsPos) continue;

            // FX fallback chain: settlement → billing → transaction (adapter already normalized amounts).
            int? ccy = null; decimal amount = 0m; string? fxBasis = null;
            if (x.SourceStagingId is int nativeId && nativeTransactions.TryGetValue(nativeId, out var native) && MatchingEvidence.IsCredit(native.ProductType))
                (ccy, amount, fxBasis) = MatchingEvidence.BillingValue(x.Currency, x.Amount, x.SettlementCurrency, x.SettlementAmount,
                    native.BillingCcy, native.BillingAmount, native.ProductType, native.IssMarkupAmount);
            else if (MatchingEvidence.IsCredit(x.CardType))
                (ccy, amount, fxBasis) = MatchingEvidence.BillingValue(x.Currency, x.Amount, x.SettlementCurrency, x.SettlementAmount,
                    x.BillingCurrency, x.BillingAmount, x.CardType);
            else if (x.Processor == "BankOne")
                (ccy, amount, fxBasis) = MatchingEvidence.BillingValue(x.Currency, x.Amount, x.SettlementCurrency, x.SettlementAmount, x.BillingCurrency, x.BillingAmount);
            else if (x.SettlementCurrency is int sc && settlementCurrencies.Contains(sc))
                (ccy, amount, fxBasis) = (sc, Math.Abs(x.SettlementAmount ?? 0m), "settlement");
            else if (x.BillingCurrency is int bc && settlementCurrencies.Contains(bc))
                (ccy, amount, fxBasis) = (bc, Math.Abs(x.BillingAmount ?? 0m), "billing");
            else
                (ccy, amount, fxBasis) = (x.Currency, Math.Abs(x.Amount ?? 0m), null);

            result.Add(new ProcRow
            {
                Id = checked((int)x.NormalizedTransactionId),
                AccountNumber = x.MaskedCardNumber,
                CardNumber = x.Processor == "BankOne" && x.SourceStagingId is int cardId && nativeTransactions.TryGetValue(cardId, out var cardRow) ? cardRow.CardAccountEntityId : null,
                Source = StagingSource.InterchangeFee, // provenance kept via NormalizedTransactionId + batch
                Leg = (ReconSettlementLeg)x.SettlementLeg,
                OriginalCcy = x.Currency,
                OriginalAmount = Math.Abs(x.Amount ?? 0m),
                BillingAmount = x.BillingAmount is null ? null : Math.Abs(x.BillingAmount.Value),
                BillingCcy = x.BillingCurrency,
                SettlementAmount = x.SettlementAmount is null ? null : Math.Abs(x.SettlementAmount.Value),
                SettlementCcy = x.SettlementCurrency,
                Ccy = ccy,
                Amount = amount,
                FxBasis = fxBasis,
                Date = x.PostingDate ?? x.TransactionDate,
                TransactionDateTime = x.TransactionDateTime,
                ProductType = x.CardType,
                Merchant = x.Merchant,
                Description = x.Description,
                Reference = x.Reference?.Trim(),
                MicrofilmReference = x.TraceNumber?.Trim(),
                MatchKey = string.Equals(x.CardType, "CR", StringComparison.OrdinalIgnoreCase)
                    ? x.TraceNumber?.Trim()
                    : x.Reference?.Trim(),
                TransactionCode = x.TransactionType?.Trim(),
                IsCash = x.IsCash,
                IsPos = x.IsPos
            });
        }
        return result;
    }

    private static async Task<List<ProcRow>> LoadProcessorLegacyAsync(AppDbContext db, int batchId, ReconFunction fn, CancellationToken ct)
    {
        var settlementCurrencies = await db.GlSettlementAccounts
            .Where(a => a.IsActive && a.Role == GlAccountRole.CardholderSettlement)
            .Select(a => a.Ccy)
            .ToHashSetAsync(ct);

        if (fn is ReconFunction.AtmToGl or ReconFunction.PosToGl)
        {
            var rows = await db.StgDebitSettlements
                .Where(x => x.ImportBatchId == batchId)
                .Select(x => new { x.StgDebitSettlementId, x.Channel, x.CardNumberHashed, x.CardNumber, x.MerchantAcronym, x.TransactionWording, x.TxnCcy, x.TxnAmount, x.BillingCcy, x.BillingAmount, x.SettlementCcy, x.SettlementAmount, x.ProcessingDate, x.TransactionDate, x.ReferenceNumber })
                .ToListAsync(ct);
            return rows.Select(x =>
            {
                var recon = ReconciliationValue(x.TxnCcy, x.TxnAmount, x.SettlementCcy, x.SettlementAmount, x.BillingCcy, x.BillingAmount, settlementCurrencies);
                return new ProcRow
                {
                    Id = x.StgDebitSettlementId,
                    AccountNumber = x.CardNumberHashed ?? Mask(x.CardNumber),
                    Source = x.Channel == SettlementChannel.Atm ? StagingSource.DebitSettlementAtm : StagingSource.DebitSettlementPos,
                    Leg = ReconSettlementLeg.DebitCardholder,
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
                    TransactionDateTime = x.TransactionDate.HasValue ? x.TransactionDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                    Merchant = x.MerchantAcronym,
                    Description = x.TransactionWording,
                    Reference = x.ReferenceNumber?.Trim(),
                    MatchKey = x.ReferenceNumber?.Trim(),
                    IsCash = x.Channel == SettlementChannel.Atm,
                    IsPos = x.Channel == SettlementChannel.Pos
                };
            }).ToList();
        }

        var ir = await db.StgInterchangeFees
            .Where(x => x.ImportBatchId == batchId && (x.OrigineCode == null || x.OrigineCode.Trim() != "0"))
            .Select(x => new { x.StgInterchangeFeeId, x.CardAccountNumber, x.CardNumberHashed, x.CardAccountEntityId, x.ProductType, x.MerchantAcronym, x.TxnCcy, x.TxnAmount, x.BillingCcy, x.BillingAmount, x.IssMarkupAmount, x.SettlementCcy, x.SettlementAmount, x.ProcessingDate, x.TransactionDate, x.ReferenceNumber, x.MicrofilmRefNumber, x.TransactionCode, x.TransactionWording })
            .ToListAsync(ct);
        return ir.Select(x =>
        {
            var w = (x.TransactionWording ?? "").ToUpperInvariant();
            var cash = w.Contains("WITHDRAWAL") || w.Contains("CASH");
            (int? Ccy, decimal Amount, string? FxBasis) recon = MatchingEvidence.BillingValue(x.TxnCcy, x.TxnAmount, x.SettlementCcy, x.SettlementAmount,
                x.BillingCcy, x.BillingAmount, x.ProductType, x.IssMarkupAmount);
            return new ProcRow
            {
                Id = x.StgInterchangeFeeId,
                AccountNumber = x.CardAccountNumber ?? x.CardNumberHashed,
                CardNumber = x.CardAccountEntityId,
                Source = StagingSource.InterchangeFee,
                Leg = MatchingEvidence.IsCredit(x.ProductType) ? ReconSettlementLeg.CreditIntermediary : ReconSettlementLeg.DebitCardholder,
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
                TransactionDateTime = x.TransactionDate,
                ProductType = x.ProductType?.Trim().ToUpperInvariant(),
                Merchant = x.MerchantAcronym,
                Description = x.TransactionWording,
                Reference = x.ReferenceNumber?.Trim(),
                MicrofilmReference = x.MicrofilmRefNumber?.Trim(),
                // Credit-card GL matching uses MICROFILM REF NUMBER, never REFERENCE NUMBER.
                MatchKey = MatchingEvidence.IsCredit(x.ProductType)
                    ? x.MicrofilmRefNumber?.Trim()
                    : x.ReferenceNumber?.Trim(),
                TransactionCode = x.TransactionCode?.Trim(),
                IsCash = cash,
                IsPos = !cash
            };
        }).ToList();
    }

    // ---- matching ------------------------------------------------------------
    private async Task<(List<ReconMatch> matches, List<ReconException> exceptions)> MatchAsync(
        int runId, ReconFunction fn, List<ProcRow> proc, List<GlRow> gl, decimal amountTolerance, IProgress<ReconProgress>? progress,
        Func<ReconProgress, Task>? beforeAmbiguousPass, CancellationToken ct)
    {
        var window = fn == ReconFunction.AtmToGl ? _w.AtmDateToleranceDays : _w.DateToleranceDays;

        // Index GL by reconciliation leg + currency/amount and leg + adapter-defined match key. Product type
        // has already routed each processor transaction to its one applicable settlement leg.
        var byAmount = gl.Where(g => g.Date.HasValue).GroupBy(g => (g.Leg, g.Ccy, g.Date!.Value.DayNumber))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Amount).ToArray());
        var byRef = new Dictionary<(ReconSettlementLeg, string), List<GlRow>>();
        void AddReference(string? reference, GlRow row)
        {
            if (!string.IsNullOrEmpty(reference))
                (byRef.TryGetValue((row.Leg, reference), out var matches) ? matches : byRef[(row.Leg, reference)] = new()).Add(row);
        }
        foreach (var g in gl)
        {
            AddReference(g.Leg == ReconSettlementLeg.CreditIntermediary ? g.CreditReference : g.Reference, g);
        }

        var matches = new List<ReconMatch>(proc.Count);
        var exceptions = new List<ReconException>();
        var matchedProc = new HashSet<int>();
        var matchedCount = 0;

        void RecordAssignment(ProcRow p, GlRow g, decimal score, string breakdown, bool amountOk, bool toleranceApplied)
        {
            var status = amountOk ? MatchStatus.Matched : MatchStatus.AmountMismatch;
            if (status == MatchStatus.Matched) matchedCount++;
            matches.Add(new ReconMatch
            {
                ReconRunId = runId,
                ReferenceNumber = p.Reference,
                MicrofilmRefNumber = p.MicrofilmReference,
                ProcessorAccountNumber = p.AccountNumber,
                ProcessorProductType = p.ProductType,
                ProcessorMerchant = p.Merchant,
                ProcessorDescription = p.Description,
                GlAccountNumber = g.AcctNum,
                GlReference = p.Leg == ReconSettlementLeg.CreditIntermediary ? g.CreditReference : g.Reference,
                // Analyst-facing comparison value: debit uses the full particular; credit removes
                // its twelve-character posting prefix before comparing to Microfilm Reference.
                GlParticular2 = p.Leg == ReconSettlementLeg.CreditIntermediary ? g.CreditReference : g.Reference,
                GlDescription = DisplayGlDescription(p, g),
                SettlementLeg = g.Leg,
                MatchStatus = status,
                Confidence = Math.Round(score, 2),
                ScoreBreakdown = p.FxBasis is null ? breakdown : $"{breakdown} ({p.FxBasis} currency)",
                MatchComment = toleranceApplied ? $"Tolerance applied: difference {Math.Abs(p.Amount - g.Amount):N2} is within the configured {amountTolerance:N2}." : null,
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
                ProcessorTransactionDateTime = p.TransactionDateTime,
                GlStagingId = g.Id,
                GlAmount = g.Amount,
                GlCcy = g.Ccy,
                GlDate = g.Date,
                GlValueDate = g.ValueDate,
                GlPostingDateTime = g.PostingDateTime,
                Ccy = p.Ccy
            });
            if (status == MatchStatus.AmountMismatch)
                exceptions.Add(Exc(runId, p, g.Leg, MatchStatus.AmountMismatch, score,
                    $"Reference matched but amount differs: processor {p.Amount:N2} vs GL {g.Amount:N2}.", g.Amount, g.AcctNum));
        }

        // Fast pass: a reference is safe to assign immediately only when it occurs exactly once
        // in both sources.  Duplicates deliberately remain for the scored pass below.
        // Distinct transaction codes may share a reference, but must compete for GL evidence.
        // Only one processor across all event codes may enter this immediate-assignment pass.
        var procByRef = proc.Where(p => !string.IsNullOrEmpty(p.MatchKey))
            .GroupBy(p => (p.Leg, p.MatchKey!))
            .ToDictionary(g => g.Key, g => g.ToList());
        var uniqueResolved = 0;
        foreach (var (_, procRows) in procByRef)
        {
            if (procRows.Count != 1) continue;
            var p = procRows[0];
            if (!byRef.TryGetValue((p.Leg, p.MatchKey!), out var glRows) || glRows.Count != 1) continue;

            var g = glRows[0];
            if (g.Used || p.Ccy is null || p.Ccy <= 0 || p.Ccy != g.Ccy || !ReferenceMatches(p, g) || !ReferencesCompatible(p, g)) continue;
            var (score, breakdown, amountOk, toleranceApplied) = Score(p, g, window, amountTolerance);
            g.Used = true;
            matchedProc.Add(p.Id);
            RecordAssignment(p, g, score, breakdown, amountOk, toleranceApplied);
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

        // Withdrawal settlement uses the transaction's calendar date, rather than its processing
        // date. Equal amounts on that day are interchangeable, but each GL is consumed once.
        if (fn == ReconFunction.InterchangeToGl)
        {
            var cashGl = gl.Where(g => !g.Used && g.IsCash && g.ValueDate.HasValue && g.Ccy > 0)
                .GroupBy(g => (g.Leg, g.Ccy, Date: g.ValueDate!.Value, g.Amount))
                .ToDictionary(g => g.Key, g => new Queue<GlRow>(g.OrderBy(x => x.Id)));
            foreach (var p in proc.Where(p => !matchedProc.Contains(p.Id) &&
                AtmWithdrawalMatching.IsWithdrawal(p.TransactionCode, p.Description)))
            {
                if (p.TransactionDateTime is not { } timestamp || p.Ccy is not > 0) continue;
                var key = (p.Leg, p.Ccy.Value, DateOnly.FromDateTime(timestamp), p.Amount);
                if (!cashGl.TryGetValue(key, out var candidates) || !candidates.TryDequeue(out var g)) continue;
                g.Used = true;
                matchedProc.Add(p.Id);
                RecordAssignment(p, g, 78m, AtmWithdrawalMatching.Evidence, true, false);
            }
        }

        // Scored pass: only unresolved rows enter candidate generation.  This is where duplicate,
        // ambiguous and reference-less records are evaluated.
        var procChoices = new Dictionary<int, CandidateChoices>();
        var glChoices = new Dictionary<int, CandidateChoices>();
        var evaluated = matchedProc.Count;
        foreach (var p in proc)
        {
            if (matchedProc.Contains(p.Id)) continue;
            var seen = new HashSet<GlRow>();
            IEnumerable<GlRow> candidates = [];
            if (p.Ccy is int ccy && p.Date is { } processorDate)
                candidates = Enumerable.Range(-window, 2 * window + 1)
                    .SelectMany(offset => byAmount.TryGetValue((p.Leg, ccy, processorDate.DayNumber + offset), out var byAmt)
                        ? AmountCandidates(byAmt, p.Amount, amountTolerance) : []);
            if (!string.IsNullOrEmpty(p.MatchKey) && byRef.TryGetValue((p.Leg, p.MatchKey), out var byRf))
                candidates = candidates.Concat(byRf);

            foreach (var g in candidates)
            {
                if (g.Used || !seen.Add(g) || p.Ccy is null || p.Ccy <= 0 || p.Ccy != g.Ccy || !ReferencesCompatible(p, g)) continue;
                var (score, breakdown, amountOk, toleranceApplied) = Score(p, g, window, amountTolerance);
                var refMatch = ReferenceMatches(p, g);
                if (refMatch || (amountOk && score >= _w.MatchThreshold))
                {
                    var pair = new CandidatePair(score, breakdown, amountOk, refMatch, p, g, toleranceApplied);
                    if (!procChoices.TryGetValue(p.Id, out var processorChoice)) procChoices[p.Id] = processorChoice = new();
                    if (!glChoices.TryGetValue(g.Id, out var glChoice)) glChoices[g.Id] = glChoice = new();
                    processorChoice.Consider(pair);
                    glChoice.Consider(pair);
                }
            }
            evaluated++;
            if (evaluated % 500 == 0 || evaluated == proc.Count)
            {
                progress?.Report(new("Evaluating transaction records…", evaluated, proc.Count, matchedCount));
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        // Remaining scored pairs require a clear best choice on BOTH sides.
        // Do not resolve a tie by source order or by eliminating competing rows first.
        var assigned = matchedProc.Count;
        foreach (var p in proc.Where(p => !matchedProc.Contains(p.Id)))
        {
            if (!procChoices.TryGetValue(p.Id, out var choices)) continue;
            var best = choices.Best!;
            var reverse = glChoices[best.Gl.Id];
            bool Clear(decimal score, bool reference, decimal otherScore, bool otherReference) =>
                reference && !otherReference || reference == otherReference && score - otherScore >= _w.MinimumScoreGap;
            if (choices.RunnerUp is { } next && !Clear(best.Score, best.RefMatch, next.Score, next.RefMatch)) continue;
            if (reverse.Best!.Processor.Id != p.Id || (reverse.RunnerUp is { } rival && !Clear(best.Score, best.RefMatch, rival.Score, rival.RefMatch))) continue;
            if (best.Gl.Used) continue;
            best.Gl.Used = true;
            matchedProc.Add(p.Id);
            RecordAssignment(p, best.Gl, best.Score, best.Breakdown, best.AmountOk, best.ToleranceApplied);
            assigned++;
            if (assigned % 500 == 0)
            {
                progress?.Report(new("Assigning unambiguous matches…", proc.Count, proc.Count, matchedCount));
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        // Processor rows with no assignment.
        foreach (var p in proc)
        {
            if (matchedProc.Contains(p.Id)) continue;
            var optionCount = procChoices.TryGetValue(p.Id, out var choices) ? choices.Count : 0;
            var unresolvedStatus = optionCount > 0 ? MatchStatus.Ambiguous : MatchStatus.UnmatchedProcessor;
            var narrative = optionCount > 0
                ? $"Review required: {optionCount} eligible GL candidates; no clear mutual best assignment."
                : $"No GL settlement entry found in the {p.Leg} leg for processor transaction {p.Reference} ({p.Amount:N2}).";
            matches.Add(new ReconMatch
            {
                SettlementLeg = p.Leg,
                ReconRunId = runId,
                ReferenceNumber = p.Reference,
                MicrofilmRefNumber = p.MicrofilmReference,
                ProcessorAccountNumber = p.AccountNumber,
                ProcessorProductType = p.ProductType,
                ProcessorMerchant = p.Merchant,
                ProcessorDescription = p.Description,
                MatchStatus = unresolvedStatus,
                MatchComment = narrative,
                ScoreBreakdown = narrative,
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
                ProcessorTransactionDateTime = p.TransactionDateTime,
                Ccy = p.Ccy
            });
            exceptions.Add(Exc(runId, p, p.Leg, unresolvedStatus, 0m, narrative, null, null));
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
                GlStagingId = g.Id,
                SettlementLeg = g.Leg,
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

    private (decimal score, string breakdown, bool amountOk, bool toleranceApplied) Score(ProcRow p, GlRow g, int window, decimal amountTolerance)
    {
        decimal score = 0m;
        var parts = new List<string>();

        var difference = Math.Abs(p.Amount - g.Amount);
        var amountOk = difference <= amountTolerance;
        var toleranceApplied = amountOk && difference > _w.AmountTolerance;
        if (amountOk) { score += _w.Amount; parts.Add(toleranceApplied ? "amount within tolerance" : "amount agrees"); }

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

        if (ReferenceMatches(p, g))
        {
            score += _w.Reference;
            parts.Add("reference agrees");
            if (score < _w.ReferenceIdentityConfidence)
            {
                score = _w.ReferenceIdentityConfidence;
                parts.Add("exact reference baseline");
            }
            // Corroborating amount improves identity evidence without hiding date disagreement.
            if (amountOk)
                score = Math.Max(score, _w.ReferenceIdentityConfidence + _w.Reference);
        }

        var channelOk = (p.IsCash && g.IsCash) || (p.IsPos && g.IsPos);
        if (p.CardNumber is not null && p.CardNumber == g.CardNumber && amountOk && p.Date is { } cardDate &&
            g.Date is { } glDate && Math.Abs(cardDate.DayNumber - glDate.DayNumber) <= window)
        {
            score += _w.Card;
            parts.Add("card identity agrees");
        }
        if (channelOk) { score += _w.Channel; parts.Add("channel agrees"); }

        return (Math.Min(100m, score), string.Join(", ", parts), amountOk, toleranceApplied);
    }

    private static ReconException Exc(int runId, ProcRow p, ReconSettlementLeg leg, MatchStatus type, decimal conf, string narrative, decimal? glAmount, string? glAccount) => new()
    {
        ReconRunId = runId,
        ReferenceNumber = p.Reference,
        ProcessorAccountNumber = p.AccountNumber,
        ProcessorStagingId = p.Id,
        ProcessorSource = p.Source,
        GlAccountNumber = glAccount,
        SettlementLeg = leg,
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

    internal static List<ReconPassSummary> BuildSummaries(int runId, List<ReconMatch> matches)
    {
        var list = new List<ReconPassSummary>();

        // Daily per (bucketed) currency. Rows with no processor date belong only in the
        // monthly summary; otherwise Day = null would duplicate the monthly row.
        foreach (var g in matches.Where(m => m.ProcessorDate.HasValue).GroupBy(m => (m.SettlementLeg, Ccy: CcyBucket(m.Ccy), Day: m.ProcessorDate)))
            list.Add(Summarise(runId, g.Key.SettlementLeg, g.Key.Ccy, g.Key.Day, g));

        // Monthly per (bucketed) currency (Day = null).
        foreach (var g in matches.GroupBy(m => (m.SettlementLeg, Ccy: CcyBucket(m.Ccy))))
            list.Add(Summarise(runId, g.Key.SettlementLeg, g.Key.Ccy, null, g));

        return list;
    }

    private static ReconPassSummary Summarise(int runId, ReconSettlementLeg leg, int ccy, DateOnly? day, IEnumerable<ReconMatch> rows)
    {
        var r = rows.ToList();
        var matched = r.Where(x => x.MatchStatus == MatchStatus.Matched).ToList();
        var exceptions = r.Where(x => x.MatchStatus != MatchStatus.Matched).ToList();
        return new ReconPassSummary
        {
            ReconRunId = runId,
            SettlementLeg = leg,
            Ccy = ccy,
            Day = day,
            MatchedCount = matched.Count,
            MatchedAmount = matched.Sum(x => x.ReconciliationAmount ?? x.ProcessorAmount ?? 0m),
            ExceptionCount = exceptions.Count,
            ExceptionAmount = exceptions.Sum(x => x.ReconciliationAmount ?? x.ProcessorAmount ?? 0m),
            AverageConfidence = matched.Count > 0 ? Math.Round(matched.Average(x => x.Confidence), 2) : 0m
        };
    }

    private static IEnumerable<GlRow> AmountCandidates(GlRow[] rows, decimal amount, decimal tolerance)
    {
        var lower = amount - tolerance;
        int lo = 0, hi = rows.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (rows[mid].Amount < lower) lo = mid + 1;
            else hi = mid;
        }
        for (var i = lo; i < rows.Length && rows[i].Amount <= amount + tolerance; i++)
            yield return rows[i];
    }

    private static bool ReferenceMatches(ProcRow p, GlRow g) => !string.IsNullOrEmpty(p.MatchKey) &&
        p.MatchKey == (p.Leg == ReconSettlementLeg.CreditIntermediary ? g.CreditReference : g.Reference);

    private static bool ReferencesCompatible(ProcRow p, GlRow g)
    {
        var glKey = p.Leg == ReconSettlementLeg.CreditIntermediary ? g.CreditReference : g.Reference;
        return (p.CardNumber is null || g.CardNumber is null || p.CardNumber == g.CardNumber) &&
            (string.IsNullOrEmpty(p.MatchKey) || string.IsNullOrEmpty(glKey) || p.MatchKey == glKey);
    }

    private static string? DisplayGlDescription(ProcRow p, GlRow g)
    {
        var reference = p.Leg == ReconSettlementLeg.CreditIntermediary ? g.CreditReference : g.Reference;
        if (string.IsNullOrEmpty(reference)) return g.Description;
        return string.IsNullOrEmpty(g.Description) ? reference : $"{reference} · {g.Description}";
    }

    /// <summary>BankOne credit-card GL particulars contain a twelve-character posting prefix (date + code) before the microfilm key.</summary>
    private static string? MastercardCreditGlKey(string? particular2)
    {
        var value = particular2?.Trim();
        return value is { Length: > 12 } ? value[12..].Trim() : null;
    }

    private static string? Mask(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber)) return null;
        var value = cardNumber.Trim();
        return value.Length <= 4 ? "****" : $"****{value[^4..]}";
    }

    /// <summary>
    /// BankOne uses billing evidence first. Preserve its recorded currency instead of relabelling it.
    /// </summary>
    private static (int? Ccy, decimal Amount, string? FxBasis) ReconciliationValue(
        int? txnCcy, decimal? txnAmount, int? settlementCcy, decimal? settlementAmount,
        int? billingCcy, decimal? billingAmount, ISet<int> settlementCurrencies)
    {
        return MatchingEvidence.BillingValue(txnCcy, txnAmount, settlementCcy, settlementAmount, billingCcy, billingAmount);
    }

    /// <summary>Reference/particular fields are stored in a 32-char column; guard against longer text.</summary>
    private static string? Cap(string? s) => s is { Length: > 32 } ? s[..32] : s;

    private static string? CombineDescription(string? narrative, string? remarks)
    {
        var first = narrative?.Trim();
        var second = CardIdentity.Display(remarks)?.Trim();
        if (string.IsNullOrEmpty(first)) return string.IsNullOrEmpty(second) ? null : second;
        return string.IsNullOrEmpty(second) || string.Equals(first, second, StringComparison.OrdinalIgnoreCase) ? first : $"{first} · {second}";
    }

    private static async Task CarryForwardResolutionsAsync(
        AppDbContext db,
        ReconRun run,
        List<ReconException> exceptions,
        CancellationToken ct)
    {
        if (exceptions.Count == 0) return;

        // Find prior runs with the same processor or GL batch, or prior analyses of the same run
        var priorRunIds = await db.ReconRuns.AsNoTracking()
            .Where(r => r.ReconRunId != run.ReconRunId && (
                r.ProcessorBatchId == run.ProcessorBatchId ||
                (run.GlBatchId != null && r.GlBatchId == run.GlBatchId) ||
                (run.AnalysisOfRunId != null && (r.ReconRunId == run.AnalysisOfRunId || r.AnalysisOfRunId == run.AnalysisOfRunId))
            ))
            .Select(r => r.ReconRunId)
            .ToListAsync(ct);

        if (priorRunIds.Count == 0) return;

        var priorExceptions = await db.ReconExceptions.AsNoTracking()
            .Where(e => priorRunIds.Contains(e.ReconRunId) && e.Status != ExceptionStatus.Open)
            .Include(e => e.Comments)
            .ToListAsync(ct);

        if (priorExceptions.Count == 0) return;

        var byProcStaging = priorExceptions
            .Where(e => e.ProcessorStagingId != null)
            .GroupBy(e => e.ProcessorStagingId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ReconExceptionId).First());

        var byGlStaging = priorExceptions
            .Where(e => e.GlStagingId != null)
            .GroupBy(e => e.GlStagingId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ReconExceptionId).First());

        foreach (var ex in exceptions)
        {
            ReconException? prior = null;
            if (ex.ProcessorStagingId != null && byProcStaging.TryGetValue(ex.ProcessorStagingId.Value, out var p))
            {
                prior = p;
            }
            else if (ex.GlStagingId != null && byGlStaging.TryGetValue(ex.GlStagingId.Value, out var g))
            {
                prior = g;
            }

            if (prior != null)
            {
                ex.Status = prior.Status;
                ex.ResolvedBy = prior.ResolvedBy;
                ex.ResolvedDate = prior.ResolvedDate;
                if (!string.IsNullOrWhiteSpace(prior.Narrative) && prior.Narrative != ex.Narrative)
                {
                    ex.Narrative = $"{ex.Narrative} [Prior resolution: {prior.Status}]";
                }
                foreach (var c in prior.Comments)
                {
                    ex.Comments.Add(new ReconExceptionComment
                    {
                        Body = c.Body,
                        CreatedBy = c.CreatedBy,
                        CreatedDate = c.CreatedDate
                    });
                }
            }
        }
    }
}
