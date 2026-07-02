using Avalonia.Threading;
using Spektra.Core;

namespace Spektra.App;

public sealed class MainWindowViewModel : ObservableObject
{
    private FfmpegPaths? _ffmpeg;
    private CancellationTokenSource? _cts;

    private string _headerText = "";
    private string _statusText = "";
    private string? _errorText;
    private bool _showHint = true;
    private bool _ffmpegMissing;

    public string HeaderText { get => _headerText; set => Set(ref _headerText, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    public string? ErrorText
    {
        get => _errorText;
        set { if (Set(ref _errorText, value)) RaisePropertyChanged(nameof(HasError)); }
    }

    public bool HasError => _errorText is not null;
    public bool ShowHint { get => _showHint; set => Set(ref _showHint, value); }
    public bool FfmpegMissing { get => _ffmpegMissing; set => Set(ref _ffmpegMissing, value); }

    public event Action<SpectrogramDocument?>? DocumentChanged;
    public event Action? DocumentUpdated;

    public MainWindowViewModel()
    {
        _ffmpeg = FfmpegLocator.LocateDefault(); // probes app dir, %LOCALAPPDATA%, then PATH
        if (_ffmpeg is null)
        {
            FfmpegMissing = true;
            ErrorText = "ffmpeg was not found. Spektra needs ffmpeg + ffprobe to decode audio.";
        }
    }

    public async Task LoadFileAsync(string path)
    {
        if (_ffmpeg is null) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        ErrorText = null;
        ShowHint = false;
        HeaderText = Path.GetFileName(path);
        StatusText = "Reading metadata…";

        try
        {
            var session = new AnalysisSession(_ffmpeg);
            var settings = new SpectrogramSettings();
            var meta = await Task.Run(() => session.ReadMetadata(path), cts.Token);
            HeaderText = meta.ToDisplayLine(Path.GetFileName(path));

            var estSamples = (long)(meta.Duration.TotalSeconds * meta.SampleRate);
            var estWindows = Math.Max(1, (estSamples - settings.WindowSize) / settings.Hop + 1);
            var estColumns = (int)Math.Min(estWindows, settings.MaxColumns);

            var doc = new SpectrogramDocument(meta, settings, estColumns);
            DocumentChanged?.Invoke(doc);

            StatusText = "Analyzing…";
            var started = DateTime.UtcNow;
            await Task.Run(() =>
            {
                var sinceRefresh = 0;
                foreach (var column in session.AnalyzeColumns(path, meta, settings, cts.Token))
                {
                    doc.Append(column);
                    if (++sinceRefresh >= 32)
                    {
                        sinceRefresh = 0;
                        Dispatcher.UIThread.Post(() => DocumentUpdated?.Invoke());
                    }
                }
            }, cts.Token);

            DocumentUpdated?.Invoke();
            StatusText = $"Done in {(DateTime.UtcNow - started).TotalSeconds:0.0}s · {doc.Count} columns";
        }
        catch (OperationCanceledException) { /* superseded by a newer load */ }
        catch (AudioDecodeException ex)
        {
            if (cts.Token.IsCancellationRequested) return;
            DocumentChanged?.Invoke(null);
            ErrorText = ex.StderrTail is null ? ex.Message : $"{ex.Message}\n{ex.StderrTail}";
            StatusText = "";
            ShowHint = true;
        }
    }

    public async Task DownloadFfmpegAsync()
    {
        try
        {
            var progress = new Progress<string>(s => StatusText = s);
            await FfmpegDownloader.InstallAsync(progress, CancellationToken.None);
            _ffmpeg = FfmpegLocator.LocateDefault();
            FfmpegMissing = _ffmpeg is null;
            if (!FfmpegMissing) ErrorText = null;
        }
        catch (Exception ex)
        {
            ErrorText = $"ffmpeg download failed: {ex.Message}";
        }
    }
}
