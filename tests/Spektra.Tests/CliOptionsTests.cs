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
}
