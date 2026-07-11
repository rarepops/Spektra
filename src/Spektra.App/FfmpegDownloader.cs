using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace Spektra.App;

public static class FfmpegDownloader
{
    // gyan.dev keeps only the current release at the versioned URL, so a pinned
    // build 404s once ffmpeg moves on. We download the pinned, integrity-verified
    // build first and fall back to the always-current build when it is gone (or
    // its bytes no longer match the pin), so the download self-heals instead of
    // breaking. Re-pin on a convenient release: run tools/get-ffmpeg.ps1
    // -RecordHash and update the URL/hash here and $Url/$Sha256 in that script.
    private const string PinnedUrl =
        "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip";
    private const string PinnedSha256 = "db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec";
    private const string LatestUrl =
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    public static async Task<string> InstallAsync(IProgress<string> progress, CancellationToken ct)
    {
        var dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spektra", "ffmpeg");
        var zipPath = Path.Combine(Path.GetTempPath(), "spektra-ffmpeg.zip");

        using var http = new HttpClient();
        progress.Report("Downloading ffmpeg…");
        if (!await TryPinnedAsync(http, zipPath, progress, ct))
        {
            progress.Report("Pinned ffmpeg build unavailable; downloading the latest instead…");
            await DownloadAsync(http, LatestUrl, zipPath, ct);
            // The latest build changes each release, so it cannot be pinned;
            // surface its hash rather than trusting it silently.
            var hash = await HashAsync(zipPath, ct);
            progress.Report($"Note: using the latest ffmpeg build, not integrity-pinned; downloaded SHA-256 {hash}.");
        }

        progress.Report("Extracting…");
        var extract = Path.Combine(Path.GetTempPath(), "spektra-ffmpeg-extract");
        if (Directory.Exists(extract)) Directory.Delete(extract, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, extract);

        var ffmpeg = Directory.EnumerateFiles(extract, "ffmpeg.exe", SearchOption.AllDirectories).First();
        Directory.CreateDirectory(dest);
        File.Copy(ffmpeg, Path.Combine(dest, "ffmpeg.exe"), overwrite: true);
        File.Copy(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"),
            Path.Combine(dest, "ffprobe.exe"), overwrite: true);
        progress.Report("ffmpeg installed.");
        return dest;
    }

    // Downloads the pinned build and checks it against PinnedSha256. Returns false
    // when the build is gone (HTTP failure) or its hash does not match, so the
    // caller falls back to the current build. Cancellation propagates instead.
    private static async Task<bool> TryPinnedAsync(
        HttpClient http, string zipPath, IProgress<string> progress, CancellationToken ct)
    {
        try
        {
            await DownloadAsync(http, PinnedUrl, zipPath, ct);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        progress.Report("Verifying download…");
        var hash = await HashAsync(zipPath, ct);
        if (hash == PinnedSha256) return true;
        progress.Report($"Pinned ffmpeg hash mismatch (expected {PinnedSha256}, got {hash}).");
        return false;
    }

    // No progress for this long reads as a dead connection: abort so the caller
    // can fall back (or fail cleanly) instead of hanging. HttpClient.Timeout only
    // covers the initial response, not a stalled body stream, so we time each read.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private static async Task DownloadAsync(HttpClient http, string url, string zipPath, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var input = await resp.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(zipPath);
        var buffer = new byte[81920];
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
                // A stall, not a user cancel: surface as a network failure so the
                // pinned path falls back to the current build instead of hanging.
                throw new HttpRequestException($"Download stalled (no data for {StallTimeout.TotalSeconds:0}s).");
            }
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static async Task<string> HashAsync(string zipPath, CancellationToken ct)
    {
        await using var check = File.OpenRead(zipPath);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct));
    }
}
