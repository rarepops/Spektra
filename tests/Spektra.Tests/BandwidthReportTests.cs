using System.Diagnostics;
using Spektra.Core;

namespace Spektra.Tests;

public class BandwidthReportTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    [Test]
    public async Task Analyze_FullBandFile_IsLossless()
    {
        var r = BandwidthReport.Analyze(Ff, P("chirp.wav"));
        await Assert.That(r.Error).IsNull();
        await Assert.That(r.Metadata).IsNotNull();
        await Assert.That(r.Verdict!.Kind).IsEqualTo(VerdictKind.Lossless);
    }

    [Test]
    public async Task Analyze_LowBitrateMp3_IsLossy()
    {
        var r = BandwidthReport.Analyze(Ff, P("chirp-mp3-64.mp3"));
        await Assert.That(r.Verdict!.Kind).IsEqualTo(VerdictKind.Lossy);
    }

    [Test]
    public async Task Analyze_MissingFile_ReturnsError()
    {
        var r = BandwidthReport.Analyze(Ff, P("does-not-exist.wav"));
        await Assert.That(r.Error).IsNotNull();
        await Assert.That(r.Verdict).IsNull();
    }

    [Test]
    public async Task FindAudioFiles_FindsFixtures_SkipsNonAudio()
    {
        var files = BandwidthReport.FindAudioFiles(Fixtures, recursive: false).ToList();
        await Assert.That(files.Any(f => f.EndsWith("chirp.wav", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(files.Any(f => f.EndsWith("chirp-mp3-64.mp3", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(files.Any(f => f.EndsWith("notaudio.txt", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task FindAudioFiles_Recursive_FindsFilesInNestedFolders_SkipsNonAudio()
    {
        var root = Directory.CreateTempSubdirectory("spektra-rec").FullName;
        try
        {
            var deep = Path.Combine(root, "nested", "deeper");
            Directory.CreateDirectory(deep);
            File.WriteAllBytes(Path.Combine(root, "top.wav"), []);
            File.WriteAllBytes(Path.Combine(deep, "buried.flac"), []);
            File.WriteAllBytes(Path.Combine(deep, "note.txt"), []);

            var files = BandwidthReport.FindAudioFiles(root, recursive: true).ToList();

            await Assert.That(files.Any(f => f.EndsWith("top.wav", StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(files.Any(f => f.EndsWith("buried.flac", StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(files.Any(f => f.EndsWith("note.txt", StringComparison.OrdinalIgnoreCase))).IsFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // A junction inside a scanned folder must not widen the scan: following it
    // pulls in files the user never chose (a link to a whole drive), lists the
    // same file under two names (a self-made "duplicate"), and a link CYCLE
    // recurses until the path length blows. The guard is on descent only: a
    // junction the user explicitly chose as the root still scans, because the
    // walk starts inside it, and reparse-point FILES (OneDrive placeholders)
    // are ordinary entries, never skipped.
    [Test]
    public async Task FindAudioFiles_ListsThroughALinkRoot_ButNeverDescendsIntoOne()
    {
        if (!OperatingSystem.IsWindows()) return; // links are set up through mklink

        var root = Directory.CreateTempSubdirectory("spektra-junc").FullName;
        var outside = Directory.CreateTempSubdirectory("spektra-junc-outside").FullName;
        var link = Path.Combine(root, "portal");
        var loop = Path.Combine(root, "loop");
        try
        {
            File.WriteAllBytes(Path.Combine(root, "inside.flac"), []);
            File.WriteAllBytes(Path.Combine(outside, "outside.flac"), []);
            Junctions.Create(link, outside);
            Junctions.Create(loop, root); // the cycle must terminate, not hang

            var files = BandwidthReport.FindAudioFiles(root, recursive: true).ToList();
            await Assert.That(files.Count).IsEqualTo(1);
            await Assert.That(files[0].EndsWith("inside.flac", StringComparison.OrdinalIgnoreCase)).IsTrue();

            // Scanning the link itself is the user choosing its target.
            var viaLink = BandwidthReport.FindAudioFiles(link, recursive: true).ToList();
            await Assert.That(viaLink.Count).IsEqualTo(1);
            await Assert.That(viaLink[0].EndsWith("outside.flac", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
        finally
        {
            // Delete the junctions first: a recursive delete must never be
            // pointed at a tree that still contains a link to the tree itself.
            Directory.Delete(loop);
            Directory.Delete(link);
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public async Task FindAudioFiles_SkipsInaccessibleSubfolder_ReturnsReachableFiles()
    {
        // A permission-denied subfolder (a drive root's System Volume Information,
        // a mixed-permission share) must not abort the whole walk; the Folder
        // Manifest already tolerates this, so the audit/dupes walk must too.
        if (!OperatingSystem.IsWindows()) return; // denial is set up through icacls

        var root = Directory.CreateTempSubdirectory("spektra-acl").FullName;
        var locked = Path.Combine(root, "locked");
        try
        {
            var reachable = Path.Combine(root, "reachable");
            Directory.CreateDirectory(reachable);
            Directory.CreateDirectory(locked);
            File.WriteAllBytes(Path.Combine(reachable, "ok.wav"), []);
            File.WriteAllBytes(Path.Combine(locked, "hidden.wav"), []);
            DenyEveryone(locked);

            var files = BandwidthReport.FindAudioFiles(root, recursive: true).ToList();

            await Assert.That(files.Any(f => f.EndsWith("ok.wav", StringComparison.OrdinalIgnoreCase))).IsTrue();
        }
        finally
        {
            RestoreEveryone(locked);
            Directory.Delete(root, recursive: true);
        }
    }

    // Deny the Everyone SID (S-1-1-0) read/list/traverse on the folder, so
    // enumerating into it throws UnauthorizedAccessException. The Deny covers
    // only read/execute, leaving the owner's WRITE_DAC/DELETE intact for cleanup.
    private static void DenyEveryone(string dir) => Icacls($"\"{dir}\" /deny *S-1-1-0:(RX)");
    private static void RestoreEveryone(string dir) => Icacls($"\"{dir}\" /remove:d *S-1-1-0");

    private static void Icacls(string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo("icacls", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        p.WaitForExit();
    }
}
