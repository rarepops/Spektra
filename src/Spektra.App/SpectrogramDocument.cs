using Spektra.Core;

namespace Spektra.App;

public sealed class SpectrogramDocument(
    AudioMetadata meta, SpectrogramSettings settings, int estimatedColumns)
{
    private readonly List<float[]> _columns = new(estimatedColumns);

    public AudioMetadata Metadata { get; } = meta;
    public SpectrogramSettings Settings { get; } = settings;
    public int Bins { get; } = settings.WindowSize / 2 + 1;
    public int EstimatedColumns { get; } = Math.Max(1, estimatedColumns);

    public int Count { get { lock (_columns) return _columns.Count; } }

    public float[] GetColumn(int i) { lock (_columns) return _columns[i]; }

    public void Append(float[] column) { lock (_columns) _columns.Add(column); }
}
