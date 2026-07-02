using Avalonia.Controls;

namespace Spektra.App;

public partial class MainWindow : Window
{
    public MainWindow(string[] args)
    {
        InitializeComponent();
    }

    public MainWindow() : this([]) { }
}
