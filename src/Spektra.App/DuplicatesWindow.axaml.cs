using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spektra.Core;

namespace Spektra.App;

/// The Duplicate Detective window: modeless, one instance (MainWindow manages
/// that), view and export only. Placement persists via AppSettings.
public partial class DuplicatesWindow : Window
{
    private readonly DuplicatesViewModel _vm;
    private readonly AppSettings _settings;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    // Parameterless constructor for the XAML previewer/loader only (Avalonia's
    // AVLN3001 wants one); real callers always use the (vm, settings) overload,
    // same pattern as CompareChooser's default-argument chain.
    public DuplicatesWindow() : this(new DuplicatesViewModel(new FfmpegPaths("", ""), new AppSettings()), new AppSettings()) { }

    public DuplicatesWindow(DuplicatesViewModel vm, AppSettings settings)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        DataContext = vm;
        if (settings.DuplicatesWindow is { } p)
        {
            // Position is physical px, Width/Height logical; the intersect test
            // is approximate across DPI scales, which is fine for "is it on-screen".
            var target = new PixelRect(p.X, p.Y, Math.Max(400, p.Width), Math.Max(300, p.Height));
            if (Screens.All.Any(s => s.Bounds.Intersects(target)))
            {
                Position = new PixelPoint(p.X, p.Y);
                Width = Math.Max(400, p.Width);
                Height = Math.Max(300, p.Height);
                if (p.Maximized) WindowState = WindowState.Maximized;
                _normalPosition = Position;
                _normalSize = new Size(Width, Height);
            }
        }
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
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
            // A scan must not outlive its window: it would keep most cores
            // busy with no cancel path left, and reopening (a fresh view
            // model) could start a second run beside it. Completed analysis
            // is cached, so cancelling loses nothing.
            _vm.Cancel();
            var pos = WindowState == WindowState.Normal ? Position : _normalPosition;
            var size = WindowState == WindowState.Normal ? ClientSize : _normalSize;
            if (size.Width >= 100 && size.Height >= 100)
                _settings.DuplicatesWindow = new WindowPlacement(
                    pos.X, pos.Y, (int)size.Width, (int)size.Height, WindowState == WindowState.Maximized);
            // Roots and placement are this window's to persist; Task 6's view
            // model deliberately never saves (view + export only).
            if (!SettingsStore.Save(SettingsStore.DefaultPath, _settings))
                _vm.SetError("Could not save the Duplicate Detective settings.");
        };
    }

    private async void OnAddFolderClicked(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a folder to scan",
            AllowMultiple = true,
        });
        foreach (var folder in picked)
            if (folder.TryGetLocalPath() is { } path)
                _vm.AddRoot(path);
    }

    private void OnRemoveFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (RootsList.SelectedItem is string root) _vm.RemoveRoot(root);
    }

    private void OnClearFoldersClicked(object? sender, RoutedEventArgs e) => _vm.ClearRoots();

    private void OnRootPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_vm.TryAddTypedRoot(RootPathBox.Text)) RootPathBox.Text = "";
        e.Handled = true;
    }

    // While a scan runs the folder list is frozen; refuse drags up front so
    // the cursor says no instead of the drop dying in a view-model guard.
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (_vm.IsScanning) e.DragEffects = DragDropEffects.None;
    }

    // Same shape as MainWindow's OnDrop, folders only: this window scans
    // roots, so a dropped file has nothing to attach to.
    private void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles()?.ToList() ?? [];
        var folders = items.OfType<IStorageFolder>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();
        foreach (var folder in folders) _vm.AddRoot(folder);
        if (folders.Count == 0 && items.Count > 0)
            _vm.SetError("Drop folders to add scan roots; single files are ignored.");
    }

    private void OnScanClicked(object? sender, RoutedEventArgs e) => _ = _vm.ScanAsync();

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => _vm.Cancel();

    // Pin the row highlight while its context menu is open: hover leaves the
    // row when the pointer moves into the menu, and without this nothing
    // marks which file the verbs act on.
    private void OnMemberMenuOpened(object? sender, RoutedEventArgs e)
    {
        if ((sender as ContextMenu)?.PlacementTarget is Control target) target.Classes.Add("menuOpen");
    }

    private void OnMemberMenuClosed(object? sender, RoutedEventArgs e)
    {
        if ((sender as ContextMenu)?.PlacementTarget is Control target) target.Classes.Remove("menuOpen");
    }

    private void OnMemberDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DupeMemberItem member)
            _vm.RequestOpen(member);
    }

    private async void OnCopyMemberPathClicked(object? sender, RoutedEventArgs e) =>
        await FileActions.CopyPathAsync(this, FileActions.ItemFrom(sender));

    private void OnRevealMemberClicked(object? sender, RoutedEventArgs e) =>
        FileActions.Reveal(FileActions.ItemFrom(sender));

    private void OnOpenMemberClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is DupeMemberItem member)
            _vm.RequestOpen(member);
    }

    private void OnCompareMemberClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is DupeMemberItem member)
            _vm.RequestCompare(member);
    }

    private async void OnCopyLosersClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is DupeGroupItem group && group.LoserPaths.Count > 0)
            await FileActions.CopyTextAsync(this, string.Join(Environment.NewLine, group.LoserPaths));
    }

    private async void OnCopyGroupPathsClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is DupeGroupItem group)
            await FileActions.CopyTextAsync(this, string.Join(Environment.NewLine, group.AllPaths));
    }

    // Open the format flyout on hover, so the three options are one move away
    // with no click. ShowAt just re-anchors if it is already open.
    private void OnExportHover(object? sender, PointerEventArgs e)
    {
        if ((sender as Button)?.Flyout is { } flyout) flyout.ShowAt((Control)sender!);
    }

    // One handler per format, chosen from the Export dropdown (the Tag carries
    // the extension). The format is explicit up front, so the save dialog only
    // offers that one type instead of hiding the choice behind the extension.
    private async void OnExportFormatClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.LastResult is not { } result) return;
        if ((sender as MenuItem)?.Tag is not string fmt) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export duplicate report",
            SuggestedFileName = $"duplicate-detective-report.{fmt}",
            DefaultExtension = fmt,
            FileTypeChoices = [new FilePickerFileType($"{fmt.ToUpperInvariant()} report") { Patterns = [$"*.{fmt}"] }],
        });
        if (file is null) return;
        try
        {
            var text = fmt switch
            {
                "csv" => Reporting.ToCsv(DuplicateScan.ToRows(result)),
                "json" => Reporting.ToJson(DuplicateScan.ToRows(result)),
                _ => HtmlReport.DupesDocument(result, "Spektra Duplicate Detective"),
            };
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // async void handler: an unguarded write failure would crash the
            // app instead of reporting (same guard as the folder view export).
            _vm.SetError($"Could not export the report: {ex.Message}");
        }
    }
}
