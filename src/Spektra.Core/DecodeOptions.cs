namespace Spektra.Core;

/// Optional decode controls: a time segment (ffmpeg -ss/-t before -i) and
/// channel selection — null Channel = mono mixdown, otherwise a 0-based
/// input channel extracted via the pan filter.
public sealed record DecodeOptions(
    TimeSpan? Start = null, TimeSpan? Duration = null, int? Channel = null);
