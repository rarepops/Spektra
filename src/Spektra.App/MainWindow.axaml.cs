using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Spektra.Core;

namespace Spektra.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm = new();

    public MainWindow(LaunchRequest request)
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.SelectedChanged += OnSelectedDocumentChanged;
        _vm.DisplayChanged += ApplyDisplay;
        OnSelectedDocumentChanged(null);

        AddHandler(DragDrop.DropEvent, OnDrop);

        _vm.RecentFilesChanged += RebuildRecentMenu;
        RestoreWindowPlacement();
        RebuildRecentMenu();
        FolderViewCtl.Settings = _vm.Settings;
        FolderViewCtl.ShowInManifestRequested += path => EnsureManifestWindow(path);

        PositionChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal) _normalPosition = e.Point;
        };
        SizeChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal) _normalSize = e.NewSize;
        };
        Closing += (_, _) =>
        {
            FolderViewCtl.HarvestLayout();
            _vm.SnapshotSession();
            SaveWindowPlacement();
        };

        Opened += async (_, _) =>
        {
            if (_vm.CheckForUpdatesOnStartup) await _vm.CheckForUpdatesOnStartupAsync();
        };

        Opened += async (_, _) => await ApplyAsync(request, isStartup: true);
    }

    /// Acts on one launch's request. Runs on startup for this process's own
    /// command line, and again for every command line a later process hands over.
    private async Task ApplyAsync(LaunchRequest request, bool isStartup)
    {
        if (request.Compare is { } pair)
        {
            var startMode = pair.Mode switch
            {
                "a" => (CompareMode?)CompareMode.A,
                "b" => CompareMode.B,
                "diff" => CompareMode.Diff,
                "both" => CompareMode.Both,
                _ => null,
            };
            if (_vm.OpenComparison(pair.PathA, pair.PathB) is not { } cmp) return;
            await cmp.Loaded; // real load completion, not a fixed delay
            if (pair.AutoAlign) await cmp.AlignAsync();
            if (startMode is { } m) cmp.Mode = m;
            return;
        }

        // Parse's compare path returns early, so DupesRoot/ManifestRoot are
        // structurally impossible alongside a Compare; these run only when
        // the block above did not already return.
        // Both target the one Duplicate Detective window, so a command line
        // naming each would have the second silently overwrite the first.
        // --diff wins as the more specific request.
        if (request.Diff is { } diff) EnsureDupesWindow(diff);
        else if (request.DupesRoot is { } dupesRoot) EnsureDupesWindow(dupesRoot);
        if (request.ManifestRoot is { } manifestRoot) EnsureManifestWindow(manifestRoot);

        if (request.Files.Count > 0) _vm.OpenFiles(request.Files);
        foreach (var folder in request.Folders) _vm.OpenFolder(folder);

        // A targeted open stays targeted: restore only on a bare launch, and
        // never on a handoff, where those tabs are already on screen.
        if (request.IsBare && isStartup) _vm.RestoreSessionTabs();
    }

    /// A second process handed us its command line. Surface first, so the user
    /// sees the window they just asked for even when nothing else happens.
    public void AcceptHandoff(LaunchRequest request)
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();

        if (request.IsBare) return; // raising the window was the whole job
        _ = ApplyAsync(request, isStartup: false);
    }

    public MainWindow(string[] args) : this(LaunchArgs.Parse(args)) { }

    public MainWindow() : this([]) { }

    private PixelPoint _normalPosition;
    private Size _normalSize;

    private void RestoreWindowPlacement()
    {
        if (_vm.Settings.Window is not { } w) return;
        // Position is physical px, Width/Height logical; the intersect test
        // is approximate across DPI scales, which is fine for "is it on-screen".
        var target = new PixelRect(w.X, w.Y, Math.Max(400, w.Width), Math.Max(300, w.Height));
        if (!Screens.All.Any(s => s.Bounds.Intersects(target))) return;
        Position = new PixelPoint(w.X, w.Y);
        Width = Math.Max(400, w.Width);
        Height = Math.Max(300, w.Height);
        if (w.Maximized) WindowState = WindowState.Maximized;
        _normalPosition = Position;
        _normalSize = new Size(Width, Height);
    }

    private void SaveWindowPlacement()
    {
        var pos = WindowState == WindowState.Normal ? Position : _normalPosition;
        var size = WindowState == WindowState.Normal ? ClientSize : _normalSize;
        if (size.Width < 100 || size.Height < 100) return;
        _vm.Settings.Window = new WindowPlacement(
            pos.X, pos.Y, (int)size.Width, (int)size.Height,
            WindowState == WindowState.Maximized);
        _vm.SaveSettings();
    }

    private void RebuildRecentMenu()
    {
        var items = new List<Control>();
        foreach (var path in _vm.Settings.RecentFiles)
        {
            var captured = path;
            var item = new MenuItem { Header = path.Replace("_", "__") }; // __ escapes access-key marker
            item.Click += (_, _) =>
            {
                if (Directory.Exists(captured)) _vm.OpenFolder(captured);
                else _vm.OpenFile(captured);
            };
            items.Add(item);
        }
        if (items.Count > 0)
        {
            items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear Recent" };
            clear.Click += (_, _) => _vm.ClearRecent();
            items.Add(clear);
        }
        RecentMenu.ItemsSource = items;
        RecentMenu.IsEnabled = items.Count > 0;
    }

    private void OnSelectedDocumentChanged(ITab? tab)
    {
        var doc = tab as DocumentViewModel;
        var cmp = tab as ComparisonViewModel;

        DocHost.DataContext = doc;
        DocHost.IsVisible = doc is not null;
        Spectro.Attach(doc);
        Spectro.IsVisible = doc is not null;

        CompareHost.DataContext = cmp;
        CompareHost.IsVisible = cmp is not null;
        CompareStrip.DataContext = cmp;
        CompareStrip.IsVisible = cmp is not null;
        CompareSurfaceCtl.Attach(cmp);
        CompareSurfaceCtl.IsVisible = cmp is not null;

        var folder = tab as FolderViewModel;
        FolderViewCtl.Attach(folder);
        FolderViewCtl.IsVisible = folder is not null;

        ApplyDisplay();

        Title = tab is null ? "Spektra" : $"{tab.TabTitle} - Spektra";
    }

    private void ApplyDisplay()
    {
        var display = _vm.ToDisplaySettings();
        Spectro.SetDisplay(display);
        CompareSurfaceCtl.SetDisplay(display);
    }
}
