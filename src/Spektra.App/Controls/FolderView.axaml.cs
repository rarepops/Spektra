using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Spektra.Core;
using System.ComponentModel;

namespace Spektra.App.Controls;

/// Grid surface for a FolderViewModel tab. Attach() swaps the active view
/// model, mirroring SpectrogramView/CompareSurface.
public partial class FolderView : UserControl
{
    private FolderViewModel? _vm;
    private DataGridCollectionView? _view;

    public FolderView()
    {
        InitializeComponent();
    }

    private AppSettings? _settings;

    /// Wired once by the shell; applies any saved folder layout. The one
    /// FolderView instance is shared by every folder tab, so the layout is
    /// global: HarvestLayout snapshots it back before the shell saves.
    public AppSettings? Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            ApplyLayout();
        }
    }

    private void ApplyLayout()
    {
        if (_settings is null) return;
        if (_settings.FolderTreeWidth is { } tree)
            Split.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(tree, 120, 1200));
        if (_settings.FolderColumnWidths is { } widths)
            foreach (var column in Grid.Columns)
                if (column.Header is string header && widths.TryGetValue(header, out var px))
                    column.Width = new DataGridLength(Math.Clamp(px, 40, 4000));
    }

    /// Snapshot the current layout into settings; the shell persists them
    /// on close.
    public void HarvestLayout()
    {
        if (_settings is null) return;
        // Never shown this session means never measured (ActualWidth 0):
        // keep whatever layout is already saved.
        var tree = Split.ColumnDefinitions[0].ActualWidth;
        if (double.IsNaN(tree) || tree <= 0) return;
        _settings.FolderTreeWidth = tree;
        var widths = new Dictionary<string, double>();
        foreach (var column in Grid.Columns)
            if (column.Header is string header && column.ActualWidth > 0)
                widths[header] = column.ActualWidth;
        if (widths.Count > 0) _settings.FolderColumnWidths = widths;
    }

    public void Attach(FolderViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        DataContext = vm;
        if (vm is null)
        {
            Grid.ItemsSource = null;
            _view = null;
            return;
        }
        vm.PropertyChanged += OnVmPropertyChanged;
        _view = new DataGridCollectionView(vm.Rows)
        {
            Filter = o => Passes((FolderRow)o),
        };
        Grid.ItemsSource = _view;
    }

    // Delegates to the view model's rule so the grid and Export share one
    // definition of "visible" (severity tier + drilldown scope).
    private bool Passes(FolderRow row) => _vm is not null && _vm.IsRowVisible(row);

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FolderViewModel.FilterIndex)
            or nameof(FolderViewModel.ScopeFolder))
            _view?.Refresh();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => _vm?.Cancel();

    private void OnAnalyzeClicked(object? sender, RoutedEventArgs e) =>
        _vm?.Analyze(fresh: _shiftHeld);

    private void OnSelectAllClicked(object? sender, RoutedEventArgs e) =>
        _vm?.SelectAll();

    private void OnSelectNoneClicked(object? sender, RoutedEventArgs e) =>
        _vm?.SelectNone();

    private void OnDrilldownClicked(object? sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is FolderNodeViewModel folder)
            _vm?.Drilldown(folder.FullPath);
    }

    private void OnDrillUpClicked(object? sender, RoutedEventArgs e) =>
        _vm?.DrillUp();

    private void OnShowAllClicked(object? sender, RoutedEventArgs e) =>
        _vm?.ShowAll();

    // Track Shift for Shift+click Analyze; KeyDown/Up bubble through the window.
    private bool _shiftHeld;
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _shiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _shiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _shiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Two quick clicks on a column header (sort, then flip direction) also
        // raise DoubleTapped on the grid; only a tap landing on a row opens.
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is null) return;
        if (Grid.SelectedItem is FolderRow row) _vm?.RequestOpen(row);
    }

    // Double-clicking a file in the tree opens it as a spectrogram tab, the
    // same as the grid; un-analyzed files open too (the viewer analyzes on
    // load). Folder nodes fall through to the tree's own expand/collapse.
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        // A double-click on the checkbox is a (double) check gesture, not open.
        if ((e.Source as Visual)?.FindAncestorOfType<CheckBox>(includeSelf: true) is not null) return;
        if (Tree.SelectedItem is FileNodeViewModel file) _vm?.RequestOpen(file.FullPath);
    }

    // The menus across the grid and the tree all bind to IFileItem, so one
    // handler per verb serves every surface. The generic two delegate to
    // FileActions; the Spektra verbs route to the view model.

    private async void OnMenuCopyPathClicked(object? sender, RoutedEventArgs e) =>
        await FileActions.CopyPathAsync(TopLevel.GetTopLevel(this), FileActions.ItemFrom(sender));

    private void OnMenuRevealClicked(object? sender, RoutedEventArgs e) =>
        FileActions.Reveal(FileActions.ItemFrom(sender));

    private void OnMenuOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (FileActions.ItemFrom(sender) is { } item) _vm?.RequestOpen(item.FullPath);
    }

    // Always fresh: a "Re-analyze" that honored the cache would do nothing.
    // The grid's item is a FolderRow and the tree's is a FileNodeViewModel,
    // so both resolve to a tree node, which is what analysis is driven from.
    private void OnMenuReanalyzeClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var node = (sender as MenuItem)?.DataContext switch
        {
            FileNodeViewModel file => file,
            FolderRow row => _vm.FileNodeFor(row.FullPath),
            _ => null,
        };
        if (node is not null) _vm.AnalyzeFiles([node], fresh: true);
    }

    private void OnMenuDrilldownClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is FolderNodeViewModel folder)
            _vm?.Drilldown(folder.FullPath);
    }

    // Honors the cache: analyzing a folder is a bulk action, and re-decoding
    // an album that is already done is exactly what the cache exists to avoid.
    private void OnMenuAnalyzeFolderClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is FolderNodeViewModel folder)
            _vm?.AnalyzeFiles(FolderViewModel.FilesUnder(folder), fresh: false);
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Grid.SelectedItem is FolderRow row)
        {
            _vm?.RequestOpen(row);
            e.Handled = true;
        }
    }

    // Open the format flyout on hover, matching the other export dropdowns.
    private void OnExportHover(object? sender, PointerEventArgs e)
    {
        if ((sender as Button)?.Flyout is { } flyout) flyout.ShowAt((Control)sender!);
    }

    // One handler per format (the Tag carries the extension); the save dialog is
    // pre-set to that one type. ReportWriter still selects by the extension.
    private async void OnExportFormatClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top) return;
        if ((sender as MenuItem)?.Tag is not string fmt) return;
        var rows = _vm.ExportRows();
        if (rows.Count == 0) return;
        // Sanitize: a drive-root tab title is "Z:\", whose ':' and '\' are
        // illegal in a file name and break the save dialog. GetInvalidFileNameChars
        // is OS-specific, so this adapts per platform.
        var stem = _vm.TabTitle;
        foreach (var c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export folder report",
            SuggestedFileName = $"{stem}-report.{fmt}",
            DefaultExtension = fmt,
            FileTypeChoices = [new FilePickerFileType($"{fmt.ToUpperInvariant()} report") { Patterns = [$"*.{fmt}"] }],
        });
        if (file is null) return;
        try
        {
            await ReportWriter.WriteAsync(file, rows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // This handler is async void (event wiring): an unguarded write
            // failure here would crash the app instead of reporting.
            _vm.SetErrorStatus($"Could not export the report: {ex.Message}");
        }
    }
}
