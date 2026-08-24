using Recon.Web.Domain.Common;

namespace Recon.Web.Domain.Import;

/// <summary>The five recognised BankOne source exports.</summary>
public enum SourceType : byte
{
    NetworkT464 = 1,
    InterchangeFee = 2,
    PosSettlement = 3,
    AtmSettlement = 4,
    GlDump = 5
}

public enum ImportStatus : byte
{
    Uploaded = 0,
    Parsed = 1,
    Failed = 2
}

/// <summary>
/// One uploaded source file. Content-hashed so the same export cannot be imported twice.
/// The staging rows it produced carry its <see cref="ImportBatchId"/>.
/// </summary>
public class ImportBatch : IAuditable
{
    public int ImportBatchId { get; set; }
    public SourceType SourceType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    /// <summary>The YYYY-MM period the file covers, e.g. "2026-03".</summary>
    public string PeriodKey { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Uploaded;
    public string? ParseError { get; set; }
    /// <summary>Scheme this file belongs to (null = legacy/unassigned Mastercard data).</summary>
    public int? SchemeProfileId { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
