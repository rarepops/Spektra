using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

        if (args is ["--compare", var pathA, var pathB, ..] && File.Exists(pathA) && File.Exists(pathB))
            Opened += (_, _) => _vm.OpenComparison(pathA, pathB);
        else
        {
            var files = args.Where(File.Exists).ToList();
            if (files.Count > 0)
                Opened += (_, _) => _vm.OpenFiles(files);
        }
    }

    public MainWindow() : this([]) { }

    private PixelPoint _normalPosition;
    private Size _normalSize;

    private void RestoreWindowPlacement()
    {
        if (_vm.Settings.Window is not { } w) return;
        // Position is physical px, Width/Height logical — the intersect test
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

        Title = tab is null ? "Spektra" : $"{tab.TabTitle} — Spektra";
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.Data.GetFiles()?.OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();
        if (paths is { Count: > 0 }) _vm.OpenFiles(paths);
    }

    private bool _dialogOpen;

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Border)?.DataContext is not ITab tab) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed)
        {
            _vm.CloseTab(tab);
            e.Handled = true;
        }
        else if (props.IsLeftButtonPressed)
        {
            _vm.Selected = tab;
            e.Handled = true;
        }
    }

    private void OnTabCloseClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is ITab tab)
            _vm.CloseTab(tab);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm.Selected is ComparisonViewModel cmp && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.T: cmp.FlipAB(); e.Handled = true; return;
                case Key.D: cmp.Mode = CompareMode.Diff; e.Handled = true; return;
                case Key.Escape: cmp.Mode = CompareMode.Both; e.Handled = true; return;
            }
        }
        if (e.Handled || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        switch (e.Key)
        {
            case Key.O:
                _ = OpenViaDialogAsync();
                e.Handled = true;
                break;
            case Key.W when _vm.Selected is { } tab:
                _vm.CloseTab(tab);
                e.Handled = true;
                break;
            case Key.Tab:
                _vm.SelectNext(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
                break;
        }
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e) =>
        await OpenViaDialogAsync();

    private async Task OpenViaDialogAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            await ShowOpenDialogAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async Task ShowOpenDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open audio files",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio")
                {
                    Patterns =
                    [
                        "*.flac", "*.mp3", "*.wav", "*.ogg", "*.opus", "*.m4a",
                        "*.aac", "*.wma", "*.ape", "*.wv", "*.aiff", "*.alac",
                    ],
                },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        var paths = files.Select(f => f.TryGetLocalPath())
            .Where(p => p is not null).Cast<string>().ToList();
        if (paths.Count > 0) _vm.OpenFiles(paths);
    }

    private async void OnDownloadFfmpegClicked(object? sender, RoutedEventArgs e) =>
        await _vm.DownloadFfmpegAsync();

    private async void OnCompareClicked(object? sender, RoutedEventArgs e)
    {
        var docs = _vm.OpenDocuments;
        if (docs.Count < 2)
        {
            _vm.StatusText = "Open at least two files to compare.";
            return;
        }
        var chooser = new CompareChooser(docs);
        await chooser.ShowDialog(this);
        if (chooser.Result is { } c && !ReferenceEquals(c.A, c.B))
            _vm.OpenComparison(c.A.FilePath, c.B.FilePath);
    }

    private async void OnAutoAlignClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Selected is ComparisonViewModel cmp) await cmp.AlignAsync();
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();
}
