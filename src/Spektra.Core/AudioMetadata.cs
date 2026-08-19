using System.Text;

namespace Spektra.Core;

/// `DurationIsEstimated`: the container carries no authoritative length and
/// ffprobe guessed it from bitrate and file size (classic no-Xing mp3), so
/// `Duration` cannot be trusted for truncation judgments.
/// Artist/Title/Album come from container tags where present; null means untagged.
public sealed record AudioMetadata(
    string Codec, int SampleRate, int Channels, int? BitsPerSample,
    long? BitRateBps, TimeSpan Duration, bool DurationIsEstimated = false,
    string? Artist = null, string? Title = null, string? Album = null,
    // Additive, exactly as Artist/Title/Album were added for the deduplicator.
    // Nothing persists this record (the cache stores the derived AuditRow and,
    // beside a fingerprint, only artist and title), so growing it invalidates
    // no cache and forces no re-analysis.
    string? AlbumArtist = null, string? Genre = null,
    int? Track = null, int? TrackTotal = null,
    int? Disc = null, int? DiscTotal = null, int? Year = null,
    // Embedded cover art. Byte size is deliberately absent: ffprobe reports no
    // size for an attached picture, and dimensions are the part worth acting
    // on anyway (a 100x100 stamp and a 1400x1400 cover both "have art").
    bool HasEmbeddedArt = false, string? ArtFormat = null,
    int? ArtWidth = null, int? ArtHeight = null)
{
    public string ToDisplayLine(string fileName)
    {
        var sb = new StringBuilder(fileName).Append(" - ").Append(Codec.ToUpperInvariant());
        var khz = SampleRate / 1000.0;
        sb.Append(" · ").Append(khz.ToString(khz % 1 == 0 ? "0" : "0.#")).Append(" kHz");
        if (BitsPerSample is { } bits) sb.Append(" · ").Append(bits).Append("-bit");
        sb.Append(" · ").Append(Channels).Append(" ch");
        sb.Append(" · ").Append(FormatDuration(Duration));
        if (BitRateBps is { } bps) sb.Append(" · ").Append(bps / 1000).Append(" kbps");
        return sb.ToString();
    }

    public static string FormatDuration(TimeSpan d) => d.TotalHours >= 1
        ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}"
        : $"{d.Minutes}:{d.Seconds:00}";
}
