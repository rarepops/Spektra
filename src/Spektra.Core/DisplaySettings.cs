namespace Spektra.Core;

/// Display-only settings that affect how the spectrogram is drawn but not how it
/// is analyzed: the baked palette LUT, the dB range mapped across it, and whether
/// the frequency axis is logarithmic. Changing these only needs a re-render.
public sealed record DisplaySettings(
    uint[]? PaletteLut = null,
    float DbFloor = -120f,
    bool LogFrequency = false,
    bool ShowSpectrum = false,
    bool ShowCrosshair = true)
{
    public float DbCeil => 0f;

    /// The resolved palette; Turbo when none was baked.
    public uint[] Lut => PaletteLut ?? Colormaps.DefaultLut;
}
