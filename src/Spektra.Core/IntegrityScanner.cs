using System.Diagnostics;

namespace Spektra.Core;

public enum IntegrityStatus { Ok, Suspect, Corrupt }

/// The outcome of an integrity check: whether the file decodes cleanly, plus any
/// interior silent gaps (possible missing data) and truncation.
public sealed record IntegrityReport(
    IntegrityStatus Status,
    int DecodeErrors,
    IReadOnlyList<DropoutRegion> Dropouts,
    double DecodedSeconds,
    double ExpectedSeconds,
    bool Truncated,
    string Summary);

/// Checks whether an audio file is intact. Catches the common "partial download"
/// failure modes: corrupt frames (via ffmpeg error detection), missing pieces
/// that decode to interior digital silence (via SilenceScanner), and truncation
/// (decoded length short of the header). Spawns ffmpeg as a separate process.
public sealed class IntegrityScanner(FfmpegPaths ffmpeg)
{
    // More than a second short of the reported duration reads as truncated
    // (small differences from codec priming/padding are normal).
    private const double TruncationToleranceSeconds = 1.0;

    public IntegrityReport Check(string path, AudioMetadata meta, CancellationToken ct = default)
    {
        var decodeErrors = CountDecodeErrors(path, ct);

        var decoder = new AudioDecoder(ffmpeg.FfmpegPath);
        long samples = 0;
        var decodeFailed = false;
        IReadOnlyList<DropoutRegion> dropouts = [];

        IEnumerable<float[]> Counted()
        {
            foreach (var chunk in decoder.DecodeMonoChunks(path, ct))
            {
                samples += chunk.Length;
                yield return chunk;
            }
        }

        try
        {
            dropouts = SilenceScanner.FindDropouts(Counted(), meta.SampleRate);
        }
        catch (AudioDecodeException)
        {
            decodeFailed = true; // corrupt enough that ffmpeg bailed out
        }

        var decodedSeconds = meta.SampleRate > 0 ? (double)samples / meta.SampleRate : 0;
        var expectedSeconds = meta.Duration.TotalSeconds;
        var truncated = expectedSeconds > 0 && expectedSeconds - decodedSeconds > TruncationToleranceSeconds;

        var status = decodeErrors > 0 || decodeFailed || truncated ? IntegrityStatus.Corrupt
            : dropouts.Count > 0 ? IntegrityStatus.Suspect
            : IntegrityStatus.Ok;

        return new IntegrityReport(status, decodeErrors, dropouts, decodedSeconds, expectedSeconds,
            truncated, Describe(status, decodeErrors, decodeFailed, dropouts, decodedSeconds, expectedSeconds, truncated));
    }

    private static string Describe(
        IntegrityStatus status, int errors, bool decodeFailed, IReadOnlyList<DropoutRegion> dropouts,
        double decoded, double expected, bool truncated)
    {
        if (status == IntegrityStatus.Ok) return "No integrity problems detected.";
        var parts = new List<string>();
        if (errors > 0) parts.Add($"{errors} decode error(s)");
        if (decodeFailed) parts.Add("decoding failed partway");
        if (truncated) parts.Add($"truncated ({Time(decoded)} of {Time(expected)})");
        if (dropouts.Count > 0)
        {
            var total = dropouts.Sum(d => d.DurationSeconds);
            parts.Add($"{dropouts.Count} silent gap(s) totaling {total:0.0}s (first at {Time(dropouts[0].StartSeconds)})");
        }
        var lead = status == IntegrityStatus.Corrupt
            ? "Likely corrupt or incomplete: "
            : "Possible missing data: ";
        return lead + string.Join(", ", parts) + ".";
    }

    private static string Time(double seconds) =>
        AudioMetadata.FormatDuration(TimeSpan.FromSeconds(Math.Max(0, seconds)));

    /// Decodes the whole file to null with error detection on, and counts the
    /// error lines ffmpeg emits (corrupt frames, CRC mismatches, and the like).
    private int CountDecodeErrors(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpeg.FfmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in new[]
                 {
                     "-hide_banner", "-nostdin", "-v", "error",
                     "-err_detect", "+crccheck+bitstream+buffer",
                     "-i", path, "-f", "null", "-",
                 })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new AudioDecodeException("Failed to start ffmpeg.");
        var drain = p.StandardOutput.BaseStream.CopyToAsync(Stream.Null, ct);
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        try { drain.Wait(CancellationToken.None); } catch { /* best effort */ }

        return stderr.Split('\n').Count(l => l.Trim().Length > 0);
    }
}
