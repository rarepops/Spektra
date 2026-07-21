using System.Collections.ObjectModel;
using Avalonia.Media;
using Spektra.Core;

namespace Spektra.App;

/// One tree node for a listed folder: header carries the recursive rollup so
/// a folder can be judged without expanding it.
public sealed class ManifestFolderItem : IFileItem
{
    public ManifestFolderItem(ManifestFolder folder)
    {
        Header = folder.Unreadable ? $"{folder.Name} · unreadable" : $"{folder.Name} · {folder.Rollup}";
        FullPath = folder.Path;
        IsUnreadable = folder.Unreadable;
        Children =
        [
            .. folder.Folders.Select(f => (object)new ManifestFolderItem(f)),
            .. folder.Files.Select(f => (object)new ManifestFileItem(f)),
        ];
    }

    public string Header { get; }
    public string FullPath { get; }
    public IReadOnlyList<object> Children { get; }
    public bool IsUnreadable { get; }

    /// Bound by the window's top-level TreeViewItem style; only the root
    /// starts open (a styled direct value cannot set IsExpanded, a bound
    /// item property can).
    public bool IsExpanded { get; set; }
}

/// One file leaf: the chip label plus its severity brush (NotAnalyzed grey
/// when the cache has never seen the file).
public sealed class ManifestFileItem(ManifestFile file) : IFileItem
{
    public string Name { get; } = file.Name;
    public string Path { get; } = file.Path;
    public string Kind { get; } = file.Kind;
    /// Gates the audio-only menu verbs: Open spectrogram is disabled on a
    /// jpg/nfo/cue rather than opening a tab that can only error.
    public bool IsAudio { get; } = file.IsAudio;
    public string SizeText { get; } = Format.Bytes(file.SizeBytes);
    public IBrush KindBrush { get; } = file.Severity switch
    {
        RowSeverity.Problem => NodeMarkers.Problem,
        RowSeverity.Suspect => NodeMarkers.Suspect,
        RowSeverity.Clean => NodeMarkers.Clean,
        _ => NodeMarkers.NotAnalyzed,
    };

    // Explicit: the display binding is Path, and renaming it would break the
    // XAML. The shared context-menu actions see it as FullPath.
    string IFileItem.FullPath => Path;
}

/// The Folder Manifest window's state: one folder at a time, listing only, never
/// decoding. Read-only by identity: nothing here modifies, moves, or deletes a
/// file. It can hand a path to the main window to open as a spectrogram, which
/// is still a read.
public sealed class FolderManifestViewModel(AppSettings settings) : ObservableObject
{
    public ObservableCollection<ManifestFolderItem> RootItems { get; } = [];

    /// Raised when a listed file asks to open as a spectrogram tab in the
    /// main window. Listing still never decodes anything itself.
    public event Action<string>? OpenFileRequested;
    public void RequestOpen(IFileItem item) => OpenFileRequested?.Invoke(item.FullPath);

