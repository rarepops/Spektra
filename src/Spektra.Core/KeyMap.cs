namespace Spektra.Core;

/// Every action a key can be bound to. Two things are deliberately absent
/// and stay hard-wired in the shell: F1 (it opens the window documenting
/// every other key, so rebinding it badly would take the discovery path for
/// all of them), and the Ctrl+1..9 tab jumps, which are positional rather
/// than a command each.
public enum KeyCommand
{
    OpenFiles,
    OpenFolder,
    CloseTab,
    NextTab,
    PreviousTab,
    Preferences,
    SaveImage,
    CopyImage,
    ExportReport,
    ResetView,
    ToggleSpectrum,
    ToggleCrosshair,
    NextChannel,
    PreviousChannel,
    NextFile,
    PreviousFile,
    CheckIntegrity,
    MeasureLoudness,
    Reload,
    ReloadFresh,
    RefreshFolder,
    Compare,
    CompareFlip,
    CompareDiff,
    CompareBoth,
    CompareAlign,
}

/// What each key does, as one table: the shipped defaults with the user's
/// keybindings.json merged over them.
///
/// It exists as much for Spektra as for the user. Before it, the same
/// shortcut was written down in four places that nothing kept in sync (the
/// shell's key switch, the menu gesture labels, the F1 sheet, and the
/// README), so a typo in any of the three display copies was invisible until
/// somebody reported it.
public sealed class KeyMap
{
    private static readonly (KeyCommand Command, string Id, string Gesture)[] Table =
    [
        (KeyCommand.OpenFiles,       "open-files",       "Ctrl+O"),
        (KeyCommand.OpenFolder,      "open-folder",      "Ctrl+Shift+O"),
        (KeyCommand.CloseTab,        "close-tab",        "Ctrl+W"),
        (KeyCommand.NextTab,         "next-tab",         "Ctrl+Tab"),
        (KeyCommand.PreviousTab,     "previous-tab",     "Ctrl+Shift+Tab"),
        (KeyCommand.Preferences,     "preferences",      "Ctrl+E"),
        (KeyCommand.SaveImage,       "save-image",       "Ctrl+S"),
        (KeyCommand.CopyImage,       "copy-image",       "Ctrl+Shift+C"),
        (KeyCommand.ExportReport,    "export-report",    "Ctrl+Shift+S"),
        (KeyCommand.ResetView,       "reset-view",       "Ctrl+0"),
        (KeyCommand.ToggleSpectrum,  "toggle-spectrum",  "Ctrl+R"),
        (KeyCommand.ToggleCrosshair, "toggle-crosshair", "Ctrl+H"),
        (KeyCommand.NextChannel,     "next-channel",     "Ctrl+Down"),
        (KeyCommand.PreviousChannel, "previous-channel", "Ctrl+Up"),
        (KeyCommand.NextFile,        "next-file",        "Ctrl+Right"),
        (KeyCommand.PreviousFile,    "previous-file",    "Ctrl+Left"),
        (KeyCommand.CheckIntegrity,  "check-integrity",  "Ctrl+I"),
        (KeyCommand.MeasureLoudness, "measure-loudness", "Ctrl+L"),
        (KeyCommand.Reload,          "reload",           "F5"),
        (KeyCommand.ReloadFresh,     "reload-fresh",     "Shift+F5"),
        (KeyCommand.RefreshFolder,   "refresh-folder",   "Ctrl+F5"),
        (KeyCommand.Compare,         "compare",          "Ctrl+D"),
        (KeyCommand.CompareFlip,     "compare-flip",     "T"),
        (KeyCommand.CompareDiff,     "compare-diff",     "D"),
        (KeyCommand.CompareBoth,     "compare-both",     "Esc"),
        (KeyCommand.CompareAlign,    "compare-align",    "A"),
    ];

