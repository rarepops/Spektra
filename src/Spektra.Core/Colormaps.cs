namespace Spektra.Core;

public enum PaletteKind { Magma, Viridis, Inferno, Grayscale, Plasma, Cividis, Turbo, MonoGreen, MonoAmber, MonoIce }

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

    private static readonly (byte R, byte G, byte B)[] PlasmaAnchors =
    [
        (13, 8, 135),
        (65, 4, 157),
        (106, 0, 168),
        (143, 13, 164),
        (177, 42, 144),
        (204, 71, 120),
        (225, 100, 98),
        (242, 132, 75),
        (252, 166, 54),
        (252, 206, 37),
        (240, 249, 33),
    ];

    // Colorblind-friendly (deuteranopia/protanopia-safe) blue-to-yellow ramp.
    private static readonly (byte R, byte G, byte B)[] CividisAnchors =
    [
        (0, 34, 78),
        (18, 53, 112),
        (59, 73, 108),
        (87, 93, 109),
        (112, 113, 115),
        (138, 134, 120),
        (165, 156, 116),
        (195, 179, 105),
        (225, 204, 85),
        (253, 234, 69),
    ];

    // Single-hue "phosphor" ramps: saturation IS the intensity (HSV with
    // S = t and V = t^0.75, hue fixed), so quiet content sits dim and
    // gray-tinted and loud content goes fully vivid. Baked to RGB anchors.
    private static readonly (byte R, byte G, byte B)[] MonoGreenAnchors =
    [
        (0, 0, 0),
        (47, 54, 47),
        (68, 90, 68),
        (76, 122, 76),
        (76, 152, 76),
        (67, 179, 67),
        (51, 206, 51),
        (29, 231, 29),
        (0, 255, 0),
    ];

    private static readonly (byte R, byte G, byte B)[] MonoAmberAnchors =
    [
        (0, 0, 0),
        (54, 52, 47),
        (90, 85, 68),
        (122, 111, 76),
        (152, 133, 76),
        (179, 151, 67),
        (206, 167, 51),
        (231, 180, 29),
        (255, 191, 0),
    ];

    private static readonly (byte R, byte G, byte B)[] MonoIceAnchors =
    [
        (0, 0, 0),
        (47, 52, 54),
        (68, 85, 90),
        (76, 111, 122),
        (76, 133, 152),
        (67, 151, 179),
        (51, 167, 206),
        (29, 180, 231),
        (0, 191, 255),
    ];

    // Google's perceptually-improved rainbow (the modern "jet").
    private static readonly (byte R, byte G, byte B)[] TurboAnchors =
    [
        (48, 18, 59),
        (69, 91, 205),
        (62, 155, 254),
        (24, 214, 203),
        (72, 248, 130),
        (164, 252, 59),
        (226, 220, 56),
        (254, 163, 49),
        (239, 89, 17),
        (194, 36, 3),
        (122, 4, 3),
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

    internal static (byte R, byte G, byte B)[] AnchorsFor(PaletteKind kind) => Anchors(kind);

    private static uint[]? _defaultLut;

    /// Magma baked once; the fallback wherever no palette LUT was resolved.
    public static uint[] DefaultLut => _defaultLut ??=
        PaletteLut.Bake(PaletteLut.EvenStops(MagmaAnchors));

    private static (byte R, byte G, byte B)[] Anchors(PaletteKind kind) => kind switch
    {
        PaletteKind.Viridis => ViridisAnchors,
        PaletteKind.Inferno => InfernoAnchors,
        PaletteKind.Grayscale => GrayscaleAnchors,
        PaletteKind.Plasma => PlasmaAnchors,
        PaletteKind.Cividis => CividisAnchors,
        PaletteKind.Turbo => TurboAnchors,
        PaletteKind.MonoGreen => MonoGreenAnchors,
        PaletteKind.MonoAmber => MonoAmberAnchors,
        PaletteKind.MonoIce => MonoIceAnchors,
        _ => MagmaAnchors,
    };
}
