using System.Reflection;
using Avalonia;
using Spektra.Core;

namespace Spektra.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // First thing, before anything can throw: an unexpected exception
        // anywhere (an async void UI handler above all) ends the process, and
        // without a note on disk the user has nothing to attach to a report.
        InstallCrashLog();

        // An instance already running takes this command line and raises its own
        // window, leaving this process with nothing to show.
        if (SingleInstance.TryHandOff(args)) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void InstallCrashLog()
    {
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
        // Any thread's unhandled exception passes here on the way down,
        // the UI thread's included, since those propagate out of Main.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Append(CrashLog.DefaultPath, CrashLog.Render(
                "unhandled", e.ExceptionObject as Exception, version, DateTimeOffset.Now));
        // A faulted task nobody awaited does not end the process, but it is a
        // bug going invisibly by; the log is the only place it surfaces.
        TaskScheduler.UnobservedTaskException += (_, e) =>
            CrashLog.Append(CrashLog.DefaultPath, CrashLog.Render(
                "unobserved-task", e.Exception, version, DateTimeOffset.Now));
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect();
}
