using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Spektra.Core;

namespace Spektra.App;

/// The Imposter Buster window: modeless, one instance (MainWindow manages that), view and export
/// only. Placement and column widths persist via AppSettings.
public partial class ImposterWindow : Window
{
    private readonly ImposterViewModel _vm;
    private readonly AppSettings _settings;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    // Parameterless constructor for the XAML previewer and loader only (Avalonia's AVLN3001 wants
    // one); real callers always use the (vm, settings) overload, as DuplicatesWindow's do.
    public ImposterWindow() : this(new ImposterViewModel(new FfmpegPaths("", ""), new AppSettings()), new AppSettings()) { }

    public ImposterWindow(ImposterViewModel vm, AppSettings settings)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        DataContext = vm;
        ShowRows();
        // Rows is replaced as a whole and never edited, so a new list is the one change to follow.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ImposterViewModel.Rows)) ShowRows();
        };
        ApplyColumnWidths();
        if (settings.ImposterWindow is { } p)
        {
            // Position is physical px, Width/Height logical; the intersect test is approximate
            // across DPI scales, which is fine for "is it on-screen".
            var target = new PixelRect(p.X, p.Y, Math.Max(400, p.Width), Math.Max(300, p.Height));
            if (Screens.All.FirstOrDefault(s => s.Bounds.Intersects(target)) is { } screen)
            {
                // Tool-window policy (see WindowSizing): the size is remembered, the maximized
                // state deliberately is not, and the size ceiling is what clears a record already
                // poisoned by the maximize race.
                var work = screen.WorkingArea;
                (Width, Height) = WindowSizing.ToolWindowRestore(
                    p.Width, p.Height, work.Width / screen.Scaling, work.Height / screen.Scaling);
                // A maximized placement's position is the shell's own offset (frame hanging off the
                // work area's corner), not anywhere a person put a window; let the OS place this one.
                if (!p.Maximized) Position = new PixelPoint(p.X, p.Y);
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
            // A scan must not outlive its window: it would keep most cores busy with no cancel
            // path left, and reopening (a fresh view model) could start a second run beside it.
            // Completed analysis is cached, so cancelling loses nothing.
            _vm.Cancel();
            var pos = WindowState == WindowState.Normal ? Position : _normalPosition;
            var size = WindowState == WindowState.Normal ? ClientSize : _normalSize;
            // Closing maximized with the maximized client size in _normalSize is the maximize race
            // (see WindowSizing); writing it through would poison the remembered size, so keep the
            // one already on record.
            if (WindowState == WindowState.Maximized
                && WindowSizing.IsMaximizeGhost(size.Width, size.Height, ClientSize.Width, ClientSize.Height))
            {
                if (_settings.ImposterWindow is { } prev)
                    _settings.ImposterWindow = prev with { Maximized = true };
            }
            else if (size.Width >= 100 && size.Height >= 100)
                _settings.ImposterWindow = new WindowPlacement(
                    pos.X, pos.Y, (int)size.Width, (int)size.Height, WindowState == WindowState.Maximized);
            HarvestColumnWidths();
            // The view model saves the folders into the settings; writing them out, with the
            // placement and the column widths, is this window's job.
            if (!SettingsStore.Save(SettingsStore.DefaultPath, _settings))
                _vm.SetError("Could not save the Imposter Buster settings.");
        };
    }

    /// Rows is replaced on every view switch, filter edit and scan, and bound straight to the grid
    /// each new list would come up unsorted. So the grid gets each list in a view of its own, as
    /// the folder grid does, and the sort on the outgoing view (its first column, if the user
    /// sorted by several) is put back through that column's own Sort, the path a header click
    /// takes, so the header shows it too. A sort that cannot be carried over is dropped, never
    /// fatal: the list shows unsorted.
    private void ShowRows()
    {
        var sort = (RowsGrid.ItemsSource as DataGridCollectionView)?.SortDescriptions.FirstOrDefault();
        RowsGrid.ItemsSource = new DataGridCollectionView(_vm.Rows);
        if (sort is not { HasPropertyPath: true }) return;
        try
        {
            RowsGrid.Columns.FirstOrDefault(c => c.SortMemberPath == sort.PropertyPath)?.Sort(sort.Direction);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            RowsGrid.ItemsSource = new DataGridCollectionView(_vm.Rows);
        }
    }

    /// Saved widths are keyed by header, as the folder grid's are. A header with no saved width
    /// keeps its XAML width, and a hand-edited junk value never reaches a column.
    private void ApplyColumnWidths()
    {
        if (_settings.ImposterColumnWidths is not { } widths) return;
        foreach (var column in RowsGrid.Columns)
        {
            if (column.Header is not string header) continue;
            var px = AppSettings.SavedColumnWidth(widths, header, double.NaN, 40, 4000);
            if (!double.IsNaN(px)) column.Width = new DataGridLength(px);
        }
    }

    private void HarvestColumnWidths()
    {
        var widths = new Dictionary<string, double>();
        foreach (var column in RowsGrid.Columns)
            if (column.Header is string header && column.ActualWidth > 0)
                widths[header] = column.ActualWidth;
        // A grid never measured has no widths to give, so the ones on record stay.
        if (widths.Count > 0) _settings.ImposterColumnWidths = widths;
    }

    // Esc abandons an edit and puts the real reference back, the usual address-bar escape hatch.
    // Enter sets the reference; a refusal leaves the typed text in place to be corrected, with
    // the reason in the footer.
    private void OnReferenceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ShowReference();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        if (_vm.TrySetReference(ReferenceBox.Text)) ShowReference();
        e.Handled = true;
    }

    private async void OnBrowseReferenceClicked(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the reference folder",
            AllowMultiple = false,
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path && _vm.TrySetReference(path))
            ShowReference();
    }

    // The OneWay binding rewrites the box only when the reference changes, and setting the same
    // folder again (pasted in quotes, or picked once more) changes nothing, so the box is put
    // right here. SetCurrentValue rather than the Text setter keeps the binding, so a reference
    // set later (by the launch switch) still reaches the box.
    private void ShowReference() => ReferenceBox.SetCurrentValue(TextBox.TextProperty, _vm.Reference);

    private async void OnAddFolderClicked(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a folder to check",
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

    // While a scan runs the folders are frozen; refuse drags up front so the cursor says no
    // instead of the drop dying in a view-model guard.
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (_vm.IsScanning) e.DragEffects = DragDropEffects.None;
    }

    // Folders only, as in Duplicate Detective: a dropped file has nothing to attach to. Every
    // dropped folder is a folder to check; the reference is chosen in its own box.
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
            _vm.SetError("Drop folders to add folders to check; single files are ignored.");
    }

    private void OnScanClicked(object? sender, RoutedEventArgs e) => _ = _vm.ScanAsync();

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => _vm.Cancel();

    // A row does not know which view is on show, so the window dims it: the confirmed rows recede
    // in the To hear view only, so the copies to hear stand out. LoadingRow runs for every row the
    // grid prepares, again for each row of a new list (a view switch or a filter edit replaces the
    // list) and for a row reused for another item, so the class is set both ways.
    private void OnLoadingRow(object? sender, DataGridRowEventArgs e) =>
        e.Row.Classes.Set("dim", _vm.ShowToHear && e.Row.DataContext is ImposterRowItem { IsConfirmed: true });

    // A copy compares with its reference file, the question this window exists for; any other row
    // opens its file. Two quick clicks on a column header (sort, then flip direction) also raise
    // DoubleTapped on the grid, so only a tap that lands on a row counts.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true)?.DataContext
            is not ImposterRowItem row) return;
        if (row.CanCompare) _vm.RequestCompare(row);
        else _vm.RequestOpen(row);
    }

    private void OnMenuCompareClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is ImposterRowItem row) _vm.RequestCompare(row);
    }

    private void OnMenuOpenClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is ImposterRowItem row) _vm.RequestOpen(row);
    }

    private void OnMenuPlayClicked(object? sender, RoutedEventArgs e)
    {
        if (FileActions.Play(FileActions.ItemFrom(sender)) is { } problem) _vm.SetError(problem);
    }

    private async void OnMenuCopyPathClicked(object? sender, RoutedEventArgs e) =>
        await FileActions.CopyPathAsync(this, FileActions.ItemFrom(sender));

    private void OnMenuRevealClicked(object? sender, RoutedEventArgs e) =>
        FileActions.Reveal(FileActions.ItemFrom(sender));

    // Open the format flyout on hover, so the options are one move away with no click. ShowAt just
    // re-anchors if it is already open.
    private void OnExportHover(object? sender, PointerEventArgs e)
    {
        if ((sender as Button)?.Flyout is { } flyout) flyout.ShowAt((Control)sender!);
    }

    // Every row of the scan, whatever the view and the filter, the same rows `spektra-cli
    // imposters` writes; or, for the Tag "best", the best copy of every song as a plain list of
    // paths to build a library from. The save dialog offers the one type chosen.
    private async void OnExportFormatClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.LastResult is not { } result) return;
        if ((sender as MenuItem)?.Tag is not string fmt) return;
        var best = fmt == "best";
        var ext = best ? "txt" : fmt;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = best ? "Export the best copies" : "Export the Imposter Buster report",
            SuggestedFileName = best ? "imposter-buster-best-copies.txt" : $"imposter-buster-report.{ext}",
            DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType(best ? "Text list" : $"{ext.ToUpperInvariant()} report") { Patterns = [$"*.{ext}"] }],
        });
        if (file is null) return;
        try
        {
            var text = fmt switch
            {
                "best" => ImposterView.BestCopiesList(result),
                "json" => Reporting.ToJson(ImposterReport.Rows(result)),
                _ => Reporting.ToCsv(ImposterReport.Rows(result)),
            };
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // async void handler: an unguarded write failure would crash the app instead of
            // reporting, as in the other windows' exports.
            _vm.SetError($"Could not export {(best ? "the best copies" : "the report")}: {ex.Message}");
        }
    }

    // Written like the export above: the picker's stream starts empty, so a shorter playlist saved
    // over a longer one leaves nothing of it behind, and StreamWriter's default UTF-8 carries no
    // byte-order mark a player could trip on.
    private async void OnSavePlaylistClicked(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save listening playlist",
            SuggestedFileName = "imposter-buster.m3u8",
            DefaultExtension = "m3u8",
            FileTypeChoices = [new FilePickerFileType("M3U8 playlist") { Patterns = ["*.m3u8"] }],
        });
        if (file is null) return;
        try
        {
            var text = _vm.ListeningPlaylist();
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.SetError($"Could not save the playlist: {ex.Message}");
        }
    }
}
