namespace Spektra.Core;

/// Anything that turns an audio file into a Fingerprint. The default is the
/// clean-room extractor below; a chromaprint fpcalc adapter could replace it
/// without touching the matcher, grouper, or UI (the spec's escape hatch).
public interface IFingerprintSource
{
    Fingerprint Extract(string path, AudioMetadata meta, CancellationToken ct);
}

/// Streams the file's mono decode into a FingerprintExtractor. Used when only
/// the fingerprint is missing; when the audit row is also missing, the audit's
/// shared decode feeds an extractor through the chunk-observer seam instead.
public sealed class DecodedFingerprintSource(FfmpegPaths ffmpeg) : IFingerprintSource
{
    public Fingerprint Extract(string path, AudioMetadata meta, CancellationToken ct)
    {
        var extractor = new FingerprintExtractor(meta.SampleRate);
        foreach (var chunk in new AudioDecoder(ffmpeg.FfmpegPath).DecodeMonoChunks(path, ct))
            extractor.Feed(chunk);
        return extractor.Finish();
    }
}

/// Clean-room acoustic fingerprint (textbook chroma approach, no chromaprint
/// source consulted). Per output frame at 8 fps: FFT a 4096-sample window,
/// fold linear power into 12 pitch classes over 55-3520 Hz (the midrange even
/// a 64 kbps MP3 preserves), unit-normalize (volume invariance), then pack one
/// 32-bit word per adjacent frame pair from sign-of-difference bits:
/// 12 temporal (cur[b] > prev[b]), 12 spectral (cur[b+1 mod 12] > cur[b]),
/// 8 diagonal (cur[b+1] > prev[b], b = 0..7). Not thread-safe: one per file.
public sealed class FingerprintExtractor
{
    public const int FramesPerSecond = 8;
    private const int WindowSize = 4096;
    private const double MinHz = 55.0, MaxHz = 3520.0;

    private readonly int _hop;
    private readonly float[] _ring = new float[WindowSize];
    private readonly float[] _windowed = new float[WindowSize];
    private readonly float[] _window;
    private readonly float _windowSum;
    private readonly FftTransform _fft = new(WindowSize);
    private readonly float[] _db;
    private readonly int[] _binClass;
    private readonly List<float[]> _frames = [];
    private long _seen;
    private long _nextFrameEnd = WindowSize;

    public FingerprintExtractor(int sampleRate)
    {
        _hop = Math.Max(1, sampleRate / FramesPerSecond);
        _window = WindowFunction.Hann(WindowSize);
        _windowSum = _window.Sum();
        _db = new float[_fft.Bins];
        _binClass = new int[_fft.Bins];
        var hzPerBin = sampleRate / 2.0 / (_fft.Bins - 1);
        for (var k = 0; k < _fft.Bins; k++)
        {
            var hz = k * hzPerBin;
            _binClass[k] = hz is >= MinHz and <= MaxHz ? PitchClass(hz) : -1;
        }
    }

    /// 0..11, semitone class with octaves folded (A = 0 reference).
    public static int PitchClass(double hz) =>
        ((int)Math.Round(12 * Math.Log2(hz / 440.0)) % 12 + 12) % 12;

    public void Feed(ReadOnlySpan<float> monoSamples)
    {
        foreach (var s in monoSamples)
        {
            _ring[(int)(_seen % WindowSize)] = s;
            _seen++;
            if (_seen == _nextFrameEnd)
            {
                EmitFrame();
                _nextFrameEnd += _hop;
            }
        }
    }

    private void EmitFrame()
    {
        var start = _seen - WindowSize;
        for (var n = 0; n < WindowSize; n++)
            _windowed[n] = _ring[(int)((start + n) % WindowSize)] * _window[n];
        _fft.DbSpectrum(_windowed, _windowSum, _db);

        var chroma = new float[12];
        for (var k = 0; k < _db.Length; k++)
            if (_binClass[k] >= 0)
                chroma[_binClass[k]] += MathF.Pow(10f, _db[k] / 10f); // dB back to linear power

        var norm = MathF.Sqrt(chroma.Sum(c => c * c));
        if (norm > 0)
            for (var b = 0; b < 12; b++) chroma[b] /= norm;
        _frames.Add(chroma);
    }

    public Fingerprint Finish()
    {
        var words = new uint[Math.Max(0, _frames.Count - 1)];
        for (var t = 0; t < words.Length; t++)
            words[t] = PackWord(_frames[t], _frames[t + 1]);
        return new Fingerprint(FramesPerSecond, words);
    }

    internal static uint PackWord(float[] prev, float[] cur)
    {
        uint w = 0;
        var bit = 0;
        for (var b = 0; b < 12; b++, bit++) if (cur[b] > prev[b]) w |= 1u << bit;
        for (var b = 0; b < 12; b++, bit++) if (cur[(b + 1) % 12] > cur[b]) w |= 1u << bit;
        for (var b = 0; b < 8; b++, bit++) if (cur[b + 1] > prev[b]) w |= 1u << bit;
        return w;
    }
}
