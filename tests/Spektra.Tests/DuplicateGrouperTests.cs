using Spektra.Core;
using static Spektra.Core.FingerprintMatcher;

namespace Spektra.Tests;

public sealed class DuplicateGrouperTests
{
    private static MatchResult M(double sim, double overlap = 1.0) => new(sim, 0, overlap);
    private static DuplicateGrouper.FileIdentity F(string path, string? artist = null, string? title = null) =>
        new(path, artist, title);

    [Test]
    public async Task Groups_OnlyConnectedFiles_AndDropsSingletons()
    {
        var files = new[] { F(@"C:\a\x.flac"), F(@"C:\b\x.mp3"), F(@"C:\c\other.mp3") };
        var groups = DuplicateGrouper.Group(files, [(0, 1, M(0.9))]);
        await Assert.That(groups).HasSingleItem();
        await Assert.That(groups[0].Members.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GroupTier_IsTheWeakestMember()
    {
        var files = new[] { F(@"C:\a.wav"), F(@"C:\b.wav"), F(@"C:\c.wav") };
        var groups = DuplicateGrouper.Group(files, [(0, 1, M(0.9)), (1, 2, M(0.45))]);
        await Assert.That(groups[0].SamenessTier).IsEqualTo("Medium");
        await Assert.That(groups[0].Members.Single(m => m.Path == @"C:\a.wav").SamenessTier).IsEqualTo("High");
        await Assert.That(groups[0].Members.Single(m => m.Path == @"C:\c.wav").Sameness).IsEqualTo(0.45);
    }

    [Test]
    public async Task ShallowOverlap_CapsThePairAtMedium()
    {
        var files = new[] { F(@"C:\a.wav"), F(@"C:\b.wav") };
        var groups = DuplicateGrouper.Group(files, [(0, 1, M(0.9, overlap: 0.5))]);
        await Assert.That(groups[0].SamenessTier).IsEqualTo("Medium");
    }

    [Test]
    public async Task Label_PrefersTagConsensus_ThenCommonNameTokens()
    {
        var tagged = new[]
        {
            F(@"C:\a\01 Amaranth.flac", "Nightwish", "Amaranth"),
            F(@"C:\b\amaranth (remaster).mp3", "Nightwish", "Amaranth"),
            F(@"C:\c\Track07.mp3"),
        };
        var g1 = DuplicateGrouper.Group(tagged, [(0, 1, M(0.9)), (0, 2, M(0.9))]);
        await Assert.That(g1[0].Label).IsEqualTo("Nightwish - Amaranth");

        var untagged = new[] { F(@"C:\a\01 - Amaranth.mp3"), F(@"C:\b\Amaranth [FLAC] (2007).flac") };
        var g2 = DuplicateGrouper.Group(untagged, [(0, 1, M(0.9))]);
        await Assert.That(g2[0].Label).IsEqualTo("amaranth");
    }

    [Test]
    public async Task RenamedStray_IsFlaggedFoundByAudio()
    {
        var files = new[]
        {
            F(@"C:\a\01 Amaranth.flac", "Nightwish", "Amaranth"),
            F(@"C:\b\Amaranth.mp3", "Nightwish", "Amaranth"),
            F(@"C:\old\Track07.mp3"),
        };
        var groups = DuplicateGrouper.Group(files, [(0, 1, M(0.9)), (0, 2, M(0.9))]);
        var stray = groups[0].Members.Single(m => m.Path == @"C:\old\Track07.mp3");
        await Assert.That(stray.FoundByAudio).IsTrue();
        await Assert.That(groups[0].Members.Single(m => m.Path == @"C:\b\Amaranth.mp3").FoundByAudio).IsFalse();
    }

    [Test]
    public async Task NameTokens_StripNumbersBracketsAndExtension()
    {
        await Assert.That(DuplicateGrouper.NameTokens(@"C:\m\07 - Amaranth [FLAC] (2007).flac")
            .SequenceEqual(["amaranth"])).IsTrue();
    }
}
