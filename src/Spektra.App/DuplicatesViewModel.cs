using System.Collections.ObjectModel;
using Avalonia.Media;
using Spektra.Core;

namespace Spektra.App;

/// One duplicate group for display: headline plus foldout members.
public sealed class DupeGroupItem(DupesGroupReport report)
{
    /// Retained for the results filter, which matches on the raw label and
    /// member paths rather than the composed display strings.
    public DupesGroupReport Report { get; } = report;

    /// The copies NOT worth keeping, for the clipboard batch verb: feed them
    /// to another tool; Spektra itself never touches files.
    public IReadOnlyList<string> LoserPaths { get; } =
        [.. report.Group.Members.Where(m => !report.Quality.Winners.Contains(m.Path)).Select(m => m.Path)];

    public IReadOnlyList<string> AllPaths { get; } =
        [.. report.Group.Members.Select(m => m.Path)];

    public string Headline { get; } =
        $"{report.Group.Label} · {report.Group.Members.Count} files · sameness {report.Group.SamenessTier} · reclaim {Reporting.FormatBytes(report.ReclaimableBytes)}";
    public string QualityLine { get; } = $"quality {report.Quality.Confidence}: {report.Quality.Reason}";
    public IReadOnlyList<DupeMemberItem> Members { get; } =
        [.. report.Group.Members.Select(m => new DupeMemberItem(m, report))];
    /// Small groups open expanded; big ones start folded.
    public bool IsSmall { get; } = report.Group.Members.Count <= 3;
}

/// The verdict facts are separate properties (not one composed line) so the
/// window can lay them out as fixed-width lanes that align across rows.
public sealed class DupeMemberItem : IFileItem
{
    public DupeMemberItem(DuplicateMember member, DupesGroupReport report)
    {
        var row = report.Rows[member.Path];
        Path = member.Path;
        IsWinner = report.Quality.Winners.Contains(member.Path);
        WinnerPath = report.Quality.Winners.Count > 0 ? report.Quality.Winners[0] : null;
        FoundByAudio = member.FoundByAudio;
        var cutoff = row.CutoffHz is { } c ? $" {c / 1000.0:0.0}k" : "";
        Codec = row.Codec ?? "";
        Bandwidth = $"{row.Bandwidth}{cutoff}";
        Integrity = row.Integrity;
        SizeText = Reporting.FormatBytes(report.Sizes[member.Path]);
        SamenessText = $"sameness {member.Sameness:0.00}";
        IntegrityBrush = row.Integrity switch
        {
            "Ok" => NodeMarkers.Clean,
            "Suspect" => NodeMarkers.Suspect,
            "Corrupt" or "Error" => NodeMarkers.Problem,
            _ => NodeMarkers.NotAnalyzed,
        };
    }

    public string Path { get; }
    public string Codec { get; }
    public string Bandwidth { get; }
    public string Integrity { get; }
    public string SizeText { get; }
    public string SamenessText { get; }
    public bool IsWinner { get; }
    public bool FoundByAudio { get; }
    public IBrush IntegrityBrush { get; }

    /// The group's best copy, for the compare verb; null when the ranking
    /// produced no winner.
    public string? WinnerPath { get; }
    /// Comparing the winner with itself is meaningless, so the verb greys.
    public bool CanCompareWithWinner => !IsWinner && WinnerPath is not null;

    // Explicit: the display binding is Path, and renaming it would break the
    // XAML. The shared context-menu actions see it as FullPath.
    string IFileItem.FullPath => Path;
}

/// One file that exists under only one scan root, shown in that root's column
/// of the folder diff. The name is relative to the root because the column
/// header already carries the root.
public sealed class DiffFileItem : IFileItem
{
    public DiffFileItem(UnpairedFile file)
    {
        Path = file.Path;
        Relative = file.Root.Length > 0 && file.Path.StartsWith(file.Root, StringComparison.OrdinalIgnoreCase)
            ? file.Path[file.Root.Length..].TrimStart('\\', '/')
            : file.Path;
        var cutoff = file.Row.CutoffHz is { } c ? $" {c / 1000.0:0.0}k" : "";
        Facts = $"{file.Row.Codec ?? "?"} · {file.Row.Bandwidth}{cutoff} · {Reporting.FormatBytes(file.SizeBytes)}";
        HasTwin = file.TwinPath is not null;
        TwinNote = file.TwinPath is { } twin
            ? string.Equals(
                System.IO.Path.GetFileName(twin), System.IO.Path.GetFileName(file.Path),
                StringComparison.OrdinalIgnoreCase)
                ? "the other folder has this name too · the audio differs"
                : $"the other folder has: {System.IO.Path.GetFileName(twin)}"
            : null;
    }

