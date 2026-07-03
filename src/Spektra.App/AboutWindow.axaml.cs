using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spektra.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
