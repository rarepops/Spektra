using Spektra.Core;

namespace Spektra.App;

public enum CompareMode { Both, A, B, Diff }

public sealed class ComparisonViewModel : ObservableObject, ITab
{
    private readonly FfmpegPaths _ffmpeg;
    private CancellationTokenSource? _diffCts;
    private CancellationTokenSource? _alignCts;

    private bool _isSelected;
    private CompareMode _mode = CompareMode.Both;
    private double _offsetSeconds;
    private string _statusText = "";
    private string? _alignHint;

    public DocumentViewModel A { get; }
    public DocumentViewModel B { get; }
    public Viewport Viewport { get; } = new();

    public string TabTitle { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public IReadOnlyList<float[]>? Diff { get; private set; }
    public double DiffT0 { get; private set; }
    public double DiffT1 { get; private set; }
    public const float DiffClamp = 40f;

    /// Set by the view from its plot width; used for diff/tile resolution.
    public int TargetColumns { get; set; } = 1024;

    public event Action? Changed;        // mode / offset / panes → repaint
    public event Action? DiffChanged;    // new diff columns ready
    public event Action? DiffRequested;  // ask the view to (re)start its debounce timer

    public ComparisonViewModel(FfmpegPaths ffmpeg, string pathA, string pathB)
    {
        _ffmpeg = ffmpeg;
        A = new DocumentViewModel(ffmpeg, pathA, Viewport);
        B = new DocumentViewModel(ffmpeg, pathB, Viewport);
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
        set
        {
            if (!Set(ref _offsetSeconds, value)) return;
            B.TimeMap = new TimeMap(A.Metadata?.Duration.TotalSeconds ?? 0, _offsetSeconds);
            RaisePropertyChanged(nameof(OffsetMs));
            Changed?.Invoke();
            _ = B.RenderTileAsync(TargetColumns);
            if (_mode == CompareMode.Diff) DiffRequested?.Invoke();
        }
    }

    public int OffsetMs
    {
        get => (int)Math.Round(_offsetSeconds * 1000);
        set => OffsetSeconds = value / 1000.0;
    }

    public string? AlignHint
    {
        get => _alignHint;
        private set { if (Set(ref _alignHint, value)) RaisePropertyChanged(nameof(HasAlignHint)); }
    }
    public bool HasAlignHint => _alignHint is not null;

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
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException ex) { AlignHint = ex.Message; StatusText = ""; }
    }

    public async Task ComputeDiffAsync(int targetColumns)
    {
        if (A.Metadata is not { } ma || B.Metadata is not { } mb) return;
        if (A.Document is null || B.Document is null)
        {
            StatusText = "Diff unavailable — one file failed to decode.";
            return;
        }
        _diffCts?.Cancel();
        var cts = _diffCts = new CancellationTokenSource();
        var vp = Viewport;
        var refDur = ma.Duration.TotalSeconds;
        var startSec = vp.T0 * refDur;
        var spanSec = vp.TimeSpanN * refDur;
        if (spanSec <= 0) return;
        try
        {
            var (t0, t1) = (vp.T0, vp.T1);
            var cols = await Task.Run(() => new SpectralDiff(_ffmpeg).Compute(
                A.FilePath, ma, A.SelectedChannel, B.FilePath, mb, B.SelectedChannel,
                startSec, spanSec, _offsetSeconds, targetColumns, cts.Token), cts.Token);
            if (cts.Token.IsCancellationRequested || cols.Count == 0) return;
            Diff = cols; DiffT0 = t0; DiffT1 = t1;
            DiffChanged?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException) { /* diff is best-effort */ }
    }

    public void Cancel()
    {
        _diffCts?.Cancel();
        _alignCts?.Cancel();
        A.Cancel();
        B.Cancel();
    }
}
