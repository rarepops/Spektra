using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spektra.Core;

namespace Spektra.App;

/// The Folder Manifest window: modeless, one instance (MainWindow manages that),
/// view-only forever. Placement and the last folder persist via AppSettings.
public partial class FolderManifestWindow : Window
{
    private readonly FolderManifestViewModel _vm;
    private readonly AppSettings _settings;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    // Parameterless constructor for the XAML previewer/loader only (AVLN3001),
    // same pattern as DuplicatesWindow.
    public FolderManifestWindow() : this(new FolderManifestViewModel(new AppSettings()), new AppSettings()) { }

    public FolderManifestWindow(FolderManifestViewModel vm, AppSettings settings)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        DataContext = vm;
        if (settings.FolderManifestWindow is { } p)
        {
            // Same safeguarded restore as DuplicatesWindow: min-clamped rect,
            // only applied while it still intersects a screen.
            var target = new PixelRect(p.X, p.Y, Math.Max(400, p.Width), Math.Max(300, p.Height));
            if (Screens.All.Any(s => s.Bounds.Intersects(target)))
            {
                Position = new PixelPoint(p.X, p.Y);
                Width = Math.Max(400, p.Width);
                Height = Math.Max(300, p.Height);
                if (p.Maximized) WindowState = WindowState.Maximized;
                _normalPosition = Position;
                _normalSize = new Size(Width, Height);
            }
        }
        AddHandler(DragDrop.DropEvent, OnDrop);
        PositionChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal) _normalPosition = e.Point;
        };
        SizeChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal) _normalSize = e.NewSize;
        };
        Closing += (_, _) =>
        {
            var pos = WindowState == WindowState.Normal ? Position : _normalPosition;
            var size = WindowState == WindowState.Normal ? ClientSize : _normalSize;
            if (size.Width >= 100 && size.Height >= 100)
                _settings.FolderManifestWindow = new WindowPlacement(
                    pos.X, pos.Y, (int)size.Width, (int)size.Height, WindowState == WindowState.Maximized);
            // The last folder is written by the view model on load; the window
            // owns flushing everything to disk on close.
            if (!SettingsStore.Save(SettingsStore.DefaultPath, _settings))
                _vm.SetError("Could not save the Folder Manifest settings.");
        };
        Opened += (_, _) =>
        {
            if (settings.FolderManifestFolder is { } last && Directory.Exists(last))
                _ = _vm.LoadAsync(last);
        };
    }

    private async void OnPickFolderClicked(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick a folder",
            AllowMultiple = false,
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
            _ = _vm.LoadAsync(path);
    }

    private void OnManifestPathKeyDown(object? sender, KeyEventArgs e)
    {
        // Esc abandons an edit and puts the real folder back, the usual
        // address-bar escape hatch.
        if (e.Key == Key.Escape)
        {
            ManifestPathBox.Text = _vm.Folder ?? "";
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        // Emptying the box and committing it means "list nothing", so the tree
        // goes away too. Otherwise: a successful load raises Folder and the
        // OneWay binding rewrites the box with the canonical path; a failed one
        // leaves the typed text in place to be corrected, reason in the footer.
        if ((ManifestPathBox.Text ?? "").Trim().Length == 0) _vm.Clear();
        else _vm.TryLoadTyped(ManifestPathBox.Text);
        e.Handled = true;
    }

    // Header drag grips: each fact lane resizes from its left edge, and the
    // Name column (the star) absorbs the difference. Dragging right shrinks
    // the lane, so the boundary follows the cursor; the view model clamps
    // and remembers the widths.
    private void OnKindLaneDrag(object? sender, VectorEventArgs e) =>
        _vm.KindLaneWidth -= e.Vector.X;

    private void OnSizeLaneDrag(object? sender, VectorEventArgs e) =>
        _vm.SizeLaneWidth -= e.Vector.X;

    // Same shape as DuplicatesWindow's OnDrop, first folder only: one folder
    // at a time is this window's model.
    private void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles()?.ToList() ?? [];
        var folder = items.OfType<IStorageFolder>()
            .Select(f => f.TryGetLocalPath())
            .FirstOrDefault(p => p is not null);
        if (folder is not null) _ = _vm.LoadAsync(folder);
        else if (items.Count > 0)
            _vm.SetError("Drop a folder to list it; single files are ignored.");
    }

    // Open the format flyout on hover, so the three options are one move away
    // with no click. ShowAt just re-anchors if it is already open.
    private async void OnMenuCopyPathClicked(object? sender, RoutedEventArgs e) =>
        await FileActions.CopyPathAsync(this, FileActions.ItemFrom(sender));

    private void OnMenuRevealClicked(object? sender, RoutedEventArgs e) =>
        FileActions.Reveal(FileActions.ItemFrom(sender));

    private void OnMenuOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (FileActions.ItemFrom(sender) is { } item) _vm.RequestOpen(item);
    }

    private void OnExportHover(object? sender, PointerEventArgs e)
    {
        if ((sender as Button)?.Flyout is { } flyout) flyout.ShowAt((Control)sender!);
    }

    // One handler per format, chosen from the Export dropdown (the Tag carries
    // the extension). The suggested name is the folder's path made
    // filename-legal (D:\Music\Zotify becomes D-Music-Zotify.html) plus the
    // active filter's kinds, so exports of different folders and filters do not
    // collide; only the chosen type is offered in the save dialog.
    private async void OnExportFormatClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.LastRoot is not { } root) return;
        if ((sender as MenuItem)?.Tag is not string fmt) return;
        var baseName = string.Join("-",
                root.Path.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            + (_vm.ActiveKinds.Count == 0 ? "" : "-" + string.Join("-", _vm.ActiveKinds));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export folder manifest",
            SuggestedFileName = $"{baseName}.{fmt}",
            DefaultExtension = fmt,
            FileTypeChoices = [new FilePickerFileType($"{fmt.ToUpperInvariant()} report") { Patterns = [$"*.{fmt}"] }],
        });
        if (file is null) return;
        try
        {
            var text = fmt switch
            {
                "csv" => Reporting.ToCsv(FolderManifest.ToRows(root)),
                "json" => Reporting.ToJson(FolderManifest.ToRows(root)),
                _ => HtmlReport.ManifestDocument(root, "Spektra Folder Manifest"),
            };
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // async void handler: an unguarded write failure would crash the
            // app instead of reporting (same guard as DuplicatesWindow).
            _vm.SetError($"Could not export the report: {ex.Message}");
        }
    }
}
