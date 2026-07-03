namespace Spektra.Core;

public enum PaletteKind { Magma, Viridis, Inferno, Grayscale }

/// dB-to-color maps for the spectrogram. Each palette is a small table of anchor
/// RGBs (matplotlib-derived / public-domain data) interpolated linearly. The
/// input dB is normalized against a configurable [floor, ceil] display range so
/// the dynamic range can be tightened or loosened without re-analyzing.
public static class Colormaps
{
    private static readonly (byte R, byte G, byte B)[] MagmaAnchors =
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

    private static readonly (byte R, byte G, byte B)[] ViridisAnchors =
    [
        (68, 1, 84),
        (72, 40, 120),
        (62, 74, 137),
        (49, 104, 142),
        (38, 130, 142),
        (31, 158, 137),
        (53, 183, 121),
        (110, 206, 88),
        (181, 222, 43),
        (253, 231, 37),
    ];

    private static readonly (byte R, byte G, byte B)[] InfernoAnchors =
    [
        (0, 0, 4),
        (31, 12, 72),
        (85, 15, 109),
        (136, 34, 106),
        (186, 54, 85),
        (227, 89, 51),
        (249, 140, 10),
        (249, 201, 50),
        (252, 255, 164),
    ];

    private static readonly (byte R, byte G, byte B)[] GrayscaleAnchors =
    [
        (0, 0, 0),
        (255, 255, 255),
    ];

    public static uint ToBgra(PaletteKind kind, float db, float floor, float ceil = 0f)
    {
        if (ceil <= floor) ceil = floor + 1f;
        var anchors = Anchors(kind);
        var t = (Math.Clamp(db, floor, ceil) - floor) / (ceil - floor); // 0..1
        var scaled = t * (anchors.Length - 1);
        var i = Math.Min((int)scaled, anchors.Length - 2);
        var f = scaled - i;

        var (r0, g0, b0) = anchors[i];
        var (r1, g1, b1) = anchors[i + 1];
        var r = (uint)(r0 + (r1 - r0) * f);
        var g = (uint)(g0 + (g1 - g0) * f);
        var b = (uint)(b0 + (b1 - b0) * f);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }

    private static (byte R, byte G, byte B)[] Anchors(PaletteKind kind) => kind switch
    {
        PaletteKind.Viridis => ViridisAnchors,
        PaletteKind.Inferno => InfernoAnchors,
        PaletteKind.Grayscale => GrayscaleAnchors,
        _ => MagmaAnchors,
    };
}
