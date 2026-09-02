using Avalonia.Controls;
using Avalonia.Interactivity;
using Spektra.Core;

namespace Spektra.App;

/// One line of the shortcut sheet: the gesture and what it does.
public sealed record ShortcutRow(string Key, string Description);

public partial class ControlsWindow : Window
{
    private readonly KeyMap _keys;

    /// The sheet reads the live key map rather than a hardcoded list, so a
    /// rebound key is documented correctly by the one window whose whole job
    /// is documenting keys. The README still describes the defaults, which is
    /// the right thing for a README to describe.
    /// For Avalonia's runtime XAML loader only, which needs a public
    /// parameterless constructor; the shell always passes its own map.
    public ControlsWindow() : this(KeyMap.Defaults, []) { }

    public ControlsWindow(KeyMap keys, IReadOnlyList<string> problems)
    {
        _keys = keys;
        Problems = problems;
        BindingsFile = KeyMapStore.DefaultPath;
        InitializeComponent();
        DataContext = this;
    }

    public IReadOnlyList<string> Problems { get; }
    public bool HasProblems => Problems.Count > 0;
    public string BindingsFile { get; }

    /// The gestures for one or more commands, skipping any the user unbound,
    /// so a row never renders a dangling separator or an empty key column.
    private string Keyed(params KeyCommand[] commands)
    {
        var labels = commands.Select(_keys.Label).Where(l => l.Length > 0).ToList();
        return labels.Count == 0 ? "(no key)" : string.Join("  ·  ", labels);
    }

    public IReadOnlyList<ShortcutRow> FilesAndTabs =>
    [
        new(Keyed(KeyCommand.OpenFiles), "Open audio files"),
        new(Keyed(KeyCommand.OpenFolder), "Open a folder"),
        new(Keyed(KeyCommand.CloseTab), "Close the current tab"),
        new(Keyed(KeyCommand.NextTab, KeyCommand.PreviousTab), "Next / previous tab"),
        new("Ctrl+1-9", "Jump to tab N"),
    ];

    public IReadOnlyList<ShortcutRow> ViewAndZoom =>
    [
        new("Wheel", "Zoom in time"),
        new("Shift+Wheel", "Zoom in frequency"),
        new("Drag", "Pan"),
        new("Double-click", "Reset the view"),
        new(Keyed(KeyCommand.ResetView), "Reset the view"),
        new(Keyed(KeyCommand.PreviousChannel, KeyCommand.NextChannel), "Previous / next channel"),
        new(Keyed(KeyCommand.PreviousFile, KeyCommand.NextFile), "Previous / next file in the folder"),
        new(Keyed(KeyCommand.ToggleCrosshair), "Toggle the crosshair"),
    ];

    public IReadOnlyList<ShortcutRow> SaveAndExport =>
    [
        new(Keyed(KeyCommand.SaveImage), "Save the spectrogram as an image"),
        new(Keyed(KeyCommand.CopyImage), "Copy the spectrogram to the clipboard"),
        new(Keyed(KeyCommand.ExportReport), "Export the current file's report"),
    ];

    public IReadOnlyList<ShortcutRow> Analysis =>
    [
        new(Keyed(KeyCommand.Preferences), "Preferences"),
        new(Keyed(KeyCommand.ToggleSpectrum), "Toggle the average-spectrum overlay"),
        new(Keyed(KeyCommand.CheckIntegrity), "Check integrity"),
        new(Keyed(KeyCommand.MeasureLoudness), "Measure loudness (LUFS)"),
        new(Keyed(KeyCommand.Reload, KeyCommand.ReloadFresh),
            "Reload the file  ·  rescan a folder from scratch"),
        new(Keyed(KeyCommand.RefreshFolder),
            "Re-read a folder tab from disk, keeping your checkboxes"),
    ];

    public IReadOnlyList<ShortcutRow> Compare =>
    [
        new(Keyed(KeyCommand.Compare), "Compare two open files"),
        new(Keyed(KeyCommand.CompareFlip), "Flip A / B"),
        new(Keyed(KeyCommand.CompareDiff), "Show the difference"),
        new(Keyed(KeyCommand.CompareAlign), "Auto-align"),
        new(Keyed(KeyCommand.CompareBoth), "Back to showing both"),
    ];

    public IReadOnlyList<ShortcutRow> Tools =>
    [
        new("Right-click", "Verbs on any listed file or folder: open, re-analyze, compare with winner, copy path, reveal, and hops between the audit and the manifest"),
        new("Ctrl/Shift+Click", "Select several audit rows; Copy path and Re-analyze act on the whole selection"),
        new("Enter / Esc", "In the tool windows' path boxes: load the typed folder / revert; emptying the manifest's box clears the listing"),
        new("F1", "This window. Always F1, the one key that cannot be rebound"),
    ];

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
