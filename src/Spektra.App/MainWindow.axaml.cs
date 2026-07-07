using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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
        _vm.DisplayChanged += ApplyDisplay;
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

        Opened += async (_, _) =>
        {
            if (_vm.CheckForUpdatesOnStartup) await _vm.CheckForUpdatesOnStartupAsync();
        };

        if (args is ["--compare", var pathA, var pathB, ..] && File.Exists(pathA) && File.Exists(pathB))
        {
            var autoAlign = args.Contains("--auto");
            var mi = Array.IndexOf(args, "--mode");
            var startMode = mi >= 0 && mi + 1 < args.Length
                ? args[mi + 1].ToLowerInvariant() switch
                {
                    "a" => (CompareMode?)CompareMode.A,
                    "b" => CompareMode.B,
                    "diff" => CompareMode.Diff,
                    "both" => CompareMode.Both,
                    _ => null,
                }
                : null;
            Opened += async (_, _) =>
            {
                _vm.OpenComparison(pathA, pathB);
                if (_vm.Selected is not ComparisonViewModel cmp) return;
                await Task.Delay(1500); // let metadata/overviews load
                if (autoAlign) await cmp.AlignAsync();
                if (startMode is { } m) cmp.Mode = m;
            };
        }
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

        ApplyDisplay();

        Title = tab is null ? "Spektra" : $"{tab.TabTitle} — Spektra";
    }

    private void ApplyDisplay()
    {
        var display = _vm.Settings.ToDisplaySettings();
        Spectro.SetDisplay(display);
        CompareSurfaceCtl.SetDisplay(display);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>()
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
            case Key.E:
                _ = new PreferencesWindow(_vm).ShowDialog(this);
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
                _ = doc.RunIntegrityCheckAsync();
                e.Handled = true;
                break;
            case Key.L when _vm.Selected is DocumentViewModel ldoc:
                _ = ldoc.RunLoudnessCheckAsync();
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

    private Control? VisibleSurface() => _vm.Selected switch
    {
        DocumentViewModel => Spectro,
        ComparisonViewModel => CompareSurfaceCtl,
        _ => null,
    };

    /// Renders the on-screen spectrogram surface to a bitmap at the current DPI.
    private RenderTargetBitmap? RenderSurface()
    {
        if (VisibleSurface() is not { } surface) return null;
        var size = surface.Bounds.Size;
        if (size.Width < 2 || size.Height < 2) return null;
        var scale = RenderScaling;
        var px = new PixelSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale)));
        var rtb = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
        rtb.Render(surface);
        return rtb;
    }

    private async void OnSaveImageClicked(object? sender, RoutedEventArgs e) => await SaveImageAsync();

    private async Task SaveImageAsync()
    {
        using var rtb = RenderSurface();
        if (rtb is null) { _vm.StatusText = "Open a file first to save its spectrogram."; return; }
        var suggested = SanitizeFileName(_vm.Selected?.TabTitle ?? "spektra") + ".png";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save spectrogram image",
            SuggestedFileName = suggested,
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        rtb.Save(stream);
        _vm.StatusText = $"Saved {file.Name}";
    }

    private async void OnCopyImageClicked(object? sender, RoutedEventArgs e) => await CopyImageAsync();

    private async Task CopyImageAsync()
    {
        using var rtb = RenderSurface();
        if (rtb is null) { _vm.StatusText = "Open a file first to copy its spectrogram."; return; }
        if (Clipboard is null) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Bitmap, (Bitmap)rtb));
        await Clipboard.SetDataAsync(data);
        _vm.StatusText = "Copied spectrogram to clipboard.";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private async void OnExportReportClicked(object? sender, RoutedEventArgs e) => await ExportReportAsync();

    /// Exports the active document's bandwidth + integrity audit as one CSV or
    /// JSON row. Reuses the verdict already computed on load; runs the
    /// integrity check first if it hasn't been run yet.
    private async Task ExportReportAsync()
    {
        if (_vm.Selected is not DocumentViewModel doc || doc.Metadata is null)
        {
            _vm.StatusText = "Open a file first to export its report.";
            return;
        }
        if (doc.Verdict is null)
        {
            _vm.StatusText = "Wait for the analysis to finish before exporting.";
            return;
        }
        if (doc.Integrity is null) await doc.RunIntegrityCheckAsync();

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export report",
            SuggestedFileName = SanitizeFileName(Path.GetFileNameWithoutExtension(doc.TabTitle)) + "-report.csv",
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV report") { Patterns = ["*.csv"] },
                new FilePickerFileType("JSON report") { Patterns = ["*.json"] },
            ],
        });
        if (file is null) return;

        var report = new FileReport(doc.FilePath, doc.Metadata, doc.Verdict, null);
        await WriteReportAsync(file, [Reporting.ToAuditRow(report, doc.Integrity, null)]);
        _vm.StatusText = $"Exported {file.Name}";
    }

    private async void OnExportFolderReportClicked(object? sender, RoutedEventArgs e) =>
        await ExportFolderReportAsync();

    private async Task ExportFolderReportAsync()
    {
        if (_vm.Ffmpeg is not { } ffmpeg)
        {
            _vm.StatusText = "ffmpeg is required to analyze a folder.";
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to audit",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder) return;

        var files = BandwidthReport.FindAudioFiles(folder).ToList();
        if (files.Count == 0)
        {
            _vm.StatusText = "No audio files found in that folder.";
            return;
        }

        var dialog = new ExportProgressDialog(ffmpeg, files);
        await dialog.ShowDialog(this);
        if (dialog.Results is not { } results) return; // cancelled: write nothing

        var suggested = SanitizeFileName(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))) + "-report.csv";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export folder report",
            SuggestedFileName = suggested,
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV report") { Patterns = ["*.csv"] },
                new FilePickerFileType("JSON report") { Patterns = ["*.json"] },
            ],
        });
        if (file is null) return;
        await WriteReportAsync(file, results.Select(r => r.ToRow()).ToList());
        _vm.StatusText = $"Exported {results.Length} file(s) to {file.Name}";
    }

    /// CSV or JSON is chosen by the saved file's extension; anything that is
    /// not .json (including no extension) writes CSV, matching the default.
    private static async Task WriteReportAsync(IStorageFile file, IReadOnlyList<AuditRow> rows)
    {
        var json = Path.GetExtension(file.Name).Equals(".json", StringComparison.OrdinalIgnoreCase);
        var text = json ? Reporting.ToJson(rows) : Reporting.ToCsv(rows);
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
    }

    private async void OnPreferencesClicked(object? sender, RoutedEventArgs e) =>
        await new PreferencesWindow(_vm).ShowDialog(this);

    private void OnCheckIntegrityClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Selected is DocumentViewModel doc) _ = doc.RunIntegrityCheckAsync();
    }

    private void OnMeasureLoudnessClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.Selected is DocumentViewModel doc) _ = doc.RunLoudnessCheckAsync();
    }

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
}
