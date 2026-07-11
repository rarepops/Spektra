namespace Spektra.Core;

public sealed class AnalysisSession(FfmpegPaths ffmpeg)
{
    public AudioMetadata ReadMetadata(string path) =>
        new FfprobeMetadataReader(ffmpeg.FfprobePath).Read(path);

    public IEnumerable<float[]> AnalyzeColumns(
        string path, AudioMetadata meta, SpectrogramSettings settings, CancellationToken ct,
        DecodeOptions? options = null)
    {
        var startSeconds = options?.Start?.TotalSeconds ?? 0;
        var spanSeconds = options?.Duration?.TotalSeconds
            ?? Math.Max(0, meta.Duration.TotalSeconds - startSeconds);
        var estimatedSamples = (long)(spanSeconds * meta.SampleRate);
        var decoder = new AudioDecoder(ffmpeg.FfmpegPath);
        var engine = new SpectrogramEngine(settings);
        return engine.Columns(decoder.DecodeMonoChunks(path, ct, options), estimatedSamples, ct);
    }

    /// Decodes the file once and tees the mono chunks through both the
    /// spectrogram engine (for the bandwidth verdict) and a silence scan (for
    /// integrity dropouts plus a decoded-sample count). The folder audit uses
    /// this so bandwidth and integrity share one decode instead of two.
    public (List<float[]> Columns, IReadOnlyList<DropoutRegion> Dropouts, long DecodedSamples)
        AnalyzeColumnsWithSilence(string path, AudioMetadata meta, SpectrogramSettings settings, CancellationToken ct)
    {
        var decoder = new AudioDecoder(ffmpeg.FfmpegPath);
        var engine = new SpectrogramEngine(settings);
        var scanner = new SilenceScanner(meta.SampleRate);
        var estimatedSamples = (long)(meta.Duration.TotalSeconds * meta.SampleRate);

        IEnumerable<float[]> Tee()
        {
            foreach (var chunk in decoder.DecodeMonoChunks(path, ct))
            {
                scanner.Feed(chunk);
                yield return chunk;
            }
        }

        // ToList drains the whole tee (the engine consumes every chunk even when
        // it peak-hold-merges to stay under the column cap), so the scanner sees
        // the entire stream.
        var columns = engine.Columns(Tee(), estimatedSamples, ct).ToList();
        return (columns, scanner.Dropouts, scanner.SampleCount);
    }
}
