using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    private static int Diff(FfmpegPaths ffmpeg, string[] args, OutFormat fmt)
    {
        var parsed = CliOptions.ParseDiff(args);
        if (parsed.Files.Count != 2)
        {
            Console.Error.WriteLine("spektra-cli diff: give exactly two audio files.");
            return 2;
        }

        var options = new CompareOptions(OffsetSeconds: parsed.OffsetMs / 1000, ThresholdDb: parsed.ThresholdDb);
        CompareReport report;
        try { report = new AudioCompare(ffmpeg).Run(parsed.Files[0], parsed.Files[1], options); }
        catch (Exception ex) when (ex is AudioDecodeException or IOException)
        {
            Console.Error.WriteLine($"spektra-cli diff: {ex.Message}");
            return 2;
        }

        if (fmt != OutFormat.Text) Emit(new[] { Reporting.ToCompareRow(report) }, fmt);
        else PrintDiff(report);
        return report.IsSame ? 0 : 1;
    }

    private static void PrintDiff(CompareReport report)
    {
        Console.WriteLine("A: " + report.MetaA.ToDisplayLine(Path.GetFileName(report.PathA)));
        Console.WriteLine("B: " + report.MetaB.ToDisplayLine(Path.GetFileName(report.PathB)));
        var overlap = AudioMetadata.FormatDuration(TimeSpan.FromSeconds(report.OverlapSeconds));
        var offsetText = $"{report.OffsetSeconds * 1000:+0.###;-0.###} ms";
        Console.WriteLine(report.AlignConfidence is { } conf
            ? $"Aligned {offsetText} (confidence {conf:0.00}) · overlap {overlap}"
            : $"Offset {offsetText} (pinned) · overlap {overlap}");
        if (report.LowConfidence)
            Console.WriteLine(
                $"warning: alignment confidence {report.AlignConfidence:0.00} is low; the offset may be wrong");
        if (report.HasDrift)
            Console.WriteLine(
                $"note: alignment drifts about {report.DriftSeconds * 1000:0} ms across the file; " +
                "a single offset can't fully align it");
        Console.WriteLine("Spectral: " + report.Diff.Summary);
        Console.WriteLine("Null:     " + report.Null.Summary);
        Console.WriteLine(VerdictLine(report));
    }

    private static string VerdictLine(CompareReport r)
    {
        if (r.Null.ResidualRmsDb <= Db.Floor + 1f)
            return "SAME      perfect null (identical samples)";
        return r.IsSame
            ? $"SAME      null depth {r.Null.NullDepthDb:0.0} dB >= threshold {r.ThresholdDb:0.0} dB"
            : $"DIFFERS   null depth {r.Null.NullDepthDb:0.0} dB < threshold {r.ThresholdDb:0.0} dB";
    }
}
