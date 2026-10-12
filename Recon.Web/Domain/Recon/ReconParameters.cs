namespace Recon.Web.Domain.Recon;

/// <summary>Administrator-managed defaults that may be opted into when a reconciliation is run.</summary>
public class ReconParameters
{
    public int ReconParametersId { get; set; }
    /// <summary>Maximum absolute amount difference that can be accepted when a user opts in.</summary>
    public decimal AmountTolerance { get; set; }
    /// <summary>
    /// Settlement spread, in days: how far a transaction date and a GL value date may differ and
    /// still be considered the same settlement. Bounds the daily/period rolling match pool and the
    /// Outstanding-vs-Aged split. Default 20.
    /// </summary>
    public int SettlementSpreadDays { get; set; } = 20;
}
