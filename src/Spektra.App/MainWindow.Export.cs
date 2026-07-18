using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Spektra.Core;

namespace Spektra.App;

// Rendering the active surface to a PNG (save / clipboard) and exporting the
// single-file and folder audit reports.
public partial class MainWindow
{
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
        if (rtb is null) { _vm.SetErrorStatus("Open a file first to save its spectrogram."); return; }
        var suggested = SanitizeFileName(_vm.Selected?.TabTitle ?? "spektra") + ".png";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save spectrogram image",
            SuggestedFileName = suggested,
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            rtb.Save(stream, PngBitmapEncoderOptions.Default);
            _vm.StatusText = $"Saved {file.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.SetErrorStatus($"Could not save the image: {ex.Message}");
        }
    }

    private async void OnCopyImageClicked(object? sender, RoutedEventArgs e) => await CopyImageAsync();

    private async Task CopyImageAsync()
    {
        using var rtb = RenderSurface();
        if (rtb is null) { _vm.SetErrorStatus("Open a file first to copy its spectrogram."); return; }
        if (Clipboard is null) return;
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DataFormat.Bitmap, (Bitmap)rtb));
            await Clipboard.SetDataAsync(data);
            _vm.StatusText = "Copied spectrogram to clipboard.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            _vm.SetErrorStatus($"Could not copy the image: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private async void OnExportReportClicked(object? sender, RoutedEventArgs e) => await ExportReportAsync();

    /// Exports the active document's bandwidth + integrity audit as one CSV,
    /// JSON, or HTML row. Reuses the verdict already computed on load; runs the
    /// integrity check first if it hasn't been run yet.
    private async Task ExportReportAsync()
    {
        if (_vm.Selected is not DocumentViewModel doc || doc.Metadata is null)
        {
            _vm.SetErrorStatus("Open a file first to export its report.");
            return;
        }
        if (doc.Verdict is null)
        {
            _vm.SetErrorStatus("Wait for the analysis to finish before exporting.");
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
                new FilePickerFileType("HTML report") { Patterns = ["*.html"] },
            ],
        });
        if (file is null) return;

        var report = new FileReport(doc.FilePath, doc.Metadata, doc.Verdict, null);
        try
        {
            await ReportWriter.WriteAsync(file, [Reporting.ToAuditRow(report, doc.Integrity, null)]);
            _vm.StatusText = $"Exported {file.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.SetErrorStatus($"Could not export the report: {ex.Message}");
        }
    }

    private async void OnExportFolderReportClicked(object? sender, RoutedEventArgs e) =>
        await ExportFolderReportAsync();

    private async Task ExportFolderReportAsync()
    {
        if (_vm.Ffmpeg is not { } ffmpeg)
        {
            _vm.SetErrorStatus("ffmpeg is required to analyze a folder.");
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to audit",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder) return;

        var targets = FolderAudit.CollectTargets(folder);
        if (targets.Length == 0)
        {
            _vm.SetErrorStatus("No audio files found in that folder.");
            return;
        }

        var dialog = new ExportProgressDialog(ffmpeg, targets, folder);
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
                new FilePickerFileType("HTML report") { Patterns = ["*.html"] },
            ],
        });
        if (file is null) return;
        try
        {
            await ReportWriter.WriteAsync(file, results
                .Select(r => r.Row with { File = Reporting.RelativeFile(folder, r.Target.Path) })
                .ToList());
            _vm.StatusText = $"Exported {results.Length} file(s) to {file.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.SetErrorStatus($"Could not export the report: {ex.Message}");
        }
    }
}
