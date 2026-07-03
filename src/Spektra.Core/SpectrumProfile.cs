namespace Spektra.Core;

/// Frequency-domain summaries of a whole spectrogram: the time-average level per
/// bin and the peak-hold (loudest each bin ever gets). Both are in dB and index
/// by FFT bin (0 = DC ... Bins-1 = Nyquist).
public static class SpectrumProfile
{
    /// Mean dB across all columns, per bin. Empty input yields an empty array.
    public static float[] Average(IReadOnlyList<float[]> columns)
    {
        if (columns.Count == 0) return [];
        var bins = columns[0].Length;
        var sum = new double[bins];
        foreach (var col in columns)
            for (var k = 0; k < bins && k < col.Length; k++)
                sum[k] += col[k];
        var avg = new float[bins];
        for (var k = 0; k < bins; k++) avg[k] = (float)(sum[k] / columns.Count);
        return avg;
    }

    /// Per-bin maximum across all columns (peak-hold).
    public static float[] Peak(IReadOnlyList<float[]> columns) => CutoffAnalyzer.PeakHold(columns);
}
