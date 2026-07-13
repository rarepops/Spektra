using Spektra.Core;

namespace Spektra.Tests;

public class ReportingTests
{
    private static readonly BandwidthRow[] Rows =
    [
        new("a.flac", "flac", 44100, 900000, 180.5, "Lossless", null, null, null),
        new("b, live.mp3", "mp3", 44100, 128000, 200.0, "Lossy", 16000, "MP3 128 / AAC ~128", null),
    ];

    [Test]
    public async Task Csv_HasHeaderInDeclarationOrder()
    {
        var lines = Reporting.ToCsv(Rows).Split('\n');
        await Assert.That(lines[0])
            .IsEqualTo("File,Codec,SampleRateHz,BitrateBps,DurationSeconds,Verdict,CutoffHz,CodecGuess,Error");
    }

    [Test]
    public async Task Csv_QuotesFieldsWithCommas_AndFormatsNumbers()
    {
        var lines = Reporting.ToCsv(Rows).Split('\n');
        await Assert.That(lines[1]).IsEqualTo("a.flac,flac,44100,900000,180.5,Lossless,,,");
        await Assert.That(lines[2]).StartsWith("\"b, live.mp3\",mp3,44100,128000,200,Lossy,16000,");
    }

    [Test]
    public async Task Json_ContainsCamelCaseFieldsAndValues()
    {
        var json = Reporting.ToJson(Rows);
        await Assert.That(json).Contains("\"file\": \"a.flac\"");
        await Assert.That(json).Contains("\"verdict\": \"Lossless\"");
        await Assert.That(json).Contains("\"cutoffHz\": 16000");
        await Assert.That(json).Contains("\"error\": null");
    }

    [Test]
    public async Task ToBandwidthRow_MapsErrorFile()
    {
        var row = Reporting.ToBandwidthRow(new FileReport("x/missing.wav", null, null, "not found"));
        await Assert.That(row.File).IsEqualTo("missing.wav");
        await Assert.That(row.Verdict).IsEqualTo("Error");
        await Assert.That(row.Error).IsEqualTo("not found");
    }

    [Test]
    public async Task RelativeFile_UsesForwardSlashesUnderTheRoot()
    {
        var root = Path.Combine("D:", "Music");
        var path = Path.Combine(root, "Album", "CD2", "03.wav");
        await Assert.That(Reporting.RelativeFile(root, path)).IsEqualTo("Album/CD2/03.wav");
    }

    [Test]
    public async Task RelativeFile_FileDirectlyInRoot_IsTheBareName()
    {
        var root = Path.Combine("D:", "Music");
        await Assert.That(Reporting.RelativeFile(root, Path.Combine(root, "single.wav")))
            .IsEqualTo("single.wav");
    }
}
