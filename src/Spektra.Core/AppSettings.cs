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
    // Every opened file runs the integrity check by itself; off makes Ctrl+I
    // a purely on-demand check again.
    public bool AutoIntegrityCheck { get; set; } = true;

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

    // Launch content policy for every window. Start new (the default, false)
    // opens the app and its tool windows empty; keep last reopens the previous
    // tabs, Duplicate Detective scan roots, and manifest folder. Launching
    // with a file argument (double click, drag, CLI) skips tab restore either
    // way, so a targeted open stays targeted. SessionTabs holds file and
    // folder paths in tab order; comparison tabs are not saved.
    // SessionSelectedIndex indexes THIS list, not the live tab strip, which
    // also holds the unsaved comparison tabs.
    public bool KeepLastOnLaunch { get; set; }
    public List<string>? SessionTabs { get; set; }
    public int SessionSelectedIndex { get; set; }

    /// Applied once at app launch, before any window reads remembered content.
    /// Start new forgets WHAT was open (tabs, scan roots, manifest folder);
    /// HOW things look (layout, column widths, window placements, recents)
    /// always survives. In-memory only: using the app repopulates these, so a
    /// mid-session close and reopen of a tool window still restores, and the
    /// next launch starts clean again. The CLI never applies this.
    public void ApplyStartupPolicy()
    {
        if (KeepLastOnLaunch) return;
        SessionTabs = null;
        SessionSelectedIndex = 0;
        DuplicateRoots = null;
        FolderManifestFolder = null;
    }

    // Duplicate Detective window state: scan roots and window placement persist
    // across sessions. Null = the window has never been used.
    public List<string>? DuplicateRoots { get; set; }
    public WindowPlacement? DuplicatesWindow { get; set; }

    // Folder Manifest window state: the last listed folder and the window
    // placement persist so the window reopens where it left off. Column widths
    // follow the FolderColumnWidths shape: keyed by header, absent key = the
    // XAML default, null = never customized.
    public string? FolderManifestFolder { get; set; }
    public WindowPlacement? FolderManifestWindow { get; set; }
    public Dictionary<string, double>? ManifestColumnWidths { get; set; }

    /// Reads one saved column width: honored only when finite and inside
    /// [min, max]; anything else (never saved, or hand-edited junk in the
    /// settings file) yields the caller's default, so a bad value can never
    /// collapse or explode a column.
    public static double SavedColumnWidth(
        Dictionary<string, double>? widths, string header, double fallback, double min, double max) =>
        widths is not null && widths.TryGetValue(header, out var px) && double.IsFinite(px)
            ? Math.Clamp(px, min, max)
            : fallback;

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
