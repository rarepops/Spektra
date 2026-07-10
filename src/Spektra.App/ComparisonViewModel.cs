using Spektra.Core;

namespace Spektra.App;

public enum CompareMode { Both, A, B, Diff }

public sealed class ComparisonViewModel : ObservableObject, ITab
{
    private readonly FfmpegPaths _ffmpeg;
    private CancellationTokenSource? _diffCts;
    private CancellationTokenSource? _alignCts;
    private CancellationTokenSource? _nullCts;

    private bool _isSelected;
    private CompareMode _mode = CompareMode.Both;
    private double _offsetSeconds;
    private int _coarseMs;
    private int _fineMs;
    private bool _syncingSliders;
    private string _statusText = "";
    private string? _alignHint;
    private string? _busyText;
    private int _windowSize = 2048;
    private WindowFunctionKind _window = WindowFunctionKind.Hann;

    public DocumentViewModel A { get; }
    public DocumentViewModel B { get; }
    public Viewport Viewport { get; } = new();

    public string TabTitle { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    /// Normal assignment resets the error flag; SetErrorStatus keeps it red.
    public string StatusText
    {
        get => _statusText;
        private set { _ = Set(ref _statusText, value); StatusIsError = false; }
    }

    private bool _statusIsError;
    public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }

    private void SetErrorStatus(string text) { StatusText = text; StatusIsError = true; }

    public IReadOnlyList<float[]>? Diff { get; private set; }
    public double DiffT0 { get; private set; }
    public double DiffT1 { get; private set; }
    public const float DiffClamp = 40f;

    /// Set by the view from its plot width; used for diff/tile resolution.
    public int TargetColumns { get; set; } = 1024;

    /// FFT window size shared by both panes and the diff; re-analyzes A and B and
    /// refreshes the difference when changed.
    public int WindowSize
    {
        get => _windowSize;
        set
        {
            if (!Set(ref _windowSize, value)) return;
            A.WindowSize = value;
            B.WindowSize = value;
            if (_mode == CompareMode.Diff) DiffRequested?.Invoke();
        }
    }

    /// FFT window shape shared by both panes and the diff.
    public WindowFunctionKind Window
    {
        get => _window;
        set
        {
            if (!Set(ref _window, value)) return;
            A.Window = value;
            B.Window = value;
            if (_mode == CompareMode.Diff) DiffRequested?.Invoke();
        }
    }

    public event Action? Changed;        // mode / offset / panes → repaint
    public event Action? DiffChanged;    // new diff columns ready
    public event Action? DiffRequested;  // ask the view to (re)start its debounce timer
    public event Action? BusyChanged;    // long-running op started/finished → repaint overlay
    public event Action? OffsetChanged;  // B shifted → repaint now, re-decode B's sharp tile debounced

    public ComparisonViewModel(FfmpegPaths ffmpeg, string pathA, string pathB)
    {
        _ffmpeg = ffmpeg;
        A = new DocumentViewModel(ffmpeg, pathA, Viewport) { PrefetchChannels = false };
        B = new DocumentViewModel(ffmpeg, pathB, Viewport) { PrefetchChannels = false };
        TabTitle = $"{A.TabTitle} ⇄ {B.TabTitle}";
        Viewport.Changed += () => { if (_mode == CompareMode.Diff) DiffRequested?.Invoke(); };
    }

