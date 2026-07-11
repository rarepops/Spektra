using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Spektra.Core;

namespace Spektra.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm = new();

    public MainWindow(string[] args)
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

        PositionChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal) _normalPosition = e.Point;
        };
        SizeChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal) _normalSize = e.NewSize;
        };
        Closing += (_, _) => SaveWindowPlacement();

        Opened += async (_, _) =>
        {
            if (_vm.CheckForUpdatesOnStartup) await _vm.CheckForUpdatesOnStartupAsync();
        };

        if (args is ["--compare", var pathA, var pathB, ..] && File.Exists(pathA) && File.Exists(pathB))
        {
            var autoAlign = args.Contains("--auto");
            var mi = Array.IndexOf(args, "--mode");
            var startMode = mi >= 0 && mi + 1 < args.Length
                ? args[mi + 1].ToLowerInvariant() switch
                {
                    "a" => (CompareMode?)CompareMode.A,
                    "b" => CompareMode.B,
                    "diff" => CompareMode.Diff,
                    "both" => CompareMode.Both,
                    _ => null,
                }
                : null;
            Opened += async (_, _) =>
            {
                if (_vm.OpenComparison(pathA, pathB) is not { } cmp) return;
                await cmp.Loaded; // real load completion, not a fixed delay
                if (autoAlign) await cmp.AlignAsync();
                if (startMode is { } m) cmp.Mode = m;
            };
        }
        else
        {
            var files = args.Where(File.Exists).ToList();
            var folders = args.Where(Directory.Exists).ToList();
            if (files.Count > 0)
                Opened += (_, _) => _vm.OpenFiles(files);
            if (folders.Count > 0)
                Opened += (_, _) =>
                {
                    foreach (var folder in folders) _vm.OpenFolder(folder);
                };
        }
    }

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
            item.Click += (_, _) => _vm.OpenFile(captured);
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

        Title = tab is null ? "Spektra" : $"{tab.TabTitle} — Spektra";
    }

    private void ApplyDisplay()
    {
        var display = _vm.ToDisplaySettings();
        Spectro.SetDisplay(display);
        CompareSurfaceCtl.SetDisplay(display);
    }
}
