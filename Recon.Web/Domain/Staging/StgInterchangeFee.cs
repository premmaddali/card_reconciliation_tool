namespace Recon.Web.Domain.Staging;

/// <summary>
/// Normalized row from the processor INTERCHANGE_FEE report (credit + debit) — the superset of
/// all card transactions. <see cref="ProductType"/> (DC/CR) separates debit from credit.
/// </summary>
public class StgInterchangeFee
{
    public int StgInterchangeFeeId { get; set; }
    public int ImportBatchId { get; set; }
    public int SourceRowNumber { get; set; }

    public string? MicrofilmRefNumber { get; set; }
    public int? MicrofilmRefSeq { get; set; }

    /// <summary>12-digit transaction join key.</summary>
    public string? ReferenceNumber { get; set; }
    public string? CardNumberHashed { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductType { get; set; }
    public string? ClientCode { get; set; }
    public string? CardAccountNumber { get; set; }
    public DateTime? TransactionDate { get; set; }
    public DateOnly? ProcessingDate { get; set; }
    public string? TransactionCode { get; set; }
    public string? TransactionWording { get; set; }

    public decimal? TxnAmount { get; set; }
    public int? TxnCcy { get; set; }
    public decimal? BillingAmount { get; set; }
    public int? BillingCcy { get; set; }
    public decimal? SettlementAmount { get; set; }
    public int? SettlementCcy { get; set; }

    public string? InterchangeFeesRaw { get; set; }
    public decimal? InterchangeFees { get; set; }
    public int? IntFeesCcy { get; set; }

    public string? OrigineCode { get; set; }
    public string? TerminalId { get; set; }
    public string? MerchantAcronym { get; set; }
    public decimal? IssMarkupAmount { get; set; }
    public decimal? ConversionFees { get; set; }
}
