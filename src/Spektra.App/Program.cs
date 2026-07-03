using Avalonia;

namespace Spektra.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Cli.IsCliMode(args)) return Cli.Run(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect();
}
