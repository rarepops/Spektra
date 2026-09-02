namespace Spektra.Core;

/// Reads the `<sha256>  <name>` list the release job publishes as
/// SHA256SUMS.txt: the format sha256sum writes and `sha256sum -c` reads.
public static class Sha256Sums
{
    /// Name to lowercase hex digest, names compared without case. A line counts
    /// only when it is 64 hex digits, a space, and a name; anything else is
    /// skipped. A leading `*` on the name (sha256sum's binary-mode marker) is
    /// dropped.
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length <= 64 || line[64] != ' ' || !IsHex(line.AsSpan(0, 64))) continue;
            var name = line[64..].TrimStart(' ').TrimStart('*');
            if (name.Length == 0) continue;
            sums[name] = line[..64].ToLowerInvariant();
        }
        return sums;
    }

    /// Two hex digests are the same digest regardless of case.
    public static bool Matches(string expectedHex, string actualHex) =>
        string.Equals(expectedHex, actualHex, StringComparison.OrdinalIgnoreCase);

    private static bool IsHex(ReadOnlySpan<char> s)
    {
        foreach (var c in s)
            if (!char.IsAsciiHexDigit(c)) return false;
        return true;
    }
}
