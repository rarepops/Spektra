using System.Diagnostics.CodeAnalysis;

namespace Spektra.Core;

/// The modifier keys a gesture can carry. Deliberately not Avalonia's
/// KeyModifiers: Core is linked by the CLI too and must not reference a UI
/// framework, and the shell has only to translate at the boundary.
[Flags]
public enum KeyMods
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// One key gesture, as a key NAME plus its modifiers, so bindings can be
/// parsed, merged, matched and rendered without a UI framework. The name is
/// the shell's key enum name ("S", "F5", "Left", "D0"), which is what the
/// shell can hand over for free, and comparison ignores case so a binding
/// still matches a name this file never learned to canonicalize.
public readonly record struct KeyStroke(string Key, KeyMods Mods)
{
    /// Spellings a person would reasonably write, mapped to the name the
    /// shell's key enum actually uses. The digit row is the one nobody could
    /// guess: Avalonia calls the "0" key D0.
    private static readonly Dictionary<string, string> Canonical =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["0"] = "D0", ["1"] = "D1", ["2"] = "D2", ["3"] = "D3", ["4"] = "D4",
            ["5"] = "D5", ["6"] = "D6", ["7"] = "D7", ["8"] = "D8", ["9"] = "D9",
            ["esc"] = "Escape",
            ["del"] = "Delete",
            ["ins"] = "Insert",
            ["pgup"] = "PageUp",
            ["pgdn"] = "PageDown",
            ["return"] = "Enter",
            ["backspace"] = "Back",
        };

    /// Names whose casing cannot be derived by capitalising the first letter.
    /// Only affects how a gesture RENDERS; matching is case-insensitive, so a
    /// name missing from here still works, it just displays as written.
    private static readonly string[] MixedCase =
    [
        "PageUp", "PageDown", "PrintScreen", "CapsLock", "NumLock", "ScrollLock",
        "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4",
        "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9",
    ];

    /// The reverse of Canonical for the two names whose enum spelling would
    /// look wrong in a menu or the shortcut sheet.
    private static string Display(string key) => key switch
    {
        "Escape" => "Esc",
        _ when key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1]) => key[1..],
        _ => key,
    };

    private static readonly string[] ModifierNames =
        ["ctrl", "control", "shift", "alt"];

    /// Reads "Ctrl+Shift+S" and friends. Forgiving about case and spacing, and
    /// about the two names above, because the file this parses is hand-edited.
    /// False for anything that is not exactly a run of modifiers followed by
    /// one key: no key, a modifier used as the key, or two keys.
    public static bool TryParse([NotNullWhen(true)] string? text, out KeyStroke stroke)
    {
        stroke = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+');
        var mods = KeyMods.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i].Trim();
            if (mod.Equals("ctrl", StringComparison.OrdinalIgnoreCase)
                || mod.Equals("control", StringComparison.OrdinalIgnoreCase))
                mods |= KeyMods.Ctrl;
            else if (mod.Equals("shift", StringComparison.OrdinalIgnoreCase))
                mods |= KeyMods.Shift;
            else if (mod.Equals("alt", StringComparison.OrdinalIgnoreCase))
                mods |= KeyMods.Alt;
            else
                return false;
        }

        var key = parts[^1].Trim();
        if (key.Length == 0) return false;
        // "Ctrl" alone, or "Ctrl+Shift": a modifier is not a key.
        if (ModifierNames.Contains(key, StringComparer.OrdinalIgnoreCase)) return false;

        stroke = new KeyStroke(Normalize(key), mods);
        return true;
    }

    private static string Normalize(string key)
    {
        if (Canonical.TryGetValue(key, out var canonical)) return canonical;
        foreach (var mixed in MixedCase)
            if (mixed.Equals(key, StringComparison.OrdinalIgnoreCase)) return mixed;
        if (key.Length == 1) return key.ToUpperInvariant();
        return char.ToUpperInvariant(key[0]) + key[1..];
    }

    /// Ctrl, Shift, Alt in that order whatever order they were written in, so
    /// the menus and the shortcut sheet cannot show two spellings of one
    /// gesture.
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Mods.HasFlag(KeyMods.Ctrl)) parts.Add("Ctrl");
        if (Mods.HasFlag(KeyMods.Shift)) parts.Add("Shift");
        if (Mods.HasFlag(KeyMods.Alt)) parts.Add("Alt");
        parts.Add(Display(Key));
        return string.Join("+", parts);
    }

    public bool Equals(KeyStroke other) =>
        Mods == other.Mods && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Key ?? ""), Mods);
}
