using Spektra.Core;
using Xunit;

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

    [Fact]
    public void BuiltIns_BakeThroughTheLut_MatchingTheLegacyLerp()
    {
        var lut = PaletteRegistry.LoadWithCustom(_dir).BakeLut("Magma", -120f);
        foreach (var db in new[] { -120f, -60f, 0f })
        {
            var expected = Colormaps.ToBgra(PaletteKind.Magma, db, -120f);
            var actual = PaletteLut.Sample(lut, db, -120f, 0f);
            Assert.InRange((byte)(actual >> 16), (byte)(expected >> 16) - 2, (byte)(expected >> 16) + 2);
            Assert.InRange((byte)(actual >> 8), (byte)(expected >> 8) - 2, (byte)(expected >> 8) + 2);
            Assert.InRange((byte)actual, (byte)expected - 2, (byte)expected + 2);
        }
    }

    [Fact]
    public void EvenStringAnchors_SpreadAcrossTheRange()
    {
        var reg = Load(("even.json", """{ "name": "Even", "anchors": ["#000000", "#FF0000", "#FFFFFF"] }"""));
        var lut = reg.BakeLut("even", -120f);
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(lut, 0));
        var (r, g, b) = At(lut, 0.5); // LUT quantization: within 2 of the stop
        Assert.InRange(r, 253, 255);
        Assert.InRange(g, 0, 2);
        Assert.InRange(b, 0, 2);
        Assert.Equal(((byte)255, (byte)255, (byte)255), At(lut, 1));
    }

    [Fact]
    public void AtStops_PlaceColorsUnevenly()
    {
        var reg = Load(("late.json", """
            { "name": "Late", "anchors": [
                { "color": "#000000", "at": 0.0 },
                { "color": "#FF0000", "at": 0.9 },
                { "color": "#FFFFFF", "at": 1.0 } ] }
            """));
        var lut = reg.BakeLut("Late", -120f);
        var (r, g, b) = At(lut, 0.45); // halfway to the 0.9 stop: 50% red
        Assert.InRange(r, 125, 130);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void DbStops_StayGluedToTheirDb_WhenTheFloorChanges()
    {
        var reg = Load(("pin.json", """
            { "name": "Pin", "anchors": [
                { "color": "#000000", "db": -120 },
                { "color": "#00FF00", "db": -60 },
                { "color": "#FFFFFF", "db": 0 } ] }
            """));
        // floor -120: -60 dB sits mid-range (within LUT quantization).
        var mid = At(reg.BakeLut("Pin", -120f), 0.5);
        Assert.InRange(mid.G, 253, 255);
        Assert.Equal(0, mid.R);
        // floor -80: -60 dB sits a quarter of the way up.
        var quarter = At(reg.BakeLut("Pin", -80f), 0.25);
        Assert.InRange(quarter.G, 253, 255);
        Assert.Equal(0, quarter.R);
    }

    [Fact]
    public void UnsortedStops_AreSorted()
    {
        var reg = Load(("rev.json", """
            { "name": "Rev", "anchors": [
                { "color": "#FFFFFF", "at": 1.0 },
                { "color": "#000000", "at": 0.0 } ] }
            """));
        var lut = reg.BakeLut("Rev", -120f);
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(lut, 0));
        Assert.Equal(((byte)255, (byte)255, (byte)255), At(lut, 1));
    }

    [Theory]
    [InlineData("""{ "name": "Mix", "anchors": [ { "color": "#000000", "at": 0 }, { "color": "#FFFFFF", "db": 0 } ] }""")]
    [InlineData("""{ "name": "MixForms", "anchors": [ "#000000", { "color": "#FFFFFF", "at": 1 } ] }""")]
    [InlineData("""{ "name": "BadHex", "anchors": ["#000000", "notacolor"] }""")]
    [InlineData("""{ "name": "Single", "anchors": ["#000000"] }""")]
    [InlineData("""not json at all""")]
    public void InvalidFiles_AreSkippedWithAReason(string json)
    {
        var reg = Load(("bad.json", json));
        Assert.Single(reg.Skipped);
        Assert.Contains("bad.json", reg.Skipped[0]);
        Assert.Equal(PaletteRegistry.LoadWithCustom(_dir + "-none").Names.Count, reg.Names.Count);
    }

    [Fact]
    public void BuiltInNameCollision_IsSkipped()
    {
        var reg = Load(("magma.json", """{ "name": "Magma", "anchors": ["#000000", "#FFFFFF"] }"""));
        Assert.Single(reg.Skipped);
        Assert.Single(reg.Names, n => n == "Magma");
    }

    [Fact]
    public void UnknownName_FallsBackToMagma()
    {
        var reg = PaletteRegistry.LoadWithCustom(_dir);
        Assert.Equal(reg.BakeLut("Magma", -120f), reg.BakeLut("NoSuchPalette", -120f));
        Assert.False(reg.Has("NoSuchPalette"));
        Assert.True(reg.Has("magma")); // case-insensitive
    }

    [Fact]
    public void CustomNames_ListAfterBuiltIns()
    {
        var reg = Load(("z.json", """{ "name": "Zebra", "anchors": ["#000000", "#FFFFFF"] }"""));
        Assert.Equal("Zebra", reg.Names[^1]);
        Assert.Contains("Magma", reg.Names);
        Assert.True(reg.Has("zebra"));
    }
}
