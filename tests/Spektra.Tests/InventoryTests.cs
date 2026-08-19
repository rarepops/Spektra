using Spektra.Core;

namespace Spektra.Tests;

/// The inventory is the handover format: everything Spektra can learn about a
/// folder without decoding it, one row per file. Its job is to save whoever
/// reads it from probing thousands of files themselves.
public class InventoryTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    [Test]
    public async Task Run_AudioFile_CarriesItsTagsAndArt()
    {
        var root = Directory.CreateTempSubdirectory("inv-tags").FullName;
        try
        {
            File.Copy(P("tagged-with-art.flac"), Path.Combine(root, "01 Intro.flac"));

            var rows = Inventory.Run(Ff, root, jobs: 2);

            var row = rows.Single();
            await Assert.That(row.IsAudio).IsTrue();
            await Assert.That(row.Ext).IsEqualTo("flac");
            await Assert.That(row.Codec).IsEqualTo("flac");
            await Assert.That(row.SampleRateHz).IsEqualTo(44100);
            await Assert.That(row.Artist).IsEqualTo("Aurora");
            await Assert.That(row.Album).IsEqualTo("First Light");
            await Assert.That(row.Track).IsEqualTo(5);
            await Assert.That(row.TrackTotal).IsEqualTo(12);
            await Assert.That(row.Year).IsEqualTo(2019);
            await Assert.That(row.HasEmbeddedArt).IsTrue();
            await Assert.That(row.ArtWidth).IsEqualTo(600);
            await Assert.That(row.Error).IsNull();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // A folder's cover.jpg is the other half of "is the artwork missing", and
    // it is answered by simply listing it rather than by columns about a
    // file's neighbours.
    [Test]
    public async Task Run_NonAudioFile_IsARowWithNoAudioFactsAndNoError()
    {
        var root = Directory.CreateTempSubdirectory("inv-mixed").FullName;
        try
        {
            File.Copy(P("tagged-with-art.flac"), Path.Combine(root, "01 Intro.flac"));
            File.WriteAllText(Path.Combine(root, "cover.jpg"), "not really a jpeg");

            var rows = Inventory.Run(Ff, root, jobs: 2);

            var cover = rows.Single(r => r.Name == "cover.jpg");
            await Assert.That(cover.IsAudio).IsFalse();
            await Assert.That(cover.Ext).IsEqualTo("jpg");
            await Assert.That(cover.SizeBytes).IsGreaterThan(0);
            await Assert.That(cover.Codec).IsNull();
            await Assert.That(cover.Artist).IsNull();
            await Assert.That(cover.HasEmbeddedArt).IsNull();
            // Never probed, so nothing went wrong. An Error here would read as
            // a broken file rather than a file that is simply not audio.
            await Assert.That(cover.Error).IsNull();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // The whole two-file workflow rests on this: verdicts stay in `audit`, so
    // an agent joins the two exports on one column. If the path forms drift
    // apart the join rots silently, which is the worst way for it to fail.
    [Test]
    public async Task Run_PathIsTheSameFormAuditExports()
    {
        var root = Directory.CreateTempSubdirectory("inv-join").FullName;
        try
        {
            var deep = Directory.CreateDirectory(Path.Combine(root, "Album", "CD1")).FullName;
            var file = Path.Combine(deep, "01 Intro.flac");
            File.Copy(P("tagged-with-art.flac"), file);

            var rows = Inventory.Run(Ff, root, jobs: 2);

            await Assert.That(rows.Single().Path)
                .IsEqualTo(Reporting.RelativeFile(root, file));
            // And that form is forward-slashed, so the join key does not
            // depend on which platform wrote the export.
            await Assert.That(rows.Single().Path).IsEqualTo("Album/CD1/01 Intro.flac");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Run_TruncatedAudioFile_IsARowWithAReason()
    {
        var root = Directory.CreateTempSubdirectory("inv-cut").FullName;
        try
        {
            // A part-downloaded FLAC. Measured: ffprobe exits 0, writes "I/O
            // error" to stderr and returns {} with no streams key, which the
            // reader rejects. Dropping the row would let a damaged file read
            // as absent, which is worse than a row with empty columns.
            var whole = File.ReadAllBytes(P("tagged-with-art.flac"));
            File.WriteAllBytes(Path.Combine(root, "cut.flac"), whole[..2000]);

            var rows = Inventory.Run(Ff, root, jobs: 2);

            var row = rows.Single();
            await Assert.That(row.IsAudio).IsTrue();
            await Assert.That(row.Error).IsNotNull();
            await Assert.That(row.Codec).IsNull();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // The subtler failure, and the one a library actually accumulates: a
    // zero-byte file left by a failed download. Measured, not assumed:
    // ffprobe EXITS 0 on it and reports a flac stream with sample rate 0,
    // channels 0 and duration 0. Passing that through as facts would tell a
    // reader the file is a real track, so a probe that returns nothing usable
    // is an error even though nothing threw.
    [Test]
    public async Task Run_ZeroByteAudioFile_IsFlagged_NotReportedAsATrack()
    {
        var root = Directory.CreateTempSubdirectory("inv-empty").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(root, "failed-download.flac"), []);

            var rows = Inventory.Run(Ff, root, jobs: 2);

            var row = rows.Single();
            await Assert.That(row.IsAudio).IsTrue();
            await Assert.That(row.Error).IsNotNull();
            await Assert.That(row.Codec).IsNull();
            await Assert.That(row.SampleRateHz).IsNull();
            await Assert.That(row.DurationSeconds).IsNull();
            // The size is a real fact and stays: 0 bytes is the diagnosis.
            await Assert.That(row.SizeBytes).IsEqualTo(0);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // A contract pin rather than a driven test: the CSV header IS the export's
    // interface, and Reporting.ToCsv takes column order from the record's
    // declaration order. Reordering a field would silently shift every column
    // for everyone already reading these files, with no error anywhere.
    // Adding a column at the END is safe and this test should then be updated.
    [Test]
    public async Task CsvHeader_IsTheDocumentedColumnOrder()
    {
        var header = Reporting.ToCsv(Array.Empty<InventoryRow>()).Split('\n')[0].TrimEnd('\r');
        await Assert.That(header).IsEqualTo(
            "Path,Name,Ext,SizeBytes,IsAudio," +
            "Codec,SampleRateHz,Channels,BitsPerSample,BitrateBps,DurationSeconds," +
            "Artist,AlbumArtist,Album,Title,Track,TrackTotal,Disc,DiscTotal,Year,Genre," +
            "HasEmbeddedArt,ArtFormat,ArtWidth,ArtHeight,Error");
    }

    [Test]
    public async Task Run_DirectoriesAreNotRows()
    {
        var root = Directory.CreateTempSubdirectory("inv-dirs").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Album"));
            Directory.CreateDirectory(Path.Combine(root, "Album", "Scans"));
            File.Copy(P("tagged-with-art.flac"), Path.Combine(root, "Album", "01 Intro.flac"));

            var rows = Inventory.Run(Ff, root, jobs: 2);

            await Assert.That(rows.Count).IsEqualTo(1);
            await Assert.That(rows.Single().Name).IsEqualTo("01 Intro.flac");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
