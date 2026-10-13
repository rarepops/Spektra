using Spektra.Core;

namespace Spektra.App;

public sealed partial class DocumentViewModel
{
    private CancellationTokenSource? _integrityCts;

    /// Ctrl+I / the Analyze menu: toggles the results (banner + lane) without
    /// re-analyzing; the check itself runs automatically at load, so this only
    /// starts one when none is in yet.
    public Task ToggleIntegrityAsync()
    {
        if (Integrity is null) return RunIntegrityCheckAsync();
        IntegrityVisible = !IntegrityVisible;
        return Task.CompletedTask;
    }

    /// Runs the integrity check (corrupt frames, missing data, truncation) on a
    /// background thread and publishes the result to the Integrity banner.
    public async Task RunIntegrityCheckAsync()
    {
        if (Metadata is not { } meta) return;
        _integrityCts?.Cancel();
        var cts = _integrityCts = new CancellationTokenSource();
        StatusText = "Checking integrity…";
        try
        {
            var report = await Task.Run(
                () => new IntegrityScanner(_ffmpeg).Check(FilePath, meta, cts.Token), cts.Token);
            if (cts.Token.IsCancellationRequested) return;
            Integrity = report;
            Cache?.PutIntegrity(FilePath, report);
            StatusText = report.Summary;
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException ex)
        {
            Integrity = new IntegrityReport(
                IntegrityStatus.Corrupt, 0, [], 0, meta.Duration.TotalSeconds, true, ex.Message);
        }
    }

    private CancellationTokenSource? _loudnessCts;

    /// Ctrl+L / the Analyze menu: the first use measures; once results exist
    /// it toggles the banner without re-measuring.
    public Task ToggleLoudnessAsync()
    {
        if (Loudness is null) return RunLoudnessCheckAsync();
        _loudnessVisible = !_loudnessVisible;
        RaisePropertyChanged(nameof(HasLoudness));
        return Task.CompletedTask;
    }

    /// Measures loudness (LUFS), true peak, and dynamics on a background thread
    /// and publishes the result to the Loudness banner.
    public async Task RunLoudnessCheckAsync()
    {
        if (Metadata is null) return;
        _loudnessCts?.Cancel();
        var cts = _loudnessCts = new CancellationTokenSource();
        var channelIndex = SelectedChannelIndex;
        var channel = SelectedChannel;
        StatusText = "Measuring loudness…";
        try
        {
            var report = await Task.Run(
                () => new LoudnessMeasurer(_ffmpeg).Measure(FilePath, channel, cts.Token), cts.Token);
            if (cts.Token.IsCancellationRequested) return;
            if (_channelCache.TryGet(channelIndex, out var entry))
            {
                var measured = entry with { Loudness = report };
                _channelCache.Set(channelIndex, measured);
                Cache?.Put(FilePath, measured.Document.Settings, channelIndex, measured);
            }
            if (channelIndex == SelectedChannelIndex)
            {
                Loudness = report;
                StatusText = report.Summary;
            }
        }
        catch (OperationCanceledException) { }
        catch (AudioDecodeException ex) { SetErrorStatus(ex.Message); }
    }
}
