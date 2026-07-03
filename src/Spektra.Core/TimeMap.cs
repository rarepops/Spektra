namespace Spektra.Core;

/// Maps a shared normalized viewport onto a file's absolute decode times.
/// ReferenceSeconds is the shared timeline length (A's duration in a compare);
/// ShiftSeconds is how much later this file's content sits (B's align offset).
/// Single-file use: shift 0, reference = the file's own duration → identity.
public readonly record struct TimeMap(double ReferenceSeconds, double ShiftSeconds = 0)
{
    public double StartSeconds(double t0Normalized) => t0Normalized * ReferenceSeconds + ShiftSeconds;
    public double DurationSeconds(double spanNormalized) => spanNormalized * ReferenceSeconds;
}
