using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Reference;
using Recon.Web.Domain.Staging;

namespace Recon.Web.Services.Recon;

public record SecondaryMatchResult(int RunId, int Added, int Ambiguous);

/// <summary>Creates a separately auditable second pass; the original run is never rewritten.</summary>
public class SecondaryMatchService(IDbContextFactory<AppDbContext> factory,
    AuthenticationStateProvider authentication, IPermissionService permissions)
{
    public const string MethodLabel = "Secondary Date/Amount/Cur";
    public const string CardMethodLabel = "Card Number/Date/Amount/Cur";
    private record Key(ReconSettlementLeg Leg, DateOnly Date, decimal Amount, int Currency, string? Card = null);

    public Task<SecondaryMatchResult> RunAsync(int originalRunId, CancellationToken ct = default) => ExecuteAsync(originalRunId, false, ct);
    public Task<SecondaryMatchResult> RunAdjacentAsync(int originalRunId, CancellationToken ct = default) => ExecuteAsync(originalRunId, true, ct);
    public Task<SecondaryMatchResult> RunCardAsync(int originalRunId, CancellationToken ct = default) => ExecuteAsync(originalRunId, false, ct, card: true);

    private async Task<SecondaryMatchResult> ExecuteAsync(int originalRunId, bool adjacent, CancellationToken ct, bool card = false)
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new UnauthorizedAccessException("Sign in to run secondary matching.");
        var grants = await permissions.GetGrantsAsync(userId);
        if (!grants.TryGetValue("BULKRECON", out var grant) || !grant.CanView || !(grant.CanAdd || grant.CanEdit))
            throw new UnauthorizedAccessException("You do not have permission to run Bulk reconciliation.");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct))
            throw new UnauthorizedAccessException("An active user account is required.");
        db.Database.SetCommandTimeout(600);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await GlAssignmentLock.AcquireAsync(db, ct);
        // Serialize clicks for the same source run and hold its inputs stable until commit.
        var source = await db.ReconRuns.FromSqlInterpolated($"SELECT * FROM [ReconRuns] WITH (UPDLOCK, HOLDLOCK) WHERE [ReconRunId] = {originalRunId}").SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The reconciliation run was not found.");
        var isAnalysis = source.AnalysisOfRunId is not null;
        var analysis = isAnalysis ? await ReconAnalysisInputs.LoadAsync(db, source, ct) : null;
        var sourcePeriod = await db.ImportBatches.Where(x => x.ImportBatchId == source.ProcessorBatchId).Select(x => x.PeriodKey).SingleAsync(ct);
        if (!isAnalysis && !string.IsNullOrWhiteSpace(sourcePeriod) && source.PeriodKey != sourcePeriod)
            throw new InvalidOperationException($"Run period {source.PeriodKey} disagrees with processor batch period {sourcePeriod}. Create a new reconciliation using the source period before running additional passes.");
        if (source.Function != ReconFunction.InterchangeToGl || source.GlBatchId is null)
            throw new InvalidOperationException("Start the secondary pass from a Bulk reconciliation run.");
        if ((source.Status != ReconRunStatus.Completed && !(isAnalysis && source.Status == ReconRunStatus.Running))
            || source.IsArchived || source.VerificationStatus != ReconVerificationStatus.NotSubmitted ||
            (!isAnalysis && await db.BatchFreezes.AnyAsync(x => x.Function == source.Function && x.PeriodKey == source.PeriodKey && x.SchemeProfileId == source.SchemeProfileId, ct)))
            throw new InvalidOperationException("Secondary matching requires a completed, unsubmitted run in an unfrozen period.");
        // Each refinement is an independent in-place method, applied once and recorded; re-running a
        // processed pass is a no-op. No ordering dependency between the three methods.
        var kind = card ? ReconPassKind.CardNumber : adjacent ? ReconPassKind.CrossPeriod : ReconPassKind.SecondaryAmount;
        var priorPasses = await db.ReconRunPasses.Where(p => p.ReconRunId == originalRunId && p.Kind == kind).ToListAsync(ct);
        if (priorPasses.Count > 0) return new(originalRunId, priorPasses.Sum(x => x.Added), priorPasses.Sum(x => x.Ambiguous));
        if (!isAnalysis && (await db.NormalizedTransactions.AnyAsync(x => x.ImportBatchId == source.ProcessorBatchId, ct) ||
            await db.NormalizedGlEntries.AnyAsync(x => x.ImportBatchId == source.GlBatchId, ct)))
            throw new InvalidOperationException("This secondary pass requires the imported interchange and GL source rows.");

        // Read-only candidate set (only the still-unmatched rows the pass could change). Loading the
        // whole batch tracked does not scale (85k matches + 150k exceptions); we write back just the
        // few rows that actually change, and resolve exceptions with set-based SQL.
        var matches = await db.ReconMatches.AsNoTracking()
            .Where(x => x.ReconRunId == originalRunId
                && (x.MatchStatus == MatchStatus.UnmatchedProcessor || x.MatchStatus == MatchStatus.Ambiguous)
                && x.GlStagingId == null)
            .ToListAsync(ct);
        var usedGl = (await db.ReconMatches.Where(x => x.ReconRunId == originalRunId && x.GlStagingId != null)
            .Select(x => x.GlStagingId!.Value).ToListAsync(ct)).ToHashSet();
        var processorBatchIds = analysis?.ProcessorBatchIds ?? [source.ProcessorBatchId];
        var transactions = await db.StgInterchangeFees.AsNoTracking().Where(x => processorBatchIds.Contains(x.ImportBatchId)).ToDictionaryAsync(x => x.StgInterchangeFeeId, ct);
        var methodLabel = card ? CardMethodLabel : MethodLabel;
        if (card && !transactions.Values.Any(t => t.CardAccountEntityId is not null))
            throw new InvalidOperationException("No TMS card identities are available. Import the new extract with CARD_ACCOUNT_ENTITY_ID as text and create a new reconciliation first.");
        var accounts = await db.GlSettlementAccounts.Where(x => x.IsActive).ToDictionaryAsync(x => x.AcctNum, x => x.Role, ct);
        var batchIds = (analysis?.GlBatchIds ?? [source.GlBatchId.Value]).ToList();
        var sourcePeriods = new Dictionary<int, string>();
        if (adjacent)
        {
            if (source.PeriodStart is { } start && source.PeriodEnd is { } end && start.ToString("yyyy-MM") != end.ToString("yyyy-MM"))
                throw new InvalidOperationException("Adjacent matching requires a single-month reconciliation range.");
            if (!DateOnly.TryParseExact(source.PeriodKey + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var month))
                throw new InvalidOperationException("Adjacent matching requires a single YYYY-MM reconciliation period.");
            var periods = new[] { month.AddMonths(-1).ToString("yyyy-MM"), month.AddMonths(1).ToString("yyyy-MM") };
            var frozen = isAnalysis ? [] : await db.BatchFreezes.Where(x => x.Function == source.Function && x.SchemeProfileId == source.SchemeProfileId).Select(x => x.PeriodKey).ToListAsync(ct);
            var batches = await db.ImportBatches.Where(x => x.SourceType == SourceType.GlDump && x.Status == ImportStatus.Parsed &&
                x.SchemeProfileId == source.SchemeProfileId && periods.Contains(x.PeriodKey) && !frozen.Contains(x.PeriodKey)).ToListAsync(ct);
            if (batches.Count == 0) throw new InvalidOperationException($"Import GL files for {string.Join(" or ", periods)} in the same scheme first. Frozen periods are excluded.");
            batchIds = batches.Select(x => x.ImportBatchId).ToList();
            sourcePeriods = batches.ToDictionary(x => x.ImportBatchId, x => x.PeriodKey);
            if (!isAnalysis && await db.NormalizedGlEntries.AnyAsync(x => batchIds.Contains(x.ImportBatchId), ct))
                throw new InvalidOperationException("Adjacent matching requires native imported GL source rows.");
            var assigned = await db.ReconMatches.Where(x => x.ReconRun.AnalysisOfRunId == null && x.ReconRunId != source.AnalysisOfRunId
                    && x.GlStagingId != null && x.ReconRun.Function != ReconFunction.NetworkToAtm &&
                    db.StgGlEntries.Any(g => g.StgGlEntryId == x.GlStagingId && batchIds.Contains(g.ImportBatchId)))
                .Select(x => x.GlStagingId!.Value).Distinct().ToListAsync(ct);
            usedGl.UnionWith(assigned);
        }
        else
        {
            var reserved = await db.ReconMatches.Where(x => x.ReconRun.AnalysisOfRunId == null && x.ReconRunId != source.AnalysisOfRunId
                && x.MatchedAcrossPeriods && x.GlStagingId != null && x.ReconRun.ProcessorBatchId != source.ProcessorBatchId)
                .Select(x => x.GlStagingId!.Value).ToListAsync(ct);
            usedGl.UnionWith(reserved);
        }
        var gl = (await db.StgGlEntries.AsNoTracking().Where(x => batchIds.Contains(x.ImportBatchId)).ToListAsync(ct))
            .Where(x => x.AcctNum is not null && accounts.TryGetValue(x.AcctNum, out var role) && role is GlAccountRole.CardholderSettlement or GlAccountRole.IntermediarySettlement).ToList();
        if (!gl.Any(x => x.ValueDate.HasValue))
            throw new InvalidOperationException("No GL VALUE_DATE is available. If the export includes VALUE_DATE, re-import the same GL file using Import files, then retry this batch. No dates will be guessed.");
        var glCards = card ? gl.ToDictionary(g => g.StgGlEntryId, g => CardIdentity.Normalize(g.TranRmks)) : new Dictionary<int, string?>();
        if (card && !glCards.Values.Any(token => token is not null))
            throw new InvalidOperationException("No complete GL card identities are available in TRAN_RMKS. Import the GL extract with that column as text first.");
        ReconSettlementLeg Leg(StgGlEntry g) => accounts[g.AcctNum!] == GlAccountRole.CardholderSettlement ? ReconSettlementLeg.DebitCardholder : ReconSettlementLeg.CreditIntermediary;
        Key? ProcessorKey(ReconMatch match)
        {
            if (match.MatchStatus is not (MatchStatus.UnmatchedProcessor or MatchStatus.Ambiguous) || match.GlStagingId is not null || match.ProcessorSource != StagingSource.InterchangeFee ||
                !transactions.TryGetValue(match.ProcessorStagingId, out var t) || t.OrigineCode?.Trim() == "0" ||
                t.TransactionDate is null || t.BillingAmount is null || t.BillingCcy is null || t.BillingCcy <= 0) return null;
            if (card && t.CardAccountEntityId is null) return null;
            return new(match.SettlementLeg, DateOnly.FromDateTime(t.TransactionDate.Value),
                MatchingEvidence.BillingAmountForMatching(t.ProductType, t.BillingAmount, t.IssMarkupAmount)!.Value,
                t.BillingCcy.Value, card ? t.CardAccountEntityId : null);
        }
        Key? GlKey(StgGlEntry row)
        {
            var currency = MatchingEvidence.Currency(row.TranCcyCode);
            var cardToken = card ? glCards[row.StgGlEntryId] : null;
            return usedGl.Contains(row.StgGlEntryId) || row.ValueDate is null || row.TranAmt is null || currency is null
                || (card && cardToken is null) ? null : new(Leg(row), row.ValueDate.Value, Math.Abs(row.TranAmt.Value), currency.Value, cardToken);
        }
        var processors = matches.Select(m => (Row:m, Key:ProcessorKey(m))).Where(x => x.Key is not null).ToLookup(x => x.Key!, x => x.Row);
        var candidates = gl.Select(g => (Row:g, Key:GlKey(g))).Where(x => x.Key is not null).ToLookup(x => x.Key!, x => x.Row);
        bool ReferencesAgree(ReconMatch m, StgGlEntry g)
        {
            var t = transactions[m.ProcessorStagingId];
            var p = m.SettlementLeg == ReconSettlementLeg.CreditIntermediary ? t.MicrofilmRefNumber?.Trim() : t.ReferenceNumber?.Trim();
            var r = g.TranParticular2?.Trim();
            if (m.SettlementLeg == ReconSettlementLeg.CreditIntermediary) r = r is { Length: > 12 } ? r[12..].Trim() : null;
            return string.IsNullOrEmpty(p) || string.IsNullOrEmpty(r) || p == r;
        }
        var groups = new List<(Key Key, List<ReconMatch> Rows, List<StgGlEntry> Gl)>();
        var reverseCards = new Dictionary<int, int>();
        foreach (var group in processors)
        {
            if (!card)
            {
                var available = candidates[group.Key].ToList();
                var cash = new Queue<StgGlEntry>(available.Where(g => AtmWithdrawalMatching.IsCashGl(g.TranParticular))
                    .OrderBy(g => g.StgGlEntryId));
                var selectedGl = new HashSet<int>();
                var rows = new List<ReconMatch>();
                foreach (var m in group.OrderBy(m => m.ReconMatchId))
                {
                    var t = transactions[m.ProcessorStagingId];
                    if (!AtmWithdrawalMatching.IsWithdrawal(t.TransactionCode, t.TransactionWording) || !cash.TryDequeue(out var g))
                    {
                        rows.Add(m);
                        continue;
                    }
                    groups.Add((group.Key, [m], [g]));
                    selectedGl.Add(g.StgGlEntryId);
                }
                if (rows.Count > 0) groups.Add((group.Key, rows, available.Where(g => !selectedGl.Contains(g.StgGlEntryId)).ToList()));
                continue;
            }
            foreach (var m in group)
            {
                var options = new List<StgGlEntry>(2);
                foreach (var g in Enumerable.Range(-2, 5).SelectMany(offset => candidates[group.Key with { Date = group.Key.Date.AddDays(offset) }])
                    .Where(g => ReferencesAgree(m, g)))
                {
                    // Two options establish ambiguity; retain complete reverse counts without
                    // materializing the full graph for repeated same-card transactions.
                    if (options.Count < 2) options.Add(g);
                    reverseCards[g.StgGlEntryId] = reverseCards.GetValueOrDefault(g.StgGlEntryId) + 1;
                }
                groups.Add((group.Key, [m], options));
            }
        }
        int added = 0, ambiguous = 0;
        var updatedMatches = new List<ReconMatch>(); // only the rows this pass changes are written back
        foreach (var group in groups)
        {
            var available = group.Gl;
            if (available.Count == 0) continue;
            if (group.Rows.Count != 1 || available.Count != 1 || (card && reverseCards[available[0].StgGlEntryId] != 1))
            {
                foreach (var match in group.Rows)
                {
                    match.MatchStatus = MatchStatus.Ambiguous;
                    match.MatchComment = $"{methodLabel}: competing transaction/GL candidates share {(card ? "card identity, " : "")}date, amount, currency and settlement leg. Review required.";
                    ambiguous++;
                    updatedMatches.Add(match);
                }
                continue;
            }
            var m = group.Rows.Single(); var g = available.Single();
            var transactionRow = transactions[m.ProcessorStagingId];
            var isWithdrawalPair = !card && AtmWithdrawalMatching.IsWithdrawal(transactionRow.TransactionCode, transactionRow.TransactionWording)
                && AtmWithdrawalMatching.IsCashGl(g.TranParticular);
            var processorReference = m.SettlementLeg == ReconSettlementLeg.CreditIntermediary
                ? transactionRow.MicrofilmRefNumber?.Trim() : transactionRow.ReferenceNumber?.Trim();
            var glReference = g.TranParticular2?.Trim();
            if (m.SettlementLeg == ReconSettlementLeg.CreditIntermediary)
                glReference = glReference is { Length: > 12 } ? glReference[12..].Trim() : null;
            if (!isWithdrawalPair && (!CardIdentity.Compatible(transactionRow.CardAccountEntityId, g.TranRmks) ||
                (!string.IsNullOrEmpty(processorReference) && !string.IsNullOrEmpty(glReference) && processorReference != glReference)))
            {
                m.MatchComment = $"{methodLabel}: date, amount and currency agree, but supplied identity references conflict. Source review required.";
                updatedMatches.Add(m);
                continue;
            }
            m.MatchStatus = MatchStatus.Matched;
            m.GlStagingId = g.StgGlEntryId;
            m.GlSourceBatchId = g.ImportBatchId;
            m.MatchedAcrossPeriods = adjacent;
            m.GlAccountNumber = g.AcctNum;
            m.GlReference = g.TranParticular2;
            m.GlParticular2 = g.TranParticular2;
            m.GlDescription = g.TranParticular;
            m.GlDate = g.TranDate;
            m.GlValueDate = g.ValueDate;
            m.GlPostingDateTime = g.EntryDate ?? g.VfdDate;
            m.GlAmount = group.Key.Amount;
            m.ReconciliationAmount = group.Key.Amount;
            m.Ccy = group.Key.Currency;
            m.GlCcy = group.Key.Currency;
            m.Confidence = card ? 95m : 78m; // Rule evidence score, not a probability.
            m.ScoreBreakdown = isWithdrawalPair ? AtmWithdrawalMatching.Evidence : card
                ? $"{methodLabel}: exact card number, billing amount, currency and settlement leg; transaction date / GL value date within 2 days; unique on both sides."
                : $"{MethodLabel}: exact transaction date / GL value date, billing amount, currency and settlement leg; unique on both sides.";
            m.MatchComment = isWithdrawalPair
                ? $"{AtmWithdrawalMatching.Evidence}. {group.Key.Date:yyyy-MM-dd}, {group.Key.Amount:0.####}, {IsoCurrencyCatalog.Display(group.Key.Currency)}."
                : card
                ? $"{methodLabel}: {group.Key.Date:yyyy-MM-dd}, {group.Key.Amount:0.####}, {IsoCurrencyCatalog.Display(group.Key.Currency)}. Card identity agrees; GL value-date difference {g.ValueDate!.Value.DayNumber - group.Key.Date.DayNumber} day(s)."
                : $"{MethodLabel}: {group.Key.Date:yyyy-MM-dd}, {group.Key.Amount:0.####}, {IsoCurrencyCatalog.Display(group.Key.Currency)}. Medium confidence. No reference match.";
            if (MatchingEvidence.IsCredit(transactionRow.ProductType))
            {
                var formula = $"CR billing minus ISS markup: {transactionRow.BillingAmount:0.####} - {transactionRow.IssMarkupAmount ?? 0m:0.####}; matching amount {group.Key.Amount:0.####}.";
                m.ScoreBreakdown = $"{methodLabel}: billing minus ISS markup, currency and leg; " +
                    (card ? "card and date window; " : "exact date; ") +
                    (isWithdrawalPair ? "one-to-one withdrawal pairing." : "unique pair.");
                m.MatchComment += " " + formula;
            }
            if (adjacent) m.MatchComment += $" Adjacent GL batch #{g.ImportBatchId}, period {sourcePeriods[g.ImportBatchId]}.";
            usedGl.Add(g.StgGlEntryId);
            added++;
            updatedMatches.Add(m);
        }
        var now = DateTime.UtcNow;
        var glBatchId = source.GlBatchId!.Value;

        // --- Persist only the delta, set-based (scales to 100k+ row batches) -----------------------
        // 1) Write back just the rows this pass changed.
        if (updatedMatches.Count > 0)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            foreach (var m in updatedMatches) db.Attach(m).State = EntityState.Modified;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            db.ChangeTracker.AutoDetectChangesEnabled = true;
        }

        // 2) Reconcile this batch's exceptions to the new match state without loading them all.
        var matchedProcIds = updatedMatches.Where(m => m.MatchStatus == MatchStatus.Matched).Select(m => m.ProcessorStagingId).ToList();
        var ambiguousProcIds = updatedMatches.Where(m => m.MatchStatus == MatchStatus.Ambiguous).Select(m => m.ProcessorStagingId).ToList();
        var newlyUsedGlIds = updatedMatches.Where(m => m.MatchStatus == MatchStatus.Matched && m.GlStagingId.HasValue).Select(m => m.GlStagingId!.Value).ToList();

        if (matchedProcIds.Count > 0)
            await db.ReconExceptions.Where(e => e.ReconRunId == originalRunId && e.ExceptionType != MatchStatus.UnmatchedGl
                && e.ProcessorStagingId != null && matchedProcIds.Contains(e.ProcessorStagingId.Value)).ExecuteDeleteAsync(ct);
        if (newlyUsedGlIds.Count > 0)
            await db.ReconExceptions.Where(e => e.ReconRunId == originalRunId && e.ExceptionType == MatchStatus.UnmatchedGl
                && e.GlStagingId != null && newlyUsedGlIds.Contains(e.GlStagingId.Value)).ExecuteDeleteAsync(ct);
        if (ambiguousProcIds.Count > 0)
            await db.ReconExceptions.Where(e => e.ReconRunId == originalRunId && e.ExceptionType != MatchStatus.UnmatchedGl
                && e.ExceptionType != MatchStatus.Ambiguous && e.ProcessorStagingId != null && ambiguousProcIds.Contains(e.ProcessorStagingId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ExceptionType, MatchStatus.Ambiguous)
                    .SetProperty(e => e.UpdatedBy, userId).SetProperty(e => e.UpdatedDate, now), ct);

        // 3) Create exceptions still missing for unmatched records (0 rows on a primary-reconciled run;
        //    covers directly-seeded data). ExceptionType byte: UnmatchedGl = 4; MatchStatus byte: Matched = 0.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO ReconExceptions (ReconRunId, ReferenceNumber, ProcessorAccountNumber, GlAccountNumber, SettlementLeg, ExceptionType, Ccy, Confidence, ExpectedAmount, ActualAmount, Difference, TxnDate, ProcessorStagingId, ProcessorSource, Narrative, Status, CreatedBy, CreatedDate)
