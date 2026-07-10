using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Spektra.Core;

namespace Spektra.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "" : "v" + UpdateChecker.FormatVersion(v);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
