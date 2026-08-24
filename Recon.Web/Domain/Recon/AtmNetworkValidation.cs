namespace Recon.Web.Domain.Recon;

public enum AtmNetworkValidationStatus : byte
{
    Matched = 0,
    AmountMismatch = 1,
    FeeMismatch = 2,
    CurrencyMismatch = 3,
    MissingNetwork = 4,
    MissingAtm = 5,
    Duplicate = 6
}

/// <summary>Comparison of one T464 network-settlement reference against the ATM settlement report.</summary>
public class AtmNetworkValidation
{
    public int AtmNetworkValidationId { get; set; }
    public int ReconRunId { get; set; }
    public int? NetworkStagingId { get; set; }
    public int? AtmStagingId { get; set; }
    public string? ReferenceNumber { get; set; }
    public AtmNetworkValidationStatus Status { get; set; }
    public int NetworkReferenceCount { get; set; }
    public int AtmReferenceCount { get; set; }
    public decimal? NetworkAmount { get; set; }
    public int? NetworkCcy { get; set; }
    public decimal? NetworkFee { get; set; }
    public DateOnly? NetworkDate { get; set; }
    public decimal? AtmAmount { get; set; }
    public int? AtmCcy { get; set; }
    public decimal? AtmFee { get; set; }
    public DateOnly? AtmDate { get; set; }
    public string Narrative { get; set; } = string.Empty;
}