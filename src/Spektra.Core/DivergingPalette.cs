namespace Spektra.Core;

/// Signed dB-difference colormap painted on black, so matching content reads as
/// silence and only real differences light up: zero → black, positive (A louder)
/// → red→orange, negative (B louder) → blue→cyan. Brightness tracks the
/// magnitude of the difference (with a mild gamma lift) so subtle deltas stay
/// visible instead of washing the whole view in gray.
public static class DivergingPalette
{
    public static uint ToBgra(float diffDb, float clamp = 40f)
    {
        var t = Math.Clamp(diffDb, -clamp, clamp) / clamp;         // -1..+1 (sign = which is louder)
        var i = MathF.Pow(MathF.Abs(t), 0.65f);                    // 0..1 intensity; small diffs lifted
        var main = (byte)Math.Clamp(i * 293f, 0f, 255f);           // hue channel: red (A) or blue (B)
        var glow = (byte)Math.Clamp((i - 0.55f) * 444f, 0f, 200f); // green highlight so big diffs pop
        return t >= 0
            ? 0xFF000000u | ((uint)main << 16) | ((uint)glow << 8) // A louder → red → orange
            : 0xFF000000u | ((uint)glow << 8) | main;              // B louder → blue → cyan
    }
}
