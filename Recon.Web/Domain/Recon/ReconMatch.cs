using Recon.Web.Domain.Import;

namespace Recon.Web.Domain.Recon;

/// <summary>
/// One processor transaction and the GL cardholder-settlement row it matched to (if any),
/// with the weighted confidence score that produced the outcome.
/// </summary>
public class ReconMatch
{
    public int ReconMatchId { get; set; }
    public int ReconRunId { get; set; }
    public ReconRun ReconRun { get; set; } = null!;

    public string? ReferenceNumber { get; set; }
    public string? ProcessorAccountNumber { get; set; }
    public string? GlAccountNumber { get; set; }
    public MatchStatus MatchStatus { get; set; }
    /// <summary>0–100 weighted score of the chosen candidate.</summary>
    public decimal Confidence { get; set; }
    /// <summary>Human-readable weight breakdown, e.g. "amount 40 + date 25 + ref 15 = 80".</summary>
    public string? ScoreBreakdown { get; set; }
    /// <summary>Reason recorded when a reviewer manually accepts a non-exact match.</summary>
    public string? MatchComment { get; set; }
    public bool IsManuallyMatched { get; set; }
    public DateTime? ManuallyMatchedDate { get; set; }

    // Processor side
    public int ProcessorStagingId { get; set; }
    public StagingSource ProcessorSource { get; set; }
    /// <summary>Original amount as reported by the processor in its transaction currency.</summary>
    public decimal? ProcessorAmount { get; set; }
    /// <summary>ISO numeric currency of <see cref="ProcessorAmount"/>.</summary>
    public int? ProcessorCcy { get; set; }
    public decimal? BillingAmount { get; set; }
    public int? BillingCcy { get; set; }
    public decimal? SettlementAmount { get; set; }
    public int? SettlementCcy { get; set; }
    /// <summary>
    /// Amount compared to the GL. For foreign transactions this can be the processor settlement or
    /// billing amount rather than <see cref="ProcessorAmount"/>.
    /// </summary>
    public decimal? ReconciliationAmount { get; set; }
    public DateOnly? ProcessorDate { get; set; }

    // GL side (null when unmatched)
    public int? GlStagingId { get; set; }
    public decimal? GlAmount { get; set; }
    /// <summary>Actual posting currency of the GL amount (the cardholder settlement GL is posted in MUR).</summary>
    public int? GlCcy { get; set; }
    public DateOnly? GlDate { get; set; }

    /// <summary>ISO numeric currency (480 MUR / 840 USD / 978 EUR).</summary>
    public int? Ccy { get; set; }
}
