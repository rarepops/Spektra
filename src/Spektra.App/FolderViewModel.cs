using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using Spektra.Core;

namespace Spektra.App;

/// One grid row: the flat audit columns plus identity and provenance.
/// Numeric values stay numeric so DataGrid sorting works; display-only
/// strings are separate properties.
public sealed class FolderRow(AuditEntry entry, string rootFolder)
{
    public AuditRow Row { get; } = entry.Row;
    public string FullPath { get; } = entry.Target.Path;
    public bool HasProblem { get; } = entry.HasProblem;
    public bool FromCache { get; } = entry.FromCache;

    /// Path relative to the audited root (Row.File is the bare name, which
    /// says nothing about where a row lives in a deep tree). Sorting the
    /// column therefore groups rows by folder.
    public string File { get; } = Reporting.RelativeFile(rootFolder, entry.Target.Path);
    public string Codec => Row.Codec ?? "";
    public int? SampleRateHz => Row.SampleRateHz;
    public double? BitrateKbps => Row.BitrateBps / 1000.0;
    public double DurationSeconds => Row.DurationSeconds;
    public string DurationText => TimeSpan.FromSeconds(DurationSeconds).ToString(
        DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    public string Bandwidth => Row.Bandwidth;
    public double? CutoffKhz => Row.CutoffHz / 1000.0;
    public string Integrity => Row.Integrity;
    public int DecodeErrors => Row.DecodeErrors;
    public int Dropouts => Row.Dropouts;
    public bool Truncated => Row.Truncated;
    public string? Error => Row.Error;

    public bool BandwidthIsBad => Row.Error is not null
        || (Row.Bandwidth is "Lossy"
            && TranscodeCheck.IsSuspectLossy(Row.Codec, Row.BitrateBps, Row.Channels, Row.CutoffHz));

    public RowSeverity Severity { get; } = FolderAudit.Severity(entry);
    public bool BandwidthIsUpsampled => Row.Bandwidth is "Upsampled";
    public bool IntegrityIsBad => Row.Integrity is "Corrupt" or "Error";

    /// The File cell's marker dot: the row's whole verdict, same rule and
    /// palette as the tree's file dot (the Integrity dot colors integrity
    /// alone, so without this the tree could look "worse" than the grid).
    public IBrush SeverityBrush { get; } = NodeMarkers.ForEntry(entry);

    /// The integrity cell's marker dot, using the same palette as the tree.
    public IBrush IntegrityBrush => Row.Integrity switch
    {
        "Ok" => NodeMarkers.Clean,
        "Suspect" => NodeMarkers.Suspect,
        "Corrupt" or "Error" => NodeMarkers.Problem,
        _ => NodeMarkers.NotAnalyzed,
    };
}

/// A folder-audit tab built around an explicit-Analyze workflow: dropping a
/// folder builds the checkbox browse tree and paints any cached verdicts
/// (OpenTree, no ffmpeg), then analysis runs only the checked worklist on
/// demand (Analyze). Entries stream into a sortable grid with byte-weighted
/// progress and an ETA, and change-driven rollups keep the tree cheap.
public sealed class FolderViewModel : TabViewModelBase
{
    private readonly FfmpegPaths _ffmpeg;
    private readonly AppSettings _settings;

    public ObservableCollection<TreeNodeViewModel> Roots { get; } = [];

    private readonly Dictionary<string, FileNodeViewModel> _fileByPath =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AuditTarget> _targetByPath =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _rowIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FolderNodeViewModel> _folders = [];
    // Files in tree display order (depth-first, folders before siblings):
    // the "Folder order" schedule and the cache replay follow this, so what
    // runs top to bottom is what the tree shows top to bottom.
    private readonly List<FileNodeViewModel> _filesInOrder = [];

    private readonly List<AuditEntry> _pending = [];   // UI thread only
    private readonly DispatcherTimer _flushTimer;
    private readonly Stopwatch _analysisClock = new();

    private int _worklistCount, _doneFiles, _analyzedFiles, _problems, _treeTotal;
    private long _totalBytes, _cachedBytes, _analyzedBytes;
    private bool _cacheUnavailable;
    private CancellationTokenSource? _cts;

    public string FolderPath { get; }
    public ObservableCollection<FolderRow> Rows { get; } = [];

