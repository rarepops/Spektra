namespace Spektra.App.Controls;

/// The frequency axis of a spectrogram surface: the visible normalized window
/// [F0,F1] (Nyquist fractions in [0,1]), whether it is logarithmic, and the
/// Nyquist frequency in Hz (only needed to anchor the log floor). Bundles the
/// four values that always travel together through the drawing code and owns the
/// mapping between Nyquist fraction and vertical position in both directions.
internal readonly record struct FreqAxis(double F0, double F1, bool Log, double Nyquist)
{
    public const double LogMinHz = 20.0;

    // Lowest normalized frequency shown on a log axis (keeps log() finite).
    private double LogFloor => Math.Min(Nyquist > 0 ? LogMinHz / Nyquist : 0.001, F1 * 0.5);

    /// Normalized vertical position (0 = bottom/low, 1 = top/high) for a Nyquist
    /// fraction q within the visible [F0,F1] window. Linear or logarithmic.
    public double PosOf(double q)
    {
        if (F1 <= F0) return 0;
        if (!Log) return (q - F0) / (F1 - F0);
        var qmin = LogFloor;
        var a = Math.Log(Math.Max(F0, qmin));
        var b = Math.Log(Math.Max(F1, qmin * 1.0001));
        return (Math.Log(Math.Max(q, qmin)) - a) / (b - a);
    }

    /// Inverse of PosOf: the Nyquist fraction at vertical position p (0 = bottom).
    public double FreqAt(double p)
    {
        if (!Log) return F0 + p * (F1 - F0);
        var qmin = LogFloor;
        var a = Math.Log(Math.Max(F0, qmin));
        var b = Math.Log(Math.Max(F1, qmin * 1.0001));
        return Math.Exp(a + p * (b - a));
    }
}
