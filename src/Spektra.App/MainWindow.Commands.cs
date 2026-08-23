using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Spektra.Core;

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

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task OpenFolderViaDialogAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            if (await PickFolderAsync("Open a folder to audit") is { } folder)
                _vm.OpenFolder(folder);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    /// The browse tail of the compare submenu. With a folder to compare from it
    /// asks only for the other side; without one it asks for both, so the
    /// command still works before any folder is open.
    private async Task CompareFoldersViaPickerAsync(string? from)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var a = from ?? await PickFolderAsync("Choose the first folder to compare");
            if (a is null) return;
            if (await PickFolderAsync("Choose the second folder to compare") is not { } b) return;
            EnsureDupesWindow(new DiffPair(a, b));
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

    /// The patterns every picker here offers. Shared so Open and Compare
    /// cannot drift apart on which extensions they admit.
    private static IReadOnlyList<FilePickerFileType> AudioFileTypes =>
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
    ];

    /// Picked entries that are real files on this machine: a picker can hand
    /// back cloud items with no local path, and those cannot be decoded.
    private async Task<List<string>> PickAudioAsync(string title, bool multiple)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            FileTypeFilter = [.. AudioFileTypes],
        });
        return [.. files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>()];
    }

    private async Task ShowOpenDialogAsync()
    {
        var paths = await PickAudioAsync("Open audio files", multiple: true);
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

    private const string PickTwoDifferent = "Choose two different files to compare.";

    /// With two or more documents open, choosing between them beats finding
    /// them on disk again. With fewer, this used to refuse the click and write
    /// "Open at least two files to compare" to an 11px status line, while the
    /// menu item stayed enabled and kept an ellipsis that promises a dialog. It
    /// picks the pair from disk instead. Comparing files that are not open as
    /// tabs is what --compare has always done from the command line; only this
    /// entry point insisted otherwise.
    private async Task CompareViaDialogAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var docs = _vm.OpenDocuments;
            if (docs.Count >= 2) await CompareOpenDocumentsAsync(docs);
            else await CompareFromDiskAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async Task CompareOpenDocumentsAsync(IReadOnlyList<DocumentViewModel> docs)
    {
        var chooser = new CompareChooser(docs);
        await chooser.ShowDialog(this);
        if (chooser.Result is not { } c) return;
        // One file on both sides was a silent no-op, which is the same
        // complaint as above: a dialog dismissed with nothing to show for it.
        if (ReferenceEquals(c.A, c.B)) { _vm.SetErrorStatus(PickTwoDifferent); return; }
        Compare(c.A.FilePath, c.B.FilePath);
    }

    private async Task CompareFromDiskAsync()
    {
        var picked = await PickAudioAsync("Choose two files to compare", multiple: true);
        switch (ComparePick.Decide(picked))
        {
            case ComparePick.Outcome.Cancelled: return;
            case ComparePick.Outcome.Compare: Compare(picked[0], picked[1]); return;
            case ComparePick.Outcome.SameFileTwice: _vm.SetErrorStatus(PickTwoDifferent); return;
            case ComparePick.Outcome.TooMany:
                _vm.SetErrorStatus($"Compare takes two files; {picked.Count} were chosen.");
                return;
        }

        // One file: ask for its partner. Clicking a single file and pressing
        // Open is a fair reading of "choose two files", so answer the half that
        // is missing rather than rejecting the half that arrived.
        var partner = await PickAudioAsync(
            $"Compare {System.IO.Path.GetFileName(picked[0])} with…", multiple: false);
        if (partner.Count == 0) return;
        if (ComparePick.Decide([picked[0], partner[0]]) is ComparePick.Outcome.SameFileTwice)
        {
            _vm.SetErrorStatus(PickTwoDifferent);
            return;
        }
        Compare(picked[0], partner[0]);
    }

    /// OpenComparison returns null when ffmpeg has not been found, which would
    /// otherwise be one more silent no-op behind this menu item.
    private void Compare(string pathA, string pathB)
    {
        if (_vm.OpenComparison(pathA, pathB) is null)
            _vm.SetErrorStatus("Compare needs ffmpeg; use the Download ffmpeg button above.");
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

    private void OnAnalyzeFolderClicked(object? sender, RoutedEventArgs e)
    {
        // Honors the cache and never touches checkboxes, exactly like the
        // tree's "Analyze this folder" verb; AnalyzeAsync already refuses a
        // concurrent run (quietly for this tab, with a status message naming
        // the busy tab for another), so no busy-state binding is needed.
        if (_vm.Selected is FolderViewModel folder)
            folder.AnalyzeFiles(folder.FilesInScope(), fresh: false);
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
    private DuplicatesViewModel? _dupesVm;

    private void OnDuplicateDetectiveClicked(object? sender, RoutedEventArgs e)
    {
        // Adaptive launcher: scoped to the folder tab's drilldown scope when
        // one is selected, today's global window otherwise. The window keeps
        // its own root box, so an unscoped run stays reachable either way.
        if (_vm.Selected is FolderViewModel folder)
            EnsureDupesWindow(folder.EffectiveScope);
        else
            EnsureDupesWindow();
    }

    /// Opens or focuses the one Duplicate Detective window. With a root given
    /// (the --dupes launch switch) that folder becomes the only scan root and
    /// the scan starts immediately, which is what the Explorer verb promises.
    private void EnsureDupesWindow(string? root = null) =>
        ShowDupesWindow(root is null ? null : vm => vm.SetSingleRoot(root));

    /// The --diff launch switch: two roots and the diff already on.
    private void EnsureDupesWindow(DiffPair pair) =>
        ShowDupesWindow(vm => vm.SetDiffRoots(pair.FolderA, pair.FolderB));

    /// Seeds the roots and starts the scan when a launch switch named one; a
    /// null seed just opens the window on whatever roots were remembered.
    private void ShowDupesWindow(Action<DuplicatesViewModel>? seed)
    {
        if (_dupesWindow is not null)
        {
            _dupesWindow.Activate();
            if (seed is not null && _dupesVm is { } existingVm)
            {
                seed(existingVm);
                _ = existingVm.ScanAsync();
            }
            return;
        }
        if (_vm.Ffmpeg is not { } ffmpeg) return;
        var vm = new DuplicatesViewModel(ffmpeg, _vm.Settings);
        vm.OpenFileRequested += path =>
        {
            _vm.OpenFile(path);
            Activate();
        };
        vm.OpenCompareRequested += (winner, challenger) =>
        {
            _vm.OpenComparison(winner, challenger);
            Activate();
        };
        _dupesVm = vm;
        _dupesWindow = new DuplicatesWindow(vm, _vm.Settings);
        _dupesWindow.Closed += (_, _) => { _dupesWindow = null; _dupesVm = null; };
        _dupesWindow.Show(this);
        if (seed is not null)
        {
            seed(vm);
            _ = vm.ScanAsync();
        }
    }

    private FolderManifestWindow? _manifestWindow;
    private FolderManifestViewModel? _manifestVm;

    private void OnFolderManifestClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Selected is FolderViewModel folder)
            EnsureManifestWindow(folder.EffectiveScope);
        else
            EnsureManifestWindow();
    }

    /// Opens or focuses the one manifest window; with a folder given (the
    /// audit tree's "Show in manifest") it lists that folder on top of
    /// whatever the window showed.
    private void EnsureManifestWindow(string? folder = null)
    {
        if (_manifestWindow is not null)
        {
            _manifestWindow.Activate();
            if (folder is not null) _ = _manifestVm!.LoadAsync(folder);
            return;
        }
        // A fresh window auto-loads the remembered folder when it opens, so
        // an explicit target just becomes the remembered folder up front;
        // racing a second load against that would only hit the busy guard.
        if (folder is not null) _vm.Settings.FolderManifestFolder = folder;
        // No ffmpeg gate: the manifest never decodes, it only lists and reads cache.
        var manifestVm = new FolderManifestViewModel(_vm.Settings);
        manifestVm.OpenFileRequested += path =>
        {
            _vm.OpenFile(path);
            Activate();
        };
        manifestVm.AuditFolderRequested += path =>
        {
            _vm.OpenFolder(path); // dedups: an already-open folder tab is focused
            Activate();
        };
        _manifestVm = manifestVm;
        _manifestWindow = new FolderManifestWindow(manifestVm, _vm.Settings);
        _manifestWindow.Closed += (_, _) => { _manifestWindow = null; _manifestVm = null; };
        _manifestWindow.Show(this);
    }
}
