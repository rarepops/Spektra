using System.Collections.ObjectModel;
using System.ComponentModel;
using Spektra.Core;

namespace Spektra.App;

public sealed class MainWindowViewModel : StatusViewModel
{
    private FfmpegPaths? _ffmpeg;
    private ITab? _selected;

    private string? _shellErrorText;
    private bool _ffmpegMissing;

    public ObservableCollection<ITab> Tabs { get; } = [];

    public FfmpegPaths? Ffmpeg => _ffmpeg;

    public string? ShellErrorText
    {
        get => _shellErrorText;
        set { if (Set(ref _shellErrorText, value)) RaisePropertyChanged(nameof(HasShellError)); }
    }

    public bool HasShellError => _shellErrorText is not null;
    public bool FfmpegMissing { get => _ffmpegMissing; set => Set(ref _ffmpegMissing, value); }
    public bool ShowHint => Tabs.Count == 0;
    public AppSettings Settings { get; }

    public IReadOnlyList<int> FftSizes { get; } = [512, 1024, 2048, 4096, 8192];

    public int FftSize
    {
        get => Settings.FftSize;
        set
        {
            if (Settings.FftSize == value) return;
            Settings.FftSize = value;
            RaisePropertyChanged(nameof(FftSize));
            SaveSettings();
            foreach (var tab in Tabs)
            {
                if (tab is DocumentViewModel d) d.WindowSize = value;
                else if (tab is ComparisonViewModel c) c.WindowSize = value;
            }
        }
    }

    public IReadOnlyList<WindowFunctionKind> WindowFunctions { get; } = Enum.GetValues<WindowFunctionKind>();

    public IReadOnlyList<string> FolderAnalysisOrders { get; } =
        ["Folder order (top to bottom)", "Smallest files first", "Largest files first"];

    /// Scheduling for the folder tab's Analyze. Read when a run starts, so a
    /// change applies from the next Analyze without touching open tabs.
    public int FolderAnalysisOrderIndex
    {
        get => (int)Settings.FolderAnalysisOrder;
        set
        {
            if (value < 0 || (int)Settings.FolderAnalysisOrder == value) return;
            Settings.FolderAnalysisOrder = (AnalysisOrder)value;
            RaisePropertyChanged(nameof(FolderAnalysisOrderIndex));
            SaveSettings();
        }
    }

    private PaletteRegistry _paletteRegistry = PaletteRegistry.LoadWithCustom();
    private IReadOnlyList<string>? _palettes;
    public IReadOnlyList<string> Palettes => _palettes ??= _paletteRegistry.Names;

    /// Re-reads %APPDATA%\Spektra\palettes (called when Preferences opens, so
    /// newly dropped JSON files show up without a restart).
    public void ReloadPalettes()
    {
        _paletteRegistry = PaletteRegistry.LoadWithCustom();
        _palettes = _paletteRegistry.Names;
        RaisePropertyChanged(nameof(Palettes));
        if (_paletteRegistry.Skipped.Count > 0)
            StatusText = $"Skipped palette file(s): {string.Join("; ", _paletteRegistry.Skipped)}";
    }

    public DisplaySettings ToDisplaySettings() => Settings.ToDisplaySettings(_paletteRegistry);

    /// FFT window shape. Like FftSize this is an analysis setting, so changing it
    /// re-analyzes every open tab.
    public WindowFunctionKind WindowFunction
    {
        get => Settings.WindowFunction;
        set
        {
            if (Settings.WindowFunction == value) return;
            Settings.WindowFunction = value;
            RaisePropertyChanged(nameof(WindowFunction));
            SaveSettings();
            foreach (var tab in Tabs)
            {
                if (tab is DocumentViewModel d) d.Window = value;
                else if (tab is ComparisonViewModel c) c.Window = value;
            }
        }
    }

    // Display-only settings: raise DisplayChanged so the view re-colors without
    // re-analyzing.
    public event Action? DisplayChanged;

    public string Palette
    {
        get => Settings.Palette;
        set { if (SetDisplay(v => Settings.Palette = v, Settings.Palette, value)) RaisePropertyChanged(nameof(Palette)); }
    }

