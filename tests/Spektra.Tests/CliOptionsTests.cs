using Spektra.Core;

namespace Spektra.Tests;

public class CliOptionsTests
{
    [Test]
    public async Task Take_ParsesFormat_Jobs_AndRest()
    {
        var (fmt, jobs, rest) = CliOptions.Take(["--json", "song.flac", "--jobs", "4"], defaultJobs: 8);
        await Assert.That(fmt).IsEqualTo(OutFormat.Json);
        await Assert.That(jobs).IsEqualTo(4);
        await Assert.That(rest).IsEquivalentTo(new[] { "song.flac" });
    }

    [Test]
    public async Task Take_NoFlags_DefaultsToTextAndDefaultJobs()
    {
        var (fmt, jobs, rest) = CliOptions.Take(["a.wav", "b.wav"], defaultJobs: 8);
        await Assert.That(fmt).IsEqualTo(OutFormat.Text);
        await Assert.That(jobs).IsEqualTo(8);
        await Assert.That(rest.Length).IsEqualTo(2);
    }

    [Test]
    public async Task Take_PassesVerbSpecificFlagsThrough()
    {
        // Verb flags (--palette, --fresh) are not global, so Take leaves them in
        // Positional for the verb to parse rather than rejecting them.
        var (_, _, positional) = CliOptions.Take(["--palette", "turbo", "song.wav"], defaultJobs: 8);
        await Assert.That(positional).IsEquivalentTo(new[] { "--palette", "turbo", "song.wav" });
    }

    [Test]
    public async Task RejectUnknownFlags_OnAFlagToken_Throws()
    {
        // A mistyped flag (--jso for --json) left among a verb's positionals must
        // error, not be silently ignored.
        await Assert.That(() => CliOptions.RejectUnknownFlags(["Music", "--jso"]))
            .Throws<OptionException>();
    }

    [Test]
    public async Task RejectUnknownFlags_AllPositional_DoesNotThrow()
    {
        await Assert.That(() => CliOptions.RejectUnknownFlags(["a.flac", "b.flac"])).ThrowsNothing();
    }

