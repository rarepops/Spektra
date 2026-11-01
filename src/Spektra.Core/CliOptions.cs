using System.Globalization;

namespace Spektra.Core;

/// The output format a machine-readable CLI verb emits.
public enum OutFormat { Text, Json, Csv }

/// The command line cannot be run and nothing was done: a flag with a missing
/// or invalid value, an unrecognized flag, a path that does not exist, a folder
/// among other paths, or an image output that already exists or is the input
/// itself. The CLI catches this and exits 2.
public sealed class OptionException(string message) : Exception(message);

/// What `diff` was asked for: every positional argument, in order, and its
/// two tuning flags. Files is not checked here: the verb needs exactly two
/// and says so itself when it gets another number.
public sealed record DiffArgs(IReadOnlyList<string> Files, double? OffsetMs, float ThresholdDb);

/// What `image` was asked for. Options carries the render settings the flags
/// set; the palette is resolved later, against the saved settings and the
/// floor, so it travels here as a name.
public sealed record ImageArgs(
    IReadOnlyList<string> Files, string? OutPath, string? PaletteName, double? Gamma, ImageOptions Options);

/// What a verb that takes "one folder or some files" was given. Folder is set
/// (and Files empty) when the one argument is a folder to search; otherwise
/// Folder is null and Files holds the paths as typed. Every path in it exists.
public sealed record InputSet(string? Folder, IReadOnlyList<string> Files);

/// Argument parsing for the command-line front end. It lives in Core so it is
/// unit-testable (the CLI project carries no tests of its own).
public static class CliOptions
{
    /// Consume the value following a recognized flag, or fail with a clear
    /// message. Rejects a value that is itself a flag, so "--palette --gamma"
    /// does not silently eat the next flag as the palette name.
    public static string Value(string flag, string[] args, ref int i) =>
        i + 1 < args.Length && !IsFlag(args[i + 1])
            ? args[++i]
            : throw new OptionException($"{flag} needs a value.");

    /// A '-' token is a flag only when it is not a number: --floor takes a
    /// negative dB level and --offset a signed millisecond count, so "-100" is
    /// a value while "--gamma" is the next flag.
    private static bool IsFlag(string a) =>
        a.StartsWith('-')
        && !double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

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

    /// Sorts a verb's positional paths into one folder or a list of files, and
    /// fails before anything is analyzed when they are neither: an unknown flag
    /// (checked first), a path that does not exist (a typo would otherwise read
    /// as an error row, or as success), or a folder given together with other
    /// paths (a verb searches one folder or takes files, never a mix). The CLI
    /// turns the OptionException into exit 2. An empty list stays empty, for
    /// the verb to say what it wants.
    public static InputSet Inputs(IReadOnlyList<string> positionals)
    {
        RejectUnknownFlags(positionals);
        var folders = new List<string>();
        foreach (var p in positionals)
        {
            if (Directory.Exists(p)) folders.Add(p);
            else if (!File.Exists(p)) throw new OptionException($"no such file or folder: '{p}'.");
        }
        if (folders.Count == 0) return new InputSet(null, positionals);
        return positionals.Count == 1
            ? new InputSet(folders[0], [])
            : throw new OptionException($"'{folders[0]}' is a folder: give one folder by itself, or files only.");
    }

    public static DiffArgs ParseDiff(string[] args)
    {
        double? offsetMs = null;
        var thresholdDb = 40f;
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--offset") offsetMs = Double(a, args, ref i);
            else if (a is "--threshold-db") thresholdDb = Float(a, args, ref i);
            else if (a.StartsWith('-')) throw new OptionException($"unknown option '{a}'.");
            else files.Add(a);
        }
        return new DiffArgs(files, offsetMs, thresholdDb);
    }

    public static ImageArgs ParseImage(string[] args)
    {
        string? outPath = null;
        string? paletteName = null;
        double? gamma = null;
        var options = new ImageOptions();
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "-o" or "--out") outPath = Value(a, args, ref i);
            else if (a is "--palette") paletteName = Value(a, args, ref i);
            else if (a is "--gamma")
            {
                var g = Double(a, args, ref i);
                gamma = g > 0 ? g : throw new OptionException($"--gamma must be greater than 0, got '{g}'.");
            }
            else if (a is "--floor") options = options with { FloorDb = Float(a, args, ref i) };
            else if (a is "--fft") options = options with { WindowSize = Int(a, args, ref i, min: 16) };
            else if (a is "--channel") options = options with { Channel = Int(a, args, ref i, min: 1) - 1 };
            else if (a is "--columns") options = options with { MaxColumns = Int(a, args, ref i, min: 1) };
            else if (a.StartsWith('-')) throw new OptionException($"unknown option '{a}'.");
            else files.Add(a);
        }
        return new ImageArgs(files, outPath, paletteName, gamma, options);
    }

    /// Where `image` writes. An explicit -o names the file, so it is used as
    /// given and may replace what is there, except that it may not be blank or
    /// be the input itself (full paths are compared, ignoring case on every
    /// platform: a false refusal on a case-sensitive file system costs another
    /// name, a missed one costs the audio). Without it the PNG goes beside the
    /// input under the same name, and a file or folder already sitting there
    /// is refused rather than replaced: it was never offered up (a cover, last
    /// week's render), and for a .png input it is the input itself. Checked
    /// before the render, so a refusal costs no decode; OpenImageOutput checks
    /// again when the file is created.
    public static string ImageOutPath(string input, string? outPath)
    {
        if (outPath is not null)
        {
            // Blank is as unusable as empty, and Path.GetFullPath throws on an empty or
            // all-spaces path (on Windows), which would crash instead of exiting 2.
            if (string.IsNullOrWhiteSpace(outPath)) throw new OptionException("-o needs a file path.");
            // A blank input is the render's to refuse ("File not found"), and -o cannot
            // name it; GetFullPath would throw on it, so the comparison is skipped.
            if (!string.IsNullOrWhiteSpace(input)
                && string.Equals(Path.GetFullPath(outPath), Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
                throw new OptionException($"-o '{outPath}' is the input file itself; pass another output file.");
            return outPath;
        }
        var path = Path.ChangeExtension(input, ".png");
        return Path.Exists(path) ? throw ImageOutExists(path) : path;
    }

    /// Opens the PNG for writing. The default name (replace false) was free when
    /// ImageOutPath looked, but the decode since took seconds: CreateNew turns a
    /// file that appeared meanwhile into the same refusal, with nothing written,
    /// instead of truncating it. An explicit -o (replace true) keeps
    /// create-or-replace. Only a collision is translated: any other failure to
    /// open keeps its own exception, so it is never dressed up as "already exists".
    public static FileStream OpenImageOutput(string path, bool replace)
    {
        try { return File.Open(path, replace ? FileMode.Create : FileMode.CreateNew); }
        catch (IOException) when (!replace && Path.Exists(path)) { throw ImageOutExists(path); }
    }

    /// The refusal for a default image output that is taken: ImageOutPath raises
    /// it up front and OpenImageOutput again if the name is taken during the
    /// render, so both say the same thing.
    private static OptionException ImageOutExists(string path) => new(
        $"'{path}' already exists; pass -o <out.png> to write somewhere else, or -o '{path}' to replace it.");
}