    public FolderViewModel(FfmpegPaths ffmpeg, string folderPath, AppSettings settings)
    {
        _ffmpeg = ffmpeg;
        _settings = settings;
        FolderPath = folderPath;
        TabTitle = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
        if (TabTitle.Length == 0) TabTitle = folderPath; // drive roots like Z:\
        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _flushTimer.Tick += (_, _) => Flush();
    }

    public override string TabTitle { get; }
    public override bool IsFolderTab => true;

    // Cancels an in-flight Analyze; a no-op while only browsing (OpenTree runs
    // no cancellable ffmpeg work, so _cts is null until the first Analyze).
    public override void Cancel() => _cts?.Cancel();

    public IReadOnlyList<string> FilterOptions { get; } = ["All files", "Suspect + worse", "Problems only"];

    private int _filterIndex;
    /// Index into FilterOptions; doubles as the minimum RowSeverity to show
    /// (a tier shows itself and everything worse).
    public int FilterIndex { get => _filterIndex; set => Set(ref _filterIndex, value); }

    private double _progressFraction;
    public double ProgressFraction { get => _progressFraction; private set => Set(ref _progressFraction, value); }

    private bool _isAnalyzing;
    public override bool IsAnalyzing => _isAnalyzing;

    private void SetAnalyzing(bool value)
    {
        if (!Set(ref _isAnalyzing, value, nameof(IsAnalyzing)))
            return;
        RaisePropertyChanged(nameof(CanAnalyze));
    }

    public bool CanAnalyze => !IsAnalyzing;

    // The folder tab currently running Analyze, if any: one run at a time
    // across every tab, since a run already fans ffmpeg out across most
    // cores and a second would fight the first for them and wreck both
    // ETAs. UI-thread only, like the rest of the tab state.
    private static FolderViewModel? _analyzingTab;

    /// Live run readout shown beside the progress bar: percent, files done,
    /// and the ETA once it means something. Empty when idle.
    private string _analyzeProgress = "";
    public string AnalyzeProgress
    {
        get => _analyzeProgress;
        private set => Set(ref _analyzeProgress, value);
    }

    private string? _scopeFolder;
    public string? ScopeFolder
    {
        get => _scopeFolder;
        private set
        {
            if (!Set(ref _scopeFolder, value))
                return;
            RaisePropertyChanged(nameof(IsScoped));
            RaisePropertyChanged(nameof(ScopeBreadcrumb));
        }
    }

    public bool IsScoped => _scopeFolder is not null;

    public string ScopeBreadcrumb => _scopeFolder is null
        ? ""
        : "Scope: " + Reporting.RelativeFile(FolderPath, _scopeFolder);

    public void Drilldown(string folder) => ScopeFolder = folder;
    public void ShowAll() => ScopeFolder = null;

    /// Widen the scope one folder toward the root; at a top-level folder
    /// (or with no scope) this clears the scope entirely.
    public void DrillUp()
    {
        if (_scopeFolder is null)
            return;
        var parent = Path.GetDirectoryName(_scopeFolder);
        if (parent is not null && PathScope.IsUnder(parent, FolderPath))
            ScopeFolder = parent;
        else
            ShowAll();
    }

    public void SelectAll() => SetAllChecks(true);
    public void SelectNone() => SetAllChecks(false);

    private void SetAllChecks(bool value)
    {
        foreach (var file in _fileByPath.Values)
            file.SetCheckedSilently(value);
        foreach (var folder in _folders)
            folder.SetCheckedSilently(value);
    }

    /// (path, checkIntegrity): the flag asks the opener to run the integrity
    /// check immediately, so the lane under the spectrogram is populated on
    /// arrival. True for Corrupt/Suspect rows and rows with silent gaps
    /// (informational, but worth seeing where); Error rows failed to decode
    /// and would just fail again.
    public event Action<string, bool>? OpenFileRequested;
    public void RequestOpen(FolderRow row) =>
        OpenFileRequested?.Invoke(row.FullPath,
            row.Integrity is "Corrupt" or "Suspect" || row.Dropouts > 0);

    /// Export mirrors the grid: File carries the path relative to the
    /// audited root, not the bare name, so rows can be located again.
    public IReadOnlyList<AuditRow> ExportRows() =>
        Rows.Select(r => r.Row with { File = r.File }).ToList();

    /// Drop entry point: walk the folder, build the tree, and paint any cached
    /// verdicts. No ffmpeg runs here; analysis waits for Analyze.
    public void OpenTree() => _ = OpenTreeAsync();

