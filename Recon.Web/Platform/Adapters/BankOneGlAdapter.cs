using Recon.Web.Domain.Import;
using Recon.Web.Domain.Staging;
using Recon.Web.Platform.Adapters;
using Recon.Web.Platform.Files;
using Recon.Web.Services.Parsing;

namespace Recon.Web.Platform.Adapters;

/// <summary>Finacle-style GL dump (BankOne core banking export).</summary>
public sealed class BankOneGlAdapter : IGlcAdapter
{
    public string Code => "bankone-gl-dump";
    public string DisplayName => "BankOne GL Dump (Finacle)";
    public SourceType? LegacySourceType => SourceType.GlDump;

    public bool CanDetect(HeaderMap headers) =>
        headers.TryGetColumn("SOL_ID", out _) && headers.TryGetColumn("GL_SUB_HEAD_CODE", out _);

    public int Parse(ParsedFile file, ParseContext ctx, IStagingSink stage)
    {
        var n = 0;
        foreach (var row in file.Rows)
        {
            if (file.Headers.IsBlankRow(row)) continue;
            var h = file.Headers;
            var entry = new StgGlEntry
            {
                ImportBatchId = ctx.ImportBatchId,
                SourceRowNumber = row.SourceRowNumber,
                SolId = ValueParsers.Clean(h.Cell(row, "SOL_ID")),
                AcctNum = ValueParsers.Clean(h.Cell(row, "ACCT_NUM")),
                GlSubHeadCode = ValueParsers.Clean(h.Cell(row, "GL_SUB_HEAD_CODE")),
                GlAccount = ValueParsers.Clean(h.Cell(row, "GL_ACCOUNT")),
                AcctName = ValueParsers.Clean(h.Cell(row, "ACCT_NAME")),
                TranDate = ValueParsers.Date(h.Cell(row, "TRAN_DATE")),
                TranId = ValueParsers.Clean(h.Cell(row, "TRAN_ID")),
                RefNum = ValueParsers.Clean(h.Cell(row, "REF_NUM")),
                TranParticular = ValueParsers.Clean(h.Cell(row, "TRAN_PARTICULAR")),
                TranParticular2 = ValueParsers.Clean(h.Cell(row, "TRAN_PARTICULAR_2")),
                TranParticularCode = ValueParsers.Clean(h.Cell(row, "TRAN_PARTICULAR_CODE")),
                Debit = ValueParsers.Amount(h.Cell(row, "DEBIT")),
                Credit = ValueParsers.Amount(h.Cell(row, "CREDIT")),
                TranRmks = ValueParsers.Clean(h.Cell(row, "TRAN_RMKS")),
                TranAmt = ValueParsers.Amount(h.Cell(row, "TRAN_AMT")),
                TranCcyCode = ValueParsers.Clean(h.Cell(row, "TRAN_CRNCY_CODE")),
                EntryUserId = ValueParsers.Clean(h.Cell(row, "ENTRY_USER_ID")),
                VfdUserId = ValueParsers.Clean(h.Cell(row, "VFD_USER_ID")),
                EntryDate = ValueParsers.DateTimeValue(h.Cell(row, "ENTRY_DATE")),
                VfdDate = ValueParsers.DateTimeValue(h.Cell(row, "VFD_DATE")),
                Rate = ValueParsers.Amount(h.Cell(row, "RATE")),
                PartTranSrlNum = ValueParsers.Int(h.Cell(row, "PART_TRAN_SRL_NUM")),
                MurAmt = ValueParsers.Amount(h.Cell(row, "MUR_AMT")),
            };

            if (GlBulkPostingClassifier.IsBulkPosting(entry.TranParticular, entry.TranParticular2, entry.TranRmks))
                stage.Add(GlBulkPostingClassifier.ToBulkPosting(entry));
            else
                stage.Add(entry);
            n++;
        }
        return n;
    }
}

/// <summary>
/// Bulk-posting detection shared by the GL adapter and the legacy parser. Extracted so the
/// classification rule lives in one place.
/// </summary>
public static class GlBulkPostingClassifier
{
    public static bool IsBulkPosting(string? tranParticular, string? tranParticular2, string? tranRmks)
    {
        var text = string.Join(' ', tranParticular, tranParticular2, tranRmks).ToUpperInvariant();
        return text.Contains("DEBIT CARD SETTLEMENT", StringComparison.Ordinal)
            || text.Contains("MC SETTLEMENT", StringComparison.Ordinal)
            || text.Contains("CARD SETTLEMENT", StringComparison.Ordinal)
            || text.Contains("SETTLEMENT DD", StringComparison.Ordinal);
    }

    public static StgGlBulkPosting ToBulkPosting(StgGlEntry e) => new()
    {
        ImportBatchId = e.ImportBatchId,
        SourceRowNumber = e.SourceRowNumber,
        SolId = e.SolId,
        AcctNum = e.AcctNum,
        GlSubHeadCode = e.GlSubHeadCode,
        GlAccount = e.GlAccount,
        AcctName = e.AcctName,
        TranDate = e.TranDate,
        TranId = e.TranId,
        RefNum = e.RefNum,
        TranParticular = e.TranParticular,
        TranParticular2 = e.TranParticular2,
        TranParticularCode = e.TranParticularCode,
        Debit = e.Debit,
        Credit = e.Credit,
        TranRmks = e.TranRmks,
        TranAmt = e.TranAmt,
        TranCcyCode = e.TranCcyCode,
        EntryUserId = e.EntryUserId,
        VfdUserId = e.VfdUserId,
        EntryDate = e.EntryDate,
        VfdDate = e.VfdDate,
        Rate = e.Rate,
        PartTranSrlNum = e.PartTranSrlNum,
        MurAmt = e.MurAmt,
    };
}
