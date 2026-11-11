using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recon.Web.Data;
using Recon.Web.Domain.Identity;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Staging;
using Recon.Web.Platform.Adapters;
using Recon.Web.Services;
using Recon.Web.Services.Recon;
using Xunit;

namespace Recon.Web.Tests;

public class ReconAnalysisServiceTests : IAsyncLifetime
{
    private ServiceProvider _sp = null!;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly TestAuth _auth = new();
    private int _trx, _gl, _source;
    private static readonly DateOnly Day = new(2023, 5, 15);
    private IDbContextFactory<AppDbContext> Factory => _sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    private SecondaryMatchService Secondary => new(Factory, _auth, new PermissionService(Factory));
    private ReconAnalysisService Service => new(Factory, new ReconEngine(Factory, Options.Create(new MatchWeights())), Secondary, _auth, new PermissionService(Factory));

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer($"Server=localhost;Database=Recon_Replay_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True"));
        _sp = services.BuildServiceProvider();
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedGlAccountsAsync(db);
        var role = new AppAccess { AccessName = "Operator" };
        db.UserAppAccesses.Add(new UserAppAccess { User = new AppUser { Id = _userId, UserName = "operator" }, AppAccess = role });
        db.AppAccessMenus.Add(new AppAccessMenu { AppAccess = role, Page = new AppPage { PageCode = "BULKRECON", PageName = "Bulk" }, CanView = true, CanAdd = true, CanEdit = true });
        var tx = Batch(SourceType.InterchangeFee); var gl = Batch(SourceType.GlDump);
        db.AddRange(tx, gl); await db.SaveChangesAsync();
        _trx = tx.ImportBatchId; _gl = gl.ImportBatchId;
        var source = new ReconRun { Function = ReconFunction.InterchangeToGl, ProcessorBatchId = _trx, GlBatchId = _gl,
            PeriodKey = "2023-05", Status = ReconRunStatus.Completed };
        db.Add(source); await db.SaveChangesAsync(); _source = source.ReconRunId;
        _auth.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _userId.ToString())], "test"));
    }

    private static ImportBatch Batch(SourceType type, string period = "2023-05") => new()
        { ContentHash = Guid.NewGuid().ToString("N"), SourceType = type, Status = ImportStatus.Parsed, PeriodKey = period };

    private async Task<int> AddTrx(decimal amount, string? card = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var t = new StgInterchangeFee { ImportBatchId = _trx, OrigineCode = "1", ProductType = "DC", TransactionCode = "01",
            TransactionDate = Day.ToDateTime(new TimeOnly(15, 30)), ProcessingDate = Day.AddDays(10),
            TxnCcy = 480, TxnAmount = amount, BillingCcy = 480, BillingAmount = amount, CardAccountEntityId = card };
        db.Add(t); await db.SaveChangesAsync();
        db.ReconMatches.Add(new ReconMatch { ReconRunId = _source, ProcessorStagingId = t.StgInterchangeFeeId,
            ProcessorSource = StagingSource.InterchangeFee, SettlementLeg = ReconSettlementLeg.DebitCardholder,
            MatchStatus = MatchStatus.UnmatchedProcessor, ProcessorDate = t.ProcessingDate,
            ProcessorTransactionDateTime = t.TransactionDate, BillingAmount = amount, BillingCcy = 480, Ccy = 480, ReconciliationAmount = amount });
        db.ReconExceptions.Add(new ReconException { ReconRunId = _source, ProcessorStagingId = t.StgInterchangeFeeId,
            ExceptionType = MatchStatus.UnmatchedProcessor, Narrative = "Original evidence",
            Comments = [new ReconExceptionComment { Body = "Keep this review" }] });
        var source = await db.ReconRuns.SingleAsync(r => r.ReconRunId == _source);
        source.ProcessorCount++; source.ExceptionCount++;
        await db.SaveChangesAsync(); return t.StgInterchangeFeeId;
    }

    private async Task<int> AddGl(decimal amount, DateOnly? valueDate = null, string? card = null, int? batch = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var g = new StgGlEntry { ImportBatchId = batch ?? _gl, AcctNum = "9849065772", TranAmt = amount, TranCcyCode = "MUR",
            TranDate = Day, ValueDate = valueDate ?? Day, TranParticular = "POS/TEST", TranRmks = card };
        db.Add(g); await db.SaveChangesAsync(); return g.StgGlEntryId;
    }

    [Fact]
    public async Task Replay_executes_all_four_stages_and_does_not_reserve_live_GL()
    {
        await AddTrx(100); await AddTrx(200, "4111111111111111"); await AddTrx(200, "5555555555554444"); await AddTrx(300);
        await AddGl(100); await AddGl(200, Day.AddDays(1), "4111111111111111"); await AddGl(200, Day.AddDays(1), "5555555555554444");
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var next = Batch(SourceType.GlDump, "2023-06"); db.Add(next); await db.SaveChangesAsync();
            await AddGl(300, batch: next.ImportBatchId);
            // Existing processed flags must not suppress the new analysis's phases.
            db.ReconRunPasses.Add(new ReconRunPass { ReconRunId = _source, Kind = ReconPassKind.SecondaryAmount, Detail = "Original pass", RunAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var result = await Service.RunAsync(_source);
        Assert.NotEqual(_source, result.RunId); Assert.Equal(4, result.Matched);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var passes = await db.ReconRunPasses.Where(p => p.ReconRunId == result.RunId).ToListAsync();
            Assert.Equal(ReconRunStatus.Completed, (await db.ReconRuns.SingleAsync(r => r.ReconRunId == result.RunId)).Status);
            Assert.Equal(4, passes.Count);
            Assert.Equal(0, passes.Single(p => p.Kind == ReconPassKind.InitialAnalysis).Added);
            Assert.Equal(1, passes.Single(p => p.Kind == ReconPassKind.SecondaryAmount).Added);
            Assert.Equal(2, passes.Single(p => p.Kind == ReconPassKind.CardNumber).Added);
            Assert.Equal(1, passes.Single(p => p.Kind == ReconPassKind.CrossPeriod).Added);
            Assert.Equal(4, await db.ReconMatches.Where(m => m.ReconRunId == result.RunId).Select(m => m.GlStagingId).Distinct().CountAsync());
            Assert.Equal(0, (await db.ReconRuns.SingleAsync(r => r.ReconRunId == _source)).MatchedCount);
            Assert.Equal(4, await db.ReconExceptionComments.CountAsync());
            Assert.Single(await db.ReconRunPasses.Where(p => p.ReconRunId == _source).ToListAsync());
        }
        var again = await Service.RunAsync(_source);
        Assert.Equal(4, again.Matched); Assert.NotEqual(result.RunId, again.RunId);
        // Analysis's adjacent-period assignments must not consume GL for the actual batch.
        Assert.Equal(1, (await Secondary.RunAdjacentAsync(_source)).Added);
    }

    [Fact]
    public async Task Replay_reads_native_credit_evidence_and_preserves_archived_verified_frozen_original()
    {
        await AddTrx(103); var glId = await AddGl(100);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var t = await db.StgInterchangeFees.SingleAsync();
            t.ProductType = "CR"; t.IssMarkupAmount = 3; t.MicrofilmRefNumber = "KEY";
            var g = await db.StgGlEntries.SingleAsync(); g.AcctNum = "9849077911"; g.TranParticular2 = "123456789012KEY";
            var source = await db.ReconRuns.SingleAsync(); source.IsArchived = true; source.VerificationStatus = ReconVerificationStatus.Verified;
            var m = await db.ReconMatches.SingleAsync(); m.SettlementLeg = ReconSettlementLeg.CreditIntermediary;
            m.IsManuallyMatched = true; m.MatchComment = "Original manual decision";
            db.BatchFreezes.Add(new BatchFreeze { Function = source.Function, PeriodKey = source.PeriodKey });
            await db.SaveChangesAsync();
            await NormalizedProjection.ProjectBatchAsync(db, _trx); await NormalizedProjection.ProjectBatchAsync(db, _gl);
        }
        var result = await Service.RunAsync(_source);
        Assert.Equal(1, result.Matched);
        await using var verify = await Factory.CreateDbContextAsync();
        var match = await verify.ReconMatches.SingleAsync(m => m.ReconRunId == result.RunId);
        Assert.Equal(100m, match.ReconciliationAmount); Assert.Equal(103m, match.BillingAmount); Assert.Equal(glId, match.GlStagingId);
        var original = await verify.ReconRuns.SingleAsync(r => r.ReconRunId == _source);
        Assert.True(original.IsArchived); Assert.Equal(ReconVerificationStatus.Verified, original.VerificationStatus);
        Assert.Equal("Original manual decision", (await verify.ReconMatches.SingleAsync(m => m.ReconRunId == _source)).MatchComment);
        Assert.Single(await verify.ReconExceptionComments.ToListAsync());
        Assert.Single(await verify.BatchFreezes.ToListAsync());
        Assert.Equal(4, await verify.ReconRunPasses.CountAsync(p => p.ReconRunId == result.RunId));
        Assert.Contains("Skipped:", (await verify.ReconRunPasses.SingleAsync(p => p.ReconRunId == result.RunId && p.Kind == ReconPassKind.CardNumber)).Detail);
    }

    [Fact]
    public async Task Daily_replay_keeps_its_range_without_adding_a_second_operational_child()
    {
        await AddTrx(100); await AddGl(100);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var container = new ReconPeriodRun { Function = ReconFunction.InterchangeToGl, PeriodKey = "2023-05" };
            db.Add(container); await db.SaveChangesAsync();
            var source = await db.ReconRuns.SingleAsync(); source.ParentPeriodRunId = container.ReconPeriodRunId;
            source.PeriodStart = source.PeriodEnd = Day; source.PeriodGranularity = PeriodGranularity.Day;
            await db.SaveChangesAsync();
        }
        var result = await Service.RunAsync(_source); Assert.Equal(1, result.Matched);
        await using var verify = await Factory.CreateDbContextAsync();
        var replay = await verify.ReconRuns.SingleAsync(r => r.ReconRunId == result.RunId);
        Assert.Equal(PeriodGranularity.Day, replay.PeriodGranularity); Assert.Equal(Day, replay.PeriodStart);
        Assert.Null(replay.ParentPeriodRunId); Assert.Equal(_source, replay.AnalysisOfRunId);
        Assert.Equal(1, await verify.ReconRuns.CountAsync(r => r.ParentPeriodRunId != null));
    }

    [Fact]
    public async Task Consolidated_daily_month_replays_all_stored_child_imports()
    {
        await AddTrx(100); await AddGl(100);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var tx = Batch(SourceType.InterchangeFee); var gl = Batch(SourceType.GlDump);
            db.AddRange(tx, gl); await db.SaveChangesAsync();
            db.StgInterchangeFees.Add(new StgInterchangeFee { ImportBatchId = tx.ImportBatchId, ProductType = "DC", OrigineCode = "1",
                TransactionDate = Day.ToDateTime(TimeOnly.MinValue), ProcessingDate = Day, BillingCcy = 480, BillingAmount = 200 });
            db.StgGlEntries.Add(new StgGlEntry { ImportBatchId = gl.ImportBatchId, AcctNum = "9849065772", TranDate = Day, ValueDate = Day, TranAmt = 200, TranCcyCode = "MUR" });
            var container = new ReconPeriodRun { Function = ReconFunction.InterchangeToGl, PeriodKey = "2023-05", Status = ReconPeriodRunStatus.RolledUp, RolledUpRunId = _source };
            db.Add(container); await db.SaveChangesAsync();
            foreach (var pair in new[] { (_trx, _gl), (tx.ImportBatchId, gl.ImportBatchId) })
                db.ReconRuns.Add(new ReconRun { Function = ReconFunction.InterchangeToGl, PeriodKey = "2023-05", ProcessorBatchId = pair.Item1,
                    GlBatchId = pair.Item2, ParentPeriodRunId = container.ReconPeriodRunId, Status = ReconRunStatus.Completed, IsArchived = true, PeriodGranularity = PeriodGranularity.Day });
            await db.SaveChangesAsync();
        }
        var result = await Service.RunAsync(_source); Assert.Equal(2, result.Matched);
        await using var verify = await Factory.CreateDbContextAsync();
        Assert.Equal(2, (await verify.ReconRuns.SingleAsync(r => r.ReconRunId == result.RunId)).ProcessorCount);
        Assert.Equal(2, await verify.ReconMatches.CountAsync(m => m.ReconRunId == result.RunId));
        Assert.Equal(4, await verify.ImportBatches.CountAsync());
    }

    [Fact]
    public async Task Analysis_cannot_enter_verification_freeze_or_operational_archive()
    {
        await AddTrx(100); await AddGl(100);
        var result = await Service.RunAsync(_source);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var roleId = await db.UserAppAccesses.Select(a => a.AppAccessId).SingleAsync();
            foreach (var code in new[] { "FREEZE_PERIOD", "ARCHIVE_BATCH" })
                db.AppAccessMenus.Add(new AppAccessMenu { AppAccessId = roleId, Page = new AppPage { PageCode = code, PageName = code }, CanView = true, CanEdit = true });
            await db.SaveChangesAsync();
        }
        var current = new TestCurrentUser(_userId);
        var verification = new ReconVerificationService(Factory, current);
        await Assert.ThrowsAsync<InvalidOperationException>(() => verification.SubmitAsync(result.RunId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => verification.VerifyAsync(result.RunId));
        var archive = new BatchArchiveService(Factory, current, new PermissionService(Factory));
        await Assert.ThrowsAsync<InvalidOperationException>(() => archive.FreezeScopeAsync(result.RunId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => archive.ArchiveRunAsync(result.RunId, false));
        Assert.Equal(1, (await new DailyReconService(Factory, _auth, new PermissionService(Factory)).OrphanSweepAsync()).BatchesInScope);
        await using var verify = await Factory.CreateDbContextAsync();
        Assert.Empty(await verify.BatchFreezes.ToListAsync()); Assert.Empty(await verify.BatchArchives.ToListAsync());
        Assert.Empty(await verify.ReconWorkflowHistories.ToListAsync());
    }

    [Theory]
    [InlineData("permission")] [InlineData("inactive")] [InlineData("anonymous")] [InlineData("missing_gl")]
    public async Task Invalid_requests_do_not_create_analysis_or_change_original(string reason)
    {
        await AddTrx(100);
        if (reason != "missing_gl") await AddGl(100);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            if (reason == "permission") { var grant = await db.AppAccessMenus.SingleAsync(); grant.CanAdd = grant.CanEdit = false; }
            if (reason == "inactive") (await db.Users.SingleAsync()).IsActive = false;
            if (reason == "anonymous") _auth.User = new ClaimsPrincipal(new ClaimsIdentity());
            await db.SaveChangesAsync();
        }
        if (reason == "missing_gl") await Assert.ThrowsAsync<InvalidOperationException>(() => Service.RunAsync(_source));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.RunAsync(_source));
        await using var verify = await Factory.CreateDbContextAsync();
        Assert.Single(await verify.ReconRuns.ToListAsync()); Assert.Single(await verify.ReconMatches.ToListAsync());
    }

    [Fact]
    public async Task Failure_after_primary_marks_only_analysis_failed_and_preserves_original()
    {
        await AddTrx(100); await AddGl(100);
        await using (var db = await Factory.CreateDbContextAsync())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER ReplayCardFailure ON ReconRunPasses AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Kind = 2) THROW 51000, 'Injected card phase failure', 1; END");
        await Assert.ThrowsAsync<DbUpdateException>(() => Service.RunAsync(_source));
        await using var verify = await Factory.CreateDbContextAsync();
        Assert.Equal(ReconRunStatus.Failed, (await verify.ReconRuns.SingleAsync(r => r.AnalysisOfRunId == _source)).Status);
        var original = await verify.ReconRuns.SingleAsync(r => r.ReconRunId == _source);
        Assert.Equal(ReconRunStatus.Completed, original.Status); Assert.Equal(0, original.MatchedCount);
        Assert.Equal(MatchStatus.UnmatchedProcessor, (await verify.ReconMatches.SingleAsync(m => m.ReconRunId == _source)).MatchStatus);
        Assert.Single(await verify.ReconExceptionComments.ToListAsync());
    }

    [Fact]
    public async Task Historical_date_labels_do_not_block_a_read_only_replay()
    {
        await AddTrx(100); await AddGl(100);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            (await db.ImportBatches.SingleAsync(b => b.ImportBatchId == _trx)).PeriodKey = "2023-04";
            (await db.StgInterchangeFees.SingleAsync()).ProcessingDate = new DateOnly(2023, 6, 5);
            await db.SaveChangesAsync();
        }
        var result = await Service.RunAsync(_source); Assert.Equal(1, result.Matched);
        await using var verify = await Factory.CreateDbContextAsync();
        Assert.Equal("2023-04", (await verify.ImportBatches.SingleAsync(b => b.ImportBatchId == _trx)).PeriodKey);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_relevant_GL_value_dates_are_reported_as_skipped_stages(bool unrelatedAccount)
    {
        await AddTrx(100); await AddGl(100);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var gl = await db.StgGlEntries.SingleAsync();
            if (unrelatedAccount) gl.AcctNum = "NOT-A-SETTLEMENT-ACCOUNT";
            else gl.ValueDate = null;
            await db.SaveChangesAsync();
        }
        var result = await Service.RunAsync(_source);
        await using var verify = await Factory.CreateDbContextAsync();
        var passes = await verify.ReconRunPasses.Where(p => p.ReconRunId == result.RunId && p.Kind != ReconPassKind.InitialAnalysis).ToListAsync();
        Assert.Equal(3, passes.Count); Assert.All(passes, p => Assert.StartsWith("Skipped:", p.Detail));
    }

    public async Task DisposeAsync()
    {
        await using var db = await Factory.CreateDbContextAsync(); await db.Database.EnsureDeletedAsync(); await _sp.DisposeAsync();
    }

    private sealed class TestAuth : AuthenticationStateProvider
    {
        public ClaimsPrincipal User = new(new ClaimsIdentity());
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUserService
    {
        public Guid UserId => userId;
    }
}