SELECT m.ReconRunId, m.ReferenceNumber, m.ProcessorAccountNumber, m.GlAccountNumber, m.SettlementLeg, m.MatchStatus, m.Ccy, m.Confidence, m.GlAmount, m.ReconciliationAmount,
       CASE WHEN m.GlAmount IS NOT NULL AND m.ReconciliationAmount IS NOT NULL THEN m.ReconciliationAmount - m.GlAmount END,
       m.ProcessorDate, m.ProcessorStagingId, m.ProcessorSource, COALESCE(m.MatchComment, m.ScoreBreakdown, N'No GL match after secondary matching.'), 0, {userId}, {now}
FROM ReconMatches m
WHERE m.ReconRunId = {originalRunId} AND m.MatchStatus <> 0
  AND NOT EXISTS (SELECT 1 FROM ReconExceptions e WHERE e.ReconRunId = {originalRunId} AND e.ProcessorStagingId = m.ProcessorStagingId AND e.ExceptionType <> 4);", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO ReconExceptions (ReconRunId, ReferenceNumber, GlAccountNumber, GlStagingId, SettlementLeg, ExceptionType, Ccy, Confidence, ExpectedAmount, TxnDate, Narrative, Status, CreatedBy, CreatedDate)
SELECT {originalRunId}, LEFT(g.TranParticular2, 32), g.AcctNum, g.StgGlEntryId, a.Role, 4,
       CASE g.TranCcyCode WHEN N'MUR' THEN 480 WHEN N'USD' THEN 840 WHEN N'EUR' THEN 978 END,
       0, ABS(COALESCE(g.TranAmt, 0)), g.TranDate, N'GL entry has no assigned transaction after secondary matching.', 0, {userId}, {now}
