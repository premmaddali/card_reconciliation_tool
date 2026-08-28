using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Staging;
using Recon.Web.Platform.Files;
using Recon.Web.Platform.Model;

namespace Recon.Web.Platform.Adapters;

/// <summary>DI-registered adapter registry; detection iterates registered adapters.</summary>
public sealed class AdapterRegistry(IEnumerable<ITransactionAdapter> transactionAdapters, IEnumerable<IGlcAdapter> glcAdapters) : IAdapterRegistry
{
    public IReadOnlyList<ITransactionAdapter> TransactionAdapters { get; } = transactionAdapters.ToList();
    public IReadOnlyList<IGlcAdapter> GlcAdapters { get; } = glcAdapters.ToList();

    public ITransactionAdapter? DetectTransaction(HeaderMap headers) =>
        TransactionAdapters.FirstOrDefault(a => a.CanDetect(headers));
    public IGlcAdapter? DetectGlc(HeaderMap headers) =>
        GlcAdapters.FirstOrDefault(a => a.CanDetect(headers));
    public ITransactionAdapter? Transaction(string code) =>
        TransactionAdapters.FirstOrDefault(a => a.Code == code);
    public IGlcAdapter? Glc(string code) =>
        GlcAdapters.FirstOrDefault(a => a.Code == code);
}

/// <summary>Collects staging entities into the DbContext change tracker for bulk insert.</summary>
public sealed class DbContextStagingSink(AppDbContext db) : IStagingSink
{
    private readonly List<object> _items = [];
    public int Count => _items.Count;
    public void Add(object stagingEntity)
    {
        _items.Add(stagingEntity);
        db.Add(stagingEntity);
    }
}

/// <summary>
/// Projects legacy Mastercard/BankOne staging rows into the normalized platform model.
/// The scheme-specific semantics (channel prefixes, currency routing, FX fallback chain,
/// settlement-leg routing) live here — the engine only ever sees normalized rows.
/// </summary>
public static class NormalizedProjection
{
    /// <summary>The account-currency convention of the BankOne cardholder GL accounts: MUR-posted amounts.</summary>
    private const int GlPostingCurrency = 480;

    public static async Task<int> ProjectBatchAsync(AppDbContext db, int importBatchId, CancellationToken ct = default)
    {
        var batch = await db.ImportBatches.FindAsync([importBatchId], ct)
            ?? throw new InvalidOperationException($"Import batch {importBatchId} not found.");
        db.NormalizedTransactions.RemoveRange(db.NormalizedTransactions.Where(t => t.ImportBatchId == importBatchId));
        db.NormalizedGlEntries.RemoveRange(db.NormalizedGlEntries.Where(g => g.ImportBatchId == importBatchId));

        var count = batch.SourceType switch
        {
            SourceType.AtmSettlement or SourceType.PosSettlement => await ProjectDebitAsync(db, importBatchId, ct),
            SourceType.InterchangeFee => await ProjectInterchangeAsync(db, importBatchId, ct),
            SourceType.NetworkT464 => await ProjectT464Async(db, importBatchId, ct),
            SourceType.GlDump => await ProjectGlAsync(db, importBatchId, ct),
            _ => 0
        };
        await db.SaveChangesAsync(ct);
        return count;
    }

    /// <summary>Currency/amount used to reconcile against the GL: settlement first, then billing,
    /// then transaction — matching the engine's FX fallback chain.</summary>
    private static (int? ccy, decimal? amount, string? basis) ReconciliationValue(
        int? txnCcy, decimal? txnAmount, int? settleCcy, decimal? settleAmount, int? billCcy, decimal? billAmount, ISet<int> glCurrencies)
    {
        if (settleCcy is int sc && glCurrencies.Contains(sc))
            return (sc, Math.Abs(settleAmount ?? 0m), "settlement");
        if (billCcy is int bc && glCurrencies.Contains(bc))
            return (bc, Math.Abs(billAmount ?? 0m), "billing");
        return (txnCcy, Math.Abs(txnAmount ?? 0m), null);
    }

