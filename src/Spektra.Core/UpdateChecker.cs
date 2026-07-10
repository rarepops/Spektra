using System.Net.Http;
using System.Text.Json;

namespace Spektra.Core;

public enum UpdateOutcome { UpToDate, UpdateAvailable, CheckFailed }

public sealed record UpdateInfo(Version Latest, string Url, string? Notes);

public sealed record UpdateCheckResult(UpdateOutcome Outcome, UpdateInfo? Info = null);

/// Notify-only update check against the GitHub Releases API. The parse/compare
/// logic is pure and unit-tested; the network call is a thin boundary that
/// fails soft (offline, timeout, rate limits, and junk payloads all read as
/// CheckFailed). Caller cancellation propagates.
public static class UpdateChecker
{
    public const string LatestReleaseApi =
        "https://api.github.com/repos/rarepops/Spektra/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub rejects requests without a User-Agent.
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Spektra-update-check");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    public static Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default) =>
        CheckAsync(Http, current, ct);

    public static async Task<UpdateCheckResult> CheckAsync(
        HttpClient http, Version current, CancellationToken ct = default)
    {
        try
        {
            var json = await http.GetStringAsync(LatestReleaseApi, ct);
            var info = Evaluate(json, current);
            return new UpdateCheckResult(
                info is null ? UpdateOutcome.UpToDate : UpdateOutcome.UpdateAvailable, info);
        }
        catch (Exception e) when (
            e is HttpRequestException or JsonException
            || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // TaskCanceledException with an untripped caller token is the
            // HttpClient timeout; a real caller cancel propagates instead.
            return new UpdateCheckResult(UpdateOutcome.CheckFailed);
        }
    }

    /// Parses a GitHub releases/latest payload and returns update info only when
    /// its tag is strictly newer than `current`; otherwise null. Malformed JSON
    /// throws: CheckAsync reads that as CheckFailed rather than up to date.
    public static UpdateInfo? Evaluate(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("tag_name", out var tagEl)) return null;
        if (!TryParseVersion(tagEl.GetString(), out var latest)) return null;
        if (!IsNewer(latest, current)) return null;

        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
        return new UpdateInfo(latest, url, notes);
    }

    /// Parses a release tag like "v0.6.0" or "1.2.3-rc1" into a Version, dropping
    /// the leading v and any pre-release/build suffix.
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var t = tag.Trim().TrimStart('v', 'V');
        var cut = t.IndexOfAny(['-', '+']);
        if (cut >= 0) t = t[..cut];
        return Version.TryParse(t, out version!);
    }

    /// Compares on major.minor.build, ignoring the revision (assembly versions are
    /// x.y.z.0 while tags are x.y.z).
    public static bool IsNewer(Version latest, Version current)
    {
        static int N(int x) => x < 0 ? 0 : x;
        var l = new Version(N(latest.Major), N(latest.Minor), N(latest.Build));
        var c = new Version(N(current.Major), N(current.Minor), N(current.Build));
        return l > c;
    }
}
