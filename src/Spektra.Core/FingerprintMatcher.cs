using System.Numerics;
using System.Runtime.InteropServices;

namespace Spektra.Core;

/// Pairwise fingerprint comparison: exact-word matches vote on the alignment
/// offset (the modal position difference IS the alignment, so extra lead-in
/// costs nothing), then the similarity is the bit-error rate over the aligned
/// overlap, measured as the EXCESS over that pair's own wrong-alignment
/// baseline, so unrelated audio lands near 0 and identical near 1.
///
/// The baseline is not optional. 12 of every word's 32 bits compare adjacent
/// pitch classes within one frame, so they encode the track's static key and
/// chord colour rather than its content, and two different songs in the same
/// key agree on most of them all track long. Raw bit similarity for such
/// strangers rides at 0.3-0.45 over a full-length overlap (measured on the
/// owner's library, 2026-08-05: two unrelated electronic tracks at 0.41), which
/// crosses MidThreshold and chained five files from two songs into one group.
/// Scoring the same pair at offsets far from the mode measures exactly that
/// floor, and subtracting it restores the contract the thresholds were
/// calibrated against.
public static class FingerprintMatcher
{
    /// Calibration starting points, pinned by the fixture suite (see the spec):
    /// at or above High the pair reads "same recording"; between Mid and High,
    /// "likely same"; below Mid, not grouped. Change only with fixture proof.
    public const double HighThreshold = 0.60;
    public const double MidThreshold = 0.40;

    /// Fewer aligned word votes than this is noise, not an alignment.
    public const int MinVotes = 8;

    /// The aligned overlap must cover at least this fraction of the shorter
    /// file for a pair to group at all (DuplicateGrouper drops it otherwise).
    /// Calibrated on the first real-library run: different songs routinely
    /// align a shared section at high similarity over a small fraction of the
    /// track, and admitting those links chained unrelated tracks together.
    public const double FullConfidenceOverlap = 0.70;

    public sealed record MatchResult(double Similarity, int OffsetFrames, double OverlapFraction);

    /// Positive OffsetFrames: b starts later than a (b carries extra lead-in).
    public static MatchResult? Match(Fingerprint a, Fingerprint b)
    {
        if (a.Words.Length == 0 || b.Words.Length == 0 || a.FramesPerSecond != b.FramesPerSecond)
            return null;

        var positions = new Dictionary<uint, List<int>>();
        for (var i = 0; i < a.Words.Length; i++)
        {
            if (!positions.TryGetValue(a.Words[i], out var list))
                positions[a.Words[i]] = list = [];
            list.Add(i);
        }

        var votes = new Dictionary<int, int>();
        for (var j = 0; j < b.Words.Length; j++)
            if (positions.TryGetValue(b.Words[j], out var hits))
                foreach (var i in hits)
                    CollectionsMarshal.GetValueRefOrAddDefault(votes, j - i, out _)++;
        if (votes.Count == 0) return null;

        // Modal offset including immediate neighbors: real alignments straddle
        // a frame edge when the true offset is not a whole frame.
        var bestOffset = 0;
        var bestVotes = -1;
        foreach (var offset in votes.Keys)
        {
            var v = votes.GetValueOrDefault(offset - 1) + votes[offset] + votes.GetValueOrDefault(offset + 1);
            if (v > bestVotes) (bestVotes, bestOffset) = (v, offset);
        }
        if (bestVotes < MinVotes) return null;
        if (ScoreAt(a, b, bestOffset) is not { } raw) return null;

        var baseline = DecoyBaseline(a, b);
        var similarity = baseline >= 0.999
            ? 0
            : Math.Clamp((raw.Similarity - baseline) / (1 - baseline), 0, 1);
        return raw with { Similarity = similarity };
    }

    /// This pair's chance agreement: the median score of a against b REVERSED.
    ///
    /// Reversal, not shifted offsets, and the distinction was learned from the
    /// fixtures: a lag decoy cannot tell self-similar content from
    /// profile-similar strangers. A chirp at any lag looks like itself, so lag
    /// decoys read a high baseline for a TRUE pair of sweeps and ate the pinned
    /// chirp-across-codecs fixtures. Reversing one side destroys the temporal
    /// alignment and every lag structure at once, while preserving exactly what
    /// the baseline must measure: the static profile each word carries. A
    /// reversed sweep meets the original as a down-sweep against an up-sweep
    /// (agreement collapses, the true pair keeps its score); a same-key
    /// stranger's static profile survives reversal untouched (the baseline
    /// stays at the profile floor, and the stranger's excess is ~0).
    ///
    /// Too-short decoy overlaps measure nothing and are skipped; with no valid
    /// decoy the baseline conservatively stays 0 and the raw score stands.
    private static double DecoyBaseline(Fingerprint a, Fingerprint b)
    {
        var reversed = new uint[b.Words.Length];
        for (var i = 0; i < reversed.Length; i++)
            reversed[i] = b.Words[b.Words.Length - 1 - i];
        var rb = new Fingerprint(b.FramesPerSecond, reversed);

        var shorter = Math.Min(a.Words.Length, b.Words.Length);
        var step = Math.Max(1, shorter / 3);
        Span<double> scores = stackalloc double[3];
        var n = 0;
        foreach (var delta in (ReadOnlySpan<int>)[0, -step, step])
            if (ScoreAt(a, rb, delta) is { OverlapFraction: >= 0.25 } decoy)
                scores[n++] = decoy.Similarity;
        if (n == 0) return 0;
        scores[..n].Sort();
        return scores[n / 2];
    }

    private static MatchResult? ScoreAt(Fingerprint a, Fingerprint b, int offset)
    {
        // b[j] aligns with a[j - offset].
        var start = Math.Max(0, offset);
        var end = Math.Min(b.Words.Length, a.Words.Length + offset);
        var overlap = end - start;
        if (overlap <= 0) return null;

        var errors = 0L;
        for (var j = start; j < end; j++)
            errors += BitOperations.PopCount(a.Words[j - offset] ^ b.Words[j]);

        var ber = errors / (32.0 * overlap);
        return new MatchResult(
            Similarity: Math.Clamp(1 - 2 * ber, 0, 1),
            OffsetFrames: offset,
            OverlapFraction: (double)overlap / Math.Min(a.Words.Length, b.Words.Length));
    }
}