    private static async Task<int> ProjectDebitAsync(AppDbContext db, int batchId, CancellationToken ct)
    {
        var glCurrencies = await GlCardholderCurrencies(db, ct);
        var rows = await db.StgDebitSettlements.Where(x => x.ImportBatchId == batchId).ToListAsync(ct);
        foreach (var x in rows)
        {
            var recon = ReconciliationValue(x.TxnCcy, x.TxnAmount, x.SettlementCcy, x.SettlementAmount, x.BillingCcy, x.BillingAmount, glCurrencies);
            db.NormalizedTransactions.Add(new NormalizedTransaction
            {
                ImportBatchId = batchId,
                SourceRowNumber = x.SourceRowNumber,
                SourceTable = nameof(StgDebitSettlement),
                SourceStagingId = x.StgDebitSettlementId,
                Reference = x.ReferenceNumber?.Trim(),
                TransactionDate = x.TransactionDate,
                PostingDate = x.ProcessingDate ?? x.TransactionDate,
                TransactionDateTime = x.TransactionDate.HasValue ? x.TransactionDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                Amount = Math.Abs(x.TxnAmount ?? 0m),
                Currency = x.TxnCcy,
                BillingAmount = x.BillingAmount is null ? null : Math.Abs(x.BillingAmount.Value),
                BillingCurrency = x.BillingCcy,
                SettlementAmount = x.SettlementAmount is null ? null : Math.Abs(x.SettlementAmount.Value),
                SettlementCurrency = x.SettlementCcy,
                DebitCreditIndicator = x.TransactionSign,
                Merchant = x.MerchantAcronym,
                MaskedCardNumber = x.CardNumberHashed ?? x.CardNumber,
                CardType = x.ProductType,
                Description = x.TransactionWording,
                TransactionType = x.ProcessingCode,
                Scheme = "Mastercard",
                Processor = "BankOne",
                IsCash = x.Channel == SettlementChannel.Atm,
                IsPos = x.Channel == SettlementChannel.Pos,
                SettlementLeg = (byte)ReconSettlementLeg.DebitCardholder,
            });
        }
        return rows.Count;
    }

    private static async Task<int> ProjectInterchangeAsync(AppDbContext db, int batchId, CancellationToken ct)
    {
        var glCurrencies = await GlCardholderCurrencies(db, ct);
        var rows = await db.StgInterchangeFees.Where(x => x.ImportBatchId == batchId).ToListAsync(ct);
        foreach (var x in rows)
        {
            var w = (x.TransactionWording ?? "").ToUpperInvariant();
            var cash = w.Contains("WITHDRAWAL") || w.Contains("CASH");
            var recon = ReconciliationValue(x.TxnCcy, x.TxnAmount, x.SettlementCcy, x.SettlementAmount, x.BillingCcy, x.BillingAmount, glCurrencies);
            db.NormalizedTransactions.Add(new NormalizedTransaction
            {
                ImportBatchId = batchId,
                SourceRowNumber = x.SourceRowNumber,
                SourceTable = nameof(StgInterchangeFee),
                SourceStagingId = x.StgInterchangeFeeId,
                // Keep debit and credit keys distinct; the engine selects the key by product type.
                Reference = x.ReferenceNumber?.Trim(),
                ExternalReference = x.ReferenceNumber?.Trim(),
                TraceNumber = x.MicrofilmRefNumber?.Trim(),
                TransactionDateTime = x.TransactionDate,
                TransactionDate = x.TransactionDate.HasValue ? DateOnly.FromDateTime(x.TransactionDate.Value) : null,
                PostingDate = x.ProcessingDate ?? (x.TransactionDate.HasValue ? DateOnly.FromDateTime(x.TransactionDate.Value) : null),
                Amount = Math.Abs(x.TxnAmount ?? 0m),
                Currency = x.TxnCcy,
                BillingAmount = x.BillingAmount is null ? null : Math.Abs(x.BillingAmount.Value),
                BillingCurrency = x.BillingCcy,
                SettlementAmount = x.SettlementAmount is null ? null : Math.Abs(x.SettlementAmount.Value),
                SettlementCurrency = x.SettlementCcy,
                Merchant = x.MerchantAcronym,
                Terminal = x.TerminalId,
                MaskedCardNumber = x.CardAccountNumber ?? x.CardNumberHashed,
                CardType = x.ProductType?.Trim().ToUpperInvariant(),
                Description = x.TransactionWording,
                TransactionType = x.TransactionCode,
                Scheme = "Mastercard",
                Processor = "BankOne",
                IsCash = cash,
                IsPos = !cash,
                SettlementLeg = (byte)(string.Equals(x.ProductType, "CR", StringComparison.OrdinalIgnoreCase)
                    ? ReconSettlementLeg.CreditIntermediary : ReconSettlementLeg.DebitCardholder),
            });
        }
        return rows.Count;
    }

