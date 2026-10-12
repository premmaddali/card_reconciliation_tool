using Recon.Web.Domain.Common;
using Recon.Web.Domain.Import;

namespace Recon.Web.Domain.Recon;

/// <summary>Lifecycle of a daily/period month container.</summary>
public enum ReconPeriodRunStatus : byte
{
    /// <summary>Accepting new date-range runs; results are still accumulating.</summary>
    Open = 0,
    /// <summary>Consolidated into a canonical month batch; children are read-only history.</summary>
    RolledUp = 1
}

/// <summary>
/// A month container for daily/period reconciliation: it groups the date-range
/// <see cref="ReconRun"/> children loaded beneath it (one day, two days, a whole month) for one
/// scheme + function + calendar month. It holds no files of its own — each child run imports its own
/// data and matches against the cumulative, spread-bounded pool. The month boundary is a
/// consolidation/reporting unit, not a matching boundary (see DAILY_RECON_DESIGN.md).
/// </summary>
public class ReconPeriodRun : IAuditable
{
    public int ReconPeriodRunId { get; set; }
    public ReconFunction Function { get; set; }
    /// <summary>Calendar month this container consolidates, as <c>yyyy-MM</c>.</summary>
    public string PeriodKey { get; set; } = string.Empty;
    /// <summary>Scheme this container belongs to (null = legacy/unassigned Mastercard data).</summary>
    public int? SchemeProfileId { get; set; }
    public ReconPeriodRunStatus Status { get; set; } = ReconPeriodRunStatus.Open;
    /// <summary>The consolidated month batch produced at rollup; null until then.</summary>
    public int? RolledUpRunId { get; set; }
    public DateTime? RolledUpDate { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