    private async Task OpenTreeAsync()
    {
        if (IsAnalyzing) return; // rebuilding would clear the maps a live run is using
        _cacheUnavailable = false; // a fresh attempt: do not carry a stale cache-open failure
        try
        {
            var targets = await Task.Run(() =>
                FolderAudit.CollectTargets(FolderPath, recursive: true));
            if (targets.Length == 0)
            {
                StatusText = "No audio files found in this folder.";
                return;
            }

            var paths = targets.Select(t => t.Path).ToList();
            var forest = await Task.Run(() => FolderTree.Build(FolderPath, paths));

            Roots.Clear();
            _fileByPath.Clear();
            _filesInOrder.Clear();
            _folders.Clear();
            _targetByPath.Clear();
            _rowIndex.Clear();
            Rows.Clear();

            foreach (var node in BuildNodes(forest))
                Roots.Add(node);
            foreach (var target in targets)
                _targetByPath[target.Path] = target;
            _treeTotal = _fileByPath.Count;

            foreach (var folder in _folders)
                folder.RefreshRollup();

            AuditCache? cache = null;
            try { cache = AuditCache.Open(AuditCache.DefaultPath); }
            catch (Exception e) when (e is not OutOfMemoryException) { _cacheUnavailable = true; }

            try
            {
                if (cache is not null)
                {
                    var orderedTargets = _filesInOrder
                        .Select(f => _targetByPath[f.FullPath]).ToList();
                    var hydrated = await Task.Run(() =>
                        FolderAudit.HydrateFromCache(cache, orderedTargets));
                    for (var i = 0; i < hydrated.Length; i++)
                        if (hydrated[i] is { } entry)
                            _pending.Add(entry);
                    Flush();
                    AuditCache live = cache;
                    await Task.Run(() => live.PruneFolder(FolderPath, paths));
                }
            }
            finally
            {
                cache?.Dispose();
            }

            SetSummaryStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetErrorStatus($"Could not read the folder: {ex.Message}");
        }
    }

    private IEnumerable<TreeNodeViewModel> BuildNodes(
        IEnumerable<FolderTreeNode> forest)
    {
        foreach (var node in forest)
            yield return Wrap(node);
    }

    private TreeNodeViewModel Wrap(FolderTreeNode node)
    {
        if (node is FolderTreeFile file)
        {
            var vm = new FileNodeViewModel(file.Name, file.FullPath);
            _fileByPath[file.FullPath] = vm;
            _filesInOrder.Add(vm);
            return vm;
        }

        var folder = (FolderTreeFolder)node;
        var children = folder.Children.Select(Wrap).ToList();
        var folderVm = new FolderNodeViewModel(folder.Name, folder.FullPath, children);
        _folders.Add(folderVm);
        return folderVm;
    }

    /// Analyze exactly the checked files. fresh ignores cached rows and
    /// re-analyzes; otherwise only files without a verdict yet are queued.
    public void Analyze(bool fresh) => _ = AnalyzeAsync(fresh);

    private async Task AnalyzeAsync(bool fresh)
    {
        if (IsAnalyzing) return; // a second F5/Analyze must never dispose the live CTS
        if (_analyzingTab is not null)
        {
            StatusText = $"Already analyzing \"{_analyzingTab.TabTitle}\" · one analysis runs at a time";
            return;
        }
        _cacheUnavailable = false; // a fresh attempt: do not carry a stale cache-open failure
        var worklist = _filesInOrder
            .Where(f => f.IsChecked && (fresh || f.Entry is null))
            .Select(f => _targetByPath[f.FullPath])
            .ToList();

        if (worklist.Count == 0)
        {
            StatusText = _fileByPath.Values.Any(f => f.IsChecked)
                ? "All checked files are already analyzed · Shift+F5 or Shift+click Analyze for a fresh pass"
                : "Nothing to analyze: check files or folders first.";
            return;
        }

        var ordered = FolderAudit.OrderWorklist(worklist, _settings.FolderAnalysisOrder);

        SetAnalyzing(true);
        _analyzingTab = this;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _worklistCount = ordered.Count;
        _doneFiles = 0;
        _analyzedFiles = 0;
        _totalBytes = ordered.Sum(t => t.SizeBytes);
        _cachedBytes = 0;
        _analyzedBytes = 0;
        _analysisClock.Restart();
        _flushTimer.Start();

        AuditCache? cache = null;
        try
        {
            try { cache = AuditCache.Open(AuditCache.DefaultPath); }
            catch (Exception e) when (e is not OutOfMemoryException) { _cacheUnavailable = true; }

            var progress = new Progress<AuditEntry>(OnEntry); // marshals to this (UI) thread
            var ct = _cts.Token;
            var localCache = cache;
            await Task.Run(() => FolderAudit.Run(
                _ffmpeg, ordered, Jobs, localCache, fresh, progress, ct), ct);

            Flush();
            SetSummaryStatus();
        }
        catch (OperationCanceledException)
        {
            Flush();
            StatusText =
                $"Cancelled at {_doneFiles} of {_worklistCount} · partial results kept";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetErrorStatus($"Analysis failed: {ex.Message}");
        }
        finally
        {
            _flushTimer.Stop();
            _analyzingTab = null;
            SetAnalyzing(false);
            AnalyzeProgress = "";
            cache?.Dispose();
            Flush(); // drain stragglers; the IsAnalyzing guard keeps the final status intact
        }
    }

