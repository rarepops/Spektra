using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    private static int Dupes(FfmpegPaths ffmpeg, string[] args, OutFormat fmt, int jobs)
    {
        var (roots, fresh, diff, html, playlist) = CliOptions.ParseDupes(args);
        if (roots.Count == 0 || !roots.All(Directory.Exists))
        {
            Console.Error.WriteLine("spektra-cli dupes: give one or more existing folders.");
            return 2;
        }

        var cache = OpenCache("dupes", "analyzing everything");
        DupesResult result;
        try { result = ScanWithProgress(ffmpeg, roots, jobs, cache, fresh); }
        finally { cache?.Dispose(); }

        // The scan reports full paths, so the folders are named and matched by
        // their full paths too, however they were typed.
        if (diff) return DupesDiff(ffmpeg, result, [.. roots.Select(Path.GetFullPath)], playlist, fmt, jobs);

        if (html is not null
            && !TryWriteText("dupes", html, HtmlReport.DupesDocument(result, "Spektra Duplicate Detective")))
            return 2;

        if (fmt == OutFormat.Text) PrintDupes(result);
        else
        {
            Emit(DuplicateScan.ToRows(result), fmt);
            // The rows are group members only, so a file the scan could not
            // analyze leaves no trace in them and an empty list would read as
            // "no duplicates". Counted on stderr, which keeps stdout pure.
            if (result.NotAnalyzed.Count > 0)
                Console.Error.WriteLine(
                    $"spektra-cli dupes: {result.NotAnalyzed.Count} file(s) were not analyzed and could not be " +
                    $"matched (shorter than {DuplicateScan.MinDurationSeconds:0} s, or unreadable); " +
                    "run without --json or --csv to list them.");
        }
        return result.Groups.Count > 0 ? 1 : 0;
    }

    /// Runs the scan with a one-line progress readout on stderr, shown only
    /// when stderr is a terminal so it never lands in a redirected log.
    private static DupesResult ScanWithProgress(
        FfmpegPaths ffmpeg, IReadOnlyList<string> roots, int jobs, AuditCache? cache, bool fresh)
    {
        var progress = new Progress<DupesProgress>(p =>
        {
            if (!Console.IsErrorRedirected)
                Console.Error.Write($"\r{p.Phase} {p.Done}/{p.Total}    ");
        });
        var result = DuplicateScan.Run(ffmpeg, roots, jobs, cache, fresh, progress);
        if (!Console.IsErrorRedirected) Console.Error.Write("\r                        \r");
        return result;
    }

    /// dupes --diff: labels each same-name pair by how its first seconds line
    /// up, writes the listening playlist when one was asked for, then prints
    /// what differs. 1 when anything does; 2, with nothing on stdout, when the
    /// labels could not be checked or the playlist could not be written.
    private static int DupesDiff(
        FfmpegPaths ffmpeg, DupesResult result, IReadOnlyList<string> roots, string? playlist,
        OutFormat fmt, int jobs)
    {
        SuspectVerdict[] verdicts;
        try { verdicts = LabelWithProgress(ffmpeg, result.Suspects, jobs); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything but a pure cancel (ffmpeg not starting, say) comes as an
            // AggregateException; its base exception says what went wrong.
            Console.Error.WriteLine(
                $"spektra-cli dupes: could not check the labels: {ex.GetBaseException().Message}");
            return 2;
        }

        if (playlist is not null)
        {
            var shown = SuspectView.Visible(
                result.Suspects.Select((p, i) => (Pair: p, Verdict: (SuspectVerdict?)verdicts[i])),
                s => s.Pair, s => s.Verdict, []);
            if (!TryWriteText("dupes", playlist, SuspectView.Playlist(shown, roots))) return 2;
        }

        var rows = DupesDiffReport.Rows(result, verdicts, roots);
        // Text ends its lines in \n; here they end as Console.WriteLine ends every
        // other verb's. Replace, not ReplaceLineEndings, which would also split a
        // file name holding U+2028 or U+0085.
        if (fmt == OutFormat.Text)
            Console.Write(DupesDiffReport.Text(result, verdicts, roots).Replace("\n", Environment.NewLine));
        else Emit(rows, fmt);
        return DupesDiffReport.Differs(rows) ? 1 : 0;
    }

    /// Labels the pairs with a running count on stderr, shown only when stderr
    /// is a terminal, as the scan's is, and cleared after. The count is written
    /// as it is reported: Progress&lt;T&gt; would post it to the thread pool, where
    /// the last one could land after the line is cleared.
    private static SuspectVerdict[] LabelWithProgress(
        FfmpegPaths ffmpeg, IReadOnlyList<SuspectPair> pairs, int jobs)
    {
        var progress = new InlineProgress<LabelProgress>(p =>
        {
            if (!Console.IsErrorRedirected)
                Console.Error.Write($"\rChecking {p.Done}/{p.Total}    ");
        });
        try { return SuspectLabeler.LabelAll(ffmpeg, pairs, jobs, progress); }
        finally { if (!Console.IsErrorRedirected) Console.Error.Write("\r                        \r"); }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static void PrintDupes(DupesResult result)
    {
        foreach (var g in result.Groups)
        {
            Console.WriteLine(
                $"Group {g.Group.Id} · {g.Group.Label} · {g.Group.Members.Count} files · " +
                $"sameness {g.Group.SamenessTier} · reclaim {Reporting.FormatBytes(g.ReclaimableBytes)}");
            foreach (var m in g.Group.Members)
            {
                var row = g.Rows[m.Path];
                var mark = g.Quality.Winners.Contains(m.Path) ? "*" : " ";
                var audio = m.FoundByAudio ? "  found by audio" : "";
                Console.WriteLine(
                    $"  {mark} {m.Path}  [{row.Codec} · {row.Bandwidth}" +
                    $"{(row.CutoffHz is { } c ? $" {c / 1000.0:0.0}k" : "")} · {row.Integrity}] " +
                    $"sameness {m.Sameness:0.00}{audio}");
            }
            Console.WriteLine($"    quality {g.Quality.Confidence}: {g.Quality.Reason}");
        }
        foreach (var na in result.NotAnalyzed)
            Console.WriteLine($"  ! not analyzed: {na.Path} ({na.Reason})");
        Console.WriteLine(
            $"{result.Groups.Count} group(s) · {result.Groups.Sum(g => g.Group.Members.Count)} files · " +
            $"reclaimable {Reporting.FormatBytes(result.ReclaimableBytes)} · {result.FilesScanned} scanned");
    }
}
