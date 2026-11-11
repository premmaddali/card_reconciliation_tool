namespace Recon.Web.Services.Recon;

/// <summary>ATM withdrawals may pair by calendar date and exact amount, including repeated amounts.</summary>
internal static class AtmWithdrawalMatching
{
    internal const string Evidence = "ATM withdrawal: exact transaction date (time ignored), GL value date, amount, currency and settlement leg; repeated amounts paired one-to-one";

    internal static bool IsWithdrawal(string? code, string? wording) =>
        code?.Trim() == "04" ||
        (wording?.Contains("WITHDRAWAL", StringComparison.OrdinalIgnoreCase) ?? false);

    internal static bool IsCashGl(string? particular) =>
        particular?.TrimStart().StartsWith("CASH", StringComparison.OrdinalIgnoreCase) == true;
}
