namespace Spektra.Core;

/// Decides when a Lossy bandwidth verdict signals a transcode rather than an
/// honest lossy file. Two cases: lossy content inside a lossless container
/// ("lossy as lossless"), and an mp3/aac whose cutoff sits far below what its
/// bitrate should deliver (re-encoded from a lossier source). Conservative by
/// design: missing codec, bitrate, channel, or cutoff data never accuses.
public static class TranscodeCheck
{
    private static readonly HashSet<string> LosslessCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "flac", "alac", "ape", "wavpack", "tta", "tak",
        "truehd", "mlp", "shorten", "wmalossless",
    };

    public static bool IsLosslessCodec(string? codec) =>
        codec is not null
        && (LosslessCodecs.Contains(codec)
            || codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
            || codec.StartsWith("dsd_", StringComparison.OrdinalIgnoreCase));

    // Minimum plausible cutoff per stereo-equivalent bitrate, MP3-calibrated
    // (mirrors CutoffAnalyzer.GuessCodec, floors set generously below each
    // bitrate's typical wall). AAC reuses it conservatively: real AAC cutoffs
    // sit higher, so only flagrant mismatches trip. Other lossy codecs
    // (vorbis, opus, wma*) are skipped - their curves differ too much.
    private static double? MinPlausibleCutoffHz(double stereoEquivalentKbps) => stereoEquivalentKbps switch
    {
        >= 288 => 19_500,
        >= 224 => 18_800,
        >= 176 => 17_500,
        >= 144 => 16_300,
        >= 112 => 15_000,
        >= 88 => 12_000,
        _ => null,
    };

    /// True when a Suspicious bandwidth verdict needs no row flag: a sharp
    /// wall at or above the high-cutoff line (20 kHz) inside an honestly
    /// lossy file is just a top-bitrate encode's own low-pass (MP3 320 cuts
    /// ~20.5 kHz), and the codec already declares the file lossy. The same
    /// wall in a lossless container stays flagged: it should not be there at
    /// all. A missing codec never accuses; a missing cutoff never excuses.
    public static bool IsHonestHighCutoff(string? codec, double? cutoffHz) =>
        !IsLosslessCodec(codec) && cutoffHz >= CutoffAnalyzer.HighCutoffSuspicionHz;

    /// True when a Lossy verdict on this file is a problem: any lossy wall in
    /// a lossless container, or an mp3/aac cutoff below the floor for its
    /// bitrate. Bitrates normalize to stereo-equivalent (mono at 64 kbps is
    /// 128-class; the rule-of-thumb tables are stereo-calibrated).
    public static bool IsSuspectLossy(string? codec, long? bitrateBps, int? channels, double? cutoffHz)
    {
        if (IsLosslessCodec(codec)) return true;
        if (codec is not ("mp3" or "aac")) return false;
        if (bitrateBps is not > 0 || channels is not > 0 || cutoffHz is not > 0) return false;
        var stereoEquivalentKbps = bitrateBps.Value / 1000.0 * 2 / channels.Value;
        return MinPlausibleCutoffHz(stereoEquivalentKbps) is { } floor && cutoffHz < floor;
    }
}
