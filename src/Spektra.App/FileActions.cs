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

    /// Hands the file to whatever the system already opens it with. Spektra
    /// draws audio and never plays it, so this is the one verb it cannot
    /// implement itself, and it is the one a user comparing two copies of a
    /// track reaches for first: the spectrogram says they differ, the ears
    /// say which one to keep.
    ///
    /// UseShellExecute is the whole mechanism, on every platform Spektra
    /// ships to: Windows resolves the file association, macOS goes through
    /// /usr/bin/open, Linux through xdg-open. Unlike Reveal there is no
    /// platform left to no-op on.
    ///
    /// Returns null when the file was handed over, or the sentence to show
    /// when it was not, which each window puts on its own status line. The
    /// catch is deliberately wide: this runs from a click handler, where an
    /// escaping exception takes the window down with it, and the ways a shell
    /// hand-off can fail are the OS's to define rather than ours to enumerate.
    public static string? Play(IFileItem? item)
    {
        if (item is null) return null;
        try
        {
            using var started = Process.Start(
                new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            return null;
        }
        catch (Exception)
        {
            // Which of the two it is matters: rows outlive the files they name
            // in a window whose whole purpose is deleting duplicates.
            return File.Exists(item.FullPath)
                ? $"Nothing on this system is set up to play {Path.GetFileName(item.FullPath)}."
                : $"That file is not there any more: {item.FullPath}";
        }
    }
}
