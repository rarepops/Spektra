using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spektra.Core;

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

    /// The keyboard is a lookup, not a switch on keys: the pressed gesture
    /// becomes a KeyCommand through the key map, and only then does the shell
    /// decide what to do with it. That is what lets keybindings.json move a
    /// key without any of this changing, and what keeps the menus and the
    /// Controls window from drifting away from what the keys actually do.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        // F1 is reserved. It opens the sheet documenting every other key, so a
        // user who rebound it badly would lose the way to find out what they
        // had done.
        if (e.Key == Key.F1)
        {
            _ = new ControlsWindow(_vm.Keys, _vm.KeyProblems).ShowDialog(this);
            e.Handled = true;
            return;
        }

        // Positional rather than one command each, so these stay hard-wired
        // along with their numpad twins.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && TabIndexOf(e.Key) is { } tab)
        {
            SelectTab(tab);
            e.Handled = true;
            return;
        }

        if (_vm.Keys.Resolve(StrokeOf(e)) is { } command && Run(command))
            e.Handled = true;
    }

    /// Avalonia's Key enum name is exactly the name the key map matches on,
    /// which is the whole reason KeyStroke holds a string rather than trying
    /// to mirror a framework enum inside Core.
    private static KeyStroke StrokeOf(KeyEventArgs e)
    {
        var mods = KeyMods.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) mods |= KeyMods.Ctrl;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) mods |= KeyMods.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) mods |= KeyMods.Alt;
        // The numpad's zero has always reset the view alongside the digit
        // row's, and the map holds one gesture per command rather than a list
        // of aliases, so the alias stays here. NumPad1-9 never reach this:
        // the tab jumps above take them first.
        var key = e.Key == Key.NumPad0 ? Key.D0 : e.Key;
        return new KeyStroke(key.ToString(), mods);
    }

    /// Ctrl+1..9 and their numpad equivalents, as a zero-based tab index.
    private static int? TabIndexOf(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D1,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1,
        _ => null,
    };

    /// Runs a command against the selected tab. Returns false when the tab in
    /// front of the user has nothing to do with it, so the key falls through
    /// unhandled exactly as it did before the map existed.
    private bool Run(KeyCommand command)
    {
        switch (command)
        {
            case KeyCommand.OpenFiles:
                _ = OpenViaDialogAsync();
                return true;
            case KeyCommand.OpenFolder:
                _ = OpenFolderViaDialogAsync();
                return true;
            case KeyCommand.Preferences:
                _ = new PreferencesWindow(_vm).ShowDialog(this);
                return true;
            case KeyCommand.SaveImage:
                _ = SaveImageAsync();
                return true;
            case KeyCommand.CopyImage:
                _ = CopyImageAsync();
                return true;
            case KeyCommand.ExportReport:
                _ = ExportReportAsync("html");
                return true;
            case KeyCommand.Compare:
                _ = CompareViaDialogAsync();
                return true;
            case KeyCommand.ToggleSpectrum:
                _vm.ShowSpectrum = !_vm.ShowSpectrum;
                return true;
            case KeyCommand.ToggleCrosshair:
                _vm.ShowCrosshair = !_vm.ShowCrosshair;
                return true;
            case KeyCommand.NextTab:
                _vm.SelectNext(1);
                return true;
            case KeyCommand.PreviousTab:
                _vm.SelectNext(-1);
                return true;

            case KeyCommand.CloseTab when _vm.Selected is { } tab:
                _vm.CloseTab(tab);
                return true;

            case KeyCommand.ResetView:
                switch (_vm.Selected)
                {
                    case DocumentViewModel doc: doc.Viewport.Reset(); break;
                    case ComparisonViewModel cmp: cmp.Viewport.Reset(); break;
                }
                return true;

            case KeyCommand.CheckIntegrity when _vm.Selected is DocumentViewModel idoc:
                _ = idoc.ToggleIntegrityAsync();
                return true;
            case KeyCommand.MeasureLoudness when _vm.Selected is DocumentViewModel ldoc:
                _ = ldoc.ToggleLoudnessAsync();
                return true;

            case KeyCommand.NextFile or KeyCommand.PreviousFile
                when _vm.Selected is DocumentViewModel:
                _vm.StepFile(command == KeyCommand.NextFile ? 1 : -1);
                return true;

            case KeyCommand.NextChannel or KeyCommand.PreviousChannel
                when _vm.Selected is DocumentViewModel cdoc && cdoc.HasMultipleChannels:
                cdoc.SelectedChannelIndex = Math.Clamp(
                    cdoc.SelectedChannelIndex + (command == KeyCommand.NextChannel ? 1 : -1),
                    0, cdoc.ChannelOptions.Count - 1);
                return true;

            // On any other tab this keeps the plain reload it has always had:
            // the old code reached its folder branch first and let everything
            // else fall through to the F5 handler below it.
            case KeyCommand.RefreshFolder:
                if (_vm.Selected is FolderViewModel folder)
                {
                    folder.Refresh();
                    return true;
                }
                return Reload(fresh: false);

            case KeyCommand.Reload:
                return Reload(fresh: false);
            case KeyCommand.ReloadFresh:
                return Reload(fresh: true);

            case KeyCommand.CompareFlip when _vm.Selected is ComparisonViewModel fcmp:
                fcmp.FlipAB();
                return true;
            case KeyCommand.CompareDiff when _vm.Selected is ComparisonViewModel dcmp:
                dcmp.Mode = CompareMode.Diff;
                return true;
            case KeyCommand.CompareBoth when _vm.Selected is ComparisonViewModel bcmp:
                bcmp.Mode = CompareMode.Both;
                return true;
            case KeyCommand.CompareAlign when _vm.Selected is ComparisonViewModel acmp:
                _ = acmp.AlignAsync();
                return true;

            default:
                return false;
        }
    }

    /// Reload means three different things by tab type, and "fresh" only
    /// means anything to a folder, where it is the ignore-the-cache pass.
    private bool Reload(bool fresh)
    {
        switch (_vm.Selected)
        {
            case DocumentViewModel doc: _ = doc.LoadOverviewAsync(); return true;
            case ComparisonViewModel cmp: _ = cmp.LoadAsync(); return true;
            case FolderViewModel folder: folder.Analyze(fresh); return true;
            default: return false;
        }
    }

    private void SelectTab(int index)
    {
        if (index >= 0 && index < _vm.Tabs.Count) _vm.Selected = _vm.Tabs[index];
    }
}
