using System.Globalization;

namespace Recon.Web.Services.Recon;

public static class SourcePeriodSelection
{
    /// <summary>Only correct a mistaken month when BOTH files prove the same source month.</summary>
    public static (DateOnly From, DateOnly To)? Correction(string? processorPeriod, string? glPeriod, DateOnly from, DateOnly to)
    {
        if (processorPeriod is null || processorPeriod != glPeriod ||
            !DateOnly.TryParseExact(processorPeriod + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month)) return null;
        if (from.ToString("yyyy-MM") == processorPeriod && to.ToString("yyyy-MM") == processorPeriod) return null;
        return (month, month.AddMonths(1).AddDays(-1));
    }
}
