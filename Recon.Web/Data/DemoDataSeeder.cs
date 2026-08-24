using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Platform.Model;
using Recon.Web.Services;

namespace Recon.Web.Data;

/// <summary>
/// Seeds demonstration reconciliation runs for the placeholder schemes (Visa, UnionPay, RuPay,
/// Domestic). Each scheme gets its own ImportBatches and ReconRuns tagged with its
/// SchemeProfileId, so every scheme's screens show realistic data without touching real
/// Mastercard runs. Idempotent: a scheme with existing runs is left alone.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var _ = db; // keep alive for the whole seed

        var profiles = await db.SchemeProfiles.Where(p => p.Cms == "Placeholder" && p.IsActive).ToListAsync();
        if (profiles.Count == 0) return;

        foreach (var profile in profiles)
        {
            if (await db.ReconRuns.AnyAsync(r => r.SchemeProfileId == profile.SchemeProfileId)) continue;
            SeedScheme(db, profile);
            // Batches must be persisted before runs so their identity IDs exist for the FKs.
            await db.SaveChangesAsync();
        }
        await db.SaveChangesAsync();
    }

    private static void SeedScheme(AppDbContext db, SchemeProfile p)
    {
        var now = DateTime.UtcNow;
        var createdBy = CurrentUserService.SystemUserId;
        var rnd = new Random(p.SchemeProfileId * 7919);

        // One GL dump shared by all three functions of this scheme.
        var gl = NewBatch(db, p, SourceType.GlDump, $"{p.Scheme}_GL_DUMP_MAR.xlsx", "2026-03", 118_400, now, createdBy);
        var atm = NewBatch(db, p, SourceType.AtmSettlement, $"{p.Scheme.ToUpper()}_ATM_SETTLEMENT_MAR.xlsx", "2026-03", 21_540, now, createdBy);
        var pos = NewBatch(db, p, SourceType.PosSettlement, $"{p.Scheme.ToUpper()}_POS_SETTLEMENT_MAR.xlsx", "2026-03", 68_912, now, createdBy);
        var bulk = NewBatch(db, p, SourceType.InterchangeFee, $"{p.Scheme.ToUpper()}_INTERCHANGE_MAR.xlsx", "2026-03", 102_335, now, createdBy);

        // Persist batches first so their identity IDs exist before the runs reference them.
        db.SaveChanges();

        db.ReconRuns.AddRange(
            Run(p, ReconFunction.AtmToGl, atm, gl, "2026-03", new(2026, 3, 1), new(2026, 3, 31), 21_540, Pct(rnd, 0.24m, 0.32m), now, createdBy),
            Run(p, ReconFunction.PosToGl, pos, gl, "2026-03", new(2026, 3, 1), new(2026, 3, 31), 68_912, Pct(rnd, 0.72m, 0.82m), now, createdBy),
            Run(p, ReconFunction.InterchangeToGl, bulk, gl, "2026-03", new(2026, 3, 1), new(2026, 3, 31), 102_335, Pct(rnd, 0.78m, 0.86m), now, createdBy));
    }

    private static decimal Pct(Random rnd, decimal lo, decimal hi) =>
        Math.Round(lo + (decimal)rnd.NextDouble() * (hi - lo), 4);

    private static ImportBatch NewBatch(AppDbContext db, SchemeProfile p, SourceType type, string name,
        string periodKey, int rows, DateTime now, Guid createdBy)
    {
        var batch = new ImportBatch
        {
            SourceType = type,
            FileName = name,
            // Synthetic hash namespaced per scheme so it can never collide with a real upload.
            ContentHash = $"demo-{p.Scheme.ToLowerInvariant()}-{type.ToString().ToLowerInvariant()}",
            PeriodKey = periodKey,
            RowCount = rows,
            Status = ImportStatus.Parsed,
            SchemeProfileId = p.SchemeProfileId,
            CreatedBy = createdBy,
            CreatedDate = now
        };
        db.ImportBatches.Add(batch);
        return batch;
    }

    private static ReconRun Run(SchemeProfile p, ReconFunction fn, ImportBatch proc, ImportBatch gl,
        string periodKey, DateOnly from, DateOnly to, int processorCount, decimal matchRate,
        DateTime now, Guid createdBy)
    {
        var matched = (int)Math.Round(processorCount * matchRate);
        return new ReconRun
        {
            Function = fn,
            PeriodKey = periodKey,
            PeriodStart = from,
            PeriodEnd = to,
            Status = ReconRunStatus.Completed,
            ProcessorBatchId = proc.ImportBatchId,
            GlBatchId = gl.ImportBatchId,
            SchemeProfileId = p.SchemeProfileId,
            StartedDate = now.AddMinutes(-14),
            CompletedDate = now,
            ProcessorCount = processorCount,
            MatchedCount = matched,
            ExceptionCount = processorCount - matched,
            AverageConfidence = Math.Round(84m + (decimal)new Random(fn.GetHashCode() + p.SchemeProfileId).NextDouble() * 10m, 2),
            VerificationStatus = ReconVerificationStatus.NotSubmitted,
            CreatedBy = createdBy,
            CreatedDate = now
        };
    }
}
