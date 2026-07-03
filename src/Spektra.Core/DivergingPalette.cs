namespace Spektra.Core;

/// Diverging colormap for signed dB differences: negative (B louder) → blue,
/// zero → light gray, positive (A louder) → red. Anchor RGBs are matplotlib's
/// coolwarm (public domain), linearly interpolated.
public static class DivergingPalette
{
    private static readonly (byte R, byte G, byte B)[] Anchors =
    [
        (59, 76, 192),
        (105, 124, 220),
        (165, 182, 238),
        (221, 221, 221),
        (238, 180, 165),
        (220, 124, 105),
        (180, 4, 38),
    ];

    public static uint ToBgra(float diffDb, float clamp = 40f)
    {
        var t = (Math.Clamp(diffDb, -clamp, clamp) + clamp) / (2 * clamp); // 0..1
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
