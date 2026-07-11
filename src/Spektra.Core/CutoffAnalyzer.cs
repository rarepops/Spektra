namespace Spektra.Core;

public enum VerdictKind { Unknown, Lossless, Suspicious, Lossy, Upsampled }

/// The outcome of a bandwidth/cutoff analysis. `CutoffHz` is the highest
/// frequency carrying real energy (null when the signal reaches Nyquist).
/// `Summary` is a one-line, user-facing verdict; `CodecGuess` names the likely
/// lossy format/bitrate when a cutoff is found.
public sealed record LosslessVerdict(
    VerdictKind Kind, double? CutoffHz, string Summary, string? CodecGuess);

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

    // Upsampled-source detection: a sharp wall sitting at another standard
    // rate's Nyquist, inside a container declared at least MinStepUpFactor
    // faster, reads as an upsample (44.1→96 is 2.18×; 44.1→48 at 1.09× must
    // NOT trigger). 16 kHz doubles as the MP3-128 cutoff, so the 32 kHz base
    // is trusted only for unambiguously hi-res containers.
    //
    // Two edges are matched because resampler skirts differ wildly (measured
    // 2026-07-06 on real 44.1/48→96 kHz output): a good resampler (soxr)
    // silences everything within ~2% of the source Nyquist, so the stopband
    // edge (peak-55 dB) lands on it; ffmpeg's default swr leaks a lazy
    // ~14 dB/kHz skirt that pushes that edge up to ~15% past the source
    // Nyquist, while the passband edge (peak-12 dB) stays within ~4%.
    private const float PassbandSpanDb = 12f;
    private const double StopbandMatchTolerance = 0.04;
    private const double PassbandMatchTolerance = 0.055;
    private const double MinStepUpFactor = 1.5;
    private const int SecondaryMinDeclaredHz = 88200;

    private static readonly (int BaseRateHz, bool Primary)[] StandardBaseRates =
    [
        (48000, true), (44100, true), (32000, false),
    ];

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
            return new LosslessVerdict(VerdictKind.Unknown, null,
                "Not enough data to analyze bandwidth.", null);

        var hzPerBin = nyquist / (peak.Length - 1);
        var globalPeak = peak.Max();
        if (globalPeak <= Db.Floor + 3f)
            return new LosslessVerdict(VerdictKind.Unknown, null,
                "Signal is too quiet to judge bandwidth.", null);

        var active = Math.Max(globalPeak - ActiveSpanDb, Db.Floor + DeadMarginDb);

        var cutoffBin = 0;
        for (var k = peak.Length - 1; k >= 0; k--)
            if (peak[k] >= active) { cutoffBin = k; break; }

        var cutoffHz = cutoffBin * hzPerBin;
        var ratio = cutoffHz / nyquist;

        if (ratio >= FullBandRatio)
            return new LosslessVerdict(VerdictKind.Lossless, null,
                $"Full-band to {Khz(nyquist)}. No cutoff detected; consistent with lossless.", null);

        if (cutoffHz < MinLossyCutoffHz)
            return new LosslessVerdict(VerdictKind.Unknown, cutoffHz,
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
        {
            var passbandLine = globalPeak - PassbandSpanDb;
            var passbandBin = 0;
            for (var k = peak.Length - 1; k >= 0; k--)
                if (peak[k] >= passbandLine) { passbandBin = k; break; }
            var passbandHz = passbandBin * hzPerBin;

            if (TryMatchUpsampleBase(cutoffHz, passbandHz, sampleRate, out var baseRateHz, out var edgeHz))
                return new LosslessVerdict(VerdictKind.Upsampled, edgeHz,
                    $"Bandwidth ends near {Khz(edgeHz)}; matches a {RateKhz(baseRateHz)} " +
                    $"source upsampled to {RateKhz(sampleRate)}.", null);

            return new LosslessVerdict(VerdictKind.Lossy, cutoffHz,
                $"Sharp cutoff at {Khz(cutoffHz)}. Consistent with lossy encoding ({guess}).", guess);
        }

        return new LosslessVerdict(VerdictKind.Suspicious, cutoffHz,
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

    /// True when either band edge sits on another standard rate's Nyquist and
    /// the declared rate is a real step up from that base rate. The stopband
    /// edge gets the tight window, the passband edge the wider one (see the
    /// constants above); among qualifying matches the closest log-ratio wins,
    /// which is what disambiguates a 44.1 kHz source from a 48 kHz one inside
    /// a lazy resampler skirt.
    private static bool TryMatchUpsampleBase(
        double stopbandHz, double passbandHz, int sampleRate, out int baseRateHz, out double edgeHz)
    {
        baseRateHz = 0;
        edgeHz = 0;
        var best = double.MaxValue;
        foreach (var (baseRate, primary) in StandardBaseRates)
        {
            if (sampleRate < MinStepUpFactor * baseRate) continue;
            if (!primary && sampleRate < SecondaryMinDeclaredHz) continue;
            var nyquist = baseRate / 2.0;

            foreach (var (hz, tolerance) in new[]
                     {
                         (stopbandHz, StopbandMatchTolerance),
                         (passbandHz, PassbandMatchTolerance),
                     })
            {
                if (hz <= 0) continue;
                var distance = Math.Abs(Math.Log(hz / nyquist));
                if (distance > Math.Log(1 + tolerance) || distance >= best) continue;
                best = distance;
                baseRateHz = baseRate;
                edgeHz = hz;
            }
        }
        return baseRateHz != 0;
    }

    private static string Khz(double hz) => $"{hz / 1000.0:0.0} kHz";

    private static string RateKhz(double hz) => $"{hz / 1000.0:0.#} kHz";
}
