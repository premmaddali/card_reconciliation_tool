using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Staging;
using Recon.Web.Services;
using Recon.Web.Services.Import;
using Recon.Web.Services.Recon;
using Xunit;
using Xunit.Abstractions;

namespace Recon.Web.Tests;

/// <summary>
/// Full engine run against the real March sample: import GL + POS + ATM, then reconcile each
/// processor file to the GL using the weighted matcher. Uses one throwaway SQL Server database.
/// </summary>
public class ReconEngineTests(ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly string Shared =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SharedFiles", "SharedFiles (5)"));
    private static readonly string SharedRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SharedFiles"));

    private readonly string _dbName = "Recon_Eng_" + Guid.NewGuid().ToString("N")[..8];
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        var conn = $"Server=localhost;Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True";
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService, SysUser>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContextFactory<AppDbContext>((sp, o) =>
        {
            o.UseSqlServer(conn, s => s.CommandTimeout(600));
            o.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
            o.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IAtmNetworkValidationService, AtmNetworkValidationService>();
        services.AddScoped<IReconEngine, ReconEngine>();
        services.AddSingleton<IOptions<MatchWeights>>(Options.Create(new MatchWeights()));
        _sp = services.BuildServiceProvider();

        var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedGlAccountsAsync(db);
    }

    public async Task DisposeAsync()
    {
        var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        await _sp.DisposeAsync();
    }

    private IDbContextFactory<AppDbContext> Factory => _sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

    private async Task<int> Import(string path)
    {
        var svc = _sp.GetRequiredService<IImportService>();
        // Allow reading even when the sample is open in Excel (which holds a share lock).
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var outcome = await svc.ImportAsync(fs, Path.GetFileName(path), "2026-03");
        Assert.True(outcome.Success, outcome.Error);
        return outcome.ImportBatchId!.Value;
    }

    [Fact]
    public async Task Foreign_transaction_matches_GL_using_billing_amount_with_MUR_posting_currency()
    {
        await using var db = await Factory.CreateDbContextAsync();
        var processorBatch = new ImportBatch
        {
            SourceType = SourceType.PosSettlement, FileName = "pos.xlsx", ContentHash = Guid.NewGuid().ToString("N"),
            PeriodKey = "2026-03", Status = ImportStatus.Parsed
        };
        var glBatch = new ImportBatch
        {
            SourceType = SourceType.GlDump, FileName = "gl.xlsx", ContentHash = Guid.NewGuid().ToString("N"),
            PeriodKey = "2026-03", Status = ImportStatus.Parsed
        };
        db.ImportBatches.AddRange(processorBatch, glBatch);
        await db.SaveChangesAsync();

        var date = new DateOnly(2026, 3, 10);
        db.StgDebitSettlements.Add(new StgDebitSettlement
        {
            ImportBatchId = processorBatch.ImportBatchId, Channel = SettlementChannel.Pos,
            ReferenceNumber = "FXREF0000001", TransactionDate = date, TxnCcy = 826, TxnAmount = 125m,
            SettlementCcy = 840, SettlementAmount = 160m, BillingCcy = 840, BillingAmount = 160m
        });
        db.StgGlEntries.Add(new StgGlEntry
        {
            ImportBatchId = glBatch.ImportBatchId, AcctNum = "9849065773", TranDate = date,
            TranAmt = 160m, TranParticular = "POS/FX", TranParticular2 = "FXREF0000001"
        });
        await db.SaveChangesAsync();

        var result = await _sp.GetRequiredService<IReconEngine>()
            .RunAsync(ReconFunction.PosToGl, processorBatch.ImportBatchId, glBatch.ImportBatchId, "2026-03");

        Assert.Equal(1, result.MatchedCount);
        var match = await db.ReconMatches.SingleAsync(x => x.ReconRunId == result.ReconRunId);
        Assert.Equal(125m, match.ProcessorAmount);              // original GBP transaction value
        Assert.Equal(160m, match.ReconciliationAmount);         // billing value matched to the GL
        Assert.Equal(480, match.Ccy);                           // the GL amount is posted as MUR
        Assert.Equal(100m, match.Confidence);
        Assert.Contains("amount agrees", match.ScoreBreakdown);
    }

    [Fact]
    public async Task Pos_and_Atm_reconcile_to_GL_with_sensible_confidence()
    {
        var glBatch = await Import(Path.Combine(SharedRoot, "GL_Dump.xlsx"));
        var posBatch = await Import(Path.Combine(Shared, "2. POS MARCH 2026 DEBIT_SETTELMENT_REPORT.xlsx"));
        var atmBatch = await Import(Path.Combine(Shared, "1-ATM MARCH 2026 DEBIT_SETTELMENT_REPORT.xlsx"));

        var engine = _sp.GetRequiredService<IReconEngine>();

        var pos = await engine.RunAsync(ReconFunction.PosToGl, posBatch, glBatch, "2026-03");
        output.WriteLine($"POS: processor={pos.ProcessorCount} matched={pos.MatchedCount} " +
                         $"exceptions={pos.ExceptionCount} avgConf={pos.AverageConfidence}");

        var atm = await engine.RunAsync(ReconFunction.AtmToGl, atmBatch, glBatch, "2026-03");
        output.WriteLine($"ATM: processor={atm.ProcessorCount} matched={atm.MatchedCount} " +
                         $"exceptions={atm.ExceptionCount} avgConf={atm.AverageConfidence}");

        // POS: most rows should match the GL POS/ cardholder entries with high confidence.
        Assert.Equal(71641, pos.ProcessorCount);
        Assert.True(pos.MatchedCount > 40000, $"POS matched too low: {pos.MatchedCount}");
        Assert.True(pos.AverageConfidence >= 70, $"POS avg confidence low: {pos.AverageConfidence}");

        // ATM: only ~6k ATM transactions exist in the GL cardholder accounts, so most are unmatched —
        // that IS the reconciliation finding. Amount+date should still recover a few thousand.
        Assert.Equal(22987, atm.ProcessorCount);
        Assert.True(atm.MatchedCount is > 3000 and < 12000, $"ATM matched out of range: {atm.MatchedCount}");
        Assert.True(atm.ExceptionCount > atm.MatchedCount, "ATM should have more exceptions than matches");

        // Both grains of summary must exist.
        var db = await Factory.CreateDbContextAsync();
        var monthly = await db.ReconPassSummaries.CountAsync(s => s.ReconRunId == pos.ReconRunId && s.Day == null);
        var daily = await db.ReconPassSummaries.CountAsync(s => s.ReconRunId == pos.ReconRunId && s.Day != null);
        Assert.True(monthly >= 1 && daily >= 1, $"summaries missing: monthly={monthly} daily={daily}");
    }

    private sealed class SysUser : ICurrentUserService
    {
        public Guid UserId => CurrentUserService.SystemUserId;
    }
}