    public CompareMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value)) return;
            RaisePropertyChanged(nameof(ModeBoth));
            RaisePropertyChanged(nameof(ModeA));
            RaisePropertyChanged(nameof(ModeB));
            RaisePropertyChanged(nameof(ModeDiff));
            Changed?.Invoke();
            if (value == CompareMode.Diff) DiffRequested?.Invoke();
        }
    }

    // RadioButton-friendly booleans for the segmented mode selector.
    public bool ModeBoth { get => _mode == CompareMode.Both; set { if (value) Mode = CompareMode.Both; } }
    public bool ModeA { get => _mode == CompareMode.A; set { if (value) Mode = CompareMode.A; } }
    public bool ModeB { get => _mode == CompareMode.B; set { if (value) Mode = CompareMode.B; } }
    public bool ModeDiff { get => _mode == CompareMode.Diff; set { if (value) Mode = CompareMode.Diff; } }

    public double OffsetSeconds
    {
        get => _offsetSeconds;
        set => SetOffset(value, syncSliders: true);
    }

    // Coarse (±5 s) and fine (±500 ms) trims sum to the offset, giving the
    // precision the single wide slider couldn't. Dragging either recomputes the
    // offset without re-seating the sliders; an external set (Auto-align) re-seats
    // both (coarse = value, fine = remainder).
    public int CoarseMs
    {
        get => _coarseMs;
        set { if (Set(ref _coarseMs, value) && !_syncingSliders) SetOffset((_coarseMs + _fineMs) / 1000.0, syncSliders: false); }
    }

    public int FineMs
    {
        get => _fineMs;
        set { if (Set(ref _fineMs, value) && !_syncingSliders) SetOffset((_coarseMs + _fineMs) / 1000.0, syncSliders: false); }
    }

    public int OffsetMs
    {
        get => (int)Math.Round(_offsetSeconds * 1000);
        set => SetOffset(value / 1000.0, syncSliders: true);
    }

    private void SetOffset(double seconds, bool syncSliders)
    {
        if (!Set(ref _offsetSeconds, seconds, nameof(OffsetSeconds))) return;
        B.TimeMap = new TimeMap(A.Metadata?.Duration.TotalSeconds ?? 0, _offsetSeconds);
        RaisePropertyChanged(nameof(OffsetMs));
        if (syncSliders)
        {
            _syncingSliders = true;
            var ms = (int)Math.Round(_offsetSeconds * 1000);
            CoarseMs = Math.Clamp(ms, -5000, 5000);
            FineMs = Math.Clamp(ms - _coarseMs, -500, 500);
            _syncingSliders = false;
        }
        Changed?.Invoke();
        OffsetChanged?.Invoke();
        if (_mode == CompareMode.Diff) DiffRequested?.Invoke();
    }

    public string? AlignHint
    {
        get => _alignHint;
        private set { if (Set(ref _alignHint, value)) RaisePropertyChanged(nameof(HasAlignHint)); }
    }
    public bool HasAlignHint => _alignHint is not null;

    /// Non-null while a long-running compare op (align / diff) is in flight;
    /// the surface paints it as a centered "processing" overlay.
    public string? BusyText
    {
        get => _busyText;
        private set
        {
            if (!Set(ref _busyText, value)) return;
            RaisePropertyChanged(nameof(IsBusy));
            BusyChanged?.Invoke();
        }
    }
    public bool IsBusy => _busyText is not null;

    public void FlipAB() => Mode = _mode == CompareMode.A ? CompareMode.B : CompareMode.A;

    public async Task LoadAsync()
    {
        await A.LoadOverviewAsync();
        if (A.Metadata is { } ma)
        {
            var samples = ma.Duration.TotalSeconds * ma.SampleRate;
            Viewport.MinTimeSpan = samples > 0 ? Math.Min(1, 64.0 * 1024 / samples) : 1;
            A.TimeMap = new TimeMap(ma.Duration.TotalSeconds);
            B.TimeMap = new TimeMap(ma.Duration.TotalSeconds, _offsetSeconds);
        }
        await B.LoadOverviewAsync();
    }

    public async Task AlignAsync()
    {
        if (A.Metadata is null || B.Metadata is null) return;
        _alignCts?.Cancel();
        var cts = _alignCts = new CancellationTokenSource();
        StatusText = "Aligning…";
        BusyText = "Aligning…";
        try
        {
            var res = await Task.Run(() => Aligner.EstimateOffset(
                _ffmpeg, A.FilePath, B.FilePath, A.SelectedChannel, B.SelectedChannel, ct: cts.Token), cts.Token);
            if (cts.Token.IsCancellationRequested) return;
            if (res.Confidence < 0.3)
            {
                AlignHint = $"Auto-align uncertain (confidence {res.Confidence:0.00}) — dial manually.";
                StatusText = "";
                return;
            }
            AlignHint = null;
            OffsetSeconds = res.Offset.TotalSeconds;
            StatusText = $"Aligned {OffsetMs:+0;-0} ms (confidence {res.Confidence:0.00})";
            var drift = await Task.Run(() => Aligner.EstimateDriftSeconds(
                _ffmpeg, A.FilePath, B.FilePath, A.SelectedChannel, B.SelectedChannel, ct: cts.Token), cts.Token);
            if (!cts.Token.IsCancellationRequested && drift > 0.02)
                AlignHint = $"Alignment drifts about {drift * 1000:0} ms across the file; a single offset can't fully align it.";
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException ex) { AlignHint = ex.Message; StatusText = ""; }
        finally { if (ReferenceEquals(_alignCts, cts)) BusyText = null; }
    }

    /// Time-domain phase-cancellation test over the visible span: A minus B
    /// (B shifted by the offset), reported as a residual level in the status line.
    public async Task NullTestAsync()
    {
        if (A.Metadata is not { } ma || B.Metadata is not { } mb) return;
        _nullCts?.Cancel();
        var cts = _nullCts = new CancellationTokenSource();
        var vp = Viewport;
        var refDur = ma.Duration.TotalSeconds;
        var startSec = vp.T0 * refDur;
        var spanSec = vp.TimeSpanN * refDur;
        if (spanSec <= 0) return;
        var rate = Math.Min(ma.SampleRate, mb.SampleRate);
        BusyText = "Null test…";
        try
        {
            var res = await Task.Run(() => new NullTest(_ffmpeg).Compute(
                A.FilePath, A.SelectedChannel, B.FilePath, B.SelectedChannel,
                startSec, spanSec, _offsetSeconds, rate, cts.Token), cts.Token);
            if (!cts.Token.IsCancellationRequested) StatusText = res.Summary;
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException ex) { SetErrorStatus(ex.Message); }
        finally { if (ReferenceEquals(_nullCts, cts)) BusyText = null; }
    }

    public async Task ComputeDiffAsync(int targetColumns)
    {
        if (A.Metadata is not { } ma || B.Metadata is not { } mb) return;
        if (A.Document is null || B.Document is null)
        {
            SetErrorStatus("Diff unavailable: one file failed to decode.");
            return;
        }
        _diffCts?.Cancel();
        var cts = _diffCts = new CancellationTokenSource();
        var vp = Viewport;
        var refDur = ma.Duration.TotalSeconds;
        var startSec = vp.T0 * refDur;
        var spanSec = vp.TimeSpanN * refDur;
        if (spanSec <= 0) return;
        BusyText = "Computing difference…";
        try
        {
            var (t0, t1) = (vp.T0, vp.T1);
            var cols = await Task.Run(() => new SpectralDiff(_ffmpeg).Compute(
                A.FilePath, ma, A.SelectedChannel, B.FilePath, mb, B.SelectedChannel,
                startSec, spanSec, _offsetSeconds, targetColumns, _windowSize, _window, cts.Token), cts.Token);
            if (cts.Token.IsCancellationRequested || cols.Count == 0) return;
            Diff = cols; DiffT0 = t0; DiffT1 = t1;
            StatusText = DiffScore.Compute(cols).Summary;
            DiffChanged?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException) { /* diff is best-effort */ }
        finally { if (ReferenceEquals(_diffCts, cts)) BusyText = null; }
    }

    public void Cancel()
    {
        _diffCts?.Cancel();
        _alignCts?.Cancel();
        _nullCts?.Cancel();
        A.Cancel();
        B.Cancel();
    }
}
