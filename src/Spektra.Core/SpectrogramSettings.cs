namespace Spektra.Core;

public sealed record SpectrogramSettings(
    int WindowSize = 2048, int MaxColumns = 4096, int HopOverride = 0,
    WindowFunctionKind Window = WindowFunctionKind.Hann)
{
    public int Hop => HopOverride > 0 ? HopOverride : WindowSize / 2;
}
