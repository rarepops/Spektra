using System.Text.Json;

namespace Spektra.Core;

/// Reads the user's keybindings.json and hands back the merged key map.
///
/// Its own file rather than a corner of settings.json, and that is not a
/// filing preference: SettingsStore serialises the whole settings object and
/// atomically replaces the file on nearly every preference change, so
/// anything hand-written in there, comments and key order included, would be
/// destroyed the next time somebody nudged the FFT size. Spektra reads this
/// file and never writes it.
public static class KeyMapStore
{
    // Hand-edited, so read it the way a person writes: comments allowed,
    // trailing comma forgiven.
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spektra", "keybindings.json");

    /// Loads the map, collecting everything wrong with the file rather than
    /// failing on the first thing, in the shape PaletteRegistry established
    /// for custom palettes. An absent file is the normal case and reports
    /// nothing; anything else that goes wrong still leaves a complete,
    /// working set of defaults, because a broken file must never cost the
    /// user their keyboard.
    public static KeyMap Load(string path, out IReadOnlyList<string> problems)
    {
        var found = new List<string>();
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var label = Path.GetFileName(path);

        try
        {
            if (!File.Exists(path))
            {
                problems = found;
                return KeyMap.From(null, out _);
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path), Options);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                found.Add($"{label}: expected an object of command names to key combinations.");
                problems = found;
                return KeyMap.From(null, out _);
            }

            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                // A wrong-typed read throws InvalidOperationException, which
                // is outside the usual catch list; checking the kind first is
                // what keeps one number from taking down the whole file.
                if (entry.Value.ValueKind != JsonValueKind.String)
                {
                    found.Add($"{label}: '{entry.Name}' must be a key combination in quotes; ignored.");
                    continue;
                }
                overrides[entry.Name] = entry.Value.GetString() ?? "";
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            found.Add($"{label}: {e.Message}");
            problems = found;
            return KeyMap.From(null, out _);
        }

        var map = KeyMap.From(overrides, out var merge);
        found.AddRange(merge);
        problems = found;
        return map;
    }
}
