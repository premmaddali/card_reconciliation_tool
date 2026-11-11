using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Recon.Web.Data;
using Recon.Web.Domain.Identity;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Staging;
using Recon.Web.Services;
using Recon.Web.Services.Import;
using Recon.Web.Services.Recon;
using Xunit;

namespace Recon.Web.Tests;

public class SecondaryMatchServiceTests : IAsyncLifetime
{
    private ServiceProvider _sp = null!;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly TestAuth _auth = new();
    private IDbContextFactory<AppDbContext> Factory => _sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    private SecondaryMatchService Service => new(Factory, _auth, new PermissionService(Factory));
    private static readonly DateOnly Day = new(2023, 5, 15);
    public async Task InitializeAsync()
    {
        var name = "Recon_Secondary_" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer($"Server=localhost;Database={name};Trusted_Connection=True;TrustServerCertificate=True"));
        _sp = services.BuildServiceProvider();
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedGlAccountsAsync(db);
        var role = new AppAccess { AccessName = "Operator" };
        db.UserAppAccesses.Add(new UserAppAccess { User = new AppUser { Id = _userId, UserName = "operator" }, AppAccess = role });
        db.AppAccessMenus.Add(new AppAccessMenu { AppAccess = role, Page = new AppPage { PageCode = "BULKRECON", PageName = "Bulk" }, CanView = true, CanAdd = true });
        db.ImportBatches.AddRange(new ImportBatch { ContentHash = "tx", Status = ImportStatus.Parsed }, new ImportBatch { ContentHash = "gl", Status = ImportStatus.Parsed });
        await db.SaveChangesAsync();
        db.ReconRuns.Add(new ReconRun { Function = ReconFunction.InterchangeToGl, ProcessorBatchId = 1, GlBatchId = 2, PeriodKey = "2023-05", Status = ReconRunStatus.Completed });
        await db.SaveChangesAsync();
        _auth.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) }, "test"));
    }
    private async Task<int> AddProcessor(decimal amount = 100, int currency = 480, DateOnly? day = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var tx = new StgInterchangeFee { ImportBatchId = 1, ProductType = "DC", OrigineCode = "1", TransactionDate = (day ?? Day).ToDateTime(new TimeOnly(15, 30)), ProcessingDate = Day.AddDays(3), BillingAmount = amount, BillingCcy = currency, ReferenceNumber = Guid.NewGuid().ToString("N")[..12] };
        db.Add(tx); await db.SaveChangesAsync();
        db.ReconMatches.Add(new ReconMatch { ReconRunId = 1, ProcessorSource = StagingSource.InterchangeFee, ProcessorStagingId = tx.StgInterchangeFeeId, MatchStatus = MatchStatus.UnmatchedProcessor, SettlementLeg = ReconSettlementLeg.DebitCardholder, ReferenceNumber = tx.ReferenceNumber, ReconciliationAmount = amount, BillingAmount = amount, BillingCcy = currency, Ccy = currency, ProcessorTransactionDateTime = tx.TransactionDate, ProcessorDate = tx.ProcessingDate });
        (await db.ReconRuns.SingleAsync(x => x.ReconRunId == 1)).ProcessorCount++;
        await db.SaveChangesAsync(); return tx.StgInterchangeFeeId;
    }
    private async Task<int> AddGl(decimal amount = 100, string currency = "MUR", DateOnly? day = null, string account = "9849065772")
    {
        await using var db = await Factory.CreateDbContextAsync();
        var gl = new StgGlEntry { ImportBatchId = 2, AcctNum = account, TranAmt = amount, TranCcyCode = currency, ValueDate = day ?? Day, TranDate = Day.AddDays(2) };
        db.Add(gl); await db.SaveChangesAsync(); return gl.StgGlEntryId;
    }
    private async Task<int> AddAdjacentGl(string period, decimal amount = 100, string currency = "MUR")
    {
        await using var db = await Factory.CreateDbContextAsync();
        var batch = new ImportBatch { ContentHash = Guid.NewGuid().ToString(), SourceType = SourceType.GlDump, Status = ImportStatus.Parsed, PeriodKey = period };
        db.Add(batch); await db.SaveChangesAsync();
        var gl = new StgGlEntry { ImportBatchId = batch.ImportBatchId, AcctNum = "9849065772", TranAmt = amount, TranCcyCode = currency, ValueDate = Day, TranDate = Day };
        db.Add(gl); await db.SaveChangesAsync(); return gl.StgGlEntryId;
    }

    [Theory]
    [InlineData("2023-04")] [InlineData("2023-06")]
    public async Task Adjacent_period_matches_exact_keys_with_medium_confidence_and_provenance(string period)
    {
        await AddProcessor(); await AddGl(999);
        var local = await Service.RunAsync(1);
        var glId = await AddAdjacentGl(period);
        var result = await Service.RunAdjacentAsync(local.RunId);
        Assert.Equal(1, result.Added);
        Assert.Equal(result, await Service.RunAdjacentAsync(local.RunId));
        await using var db = await Factory.CreateDbContextAsync();
        var match = await db.ReconMatches.SingleAsync(x => x.ReconRunId == result.RunId);
        Assert.Equal(glId, match.GlStagingId); Assert.True(match.MatchedAcrossPeriods);
        Assert.NotNull(match.GlSourceBatchId); Assert.Equal(78m, match.Confidence);
        Assert.Contains("Medium confidence", match.MatchComment); Assert.Contains(period, match.MatchComment);
        Assert.Equal(local.RunId, result.RunId); // in place: no child run
        Assert.True(await db.ReconRunPasses.AnyAsync(p => p.ReconRunId == result.RunId && p.Kind == ReconPassKind.CrossPeriod));
        Assert.Single(await db.ReconExceptions.Where(x => x.ReconRunId == result.RunId && x.ExceptionType == MatchStatus.UnmatchedGl).ToListAsync());
    }

    [Fact]
    public async Task Adjacent_candidates_on_both_sides_remain_ambiguous()
    {
        await AddProcessor(); await AddGl(999); var local = await Service.RunAsync(1);
        await AddAdjacentGl("2023-04"); await AddAdjacentGl("2023-06");
        var result = await Service.RunAdjacentAsync(local.RunId);
        Assert.Equal(0, result.Added); Assert.Equal(1, result.Ambiguous);
    }

    [Theory]
    [InlineData("amount")] [InlineData("currency")] [InlineData("date")] [InlineData("leg")]
    [InlineData("used")] [InlineData("distant")] [InlineData("frozen")] [InlineData("scheme")]
    public async Task Adjacent_scope_and_assignment_safeguards(string reason)
    {
        await AddProcessor(); await AddGl(999); var local = await Service.RunAsync(1);
        var glId = await AddAdjacentGl(reason == "distant" ? "2023-03" : "2023-04", reason == "amount" ? 101 : 100, reason == "currency" ? "USD" : "MUR");
        await using var db = await Factory.CreateDbContextAsync();
        var gl = (await db.StgGlEntries.FindAsync(glId))!;
        if (reason == "date") gl.ValueDate = Day.AddDays(1);
        if (reason == "leg") gl.AcctNum = "9849077911";
        if (reason == "used") db.ReconMatches.Add(new ReconMatch { ReconRunId = 1, GlStagingId = glId, MatchStatus = MatchStatus.AmountMismatch });
        if (reason == "frozen") db.BatchFreezes.Add(new BatchFreeze { Function = ReconFunction.InterchangeToGl, PeriodKey = "2023-04" });
        if (reason == "scheme")
        {
            var scheme = new Recon.Web.Platform.Model.SchemeProfile { Name = "Other", Scheme = "Visa", Cms = "Other" };
            db.Add(scheme); await db.SaveChangesAsync();
            (await db.ImportBatches.FindAsync(gl.ImportBatchId))!.SchemeProfileId = scheme.SchemeProfileId;
        }
        await db.SaveChangesAsync();
        if (reason is "distant" or "frozen" or "scheme")
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service.RunAdjacentAsync(local.RunId));
        else Assert.Equal(0, (await Service.RunAdjacentAsync(local.RunId)).Added);
    }

    [Fact]
    public async Task Adjacent_year_boundary_and_concurrent_clicks_are_safe()
    {
        await AddProcessor(); await AddGl(999);
        await using var db = await Factory.CreateDbContextAsync();
        (await db.ReconRuns.FindAsync(1))!.PeriodKey = "2023-01"; await db.SaveChangesAsync();
        var local = await Service.RunAsync(1); await AddAdjacentGl("2022-12");
        var results = await Task.WhenAll(Service.RunAdjacentAsync(local.RunId), Service.RunAdjacentAsync(local.RunId));
        Assert.Equal(results[0], results[1]); Assert.Equal(1, results[0].Added);
    }

    [Fact]
    public async Task Primary_engine_cannot_reuse_a_cross_period_assignment()
    {
        await AddProcessor(); await AddGl(999); var local = await Service.RunAsync(1);
        var glId = await AddAdjacentGl("2023-04"); await Service.RunAdjacentAsync(local.RunId);
        await using var db = await Factory.CreateDbContextAsync();
        var gl = (await db.StgGlEntries.FindAsync(glId))!;
        gl.TranDate = new DateOnly(2023, 4, 15);
        var batch = new ImportBatch { ContentHash = "other-tx", SourceType = SourceType.InterchangeFee, PeriodKey = "2023-04" };
        db.Add(batch); await db.SaveChangesAsync();
        db.StgInterchangeFees.Add(new StgInterchangeFee { ImportBatchId = batch.ImportBatchId, OrigineCode = "1", ProductType = "DC", BillingAmount = 100, BillingCcy = 480, TransactionDate = new DateTime(2023, 4, 15), ProcessingDate = new DateOnly(2023, 4, 15) });
        await db.SaveChangesAsync();
        var engine = new ReconEngine(Factory, Microsoft.Extensions.Options.Options.Create(new MatchWeights()));
        var result = await engine.RunAsync(ReconFunction.InterchangeToGl, batch.ImportBatchId, gl.ImportBatchId, "2023-04");
        Assert.Equal(0, result.MatchedCount);
    }

    [Fact]
    public async Task In_place_pass_matches_uniquely_and_preserves_review_comments_on_remaining_exceptions()
    {
        var t1 = await AddProcessor(); await AddGl();          // t1 will match the seeded GL
        var t2 = await AddProcessor(amount: 250);              // t2 stays unmatched (no GL at 250)
        await using var db = await Factory.CreateDbContextAsync();
        db.ReconExceptions.Add(new ReconException { ReconRunId = 1, ProcessorStagingId = t2, ProcessorSource = StagingSource.InterchangeFee, ExceptionType = MatchStatus.UnmatchedProcessor, Narrative = "Original evidence", Comments = [new ReconExceptionComment { Body = "Keep this review" }] });
        await db.SaveChangesAsync();
        var result = await Service.RunAsync(1);
        Assert.Equal(1, result.Added); Assert.Equal(0, result.Ambiguous);
        Assert.Equal(1, result.RunId);                         // in place: same batch, no child run
        Assert.Equal(result, await Service.RunAsync(1));       // pass recorded once; re-running is a no-op
        var run = await db.ReconRuns.AsNoTracking().SingleAsync();
        Assert.Equal(1, run.MatchedCount);
        var m1 = await db.ReconMatches.AsNoTracking().SingleAsync(x => x.ProcessorStagingId == t1);
        Assert.Equal(MatchStatus.Matched, m1.MatchStatus); Assert.Equal(Day, m1.GlValueDate);
        Assert.Equal(Day.AddDays(2), m1.GlDate); Assert.Equal(480, m1.GlCcy);
        Assert.Contains(SecondaryMatchService.MethodLabel, m1.ScoreBreakdown);
        Assert.Equal(MatchStatus.UnmatchedProcessor, (await db.ReconMatches.AsNoTracking().SingleAsync(x => x.ProcessorStagingId == t2)).MatchStatus);
        Assert.Single(await db.ReconExceptionComments.ToListAsync());  // comment on the still-unmatched t2 kept
        Assert.Equal(1, (await db.ReconPassSummaries.SingleAsync(x => x.ReconRunId == 1 && x.Day == null)).MatchedCount);
        Assert.True(await db.ReconRunPasses.AnyAsync(p => p.ReconRunId == 1 && p.Kind == ReconPassKind.SecondaryAmount));
    }
    [Theory]
    [InlineData("secondary")] [InlineData("adjacent")] [InlineData("card")]
    public async Task Credit_refinement_uses_billing_less_ISS(string pass)
    {
        await AddProcessor(amount: 103);
        var glId = pass == "adjacent" ? await AddAdjacentGl("2023-04") : await AddGl();
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var t = await db.StgInterchangeFees.SingleAsync();
            t.ProductType = "CR"; t.IssMarkupAmount = 3; t.MicrofilmRefNumber = "KEY";
            t.CardAccountEntityId = "4111111111111111";
            (await db.ReconMatches.SingleAsync()).SettlementLeg = ReconSettlementLeg.CreditIntermediary;
            var g = await db.StgGlEntries.SingleAsync(g => g.StgGlEntryId == glId);
            g.AcctNum = "9849077911"; g.TranParticular2 = "123456789012KEY";
            g.TranRmks = CardIdentity.ImportRemarks("4111111111111111");
            await db.SaveChangesAsync();
        }
        var review = await new MatchingReviewService(Factory, _auth, new PermissionService(Factory)).AnalyzeAsync(1);
        if (pass != "adjacent")
        {
            Assert.Equal(1, review.UniqueDateWindowCandidates);
            Assert.Equal(100m, Assert.Single(review.DateWindowCandidates).Amount);
        }
        var result = pass switch
        {
            "adjacent" => await Service.RunAdjacentAsync(1),
            "card" => await Service.RunCardAsync(1),
            _ => await Service.RunAsync(1)
        };
        Assert.Equal(1, result.Added);
        await using var verify = await Factory.CreateDbContextAsync();
        var match = await verify.ReconMatches.SingleAsync();
        Assert.Equal(100m, match.ReconciliationAmount);
        Assert.Equal(100m, match.GlAmount);
        Assert.Equal(103m, match.BillingAmount);
        Assert.Contains("billing minus ISS markup", match.ScoreBreakdown);
    }

    [Theory]
    [InlineData(2, 1)] [InlineData(1, 2)] [InlineData(2, 2)]
    public async Task Withdrawal_secondary_pass_pairs_same_day_amounts_once(int processors, int gls)
    {
        for (var i = 0; i < processors; i++) await AddProcessor();
        for (var i = 0; i < gls; i++) await AddGl();
        await using (var db = await Factory.CreateDbContextAsync())
        {
            foreach (var t in await db.StgInterchangeFees.ToListAsync()) t.TransactionCode = "04";
            foreach (var g in await db.StgGlEntries.ToListAsync())
            {
                g.TranParticular = "CASH/ATM";
                g.TranParticular2 = "DIFFERENT";
            }
            await db.SaveChangesAsync();
        }
        var result = await Service.RunAsync(1);
        Assert.Equal(Math.Min(processors, gls), result.Added);
        Assert.Equal(result, await Service.RunAsync(1));
        await using var verify = await Factory.CreateDbContextAsync();
        var matched = await verify.ReconMatches.Where(m => m.MatchStatus == MatchStatus.Matched).ToListAsync();
        Assert.Equal(matched.Count, matched.Select(m => m.GlStagingId).Distinct().Count());
        Assert.All(matched, m => Assert.Contains("time ignored", m.ScoreBreakdown));
    }

    [Theory]
    [InlineData(2, 1)] [InlineData(1, 2)] [InlineData(2, 2)]
    public async Task Repeated_composite_keys_remain_ambiguous(int processors, int gls)
    {
        for (var i=0;i<processors;i++) await AddProcessor();
        for (var i=0;i<gls;i++) await AddGl();
        var result = await Service.RunAsync(1);
        Assert.Equal(0,result.Added); Assert.Equal(processors,result.Ambiguous);
        await using var db = await Factory.CreateDbContextAsync();
        Assert.True(await db.ReconMatches.Where(x=>x.ReconRunId==result.RunId).AllAsync(x=>x.MatchStatus==MatchStatus.Ambiguous && x.GlStagingId==null));
        Assert.Equal(gls,await db.ReconExceptions.CountAsync(x=>x.ReconRunId==result.RunId && x.ExceptionType==MatchStatus.UnmatchedGl));
    }
    [Theory]
    [InlineData("amount")] [InlineData("currency")] [InlineData("date")] [InlineData("leg")] [InlineData("missingCurrency")]
    public async Task Every_key_component_is_required(string mismatch)
    {
        await AddProcessor();
        await AddGl(mismatch=="amount"?100.01m:100, mismatch=="currency"?"USD":mismatch=="missingCurrency"?"":"MUR", mismatch=="date"?Day.AddDays(1):Day, mismatch=="leg"?"9849077911":"9849065772");
        Assert.Equal(0,(await Service.RunAsync(1)).Added);
    }
    [Fact]
    public async Task Used_gl_is_not_reassigned_and_existing_match_is_carried_forward()
    {
        await AddProcessor(); var used=await AddGl(); await AddProcessor();
        await using var db=await Factory.CreateDbContextAsync();
        var original=await db.ReconMatches.OrderBy(x=>x.ReconMatchId).FirstAsync();
        original.MatchStatus=MatchStatus.Matched; original.GlStagingId=used; original.ScoreBreakdown="reference agrees";
        await db.SaveChangesAsync();
        var result=await Service.RunAsync(1);
        Assert.Equal(0,result.Added);
        Assert.Equal(1,await db.ReconMatches.CountAsync(x=>x.ReconRunId==result.RunId && x.GlStagingId==used));
        Assert.Equal(1,await db.ReconMatches.CountAsync(x=>x.ReconRunId==result.RunId && x.MatchStatus==MatchStatus.UnmatchedProcessor));
    }
    [Theory]
    [InlineData("anonymous")] [InlineData("viewer")] [InlineData("inactive")]
    public async Task Service_enforces_authorization(string kind)
    {
        await using var db=await Factory.CreateDbContextAsync();
        if(kind=="anonymous") _auth.User=new ClaimsPrincipal(new ClaimsIdentity());
        if(kind=="viewer") (await db.AppAccessMenus.SingleAsync()).CanAdd=false;
        if(kind=="inactive") (await db.Users.SingleAsync()).IsActive=false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Service.RunAsync(1));
        Assert.Single(await db.ReconRuns.ToListAsync());
    }
    [Theory]
    [InlineData("archived")] [InlineData("submitted")] [InlineData("frozen")] [InlineData("running")]
    public async Task Protected_runs_cannot_start_secondary_matching(string kind)
    {
        await using var db=await Factory.CreateDbContextAsync(); var run=await db.ReconRuns.SingleAsync();
        if(kind=="archived") run.IsArchived=true;
        if(kind=="submitted") run.VerificationStatus=ReconVerificationStatus.AwaitingVerification;
        if(kind=="running") run.Status=ReconRunStatus.Running;
        if(kind=="frozen") db.BatchFreezes.Add(new BatchFreeze{Function=run.Function,PeriodKey=run.PeriodKey});
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service.RunAsync(1));
    }
    [Fact]
    public async Task Missing_value_date_does_not_fall_back_to_posting_date()
    {
        await AddProcessor();await AddGl();await using var db=await Factory.CreateDbContextAsync();
        (await db.StgGlEntries.SingleAsync()).ValueDate=null;await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service.RunAsync(1));
        Assert.Single(await db.ReconRuns.ToListAsync());
    }
    [Fact]
    public async Task Concurrent_clicks_create_only_one_child()
    {
        await AddProcessor();await AddGl();
        var results=await Task.WhenAll(Service.RunAsync(1),Service.RunAsync(1));
        Assert.Equal(results[0].RunId,results[1].RunId);
    }
    [Fact]
    public async Task Failure_rolls_back_child_and_keeps_original()
    {
        await AddProcessor();await AddGl();await using var db=await Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailSecondary ON [ReconRunPasses] AFTER INSERT AS THROW 50001, 'Test rollback', 1;");
        await Assert.ThrowsAnyAsync<Exception>(()=>Service.RunAsync(1));
        Assert.Single(await db.ReconRuns.ToListAsync());Assert.Single(await db.ReconMatches.ToListAsync());
        Assert.Empty(await db.ReconRunPasses.ToListAsync());
    }
    [Fact]
    public async Task Reimport_same_gl_backfills_value_dates_without_duplicate_rows()
    {
        var csv="SOL_ID,GL_SUB_HEAD_CODE,ACCT_NUM,TRAN_DATE,VALUE_DATE,TRAN_AMT,TRAN_CRNCY_CODE\n1,1,9849065772,2023-05-17,2023-05-15,100,MUR\n";
        var importer=new ImportService(Factory);
        using var first=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var initial=await importer.ImportAsync(first,"value-dates.csv","2023-05");Assert.True(initial.Success,initial.Error);
        await using var db=await Factory.CreateDbContextAsync();
        var row=await db.StgGlEntries.SingleAsync(x=>x.ImportBatchId==initial.ImportBatchId);
        Assert.Equal(Day,row.ValueDate);row.ValueDate=null;await db.SaveChangesAsync();
        using var second=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var duplicate=await importer.ImportAsync(second,"value-dates.csv","2023-05");
        Assert.True(duplicate.Duplicate);Assert.Equal(initial.ImportBatchId,duplicate.ImportBatchId);
        Assert.Equal(Day,(await db.StgGlEntries.AsNoTracking().SingleAsync(x=>x.ImportBatchId==initial.ImportBatchId)).ValueDate);
    }
    [Fact]
    public async Task May_files_secondary_pass_preserves_primary_assignments_and_uses_exact_keys()
    {
        var shared=Path.Combine(SampleFiles.Root,"SharedFiles (5)");
        var importer=new ImportService(Factory);
        async Task<int> Import(string name)
        {
            await using var file=File.OpenRead(Path.Combine(shared,name));
            var outcome=await importer.ImportAsync(file,name,"2023-05");
            Assert.True(outcome.Success,outcome.Error);return outcome.ImportBatchId!.Value;
        }
        var tx=await Import("may 2023.xlsx");var gl=await Import("May 2023 gl.xlsx");
        var engine=new ReconEngine(Factory,Microsoft.Extensions.Options.Options.Create(new MatchWeights()));
        var primary=await engine.RunAsync(ReconFunction.InterchangeToGl,tx,gl,"2023-05");
        var secondary=await Service.RunAsync(primary.ReconRunId);
        Assert.True(secondary.Added>0);
        await using var db=await Factory.CreateDbContextAsync();
        var primaryAssignments=await db.ReconMatches.AsNoTracking().Where(x=>x.ReconRunId==primary.ReconRunId && x.GlStagingId!=null).ToListAsync();
        var results=await db.ReconMatches.AsNoTracking().Where(x=>x.ReconRunId==secondary.RunId).ToListAsync();
        Assert.Equal(primary.ProcessorCount,results.Count);
        var resultsByProcessor=results.ToDictionary(x=>x.ProcessorStagingId);
        foreach(var prior in primaryAssignments)
        {
            var inherited=resultsByProcessor[prior.ProcessorStagingId];
            Assert.Equal(prior.GlStagingId,inherited.GlStagingId);
            Assert.Equal(prior.MatchStatus,inherited.MatchStatus);
            Assert.Equal(prior.ScoreBreakdown,inherited.ScoreBreakdown);
        }
        Assert.Equal(results.Count(x=>x.GlStagingId!=null),results.Where(x=>x.GlStagingId!=null).Select(x=>x.GlStagingId).Distinct().Count());
        var added=results.Where(x=>x.MatchStatus==MatchStatus.Matched && x.ScoreBreakdown?.StartsWith(SecondaryMatchService.MethodLabel)==true).ToList();
        Assert.Equal(secondary.Added,added.Count);
        var stagedTransactions = await db.StgInterchangeFees.ToDictionaryAsync(t => t.StgInterchangeFeeId);
        Assert.All(added,x=>{
            Assert.Equal(DateOnly.FromDateTime(x.ProcessorTransactionDateTime!.Value),x.GlValueDate);
            Assert.Equal(x.BillingCcy,x.GlCcy);
            var t = stagedTransactions[x.ProcessorStagingId];
            Assert.Equal(MatchingEvidence.BillingAmountForMatching(t.ProductType, t.BillingAmount, t.IssMarkupAmount), x.GlAmount);
        });
        Assert.Equal(primary.MatchedCount+secondary.Added,results.Count(x=>x.MatchStatus==MatchStatus.Matched));
    }
    [Fact]
    public async Task Review_is_read_only_and_includes_ambiguous_competitors_in_uniqueness()
    {
        var first = await AddProcessor(); var second = await AddProcessor(day: Day.AddDays(1));
        await AddGl(day: Day.AddDays(1));
        await using var db = await Factory.CreateDbContextAsync();
        (await db.ReconMatches.SingleAsync(x => x.ProcessorStagingId == second)).MatchStatus = MatchStatus.Ambiguous;
        await db.SaveChangesAsync();
        var service = new MatchingReviewService(Factory, _auth, new PermissionService(Factory));
        var report = await service.AnalyzeAsync(1);
        Assert.Equal(0, report.UniqueDateWindowCandidates);
        Assert.Equal(1, report.AmbiguousTransactions);
        Assert.Equal(1, report.UnmatchedTransactions);
        Assert.Equal(1, await db.ReconRuns.CountAsync());
        Assert.All(await db.ReconMatches.AsNoTracking().ToListAsync(), x => Assert.Null(x.GlStagingId));
    }
    [Fact]
    public async Task Review_rejects_unauthenticated_access()
    {
        _auth.User = new ClaimsPrincipal(new ClaimsIdentity());
        var service = new MatchingReviewService(Factory, _auth, new PermissionService(Factory));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AnalyzeAsync(1));
    }
    [Fact]
    public async Task Additional_pass_rejects_wrong_source_period()
    {
        await AddProcessor(); await AddGl();
        await using var db = await Factory.CreateDbContextAsync();
        (await db.ImportBatches.FindAsync(1))!.PeriodKey = "2023-04";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.RunAsync(1));
        Assert.Equal(1, await db.ReconRuns.CountAsync());
    }
    [Fact]
    public async Task Local_ai_rejects_cloud_alias_before_sending_evidence()
    {
        await AddProcessor(); await AddGl(999);
        var calls = 0;
        using var handler = new ReviewHttpHandler(_ =>
        {
            calls++;
            return Task.FromResult(JsonResponse("""{"models":[{"name":"qwen2.5:7b","remote_host":"https://ollama.com"}]}"""));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        var service = new LocalAiReviewService(new MatchingReviewService(Factory, _auth, new PermissionService(Factory)),
            http, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(1));
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task Secondary_and_review_passes_reject_conflicting_identity_references()
    {
        await AddProcessor(); await AddGl();
        await using var db = await Factory.CreateDbContextAsync();
        (await db.StgGlEntries.SingleAsync()).TranParticular2 = "DIFFERENT_REFERENCE";
        await db.SaveChangesAsync();
        var result = await Service.RunAsync(1);
        Assert.Equal(0, result.Added);
        var report = await new MatchingReviewService(Factory, _auth, new PermissionService(Factory)).AnalyzeAsync(result.RunId);
        Assert.Equal(0, report.UniqueDateWindowCandidates);
        Assert.Contains("references conflict", (await db.ReconMatches.SingleAsync(x => x.ReconRunId == result.RunId)).MatchComment);
    }
    [Fact]
    public async Task Local_ai_receives_redacted_evidence_and_never_writes_assignments()
    {
        await AddProcessor(); await AddGl(999);
        await using var db = await Factory.CreateDbContextAsync();
        var tx = await db.StgInterchangeFees.SingleAsync();
        tx.CardNumberHashed = "PRIVATE_CARD"; tx.CardAccountNumber = "PRIVATE_ACCOUNT";
        tx.ReferenceNumber = "PRIVATE_REF"; tx.MerchantAcronym = "PRIVATE_MERCHANT";
        tx.TransactionWording = "PRIVATE_NARRATIVE";
        await db.SaveChangesAsync();
        var matchId = (await db.ReconMatches.SingleAsync()).ReconMatchId;
        string? prompt = null;
        using var handler = new ReviewHttpHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return JsonResponse("""{"models":[{"name":"qwen2.5:7b"}]}""");
            prompt = await request.Content!.ReadAsStringAsync();
            return JsonResponse(System.Text.Json.JsonSerializer.Serialize(new {
                done = true, done_reason = "stop", message = new { content = System.Text.Json.JsonSerializer.Serialize(new {
                    findings = new[] { new { matchId, action = "request_reference", glIds = Array.Empty<int>(), explanation = "Request source reference." } }
                }) }
            }));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434/") };
        var service = new LocalAiReviewService(new MatchingReviewService(Factory, _auth, new PermissionService(Factory)),
            http, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var result = await service.RunAsync(1);
        Assert.Single(result.Findings); Assert.NotNull(prompt);
        foreach (var value in new[] { "PRIVATE_CARD", "PRIVATE_ACCOUNT", "PRIVATE_REF", "PRIVATE_MERCHANT", "PRIVATE_NARRATIVE" })
            Assert.DoesNotContain(value, prompt);
        Assert.Equal(1, await db.ReconRuns.CountAsync());
        Assert.Null((await db.ReconMatches.AsNoTracking().SingleAsync()).GlStagingId);
    }
    private static HttpResponseMessage JsonResponse(string content) => new(System.Net.HttpStatusCode.OK)
    { Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json") };
    [Theory]
    [InlineData("4111111111111111", 1)] [InlineData("5555555555554444", 0)]
    public async Task Secondary_matching_and_review_respect_card_evidence(string glCard, int expected)
    {
        var txId = await AddProcessor(); var glId = await AddGl();
        await using (var db = await Factory.CreateDbContextAsync())
        {
            (await db.StgInterchangeFees.FindAsync(txId))!.CardAccountEntityId = CardIdentity.Normalize("4111111111111111");
            (await db.StgGlEntries.FindAsync(glId))!.TranRmks = CardIdentity.ImportRemarks(glCard);
            await db.SaveChangesAsync();
        }
        var review = new MatchingReviewService(Factory, _auth, new PermissionService(Factory));
        var report = await review.AnalyzeAsync(1);
        Assert.Equal(expected, report.UniqueDateWindowCandidates);
        Assert.Equal(1, report.CardIdentityTransactions);
        var payload = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.DoesNotContain("4111111111111111", payload);
        Assert.DoesNotContain("card:v1:", payload);
        if (expected == 1) Assert.True(Assert.Single(Assert.Single(report.DateWindowCandidates).Candidates).CardIdentityAgrees);
        Assert.Equal(expected, (await Service.RunAsync(1)).Added);
    }

    private async Task SetCards(int txId, int glId, string txCard = "4111111111111111", string glCard = "4111111111111111")
    {
        await using var db = await Factory.CreateDbContextAsync();
        (await db.StgInterchangeFees.FindAsync(txId))!.CardAccountEntityId = CardIdentity.Normalize(txCard);
        (await db.StgGlEntries.FindAsync(glId))!.TranRmks = CardIdentity.ImportRemarks(glCard);
        await db.SaveChangesAsync();
    }
    [Fact]
    public async Task Card_pass_disambiguates_in_place_and_is_idempotent()
    {
        var t1 = await AddProcessor(); var t2 = await AddProcessor();
        var g1 = await AddGl(); var g2 = await AddGl();
        await SetCards(t1, g2); await SetCards(t2, g1, "5555555555554444", "5555555555554444");
        var datePass = await Service.RunAsync(1); Assert.Equal(0, datePass.Added);   // identical amount/date/cur → ambiguous
        var result = await Service.RunCardAsync(1); Assert.Equal(2, result.Added);
        Assert.Equal(1, result.RunId); Assert.Equal(datePass.RunId, result.RunId);  // same batch throughout
        Assert.Equal(result, await Service.RunCardAsync(1));                          // idempotent
        await using var db = await Factory.CreateDbContextAsync();
        var matches = await db.ReconMatches.Where(x => x.ReconRunId == 1).ToListAsync();
        Assert.Equal(g2, matches.Single(x => x.ProcessorStagingId == t1).GlStagingId);
        Assert.Equal(g1, matches.Single(x => x.ProcessorStagingId == t2).GlStagingId);
        Assert.All(matches, x => Assert.StartsWith(SecondaryMatchService.CardMethodLabel, x.ScoreBreakdown));
        var payload = System.Text.Json.JsonSerializer.Serialize(matches);
        Assert.DoesNotContain("4111111111111111", payload); Assert.DoesNotContain("card:v1:", payload);
        Assert.True(await db.ReconRunPasses.AnyAsync(p => p.ReconRunId == 1 && p.Kind == ReconPassKind.CardNumber));
        Assert.True(await db.ReconRunPasses.AnyAsync(p => p.ReconRunId == 1 && p.Kind == ReconPassKind.SecondaryAmount));
    }
    [Theory]
    [InlineData(2, 1)] [InlineData(1, 2)] [InlineData(2, 2)]
    public async Task Card_pass_requires_uniqueness_on_both_sides(int processorCount, int glCount)
    {
        var txs = new List<int>(); var gls = new List<int>();
        for (var i = 0; i < processorCount; i++) txs.Add(await AddProcessor());
        for (var i = 0; i < glCount; i++) gls.Add(await AddGl());
        foreach (var t in txs) foreach (var g in gls) await SetCards(t, g);
        var result = await Service.RunCardAsync(1); Assert.Equal(0, result.Added); Assert.Equal(processorCount, result.Ambiguous);
    }
    [Theory]
    [InlineData(-2, 1)] [InlineData(2, 1)] [InlineData(3, 0)]
    public async Task Card_pass_uses_transaction_and_value_dates_with_bounded_window(int lag, int expected)
    {
        var t = await AddProcessor(); var g = await AddGl(day: Day.AddDays(lag)); await SetCards(t, g);
        var result = await Service.RunCardAsync(1); Assert.Equal(expected, result.Added);
    }
    [Fact]
    public async Task Card_pass_does_not_assign_overlapping_date_candidates_by_iteration_order()
    {
        var t1 = await AddProcessor(); var t2 = await AddProcessor(day: Day.AddDays(1));
        var g = await AddGl(day: Day.AddDays(1)); await SetCards(t1, g); await SetCards(t2, g);
        var result = await Service.RunCardAsync(1); Assert.Equal(0, result.Added); Assert.Equal(2, result.Ambiguous);
    }
    [Fact]
    public async Task Card_pass_preserves_existing_assignment_and_never_reuses_its_GL()
    {
        var t1 = await AddProcessor(); var t2 = await AddProcessor(); var g = await AddGl();
        await SetCards(t1, g); await SetCards(t2, g);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var assigned = await db.ReconMatches.SingleAsync(x => x.ReconRunId == 1 && x.ProcessorStagingId == t1);
            assigned.MatchStatus = MatchStatus.Matched; assigned.GlStagingId = g; assigned.Confidence = 88;
            assigned.ScoreBreakdown = "Initial reference evidence"; await db.SaveChangesAsync();
        }
        var result = await Service.RunCardAsync(1); Assert.Equal(0, result.Added);
        await using var read = await Factory.CreateDbContextAsync();
        var matches = await read.ReconMatches.Where(x => x.ReconRunId == result.RunId).ToListAsync();
        var inherited = matches.Single(x => x.ProcessorStagingId == t1);
        Assert.Equal(g, inherited.GlStagingId); Assert.Equal(88, inherited.Confidence); Assert.Equal("Initial reference evidence", inherited.ScoreBreakdown);
        Assert.Null(matches.Single(x => x.ProcessorStagingId == t2).GlStagingId);
    }
    [Theory]
    [InlineData("card")] [InlineData("currency")] [InlineData("amount")] [InlineData("leg")] [InlineData("reference")]
    public async Task Card_pass_rejects_conflicting_evidence(string conflict)
    {
        var t = await AddProcessor();
        var g = await AddGl(amount: conflict == "amount" ? 101 : 100, currency: conflict == "currency" ? "USD" : "MUR", account: conflict == "leg" ? "9849077911" : "9849065772");
        await SetCards(t, g, glCard: conflict == "card" ? "5555555555554444" : "4111111111111111");
        if (conflict == "reference")
        {
            await using var db = await Factory.CreateDbContextAsync(); (await db.StgGlEntries.FindAsync(g))!.TranParticular2 = "DIFFERENT"; await db.SaveChangesAsync();
        }
        Assert.Equal(0, (await Service.RunCardAsync(1)).Added);
    }
    [Fact]
    public async Task Card_pass_without_TMS_identity_explains_required_import_and_saves_no_run()
    {
        await AddProcessor(); await AddGl();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service.RunCardAsync(1));
        Assert.Contains("CARD_ACCOUNT_ENTITY_ID", error.Message);
        await using var db = await Factory.CreateDbContextAsync(); Assert.Equal(1, await db.ReconRuns.CountAsync());
    }

    private class ReviewHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
    public async Task DisposeAsync(){await using var db=await Factory.CreateDbContextAsync();await db.Database.EnsureDeletedAsync();await _sp.DisposeAsync();}
    private class TestAuth:AuthenticationStateProvider {public ClaimsPrincipal User=new(new ClaimsIdentity()); public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(User));}
}

