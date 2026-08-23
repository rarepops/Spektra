using Spektra.Core;

namespace Spektra.Tests;

public class FfprobeMetadataReaderTests
{
    private static readonly string Fixtures =
        Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static FfprobeMetadataReader Reader()
        => new(FfmpegLocator.Locate([])!.FfprobePath);

    [Test]
    public async Task ReadsFlacMetadata()
    {
        var m = Reader().Read(Path.Combine(Fixtures, "sine-1khz.flac"));
        await Assert.That(m.Codec).IsEqualTo("flac");
        await Assert.That(m.SampleRate).IsEqualTo(44100);
        await Assert.That(m.Channels).IsEqualTo(1);
        await Assert.That(m.BitsPerSample).IsEqualTo(16);
        await Assert.That(m.Duration.TotalSeconds).IsBetween(2.9, 3.1);
    }

    [Test]
    public async Task ReadsMp3Metadata_NoBitDepth_HasBitrate()
    {
        var m = Reader().Read(Path.Combine(Fixtures, "sine-1khz.mp3"));
        await Assert.That(m.Codec).IsEqualTo("mp3");
        await Assert.That(m.BitsPerSample).IsNull();
        await Assert.That(m.BitRateBps).IsNotNull();
        await Assert.That(m.BitRateBps!.Value).IsBetween(100_000, 160_000);
    }

    [Test]
    public async Task NonAudioFile_Throws()
    {
        var ex = Assert.Throws<AudioDecodeException>(
            () => { Reader().Read(Path.Combine(Fixtures, "notaudio.txt")); });
        await Assert.That(string.IsNullOrEmpty(ex.Message)).IsFalse();
    }

    [Test]
    public async Task PreCancelled_ThrowsWithoutSpawningFfprobe()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.That(() => new FfprobeMetadataReader(@"C:\nope\ffprobe.exe")
            .Read(@"C:\nope\file.wav", cts.Token)).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Parse_MalformedJson_ReadsAsDecodeError()
    {
        var ex = Assert.Throws<AudioDecodeException>(
            () => { FfprobeMetadataReader.Parse("garbage {", "boom from stderr"); });
        await Assert.That(ex.Message).Contains("unreadable");
    }

    // ParseDocument already turns non-JSON into a decode error; these pin the
    // shapes that ARE valid JSON but not the report's (a probe killed mid
    // write, or a different program named ffprobe answering on PATH). They
    // must read as a per-file decode error too, not escape the audit
    // pipeline's catch lists as an InvalidOperationException.
    [Test]
    [Arguments("[]")]
    [Arguments("\"streams\"")]
    [Arguments("""{"streams":{}}""")]
    [Arguments("""{"streams":[]}""")]
    [Arguments("""{"streams":[3,"x"]}""")]
    public async Task Parse_WrongShapedJson_ReadsAsDecodeError(string json)
    {
        await Assert.That(() => FfprobeMetadataReader.Parse(json, ""))
            .Throws<AudioDecodeException>();
    }

    [Test]
    public async Task Parse_SkipsNonObjectStreamEntries()
    {
        const string json = """
            {"streams":[7,{"codec_name":"flac","codec_type":"audio","sample_rate":"44100","channels":2}],
             "format":{"duration":"10.0"}}
            """;
        await Assert.That(FfprobeMetadataReader.Parse(json, "").Codec).IsEqualTo("flac");
    }

