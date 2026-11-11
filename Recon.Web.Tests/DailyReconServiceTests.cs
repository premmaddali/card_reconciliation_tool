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
using Recon.Web.Services.Recon;
using Xunit;

namespace Recon.Web.Tests;

/// <summary>
/// Proves the daily rolling-pool back-match: a later date-range run's GL resolves an earlier run's
/// outstanding transaction (and the reverse), bounded by the admin settlement spread, writing into
/// whichever run owns each record and returning a per-run change table.
/// </summary>
public class DailyReconServiceTests : IAsyncLifetime
{
    private ServiceProvider _sp = null!;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly TestAuth _auth = new();
    private int _containerId, _runA, _runB, _procA, _glA, _procB, _glB;
    private IDbContextFactory<AppDbContext> Factory => _sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    private DailyReconService Service => new(Factory, _auth, new PermissionService(Factory));
    private static readonly DateOnly EarlyDay = new(2023, 5, 3);
    private static readonly DateOnly LateDay = new(2023, 5, 23); // exactly 20 days later — spread boundary

    public async Task InitializeAsync()
    {
        var name = "Recon_Daily_" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer($"Server=localhost;Database={name};Trusted_Connection=True;TrustServerCertificate=True"));
        _sp = services.BuildServiceProvider();
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedGlAccountsAsync(db);

        var role = new AppAccess { AccessName = "Operator" };
        db.UserAppAccesses.Add(new UserAppAccess { User = new AppUser { Id = _userId, UserName = "operator" }, AppAccess = role });
        db.AppAccessMenus.Add(new AppAccessMenu { AppAccess = role, Page = new AppPage { PageCode = "BULKRECON", PageName = "Bulk" }, CanView = true, CanAdd = true, CanEdit = true });

        var container = new ReconPeriodRun { Function = ReconFunction.InterchangeToGl, PeriodKey = "2023-05", Status = ReconPeriodRunStatus.Open };
        db.ReconPeriodRuns.Add(container);
        var procA = new ImportBatch { ContentHash = "pa", Status = ImportStatus.Parsed, SourceType = SourceType.InterchangeFee };
        var glA = new ImportBatch { ContentHash = "ga", Status = ImportStatus.Parsed, SourceType = SourceType.GlDump, PeriodKey = "2023-05" };
        var procB = new ImportBatch { ContentHash = "pb", Status = ImportStatus.Parsed, SourceType = SourceType.InterchangeFee };
        var glB = new ImportBatch { ContentHash = "gb", Status = ImportStatus.Parsed, SourceType = SourceType.GlDump, PeriodKey = "2023-05" };
        db.ImportBatches.AddRange(procA, glA, procB, glB);
        await db.SaveChangesAsync();
        _containerId = container.ReconPeriodRunId; _procA = procA.ImportBatchId; _glA = glA.ImportBatchId; _procB = procB.ImportBatchId; _glB = glB.ImportBatchId;

