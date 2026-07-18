namespace Spektra.Core;

public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);

public sealed class AppSettings
{
    public const int MaxRecent = 10;

    public WindowPlacement? Window { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public int FftSize { get; set; } = 2048;

    // Folder tab layout, harvested when the window closes. Column widths in
    // px keyed by header (a header absent from the map keeps its default
    // width, so new columns are unaffected by old files); null = never
    // customized, keep the XAML defaults.
    public Dictionary<string, double>? FolderColumnWidths { get; set; }
    public double? FolderTreeWidth { get; set; }

    // Analysis
    public WindowFunctionKind WindowFunction { get; set; } = WindowFunctionKind.Hann;
    // Scheduling for the folder tab's Analyze; read when a run starts.
    public AnalysisOrder FolderAnalysisOrder { get; set; } = AnalysisOrder.FolderOrder;

    // Display. Palette is a name: a built-in (old settings files stored the
    // enum by name, so they load unchanged) or a custom palette from
    // PaletteRegistry; unknown names fall back to Turbo at bake time.
    public string Palette { get; set; } = "Turbo";
    // Level-curve exponent ("Tightness" in Preferences): >1 keeps quiet
    // detail darker so peaks read tighter, <1 blooms.
    public double PaletteGamma { get; set; } = 1.0;
    public int DbFloor { get; set; } = -120;
    public bool LogFrequency { get; set; }
    public bool ShowSpectrum { get; set; }
    public bool ShowCrosshair { get; set; } = true;

    // Updates
    public bool CheckForUpdatesOnStartup { get; set; }
    public DateTime? LastUpdateCheck { get; set; }

    // Dedup Destroyer window state: scan roots and window placement persist
    // across sessions. Null = the window has never been used.
    public List<string>? DuplicateRoots { get; set; }
    public WindowPlacement? DuplicatesWindow { get; set; }

    /// The registry resolves the palette name (built-in or custom) to a baked
    /// LUT; db-pinned custom stops resolve against the current floor.
    public DisplaySettings ToDisplaySettings(PaletteRegistry palettes) => new(
        palettes.BakeLut(Palette, DbFloor, gamma: PaletteGamma),
        DbFloor, LogFrequency, ShowSpectrum, ShowCrosshair);

    public void PushRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecent)
            RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
    }
}
