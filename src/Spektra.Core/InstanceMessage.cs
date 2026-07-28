using System.Text.Json;

namespace Spektra.Core;

/// One launch's command line as it travels from a freshly started process to the
/// instance already running. The working directory travels with it because the
/// receiver's own is unrelated.
public sealed record InstancePayload(string WorkingDirectory, IReadOnlyList<string> Args);

/// Wire format for the single-instance handoff.
///
/// One JSON object per line, so a reader can take exactly one message with a
/// single ReadLine. A file name containing a newline still cannot split a
/// message, because JSON escapes it rather than emitting it raw.
public static class InstanceMessage
{
    public static string Encode(string workingDirectory, IReadOnlyList<string> args) =>
        JsonSerializer.Serialize(new InstancePayload(workingDirectory, args));

    /// Null for anything unreadable. A stray connection from another program is
    /// not worth taking the running instance down over.
    public static InstancePayload? Decode(string line)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<InstancePayload>(line);
            return payload?.Args is null ? null : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// Resolves the sender's relative paths against the sender's directory.
    ///
    /// Without this a `spektra song.flac` typed in a terminal would be re-parsed
    /// by a receiver sitting in a different folder, fail its existence check, and
    /// be dropped in silence, which looks exactly like the bug this whole change
    /// is fixing.
    ///
    /// A token is only rewritten when the rebased path actually exists, so
    /// non-path arguments (a `--mode` value, say) are left alone rather than
    /// being mangled into a path that means nothing.
    public static string[] Rebase(
        InstancePayload payload,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? dirExists = null)
    {
        var isFile = fileExists ?? File.Exists;
        var isDir = dirExists ?? Directory.Exists;

        var rebased = new string[payload.Args.Count];
        for (var i = 0; i < payload.Args.Count; i++)
        {
            var arg = payload.Args[i];
            rebased[i] = TryRebase(arg, payload.WorkingDirectory, isFile, isDir) ?? arg;
        }
        return rebased;
    }

    private static string? TryRebase(
        string arg, string workingDirectory, Func<string, bool> isFile, Func<string, bool> isDir)
    {
        // "--" marks a switch. A single "-" does not: paths and negative numbers
        // both start with one, matching LaunchArgs.
        if (arg.StartsWith("--", StringComparison.Ordinal)) return null;
        if (string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(arg)) return null;

        string combined;
        try
        {
            if (Path.IsPathRooted(arg)) return null;
            combined = Path.GetFullPath(Path.Combine(workingDirectory, arg));
        }
        catch (ArgumentException)
        {
            return null; // invalid characters: leave the token untouched
        }
        catch (PathTooLongException)
        {
            return null;
        }

        return isFile(combined) || isDir(combined) ? combined : null;
    }
}
