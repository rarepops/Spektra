namespace Spektra.Core;

/// Normalized visible ranges of a spectrogram: time [T0,T1] and frequency
/// [F0,F1], both within [0,1]. Mutations clamp to bounds and minimum spans;
/// Changed fires only when a value actually moved.
public sealed class Viewport
{
    public const double MinFreqSpan = 0.01;

    /// Set per document: min normalized time span so tile hop never dips
    /// below the 64-sample max-zoom clamp.
    public double MinTimeSpan { get; set; } = 0.001;

    public double T0 { get; private set; }
    public double T1 { get; private set; } = 1;
    public double F0 { get; private set; }
    public double F1 { get; private set; } = 1;

    public double TimeSpanN => T1 - T0;
    public double FreqSpanN => F1 - F0;
    public bool IsTimeZoomed => T0 > 0 || T1 < 1;
    public bool IsZoomed => IsTimeZoomed || F0 > 0 || F1 < 1;

    public event Action? Changed;

    public void ZoomTime(double anchor, double factor)
    {
        var (a, b) = ZoomAxis(T0, T1, anchor, factor, MinTimeSpan);
        Apply(a, b, F0, F1);
    }

    public void ZoomFrequency(double anchor, double factor)
    {
        var (a, b) = ZoomAxis(F0, F1, anchor, factor, MinFreqSpan);
        Apply(T0, T1, a, b);
    }

    public void PanTime(double delta)
    {
        var (a, b) = PanAxis(T0, T1, delta);
        Apply(a, b, F0, F1);
    }

    public void PanFrequency(double delta)
    {
        var (a, b) = PanAxis(F0, F1, delta);
        Apply(T0, T1, a, b);
    }

    public void Reset() => Apply(0, 1, 0, 1);

    private static (double, double) ZoomAxis(
        double lo, double hi, double anchor, double factor, double minSpan)
    {
        anchor = Math.Clamp(anchor, 0, 1);
        var span = Math.Clamp((hi - lo) * factor, Math.Min(minSpan, 1), 1);
        var pivot = lo + anchor * (hi - lo);
        var newLo = Math.Clamp(pivot - anchor * span, 0, 1 - span);
        return (newLo, newLo + span);
    }

    private static (double, double) PanAxis(double lo, double hi, double delta)
    {
        var span = hi - lo;
        var newLo = Math.Clamp(lo + delta, 0, 1 - span);
        return (newLo, newLo + span);
    }

    private void Apply(double t0, double t1, double f0, double f1)
    {
        const double eps = 1e-12;
        if (Math.Abs(t0 - T0) < eps && Math.Abs(t1 - T1) < eps &&
            Math.Abs(f0 - F0) < eps && Math.Abs(f1 - F1) < eps) return;
        T0 = t0; T1 = t1; F0 = f0; F1 = f1;
        Changed?.Invoke();
    }
}
