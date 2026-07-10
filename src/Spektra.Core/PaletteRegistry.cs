using System.Globalization;
using System.Text.Json;

namespace Spektra.Core;

/// One gradient stop after resolution: Position is a fraction (0..1) of the
/// displayed dB range, floor to ceiling.
public sealed record PaletteStop(double Position, byte R, byte G, byte B);

/// Bakes gradient stops into a BGRA lookup table and samples it. The LUT maps
/// the normalized dB position to a color, so the per-pixel hot path is an
/// array index instead of a lerp.
public static class PaletteLut
{
    public const int Size = 1024;

    public static IReadOnlyList<PaletteStop> EvenStops((byte R, byte G, byte B)[] anchors) =>
        [.. anchors.Select((a, i) => new PaletteStop(
            anchors.Length == 1 ? 0 : (double)i / (anchors.Length - 1), a.R, a.G, a.B))];

    /// `stops` must be sorted by Position and non-empty; positions outside the
    /// outermost stops fill flat with the nearest stop's color.
    public static uint[] Bake(IReadOnlyList<PaletteStop> stops, int size = Size)
    {
        var lut = new uint[size];
        var k = 0;
        for (var i = 0; i < size; i++)
        {
            var t = (double)i / (size - 1);
            while (k < stops.Count - 2 && t > stops[k + 1].Position) k++;
            var a = stops[k];
            var b = stops[Math.Min(k + 1, stops.Count - 1)];
            var f = b.Position <= a.Position
                ? (t >= b.Position ? 1.0 : 0.0)
                : Math.Clamp((t - a.Position) / (b.Position - a.Position), 0, 1);
            var r = (uint)(a.R + (b.R - a.R) * f);
            var g = (uint)(a.G + (b.G - a.G) * f);
            var bl = (uint)(a.B + (b.B - a.B) * f);
            lut[i] = 0xFF000000u | (r << 16) | (g << 8) | bl;
        }
        return lut;
    }

    public static uint Sample(uint[] lut, float db, float floor, float ceil)
    {
        if (ceil <= floor) ceil = floor + 1f;
        var t = (Math.Clamp(db, floor, ceil) - floor) / (ceil - floor);
        return lut[(int)(t * (lut.Length - 1))];
    }
}

/// Built-in palettes plus user palettes loaded from JSON files. A custom file
/// is `{ "name": "...", "anchors": [...] }` where anchors are hex strings
/// (evenly spaced) or stop objects `{ "color": "#..", "at": 0..1 }` /
/// `{ "color": "#..", "db": -60 }`. `db` stops re-resolve against the current
/// display floor/ceiling, so the color stays glued to its dB level.
public sealed class PaletteRegistry
{
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spektra", "palettes");

    // Raw stop values are `at` fractions or absolute dB depending on DbPinned.
    private sealed record CustomEntry(IReadOnlyList<(double Raw, byte R, byte G, byte B)> Stops, bool DbPinned);

    private readonly Dictionary<string, PaletteKind> _builtIns;
    private readonly Dictionary<string, CustomEntry> _customs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Names { get; private set; }
    public IReadOnlyList<string> Skipped { get; }

    private PaletteRegistry(List<string> skipped)
    {
        _builtIns = Enum.GetValues<PaletteKind>()
            .ToDictionary(k => k.ToString(), k => k, StringComparer.OrdinalIgnoreCase);
        Names = [.. Enum.GetValues<PaletteKind>().Select(k => k.ToString())];
        Skipped = skipped;
    }

    public bool Has(string? name) =>
        name is not null && (_builtIns.ContainsKey(name) || _customs.ContainsKey(name));

    /// Unknown or null names fall back to Magma, so a deleted custom palette
    /// degrades gracefully instead of failing.
    public uint[] BakeLut(string? name, float floorDb, float ceilDb = 0f)
    {
        if (name is not null && _customs.TryGetValue(name, out var custom))
            return PaletteLut.Bake(Resolve(custom, floorDb, ceilDb));
        var kind = name is not null && _builtIns.TryGetValue(name, out var k) ? k : PaletteKind.Magma;
        return PaletteLut.Bake(PaletteLut.EvenStops(Colormaps.AnchorsFor(kind)));
    }