    /// Level-curve exponent: >1 keeps quiet detail darker (tighter peaks),
    /// <1 blooms. Rebakes the palette LUT like any display change.
    public double Tightness
    {
        get => Settings.PaletteGamma;
        set
        {
            value = Math.Clamp(Math.Round(value, 2), 0.5, 2.5);
            if (SetDisplay(v => Settings.PaletteGamma = v, Settings.PaletteGamma, value))
                RaisePropertyChanged(nameof(Tightness));
        }
    }

    public int DbFloor
    {
        get => Settings.DbFloor;
        set
        {
            value = Math.Clamp(value, -140, -40);
            if (SetDisplay(v => Settings.DbFloor = v, Settings.DbFloor, value)) RaisePropertyChanged(nameof(DbFloor));
        }
    }

    public bool LogFrequency
    {
        get => Settings.LogFrequency;
        set { if (SetDisplay(v => Settings.LogFrequency = v, Settings.LogFrequency, value)) RaisePropertyChanged(nameof(LogFrequency)); }
    }

    public bool ShowSpectrum
    {
        get => Settings.ShowSpectrum;
        set { if (SetDisplay(v => Settings.ShowSpectrum = v, Settings.ShowSpectrum, value)) RaisePropertyChanged(nameof(ShowSpectrum)); }
    }

    public bool ShowCrosshair
    {
        get => Settings.ShowCrosshair;
        set { if (SetDisplay(v => Settings.ShowCrosshair = v, Settings.ShowCrosshair, value)) RaisePropertyChanged(nameof(ShowCrosshair)); }
    }

