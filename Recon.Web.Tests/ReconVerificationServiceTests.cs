using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Recon.Web.Data;
using Recon.Web.Domain.Identity;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Services;
using Recon.Web.Services.Recon;
using Xunit;

namespace Recon.Web.Tests;

public class ReconVerificationServiceTests : IAsyncLifetime
{
    private readonly Guid _makerUserId = Guid.NewGuid();
    private readonly Guid _checkerUserId = Guid.NewGuid();
    private readonly string _databaseName = "Recon_Verification_" + Guid.NewGuid().ToString("N")[..8];
    private ServiceProvider _services = null!;
    private TestCurrentUser _currentUser = null!;

    public async Task InitializeAsync()
    {
        var connection = $"Server=localhost;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";
        _currentUser = new TestCurrentUser { UserId = _makerUserId };
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(_currentUser);
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContextFactory<AppDbContext>((provider, options) =>
        {
            options.UseSqlServer(connection);
            options.AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>());
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddScoped<IReconVerificationService, ReconVerificationService>();
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
    public async Task Maker_submission_requires_assigned_checker_and_records_the_complete_audit_trail()
    {
        var runId = await SeedCompletedRunAndWorkflowAsync();

        await VerificationService.SubmitAsync(runId);

        await using (var context = await Factory.CreateDbContextAsync())
        {
            var run = await context.ReconRuns.SingleAsync(run => run.ReconRunId == runId);
            Assert.Equal(ReconVerificationStatus.AwaitingVerification, run.VerificationStatus);
            Assert.Equal("Checker review", run.NextAction);
            Assert.Equal("Checker", run.ResponsibleActor);
            Assert.Equal(1, await context.ReconWorkflowActors.CountAsync(actor => actor.ReconRunId == runId && actor.Status == ReconWorkflowActorStatus.Completed));
            Assert.Equal(1, await context.ReconWorkflowActors.CountAsync(actor => actor.ReconRunId == runId && actor.Status == ReconWorkflowActorStatus.AwaitingAction));
            Assert.Equal(ReconWorkflowAction.Submitted, await context.ReconWorkflowHistories.SingleAsync(history => history.ReconRunId == runId).ContinueWith(task => task.Result.Action));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => VerificationService.VerifyAsync(runId));

        _currentUser.UserId = _checkerUserId;
        await VerificationService.VerifyAsync(runId);

        await using (var context = await Factory.CreateDbContextAsync())
        {
            var run = await context.ReconRuns.SingleAsync(run => run.ReconRunId == runId);
            Assert.Equal(ReconVerificationStatus.Verified, run.VerificationStatus);
            Assert.Null(run.CurrentWorkflowStepId);
            Assert.Equal("Verification complete", run.NextAction);
            Assert.Null(run.ResponsibleActor);
            Assert.Equal(2, await context.ReconWorkflowActors.CountAsync(actor => actor.ReconRunId == runId && actor.Status == ReconWorkflowActorStatus.Completed));
            Assert.Equal(new[] { ReconWorkflowAction.Submitted, ReconWorkflowAction.Verified }, await context.ReconWorkflowHistories
                .Where(history => history.ReconRunId == runId)
                .OrderBy(history => history.ReconWorkflowHistoryId)
                .Select(history => history.Action)
                .ToArrayAsync());
        }
    }

    private IDbContextFactory<AppDbContext> Factory => _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    private IReconVerificationService VerificationService => _services.GetRequiredService<IReconVerificationService>();

    private async Task<int> SeedCompletedRunAndWorkflowAsync()
    {
        await using var context = await Factory.CreateDbContextAsync();
        var makerAccess = new AppAccess { AccessName = "Maker" };
        var checkerAccess = new AppAccess { AccessName = "Checker" };
        context.AppAccesses.AddRange(makerAccess, checkerAccess);
        context.Users.AddRange(
            new AppUser { Id = _makerUserId, UserName = "maker", FullName = "Maker User" },
            new AppUser { Id = _checkerUserId, UserName = "checker", FullName = "Checker User" });
        await context.SaveChangesAsync();

        context.UserAppAccesses.AddRange(
            new UserAppAccess { UserId = _makerUserId, AppAccessId = makerAccess.AppAccessId },
            new UserAppAccess { UserId = _checkerUserId, AppAccessId = checkerAccess.AppAccessId });
        var workflow = new ReconWorkflowDefinition { Name = "Test maker checker", IsDefault = true };
        var makerStep = new ReconWorkflowStep { StepOrder = 1, Label = "Maker submission" };
        makerStep.Roles.Add(new ReconWorkflowStepRole { AppAccessId = makerAccess.AppAccessId });
        var checkerStep = new ReconWorkflowStep { StepOrder = 2, Label = "Checker review" };
        checkerStep.Roles.Add(new ReconWorkflowStepRole { AppAccessId = checkerAccess.AppAccessId });
        workflow.Steps.Add(makerStep);
        workflow.Steps.Add(checkerStep);
        context.ReconWorkflowDefinitions.Add(workflow);

        var processorBatch = new ImportBatch { SourceType = SourceType.PosSettlement, FileName = "processor.xlsx", ContentHash = Guid.NewGuid().ToString("N"), PeriodKey = "2026-03", Status = ImportStatus.Parsed };
        var glBatch = new ImportBatch { SourceType = SourceType.GlDump, FileName = "gl.xlsx", ContentHash = Guid.NewGuid().ToString("N"), PeriodKey = "2026-03", Status = ImportStatus.Parsed };
        context.ImportBatches.AddRange(processorBatch, glBatch);
        await context.SaveChangesAsync();

        var run = new ReconRun
        {
            Function = ReconFunction.PosToGl,
            PeriodKey = "2026-03",
            Status = ReconRunStatus.Completed,
            ProcessorBatchId = processorBatch.ImportBatchId,
            GlBatchId = glBatch.ImportBatchId,
            CompletedDate = DateTime.UtcNow
        };
        context.ReconRuns.Add(run);
        await context.SaveChangesAsync();
        return run.ReconRunId;
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; set; }
    }
}