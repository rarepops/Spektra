using Avalonia.Threading;
using Spektra.Core;

namespace Spektra.App;

public sealed class DocumentViewModel : TabViewModelBase
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
            RaisePropertyChanged(nameof(CutoffHz));
        }
    }

    public bool HasVerdict => _verdict is { Kind: not VerdictKind.Unknown };
    public string? VerdictText => _verdict?.Summary;
    public bool VerdictIsLossless => _verdict?.Kind == VerdictKind.Lossless;
    public bool VerdictIsSuspicious => _verdict?.Kind == VerdictKind.Suspicious;
    public bool VerdictIsLossy => _verdict?.Kind == VerdictKind.Lossy;
    public bool VerdictIsUpsampled => _verdict?.Kind == VerdictKind.Upsampled;

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

    /// Ctrl+L / the Analyze menu: the first use measures; once results exist
    /// it toggles the banner without re-measuring.
    public Task ToggleLoudnessAsync()
    {
        if (Loudness is null) return RunLoudnessCheckAsync();
        _loudnessVisible = !_loudnessVisible;
        RaisePropertyChanged(nameof(HasLoudness));
        return Task.CompletedTask;
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
                Document = hit.Document;
                DocumentChanged?.Invoke();
                Verdict = hit.Verdict;
                Loudness = hit.Loudness;
                StatusText = $"Cached · {hit.Document.Count} columns";
            }
            else if (_computingIndex == value && _computingDoc is { } doc)
            {
                // Attach to the in-flight compute: show its partial document;
                // the loop publishes verdict and status when it finishes.
                Document = doc;
                DocumentChanged?.Invoke();
                Verdict = null;
                Loudness = null;
                StatusText = "Analyzing…";
            }
            else
            {
                Verdict = null;
                Loudness = null;
                StatusText = "Analyzing…";
                _ = RestartComputeLoopAsync(meta);
            }
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

    private bool _loadStarted;

    /// Loads the overview once, on first need. Restored tabs are added
    /// without loading, so a restored session costs one decode (the selected
    /// tab) instead of one per tab; the rest load when first selected.
    /// Idempotent: every later selection of the same tab is a no-op.
    public Task EnsureLoadedAsync()
    {
        if (_loadStarted) return Task.CompletedTask;
        return LoadOverviewAsync();
    }

    public async Task LoadOverviewAsync()
    {
        _loadStarted = true;
        _loadCts?.Cancel();
        _tileCts?.Cancel();
        _computeCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();

        _channelCache.Clear();
        ErrorText = null;
        Tile = null;
        Verdict = null;
        Integrity = null;
        Loudness = null;
        StatusText = "Reading metadata…";

        try
        {
            var session = new AnalysisSession(_ffmpeg);
            var meta = await Task.Run(() => session.ReadMetadata(FilePath), cts.Token);
            if (cts.Token.IsCancellationRequested) return;
            Metadata = meta;
            if (ChannelOptions.Count == 1 && meta.Channels > 1)
                ChannelOptions = ["Mix", .. Enumerable.Range(1, meta.Channels).Select(i => $"Ch {i}")];
            HeaderText = meta.ToDisplayLine(TabTitle);

            // max-zoom clamp: a nominal 1024-column tile never drops below 64 samples/hop
            var totalSamples = meta.Duration.TotalSeconds * meta.SampleRate;
            Viewport.MinTimeSpan = totalSamples > 0 ? Math.Min(1, 64.0 * 1024 / totalSamples) : 1;

            // Every load checks integrity by itself (unless the preference
            // says otherwise): a damaged file must not hide behind a green
            // bandwidth banner until someone remembers Ctrl+I. Runs beside
            // the overview decode; the banner and lane appear whenever the
            // result lands.
            if (AutoIntegrityCheck) _ = RunIntegrityCheckAsync();

            await RestartComputeLoopAsync(meta);
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

    /// Cancels the running compute loop, waits for it to unwind (so decodes
    /// never overlap), then starts a fresh loop unless superseded meanwhile.
    private async Task RestartComputeLoopAsync(AudioMetadata meta)
    {
        _computeCts?.Cancel();
        _computingIndex = -1;
        _computingDoc = null;
        var cts = _computeCts = new CancellationTokenSource();
        if (_computeLoop is { } previous)
        {
            // The await is sequencing only, so decodes never overlap. The loop
            // catches cancellation and decode errors itself, so an unexpected
            // fault (an ffmpeg spawn failure, say) lives in the task: rethrown
            // here it would kill this restart, which is usually fire-and-forget
            // and would die unobserved, hanging the tab on "Analyzing…".
            try { await previous; }
            catch (Exception e) when (e is not OutOfMemoryException) { }
        }
        if (cts.Token.IsCancellationRequested) return;
        _computeLoop = RunComputeLoopAsync(meta, cts.Token);
    }

    /// Sequentially computes overviews: the selected channel first, then (when
    /// the whole file fits the cache) the remaining variants as silent
    /// prefetch. Runs on the UI thread; only decode/FFT work is offloaded.
    /// Handles cancellation and decode failures itself; anything unexpected
    /// faults the loop task.
    private async Task RunComputeLoopAsync(AudioMetadata meta, CancellationToken ct)
    {
        var failed = new HashSet<int>();
        while (!ct.IsCancellationRequested)
        {
            var index = NextIndexToCompute(meta, failed);
            if (index < 0) return;
            var started = DateTime.UtcNow;
            _computingIndex = index;
            try
            {
                var entry = await ComputeOverviewAsync(index, meta, ct);
                _channelCache.Set(index, entry);
                if (index == SelectedChannelIndex)
                {
                    Document = entry.Document;
                    DocumentUpdated?.Invoke();
                    Verdict = entry.Verdict;
                    Loudness = entry.Loudness;
                    StatusText = $"Done in {(DateTime.UtcNow - started).TotalSeconds:0.0}s · {entry.Document.Count} columns";
                }
            }
            catch (OperationCanceledException) { return; }
            catch (AudioDecodeException ex)
            {
                if (ct.IsCancellationRequested) return;
                if (index == SelectedChannelIndex)
                {
                    // Visible failure: show the banner and stop; if one channel
                    // cannot decode the rest almost certainly cannot either.
                    // Re-selecting the channel retries with a fresh run.
                    Document = null;
                    DocumentChanged?.Invoke();
                    ErrorText = ex.StderrTail is null ? ex.Message : $"{ex.Message}\n{ex.StderrTail}";
                    StatusText = "";
                    return;
                }
                failed.Add(index); // silent per-channel prefetch failure
            }
            finally
            {
                _computingIndex = -1;
                _computingDoc = null;
            }
        }
    }

    /// The selected channel if not yet cached, else the first missing prefetch
    /// candidate (only when prefetch is on and 1 + Channels fits the cache,
    /// i.e. stereo), else -1 to stop.
    private int NextIndexToCompute(AudioMetadata meta, HashSet<int> failed)
    {
        if (!_channelCache.TryGet(SelectedChannelIndex, out _)) return SelectedChannelIndex;
        if (!PrefetchChannels || meta.Channels < 2) return -1;
        var variants = 1 + meta.Channels;
        if (variants > ChannelCacheCapacity) return -1;
        for (var i = 0; i < variants; i++)
            if (!failed.Contains(i) && !_channelCache.TryGet(i, out _)) return i;
        return -1;
    }

    /// Computes one channel's overview end to end (stream columns, then the
    /// bandwidth verdict). While its document is the visible one it paints
    /// progressively, exactly like a fresh load; as silent prefetch it posts
    /// nothing.
    private async Task<ChannelOverview> ComputeOverviewAsync(int index, AudioMetadata meta, CancellationToken ct)
    {
        var session = new AnalysisSession(_ffmpeg);
        var settings = new SpectrogramSettings(WindowSize: _windowSize, Window: _window);
        var estSamples = (long)(meta.Duration.TotalSeconds * meta.SampleRate);
        var estWindows = Math.Max(1, (estSamples - settings.WindowSize) / settings.Hop + 1);
        var estColumns = (int)Math.Min(estWindows, settings.MaxColumns);

        var doc = new SpectrogramDocument(meta, settings, estColumns);
        _computingDoc = doc;
        if (index == SelectedChannelIndex)
        {
            Document = doc;
            DocumentChanged?.Invoke();
            StatusText = "Analyzing…";
        }

        var channel = index == 0 ? (int?)null : index - 1;
        await Task.Run(() =>
        {
            var sinceRefresh = 0;
            foreach (var column in session.AnalyzeColumns(FilePath, meta, settings, ct,
                new DecodeOptions(Channel: channel)))
            {
                doc.Append(column);
                if (++sinceRefresh >= 32)
                {
                    sinceRefresh = 0;
                    if (ReferenceEquals(Document, doc))
                        Dispatcher.UIThread.Post(() => DocumentUpdated?.Invoke());
                }
            }
        }, ct);

        var verdict = await Task.Run(() => AnalyzeBandwidth(doc, meta.SampleRate), ct);
        return new ChannelOverview(doc, verdict, null);
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
        var settings = new SpectrogramSettings(WindowSize: _windowSize, MaxColumns: targetColumns, HopOverride: hop, Window: _window);
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

    public override void Cancel()
    {
        _loadCts?.Cancel();
        _tileCts?.Cancel();
        _computeCts?.Cancel();
        _integrityCts?.Cancel();
        _loudnessCts?.Cancel();
    }

    private CancellationTokenSource? _integrityCts;

    /// Ctrl+I / the Analyze menu: toggles the results (banner + lane) without
    /// re-analyzing; the check itself runs automatically at load, so this only
    /// starts one when none is in yet.
    public Task ToggleIntegrityAsync()
    {
        if (Integrity is null) return RunIntegrityCheckAsync();
        IntegrityVisible = !IntegrityVisible;
        return Task.CompletedTask;
    }

    /// Runs the integrity check (corrupt frames, missing data, truncation) on a
    /// background thread and publishes the result to the Integrity banner.
    public async Task RunIntegrityCheckAsync()
    {
        if (Metadata is not { } meta) return;
        _integrityCts?.Cancel();
        var cts = _integrityCts = new CancellationTokenSource();
        StatusText = "Checking integrity…";
        try
        {
            var report = await Task.Run(
                () => new IntegrityScanner(_ffmpeg).Check(FilePath, meta, cts.Token), cts.Token);
            if (cts.Token.IsCancellationRequested) return;
            Integrity = report;
            StatusText = report.Summary;
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException ex)
        {
            Integrity = new IntegrityReport(
                IntegrityStatus.Corrupt, 0, [], 0, meta.Duration.TotalSeconds, true, ex.Message);
        }
    }

    private CancellationTokenSource? _loudnessCts;

    /// Measures loudness (LUFS), true peak, and dynamics on a background thread
    /// and publishes the result to the Loudness banner.
    public async Task RunLoudnessCheckAsync()
    {
        if (Metadata is null) return;
        _loudnessCts?.Cancel();
        var cts = _loudnessCts = new CancellationTokenSource();
        var channelIndex = SelectedChannelIndex;
        var channel = SelectedChannel;
        StatusText = "Measuring loudness…";
        try
        {
            var report = await Task.Run(
                () => new LoudnessMeasurer(_ffmpeg).Measure(FilePath, channel, cts.Token), cts.Token);
            if (cts.Token.IsCancellationRequested) return;
            if (_channelCache.TryGet(channelIndex, out var entry))
                _channelCache.Set(channelIndex, entry with { Loudness = report });
            if (channelIndex == SelectedChannelIndex)
            {
                Loudness = report;
                StatusText = report.Summary;
            }
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException ex) { SetErrorStatus(ex.Message); }
    }

    /// Snapshots the finished overview columns and runs the bandwidth/lossless
    /// analysis. Peak-hold over the whole file, so the zoomed-out max-merged
    /// columns are exactly what the detector needs.
    private static LosslessVerdict AnalyzeBandwidth(SpectrogramDocument doc, int sampleRate)
    {
        var count = doc.Count;
        var columns = new List<float[]>(count);
        for (var i = 0; i < count; i++) columns.Add(doc.GetColumn(i));
        return CutoffAnalyzer.Analyze(columns, sampleRate);
    }

    /// One channel's finished analysis: the overview document, its bandwidth
    /// verdict, and any loudness measured while that channel was active.
    private sealed record ChannelOverview(
        SpectrogramDocument Document, LosslessVerdict? Verdict, LoudnessReport? Loudness);
}
