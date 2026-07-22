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
