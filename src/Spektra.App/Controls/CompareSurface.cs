using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Globalization;
using System.Runtime.InteropServices;
using Spektra.Core;

namespace Spektra.App.Controls;

public sealed class CompareSurface : Control
{
    private ComparisonViewModel? _vm;
    private readonly PaneRenderer _a = new();
    private readonly PaneRenderer _b = new();
    private WriteableBitmap? _diff;
    private readonly DispatcherTimer _tileTimer;
    private readonly DispatcherTimer _diffTimer;
    private readonly DispatcherTimer _bTileTimer;
    private readonly DispatcherTimer _spinTimer;
    private int _spinPhase;
    private bool _panning;
    private Point _lastPointer;
    private Point? _cursor;
    private DisplaySettings _display = new();

    /// Colormap + dB range for the A/B panes and their legends (the signed diff
    /// keeps its own diverging map). Re-colors both panes and repaints.
    public void SetDisplay(DisplaySettings display)
    {
        _display = display;
        _a.SetDisplay(display);
        _b.SetDisplay(display);
        InvalidateVisual();
    }

    public CompareSurface()
    {
        _tileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tileTimer.Tick += (_, _) => { _tileTimer.Stop(); RenderTiles(); };
        _diffTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _diffTimer.Tick += (_, _) =>
        {
            _diffTimer.Stop();
            if (_vm?.Mode == CompareMode.Diff) _ = _vm.ComputeDiffAsync(Cols());
        };
        _bTileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _bTileTimer.Tick += (_, _) =>
        {
            _bTileTimer.Stop();
            if (_vm?.Viewport.IsTimeZoomed == true) _ = _vm.B.RenderTileAsync(Cols());
        };
        _spinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _spinTimer.Tick += (_, _) => { _spinPhase++; InvalidateVisual(); };
        // Crisp, cell-accurate spectrograms/diff — no bilinear halo ("bloom").
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    private int Cols() => Math.Clamp((int)SpectrogramDraw.PlotRect(Bounds).Width, 64, 4096);

    public void Attach(ComparisonViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.A.DocumentChanged -= OnAChanged; _vm.A.DocumentUpdated -= OnAUpdated; _vm.A.TileChanged -= OnATile;
            _vm.B.DocumentChanged -= OnBChanged; _vm.B.DocumentUpdated -= OnBUpdated; _vm.B.TileChanged -= OnBTile;
            _vm.Viewport.Changed -= OnViewport;
            _vm.Changed -= OnVmChanged; _vm.DiffChanged -= OnDiffReady; _vm.DiffRequested -= OnDiffRequested;
            _vm.BusyChanged -= OnBusyChanged; _vm.OffsetChanged -= OnOffsetChanged;
        }
        _vm = vm;
        if (vm is not null)
        {
            vm.A.DocumentChanged += OnAChanged; vm.A.DocumentUpdated += OnAUpdated; vm.A.TileChanged += OnATile;
            vm.B.DocumentChanged += OnBChanged; vm.B.DocumentUpdated += OnBUpdated; vm.B.TileChanged += OnBTile;
            vm.Viewport.Changed += OnViewport;
            vm.Changed += OnVmChanged; vm.DiffChanged += OnDiffReady; vm.DiffRequested += OnDiffRequested;
            vm.BusyChanged += OnBusyChanged; vm.OffsetChanged += OnOffsetChanged;
            vm.TargetColumns = Cols();
            _a.SetDocument(vm.A.Document); _a.SetTile(vm.A.Tile);
            _b.SetDocument(vm.B.Document); _b.SetTile(vm.B.Tile);
        }
        OnBusyChanged();
        InvalidateVisual();
    }

    private void OnAChanged() { _a.SetDocument(_vm?.A.Document); _a.SetTile(_vm?.A.Tile); ScheduleTiles(); InvalidateVisual(); }
    private void OnAUpdated() { _a.SyncColumns(); InvalidateVisual(); }
    private void OnATile() { _a.SetTile(_vm?.A.Tile); InvalidateVisual(); }
    private void OnBChanged() { _b.SetDocument(_vm?.B.Document); _b.SetTile(_vm?.B.Tile); ScheduleTiles(); InvalidateVisual(); }
    private void OnBUpdated() { _b.SyncColumns(); InvalidateVisual(); }
    private void OnBTile() { _b.SetTile(_vm?.B.Tile); InvalidateVisual(); }
    private void OnViewport() { if (_vm is not null) _vm.TargetColumns = Cols(); InvalidateVisual(); ScheduleTiles(); }
    private void OnVmChanged() => InvalidateVisual();
    private void OnDiffReady() { RebuildDiff(); InvalidateVisual(); }
    private void OnDiffRequested() { _diffTimer.Stop(); _diffTimer.Start(); }

