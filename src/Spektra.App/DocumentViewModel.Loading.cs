using Avalonia.Threading;
using Spektra.Core;

namespace Spektra.App;

public sealed partial class DocumentViewModel
{
    private bool _loadStarted;

    /// Loads the overview once, on first need. Restored tabs are added
    /// without loading, so a restored session costs one decode (the selected
    /// tab) instead of one per tab; the rest load when first selected.
    /// Idempotent: every later selection of the same tab is a no-op.
    public Task EnsureLoadedAsync()
    {
        if (_loadStarted) return Task.CompletedTask;
        return LoadOverviewAsync();
    }

    /// F5: an explicit reload reads the file again, whatever the shared store
    /// remembers about it. Everything else (first load, a settings change,
    /// Ctrl+Left back to a file) is free to take what is remembered.
    public Task ReloadAsync()
    {
        Cache?.Forget(FilePath);
        return LoadOverviewAsync();
    }

    public async Task LoadOverviewAsync()
    {
        _loadStarted = true;
        _loadCts?.Cancel();
        _tileCts?.Cancel();
        _computeCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        ClearForLoad();

        try
        {
            // What an earlier tab already finished for this file at these
            // settings. Any remembered overview carries the metadata, so a
            // hit skips ffprobe as well as the decode.
            var remembered = Cache?.Lookup(FilePath, CurrentSettings);
            AudioMetadata meta;
            if (remembered is { Overviews.Count: > 0 })
            {
                meta = remembered.Overviews.Values.First().Document.Metadata;
            }
            else
            {
                StatusText = "Reading metadata…";
                var session = new AnalysisSession(_ffmpeg);
                meta = await Task.Run(() => session.ReadMetadata(FilePath, cts.Token), cts.Token);
                if (cts.Token.IsCancellationRequested) return;
            }
            ApplyMetadata(meta);
            if (remembered is not null) SeedFrom(remembered);
            if (_channelCache.TryGet(SelectedChannelIndex, out var shown))
                Show(shown, CachedStatus(shown));

            // Every load checks integrity by itself (unless the preference
            // says otherwise, or the result is already remembered): a damaged
            // file must not hide behind a green bandwidth banner until someone
            // remembers Ctrl+I. Runs beside the overview decode; the banner
            // and lane appear whenever the result lands.
            if (Integrity is null && AutoIntegrityCheck) _ = RunIntegrityCheckAsync();

            await RestartComputeLoopAsync(meta);
        }
        catch (OperationCanceledException) { /* superseded or closed */ }
        catch (AudioDecodeException ex)
        {
            if (cts.Token.IsCancellationRequested) return;
            ShowDecodeError(ex);
        }
    }

    private void ClearForLoad()
    {
        _channelCache.Clear();
        ErrorText = null;
        Tile = null;
        Verdict = null;
        Integrity = null;
        Loudness = null;
    }

    private void ApplyMetadata(AudioMetadata meta)
    {
        Metadata = meta;
        if (ChannelOptions.Count == 1 && meta.Channels > 1)
            ChannelOptions = ChannelList.Options(meta.Channels);
        HeaderText = meta.ToDisplayLine(TabTitle);
        Viewport.MinTimeSpan = TilePlan.MinTimeSpan(meta.Duration, meta.SampleRate);
    }

    private void SeedFrom(OverviewCache.Snapshot remembered)
    {
        // Seed the per-document cache, the shown channel last so it is
        // the one the three-entry LRU is surest to keep; the compute
        // loop below then has only the missing variants to prefetch.
        foreach (var (index, overview) in remembered.Overviews.OrderBy(kv => kv.Key == SelectedChannelIndex))
            _channelCache.Set(index, overview);
        if (remembered.Integrity is { } report) Integrity = report;
    }

    /// Cancels the running compute loop, waits for it to unwind (so decodes
    /// never overlap), then starts a fresh loop unless superseded meanwhile.
    private async Task RestartComputeLoopAsync(AudioMetadata meta)
    {
        _computeCts?.Cancel();
        _computingIndex = -1;
        _computingDoc = null;
        var cts = _computeCts = new CancellationTokenSource();
        if (_computeLoop is { } previous)
        {
            // The await is sequencing only, so decodes never overlap. The loop
            // catches cancellation and decode errors itself, so an unexpected
            // fault (an ffmpeg spawn failure, say) lives in the task: rethrown
            // here it would kill this restart, which is usually fire-and-forget
            // and would die unobserved, hanging the tab on "Analyzing…".
            try { await previous; }
            catch (Exception e) when (e is not OutOfMemoryException) { }
        }
        if (cts.Token.IsCancellationRequested) return;
        _computeLoop = RunComputeLoopAsync(meta, cts.Token);
    }

