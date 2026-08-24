using Recon.Web.Domain.Import;

namespace Recon.Web.Domain.Recon;

/// <summary>
/// Roll-up of a run by currency at two grains: daily (<see cref="Day"/> set) and
/// monthly (<see cref="Day"/> null). Frozen when the run completes.
/// </summary>
public class ReconPassSummary
{
    public int ReconPassSummaryId { get; set; }
    public int ReconRunId { get; set; }
    public ReconRun ReconRun { get; set; } = null!;

    public int Ccy { get; set; }
    public ReconSettlementLeg SettlementLeg { get; set; }
    /// <summary>Null = monthly roll-up for the run; set = that day's roll-up.</summary>
    public DateOnly? Day { get; set; }

    public int MatchedCount { get; set; }
    public decimal MatchedAmount { get; set; }
    public int ExceptionCount { get; set; }
    public decimal ExceptionAmount { get; set; }
    public decimal AverageConfidence { get; set; }
}
