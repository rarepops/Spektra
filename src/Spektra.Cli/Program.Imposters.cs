using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    /// imposters: checks the folders against a reference folder the user trusts, song by song,
    /// writes the listening playlist when one was asked for, then prints what it found. Report
    /// only: no audio file is touched. 1 when a checked copy is not confirmed as the reference
    /// recording; 2, with nothing on stdout, when a folder is missing or is the reference folder
    /// itself, when the copies could not be checked, or when the playlist could not be written.
    private static int Imposters(FfmpegPaths ffmpeg, string[] args, OutFormat fmt, int jobs)
    {
        var (reference, roots, fresh, playlist) = CliOptions.ParseImposters(args);
        if (!Directory.Exists(reference) || !roots.All(Directory.Exists))
        {
            Console.Error.WriteLine(
                "spektra-cli imposters: give an existing reference folder and one or more existing folders to check.");
            return 2;
        }
        // ImposterScan.Run throws on this; here it is a usage error like any other.
        if (roots.Any(root => ImposterScan.IsSameFolder(root, reference)))
        {
            Console.Error.WriteLine("spektra-cli imposters: the reference folder cannot also be a folder to check.");
            return 2;
        }

        var cache = OpenCache("imposters", "analyzing everything");
        ImposterResult result;
        try { result = CheckWithProgress(ffmpeg, reference, roots, jobs, cache, fresh); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // As in dupes --diff: anything but a pure cancel (ffmpeg not starting, say) comes as
            // an AggregateException; its base exception says what went wrong.
            Console.Error.WriteLine($"spektra-cli imposters: could not check the copies: {ex.GetBaseException().Message}");
            return 2;
        }
        finally { cache?.Dispose(); }

        if (playlist is not null && !TryWriteText(
                "imposters", playlist,
                ImposterView.Playlist(ImposterView.Ordered(result.Songs, []), result.ReferenceRoot, result.Roots)))
            return 2;

        // As dupes --diff prints its text: \n becomes the line ending every other verb's output has.
        if (fmt == OutFormat.Text) Console.Write(ImposterReport.Text(result).Replace("\n", Environment.NewLine));
        else Emit(ImposterReport.Rows(result), fmt);
        return ImposterReport.HasFindings(result) ? 1 : 0;
    }

    /// Runs the check with a one-line progress readout on stderr (Analyzing, Matching, Reading
    /// tags, then Checking), shown only when stderr is a terminal and cleared after. Each count is
    /// written as it is reported, as LabelWithProgress writes its own.
    private static ImposterResult CheckWithProgress(
        FfmpegPaths ffmpeg, string reference, IReadOnlyList<string> roots, int jobs, AuditCache? cache, bool fresh)
    {
        var progress = new InlineProgress<DupesProgress>(p =>
        {
            if (!Console.IsErrorRedirected)
                Console.Error.Write($"\r{p.Phase} {p.Done}/{p.Total}    ");
        });
        try { return ImposterScan.Run(ffmpeg, reference, roots, jobs, cache, fresh, progress); }
        finally { if (!Console.IsErrorRedirected) Console.Error.Write("\r                        \r"); }
    }
}
