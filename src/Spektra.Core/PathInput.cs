namespace Spektra.Core;

/// Cleans a folder path a user typed or pasted into an address box. The picker
/// and drag-drop hand back real paths, but typed text can carry padding, and
/// Windows "Copy as path" wraps the path in double quotes; both are stripped
/// here so the two tool windows validate the same shape. Whitespace inside the
/// quotes is trimmed too, but a folder name's own inner spaces are kept.
public static class PathInput
{
    public static string Normalize(string? raw) => (raw ?? "").Trim().Trim('"').Trim();
}
