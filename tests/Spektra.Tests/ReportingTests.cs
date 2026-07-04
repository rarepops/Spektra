using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class ReportingTests
{
    private static readonly BandwidthRow[] Rows =
    [
        new("a.flac", "flac", 44100, 900000, 180.5, "Lossless", null, null, null),
        new("b, live.mp3", "mp3", 44100, 128000, 200.0, "Lossy", 16000, "MP3 128 / AAC ~128", null),
    ];

    [Fact]
    public void Csv_HasHeaderInDeclarationOrder()
    {
        var lines = Reporting.ToCsv(Rows).Split('\n');
        Assert.Equal("File,Codec,SampleRateHz,BitrateBps,DurationSeconds,Verdict,CutoffHz,CodecGuess,Error",
            lines[0]);
    }

    [Fact]
    public void Csv_QuotesFieldsWithCommas_AndFormatsNumbers()
    {
        var lines = Reporting.ToCsv(Rows).Split('\n');
        Assert.Equal("a.flac,flac,44100,900000,180.5,Lossless,,,", lines[1]);
        Assert.StartsWith("\"b, live.mp3\",mp3,44100,128000,200,Lossy,16000,", lines[2]);
    }

    [Fact]
    public void Json_ContainsCamelCaseFieldsAndValues()
    {
        var json = Reporting.ToJson(Rows);
        Assert.Contains("\"file\": \"a.flac\"", json);
        Assert.Contains("\"verdict\": \"Lossless\"", json);
        Assert.Contains("\"cutoffHz\": 16000", json);
        Assert.Contains("\"error\": null", json);
    }

    [Fact]
    public void ToBandwidthRow_MapsErrorFile()
    {
        var row = Reporting.ToBandwidthRow(new FileReport("x/missing.wav", null, null, "not found"));
        Assert.Equal("missing.wav", row.File);
        Assert.Equal("Error", row.Verdict);
        Assert.Equal("not found", row.Error);
    }
}
