using System.Text;

namespace Spektra.Core;

public sealed record AudioMetadata(
    string Codec, int SampleRate, int Channels, int? BitsPerSample,
    long? BitRateBps, TimeSpan Duration)
{
    public string ToDisplayLine(string fileName)
    {
        var sb = new StringBuilder(fileName).Append(" — ").Append(Codec.ToUpperInvariant());
        var khz = SampleRate / 1000.0;
        sb.Append(" · ").Append(khz.ToString(khz % 1 == 0 ? "0" : "0.#")).Append(" kHz");
        if (BitsPerSample is { } bits) sb.Append(" · ").Append(bits).Append("-bit");
        sb.Append(" · ").Append(Channels).Append(" ch");
        sb.Append(" · ").Append(FormatDuration(Duration));
        if (BitRateBps is { } bps) sb.Append(" · ").Append(bps / 1000).Append(" kbps");
        return sb.ToString();
    }

    public static string FormatDuration(TimeSpan d) => d.TotalHours >= 1
        ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}"
        : $"{d.Minutes}:{d.Seconds:00}";
}
