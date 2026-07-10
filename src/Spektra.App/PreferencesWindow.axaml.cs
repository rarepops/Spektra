using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spektra.App;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow() => InitializeComponent();

    public PreferencesWindow(MainWindowViewModel vm) : this()
    {
        vm.ReloadPalettes(); // pick up JSON files dropped in the palettes folder
        DataContext = vm;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
