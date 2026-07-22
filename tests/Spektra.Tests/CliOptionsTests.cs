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
