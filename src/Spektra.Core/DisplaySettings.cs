namespace Spektra.Core;

/// Display-only settings that affect how the spectrogram is drawn but not how it
/// is analyzed: colormap, the dB range mapped across that colormap, and whether
/// the frequency axis is logarithmic. Changing these only needs a re-render.
public sealed record DisplaySettings(
    PaletteKind Palette = PaletteKind.Magma,
    float DbFloor = -120f,
    bool LogFrequency = false,
    bool ShowSpectrum = false)
{
    public float DbCeil => 0f;
}
