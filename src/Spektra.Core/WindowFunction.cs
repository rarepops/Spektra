namespace Spektra.Core;

public static class WindowFunction
{
    /// Symmetric Hann: w[n] = 0.5 * (1 - cos(2*pi*n / (N-1)))
    public static float[] Hann(int size)
    {
        var w = new float[size];
        for (var n = 0; n < size; n++)
            w[n] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * n / (size - 1)));
        return w;
    }
}
