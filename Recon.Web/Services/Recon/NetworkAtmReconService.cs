using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;

namespace Recon.Web.Services.Recon;

public record NetworkAtmRunResult(int ReconRunId, int ReferenceCount, int MatchedCount, int ExceptionCount);

public interface INetworkAtmReconService
{
    /// <summary>Creates a Network ↔ ATM run over a T464 network batch and an ATM settlement batch.</summary>
    Task<NetworkAtmRunResult> RunAsync(int networkBatchId, int atmBatchId, string periodKey,
        DateOnly? periodStart = null, DateOnly? periodEnd = null, CancellationToken ct = default);
}

/// <summary>
/// Standalone reconciliation of the MasterCard T464 network settlement file against the ATM
/// debit settlement report. Unlike ATM/POS/Bulk, there is no GL side — this validates the
/// upstream processor/network traffic itself.
/// </summary>
public class NetworkAtmReconService(IDbContextFactory<AppDbContext> dbFactory, IAtmNetworkValidationService validation) : INetworkAtmReconService
{
    public async Task<NetworkAtmRunResult> RunAsync(int networkBatchId, int atmBatchId, string periodKey,
        DateOnly? periodStart = null, DateOnly? periodEnd = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = new ReconRun
        {
            Function = ReconFunction.NetworkToAtm,
            PeriodKey = periodKey,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Status = ReconRunStatus.Running,
            ProcessorBatchId = atmBatchId,
            GlBatchId = null,
            NetworkBatchId = networkBatchId,
            StartedDate = DateTime.UtcNow
        };
        db.ReconRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            await validation.ValidateAsync(run.ReconRunId, networkBatchId, atmBatchId, ct);

            var results = db.AtmNetworkValidations.Where(v => v.ReconRunId == run.ReconRunId);
            var referenceCount = await results.CountAsync(ct);
            var matched = await results.CountAsync(v => v.Status == AtmNetworkValidationStatus.Matched, ct);

            run.ProcessorCount = await db.StgDebitSettlements.CountAsync(x => x.ImportBatchId == atmBatchId, ct);
            run.MatchedCount = matched;
            run.ExceptionCount = referenceCount - matched;
            run.AverageConfidence = referenceCount > 0 ? Math.Round(100m * matched / referenceCount, 2) : 0m;
            run.Status = ReconRunStatus.Completed;
            run.CompletedDate = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return new NetworkAtmRunResult(run.ReconRunId, referenceCount, matched, referenceCount - matched);
        }
        catch
        {
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
}