FROM StgGlEntries g
JOIN GlSettlementAccounts a ON a.AcctNum = g.AcctNum AND a.IsActive = 1 AND a.Role IN (1, 2)
WHERE g.ImportBatchId = {glBatchId}
  AND NOT EXISTS (SELECT 1 FROM ReconMatches m WHERE m.ReconRunId = {originalRunId} AND m.GlStagingId = g.StgGlEntryId)
  AND NOT EXISTS (SELECT 1 FROM ReconExceptions e WHERE e.ReconRunId = {originalRunId} AND e.GlStagingId = g.StgGlEntryId AND e.ExceptionType = 4);", ct);

        // 4) Rebuild per-currency summaries from the match set (daily + monthly), set-based.
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ReconPassSummaries WHERE ReconRunId = {originalRunId};", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
WITH b AS (
  SELECT SettlementLeg,
         CASE WHEN Ccy IN (480, 840, 978) THEN Ccy ELSE 0 END AS Bucket,
         ProcessorDate,
         CASE WHEN MatchStatus = 0 THEN 1 ELSE 0 END AS IsMatched,
         COALESCE(ReconciliationAmount, ProcessorAmount, 0) AS Amt,
         Confidence
  FROM ReconMatches WHERE ReconRunId = {originalRunId}
)
INSERT INTO ReconPassSummaries (ReconRunId, SettlementLeg, Ccy, Day, MatchedCount, MatchedAmount, ExceptionCount, ExceptionAmount, AverageConfidence)
SELECT {originalRunId}, SettlementLeg, Bucket, ProcessorDate, SUM(IsMatched),
       SUM(CASE WHEN IsMatched = 1 THEN Amt ELSE 0 END), SUM(1 - IsMatched),
       SUM(CASE WHEN IsMatched = 0 THEN Amt ELSE 0 END),
       CASE WHEN SUM(IsMatched) > 0 THEN ROUND(AVG(CASE WHEN IsMatched = 1 THEN Confidence END), 2) ELSE 0 END
