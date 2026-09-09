namespace Spektra.Core;

/// The folder name a menu header shows for a folder tab: the drilldown
/// scope when one is set, the tab root otherwise. Kept in Core so the two
/// edge rules stay test-pinned; the App composes the surrounding text
/// ("_Analyze '{name}'").
public static class ScopeLabel
{
    /// Last path segment of the effective scope. Drive roots fall back to
    /// the path itself (GetFileName of C:\ is empty, and an empty label
    /// would render as Analyze '').
    public static string For(string rootFolder, string? scopeFolder)
    {
        var target = Path.TrimEndingDirectorySeparator(scopeFolder ?? rootFolder);
        var name = Path.GetFileName(target);
        return name.Length == 0 ? target : name;
    }

    /// The same name escaped for a menu header: underscores double because
    /// Avalonia treats a lone '_' as an access-key marker, and the header
    /// strings carry their own mnemonics around this name. Status-bar text
    /// wants For instead, where a doubled underscore would render literally.
    public static string ForMenu(string rootFolder, string? scopeFolder) =>
        For(rootFolder, scopeFolder).Replace("_", "__");

    /// Labels for several folders shown beside each other, each the shortest
    /// run of trailing segments no other folder in the set shares.
    ///
    /// A leaf name alone fails exactly where this is used. The folders worth
    /// diffing are usually two copies of one library, so their leaves match by
    /// construction and both columns end up headed "Album", with the full path
    /// available only on hover. Colliding labels take one more parent segment,
    /// repeatedly, and become the whole path when the segments run out, which
    /// is what happens when the difference is the drive: a drive is not a path
    /// segment, so no amount of growing reaches it.
    ///
    /// Only the labels that actually collide grow. A folder that was never
    /// ambiguous keeps its short name, because the shortest label that is
    /// still unique is the one worth reading across a row of columns.
    ///
    /// Case-insensitive, matching how the roots themselves are deduplicated:
    /// two labels differing only in case read as the same label.
    public static IReadOnlyList<string> Distinguish(IReadOnlyList<string> folders)
    {
        var full = new string[folders.Count];
        var segments = new string[folders.Count][];
        for (var i = 0; i < folders.Count; i++)
        {
            full[i] = Path.TrimEndingDirectorySeparator(folders[i]);
            // The path root ("D:\", or a UNC share) is held back so it can be
            // the last resort rather than a segment that gets grown into.
            var prefix = Path.GetPathRoot(full[i]) ?? "";
            segments[i] = full[i][prefix.Length..].Split(
                ['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        }

        var depth = new int[folders.Count];
        Array.Fill(depth, 1);

        // One past the last segment means "the whole path", which is how a
        // drive letter gets into a label at all.
        string Label(int i) => segments[i].Length == 0 || depth[i] > segments[i].Length
            ? full[i]
            : string.Join(Path.DirectorySeparatorChar, segments[i][^depth[i]..]);

        while (true)
        {
            var grew = false;
            foreach (var clash in Enumerable.Range(0, folders.Count)
                         .GroupBy(Label, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1))
            {
                foreach (var i in clash)
                {
                    // A label already showing its whole path has nothing left
                    // to grow into; without this, two spellings of one folder
                    // would spin here forever.
                    if (depth[i] > segments[i].Length) continue;
                    depth[i]++;
                    grew = true;
                }
            }
            if (!grew) break;
        }

        return [.. Enumerable.Range(0, folders.Count).Select(Label)];
    }
}