    private static IReadOnlyList<PaletteStop> Resolve(CustomEntry entry, float floor, float ceil)
    {
        if (ceil <= floor) ceil = floor + 1f;
        return [.. entry.Stops
            .Select(s => new PaletteStop(
                Math.Clamp(entry.DbPinned ? (s.Raw - floor) / (ceil - floor) : s.Raw, 0, 1),
                s.R, s.G, s.B))
            .OrderBy(s => s.Position)];
    }

    /// Loads built-ins plus every valid `*.json` in `dir` (default:
    /// %APPDATA%\Spektra\palettes). Invalid files and name collisions are
    /// skipped and listed in `Skipped`, never fatal.
    public static PaletteRegistry LoadWithCustom(string? dir = null)
    {
        dir ??= DefaultDir;
        var customNames = new List<string>();
        var skipped = new List<string>();
        var registry = new PaletteRegistry(skipped);

        if (!Directory.Exists(dir)) return registry;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var label = Path.GetFileName(file);
            try
            {
                var (name, entry) = ParseFile(file);
                if (registry._builtIns.ContainsKey(name))
                    skipped.Add($"{label}: name '{name}' collides with a built-in");
                else if (registry._customs.ContainsKey(name))
                    skipped.Add($"{label}: duplicate name '{name}'");
                else
                {
                    registry._customs.Add(name, entry);
                    customNames.Add(name);
                }
            }
            catch (Exception e) when (e is JsonException or FormatException or IOException)
            {
                skipped.Add($"{label}: {e.Message}");
            }
        }
        customNames.Sort(StringComparer.OrdinalIgnoreCase);
        registry.Names = [.. registry.Names, .. customNames];
        return registry;
    }

    private static (string Name, CustomEntry Entry) ParseFile(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("the root must be a JSON object");
        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()!
            : Path.GetFileNameWithoutExtension(file);
        if (!root.TryGetProperty("anchors", out var anchors) || anchors.ValueKind != JsonValueKind.Array)
            throw new FormatException("missing an 'anchors' array");
        var items = anchors.EnumerateArray().ToList();
        if (items.Count < 2) throw new FormatException("needs at least 2 anchors");

        if (items.All(i => i.ValueKind == JsonValueKind.String))
        {
            var stops = items
                .Select((i, idx) => WithColor(i.GetString()!, (double)idx / (items.Count - 1)))
                .ToList();
            return (name, new CustomEntry(stops, DbPinned: false));
        }

        if (items.All(i => i.ValueKind == JsonValueKind.Object))
        {
            var atCount = items.Count(i => i.TryGetProperty("at", out _));
            var dbCount = items.Count(i => i.TryGetProperty("db", out _));
            var dbPinned = dbCount == items.Count && atCount == 0;
            var atBased = atCount == items.Count && dbCount == 0;
            if (!dbPinned && !atBased)
                throw new FormatException("stops must all use 'at' or all use 'db'");
            var stops = items.Select(i =>
            {
                if (!i.TryGetProperty("color", out var c) || c.ValueKind != JsonValueKind.String)
                    throw new FormatException("every stop needs a 'color'");
                var raw = i.GetProperty(dbPinned ? "db" : "at").GetDouble();
                return WithColor(c.GetString()!, raw);
            }).ToList();
            return (name, new CustomEntry(stops, dbPinned));
        }

        throw new FormatException("anchors must be all hex strings or all stop objects");
    }

    private static (double Raw, byte R, byte G, byte B) WithColor(string hex, double raw)
    {
        var s = hex.StartsWith('#') ? hex[1..] : null;
        if (s is { Length: 3 })
            s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        if (s is not { Length: 6 } || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"bad color '{hex}' (use #RGB or #RRGGBB)");
        return (raw, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
