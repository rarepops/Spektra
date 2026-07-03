using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Spektra.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm = new();

    public MainWindow(string[] args)
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.SelectedChanged += OnSelectedDocumentChanged;
        OnSelectedDocumentChanged(null);

        AddHandler(DragDrop.DropEvent, OnDrop);

        var files = args.Where(File.Exists).ToList();
        if (files.Count > 0)
            Opened += (_, _) => _vm.OpenFiles(files);
    }

    public MainWindow() : this([]) { }

    private void OnSelectedDocumentChanged(DocumentViewModel? doc)
    {
        DocHost.DataContext = doc;
        DocHost.IsVisible = doc is not null;
        Spectro.Attach(doc);
        Title = doc is null ? "Spektra" : $"{doc.TabTitle} — Spektra";
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.Data.GetFiles()?.OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();
        if (paths is { Count: > 0 }) _vm.OpenFiles(paths);
    }

    private bool _dialogOpen;

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Border)?.DataContext is not DocumentViewModel doc) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed)
        {
            _vm.CloseDocument(doc);
            e.Handled = true;
        }
        else if (props.IsLeftButtonPressed)
        {
            _vm.Selected = doc;
            e.Handled = true;
        }
    }

    private void OnTabCloseClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is DocumentViewModel doc)
            _vm.CloseDocument(doc);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        switch (e.Key)
        {
            case Key.O:
                _ = OpenViaDialogAsync();
                e.Handled = true;
                break;
            case Key.W when _vm.Selected is { } doc:
                _vm.CloseDocument(doc);
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

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();
}
