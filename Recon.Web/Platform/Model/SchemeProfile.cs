using Recon.Web.Domain.Common;

namespace Recon.Web.Platform.Model;

/// <summary>
/// One configured reconciliation scheme: a payment scheme + CMS transaction adapter paired
/// with a GL/CBS adapter, plus optional matching-rule overrides. Adding a new scheme or CMS
/// means adding an adapter class and one row here — no engine changes.
/// </summary>
public class SchemeProfile : IAuditable
{
    public int SchemeProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Payment scheme label (Mastercard, Visa, UnionPay…).</summary>
    public string Scheme { get; set; } = string.Empty;
    /// <summary>CMS/vendor label (BankOne, OpenWay…).</summary>
    public string Cms { get; set; } = string.Empty;
    /// <summary>ITransactionAdapter code used to parse CMS transaction files.</summary>
    public string TransactionAdapterCode { get; set; } = string.Empty;
    /// <summary>IGlcAdapter code used to parse GL files.</summary>
    public string GlcAdapterCode { get; set; } = string.Empty;
    /// <summary>Reconciliation functions this scheme supports, semicolon-separated
    /// (e.g. "AtmToGl;PosToGl;InterchangeToGl"). Drives the tabs on the scheme's run screen.</summary>
    public string Functions { get; set; } = "InterchangeToGl";
    public bool IsActive { get; set; } = true;

    // Sparse matching-rule overrides — null inherits the global MatchWeights defaults.
    public decimal? MatchThresholdOverride { get; set; }
    public int? DateToleranceDaysOverride { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