        var runA = new ReconRun { Function = ReconFunction.InterchangeToGl, ProcessorBatchId = _procA, GlBatchId = _glA, PeriodKey = "2023-05", PeriodStart = EarlyDay, PeriodEnd = EarlyDay, Status = ReconRunStatus.Completed, ParentPeriodRunId = _containerId, PeriodGranularity = PeriodGranularity.Day };
        var runB = new ReconRun { Function = ReconFunction.InterchangeToGl, ProcessorBatchId = _procB, GlBatchId = _glB, PeriodKey = "2023-05", PeriodStart = LateDay, PeriodEnd = LateDay, Status = ReconRunStatus.Completed, ParentPeriodRunId = _containerId, PeriodGranularity = PeriodGranularity.Day };
        db.ReconRuns.AddRange(runA, runB);
        await db.SaveChangesAsync();
        _runA = runA.ReconRunId; _runB = runB.ReconRunId;
        _auth.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) }, "test"));
    }

    /// <summary>Outstanding transaction in a run: a staged interchange row, an UnmatchedProcessor match and its exception.</summary>
    private async Task<int> AddOutstanding(int runId, int procBatch, DateOnly day, decimal amount = 100, int ccy = 480, string? reference = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var r = reference ?? Guid.NewGuid().ToString("N")[..12];
        var tx = new StgInterchangeFee { ImportBatchId = procBatch, ProductType = "DC", OrigineCode = "1", TransactionDate = day.ToDateTime(new TimeOnly(15, 30)), ProcessingDate = day.AddDays(1), BillingAmount = amount, BillingCcy = ccy, ReferenceNumber = r };
        db.Add(tx); await db.SaveChangesAsync();
        db.ReconMatches.Add(new ReconMatch { ReconRunId = runId, ProcessorSource = StagingSource.InterchangeFee, ProcessorStagingId = tx.StgInterchangeFeeId, MatchStatus = MatchStatus.UnmatchedProcessor, SettlementLeg = ReconSettlementLeg.DebitCardholder, ReferenceNumber = r, ReconciliationAmount = amount, BillingAmount = amount, BillingCcy = ccy, Ccy = ccy, ProcessorTransactionDateTime = tx.TransactionDate, ProcessorDate = day.AddDays(1) });
        db.ReconExceptions.Add(new ReconException { ReconRunId = runId, ExceptionType = MatchStatus.UnmatchedProcessor, ProcessorStagingId = tx.StgInterchangeFeeId, ProcessorSource = StagingSource.InterchangeFee, ReferenceNumber = r, Ccy = ccy, Narrative = "no GL" });
        await db.SaveChangesAsync();
        return tx.StgInterchangeFeeId;
    }

    /// <summary>Unassigned GL in a run: a staged GL row and its UnmatchedGl exception (primary engine shape).</summary>
    private async Task<int> AddUnassignedGl(int runId, int glBatch, DateOnly valueDate, decimal amount = 100, string ccy = "MUR", string? reference = null, string account = "9849065772")
    {
        await using var db = await Factory.CreateDbContextAsync();
        var gl = new StgGlEntry { ImportBatchId = glBatch, AcctNum = account, TranAmt = amount, TranCcyCode = ccy, ValueDate = valueDate, TranDate = valueDate, TranParticular2 = reference };
        db.Add(gl); await db.SaveChangesAsync();
        db.ReconExceptions.Add(new ReconException { ReconRunId = runId, ExceptionType = MatchStatus.UnmatchedGl, GlStagingId = gl.StgGlEntryId, GlAccountNumber = account, Ccy = 480, Narrative = "no txn" });
        await db.SaveChangesAsync();
        return gl.StgGlEntryId;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Credit_daily_and_orphan_matching_use_billing_less_ISS(bool orphan)
    {
        await AddOutstanding(_runA, _procA, EarlyDay, amount: 103);
        await AddUnassignedGl(_runB, _glB, orphan ? EarlyDay.AddDays(45) : EarlyDay, reference: "123456789012KEY");
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var t = await db.StgInterchangeFees.SingleAsync();
            t.ProductType = "CR"; t.IssMarkupAmount = 3; t.MicrofilmRefNumber = "KEY";
            (await db.ReconMatches.SingleAsync()).SettlementLeg = ReconSettlementLeg.CreditIntermediary;
            (await db.StgGlEntries.SingleAsync()).AcctNum = "9849077911";
            if (orphan)
                foreach (var r in await db.ReconRuns.ToListAsync())
                {
                    r.ParentPeriodRunId = null;
                    r.PeriodGranularity = PeriodGranularity.Month;
                }
            await db.SaveChangesAsync();
        }
        var added = orphan ? (await Service.OrphanSweepAsync()).Added : (await Service.BackMatchAsync(_containerId)).Added;
        Assert.Equal(1, added);
        await using var verify = await Factory.CreateDbContextAsync();
        var match = await verify.ReconMatches.SingleAsync();
        Assert.Equal(100m, match.ReconciliationAmount);
        Assert.Equal(100m, match.GlAmount);
        Assert.Equal(103m, match.BillingAmount);
        Assert.Contains("billing minus ISS markup", match.ScoreBreakdown);
        Assert.Empty(await verify.ReconExceptions.ToListAsync());
    }

    [Theory]
    [InlineData(2, 2)] [InlineData(3, 2)] [InlineData(2, 3)]
    public async Task Withdrawal_pairs_repeated_calendar_dates_across_daily_runs(int processors, int glCount)
    {
        for (var i = 0; i < processors; i++) await AddOutstanding(_runA, _procA, EarlyDay);
        for (var i = 0; i < glCount; i++) await AddUnassignedGl(_runB, _glB, EarlyDay, reference: "GL" + i);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            foreach (var t in await db.StgInterchangeFees.ToListAsync())
            {
                t.TransactionCode = "04";
                t.TransactionWording = "ATM WITHDRAWAL";
            }
            foreach (var g in await db.StgGlEntries.ToListAsync()) g.TranParticular = "CASH/ATM";
            await db.SaveChangesAsync();
        }
        var result = await Service.BackMatchAsync(_containerId);
        Assert.Equal(Math.Min(processors, glCount), result.Added);
        Assert.Equal(0, (await Service.BackMatchAsync(_containerId)).Added);
        await using var verify = await Factory.CreateDbContextAsync();
        var matched = await verify.ReconMatches.Where(m => m.MatchStatus == MatchStatus.Matched).ToListAsync();
        Assert.Equal(matched.Count, matched.Select(m => m.GlStagingId).Distinct().Count());
        Assert.All(matched, m => Assert.True(m.MatchedAcrossPeriods));
        Assert.All(matched, m => Assert.Contains("time ignored", m.ScoreBreakdown));
        Assert.Equal(Math.Max(0, glCount - processors), await verify.ReconExceptions.CountAsync(e => e.ExceptionType == MatchStatus.UnmatchedGl));
        Assert.Equal(Math.Max(0, processors - glCount), await verify.ReconExceptions.CountAsync(e => e.ExceptionType == MatchStatus.UnmatchedProcessor));
    }

    [Theory]
    [InlineData("date")] [InlineData("amount")] [InlineData("currency")]
    [InlineData("leg")] [InlineData("channel")]
    public async Task Withdrawal_daily_rule_requires_exact_key_and_cash_GL(string mismatch)
    {
        await AddOutstanding(_runA, _procA, EarlyDay, reference: "TRX");
        await AddUnassignedGl(_runB, _glB, EarlyDay, reference: "OTHER");
        await using (var db = await Factory.CreateDbContextAsync())
        {
            (await db.StgInterchangeFees.SingleAsync()).TransactionCode = "04";
            var g = await db.StgGlEntries.SingleAsync();
            g.TranParticular = mismatch == "channel" ? "POS/TEST" : "CASH/ATM";
            if (mismatch == "date") g.ValueDate = EarlyDay.AddDays(1);
            if (mismatch == "amount") g.TranAmt = 100.01m;
            if (mismatch == "currency") g.TranCcyCode = "USD";
            if (mismatch == "leg") g.AcctNum = "9849077911";
            await db.SaveChangesAsync();
        }
        Assert.Equal(0, (await Service.BackMatchAsync(_containerId)).Added);
    }

    [Fact]
    public async Task Back_match_resolves_outstanding_across_runs_and_is_idempotent()
    {
        var reference = "REF123456789";
        var txId = await AddOutstanding(_runA, _procA, EarlyDay, reference: reference);   // early day, no GL in its own files
        var glId = await AddUnassignedGl(_runB, _glB, LateDay, reference: reference);      // settles 20 days later, in run B

        var result = await Service.BackMatchAsync(_containerId);

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Ambiguous);
        Assert.Equal(2, result.Changes.Count); // both runs touched
        Assert.All(result.Changes, c => Assert.Equal(1, c.Added));

        await using var db = await Factory.CreateDbContextAsync();
        var match = await db.ReconMatches.SingleAsync(x => x.ReconRunId == _runA && x.ProcessorStagingId == txId);
        Assert.Equal(MatchStatus.Matched, match.MatchStatus);
        Assert.Equal(glId, match.GlStagingId);
        Assert.True(match.MatchedAcrossPeriods);                 // GL came from run B
        Assert.Equal(_glB, match.GlSourceBatchId);
        // Both sides' exceptions cleared, in their own runs.
        Assert.False(await db.ReconExceptions.AnyAsync(e => e.ReconRunId == _runA && e.ProcessorStagingId == txId));
        Assert.False(await db.ReconExceptions.AnyAsync(e => e.ReconRunId == _runB && e.GlStagingId == glId));
        Assert.True(await db.ReconRunPasses.AnyAsync(p => p.ReconRunId == _runA && p.Kind == ReconPassKind.CrossPeriod));

        // Idempotent: nothing left outstanding to resolve.
        var again = await Service.BackMatchAsync(_containerId);
        Assert.Equal(0, again.Added);
    }

    [Fact]
    public async Task Spread_bound_leaves_distant_gl_outstanding()
    {
        var reference = "REF987654321";
        var txId = await AddOutstanding(_runA, _procA, EarlyDay, reference: reference);
        await AddUnassignedGl(_runB, _glB, EarlyDay.AddDays(21), reference: reference); // 21 days > spread 20

        var result = await Service.BackMatchAsync(_containerId);

        Assert.Equal(0, result.Added);
        await using var db = await Factory.CreateDbContextAsync();
        var match = await db.ReconMatches.SingleAsync(x => x.ReconRunId == _runA && x.ProcessorStagingId == txId);
        Assert.Equal(MatchStatus.UnmatchedProcessor, match.MatchStatus); // still outstanding, not matched, not an exception change
    }

    [Fact]
    public async Task Competing_gl_candidates_are_ambiguous()
    {
        // One outstanding txn, two GL rows within spread sharing amount/ccy/leg — unresolvable.
        var txId = await AddOutstanding(_runA, _procA, EarlyDay);        // blank references → agree with anything
        await AddUnassignedGl(_runB, _glB, EarlyDay.AddDays(2));
        await AddUnassignedGl(_runB, _glB, EarlyDay.AddDays(4));

        var result = await Service.BackMatchAsync(_containerId);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Ambiguous);
        await using var db = await Factory.CreateDbContextAsync();
        var match = await db.ReconMatches.SingleAsync(x => x.ReconRunId == _runA && x.ProcessorStagingId == txId);
        Assert.Equal(MatchStatus.Ambiguous, match.MatchStatus);
    }

    [Fact]
    public async Task Cross_month_load_resolves_prior_month_outstanding()
    {
        // April container with an outstanding transaction dated 28 Apr…
        int aprContainer, aprRun, aprProc;
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var container = new ReconPeriodRun { Function = ReconFunction.InterchangeToGl, PeriodKey = "2023-04", Status = ReconPeriodRunStatus.Open };
            var proc = new ImportBatch { ContentHash = "pap", Status = ImportStatus.Parsed, SourceType = SourceType.InterchangeFee };
            var gl = new ImportBatch { ContentHash = "gap", Status = ImportStatus.Parsed, SourceType = SourceType.GlDump, PeriodKey = "2023-04" };
            db.ReconPeriodRuns.Add(container); db.ImportBatches.AddRange(proc, gl);
            await db.SaveChangesAsync();
            aprContainer = container.ReconPeriodRunId; aprProc = proc.ImportBatchId;
            var run = new ReconRun { Function = ReconFunction.InterchangeToGl, ProcessorBatchId = proc.ImportBatchId, GlBatchId = gl.ImportBatchId, PeriodKey = "2023-04", PeriodStart = new(2023, 4, 28), PeriodEnd = new(2023, 4, 28), Status = ReconRunStatus.Completed, ParentPeriodRunId = aprContainer, PeriodGranularity = PeriodGranularity.Day };
            db.ReconRuns.Add(run); await db.SaveChangesAsync();
            aprRun = run.ReconRunId;
        }
        var reference = "REFCROSS0502";
        var txId = await AddOutstanding(aprRun, aprProc, new DateOnly(2023, 4, 28), reference: reference);
        // …settles in GL loaded under the May container, 4 days later (within spread).
        var glId = await AddUnassignedGl(_runB, _glB, new DateOnly(2023, 5, 2), reference: reference);

        // Running the May container's back-match reaches back across the month boundary.
        var result = await Service.BackMatchAsync(_containerId);

        Assert.Equal(1, result.Added);
        await using var verify = await Factory.CreateDbContextAsync();
        var match = await verify.ReconMatches.SingleAsync(x => x.ReconRunId == aprRun && x.ProcessorStagingId == txId);
        Assert.Equal(MatchStatus.Matched, match.MatchStatus);
        Assert.Equal(glId, match.GlStagingId);
        Assert.True(match.MatchedAcrossPeriods);
        Assert.False(await verify.ReconExceptions.AnyAsync(e => e.ReconRunId == aprRun && e.ProcessorStagingId == txId));
        Assert.False(await verify.ReconExceptions.AnyAsync(e => e.ReconRunId == _runB && e.GlStagingId == glId));
        Assert.Contains(result.Changes, c => c.RunId == aprRun); // April run shows in the change table
    }

    [Fact]
    public async Task Rollup_consolidates_children_into_a_month_batch()
    {
        // One cross-run match plus one unresolved outstanding item.
        var reference = "REFROLLUP001";
        await AddOutstanding(_runA, _procA, EarlyDay, reference: reference);
        await AddUnassignedGl(_runB, _glB, LateDay, reference: reference);
        var leftoverTx = await AddOutstanding(_runA, _procA, EarlyDay, amount: 500);
        await Service.BackMatchAsync(_containerId);

        var result = await Service.RollUpAsync(_containerId);
        var monthRunId = result.MonthBatchRunId;

        Assert.NotEqual(_runA, monthRunId);
        Assert.NotEqual(_runB, monthRunId);
        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Exceptions); // the 500 leftover

        await using var db = await Factory.CreateDbContextAsync();
        var container = await db.ReconPeriodRuns.SingleAsync(c => c.ReconPeriodRunId == _containerId);
        Assert.Equal(ReconPeriodRunStatus.RolledUp, container.Status);
        Assert.Equal(monthRunId, container.RolledUpRunId);
        // Children archived; nothing left pointing at them.
        Assert.True(await db.ReconRuns.Where(r => r.ReconRunId == _runA || r.ReconRunId == _runB).AllAsync(r => r.IsArchived));
        Assert.False(await db.ReconMatches.AnyAsync(m => m.ReconRunId == _runA || m.ReconRunId == _runB));
        Assert.False(await db.ReconExceptions.AnyAsync(e => e.ReconRunId == _runA || e.ReconRunId == _runB));
        // The month batch owns the consolidated state and reads as a standalone month run.
        var monthRun = await db.ReconRuns.SingleAsync(r => r.ReconRunId == monthRunId);
        Assert.Null(monthRun.ParentPeriodRunId);
        Assert.Equal(PeriodGranularity.Month, monthRun.PeriodGranularity);
        Assert.Equal(1, monthRun.MatchedCount);
        Assert.True(await db.ReconExceptions.AnyAsync(e => e.ReconRunId == monthRunId && e.ProcessorStagingId == leftoverTx));

        // Already rolled up — a second attempt is rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.RollUpAsync(_containerId));
    }

    [Fact]
    public async Task Orphan_sweep_resolves_reference_match_beyond_spread_and_ignores_refless()
    {
        // Two standalone month batches, six months apart (far beyond the 20-day spread).
        int runOA, procOA, runOB, glB;
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var pA = new ImportBatch { ContentHash = "opa", Status = ImportStatus.Parsed, SourceType = SourceType.InterchangeFee };
            var gA = new ImportBatch { ContentHash = "oga", Status = ImportStatus.Parsed, SourceType = SourceType.GlDump, PeriodKey = "2023-01" };
            var pB = new ImportBatch { ContentHash = "opb", Status = ImportStatus.Parsed, SourceType = SourceType.InterchangeFee };
            var gB = new ImportBatch { ContentHash = "ogb", Status = ImportStatus.Parsed, SourceType = SourceType.GlDump, PeriodKey = "2023-06" };
            db.ImportBatches.AddRange(pA, gA, pB, gB); await db.SaveChangesAsync();
            procOA = pA.ImportBatchId; glB = gB.ImportBatchId;
            var rA = new ReconRun { Function = ReconFunction.InterchangeToGl, ProcessorBatchId = pA.ImportBatchId, GlBatchId = gA.ImportBatchId, PeriodKey = "2023-01", PeriodStart = new(2023, 1, 1), PeriodEnd = new(2023, 1, 31), Status = ReconRunStatus.Completed, PeriodGranularity = PeriodGranularity.Month };
            var rB = new ReconRun { Function = ReconFunction.InterchangeToGl, ProcessorBatchId = pB.ImportBatchId, GlBatchId = gB.ImportBatchId, PeriodKey = "2023-06", PeriodStart = new(2023, 6, 1), PeriodEnd = new(2023, 6, 30), Status = ReconRunStatus.Completed, PeriodGranularity = PeriodGranularity.Month };
            db.ReconRuns.AddRange(rA, rB); await db.SaveChangesAsync();
            runOA = rA.ReconRunId; runOB = rB.ReconRunId;
        }
        // A reference-anchored pair 166 days apart — only the orphan sweep can match it.
        var refTx = await AddOutstanding(runOA, procOA, new DateOnly(2023, 1, 5), reference: "ORPHANREF01");
        await AddUnassignedGl(runOB, glB, new DateOnly(2023, 6, 20), reference: "ORPHANREF01");
        // A reference-less pair of equal amount — must NOT be matched without a date window.
        var reflessTx = await AddOutstanding(runOA, procOA, new DateOnly(2023, 1, 6), amount: 777, reference: "");
        await AddUnassignedGl(runOB, glB, new DateOnly(2023, 1, 7), amount: 777, reference: null);

        var result = await Service.OrphanSweepAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(2, result.BatchesInScope);
        await using var db2 = await Factory.CreateDbContextAsync();
        Assert.Equal(MatchStatus.Matched, (await db2.ReconMatches.SingleAsync(m => m.ReconRunId == runOA && m.ProcessorStagingId == refTx)).MatchStatus);
        Assert.Equal(MatchStatus.UnmatchedProcessor, (await db2.ReconMatches.SingleAsync(m => m.ReconRunId == runOA && m.ProcessorStagingId == reflessTx)).MatchStatus);
        // Idempotent.
        Assert.Equal(0, (await Service.OrphanSweepAsync()).Added);
    }

    public async Task DisposeAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        await _sp.DisposeAsync();
    }

    private sealed class TestAuth : AuthenticationStateProvider
    {
        public ClaimsPrincipal User = new(new ClaimsIdentity());
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
    }
}
