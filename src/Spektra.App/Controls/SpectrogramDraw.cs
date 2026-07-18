using Avalonia;
using Avalonia.Media;
using System.Globalization;
using Spektra.Core;

namespace Spektra.App.Controls;

/// Shared static drawing helpers for spectrogram surfaces (rulers, legends,
/// plot geometry). No per-instance state; the legend and text caches below are
/// static and touched only on the render (UI) thread.
static class SpectrogramDraw
{
    // RulerLeft/RulerBottom reserve room for the tick labels AND the axis
    // titles ("Frequency (kHz)" up the left edge, "Time" under the bottom).
    public const double RulerLeft = 62, RulerBottom = 38, LegendWidth = 64, Pad = 8;
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#9A9A9A"));
    private static readonly Typeface Font = new("Segoe UI, Inter, sans-serif");

    // Dual-stroke cursor line: a dark underlay with a light core stays visible
    // over both the black background and bright colormap content.
    public static readonly IBrush CursorUnderlayBrush = new SolidColorBrush(Color.FromArgb(0x8C, 0x00, 0x00, 0x00));
    public static readonly IBrush CursorCoreBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    public static readonly IPen CursorUnderlayPen = new Pen(CursorUnderlayBrush, 3);
    public static readonly IPen CursorLinePen = new Pen(CursorCoreBrush, 1);

    // Hoisted out of the per-frame ruler loops: the tick pen and the tick-step
    // tables never change, so rebuilding them on every Render was pure garbage.
    private static readonly IPen TickPen = new Pen(TextBrush, 1);
    private static readonly double[] LogFreqTicks = [20.0, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000];
    private static readonly double[] LinFreqSteps = [50.0, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000];
    private static readonly double[] TimeSteps = [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600, 86400];

    // Minimum on-screen spacing between ticks. The tick budget is the axis
    // length divided by these, so a longer axis fills in more ticks and a
    // shorter one thins them out instead of using one fixed count everywhere.
    // Frequency is the roomiest so the vertical scale reads uncrowded.
    private const double TimeTickMinPx = 70, FreqTickMinPx = 50, LegendTickMinPx = 26;
    // "Nice" dB label steps for the legend, coarsest last (see PickStep).
    private static readonly double[] DbSteps = [10.0, 20, 30, 60, 120];

    public static Rect PlotRect(Rect bounds) => new(
        RulerLeft, Pad,
        Math.Max(1, bounds.Width - RulerLeft - LegendWidth - Pad),
        Math.Max(1, bounds.Height - RulerBottom - 2 * Pad));

    // First step whose tick count fits the budget, else the coarsest. A plain
    // loop, so no per-frame closure/enumerator like LinFreqSteps.First(lambda).
    private static double PickStep(double[] steps, double span, double maxTicks)
    {
        foreach (var s in steps)
            if (span / s <= maxTicks) return s;
        return steps[^1];
    }

