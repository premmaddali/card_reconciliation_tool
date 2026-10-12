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
    /// <summary>Credit-card processor key from MICROFILM REF NUMBER. Never substitutes the debit reference.</summary>
    public string? MicrofilmRefNumber { get; set; }
    public string? ProcessorAccountNumber { get; set; }
    /// <summary>Interchange product type, e.g. DC (debit) or CR (credit).</summary>
    public string? ProcessorProductType { get; set; }
    /// <summary>Merchant name/acronym supplied by the processor source.</summary>
    public string? ProcessorMerchant { get; set; }
    /// <summary>Processor transaction narrative/wording.</summary>
    public string? ProcessorDescription { get; set; }
    public string? GlAccountNumber { get; set; }
    /// <summary>GL-side match key: debit particular as-is; credit particular after its twelve-character posting prefix.</summary>
    public string? GlReference { get; set; }
    /// <summary>Raw GL TRAN_PARTICULAR_2 retained for analyst review, before any credit-prefix trimming.</summary>
    public string? GlParticular2 { get; set; }
    /// <summary>GL transaction narrative and any supplementary remarks.</summary>
    public string? GlDescription { get; set; }
    public ReconSettlementLeg SettlementLeg { get; set; }
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
    /// <summary>Original processor transaction timestamp where the source report provides it.</summary>
    public DateTime? ProcessorTransactionDateTime { get; set; }

    // GL side (null when unmatched)
    public int? GlSourceBatchId { get; set; }
    public bool MatchedAcrossPeriods { get; set; }
    public int? GlStagingId { get; set; }
    public decimal? GlAmount { get; set; }
    /// <summary>Actual posting currency of the GL amount (the cardholder settlement GL is posted in MUR).</summary>
    public int? GlCcy { get; set; }
    public DateOnly? GlDate { get; set; }
    public DateOnly? GlValueDate { get; set; }
    /// <summary>GL entry/verification timestamp where the GL export provides it.</summary>
    public DateTime? GlPostingDateTime { get; set; }

    /// <summary>ISO numeric currency (480 MUR / 840 USD / 978 EUR).</summary>
    public int? Ccy { get; set; }
}
