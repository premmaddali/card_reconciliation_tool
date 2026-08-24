using Recon.Web.Domain.Common;
using Recon.Web.Domain.Identity;

namespace Recon.Web.Domain.Recon;

public class ReconWorkflowDefinition : IAuditable, IActivatable
{
    public int ReconWorkflowDefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<ReconWorkflowStep> Steps { get; set; } = [];
    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}

public class ReconWorkflowStep
{
    public int ReconWorkflowStepId { get; set; }
    public int ReconWorkflowDefinitionId { get; set; }
    public ReconWorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public int StepOrder { get; set; }
    public string Label { get; set; } = string.Empty;
    public ICollection<ReconWorkflowStepRole> Roles { get; set; } = [];
}

/// <summary>Role(s) eligible to act at a configured workflow step.</summary>
public class ReconWorkflowStepRole
{
    public int ReconWorkflowStepRoleId { get; set; }
    public int ReconWorkflowStepId { get; set; }
    public ReconWorkflowStep WorkflowStep { get; set; } = null!;
    public int AppAccessId { get; set; }
    public AppAccess AppAccess { get; set; } = null!;
}

public enum ReconWorkflowActorStatus : byte { Future = 0, AwaitingAction = 1, Completed = 2 }

/// <summary>Role snapshot created for every step when a reconciliation is submitted.</summary>
public class ReconWorkflowActor
{
    public int ReconWorkflowActorId { get; set; }
    public int ReconRunId { get; set; }
    public int ReconWorkflowStepId { get; set; }
    public int AppAccessId { get; set; }
    public ReconWorkflowActorStatus Status { get; set; }
}

public enum ReconWorkflowAction : byte { Submitted = 1, Verified = 2 }

/// <summary>Immutable maker/checker audit trail.</summary>
public class ReconWorkflowHistory
{
    public int ReconWorkflowHistoryId { get; set; }
    public int ReconRunId { get; set; }
    public int? ReconWorkflowStepId { get; set; }
    public int? AppAccessId { get; set; }
    public ReconWorkflowAction Action { get; set; }
    public Guid ActionedByUserId { get; set; }
    public DateTime ActionedDate { get; set; }
    public string? Comment { get; set; }
}
