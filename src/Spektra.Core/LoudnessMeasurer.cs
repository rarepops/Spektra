using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Spektra.Core;

/// Loudness + dynamics for a file. LUFS / LRA / true peak come from ffmpeg's
/// EBU R128 meter; crest factor and the clipping hint come from the samples.
public sealed record LoudnessReport(
    double? IntegratedLufs, double? LoudnessRangeLu, double? TruePeakDbtp,
    float PeakDbfs, float RmsDbfs, float CrestFactorDb, long ClippedSamples, string Summary);

public sealed partial class LoudnessMeasurer(FfmpegPaths ffmpeg)
{
    public LoudnessReport Measure(string path, int? channel = null, CancellationToken ct = default)
    {
        var (i, lra, tp) = RunEbur128(path, ct);
        var dyn = Dynamics.FromSamples(
            new AudioDecoder(ffmpeg.FfmpegPath).DecodeMonoChunks(path, ct, new DecodeOptions(Channel: channel)));
        return new LoudnessReport(i, lra, tp, dyn.PeakDbfs, dyn.RmsDbfs, dyn.CrestFactorDb, dyn.ClippedSamples,
            Describe(i, lra, tp, dyn));
    }

    private static string Describe(double? i, double? lra, double? tp, DynamicsStats d)
    {
        var parts = new List<string>();
        if (i is { } iv) parts.Add($"{iv:0.0} LUFS integrated");
        if (lra is { } l) parts.Add($"LRA {l:0.0} LU");
        if (tp is { } t) parts.Add($"true peak {t:0.0} dBTP");
        parts.Add($"crest {d.CrestFactorDb:0.0} dB");
        if (d.ClippedSamples > 0) parts.Add($"{d.ClippedSamples} samples near full scale");
        return string.Join(", ", parts) + ".";
    }

    /// Extracts Integrated LUFS, LRA, and true peak from the ffmpeg ebur128
    /// Summary block. Parses only the summary (the per-frame progress lines carry
    /// the same tokens). Pure; unit-tested.
    public static (double? Integrated, double? LoudnessRange, double? TruePeak) ParseEbur128(string stderr)
    {
        var idx = stderr.LastIndexOf("Summary:", StringComparison.Ordinal);
        var s = idx >= 0 ? stderr[idx..] : stderr;
        return (
            Num(IntegratedRegex().Match(s)),
            Num(LraRegex().Match(s)),
            Num(TruePeakRegex().Match(s)));
    }

    private static double? Num(Match m) =>
        m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : null;

    private (double? I, double? Lra, double? Tp) RunEbur128(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var psi = new ProcessStartInfo(ffmpeg.FfmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in new[]
                 {
                     "-hide_banner", "-nostdin", "-nostats",
                     "-i", path, "-af", "ebur128=peak=true", "-f", "null", "-",
                 })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new AudioDecodeException("Failed to start ffmpeg.");
        var sb = new StringBuilder();
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (sb)
            {
                sb.AppendLine(e.Data);
                if (sb.Length > 8000) sb.Remove(0, sb.Length - 6000); // the summary is at the end
            }
        };
        p.BeginErrorReadLine();
        // Kill on cancel so WaitForExit unblocks; the token used to reach
        // only the stdout drain, leaving the measurement uncancellable.
        using var killOnCancel = ct.Register(() =>
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
        });
        var drain = p.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        p.WaitForExit();
        try { drain.Wait(CancellationToken.None); } catch { /* best effort */ }
        ct.ThrowIfCancellationRequested();

        string text;
        lock (sb) text = sb.ToString();
        return ParseEbur128(text);
    }

    [GeneratedRegex(@"\bI:\s*(-?\d+(?:\.\d+)?)\s*LUFS")]
    private static partial Regex IntegratedRegex();

    [GeneratedRegex(@"\bLRA:\s*(-?\d+(?:\.\d+)?)\s*LU\b")]
    private static partial Regex LraRegex();

    [GeneratedRegex(@"\bPeak:\s*(-?\d+(?:\.\d+)?)\s*dBFS")]
    private static partial Regex TruePeakRegex();
}
