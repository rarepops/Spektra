using System.Text.Json;

namespace Spektra.Core;

public sealed class FfprobeMetadataReader(string ffprobePath)
{
    public AudioMetadata Read(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(filePath))
            throw new AudioDecodeException($"File not found: {filePath}");

        var psi = FfmpegProcess.StartInfo(ffprobePath,
        [
            // -v warning (not error): the demuxer's "Estimating duration from
            // bitrate" notice is how we know the duration is untrustworthy.
            "-v", "warning", "-print_format", "json",
            // No -select_streams filter: embedded cover art is an attached
            // VIDEO stream, and filtering to audio discarded it before the
            // JSON was ever parsed. Measured cost of dropping the filter over
            // 20 probes: 0.719 s against 0.793 s with it, so this is free.
            // Parse selects the audio stream by predicate, never by position.
            "-show_format", "-show_streams", filePath,
        ]);

        using var p = FfmpegProcess.Start(psi, "ffprobe");
        // Kill the probe when the token cancels, so the blocking reads and wait
        // below unblock instead of hanging until ffprobe ends on its own.
        using var killOnCancel = FfmpegProcess.KillOnCancel(p, ct);
        // Drain stderr concurrently with stdout. Reading stdout to EOF blocks
        // until ffprobe exits, and with -v warning a bad file can emit enough
        // stderr to fill the OS pipe buffer and block ffprobe writing it, which
        // deadlocks a sequential read (the decode and metering readers drain
        // both pipes at once for the same reason).
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = stderrTask.GetAwaiter().GetResult();
        p.WaitForExit();
        ct.ThrowIfCancellationRequested();

        if (p.ExitCode != 0)
            throw new AudioDecodeException("ffprobe could not read this file.", Tail(stderr));