    private bool SetDisplay<T>(Action<T> assign, T current, T value)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        assign(value);
        SaveSettings();
        DisplayChanged?.Invoke();
        return true;
    }

    // --- Updates (notify-only) ---

    private UpdateInfo? _update;
    private static Version CurrentVersion =>
        typeof(MainWindowViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public string CurrentVersionText => UpdateChecker.FormatVersion(CurrentVersion);

    public UpdateInfo? Update
    {
        get => _update;
        private set
        {
            if (!Set(ref _update, value)) return;
            RaisePropertyChanged(nameof(HasUpdate));
            RaisePropertyChanged(nameof(UpdateText));
        }
    }

    public bool HasUpdate => _update is not null;
    public string UpdateText => _update is null
        ? ""
        : $"Spektra {UpdateChecker.FormatVersion(_update.Latest)} is available (you have {UpdateChecker.FormatVersion(CurrentVersion)}).";

    public void DismissUpdate() => Update = null;

    /// The launch policy as a combo row: index 0 = Start new, 1 = Keep last,
    /// matching StartupModes order.
    public IReadOnlyList<string> StartupModes { get; } = ["Start new", "Keep last"];

    public int StartupModeIndex
    {
        get => Settings.KeepLastOnLaunch ? 1 : 0;
        set
        {
            var keep = value == 1;
            if (Settings.KeepLastOnLaunch == keep) return;
            Settings.KeepLastOnLaunch = keep;
            RaisePropertyChanged(nameof(StartupModeIndex));
            SaveSettings();
        }
    }

    public bool CheckForUpdatesOnStartup
    {
        get => Settings.CheckForUpdatesOnStartup;
        set
        {
            if (Settings.CheckForUpdatesOnStartup == value) return;
            Settings.CheckForUpdatesOnStartup = value;
            RaisePropertyChanged(nameof(CheckForUpdatesOnStartup));
            SaveSettings();
        }
    }

    /// Preferences toggle: opened files run the integrity check by
    /// themselves. Copied into each document at creation, so flipping it
    /// applies to files opened afterwards.
    public bool AutoIntegrityCheck
    {
        get => Settings.AutoIntegrityCheck;
        set
        {
            if (Settings.AutoIntegrityCheck == value) return;
            Settings.AutoIntegrityCheck = value;
            RaisePropertyChanged(nameof(AutoIntegrityCheck));
            SaveSettings();
        }
    }

    /// Silent, once-a-day check run at startup. Shows the banner only when an
    /// update is available; stays quiet when up to date or offline.
    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (Settings.LastUpdateCheck is { } last &&
            DateTime.UtcNow - last < TimeSpan.FromDays(1))
            return;

        var result = await UpdateChecker.CheckAsync(CurrentVersion);
        Settings.LastUpdateCheck = DateTime.UtcNow;
        SaveSettings();
        if (result.Outcome == UpdateOutcome.UpdateAvailable)
            Update = result.Info;
    }

    /// Manual check (Help menu). Always contacts GitHub and returns the outcome so
    /// the caller can show a popup, and refreshes the banner when an update exists.
    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        StatusText = "Checking for updates\u2026";
        var result = await UpdateChecker.CheckAsync(CurrentVersion);
        Settings.LastUpdateCheck = DateTime.UtcNow;
        SaveSettings();
        StatusText = "";
        if (result.Outcome == UpdateOutcome.UpdateAvailable)
            Update = result.Info;
        return result;
    }

    public event Action<ITab?>? SelectedChanged;
    public event Action? RecentFilesChanged;

    public ITab? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            if (_selected is not null)
            {
                _selected.IsSelected = false;
                _selected.PropertyChanged -= OnSelectedDocPropertyChanged;
            }
            _selected = value;
            if (value is not null)
            {
                value.IsSelected = true;
                value.PropertyChanged += OnSelectedDocPropertyChanged;
                // A restored tab defers its decode until it is first shown.
                if (value is DocumentViewModel doc) _ = doc.EnsureLoadedAsync();
            }
            StatusText = value?.StatusText ?? "";
            StatusIsError = value?.StatusIsError ?? false;
            RaisePropertyChanged(nameof(Selected));
            RaisePropertyChanged(nameof(CanExportFile));
            SelectedChanged?.Invoke(value);
        }
    }

    /// True only when a single-file tab is active: the "This File as …" export
    /// items disable otherwise (a folder tab or the empty hint has no one file).
    /// The "Folder as …" items open their own picker, so they stay enabled.
    public bool CanExportFile => _selected is DocumentViewModel;

    public MainWindowViewModel()
    {
        Settings = SettingsStore.Load(SettingsStore.DefaultPath);
        // Start new (the default) forgets last run's content before any
        // window reads it; layout and placements survive regardless.
        Settings.ApplyStartupPolicy();
        Tabs.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(ShowHint));
        _ffmpeg = FfmpegLocator.LocateDefault(); // probes app dir, %LOCALAPPDATA%, then PATH
        if (_ffmpeg is null)
        {
            FfmpegMissing = true;
            ShellErrorText = "ffmpeg was not found. Spektra needs ffmpeg + ffprobe to decode audio.";
        }
    }

    private void OnSelectedDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _selected)) return;
        if (e.PropertyName is nameof(ITab.StatusText) or nameof(ITab.StatusIsError))
        {
            StatusText = _selected!.StatusText;
            StatusIsError = _selected.StatusIsError;
        }
    }

    public void OpenFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths) OpenFile(path);
    }

    public void OpenFile(string path)
    {
        if (_ffmpeg is null) return;
        // Same contract as OpenFolder: a file that is already open re-selects
        // its tab rather than building a second view model and decoding the
        // file all over again.
        var existing = Tabs.OfType<DocumentViewModel>().FirstOrDefault(
            d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Selected = existing;
            RememberRecent(path);
            return;
        }
        var doc = new DocumentViewModel(_ffmpeg, path)
        {
            WindowSize = Settings.FftSize,
            Window = Settings.WindowFunction,
            AutoIntegrityCheck = Settings.AutoIntegrityCheck,
        };
        Tabs.Add(doc);
        Selected = doc;
        RememberRecent(path);
        _ = doc.LoadOverviewAsync();
    }

    /// Files and folders share one recent list; the menu tells them apart.
    private void RememberRecent(string path)
    {
        Settings.PushRecent(path);
        SaveSettings();
        RecentFilesChanged?.Invoke();
    }

    public ComparisonViewModel? OpenComparison(string pathA, string pathB)
    {
        if (_ffmpeg is null) return null;
        var cmp = new ComparisonViewModel(_ffmpeg, pathA, pathB) { WindowSize = Settings.FftSize, Window = Settings.WindowFunction };
        Tabs.Add(cmp);
        Selected = cmp;
        cmp.BeginLoad();
        return cmp;
    }

    /// Opens (or re-selects) a folder-audit tab. Opening builds the browse
    /// tree and hydrates cached verdicts (no ffmpeg runs); analysis waits for
    /// an explicit Analyze. An existing tab for the same folder is re-selected
    /// as is, without rebuilding.
    public void OpenFolder(string path)
    {
        if (_ffmpeg is null) return;
        var existing = Tabs.OfType<FolderViewModel>().FirstOrDefault(
            f => string.Equals(f.FolderPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Selected = existing;
            RememberRecent(path);
            return;
        }
        var folder = new FolderViewModel(_ffmpeg, path, Settings);
        folder.OpenFileRequested += file => OpenFile(file);
        Tabs.Add(folder);
        Selected = folder;
        RememberRecent(path);
        folder.OpenTree();
    }

    public IReadOnlyList<DocumentViewModel> OpenDocuments => Tabs.OfType<DocumentViewModel>().ToList();

    /// Records the open tabs so the next launch can reopen them. Comparison
    /// tabs are skipped (they need two files and an alignment), so the saved
    /// index refers to THIS list, not to the live tab strip.
    public void SnapshotSession()
    {
        var paths = new List<string>();
        var selected = 0;
        foreach (var tab in Tabs)
        {
            var path = tab switch
            {
                DocumentViewModel doc => doc.FilePath,
                FolderViewModel folder => folder.FolderPath,
                _ => null,
            };
            if (path is null) continue;
            if (ReferenceEquals(tab, Selected)) selected = paths.Count;
            paths.Add(path);
        }
        Settings.SessionTabs = paths;
        Settings.SessionSelectedIndex = selected;
    }

    /// Reopens the saved tabs. Files are added WITHOUT loading (the selected
    /// one loads via the Selected setter), so a big session costs one decode.
    /// Folders are cheap: OpenTree hydrates from the cache without ffmpeg.
    public void RestoreSessionTabs()
    {
        if (_ffmpeg is null || !Settings.KeepLastOnLaunch) return;
        var resolved = SessionRestore.Resolve(
            Settings.SessionTabs, Settings.SessionSelectedIndex,
            p => File.Exists(p) || Directory.Exists(p));
        if (resolved.Paths.Count == 0) return;

        ITab? toSelect = null;
        foreach (var path in resolved.Paths)
        {
            ITab tab;
            if (Directory.Exists(path))
            {
                var folder = new FolderViewModel(_ffmpeg, path, Settings);
                folder.OpenFileRequested += file => OpenFile(file);
                Tabs.Add(folder);
                folder.OpenTree();
                tab = folder;
            }
            else
            {
                var doc = new DocumentViewModel(_ffmpeg, path)
                {
                    WindowSize = Settings.FftSize,
                    Window = Settings.WindowFunction,
                    AutoIntegrityCheck = Settings.AutoIntegrityCheck,
                };
                Tabs.Add(doc); // deliberately no LoadOverviewAsync here
                tab = doc;
            }
            if (string.Equals(path, resolved.Selected, StringComparison.OrdinalIgnoreCase))
                toSelect = tab;
        }
        Selected = toSelect ?? Tabs.FirstOrDefault();
    }

    public void ClearRecent()
    {
        Settings.RecentFiles.Clear();
        SaveSettings();
        RecentFilesChanged?.Invoke();
    }

    public void SaveSettings()
    {
        try { SettingsStore.Save(SettingsStore.DefaultPath, Settings); }
        catch (Exception ex) { ShellErrorText = $"Could not save settings: {ex.Message}"; }
    }

    public void CloseTab(ITab tab)
    {
        var i = Tabs.IndexOf(tab);
        if (i < 0) return;
        tab.Cancel();
        Tabs.RemoveAt(i);
        if (ReferenceEquals(Selected, tab))
            Selected = Tabs.Count == 0 ? null : Tabs[Math.Min(i, Tabs.Count - 1)];
    }

    public void SelectNext(int direction)
    {
        if (Tabs.Count == 0) return;
        var i = Selected is null
            ? 0
            : (Tabs.IndexOf(Selected) + direction + Tabs.Count) % Tabs.Count;
        Selected = Tabs[i];
    }

    public async Task DownloadFfmpegAsync()
    {
        try
        {
            var progress = new Progress<string>(s => StatusText = s);
            await FfmpegDownloader.InstallAsync(progress, CancellationToken.None);
            _ffmpeg = FfmpegLocator.LocateDefault();
            FfmpegMissing = _ffmpeg is null;
            if (!FfmpegMissing) ShellErrorText = null;
        }
        catch (Exception ex)
        {
            ShellErrorText = $"ffmpeg download failed: {ex.Message}";
        }
    }
}
