using System.Globalization;
using System.Text;

namespace Spektra.Core;

/// The last words of a crashing process: renders an unhandled exception into
/// one plain-text entry and appends it to a capped log file, so a field bug
/// report can carry the actual failure instead of "it disappeared". The app
/// installs the hooks (AppDomain unhandled, unobserved tasks) at startup;
/// this class only formats and writes, which is the testable part.
public static class CrashLog
{
    /// Beside the other machine-local data (the ffmpeg download dir), not in
    /// roaming AppData with the settings: a crash is a fact about this
    /// machine, and a log that follows a roaming profile helps nobody.
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spektra", "crash.log");

    /// One log entry: a header a person can scan (when, which build, which
    /// hook fired, which Windows) and the full exception chain underneath.
    /// Pure over its inputs so the shape is pinnable in tests.
    public static string Render(string source, Exception? exception, string version, DateTimeOffset when)
    {
        var sb = new StringBuilder();
        sb.Append("==== ")
          .Append(when.ToString("o", CultureInfo.InvariantCulture))
          .Append(" · Spektra ").Append(version)
          .Append(" · ").Append(source)
          .Append(" · ").Append(Environment.OSVersion.VersionString)
          .AppendLine();
        // ToString carries the type, the message, every inner exception, and
        // the stack, which is exactly the set a bug report needs.
        sb.AppendLine(exception?.ToString() ?? "(no exception object)");
        sb.AppendLine();
        return sb.ToString();
    }

    /// Appends one entry, fail-soft by contract: this runs inside a crash
    /// handler, and a throw here is a crash inside the crash. When the file
    /// already exceeds the cap it is rotated to ".1" first (one generation
    /// kept), so the log never grows without bound and the crash before this
    /// one is still there to read.
    public static void Append(string path, string entry, long maxBytes = 256 * 1024)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (new FileInfo(path) is { Exists: true } existing && existing.Length > maxBytes)
                File.Move(path, path + ".1", overwrite: true);
            File.AppendAllText(path, entry);
        }
        catch
        {
            // Nothing to do and nowhere to say it: an unwritable log location
            // degrades to "no entry", never to a second failure.
        }
    }
}
