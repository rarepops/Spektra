using Avalonia.Threading;
using Spektra.Core;

namespace Spektra.App;

public sealed class DocumentViewModel : ObservableObject, ITab
{
    private readonly FfmpegPaths _ffmpeg;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _tileCts;

    private string _headerText;
    private string _statusText = "";
    private string? _errorText;
    private bool _isSelected;
    private int _selectedChannelIndex;
    private int _windowSize = 2048;
    private List<string> _channelOptions = ["Mix"];

    public string FilePath { get; }
    public string TabTitle => Path.GetFileName(FilePath);

    public string HeaderText { get => _headerText; private set => Set(ref _headerText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public string? ErrorText
    {
        get => _errorText;
        private set { if (Set(ref _errorText, value)) RaisePropertyChanged(nameof(HasError)); }
    }

    public bool HasError => _errorText is not null;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public List<string> ChannelOptions
    {
        get => _channelOptions;
        private set { if (Set(ref _channelOptions, value)) RaisePropertyChanged(nameof(HasMultipleChannels)); }
    }

    public bool HasMultipleChannels => _channelOptions.Count > 1;

    public int SelectedChannelIndex
    {
        get => _selectedChannelIndex;
        set
        {
            if (value < 0) return; // ComboBox emits -1 transiently on ItemsSource swap
            if (!Set(ref _selectedChannelIndex, value)) return;
            if (Metadata is not null) _ = LoadOverviewAsync();
        }
    }

    public int? SelectedChannel => _selectedChannelIndex == 0 ? null : _selectedChannelIndex - 1;

    /// FFT window size (frequency buckets = WindowSize / 2 + 1). Changing it while
    /// a file is loaded re-analyzes at the new resolution.
    public int WindowSize
    {
        get => _windowSize;
        set { if (Set(ref _windowSize, value) && Metadata is not null) _ = LoadOverviewAsync(); }
    }

    public AudioMetadata? Metadata { get; private set; }
    public SpectrogramDocument? Document { get; private set; }
    public Viewport Viewport { get; }
    public TimeMap? TimeMap { get; set; }
    public SpectrogramTile? Tile { get; private set; }

    public event Action? DocumentChanged;
    public event Action? DocumentUpdated;
    public event Action? TileChanged;

    public DocumentViewModel(FfmpegPaths ffmpeg, string filePath, Viewport? viewport = null)
    {
        _ffmpeg = ffmpeg;
        FilePath = filePath;
        _headerText = Path.GetFileName(filePath);
        Viewport = viewport ?? new Viewport();
    }

    public async Task LoadOverviewAsync()
    {
        _loadCts?.Cancel();
        _tileCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();

        ErrorText = null;
        Tile = null;
        StatusText = "Reading metadata…";

        try
        {
            var session = new AnalysisSession(_ffmpeg);
            var settings = new SpectrogramSettings(WindowSize: _windowSize);

            var meta = await Task.Run(() => session.ReadMetadata(FilePath), cts.Token);
            Metadata = meta;
            if (ChannelOptions.Count == 1 && meta.Channels > 1)
                ChannelOptions = ["Mix", .. Enumerable.Range(1, meta.Channels).Select(i => $"Ch {i}")];
            HeaderText = meta.ToDisplayLine(TabTitle);

            // max-zoom clamp: a nominal 1024-column tile never drops below 64 samples/hop
            var totalSamples = meta.Duration.TotalSeconds * meta.SampleRate;
            Viewport.MinTimeSpan = totalSamples > 0 ? Math.Min(1, 64.0 * 1024 / totalSamples) : 1;

            var estSamples = (long)totalSamples;
            var estWindows = Math.Max(1, (estSamples - settings.WindowSize) / settings.Hop + 1);
            var estColumns = (int)Math.Min(estWindows, settings.MaxColumns);

            var doc = new SpectrogramDocument(meta, settings, estColumns);
            Document = doc;
            DocumentChanged?.Invoke();

            StatusText = "Analyzing…";
            var started = DateTime.UtcNow;
            await Task.Run(() =>
            {
                var sinceRefresh = 0;
                foreach (var column in session.AnalyzeColumns(FilePath, meta, settings, cts.Token,
                    new DecodeOptions(Channel: SelectedChannel)))
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
        catch (OperationCanceledException) { /* superseded or closed */ }
        catch (AudioDecodeException ex)
        {
            if (cts.Token.IsCancellationRequested) return;
            Document = null;
            DocumentChanged?.Invoke();
            ErrorText = ex.StderrTail is null ? ex.Message : $"{ex.Message}\n{ex.StderrTail}";
            StatusText = "";
        }
    }

    public async Task RenderTileAsync(int targetColumns)
    {
        if (Metadata is not { } meta || Document is null || !Viewport.IsTimeZoomed) return;
        _tileCts?.Cancel();
        var cts = _tileCts = new CancellationTokenSource();
        var (t0, t1) = (Viewport.T0, Viewport.T1);

        var map = TimeMap ?? new TimeMap(meta.Duration.TotalSeconds);
        var startSec = Math.Max(0, map.StartSeconds(t0));
        var spanSec = map.DurationSeconds(t1 - t0);
        var spanSamples = (long)(spanSec * meta.SampleRate);
        if (spanSamples <= 0 || targetColumns <= 0) return;

        var hop = (int)Math.Clamp(spanSamples / targetColumns, 64, 1024);
        var settings = new SpectrogramSettings(WindowSize: _windowSize, MaxColumns: targetColumns, HopOverride: hop);
        var options = new DecodeOptions(
            Start: TimeSpan.FromSeconds(startSec),
            Duration: TimeSpan.FromSeconds(spanSec),
            Channel: SelectedChannel);

        try
        {
            var session = new AnalysisSession(_ffmpeg);
            var columns = await Task.Run(
                () => session.AnalyzeColumns(FilePath, meta, settings, cts.Token, options).ToList(),
                cts.Token);
            if (cts.Token.IsCancellationRequested || columns.Count == 0) return;
            Tile = new SpectrogramTile(t0, t1, columns[0].Length, columns);
            TileChanged?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException) { /* tile is best-effort; overview stays */ }
    }

    public void Cancel()
    {
        _loadCts?.Cancel();
        _tileCts?.Cancel();
    }
}
