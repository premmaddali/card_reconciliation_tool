using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;

namespace Recon.Web.Services.Recon;

/// <summary>Original imports behind a normal run, daily range, or consolidated daily month.</summary>
public record ReconAnalysisInputs(int SourceRunId, int[] ProcessorBatchIds, int[] GlBatchIds,
    PeriodGranularity Granularity, bool IsConsolidated)
{
    public static async Task<ReconAnalysisInputs> LoadAsync(AppDbContext db, ReconRun source, CancellationToken ct)
    {
        if (source.AnalysisOfRunId is int originalId)
            source = await db.ReconRuns.SingleAsync(r => r.ReconRunId == originalId, ct);
        var containerId = await db.ReconPeriodRuns.Where(c => c.RolledUpRunId == source.ReconRunId)
            .Select(c => (int?)c.ReconPeriodRunId).SingleOrDefaultAsync(ct);
        if (containerId is int id)
        {
            var children = await db.ReconRuns.Where(r => r.ParentPeriodRunId == id && r.AnalysisOfRunId == null
                && r.Status == ReconRunStatus.Completed).Select(r => new { r.ProcessorBatchId, r.GlBatchId }).ToListAsync(ct);
            if (children.Count > 0 && children.All(r => r.GlBatchId is not null))
            {
                return new(source.ReconRunId, children.Select(r => r.ProcessorBatchId).Distinct().ToArray(),
                    children.Select(r => r.GlBatchId!.Value).Distinct().ToArray(), source.PeriodGranularity, true);
            }
        }
        if (source.GlBatchId is int glId)
            return new(source.ReconRunId, [source.ProcessorBatchId], [glId], source.PeriodGranularity, false);
        return new(source.ReconRunId, [source.ProcessorBatchId], [], source.PeriodGranularity, false);
    }
}
