using System.Runtime.InteropServices;

namespace Spektra.Core;

/// Inverted index over fingerprint words: files sharing at least
/// `minSharedWords` distinct words become candidate pairs for the full
/// matcher. Near-linear in total words, which is what keeps the dupes scan
/// from being all-pairs over the whole library.
public sealed class FingerprintIndex
{
    public const int DefaultMinSharedWords = 8;

    /// A word carried by more files than this says nothing about identity
    /// (near-silence and fade frames produce a handful of ubiquitous words);
    /// its posting list is skipped during pair counting.
    public const int MaxPostingFanout = 64;

    private readonly Dictionary<uint, List<int>> _byWord = [];
    private int _files;

    public int Add(Fingerprint fp)
    {
        var id = _files++;
        foreach (var word in fp.Words.Distinct())
        {
            if (!_byWord.TryGetValue(word, out var list))
                _byWord[word] = list = [];
            list.Add(id);
        }
        return id;
    }

    public IReadOnlyList<(int A, int B)> CandidatePairs(int minSharedWords = DefaultMinSharedWords)
    {
        var shared = new Dictionary<(int A, int B), int>();
        foreach (var ids in _byWord.Values)
        {
            if (ids.Count < 2 || ids.Count > MaxPostingFanout) continue;
            for (var x = 0; x < ids.Count; x++)
                for (var y = x + 1; y < ids.Count; y++)
                    CollectionsMarshal.GetValueRefOrAddDefault(shared, (ids[x], ids[y]), out _)++;
        }
        return [.. shared.Where(kv => kv.Value >= minSharedWords).Select(kv => kv.Key)];
    }
}
