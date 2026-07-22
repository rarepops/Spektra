namespace Spektra.Core;

/// The shared word-splitter for the app's filter boxes (Duplicate Detective
/// results, Folder Manifest kinds). Space, comma, semicolon and tab all
/// separate; empty entries are dropped and each word is trimmed. Callers layer
/// their own normalization (lowercasing, extension stripping) on top.
internal static class FilterTokens
{
    private static readonly char[] Separators = [' ', ',', ';', '\t'];

    public static IEnumerable<string> Split(string? text) =>
        (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
