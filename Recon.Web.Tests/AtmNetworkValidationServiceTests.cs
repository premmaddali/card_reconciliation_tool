using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Staging;
using Recon.Web.Services;
using Recon.Web.Services.Recon;
using Xunit;

namespace Recon.Web.Tests;

public class AtmNetworkValidationServiceTests : IAsyncLifetime
{
    private readonly string _databaseName = "Recon_NetworkValidation_" + Guid.NewGuid().ToString("N")[..8];
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService, SystemUser>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContextFactory<AppDbContext>((provider, options) =>
        {
            options.UseSqlServer($"Server=localhost;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True");
            options.AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>());
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddScoped<IAtmNetworkValidationService, AtmNetworkValidationService>();
        services.AddScoped<INetworkAtmReconService, NetworkAtmReconService>();
        _services = services.BuildServiceProvider();
        await using var context = await Factory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = await Factory.CreateDbContextAsync();
        await context.Database.EnsureDeletedAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task Validates_unique_references_and_preserves_one_sided_and_duplicate_exceptions()
    {
        await using var context = await Factory.CreateDbContextAsync();
        var networkBatch = new ImportBatch { SourceType = SourceType.NetworkT464, FileName = "t464.xlsx", ContentHash = Guid.NewGuid().ToString("N"), PeriodKey = "2026-03", Status = ImportStatus.Parsed };
        var atmBatch = new ImportBatch { SourceType = SourceType.AtmSettlement, FileName = "atm.xlsx", ContentHash = Guid.NewGuid().ToString("N"), PeriodKey = "2026-03", Status = ImportStatus.Parsed };
        context.ImportBatches.AddRange(networkBatch, atmBatch);
        await context.SaveChangesAsync();
        var run = new ReconRun { Function = ReconFunction.AtmToGl, PeriodKey = "2026-03", Status = ReconRunStatus.Completed, ProcessorBatchId = atmBatch.ImportBatchId, GlBatchId = atmBatch.ImportBatchId };
        context.ReconRuns.Add(run);
        context.StgNetworkT464.AddRange(
            Network(networkBatch.ImportBatchId, "MATCH", 100m, 480, 1m),
            Network(networkBatch.ImportBatchId, "AMOUNT", 200m, 480, 1m),
            Network(networkBatch.ImportBatchId, "FEE", 300m, 480, 1m),
            Network(networkBatch.ImportBatchId, "NETWORKONLY", 400m, 480, 1m),
            Network(networkBatch.ImportBatchId, "DUP", 500m, 480, 1m),
            Network(networkBatch.ImportBatchId, "DUP", 500m, 480, 1m));
        context.StgDebitSettlements.AddRange(
            Atm(atmBatch.ImportBatchId, "MATCH", -100m, 480, -1m),
            Atm(atmBatch.ImportBatchId, "AMOUNT", -201m, 480, -1m),
            Atm(atmBatch.ImportBatchId, "FEE", -300m, 480, -2m),
            Atm(atmBatch.ImportBatchId, "ATMONLY", -600m, 480, -1m),
            Atm(atmBatch.ImportBatchId, "DUP", -500m, 480, -1m));
        await context.SaveChangesAsync();

        await _services.GetRequiredService<IAtmNetworkValidationService>().ValidateAsync(run.ReconRunId, networkBatch.ImportBatchId, atmBatch.ImportBatchId);

        var outcomes = await context.AtmNetworkValidations.ToDictionaryAsync(item => item.ReferenceNumber!);
        Assert.Equal(AtmNetworkValidationStatus.Matched, outcomes["MATCH"].Status);
        Assert.Equal(AtmNetworkValidationStatus.AmountMismatch, outcomes["AMOUNT"].Status);
        Assert.Equal(AtmNetworkValidationStatus.FeeMismatch, outcomes["FEE"].Status);
        Assert.Equal(AtmNetworkValidationStatus.MissingAtm, outcomes["NETWORKONLY"].Status);
        Assert.Equal(AtmNetworkValidationStatus.MissingNetwork, outcomes["ATMONLY"].Status);
        Assert.Equal(AtmNetworkValidationStatus.Duplicate, outcomes["DUP"].Status);
        Assert.Equal(2, outcomes["DUP"].NetworkReferenceCount);
    }

    [Fact]
    public async Task Network_run_completes_with_headline_counts_and_no_gl_batch()
    {
        await using var context = await Factory.CreateDbContextAsync();
        var networkBatch = new ImportBatch { SourceType = SourceType.NetworkT464, FileName = "t464.xlsx", ContentHash = Guid.NewGuid().ToString("N"), PeriodKey = "2026-03", Status = ImportStatus.Parsed };
        var atmBatch = new ImportBatch { SourceType = SourceType.AtmSettlement, FileName = "atm.xlsx", ContentHash = Guid.NewGuid().ToString("N"), PeriodKey = "2026-03", Status = ImportStatus.Parsed };
        context.ImportBatches.AddRange(networkBatch, atmBatch);
        await context.SaveChangesAsync();
        context.StgNetworkT464.AddRange(
            Network(networkBatch.ImportBatchId, "MATCH", 100m, 480, 1m),
            Network(networkBatch.ImportBatchId, "ONLY", 200m, 480, 1m));
        context.StgDebitSettlements.Add(Atm(atmBatch.ImportBatchId, "MATCH", -100m, 480, -1m));
        await context.SaveChangesAsync();

        var result = await _services.GetRequiredService<INetworkAtmReconService>()
            .RunAsync(networkBatch.ImportBatchId, atmBatch.ImportBatchId, "2026-03");

        var run = await context.ReconRuns.SingleAsync(item => item.ReconRunId == result.ReconRunId);
        Assert.Equal(ReconFunction.NetworkToAtm, run.Function);
        Assert.Equal(ReconRunStatus.Completed, run.Status);
        Assert.Null(run.GlBatchId);
        Assert.Equal(networkBatch.ImportBatchId, run.NetworkBatchId);
        Assert.Equal(1, run.ProcessorCount);
        Assert.Equal(1, run.MatchedCount);
        Assert.Equal(1, run.ExceptionCount);
    }

    private IDbContextFactory<AppDbContext> Factory => _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    private static StgNetworkT464 Network(int batchId, string reference, decimal amount, int ccy, decimal fee) => new() { ImportBatchId = batchId, ReferenceNumber = reference, CompletedAmtLocal = amount, TxnCcy = ccy, InterchangeFee = fee };
    private static StgDebitSettlement Atm(int batchId, string reference, decimal amount, int ccy, decimal fee) => new() { ImportBatchId = batchId, Channel = SettlementChannel.Atm, ReferenceNumber = reference, TxnAmount = amount, TxnCcy = ccy, InterchangeFees = fee };

    private sealed class SystemUser : ICurrentUserService
    {
        public Guid UserId => CurrentUserService.SystemUserId;
    }
}