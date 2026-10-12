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

/// <summary>One line of the load-time change table: a run whose outstanding items a back-match resolved.</summary>
public record BackMatchChange(int RunId, string RangeLabel, int Added);

public record DailyBackMatchResult(int ContainerId, int Added, int Ambiguous, IReadOnlyList<BackMatchChange> Changes);

public record RollUpResult(int ContainerId, int MonthBatchRunId, int Matched, int Exceptions);

public record OrphanSweepResult(int Added, int Ambiguous, int BatchesInScope, IReadOnlyList<BackMatchChange> Changes);

/// <summary>
/// Daily / period reconciliation back-match. A month container (<see cref="ReconPeriodRun"/>) groups
/// date-range runs; each run first matches its own files, then this reconciles the cumulative pool of
/// still-outstanding items against all still-available GL within the admin settlement spread. Because
/// the pool holds every run's transactions and GL, matching is inherently bidirectional and crosses
/// run boundaries (a later day's GL resolves an earlier day's outstanding transaction, and vice
/// versa). It writes into whichever run owns each record and returns a per-run change table.
/// Scope is Bulk (InterchangeToGl); card evidence is not used here. The classic monthly flow and
/// <see cref="SecondaryMatchService"/> are untouched.
/// </summary>
public class DailyReconService(IDbContextFactory<AppDbContext> factory,
    AuthenticationStateProvider authentication, IPermissionService permissions)
{
    public const string MethodLabel = "Daily pool back-match (date/amount/cur)";
    public const string OrphanMethodLabel = "Orphan sweep (reference)";
    private record Key(ReconSettlementLeg Leg, decimal Amount, int Currency);

    /// <summary>
    /// Reconcile the container's cumulative pool. Re-runnable after each load: with no new resolvable
    /// pairs it adds nothing. Only flips outstanding → Matched/Ambiguous and resolves the matching
    /// exceptions; it never turns an open item into an aged exception (that happens at rollup).
    /// </summary>
    public async Task<DailyBackMatchResult> BackMatchAsync(int containerId, CancellationToken ct = default)
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new UnauthorizedAccessException("Sign in to run daily reconciliation.");
        var grants = await permissions.GetGrantsAsync(userId);
        if (!grants.TryGetValue("BULKRECON", out var grant) || !grant.CanView || !(grant.CanAdd || grant.CanEdit))
            throw new UnauthorizedAccessException("You do not have permission to run Bulk reconciliation.");

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct))
            throw new UnauthorizedAccessException("An active user account is required.");
        db.Database.SetCommandTimeout(600);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await GlAssignmentLock.AcquireAsync(db, ct);

        var container = await db.ReconPeriodRuns
            .FromSqlInterpolated($"SELECT * FROM [ReconPeriodRuns] WITH (UPDLOCK, HOLDLOCK) WHERE [ReconPeriodRunId] = {containerId}")
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The month container was not found.");
        if (container.Status != ReconPeriodRunStatus.Open)
            throw new InvalidOperationException("This month has been rolled up and no longer accepts changes.");
        if (container.Function != ReconFunction.InterchangeToGl)
            throw new InvalidOperationException("Daily reconciliation currently supports Bulk (Interchange → GL).");

        var spread = await db.ReconParameters.Select(x => x.SettlementSpreadDays).FirstOrDefaultAsync(ct);
        if (spread <= 0) spread = 20;

        // The pool spans this month container and its immediate calendar neighbours (prior/next), so a
        // later month's load back-matches an earlier month's outstanding and vice versa — bounded by
        // the spread per pair. A neighbour contributes its open child runs, or, once rolled up, its
        // consolidated month batch. The month boundary is only a label on one rolling pool.
        var neighbourKeys = AdjacentMonthKeys(container.PeriodKey);
        var neighbours = neighbourKeys.Count == 0 ? [] : await db.ReconPeriodRuns
            .Where(c => c.Function == container.Function && c.SchemeProfileId == container.SchemeProfileId && neighbourKeys.Contains(c.PeriodKey))
            .Select(c => new { c.ReconPeriodRunId, c.Status, c.RolledUpRunId }).ToListAsync(ct);
        var openContainerIds = new List<int> { containerId };
        var rolledUpRunIds = new List<int>();
        foreach (var n in neighbours)
            if (n.Status == ReconPeriodRunStatus.Open) openContainerIds.Add(n.ReconPeriodRunId);
            else if (n.RolledUpRunId is int rid) rolledUpRunIds.Add(rid);

        var runs = await db.ReconRuns.Where(r =>
                (r.ParentPeriodRunId != null && openContainerIds.Contains(r.ParentPeriodRunId.Value) && r.Status == ReconRunStatus.Completed && !r.IsArchived)
                || rolledUpRunIds.Contains(r.ReconRunId))
            .Select(r => new { r.ReconRunId, r.PeriodStart, r.PeriodEnd }).ToListAsync(ct);
        if (runs.Count == 0)
            return new(containerId, 0, 0, []);
        var runIds = runs.Select(r => r.ReconRunId).ToList();
        var rangeLabel = runs.ToDictionary(r => r.ReconRunId,
            r => r.PeriodStart is { } s && r.PeriodEnd is { } e
                ? (s == e ? s.ToString("yyyy-MM-dd") : $"{s:yyyy-MM-dd}–{e:yyyy-MM-dd}") : $"run #{r.ReconRunId}");

        // Still-outstanding processor matches across the pool, and GL already consumed by any run.
        var matches = await db.ReconMatches.AsNoTracking()
            .Where(x => runIds.Contains(x.ReconRunId)
                && (x.MatchStatus == MatchStatus.UnmatchedProcessor || x.MatchStatus == MatchStatus.Ambiguous)
                && x.GlStagingId == null && x.ProcessorSource == StagingSource.InterchangeFee)
            .ToListAsync(ct);
        if (matches.Count == 0)
            return new(containerId, 0, 0, []);

        // Available GL is discovered from the pool's UnmatchedGl exceptions, not a run's single
        // GlBatchId — a rolled-up month spans many GL batches. The exception's run owns the GL row.
        var glOwnerRun = (await db.ReconExceptions.AsNoTracking()
            .Where(e => runIds.Contains(e.ReconRunId) && e.ExceptionType == MatchStatus.UnmatchedGl && e.GlStagingId != null)
            .Select(e => new { GlId = e.GlStagingId!.Value, e.ReconRunId }).ToListAsync(ct))
            .GroupBy(x => x.GlId).ToDictionary(g => g.Key, g => g.First().ReconRunId);
        if (glOwnerRun.Count == 0)
            return new(containerId, 0, 0, []);
        var usedGl = (await db.ReconMatches.Where(x => runIds.Contains(x.ReconRunId) && x.GlStagingId != null)
            .Select(x => x.GlStagingId!.Value).ToListAsync(ct)).ToHashSet();

        var procIds = matches.Select(m => m.ProcessorStagingId).ToList();
        var transactions = await db.StgInterchangeFees.AsNoTracking()
            .Where(x => procIds.Contains(x.StgInterchangeFeeId)).ToDictionaryAsync(x => x.StgInterchangeFeeId, ct);
        var glIds = glOwnerRun.Keys.ToList();
        var accounts = await db.GlSettlementAccounts.Where(x => x.IsActive).ToDictionaryAsync(x => x.AcctNum, x => x.Role, ct);
        var glRows = (await db.StgGlEntries.AsNoTracking().Where(x => glIds.Contains(x.StgGlEntryId)).ToListAsync(ct))
            .Where(x => x.AcctNum is not null && accounts.TryGetValue(x.AcctNum, out var role)
                && role is GlAccountRole.CardholderSettlement or GlAccountRole.IntermediarySettlement).ToList();

        var (added, ambiguous, changes) = await ApplyPoolMatchAsync(db, runIds, matches, glRows, glOwnerRun,
            usedGl, transactions, accounts, rangeLabel, maxSpreadDays: spread, requireReference: false,
            MethodLabel, "the month pool", container.PeriodKey, userId, ct);
        await tx.CommitAsync(ct);
        return new(containerId, added, ambiguous, changes);
    }

    /// <summary>
    /// The orphan sweep: a spread-UNBOUNDED, reference-anchored final pass over all standalone month
    /// batches (rolled-up daily months and classic monthly Bulk batches) for one scheme. It catches
    /// long-tail stragglers that settled beyond the spread or landed in non-adjacent months. With no
    /// date window the only safe anchor is an exact reference match (plus amount/currency/leg and
    /// uniqueness on both sides); reference-less stragglers are left for manual review. Writes into
    /// whichever batch owns each record (freeze does not block late settlement). Manager/Admin only.
    /// </summary>
    public async Task<OrphanSweepResult> OrphanSweepAsync(int? schemeProfileId = null, CancellationToken ct = default)
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new UnauthorizedAccessException("Sign in to run the orphan sweep.");
        var grants = await permissions.GetGrantsAsync(userId);
        if (!grants.TryGetValue("BULKRECON", out var grant) || !grant.CanView || !grant.CanEdit)
            throw new UnauthorizedAccessException("The orphan sweep requires Recon Manager or Administrator rights.");

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct))
            throw new UnauthorizedAccessException("An active user account is required.");
        db.Database.SetCommandTimeout(600);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await GlAssignmentLock.AcquireAsync(db, ct);

        // Scope: standalone month batches (rolled-up daily months + classic monthly Bulk), one scheme.
        var runs = await db.ReconRuns.Where(r => r.Function == ReconFunction.InterchangeToGl
                && r.ParentPeriodRunId == null && r.Status == ReconRunStatus.Completed && !r.IsArchived
                && r.SchemeProfileId == schemeProfileId)
            .Select(r => new { r.ReconRunId, r.PeriodKey, r.PeriodStart, r.PeriodEnd }).ToListAsync(ct);
        if (runs.Count < 1)
            return new(0, 0, 0, []);
        var runIds = runs.Select(r => r.ReconRunId).ToList();
        var rangeLabel = runs.ToDictionary(r => r.ReconRunId,
            r => r.PeriodStart is { } s && r.PeriodEnd is { } e
                ? (s == e ? s.ToString("yyyy-MM-dd") : $"{s:yyyy-MM-dd}–{e:yyyy-MM-dd}") : r.PeriodKey);

        var matches = await db.ReconMatches.AsNoTracking()
            .Where(x => runIds.Contains(x.ReconRunId)
                && (x.MatchStatus == MatchStatus.UnmatchedProcessor || x.MatchStatus == MatchStatus.Ambiguous)
                && x.GlStagingId == null && x.ProcessorSource == StagingSource.InterchangeFee)
            .ToListAsync(ct);
        var glOwnerRun = (await db.ReconExceptions.AsNoTracking()
            .Where(e => runIds.Contains(e.ReconRunId) && e.ExceptionType == MatchStatus.UnmatchedGl && e.GlStagingId != null)
            .Select(e => new { GlId = e.GlStagingId!.Value, e.ReconRunId }).ToListAsync(ct))
            .GroupBy(x => x.GlId).ToDictionary(g => g.Key, g => g.First().ReconRunId);
        if (matches.Count == 0 || glOwnerRun.Count == 0)
            return new(0, 0, runs.Count, []);
        var usedGl = (await db.ReconMatches.Where(x => runIds.Contains(x.ReconRunId) && x.GlStagingId != null)
            .Select(x => x.GlStagingId!.Value).ToListAsync(ct)).ToHashSet();
        var procIds = matches.Select(m => m.ProcessorStagingId).ToList();
        var transactions = await db.StgInterchangeFees.AsNoTracking()
            .Where(x => procIds.Contains(x.StgInterchangeFeeId)).ToDictionaryAsync(x => x.StgInterchangeFeeId, ct);
        var glIds = glOwnerRun.Keys.ToList();
        var accounts = await db.GlSettlementAccounts.Where(x => x.IsActive).ToDictionaryAsync(x => x.AcctNum, x => x.Role, ct);
        var glRows = (await db.StgGlEntries.AsNoTracking().Where(x => glIds.Contains(x.StgGlEntryId)).ToListAsync(ct))
            .Where(x => x.AcctNum is not null && accounts.TryGetValue(x.AcctNum, out var role)
                && role is GlAccountRole.CardholderSettlement or GlAccountRole.IntermediarySettlement).ToList();

        var (added, ambiguous, changes) = await ApplyPoolMatchAsync(db, runIds, matches, glRows, glOwnerRun,
            usedGl, transactions, accounts, rangeLabel, maxSpreadDays: null, requireReference: true,
            OrphanMethodLabel, "the orphan sweep", targetPeriodKey: null, userId, ct);
        await tx.CommitAsync(ct);
        return new(added, ambiguous, runs.Count, changes);
    }

    /// <summary>
    /// The shared matcher + set-based write-back for a pool of outstanding processor items against a
    /// pool of available GL. Runs inside the caller's open transaction (the caller commits). It flips
    /// unique one-to-one pairs to Matched and competing pairs to Ambiguous, clears the corresponding
    /// exceptions in each owner run, recomputes each touched run's summaries/counts, and logs a change
    /// row per run. <paramref name="maxSpreadDays"/> null = no date limit (Orphan); when set, pairs
    /// must fall within it. <paramref name="requireReference"/> true = both references present and
    /// equal (the safe anchor when there is no date limit); false = references must merely agree.
    /// </summary>
    private async Task<(int Added, int Ambiguous, List<BackMatchChange> Changes)> ApplyPoolMatchAsync(
        AppDbContext db, List<int> runIds, List<ReconMatch> matches, List<StgGlEntry> glRows,
        Dictionary<int, int> glOwnerRun, HashSet<int> usedGl, Dictionary<int, StgInterchangeFee> transactions,
        Dictionary<string, GlAccountRole> accounts, Dictionary<int, string> rangeLabel,
        int? maxSpreadDays, bool requireReference, string methodLabel, string poolDescription,
        string? targetPeriodKey, Guid userId, CancellationToken ct)
    {
        ReconSettlementLeg Leg(StgGlEntry g) => accounts[g.AcctNum!] == GlAccountRole.CardholderSettlement
            ? ReconSettlementLeg.DebitCardholder : ReconSettlementLeg.CreditIntermediary;
        (Key Key, DateOnly Date)? ProcKey(ReconMatch m)
        {
            if (m.GlStagingId is not null || m.ProcessorSource != StagingSource.InterchangeFee ||
                !transactions.TryGetValue(m.ProcessorStagingId, out var t) || t.OrigineCode?.Trim() == "0" ||
                t.TransactionDate is null || t.BillingAmount is null || t.BillingCcy is null || t.BillingCcy <= 0)
                return null;
            return (new Key(m.SettlementLeg, Math.Abs(t.BillingAmount.Value), t.BillingCcy.Value), DateOnly.FromDateTime(t.TransactionDate.Value));
        }
        (Key Key, DateOnly Date)? GlKeyOf(StgGlEntry g)
        {
            var currency = MatchingEvidence.Currency(g.TranCcyCode);
            if (usedGl.Contains(g.StgGlEntryId) || g.ValueDate is null || g.TranAmt is null || currency is null) return null;
            return (new Key(Leg(g), Math.Abs(g.TranAmt.Value), currency.Value), g.ValueDate.Value);
        }
        bool CandidateOk(ReconMatch m, StgGlEntry g, int dayDiff)
        {
            if (maxSpreadDays is int s && dayDiff > s) return false;
            var t = transactions[m.ProcessorStagingId];
            var p = m.SettlementLeg == ReconSettlementLeg.CreditIntermediary ? t.MicrofilmRefNumber?.Trim() : t.ReferenceNumber?.Trim();
            var r = g.TranParticular2?.Trim();
            if (m.SettlementLeg == ReconSettlementLeg.CreditIntermediary) r = r is { Length: > 12 } ? r[12..].Trim() : null;
            if (requireReference) return !string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(r) && p == r;
            return string.IsNullOrEmpty(p) || string.IsNullOrEmpty(r) || p == r;
        }

        var glByKey = glRows.Select(g => (Row: g, K: GlKeyOf(g))).Where(x => x.K is not null)
            .ToLookup(x => x.K!.Value.Key, x => (x.Row, x.K!.Value.Date));
        var procByKey = matches.Select(m => (Row: m, K: ProcKey(m))).Where(x => x.K is not null)
            .Select(x => (x.Row, x.K!.Value.Key, x.K!.Value.Date)).ToList();

        var glCandidateCount = new Dictionary<int, int>();
        var perProc = new List<(ReconMatch Match, DateOnly Date, List<StgGlEntry> Options)>();
        foreach (var (m, key, date) in procByKey)
        {
            var options = new List<StgGlEntry>(2);
            foreach (var (g, gdate) in glByKey[key])
            {
                if (!CandidateOk(m, g, Math.Abs(gdate.DayNumber - date.DayNumber))) continue;
                if (options.Count < 2) options.Add(g);
                glCandidateCount[g.StgGlEntryId] = glCandidateCount.GetValueOrDefault(g.StgGlEntryId) + 1;
            }
            perProc.Add((m, date, options));
        }

        var basis = maxSpreadDays is int sp ? $"transaction date / GL value date within {sp} day(s), " : "exact reference, ";
        int added = 0, ambiguous = 0;
        var updated = new List<ReconMatch>();
        var glOwnerResolved = new List<int>();
        foreach (var (m, date, options) in perProc)
        {
            if (options.Count == 0) continue;
            if (options.Count > 1 || glCandidateCount[options[0].StgGlEntryId] != 1)
            {
                if (m.MatchStatus != MatchStatus.Ambiguous)
                {
                    m.MatchStatus = MatchStatus.Ambiguous;
                    m.MatchComment = $"{methodLabel}: competing GL candidates share the match key. Review required.";
                    ambiguous++; updated.Add(m);
                }
                continue;
            }
            var g = options[0];
            var currency = MatchingEvidence.Currency(g.TranCcyCode)!.Value;
            m.MatchStatus = MatchStatus.Matched;
            m.GlStagingId = g.StgGlEntryId;
            m.GlSourceBatchId = g.ImportBatchId;
            m.MatchedAcrossPeriods = glOwnerRun.TryGetValue(g.StgGlEntryId, out var owner) && owner != m.ReconRunId;
            m.GlAccountNumber = g.AcctNum;
            m.GlReference = g.TranParticular2;
            m.GlParticular2 = g.TranParticular2;
            m.GlDescription = g.TranParticular;
            m.GlDate = g.TranDate;
            m.GlValueDate = g.ValueDate;
            m.GlPostingDateTime = g.EntryDate ?? g.VfdDate;
            m.GlAmount = Math.Abs(g.TranAmt!.Value);
            m.ReconciliationAmount = Math.Abs(g.TranAmt.Value);
            m.Ccy = currency;
            m.GlCcy = currency;
            m.Confidence = 78m; // rule evidence, not a probability
            m.ScoreBreakdown = $"{methodLabel}: {basis}exact billing amount, currency and settlement leg; unique on both sides.";
            m.MatchComment = $"{methodLabel}: {date:yyyy-MM-dd} ↔ GL value date {g.ValueDate:yyyy-MM-dd}, {m.GlAmount:0.####}, {IsoCurrencyCatalog.Display(currency)}. GL batch #{g.ImportBatchId}.";
            usedGl.Add(g.StgGlEntryId);
            glCandidateCount[g.StgGlEntryId] = -1;
            glOwnerResolved.Add(g.StgGlEntryId);
            added++; updated.Add(m);
        }

        if (updated.Count == 0) return (0, 0, []);

        var now = DateTime.UtcNow;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        foreach (var m in updated) db.Attach(m).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        db.ChangeTracker.AutoDetectChangesEnabled = true;

        var matchedProcIds = updated.Where(m => m.MatchStatus == MatchStatus.Matched).Select(m => m.ProcessorStagingId).ToList();
        var ambiguousProcIds = updated.Where(m => m.MatchStatus == MatchStatus.Ambiguous).Select(m => m.ProcessorStagingId).ToList();
        if (matchedProcIds.Count > 0)
            await db.ReconExceptions.Where(e => runIds.Contains(e.ReconRunId) && e.ExceptionType != MatchStatus.UnmatchedGl
                && e.ProcessorStagingId != null && matchedProcIds.Contains(e.ProcessorStagingId.Value)).ExecuteDeleteAsync(ct);
        if (glOwnerResolved.Count > 0)
            await db.ReconExceptions.Where(e => runIds.Contains(e.ReconRunId) && e.ExceptionType == MatchStatus.UnmatchedGl
                && e.GlStagingId != null && glOwnerResolved.Contains(e.GlStagingId.Value)).ExecuteDeleteAsync(ct);
        if (ambiguousProcIds.Count > 0)
            await db.ReconExceptions.Where(e => runIds.Contains(e.ReconRunId) && e.ExceptionType != MatchStatus.UnmatchedGl
                && e.ExceptionType != MatchStatus.Ambiguous && e.ProcessorStagingId != null && ambiguousProcIds.Contains(e.ProcessorStagingId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ExceptionType, MatchStatus.Ambiguous)
                    .SetProperty(e => e.UpdatedBy, userId).SetProperty(e => e.UpdatedDate, now), ct);

        var procOwnerAdds = updated.Where(m => m.MatchStatus == MatchStatus.Matched)
            .GroupBy(m => m.ReconRunId).ToDictionary(g => g.Key, g => g.Count());
        var glOwnerAdds = updated.Where(m => m.MatchStatus == MatchStatus.Matched && m.GlStagingId.HasValue)
            .Select(m => glOwnerRun.TryGetValue(m.GlStagingId!.Value, out var r) ? r : m.ReconRunId)
            .GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
        var affected = procOwnerAdds.Keys.Union(glOwnerAdds.Keys)
            .Union(updated.Where(m => m.MatchStatus == MatchStatus.Ambiguous).Select(m => m.ReconRunId)).Distinct().ToList();

        var changes = new List<BackMatchChange>();
        foreach (var runId in affected)
        {
            await RecomputeRunAsync(db, runId, now, userId, ct);
            var a = procOwnerAdds.GetValueOrDefault(runId);
            var g = glOwnerAdds.GetValueOrDefault(runId);
            changes.Add(new BackMatchChange(runId, rangeLabel.GetValueOrDefault(runId, $"run #{runId}"), Math.Max(a, g)));
            db.ReconRunPasses.Add(new ReconRunPass
            {
                ReconRunId = runId, Kind = ReconPassKind.CrossPeriod, RunGroup = Guid.NewGuid(),
                Added = a, Ambiguous = updated.Count(m => m.MatchStatus == MatchStatus.Ambiguous && m.ReconRunId == runId),
                RunAt = now, TargetPeriodKey = targetPeriodKey,
                Detail = $"{methodLabel}: resolved {a} transaction(s), {g} GL row(s) in this run from {poolDescription}.",
                CreatedBy = userId, CreatedDate = now
            });
        }
        await db.SaveChangesAsync(ct);
        return (added, ambiguous, changes.OrderBy(c => c.RangeLabel).ToList());
    }

    /// <summary>
    /// Consolidate a completed month into a canonical month batch: re-point every date-range child's
    /// matches and exceptions onto a new Month-granularity run, archive the children, and mark the
    /// container rolled up. The month batch then behaves like a classic Bulk batch (it is what the
    /// Orphan run and verification workflow act on), and — because GL is discovered from UnmatchedGl
    /// exceptions — it still participates in the next month's load-time back-match. Manager/Admin only.
    /// </summary>
    public async Task<RollUpResult> RollUpAsync(int containerId, CancellationToken ct = default)
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new UnauthorizedAccessException("Sign in to roll up a month.");
        var grants = await permissions.GetGrantsAsync(userId);
        if (!grants.TryGetValue("BULKRECON", out var grant) || !grant.CanView || !grant.CanEdit)
            throw new UnauthorizedAccessException("Rolling up a month requires Recon Manager or Administrator rights.");

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct))
            throw new UnauthorizedAccessException("An active user account is required.");
        db.Database.SetCommandTimeout(600);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await GlAssignmentLock.AcquireAsync(db, ct);

        var container = await db.ReconPeriodRuns
            .FromSqlInterpolated($"SELECT * FROM [ReconPeriodRuns] WITH (UPDLOCK, HOLDLOCK) WHERE [ReconPeriodRunId] = {containerId}")
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The month container was not found.");
        if (container.Status != ReconPeriodRunStatus.Open)
            throw new InvalidOperationException("This month has already been rolled up.");

        var children = await db.ReconRuns.Where(r => r.ParentPeriodRunId == containerId
                && r.Status == ReconRunStatus.Completed && !r.IsArchived)
            .Select(r => new { r.ReconRunId, r.ProcessorBatchId, r.GlBatchId, r.ProcessorCount }).ToListAsync(ct);
        if (children.Count == 0)
            throw new InvalidOperationException("There are no completed date-range runs to roll up.");
        var childIds = children.Select(c => c.ReconRunId).ToList();

        DateOnly.TryParseExact(container.PeriodKey + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var first);
        var last = first.AddMonths(1).AddDays(-1);
        var now = DateTime.UtcNow;
        var monthBatch = new ReconRun
        {
            Function = ReconFunction.InterchangeToGl, PeriodKey = container.PeriodKey,
            PeriodStart = first, PeriodEnd = last, Status = ReconRunStatus.Completed,
            SchemeProfileId = container.SchemeProfileId, ParentPeriodRunId = null,
            PeriodGranularity = PeriodGranularity.Month,
            ProcessorBatchId = children[0].ProcessorBatchId, GlBatchId = children[0].GlBatchId,
            ProcessorCount = children.Sum(c => c.ProcessorCount),
            StartedDate = now, CompletedDate = now, CreatedBy = userId, CreatedDate = now
        };
        db.ReconRuns.Add(monthBatch);
        await db.SaveChangesAsync(ct);
        var monthRunId = monthBatch.ReconRunId;

        await db.ReconMatches.Where(m => childIds.Contains(m.ReconRunId))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReconRunId, monthRunId), ct);
        await db.ReconExceptions.Where(e => childIds.Contains(e.ReconRunId))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReconRunId, monthRunId), ct);
        await db.ReconPassSummaries.Where(x => childIds.Contains(x.ReconRunId)).ExecuteDeleteAsync(ct);
        await db.ReconRuns.Where(r => childIds.Contains(r.ReconRunId))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsArchived, true)
                .SetProperty(r => r.UpdatedBy, userId).SetProperty(r => r.UpdatedDate, now), ct);

        await RecomputeRunAsync(db, monthRunId, now, userId, ct);

        container.Status = ReconPeriodRunStatus.RolledUp;
        container.RolledUpRunId = monthRunId;
        container.RolledUpDate = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var matched = await db.ReconMatches.CountAsync(m => m.ReconRunId == monthRunId && m.MatchStatus == MatchStatus.Matched, ct);
        var exceptions = await db.ReconExceptions.CountAsync(e => e.ReconRunId == monthRunId, ct);
        return new(containerId, monthRunId, matched, exceptions);
    }

    /// <summary>The immediate calendar-month neighbours of a yyyy-MM key, or empty if it does not parse.</summary>
    private static List<string> AdjacentMonthKeys(string periodKey)
    {
        if (!DateOnly.TryParseExact(periodKey + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var month))
            return [];
        return [month.AddMonths(-1).ToString("yyyy-MM"), month.AddMonths(1).ToString("yyyy-MM")];
    }

    /// <summary>Rebuild one run's per-currency summaries and headline counts from its current match/exception state.</summary>
    private static async Task RecomputeRunAsync(AppDbContext db, int runId, DateTime now, Guid userId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ReconPassSummaries WHERE ReconRunId = {runId};", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
WITH b AS (
  SELECT SettlementLeg,
         CASE WHEN Ccy IN (480, 840, 978) THEN Ccy ELSE 0 END AS Bucket,
         ProcessorDate,
         CASE WHEN MatchStatus = 0 THEN 1 ELSE 0 END AS IsMatched,
         COALESCE(ReconciliationAmount, ProcessorAmount, 0) AS Amt,
         Confidence
  FROM ReconMatches WHERE ReconRunId = {runId}
)
INSERT INTO ReconPassSummaries (ReconRunId, SettlementLeg, Ccy, Day, MatchedCount, MatchedAmount, ExceptionCount, ExceptionAmount, AverageConfidence)
SELECT {runId}, SettlementLeg, Bucket, ProcessorDate, SUM(IsMatched),
       SUM(CASE WHEN IsMatched = 1 THEN Amt ELSE 0 END), SUM(1 - IsMatched),
       SUM(CASE WHEN IsMatched = 0 THEN Amt ELSE 0 END),
       CASE WHEN SUM(IsMatched) > 0 THEN ROUND(AVG(CASE WHEN IsMatched = 1 THEN Confidence END), 2) ELSE 0 END
FROM b WHERE ProcessorDate IS NOT NULL GROUP BY SettlementLeg, Bucket, ProcessorDate
UNION ALL
SELECT {runId}, SettlementLeg, Bucket, NULL, SUM(IsMatched),
       SUM(CASE WHEN IsMatched = 1 THEN Amt ELSE 0 END), SUM(1 - IsMatched),
       SUM(CASE WHEN IsMatched = 0 THEN Amt ELSE 0 END),
       CASE WHEN SUM(IsMatched) > 0 THEN ROUND(AVG(CASE WHEN IsMatched = 1 THEN Confidence END), 2) ELSE 0 END
FROM b GROUP BY SettlementLeg, Bucket;", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE ReconRuns SET
  MatchedCount = (SELECT COUNT(*) FROM ReconMatches WHERE ReconRunId = {runId} AND MatchStatus = 0),
  ExceptionCount = (SELECT COUNT(*) FROM ReconExceptions WHERE ReconRunId = {runId}),
  AverageConfidence = COALESCE((SELECT ROUND(AVG(Confidence), 2) FROM ReconMatches WHERE ReconRunId = {runId} AND MatchStatus = 0), 0),
  UpdatedBy = {userId}, UpdatedDate = {now}
WHERE ReconRunId = {runId};", ct);
    }
}
