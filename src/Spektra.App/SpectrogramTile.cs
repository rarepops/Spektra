namespace Spektra.App;

/// A sharp re-render of one zoomed time span: Columns cover normalized time
/// [T0,T1] across the full frequency range (Bins rows, freq 0 first).
public sealed record SpectrogramTile(
    double T0, double T1, int Bins, IReadOnlyList<float[]> Columns);
