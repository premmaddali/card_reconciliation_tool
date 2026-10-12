using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Reference;
using Recon.Web.Domain.Staging;

namespace Recon.Web.Services.Recon;

public record ReviewGlEvidence(int GlId, int SourceRow, DateOnly? ValueDate, decimal Amount, int Currency, int? DateLag,
    string Direction = "unknown", bool? CardIdentityAgrees = null);
public record MatchingReviewCase(int MatchId, int ProcessorRow, string Category, string TransactionCode,
    ReconSettlementLeg Leg, DateOnly? TransactionDate, decimal? Amount, int? Currency,
    int CandidateCount, IReadOnlyList<ReviewGlEvidence> Candidates, int? AssignedCurrency = null);
public record MatchingReviewReport(int RunId, string RunPeriod, string SourcePeriod, int MissingGlReferences,
    int CurrencyConflicts, int RepeatedPrimaryAssignments, int UniqueDateWindowCandidates,
    int AmbiguousTransactions, int UnmatchedTransactions, IReadOnlyList<MatchingReviewCase> Cases,
    IReadOnlyList<MatchingReviewCase> DateWindowCandidates, int CardIdentityTransactions = 0, int CardIdentityGlEntries = 0);

/// <summary>Read-only assessment of existing assignments and residual evidence. No run is rewritten.</summary>
public class MatchingReviewService(IDbContextFactory<AppDbContext> factory,
    AuthenticationStateProvider authentication, IPermissionService permissions)
{
    public async Task<MatchingReviewReport> AnalyzeAsync(int runId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(600);
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        var run = await db.ReconRuns.AsNoTracking().SingleOrDefaultAsync(x => x.ReconRunId == runId, ct)
            ?? throw new InvalidOperationException("The reconciliation run was not found.");
        var page = run.Function == ReconFunction.PosToGl ? "POSRECON" : "BULKRECON";
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !await permissions.CanViewAsync(userId, page) || !await db.Users.AnyAsync(x => x.Id == userId && x.IsActive, ct))
            throw new UnauthorizedAccessException("A page-view grant and an active account are required for matching review.");
        if (run.Function != ReconFunction.InterchangeToGl || run.GlBatchId is null || run.Status != ReconRunStatus.Completed)
            throw new InvalidOperationException("Matching review currently supports completed Bulk runs with native interchange and GL evidence.");
        if (await db.NormalizedTransactions.AnyAsync(x => x.ImportBatchId == run.ProcessorBatchId, ct) ||
            await db.NormalizedGlEntries.AnyAsync(x => x.ImportBatchId == run.GlBatchId, ct))
            throw new InvalidOperationException("Matching review currently requires native staging IDs; normalized runs are excluded to avoid confusing source identities.");
        var transactions = await db.StgInterchangeFees.AsNoTracking().Where(x => x.ImportBatchId == run.ProcessorBatchId).ToDictionaryAsync(x => x.StgInterchangeFeeId, ct);
        var accounts = await db.GlSettlementAccounts.AsNoTracking().Where(x => x.IsActive &&
            (x.Role == GlAccountRole.CardholderSettlement || x.Role == GlAccountRole.IntermediarySettlement))
            .ToDictionaryAsync(x => x.AcctNum, x => x.Role, ct);
        var gl = (await db.StgGlEntries.AsNoTracking().Where(x => x.ImportBatchId == run.GlBatchId).ToListAsync(ct))
            .Where(x => x.AcctNum != null && accounts.ContainsKey(x.AcctNum)).ToList();
        var glById = gl.ToDictionary(x => x.StgGlEntryId);
        var matches = await db.ReconMatches.AsNoTracking().Where(x => x.ReconRunId == runId).ToListAsync(ct);
        var assigned = matches.Where(x => x.GlStagingId.HasValue).Select(x => x.GlStagingId!.Value).ToHashSet();
        assigned.UnionWith(await db.ReconMatches.Where(x => x.MatchedAcrossPeriods && x.GlStagingId != null &&
            x.ReconRun.ProcessorBatchId != run.ProcessorBatchId).Select(x => x.GlStagingId!.Value).ToListAsync(ct));
        ReconSettlementLeg Leg(StgGlEntry g) => accounts[g.AcctNum!] == GlAccountRole.CardholderSettlement
            ? ReconSettlementLeg.DebitCardholder : ReconSettlementLeg.CreditIntermediary;
        DateOnly? Date(StgInterchangeFee t) => t.TransactionDate.HasValue ? DateOnly.FromDateTime(t.TransactionDate.Value) : null;
        MatchingReviewCase Case(ReconMatch m, string category, int count, IEnumerable<StgGlEntry> candidates)
        {
            var t = transactions[m.ProcessorStagingId];
            var date = Date(t);
            return new(m.ReconMatchId, t.SourceRowNumber, category, t.TransactionCode ?? "", m.SettlementLeg,
                date, t.BillingAmount.HasValue ? Math.Abs(t.BillingAmount.Value) : null, t.BillingCcy, count,
                candidates.Take(5).Select(g => new ReviewGlEvidence(g.StgGlEntryId, g.SourceRowNumber, g.ValueDate,
                    Math.Abs(g.TranAmt ?? 0), MatchingEvidence.GlCurrency(g.TranCcyCode),
                    date.HasValue && g.ValueDate.HasValue ? g.ValueDate.Value.DayNumber - date.Value.DayNumber : null,
                    (g.Debit ?? 0) > 0 && (g.Credit ?? 0) == 0 ? "D" : (g.Credit ?? 0) > 0 && (g.Debit ?? 0) == 0 ? "C" : "unknown",
                    t.CardAccountEntityId is not null && CardIdentity.Normalize(g.TranRmks) is { } cardToken ? t.CardAccountEntityId == cardToken : null)).ToList(), m.Ccy);
        }
        var cases = new List<MatchingReviewCase>();
        var currencyConflicts = 0;
        foreach (var m in matches.Where(x => x.MatchStatus == MatchStatus.Matched && transactions.ContainsKey(x.ProcessorStagingId)))
        {
            if (m.GlStagingId is not int id || !glById.TryGetValue(id, out var g)) continue;
            var ccy = MatchingEvidence.Currency(g.TranCcyCode);
            if (ccy.HasValue && m.Ccy != ccy)
            {
                currencyConflicts++;
                if (cases.Count(x => x.Category == "currency_conflict") < 10) cases.Add(Case(m, "currency_conflict", 1, [g]));
            }
        }
        var processorGroups = matches.ToLookup(x => (x.SettlementLeg, x.Ccy, x.ReconciliationAmount, x.ProcessorDate));
        var glGroups = gl.ToLookup(g => (Leg(g), Math.Abs(g.TranAmt ?? 0), g.TranDate));
        var repeated = matches.Where(m => m.MatchStatus == MatchStatus.Matched &&
            m.ScoreBreakdown?.StartsWith(SecondaryMatchService.MethodLabel) != true &&
            m.ScoreBreakdown?.StartsWith(SecondaryMatchService.CardMethodLabel) != true &&
            (processorGroups[(m.SettlementLeg, m.Ccy, m.ReconciliationAmount, m.ProcessorDate)].Count() > 1 ||
             glGroups[(m.SettlementLeg, m.GlAmount ?? 0, m.GlDate)].Count() > 1)).ToList();
        foreach (var m in repeated.Take(5))
            if (!cases.Any(x => x.MatchId == m.ReconMatchId) && transactions.ContainsKey(m.ProcessorStagingId) && m.GlStagingId is int id && glById.TryGetValue(id, out var g))
                cases.Add(Case(m, "repeated_primary_key", glGroups[(m.SettlementLeg, m.GlAmount ?? 0, m.GlDate)].Count(), [g]));

        var available = gl.Where(g => !assigned.Contains(g.StgGlEntryId) && g.TranAmt.HasValue && g.ValueDate.HasValue)
            .ToLookup(g => (Leg(g), MatchingEvidence.Currency(g.TranCcyCode), Math.Abs(g.TranAmt!.Value)));
        var edges = new Dictionary<int, List<StgGlEntry>>();
        var reverse = new Dictionary<int, int>();
        var residual = matches.Where(m => m.MatchStatus is MatchStatus.UnmatchedProcessor or MatchStatus.Ambiguous &&
            m.GlStagingId is null && m.ProcessorSource == StagingSource.InterchangeFee && transactions.ContainsKey(m.ProcessorStagingId)).ToList();
        foreach (var m in residual)
        {
            var t = transactions[m.ProcessorStagingId];
            var date = Date(t);
            if (t.OrigineCode?.Trim() == "0" || date is null || t.BillingAmount is null || t.BillingCcy is null or <= 0) continue;
            var processorReference = m.SettlementLeg == ReconSettlementLeg.CreditIntermediary
                ? t.MicrofilmRefNumber?.Trim() : t.ReferenceNumber?.Trim();
            bool CompatibleReference(StgGlEntry g)
            {
                var key = g.TranParticular2?.Trim();
                if (m.SettlementLeg == ReconSettlementLeg.CreditIntermediary)
                    key = key is { Length: > 12 } ? key[12..].Trim() : null;
                return CardIdentity.Compatible(t.CardAccountEntityId, g.TranRmks) &&
                    (string.IsNullOrEmpty(processorReference) || string.IsNullOrEmpty(key) || processorReference == key);
            }
            var candidates = available[(m.SettlementLeg, t.BillingCcy, Math.Abs(t.BillingAmount.Value))]
                .Where(g => Math.Abs(g.ValueDate!.Value.DayNumber - date.Value.DayNumber) <= 2 && CompatibleReference(g)).ToList();
            edges[m.ReconMatchId] = candidates;
            foreach (var g in candidates) reverse[g.StgGlEntryId] = reverse.GetValueOrDefault(g.StgGlEntryId) + 1;
        }
        var unique = residual.Where(m => edges.TryGetValue(m.ReconMatchId, out var rows) && rows.Count == 1 && reverse[rows[0].StgGlEntryId] == 1).ToList();
        foreach (var m in unique.Take(10)) cases.Add(Case(m, "unique_date_window", 1, edges[m.ReconMatchId]));
        foreach (var m in residual.Where(m => edges.TryGetValue(m.ReconMatchId, out var rows) && rows.Count > 1).Take(5))
            cases.Add(Case(m, "ambiguous", edges[m.ReconMatchId].Count, edges[m.ReconMatchId]));
        foreach (var m in residual.Where(m => !edges.TryGetValue(m.ReconMatchId, out var rows) || rows.Count == 0).Take(5))
            cases.Add(Case(m, "missing_evidence", 0, []));
        var sourcePeriod = await db.ImportBatches.Where(x => x.ImportBatchId == run.ProcessorBatchId).Select(x => x.PeriodKey).SingleAsync(ct);
        return new(runId, run.PeriodKey, sourcePeriod, gl.Count(x => string.IsNullOrWhiteSpace(x.TranParticular2)),
            currencyConflicts, repeated.Count, unique.Count,
            matches.Count(x => x.MatchStatus == MatchStatus.Ambiguous), matches.Count(x => x.MatchStatus == MatchStatus.UnmatchedProcessor), cases,
            unique.Select(m => Case(m, "unique_date_window", 1, edges[m.ReconMatchId])).ToList(),
            transactions.Values.Count(t => t.CardAccountEntityId is not null), gl.Count(g => CardIdentity.Normalize(g.TranRmks) is not null));
    }
}
