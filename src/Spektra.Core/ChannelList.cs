namespace Spektra.Core;

/// A file's channel list and the decode channel each entry stands for: Mix,
/// then Ch 1 to Ch n, then Difference when there are two channels or more to
/// subtract. One place for both, because the view once kept a second copy of
/// the mapping, missed Difference in it, and decoded Difference as a channel
/// the file does not have, which ffmpeg answers with silence, not an error.
public static class ChannelList
{
    public static List<string> Options(int channels) =>
        channels > 1
            ? ["Mix", .. Enumerable.Range(1, channels).Select(i => $"Ch {i}"), "Difference"]
            : ["Mix"];

    /// Difference is last, so each Ch n entry keeps the index that the
    /// per-channel caches and remembered overviews are keyed by.
    public static int? ChannelFor(int index, int channels) =>
        index == 0 ? null
        : channels > 1 && index == channels + 1 ? DecodeOptions.Difference
        : index - 1;

    /// Whether Mix and every channel fit in a cache of `capacity` entries
    /// alongside the selected entry. Mix or a channel selected is one of them;
    /// Difference is never prefetched, so it needs a slot of its own. Leaving
    /// it out of the count had each prefetch evict the entry due next, forever.
    public static bool CanPrefetch(int channels, int selectedIndex, int capacity)
    {
        var prefetched = 1 + channels;
        return prefetched + (selectedIndex < prefetched ? 0 : 1) <= capacity;
    }
}
