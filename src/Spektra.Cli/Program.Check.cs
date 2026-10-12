using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    private static int Check(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra-cli check: give one or more audio files or a folder.");
            return 2;
        }
        var computed = MapParallel(files, jobs, path =>
        {
            try
            {
                var meta = new AnalysisSession(ffmpeg).ReadMetadata(path);
                return (ir: (IntegrityReport?)new IntegrityScanner(ffmpeg).Check(path, meta), err: (string?)null);
            }
            catch (Exception ex) when (ex is AudioDecodeException or IOException)
            {
                return (ir: (IntegrityReport?)null, err: (string?)ex.Message);
            }
        });

        var rows = files.Select((path, i) => Reporting.ToIntegrityRow(path, computed[i].ir, computed[i].err)).ToList();
        var corrupt = computed.Count(c => c.err is not null || c.ir?.Status == IntegrityStatus.Corrupt);
        if (fmt != OutFormat.Text) Emit(rows, fmt);
        else PrintCheck(files, computed, corrupt);
        return corrupt > 0 ? 1 : 0;
    }

    private static void PrintCheck(
        IReadOnlyList<string> files, (IntegrityReport? ir, string? err)[] computed, int corrupt)
    {
        var suspect = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var path = files[i];
            var (ir, err) = computed[i];
            if (err is null && ir?.Status == IntegrityStatus.Suspect) suspect++;

            if (ir?.Status == IntegrityStatus.Ok)
                Console.WriteLine($"  [OK]      {Path.GetFileName(path)}");
            else
            {
                var tag = err is not null ? "CORRUPT" : ir!.Status.ToString().ToUpperInvariant();
                Console.WriteLine($"  [{tag}] {Path.GetFileName(path)} - {err ?? ir!.Summary}");
            }
        }
        Console.WriteLine(
            $"{Environment.NewLine}{files.Count} files: {files.Count - suspect - corrupt} ok, {suspect} suspect, {corrupt} corrupt.");
    }
}
