using Spektra.Core;

namespace Spektra.App;

public sealed partial class DocumentViewModel : TabViewModelBase
{
    /// Mix + both channels of a stereo file fit; surround files stay bounded
    /// and fall back to lazy recompute past this.
    private const int ChannelCacheCapacity = 3;

    private readonly FfmpegPaths _ffmpeg;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _tileCts;

    // Finished overviews per combo index (0 = Mix, i = channel i-1), UI thread only.
    private readonly LruCache<int, ChannelOverview> _channelCache = new(ChannelCacheCapacity);
    private CancellationTokenSource? _computeCts;
    private Task? _computeLoop;
    private SpectrogramDocument? _computingDoc;
    private int _computingIndex = -1;

    private string _headerText;
    private string? _errorText;
    private int _selectedChannelIndex;
    private int _windowSize = 2048;
    private WindowFunctionKind _window = WindowFunctionKind.Hann;
    private List<string> _channelOptions = ["Mix"];

    public string FilePath { get; }
    public override string TabTitle => Path.GetFileName(FilePath);

    /// Background-compute the remaining channel variants after the visible
    /// overview finishes. Comparison documents turn this off: they never
    /// expose the channel selector.
    public bool PrefetchChannels { get; init; } = true;

    /// Run the integrity check by itself once metadata lands (the Preferences
    /// toggle, copied at creation). Comparison documents turn this off: the
    /// compare layout never shows the integrity banner.
    public bool AutoIntegrityCheck { get; init; } = true;

    /// The store of finished analyses shared across tabs (null = none). A hit
    /// at load skips ffprobe and the decode; every finished overview,
    /// integrity report, and loudness measurement is published into it, so
    /// the next tab to open this file (Ctrl+Left back to it, a compare)
    /// finds it. The per-document channel cache above stays the store for
    /// what is on screen; this only decides whether the next load is instant.
    public OverviewCache? Cache { get; init; }

    private SpectrogramSettings CurrentSettings => new(WindowSize: _windowSize, Window: _window);

    /// A finished overview of this channel at the current settings from the
    /// shared store, or null.
    private ChannelOverview? Remembered(int channelIndex) =>
        Cache?.Lookup(FilePath, CurrentSettings) is { } snapshot
            && snapshot.Overviews.TryGetValue(channelIndex, out var overview) ? overview : null;

    public string HeaderText { get => _headerText; private set => Set(ref _headerText, value); }

    public string? ErrorText
    {
        get => _errorText;
        private set { if (Set(ref _errorText, value)) RaisePropertyChanged(nameof(HasError)); }
    }

    public bool HasError => _errorText is not null;

    private LosslessVerdict? _verdict;

    /// Bandwidth/lossless verdict computed from the finished overview. Null while
    /// loading or when there is not enough signal to judge.
    public LosslessVerdict? Verdict
    {
        get => _verdict;
        private set
        {
            if (!Set(ref _verdict, value)) return;
            RaisePropertyChanged(nameof(VerdictText));
            RaisePropertyChanged(nameof(HasVerdict));
            RaisePropertyChanged(nameof(VerdictIsLossless));
            RaisePropertyChanged(nameof(VerdictIsSuspicious));
            RaisePropertyChanged(nameof(VerdictIsLossy));
            RaisePropertyChanged(nameof(VerdictIsUpsampled));
            RaisePropertyChanged(nameof(VerdictIsMixed));
            RaisePropertyChanged(nameof(CutoffHz));
        }
    }

    public bool HasVerdict => _verdict is { Kind: not VerdictKind.Unknown };
    public string? VerdictText => _verdict?.Summary;
    public bool VerdictIsLossless => _verdict?.Kind == VerdictKind.Lossless;
    public bool VerdictIsSuspicious => _verdict?.Kind == VerdictKind.Suspicious;
    public bool VerdictIsLossy => _verdict?.Kind == VerdictKind.Lossy;
    public bool VerdictIsUpsampled => _verdict?.Kind == VerdictKind.Upsampled;
    public bool VerdictIsMixed => _verdict?.Kind == VerdictKind.Mixed;

    /// The detected cutoff frequency in Hz for the shown verdict, or null when
    /// none is reported (lossless, or too little signal to judge). Drives the
    /// thin cutoff marker on the spectrogram; tracks the verdict.
    public double? CutoffHz => HasVerdict ? _verdict?.CutoffHz : null;

    private IntegrityReport? _integrity;
    private bool _integrityVisible = true;

    /// File integrity result, populated on demand by RunIntegrityCheckAsync.
    public IntegrityReport? Integrity
    {
        get => _integrity;
        private set
        {
            if (!Set(ref _integrity, value)) return;
            _integrityVisible = true; // fresh results always show
            RaisePropertyChanged(nameof(IntegrityVisible));
            RaisePropertyChanged(nameof(IntegrityText));
            RaisePropertyChanged(nameof(HasIntegrity));
            RaisePropertyChanged(nameof(IntegrityIsOk));
            RaisePropertyChanged(nameof(IntegrityIsSuspect));
            RaisePropertyChanged(nameof(IntegrityIsCorrupt));
        }
    }

