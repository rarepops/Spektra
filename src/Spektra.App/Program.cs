using Avalonia;

namespace Spektra.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // An instance already running takes this command line and raises its own
        // window, leaving this process with nothing to show.
        if (SingleInstance.TryHandOff(args)) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect();
}
