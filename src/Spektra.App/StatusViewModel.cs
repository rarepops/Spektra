namespace Spektra.App;

/// A view model that drives a status line: a message plus a sticky error flag.
/// A normal StatusText assignment clears the flag; SetErrorStatus writes the
/// message and keeps it red until the next normal assignment. Shared by the
/// shell and every tab.
public abstract class StatusViewModel : ObservableObject
{
    private string _statusText = "";
    private bool _statusIsError;

    public string StatusText
    {
        get => _statusText;
        set { _ = Set(ref _statusText, value); StatusIsError = false; }
    }

    /// Get-only to the outside; the shell sets it (via protected access) only to
    /// mirror the selected tab's flag, everyone else goes through the setters above.
    public bool StatusIsError { get => _statusIsError; protected set => Set(ref _statusIsError, value); }

    /// Red status: sets the message and keeps the error flag past the write.
    public void SetErrorStatus(string text) { StatusText = text; StatusIsError = true; }
}
