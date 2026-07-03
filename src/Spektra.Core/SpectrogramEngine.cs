namespace Spektra.Core;

public sealed class SpectrogramEngine(SpectrogramSettings settings)
{
    public int Bins { get; } = settings.WindowSize / 2 + 1;

    /// Streams dB columns from a stream of mono PCM chunks. When
    /// estimatedTotalSamples > 0 and the file would exceed MaxColumns,
    /// consecutive columns are merged by per-bin max (peak-hold) so the
    /// output stays within budget.
    public IEnumerable<float[]> Columns(
        IEnumerable<float[]> monoChunks, long estimatedTotalSamples, CancellationToken ct)
    {
        var window = WindowFunction.Create(settings.Window, settings.WindowSize);
        var windowSum = 0f;
        foreach (var v in window) windowSum += v;
        var fft = new FftTransform(settings.WindowSize);

        var group = 1;
        if (estimatedTotalSamples > 0)
        {
            var estWindows = Math.Max(1, (estimatedTotalSamples - settings.WindowSize) / settings.Hop + 1);
            group = (int)Math.Max(1, (estWindows + settings.MaxColumns - 1) / settings.MaxColumns);
        }

        var buffer = new float[settings.WindowSize * 4];
        var filled = 0;
        var windowed = new float[settings.WindowSize];
        var db = new float[Bins];
        float[]? agg = null;
        var aggCount = 0;

        foreach (var chunk in monoChunks)
        {
            ct.ThrowIfCancellationRequested();
            var offset = 0;
            while (offset < chunk.Length)
            {
                var copy = Math.Min(chunk.Length - offset, buffer.Length - filled);
                Array.Copy(chunk, offset, buffer, filled, copy);
                filled += copy;
                offset += copy;

                var pos = 0;
                while (filled - pos >= settings.WindowSize)
                {
                    ct.ThrowIfCancellationRequested();
                    for (var n = 0; n < settings.WindowSize; n++)
                        windowed[n] = buffer[pos + n] * window[n];
                    fft.DbSpectrum(windowed, windowSum, db);

                    if (group == 1)
                    {
                        yield return (float[])db.Clone();
                    }
                    else
                    {
                        agg ??= CreateFloorColumn();
                        for (var k = 0; k < Bins; k++)
                            if (db[k] > agg[k]) agg[k] = db[k];
                        if (++aggCount == group)
                        {
                            yield return agg;
                            agg = null;
                            aggCount = 0;
                        }
                    }
                    pos += settings.Hop;
                }
                if (pos > 0)
                {
                    Array.Copy(buffer, pos, buffer, 0, filled - pos);
                    filled -= pos;
                }
            }
        }
        if (agg is not null && aggCount > 0) yield return agg;
    }

    private float[] CreateFloorColumn()
    {
        var c = new float[Bins];
        Array.Fill(c, Db.Floor);
        return c;
    }
}
