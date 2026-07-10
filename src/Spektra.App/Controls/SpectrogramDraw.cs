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
    public const double LogMinHz = 20.0;
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#9A9A9A"));
    private static readonly Typeface Font = new("Segoe UI, Inter, sans-serif");

    // Dual-stroke cursor line: a dark underlay with a light core stays visible
    // over both the black background and bright colormap content.
    public static readonly IBrush CursorUnderlayBrush = new SolidColorBrush(Color.FromArgb(0x8C, 0x00, 0x00, 0x00));
    public static readonly IBrush CursorCoreBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    public static readonly IPen CursorUnderlayPen = new Pen(CursorUnderlayBrush, 3);
    public static readonly IPen CursorLinePen = new Pen(CursorCoreBrush, 1);

    public static Rect PlotRect(Rect bounds) => new(
        RulerLeft, Pad,
        Math.Max(1, bounds.Width - RulerLeft - LegendWidth - Pad),
        Math.Max(1, bounds.Height - RulerBottom - 2 * Pad));

    // Lowest normalized frequency shown on a log axis (keeps log() finite).
    private static double LogFloor(double f1, double nyquist) =>
        Math.Min(nyquist > 0 ? LogMinHz / nyquist : 0.001, f1 * 0.5);

    /// Normalized vertical position (0 = bottom/low, 1 = top/high) for a Nyquist
    /// fraction q within the visible [f0,f1] window. Linear or logarithmic.
    public static double FreqPos(double q, double f0, double f1, bool log, double nyquist)
    {
        if (f1 <= f0) return 0;
        if (!log) return (q - f0) / (f1 - f0);
        var qmin = LogFloor(f1, nyquist);
        var a = Math.Log(Math.Max(f0, qmin));
        var b = Math.Log(Math.Max(f1, qmin * 1.0001));
        return (Math.Log(Math.Max(q, qmin)) - a) / (b - a);
    }

    /// Inverse of FreqPos: the Nyquist fraction at vertical position p (0 = bottom).
    public static double PosToFreq(double p, double f0, double f1, bool log, double nyquist)
    {
        if (!log) return f0 + p * (f1 - f0);
        var qmin = LogFloor(f1, nyquist);
        var a = Math.Log(Math.Max(f0, qmin));
        var b = Math.Log(Math.Max(f1, qmin * 1.0001));
        return Math.Exp(a + p * (b - a));
    }

    public static void FrequencyRuler(DrawingContext ctx, Rect plot, double sampleRate, double f0, double f1, bool log = false)
    {
        var nyquist = sampleRate / 2.0;
        if (nyquist <= 0) return;
        if (log)
        {
            foreach (var hz in new[] { 20.0, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 })
            {
                var q = hz / nyquist;
                if (q < f0 || q > f1) continue;
                var y = plot.Bottom - FreqPos(q, f0, f1, true, nyquist) * plot.Height;
                ctx.DrawLine(new Pen(TextBrush, 1), new Point(plot.Left - 4, y), new Point(plot.Left, y));
                var label = hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";
                Text(ctx, label, plot.Left - 8, y - 7, alignRight: true);
            }
            return;
        }
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

    // Vivid, cool-vs-warm split so the lane never blends into the warm
    // Magma/Inferno palettes right above it: cyan = informational silent gap,
    // saturated red = damage (missing tail).
    private static readonly IBrush DropoutBrush = new SolidColorBrush(Color.Parse("#4DD0E1"));
    private static readonly IBrush MissingTailBrush = new SolidColorBrush(Color.Parse("#FF5252"));

    /// 4 px integrity lane sitting on the time axis: cyan = silent gap
    /// (informational), red = truncated tail (damage). Call BEFORE TimeRuler
    /// so tick marks stay visible on top. Segments narrower than 2 px are
    /// widened so a tiny gap stays visible zoomed out.
    public static void IntegrityLane(DrawingContext ctx, Rect plot, IReadOnlyList<StripSegment> segments)
    {
        foreach (var s in segments)
        {
            var x = plot.Left + s.X0 * plot.Width;
            var w = Math.Max(2, (s.X1 - s.X0) * plot.Width);
            if (x + w > plot.Right) x = plot.Right - w;
            ctx.FillRectangle(s.MissingTail ? MissingTailBrush : DropoutBrush,
                new Rect(x, plot.Bottom + 1, w, 4));
        }
    }

    private static string FormatTick(double seconds, double step)
    {
        var basic = AudioMetadata.FormatDuration(TimeSpan.FromSeconds(Math.Floor(seconds)));
        if (step >= 1) return basic;
        var tenths = (int)(Math.Round(seconds * 10) % 10);
        return $"{basic}.{tenths}";
    }

    public static void Legend(DrawingContext ctx, Rect plot, DisplaySettings display)
    {
        var x = plot.Right + 10;
        const double w = 14;
        const int steps = 64;
        float floor = display.DbFloor, ceil = display.DbCeil;
        for (var i = 0; i < steps; i++)
        {
            var db = floor + (ceil - floor) * (steps - 1 - i) / (steps - 1);
            var brush = new SolidColorBrush(Color.FromUInt32(Colormaps.ToBgra(display.Palette, db, floor, ceil)));
            var h = plot.Height / steps;
            ctx.FillRectangle(brush, new Rect(x, plot.Top + i * h, w, h + 1));
        }
        for (var db = (int)ceil; db >= floor; db -= 30)
        {
            var y = plot.Top + (ceil - db) / (ceil - floor) * plot.Height;
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
