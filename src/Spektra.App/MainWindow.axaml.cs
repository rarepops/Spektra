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
        _vm.DocumentChanged += doc => Spectro.SetDocument(doc);
        _vm.DocumentUpdated += () => Spectro.RefreshFromDocument();

        AddHandler(DragDrop.DropEvent, OnDrop);

        var file = args.FirstOrDefault(File.Exists);
        if (file is not null)
            Opened += (_, _) => _ = _vm.LoadFileAsync(file);
    }

    public MainWindow() : this([]) { }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.Data.GetFiles()?.OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (path is not null) _ = _vm.LoadFileAsync(path);
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open audio file",
            AllowMultiple = false,
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
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) await _vm.LoadFileAsync(path);
    }

    private async void OnDownloadFfmpegClicked(object? sender, RoutedEventArgs e) =>
        await _vm.DownloadFfmpegAsync();

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();
}
