using System.Globalization;

namespace Spektra.Core;

/// Normalizes the free text that arrives in audio tags. Every tagger writes
/// track numbers and dates differently, so this is done once here rather than
/// left to whoever reads an export.
public static class TagValues
{
    /// "5/12" is the common shape for "track 5 of 12"; a bare "5" carries no
    /// total. Anything unparseable is null rather than 0, because 0 is a real
    /// position and "no track number" is not.
    public static (int? Value, int? Total) SplitNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var slash = raw.IndexOf('/');
        return slash < 0
            ? (Int(raw), null)
            : (Int(raw[..slash]), Int(raw[(slash + 1)..]));
    }

    /// Tags carry a release date, not a year: "2019", "2019-04-12" and
    /// "2019-04-12T00:00:00Z" all mean 2019, and padded shapes like
    /// "1997-00-00" are common from older taggers. Only the leading four
    /// digits are read; a two-digit year is ambiguous and is not guessed, and
    /// an all-zero date means "unknown" rather than the year 0.
    public static int? Year(string? raw)
    {
        var s = raw?.Trim();
        if (s is null || s.Length < 4) return null;
        var head = s[..4];
        if (!head.All(char.IsAsciiDigit)) return null;
        return Int(head) is > 0 and var y ? y : null;
    }

    /// A tag that is present but blank says exactly what an absent tag says,
    /// so both become null. Keeping "" would make an export claim a file has
    /// an artist whose name happens to be empty.
    public static string? Text(string? raw)
    {
        var s = raw?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static int? Int(string? s) =>
        int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
}
