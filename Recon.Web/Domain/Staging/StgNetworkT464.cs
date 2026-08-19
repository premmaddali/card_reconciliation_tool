namespace Recon.Web.Domain.Staging;

/// <summary>
/// Normalized row from the MasterCard network settlement file (T464, MTI=FREC), acquiring side.
/// Amounts arrive as zero-padded text with a separate C/D indicator; stored raw + as signed decimal.
/// </summary>
public class StgNetworkT464
{
    public int StgNetworkT464Id { get; set; }
    public int ImportBatchId { get; set; }
    public int SourceRowNumber { get; set; }

    public string? HeaderCode { get; set; }
    public string? MessageTypeIndicator { get; set; }
    public string? SwitchSerialNumber { get; set; }
    public string? ProcessorId { get; set; }
    public DateOnly? TransactionDate { get; set; }
    public TimeOnly? TransactionTime { get; set; }
    public string? Pan { get; set; }
    public string? ProcessingCode { get; set; }
    public string? TraceNumber { get; set; }
    public string? MerchantType { get; set; }

    /// <summary>12-digit transaction join key.</summary>
    public string? ReferenceNumber { get; set; }
    public string? AcquirerInstitutionId { get; set; }
    public string? TerminalId { get; set; }
    public string? ResponseCode { get; set; }

    public int? TxnCcy { get; set; }
    public string? TxnCcyCode { get; set; }
    public string? CompletedAmtLocalRaw { get; set; }
    public decimal? CompletedAmtLocal { get; set; }

    public int? SettleCcy { get; set; }
    public string? SettleCcyCode { get; set; }
    public decimal? ConversionRate { get; set; }
    public string? CompletedAmtSettleRaw { get; set; }
    public decimal? CompletedAmtSettle { get; set; }

    public string? InterchangeFeeRaw { get; set; }
    public decimal? InterchangeFee { get; set; }

    public decimal? RequestedAmtLocal { get; set; }
}
