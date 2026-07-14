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

    // One or two stray decode errors (an isolated damaged frame, or embedded
    // junk the decoder resyncs past) make a file worth a listen, not provably
    // corrupt; from this many errors on, it counts as damaged.
    private const int CorruptErrorThreshold = 3;

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

        return Build(meta, decodeErrors, decodeFailed, dropouts, samples);
    }

    /// Audit path: the dropouts and decoded-sample count already came from the
    /// shared decode (see AnalysisSession.AnalyzeColumnsWithSilence), so this
    /// only runs the error-count pass. decodeFailed is false here: a failed
    /// shared decode never reaches this point, since the caller reports the file
    /// as an error row instead.
    public IntegrityReport CheckPreDecoded(
        string path, AudioMetadata meta, IReadOnlyList<DropoutRegion> dropouts, long decodedSamples,
        CancellationToken ct = default)
    {
        var decodeErrors = CountDecodeErrors(path, ct);
        return Build(meta, decodeErrors, decodeFailed: false, dropouts, decodedSamples);
    }

    private static IntegrityReport Build(
        AudioMetadata meta, int decodeErrors, bool decodeFailed,
        IReadOnlyList<DropoutRegion> dropouts, long decodedSamples)
    {
        var decodedSeconds = meta.SampleRate > 0 ? (double)decodedSamples / meta.SampleRate : 0;
        var expectedSeconds = meta.Duration.TotalSeconds;
        // An estimated duration (no-Xing mp3 and friends) proves nothing about
        // truncation: appended junk inflates the estimate on healthy files.
        var truncated = !meta.DurationIsEstimated
            && expectedSeconds > 0 && expectedSeconds - decodedSeconds > TruncationToleranceSeconds;

        // Silent gaps are informational (shown in the summary, the report row,
        // and the time-axis lane) but do not raise the status by themselves:
        // interior silence is legal audio and cannot be proven to be damage.
        var status = decodeFailed || truncated || decodeErrors >= CorruptErrorThreshold ? IntegrityStatus.Corrupt
            : decodeErrors > 0 ? IntegrityStatus.Suspect
            : IntegrityStatus.Ok;

        return new IntegrityReport(status, decodeErrors, dropouts, decodedSeconds, expectedSeconds,
            truncated, Describe(status, decodeErrors, decodeFailed, dropouts, decodedSeconds, expectedSeconds, truncated));
    }

    private static string Describe(
        IntegrityStatus status, int errors, bool decodeFailed, IReadOnlyList<DropoutRegion> dropouts,
        double decoded, double expected, bool truncated)
    {
        if (status == IntegrityStatus.Ok)
            return dropouts.Count == 0
                ? "No integrity problems detected."
                : $"No damage detected; {Gaps(dropouts)} totaling "
                  + $"{dropouts.Sum(d => d.DurationSeconds):0.0}s (first at {Time(dropouts[0].StartSeconds)}).";
        var parts = new List<string>();
        if (errors > 0) parts.Add($"{errors} decode error{Plural(errors)}");
        if (decodeFailed) parts.Add("decoding failed partway");
        if (truncated) parts.Add($"truncated ({Time(decoded)} of {Time(expected)})");
        if (dropouts.Count > 0)
        {
            var total = dropouts.Sum(d => d.DurationSeconds);
            parts.Add($"{Gaps(dropouts)} totaling {total:0.0}s (first at {Time(dropouts[0].StartSeconds)})");
        }
        // A middot, not a colon: the GUI banner and CLI rows prefix this with
        // "Integrity:" already, and two colons stack badly.
        var lead = status == IntegrityStatus.Corrupt
            ? "Likely corrupt or incomplete · "
            : "Worth a listen · ";
        return lead + string.Join(", ", parts) + ".";
    }

    private static string Plural(int n) => n == 1 ? "" : "s";

    private static string Gaps(IReadOnlyList<DropoutRegion> dropouts) =>
        $"{dropouts.Count} silent gap{Plural(dropouts.Count)}";

    private static string Time(double seconds) =>
        AudioMetadata.FormatDuration(TimeSpan.FromSeconds(Math.Max(0, seconds)));

    /// Decodes the whole file to null and counts the error lines ffmpeg emits
    /// (corrupt frames, resync failures, and the like).
    private int CountDecodeErrors(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Default error detection only. The strict flags flag harmless encoder
        // quirks that play fine, one line per frame: +bitstream and +buffer
        // both surface mp3 "bits_left" padding slop, and +crccheck fails whole
        // libraries of old rips whose encoders wrote bogus frame CRCs. Real
        // damage still logs at error level without them (header resync,
        // invalid data), which the corrupt fixtures pin.
        var psi = FfmpegProcess.StartInfo(ffmpeg.FfmpegPath,
        [
            "-hide_banner", "-nostdin", "-v", "error",
            "-i", path, "-f", "null", "-",
        ]);
        // tailChars 0: keep all of stderr so every error line gets counted.
        var (_, stderr) = FfmpegProcess.RunToNull(psi, "ffmpeg", ct);
        return stderr.Split('\n').Count(l => l.Trim().Length > 0);
    }
}
