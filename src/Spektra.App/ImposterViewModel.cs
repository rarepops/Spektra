using System.Collections.ObjectModel;
using Spektra.Core;

namespace Spektra.App;

/// Which files the Imposter Buster grid lists.
public enum ImposterShow { ToHear, AllClear, NoReference, ReferenceOnly }

/// One grid row: a song's reference file, a checked copy, a checked file with no reference,
/// or a reference file no checked file names.
public sealed class ImposterRowItem : IFileItem
{
    /// Kind names the row as ImposterReport does: "reference" and "copy" come with their song, a
    /// copy with its grade too, and "no-reference" and "reference-only" with neither.
    public ImposterRowItem(
        ImposterResult result, string kind, ScannedFile file, ImposterSong? song = null, GradedCopy? copy = null)
    {
        Kind = kind;
        Song = song is null ? "" : ImposterView.SongName(song);
        Verdict = song is null ? "" : ImposterView.VerdictText(song.Verdict);
        VerdictShort = song is null ? "" : ImposterView.ShortVerdict(song.Verdict);
        Folder = ImposterView.FolderLabel(file.Root, result.ReferenceRoot, result.Roots);
        FolderPath = file.Root;
        File = FolderDiffView.RelativePath(file.Path, file.Root);
        Path = file.Path;
        Grade = copy is not null ? ImposterView.GradeText(copy.Grade) : song is not null ? "reference" : "";
        Seconds = file.Seconds;
        Length = ImposterView.Length(file.Seconds);
        Quality = ImposterView.Quality(file.Row);
        SizeBytes = file.SizeBytes;
        Size = Reporting.FormatBytes(file.SizeBytes);
        IsBest = song?.Best?.Winners.Contains(file.Path) == true;
        Note = copy is null ? "" : ImposterView.SameAudioNote(copy, result.ReferenceRoot, result.Roots) ?? "";
        GradeTip = Grade.Length == 0 ? ""
            : Note.Length == 0 ? $"{Grade}\n{ImposterView.GradeLimit}"
            : $"{Grade}\n{Note}\n{ImposterView.GradeLimit}";
        Match = copy is null ? "" : ImposterView.MatchText(copy.MatchedBy);
        MatchTip = Match.Length == 0 ? "" : ImposterView.MatchLimit;
        ReferencePath = copy is null ? null : song?.Reference.Path;
        IsConfirmed = song is not null && (copy is null || copy.Grade == CopyGrade.Confirmed);
        HasLengthGap = copy is not null && song is not null && ImposterView.HasLengthGap(copy, song);
    }

    public string Kind { get; }
    public string Song { get; }
    public string Verdict { get; }
    /// The verdict as the grid's column shows it, where the full sentence would not fit; Verdict is
    /// its tooltip. "" on the two kinds that belong to no song.
    public string VerdictShort { get; }
    public string Folder { get; }
    /// The full path of the folder the file was found under: the Folder cell's tooltip.
    public string FolderPath { get; }
    public string File { get; }
    public string Path { get; }
    public string Grade { get; }
    public string Length { get; }
    /// The length in seconds, for the grid to sort the Length column by: as text, "10:02" would
    /// sort before "3:41".
    public double? Seconds { get; }
    public string Quality { get; }
    public string Size { get; }
    /// The size in bytes, for the grid to sort the Size column by.
    public long SizeBytes { get; }
    public bool IsBest { get; }
    /// The grid shows it in a Note column, and under the grade in the Grade cell's tooltip.
    public string Note { get; }
    /// The Grade cell's tooltip: the grade, the note when there is one, then what a grade can and
    /// cannot tell. "" on a row with no grade, so its cell shows no tooltip.
    public string GradeTip { get; }
    /// How a copy was paired with its reference file, as the grid's Match column shows it: by the
    /// Spotify ID, the ISRC or the name. "" on the kinds that are not copies.
    public string Match { get; }
    /// The Match cell's tooltip: what a match can and cannot tell. "" on a row with no match, so
    /// its cell shows no tooltip.
    public string MatchTip { get; }
    /// A copy's reference file, null on every other kind.
    public string? ReferencePath { get; }
    public bool CanCompare => ReferencePath is not null;
    /// A Confirmed copy or a reference row: the window dims these in the To hear view, so the
    /// copies to hear stand out.
    public bool IsConfirmed { get; }
    public bool HasLengthGap { get; }

    // Explicit, as DupeMemberItem's is: the display binding is Path, and the shared context-menu
    // actions see it as FullPath.
    string IFileItem.FullPath => Path;
}

