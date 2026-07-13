using Avalonia.Media;
using Spektra.Core;

namespace Spektra.App;

/// Base for the folder browse tree nodes. Holds the parent link (for
/// bubbling checkbox and rollup changes upward); each concrete node supplies
/// its own marker color.
public abstract class TreeNodeViewModel(string name, string fullPath) : ObservableObject
{
    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
    public TreeNodeViewModel? Parent { get; internal set; }

    public abstract IBrush MarkerBrush { get; }
}

/// A file leaf. Its checkbox is part of the analyze worklist; ticking it
/// bubbles a tri-state recompute up to every ancestor folder. Once
/// analyzed, Entry drives the severity marker dot.
public sealed class FileNodeViewModel(string name, string fullPath)
    : TreeNodeViewModel(name, fullPath)
{
    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!Set(ref _isChecked, value))
                return;
            var ancestor = Parent as FolderNodeViewModel;
            while (ancestor is not null)
            {
                ancestor.RecomputeCheckFromFiles();
                ancestor = ancestor.Parent as FolderNodeViewModel;
            }
        }
    }

    /// Set the checkbox without bubbling (used by a folder cascade, which
    /// updates ancestors itself).
    public void SetCheckedSilently(bool value) => Set(ref _isChecked, value, nameof(IsChecked));

    private AuditEntry? _entry;
    public AuditEntry? Entry
    {
        get => _entry;
        set
        {
            _entry = value;
            RaisePropertyChanged(nameof(Severity));
            RaisePropertyChanged(nameof(MarkerBrush));
        }
    }

    public RowSeverity? Severity => _entry is null ? null : FolderAudit.Severity(_entry);

    public override IBrush MarkerBrush =>
        _entry is null ? NodeMarkers.NotAnalyzed : NodeMarkers.ForEntry(_entry);
}

/// A folder node. Checking it cascades down to every descendant node (files
/// and sub-folders alike, so nested checkboxes stay in sync); checking a
/// descendant recomputes this folder's tri-state value. Rollup summarizes
/// the subtree's audit status for the marker and the label.
public sealed class FolderNodeViewModel : TreeNodeViewModel
{
    private bool _cascading;

    public FolderNodeViewModel(string name, string fullPath,
        IReadOnlyList<TreeNodeViewModel> children)
        : base(name, fullPath)
    {
        Children = children;
        foreach (var child in children)
            child.Parent = this;
    }

    public IReadOnlyList<TreeNodeViewModel> Children { get; }

    private bool? _isChecked = false;
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_cascading)
            {
                Set(ref _isChecked, value);
                return;
            }
            var target = value ?? false;
            if (!Set(ref _isChecked, target, nameof(IsChecked)))
                return;
            foreach (var child in Children)
            {
                if (child is FileNodeViewModel file)
                    file.SetCheckedSilently(target);
                else if (child is FolderNodeViewModel folder)
                    folder.CascadeSilently(target);
            }
            var ancestor = Parent as FolderNodeViewModel;
            while (ancestor is not null)
            {
                ancestor.RecomputeCheckFromFiles();
                ancestor = ancestor.Parent as FolderNodeViewModel;
            }
        }
    }

    public void SetCheckedSilently(bool? value) => Set(ref _isChecked, value, nameof(IsChecked));

    /// Set this folder and its whole subtree (files and sub-folders) to a
    /// concrete state without bubbling. Used by a parent cascade, where the
    /// entire subtree is uniform so no node is indeterminate.
    private void CascadeSilently(bool target)
    {
        SetCheckedSilently(target);
        foreach (var child in Children)
        {
            if (child is FileNodeViewModel file)
                file.SetCheckedSilently(target);
            else if (child is FolderNodeViewModel folder)
                folder.CascadeSilently(target);
        }
    }

    /// Recompute this folder's tri-state from its descendant files without
    /// cascading back down (the _cascading guard suppresses the setter's
    /// cascade so a mixed set reads as indeterminate).
    public void RecomputeCheckFromFiles()
    {
        _cascading = true;
        IsChecked = FolderTree.AggregateCheck(
            DescendantFiles().Select(f => f.IsChecked).ToList());
        _cascading = false;
    }

    public IEnumerable<FileNodeViewModel> DescendantFiles()
    {
        foreach (var child in Children)
        {
            if (child is FileNodeViewModel file)
                yield return file;
            else if (child is FolderNodeViewModel folder)
                foreach (var f in folder.DescendantFiles())
                    yield return f;
        }
    }

    private IBrush _markerBrush = NodeMarkers.NotAnalyzed;
    public override IBrush MarkerBrush => _markerBrush;

    private string _rollupText = "";
    public string RollupText { get => _rollupText; private set => Set(ref _rollupText, value); }

    /// Recompute the marker and summary label from the subtree's current
    /// severities. Does not bubble; the caller refreshes ancestors.
    public void RefreshRollup()
    {
        var status = FolderTree.Rollup(DescendantFiles().Select(f => f.Severity));
        _markerBrush = status.Analyzed == 0
            ? NodeMarkers.NotAnalyzed
            : NodeMarkers.For(status.Worst);
        RaisePropertyChanged(nameof(MarkerBrush));
        RollupText = FormatRollup(status);
    }

    private static string FormatRollup(FolderStatus status)
    {
        if (status.Analyzed == 0)
            return $"{status.Total} file{(status.Total == 1 ? "" : "s")}";
        var parts = new List<string> { $"{status.Analyzed}/{status.Total}" };
        if (status.Problem > 0) parts.Add($"{status.Problem} problem{(status.Problem == 1 ? "" : "s")}");
        if (status.Suspect > 0) parts.Add($"{status.Suspect} suspect");
        return string.Join(" · ", parts);
    }
}

/// The marker dot palette, shared by files (own severity) and folders
/// (subtree worst). One SolidColorBrush per state.
public static class NodeMarkers
{
    public static readonly IBrush NotAnalyzed = new SolidColorBrush(Color.Parse("#3A3A3A"));
    public static readonly IBrush Clean = new SolidColorBrush(Color.Parse("#4A7A4A"));
    public static readonly IBrush Suspect = new SolidColorBrush(Color.Parse("#D8B060"));
    public static readonly IBrush Problem = new SolidColorBrush(Color.Parse("#E08080"));
    public static readonly IBrush Upsampled = new SolidColorBrush(Color.Parse("#B08FD8"));

    public static IBrush For(RowSeverity severity) => severity switch
    {
        RowSeverity.Problem => Problem,
        RowSeverity.Suspect => Suspect,
        _ => Clean,
    };

    /// The one whole-row marker rule, shared by the tree's file dot and the
    /// grid's File-column dot so the two can never disagree: violet for an
    /// upsampled verdict, else the overall severity color (worst of
    /// bandwidth and integrity).
    public static IBrush ForEntry(AuditEntry entry) =>
        entry.Row.Bandwidth is "Upsampled"
            ? Upsampled
            : For(FolderAudit.Severity(entry));
}
