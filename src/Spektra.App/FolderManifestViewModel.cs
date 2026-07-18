using System.Collections.ObjectModel;
using Avalonia.Media;
using Spektra.Core;

namespace Spektra.App;

/// One tree node for a listed folder: header carries the recursive rollup so
/// a folder can be judged without expanding it.
public sealed class ManifestFolderItem
{
    public ManifestFolderItem(ManifestFolder folder)
    {
        Header = folder.Unreadable ? $"{folder.Name} · unreadable" : $"{folder.Name} · {folder.Rollup}";
        IsUnreadable = folder.Unreadable;
        Children =
        [
            .. folder.Folders.Select(f => (object)new ManifestFolderItem(f)),
            .. folder.Files.Select(f => (object)new ManifestFileItem(f)),
        ];
    }

    public string Header { get; }
    public IReadOnlyList<object> Children { get; }
    public bool IsUnreadable { get; }

    /// Bound by the window's top-level TreeViewItem style; only the root
    /// starts open (a styled direct value cannot set IsExpanded, a bound
    /// item property can).
    public bool IsExpanded { get; set; }
}

/// One file leaf: the chip label plus its severity brush (NotAnalyzed grey
/// when the cache has never seen the file).
public sealed class ManifestFileItem(ManifestFile file)
{
    public string Name { get; } = file.Name;
    public string Path { get; } = file.Path;
    public string Kind { get; } = file.Kind;
    public string SizeText { get; } = Format.Bytes(file.SizeBytes);
    public IBrush KindBrush { get; } = file.Severity switch
    {
        RowSeverity.Problem => NodeMarkers.Problem,
        RowSeverity.Suspect => NodeMarkers.Suspect,
        RowSeverity.Clean => NodeMarkers.Clean,
        _ => NodeMarkers.NotAnalyzed,
    };
}

/// The Folder Manifest window's state: one folder at a time, listing only, never
/// decoding. View-only by identity; nothing here can touch the files.
public sealed class FolderManifestViewModel(AppSettings settings) : ObservableObject
{
    public ObservableCollection<ManifestFolderItem> RootItems { get; } = [];

    private string? _folder = settings.FolderManifestFolder;
    public string? Folder { get => _folder; private set => Set(ref _folder, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    private string _footerText = "Pick a folder to see its manifest.";
    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

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

    public async Task LoadAsync(string folder)
    {
        if (IsLoading) return;
        IsLoading = true;
        RootItems.Clear();
        LastRoot = null;
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

            LastRoot = root;
            Folder = folder;
            settings.FolderManifestFolder = folder;
            RootItems.Add(new ManifestFolderItem(root) { IsExpanded = true });
            var (files, folders) = CountOf(root);
            FooterText = root.Unreadable
                ? $"Could not read {folder}"
                : $"{files} files · {folders} folders · {root.Rollup}{cacheNote}";
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
