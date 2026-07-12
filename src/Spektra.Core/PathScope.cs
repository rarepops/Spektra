namespace Spektra.Core;

/// Shared definition of "this path lives inside that folder". Used by the
/// audit cache prune and by the folder grid's drill-down scope filter so
/// both agree on containment (case-insensitive, prefix on a separator
/// boundary, and never the folder itself).
public static class PathScope
{
    public static bool IsUnder(string path, string folder)
    {
        var prefix = Path.TrimEndingDirectorySeparator(folder)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
