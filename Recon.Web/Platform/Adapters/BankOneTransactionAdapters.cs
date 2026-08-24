using Recon.Web.Domain.Import;
using Recon.Web.Domain.Staging;
using Recon.Web.Platform.Adapters;
using Recon.Web.Platform.Files;
using Recon.Web.Services.Parsing;

namespace Recon.Web.Platform.Adapters;

/// <summary>
/// Shared helpers for the BankOne (Mastercard) adapters: header detection and cell access.
/// </summary>
internal static class BankOne
{
    public static object? Cell(IImportRow row, HeaderMap h, string name) => h.Cell(row, name);
}

/// <summary>ATM + POS debit settlement report (one adapter; channel decided at detection).</summary>
public sealed class BankOneDebitSettlementAdapter : ITransactionAdapter
{
    public string Code => "bankone-debit-settlement";
    public string DisplayName => "BankOne Debit Settlement (ATM/POS)";
    public SourceType? LegacySourceType => null; // channel-specific SourceType set during detection

    /// <summary>The channel resolved from the file content (set by <see cref="DetectChannel"/>).</summary>
    public SettlementChannel Channel { get; private set; } = SettlementChannel.Pos;

    public bool CanDetect(HeaderMap headers) =>
        headers.TryGetColumn("MERCHANT_ACRONYM", out _) && headers.TryGetColumn("SETTLEMENT_AMOUNT", out _);

    /// <summary>ATM vs POS disambiguated by TRANSACTION_WORDING content.</summary>
    public void DetectChannel(IEnumerable<IImportRow> rows, HeaderMap headers)
    {
        if (!headers.TryGetColumn("TRANSACTION_WORDING", out var col)) { Channel = SettlementChannel.Pos; return; }
        var sample = rows.Take(50).Select(r => r.GetCell(col)?.ToString() ?? "");
        Channel = sample.Any(v => v.Contains("WITHDRAWAL", StringComparison.OrdinalIgnoreCase))
            ? SettlementChannel.Atm : SettlementChannel.Pos;
    }

    public int Parse(ParsedFile file, ParseContext ctx, IStagingSink stage)
    {
        var n = 0;
        foreach (var row in file.Rows)
        {
            if (file.Headers.IsBlankRow(row)) continue;
            var h = file.Headers;
            var txnCcy = ValueParsers.Int(BankOne.Cell(row, h, "TRANSACTION_CURRENCY"));
            var feeCcy = ValueParsers.Int(BankOne.Cell(row, h, "INTERCHANGE_FEES_CURRENCY"));
            stage.Add(new StgDebitSettlement
            {
                ImportBatchId = ctx.ImportBatchId,
                SourceRowNumber = row.SourceRowNumber,
                Channel = Channel,
                ExtractedDate = ValueParsers.Date(BankOne.Cell(row, h, "EXTRACTED_DATE")),
                MerchantAcronym = ValueParsers.Clean(BankOne.Cell(row, h, "MERCHANT_ACRONYM")),
                ProcessingCode = ValueParsers.Clean(BankOne.Cell(row, h, "PROCESSING_CODE")),
                ReferenceNumber = ValueParsers.Clean(BankOne.Cell(row, h, "REFERENCE_NUMBER")),
                CardNumberHashed = ValueParsers.Clean(BankOne.Cell(row, h, "CARD_NUMBER_HASHED")),
                CardNumber = ValueParsers.Clean(BankOne.Cell(row, h, "CARD_NUMBER")),
                Wording = ValueParsers.Clean(BankOne.Cell(row, h, "WORDING")),
                ProductType = ValueParsers.Clean(BankOne.Cell(row, h, "PRODUCT_TYPE")),
                TransactionSign = ValueParsers.Clean(BankOne.Cell(row, h, "TRANSACTION_SIGN")),
                TransactionWording = ValueParsers.Clean(BankOne.Cell(row, h, "TRANSACTION_WORDING")),
                TransactionDate = ValueParsers.Date(BankOne.Cell(row, h, "TRANSACTION_DATE")),
                ProcessingDate = ValueParsers.Date(BankOne.Cell(row, h, "PROCESSING_DATE")),
                TxnAmountRaw = ValueParsers.Clean(BankOne.Cell(row, h, "TRANSACTION_AMOUNT")),
                TxnAmount = ValueParsers.SignedAmount(BankOne.Cell(row, h, "TRANSACTION_AMOUNT"), BankOne.Cell(row, h, "TRANSACTION_SIGN")),
                TxnCcy = txnCcy,
                TxnCcyCode = ValueParsers.CcyCode(txnCcy),
                InterchangeFeesRaw = ValueParsers.Clean(BankOne.Cell(row, h, "INTERCHANGE_FEES")),
                InterchangeFees = ValueParsers.SignedAmount(BankOne.Cell(row, h, "INTERCHANGE_FEES"), BankOne.Cell(row, h, "INTERCHANGE_FEES_SIGN")),
                IntFeesCcy = feeCcy,
                IntFeesCcyCode = ValueParsers.CcyCode(feeCcy),
                BillingAmount = ValueParsers.Amount(BankOne.Cell(row, h, "BILLING_AMOUNT")),
                BillingCcy = ValueParsers.Int(BankOne.Cell(row, h, "BILLING_CURRENCY")),
                ConversionFees = ValueParsers.Amount(BankOne.Cell(row, h, "CONVERSION_FEES")),
                SettlementAmount = ValueParsers.Amount(BankOne.Cell(row, h, "SETTLEMENT_AMOUNT")),
                SettlementCcy = ValueParsers.Int(BankOne.Cell(row, h, "SETTLEMENT_CURRENCY")),
            });
            n++;
        }
        return n;
    }
}

