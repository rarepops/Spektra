using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;

namespace Spektra.App;

/// The generic file verbs shared by every right-click menu. Spektra's own
/// verbs live on the view models (they differ per surface); these two are
/// the escape hatches to the OS, so they exist once here.
public static class FileActions
{
    /// The clicked menu item's item, or null when the menu was opened over
    /// something that does not stand for a file.
    public static IFileItem? ItemFrom(object? sender) =>
        (sender as MenuItem)?.DataContext as IFileItem;

    public static Task CopyPathAsync(TopLevel? top, IFileItem? item) =>
        item is null ? Task.CompletedTask : CopyTextAsync(top, item.FullPath);

    /// One clipboard write for everything: single paths and the multi-line
    /// batch verbs (one path per line) go through the same door.
    public static async Task CopyTextAsync(TopLevel? top, string text)
    {
        if (top?.Clipboard is not { } clipboard) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Text, text));
        await clipboard.SetDataAsync(data);
    }

    /// Windows only. Selecting a file in a file manager has no portable
    /// equivalent, so this is a no-op elsewhere rather than a wrong guess.
    public static void Reveal(IFileItem? item)
    {
        if (item is null || !OperatingSystem.IsWindows()) return;
        Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
    }
}
