using System.Collections.ObjectModel;
using Avalonia.Media;
using Spektra.Core;

namespace Spektra.App;

/// One duplicate group for display: headline plus foldout members.
public sealed class DupeGroupItem(DupesGroupReport report)
{
    public string Headline { get; } =
        $"{report.Group.Label} · {report.Group.Members.Count} files · sameness {report.Group.SamenessTier} · reclaim {Format.Bytes(report.ReclaimableBytes)}";
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
        SizeText = Format.Bytes(report.Sizes[member.Path]);
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

internal static class Format
{
    public static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        _ => $"{b / 1024.0:0.0} KB",
    };
}

/// The Duplicate Detective window's state: scan roots, one run at a time, groups
/// sorted by reclaimable bytes. View and export only; nothing here can touch
/// the files themselves.
public sealed class DuplicatesViewModel(FfmpegPaths ffmpeg, AppSettings settings) : ObservableObject
{
    public ObservableCollection<string> Roots { get; } = [.. settings.DuplicateRoots ?? []];
    public ObservableCollection<DupeGroupItem> Groups { get; } = [];
    public ObservableCollection<NotAnalyzedFile> NotAnalyzed { get; } = [];

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

    private string _footerText = "Add one or more folders, then Scan.";
    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    public DupesResult? LastResult { get; private set; }

    public event Action<string>? OpenFileRequested;
    public void RequestOpen(DupeMemberItem member) => OpenFileRequested?.Invoke(member.Path);

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

    /// Add a pasted or typed path. The picker and drag-drop always hand back
    /// real folders, but typed text can be wrong, so check it exists first.
    /// Windows "Copy as path" wraps the path in quotes, so strip those too.
    /// Returns true once the path is a real folder (added or already present)
    /// so the caller can clear its input box.
    public bool TryAddTypedRoot(string? raw)
    {
        if (IsScanning) { SetError(ScanBusyNote); return false; }
        var path = (raw ?? "").Trim().Trim('"').Trim();
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

    /// Empties the whole folder list in one go, for recomposing a scan set
    /// without removing entries one by one. Like Remove, it leaves the last
    /// results on screen; the next Scan is what clears those.
    public void ClearRoots()
    {
        if (IsScanning) { SetError(ScanBusyNote); return; }
        if (Roots.Count == 0) return;
        Roots.Clear();
        PersistRoots();
        RaisePropertyChanged(nameof(CanScan));
    }

    private void PersistRoots() => settings.DuplicateRoots = [.. Roots];

    public void Cancel() => _cts?.Cancel();

    public async Task ScanAsync()
    {
        if (IsScanning || Roots.Count == 0) return;
        IsScanning = true;
        Groups.Clear();
        NotAnalyzed.Clear();
        LastResult = null;
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
            try { cache = AuditCache.Open(AuditCache.DefaultPath); }
            catch (Exception e) when (e is not OutOfMemoryException) { cacheNote = " · cache unavailable"; }

            var localCache = cache;
            var result = await Task.Run(() => DuplicateScan.Run(
                ffmpeg, roots, jobs, localCache, fresh: false, progress, ct), ct);

            LastResult = result;
            foreach (var g in result.Groups)
                Groups.Add(new DupeGroupItem(g));
            foreach (var n in result.NotAnalyzed)
                NotAnalyzed.Add(n);
            FooterText = $"{result.Groups.Count} groups · "
                + $"{result.Groups.Sum(g => g.Group.Members.Count)} duplicate files · "
                + $"reclaimable {Format.Bytes(result.ReclaimableBytes)} · {result.FilesScanned} scanned{cacheNote}";
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
