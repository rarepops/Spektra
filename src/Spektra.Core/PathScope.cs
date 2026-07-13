namespace Spektra.Core;

/// Shared definition of "this path lives inside that folder". Used by the
/// audit cache prune and by the folder grid's drill-down scope filter so
/// both agree on containment (case-insensitive, prefix on a separator
/// boundary, and never the folder itself). FolderTree.Build also derives
/// its level prefixes from PrefixOf.
public static class PathScope
{
    public static bool IsUnder(string path, string folder)
    {
        var prefix = PrefixOf(folder);
        // Strictly longer than the prefix so a root never contains itself:
        // PrefixOf(@"C:\") is @"C:\", which the root path equals exactly.
        return path.Length > prefix.Length
            && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// The folder's path as a separator-terminated prefix. Root paths
    /// (C:\) keep their existing separator instead of gaining a second one.
    internal static string PrefixOf(string folder)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(folder);
        return trimmed.EndsWith(Path.DirectorySeparatorChar) ? trimmed : trimmed + Path.DirectorySeparatorChar;
    }
}
