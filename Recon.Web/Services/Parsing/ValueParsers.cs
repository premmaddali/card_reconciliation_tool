using System.Globalization;

namespace Recon.Web.Services.Parsing;

/// <summary>
/// Shared normalization for the BankOne exports: zero-padded amounts, separate sign columns,
/// literal "NULL" strings, numeric ISO currencies and several date formats.
/// </summary>
public static class ValueParsers
{
    /// <summary>Trims and collapses the literal string "NULL" (and blanks) to a real null.</summary>
    public static string? Clean(object? cell)
    {
        var s = cell?.ToString()?.Trim();
        if (string.IsNullOrEmpty(s) || s.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return null;
        return s;
    }

    public static decimal? Amount(object? cell)
    {
        var s = Clean(cell);
        if (s is null) return null;
        // Strip zero-padding and thousands separators; keep sign and decimal point.
        s = s.Replace(",", "");
        return decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>
    /// Amount with the sign carried in a separate column. "C"/credit is treated as positive,
    /// "D"/debit as negative (matching how the settlement reports present cardholder movements).
    /// </summary>
    public static decimal? SignedAmount(object? amountCell, object? signCell)
    {
        var mag = Amount(amountCell);
        if (mag is null) return null;
        var sign = Clean(signCell)?.ToUpperInvariant();
        var negative = sign is "D";
        var m = Math.Abs(mag.Value);
        return negative ? -m : m;
    }

    public static int? Int(object? cell)
    {
        var s = Clean(cell);
        if (s is null) return null;
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    /// <summary>Numeric ISO currency code → 3-letter code for display (480→MUR, 840→USD, 978→EUR).</summary>
    public static string? CcyCode(int? iso) => iso switch
    {
        480 => "MUR",
        840 => "USD",
        978 => "EUR",
        null => null,
        _ => iso.Value.ToString(CultureInfo.InvariantCulture)
    };

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.ff",
        "MM/dd/yyyy", "dd/MM/yyyy", "yyyyMMdd"
    };

    public static DateOnly? Date(object? cell)
    {
        if (cell is DateTime dt) return DateOnly.FromDateTime(dt);
        var s = Clean(cell);
        if (s is null) return null;
        if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return DateOnly.FromDateTime(parsed);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose)
            ? DateOnly.FromDateTime(loose) : null;
    }

    public static DateTime? DateTimeValue(object? cell)
    {
        if (cell is DateTime dt) return dt;
        var s = Clean(cell);
        if (s is null) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;
    }

    /// <summary>T464 transaction time as HHMMSS.</summary>
    public static TimeOnly? TimeHhmmss(object? cell)
    {
        var s = Clean(cell);
        if (s is null) return null;
        s = s.PadLeft(6, '0');
        return TimeOnly.TryParseExact(s, "HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var t) ? t : null;
    }

    /// <summary>T464 transaction date as MMDDYY (network convention).</summary>
    public static DateOnly? DateMmddyy(object? cell)
    {
        var s = Clean(cell);
        if (s is null || s.Length != 6) return null;
        return DateOnly.TryParseExact(s, "MMddyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
    }
}
