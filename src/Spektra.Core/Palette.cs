namespace Spektra.Core;

/// Magma-family colormap: black -> deep purple -> magenta -> orange -> pale yellow.
/// Anchor values are original; linear interpolation between anchors.
public static class Palette
{
    private static readonly (byte R, byte G, byte B)[] Anchors =
    [
        (0, 0, 4),
        (28, 16, 68),
        (79, 18, 123),
        (129, 37, 129),
        (181, 54, 122),
        (229, 80, 100),
        (251, 135, 97),
        (254, 194, 135),
        (252, 253, 191),
    ];

    public static uint ToBgra(float db)
    {
        var t = (Math.Clamp(db, Db.Floor, 0f) - Db.Floor) / (0f - Db.Floor); // 0..1
        var scaled = t * (Anchors.Length - 1);
        var i = Math.Min((int)scaled, Anchors.Length - 2);
        var f = scaled - i;

        var (r0, g0, b0) = Anchors[i];
        var (r1, g1, b1) = Anchors[i + 1];
        var r = (uint)(r0 + (r1 - r0) * f);
        var g = (uint)(g0 + (g1 - g0) * f);
        var b = (uint)(b0 + (b1 - b0) * f);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }
}