    public string Path { get; }
    public string Relative { get; }
    public string Facts { get; }

    /// Another scan root holds a file naming the same track, so this is a
    /// track both folders have and Spektra could not confirm by ear, NOT a
    /// track only this folder has. Saying so is the whole point: on the
    /// owner's library 278 of 319 files in these columns were of this kind,
    /// which made the 41 real ones impossible to see.
    public bool HasTwin { get; }
    public string? TwinNote { get; }

    string IFileItem.FullPath => Path;
}

/// One side of the folder diff: a scan root and the files only it has. Roots
/// with nothing unique still get a column, because "this folder has no extras"
/// and "this folder is not in the comparison" must not look the same.
public sealed class DiffColumnItem(
    string root, string label, IReadOnlyList<DiffFileItem> files, long bytes, int alsoThere)
{
    public string Root { get; } = root;

    /// Handed in rather than derived from this one root, because the shortest
    /// label that identifies a folder depends on what it is sitting next to.
    /// The leaf name alone fails the common case: a diff usually compares two
    /// copies of one library, whose leaves match by construction, and two
    /// columns both headed "Album" tell the reader nothing. ScopeLabel
    /// .Distinguish gives each root the shortest unique one.
    public string Label { get; } = label;
    public IReadOnlyList<DiffFileItem> Files { get; } = files;

    /// Always names both numbers, and names them even when one list is
    /// hidden. A count of what is left, on its own, cannot be told apart from
    /// a folder that really has nothing extra, and the two mean opposite
    /// things to someone deciding what to copy across.
    public string Summary { get; } = Describe(files.Count(f => !f.HasTwin), alsoThere, bytes);
    public bool HasFiles { get; } = files.Count > 0;

    /// Counted whether or not they are being shown, so the footer can say so
    /// either way.
    public int AlsoThere { get; } = alsoThere;

    private static string Describe(int only, int alsoThere, long bytes)
    {
        var here = only == 0 ? "nothing only here" : $"{only} file(s) only here · {Reporting.FormatBytes(bytes)}";
        return alsoThere == 0 ? here : $"{here} · {alsoThere} the other folder has under a matching name";
    }
}

/// The Duplicate Detective window's state: scan roots, one run at a time, groups
/// sorted by reclaimable bytes. View and export only; nothing here can touch
/// the files themselves.
public sealed class DuplicatesViewModel(FfmpegPaths ffmpeg, AppSettings settings) : ObservableObject
{
    public ObservableCollection<string> Roots { get; } = [.. settings.DuplicateRoots ?? []];
    public ObservableCollection<DupeGroupItem> Groups { get; } = [];
    public ObservableCollection<NotAnalyzedFile> NotAnalyzed { get; } = [];

    /// The folder diff, one column per scan root. Only populated while
    /// OnlyDifferences is on, because unpaired files are meaningless to a
    /// duplicate hunt and are the substance of a diff.
    public ObservableCollection<DiffColumnItem> DiffColumns { get; } = [];
    private readonly List<UnpairedFile> _allUnpaired = [];

    /// Totals across the columns, for the footer. Files the other folder has
    /// under a matching name are counted apart from the ones it has not got:
    /// lumping them together is what made a 319-file answer out of a 41-file
    /// one, and the footer is the last place that should repeat the mistake.
    private int UnpairedShown => DiffColumns.Sum(c => c.Files.Count(f => !f.HasTwin));
    private int NameMatched => DiffColumns.Sum(c => c.AlsoThere);

    private bool _onlyDifferences;
    /// Hides the groups that are confidently the same recording, and reveals
    /// the files that matched nothing. What is left is what differs between the
    /// scanned folders: tracks only one side has, plus matches too weak to take
    /// on trust. Ignores format and quality entirely; that is what the ordinary
    /// view is for.
    public bool OnlyDifferences
    {
        get => _onlyDifferences;
        set { if (Set(ref _onlyDifferences, value)) ApplyGroupFilter(); }
    }

