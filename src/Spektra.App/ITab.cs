using System.ComponentModel;

namespace Spektra.App;

/// A selectable tab in the shell: a document, a comparison, or a folder audit.
public interface ITab : INotifyPropertyChanged
{
    string TabTitle { get; }
    bool IsSelected { get; set; }
    string StatusText { get; }
    bool StatusIsError { get; }
    void Cancel();
}
