using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spektra.App;

public partial class CompareChooser : Window
{
    public sealed record Choice(DocumentViewModel A, DocumentViewModel B);

    private readonly IReadOnlyList<DocumentViewModel> _docs;
    public Choice? Result { get; private set; }

    public CompareChooser() : this([]) { }

    public CompareChooser(IReadOnlyList<DocumentViewModel> docs)
    {
        _docs = docs;
        InitializeComponent();
        AList.ItemsSource = docs.Select(d => d.TabTitle).ToList();
        BList.ItemsSource = docs.Select(d => d.TabTitle).ToList();
        // default: A = second-most-recent, B = most-recent (last two opened)
        AList.SelectedIndex = Math.Max(0, docs.Count - 2);
        BList.SelectedIndex = Math.Max(0, docs.Count - 1);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnCompare(object? sender, RoutedEventArgs e)
    {
        var ai = AList.SelectedIndex;
        var bi = BList.SelectedIndex;
        if (ai >= 0 && bi >= 0) Result = new Choice(_docs[ai], _docs[bi]);
        Close();
    }
}
