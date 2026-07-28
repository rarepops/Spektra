using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Spektra.Core;

namespace Spektra.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(LaunchArgs.Parse(desktop.Args ?? []));
            desktop.MainWindow = window;

            // Later launches arrive on a pipe thread; Parse runs here, in the
            // receiving process, so paths are checked against this machine.
            SingleInstance.SetHandler(payload => Dispatcher.UIThread.Post(
                () => window.AcceptHandoff(LaunchArgs.Parse(InstanceMessage.Rebase(payload)))));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