    private string? _folder = settings.FolderManifestFolder;
    public string? Folder { get => _folder; private set => Set(ref _folder, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    /// Kind and Size lane widths, dragged at the header (Name is the star
    /// column that absorbs the difference). Saved per header like the audit
    /// grid's FolderColumnWidths; the setters clamp so a drag can never
    /// collapse a lane or push it past the window.
    private const double MinLane = 40;
    private const double MaxLane = 600;

    private double _kindLaneWidth = AppSettings.SavedColumnWidth(
        settings.ManifestColumnWidths, "Kind", 64, MinLane, MaxLane);
    public double KindLaneWidth
    {
        get => _kindLaneWidth;
        set { if (Set(ref _kindLaneWidth, Math.Clamp(value, MinLane, MaxLane))) SaveLane("Kind", _kindLaneWidth); }
    }

    private double _sizeLaneWidth = AppSettings.SavedColumnWidth(
        settings.ManifestColumnWidths, "Size", 68, MinLane, MaxLane);
    public double SizeLaneWidth
    {
        get => _sizeLaneWidth;
        set { if (Set(ref _sizeLaneWidth, Math.Clamp(value, MinLane, MaxLane))) SaveLane("Size", _sizeLaneWidth); }
    }

    private void SaveLane(string header, double px) =>
        (settings.ManifestColumnWidths ??= new())[header] = px;

    private string _footerText = "Pick a folder to see its manifest.";
    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    /// The filter box text: kinds/extensions, space separated. Applied live
    /// against the already-built tree, so typing never touches the disk.
    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set { if (Set(ref _filterText, value)) ApplyFilter(); }
    }

    /// Normalized kinds of the active filter, empty when showing everything.
    /// The window appends these to the suggested export file name.
    public IReadOnlyList<string> ActiveKinds { get; private set; } = [];

    /// The unfiltered build of the current folder; LastRoot is the filtered
    /// view of it, and exports read LastRoot so they write what is shown.
    private ManifestFolder? _fullRoot;
    private string _cacheNote = "";

    public ManifestFolder? LastRoot { get; private set; }

    /// The window's error surface (the footer line doubles as the status bar).
    public void SetError(string message) => FooterText = message;

    /// Pasted or typed input: validate before loading (Copy as path wraps in
    /// quotes, so strip those). True once a load started, so the caller can
    /// clear its input box.
    public bool TryLoadTyped(string? raw)
    {
        if (IsLoading) return false;
        var path = (raw ?? "").Trim().Trim('"').Trim();
        if (path.Length == 0) return false;
        if (!Directory.Exists(path))
        {
            SetError($"Not a folder: {path}");
            return false;
        }
        _ = LoadAsync(path);
        return true;
    }

    /// Empties the listing, for when the address bar is cleared: leaving the
    /// previous folder's tree on screen under an empty path box would show
    /// something the header says is not there. Returns the window to the
    /// state it opens in, and forgets the folder so the next launch does too.
    public void Clear()
    {
        if (IsLoading) return;
        RootItems.Clear();
        LastRoot = null;
        _fullRoot = null;
        _cacheNote = "";
        Folder = null;
        settings.FolderManifestFolder = null;
        FooterText = "Pick a folder to see its manifest.";
    }

    public async Task LoadAsync(string folder)
    {
        if (IsLoading) return;
        IsLoading = true;
        RootItems.Clear();
        LastRoot = null;
        _fullRoot = null;
        var cacheNote = "";
        try
        {
            AuditCache? cache = null;
            try { cache = AuditCache.Open(AuditCache.DefaultPath); }
            catch (Exception e) when (e is not OutOfMemoryException) { cacheNote = " · cache unavailable, no verdict chips"; }

            var localCache = cache;
            ManifestFolder root;
            try { root = await Task.Run(() => FolderManifest.Build(folder, localCache)); }
            finally { cache?.Dispose(); }

            _fullRoot = root;
            _cacheNote = cacheNote;
            Folder = folder;
            settings.FolderManifestFolder = folder;
            ApplyFilter();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetError($"Could not build the manifest: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        if (_fullRoot is not { } full) return;
        ActiveKinds = FolderManifest.ParseKinds(FilterText);
        var root = ActiveKinds.Count == 0 ? full : FolderManifest.Filter(full, ActiveKinds);
        LastRoot = root;
        RootItems.Clear();
        RootItems.Add(new ManifestFolderItem(root) { IsExpanded = true });
        var (files, folders) = CountOf(root);
        var filterNote = ActiveKinds.Count == 0 ? "" : $" · filter: {string.Join(" · ", ActiveKinds)}";
        FooterText = root.Unreadable
            ? $"Could not read {Folder}"
            : $"{files} files · {folders} folders · {root.Rollup}{filterNote}{_cacheNote}";
    }

    private static (int Files, int Folders) CountOf(ManifestFolder f)
    {
        var files = f.Files.Count;
        var folders = f.Folders.Count;
        foreach (var sub in f.Folders)
        {
            var (cf, cd) = CountOf(sub);
            files += cf;
            folders += cd;
        }
        return (files, folders);
    }
}