/// <summary>Bulk interchange-fee report.</summary>
public sealed class BankOneInterchangeAdapter : ITransactionAdapter
{
    public string Code => "bankone-interchange";
    public string DisplayName => "BankOne Interchange Fees";
    public SourceType? LegacySourceType => SourceType.InterchangeFee;

    public bool CanDetect(HeaderMap headers) =>
        headers.TryGetColumn("MICROFILM REF NUMBER", out _) && headers.TryGetColumn("INTERCHANGE FEES", out _);

    public int Parse(ParsedFile file, ParseContext ctx, IStagingSink stage)
    {
        var n = 0;
        foreach (var row in file.Rows)
        {
            if (file.Headers.IsBlankRow(row)) continue;
            var h = file.Headers;
            stage.Add(new StgInterchangeFee
            {
                ImportBatchId = ctx.ImportBatchId,
                SourceRowNumber = row.SourceRowNumber,
                MicrofilmRefNumber = ValueParsers.Clean(BankOne.Cell(row, h, "MICROFILM REF NUMBER")),
                MicrofilmRefSeq = ValueParsers.Int(BankOne.Cell(row, h, "MICROFILM REF SEQ")),
                ReferenceNumber = ValueParsers.Clean(BankOne.Cell(row, h, "REFERENCE NUMBER")),
                CardNumberHashed = ValueParsers.Clean(BankOne.Cell(row, h, "CARD NUMBER")),
                ProductCode = ValueParsers.Clean(BankOne.Cell(row, h, "PRODUCT CODE")),
                ProductType = ValueParsers.Clean(BankOne.Cell(row, h, "PRODUCT TYPE")),
                ClientCode = ValueParsers.Clean(BankOne.Cell(row, h, "CLIENT CODE")),
                CardAccountNumber = ValueParsers.Clean(BankOne.Cell(row, h, "CARD ACCOUNT NUMBER")),
                TransactionDate = ValueParsers.DateTimeValue(BankOne.Cell(row, h, "TRANSACTION DATE")),
                ProcessingDate = ValueParsers.Date(BankOne.Cell(row, h, "PROCESSING DATE")),
                TransactionCode = ValueParsers.Clean(BankOne.Cell(row, h, "TRANSACTION CODE")),
                TransactionWording = ValueParsers.Clean(BankOne.Cell(row, h, "TRANSACTION WORDING")),
                TxnAmount = ValueParsers.Amount(BankOne.Cell(row, h, "TRANSACTION AMOUNT")),
                TxnCcy = ValueParsers.Int(BankOne.Cell(row, h, "TRANSACTION CURRENCY")),
                BillingAmount = ValueParsers.Amount(BankOne.Cell(row, h, "BILLING AMOUNT")),
                BillingCcy = ValueParsers.Int(BankOne.Cell(row, h, "BILLING CURRENCY")),
                SettlementAmount = ValueParsers.Amount(BankOne.Cell(row, h, "SETTLEMENT AMOUNT")),
                SettlementCcy = ValueParsers.Int(BankOne.Cell(row, h, "SETTLEMENT CURRENCY")),
                InterchangeFeesRaw = ValueParsers.Clean(BankOne.Cell(row, h, "INTERCHANGE FEES")),
                InterchangeFees = ValueParsers.SignedAmount(BankOne.Cell(row, h, "INTERCHANGE FEES"), BankOne.Cell(row, h, "INTERCHANGE FEES SIGN")),
                IntFeesCcy = ValueParsers.Int(BankOne.Cell(row, h, "INTERCHANGE FEES CURRENCY")),
                OrigineCode = ValueParsers.Clean(BankOne.Cell(row, h, "ORIGINE CODE")),
                TerminalId = ValueParsers.Clean(BankOne.Cell(row, h, "TERMINAL ID")),
                MerchantAcronym = ValueParsers.Clean(BankOne.Cell(row, h, "MERCHANT ACRONYM")),
                IssMarkupAmount = ValueParsers.Amount(BankOne.Cell(row, h, "ISS MARKUP AMOUNT")),
                ConversionFees = ValueParsers.Amount(BankOne.Cell(row, h, "CONVERSION FEES")),
            });
            n++;
        }
        return n;
    }
}

