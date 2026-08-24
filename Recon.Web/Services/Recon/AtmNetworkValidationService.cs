using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Recon;

namespace Recon.Web.Services.Recon;

public interface IAtmNetworkValidationService
{
    Task ValidateAsync(int runId, int networkBatchId, int atmBatchId, CancellationToken ct = default);
}

public class AtmNetworkValidationService(IDbContextFactory<AppDbContext> dbFactory) : IAtmNetworkValidationService
{
    private const decimal Tolerance = 0.05m;

    public async Task ValidateAsync(int runId, int networkBatchId, int atmBatchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var network = await db.StgNetworkT464.Where(row => row.ImportBatchId == networkBatchId)
            .Select(row => new NetworkRow(row.StgNetworkT464Id, row.ReferenceNumber, row.CompletedAmtLocal, row.TxnCcy, row.InterchangeFee, row.TransactionDate))
            .ToListAsync(ct);
        var atm = await db.StgDebitSettlements.Where(row => row.ImportBatchId == atmBatchId)
            .Select(row => new AtmRow(row.StgDebitSettlementId, row.ReferenceNumber, row.TxnAmount, row.TxnCcy, row.InterchangeFees, row.ProcessingDate ?? row.TransactionDate))
            .ToListAsync(ct);

        var networkByReference = network.Where(row => !string.IsNullOrWhiteSpace(row.Reference))
            .GroupBy(row => row.Reference!).ToDictionary(group => group.Key, group => group.ToList());
        var atmByReference = atm.Where(row => !string.IsNullOrWhiteSpace(row.Reference))
            .GroupBy(row => row.Reference!).ToDictionary(group => group.Key, group => group.ToList());
        var results = new List<AtmNetworkValidation>();

        foreach (var reference in networkByReference.Keys.Union(atmByReference.Keys).OrderBy(reference => reference))
        {
            var networkRows = networkByReference.GetValueOrDefault(reference, []);
            var atmRows = atmByReference.GetValueOrDefault(reference, []);
            var networkRow = networkRows.FirstOrDefault();
            var atmRow = atmRows.FirstOrDefault();
            var status = Classify(networkRows, atmRows);
            results.Add(new AtmNetworkValidation
            {
                ReconRunId = runId,
                NetworkStagingId = networkRow?.Id,
                AtmStagingId = atmRow?.Id,
                ReferenceNumber = reference,
                Status = status,
                NetworkReferenceCount = networkRows.Count,
                AtmReferenceCount = atmRows.Count,
                NetworkAmount = networkRow?.Amount is { } networkAmount ? Math.Abs(networkAmount) : null,
                NetworkCcy = networkRow?.Ccy,
                NetworkFee = networkRow?.Fee is { } networkFee ? Math.Abs(networkFee) : null,
                NetworkDate = networkRow?.Date,
                AtmAmount = atmRow?.Amount is { } atmAmount ? Math.Abs(atmAmount) : null,
                AtmCcy = atmRow?.Ccy,
                AtmFee = atmRow?.Fee is { } atmFee ? Math.Abs(atmFee) : null,
                AtmDate = atmRow?.Date,
                Narrative = Narrative(status, reference, networkRows.Count, atmRows.Count)
            });
        }

        db.AtmNetworkValidations.AddRange(results);
        await db.SaveChangesAsync(ct);
    }

    private static AtmNetworkValidationStatus Classify(List<NetworkRow> network, List<AtmRow> atm)
    {
        if (network.Count == 0) return AtmNetworkValidationStatus.MissingNetwork;
        if (atm.Count == 0) return AtmNetworkValidationStatus.MissingAtm;
        if (network.Count != 1 || atm.Count != 1) return AtmNetworkValidationStatus.Duplicate;
        var networkRow = network[0];
        var atmRow = atm[0];
        if (networkRow.Ccy != atmRow.Ccy) return AtmNetworkValidationStatus.CurrencyMismatch;
        if (!AmountsAgree(networkRow.Amount, atmRow.Amount)) return AtmNetworkValidationStatus.AmountMismatch;
        if (networkRow.Fee.HasValue && atmRow.Fee.HasValue && !AmountsAgree(networkRow.Fee, atmRow.Fee)) return AtmNetworkValidationStatus.FeeMismatch;
        return AtmNetworkValidationStatus.Matched;
    }

    private static bool AmountsAgree(decimal? left, decimal? right) => left.HasValue && right.HasValue
        && Math.Abs(Math.Abs(left.Value) - Math.Abs(right.Value)) <= Tolerance;

    private static string Narrative(AtmNetworkValidationStatus status, string reference, int networkCount, int atmCount) => status switch
    {
        AtmNetworkValidationStatus.Matched => $"T464 and ATM settlement agree for reference {reference}.",
        AtmNetworkValidationStatus.AmountMismatch => $"T464 and ATM settlement amounts differ for reference {reference}.",
        AtmNetworkValidationStatus.FeeMismatch => $"T464 and ATM interchange fees differ for reference {reference}.",
        AtmNetworkValidationStatus.CurrencyMismatch => $"T464 and ATM transaction currencies differ for reference {reference}.",
        AtmNetworkValidationStatus.MissingNetwork => $"ATM settlement reference {reference} is absent from T464.",
        AtmNetworkValidationStatus.MissingAtm => $"T464 reference {reference} is absent from ATM settlement.",
        _ => $"Reference {reference} is duplicated (T464: {networkCount}, ATM: {atmCount}) and requires review."
    };

    private sealed record NetworkRow(int Id, string? Reference, decimal? Amount, int? Ccy, decimal? Fee, DateOnly? Date);
    private sealed record AtmRow(int Id, string? Reference, decimal? Amount, int? Ccy, decimal? Fee, DateOnly? Date);
}