using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
            Filter = o => _vm is null || ((FolderRow)o).Severity >= (RowSeverity)_vm.FilterIndex,
        };
        Grid.ItemsSource = _view;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderViewModel.FilterIndex)) _view?.Refresh();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => _vm?.Cancel();

    private void OnRescanClicked(object? sender, RoutedEventArgs e) =>
        _vm?.StartScan(fresh: _shiftHeld);

    // Track Shift for Shift+click Rescan; KeyDown/Up bubble through the window.
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
        await ReportWriter.WriteAsync(file, rows);
    }
}
