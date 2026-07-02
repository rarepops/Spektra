namespace Spektra.Core;

public sealed class AudioDecodeException(string message, string? stderrTail = null)
    : Exception(message)
{
    public string? StderrTail { get; } = stderrTail;
}
