using Spektra.Core;

namespace Spektra.Tests;

public sealed class QualityRankerTests
{
    private static AuditRow Row(string codec, string bandwidth, double? cutoffHz,
        string integrity = "Ok", long? bitrateBps = 900_000) =>
        new("f", codec, 44100, 2, bitrateBps, 60, bandwidth, cutoffHz, integrity, 0, 0, false, null);

    private static readonly Func<string, string, double> NoSim = (_, _) => 0;

    [Test]
    public async Task FullBandLossless_BeatsHonestLossy_WithHighConfidence()
    {
        var r = QualityRanker.Rank([("a.flac", Row("flac", "Lossless", null)),
                                    ("b.mp3", Row("mp3", "Lossy", 20_500))], NoSim);
        await Assert.That(r.Winners.Single()).IsEqualTo("a.flac");
        await Assert.That(r.Confidence).IsEqualTo("High");
        await Assert.That(r.Reason).Contains("full-band lossless");
    }

    [Test]
    public async Task HighWallFlac_OutranksHonestLossy_ButConfidenceCapsAtMedium()
    {
        var r = QualityRanker.Rank([("wall.flac", Row("flac", "Suspicious", 20_500)),
                                    ("b.mp3", Row("mp3", "Lossy", 20_500))], NoSim);
        await Assert.That(r.Winners.Single()).IsEqualTo("wall.flac");
        await Assert.That(r.Confidence).IsEqualTo("Medium");
    }

    [Test]
    public async Task HonestLossy_BeatsProvenTranscode_AtTheSameCutoff()
    {
        var r = QualityRanker.Rank([("fake.flac", Row("flac", "Lossy", 16_000)),
                                    ("honest.mp3", Row("mp3", "Lossy", 16_000, bitrateBps: 128_000))], NoSim);
        await Assert.That(r.Winners.Single()).IsEqualTo("honest.mp3");
    }

    [Test]
    public async Task CorruptLossless_LosesToCleanLossy()
    {
        var r = QualityRanker.Rank([("broken.flac", Row("flac", "Lossless", null, integrity: "Corrupt")),
                                    ("ok.mp3", Row("mp3", "Lossy", 20_500))], NoSim);
        await Assert.That(r.Winners.Single()).IsEqualTo("ok.mp3");
        await Assert.That(r.Reason).Contains("integrity");
    }

    [Test]
    public async Task SuspectIntegrity_CostsOneClass_AndCapsConfidence()
    {
        var r = QualityRanker.Rank([("clean.flac", Row("flac", "Lossless", null)),
                                    ("iffy.flac", Row("flac", "Lossless", null, integrity: "Suspect"))], NoSim);
        await Assert.That(r.Winners.Single()).IsEqualTo("clean.flac");

        var inverted = QualityRanker.Rank([("iffy.flac", Row("flac", "Lossless", null, integrity: "Suspect")),
                                           ("b.mp3", Row("mp3", "Lossy", 16_000, bitrateBps: 128_000))], NoSim);
        await Assert.That(inverted.Winners.Single()).IsEqualTo("iffy.flac");
        await Assert.That(inverted.Confidence).IsEqualTo("Medium");
    }

    [Test]
    public async Task IdenticalCopies_Tie_InsteadOfInventingAWinner()
    {
        var r = QualityRanker.Rank([(@"C:\a.flac", Row("flac", "Lossless", null)),
                                    (@"C:\b.flac", Row("flac", "Lossless", null))],
                                   (_, _) => 0.99);
        await Assert.That(r.Winners.Count).IsEqualTo(2);
        await Assert.That(r.Reason).Contains("keep either");
    }

    [Test]
    public async Task Upsampled_RanksBelowHonestLossy()
    {
        var r = QualityRanker.Rank([("fake96.flac", Row("flac", "Upsampled", 22_050)),
                                    ("honest.mp3", Row("mp3", "Lossy", 20_500))], NoSim);
        await Assert.That(r.Winners.Single()).IsEqualTo("honest.mp3");
    }

    [Test]
    public async Task SameClass_CloseCutoffs_IsLowConfidence()
    {
        var r = QualityRanker.Rank([("a.mp3", Row("mp3", "Lossy", 20_500)),
                                    ("b.mp3", Row("mp3", "Lossy", 20_100, bitrateBps: 256_000))], NoSim);
        await Assert.That(r.Winners.Single()).IsEqualTo("a.mp3");
        await Assert.That(r.Confidence).IsEqualTo("Low");
    }

    [Test]
    public async Task IdenticalCopiesPlusALesserFile_StillSaysKeepEither()
    {
        // The common real-library shape: two identical lossless copies plus a
        // lossy version. The tie hint must not vanish just because a
        // runner-up exists (plan 1 final-review finding #2).
        var r = QualityRanker.Rank(
            [(@"C:\a.flac", Row("flac", "Lossless", null)),
             (@"C:\b.flac", Row("flac", "Lossless", null)),
             ("c.mp3", Row("mp3", "Lossy", 16_000, bitrateBps: 128_000))],
            (x, y) => x.EndsWith(".flac") && y.EndsWith(".flac") ? 0.99 : 0);
        await Assert.That(r.Winners.Count).IsEqualTo(2);
        await Assert.That(r.Reason).Contains("keep either");
        await Assert.That(r.Reason).Contains("runner-up");
        await Assert.That(r.Confidence).IsEqualTo("High");
    }
}
