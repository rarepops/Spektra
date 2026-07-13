using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Spektra.Core;

namespace Spektra.App.Controls;

public sealed class SpectrogramView : Control
{
    private DocumentViewModel? _vm;
    private readonly PaneRenderer _pane = new();
    private readonly PlotInteraction _interaction;
    private readonly DispatcherTimer _tileTimer;
    private DisplaySettings _display = new();
    private float[]? _avgProfile;
    private float[]? _peakProfile;
    private SpectrogramDocument? _profileDoc;
    private int _profileCount = -1;

    /// Colormap + dB range used to paint and to draw the legend.
    public void SetDisplay(DisplaySettings display)
    {
        _display = display;
        _pane.SetDisplay(display);
        InvalidateVisual();
    }

    public SpectrogramView()
    {
        _tileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tileTimer.Tick += (_, _) =>
        {
            _tileTimer.Stop();
            if (_vm is null) return;
            var columns = Math.Clamp((int)SpectrogramDraw.PlotRect(Bounds).Width, 64, 4096);
            _ = _vm.RenderTileAsync(columns);
        };
        _interaction = new PlotInteraction(this,
            () => _vm?.Document is null ? null : _vm.Viewport,
            () => SpectrogramDraw.PlotRect(Bounds));
    }

    public void Attach(DocumentViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.DocumentChanged -= OnDocumentChanged;
            _vm.DocumentUpdated -= OnDocumentUpdated;
            _vm.TileChanged -= OnTileChanged;
            _vm.Viewport.Changed -= OnViewportChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = vm;
        if (vm is not null)
        {
            vm.DocumentChanged += OnDocumentChanged;
            vm.DocumentUpdated += OnDocumentUpdated;
            vm.TileChanged += OnTileChanged;
            vm.Viewport.Changed += OnViewportChanged;
            vm.PropertyChanged += OnVmPropertyChanged;
        }
        _pane.SetDocument(vm?.Document);
        _pane.SetTile(vm?.Tile);
        ResetProfiles();
        ScheduleTile();
        InvalidateVisual();
    }

    private void OnViewportChanged() { InvalidateVisual(); ScheduleTile(); }