    private static int Jobs => Math.Max(1, (int)(Environment.ProcessorCount * 0.8));

    private void OnEntry(AuditEntry entry)
    {
        _pending.Add(entry);
        _doneFiles++;
        if (entry.FromCache)
        {
            _cachedBytes += entry.Target.SizeBytes;
        }
        else
        {
            _analyzedFiles++;
            _analyzedBytes += entry.Target.SizeBytes;
        }
    }

    /// Batched UI update: appending per entry would flood the grid when a
    /// large cache replay lands. Apply pending entries and refresh touched
    /// rollups first, then (only while analyzing) update progress and the ETA;
    /// ScanProgress.Fraction returns 1 at zero bytes, so the early return keeps
    /// the bar empty during the browse/hydrate phase.
    private void Flush()
    {
        if (_pending.Count > 0)
        {
            var touched = new HashSet<FolderNodeViewModel>();
            foreach (var entry in _pending)
            {
                ApplyEntry(entry);
                MarkRollupDirty(entry.Target.Path, touched);
            }
            _pending.Clear();
            foreach (var folder in touched)
                folder.RefreshRollup();
            _problems = Rows.Count(r => r.HasProblem);
        }

        if (!IsAnalyzing)
            return;   // progress + ETA only mean something while analyzing

        var progress = new ScanProgress(
            _totalBytes, _cachedBytes, _analyzedBytes, _analyzedFiles,
            _analysisClock.Elapsed.TotalSeconds);
        ProgressFraction = progress.Fraction;
        if (_worklistCount > 0)
        {
            var eta = progress.EtaSeconds is { } s ? $" · ~{FormatEta(s)} left" : "";
            AnalyzeProgress =
                $"{progress.Fraction * 100:0}% · {_doneFiles} of {_worklistCount}{eta}";
        }
    }

    private void ApplyEntry(AuditEntry entry)
    {
        var path = entry.Target.Path;
        var row = new FolderRow(entry, FolderPath);
        if (_rowIndex.TryGetValue(path, out var i))
        {
            Rows[i] = row;
        }
        else
        {
            _rowIndex[path] = Rows.Count;
            Rows.Add(row);
        }
        if (_fileByPath.TryGetValue(path, out var node))
            node.Entry = entry;
    }

    private void MarkRollupDirty(string path, HashSet<FolderNodeViewModel> touched)
    {
        if (!_fileByPath.TryGetValue(path, out var file))
            return;
        var ancestor = file.Parent as FolderNodeViewModel;
        while (ancestor is not null)
        {
            touched.Add(ancestor);
            ancestor = ancestor.Parent as FolderNodeViewModel;
        }
    }

    private void SetSummaryStatus() =>
        StatusText =
            $"{_treeTotal:N0} files · {Rows.Count:N0} analyzed · {_problems} problems{CacheNote()}";

    private string CacheNote() => _cacheUnavailable ? " · cache unavailable" : "";

    private static string FormatEta(double seconds) => seconds switch
    {
        < 90 => $"{Math.Max(1, Math.Round(seconds / 10) * 10)} s",
        < 5400 => $"{Math.Round(seconds / 60)} min",
        _ => $"{seconds / 3600:0.0} h",
    };
}
