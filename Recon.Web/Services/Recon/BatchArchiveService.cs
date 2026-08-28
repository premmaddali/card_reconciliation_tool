using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;

namespace Recon.Web.Services.Recon;

public interface IBatchArchiveService
{
    Task ArchiveRunAsync(int runId, bool frozen, CancellationToken ct = default);
    Task FreezeScopeAsync(int runId, CancellationToken ct = default);
    Task<bool> IsFrozenAsync(ReconFunction function, DateOnly from, DateOnly to, int? schemeProfileId, CancellationToken ct = default);
}

public sealed class BatchArchiveService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUserService currentUser, IPermissionService permissions) : IBatchArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task ArchiveRunAsync(int runId, bool frozen, CancellationToken ct = default)
    {
        await EnsureCanEditAsync(frozen ? "FREEZE_PERIOD" : "ARCHIVE_BATCH");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await ArchiveRunAsync(db, runId, frozen, ct);
        await db.SaveChangesAsync(ct);
    }
    public async Task FreezeScopeAsync(int runId, CancellationToken ct = default)
    {
        await EnsureCanEditAsync("FREEZE_PERIOD");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var selected = await db.ReconRuns.SingleOrDefaultAsync(r => r.ReconRunId == runId, ct) ?? throw new InvalidOperationException("Reconciliation batch was not found.");
        if (selected.Status != ReconRunStatus.Completed) throw new InvalidOperationException("Only completed reconciliation batches can be frozen.");
        if (await db.BatchFreezes.AnyAsync(f => f.Function == selected.Function && f.PeriodKey == selected.PeriodKey && f.SchemeProfileId == selected.SchemeProfileId, ct)) throw new InvalidOperationException("This reconciliation period is already frozen.");
        var ids = await db.ReconRuns.Where(r => !r.IsArchived && r.Status == ReconRunStatus.Completed && r.Function == selected.Function && r.PeriodKey == selected.PeriodKey && r.SchemeProfileId == selected.SchemeProfileId).Select(r => r.ReconRunId).ToListAsync(ct);
        foreach (var id in ids) await ArchiveRunAsync(db, id, true, ct);
        db.BatchFreezes.Add(new BatchFreeze { Function = selected.Function, PeriodKey = selected.PeriodKey, PeriodStart = selected.PeriodStart, PeriodEnd = selected.PeriodEnd, SchemeProfileId = selected.SchemeProfileId, FrozenFromReconRunId = runId, FrozenDate = DateTime.UtcNow, FrozenBy = currentUser.UserId });
        await db.SaveChangesAsync(ct);
    }
    public async Task<bool> IsFrozenAsync(ReconFunction function, DateOnly from, DateOnly to, int? schemeProfileId, CancellationToken ct = default) { await using var db = await dbFactory.CreateDbContextAsync(ct); return await db.BatchFreezes.AnyAsync(f => f.Function == function && f.PeriodStart == from && f.PeriodEnd == to && f.SchemeProfileId == schemeProfileId, ct); }
    private async Task EnsureCanEditAsync(string pageCode)
    {
        var grants = await permissions.GetGrantsAsync(currentUser.UserId);
        if (!grants.TryGetValue(pageCode, out var grant) || !grant.CanEdit)
            throw new UnauthorizedAccessException("You do not have permission to perform this action.");
    }
    private async Task ArchiveRunAsync(AppDbContext db, int runId, bool frozen, CancellationToken ct)
    {
        var run = await db.ReconRuns.SingleOrDefaultAsync(r => r.ReconRunId == runId, ct) ?? throw new InvalidOperationException("Reconciliation batch was not found."); if (run.IsArchived) return;
        var matches = await db.ReconMatches.Where(m => m.ReconRunId == runId).ToListAsync(ct); var exceptions = await db.ReconExceptions.Include(e => e.Comments).Where(e => e.ReconRunId == runId).ToListAsync(ct);
        db.BatchArchives.Add(new BatchArchive { OriginalReconRunId = runId, Function = run.Function, PeriodKey = run.PeriodKey, PeriodStart = run.PeriodStart, PeriodEnd = run.PeriodEnd, SchemeProfileId = run.SchemeProfileId, ArchivedByFreeze = frozen, ArchivedDate = DateTime.UtcNow, ArchivedBy = currentUser.UserId, SnapshotJson = JsonSerializer.Serialize(new { Run = run, Matches = matches, Exceptions = exceptions }, JsonOptions), Transactions = matches.Select(m => new BatchArchiveTransaction { ReferenceNumber = m.ReferenceNumber, MatchStatus = m.MatchStatus, ReconciliationAmount = m.ReconciliationAmount, GlAmount = m.GlAmount, SnapshotJson = JsonSerializer.Serialize(m, JsonOptions) }).ToList() }); run.IsArchived = true;
    }
}
