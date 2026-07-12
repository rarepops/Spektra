using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using Spektra.Core;

namespace Spektra.App;

/// One grid row: the flat audit columns plus identity and provenance.
/// Numeric values stay numeric so DataGrid sorting works; display-only
/// strings are separate properties.
public sealed class FolderRow(AuditEntry entry)
{
    public AuditRow Row { get; } = entry.Row;
    public string FullPath { get; } = entry.Target.Path;
    public bool HasProblem { get; } = entry.HasProblem;
    public bool FromCache { get; } = entry.FromCache;

    public string File => Row.File;
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
}

/// A folder-audit tab: streams FolderAudit entries into a sortable grid with
/// byte-weighted progress, an ETA, and the persistent cache.
public sealed class FolderViewModel : TabViewModelBase
{
    private readonly FfmpegPaths _ffmpeg;
    private CancellationTokenSource _cts = new();
    private readonly List<AuditEntry> _pending = [];   // UI thread only
    private readonly DispatcherTimer _flushTimer;
    private readonly Stopwatch _analysisClock = new();

    private long _totalBytes, _cachedBytes, _analyzedBytes;
    private int _totalFiles, _doneFiles, _analyzedFiles, _problems, _cachedFiles;
    private bool _cacheUnavailable;

    public string FolderPath { get; }
    public ObservableCollection<FolderRow> Rows { get; } = [];

    public FolderViewModel(FfmpegPaths ffmpeg, string folderPath)
    {
        _ffmpeg = ffmpeg;
        FolderPath = folderPath;
        TabTitle = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
        if (TabTitle.Length == 0) TabTitle = folderPath; // drive roots like Z:\
        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _flushTimer.Tick += (_, _) => Flush();
    }

    public override string TabTitle { get; }

    public override void Cancel() => _cts.Cancel();

    public IReadOnlyList<string> FilterOptions { get; } = ["All files", "Suspect + worse", "Problems only"];

    private int _filterIndex;
    /// Index into FilterOptions; doubles as the minimum RowSeverity to show
    /// (a tier shows itself and everything worse).
    public int FilterIndex { get => _filterIndex; set => Set(ref _filterIndex, value); }

    private double _progressFraction;
    public double ProgressFraction { get => _progressFraction; private set => Set(ref _progressFraction, value); }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set { if (Set(ref _isScanning, value)) RaisePropertyChanged(nameof(CanRescan)); }
    }

    public bool CanRescan => !IsScanning;

    /// (path, checkIntegrity): the flag asks the opener to run the integrity
    /// check immediately, so the lane under the spectrogram is populated on
    /// arrival. True for Corrupt/Suspect rows and rows with silent gaps
    /// (informational, but worth seeing where); Error rows failed to decode
    /// and would just fail again.
    public event Action<string, bool>? OpenFileRequested;
    public void RequestOpen(FolderRow row) =>
        OpenFileRequested?.Invoke(row.FullPath,
            row.Integrity is "Corrupt" or "Suspect" || row.Dropouts > 0);

    public IReadOnlyList<AuditRow> ExportRows() => Rows.Select(r => r.Row).ToList();

    public void StartScan(bool fresh) => _ = ScanAsync(fresh);

    private async Task ScanAsync(bool fresh)
    {
        if (IsScanning) return;
        IsScanning = true;
        _cts = new CancellationTokenSource();
        Rows.Clear();
        _pending.Clear();
        _totalBytes = _cachedBytes = _analyzedBytes = 0;
        _totalFiles = _doneFiles = _analyzedFiles = _problems = _cachedFiles = 0;
        _cacheUnavailable = false;
        _analysisClock.Reset();
        ProgressFraction = 0;
        StatusText = "Scanning…";
        _flushTimer.Start();
        try
        {
            var ct = _cts.Token;
            var targets = await Task.Run(() => FolderAudit.CollectTargets(FolderPath), ct);
            _totalFiles = targets.Length;
            _totalBytes = targets.Sum(t => t.SizeBytes);
            if (_totalFiles == 0)
            {
                StatusText = "No audio files found.";
                return;
            }

            AuditCache? cache = null;
            try { cache = AuditCache.Open(AuditCache.DefaultPath); }
            catch (Exception e) when (e is not OutOfMemoryException) { _cacheUnavailable = true; }
            try
            {
                var progress = new Progress<AuditEntry>(OnEntry); // marshals to this (UI) thread
                var jobs = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));
                await Task.Run(() => FolderAudit.Run(_ffmpeg, targets, jobs, cache, fresh, progress, ct), ct);
                cache?.PruneFolder(FolderPath, targets.Select(t => t.Path).ToList());
                Flush();
                StatusText = ComposeDone();
            }
            finally { cache?.Dispose(); }
        }
        catch (OperationCanceledException)
        {
            Flush();
            StatusText = $"Cancelled at {_doneFiles} of {_totalFiles} · partial results kept";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetErrorStatus(ex.Message);
        }
        finally
        {
            _flushTimer.Stop();
            IsScanning = false;
            Flush(); // drain stragglers; the IsScanning guard keeps the final status intact
        }
    }

    private void OnEntry(AuditEntry entry)
    {
        _pending.Add(entry);
        _doneFiles++;
        if (entry.HasProblem) _problems++;
        if (entry.FromCache)
        {
            _cachedFiles++;
            _cachedBytes += entry.Target.SizeBytes;
        }
        else
        {
            if (!_analysisClock.IsRunning) _analysisClock.Start();
            _analyzedFiles++;
            _analyzedBytes += entry.Target.SizeBytes;
        }
    }

    /// Batched UI update: appending per entry would flood the grid when a
    /// 20k-entry cache replay lands.
    private void Flush()
    {
        foreach (var entry in _pending) Rows.Add(new FolderRow(entry));
        _pending.Clear();

        var progress = new ScanProgress(
            _totalBytes, _cachedBytes, _analyzedBytes,
            _analyzedFiles, _analysisClock.Elapsed.TotalSeconds);
        ProgressFraction = progress.Fraction;
        if (IsScanning && _totalFiles > 0)
        {
            var eta = progress.EtaSeconds is { } s ? $" · ~{FormatEta(s)} left" : "";
            StatusText = $"Scanning · {_doneFiles} of {_totalFiles} · {_problems} problems{eta}{CacheNote()}";
        }
    }

    private string ComposeDone() =>
        $"{_totalFiles} files · {_problems} problems · {_cachedFiles} from cache{CacheNote()}";

    private string CacheNote() => _cacheUnavailable ? " · cache unavailable" : "";

    private static string FormatEta(double seconds) => seconds switch
    {
        < 90 => $"{Math.Max(1, Math.Round(seconds / 10) * 10)} s",
        < 5400 => $"{Math.Round(seconds / 60)} min",
        _ => $"{seconds / 3600:0.0} h",
    };
}