    /// Whether existing integrity results are shown (banner + time-axis lane).
    public bool IntegrityVisible
    {
        get => _integrityVisible;
        private set { if (Set(ref _integrityVisible, value)) RaisePropertyChanged(nameof(HasIntegrity)); }
    }

    public bool HasIntegrity => _integrity is not null && _integrityVisible;
    public string? IntegrityText => _integrity is null ? null : $"Integrity: {_integrity.Summary}";
    public bool IntegrityIsOk => _integrity?.Status == IntegrityStatus.Ok;
    public bool IntegrityIsSuspect => _integrity?.Status == IntegrityStatus.Suspect;
    public bool IntegrityIsCorrupt => _integrity?.Status == IntegrityStatus.Corrupt;

    private LoudnessReport? _loudness;
    private bool _loudnessVisible = true;

    /// Loudness/dynamics result, populated on demand by RunLoudnessCheckAsync.
    public LoudnessReport? Loudness
    {
        get => _loudness;
        private set
        {
            if (!Set(ref _loudness, value)) return;
            _loudnessVisible = true; // fresh results always show
            RaisePropertyChanged(nameof(LoudnessText));
            RaisePropertyChanged(nameof(HasLoudness));
        }
    }

    public bool HasLoudness => _loudness is not null && _loudnessVisible;
    public string? LoudnessText => _loudness is null ? null : $"Loudness: {_loudness.Summary}";

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
            if (Metadata is not { } meta) return;
            _tileCts?.Cancel();
            Tile = null;
            ErrorText = null;
            if (_channelCache.TryGet(value, out var hit))
            {
                // Instant swap; DocumentChanged also re-schedules the sharp
                // zoom tile via SpectrogramView.OnDocumentChanged.
                Show(hit, CachedStatus(hit));
            }
            else if (_computingIndex == value && _computingDoc is { } doc)
            {
                // Attach to the in-flight compute: show its partial document;
                // the loop publishes verdict and status when it finishes.
                Document = doc;
                DocumentChanged?.Invoke();
                ShowAnalyzing();
            }
            else if (Remembered(value) is { } remembered)
            {
                // The shared store backs the per-document one: a channel this
                // document never computed, or let go of past its three, that
                // an earlier tab finished is still an instant swap.
                _channelCache.Set(value, remembered);
                Show(remembered, CachedStatus(remembered));
            }
            else
            {
                ShowAnalyzing();
                _ = RestartComputeLoopAsync(meta);
            }
        }
    }

    public int? SelectedChannel => ChannelList.ChannelFor(_selectedChannelIndex, Metadata?.Channels ?? 1);

    /// FFT window size (frequency buckets = WindowSize / 2 + 1). Changing it while
    /// a file is loaded re-analyzes at the new resolution.
    public int WindowSize
    {
        get => _windowSize;
        set { if (Set(ref _windowSize, value) && Metadata is not null) _ = LoadOverviewAsync(); }
    }

    /// FFT analysis window shape. Changing it while a file is loaded re-analyzes.
    public WindowFunctionKind Window
    {
        get => _window;
        set { if (Set(ref _window, value) && Metadata is not null) _ = LoadOverviewAsync(); }
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

    /// Puts a finished overview on screen. The view is told the document
    /// changed, unless this is the visible one completing where it stood, in
    /// which case it is told the document was updated.
    private void Show(ChannelOverview overview, string status, bool completedInPlace = false)
    {
        Document = overview.Document;
        if (completedInPlace) DocumentUpdated?.Invoke();
        else DocumentChanged?.Invoke();
        Verdict = overview.Verdict;
        Loudness = overview.Loudness;
        StatusText = status;
    }

    private static string CachedStatus(ChannelOverview overview) =>
        $"Cached · {overview.Document.Count} columns";

    /// Nothing to judge yet for the channel on screen.
    private void ShowAnalyzing()
    {
        Verdict = null;
        Loudness = null;
        StatusText = "Analyzing…";
    }

    /// A decode failure takes the picture away and puts the reason in its place.
    private void ShowDecodeError(AudioDecodeException ex)
    {
        Document = null;
        DocumentChanged?.Invoke();
        ErrorText = ex.StderrTail is null ? ex.Message : $"{ex.Message}\n{ex.StderrTail}";
        StatusText = "";
    }

    public async Task RenderTileAsync(int targetColumns)
    {
        if (Metadata is not { } meta || Document is null || !Viewport.IsTimeZoomed) return;
        _tileCts?.Cancel();
        var cts = _tileCts = new CancellationTokenSource();
        var (t0, t1) = (Viewport.T0, Viewport.T1);

        var map = TimeMap ?? new TimeMap(meta.Duration.TotalSeconds);
        if (TilePlan.For(map, t0, t1, meta.SampleRate, targetColumns) is not { } plan) return;

        var settings = new SpectrogramSettings(WindowSize: _windowSize, MaxColumns: targetColumns, HopOverride: plan.Hop, Window: _window);
        var options = new DecodeOptions(Start: plan.Start, Duration: plan.Duration, Channel: SelectedChannel);

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

    public override void Cancel()
    {
        _loadCts?.Cancel();
        _tileCts?.Cancel();
        _computeCts?.Cancel();
        _integrityCts?.Cancel();
        _loudnessCts?.Cancel();
    }
}
