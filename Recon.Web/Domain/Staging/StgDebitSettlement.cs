namespace Recon.Web.Domain.Staging;

/// <summary>Which processor debit settlement file a row came from.</summary>
public enum SettlementChannel : byte
{
    Atm = 1,
    Pos = 2
}

/// <summary>
/// Normalized row from a processor DEBIT_SETTLEMENT_REPORT. The ATM and POS exports share an
/// identical 22-column schema, so both load here distinguished by <see cref="Channel"/>.
/// </summary>
public class StgDebitSettlement
{
    public int StgDebitSettlementId { get; set; }
    public int ImportBatchId { get; set; }
    public int SourceRowNumber { get; set; }
    public SettlementChannel Channel { get; set; }

    public DateOnly? ExtractedDate { get; set; }
    public string? MerchantAcronym { get; set; }
    public string? ProcessingCode { get; set; }

    /// <summary>12-digit transaction join key.</summary>
    public string? ReferenceNumber { get; set; }
    public string? CardNumberHashed { get; set; }
    public string? CardNumber { get; set; }
    public string? Wording { get; set; }
    public string? ProductType { get; set; }
    public string? TransactionSign { get; set; }
    public string? TransactionWording { get; set; }
    public DateOnly? TransactionDate { get; set; }
    public DateOnly? ProcessingDate { get; set; }

    public string? TxnAmountRaw { get; set; }
    public decimal? TxnAmount { get; set; }
    public int? TxnCcy { get; set; }
    public string? TxnCcyCode { get; set; }

    public string? InterchangeFeesRaw { get; set; }
    public decimal? InterchangeFees { get; set; }
    public int? IntFeesCcy { get; set; }
    public string? IntFeesCcyCode { get; set; }

    public decimal? BillingAmount { get; set; }
    public int? BillingCcy { get; set; }
    public decimal? ConversionFees { get; set; }
    public decimal? SettlementAmount { get; set; }
    public int? SettlementCcy { get; set; }
}
