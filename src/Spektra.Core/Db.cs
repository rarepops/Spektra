namespace Spektra.Core;

public static class Db
{
    public const float Floor = -120f;

    public static float FromAmplitude(float amplitude)
    {
        if (amplitude <= 0f) return Floor;
        return Math.Clamp(20f * MathF.Log10(amplitude), Floor, 0f);
    }

    /// Same value from magnitude SQUARED: 10*log10(p) equals 20*log10(sqrt(p)),
    /// so a caller holding power skips a square root per bin.
    public static float FromPower(float power)
    {
        if (power <= 0f) return Floor;
        return Math.Clamp(10f * MathF.Log10(power), Floor, 0f);
    }
}
