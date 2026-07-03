namespace Spektra.Core;

public enum VerdictKind { Unknown, Lossless, Suspicious, Lossy }

/// The outcome of a bandwidth/cutoff analysis. `CutoffHz` is the highest
/// frequency carrying real energy (null when the signal reaches Nyquist).
/// `Summary` is a one-line, user-facing verdict; `CodecGuess` names the likely
/// lossy format/bitrate when a cutoff is found.
public sealed record LosslessVerdict(
    VerdictKind Kind, double? CutoffHz, double NyquistHz, string Summary, string? CodecGuess);

/// Estimates an audio file's real bandwidth from its spectrogram and decides
/// whether a lossy low-pass cutoff is present. Heuristic: lossy encoders zero
/// out everything above a codec/bitrate-dependent frequency, leaving a hard
/// brick wall well below Nyquist. Natural high-frequency rolloff can look
/// similar, so an ambiguous rolloff is reported as `Suspicious`, not `Lossy`.
public static class CutoffAnalyzer
{
    // A bin counts as carrying "real" energy when its peak-hold level is within
    // this many dB of the loudest bin. Lossy-killed bands sit >100 dB down, so a
    // wide span cleanly separates them while tolerating naturally quiet treble.
    private const float ActiveSpanDb = 55f;

    // The active line is never dragged below the noise floor by this margin.
    private const float DeadMarginDb = 6f;

    // A cutoff within this fraction of Nyquist is treated as effectively full-band.
    private const double FullBandRatio = 0.92;

    // No lossy codec low-passes this far down; a cutoff below this is just
    // naturally band-limited content (a pure tone, a bass-only signal), which
    // says nothing about the encoding.
    private const double MinLossyCutoffHz = 6000.0;

    // How far below the active line counts as "dead" air, and how narrow the drop
    // from the last live bin to dead air must be to read as a codec brick wall
    // (rather than a gradual, natural high-frequency rolloff).
    private const float DeadDropDb = 18f;
    private const double SharpTransitionHz = 900.0;

    /// Per-bin maximum across every column (peak-hold). Captures the brightest
    /// each frequency ever gets, which is what a hard cutoff flattens to silence.
    public static float[] PeakHold(IReadOnlyList<float[]> columns)
    {
        if (columns.Count == 0) return [];
        var bins = columns[0].Length;
        var peak = new float[bins];
        Array.Fill(peak, Db.Floor);
        foreach (var col in columns)
            for (var k = 0; k < bins && k < col.Length; k++)
                if (col[k] > peak[k]) peak[k] = col[k];
        return peak;
    }

    public static LosslessVerdict Analyze(IReadOnlyList<float[]> columns, int sampleRate)
    {
        var nyquist = sampleRate / 2.0;
        var peak = PeakHold(columns);
        if (peak.Length < 2 || nyquist <= 0)
            return new LosslessVerdict(VerdictKind.Unknown, null, nyquist,
                "Not enough data to analyze bandwidth.", null);

        var hzPerBin = nyquist / (peak.Length - 1);
        var globalPeak = peak.Max();
        if (globalPeak <= Db.Floor + 3f)
            return new LosslessVerdict(VerdictKind.Unknown, null, nyquist,
                "Signal is too quiet to judge bandwidth.", null);

        var active = Math.Max(globalPeak - ActiveSpanDb, Db.Floor + DeadMarginDb);

        var cutoffBin = 0;
        for (var k = peak.Length - 1; k >= 0; k--)
            if (peak[k] >= active) { cutoffBin = k; break; }

        var cutoffHz = cutoffBin * hzPerBin;
        var ratio = cutoffHz / nyquist;

        if (ratio >= FullBandRatio)
            return new LosslessVerdict(VerdictKind.Lossless, null, nyquist,
                $"Full-band to {Khz(nyquist)}. No cutoff detected; consistent with lossless.", null);

        if (cutoffHz < MinLossyCutoffHz)
            return new LosslessVerdict(VerdictKind.Unknown, cutoffHz, nyquist,
                $"Band-limited to {Khz(cutoffHz)}; too little high-frequency content to judge encoding.", null);

        // How quickly does the top of the band collapse into dead air? A codec
        // brick wall drops within a few hundred Hz; a natural rolloff takes kHz.
        var deadLine = active - DeadDropDb;
        var firstDeadBin = -1;
        for (var k = cutoffBin + 1; k < peak.Length; k++)
            if (peak[k] < deadLine) { firstDeadBin = k; break; }

        var guess = GuessCodec(cutoffHz);
        var transitionHz = firstDeadBin < 0 ? double.PositiveInfinity : (firstDeadBin - cutoffBin) * hzPerBin;

        if (transitionHz <= SharpTransitionHz)
            return new LosslessVerdict(VerdictKind.Lossy, cutoffHz, nyquist,
                $"Sharp cutoff at {Khz(cutoffHz)}. Consistent with lossy encoding ({guess}).", guess);

        return new LosslessVerdict(VerdictKind.Suspicious, cutoffHz, nyquist,
            $"Energy rolls off above {Khz(cutoffHz)}. Could be lossy ({guess}) or natural high-frequency rolloff.",
            guess);
    }

    /// Maps a detected cutoff frequency to the lossy format/bitrate most often
    /// associated with it (audiophile rule-of-thumb values; approximate).
    public static string GuessCodec(double cutoffHz)
    {
        var k = cutoffHz / 1000.0;
        return k switch
        {
            >= 20.0 => "MP3 320 / AAC 256+",
            >= 19.0 => "MP3 ~256 / AAC ~192",
            >= 18.5 => "MP3 V0",
            >= 17.5 => "MP3 V2 / AAC ~160",
            >= 16.5 => "MP3 ~160-192",
            >= 15.5 => "MP3 128 / AAC ~128",
            >= 14.5 => "MP3 ~112 / AAC ~96",
            >= 12.5 => "MP3 ~96",
            >= 10.5 => "MP3 ~64-80",
            _ => "low-bitrate lossy (<64 kbps)",
        };
    }

    private static string Khz(double hz) => $"{hz / 1000.0:0.0} kHz";
}
