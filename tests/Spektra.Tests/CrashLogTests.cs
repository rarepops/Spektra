using Spektra.Core;

namespace Spektra.Tests;

public class CrashLogTests
{
    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "spektra-crashlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Test]
    public async Task Render_CarriesEverythingAReportNeeds()
    {
        var ex = new InvalidOperationException("outer boom",
            new ArgumentException("inner boom"));
        var entry = CrashLog.Render(
            "unhandled", ex, "0.20.0", new DateTimeOffset(2026, 8, 23, 14, 5, 12, TimeSpan.FromHours(2)));

        await Assert.That(entry).Contains("2026-08-23T14:05:12");
        await Assert.That(entry).Contains("0.20.0");
        await Assert.That(entry).Contains("unhandled");
        await Assert.That(entry).Contains("InvalidOperationException");
        await Assert.That(entry).Contains("outer boom");
        await Assert.That(entry).Contains("inner boom").Because("inner exceptions are the half that matters");
    }

    [Test]
    public async Task Render_SurvivesAMissingExceptionObject()
    {
        // AppDomain hands ExceptionObject as object; a non-Exception in it is
        // rare but legal, and the log line is the one place that cannot throw.
        var entry = CrashLog.Render("unhandled", null, "0.20.0", DateTimeOffset.UnixEpoch);
        await Assert.That(entry).Contains("no exception object");
    }

    [Test]
    public async Task Append_CreatesTheDirectory_AndAccumulates()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "deeper", "crash.log");
        try
        {
            CrashLog.Append(path, "first entry\n");
            CrashLog.Append(path, "second entry\n");
            var text = await File.ReadAllTextAsync(path);
            await Assert.That(text).Contains("first entry");
            await Assert.That(text).Contains("second entry");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Append_RotatesOnceOverTheCap_KeepingOneGeneration()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "crash.log");
        try
        {
            CrashLog.Append(path, "old generation\n", maxBytes: 8);
            // The existing file is over the tiny cap, so this append rotates
            // it aside first: the log never grows without bound, and the
            // crash before the crash is still there to read.
            CrashLog.Append(path, "new generation\n", maxBytes: 8);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("new generation\n");
            await Assert.That(await File.ReadAllTextAsync(path + ".1")).IsEqualTo("old generation\n");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Append_NeverThrows_EvenWithTheFileLocked()
    {
        // Append runs inside a crash handler; a throw there is a crash inside
        // the crash. A locked or unwritable path degrades to "no log entry".
        var dir = NewDir();
        var path = Path.Combine(dir, "crash.log");
        try
        {
            await using (File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                CrashLog.Append(path, "will not land\n");
            }
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
