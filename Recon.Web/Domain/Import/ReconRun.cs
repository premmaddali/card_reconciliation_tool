using Recon.Web.Domain.Common;

namespace Recon.Web.Domain.Import;

/// <summary>The independent MasterCard reconciliation functions (each a processor file → GL, or T464 network → ATM).</summary>
public enum ReconFunction : byte
{
    AtmToGl = 1,
    PosToGl = 2,
    InterchangeToGl = 3,
    NetworkToAtm = 4
}

public enum ReconRunStatus : byte
{
    Draft = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>Business approval state, independent from the technical reconciliation run state.</summary>
public enum ReconVerificationStatus : byte
{
    NotSubmitted = 0,
    AwaitingVerification = 1,
    Verified = 2
}

/// <summary>
/// One reconciliation execution: a processor batch matched against a GL batch for a period,
/// under one <see cref="ReconFunction"/>. Owns the produced matches, exceptions and summaries.
/// </summary>
public class ReconRun : IAuditable
{
    public int ReconRunId { get; set; }
    public ReconFunction Function { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    /// <summary>The batch's reconciliation date range (duplicate detection is per function + range).</summary>
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public ReconRunStatus Status { get; set; } = ReconRunStatus.Draft;

    /// <summary>The processor-side batch (ATM, POS or Interchange, per <see cref="Function"/>).</summary>
    public int ProcessorBatchId { get; set; }
    public ImportBatch? ProcessorBatch { get; set; }
    /// <summary>The GL batch; null for Network ↔ ATM runs, which have no GL side.</summary>
    public int? GlBatchId { get; set; }
    public ImportBatch? GlBatch { get; set; }
    /// <summary>The T464 network batch for Network ↔ ATM runs; null otherwise.</summary>
    public int? NetworkBatchId { get; set; }
    public ImportBatch? NetworkBatch { get; set; }
    /// <summary>Scheme this run belongs to (null = legacy/unassigned Mastercard data).</summary>
    public int? SchemeProfileId { get; set; }

    public DateTime? StartedDate { get; set; }
    public DateTime? CompletedDate { get; set; }

    // Headline results (also broken down in ReconPassSummary).
    public int ProcessorCount { get; set; }
    public int MatchedCount { get; set; }
    public int ExceptionCount { get; set; }
    public decimal AverageConfidence { get; set; }
    /// <summary>Whether this run accepted small amount differences, plus the frozen applied value.</summary>
    public bool ApplyAmountTolerance { get; set; }
    public decimal? AmountTolerance { get; set; }
    public bool IsArchived { get; set; }

    // Configurable maker/checker workflow state.  The responsible actor is a readable snapshot
    // because a step may be assigned to more than one business role.
    public int? WorkflowDefinitionId { get; set; }
    public int? CurrentWorkflowStepId { get; set; }
    public ReconVerificationStatus VerificationStatus { get; set; } = ReconVerificationStatus.NotSubmitted;
    public string? NextAction { get; set; }
    public string? ResponsibleActor { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
