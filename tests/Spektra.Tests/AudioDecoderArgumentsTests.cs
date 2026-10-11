using Spektra.Core;

namespace Spektra.Tests;

public sealed class AudioDecoderArgumentsTests
{
    private static string Args(DecodeOptions options) =>
        string.Join(" ", AudioDecoder.BuildArguments("in.flac", options));

    [Test]
    public async Task Defaults_MixDownToMono()
    {
        await Assert.That(Args(new DecodeOptions()))
            .IsEqualTo("-nostdin -v error -i in.flac -map 0:a:0 -ac 1 -f f32le -");
    }

    [Test]
    public async Task ASegment_GoesAheadOfTheInput()
    {
        await Assert.That(Args(new DecodeOptions(
                Start: TimeSpan.FromSeconds(1.5), Duration: TimeSpan.FromSeconds(0.25))))
            .IsEqualTo("-nostdin -v error -ss 1.5 -t 0.25 -i in.flac -map 0:a:0 -ac 1 -f f32le -");
    }

    [Test]
    public async Task AStartAlone_AddsNoDuration()
    {
        await Assert.That(Args(new DecodeOptions(Start: TimeSpan.FromSeconds(2))))
            .IsEqualTo("-nostdin -v error -ss 2 -i in.flac -map 0:a:0 -ac 1 -f f32le -");
    }

    [Test]
    public async Task Times_KeepSixDecimals_AndNoMore()
    {
        // A third of a second, to the tick. Seven digits arrive, six are sent.
        await Assert.That(Args(new DecodeOptions(Start: TimeSpan.FromTicks(3_333_333))))
            .IsEqualTo("-nostdin -v error -ss 0.333333 -i in.flac -map 0:a:0 -ac 1 -f f32le -");
    }

    [Test]
    public async Task AChannel_ReplacesTheMixdownWithAPanFilter()
    {
        await Assert.That(Args(new DecodeOptions(Channel: 1)))
            .IsEqualTo("-nostdin -v error -i in.flac -map 0:a:0 -af pan=mono|c0=c1 -f f32le -");
    }

    [Test]
    public async Task Difference_SubtractsRightFromLeftAtHalfGain()
    {
        // Not c0=c<n> with n = -1 or n = channel count: a channel ffmpeg does
        // not have exits 0 and decodes silence, so only the filter text can
        // show this is right.
        await Assert.That(Args(new DecodeOptions(Channel: DecodeOptions.Difference)))
            .IsEqualTo("-nostdin -v error -i in.flac -map 0:a:0 -af pan=mono|c0=0.5*c0-0.5*c1 -f f32le -");
    }

    [Test]
    public async Task ASampleRate_IsAskedForAfterTheMixdown()
    {
        await Assert.That(Args(new DecodeOptions(SampleRate: 22050)))
            .IsEqualTo("-nostdin -v error -i in.flac -map 0:a:0 -ac 1 -ar 22050 -f f32le -");
    }

    [Test]
    public async Task EveryOptionTogether_KeepsItsPlace()
    {
        await Assert.That(Args(new DecodeOptions(
                Start: TimeSpan.FromSeconds(1.5), Duration: TimeSpan.FromSeconds(0.25),
                Channel: 0, SampleRate: 16000)))
            .IsEqualTo(
                "-nostdin -v error -ss 1.5 -t 0.25 -i in.flac -map 0:a:0 -af pan=mono|c0=c0 -ar 16000 -f f32le -");
    }

    [Test]
    public async Task ThePath_StaysOneArgument()
    {
        // The list is handed to the process as separate arguments, so a path
        // with spaces must arrive whole and unquoted.
        var args = AudioDecoder.BuildArguments("my song.flac", new DecodeOptions());
        await Assert.That(args[args.IndexOf("-i") + 1]).IsEqualTo("my song.flac");
    }
}
