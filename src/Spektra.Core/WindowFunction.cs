namespace Spektra.Core;

public enum WindowFunctionKind { Hann, Hamming, Blackman, BlackmanHarris }

public static class WindowFunction
{
    /// Builds the analysis window of the requested shape. All are symmetric
    /// (denominator N-1) so they match the existing Hann behavior.
    public static float[] Create(WindowFunctionKind kind, int size) => kind switch
    {
        WindowFunctionKind.Hamming => Hamming(size),
        WindowFunctionKind.Blackman => Blackman(size),
        WindowFunctionKind.BlackmanHarris => BlackmanHarris(size),
        _ => Hann(size),
    };

    /// Symmetric Hann: w[n] = 0.5 * (1 - cos(2*pi*n / (N-1)))
    public static float[] Hann(int size)
    {
        var w = new float[size];
        for (var n = 0; n < size; n++)
            w[n] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * n / (size - 1)));
        return w;
    }

    /// Hamming: w[n] = 0.54 - 0.46 * cos(2*pi*n / (N-1))
    public static float[] Hamming(int size)
    {
        var w = new float[size];
        for (var n = 0; n < size; n++)
            w[n] = 0.54f - 0.46f * MathF.Cos(2f * MathF.PI * n / (size - 1));
        return w;
    }

    /// Blackman: 0.42 - 0.5*cos(2*pi*n/(N-1)) + 0.08*cos(4*pi*n/(N-1))
    public static float[] Blackman(int size)
    {
        var w = new float[size];
        for (var n = 0; n < size; n++)
        {
            var a = 2f * MathF.PI * n / (size - 1);
            w[n] = 0.42f - 0.5f * MathF.Cos(a) + 0.08f * MathF.Cos(2f * a);
        }
        return w;
    }

    /// 4-term Blackman-Harris: strongest side-lobe suppression of the four.
    public static float[] BlackmanHarris(int size)
    {
        var w = new float[size];
        for (var n = 0; n < size; n++)
        {
            var a = 2f * MathF.PI * n / (size - 1);
            w[n] = 0.35875f - 0.48829f * MathF.Cos(a) + 0.14128f * MathF.Cos(2f * a)
                   - 0.01168f * MathF.Cos(3f * a);
        }
        return w;
    }
}
