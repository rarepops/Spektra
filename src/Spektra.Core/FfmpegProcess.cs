using System.Diagnostics;
using System.Text;

namespace Spektra.Core;

/// Shared plumbing for the ffmpeg/ffprobe child processes: the standard
/// redirected, hidden-window launch, start-failure mapping, kill-on-cancel, and
/// the run-to-completion drain the metering passes share. The streaming decoder
/// (AudioDecoder) keeps its own read loop and only borrows the launch helpers;
/// everything else runs a process to completion and reads its stderr.
internal static class FfmpegProcess
{
    /// The redirected, no-window ProcessStartInfo every ffmpeg/ffprobe call
    /// uses, with the arguments already appended.
    public static ProcessStartInfo StartInfo(string exePath, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    /// Starts the process, mapping a launch failure (missing binary) to the
    /// per-file AudioDecodeException the audit pipeline already catches.
    public static Process Start(ProcessStartInfo psi, string exeName) =>
        Process.Start(psi) ?? throw new AudioDecodeException($"Failed to start {exeName}.");

    /// Kills the whole process tree when the token cancels, so the blocking
    /// reads/waits downstream unblock instead of hanging until ffmpeg ends on its
    /// own. The caller's `using` unregisters it on the happy path.
    public static CancellationTokenRegistration KillOnCancel(Process p, CancellationToken ct) =>
        ct.Register(() =>
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
        });

    /// Runs the process to completion, discarding stdout and capturing stderr,
    /// killing the tree on cancel. For the metering passes (ebur128, decode-error
    /// count) that read only stderr. tailChars > 0 keeps just the last N chars of
    /// stderr (the meters want the Summary block, which is at the end, and an
    /// unbounded buffer would grow with a pathological file); 0 keeps everything
    /// (the integrity pass counts every error line).
    public static (int ExitCode, string Stderr) RunToNull(
        ProcessStartInfo psi, string exeName, CancellationToken ct, int tailChars = 0)
    {
        using var p = Start(psi, exeName);
        var stderr = new StringBuilder();
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr)
            {
                stderr.AppendLine(e.Data);
                if (tailChars > 0 && stderr.Length > tailChars * 2)
                    stderr.Remove(0, stderr.Length - tailChars);
            }
        };
        p.BeginErrorReadLine();
        using var killOnCancel = KillOnCancel(p, ct);
        var drain = p.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        p.WaitForExit();
        try { drain.Wait(CancellationToken.None); } catch { /* best effort */ }
        ct.ThrowIfCancellationRequested();

        string text;
        lock (stderr) text = stderr.ToString();
        return (p.ExitCode, text);
    }
}
