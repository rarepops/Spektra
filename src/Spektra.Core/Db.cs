namespace Spektra.Core;

public static class Db
{
    public const float Floor = -120f;

    public static float FromAmplitude(float amplitude)
    {
        if (amplitude <= 0f) return Floor;
        return Math.Clamp(20f * MathF.Log10(amplitude), Floor, 0f);
    }
}
