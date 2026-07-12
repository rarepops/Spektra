using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spektra.App;

public partial class ControlsWindow : Window
{
    public ControlsWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
