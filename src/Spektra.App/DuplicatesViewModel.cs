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

public sealed class DupeMemberItem
{
    public DupeMemberItem(DuplicateMember member, DupesGroupReport report)
    {
        var row = report.Rows[member.Path];
        Path = member.Path;
        IsWinner = report.Quality.Winners.Contains(member.Path);
        FoundByAudio = member.FoundByAudio;
        var cutoff = row.CutoffHz is { } c ? $" {c / 1000.0:0.0}k" : "";
        Detail = $"{row.Codec} · {row.Bandwidth}{cutoff} · {row.Integrity} · "
            + $"{Format.Bytes(report.Sizes[member.Path])} · sameness {member.Sameness:0.00}";
        IntegrityBrush = row.Integrity switch
        {
            "Ok" => NodeMarkers.Clean,
            "Suspect" => NodeMarkers.Suspect,
            "Corrupt" or "Error" => NodeMarkers.Problem,
            _ => NodeMarkers.NotAnalyzed,
        };
    }

    public string Path { get; }
    public string Detail { get; }
    public bool IsWinner { get; }
    public bool FoundByAudio { get; }
    public IBrush IntegrityBrush { get; }
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

/// The Dedup Destroyer window's state: scan roots, one run at a time, groups
/// sorted by reclaimable bytes. View and export only; nothing here can touch
/// the files themselves.
public sealed class DuplicatesViewModel(FfmpegPaths ffmpeg, AppSettings settings) : ObservableObject
{
    public ObservableCollection<string> Roots { get; } = [.. settings.DuplicateRoots ?? []];
    public ObservableCollection<DupeGroupItem> Groups { get; } = [];
    public ObservableCollection<NotAnalyzedFile> NotAnalyzed { get; } = [];

    private CancellationTokenSource? _cts;

    private bool _isScanning;
    public bool IsScanning { get => _isScanning; private set { if (Set(ref _isScanning, value)) RaisePropertyChanged(nameof(CanScan)); } }
    public bool CanScan => !IsScanning && Roots.Count > 0;

    private double _progressFraction;
    public double ProgressFraction { get => _progressFraction; private set => Set(ref _progressFraction, value); }

    private string _progressText = "";
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    private string _footerText = "Add one or more folders, then Scan.";
    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    public DupesResult? LastResult { get; private set; }

    public event Action<string>? OpenFileRequested;
    public void RequestOpen(DupeMemberItem member) => OpenFileRequested?.Invoke(member.Path);

    /// The window's error surface (the footer line doubles as the status bar).
    public void SetError(string message) => FooterText = message;

    public void AddRoot(string folder)
    {
        if (Roots.Any(r => string.Equals(r, folder, StringComparison.OrdinalIgnoreCase))) return;
        Roots.Add(folder);
        PersistRoots();
        RaisePropertyChanged(nameof(CanScan));
    }

    public void RemoveRoot(string folder)
    {
        Roots.Remove(folder);
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
