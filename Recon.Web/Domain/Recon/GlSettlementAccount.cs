using Recon.Web.Domain.Common;

namespace Recon.Web.Domain.Recon;

public enum GlAccountRole : byte
{
    CardholderSettlement = 1,
    IntermediarySettlement = 2
}

/// <summary>
/// A GL account that participates in card reconciliation. Only these accounts are considered;
/// everything else in the GL dump is ignored. Currency routes a processor transaction to its account.
/// Seeded from the six known MasterCard accounts; editable so the tool stays generic.
/// </summary>
public class GlSettlementAccount : IActivatable, IAuditable
{
    public int GlSettlementAccountId { get; set; }
    public string AcctNum { get; set; } = string.Empty;
    /// <summary>ISO numeric currency (480 MUR / 840 USD / 978 EUR).</summary>
    public int Ccy { get; set; }
    public GlAccountRole Role { get; set; }
    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
