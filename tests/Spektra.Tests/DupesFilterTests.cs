using Spektra.Core;

namespace Spektra.Tests;

public sealed class DupesFilterTests
{
    private static AuditRow Row(string file) =>
        new(file, "flac", 44100, 2, 900_000, 60, "Lossless", null, "Ok", 0, 0, false, null);

    private static DupesGroupReport Group(string label, params string[] paths) =>
        new(
            new DuplicateGroup(1, label, "High",
                [.. paths.Select((p, i) => new DuplicateMember(p, 1.0 - i * 0.1, "High", i > 0))]),
            new QualityRanking([paths[0]], "High", "winner", [.. paths]),
            paths.ToDictionary(p => p, Row),
            paths.ToDictionary(p => p, _ => 1_000_000L),
            1_000_000);

    [Test]
    public async Task ParseFilterTokens_SplitsLowercasesAndDedupes()
    {
        await Assert.That(DuplicateScan.ParseFilterTokens(null)).IsEmpty();
        await Assert.That(DuplicateScan.ParseFilterTokens("   ")).IsEmpty();
        await Assert.That(DuplicateScan.ParseFilterTokens(" Flac  2019, flac; mp3 ")
            .SequenceEqual(["flac", "2019", "mp3"])).IsTrue();
    }

    [Test]
    public async Task GroupMatches_TokenHitsLabelOrAnyMemberPath_CaseInsensitive()
    {
        var g = Group("Artist · Great Song", @"C:\Rips\2019\great.flac", @"D:\Backup\great.mp3");

        await Assert.That(DuplicateScan.GroupMatches(g, [])).IsTrue();
        await Assert.That(DuplicateScan.GroupMatches(g, ["great"])).IsTrue();     // label
        await Assert.That(DuplicateScan.GroupMatches(g, ["backup"])).IsTrue();    // one member's path
        await Assert.That(DuplicateScan.GroupMatches(g, ["GREAT"])).IsTrue();     // case-insensitive
        await Assert.That(DuplicateScan.GroupMatches(g, ["zzz"])).IsFalse();
    }

    [Test]
    public async Task GroupMatches_EveryTokenMustMatch_TargetsMayDiffer()
    {
        var g = Group("Artist · Great Song", @"C:\Rips\2019\great.flac", @"D:\Backup\great.mp3");

        // "2019" only lives in one path, "artist" only in the label: both
        // present somewhere means the group stays.
        await Assert.That(DuplicateScan.GroupMatches(g, ["artist", "2019"])).IsTrue();
        await Assert.That(DuplicateScan.GroupMatches(g, ["artist", "zzz"])).IsFalse();
    }
}
