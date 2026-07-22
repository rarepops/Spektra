using System.Globalization;

namespace Spektra.Core;

/// The output format a machine-readable CLI verb emits.
public enum OutFormat { Text, Json, Csv }

/// A recognized option flag was given a missing or invalid value, or an
/// unrecognized flag was passed. The CLI catches this and exits 2.
public sealed class OptionException(string message) : Exception(message);

/// Argument parsing for the command-line front end. It lives in Core so it is
/// unit-testable (the CLI project carries no tests of its own).
public static class CliOptions
{
    /// Consume the value following a recognized flag, or fail with a clear
    /// message. Rejects a value that is itself a flag, so "--palette --gamma"
    /// does not silently eat the next flag as the palette name.
    public static string Value(string flag, string[] args, ref int i) =>
        i + 1 < args.Length && !args[i + 1].StartsWith('-')
            ? args[++i]
            : throw new OptionException($"{flag} needs a value.");

    public static int Int(string flag, string[] args, ref int i, int min)
    {
        var v = Value(flag, args, ref i);
        return int.TryParse(v, out var n) && n >= min ? n
            : throw new OptionException($"{flag} must be a whole number >= {min}, got '{v}'.");
    }

    public static double Double(string flag, string[] args, ref int i)
    {
        var v = Value(flag, args, ref i);
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n
            : throw new OptionException($"{flag} must be a number, got '{v}'.");
    }

    public static float Float(string flag, string[] args, ref int i)
    {
        var v = Value(flag, args, ref i);
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n
            : throw new OptionException($"{flag} must be a number, got '{v}'.");
    }

    /// Pulls "--html &lt;path&gt;" out of a verb's argument list; null when absent.
    public static string? TakeHtml(ref string[] args)
    {
        var i = Array.IndexOf(args, "--html");
        if (i < 0) return null;
        if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
            throw new OptionException("--html needs a file path.");
        var path = args[i + 1];
        args = [.. args[..i], .. args[(i + 2)..]];
        return path;
    }

    /// Splits the format flags (--json/--csv) and the worker count (--jobs/-j)
    /// out of a verb's arguments; everything else (verb-specific flags and the
    /// positionals) flows through in Positional for the verb to parse.
    public static (OutFormat Fmt, int Jobs, string[] Positional) Take(string[] args, int defaultJobs)
    {
        var fmt = OutFormat.Text;
        var jobs = defaultJobs;
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--json") fmt = OutFormat.Json;
            else if (a is "--csv") fmt = OutFormat.Csv;
            else if (a is "--jobs" or "-j") jobs = Int(a, args, ref i, min: 1);
            else rest.Add(a);
        }
        return (fmt, jobs, rest.ToArray());
    }

    /// Throws if any token is an unconsumed flag (starts with '-'). A verb calls
    /// this on its final positional list so a mistyped flag (--jso for --json)
    /// is an error instead of a silently dropped, ignored argument.
    public static void RejectUnknownFlags(IEnumerable<string> positionals)
    {
        foreach (var a in positionals)
            if (a.StartsWith('-'))
                throw new OptionException($"unknown option '{a}'.");
    }
}
