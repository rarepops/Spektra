using System.Diagnostics;
using System.Text;

namespace Spektra.Core;

public sealed class AudioDecoder(string ffmpegPath)
{
    private const int ChunkBytes = 256 * 1024; // 64k floats

    public IEnumerable<float[]> DecodeMonoChunks(string filePath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in new[]
        {
            "-v", "error", "-i", filePath, "-map", "0:a:0",
            "-ac", "1", "-f", "f32le", "-",
        }) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new AudioDecodeException("Failed to start ffmpeg.");

        var stderr = new StringBuilder();
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr)
            {
                stderr.AppendLine(e.Data);
                if (stderr.Length > 4000) stderr.Remove(0, stderr.Length - 2000);
            }
        };
        p.BeginErrorReadLine();

        var stream = p.StandardOutput.BaseStream;
        var bytes = new byte[ChunkBytes];
        var carry = 0;            // bytes of a partial float carried between reads
        long totalSamples = 0;

        try
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
                    ct.ThrowIfCancellationRequested();
                }

                var read = stream.Read(bytes, carry, bytes.Length - carry);
                if (read == 0) break;

                var available = carry + read;
                var usable = available - available % 4;
                if (usable > 0)
                {
                    var floats = new float[usable / 4];
                    Buffer.BlockCopy(bytes, 0, floats, 0, usable);
                    totalSamples += floats.Length;
                    yield return floats;
                }
                carry = available - usable;
                if (carry > 0) Array.Copy(bytes, usable, bytes, 0, carry);
            }
        }
        finally
        {
            if (!p.HasExited)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
            }
            p.WaitForExit();
        }

        string? tail;
        lock (stderr) tail = FfprobeMetadataReader.Tail(stderr.ToString());

        if (p.ExitCode != 0)
            throw new AudioDecodeException("ffmpeg failed to decode this file.", tail);
        if (totalSamples == 0)
            throw new AudioDecodeException("The file produced no audio data.", tail);
    }
}
