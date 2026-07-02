namespace Spektra.Core;

public sealed class AnalysisSession(FfmpegPaths ffmpeg)
{
    public AudioMetadata ReadMetadata(string path) =>
        new FfprobeMetadataReader(ffmpeg.FfprobePath).Read(path);

    public IEnumerable<float[]> AnalyzeColumns(
        string path, AudioMetadata meta, SpectrogramSettings settings, CancellationToken ct)
    {
        var estimatedSamples = (long)(meta.Duration.TotalSeconds * meta.SampleRate);
        var decoder = new AudioDecoder(ffmpeg.FfmpegPath);
        var engine = new SpectrogramEngine(settings);
        return engine.Columns(decoder.DecodeMonoChunks(path, ct), estimatedSamples, ct);
    }
}
