using Spektra.Core;

namespace Spektra.Tests;

public sealed class PaletteRegistryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("spektra-pal").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private PaletteRegistry Load(params (string File, string Json)[] files)
    {
        foreach (var (file, json) in files)
            File.WriteAllText(Path.Combine(_dir, file), json);
        return PaletteRegistry.LoadWithCustom(_dir);
    }

    private static (byte R, byte G, byte B) At(uint[] lut, double t)
    {
        var v = lut[(int)(t * (lut.Length - 1))];
        return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    [Test]
    public async Task BuiltIns_BakeThroughTheLut_MatchingTheLegacyLerp()
    {
        var lut = PaletteRegistry.LoadWithCustom(_dir).BakeLut("Magma", -120f);
        foreach (var db in new[] { -120f, -60f, 0f })
        {
            var expected = Colormaps.ToBgra(PaletteKind.Magma, db, -120f);
            var actual = PaletteLut.Sample(lut, db, -120f, 0f);
            await Assert.That((int)(byte)(actual >> 16)).IsBetween((byte)(expected >> 16) - 2, (byte)(expected >> 16) + 2);
            await Assert.That((int)(byte)(actual >> 8)).IsBetween((byte)(expected >> 8) - 2, (byte)(expected >> 8) + 2);
            await Assert.That((int)(byte)actual).IsBetween((byte)expected - 2, (byte)expected + 2);
        }
    }

    [Test]
    public async Task EvenStringAnchors_SpreadAcrossTheRange()
    {
        var reg = Load(("even.json", """{ "name": "Even", "anchors": ["#000000", "#FF0000", "#FFFFFF"] }"""));
        var lut = reg.BakeLut("even", -120f);
        await Assert.That(At(lut, 0)).IsEqualTo(((byte)0, (byte)0, (byte)0));
        var (r, g, b) = At(lut, 0.5); // LUT quantization: within 2 of the stop
        await Assert.That((int)r).IsBetween(253, 255);
        await Assert.That((int)g).IsBetween(0, 2);
        await Assert.That((int)b).IsBetween(0, 2);
        await Assert.That(At(lut, 1)).IsEqualTo(((byte)255, (byte)255, (byte)255));
    }

    [Test]
    public async Task AtStops_PlaceColorsUnevenly()
    {
        var reg = Load(("late.json", """
            { "name": "Late", "anchors": [
                { "color": "#000000", "at": 0.0 },
                { "color": "#FF0000", "at": 0.9 },
                { "color": "#FFFFFF", "at": 1.0 } ] }
            """));
        var lut = reg.BakeLut("Late", -120f);
        var (r, g, b) = At(lut, 0.45); // halfway to the 0.9 stop: 50% red
        await Assert.That((int)r).IsBetween(125, 130);
        await Assert.That((int)g).IsEqualTo(0);
        await Assert.That((int)b).IsEqualTo(0);
    }

    [Test]
    public async Task DbStops_StayGluedToTheirDb_WhenTheFloorChanges()
    {
        var reg = Load(("pin.json", """
            { "name": "Pin", "anchors": [
                { "color": "#000000", "db": -120 },
                { "color": "#00FF00", "db": -60 },
                { "color": "#FFFFFF", "db": 0 } ] }
            """));
        // floor -120: -60 dB sits mid-range (within LUT quantization).
        var mid = At(reg.BakeLut("Pin", -120f), 0.5);
        await Assert.That((int)mid.G).IsBetween(253, 255);
        await Assert.That((int)mid.R).IsEqualTo(0);
        // floor -80: -60 dB sits a quarter of the way up.
        var quarter = At(reg.BakeLut("Pin", -80f), 0.25);
        await Assert.That((int)quarter.G).IsBetween(253, 255);
        await Assert.That((int)quarter.R).IsEqualTo(0);
    }

    [Test]
    public async Task UnsortedStops_AreSorted()
    {
        var reg = Load(("rev.json", """
            { "name": "Rev", "anchors": [
                { "color": "#FFFFFF", "at": 1.0 },
                { "color": "#000000", "at": 0.0 } ] }
            """));
        var lut = reg.BakeLut("Rev", -120f);
        await Assert.That(At(lut, 0)).IsEqualTo(((byte)0, (byte)0, (byte)0));
        await Assert.That(At(lut, 1)).IsEqualTo(((byte)255, (byte)255, (byte)255));
    }

    [Test]
    [Arguments("""{ "name": "Mix", "anchors": [ { "color": "#000000", "at": 0 }, { "color": "#FFFFFF", "db": 0 } ] }""")]
    [Arguments("""{ "name": "MixForms", "anchors": [ "#000000", { "color": "#FFFFFF", "at": 1 } ] }""")]
    [Arguments("""{ "name": "BadHex", "anchors": ["#000000", "notacolor"] }""")]
    [Arguments("""{ "name": "StrAt", "anchors": [ { "color": "#000000", "at": 0 }, { "color": "#FFFFFF", "at": "1" } ] }""")]
    [Arguments("""{ "name": "BoolDb", "anchors": [ { "color": "#000000", "db": -60 }, { "color": "#FFFFFF", "db": true } ] }""")]
    [Arguments("""{ "name": "Single", "anchors": ["#000000"] }""")]
    [Arguments("""not json at all""")]
    public async Task InvalidFiles_AreSkippedWithAReason(string json)
    {
        var reg = Load(("bad.json", json));
        await Assert.That(reg.Skipped).HasSingleItem();
        await Assert.That(reg.Skipped[0]).Contains("bad.json");
        await Assert.That(reg.Names.Count).IsEqualTo(PaletteRegistry.LoadWithCustom(_dir + "-none").Names.Count);
    }

    [Test]
    public async Task BuiltInNameCollision_IsSkipped()
    {
        var reg = Load(("magma.json", """{ "name": "Magma", "anchors": ["#000000", "#FFFFFF"] }"""));
        await Assert.That(reg.Skipped).HasSingleItem();
        await Assert.That(reg.Names.Count(n => n == "Magma")).IsEqualTo(1);
    }

    [Test]
    public async Task UnknownName_FallsBackToTheTurboDefault()
    {
        var reg = PaletteRegistry.LoadWithCustom(_dir);
        await Assert.That(reg.BakeLut("NoSuchPalette", -120f).SequenceEqual(reg.BakeLut("Turbo", -120f))).IsTrue();
        await Assert.That(reg.Has("NoSuchPalette")).IsFalse();
        await Assert.That(reg.Has("magma")).IsTrue(); // case-insensitive
    }

    [Test]
    public async Task Gamma_TightensTheLowEnd_EndpointsUntouched()
    {
        var reg = PaletteRegistry.LoadWithCustom(_dir);
        var straight = reg.BakeLut("Grayscale", -120f);
        var tight = reg.BakeLut("Grayscale", -120f, gamma: 2.0);
        await Assert.That(At(tight, 0)).IsEqualTo(At(straight, 0));
        await Assert.That(At(tight, 1)).IsEqualTo(At(straight, 1));
        await Assert.That((int)At(straight, 0.5).R).IsBetween(126, 129); // linear ramp midpoint
        await Assert.That((int)At(tight, 0.5).R).IsBetween(62, 66);      // 0.5^2 = 0.25 of the ramp
    }

    [Test]
    public async Task EarlierDirectories_ShadowLaterOnes()
    {
        var userDir = Path.Combine(_dir, "user");
        var appDir = Path.Combine(_dir, "app");
        Directory.CreateDirectory(userDir);
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(userDir, "a.json"), """{ "name": "Shade", "anchors": ["#000000", "#FF0000"] }""");
        File.WriteAllText(Path.Combine(appDir, "b.json"), """{ "name": "Shade", "anchors": ["#000000", "#00FF00"] }""");
        File.WriteAllText(Path.Combine(appDir, "c.json"), """{ "name": "AppOnly", "anchors": ["#000000", "#FFFFFF"] }""");

        var reg = PaletteRegistry.LoadWithCustom([userDir, appDir]);

        await Assert.That(reg.Has("AppOnly")).IsTrue();
        await Assert.That(reg.Skipped).HasSingleItem(); // the app-dir Shade lost the collision
        await Assert.That(At(reg.BakeLut("Shade", -120f), 1)).IsEqualTo(((byte)255, (byte)0, (byte)0)); // user red won
    }

    [Test]
    public async Task BuiltIns_AllOpenAtTrueBlack()
    {
        var reg = PaletteRegistry.LoadWithCustom(_dir);
        foreach (var name in reg.Names)
            await Assert.That(At(reg.BakeLut(name, -120f), 0)).IsEqualTo(((byte)0, (byte)0, (byte)0));
    }

    [Test]
    public async Task CustomNames_ListAfterBuiltIns()
    {
        var reg = Load(("z.json", """{ "name": "Zebra", "anchors": ["#000000", "#FFFFFF"] }"""));
        await Assert.That(reg.Names[^1]).IsEqualTo("Zebra");
        await Assert.That(reg.Names).Contains("Magma");
        await Assert.That(reg.Has("zebra")).IsTrue();
    }
}
