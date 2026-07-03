using Avalonia;
using Avalonia.Media;
using System.Globalization;
using Spektra.Core;

namespace Spektra.App.Controls;

/// Shared static drawing helpers for spectrogram surfaces (rulers, legends,
/// plot geometry). No per-instance state.
static class SpectrogramDraw
{
    public const double RulerLeft = 46, RulerBottom = 22, LegendWidth = 64, Pad = 8;
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#9A9A9A"));
    private static readonly Typeface Font = new("Segoe UI, Inter, sans-serif");

    public static Rect PlotRect(Rect bounds) => new(
        RulerLeft, Pad,
        Math.Max(1, bounds.Width - RulerLeft - LegendWidth - Pad),
        Math.Max(1, bounds.Height - RulerBottom - 2 * Pad));

    public static void FrequencyRuler(DrawingContext ctx, Rect plot, double sampleRate, double f0, double f1)
    {
        var nyquist = sampleRate / 2.0;
        if (nyquist <= 0) return;
        var lo = f0 * nyquist;
        var hi = f1 * nyquist;
        var step = new[] { 50.0, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 }
            .First(s => (hi - lo) / s <= 8);
        for (var i = (long)Math.Ceiling(lo / step - 1e-9); i * step <= hi + 1e-9; i++)
        {
            var hz = i * step;
            var y = plot.Bottom - (hz - lo) / (hi - lo) * plot.Height;
            ctx.DrawLine(new Pen(TextBrush, 1), new Point(plot.Left - 4, y), new Point(plot.Left, y));
            var label = hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";
            Text(ctx, label, plot.Left - 8, y - 7, alignRight: true);
        }
    }

    public static void TimeRuler(DrawingContext ctx, Rect plot, double durationSeconds, double t0, double t1)
    {
        if (durationSeconds <= 0) return;
        var lo = t0 * durationSeconds;
        var hi = t1 * durationSeconds;
        var step = new[] { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600, 86400 }
            .First(s => (hi - lo) / s <= 10);
        for (var i = (long)Math.Ceiling(lo / step - 1e-9); i * step <= hi + 1e-9; i++)
        {
            var t = i * step;
            var x = plot.Left + (t - lo) / (hi - lo) * plot.Width;
            ctx.DrawLine(new Pen(TextBrush, 1), new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            Text(ctx, FormatTick(t, step), x + 2, plot.Bottom + 5);
        }
    }

    private static string FormatTick(double seconds, double step)
    {
        var basic = AudioMetadata.FormatDuration(TimeSpan.FromSeconds(Math.Floor(seconds)));
        if (step >= 1) return basic;
        var tenths = (int)(Math.Round(seconds * 10) % 10);
        return $"{basic}.{tenths}";
    }

    public static void Legend(DrawingContext ctx, Rect plot)
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
            Text(ctx, $"{db}", x + w + 4, y - 7);
        }
    }

    public static void DivergingLegend(DrawingContext ctx, Rect plot, float clamp)
    {
        var x = plot.Right + 10;
        const double w = 14;
        const int steps = 64;
        for (var i = 0; i < steps; i++)
        {
            var diff = clamp - 2 * clamp * i / (steps - 1); // +clamp at top → −clamp at bottom
            var brush = new SolidColorBrush(Color.FromUInt32(DivergingPalette.ToBgra(diff, clamp)));
            var h = plot.Height / steps;
            ctx.FillRectangle(brush, new Rect(x, plot.Top + i * h, w, h + 1));
        }
        foreach (var db in new[] { clamp, 0f, -clamp })
        {
            var y = plot.Top + (clamp - db) / (2 * clamp) * plot.Height;
            Text(ctx, $"{db:+0;-0;0}", x + w + 4, y - 7);
        }
    }

    public static void Text(DrawingContext ctx, string text, double x, double y, bool alignRight = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Font, 11, TextBrush);
        ctx.DrawText(ft, new Point(alignRight ? x - ft.Width : x, y));
    }
}
