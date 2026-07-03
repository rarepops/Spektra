using Avalonia;
using Avalonia.Controls;
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

    public void Attach(DocumentViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.DocumentChanged -= OnDocumentChanged;
            _vm.DocumentUpdated -= OnDocumentUpdated;
        }
        _vm = vm;
        if (vm is not null)
        {
            vm.DocumentChanged += OnDocumentChanged;
            vm.DocumentUpdated += OnDocumentUpdated;
        }
        RebuildOverviewBitmap();
        InvalidateVisual();
    }

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

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        ctx.FillRectangle(Brushes.Black, new Rect(bounds.Size));
        var plot = new Rect(
            RulerLeft, Pad,
            Math.Max(1, bounds.Width - RulerLeft - LegendWidth - Pad),
            Math.Max(1, bounds.Height - RulerBottom - 2 * Pad));

        if (_doc is not null && _bitmap is not null && _writtenColumns > 0)
        {
            var src = new Rect(0, 0, _writtenColumns, _doc.Bins);
            ctx.DrawImage(_bitmap, src, plot);
        }

        if (_doc is not null)
        {
            DrawFrequencyRuler(ctx, plot);
            DrawTimeRuler(ctx, plot);
        }
        DrawLegend(ctx, plot);
    }

    private void DrawFrequencyRuler(DrawingContext ctx, Rect plot)
    {
        var nyquist = _doc!.Metadata.SampleRate / 2.0;
        if (nyquist <= 0) return;
        var stepKhz = new[] { 1, 2, 5, 10, 20 }.First(s => nyquist / 1000.0 / s <= 8);
        for (var khz = 0.0; khz * 1000 <= nyquist; khz += stepKhz)
        {
            var y = plot.Bottom - khz * 1000 / nyquist * plot.Height;
            ctx.DrawLine(new Pen(TextBrush, 1), new Point(plot.Left - 4, y), new Point(plot.Left, y));
            DrawText(ctx, $"{khz:0}k", plot.Left - 8, y - 7, alignRight: true);
        }
    }

    private void DrawTimeRuler(DrawingContext ctx, Rect plot)
    {
        var dur = _doc!.Metadata.Duration.TotalSeconds;
        if (dur <= 0) return;
        var step = new[] { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800 }.First(s => dur / s <= 10);
        for (var t = 0.0; t <= dur; t += step)
        {
            var x = plot.Left + t / dur * plot.Width;
            ctx.DrawLine(new Pen(TextBrush, 1), new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            DrawText(ctx, AudioMetadata.FormatDuration(TimeSpan.FromSeconds(t)), x + 2, plot.Bottom + 5);
        }
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

    private static void DrawText(DrawingContext ctx, string text, double x, double y, bool alignRight = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Font, 11, TextBrush);
        ctx.DrawText(ft, new Point(alignRight ? x - ft.Width : x, y));
    }
}