    private void OnOffsetChanged()
    {
        // The shifted overview is the instant preview; the sharp B tile was
        // decoded for the old offset, so drop it and re-decode B debounced.
        _b.SetTile(null);
        _bTileTimer.Stop();
        _bTileTimer.Start();
        InvalidateVisual();
    }

    private void OnBusyChanged()
    {
        if (_vm?.IsBusy == true) { if (!_spinTimer.IsEnabled) _spinTimer.Start(); }
        else _spinTimer.Stop();
        InvalidateVisual();
    }

    private void ScheduleTiles()
    {
        _tileTimer.Stop();
        if (_vm?.Viewport.IsTimeZoomed == true) _tileTimer.Start();
    }

    private void RenderTiles()
    {
        if (_vm?.Viewport.IsTimeZoomed != true) return;
        var cols = Cols();
        _ = _vm.A.RenderTileAsync(cols);
        _ = _vm.B.RenderTileAsync(cols);
    }

    // B pane mapping: shared time [t0,t1] over A's duration → B's own columns.
    private (double b0, double b1) BPaneRange(double t0, double t1)
    {
        if (_vm?.A.Metadata is not { } ma || _vm.B.Metadata is not { } mb || mb.Duration.TotalSeconds <= 0)
            return (t0, t1);
        var refDur = ma.Duration.TotalSeconds;
        var off = _vm.OffsetSeconds;
        return ((t0 * refDur + off) / mb.Duration.TotalSeconds,
                (t1 * refDur + off) / mb.Duration.TotalSeconds);
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (_vm is null) return;
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var vp = _vm.Viewport;
        var srA = _vm.A.Metadata?.SampleRate ?? 0;
        var srB = _vm.B.Metadata?.SampleRate ?? 0;
        var durA = _vm.A.Metadata?.Duration.TotalSeconds ?? 0;
        // Shared frequency axis: both panes (and the diff) are plotted against a
        // common Nyquist (the lower of the two sample rates), so a given Hz sits
        // at the same pixel height everywhere and toggling A/B never shifts a
        // feature vertically. A higher-rate pane is clipped to that ceiling; when
        // only one file is loaded we fall back to whichever rate is present.
        var commonRate = srA > 0 && srB > 0 ? Math.Min(srA, srB) : Math.Max(srA, srB);
        // Map the shared-axis [F0,F1] window into a single pane's own bins.
        double f0For(int sr) => sr > 0 ? vp.F0 * commonRate / sr : vp.F0;
        double f1For(int sr) => sr > 0 ? vp.F1 * commonRate / sr : vp.F1;

        switch (_vm.Mode)
        {
            case CompareMode.A:
                _a.DrawInto(ctx, plot, vp.T0, vp.T1, f0For(srA), f1For(srA), vp.T0, vp.T1);
                SpectrogramDraw.FrequencyRuler(ctx, plot, commonRate, vp.F0, vp.F1);
                SpectrogramDraw.TimeRuler(ctx, plot, durA, vp.T0, vp.T1);
                SpectrogramDraw.Legend(ctx, plot, _display);
                DrawPaneError(ctx, plot, _vm.A);
                break;
            case CompareMode.B:
            {
                var (b0, b1) = BPaneRange(vp.T0, vp.T1);
                _b.DrawInto(ctx, plot, b0, b1, f0For(srB), f1For(srB), vp.T0, vp.T1);
                SpectrogramDraw.FrequencyRuler(ctx, plot, commonRate, vp.F0, vp.F1);
                SpectrogramDraw.TimeRuler(ctx, plot, durA, vp.T0, vp.T1);
                SpectrogramDraw.Legend(ctx, plot, _display);
                DrawPaneError(ctx, plot, _vm.B);
                break;
            }
            case CompareMode.Diff:
                RenderDiff(ctx, plot, vp);
                break;
            default: // Both
            {
                const double gap = 8;
                var half = (plot.Height - gap) / 2;
                var top = new Rect(plot.X, plot.Y, plot.Width, half);
                var bot = new Rect(plot.X, plot.Y + half + gap, plot.Width, half);
                _a.DrawInto(ctx, top, vp.T0, vp.T1, f0For(srA), f1For(srA), vp.T0, vp.T1);
                var (b0, b1) = BPaneRange(vp.T0, vp.T1);
                _b.DrawInto(ctx, bot, b0, b1, f0For(srB), f1For(srB), vp.T0, vp.T1);
                SpectrogramDraw.FrequencyRuler(ctx, top, commonRate, vp.F0, vp.F1);
                SpectrogramDraw.FrequencyRuler(ctx, bot, commonRate, vp.F0, vp.F1);
                SpectrogramDraw.TimeRuler(ctx, bot, durA, vp.T0, vp.T1); // one shared ruler under B
                SpectrogramDraw.Legend(ctx, plot, _display);
                SpectrogramDraw.Text(ctx, "A", top.X + 4, top.Y + 2);
                SpectrogramDraw.Text(ctx, "B", bot.X + 4, bot.Y + 2);
                DrawPaneError(ctx, top, _vm.A);
                DrawPaneError(ctx, bot, _vm.B);
                break;
            }
        }

        DrawCursorLine(ctx, plot);

        if (_vm.BusyText is { } busy) DrawBusyBadge(ctx, plot, busy);
    }

