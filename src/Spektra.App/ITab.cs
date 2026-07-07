using System.ComponentModel;

namespace Spektra.App;

/// A selectable tab in the shell: a single document or a comparison.
public interface ITab : INotifyPropertyChanged
{
    string TabTitle { get; }
    bool IsSelected { get; set; }
    string StatusText { get; }
    bool StatusIsError { get; }
    void Cancel();
}
