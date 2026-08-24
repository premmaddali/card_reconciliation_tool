namespace Recon.Web.Platform.Model;

/// <summary>Which side of a reconciliation a normalized record belongs to.</summary>
public enum NormalizedSide : byte
{
    Transaction = 1,
    GlEntry = 2
}

/// <summary>
/// The scheme-agnostic transaction model every adapter must produce. The reconciliation
/// engine consumes only this — it never sees scheme, CMS or CBS specifics.
/// </summary>
public class NormalizedTransaction
{
    public long NormalizedTransactionId { get; set; }

    public int ImportBatchId { get; set; }
    /// <summary>1-based row number in the source file, for evidence drill-in.</summary>
    public int SourceRowNumber { get; set; }
    /// <summary>The legacy staging row this was projected from, when applicable.</summary>
    public string? SourceTable { get; set; }
    public int? SourceStagingId { get; set; }

    // Identity & references
    public string? Reference { get; set; }
    public string? ExternalReference { get; set; }
    public string? AuthorizationCode { get; set; }
    public string? TraceNumber { get; set; }
    public string? BatchNumber { get; set; }

    // Dates
    public DateOnly? TransactionDate { get; set; }
    public DateOnly? PostingDate { get; set; }
    public DateOnly? SettlementDate { get; set; }
    public DateTime? TransactionDateTime { get; set; }

    // Amounts & currencies (all signed, normalized)
    public decimal? Amount { get; set; }
    public int? Currency { get; set; }
    public decimal? BillingAmount { get; set; }
    public int? BillingCurrency { get; set; }
    public decimal? SettlementAmount { get; set; }
    public int? SettlementCurrency { get; set; }
    /// <summary>D or C as received.</summary>
    public string? DebitCreditIndicator { get; set; }

    // Parties & cards
    public string? Merchant { get; set; }
    public string? Terminal { get; set; }
    public string? Acquirer { get; set; }
    public string? Issuer { get; set; }
    public string? MaskedCardNumber { get; set; }
    public string? CardType { get; set; }
    public string? Scheme { get; set; }
    public string? Processor { get; set; }

    // Ledger routing
    public string? Account { get; set; }
    public string? Description { get; set; }
    public string? TransactionType { get; set; }

    /// <summary>Channel flag derived by the adapter (e.g. CASH vs POS) — the engine treats it opaquely.</summary>
    public bool IsCash { get; set; }
    public bool IsPos { get; set; }
    /// <summary>Settlement leg routed by the adapter: 1 = debit cardholder, 2 = credit intermediary.</summary>
    public byte SettlementLeg { get; set; } = 1;
}

/// <summary>
/// The scheme-agnostic GL entry model produced by a Core-Banking adapter.
/// </summary>
public class NormalizedGlEntry
{
    public long NormalizedGlEntryId { get; set; }

    public int ImportBatchId { get; set; }
    public int SourceRowNumber { get; set; }
    public string? SourceTable { get; set; }
    public int? SourceStagingId { get; set; }

    public string? Account { get; set; }
    public DateOnly? PostingDate { get; set; }
    public DateTime? PostingDateTime { get; set; }
    public decimal Amount { get; set; }
    public int Currency { get; set; }
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public string? DebitCreditIndicator { get; set; }
    public bool IsCash { get; set; }
    public bool IsPos { get; set; }
    /// <summary>Settlement leg routed by the adapter.</summary>
    public byte SettlementLeg { get; set; } = 1;
}
