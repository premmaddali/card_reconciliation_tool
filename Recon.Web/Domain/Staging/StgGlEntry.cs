namespace Recon.Web.Domain.Staging;

/// <summary>
/// Normalized Finacle general-ledger posting. REF_NUM is populated on only a fraction of rows,
/// so GL reconciles at account/total level (by <see cref="GlSubHeadCode"/> × date × currency).
/// </summary>
public class StgGlEntry
{
    public int StgGlEntryId { get; set; }
    public int ImportBatchId { get; set; }
    public int SourceRowNumber { get; set; }

    public string? SolId { get; set; }
    public string? AcctNum { get; set; }
    public string? GlSubHeadCode { get; set; }
    public string? GlAccount { get; set; }
    public string? AcctName { get; set; }
    public DateOnly? TranDate { get; set; }
    public string? TranId { get; set; }
    public string? RefNum { get; set; }
    public string? TranParticular { get; set; }
    public string? TranParticular2 { get; set; }
    public string? TranParticularCode { get; set; }

    public decimal? Debit { get; set; }
    public decimal? Credit { get; set; }
    public string? TranRmks { get; set; }
    public decimal? TranAmt { get; set; }
    public string? TranCcyCode { get; set; }

    public string? EntryUserId { get; set; }
    public string? VfdUserId { get; set; }
    public DateTime? EntryDate { get; set; }
    public DateTime? VfdDate { get; set; }
    public decimal? Rate { get; set; }
    public int? PartTranSrlNum { get; set; }
    public decimal? MurAmt { get; set; }
}
