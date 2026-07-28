using Spektra.Core;

namespace Spektra.Tests;

public class InstanceMessageTests
{
    // The sender's folder. Only paths that resolve inside it get rebased.
    private const string Cwd = @"D:\Music\Albums";

    private static readonly Func<string, bool> IsFile =
        p => p.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)
          || p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
    private static readonly Func<string, bool> IsDir =
        p => p.EndsWith("Albums", StringComparison.OrdinalIgnoreCase)
          || p.EndsWith("Live", StringComparison.OrdinalIgnoreCase);

    private static string[] Rebase(string cwd, params string[] args) =>
        InstanceMessage.Rebase(new InstancePayload(cwd, args), IsFile, IsDir);

    [Test]
    public async Task RoundTrip_PreservesDirectoryAndArguments()
    {
        var encoded = InstanceMessage.Encode(Cwd, ["a.flac", "--mode", "diff"]);
        var decoded = InstanceMessage.Decode(encoded);

        await Assert.That(decoded).IsNotNull();
        await Assert.That(decoded!.WorkingDirectory).IsEqualTo(Cwd);
        await Assert.That(decoded.Args.Count).IsEqualTo(3);
        await Assert.That(decoded.Args[0]).IsEqualTo("a.flac");
        await Assert.That(decoded.Args[1]).IsEqualTo("--mode");
        await Assert.That(decoded.Args[2]).IsEqualTo("diff");
    }

    [Test]
    public async Task Encode_IsASingleLine_EvenWhenAFileNameContainsANewline()
    {
        // The reader takes one message per ReadLine, so an embedded newline must
        // not be able to split one message into two.
        var encoded = InstanceMessage.Encode(Cwd, ["we\nird.flac"]);

        await Assert.That(encoded.Contains('\n')).IsFalse();
        await Assert.That(encoded.Contains('\r')).IsFalse();

        var decoded = InstanceMessage.Decode(encoded);
        await Assert.That(decoded!.Args[0]).IsEqualTo("we\nird.flac");
    }

    [Test]
    public async Task Decode_ReturnsNullForGarbage()
    {
        // Anything may connect to a named pipe; a bad message loses that message
        // and nothing else.
        await Assert.That(InstanceMessage.Decode("not json at all")).IsNull();
        await Assert.That(InstanceMessage.Decode("")).IsNull();
        await Assert.That(InstanceMessage.Decode("{}")).IsNull();
    }

    [Test]
    public async Task Rebase_ResolvesARelativeFileAgainstTheSendersDirectory()
    {
        var rebased = Rebase(Cwd, "track.flac");
        await Assert.That(rebased[0]).IsEqualTo(@"D:\Music\Albums\track.flac");
    }

    [Test]
    public async Task Rebase_LeavesAnAbsolutePathAlone()
    {
        var rebased = Rebase(Cwd, @"E:\Other\track.flac");
        await Assert.That(rebased[0]).IsEqualTo(@"E:\Other\track.flac");
    }

    [Test]
    public async Task Rebase_LeavesSwitchesAndTheirValuesAlone()
    {
        // "diff" is not a path and must survive as itself; rebasing it would
        // silently change what --mode means.
        var rebased = Rebase(Cwd, "--compare", "a.flac", "b.flac", "--mode", "diff", "--auto");

        await Assert.That(rebased[0]).IsEqualTo("--compare");
        await Assert.That(rebased[1]).IsEqualTo(@"D:\Music\Albums\a.flac");
        await Assert.That(rebased[2]).IsEqualTo(@"D:\Music\Albums\b.flac");
        await Assert.That(rebased[3]).IsEqualTo("--mode");
        await Assert.That(rebased[4]).IsEqualTo("diff");
        await Assert.That(rebased[5]).IsEqualTo("--auto");
    }

    [Test]
    public async Task Rebase_LeavesATokenThatDoesNotResolveAlone()
    {
        // Nothing on disk to point at: keep the token so the parser can drop it
        // for the same reason it always would.
        var rebased = Rebase(Cwd, "ghost.wav");
        await Assert.That(rebased[0]).IsEqualTo("ghost.wav");
    }

    [Test]
    public async Task Rebase_ResolvesARelativeFolder()
    {
        var rebased = Rebase(Cwd, "Live");
        await Assert.That(rebased[0]).IsEqualTo(@"D:\Music\Albums\Live");
    }

    [Test]
    public async Task Rebase_HandlesADotSegment()
    {
        var rebased = Rebase(Cwd, @".\track.flac");
        await Assert.That(rebased[0]).IsEqualTo(@"D:\Music\Albums\track.flac");
    }

    [Test]
    public async Task Rebase_WithNoWorkingDirectory_ChangesNothing()
    {
        var rebased = Rebase("", "track.flac");
        await Assert.That(rebased[0]).IsEqualTo("track.flac");
    }

    [Test]
    public async Task RebasedRelativePath_SurvivesLaunchArgsParse()
    {
        // The two halves have to agree: a rebased token must come out of Parse
        // as a file, which is the whole point of rebasing it.
        var rebased = Rebase(Cwd, "track.flac");
        var request = LaunchArgs.Parse(rebased, IsFile, IsDir);

        await Assert.That(request.IsBare).IsFalse();
        await Assert.That(request.Files.Count).IsEqualTo(1);
        await Assert.That(request.Files[0]).IsEqualTo(@"D:\Music\Albums\track.flac");
    }
}
