namespace Spektra.App;

/// A selectable shell tab: the status line (from StatusViewModel) plus the
/// selection flag, and the title and cancellation each concrete tab supplies.
public abstract class TabViewModelBase : StatusViewModel, ITab
{
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public abstract string TabTitle { get; }
    public abstract void Cancel();
}
