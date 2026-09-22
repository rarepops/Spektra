using Spektra.Core;

namespace Spektra.Tests;

public sealed class ChannelListTests
{
    [Test]
    public async Task Options_ends_with_Difference_only_when_there_is_something_to_subtract()
    {
        await Assert.That(string.Join(",", ChannelList.Options(1))).IsEqualTo("Mix");
        await Assert.That(string.Join(",", ChannelList.Options(2))).IsEqualTo("Mix,Ch 1,Ch 2,Difference");
    }

    [Test]
    [Arguments(0, 2, null)]
    [Arguments(1, 2, 0)]
    [Arguments(2, 2, 1)]
    [Arguments(3, 2, DecodeOptions.Difference)]
    [Arguments(0, 1, null)]
    [Arguments(3, 6, 2)]
    [Arguments(7, 6, DecodeOptions.Difference)]
    public async Task ChannelFor_maps_each_entry_to_its_decode_channel(int index, int channels, int? expected)
    {
        // The regression: the view kept a second copy of this mapping, missed
        // Difference in it, and decoded Difference as channel 2 of a stereo
        // file, which ffmpeg answers with silence rather than an error.
        await Assert.That(ChannelList.ChannelFor(index, channels)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(2, 0, 3, true)]
    [Arguments(2, 2, 3, true)]
    [Arguments(2, 3, 3, false)]
    [Arguments(2, 3, 4, true)]
    [Arguments(6, 0, 3, false)]
    public async Task CanPrefetch_counts_the_selected_entry(int channels, int selected, int capacity, bool expected)
    {
        // The loop: prefetching Mix and both channels while Difference was
        // selected wanted four entries in a three-entry cache, so each decode
        // evicted the next one due, forever. Mix or a channel selected is one
        // of the prefetched entries; Difference is not, and needs its own slot.
        await Assert.That(ChannelList.CanPrefetch(channels, selected, capacity)).IsEqualTo(expected);
    }
}
