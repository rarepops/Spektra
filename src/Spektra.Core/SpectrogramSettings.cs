namespace Spektra.Core;

public sealed record SpectrogramSettings(int WindowSize = 2048, int MaxColumns = 4096)
{
    public int Hop => WindowSize / 2;
}
