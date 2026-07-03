using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Globalization;
using System.Runtime.InteropServices;
using Spektra.Core;

namespace Spektra.App.Controls;

public sealed class SpectrogramView : Control
{
    private const double RulerLeft = 46, RulerBottom = 22, LegendWidth = 64, Pad = 8;
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#9A9A9A"));
    private static readonly Typeface Font = new("Segoe UI, Inter, sans-serif");

    private DocumentViewModel? _vm;
    private SpectrogramDocument? _doc;
    private WriteableBitmap? _bitmap;
    private int _writtenColumns;
    private bool _panning;
    private Point _lastPointer;

    public void Attach(DocumentViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.DocumentChanged -= OnDocumentChanged;
            _vm.DocumentUpdated -= OnDocumentUpdated;
            _vm.Viewport.Changed -= OnViewportChanged;
        }
        _vm = vm;
        if (vm is not null)
        {
            vm.DocumentChanged += OnDocumentChanged;
            vm.DocumentUpdated += OnDocumentUpdated;
            vm.Viewport.Changed += OnViewportChanged;
        }
        RebuildOverviewBitmap();
        InvalidateVisual();
    }

    private void OnViewportChanged() => InvalidateVisual();

    private void OnDocumentChanged()
    {
        RebuildOverviewBitmap();
        InvalidateVisual();
    }

    private void OnDocumentUpdated()
    {
        if (_doc is null || _bitmap is null) return;
        var count = Math.Min(_doc.Count, _bitmap.PixelSize.Width);
        if (count > _writtenColumns)
        {
            WriteColumns(_writtenColumns, count);
            _writtenColumns = count;
        }
        InvalidateVisual();
    }

    private void RebuildOverviewBitmap()
    {
        _doc = _vm?.Document;
        _bitmap?.Dispose();
        _bitmap = null;
        _writtenColumns = 0;
        if (_doc is not null)
        {
            _bitmap = new WriteableBitmap(
                new PixelSize(_doc.EstimatedColumns, _doc.Bins),
                new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            ClearBitmap();
            OnDocumentUpdated(); // tab switch: replay columns analyzed while detached
        }
    }

    private void ClearBitmap()
    {
        using var fb = _bitmap!.Lock();
        var black = unchecked((int)Palette.ToBgra(Db.Floor));
        var px = fb.Size.Width * fb.Size.Height;
        for (var i = 0; i < px; i++)
            Marshal.WriteInt32(fb.Address + i * 4, black);
    }

    private void WriteColumns(int from, int to)
    {
        var doc = _doc!;
        using var fb = _bitmap!.Lock();
        for (var col = from; col < to; col++)
        {
            var data = doc.GetColumn(col);
            for (var bin = 0; bin < doc.Bins; bin++)
            {
                var row = doc.Bins - 1 - bin; // freq 0 at bottom
                var addr = fb.Address + row * fb.RowBytes + col * 4;
                Marshal.WriteInt32(addr, unchecked((int)Palette.ToBgra(data[bin])));
            }
        }
    }

    private static Rect PlotRect(Rect bounds) => new(
        RulerLeft, Pad,
        Math.Max(1, bounds.Width - RulerLeft - LegendWidth - Pad),
        Math.Max(1, bounds.Height - RulerBottom - 2 * Pad));

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var plot = PlotRect(Bounds);
        var vp = _vm?.Viewport;

        if (_doc is not null && vp is not null && _bitmap is not null && _writtenColumns > 0)
        {
            // Unzoomed: written columns stretch across the full plot so the
            // image fills progressively during analysis. Zoomed: map the
            // normalized viewport onto the written columns (during analysis
            // this treats written columns as the whole file; it self-corrects
            // as columns arrive, and the sharp tile replaces it anyway).
            var src = !vp.IsZoomed
                ? new Rect(0, 0, _writtenColumns, _doc.Bins)
                : new Rect(
                    vp.T0 * _writtenColumns, (1 - vp.F1) * _doc.Bins,
                    Math.Max(1, vp.TimeSpanN * _writtenColumns),
                    Math.Max(1, vp.FreqSpanN * _doc.Bins));
            ctx.DrawImage(_bitmap, src, plot);
        }

        if (_doc is not null && vp is not null)
        {
            DrawFrequencyRuler(ctx, plot, vp);
            DrawTimeRuler(ctx, plot, vp);
        }
        DrawLegend(ctx, plot);
    }

    private void DrawFrequencyRuler(DrawingContext ctx, Rect plot, Viewport vp)
    {
        var nyquist = _doc!.Metadata.SampleRate / 2.0;
        if (nyquist <= 0) return;
        var lo = vp.F0 * nyquist;
        var hi = vp.F1 * nyquist;
        var step = new[] { 50.0, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 }
            .First(s => (hi - lo) / s <= 8);
        for (var i = (long)Math.Ceiling(lo / step - 1e-9); i * step <= hi + 1e-9; i++)
        {
            var hz = i * step;
            var y = plot.Bottom - (hz - lo) / (hi - lo) * plot.Height;
            ctx.DrawLine(new Pen(TextBrush, 1), new Point(plot.Left - 4, y), new Point(plot.Left, y));
            var label = hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";
            DrawText(ctx, label, plot.Left - 8, y - 7, alignRight: true);
        }
    }

    private void DrawTimeRuler(DrawingContext ctx, Rect plot, Viewport vp)
    {
        var dur = _doc!.Metadata.Duration.TotalSeconds;
        if (dur <= 0) return;
        var lo = vp.T0 * dur;
        var hi = vp.T1 * dur;
        var step = new[] { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600, 86400 }
            .First(s => (hi - lo) / s <= 10);
        for (var i = (long)Math.Ceiling(lo / step - 1e-9); i * step <= hi + 1e-9; i++)
        {
            var t = i * step;
            var x = plot.Left + (t - lo) / (hi - lo) * plot.Width;
            ctx.DrawLine(new Pen(TextBrush, 1), new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            DrawText(ctx, FormatTick(t, step), x + 2, plot.Bottom + 5);
        }
    }

    private static string FormatTick(double seconds, double step)
    {
        var basic = AudioMetadata.FormatDuration(TimeSpan.FromSeconds(Math.Floor(seconds)));
        if (step >= 1) return basic;
        var tenths = (int)(Math.Round(seconds * 10) % 10);
        return $"{basic}.{tenths}";
    }

    private void DrawLegend(DrawingContext ctx, Rect plot)
    {
        var x = plot.Right + 10;
        const double w = 14;
        const int steps = 64;
        for (var i = 0; i < steps; i++)
        {
            var db = Db.Floor + (0f - Db.Floor) * (steps - 1 - i) / (steps - 1);
            var brush = new SolidColorBrush(Color.FromUInt32(Palette.ToBgra(db)));
            var h = plot.Height / steps;
            ctx.FillRectangle(brush, new Rect(x, plot.Top + i * h, w, h + 1));
        }
        for (var db = 0; db >= Db.Floor; db -= 30)
        {
            var y = plot.Top + (0 - db) / (0 - (double)Db.Floor) * plot.Height;
            DrawText(ctx, $"{db}", x + w + 4, y - 7);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm?.Document is null) return;
        var plot = PlotRect(Bounds);
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
        var plot = PlotRect(Bounds);
        var p = e.GetPosition(this);
        if (!plot.Contains(p)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            _vm.Viewport.Reset();
            e.Handled = true;
            return;
        }
        _panning = true;
        _lastPointer = p;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_panning || _vm is null) return;
        var plot = PlotRect(Bounds);
        var p = e.GetPosition(this);
        var vp = _vm.Viewport;
        vp.PanTime(-(p.X - _lastPointer.X) / plot.Width * vp.TimeSpanN);
        vp.PanFrequency((p.Y - _lastPointer.Y) / plot.Height * vp.FreqSpanN);
        _lastPointer = p;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_panning) return;
        _panning = false;
        e.Pointer.Capture(null);
    }

    private static void DrawText(DrawingContext ctx, string text, double x, double y, bool alignRight = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Font, 11, TextBrush);
        ctx.DrawText(ft, new Point(alignRight ? x - ft.Width : x, y));
    }
}