    private static Task<int> ProjectT464Async(AppDbContext db, int batchId, CancellationToken ct)
    {
        // T464 feeds Network↔ATM validation, not the GL matcher; project references and amounts
        // so future network-side functions can use the normalized model too.
        return db.StgNetworkT464.Where(x => x.ImportBatchId == batchId).ToListAsync(ct).ContinueWith(t =>
        {
            foreach (var x in t.Result)
            {
                db.NormalizedTransactions.Add(new NormalizedTransaction
                {
                    ImportBatchId = batchId,
                    SourceRowNumber = x.SourceRowNumber,
                    SourceTable = nameof(StgNetworkT464),
                    SourceStagingId = x.StgNetworkT464Id,
                    Reference = x.ReferenceNumber?.Trim(),
                    ExternalReference = x.SwitchSerialNumber,
                    TraceNumber = x.TraceNumber,
                    TransactionDate = x.TransactionDate,
                    Amount = Math.Abs(x.CompletedAmtLocal ?? 0m),
                    Currency = x.TxnCcy,
                    SettlementAmount = x.CompletedAmtSettle is null ? null : Math.Abs(x.CompletedAmtSettle.Value),
                    SettlementCurrency = x.SettleCcy,
                    MaskedCardNumber = x.Pan,
                    Terminal = x.TerminalId,
                    Acquirer = x.AcquirerInstitutionId,
                    Description = x.MessageTypeIndicator,
                    TransactionType = x.ProcessingCode,
                    Scheme = "Mastercard",
                    Processor = x.ProcessorId,
                });
            }
            return t.Result.Count;
        }, ct);
    }

    private static async Task<int> ProjectGlAsync(AppDbContext db, int batchId, CancellationToken ct)
    {
        var legs = await db.GlSettlementAccounts
            .Where(a => a.IsActive && (a.Role == GlAccountRole.CardholderSettlement || a.Role == GlAccountRole.IntermediarySettlement))
            .ToDictionaryAsync(a => a.AcctNum!, a => a.Role == GlAccountRole.CardholderSettlement
                ? ReconSettlementLeg.DebitCardholder : ReconSettlementLeg.CreditIntermediary, ct);

        var rows = await db.StgGlEntries
            .Where(g => g.ImportBatchId == batchId && g.AcctNum != null && legs.Keys.Contains(g.AcctNum))
            .ToListAsync(ct);

        foreach (var g in rows)
        {
            var part = (g.TranParticular ?? "").TrimStart().ToUpperInvariant();
            var isCash = part.StartsWith("CASH");
            var isPos = part.StartsWith("POS");
            db.NormalizedGlEntries.Add(new NormalizedGlEntry
            {
                ImportBatchId = batchId,
                SourceRowNumber = g.SourceRowNumber,
                SourceTable = nameof(StgGlEntry),
                SourceStagingId = g.StgGlEntryId,
                Account = g.AcctNum,
                PostingDate = g.TranDate,
                PostingDateTime = g.EntryDate ?? g.VfdDate,
                // Cardholder accounts are denominated per card currency but post in MUR.
                Amount = Math.Abs(g.TranAmt ?? 0m),
                Currency = GlPostingCurrency,
                // Preserve the complete GL particular. The engine derives the credit match key by
                // removing its first twelve-character BankOne posting prefix.
                Reference = g.TranParticular2?.Trim(),
                Description = CombineDescription(g.TranParticular, g.TranRmks),
                DebitCreditIndicator = (g.Debit ?? 0m) > 0 ? "D" : "C",
                IsCash = isCash,
                IsPos = isPos,
                SettlementLeg = (byte)legs[g.AcctNum!],
            });
        }
        return rows.Count;
    }

    private static async Task<HashSet<int>> GlCardholderCurrencies(AppDbContext db, CancellationToken ct) =>
        [..(await db.GlSettlementAccounts
            .Where(a => a.IsActive && a.Role == GlAccountRole.CardholderSettlement)
            .Select(a => a.Ccy).ToListAsync(ct))];

    private static string? CombineDescription(string? particular, string? rmks)
    {
        var p = particular?.Trim();
        var r = rmks?.Trim();
        if (string.IsNullOrEmpty(p)) return string.IsNullOrEmpty(r) ? null : r;
        return string.IsNullOrEmpty(r) ? p : $"{p} {r}";
    }
}
