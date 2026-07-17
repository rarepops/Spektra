using System.Text.Json;

namespace Spektra.Core;

public sealed class FfprobeMetadataReader(string ffprobePath)
{
    public AudioMetadata Read(string filePath)
    {
        if (!File.Exists(filePath))
            throw new AudioDecodeException($"File not found: {filePath}");

        var psi = FfmpegProcess.StartInfo(ffprobePath,
        [
            // -v warning (not error): the demuxer's "Estimating duration from
            // bitrate" notice is how we know the duration is untrustworthy.
            "-v", "warning", "-print_format", "json",
            "-show_format", "-show_streams", "-select_streams", "a", filePath,
        ]);

        using var p = FfmpegProcess.Start(psi, "ffprobe");
        // Drain stderr concurrently with stdout. Reading stdout to EOF blocks
        // until ffprobe exits, and with -v warning a bad file can emit enough
        // stderr to fill the OS pipe buffer and block ffprobe writing it, which
        // deadlocks a sequential read (the decode and metering readers drain
        // both pipes at once for the same reason).
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = stderrTask.GetAwaiter().GetResult();
        p.WaitForExit();

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
        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            throw new AudioDecodeException("No audio stream found in this file.", Tail(stderr));

        var s = streams[0];
        var format = root.TryGetProperty("format", out var f) ? f : default;

        var formatTags = format.ValueKind == JsonValueKind.Object && format.TryGetProperty("tags", out var ft) ? ft : default;
        var streamTags = s.TryGetProperty("tags", out var st) ? st : default;
        string? Tag(string name) => TagIn(formatTags, name) ?? TagIn(streamTags, name);

        return new AudioMetadata(
            Codec: Str(s, "codec_name") ?? "unknown",
            SampleRate: Int(Str(s, "sample_rate")) ?? 0,
            Channels: s.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0,
            BitsPerSample: NonZero(IntProp(s, "bits_per_raw_sample") ?? IntProp(s, "bits_per_sample")),
            BitRateBps: NonZero(Long(Str(s, "bit_rate")) ?? Long(Str(format, "bit_rate"))),
            Duration: TimeSpan.FromSeconds(
                Dbl(Str(format, "duration")) ?? Dbl(Str(s, "duration")) ?? 0),
            DurationIsEstimated: stderr.Contains("Estimating duration from bitrate"),
            Artist: Tag("artist"),
            Title: Tag("title"),
            Album: Tag("album"));
    }

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
