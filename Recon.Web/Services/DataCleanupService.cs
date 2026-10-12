using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;

namespace Recon.Web.Services;

public record CleanupPreview(int Batches, int Runs, int Archives, int SharedBatchesKept = 0);

public class DataCleanupService(
    IDbContextFactory<AppDbContext> factory,
    AuthenticationStateProvider authentication,
    IConfiguration configuration,
    ILogger<DataCleanupService> logger)
{
    public const string Confirmation = "DELETE ALL TRANSACTIONS";

    public static string ConfirmationFor(string? beforeMonth) => beforeMonth is null
        ? Confirmation : $"DELETE BEFORE {ValidateMonth(beforeMonth):yyyy-MM}";

    private static DateOnly ValidateMonth(string month)
    {
        if (month.Length != 7 || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var cutoff))
            throw new InvalidOperationException("Select a valid cutoff month (YYYY-MM).");
        return cutoff;
    }

    private static bool IsBefore(string period, DateOnly? start, DateOnly? end, DateOnly cutoff)
    {
        // Preserve ranges touching the cutoff, even when their period key is older.
        if (start >= cutoff || end >= cutoff) return false;
        if (end.HasValue) return end.Value < cutoff;
        return period.Length == 7 && DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) && month < cutoff;
    }

    private async Task<Guid> RequireAdministratorAsync(AppDbContext db)
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !await db.UserAppAccesses.AnyAsync(x => x.UserId == userId && x.IsActive &&
                x.User.IsActive && x.AppAccess.IsActive && x.AppAccess.AccessName == "Administrator"))
            throw new UnauthorizedAccessException("Only an active Administrator can clear transaction data.");
        return userId;
    }

    private sealed record CleanupScope(int[] Batches, int[] Runs, int[] Archives, int[] Freezes, int SharedBatchesKept);

    private static async Task<CleanupScope> GetScopeAsync(AppDbContext db, string? beforeMonth)
    {
        DateOnly? cutoff = beforeMonth is null ? null : ValidateMonth(beforeMonth);
        // Load only batch/run metadata; transaction rows remain in the database.
        var runs = await db.ReconRuns.AsNoTracking().Select(x => new { x.ReconRunId, x.PeriodKey, x.PeriodStart, x.PeriodEnd, x.ProcessorBatchId, x.GlBatchId, x.NetworkBatchId }).ToListAsync();
        var runIds = runs.Where(x => cutoff is null || IsBefore(x.PeriodKey, x.PeriodStart, x.PeriodEnd, cutoff.Value)).Select(x => x.ReconRunId).ToHashSet();
        var retainedBatchIds = runs.Where(x => !runIds.Contains(x.ReconRunId))
            .SelectMany(x => new int?[] { x.ProcessorBatchId, x.GlBatchId, x.NetworkBatchId }).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        var externalBatches = await db.ReconMatches.Where(x => !runIds.Contains(x.ReconRunId) && x.GlSourceBatchId != null)
            .Select(x => x.GlSourceBatchId!.Value).Distinct().ToListAsync();
        retainedBatchIds.UnionWith(externalBatches);
        var batches = await db.ImportBatches.Select(x => new { x.ImportBatchId, x.PeriodKey }).ToListAsync();
        var eligibleBatches = batches.Where(x => cutoff is null || IsBefore(x.PeriodKey, null, null, cutoff.Value)).ToList();
        var archives = await db.BatchArchives.Select(x => new { x.BatchArchiveId, x.PeriodKey, x.PeriodStart, x.PeriodEnd }).ToListAsync();
        var freezes = await db.BatchFreezes.Select(x => new { x.BatchFreezeId, x.PeriodKey, x.PeriodStart, x.PeriodEnd }).ToListAsync();
        return new(
            eligibleBatches.Where(x => !retainedBatchIds.Contains(x.ImportBatchId)).Select(x => x.ImportBatchId).ToArray(),
            runIds.ToArray(),
            archives.Where(x => cutoff is null || IsBefore(x.PeriodKey, x.PeriodStart, x.PeriodEnd, cutoff.Value)).Select(x => x.BatchArchiveId).ToArray(),
            freezes.Where(x => cutoff is null || IsBefore(x.PeriodKey, x.PeriodStart, x.PeriodEnd, cutoff.Value)).Select(x => x.BatchFreezeId).ToArray(),
            eligibleBatches.Count(x => retainedBatchIds.Contains(x.ImportBatchId)));
    }

    public async Task<CleanupPreview> PreviewAsync(string? beforeMonth = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        await RequireAdministratorAsync(db);
        var scope = await GetScopeAsync(db, beforeMonth);
        return new(scope.Batches.Length, scope.Runs.Length, scope.Archives.Length, scope.SharedBatchesKept);
    }

    public async Task ClearAsync(string confirmation, string? beforeMonth = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var userId = await RequireAdministratorAsync(db);
        var expected = ConfirmationFor(beforeMonth);
        if (confirmation != expected)
            throw new InvalidOperationException($"Type {expected} to confirm the cleanup.");
        if (configuration.GetValue<bool>("DemoData:Enabled"))
            throw new InvalidOperationException("Disable DemoData:Enabled in server configuration and restart before cleanup, so demo transactions cannot return.");

        db.Database.SetCommandTimeout(300);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await db.Database.ExecuteSqlRawAsync("SELECT COUNT_BIG(*) FROM [ImportBatches] WITH (TABLOCKX, HOLDLOCK)");
        await db.Database.ExecuteSqlRawAsync("SELECT COUNT_BIG(*) FROM [ReconRuns] WITH (TABLOCKX, HOLDLOCK)");
        if (await db.ImportBatches.AnyAsync(x => x.Status == ImportStatus.Uploaded) ||
            await db.ReconRuns.AnyAsync(x => x.Status == ReconRunStatus.Running))
            throw new InvalidOperationException("An import or reconciliation is still in progress. Let it finish before clearing data.");

        var scope = await GetScopeAsync(db, beforeMonth);
        var all = beforeMonth is null;
        var batchIds = scope.Batches;
        var runIds = scope.Runs;
        var archiveIds = scope.Archives;
        var freezeIds = scope.Freezes;
        var exceptions = db.ReconExceptions.IgnoreQueryFilters().Where(x => all || runIds.Contains(x.ReconRunId));
        var exceptionIds = exceptions.Select(x => x.ReconExceptionId);
        // Explicit allowlist: reference data and configuration are never deleted.
        await db.ReconExceptionComments.IgnoreQueryFilters().Where(x => all || exceptionIds.Contains(x.ReconExceptionId)).ExecuteDeleteAsync();
        await exceptions.ExecuteDeleteAsync();
        await db.ReconMatches.Where(x => all || runIds.Contains(x.ReconRunId)).ExecuteDeleteAsync();
        await db.ReconPassSummaries.Where(x => all || runIds.Contains(x.ReconRunId)).ExecuteDeleteAsync();
        await db.AtmNetworkValidations.Where(x => all || runIds.Contains(x.ReconRunId)).ExecuteDeleteAsync();
        await db.ReconWorkflowActors.Where(x => all || runIds.Contains(x.ReconRunId)).ExecuteDeleteAsync();
        await db.ReconWorkflowHistories.Where(x => all || runIds.Contains(x.ReconRunId)).ExecuteDeleteAsync();
        await db.BatchArchiveTransactions.Where(x => all || archiveIds.Contains(x.BatchArchiveId)).ExecuteDeleteAsync();
        await db.BatchArchives.Where(x => all || archiveIds.Contains(x.BatchArchiveId)).ExecuteDeleteAsync();
        await db.BatchFreezes.Where(x => all || freezeIds.Contains(x.BatchFreezeId)).ExecuteDeleteAsync();
        await db.ReconRuns.Where(x => all || runIds.Contains(x.ReconRunId)).ExecuteDeleteAsync();
        await db.NormalizedTransactions.Where(x => all || batchIds.Contains(x.ImportBatchId)).ExecuteDeleteAsync();
        await db.NormalizedGlEntries.Where(x => all || batchIds.Contains(x.ImportBatchId)).ExecuteDeleteAsync();
        await db.StgNetworkT464.Where(x => all || batchIds.Contains(x.ImportBatchId)).ExecuteDeleteAsync();
        await db.StgDebitSettlements.Where(x => all || batchIds.Contains(x.ImportBatchId)).ExecuteDeleteAsync();
        await db.StgInterchangeFees.Where(x => all || batchIds.Contains(x.ImportBatchId)).ExecuteDeleteAsync();
        await db.StgGlEntries.Where(x => all || batchIds.Contains(x.ImportBatchId)).ExecuteDeleteAsync();
        await db.StgGlBulkPostings.Where(x => all || batchIds.Contains(x.ImportBatchId)).ExecuteDeleteAsync();
        var batches = await db.ImportBatches.Where(x => all || batchIds.Contains(x.ImportBatchId)).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        logger.LogWarning("Administrator {UserId} cleared transaction data ({Scope}) and {BatchCount} import batches at {UtcTime}. Reference data was preserved.", userId, beforeMonth is null ? "all periods" : $"before {beforeMonth}", batches, DateTime.UtcNow);
    }
}
