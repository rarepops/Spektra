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
}
