using Recon.Web.Domain.Common;

namespace Recon.Web.Domain.Import;

/// <summary>The three independent MasterCard reconciliation functions (each a processor file → GL).</summary>
public enum ReconFunction : byte
{
    AtmToGl = 1,
    PosToGl = 2,
    InterchangeToGl = 3
}

public enum ReconRunStatus : byte
{
    Draft = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
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
    public int GlBatchId { get; set; }
    public ImportBatch? GlBatch { get; set; }

    public DateTime? StartedDate { get; set; }
    public DateTime? CompletedDate { get; set; }

    // Headline results (also broken down in ReconPassSummary).
    public int ProcessorCount { get; set; }
    public int MatchedCount { get; set; }
    public int ExceptionCount { get; set; }
    public decimal AverageConfidence { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
