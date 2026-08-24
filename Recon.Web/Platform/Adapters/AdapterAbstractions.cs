using Recon.Web.Domain.Import;
using Recon.Web.Platform.Files;
using Recon.Web.Platform.Model;

namespace Recon.Web.Platform.Adapters;

/// <summary>Context handed to adapters during parsing.</summary>
public sealed record ParseContext(int ImportBatchId, string PeriodKey, string FileName);

/// <summary>
/// A transaction-file adapter for one scheme + CMS combination. Adapters detect their own
/// header signature and produce normalized transactions; they never touch the engine.
/// </summary>
public interface ITransactionAdapter
{
    /// <summary>Stable code stored on SchemeProfile, e.g. "bankone-debit-settlement".</summary>
    string Code { get; }
    string DisplayName { get; }
    /// <summary>The legacy SourceType this adapter stages into (null for pure-normalized adapters).</summary>
    SourceType? LegacySourceType { get; }
    bool CanDetect(HeaderMap headers);
    /// <summary>Parses data rows into legacy staging entities (via <paramref name="stage"/>) and
    /// returns the parsed row count. Projection to the normalized model happens separately.</summary>
    int Parse(ParsedFile file, ParseContext ctx, IStagingSink stage);
}

/// <summary>A GL / core-banking export adapter for one CBS or export format.</summary>
public interface IGlcAdapter
{
    string Code { get; }
    string DisplayName { get; }
    SourceType? LegacySourceType { get; }
    bool CanDetect(HeaderMap headers);
    int Parse(ParsedFile file, ParseContext ctx, IStagingSink stage);
}

/// <summary>
/// Collects staging entities during parsing so the bulk-insert pipeline stays generic.
/// Implemented over the DbContext change tracker.
/// </summary>
public interface IStagingSink
{
    void Add(object stagingEntity);
    int Count { get; }
}

/// <summary>
/// Resolves adapters by code and auto-detects which adapter matches an uploaded file's headers.
/// </summary>
public interface IAdapterRegistry
{
    IReadOnlyList<ITransactionAdapter> TransactionAdapters { get; }
    IReadOnlyList<IGlcAdapter> GlcAdapters { get; }
    ITransactionAdapter? DetectTransaction(HeaderMap headers);
    IGlcAdapter? DetectGlc(HeaderMap headers);
    ITransactionAdapter? Transaction(string code);
    IGlcAdapter? Glc(string code);
}
