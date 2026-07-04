using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spektra.Core;

/// Flat, stable rows for machine-readable CLI reports.
public sealed record BandwidthRow(
    string File, string? Codec, int? SampleRateHz, long? BitrateBps, double DurationSeconds,
    string Verdict, double? CutoffHz, string? CodecGuess, string? Error);

public sealed record IntegrityRow(
    string File, string Status, int DecodeErrors, int Dropouts,
    double DecodedSeconds, double ExpectedSeconds, bool Truncated, string Detail, string? Error);

public sealed record AuditRow(
    string File, string? Codec, int? SampleRateHz, long? BitrateBps, double DurationSeconds,
    string Bandwidth, double? CutoffHz, string Integrity, int DecodeErrors, int Dropouts,
    bool Truncated, string? Error);

public sealed record LoudnessRow(
    string File, double? IntegratedLufs, double? LoudnessRangeLu, double? TruePeakDbtp,
    double PeakDbfs, double RmsDbfs, double CrestFactorDb, long ClippedSamples, string? Error);

/// Builds report rows from analysis results and serializes them to JSON or CSV.
/// Column order follows the record's declaration order.
public static class Reporting
{
    public static BandwidthRow ToBandwidthRow(FileReport r) => new(
        Path.GetFileName(r.Path), r.Metadata?.Codec, r.Metadata?.SampleRate, r.Metadata?.BitRateBps,
        r.Metadata?.Duration.TotalSeconds ?? 0,
        r.Verdict?.Kind.ToString() ?? "Error", r.Verdict?.CutoffHz, r.Verdict?.CodecGuess, r.Error);

    public static IntegrityRow ToIntegrityRow(string path, IntegrityReport? ir, string? error) => new(
        Path.GetFileName(path), ir?.Status.ToString() ?? "Error",
        ir?.DecodeErrors ?? 0, ir?.Dropouts.Count ?? 0,
        ir?.DecodedSeconds ?? 0, ir?.ExpectedSeconds ?? 0, ir?.Truncated ?? false,
        ir?.Summary ?? error ?? "", error);

    public static AuditRow ToAuditRow(FileReport r, IntegrityReport? ir, string? integrityError) => new(
        Path.GetFileName(r.Path), r.Metadata?.Codec, r.Metadata?.SampleRate, r.Metadata?.BitRateBps,
        r.Metadata?.Duration.TotalSeconds ?? 0,
        r.Verdict?.Kind.ToString() ?? "Error", r.Verdict?.CutoffHz,
        ir?.Status.ToString() ?? "Error", ir?.DecodeErrors ?? 0, ir?.Dropouts.Count ?? 0,
        ir?.Truncated ?? false, r.Error ?? integrityError);

    public static LoudnessRow ToLoudnessRow(string path, LoudnessReport? r, string? error) => new(
        Path.GetFileName(path), r?.IntegratedLufs, r?.LoudnessRangeLu, r?.TruePeakDbtp,
        r?.PeakDbfs ?? 0, r?.RmsDbfs ?? 0, r?.CrestFactorDb ?? 0, r?.ClippedSamples ?? 0, error);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson<T>(IEnumerable<T> rows) => JsonSerializer.Serialize(rows, JsonOptions);

    public static string ToCsv<T>(IEnumerable<T> rows)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var sb = new StringBuilder();
        sb.Append(string.Join(",", props.Select(p => Escape(p.Name)))).Append('\n');
        foreach (var row in rows)
            sb.Append(string.Join(",", props.Select(p => Escape(Format(p.GetValue(row)))))).Append('\n');
        return sb.ToString();
    }

    private static string Format(object? v) => v switch
    {
        null => "",
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string Escape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
