namespace Spektra.Core;

public sealed record SpectrogramSettings(
    int WindowSize = 2048, int MaxColumns = 4096, int HopOverride = 0,
    WindowFunctionKind Window = WindowFunctionKind.Hann)
{
    public int Hop => HopOverride > 0 ? HopOverride : WindowSize / 2;

    /// An upper estimate of how many overview columns a file of this length
    /// produces, capped at MaxColumns: a capacity to allocate before the
    /// decode has counted them. It errs high on purpose. The engine merges
    /// windows in whole groups, so a long file yields fewer columns than
    /// this, and a buffer sized from a number that came out low would have
    /// no room for the last ones.
    public int EstimateColumns(TimeSpan duration, int sampleRate)
    {
        var samples = (long)(duration.TotalSeconds * sampleRate);
        var windows = Math.Max(1, (samples - WindowSize) / Hop + 1);
        return (int)Math.Min(windows, MaxColumns);
    }
}
