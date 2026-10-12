using Recon.Web.Domain.Common;
using Recon.Web.Domain.Import;

namespace Recon.Web.Domain.Recon;

/// <summary>The refinement passes that can be applied to a completed reconciliation batch.</summary>
public enum ReconPassKind : byte
{
    /// <summary>Secondary date + billing amount + currency matching, applied in place.</summary>
    SecondaryAmount = 1,
    /// <summary>Card-number + date + amount + currency matching, applied in place.</summary>
    CardNumber = 2,
    /// <summary>Uses this batch's data to reconcile adjacent (previous/next) batches' leftovers.</summary>
    CrossPeriod = 3
}

/// <summary>
/// Records one refinement pass run against a batch. For in-place passes (<see cref="ReconPassKind.SecondaryAmount"/>,
/// <see cref="ReconPassKind.CardNumber"/>) a single row marks the pass processed and stores how many it added.
/// For <see cref="ReconPassKind.CrossPeriod"/> one row is written per neighbour batch that was updated, so the
/// set of rows sharing a <see cref="RunGroup"/> is the "what changed in previous/next batches" table.
/// </summary>
public class ReconRunPass : IAuditable
{
    public int ReconRunPassId { get; set; }

    /// <summary>The batch the pass was launched from.</summary>
    public int ReconRunId { get; set; }
    public ReconRun ReconRun { get; set; } = null!;

    public ReconPassKind Kind { get; set; }
    /// <summary>Groups the rows produced by a single click (one GUID per invocation).</summary>
    public Guid RunGroup { get; set; }

    /// <summary>Records newly matched this pass (in the target batch for cross-period rows).</summary>
    public int Added { get; set; }
    public int Ambiguous { get; set; }

    // Cross-period target (null for in-place passes).
    public int? TargetRunId { get; set; }
    public string? TargetPeriodKey { get; set; }

    public string? Detail { get; set; }
    public DateTime RunAt { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
