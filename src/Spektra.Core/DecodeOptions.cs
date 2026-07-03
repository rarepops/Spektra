namespace Spektra.Core;

/// Optional decode controls: a time segment (ffmpeg -ss/-t before -i), channel
/// selection (null = mono mixdown, else a 0-based input channel via the pan
/// filter), and an output sample-rate override (-ar) used to put two files on a
/// common analysis grid.
public sealed record DecodeOptions(
    TimeSpan? Start = null, TimeSpan? Duration = null, int? Channel = null, int? SampleRate = null);