/// <summary>T464 network settlement (MTI=FREC).</summary>
public sealed class BankOneNetworkT464Adapter : ITransactionAdapter
{
    public string Code => "bankone-network-t464";
    public string DisplayName => "BankOne T464 Network Settlement";
    public SourceType? LegacySourceType => SourceType.NetworkT464;

    public bool CanDetect(HeaderMap headers) =>
        headers.TryGetColumn("HEADER CODE", out _) && headers.TryGetColumn("MESSAGE TYPE INDICATOR", out _);

    public int Parse(ParsedFile file, ParseContext ctx, IStagingSink stage)
    {
        var n = 0;
        foreach (var row in file.Rows)
        {
            if (file.Headers.IsBlankRow(row)) continue;
            var h = file.Headers;
            var txnCcy = ValueParsers.Int(BankOne.Cell(row, h, "Currency Code Transaction"));
            var setCcy = ValueParsers.Int(BankOne.Cell(row, h, "Currency Code Settlement"));
            stage.Add(new StgNetworkT464
            {
                ImportBatchId = ctx.ImportBatchId,
                SourceRowNumber = row.SourceRowNumber,
                HeaderCode = ValueParsers.Clean(BankOne.Cell(row, h, "Header Code")),
                MessageTypeIndicator = ValueParsers.Clean(BankOne.Cell(row, h, "Message Type Indicator")),
                SwitchSerialNumber = ValueParsers.Clean(BankOne.Cell(row, h, "Switch Serial Number")),
                ProcessorId = ValueParsers.Clean(BankOne.Cell(row, h, "Processor ID")),
                TransactionDate = ValueParsers.DateMmddyy(BankOne.Cell(row, h, "Transaction Date")),
                TransactionTime = ValueParsers.TimeHhmmss(BankOne.Cell(row, h, "Transaction Time")),
                Pan = ValueParsers.Clean(BankOne.Cell(row, h, "PAN")),
                ProcessingCode = ValueParsers.Clean(BankOne.Cell(row, h, "Processing Code")),
                TraceNumber = ValueParsers.Clean(BankOne.Cell(row, h, "Trace Number")),
                MerchantType = ValueParsers.Clean(BankOne.Cell(row, h, "Merchant Type")),
                ReferenceNumber = ValueParsers.Clean(BankOne.Cell(row, h, "Reference Number")),
                AcquirerInstitutionId = ValueParsers.Clean(BankOne.Cell(row, h, "Acquirer InstitutionID")),
                TerminalId = ValueParsers.Clean(BankOne.Cell(row, h, "Terminal ID")),
                ResponseCode = ValueParsers.Clean(BankOne.Cell(row, h, "Response Code")),
                TxnCcy = txnCcy,
                TxnCcyCode = ValueParsers.CcyCode(txnCcy),
                CompletedAmtLocalRaw = ValueParsers.Clean(BankOne.Cell(row, h, "Completed Amt TransLocal")),
                CompletedAmtLocal = ValueParsers.SignedAmount(BankOne.Cell(row, h, "Completed Amt TransLocal"), BankOne.Cell(row, h, "Completed Amt TransLocal Indicator")),
                SettleCcy = setCcy,
                SettleCcyCode = ValueParsers.CcyCode(setCcy),
                ConversionRate = ValueParsers.Amount(BankOne.Cell(row, h, "Conversion Rate Settlement")),
                CompletedAmtSettleRaw = ValueParsers.Clean(BankOne.Cell(row, h, "Completed Amt Settlement")),
                CompletedAmtSettle = ValueParsers.SignedAmount(BankOne.Cell(row, h, "Completed Amt Settlement"), BankOne.Cell(row, h, "Completed Amt Settlement Indicator")),
                InterchangeFeeRaw = ValueParsers.Clean(BankOne.Cell(row, h, "Interchange Fee")),
                InterchangeFee = ValueParsers.SignedAmount(BankOne.Cell(row, h, "Interchange Fee"), BankOne.Cell(row, h, "Interchange Fee Indicator")),
                RequestedAmtLocal = ValueParsers.Amount(BankOne.Cell(row, h, "Requested Amt TransLocal")),
            });
            n++;
        }
        return n;
    }
}
