namespace Recon.Web.Services.Recon;

/// <summary>Plain card-number comparison. No hashing, encryption, key files, or cryptography.</summary>
public static class CardIdentity
{
    // Numeric Excel cells may already have lost digits. Require text for exact identity.
    public static string? FromSource(object? value) => value is string text ? Normalize(text) : null;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Any(c => !(c is >= '0' and <= '9') && c != ' ' && c != '-')) return null;
        var digits = value.Replace(" ", "").Replace("-", "");
        return digits.Length is >= 13 and <= 19 ? digits : null;
    }

    public static bool Compatible(string? processorCard, string? glRemarks)
    {
        var p = Normalize(processorCard);
        var g = Normalize(glRemarks);
        return p is null || g is null || p == g;
    }

    public static string? ImportRemarks(object? value)
    {
        var text = global::Recon.Web.Services.Parsing.ValueParsers.Clean(value);
        return value is string || Normalize(text) is null ? text : "[card identity requires text]";
    }

    // Display choices do not alter imported plain values or comparison rules.
    public static string? Display(string? value) => Normalize(value) is not null ? "[card number available]" : value;
}