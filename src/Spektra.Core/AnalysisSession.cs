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
}