    // A real probe writes channels as a JSON number; string digits still
    // count, and garbage reads as 0 (the "parsed nothing" marker the callers
    // already treat as no readable stream) rather than throwing.
    [Test]
    public async Task Parse_ChannelsAsStringOrGarbage_NeverThrows()
    {
        const string asString = """
            {"streams":[{"codec_name":"flac","sample_rate":"44100","channels":"2"}],
             "format":{"duration":"10.0"}}
            """;
        await Assert.That(FfprobeMetadataReader.Parse(asString, "").Channels).IsEqualTo(2);
        const string asObject = """
            {"streams":[{"codec_name":"flac","sample_rate":"44100","channels":{}}],
             "format":{"duration":"10.0"}}
            """;
        await Assert.That(FfprobeMetadataReader.Parse(asObject, "").Channels).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_ValidPayload_MapsFields()
    {
        var json = """
            {
              "streams": [ { "codec_name": "flac", "sample_rate": "44100",
                             "channels": 2, "bits_per_raw_sample": "16" } ],
              "format": { "duration": "3.0", "bit_rate": "900000" }
            }
            """;
        var m = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(m.Codec).IsEqualTo("flac");
        await Assert.That(m.SampleRate).IsEqualTo(44100);
        await Assert.That(m.Channels).IsEqualTo(2);
        await Assert.That(m.BitsPerSample).IsEqualTo(16);
        await Assert.That(m.BitRateBps).IsEqualTo(900_000L);
        await Assert.That(m.Duration.TotalSeconds).IsCloseTo(3.0, 0.001);
        await Assert.That(m.DurationIsEstimated).IsFalse();
    }

    [Test]
    public async Task DisplayLine_FormatsSegments()
    {
        var m = new AudioMetadata("flac", 44100, 2, 16, 1_017_000, TimeSpan.FromSeconds(252));
        await Assert.That(m.ToDisplayLine("song.flac"))
            .IsEqualTo("song.flac - FLAC · 44.1 kHz · 16-bit · 2 ch · 4:12 · 1017 kbps");

        var lossy = new AudioMetadata("mp3", 48000, 2, null, null, TimeSpan.FromSeconds(3661));
        await Assert.That(lossy.ToDisplayLine("x.mp3")).IsEqualTo("x.mp3 - MP3 · 48 kHz · 2 ch · 1:01:01");
    }

    [Test]
    public async Task Parse_ReadsFormatTags_CaseInsensitively()
    {
        const string json = """
            {"streams":[{"codec_name":"flac","sample_rate":"44100","channels":2}],
             "format":{"duration":"10.0","tags":{"ARTIST":"Nightwish","Title":"Amaranth","album":"Dark Passion Play"}}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.Artist).IsEqualTo("Nightwish");
        await Assert.That(meta.Title).IsEqualTo("Amaranth");
        await Assert.That(meta.Album).IsEqualTo("Dark Passion Play");
    }

    [Test]
    public async Task Parse_StreamTags_FillInWhenFormatTagsMissing()
    {
        const string json = """
            {"streams":[{"codec_name":"vorbis","sample_rate":"44100","channels":2,
                         "tags":{"artist":"Solar Fields","title":"Sol"}}],
             "format":{"duration":"10.0"}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.Artist).IsEqualTo("Solar Fields");
        await Assert.That(meta.Title).IsEqualTo("Sol");
        await Assert.That(meta.Album).IsNull();
    }

    [Test]
    public async Task Parse_NoTags_YieldsNulls()
    {
        const string json = """
            {"streams":[{"codec_name":"flac","sample_rate":"44100","channels":2}],
             "format":{"duration":"10.0"}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.Artist).IsNull();
        await Assert.That(meta.Title).IsNull();
    }

    // Cover art is a video stream, so the probe can no longer filter to audio.
    // That makes stream ORDER a hazard: reading streams[0] was safe only while
    // the filter guaranteed what sat there. A container whose picture sorts
    // first would otherwise report codec "png", sample rate 0 and channels 0,
    // silently wrong in every field rather than throwing.
    [Test]
    public async Task Parse_FindsTheAudioStreamWhenThePictureSortsFirst()
    {
        const string json = """
            {"streams":[
                {"codec_name":"png","codec_type":"video","width":600,"height":600,
                 "disposition":{"attached_pic":1}},
                {"codec_name":"flac","codec_type":"audio","sample_rate":"44100",
                 "channels":2,"bits_per_raw_sample":"16"}],
             "format":{"duration":"180.5","bit_rate":"1000000"}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.Codec).IsEqualTo("flac");
        await Assert.That(meta.SampleRate).IsEqualTo(44100);
        await Assert.That(meta.Channels).IsEqualTo(2);
        await Assert.That(meta.BitsPerSample).IsEqualTo(16);
    }

    [Test]
    public async Task Parse_ReadsEmbeddedArt()
    {
        const string json = """
            {"streams":[
                {"codec_name":"flac","codec_type":"audio","sample_rate":"44100","channels":2},
                {"codec_name":"mjpeg","codec_type":"video","width":1400,"height":1400,
                 "disposition":{"attached_pic":1}}],
             "format":{"duration":"10.0"}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.HasEmbeddedArt).IsTrue();
        await Assert.That(meta.ArtFormat).IsEqualTo("mjpeg");
        await Assert.That(meta.ArtWidth).IsEqualTo(1400);
        await Assert.That(meta.ArtHeight).IsEqualTo(1400);
    }

    [Test]
    public async Task Parse_NoPicture_ReportsNoArt()
    {
        const string json = """
            {"streams":[{"codec_name":"flac","codec_type":"audio","sample_rate":"44100","channels":2}],
             "format":{"duration":"10.0"}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.HasEmbeddedArt).IsFalse();
        await Assert.That(meta.ArtFormat).IsNull();
        await Assert.That(meta.ArtWidth).IsNull();
    }

    // A video stream that is NOT an attached picture is real video (a music
    // video, a stray track in an MKV), not artwork. Calling it a thumbnail
    // would tell a librarian the file has a cover when it does not.
    [Test]
    public async Task Parse_VideoStreamThatIsNotAPicture_IsNotArt()
    {
        const string json = """
            {"streams":[
                {"codec_name":"flac","codec_type":"audio","sample_rate":"44100","channels":2},
                {"codec_name":"h264","codec_type":"video","width":1920,"height":1080,
                 "disposition":{"attached_pic":0}}],
             "format":{"duration":"10.0"}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.HasEmbeddedArt).IsFalse();
        await Assert.That(meta.ArtFormat).IsNull();
    }

    [Test]
    public async Task Parse_ReadsTheTagsAnInventoryNeeds()
    {
        const string json = """
            {"streams":[{"codec_name":"flac","codec_type":"audio","sample_rate":"44100","channels":2}],
             "format":{"duration":"10.0","tags":{
                "artist":"Aurora","album_artist":"Aurora","album":"First Light",
                "title":"Intro","track":"5/12","disc":"1/2",
                "date":"2019-04-12","genre":"Ambient"}}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.AlbumArtist).IsEqualTo("Aurora");
        await Assert.That(meta.Genre).IsEqualTo("Ambient");
        await Assert.That(meta.Track).IsEqualTo(5);
        await Assert.That(meta.TrackTotal).IsEqualTo(12);
        await Assert.That(meta.Disc).IsEqualTo(1);
        await Assert.That(meta.DiscTotal).IsEqualTo(2);
        await Assert.That(meta.Year).IsEqualTo(2019);
    }

    // FLAC written with Vorbis comments keeps the total in a tag of its own.
    // Verified against a real ffmpeg-written file: TRACKNUMBER normalizes to
    // "track" and ALBUMARTIST to "album_artist", but TOTALTRACKS is left
    // alone and GENRE keeps its uppercase name. Without this the commonest
    // lossless library in the world reports "track 5 of unknown".
    [Test]
    public async Task Parse_VorbisTotalsLiveInTheirOwnTags()
    {
        const string json = """
            {"streams":[{"codec_name":"flac","codec_type":"audio","sample_rate":"44100","channels":2}],
             "format":{"duration":"10.0","tags":{
                "track":"5","TOTALTRACKS":"12",
                "disc":"1","TOTALDISCS":"2",
                "GENRE":"Ambient"}}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.Track).IsEqualTo(5);
        await Assert.That(meta.TrackTotal).IsEqualTo(12);
        await Assert.That(meta.Disc).IsEqualTo(1);
        await Assert.That(meta.DiscTotal).IsEqualTo(2);
        await Assert.That(meta.Genre).IsEqualTo("Ambient");
    }

    // "5/12" wins over a separate total tag when both are present: it is the
    // more specific statement, and a file carrying both that disagree is
    // better read one way consistently than half from each.
    // Every art test above feeds Parse synthetic JSON, which cannot prove the
    // real probe ARGUMENTS let a picture stream through: -select_streams a
    // discards it before any JSON is parsed. Only a real file can pin that.
    [Test]
    public async Task Read_RealFileWithEmbeddedArt_SeesThePicture()
    {
        var m = Reader().Read(Path.Combine(Fixtures, "tagged-with-art.flac"));
        await Assert.That(m.Codec).IsEqualTo("flac");
        await Assert.That(m.HasEmbeddedArt).IsTrue();
        await Assert.That(m.ArtFormat).IsEqualTo("png");
        await Assert.That(m.ArtWidth).IsEqualTo(600);
        await Assert.That(m.ArtHeight).IsEqualTo(600);
    }

    [Test]
    public async Task Read_RealFileWithTags_NormalizesThem()
    {
        var m = Reader().Read(Path.Combine(Fixtures, "tagged-with-art.flac"));
        await Assert.That(m.Artist).IsEqualTo("Aurora");
        await Assert.That(m.AlbumArtist).IsEqualTo("Aurora");
        await Assert.That(m.Album).IsEqualTo("First Light");
        await Assert.That(m.Title).IsEqualTo("Intro");
        await Assert.That(m.Genre).IsEqualTo("Ambient");
        await Assert.That(m.Track).IsEqualTo(5);
        await Assert.That(m.TrackTotal).IsEqualTo(12);
        await Assert.That(m.Disc).IsEqualTo(1);
        await Assert.That(m.DiscTotal).IsEqualTo(2);
        await Assert.That(m.Year).IsEqualTo(2019);
    }

    [Test]
    public async Task Parse_SlashTotalWinsOverASeparateTotalTag()
    {
        const string json = """
            {"streams":[{"codec_name":"flac","codec_type":"audio","sample_rate":"44100","channels":2}],
             "format":{"duration":"10.0","tags":{"track":"5/12","TOTALTRACKS":"99"}}}
            """;
        var meta = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(meta.TrackTotal).IsEqualTo(12);
    }
}