    [Test]
    public async Task Inputs_OneFolder_IsFolderMode()
    {
        using var scratch = new Scratch();
        var dir = scratch.NewFolder("album");

        var inputs = CliOptions.Inputs([dir]);

        await Assert.That(inputs.Folder).IsEqualTo(dir);
        await Assert.That(inputs.Files.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Inputs_OneFolderWithTrailingSeparator_IsFolderMode()
    {
        using var scratch = new Scratch();
        var dir = scratch.NewFolder("album");

        var inputs = CliOptions.Inputs([dir + Path.DirectorySeparatorChar]);

        await Assert.That(inputs.Folder).IsNotNull();
    }

    [Test]
    public async Task Inputs_Files_AreFiles()
    {
        using var scratch = new Scratch();
        var a = scratch.NewFile("a.flac");
        var b = scratch.NewFile("b.wav");

        var inputs = CliOptions.Inputs([a, b]);

        await Assert.That(inputs.Folder).IsNull();
        await Assert.That(string.Join("|", inputs.Files)).IsEqualTo($"{a}|{b}");
    }

    [Test]
    public async Task Inputs_MissingPath_Throws_NamingIt()
    {
        using var scratch = new Scratch();
        var file = scratch.NewFile("a.flac");
        var missing = scratch.Missing("missing.flac");

        var amongFiles = Assert.Throws<OptionException>(() => { CliOptions.Inputs([file, missing]); });
        await Assert.That(amongFiles.Message).Contains(missing);

        // A mistyped folder, trailing separator and all, is named as typed.
        var typed = missing + Path.DirectorySeparatorChar;
        var alone = Assert.Throws<OptionException>(() => { CliOptions.Inputs([typed]); });
        await Assert.That(alone.Message).Contains(typed);
    }

    [Test]
    public async Task Inputs_FolderAmongOthers_Throws()
    {
        using var scratch = new Scratch();
        var dirA = scratch.NewFolder("a");
        var dirB = scratch.NewFolder("b");
        var file = scratch.NewFile("song.flac");

        var twoFolders = Assert.Throws<OptionException>(() => { CliOptions.Inputs([dirA, dirB]); });
        await Assert.That(twoFolders.Message).Contains("is a folder");

        var folderFirst = Assert.Throws<OptionException>(() => { CliOptions.Inputs([dirA, file]); });
        await Assert.That(folderFirst.Message).Contains("is a folder");

        // Position does not matter, and the message names the folder, not the file.
        var folderLast = Assert.Throws<OptionException>(() => { CliOptions.Inputs([file, dirA]); });
        await Assert.That(folderLast.Message).Contains("is a folder");
        await Assert.That(folderLast.Message).Contains(dirA);
    }

    [Test]
    public async Task Inputs_Empty_IsEmpty()
    {
        // No paths is the verb's own "give one or more audio files or a folder."
        // exit 2, not a complaint from here.
        var inputs = CliOptions.Inputs([]);

        await Assert.That(inputs.Folder).IsNull();
        await Assert.That(inputs.Files.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Inputs_UnknownFlag_StillRejectedFirst()
    {
        using var scratch = new Scratch();
        var missing = scratch.Missing("missing.flac");

        // The mistyped flag is named even when a path is also wrong.
        var ex = Assert.Throws<OptionException>(() => { CliOptions.Inputs([missing, "--jso"]); });
        await Assert.That(ex.Message).IsEqualTo("unknown option '--jso'.");
    }

    [Test]
    public async Task Value_NextTokenIsAnotherFlag_Throws()
    {
        // "--palette --gamma x" must not consume "--gamma" as the palette value
        // and silently drop the real flag.
        await Assert.That(() => Consume(["--palette", "--gamma"])).Throws<OptionException>();

        static string Consume(string[] args)
        {
            var i = 0;
            return CliOptions.Value("--palette", args, ref i);
        }
    }

    [Test]
    public async Task Value_NegativeNumber_IsAValueNotAFlag()
    {
        // --floor takes a dB level (default -120) and --offset a signed
        // millisecond count, so a leading '-' on a NUMBER must not read as a
        // flag. Only a non-numeric '-' token is a flag (see the --palette case).
        var i = 0;
        await Assert.That(CliOptions.Value("--floor", ["--floor", "-100"], ref i)).IsEqualTo("-100");

        var j = 0;
        await Assert.That(CliOptions.Float("--floor", ["--floor", "-100.5"], ref j)).IsEqualTo(-100.5f);

        var k = 0;
        await Assert.That(CliOptions.Double("--offset", ["--offset", "-500"], ref k)).IsEqualTo(-500.0);
    }

    [Test]
    public async Task Value_MissingValue_Throws()
    {
        await Assert.That(() => Consume(["--palette"])).Throws<OptionException>();

        static string Consume(string[] args)
        {
            var i = 0;
            return CliOptions.Value("--palette", args, ref i);
        }
    }

    [Test]
    public async Task ParseDiff_Defaults()
    {
        var parsed = CliOptions.ParseDiff(["a.flac", "b.mp3"]);
        await Assert.That(string.Join("|", parsed.Files)).IsEqualTo("a.flac|b.mp3");
        await Assert.That(parsed.OffsetMs).IsNull();
        await Assert.That(parsed.ThresholdDb).IsEqualTo(40f);
    }

    [Test]
    public async Task ParseDiff_ReadsBothFlags_WhereverTheySit()
    {
        // A negative offset is a value, not the next flag.
        var parsed = CliOptions.ParseDiff(["--offset", "-12.5", "a.flac", "--threshold-db", "20", "b.mp3"]);
        await Assert.That(string.Join("|", parsed.Files)).IsEqualTo("a.flac|b.mp3");
        await Assert.That(parsed.OffsetMs).IsEqualTo(-12.5);
        await Assert.That(parsed.ThresholdDb).IsEqualTo(20f);
    }

    [Test]
    public async Task ParseDiff_UnknownFlag_Throws()
    {
        var ex = Assert.Throws<OptionException>(() => { CliOptions.ParseDiff(["a.flac", "--ofset", "3", "b.mp3"]); });
        await Assert.That(ex.Message).IsEqualTo("unknown option '--ofset'.");
    }

    [Test]
    public async Task ParseDiff_FlagWithoutAValue_Throws()
    {
        var ex = Assert.Throws<OptionException>(() => { CliOptions.ParseDiff(["a.flac", "b.mp3", "--offset"]); });
        await Assert.That(ex.Message).IsEqualTo("--offset needs a value.");
    }

    [Test]
    public async Task ParseImage_Defaults()
    {
        var parsed = CliOptions.ParseImage(["a.flac"]);
        await Assert.That(string.Join("|", parsed.Files)).IsEqualTo("a.flac");
        await Assert.That(parsed.OutPath).IsNull();
        await Assert.That(parsed.PaletteName).IsNull();
        await Assert.That(parsed.Gamma).IsNull();
        await Assert.That(parsed.Options).IsEqualTo(new ImageOptions());
    }

    [Test]
    public async Task ParseImage_ReadsEveryFlag()
    {
        var parsed = CliOptions.ParseImage(
        [
            "--out", "x.png", "--palette", "magma", "--gamma", "1.5", "--floor", "-100",
            "--fft", "1024", "--channel", "2", "--columns", "512", "a.flac",
        ]);
        await Assert.That(parsed.OutPath).IsEqualTo("x.png");
        await Assert.That(parsed.PaletteName).IsEqualTo("magma");
        await Assert.That(parsed.Gamma).IsEqualTo(1.5);
        await Assert.That(parsed.Options.FloorDb).IsEqualTo(-100f);
        await Assert.That(parsed.Options.WindowSize).IsEqualTo(1024);
        // The flag counts channels from 1, the decoder from 0.
        await Assert.That(parsed.Options.Channel).IsEqualTo(1);
        await Assert.That(parsed.Options.MaxColumns).IsEqualTo(512);
        await Assert.That(string.Join("|", parsed.Files)).IsEqualTo("a.flac");
    }

    [Test]
    public async Task ParseImage_ShortOutFlag_IsTheSameFlag()
    {
        await Assert.That(CliOptions.ParseImage(["-o", "y.png", "a.flac"]).OutPath).IsEqualTo("y.png");
    }

    [Test]
    [Arguments("--gamma", "0", "--gamma must be greater than 0, got '0'.")]
    [Arguments("--gamma", "-2", "--gamma must be greater than 0, got '-2'.")]
    [Arguments("--fft", "8", "--fft must be a whole number >= 16, got '8'.")]
    [Arguments("--channel", "0", "--channel must be a whole number >= 1, got '0'.")]
    [Arguments("--columns", "0", "--columns must be a whole number >= 1, got '0'.")]
    public async Task ParseImage_OutOfRange_Throws(string flag, string value, string message)
    {
        var ex = Assert.Throws<OptionException>(() => { CliOptions.ParseImage([flag, value, "a.flac"]); });
        await Assert.That(ex.Message).IsEqualTo(message);
    }

    [Test]
    public async Task ParseImage_UnknownFlag_Throws()
    {
        var ex = Assert.Throws<OptionException>(() => { CliOptions.ParseImage(["a.flac", "--pallete", "magma"]); });
        await Assert.That(ex.Message).IsEqualTo("unknown option '--pallete'.");
    }

    [Test]
    public async Task ImageOutPath_Explicit_IsReturnedEvenWhenItExists()
    {
        using var scratch = new Scratch();
        var input = scratch.NewFile("song.flac");
        var existing = scratch.NewFile("out.png");

        // -o names the file, so replacing it is what was asked for.
        await Assert.That(CliOptions.ImageOutPath(input, existing)).IsEqualTo(existing);
    }

    [Test]
    public async Task ImageOutPath_Default_IsBesideTheInput()
    {
        using var scratch = new Scratch();
        var input = scratch.NewFile("song.flac");

        await Assert.That(CliOptions.ImageOutPath(input, null)).IsEqualTo(scratch.Missing("song.png"));
    }

    [Test]
    public async Task ImageOutPath_DefaultExists_Throws()
    {
        using var scratch = new Scratch();
        var input = scratch.NewFile("song.flac");
        var png = scratch.NewFile("song.png", "keep me");

        var ex = Assert.Throws<OptionException>(() => { CliOptions.ImageOutPath(input, null); });

        // The message names what is in the way and the way out. The spaces keep
        // a path that happens to contain "-o" from satisfying the second check.
        await Assert.That(ex.Message).Contains(png);
        await Assert.That(ex.Message).Contains(" -o ");
        // Refusing leaves the file exactly as it was.
        await Assert.That(File.ReadAllText(png)).IsEqualTo("keep me");
    }

    [Test]
    public async Task ImageOutPath_InputIsAPng_Throws()
    {
        using var scratch = new Scratch();
        var cover = scratch.NewFile("cover.png");

        // Without -o the default output is the input itself, so rendering it
        // would overwrite the only copy.
        var ex = Assert.Throws<OptionException>(() => { CliOptions.ImageOutPath(cover, null); });
        await Assert.That(ex.Message).Contains(cover);
    }

    [Test]
    public async Task ImageOutPath_DefaultIsAFolder_Throws()
    {
        using var scratch = new Scratch();
        var input = scratch.NewFile("song.flac");
        var folder = scratch.NewFolder("song.png");

        // A folder in the way is as unwritable as a file the caller never offered.
        var ex = Assert.Throws<OptionException>(() => { CliOptions.ImageOutPath(input, null); });
        await Assert.That(ex.Message).Contains(folder);
    }

    [Test]
    public async Task ImageOutPath_ExplicitIsTheInput_Throws()
    {
        using var scratch = new Scratch();
        var input = scratch.NewFile("song.wav", "audio");

        // The audio would be decoded and then replaced by its own spectrogram,
        // so -o may not name it. By the same spelling (the message says why):
        var same = Assert.Throws<OptionException>(() => { CliOptions.ImageOutPath(input, input); });
        await Assert.That(same.Message).StartsWith("-o ");
        await Assert.That(same.Message).Contains("is the input file");

        // in another case (ignored on every platform: a false refusal on a
        // case-sensitive file system only costs another name, a missed one the audio):
        Assert.Throws<OptionException>(() => { CliOptions.ImageOutPath(input, input.ToUpperInvariant()); });

        // through a detour that lands on the same file:
        var detour = Path.Combine(Path.GetDirectoryName(input)!, "elsewhere", "..", "song.wav");
        Assert.Throws<OptionException>(() => { CliOptions.ImageOutPath(input, detour); });

        // and as relative names, which both resolve against the working directory:
        Assert.Throws<OptionException>(() => { CliOptions.ImageOutPath("track.flac", Path.Combine(".", "track.flac")); });
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("   ")]
    [Arguments("\t")]
    public async Task ImageOutPath_EmptyExplicit_Throws(string blank)
    {
        // An empty -o used to reach File.Create("") after the whole render, and crash.
        // Spaces only are as unusable, and Path.GetFullPath throws on them on Windows.
        var ex = Assert.Throws<OptionException>(() => { CliOptions.ImageOutPath("song.flac", blank); });
        await Assert.That(ex.Message).IsEqualTo("-o needs a file path.");
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("   ")]
    public async Task ImageOutPath_EmptyInput_ReturnsTheExplicitPath(string blank)
    {
        // `image "" -o out.png` is the render's to refuse (exit 2, "File not found:"):
        // the same-file comparison must not turn it into a crash. Path.GetFullPath
        // throws on an empty or all-spaces path (on Windows).
        await Assert.That(CliOptions.ImageOutPath(blank, "out.png")).IsEqualTo("out.png");
    }

    [Test]
    public async Task OpenImageOutput_Default_RefusesAFileThatAppeared()
    {
        using var scratch = new Scratch();
        var input = scratch.NewFile("song.flac");
        var png = scratch.NewFile("song.png", "keep me");

        // The name is taken by the time the decode is done: the refusal is the one the
        // user gets up front, and what was there is not touched.
        var upFront = Assert.Throws<OptionException>(() => { CliOptions.ImageOutPath(input, null); });
        var late = Assert.Throws<OptionException>(() => { using var stream = CliOptions.OpenImageOutput(png, replace: false); });

        await Assert.That(late.Message).IsEqualTo(upFront.Message);
        await Assert.That(late.Message).Contains(png);
        await Assert.That(File.ReadAllText(png)).IsEqualTo("keep me");
    }

    [Test]
    public async Task OpenImageOutput_Explicit_ReplacesTheFile()
    {
        using var scratch = new Scratch();
        var png = scratch.NewFile("out.png", "an older, longer render");

        using (var stream = CliOptions.OpenImageOutput(png, replace: true))
            stream.Write("new"u8);

        await Assert.That(File.ReadAllText(png)).IsEqualTo("new");
    }

    [Test]
    public async Task OpenImageOutput_Default_CreatesAFreeName()
    {
        using var scratch = new Scratch();
        var png = scratch.Missing("song.png");

        using (var stream = CliOptions.OpenImageOutput(png, replace: false))
            stream.Write("new"u8);

        await Assert.That(File.ReadAllText(png)).IsEqualTo("new");
    }

    [Test]
    public async Task OpenImageOutput_Default_OtherFailuresKeepTheirOwnError()
    {
        using var scratch = new Scratch();
        // The folder is missing, so nothing is "already there": the OS error must not be
        // dressed up as the refusal.
        var inMissingFolder = Path.Combine(scratch.Missing("nowhere"), "song.png");

        var ex = Assert.Throws<DirectoryNotFoundException>(
            () => { using var stream = CliOptions.OpenImageOutput(inMissingFolder, replace: false); });
        await Assert.That(ex.Message).Contains("nowhere");
    }

    // A throwaway folder for the tests that need paths that exist (or visibly
    // do not). Everything under it is removed on dispose.
    private sealed class Scratch : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("spektra-inputs").FullName;

        public string NewFolder(string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;

        public string NewFile(string name, string content = "")
        {
            var path = Path.Combine(root, name);
            File.WriteAllText(path, content);
            return path;
        }

        // A path under the scratch folder where nothing was made.
        public string Missing(string name) => Path.Combine(root, name);

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