    public static void FrequencyRuler(DrawingContext ctx, Rect plot, FreqAxis axis)
    {
        var nyquist = axis.Nyquist;
        if (nyquist <= 0) return;
        if (axis.Log)
        {
            // Fixed decade-ish positions; drop any that would crowd the one
            // below it, so a short axis thins out (ticks climb, y decreases).
            var lastY = double.PositiveInfinity;
            foreach (var hz in LogFreqTicks)
            {
                var q = hz / nyquist;
                if (q < axis.F0 || q > axis.F1) continue;
                var y = plot.Bottom - axis.PosOf(q) * plot.Height;
                if (lastY - y < FreqTickMinPx) continue;
                lastY = y;
                ctx.DrawLine(TickPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));
                var label = hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";
                Text(ctx, label, plot.Left - 8, y - 7, alignRight: true);
            }
            return;
        }
        var lo = axis.F0 * nyquist;
        var hi = axis.F1 * nyquist;
        var step = PickStep(LinFreqSteps, hi - lo, Math.Max(2, plot.Height / FreqTickMinPx));
        for (var i = (long)Math.Ceiling(lo / step - 1e-9); i * step <= hi + 1e-9; i++)
        {
            var hz = i * step;
            var y = plot.Bottom - (hz - lo) / (hi - lo) * plot.Height;
            ctx.DrawLine(TickPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));
            var label = hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";
            Text(ctx, label, plot.Left - 8, y - 7, alignRight: true);
        }
    }

    public static void TimeRuler(DrawingContext ctx, Rect plot, double durationSeconds, double t0, double t1)
    {
        if (durationSeconds <= 0) return;
        var lo = t0 * durationSeconds;
        var hi = t1 * durationSeconds;
        var step = PickStep(TimeSteps, hi - lo, Math.Max(2, plot.Width / TimeTickMinPx));
        for (var i = (long)Math.Ceiling(lo / step - 1e-9); i * step <= hi + 1e-9; i++)
        {
            var t = i * step;
            var x = plot.Left + (t - lo) / (hi - lo) * plot.Width;
            ctx.DrawLine(TickPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
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

    // The 64-step legend ramp depends only on the palette LUT and dB range, so
    // cache the brushes and rebuild only when those change; Render runs on every
    // repaint (pointer moves included), so rebuilding 64 brushes each frame was
    // pure garbage.
    private const int LegendSteps = 64;
    private static uint[]? _legendLut;
    private static float _legendFloor, _legendCeil;
    private static IBrush[]? _legendBrushes;

    private static IBrush[] LegendBrushes(DisplaySettings display)
    {
        float floor = display.DbFloor, ceil = display.DbCeil;
        if (_legendBrushes is not null && ReferenceEquals(_legendLut, display.Lut) &&
            _legendFloor == floor && _legendCeil == ceil)
            return _legendBrushes;
        var brushes = new IBrush[LegendSteps];
        for (var i = 0; i < LegendSteps; i++)
        {
            var db = floor + (ceil - floor) * (LegendSteps - 1 - i) / (LegendSteps - 1);
            brushes[i] = new SolidColorBrush(Color.FromUInt32(PaletteLut.Sample(display.Lut, db, floor, ceil)));
        }
        (_legendLut, _legendFloor, _legendCeil, _legendBrushes) = (display.Lut, floor, ceil, brushes);
        return brushes;
    }

    public static void Legend(DrawingContext ctx, Rect plot, DisplaySettings display)
    {
        var x = plot.Right + 10;
        const double w = 14;
        // Shorter than the plot and vertically centered, so it reads as a key
        // rather than a second frequency axis.
        var barTop = plot.Top + plot.Height * 0.15;
        var barH = Math.Max(1, plot.Height * 0.7);
        var brushes = LegendBrushes(display);
        var h = barH / LegendSteps;
        for (var i = 0; i < LegendSteps; i++)
            ctx.FillRectangle(brushes[i], new Rect(x, barTop + i * h, w, h + 1));
        float floor = display.DbFloor, ceil = display.DbCeil;
        var dbStep = (int)PickStep(DbSteps, ceil - floor, Math.Max(2, barH / LegendTickMinPx));
        for (var db = (int)ceil; db >= floor; db -= dbStep)
        {
            var y = barTop + (ceil - db) / (ceil - floor) * barH;
            Text(ctx, $"{db}", x + w + 4, y - 7);
        }
        // The unit these numbers are in: decibels relative to full scale.
        Text(ctx, "dBFS", x, barTop + barH + 6);
    }

    /// Single-file viewer axis titles: "Frequency (kHz)" up the left edge and
    /// "Time" centered under the bottom ruler. The compare surface keeps its
    /// denser stacked layout to the ticks alone and does not call this.
    public static void AxisTitles(DrawingContext ctx, Rect plot)
    {
        var time = GetText("Time");
        Text(ctx, "Time", plot.Left + (plot.Width - time.Width) / 2, plot.Bottom + 21);

        // Rotated -90° so it reads bottom-to-top; centered on the plot's left
        // edge, clear of the frequency tick labels.
        var freq = GetText("Frequency (kHz)");
        double cx = 13, cy = plot.Top + plot.Height / 2;
        using (ctx.PushTransform(
            Matrix.CreateTranslation(-freq.Width / 2, -freq.Height / 2)
            * Matrix.CreateRotation(-Math.PI / 2)
            * Matrix.CreateTranslation(cx, cy)))
            ctx.DrawText(freq, new Point(0, 0));
    }

    private static float _divClamp = float.NaN;
    private static IBrush[]? _divBrushes;

    // The diff ramp depends only on clamp (a constant), so it caches after the
    // first frame; the label span is stack-allocated to avoid a per-frame array.
    private static IBrush[] DivergingBrushes(float clamp)
    {
        if (_divBrushes is not null && _divClamp == clamp) return _divBrushes;
        var brushes = new IBrush[LegendSteps];
        for (var i = 0; i < LegendSteps; i++)
        {
            var diff = clamp - 2 * clamp * i / (LegendSteps - 1); // +clamp at top → −clamp at bottom
            brushes[i] = new SolidColorBrush(Color.FromUInt32(DivergingPalette.ToBgra(diff, clamp)));
        }
        (_divClamp, _divBrushes) = (clamp, brushes);
        return brushes;
    }

    public static void DivergingLegend(DrawingContext ctx, Rect plot, float clamp)
    {
        var x = plot.Right + 10;
        const double w = 14;
        var brushes = DivergingBrushes(clamp);
        var h = plot.Height / LegendSteps;
        for (var i = 0; i < LegendSteps; i++)
            ctx.FillRectangle(brushes[i], new Rect(x, plot.Top + i * h, w, h + 1));
        foreach (var db in stackalloc[] { clamp, 0f, -clamp })
        {
            var y = plot.Top + (clamp - db) / (2 * clamp) * plot.Height;
            Text(ctx, $"{db:+0;-0;0}", x + w + 4, y - 7);
        }
    }

    // Ruler and legend labels repeat every frame from a small, fixed string set,
    // so cache the FormattedText (heavier than a brush) by string. The cap guards
    // against unbounded growth from many distinct time labels over a session; in
    // practice the set is tiny, so it rarely trips.
    private static readonly Dictionary<string, FormattedText> TextCache = [];

    public static void Text(DrawingContext ctx, string text, double x, double y, bool alignRight = false)
    {
        var ft = GetText(text);
        ctx.DrawText(ft, new Point(alignRight ? x - ft.Width : x, y));
    }

    // Cached FormattedText, also used by AxisTitles to measure a title's width
    // (for centering) and height (for the rotation pivot).
    private static FormattedText GetText(string text)
    {
        if (!TextCache.TryGetValue(text, out var ft))
        {
            if (TextCache.Count > 512) TextCache.Clear();
            ft = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Font, 11, TextBrush);
            TextCache[text] = ft;
        }
        return ft;
    }
}