    // The integrity lane appears the moment RunIntegrityCheckAsync lands; the
    // cutoff line appears the moment the bandwidth verdict does.
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.Integrity) or nameof(DocumentViewModel.IntegrityVisible)
            or nameof(DocumentViewModel.CutoffHz))
            InvalidateVisual();
    }

    private void ScheduleTile()
    {
        _tileTimer.Stop();
        if (_vm?.Viewport.IsTimeZoomed == true) _tileTimer.Start();
    }

    private void OnTileChanged() { _pane.SetTile(_vm?.Tile); InvalidateVisual(); }
    private void OnDocumentChanged() { _pane.SetDocument(_vm?.Document); _pane.SetTile(_vm?.Tile); ResetProfiles(); ScheduleTile(); InvalidateVisual(); }
    private void OnDocumentUpdated() { _pane.SyncColumns(); InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var vp = _vm?.Viewport;
        if (_vm?.Document is { } doc && vp is not null)
        {
            var freq = new FreqAxis(vp.F0, vp.F1, _display.LogFrequency, doc.Metadata.SampleRate / 2.0);
            _pane.DrawInto(ctx, plot, vp.T0, vp.T1, vp.T0, vp.T1, freq);
            SpectrogramDraw.FrequencyRuler(ctx, plot, freq);
            if (_vm.IntegrityVisible && _vm.Integrity is { } integrity)
                SpectrogramDraw.IntegrityLane(ctx, plot,
                    IntegrityStrip.Segments(integrity, doc.Metadata.Duration.TotalSeconds, vp.T0, vp.T1));
            SpectrogramDraw.TimeRuler(ctx, plot, doc.Metadata.Duration.TotalSeconds, vp.T0, vp.T1);
        }
        SpectrogramDraw.Legend(ctx, plot, _display);
        DrawSpectrumOverlay(ctx, plot);
        DrawCutoffLine(ctx, plot);
        DrawCursorReadout(ctx, plot);
    }

    // Verdict-tier colours (the folder-grid marker palette): red = lossy wall,
    // violet = upsample edge, amber = suspicious rolloff. The 0xC0 alpha keeps
    // the line subtle over the colormap while staying legible.
    private static readonly IPen CutoffLossyPen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0xE0, 0x80, 0x80)), 1);
    private static readonly IPen CutoffUpsampledPen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0xB0, 0x8F, 0xD8)), 1);
    private static readonly IPen CutoffSuspiciousPen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0xD8, 0xB0, 0x60)), 1);

    // A thin line across the plot at the detected cutoff frequency, plus a
    // matching tick in the frequency gutter, so the wall is visible against the
    // image. Only drawn when a cutoff was detected (null = nothing). Reuses
    // FreqAxis, so it tracks zoom/pan and the log axis and clips to the visible
    // band; coloured by the verdict tier like the rest of the app.
    private void DrawCutoffLine(DrawingContext ctx, Rect plot)
    {
        if (_vm?.CutoffHz is not { } hz || _vm.Metadata is not { } meta) return;
        var nyquist = meta.SampleRate / 2.0;
        if (nyquist <= 0) return;
        var q = hz / nyquist;
        var vp = _vm.Viewport;
        if (q < vp.F0 || q > vp.F1) return; // cutoff outside the visible span
        var freq = new FreqAxis(vp.F0, vp.F1, _display.LogFrequency, nyquist);
        var y = plot.Bottom - freq.PosOf(q) * plot.Height;
        var pen = _vm.Verdict?.Kind switch
        {
            VerdictKind.Upsampled => CutoffUpsampledPen,
            VerdictKind.Suspicious => CutoffSuspiciousPen,
            _ => CutoffLossyPen,
        };
        ctx.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
        ctx.DrawLine(pen, new Point(plot.Left - 5, y), new Point(plot.Left, y));
    }

    private static readonly IBrush PeakBrush = new SolidColorBrush(Color.FromArgb(90, 180, 210, 255));
    private static readonly IPen AvgPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 120, 235, 150)), 1.4);
    private static readonly IPen PeakPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 150, 190, 255)), 1);

    // Marginal "level vs. frequency" plot over the right third of the spectrogram,
    // aligned to the frequency axis: peak-hold (filled) and time-average (line).
    private void DrawSpectrumOverlay(DrawingContext ctx, Rect plot)
    {
        if (!_display.ShowSpectrum || _vm?.Document is not { } doc || _vm.Metadata is not { } meta) return;
        if ((doc != _profileDoc || doc.Count != _profileCount) && doc.Count > 0)
        {
            var snap = Snapshot(doc);
            _avgProfile = SpectrumProfile.Average(snap);
            _peakProfile = SpectrumProfile.Peak(snap);
            _profileDoc = doc;
            _profileCount = doc.Count;
        }
        if (_peakProfile is not { Length: > 1 } peak || _avgProfile is not { } avg) return;

        var vp = _vm.Viewport;
        var freq = new FreqAxis(vp.F0, vp.F1, _display.LogFrequency, meta.SampleRate / 2.0);
        float floor = _display.DbFloor, ceil = _display.DbCeil;
        var overlayW = plot.Width * 0.33;
        var bins = peak.Length;

        Point Pt(float[] prof, int k)
        {
            var q = (double)k / (bins - 1);
            var y = plot.Bottom - freq.PosOf(q) * plot.Height;
            var lvl = Math.Clamp((prof[k] - floor) / (ceil - floor), 0f, 1f);
            return new Point(plot.Right - lvl * overlayW, y);
        }

        var kFrom = Math.Max(0, (int)Math.Floor(vp.F0 * (bins - 1)));
        var kTo = Math.Min(bins - 1, (int)Math.Ceiling(vp.F1 * (bins - 1)));
        if (kTo <= kFrom) return;

        var peakFill = new StreamGeometry();
        using (var g = peakFill.Open())
        {
            g.BeginFigure(new Point(plot.Right, Pt(peak, kFrom).Y), true);
            for (var k = kFrom; k <= kTo; k++) g.LineTo(Pt(peak, k));
            g.LineTo(new Point(plot.Right, Pt(peak, kTo).Y));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(PeakBrush, PeakPen, peakFill);

        var avgLine = new StreamGeometry();
        using (var g = avgLine.Open())
        {
            g.BeginFigure(Pt(avg, kFrom), false);
            for (var k = kFrom + 1; k <= kTo; k++) g.LineTo(Pt(avg, k));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, AvgPen, avgLine);
    }

    // A new document can share the old one's column count (same length, same
    // analysis settings), so the cache is keyed on identity too and dropped
    // eagerly on swap to release the old document.
    private void ResetProfiles()
    {
        _avgProfile = null;
        _peakProfile = null;
        _profileDoc = null;
        _profileCount = -1;
    }

    private static List<float[]> Snapshot(SpectrogramDocument doc)
    {
        var n = doc.Count;
        var list = new List<float[]>(n);
        for (var i = 0; i < n; i++) list.Add(doc.GetColumn(i));
        return list;
    }

    private static readonly IBrush ReadoutBg = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
    private static readonly IBrush ReadoutFg = new SolidColorBrush(Color.FromRgb(230, 230, 230));
    private static readonly Typeface ReadoutFont = new("Segoe UI, Inter, sans-serif");

    // Crosshair + a "time · frequency · dB" label at the pointer. dB is sampled
    // from the overview column nearest the cursor (coarse but instant).
    private void DrawCursorReadout(DrawingContext ctx, Rect plot)
    {
        if (!_display.ShowCrosshair) return;
        if (_interaction.Cursor is not { } p || _vm?.Document is not { } doc || _vm.Metadata is not { } meta) return;
        if (!plot.Contains(p)) return;
        var vp = _vm.Viewport;

        var tN = vp.T0 + (p.X - plot.Left) / plot.Width * (vp.T1 - vp.T0);
        var seconds = tN * meta.Duration.TotalSeconds;
        var pos = 1 - (p.Y - plot.Top) / plot.Height;
        var freq = new FreqAxis(vp.F0, vp.F1, _display.LogFrequency, meta.SampleRate / 2.0);
        var q = freq.FreqAt(pos);
        var hz = q * freq.Nyquist;
        var db = SampleDb(doc, tN, q);

        ctx.DrawLine(SpectrogramDraw.CursorUnderlayPen, new Point(p.X, plot.Top), new Point(p.X, plot.Bottom));
        ctx.DrawLine(SpectrogramDraw.CursorUnderlayPen, new Point(plot.Left, p.Y), new Point(plot.Right, p.Y));
        ctx.DrawLine(SpectrogramDraw.CursorLinePen, new Point(p.X, plot.Top), new Point(p.X, plot.Bottom));
        ctx.DrawLine(SpectrogramDraw.CursorLinePen, new Point(plot.Left, p.Y), new Point(plot.Right, p.Y));

        var timeLabel = AudioMetadata.FormatDuration(TimeSpan.FromSeconds(Math.Max(0, seconds)))
                        + "." + (int)(Math.Max(0, seconds) * 10 % 10);
        var freqLabel = hz >= 1000 ? $"{hz / 1000:0.0} kHz" : $"{hz:0} Hz";
        var text = $"{timeLabel}   {freqLabel}   {db:0} dB";
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, ReadoutFont, 12, ReadoutFg);
        var boxW = ft.Width + 12;
        var boxH = ft.Height + 8;
        var bx = Math.Min(p.X + 14, plot.Right - boxW);
        var by = Math.Min(Math.Max(p.Y - boxH - 6, plot.Top), plot.Bottom - boxH);
        ctx.FillRectangle(ReadoutBg, new Rect(bx, by, boxW, boxH), 3);
        ctx.DrawText(ft, new Point(bx + 6, by + 4));
    }

    private static float SampleDb(SpectrogramDocument doc, double tN, double q)
    {
        if (doc.Count == 0) return Db.Floor;
        var col = Math.Clamp((int)(tN * doc.Count), 0, doc.Count - 1);
        var bin = Math.Clamp((int)Math.Round(q * (doc.Bins - 1)), 0, doc.Bins - 1);
        return doc.GetColumn(col)[bin];
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _interaction.WheelChanged(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _interaction.Pressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _interaction.Moved(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _interaction.Exited();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _interaction.Released(e);
    }
}
