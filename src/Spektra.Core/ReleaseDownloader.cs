using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Spektra.Core;

public enum DownloadOutcome { Verified, ChecksumMismatch }

/// Bytes written so far and the expected total: the asset's declared size, or
/// the response's Content-Length when the payload had none, and never less than
/// Done.
public readonly record struct DownloadProgress(long Done, long Total);

/// Fetches a release file into place and refuses to call it done until its
/// SHA-256 matches the release's own checksum list. The network is a thin
/// boundary: HttpRequestException and IOException propagate to the caller, as
/// does a caller cancel, and on every failure the partial file is removed first.
public static class ReleaseDownloader
{
    /// No overall timeout (a 76 MB installer on a slow line is fine); a stalled
    /// body is caught per read instead, see StallTimeout.
    public static HttpClient DefaultClient { get; } = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Spektra-update-download");
        return h;
    }

    // No progress for this long reads as a dead connection. HttpClient.Timeout
    // only covers the initial response, not a stalled body, so each read is timed.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// The user's Downloads folder: the shell's known folder on Windows, which
    /// follows a relocated Downloads; `~/Downloads` elsewhere and as fallback.
    public static string DownloadsFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
                if (key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") is string s && s.Length > 0)
                    return Environment.ExpandEnvironmentVariables(s);
            }
            catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException)
            {
                // Fall through to the conventional location.
            }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// A small text asset, such as the checksum list. Non-success status throws.
    public static async Task<string> FetchTextAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// True when `path` exists and hashes to `expectedHex`.
    public static async Task<bool> IsVerifiedAsync(string path, string expectedHex, CancellationToken ct)
    {
        if (!File.Exists(path)) return false;
        return Sha256Sums.Matches(expectedHex, await HashAsync(path, ct));
    }

    /// Streams `asset` to `destination` through a `.partial` sibling, renames it
    /// into place (replacing any earlier file of that name), then hashes it. A
    /// mismatch deletes the file and reports ChecksumMismatch.
    public static async Task<DownloadOutcome> DownloadVerifiedAsync(
        HttpClient http, ReleaseAsset asset, string destination, string expectedHex,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var partial = destination + ".partial";
        try
        {
            using var resp = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = asset.Size > 0 ? asset.Size : resp.Content.Headers.ContentLength ?? 0;
            await using (var input = await resp.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(partial))
            {
                var buffer = new byte[81920];
                long done = 0;
                while (true)
                {
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    stall.CancelAfter(StallTimeout);
                    int read;
                    try
                    {
                        read = await input.ReadAsync(buffer, stall.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new HttpRequestException(
                            $"Download stalled (no data for {StallTimeout.TotalSeconds:0}s).");
                    }
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report(new DownloadProgress(done, Math.Max(total, done)));
                }
            }
            File.Move(partial, destination, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        if (Sha256Sums.Matches(expectedHex, await HashAsync(destination, ct)))
            return DownloadOutcome.Verified;
        TryDelete(destination);
        return DownloadOutcome.ChecksumMismatch;
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover .partial is untidy, not a failure of the download.
        }
    }
}
