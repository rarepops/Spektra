using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace Spektra.App;

public static class FfmpegDownloader
{
    private const string Url =
        "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-7.1.1-essentials_build.zip";
    // Trust-on-first-use pin; keep in sync with tools/get-ffmpeg.ps1 ($Sha256).
    private const string PinnedSha256 = "";

    public static async Task<string> InstallAsync(IProgress<string> progress, CancellationToken ct)
    {
        var dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spektra", "ffmpeg");
        var zipPath = Path.Combine(Path.GetTempPath(), "spektra-ffmpeg.zip");

        progress.Report("Downloading ffmpeg…");
        using (var http = new HttpClient())
        await using (var input = await http.GetStreamAsync(Url, ct))
        await using (var output = File.Create(zipPath))
            await input.CopyToAsync(output, ct);

        if (PinnedSha256.Length != 0)
        {
            await using var check = File.OpenRead(zipPath);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct));
            if (hash != PinnedSha256)
                throw new InvalidOperationException($"ffmpeg download hash mismatch: {hash}");
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
}