    private bool _hideTitleTwins = true;
    /// Drops the files whose track the other folder demonstrably has, leaving
    /// the ones it does not.
    ///
    /// On by default, which is a deliberate exception to this window's rule of
    /// hiding nothing without audio proof. The columns exist to answer "what
    /// would I have to copy across", and a name is sound evidence for that
    /// even though it is not sound evidence for deleting anything. The count
    /// stays in the column summary either way, so the answer is never silently
    /// smaller than it looks.
    public bool HideTitleTwins
    {
        get => _hideTitleTwins;
        set { if (Set(ref _hideTitleTwins, value)) ApplyGroupFilter(); }
    }

    /// Every group of the last completed scan; Groups is the filtered view.
    private readonly List<DupeGroupItem> _allGroups = [];
    /// The unfiltered footer line of the last scan, re-suffixed as the
    /// filter changes; null until a scan completes.
    private string? _baseFooter;

    /// Shows the results filter row once a scan has produced anything to look
    /// at. Unpaired files count, and that is not a detail: keyed on groups
    /// alone, a scan that finds no duplicates hides the whole row including the
    /// diff toggle, which is exactly the case a folder diff exists for (two
    /// folders that share nothing still differ, and loudly).
    public bool HasResults => _allGroups.Count > 0 || _allUnpaired.Count > 0;