    private static readonly Typeface BusyFont = new("Segoe UI, Inter, sans-serif");
    private static readonly IPen TickUnderlayPen = new Pen(SpectrogramDraw.CursorUnderlayBrush, 5);
    private static readonly IPen TickPen = new Pen(SpectrogramDraw.CursorCoreBrush, 3);
    private static readonly IBrush LabelBg = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
    private static readonly IBrush LabelFg = new SolidColorBrush(Color.FromRgb(230, 230, 230));

    // Centered "processing" pill with an animated spinner, painted while a
    // long-running compare op (align / diff) is in flight.
    private void DrawBusyBadge(DrawingContext ctx, Rect plot, string text)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            BusyFont, 13, Brushes.White);
        const double padX = 16, padY = 11, spinner = 16, gap = 11;
        var w = padX + spinner + gap + ft.Width + padX;
        var h = Math.Max(spinner, ft.Height) + 2 * padY;
        var cx = plot.X + plot.Width / 2;
        var cy = plot.Y + plot.Height / 2;
        var badge = new Rect(cx - w / 2, cy - h / 2, w, h);

        ctx.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(0xDA, 0x1C, 0x1C, 0x1C)),
            new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF))),
            badge, 9, 9);

        DrawSpinner(ctx, badge.X + padX + spinner / 2, cy, spinner / 2);
        ctx.DrawText(ft, new Point(badge.X + padX + spinner + gap, cy - ft.Height / 2));
    }

    private void DrawSpinner(DrawingContext ctx, double cx, double cy, double radius)
    {
        const int spokes = 8;
        var head = _spinPhase % spokes;
        for (var i = 0; i < spokes; i++)
        {
            var ang = i * (2 * Math.PI / spokes) - Math.PI / 2;
            var behind = (head - i + spokes) % spokes;      // 0 = brightest, trailing off
            var opacity = 0.15 + 0.85 * (1 - behind / (double)spokes);
            var pen = new Pen(new SolidColorBrush(Colors.White, opacity), 2)
            {
                LineCap = PenLineCap.Round,
            };
            ctx.DrawLine(pen,
                new Point(cx + Math.Cos(ang) * radius * 0.5, cy + Math.Sin(ang) * radius * 0.5),
                new Point(cx + Math.Cos(ang) * radius, cy + Math.Sin(ang) * radius));
        }
    }

    private void RebuildDiff()
    {
        _diff?.Dispose();
        _diff = null;
        if (_vm?.Diff is not { Count: > 0 } cols) return;
        var bins = cols[0].Length;
        _diff = new WriteableBitmap(new PixelSize(cols.Count, bins),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = _diff.Lock();
        for (var col = 0; col < cols.Count; col++)
        {
            var data = cols[col];
            for (var bin = 0; bin < bins; bin++)
            {
                var row = bins - 1 - bin;
                Marshal.WriteInt32(fb.Address + row * fb.RowBytes + col * 4,
                    unchecked((int)DivergingPalette.ToBgra(data[bin], ComparisonViewModel.DiffClamp)));
            }
        }
    }

    private static void DrawPaneError(DrawingContext ctx, Rect rect, DocumentViewModel dvm)
    {
        if (dvm.Document is not null || dvm.ErrorText is not { } err) return;
        var msg = err.Split('\n')[0];
        if (msg.Length > 90) msg = msg[..90] + "…";
        SpectrogramDraw.Text(ctx, "⚠ " + msg, rect.X + 12, rect.Y + rect.Height / 2 - 6);
    }

    // Vertical cursor line through the pane(s), with a thicker tick and a time
    // label on the shared ruler: lets a feature in A be matched to the same
    // instant in B. Toggled by View > Crosshair. In Both mode the panes carve
    // up the plot rect, so one full-height line spans A, the gap, and B.
    private void DrawCursorLine(DrawingContext ctx, Rect plot)
    {
        if (!_display.ShowCrosshair || _cursor is not { } p || _vm is null) return;
        if (!plot.Contains(p)) return;

        ctx.DrawLine(SpectrogramDraw.CursorUnderlayPen, new Point(p.X, plot.Top), new Point(p.X, plot.Bottom));
        ctx.DrawLine(SpectrogramDraw.CursorLinePen, new Point(p.X, plot.Top), new Point(p.X, plot.Bottom));

        var tickTop = new Point(p.X, plot.Bottom);
        var tickBottom = new Point(p.X, plot.Bottom + 10);
        ctx.DrawLine(TickUnderlayPen, tickTop, tickBottom);
        ctx.DrawLine(TickPen, tickTop, tickBottom);

        var durA = _vm.A.Metadata?.Duration.TotalSeconds ?? 0;
        if (durA <= 0) return;
        var vp = _vm.Viewport;
        var seconds = Math.Max(0, (vp.T0 + (p.X - plot.Left) / plot.Width * (vp.T1 - vp.T0)) * durA);
        var label = AudioMetadata.FormatDuration(TimeSpan.FromSeconds(seconds))
                    + "." + (int)(seconds * 10 % 10);
        var ft = new FormattedText(label, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, BusyFont, 11, LabelFg);
        var boxW = ft.Width + 10;
        var boxH = ft.Height + 4;
        if (plot.Width <= boxW) return; // degenerate window: skip the label
        var bx = Math.Clamp(p.X - boxW / 2, plot.Left, plot.Right - boxW);
        var by = plot.Bottom + 11;
        ctx.FillRectangle(LabelBg, new Rect(bx, by, boxW, boxH), 3);
        ctx.DrawText(ft, new Point(bx + 5, by + 2));
    }

    private void RenderDiff(DrawingContext ctx, Rect plot, Viewport vp)
    {
        var srA = _vm!.A.Metadata?.SampleRate ?? 0;
        var srB = _vm.B.Metadata?.SampleRate ?? 0;
        // Diff bins already span the common Nyquist (SpectralDiff resamples to the
        // lower rate), so the ruler uses that same shared rate.
        var commonRate = srA > 0 && srB > 0 ? Math.Min(srA, srB) : Math.Max(srA, srB);
        var durA = _vm.A.Metadata?.Duration.TotalSeconds ?? 0;
        if (_diff is not null && _vm.Diff is not null &&
            Math.Abs(_vm.DiffT0 - vp.T0) < 1e-9 && Math.Abs(_vm.DiffT1 - vp.T1) < 1e-9)
        {
            var src = new Rect(0, (1 - vp.F1) * _diff.PixelSize.Height,
                _diff.PixelSize.Width, Math.Max(1, vp.FreqSpanN * _diff.PixelSize.Height));
            ctx.DrawImage(_diff, src, plot);
        }
        SpectrogramDraw.FrequencyRuler(ctx, plot, commonRate, vp.F0, vp.F1);
        SpectrogramDraw.TimeRuler(ctx, plot, durA, vp.T0, vp.T1);
        SpectrogramDraw.DivergingLegend(ctx, plot, ComparisonViewModel.DiffClamp);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm is null) return;
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
        if (_vm is null) return;
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
