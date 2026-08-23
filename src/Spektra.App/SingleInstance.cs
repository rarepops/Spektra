using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Spektra.Core;

namespace Spektra.App;

/// Funnels every launch into the window that is already open.
///
/// This is what makes the Explorer file verb work on a multiple selection. The
/// shell invokes a command-line verb once per selected file, so three files mean
/// three processes; without a handoff that is three windows. The first process
/// wins a named mutex and listens on a named pipe, the rest send their command
/// line to it and exit.
///
/// Degrading is always toward the old behavior: any failure to take part leaves
/// the process showing its own window, never showing nothing.
internal static class SingleInstance
{
    /// One extra pipe instance beyond the one being read, so a burst of launches
    /// from a multiple selection queues in the OS instead of being refused.
    private const int MaxPipeInstances = 4;

    /// Senders write the moment they connect (TrySend), so anything slower is
    /// not a Spektra handoff: a connected client that never writes must cost
    /// one dropped connection, not a listener wedged on ReadLine forever
    /// (every later launch would then fall back to its own window).
    private static readonly TimeSpan HandoffReadTimeout = TimeSpan.FromSeconds(5);

    private static readonly string Id = BuildId();
    private static readonly object Sync = new();
    private static readonly List<InstancePayload> Pending = [];

    /// Held for the lifetime of the primary process. The field is what keeps it
    /// from being collected; the OS releases it when the process exits.
    private static Mutex? _primaryLock;
    private static Action<InstancePayload>? _handler;

    private static string MutexName => $@"Local\spektra-instance-{Id}";
    private static string PipeName => $"spektra-instance-{Id}";

    /// True when another instance accepted this command line and this process
    /// should exit without showing a window.
    public static bool TryHandOff(string[] args)
    {
        var createdNew = false;
        try
        {
            _primaryLock = new Mutex(initiallyOwned: true, MutexName, out createdNew);
        }
        catch (Exception ex) when (
            ex is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
        {
            return false;
        }

        if (createdNew)
        {
            StartListening();
            return false;
        }

        _primaryLock.Dispose();
        _primaryLock = null;

        // The primary may have won the mutex microseconds ago and not have its
        // pipe up yet, so this is worth a few attempts before giving up.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (TrySend(args)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    /// Attaches the receiver once there is a window to act on, and replays
    /// anything that arrived while the app was still starting up.
    public static void SetHandler(Action<InstancePayload> handler)
    {
        List<InstancePayload> buffered;
        lock (Sync)
        {
            _handler = handler;
            buffered = [.. Pending];
            Pending.Clear();
        }
        foreach (var payload in buffered) handler(payload);
    }

    private static bool TrySend(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            // Connect waits out a busy pipe rather than failing immediately,
            // which is what absorbs a multiple selection's burst.
            client.Connect(500);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.WriteLine(InstanceMessage.Encode(CurrentDirectoryOrEmpty(), args));
            writer.Flush();
            // Makes sure the message is read before this process exits. Windows
            // only; elsewhere closing the pipe still leaves the data readable.
            if (OperatingSystem.IsWindows()) client.WaitForPipeDrain();
            return true;
        }
        catch (Exception ex) when (
            ex is TimeoutException or IOException or UnauthorizedAccessException
                or PlatformNotSupportedException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static void StartListening()
    {
        var listener = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "spektra-single-instance",
        };
        listener.Start();
    }

    private static void ListenLoop()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, MaxPipeInstances,
                    // Asynchronous matters: without an overlapped handle the
                    // timed read below is sync-over-async and uncancellable.
                    PipeTransmissionMode.Byte,
                    PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                server.WaitForConnection();
                using var reader = new StreamReader(server, new UTF8Encoding(false));
                using var stall = new CancellationTokenSource(HandoffReadTimeout);
                if (ReadLineOrNull(reader, stall.Token) is { } line
                    && InstanceMessage.Decode(line) is { } payload)
                    Deliver(payload);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A half-open connection loses that one message, not the loop.
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or ObjectDisposedException)
            {
                return;
            }
        }
    }

    /// One line, or null when the stall timeout fires first; the listener
    /// treats null as "no message" and moves to the next connection.
    private static string? ReadLineOrNull(StreamReader reader, CancellationToken ct)
    {
        try
        {
            return reader.ReadLineAsync(ct).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static void Deliver(InstancePayload payload)
    {
        Action<InstancePayload>? handler;
        lock (Sync)
        {
            handler = _handler;
            if (handler is null)
            {
                Pending.Add(payload);
                return;
            }
        }
        handler(payload); // outside the lock: it marshals onto the UI thread
    }

    private static string CurrentDirectoryOrEmpty()
    {
        try
        {
            return Environment.CurrentDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty; // a deleted working directory is not fatal here
        }
    }

    /// Per user and per session: named pipes are machine-wide, so two people
    /// signed in at once must not land on the same name, and neither must two
    /// sessions of the same account.
    private static string BuildId()
    {
        var session = 0;
        try
        {
            using var self = Process.GetCurrentProcess();
            session = self.SessionId;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException)
        {
            // Leaves the id session-agnostic, which is still per user.
        }

        var seed = Encoding.UTF8.GetBytes($"{Environment.UserName}|{session}");
        return Convert.ToHexString(SHA256.HashData(seed), 0, 8);
    }
}