/// The Imposter Buster window's state: its folders, its one scan at a time, the rows of its grid
/// for each view, and its settings. It mirrors DuplicatesViewModel wherever the two share a
/// concern, and every word and every order on a row comes from ImposterView.
public sealed class ImposterViewModel(FfmpegPaths ffmpeg, AppSettings settings, string? cachePath = null) : ObservableObject
{
    private string _reference = settings.ImposterReference ?? "";
    /// The trusted folder every checked copy is compared with; "" until one is chosen.
    public string Reference
    {
        get => _reference;
        private set { if (Set(ref _reference, value)) RaisePropertyChanged(nameof(CanScan)); }
    }

    public ObservableCollection<string> Roots { get; } = [.. settings.ImposterRoots ?? []];

    private IReadOnlyList<ImposterRowItem> _rows = [];
    /// The rows of the view on show, replaced as a whole with one change notification, never
    /// cleared and refilled: the All clear view of a real library holds thousands of rows, and a
    /// grid redraws on every single add to a collection it watches.
    public IReadOnlyList<ImposterRowItem> Rows { get => _rows; private set => Set(ref _rows, value); }

    /// The songs to hear as the filter leaves them, in ImposterView's order. The playlist is made
    /// of them, and they are listed with the rows so the two always agree.
    private IReadOnlyList<ImposterSong> _toHear = [];

    private ImposterShow _show = ImposterShow.ToHear;
    public ImposterShow Show
    {
        get => _show;
        set
        {
            if (!Set(ref _show, value)) return;
            RaisePropertyChanged(nameof(ShowToHear));
            RaisePropertyChanged(nameof(ShowAllClear));
            RaisePropertyChanged(nameof(ShowNoReference));
            RaisePropertyChanged(nameof(ShowReferenceOnly));
            ListRows();
        }
    }

    // RadioButton-friendly booleans for the view selector, as ComparisonViewModel has for its modes.
    public bool ShowToHear { get => _show == ImposterShow.ToHear; set { if (value) Show = ImposterShow.ToHear; } }
    public bool ShowAllClear { get => _show == ImposterShow.AllClear; set { if (value) Show = ImposterShow.AllClear; } }
    public bool ShowNoReference { get => _show == ImposterShow.NoReference; set { if (value) Show = ImposterShow.NoReference; } }
    public bool ShowReferenceOnly { get => _show == ImposterShow.ReferenceOnly; set { if (value) Show = ImposterShow.ReferenceOnly; } }

    /// Each label counts the whole result, whatever the filter hides, so a filter never reads as
    /// fewer songs found. Before a scan there is nothing to count.
    public string ToHearLabel => Label("To hear", r => r.Songs.Count(s => !IsAllClear(s)));
    public string AllClearLabel => Label("All clear", r => r.Songs.Count(IsAllClear));
    public string NoReferenceLabel => Label("No reference", r => r.NoReference.Count);
    public string ReferenceOnlyLabel => Label("Not in the checked folders", r => r.ReferenceOnly.Count);

    private string Label(string view, Func<ImposterResult, int> count) =>
        LastResult is { } result ? $"{view} · {count(result)}" : view;

    private static bool IsAllClear(ImposterSong song) => song.Verdict == SongVerdict.AllConfirmed;

