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
/// One-shot loader (not a unit test): imports the March/April/May 2023 interchange + GL files into the
/// live DEV `Recon` database and runs a primary Bulk reconciliation for each month, so the running app
/// has three adjacent batches to exercise Previous/Next matching. These files carry no TMS card field,
/// which is fine — the import maps columns by header and never requires card data.
/// Run: dotnet test --filter FullyQualifiedName~LoadQ2_2023
/// </summary>
public class LoadQ2_2023(ITestOutputHelper output)
{
    private static readonly string Dir = Path.Combine(SampleFiles.Root, "SharedFiles (5)");

    [Fact]
    public async Task Load()
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
        var engine = sp.GetRequiredService<IReconEngine>();

        async Task<int> Import(string name, string period)
        {
            await using var fs = new FileStream(Path.Combine(Dir, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var r = await importer.ImportAsync(fs, name, period);
            output.WriteLine($"import {name} [{period}]: success={r.Success} type={r.DetectedType} rows={r.RowCount} dup={r.Duplicate} {r.Error}");
            if (!r.Success || r.ImportBatchId is null) throw new InvalidOperationException($"Import failed for {name}: {r.Error}");
            return r.ImportBatchId.Value;
        }

        var months = new (string Trx, string Gl, string Period, DateOnly Start, DateOnly End)[]
        {
            ("March 2023.xlsx", "March 2023 gl.xlsx", "2023-03", new(2023, 3, 1), new(2023, 3, 31)),
            ("April 2023.xlsx", "April 2023 GL.xlsx", "2023-04", new(2023, 4, 1), new(2023, 4, 30)),
            ("may 2023.xlsx",   "May 2023 gl.xlsx",   "2023-05", new(2023, 5, 1), new(2023, 5, 31)),
        };

        foreach (var m in months)
        {
            await using var db = await factory.CreateDbContextAsync();
            if (await db.ReconRuns.AnyAsync(r => r.Function == ReconFunction.InterchangeToGl && r.PeriodKey == m.Period
                    && r.SchemeProfileId == null && r.Status == ReconRunStatus.Completed && r.SecondaryOfRunId == null && r.CardOfRunId == null))
            {
                output.WriteLine($"{m.Period}: a Bulk run already exists — skipping.");
                continue;
            }
            var tx = await Import(m.Trx, m.Period);
            var gl = await Import(m.Gl, m.Period);
            var r = await engine.RunAsync(ReconFunction.InterchangeToGl, tx, gl, m.Period, m.Start, m.End);
            output.WriteLine($"{m.Period}: run #{r.ReconRunId} processor={r.ProcessorCount} matched={r.MatchedCount} exceptions={r.ExceptionCount} avgConf={r.AverageConfidence}");
        }
    }

    private sealed class Sys : ICurrentUserService
    {
        public Guid UserId => CurrentUserService.SystemUserId;
    }
}
