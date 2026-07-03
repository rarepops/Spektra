using System.Collections.ObjectModel;
using System.ComponentModel;
using Spektra.Core;

namespace Spektra.App;

public sealed class MainWindowViewModel : ObservableObject
{
    private FfmpegPaths? _ffmpeg;
    private DocumentViewModel? _selected;

    private string _statusText = "";
    private string? _shellErrorText;
    private bool _ffmpegMissing;

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    public string? ShellErrorText
    {
        get => _shellErrorText;
        set { if (Set(ref _shellErrorText, value)) RaisePropertyChanged(nameof(HasShellError)); }
    }

    public bool HasShellError => _shellErrorText is not null;
    public bool FfmpegMissing { get => _ffmpegMissing; set => Set(ref _ffmpegMissing, value); }
    public bool ShowHint => Documents.Count == 0;
    public AppSettings Settings { get; }

    public event Action<DocumentViewModel?>? SelectedChanged;
    public event Action? RecentFilesChanged;

    public DocumentViewModel? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected is not null)
            {
                _selected.IsSelected = false;
                _selected.PropertyChanged -= OnSelectedDocPropertyChanged;
            }
            _selected = value;
            if (value is not null)
            {
                value.IsSelected = true;
                value.PropertyChanged += OnSelectedDocPropertyChanged;
            }
            StatusText = value?.StatusText ?? "";
            RaisePropertyChanged(nameof(Selected));
            SelectedChanged?.Invoke(value);
        }
    }

    public MainWindowViewModel()
    {
        Settings = SettingsStore.Load(SettingsStore.DefaultPath);
        Documents.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(ShowHint));
        _ffmpeg = FfmpegLocator.LocateDefault(); // probes app dir, %LOCALAPPDATA%, then PATH
        if (_ffmpeg is null)
        {
            FfmpegMissing = true;
            ShellErrorText = "ffmpeg was not found. Spektra needs ffmpeg + ffprobe to decode audio.";
        }
    }

    private void OnSelectedDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == _selected && e.PropertyName == nameof(DocumentViewModel.StatusText))
            StatusText = _selected!.StatusText;
    }

    public void OpenFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths) OpenFile(path);
    }

    public void OpenFile(string path)
    {
        if (_ffmpeg is null) return;
        var doc = new DocumentViewModel(_ffmpeg, path);
        Documents.Add(doc);
        Selected = doc;
        Settings.PushRecent(path);
        SaveSettings();
        RecentFilesChanged?.Invoke();
        _ = doc.LoadOverviewAsync();
    }

    public void ClearRecent()
    {
        Settings.RecentFiles.Clear();
        SaveSettings();
        RecentFilesChanged?.Invoke();
    }

    public void SaveSettings()
    {
        try { SettingsStore.Save(SettingsStore.DefaultPath, Settings); }
        catch (Exception ex) { ShellErrorText = $"Could not save settings: {ex.Message}"; }
    }

    public void CloseDocument(DocumentViewModel doc)
    {
        var i = Documents.IndexOf(doc);
        if (i < 0) return;
        doc.Cancel();
        Documents.RemoveAt(i);
        if (Selected == doc)
            Selected = Documents.Count == 0 ? null : Documents[Math.Min(i, Documents.Count - 1)];
    }

    public void SelectNext(int direction)
    {
        if (Documents.Count == 0) return;
        var i = Selected is null
            ? 0
            : (Documents.IndexOf(Selected) + direction + Documents.Count) % Documents.Count;
        Selected = Documents[i];
    }

    public async Task DownloadFfmpegAsync()
    {
        try
        {
            var progress = new Progress<string>(s => StatusText = s);
            await FfmpegDownloader.InstallAsync(progress, CancellationToken.None);
            _ffmpeg = FfmpegLocator.LocateDefault();
            FfmpegMissing = _ffmpeg is null;
            if (!FfmpegMissing) ShellErrorText = null;
        }
        catch (Exception ex)
        {
            ShellErrorText = $"ffmpeg download failed: {ex.Message}";
        }
    }
}
