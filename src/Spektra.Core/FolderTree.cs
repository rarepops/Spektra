namespace Spektra.Core;

/// A node in the folder browse tree: either a file leaf or a folder with
/// children. Built purely from a flat path list (no disk or ffmpeg), so it
/// is fully unit-testable and drives the GUI drop tree.
public abstract record FolderTreeNode(string Name, string FullPath);

public sealed record FolderTreeFile(string Name, string FullPath)
    : FolderTreeNode(Name, FullPath);

public sealed record FolderTreeFolder(
    string Name,
    string FullPath,
    IReadOnlyList<FolderTreeNode> Children) : FolderTreeNode(Name, FullPath);

public static class FolderTree
{
    private static readonly char Sep = Path.DirectorySeparatorChar;

    /// Build a sorted forest from every audio file under root. Folders sort
    /// before files, each group alphabetically (ordinal, case-insensitive).
    public static IReadOnlyList<FolderTreeNode> Build(
        string root, IReadOnlyList<string> audioFiles)
    {
        var prefix = Path.TrimEndingDirectorySeparator(root) + Sep;
        return BuildLevel(prefix, audioFiles);
    }

    private static IReadOnlyList<FolderTreeNode> BuildLevel(
        string prefix, IReadOnlyList<string> files)
    {
        var leaves = new List<FolderTreeFile>();
        var order = new List<string>();
        var groups = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var rest = file[prefix.Length..];
            var sep = rest.IndexOf(Sep);
            if (sep < 0)
            {
                leaves.Add(new FolderTreeFile(rest, file));
                continue;
            }

            var segment = rest[..sep];
            if (!groups.TryGetValue(segment, out var bucket))
            {
                bucket = [];
                groups[segment] = bucket;
                order.Add(segment);
            }
            bucket.Add(file);
        }

        var folders = new List<FolderTreeFolder>();
        foreach (var segment in order)
        {
            var childPrefix = prefix + segment + Sep;
            folders.Add(new FolderTreeFolder(
                segment,
                Path.TrimEndingDirectorySeparator(childPrefix),
                BuildLevel(childPrefix, groups[segment])));
        }

        folders.Sort((a, b) =>
            string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        leaves.Sort((a, b) =>
            string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return [.. folders, .. leaves];
    }

    /// Depth-first file enumeration: folders (in order) before sibling
    /// files, matching Build's ordering.
    public static IEnumerable<FolderTreeFile> Files(FolderTreeNode node)
    {
        switch (node)
        {
            case FolderTreeFile file:
                yield return file;
                break;
            case FolderTreeFolder folder:
                foreach (var child in folder.Children)
                    foreach (var f in Files(child))
                        yield return f;
                break;
        }
    }

    public static IEnumerable<FolderTreeFile> Files(
        IEnumerable<FolderTreeNode> forest)
    {
        foreach (var node in forest)
            foreach (var file in Files(node))
                yield return file;
    }

    /// Fold child checkbox states into a tri-state parent: all checked is
    /// true, none is false, a mix is null (indeterminate). Empty is false.
    public static bool? AggregateCheck(IReadOnlyList<bool> childStates)
    {
        if (childStates.Count == 0)
            return false;
        var anyChecked = false;
        var anyUnchecked = false;
        foreach (var state in childStates)
        {
            if (state) anyChecked = true;
            else anyUnchecked = true;
        }
        if (anyChecked && anyUnchecked)
            return null;
        return anyChecked;
    }
}