    /// Sequentially computes overviews: the selected channel first, then (when
    /// the whole file fits the cache) the remaining variants as silent
    /// prefetch. Runs on the UI thread; only decode/FFT work is offloaded.
    /// Handles cancellation and decode failures itself; anything unexpected
    /// faults the loop task.
    private async Task RunComputeLoopAsync(AudioMetadata meta, CancellationToken ct)
    {
        var failed = new HashSet<int>();
        while (!ct.IsCancellationRequested)
        {
            var index = NextIndexToCompute(meta, failed);
            if (index < 0) return;
            var started = DateTime.UtcNow;
            _computingIndex = index;
            try
            {
                var entry = await ComputeOverviewAsync(index, meta, ct);
                _channelCache.Set(index, entry);
                // Published even if this tab has since been stepped away
                // from: a finished decode is worth keeping whoever asked.
                Cache?.Put(FilePath, entry.Document.Settings, index, entry);
                if (index == SelectedChannelIndex)
                    Show(entry,
                        $"Done in {(DateTime.UtcNow - started).TotalSeconds:0.0}s · {entry.Document.Count} columns",
                        completedInPlace: true);
            }
            catch (OperationCanceledException) { return; }
            catch (AudioDecodeException ex)
            {
                if (ct.IsCancellationRequested) return;
                if (index == SelectedChannelIndex)
                {
                    // Visible failure: show the banner and stop; if one channel
                    // cannot decode the rest almost certainly cannot either.
                    // Re-selecting the channel retries with a fresh run.
                    ShowDecodeError(ex);
                    return;
                }
                failed.Add(index); // silent per-channel prefetch failure
            }
            finally
            {
                _computingIndex = -1;
                _computingDoc = null;
            }
        }
    }

    /// The selected channel if not yet cached, else the first missing prefetch
    /// candidate (only when prefetch is on and 1 + Channels fits the cache,
    /// i.e. stereo), else -1 to stop.
    private int NextIndexToCompute(AudioMetadata meta, HashSet<int> failed)
    {
        if (!_channelCache.TryGet(SelectedChannelIndex, out _)) return SelectedChannelIndex;
        if (!PrefetchChannels || meta.Channels < 2) return -1;
        if (!ChannelList.CanPrefetch(meta.Channels, SelectedChannelIndex, ChannelCacheCapacity)) return -1;
        for (var i = 0; i <= meta.Channels; i++)
            if (!failed.Contains(i) && !_channelCache.TryGet(i, out _)) return i;
        return -1;
    }

    /// Computes one channel's overview end to end (stream columns, then the
    /// bandwidth verdict). While its document is the visible one it paints
    /// progressively, exactly like a fresh load; as silent prefetch it posts
    /// nothing.
    private async Task<ChannelOverview> ComputeOverviewAsync(int index, AudioMetadata meta, CancellationToken ct)
    {
        var session = new AnalysisSession(_ffmpeg);
        var settings = new SpectrogramSettings(WindowSize: _windowSize, Window: _window);
        var estColumns = settings.EstimateColumns(meta.Duration, meta.SampleRate);

        var doc = new SpectrogramDocument(meta, settings, estColumns);
        _computingDoc = doc;
        if (index == SelectedChannelIndex)
        {
            Document = doc;
            DocumentChanged?.Invoke();
            StatusText = "Analyzing…";
        }

        var channel = ChannelList.ChannelFor(index, meta.Channels);
        await Task.Run(() =>
        {
            var sinceRefresh = 0;
            foreach (var column in session.AnalyzeColumns(FilePath, meta, settings, ct,
                new DecodeOptions(Channel: channel)))
            {
                doc.Append(column);
                if (++sinceRefresh >= 32)
                {
                    sinceRefresh = 0;
                    if (ReferenceEquals(Document, doc))
                        Dispatcher.UIThread.Post(() => DocumentUpdated?.Invoke());
                }
            }
        }, ct);

        var verdict = await Task.Run(() => AnalyzeBandwidth(doc, meta), ct);
        return new ChannelOverview(doc, verdict, null);
    }

    /// Snapshots the finished overview columns and runs the provenance-aware
    /// bandwidth analysis. Peak-hold is associative, so the zoomed-out
    /// max-merged columns give the same per-window verdicts the full frames would.
    private static LosslessVerdict AnalyzeBandwidth(SpectrogramDocument doc, AudioMetadata meta)
    {
        var count = doc.Count;
        var columns = new List<float[]>(count);
        for (var i = 0; i < count; i++) columns.Add(doc.GetColumn(i));
        return ProvenanceScan.Analyze(columns, meta);
    }
}
