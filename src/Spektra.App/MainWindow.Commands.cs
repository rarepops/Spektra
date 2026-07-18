using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace Spektra.App;

// Menu and toolbar command handlers: open/compare dialogs, the analyze actions,
// preferences, and the update banner. Image and report export live in
// MainWindow.Export.cs; keyboard shortcuts route here from MainWindow.Input.cs.
public partial class MainWindow
{
    private bool _dialogOpen;

    private async void OnOpenClicked(object? sender, RoutedEventArgs e) =>
        await OpenViaDialogAsync();

    private async void OnOpenFolderClicked(object? sender, RoutedEventArgs e) =>
        await OpenFolderViaDialogAsync();

    private async Task OpenFolderViaDialogAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Open a folder to audit",
                AllowMultiple = false,
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } folder)
                _vm.OpenFolder(folder);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

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

    // The header's context menu: the header shows the bare name (the tooltip
    // has the full path), so the path can be taken elsewhere from here.
    private async void OnCopyPathClicked(object? sender, RoutedEventArgs e)
    {
        if (DocHost.DataContext is not DocumentViewModel doc || Clipboard is null) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Text, doc.FilePath));
        await Clipboard.SetDataAsync(data);
        doc.StatusText = "Path copied to clipboard";
    }

    private async void OnCompareClicked(object? sender, RoutedEventArgs e) => await CompareViaDialogAsync();

    private async Task CompareViaDialogAsync()
    {
        var docs = _vm.OpenDocuments;
        if (docs.Count < 2)
        {
            _vm.SetErrorStatus("Open at least two files to compare.");
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

    private async void OnNullTestClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Selected is ComparisonViewModel cmp) await cmp.NullTestAsync();
    }

    private void OnNudgeOffsetLeft(object? sender, RoutedEventArgs e)
    {
        if (_vm.Selected is ComparisonViewModel cmp) cmp.OffsetMs--;
    }

    private void OnNudgeOffsetRight(object? sender, RoutedEventArgs e)
    {
        if (_vm.Selected is ComparisonViewModel cmp) cmp.OffsetMs++;
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    private async void OnPreferencesClicked(object? sender, RoutedEventArgs e) =>
        await new PreferencesWindow(_vm).ShowDialog(this);

    private void OnCheckIntegrityClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Selected is DocumentViewModel doc) _ = doc.ToggleIntegrityAsync();
    }

    private void OnMeasureLoudnessClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Selected is DocumentViewModel doc) _ = doc.ToggleLoudnessAsync();
    }

    private async void OnControlsClicked(object? sender, RoutedEventArgs e) =>
        await new ControlsWindow().ShowDialog(this);

    private async void OnAboutClicked(object? sender, RoutedEventArgs e) =>
        await new AboutWindow().ShowDialog(this);

    private async void OnCheckUpdatesClicked(object? sender, RoutedEventArgs e)
    {
        var result = await _vm.CheckForUpdatesAsync();
        ForceRedraw(); // status text just cleared, repaint so no glyph pixels linger in the bottom strip
        await new UpdateDialog(result, _vm.CurrentVersionText).ShowDialog(this);
    }

    // Clearing a docked status message collapses its row, and the newly exposed
    // strip can keep a few stale glyph pixels until something paints over it.
    private void ForceRedraw()
    {
        static void Invalidate(Visual visual)
        {
            visual.InvalidateVisual();
            foreach (var child in visual.GetVisualChildren())
                Invalidate(child);
        }
        Invalidate(this);
    }

    private async void OnViewReleaseClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Update is { Url: { Length: > 0 } url } && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Launcher.LaunchUriAsync(uri);
    }

    private void OnDismissUpdateClicked(object? sender, RoutedEventArgs e) => _vm.DismissUpdate();

    private DuplicatesWindow? _dupesWindow;

    private void OnDuplicateDetectiveClicked(object? sender, RoutedEventArgs e)
    {
        if (_dupesWindow is not null)
        {
            _dupesWindow.Activate();
            return;
        }
        if (_vm.Ffmpeg is not { } ffmpeg) return;
        var vm = new DuplicatesViewModel(ffmpeg, _vm.Settings);
        vm.OpenFileRequested += path =>
        {
            _vm.OpenFile(path);
            Activate();
        };
        _dupesWindow = new DuplicatesWindow(vm, _vm.Settings);
        _dupesWindow.Closed += (_, _) => _dupesWindow = null;
        _dupesWindow.Show(this);
    }

    private FolderManifestWindow? _manifestWindow;

    private void OnFolderManifestClicked(object? sender, RoutedEventArgs e)
    {
        if (_manifestWindow is not null)
        {
            _manifestWindow.Activate();
            return;
        }
        // No ffmpeg gate: the manifest never decodes, it only lists and reads cache.
        _manifestWindow = new FolderManifestWindow(new FolderManifestViewModel(_vm.Settings), _vm.Settings);
        _manifestWindow.Closed += (_, _) => _manifestWindow = null;
        _manifestWindow.Show(this);
    }
}
