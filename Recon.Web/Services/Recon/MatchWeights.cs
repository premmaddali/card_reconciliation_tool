namespace Recon.Web.Services.Recon;

/// <summary>
/// Configurable weights and tolerances for the confidence matcher (bindable from appsettings
/// "MatchWeights"). Amount and date are primary; reference, card and channel add weight.
/// Total of the weights should be 100 so confidence reads as a percentage.
/// </summary>
public class MatchWeights
{
    public const string SectionName = "MatchWeights";

    // Defaults: amount + date alone (45 + 33 = 78) clear the threshold. Reference/channel push confidence higher.
    // Card and Time are reserved (0): the GL cardholder rows carry neither, so they cannot be scored yet.
    public decimal Amount { get; set; } = 45m;
    public decimal Date { get; set; } = 33m;
    public decimal Time { get; set; } = 0m;
    public decimal Reference { get; set; } = 17m;
    /// <summary>Minimum confidence when the processor and GL references are an exact identity match.</summary>
    public decimal ReferenceIdentityConfidence { get; set; } = 75m;
    public decimal Card { get; set; } = 0m;
    public decimal Channel { get; set; } = 5m;

    /// <summary>Amounts within this absolute difference (account currency) count as equal.</summary>
    public decimal AmountTolerance { get; set; } = 0.05m;

    /// <summary>Date-decay window (days). Same day = full Date weight; beyond this = 0.</summary>
    public int DateToleranceDays { get; set; } = 2;
    /// <summary>Reserved for a future ATM GL source; ATM currently reconciles only to T464.</summary>
    public int AtmDateToleranceDays { get; set; } = 4;

    /// <summary>Minimum confidence for an amount-based pair to be accepted as a match.</summary>
    public decimal MatchThreshold { get; set; } = 70m;
}