    private static readonly Dictionary<string, KeyCommand> ByIdMap =
        Table.ToDictionary(t => t.Id, t => t.Command, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<KeyCommand, string> IdMap =
        Table.ToDictionary(t => t.Command, t => t.Id);

    private readonly Dictionary<KeyCommand, KeyStroke?> _bindings;

    private KeyMap(Dictionary<KeyCommand, KeyStroke?> bindings) => _bindings = bindings;

    /// The command id used in keybindings.json ("next-file").
    public static string IdOf(KeyCommand command) => IdMap[command];

    public static bool TryParseCommand(string id, out KeyCommand command) =>
        ByIdMap.TryGetValue(id, out command);

    /// Every command id, for documenting the file.
    public static IReadOnlyList<string> CommandIds => [.. Table.Select(t => t.Id)];

    public static KeyMap Defaults { get; } = new(
        Table.ToDictionary(
            t => t.Command,
            t => KeyStroke.TryParse(t.Gesture, out var s) ? s : (KeyStroke?)null));

    /// The defaults with the user's entries merged over them. Never throws and
    /// never fails wholesale: a bad entry is reported and skipped, so one typo
    /// costs its own line rather than the whole keyboard.
    ///
    /// Three rules, in this order:
    ///  * a user entry that cannot be parsed keeps that command's default,
    ///  * a user entry claiming a gesture an earlier user entry already took
    ///    is rejected and keeps its default,
    ///  * a command the user did not mention loses its default key if a user
    ///    entry took it. That is not an error: taking a key from its old owner
    ///    is what remapping means, and leaving both bound would fire two
    ///    commands on one press. Swapping two commands' keys therefore works,
    ///    because neither is left holding a default by the time this applies.
    public static KeyMap From(
        IReadOnlyDictionary<string, string>? overrides, out IReadOnlyList<string> problems)
    {
        var found = new List<string>();
        problems = found;

        var bindings = new Dictionary<KeyCommand, KeyStroke?>(Defaults._bindings);
        if (overrides is null || overrides.Count == 0) return new KeyMap(bindings);

        var assigned = new Dictionary<KeyStroke, KeyCommand>();
        var chosen = new Dictionary<KeyCommand, KeyStroke?>();

        foreach (var (id, gesture) in overrides)
        {
            if (!TryParseCommand(id, out var command))
            {
                found.Add($"'{id}' is not a command Spektra knows; ignored.");
                continue;
            }
            // An empty gesture is a deliberate "no key for this".
            if (string.IsNullOrWhiteSpace(gesture))
            {
                chosen[command] = null;
                continue;
            }
            if (!KeyStroke.TryParse(gesture, out var stroke))
            {
                found.Add($"'{gesture}' for {id} is not a key combination; kept the default.");
                continue;
            }
            if (assigned.TryGetValue(stroke, out var owner))
            {
                found.Add($"{stroke} is already set for {IdOf(owner)}; kept the default for {id}.");
                continue;
            }
            assigned[stroke] = command;
            chosen[command] = stroke;
        }

        foreach (var (command, stroke) in chosen)
            bindings[command] = stroke;

        // Anything the user did not mention yields its key to whoever took it.
        foreach (var command in Enum.GetValues<KeyCommand>())
        {
            if (chosen.ContainsKey(command)) continue;
            if (bindings[command] is not { } stroke) continue;
            if (!assigned.TryGetValue(stroke, out var owner)) continue;
            bindings[command] = null;
            found.Add($"{stroke} now runs {IdOf(owner)}; {IdOf(command)} has no key.");
        }

        return new KeyMap(bindings);
    }

    /// The gesture bound to a command, or null when it has none.
    public KeyStroke? For(KeyCommand command) =>
        _bindings.TryGetValue(command, out var stroke) ? stroke : null;

    /// The command a keypress runs, or null when nothing is bound to it.
    public KeyCommand? Resolve(KeyStroke stroke)
    {
        foreach (var (command, bound) in _bindings)
            if (bound is { } b && b.Equals(stroke)) return command;
        return null;
    }

    /// What the menus and the shortcut sheet print; empty when unbound, so a
    /// menu row shows no gesture rather than a stale one.
    public string Label(KeyCommand command) => For(command)?.ToString() ?? "";
}
