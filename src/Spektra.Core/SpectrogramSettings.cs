namespace Spektra.Core;

public sealed record SpectrogramSettings(
    int WindowSize = 2048, int MaxColumns = 4096, int HopOverride = 0,
    WindowFunctionKind Window = WindowFunctionKind.Hann)
{
    public int Hop => HopOverride > 0 ? HopOverride : WindowSize / 2;

    /// How many overview columns a file of this length will produce, capped at
    /// MaxColumns: the size to allocate before the decode has counted them.
    public int EstimateColumns(TimeSpan duration, int sampleRate)
    {
        var samples = (long)(duration.TotalSeconds * sampleRate);
        var windows = Math.Max(1, (samples - WindowSize) / Hop + 1);
        return (int)Math.Min(windows, MaxColumns);
    }
}
