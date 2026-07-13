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

    // A row shows when it meets the severity tier and, when the grid is scoped,
    // lives under the scope folder.
    private bool Passes(FolderRow row) =>
        _vm is not null
        && row.Severity >= (RowSeverity)_vm.FilterIndex
        && (_vm.ScopeFolder is null || PathScope.IsUnder(row.FullPath, _vm.ScopeFolder));

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

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Grid.SelectedItem is FolderRow row)
        {
            _vm?.RequestOpen(row);
            e.Handled = true;
        }
    }

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var rows = _vm.ExportRows();
        if (rows.Count == 0) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export folder report",
            SuggestedFileName = _vm.TabTitle + "-report.csv",
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV report") { Patterns = ["*.csv"] },
                new FilePickerFileType("JSON report") { Patterns = ["*.json"] },
            ],
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