FROM b WHERE ProcessorDate IS NOT NULL GROUP BY SettlementLeg, Bucket, ProcessorDate
UNION ALL
SELECT {originalRunId}, SettlementLeg, Bucket, NULL, SUM(IsMatched),
       SUM(CASE WHEN IsMatched = 1 THEN Amt ELSE 0 END), SUM(1 - IsMatched),
       SUM(CASE WHEN IsMatched = 0 THEN Amt ELSE 0 END),
       CASE WHEN SUM(IsMatched) > 0 THEN ROUND(AVG(CASE WHEN IsMatched = 1 THEN Confidence END), 2) ELSE 0 END
FROM b GROUP BY SettlementLeg, Bucket;", ct);

        // 5) Refresh the run's headline counts and record the pass.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE ReconRuns SET
  MatchedCount = (SELECT COUNT(*) FROM ReconMatches WHERE ReconRunId = {originalRunId} AND MatchStatus = 0),
  ExceptionCount = (SELECT COUNT(*) FROM ReconExceptions WHERE ReconRunId = {originalRunId}),
  AverageConfidence = COALESCE((SELECT ROUND(AVG(Confidence), 2) FROM ReconMatches WHERE ReconRunId = {originalRunId} AND MatchStatus = 0), 0),
  SecondaryMatchCount = SecondaryMatchCount + {added},
  SecondaryAmbiguousCount = SecondaryAmbiguousCount + {ambiguous},
  UpdatedBy = {userId}, UpdatedDate = {now}
WHERE ReconRunId = {originalRunId};", ct);

        db.ReconRunPasses.Add(new ReconRunPass {
            ReconRunId = originalRunId, Kind = kind, RunGroup = Guid.NewGuid(),
            Added = added, Ambiguous = ambiguous, RunAt = now, Detail = methodLabel,
            CreatedBy = userId, CreatedDate = now
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(originalRunId, added, ambiguous);
    }

}
