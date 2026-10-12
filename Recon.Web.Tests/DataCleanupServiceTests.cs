using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Recon.Web.Data;
using Recon.Web.Domain.Identity;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Staging;
using Recon.Web.Platform.Model;
using Recon.Web.Services;
using Xunit;

namespace Recon.Web.Tests;

public class DataCleanupServiceTests : IAsyncLifetime
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly TestAuthentication _authentication = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
    private ServiceProvider _services = null!;
    private IDbContextFactory<AppDbContext> Factory => _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    private DataCleanupService Service => new(Factory, _authentication, _configuration, NullLogger<DataCleanupService>.Instance);

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer($"Server=localhost;Database=Recon_Cleanup_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True"));
        services.AddSingleton(_configuration);
        _services = services.BuildServiceProvider();
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.UserAppAccesses.Add(new UserAppAccess { User = new AppUser { Id = _userId, UserName = "cleanup-admin" }, AppAccess = new AppAccess { AccessName = "Administrator" } });
        db.ReconParameters.Add(new ReconParameters { AmountTolerance = 2.5m });
        db.SchemeProfiles.Add(new SchemeProfile { Name = "Test", Scheme = "Visa", Cms = "Placeholder" });
        db.ReconWorkflowDefinitions.Add(new ReconWorkflowDefinition { Name = "Keep workflow" });
        db.AppPages.Add(new AppPage { PageCode = "KEEP", PageName = "Keep" });
        await db.SaveChangesAsync();
        await DbSeeder.SeedGlAccountsAsync(db);
        await DbSeeder.SeedCurrenciesAsync(db);
        _authentication.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) }, "test"));

        // A row in every transactional table, with real FK dependencies.
        db.ImportBatches.Add(new ImportBatch { ContentHash = "test", Status = ImportStatus.Parsed, PeriodKey = "2025-12" });
        await db.SaveChangesAsync();
        db.ReconRuns.Add(new ReconRun { ProcessorBatchId = 1, Status = ReconRunStatus.Completed, PeriodKey = "2025-12" });
        db.BatchArchives.Add(new BatchArchive { PeriodKey = "2025-12" });
        await db.SaveChangesAsync();
        db.ReconExceptions.Add(new ReconException { ReconRunId = 1 });
        await db.SaveChangesAsync();
        db.AddRange(new ReconExceptionComment { ReconExceptionId = 1 }, new ReconMatch { ReconRunId = 1 },
            new ReconPassSummary { ReconRunId = 1 }, new AtmNetworkValidation { ReconRunId = 1 },
            new ReconWorkflowActor { ReconRunId = 1 }, new ReconWorkflowHistory { ReconRunId = 1 },
            new BatchArchiveTransaction { BatchArchiveId = 1 }, new BatchFreeze { PeriodKey = "2025-12" },
            new NormalizedTransaction { ImportBatchId = 1 }, new NormalizedGlEntry { ImportBatchId = 1 },
            new StgNetworkT464 { ImportBatchId = 1 }, new StgDebitSettlement { ImportBatchId = 1 },
            new StgInterchangeFee { ImportBatchId = 1 }, new StgGlEntry { ImportBatchId = 1 }, new StgGlBulkPosting { ImportBatchId = 1 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cutoff_preserves_older_gl_batch_used_by_retained_cross_period_match()
    {
        await using var db = await Factory.CreateDbContextAsync();
        var batch = new ImportBatch { ContentHash = "later", PeriodKey = "2026-01", Status = ImportStatus.Parsed };
        db.Add(batch); await db.SaveChangesAsync();
        var run = new ReconRun { ProcessorBatchId = batch.ImportBatchId, GlBatchId = batch.ImportBatchId, PeriodKey = "2026-01", Status = ReconRunStatus.Completed };
        db.Add(run); await db.SaveChangesAsync();
        db.ReconMatches.Add(new ReconMatch { ReconRunId = run.ReconRunId, GlSourceBatchId = 1, GlStagingId = 1, MatchedAcrossPeriods = true });
        await db.SaveChangesAsync();
        await Service.ClearAsync(DataCleanupService.ConfirmationFor("2026-01"), "2026-01");
        db.ChangeTracker.Clear();
        Assert.NotNull(await db.ImportBatches.FindAsync(1)); Assert.NotNull(await db.StgGlEntries.FindAsync(1));
        Assert.NotNull(await db.ReconRuns.FindAsync(run.ReconRunId)); Assert.Null(await db.ReconRuns.FindAsync(1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-01")]
    public async Task Clears_every_transaction_table_preserves_reference_data_and_stays_empty_after_seed(string? cutoff)
    {
        await Service.ClearAsync(DataCleanupService.ConfirmationFor(cutoff), cutoff);
        await Service.ClearAsync(DataCleanupService.ConfirmationFor(cutoff), cutoff); // Empty database is safe to clear again.
        await DemoDataSeeder.SeedAsync(_services);
        await using var db = await Factory.CreateDbContextAsync();
        foreach (var table in new[] { "ImportBatches", "ReconRuns", "ReconExceptionComments", "ReconExceptions", "ReconMatches", "ReconPassSummaries", "AtmNetworkValidations", "ReconWorkflowActors", "ReconWorkflowHistories", "BatchArchiveTransactions", "BatchArchives", "BatchFreezes", "NormalizedTransactions", "NormalizedGlEntries", "StgNetworkT464", "StgDebitSettlements", "StgInterchangeFees", "StgGlEntries", "StgGlBulkPostings" })
        {
            // Names are a fixed test allowlist, not user input.
#pragma warning disable EF1002
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM [{table}]").SingleAsync());
#pragma warning restore EF1002
        }
        Assert.Equal(6, await db.GlSettlementAccounts.CountAsync());
        Assert.True(await db.IsoCurrencies.AnyAsync());
        Assert.Equal(2.5m, (await db.ReconParameters.SingleAsync()).AmountTolerance);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.UserAppAccesses.ToListAsync());
        Assert.Single(await db.AppAccesses.ToListAsync());
        Assert.Single(await db.AppPages.ToListAsync());
        Assert.Single(await db.SchemeProfiles.ToListAsync());
        Assert.Single(await db.ReconWorkflowDefinitions.ToListAsync());
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("officer")]
    [InlineData("inactive")]
    [InlineData("revoked")]
    public async Task Rejects_non_administrators_even_with_direct_service_calls(string mode)
    {
        await using var db = await Factory.CreateDbContextAsync();
        if (mode == "anonymous") _authentication.User = new ClaimsPrincipal(new ClaimsIdentity());
        if (mode == "officer") (await db.AppAccesses.SingleAsync()).AccessName = "Recon Officer";
        if (mode == "inactive") (await db.Users.SingleAsync()).IsActive = false;
        if (mode == "revoked") (await db.UserAppAccesses.SingleAsync()).IsDeleted = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.ClearAsync(DataCleanupService.Confirmation));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.PreviewAsync());
        Assert.Single(await db.ImportBatches.ToListAsync());
    }

    [Theory]
    [InlineData("confirmation")]
    [InlineData("import")]
    [InlineData("run")]
    [InlineData("demo")]
    public async Task Failed_preconditions_leave_data_untouched(string mode)
    {
        await using var db = await Factory.CreateDbContextAsync();
        if (mode == "import") (await db.ImportBatches.SingleAsync()).Status = ImportStatus.Uploaded;
        if (mode == "run") (await db.ReconRuns.SingleAsync()).Status = ReconRunStatus.Running;
        if (mode == "demo") _configuration["DemoData:Enabled"] = "true";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ClearAsync(mode == "confirmation" ? "wrong" : DataCleanupService.Confirmation));
        Assert.Single(await db.ImportBatches.ToListAsync());
        Assert.Single(await db.ReconExceptionComments.ToListAsync());
    }

    [Fact]
    public async Task Database_failure_rolls_back_earlier_deletes()
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailCleanup ON [StgGlEntries] AFTER DELETE AS THROW 50001, 'Test cleanup rollback', 1;");
        await Assert.ThrowsAnyAsync<Exception>(() => Service.ClearAsync(DataCleanupService.Confirmation));
        Assert.Single(await db.ReconExceptionComments.ToListAsync());
        Assert.Single(await db.ReconRuns.ToListAsync());
        Assert.Single(await db.ImportBatches.ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        await _services.DisposeAsync();
    }

    [Theory]
    [InlineData("2026-01", false)]
    [InlineData("2026-02", false)]
    [InlineData("", false)]
    [InlineData("2026-13", false)]
    [InlineData("2025-12", true)]
    public async Task Filter_preserves_cutoff_later_unknown_and_overlapping_periods(string period, bool overlaps)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var batch = new ImportBatch { ContentHash = "keep", PeriodKey = overlaps ? "2026-01" : period, Status = ImportStatus.Parsed };
        db.Add(batch);
        await db.SaveChangesAsync();
        var run = new ReconRun { ProcessorBatchId = batch.ImportBatchId, PeriodKey = period, Status = ReconRunStatus.Completed, PeriodEnd = overlaps ? new DateOnly(2026, 1, 2) : null };
        var archive = new BatchArchive { OriginalReconRunId = 99, PeriodKey = period, PeriodEnd = run.PeriodEnd };
        var freeze = new BatchFreeze { Function = ReconFunction.PosToGl, PeriodKey = period, PeriodEnd = run.PeriodEnd };
        db.AddRange(run, archive, freeze);
        await db.SaveChangesAsync();
        var exception = new ReconException { ReconRunId = run.ReconRunId };
        db.Add(exception);
        await db.SaveChangesAsync();
        db.AddRange(new ReconExceptionComment { ReconExceptionId = exception.ReconExceptionId },
            new ReconMatch { ReconRunId = run.ReconRunId }, new ReconPassSummary { ReconRunId = run.ReconRunId },
            new AtmNetworkValidation { ReconRunId = run.ReconRunId }, new ReconWorkflowActor { ReconRunId = run.ReconRunId },
            new ReconWorkflowHistory { ReconRunId = run.ReconRunId }, new BatchArchiveTransaction { BatchArchiveId = archive.BatchArchiveId },
            new StgGlEntry { ImportBatchId = batch.ImportBatchId }, new NormalizedTransaction { ImportBatchId = batch.ImportBatchId });
        await db.SaveChangesAsync();
        Assert.Equal(new CleanupPreview(1, 1, 1), await Service.PreviewAsync("2026-01"));
        await Service.ClearAsync("DELETE BEFORE 2026-01", "2026-01");
        Assert.Equal(batch.ImportBatchId, (await db.ImportBatches.AsNoTracking().SingleAsync()).ImportBatchId);
        Assert.Equal(run.ReconRunId, (await db.ReconRuns.AsNoTracking().SingleAsync()).ReconRunId);
        Assert.Equal(archive.BatchArchiveId, (await db.BatchArchives.AsNoTracking().SingleAsync()).BatchArchiveId);
        Assert.Equal(freeze.BatchFreezeId, (await db.BatchFreezes.AsNoTracking().SingleAsync()).BatchFreezeId);
        Assert.Single(await db.ReconExceptionComments.AsNoTracking().ToListAsync());
        Assert.Single(await db.ReconMatches.AsNoTracking().ToListAsync());
        Assert.Single(await db.ReconPassSummaries.AsNoTracking().ToListAsync());
        Assert.Single(await db.AtmNetworkValidations.AsNoTracking().ToListAsync());
        Assert.Single(await db.ReconWorkflowActors.AsNoTracking().ToListAsync());
        Assert.Single(await db.ReconWorkflowHistories.AsNoTracking().ToListAsync());
        Assert.Single(await db.BatchArchiveTransactions.AsNoTracking().ToListAsync());
        Assert.Single(await db.StgGlEntries.AsNoTracking().ToListAsync());
        Assert.Single(await db.NormalizedTransactions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Shared_older_batch_and_its_transactions_are_preserved_for_newer_runs()
    {
        await using var db = await Factory.CreateDbContextAsync();
        db.ReconRuns.Add(new ReconRun { ProcessorBatchId = 1, GlBatchId = 1, NetworkBatchId = 1, PeriodKey = "2026-01", Status = ReconRunStatus.Completed });
        await db.SaveChangesAsync();
        Assert.Equal(new CleanupPreview(0, 1, 1, 1), await Service.PreviewAsync("2026-01"));
        await Service.ClearAsync("DELETE BEFORE 2026-01", "2026-01");
        Assert.Single(await db.ImportBatches.ToListAsync());
        Assert.Single(await db.StgGlEntries.ToListAsync());
        Assert.Single(await db.NormalizedTransactions.ToListAsync());
        Assert.Equal("2026-01", (await db.ReconRuns.AsNoTracking().SingleAsync()).PeriodKey);
        Assert.Empty(await db.ReconMatches.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("0000-01")]
    public async Task Invalid_month_never_falls_back_to_clearing_all(string cutoff)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.PreviewAsync(cutoff));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ClearAsync(DataCleanupService.Confirmation, cutoff));
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Single(await db.ImportBatches.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_must_match_selected_month()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ClearAsync("DELETE BEFORE 2026-01", "2026-02"));
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Single(await db.ImportBatches.ToListAsync());
    }
    private sealed class TestAuthentication : AuthenticationStateProvider
    {
        public ClaimsPrincipal User { get; set; } = new(new ClaimsIdentity());
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
    }
}

