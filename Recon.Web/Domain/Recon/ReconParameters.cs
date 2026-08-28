namespace Recon.Web.Domain.Recon;

/// <summary>Administrator-managed defaults that may be opted into when a reconciliation is run.</summary>
public class ReconParameters
{
    public int ReconParametersId { get; set; }
    /// <summary>Maximum absolute amount difference that can be accepted when a user opts in.</summary>
    public decimal AmountTolerance { get; set; }
}
