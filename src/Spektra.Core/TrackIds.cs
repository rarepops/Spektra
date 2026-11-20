using System.Text.RegularExpressions;

namespace Spektra.Core;

/// The identifiers a file's tags carry for the track it was made as: a Spotify track ID and an
/// ISRC, each null when the tags hold none. They say which track a file claims to be, never which
/// audio it holds.
public sealed partial record TrackIds(string? SpotifyId, string? Isrc)
{
    public static TrackIds None { get; } = new(null, null);

    public bool IsEmpty => SpotifyId is null && Isrc is null;

    private static readonly HashSet<string> IsrcKeys = new(StringComparer.OrdinalIgnoreCase) { "isrc", "tsrc" };

    private static readonly HashSet<string> IdKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "spotid", "spotify_id", "spotifyid", "spotify_track_id",
    };

    /// Reads the IDs out of a file's tags, in the order given. Pure.
    ///
    /// The ISRC is the first isrc or tsrc tag whose value is a valid code once its separators are
    /// gone. The Spotify ID is the first of the tags made for it that holds one: failing that, the
    /// first track link or URI in a tag of any name. A tag made for the ID is the better witness
    /// than a link that a download left in a comment.
    public static TrackIds FromTags(IEnumerable<KeyValuePair<string, string>> tags)
    {
        string? isrc = null, own = null, link = null;
        foreach (var (key, value) in tags)
        {
            if (isrc is null && IsrcKeys.Contains(key)) isrc = ReadIsrc(value);
            if (own is null && IdKeys.Contains(key)) own = ReadOwnId(value);
            link ??= ReadLink(value);
        }
        return new TrackIds(own ?? link, isrc);
    }

    // An ISRC is printed with hyphens or spaces and in either case: the code is the letters and
    // digits left, upper-cased.
    private static string? ReadIsrc(string value)
    {
        var code = string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        return IsrcShape().IsMatch(code) ? code : null;
    }

    private static string? ReadOwnId(string value)
    {
        var id = value.Trim();
        return id.Length == 22 && id.All(char.IsAsciiLetterOrDigit) ? id : null;
    }

    private static string? ReadLink(string value)
    {
        var link = TrackLink().Match(value);
        return link.Success ? link.Groups[1].Value : null;
    }

    // Two letters, three letters or digits, seven digits.
    [GeneratedRegex(@"^[A-Z]{2}[A-Z0-9]{3}[0-9]{7}\z")]
    private static partial Regex IsrcShape();

    // A track link, with the language segment Spotify's web player adds for some regions, or a
    // track URI. The 22 base62 characters must end the run: a longer one is no ID.
    [GeneratedRegex(@"(?:open\.spotify\.com/(?:intl-[A-Za-z-]+/)?track/|spotify:track:)([0-9A-Za-z]{22})(?![0-9A-Za-z])")]
    private static partial Regex TrackLink();
}
