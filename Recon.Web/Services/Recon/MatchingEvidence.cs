using Recon.Web.Domain.Reference;

namespace Recon.Web.Services.Recon;

/// <summary>Shared currency interpretation for primary, secondary and review passes.</summary>
public static class MatchingEvidence
{
    public static bool IsCredit(string? productType) =>
        string.Equals(productType?.Trim(), "CR", StringComparison.OrdinalIgnoreCase);

    public static string BillingBasis(string? productType) => IsCredit(productType) ? "billing minus ISS markup" : "billing";

    // Subtract signed source amounts before taking the magnitude used by the GL matcher.
    // A blank markup means no markup; a missing billing amount cannot establish a CR amount.
    public static decimal? BillingAmountForMatching(string? productType, decimal? billingAmount, decimal? issMarkupAmount) =>
        billingAmount is decimal billing ? Math.Abs(billing - (IsCredit(productType) ? issMarkupAmount ?? 0m : 0m)) : null;

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
        int? billingCcy, decimal? billingAmount, string? productType = null, decimal? issMarkupAmount = null)
    {
        if (billingAmount.HasValue)
            return (billingCcy ?? 480, BillingAmountForMatching(productType, billingAmount, issMarkupAmount)!.Value, BillingBasis(productType));
        if (IsCredit(productType)) return (null, 0m, BillingBasis(productType));
        if (txnAmount.HasValue) return (txnCcy, Math.Abs(txnAmount.Value), null);
        if (settlementAmount.HasValue) return (settlementCcy, Math.Abs(settlementAmount.Value), "settlement");
        return (null, 0m, null);
    }
}
