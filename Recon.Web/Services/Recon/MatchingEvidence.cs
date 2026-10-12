using Recon.Web.Domain.Reference;

namespace Recon.Web.Services.Recon;

/// <summary>Shared currency interpretation for primary, secondary and review passes.</summary>
public static class MatchingEvidence
{
    public static int? Currency(string? value)
    {
        value = value?.Trim();
        if (int.TryParse(value, out var numeric) && IsoCurrencyCatalog.Entries.Any(x => x.Numeric == numeric)) return numeric;
        var item = IsoCurrencyCatalog.Entries.FirstOrDefault(x => string.Equals(x.Alpha, value, StringComparison.OrdinalIgnoreCase));
        return item.Numeric == 0 ? null : item.Numeric;
    }

    // Missing currency in older BankOne exports uses the historical MUR convention.
    // An explicit but unknown code is never silently converted to MUR.
    public static int GlCurrency(string? value) => string.IsNullOrWhiteSpace(value) ? 480 : Currency(value) ?? 0;

    public static (int? Ccy, decimal Amount, string? Basis) BillingValue(
        int? txnCcy, decimal? txnAmount, int? settlementCcy, decimal? settlementAmount,
        int? billingCcy, decimal? billingAmount)
    {
        if (billingAmount.HasValue)
            return (billingCcy ?? 480, Math.Abs(billingAmount.Value), "billing");
        if (txnAmount.HasValue) return (txnCcy, Math.Abs(txnAmount.Value), null);
        if (settlementAmount.HasValue) return (settlementCcy, Math.Abs(settlementAmount.Value), "settlement");
        return (null, 0m, null);
    }
}