    /// The results filter: every word must be in the path of a file the row stands for (for a song,
    /// any of its files). Applied live to the finished scan, never the disk.
    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set { if (Set(ref _filterText, value)) ListRows(); }
    }

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
            RaisePropertyChanged(nameof(CanSavePlaylist));
        }
    }

    public bool CanScan => !IsScanning && Reference.Length > 0 && Roots.Count > 0;

    /// Export dims until a completed scan has something to write; a starting run clears
    /// LastResult, so it dims again while scanning.
    public bool CanExport => !IsScanning && LastResult is not null;

    /// The playlist holds the songs to hear that the filter leaves, so one must be there.
    public bool CanSavePlaylist => !IsScanning && _toHear.Count > 0;

    /// True from the moment a scan's result lands until it is cleared, so the views and the filter
    /// show only once there is a result for their labels to count.
    public bool HasResults => LastResult is not null;

    private double _progressFraction;
    public double ProgressFraction { get => _progressFraction; private set => Set(ref _progressFraction, value); }

    private string _progressText = "";
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    /// The empty state. The footer doubles as the status line, so getting back to it has to be an
    /// explicit write; nothing restores it on its own.
    private const string EmptyFooter = "Choose a reference folder and one or more folders to check, then Scan.";

    private string _footerText = EmptyFooter;
    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    private ImposterResult? _lastResult;
    public ImposterResult? LastResult
    {
        get => _lastResult;
        private set
        {
            if (!Set(ref _lastResult, value)) return;
            RaisePropertyChanged(nameof(HasResults));
            RaisePropertyChanged(nameof(CanExport));
            RaisePropertyChanged(nameof(ToHearLabel));
            RaisePropertyChanged(nameof(AllClearLabel));
            RaisePropertyChanged(nameof(NoReferenceLabel));
            RaisePropertyChanged(nameof(ReferenceOnlyLabel));
            ListRows();
        }
    }

    /// Lists the rows of the view on show, and the songs the playlist is made of, from the last
    /// result and the filter words.
    private void ListRows()
    {
        var tokens = DuplicateScan.ParseFilterTokens(FilterText);
        var result = LastResult;
        _toHear = result is null ? [] : ImposterView.Ordered(result.Songs.Where(s => !IsAllClear(s)), tokens);
        Rows = result is null ? [] : Show switch
        {
            ImposterShow.ToHear => SongRows(result, _toHear),
            ImposterShow.AllClear => SongRows(result, ImposterView.Ordered(result.Songs.Where(IsAllClear), tokens)),
            ImposterShow.NoReference => FileRows(result, result.NoReference, "no-reference", tokens),
            ImposterShow.ReferenceOnly => FileRows(result, result.ReferenceOnly, "reference-only", tokens),
            _ => throw new ArgumentOutOfRangeException(nameof(Show), Show, null),
        };
        RaisePropertyChanged(nameof(CanSavePlaylist));
    }

    // Each song as its reference row followed by its copies' rows, as ImposterReport lists them.
    private static ImposterRowItem[] SongRows(ImposterResult result, IEnumerable<ImposterSong> songs) =>
    [
        .. songs.SelectMany(song => song.Copies
            .Select(copy => new ImposterRowItem(result, "copy", copy.File, song, copy))
            .Prepend(new ImposterRowItem(result, "reference", song.Reference, song))),
    ];

    private static ImposterRowItem[] FileRows(
        ImposterResult result, IEnumerable<ScannedFile> files, string kind, IReadOnlyCollection<string> tokens) =>
        [.. files.Where(f => ImposterView.HoldsEveryWord(f.Path, tokens)).Select(f => new ImposterRowItem(result, kind, f))];

    /// The songs to hear as filtered and ordered, as an extended M3U: each song's copies that are
    /// not the reference recording, then its reference file.
    public string ListeningPlaylist() =>
        ImposterView.Playlist(_toHear, LastResult?.ReferenceRoot ?? "", LastResult?.Roots ?? []);

    public event Action<string>? OpenFileRequested;
    public void RequestOpen(ImposterRowItem row) => OpenFileRequested?.Invoke(row.Path);

    /// Raised to compare a copy with its reference file in the main window, aligned (the two may
    /// start at different points), the reference as side A as the row reads.
    public event Action<string, string>? OpenAlignedCompareRequested;
    /// Only a copy has a reference file to compare with; any other row does nothing.
    public void RequestCompare(ImposterRowItem row)
    {
        if (row.ReferencePath is { } reference) OpenAlignedCompareRequested?.Invoke(reference, row.Path);
    }

    /// The window's error surface (the footer line doubles as the status bar).
    public void SetError(string message) => FooterText = message;

    /// The run snapshots the folders when it starts, so a mid-scan edit could never corrupt it; it
    /// would only desync the folders on show from what the results cover. The inputs are frozen
    /// instead: the window disables them, and these guards catch the paths already in flight (a
    /// picker opened before Scan, a drop the drag effects let through).
    private const string ScanBusyNote =
        "Scan running · cancel it or let it finish before changing the folders.";

    /// The scan refuses a folder to check that is the reference folder itself, so neither entry
    /// lets one in: the answer comes at the input, not as a failed scan later.
    private const string ReferenceIsChecked = "The reference folder cannot also be a folder to check.";

    /// Sets the reference folder from a picked, pasted or typed path. Returns true once the folder
    /// is set.
    public bool TrySetReference(string? raw)
    {
        if (TypedFolder(raw) is not { } path) return false;
        if (Roots.Any(r => ImposterScan.IsSameFolder(r, path)))
        {
            SetError(ReferenceIsChecked);
            return false;
        }
        Reference = path;
        Persist();
        return true;
    }

    public void AddRoot(string folder) => TryAddRoot(folder, persist: true);

    /// Adds a pasted or typed path. Returns true once the folder is added, or was listed already,
    /// so the caller can clear its input box.
    public bool TryAddTypedRoot(string? raw) => TypedFolder(raw) is { } path && TryAddRoot(path, persist: true);

    /// The picker and drag-drop always hand back real folders, but typed text can be wrong, so it
    /// must be a folder that exists; Windows "Copy as path" wraps the path in quotes, which go.
    /// Null when there is no folder, with the reason in the footer when there is one to give.
    private string? TypedFolder(string? raw)
    {
        if (IsScanning) { SetError(ScanBusyNote); return null; }
        var path = PathInput.Normalize(raw);
        if (path.Length == 0) return null;
        if (Directory.Exists(path)) return path;
        SetError($"Not a folder: {path}");
        return null;
    }

    /// Adds a folder to check unless it is the reference folder. Two spellings of one folder are
    /// one folder, compared the way the scan compares them. False when the folder is refused.
    private bool TryAddRoot(string folder, bool persist)
    {
        if (IsScanning) { SetError(ScanBusyNote); return false; }
        if (Reference.Length > 0 && ImposterScan.IsSameFolder(folder, Reference))
        {
            SetError(ReferenceIsChecked);
            return false;
        }
        if (Roots.Any(r => ImposterScan.IsSameFolder(r, folder))) return true;
        Roots.Add(folder);
        if (persist) Persist();
        RaisePropertyChanged(nameof(CanScan));
        return true;
    }

    public void RemoveRoot(string folder)
    {
        if (IsScanning) { SetError(ScanBusyNote); return; }
        Roots.Remove(folder);
        Persist();
        RaisePropertyChanged(nameof(CanScan));
    }

    /// Empties the folders to check and the results with them. The reference stays: it is the
    /// library every check is made against, so starting over keeps it. Results left behind would
    /// describe folders no longer listed, down to a footer counting songs found in them, which is
    /// worse than an empty grid because it reads as current.
    ///
    /// The early return also considers the results, or clearing after the folders were removed one
    /// at a time would find nothing to do and leave those results on screen.
    public void ClearRoots()
    {
        if (IsScanning) { SetError(ScanBusyNote); return; }
        if (Roots.Count == 0 && !HasResults) return;
        Roots.Clear();
        Persist();
        LastResult = null;
        FooterText = EmptyFooter;
        RaisePropertyChanged(nameof(CanScan));
    }

    /// The --imposters launch switch: exactly these folders. Nothing is saved here, so the saved
    /// folders survive a one-off launch, and a folder to check that is the reference is refused as
    /// it would be when added by hand.
    public void SetFolders(string reference, IReadOnlyList<string> roots)
    {
        if (IsScanning) { SetError(ScanBusyNote); return; }
        Reference = reference;
        Roots.Clear();
        foreach (var root in roots) TryAddRoot(root, persist: false);
        RaisePropertyChanged(nameof(CanScan));
    }

    /// Both folders are saved together, so the saved pair is always one the guards let through.
    /// After the launch switch, saving the folders to check alone would put them beside an older
    /// saved reference, which may be one of them.
    private void Persist()
    {
        settings.ImposterReference = Reference;
        settings.ImposterRoots = [.. Roots];
    }

    public void Cancel() => _cts?.Cancel();

    public async Task ScanAsync()
    {
        if (!CanScan) return;
        IsScanning = true;
        // The footer is left alone here on purpose: it still reads as the last scan's summary while
        // this one runs, which beats the "choose some folders" prompt underneath a running scan. The
        // last result goes at once, so no row describes a run that is being replaced.
        LastResult = null;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var reference = Reference;
        var roots = Roots.ToArray();
        var jobs = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));
        // Made here, on the calling thread, so the callbacks run there.
        var progress = new Progress<DupesProgress>(p =>
        {
            ProgressFraction = p.Fraction;
            ProgressText = $"{p.Phase} {p.Done} of {p.Total}";
        });

        AuditCache? cache = null;
        try
        {
            // Only tests pass a cachePath: their scans must not touch the user's cache.
            cache = AuditCache.TryOpen(cachePath ?? AuditCache.DefaultPath, out _);
            var localCache = cache;
            var result = await Task.Run(
                () => ImposterScan.Run(ffmpeg, reference, roots, jobs, localCache, fresh: false, progress, ct), ct);
            // The result lands only once the whole run is over, so a cancelled or failed run leaves
            // no half-built result behind.
            LastResult = result;
            FooterText = ImposterView.Summary(result) + (cache is null ? " · cache unavailable" : "");
        }
        catch (OperationCanceledException)
        {
            FooterText = "Scan cancelled · completed analysis stays cached";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The alignment step wraps any failure but a pure cancel in an AggregateException, whose
            // own message only says that errors occurred.
            SetError($"Scan failed: {(ex is AggregateException ? ex.GetBaseException() : ex).Message}");
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
