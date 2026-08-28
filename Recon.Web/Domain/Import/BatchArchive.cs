using Recon.Web.Domain.Recon;

namespace Recon.Web.Domain.Import;

/// <summary>Immutable audit copy of a reconciliation run that has been archived or frozen.</summary>
public class BatchArchive
{
    public int BatchArchiveId { get; set; }
    public int OriginalReconRunId { get; set; }
    public ReconFunction Function { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public int? SchemeProfileId { get; set; }
    public bool ArchivedByFreeze { get; set; }
    public DateTime ArchivedDate { get; set; }
    public Guid ArchivedBy { get; set; }
    /// <summary>Complete run, upload and result snapshot retained independently of live tables.</summary>
    public string SnapshotJson { get; set; } = string.Empty;
    public ICollection<BatchArchiveTransaction> Transactions { get; set; } = [];
}

/// <summary>One archived reconciliation outcome, kept separately for transaction-level archive review.</summary>
public class BatchArchiveTransaction
{
    public int BatchArchiveTransactionId { get; set; }
    public int BatchArchiveId { get; set; }
    public BatchArchive BatchArchive { get; set; } = null!;
    public string? ReferenceNumber { get; set; }
    public MatchStatus MatchStatus { get; set; }
    public decimal? ReconciliationAmount { get; set; }
    public decimal? GlAmount { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
}

/// <summary>A frozen reconciliation scope. New runs are rejected for the same function, period, and scheme.</summary>
public class BatchFreeze
{
    public int BatchFreezeId { get; set; }
    public ReconFunction Function { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public int? SchemeProfileId { get; set; }
    public int FrozenFromReconRunId { get; set; }
    public DateTime FrozenDate { get; set; }
    public Guid FrozenBy { get; set; }
}