    /// The results filter: every word must match the group label or some
    /// member's path. Applied live against the finished scan, never the disk.
    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set { if (Set(ref _filterText, value)) ApplyGroupFilter(); }
    }

    private void ApplyGroupFilter()
    {
        var tokens = DuplicateScan.ParseFilterTokens(FilterText);
        Groups.Clear();
        foreach (var g in _allGroups)
            if ((tokens.Count == 0 || DuplicateScan.GroupMatches(g.Report, tokens))
                && !(OnlyDifferences && g.Report.IsSameTrack))
                Groups.Add(g);

        DiffColumns.Clear();
        if (OnlyDifferences)
        {
            // One column per scan root, in the order they were added, including
            // roots with nothing unique: an empty column says "this folder has
            // no extras", which is an answer, not an absence.
            var byRoot = _allUnpaired
                .Where(u => tokens.Count == 0 || Matches(u, tokens))
                .GroupBy(u => u.Root, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            // Computed over all the roots at once: a column's label is only
            // meaningful next to its neighbours, so it cannot be worked out
            // one root at a time inside DiffColumnItem.
            var labels = ScopeLabel.Distinguish(Roots);
            for (var i = 0; i < Roots.Count; i++)
            {
                var root = Roots[i];
                var files = byRoot.GetValueOrDefault(root) ?? [];
                var alsoThere = files.Count(f => f.TwinPath is not null);
                var shown = HideTitleTwins ? files.Where(f => f.TwinPath is null).ToList() : files;
                DiffColumns.Add(new DiffColumnItem(
                    root,
                    labels[i],
                    // Files the other folder has not got come first even when
                    // both kinds are shown: they are the answer, the rest is
                    // the working.
                    [.. shown.OrderBy(f => f.TwinPath is not null)
                             .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                             .Select(f => new DiffFileItem(f))],
                    shown.Sum(f => f.SizeBytes),
                    alsoThere));
            }
        }

        if (_baseFooter is not { } baseText) return;
        var suffix = "";
        if (OnlyDifferences)
        {
            // Say what was hidden, not only what is left. An empty diff is a
            // real answer ("these folders hold the same music") and without a
            // count it is indistinguishable from a scan that found nothing.
            var hidden = _allGroups.Count(g => g.Report.IsSameTrack);
            // Confidence, descending: proved the same, named the same, only
            // one side has it, too weak to call either way.
            suffix += $" · differences: {hidden} same hidden";
            if (NameMatched > 0) suffix += $" · {NameMatched} same name only";
            suffix += $" · {UnpairedShown} in one folder only · {Groups.Count} weak match";
            // Never let an unanalysable file pass as agreement: a file absent
            // from a diff reads as "these folders match", which would be a lie.
            if (NotAnalyzed.Count > 0)
                suffix += $" · {NotAnalyzed.Count} not comparable";
        }
        if (tokens.Count > 0)
            suffix += $" · filter: {Groups.Count} of {_allGroups.Count} groups";
        FooterText = baseText + suffix;
    }

    /// The results filter applied to an unpaired file: every word must appear
    /// in its path, matching how GroupMatches treats a group's members.
    private static bool Matches(UnpairedFile file, IReadOnlyCollection<string> tokens) =>
        tokens.All(t => file.Path.Contains(t, StringComparison.OrdinalIgnoreCase));

    private CancellationTokenSource? _cts;

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!Set(ref _isScanning, value)) return;
            RaisePropertyChanged(nameof(CanScan));
            RaisePropertyChanged(nameof(CanExport));
        }
    }
    public bool CanScan => !IsScanning && Roots.Count > 0;

    /// Export dims until a completed scan has something to write; a starting
    /// run clears LastResult, so it dims again while scanning.
    public bool CanExport => !IsScanning && LastResult is not null;

    private double _progressFraction;
    public double ProgressFraction { get => _progressFraction; private set => Set(ref _progressFraction, value); }

    private string _progressText = "";
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    /// The empty state. The footer doubles as the status line, so getting back
    /// to it has to be an explicit write; nothing restores it on its own.
    private const string EmptyFooter = "Add one or more folders, then Scan.";

    private string _footerText = EmptyFooter;
    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    public DupesResult? LastResult { get; private set; }

    public event Action<string>? OpenFileRequested;
    public void RequestOpen(DupeMemberItem member) => OpenFileRequested?.Invoke(member.Path);
    /// The diff's one-sided rows open the same way; they are files like any
    /// other, they simply have no counterpart to sit beside.
    public void RequestOpen(DiffFileItem file) => OpenFileRequested?.Invoke(file.Path);

    /// Raised to open a comparison tab in the main window: the winner is
    /// side A, the challenger side B, matching how the tab title reads.
    public event Action<string, string>? OpenCompareRequested;
    public void RequestCompare(DupeMemberItem member)
    {
        if (member.CanCompareWithWinner) OpenCompareRequested?.Invoke(member.WinnerPath!, member.Path);
    }

    /// The window's error surface (the footer line doubles as the status bar).
    public void SetError(string message) => FooterText = message;

    /// The run snapshots Roots when it starts, so a mid-scan edit could never
    /// corrupt it; it would only desync the visible list from what the results
    /// actually cover. The list is frozen instead: the window disables the
    /// inputs, and these guards catch the paths already in flight (a picker
    /// opened before Scan, a drop the drag effects let through).
    private const string ScanBusyNote =
        "Scan running · cancel it or let it finish before changing the folder list.";

    public void AddRoot(string folder)
    {
        if (IsScanning) { SetError(ScanBusyNote); return; }
        if (Roots.Any(r => string.Equals(r, folder, StringComparison.OrdinalIgnoreCase))) return;
        Roots.Add(folder);
        PersistRoots();
        RaisePropertyChanged(nameof(CanScan));
    }

    /// The launch-argument path ("Find duplicates" from Explorer): scan exactly
    /// the folder that was right-clicked. Roots are NOT persisted here, so the
    /// user's saved root list survives a one-off context-menu scan untouched.
    public void SetSingleRoot(string folder)
    {
        if (IsScanning) { SetError(ScanBusyNote); return; }
        Roots.Clear();
        Roots.Add(folder);
        RaisePropertyChanged(nameof(CanScan));
    }

    /// The --diff launch switch: exactly these two folders, with the diff
    /// already showing. Roots are not persisted here for the same reason
    /// SetSingleRoot does not persist them: a one-off launch must leave the
    /// saved root list alone. Turning the filter on before the scan is safe
    /// because ScanAsync ends in ApplyGroupFilter, so the columns are built the
    /// moment results land.
    public void SetDiffRoots(string folderA, string folderB)
    {
        if (IsScanning) { SetError(ScanBusyNote); return; }
        Roots.Clear();
        Roots.Add(folderA);
        // Two spellings of one folder is one root, matching AddRoot. The result
        // is a single column, which shows the mistake instead of hiding it.
        if (!string.Equals(folderA, folderB, StringComparison.OrdinalIgnoreCase))
            Roots.Add(folderB);
        OnlyDifferences = true;
        RaisePropertyChanged(nameof(CanScan));
    }

    /// Add a pasted or typed path. The picker and drag-drop always hand back
    /// real folders, but typed text can be wrong, so check it exists first.
    /// Windows "Copy as path" wraps the path in quotes, so strip those too.
    /// Returns true once the path is a real folder (added or already present)
    /// so the caller can clear its input box.
    public bool TryAddTypedRoot(string? raw)
    {
        if (IsScanning) { SetError(ScanBusyNote); return false; }
        var path = PathInput.Normalize(raw);
        if (path.Length == 0) return false;
        if (!Directory.Exists(path))
        {
            SetError($"Not a folder: {path}");
            return false;
        }
        AddRoot(path);
        return true;
    }

    public void RemoveRoot(string folder)
    {
        if (IsScanning) { SetError(ScanBusyNote); return; }
        Roots.Remove(folder);
        PersistRoots();
        RaisePropertyChanged(nameof(CanScan));
    }

    /// Empties the folder list and the results with it.
    ///
    /// Removing one folder deliberately keeps the results: that is usually
    /// recomposing a set the results still largely describe. Clearing
    /// everything is not that. It means starting over, and results left behind
    /// describe folders that are no longer listed anywhere, down to a footer
    /// counting groups found in them, which is worse than an empty panel
    /// because it reads as current.
    ///
    /// The empty-list early return also has to consider the results, or
    /// clearing after the roots were removed one at a time would find nothing
    /// to do and leave those results on screen.
    public void ClearRoots()
    {
        if (IsScanning) { SetError(ScanBusyNote); return; }
        if (Roots.Count == 0 && !HasResults) return;
        Roots.Clear();
        PersistRoots();
        ClearResults();
        FooterText = EmptyFooter;
        RaisePropertyChanged(nameof(CanScan));
    }

    /// Drops everything the last scan produced. A starting scan needs this so
    /// the old run's rows are not on screen while it works, and clearing the
    /// folder list needs it for the reason above.
    ///
    /// CanExport is raised by hand because it reads LastResult as well as
    /// IsScanning. A scan gets that refresh free, since IsScanning changes on
    /// the line before; clearing the roots changes neither, so without this
    /// Export stays enabled over results that are no longer displayed.
    private void ClearResults()
    {
        Groups.Clear();
        _allGroups.Clear();
        _allUnpaired.Clear();
        NotAnalyzed.Clear();
        DiffColumns.Clear();
        _baseFooter = null;
        LastResult = null;
        RaisePropertyChanged(nameof(HasResults));
        RaisePropertyChanged(nameof(CanExport));
    }

    private void PersistRoots() => settings.DuplicateRoots = [.. Roots];

    public void Cancel() => _cts?.Cancel();

    public async Task ScanAsync()
    {
        if (IsScanning || Roots.Count == 0) return;
        IsScanning = true;
        // The footer is left alone here on purpose: it still reads as the last
        // scan's summary while this one runs, which beats resetting it to the
        // "add some folders" prompt underneath a running scan.
        ClearResults();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var roots = Roots.ToArray();
        var jobs = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));
        var progress = new Progress<DupesProgress>(p =>
        {
            ProgressFraction = p.Fraction;
            ProgressText = $"{p.Phase} {p.Done} of {p.Total}";
        });

        AuditCache? cache = null;
        var cacheNote = "";
        try
        {
            cache = AuditCache.TryOpen(out _);
            if (cache is null) cacheNote = " · cache unavailable";

            var localCache = cache;
            var result = await Task.Run(() => DuplicateScan.Run(
                ffmpeg, roots, jobs, localCache, fresh: false, progress, ct), ct);

            LastResult = result;
            _allGroups.AddRange(result.Groups.Select(g => new DupeGroupItem(g)));
            foreach (var n in result.NotAnalyzed)
                NotAnalyzed.Add(n);
            _allUnpaired.AddRange(result.Unpaired);
            // After both lists, since HasResults now reads them both.
            RaisePropertyChanged(nameof(HasResults));
            _baseFooter = $"{result.Groups.Count} groups · "
                + $"{result.Groups.Sum(g => g.Group.Members.Count)} duplicate files · "
                + $"reclaimable {Reporting.FormatBytes(result.ReclaimableBytes)} · {result.FilesScanned} scanned{cacheNote}";
            ApplyGroupFilter(); // populates Groups and writes the footer, honoring any typed filter
        }
        catch (OperationCanceledException)
        {
            FooterText = "Scan cancelled · completed analysis stays cached";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetError($"Scan failed: {ex.Message}");
        }
        finally
        {
            cache?.Dispose();
            IsScanning = false;
            ProgressText = "";
            ProgressFraction = 0;
        }
    }
}
