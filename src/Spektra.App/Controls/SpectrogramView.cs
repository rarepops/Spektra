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
    private bool _panning;
    private Point _lastPointer;
    private Point? _cursor;
    private readonly DispatcherTimer _tileTimer;
    private DisplaySettings _display = new();
    private float[]? _avgProfile;
    private float[]? _peakProfile;
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
        ScheduleTile();
        InvalidateVisual();
    }

    private void OnViewportChanged() { InvalidateVisual(); ScheduleTile(); }

    // The integrity lane appears the moment RunIntegrityCheckAsync lands.
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.Integrity)) InvalidateVisual();
    }

    private void ScheduleTile()
    {
        _tileTimer.Stop();
        if (_vm?.Viewport.IsTimeZoomed == true) _tileTimer.Start();
    }

    private void OnTileChanged() { _pane.SetTile(_vm?.Tile); InvalidateVisual(); }
    private void OnDocumentChanged() { _pane.SetDocument(_vm?.Document); _pane.SetTile(_vm?.Tile); ScheduleTile(); InvalidateVisual(); }
    private void OnDocumentUpdated() { _pane.SyncColumns(); InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var vp = _vm?.Viewport;
        if (_vm?.Document is { } doc && vp is not null)
        {
            var nyquist = doc.Metadata.SampleRate / 2.0;
            _pane.DrawInto(ctx, plot, vp.T0, vp.T1, vp.F0, vp.F1, vp.T0, vp.T1, _display.LogFrequency, nyquist);
            SpectrogramDraw.FrequencyRuler(ctx, plot, doc.Metadata.SampleRate, vp.F0, vp.F1, _display.LogFrequency);
            if (_vm.Integrity is { } integrity)
                SpectrogramDraw.IntegrityLane(ctx, plot,
                    IntegrityStrip.Segments(integrity, doc.Metadata.Duration.TotalSeconds, vp.T0, vp.T1));
            SpectrogramDraw.TimeRuler(ctx, plot, doc.Metadata.Duration.TotalSeconds, vp.T0, vp.T1);
        }
        SpectrogramDraw.Legend(ctx, plot, _display);
        DrawSpectrumOverlay(ctx, plot);
        DrawCursorReadout(ctx, plot);
    }

    private static readonly IBrush PeakBrush = new SolidColorBrush(Color.FromArgb(90, 180, 210, 255));
    private static readonly IPen AvgPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 120, 235, 150)), 1.4);
    private static readonly IPen PeakPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 150, 190, 255)), 1);

    // Marginal "level vs. frequency" plot over the right third of the spectrogram,
    // aligned to the frequency axis: peak-hold (filled) and time-average (line).
    private void DrawSpectrumOverlay(DrawingContext ctx, Rect plot)
    {
        if (!_display.ShowSpectrum || _vm?.Document is not { } doc || _vm.Metadata is not { } meta) return;
        if (doc.Count != _profileCount && doc.Count > 0)
        {
            var snap = Snapshot(doc);
            _avgProfile = SpectrumProfile.Average(snap);
            _peakProfile = SpectrumProfile.Peak(snap);
            _profileCount = doc.Count;
        }
        if (_peakProfile is not { Length: > 1 } peak || _avgProfile is not { } avg) return;

        var vp = _vm.Viewport;
        var nyquist = meta.SampleRate / 2.0;
        float floor = _display.DbFloor, ceil = _display.DbCeil;
        var overlayW = plot.Width * 0.33;
        var bins = peak.Length;

        Point Pt(float[] prof, int k)
        {
            var q = (double)k / (bins - 1);
            var y = plot.Bottom - SpectrogramDraw.FreqPos(q, vp.F0, vp.F1, _display.LogFrequency, nyquist) * plot.Height;
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
        if (_cursor is not { } p || _vm?.Document is not { } doc || _vm.Metadata is not { } meta) return;
        if (!plot.Contains(p)) return;
        var vp = _vm.Viewport;

        var tN = vp.T0 + (p.X - plot.Left) / plot.Width * (vp.T1 - vp.T0);
        var seconds = tN * meta.Duration.TotalSeconds;
        var pos = 1 - (p.Y - plot.Top) / plot.Height;
        var nyquist = meta.SampleRate / 2.0;
        var q = SpectrogramDraw.PosToFreq(pos, vp.F0, vp.F1, _display.LogFrequency, nyquist);
        var hz = q * nyquist;
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
        if (_vm?.Document is null) return;
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var p = e.GetPosition(this);
        if (!plot.Contains(p)) return;
        var delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
        if (delta == 0) return;
        var factor = delta > 0 ? 0.8 : 1.25;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            _vm.Viewport.ZoomFrequency(1 - (p.Y - plot.Top) / plot.Height, factor);
        else
            _vm.Viewport.ZoomTime((p.X - plot.Left) / plot.Width, factor);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm?.Document is null) return;
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var p = e.GetPosition(this);
        if (!plot.Contains(p)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { _vm.Viewport.Reset(); e.Handled = true; return; }
        _panning = true;
        _lastPointer = p;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null) return;
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var p = e.GetPosition(this);
        _cursor = p;
        if (_panning)
        {
            var vp = _vm.Viewport;
            vp.PanTime(-(p.X - _lastPointer.X) / plot.Width * vp.TimeSpanN);
            vp.PanFrequency((p.Y - _lastPointer.Y) / plot.Height * vp.FreqSpanN);
            _lastPointer = p;
        }
        else InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _cursor = null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_panning) return;
        _panning = false;
        e.Pointer.Capture(null);
    }
}
