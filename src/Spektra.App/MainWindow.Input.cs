using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Spektra.App;

// Pointer, keyboard, and drag-and-drop routing for the shell window.
public partial class MainWindow
{
    private void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles()?.ToList() ?? [];
        var files = items.OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();
        var folders = items.OfType<IStorageFolder>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();
        if (files.Count > 0) _vm.OpenFiles(files);
        foreach (var folder in folders) _vm.OpenFolder(folder);
    }

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
        if (e.Key == Key.F1)
        {
            _ = new ControlsWindow().ShowDialog(this);
            e.Handled = true;
            return;
        }
        if (_vm.Selected is ComparisonViewModel cmp && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.T: cmp.FlipAB(); e.Handled = true; return;
                case Key.D: cmp.Mode = CompareMode.Diff; e.Handled = true; return;
                case Key.Escape: cmp.Mode = CompareMode.Both; e.Handled = true; return;
                case Key.A: _ = cmp.AlignAsync(); e.Handled = true; return;
            }
        }
        if (e.Key == Key.F5)
        {
            switch (_vm.Selected)
            {
                case DocumentViewModel rdoc: _ = rdoc.LoadOverviewAsync(); e.Handled = true; return;
                case ComparisonViewModel rcmp: _ = rcmp.LoadAsync(); e.Handled = true; return;
                case FolderViewModel rfold:
                    rfold.StartScan(fresh: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    e.Handled = true;
                    return;
            }
        }
        if (e.Handled || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        switch (e.Key)
        {
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                _ = OpenFolderViaDialogAsync();
                e.Handled = true;
                break;
            case Key.O:
                _ = OpenViaDialogAsync();
                e.Handled = true;
                break;
            case Key.E:
                _ = new PreferencesWindow(_vm).ShowDialog(this);
                e.Handled = true;
                break;
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                _ = ExportReportAsync();
                e.Handled = true;
                break;
            case Key.S:
                _ = SaveImageAsync();
                e.Handled = true;
                break;
            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                _ = CopyImageAsync();
                e.Handled = true;
                break;
            case Key.R:
                _vm.ShowSpectrum = !_vm.ShowSpectrum;
                e.Handled = true;
                break;
            case Key.H:
                _vm.ShowCrosshair = !_vm.ShowCrosshair;
                e.Handled = true;
                break;
            case Key.I when _vm.Selected is DocumentViewModel doc:
                _ = doc.ToggleIntegrityAsync();
                e.Handled = true;
                break;
            case Key.L when _vm.Selected is DocumentViewModel ldoc:
                _ = ldoc.ToggleLoudnessAsync();
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
            case Key.D:
                _ = CompareViaDialogAsync();
                e.Handled = true;
                break;
            case Key.D0 or Key.NumPad0:
                switch (_vm.Selected)
                {
                    case DocumentViewModel zdoc: zdoc.Viewport.Reset(); break;
                    case ComparisonViewModel zcmp: zcmp.Viewport.Reset(); break;
                }
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D9:
                SelectTab(e.Key - Key.D1);
                e.Handled = true;
                break;
            case >= Key.NumPad1 and <= Key.NumPad9:
                SelectTab(e.Key - Key.NumPad1);
                e.Handled = true;
                break;
            case Key.Down or Key.Up when _vm.Selected is DocumentViewModel cdoc && cdoc.HasMultipleChannels:
                {
                    var step = e.Key == Key.Down ? 1 : -1;
                    cdoc.SelectedChannelIndex = Math.Clamp(
                        cdoc.SelectedChannelIndex + step, 0, cdoc.ChannelOptions.Count - 1);
                    e.Handled = true;
                    break;
                }
        }
    }

    private void SelectTab(int index)
    {
        if (index >= 0 && index < _vm.Tabs.Count) _vm.Selected = _vm.Tabs[index];
    }
}
