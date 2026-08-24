using Recon.Web.Domain.Import;
using Recon.Web.Domain.Staging;
using Recon.Web.Platform.Adapters;
using Recon.Web.Platform.Files;
using Recon.Web.Services.Parsing;

namespace Recon.Web.Platform.Adapters;

/// <summary>
/// Demonstration adapter proving platform extensibility: a generic Visa CMS settlement CSV
/// (columns REFERENCE, AMOUNT, CURRENCY, TXN_DATE, MERCHANT, CARD_NUMBER). Adding it required
/// no changes to the reconciliation engine — only this class and a SchemeProfile row.
/// </summary>
public sealed class VisaDemoTransactionAdapter : ITransactionAdapter
{
    public string Code => "visa-demo-csv";
    public string DisplayName => "Visa Demo Settlement CSV";
    public SourceType? LegacySourceType => null;

    public bool CanDetect(HeaderMap headers) =>
        headers.TryGetColumn("REFERENCE", out _) && headers.TryGetColumn("SETTLEMENT_AMOUNT", out _);

    public int Parse(ParsedFile file, ParseContext ctx, IStagingSink stage)
    {
        var n = 0;
        foreach (var row in file.Rows)
        {
            if (file.Headers.IsBlankRow(row)) continue;
            var h = file.Headers;
            var ccy = ValueParsers.Int(h.Cell(row, "CURRENCY"));
            stage.Add(new StgDebitSettlement
            {
                ImportBatchId = ctx.ImportBatchId,
                SourceRowNumber = row.SourceRowNumber,
                Channel = SettlementChannel.Pos,
                ReferenceNumber = ValueParsers.Clean(h.Cell(row, "REFERENCE")),
                MerchantAcronym = ValueParsers.Clean(h.Cell(row, "MERCHANT")),
                CardNumber = ValueParsers.Clean(h.Cell(row, "CARD_NUMBER")),
                TransactionDate = ValueParsers.Date(h.Cell(row, "TXN_DATE")),
                TxnAmount = ValueParsers.Amount(h.Cell(row, "AMOUNT")),
                TxnCcy = ccy,
                TxnCcyCode = ValueParsers.CcyCode(ccy),
                SettlementAmount = ValueParsers.Amount(h.Cell(row, "SETTLEMENT_AMOUNT")),
                SettlementCcy = ValueParsers.Int(h.Cell(row, "SETTLEMENT_CURRENCY")),
            });
            n++;
        }
        return n;
    }
}
