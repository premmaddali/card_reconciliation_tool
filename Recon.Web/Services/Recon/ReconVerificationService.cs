using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;

namespace Recon.Web.Services.Recon;

public interface IReconVerificationService
{
    Task SubmitAsync(int runId, CancellationToken ct = default);
    Task VerifyAsync(int runId, CancellationToken ct = default);
}

/// <summary>Runs the configured, role-based verification flow and preserves a role snapshot per submission.</summary>
public class ReconVerificationService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUserService currentUser) : IReconVerificationService
{
    public async Task SubmitAsync(int runId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.ReconRuns.SingleOrDefaultAsync(r => r.ReconRunId == runId, ct)
            ?? throw new InvalidOperationException("Reconciliation batch was not found.");
        if (run.AnalysisOfRunId is not null) throw new InvalidOperationException("Analysis results cannot be submitted for operational verification.");
        if (run.Status != ReconRunStatus.Completed) throw new InvalidOperationException("Only completed reconciliations can be submitted for verification.");
        if (run.VerificationStatus != ReconVerificationStatus.NotSubmitted) throw new InvalidOperationException("This reconciliation has already been submitted.");

        var workflow = await db.ReconWorkflowDefinitions
            .Include(w => w.Steps).ThenInclude(s => s.Roles).ThenInclude(r => r.AppAccess)
            .Where(w => w.IsActive).OrderByDescending(w => w.IsDefault).ThenBy(w => w.ReconWorkflowDefinitionId)
            .FirstOrDefaultAsync(ct) ?? throw new InvalidOperationException("No active verification workflow is configured.");
        var steps = workflow.Steps.OrderBy(s => s.StepOrder).ToList();
        if (steps.Count < 2 || steps.Any(s => s.Roles.Count == 0))
            throw new InvalidOperationException("The active workflow needs a Maker step and at least one verification step, each with a role.");

        var maker = steps[0];
        var userRoleIds = await db.UserAppAccesses.Where(x => x.UserId == currentUser.UserId && x.IsActive)
            .Select(x => x.AppAccessId).ToListAsync(ct);
        var makerRole = maker.Roles.FirstOrDefault(r => userRoleIds.Contains(r.AppAccessId))
            ?? throw new InvalidOperationException("Your role is not assigned to the Maker step of this workflow.");
        var firstVerification = steps[1];
        foreach (var step in steps)
            foreach (var role in step.Roles)
                db.ReconWorkflowActors.Add(new ReconWorkflowActor
                {
                    ReconRunId = runId, ReconWorkflowStepId = step.ReconWorkflowStepId, AppAccessId = role.AppAccessId,
                    Status = step.ReconWorkflowStepId == maker.ReconWorkflowStepId ? ReconWorkflowActorStatus.Completed
                        : step.ReconWorkflowStepId == firstVerification.ReconWorkflowStepId ? ReconWorkflowActorStatus.AwaitingAction : ReconWorkflowActorStatus.Future
                });

        run.WorkflowDefinitionId = workflow.ReconWorkflowDefinitionId;
        SetCurrentStep(run, firstVerification);
        run.VerificationStatus = ReconVerificationStatus.AwaitingVerification;
        db.ReconWorkflowHistories.Add(new ReconWorkflowHistory
        {
            ReconRunId = runId, ReconWorkflowStepId = maker.ReconWorkflowStepId, AppAccessId = makerRole.AppAccessId, Action = ReconWorkflowAction.Submitted,
            ActionedByUserId = currentUser.UserId, ActionedDate = DateTime.UtcNow,
            Comment = $"Submitted by Maker; awaiting {firstVerification.Label}."
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task VerifyAsync(int runId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.ReconRuns.SingleOrDefaultAsync(r => r.ReconRunId == runId, ct)
            ?? throw new InvalidOperationException("Reconciliation batch was not found.");
        if (run.AnalysisOfRunId is not null) throw new InvalidOperationException("Analysis results cannot be operationally verified.");
        if (run.VerificationStatus != ReconVerificationStatus.AwaitingVerification || run.CurrentWorkflowStepId is null)
            throw new InvalidOperationException("This reconciliation is not awaiting verification.");

        var userRoleIds = await db.UserAppAccesses.Where(x => x.UserId == currentUser.UserId && x.IsActive)
            .Select(x => x.AppAccessId).ToListAsync(ct);
        var actor = await db.ReconWorkflowActors.FirstOrDefaultAsync(a => a.ReconRunId == runId &&
            a.ReconWorkflowStepId == run.CurrentWorkflowStepId && a.Status == ReconWorkflowActorStatus.AwaitingAction && userRoleIds.Contains(a.AppAccessId), ct);
        if (actor is null) throw new InvalidOperationException("Your role is not assigned to the current verification step.");

        var currentStep = await db.ReconWorkflowSteps.Include(s => s.Roles).ThenInclude(r => r.AppAccess)
            .SingleAsync(s => s.ReconWorkflowStepId == run.CurrentWorkflowStepId, ct);
        var allCurrentActors = await db.ReconWorkflowActors.Where(a => a.ReconRunId == runId && a.ReconWorkflowStepId == currentStep.ReconWorkflowStepId).ToListAsync(ct);
        foreach (var current in allCurrentActors) current.Status = ReconWorkflowActorStatus.Completed;

        db.ReconWorkflowHistories.Add(new ReconWorkflowHistory
        {
            ReconRunId = runId, ReconWorkflowStepId = currentStep.ReconWorkflowStepId, AppAccessId = actor.AppAccessId,
            Action = ReconWorkflowAction.Verified, ActionedByUserId = currentUser.UserId, ActionedDate = DateTime.UtcNow,
            Comment = $"Verified at {currentStep.Label}."
        });

        var next = await db.ReconWorkflowSteps.Include(s => s.Roles).ThenInclude(r => r.AppAccess)
            .Where(s => s.ReconWorkflowDefinitionId == run.WorkflowDefinitionId && s.StepOrder > currentStep.StepOrder)
            .OrderBy(s => s.StepOrder).FirstOrDefaultAsync(ct);
        if (next is null)
        {
            run.CurrentWorkflowStepId = null;
            run.NextAction = "Verification complete";
            run.ResponsibleActor = null;
            run.VerificationStatus = ReconVerificationStatus.Verified;
        }
        else
        {
            var nextActors = await db.ReconWorkflowActors.Where(a => a.ReconRunId == runId && a.ReconWorkflowStepId == next.ReconWorkflowStepId).ToListAsync(ct);
            foreach (var nextActor in nextActors) nextActor.Status = ReconWorkflowActorStatus.AwaitingAction;
            SetCurrentStep(run, next);
        }
        await db.SaveChangesAsync(ct);
    }

    private static void SetCurrentStep(ReconRun run, ReconWorkflowStep step)
    {
        run.CurrentWorkflowStepId = step.ReconWorkflowStepId;
        run.NextAction = step.Label;
        run.ResponsibleActor = string.Join(", ", step.Roles.Select(r => r.AppAccess.AccessName).OrderBy(x => x));
    }
}
