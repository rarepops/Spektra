using System.Globalization;
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
    public async Task Csv_FormatsNumbersInvariantly_UnderCommaDecimalCulture()
    {
        // A comma-decimal locale must not corrupt a comma-delimited CSV: numeric
        // fields stay '.'-separated whatever the machine culture. Guards the
        // explicit InvariantCulture in Reporting.Format against silent removal.
        var previous = CultureInfo.CurrentCulture;
        string csv;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            csv = Reporting.ToCsv(Rows);
        }
        finally { CultureInfo.CurrentCulture = previous; }

        await Assert.That(csv.Split('\n')[1]).IsEqualTo("a.flac,flac,44100,900000,180.5,Lossless,,,");
        await Assert.That(csv).DoesNotContain("180,5");
    }

    [Test]
    public async Task Csv_NeutralizesFormulaTriggers_InStringFieldsOnly()
    {
        // File names and tags are attacker-chosen the moment the music was
        // downloaded, and a cell starting with = + - or @ (or a stray tab) is
        // a formula to Excel and LibreOffice. RFC 4180 quoting does not help:
        // the cell is evaluated after unquoting. The guard is the spreadsheet
        // convention, a leading apostrophe, and applies to string fields only,
        // because a guarded "-180.5" would stop being a number.
        var rows = new BandwidthRow[]
        {
            new("=HYPERLINK(\"http://evil\").flac", "flac", 44100, 900000, -180.5,
                "Lossless", null, null, null),
            new("-=[FLAC]=- rip.mp3", "mp3", 44100, 128000, 200.0,
                "Lossy", 16000, "@guess", null),
        };
        var lines = Reporting.ToCsv(rows).Split('\n');
        await Assert.That(lines[1]).StartsWith("\"'=HYPERLINK(\"\"http://evil\"\").flac\"");
        await Assert.That(lines[1]).Contains(",-180.5,").Because("numeric fields keep their minus");
        await Assert.That(lines[2]).StartsWith("'-=[FLAC]=- rip.mp3");
        await Assert.That(lines[2]).Contains(",'@guess,");
    }

    [Test]
    public async Task Csv_GuardsIdenticallyOnBothSidesOfAJoin()
    {
        // audit and inventory exports join on their path column; the guard must
        // be a pure function of the value so a guarded path still matches itself.
        var name = "-=[FLAC]=-/track.flac";
        var audit = Reporting.ToCsv(new AuditRow[]
        {
            new(name, "flac", 44100, 2, 900000, 180.5, "Lossless", null, "Ok", 0, 0, false, null),
        }).Split('\n')[1];
        var bandwidth = Reporting.ToCsv(new BandwidthRow[]
        {
            new(name, "flac", 44100, 900000, 180.5, "Lossless", null, null, null),
        }).Split('\n')[1];
        await Assert.That(audit.Split(',')[0]).IsEqualTo("'" + name);
        await Assert.That(bandwidth.Split(',')[0]).IsEqualTo("'" + name);
    }

    [Test]
    public async Task Csv_QuotesBareCarriageReturns()
    {
        // File names cannot carry a CR, but tags can, and an unquoted CR mid
        // row bends the row shape for any parser that honours it.
        var rows = new BandwidthRow[]
        {
            new("a.flac", "fl\rac", 44100, 900000, 180.5, "Lossless", null, null, null),
        };
        await Assert.That(Reporting.ToCsv(rows).Split('\n')[1]).Contains("\"fl\rac\"");
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
    public async Task FormatBytes_ScalesUnits_WithInvariantDecimal()
    {
        await Assert.That(Reporting.FormatBytes(1536)).IsEqualTo("1.5 KB");
        await Assert.That(Reporting.FormatBytes(5L << 20)).IsEqualTo("5.0 MB");
        await Assert.That(Reporting.FormatBytes(3L << 30)).IsEqualTo("3.0 GB");
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
