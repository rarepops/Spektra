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
}
