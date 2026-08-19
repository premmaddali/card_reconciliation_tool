using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Services;
using Recon.Web.Services.Import;
using Recon.Web.Services.Recon;
using Xunit;
using Xunit.Abstractions;

namespace Recon.Web.Tests;

/// <summary>
/// One-shot utility (not a unit test): loads the real March files into the DEV `Recon` database and
/// runs the three reconciliations, so the running app shows live data. Safe to re-run — imports are
/// deduped by content hash and recons only run when that function has no completed run yet.
/// Run explicitly: dotnet test --filter FullyQualifiedName~SeedDevDatabase
/// </summary>
public class SeedDevDatabase(ITestOutputHelper output)
{
    private static readonly string Shared =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SharedFiles", "SharedFiles (5)"));
    private static readonly string SharedRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SharedFiles"));

    [Fact]
    public async Task Seed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService, Sys>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContextFactory<AppDbContext>((sp, o) =>
        {
            o.UseSqlServer("Server=localhost;Database=Recon;Trusted_Connection=True;TrustServerCertificate=True",
                s => s.CommandTimeout(600));
            o.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
            o.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IReconEngine, ReconEngine>();
        services.AddSingleton<IOptions<MatchWeights>>(Options.Create(new MatchWeights()));
        await using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
            await DbSeeder.SeedGlAccountsAsync(db);

        var importer = sp.GetRequiredService<IImportService>();

        async Task Import(string path)
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var r = await importer.ImportAsync(fs, Path.GetFileName(path), "2026-03");
            output.WriteLine($"import {Path.GetFileName(path)}: success={r.Success} type={r.DetectedType} rows={r.RowCount} dup={r.Duplicate} {r.Error}");
        }

        await Import(Path.Combine(SharedRoot, "GL_Dump.xlsx"));
        await Import(Path.Combine(Shared, "1-ATM MARCH 2026 DEBIT_SETTELMENT_REPORT.xlsx"));
        await Import(Path.Combine(Shared, "2. POS MARCH 2026 DEBIT_SETTELMENT_REPORT.xlsx"));
        await Import(Path.Combine(Shared, "3-INTERCHANGE_FEE_REPORT_CREDIT_AND_DEBIT (1).xlsx"));

        int BatchId(AppDbContext db, SourceType t) => db.ImportBatches
            .Where(b => b.SourceType == t && b.Status == ImportStatus.Parsed)
            .OrderByDescending(b => b.ImportBatchId).Select(b => b.ImportBatchId).First();

        var engine = sp.GetRequiredService<IReconEngine>();
        await using var db2 = await factory.CreateDbContextAsync();
        var gl = BatchId(db2, SourceType.GlDump);

        await RunIfMissing(db2, engine, ReconFunction.AtmToGl, BatchId(db2, SourceType.AtmSettlement), gl, output);
        await RunIfMissing(db2, engine, ReconFunction.PosToGl, BatchId(db2, SourceType.PosSettlement), gl, output);
        await RunIfMissing(db2, engine, ReconFunction.InterchangeToGl, BatchId(db2, SourceType.InterchangeFee), gl, output);
    }

    private static async Task RunIfMissing(AppDbContext db, IReconEngine engine, ReconFunction fn,
        int processorBatch, int glBatch, ITestOutputHelper output)
    {
        if (await db.ReconRuns.AnyAsync(r => r.Function == fn && r.Status == ReconRunStatus.Completed))
        {
            output.WriteLine($"{fn}: already run, skipping");
            return;
        }
        var r = await engine.RunAsync(fn, processorBatch, glBatch, "2026-03");
        output.WriteLine($"{fn}: run #{r.ReconRunId} processor={r.ProcessorCount} matched={r.MatchedCount} exceptions={r.ExceptionCount} avgConf={r.AverageConfidence}");
    }

    private sealed class Sys : ICurrentUserService
    {
        public Guid UserId => CurrentUserService.SystemUserId;
    }
}