        return Parse(stdout, stderr);
    }

    /// Maps ffprobe's -print_format json output (stderr rides along for error
    /// detail and the estimated-duration marker). Pure; unit-tested.
    public static AudioMetadata Parse(string stdout, string stderr)
    {
        using var doc = ParseDocument(stdout, stderr);
        var root = doc.RootElement;
        // Valid JSON in the wrong shape is the same story as unreadable output
        // (a probe killed mid-write, a different program named ffprobe on
        // PATH): a per-file decode error, never an InvalidOperationException
        // escaping the audit pipeline's catch lists.
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("streams", out var streams)
            || streams.ValueKind != JsonValueKind.Array
            || streams.GetArrayLength() == 0)
            throw new AudioDecodeException("No audio stream found in this file.", Tail(stderr));

        // Selection is by predicate, never by position. The probe no longer
        // filters to audio streams (that filter is what hid cover art), so
        // stream 0 can be an attached picture: reading it would report codec
        // "png", sample rate 0 and channels 0, silently wrong in every field
        // rather than throwing. A stream that declares a type must say audio;
        // one that declares none is audio as long as it is not a picture.
        var s = FirstStream(streams, e =>
            !IsAttachedPicture(e) && Str(e, "codec_type") is null or "audio");
        if (s is not { } audio)
            throw new AudioDecodeException("No audio stream found in this file.", Tail(stderr));

        var picture = FirstStream(streams, IsAttachedPicture);
        var format = root.TryGetProperty("format", out var f) ? f : default;

        var formatTags = format.ValueKind == JsonValueKind.Object && format.TryGetProperty("tags", out var ft) ? ft : default;
        var streamTags = audio.TryGetProperty("tags", out var st) ? st : default;
        string? Tag(string name) => TagIn(formatTags, name) ?? TagIn(streamTags, name);

        // "5/12" carries its own total; Vorbis comments keep it in a separate
        // TOTALTRACKS tag that ffprobe does not fold in. The slash form wins
        // when both exist, being the more specific statement.
        var (track, trackTotal) = TagValues.SplitNumber(Tag("track"));
        var (disc, discTotal) = TagValues.SplitNumber(Tag("disc"));

        return new AudioMetadata(
            Codec: Str(audio, "codec_name") ?? "unknown",
            SampleRate: Int(Str(audio, "sample_rate")) ?? 0,
            // Via IntProp, not GetInt32: a real probe writes a number, but a
            // string still counts and garbage reads as 0 instead of throwing.
            Channels: IntProp(audio, "channels") ?? 0,
            BitsPerSample: NonZero(IntProp(audio, "bits_per_raw_sample") ?? IntProp(audio, "bits_per_sample")),
            BitRateBps: NonZero(Long(Str(audio, "bit_rate")) ?? Long(Str(format, "bit_rate"))),
            Duration: TimeSpan.FromSeconds(
                Dbl(Str(format, "duration")) ?? Dbl(Str(audio, "duration")) ?? 0),
            DurationIsEstimated: stderr.Contains("Estimating duration from bitrate"),
            Artist: TagValues.Text(Tag("artist")),
            Title: TagValues.Text(Tag("title")),
            Album: TagValues.Text(Tag("album")),
            AlbumArtist: TagValues.Text(Tag("album_artist")),
            Genre: TagValues.Text(Tag("genre")),
            Track: track,
            TrackTotal: trackTotal ?? Int(Tag("totaltracks") ?? Tag("tracktotal")),
            Disc: disc,
            DiscTotal: discTotal ?? Int(Tag("totaldiscs") ?? Tag("disctotal")),
            Year: TagValues.Year(Tag("date") ?? Tag("year")),
            HasEmbeddedArt: picture is not null,
            ArtFormat: picture is { } p ? Str(p, "codec_name") : null,
            ArtWidth: picture is { } pw ? IntProp(pw, "width") : null,
            ArtHeight: picture is { } ph ? IntProp(ph, "height") : null);
    }

    /// First stream matching a predicate, or null. JsonElement is a struct, so
    /// the nullable wrapper is what distinguishes "no match" from "default".
    /// Non-object entries are skipped before the predicate sees them: a stream
    /// must be an object to be either audio or a picture, and the predicates
    /// read properties off it.
    private static JsonElement? FirstStream(JsonElement streams, Func<JsonElement, bool> match)
    {
        foreach (var s in streams.EnumerateArray())
            if (s.ValueKind == JsonValueKind.Object && match(s)) return s;
        return null;
    }

    /// An attached picture is cover art. A video stream WITHOUT this
    /// disposition is real video (a music video, a stray track in a container)
    /// and calling it artwork would tell a librarian a file has a cover it
    /// does not have.
    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var d)
        && d.ValueKind == JsonValueKind.Object
        && d.TryGetProperty("attached_pic", out var a)
        && a.ValueKind == JsonValueKind.Number
        && a.GetInt32() == 1;

    private static JsonDocument ParseDocument(string stdout, string stderr)
    {
        try
        {
            return JsonDocument.Parse(stdout);
        }
        catch (JsonException)
        {
            // A zero-exit ffprobe can still emit unusable output (killed mid
            // write, wrong binary on PATH); that must read as a per-file
            // decode error, not escape the audit pipeline's catch lists.
            throw new AudioDecodeException("ffprobe produced unreadable output.", Tail(stderr));
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    private static string? TagIn(JsonElement tags, string name)
    {
        if (tags.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in tags.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        return null;
    }

    private static int? IntProp(JsonElement e, string name)
    {
        var s = Str(e, name);
        return int.TryParse(s, out var v) ? v : null;
    }

    private static int? Int(string? s) => int.TryParse(s, out var v) ? v : null;
    private static long? Long(string? s) => long.TryParse(s, out var v) ? v : null;
    private static double? Dbl(string? s) =>
        double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    private static int? NonZero(int? v) => v is > 0 ? v : null;
    private static long? NonZero(long? v) => v is > 0 ? v : null;

    internal static string? Tail(string? text, int max = 2000)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        return text.Length <= max ? text : text[^max..];
    }
}
