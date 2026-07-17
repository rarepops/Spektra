using System.Numerics;
using System.Runtime.InteropServices;

namespace Spektra.Core;

/// Pairwise fingerprint comparison: exact-word matches vote on the alignment
/// offset (the modal position difference IS the alignment, so extra lead-in
/// costs nothing), then the similarity is the bit-error rate over the aligned
/// overlap mapped so unrelated audio lands near 0 and identical near 1.
public static class FingerprintMatcher
{
    /// Calibration starting points, pinned by the fixture suite (see the spec):
    /// at or above High the pair reads "same recording"; between Mid and High,
    /// "likely same"; below Mid, not grouped. Change only with fixture proof.
    public const double HighThreshold = 0.60;
    public const double MidThreshold = 0.40;

    /// Fewer aligned word votes than this is noise, not an alignment.
    public const int MinVotes = 8;

    /// Overlap below this fraction of the shorter file caps the pair at Medium.
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
        return bestVotes < MinVotes ? null : ScoreAt(a, b, bestOffset);
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
