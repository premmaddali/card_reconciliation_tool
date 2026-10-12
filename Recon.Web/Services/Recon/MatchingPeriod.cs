using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;

namespace Recon.Web.Services.Recon;

public static class MatchingPeriod
{
    /// <param name="enforcePostingPeriod">
    /// When true (monthly batches), the imported batch periods must agree with the selected period and
    /// every processor posting date must fall inside the range — this catches mislabeled files. Daily /
    /// period runs pass false: a deliberate sub-month window legitimately holds posting dates that lag
    /// past it, and its files are sliced by transaction date, so only scheme, period format and range
    /// order are checked.
    /// </param>
    public static async Task ValidateAsync(AppDbContext db, ReconFunction function, int processorBatchId,
        int glBatchId, string period, DateOnly? start, DateOnly? end, int? scheme, CancellationToken ct,
        bool enforcePostingPeriod = true)
    {
        var processor = await db.ImportBatches.SingleAsync(x => x.ImportBatchId == processorBatchId, ct);
        var gl = await db.ImportBatches.SingleAsync(x => x.ImportBatchId == glBatchId, ct);
        if (processor.SchemeProfileId != scheme || gl.SchemeProfileId != scheme)
            throw new InvalidOperationException("Both source batches must belong to the selected scheme.");
        if (!DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            throw new InvalidOperationException("Select a valid YYYY-MM reconciliation period.");
        var from = start ?? month;
        var to = end ?? month.AddMonths(1).AddDays(-1);
        if (to < from)
            throw new InvalidOperationException("The 'to' date must be on or after the 'from' date.");
        if (!enforcePostingPeriod)
            return; // daily/period run: posting dates may legitimately fall outside the chosen window.
        if ((!string.IsNullOrEmpty(processor.PeriodKey) && processor.PeriodKey != period) ||
            (!string.IsNullOrEmpty(gl.PeriodKey) && gl.PeriodKey != period))
            throw new InvalidOperationException($"Selected period {period} disagrees with imported batches ({processor.PeriodKey}, {gl.PeriodKey}). Select the source period before running reconciliation.");
        if (from.ToString("yyyy-MM") != period)
            throw new InvalidOperationException("The date range must start in the selected reconciliation period.");
        List<DateOnly?> dates;
        if (await db.NormalizedTransactions.AnyAsync(x => x.ImportBatchId == processorBatchId, ct))
            dates = await db.NormalizedTransactions.Where(x => x.ImportBatchId == processorBatchId)
                .Select(x => x.PostingDate ?? x.TransactionDate).Distinct().ToListAsync(ct);
        else if (function == ReconFunction.InterchangeToGl)
            dates = (await db.StgInterchangeFees.Where(x => x.ImportBatchId == processorBatchId)
                .Select(x => new { x.ProcessingDate, x.TransactionDate }).ToListAsync(ct))
                .Select(x => x.ProcessingDate ?? (x.TransactionDate.HasValue ? DateOnly.FromDateTime(x.TransactionDate.Value) : (DateOnly?)null)).Distinct().ToList();
        else
            dates = await db.StgDebitSettlements.Where(x => x.ImportBatchId == processorBatchId)
                .Select(x => x.ProcessingDate ?? x.TransactionDate).Distinct().ToListAsync(ct);
        if (dates.Any(x => x.HasValue && (x.Value < from || x.Value > to)))
            throw new InvalidOperationException($"Processor posting dates fall outside {from:yyyy-MM-dd} to {to:yyyy-MM-dd}. Choose the source date range; transaction dates may be earlier.");
    }
}
