namespace Spektra.Core;

/// Optional decode controls: a time segment (ffmpeg -ss/-t before -i), channel
/// selection (null = mono mixdown, else a 0-based input channel or Difference,
/// via the pan filter), and an output sample-rate override (-ar) used to put two
/// files on a common analysis grid.
public sealed record DecodeOptions(
    TimeSpan? Start = null, TimeSpan? Duration = null, int? Channel = null, int? SampleRate = null)
{
    /// The Channel value for what the first two channels do NOT share: left
    /// minus right. Silent for mono content in a stereo file, and in a
    /// joint-stereo lossy encode it often stops lower than the mix does,
    /// because those encoders starve exactly this part of the signal.
    /// Negative so no real channel number can ever mean it.
    public const int Difference = -1;
}
