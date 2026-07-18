using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spektra.Core;

namespace Spektra.App;

/// The Dedup Destroyer window: modeless, one instance (MainWindow manages
/// that), view and export only. Placement persists via AppSettings.
public partial class DuplicatesWindow : Window
{
    private readonly DuplicatesViewModel _vm;
    private readonly AppSettings _settings;

    // Parameterless constructor for the XAML previewer/loader only (Avalonia's
    // AVLN3001 wants one); real callers always use the (vm, settings) overload,
    // same pattern as CompareChooser's default-argument chain.
    public DuplicatesWindow() : this(new DuplicatesViewModel(new FfmpegPaths("", ""), new AppSettings()), new AppSettings()) { }

    public DuplicatesWindow(DuplicatesViewModel vm, AppSettings settings)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        DataContext = vm;
        if (settings.DuplicatesWindow is { } p)
        {
            Position = new Avalonia.PixelPoint(p.X, p.Y);
            Width = p.Width;
            Height = p.Height;
            if (p.Maximized) WindowState = WindowState.Maximized;
        }
        Closing += (_, _) =>
        {
            _settings.DuplicatesWindow = new WindowPlacement(
                Position.X, Position.Y, (int)Width, (int)Height, WindowState == WindowState.Maximized);
            // Roots and placement are this window's to persist; Task 6's view
            // model deliberately never saves (view + export only).
            try { SettingsStore.Save(SettingsStore.DefaultPath, _settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _vm.SetError($"Could not save settings: {ex.Message}");
            }
        };
    }

    private async void OnAddFolderClicked(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a folder to scan",
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

    private void OnScanClicked(object? sender, RoutedEventArgs e) => _ = _vm.ScanAsync();

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => _vm.Cancel();

    private void OnMemberDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DupeMemberItem member)
            _vm.RequestOpen(member);
    }

    private async void OnCopyMemberPathClicked(object? sender, RoutedEventArgs e)
    {
        if (MemberFrom(sender) is not { } member || Clipboard is null) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Text, member.Path));
        await Clipboard.SetDataAsync(data);
    }

    private void OnRevealMemberClicked(object? sender, RoutedEventArgs e)
    {
        if (MemberFrom(sender) is not { } member || !OperatingSystem.IsWindows()) return;
        Process.Start("explorer.exe", $"/select,\"{member.Path}\"");
    }

    private static DupeMemberItem? MemberFrom(object? sender) =>
        (sender as MenuItem)?.DataContext as DupeMemberItem;

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.LastResult is not { } result) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export duplicate report",
            SuggestedFileName = "dedup-destroyer-report.html",
            DefaultExtension = "html",
            FileTypeChoices =
            [
                new FilePickerFileType("HTML report") { Patterns = ["*.html"] },
                new FilePickerFileType("CSV report") { Patterns = ["*.csv"] },
                new FilePickerFileType("JSON report") { Patterns = ["*.json"] },
            ],
        });
        if (file is null) return;
        try
        {
            var ext = Path.GetExtension(file.Name);
            var text = ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                ? HtmlReport.DupesDocument(result, "Spektra Dedup Destroyer")
                : ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
                    ? Reporting.ToJson(DuplicateScan.ToRows(result))
                    : Reporting.ToCsv(DuplicateScan.ToRows(result));
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // async void handler: an unguarded write failure would crash the
            // app instead of reporting (same guard as the folder view export).
            _vm.SetError($"Could not export the report: {ex.Message}");
        }
    }
}
