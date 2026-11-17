using static Spektra.Tests.TestAudio;

namespace Spektra.Tests;

/// The library the Imposter Buster tests scan: a reference folder, Rips, and two folders to check,
/// Old and New, built from the committed fixtures in a new temp folder that Dispose deletes. Every
/// fixture is 3 s long, so each file is a fixture looped, or made from one with ffmpeg.
internal sealed class ImposterLibrary : IDisposable
{
    private readonly string _dir;

    /// The reference folder.
    public string Rips { get; }

    public string Old { get; }

    public string New { get; }

    public ImposterLibrary(string prefix)
    {
        _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"))).FullName;
        Rips = Directory.CreateDirectory(Path.Combine(_dir, "Rips")).FullName;
        Old = Directory.CreateDirectory(Path.Combine(_dir, "Old")).FullName;
        New = Directory.CreateDirectory(Path.Combine(_dir, "New")).FullName;

        // Song One: the reference is a lossy encode of Old's copy, so the duplicate scan groups the
        // two by their audio, and New's copy is another sound under the same name. The encode is made
        // from Old's file, so it has to come after it.
        Loop("tones-a.wav", 8, Path.Combine(Old, "Artist Name - Song One.wav"));
        Ffmpeg("-i", Path.Combine(Old, "Artist Name - Song One.wav"), "-codec:a", "libmp3lame", "-b:a", "128k",
            Path.Combine(Rips, "Artist Name - Song One.mp3"));
        Loop("noise.wav", 8, Path.Combine(New, "Artist Name - Song One.wav"));

        // Song Two: 15 s, under the scan's 20 s fingerprint minimum, so no audio group can settle it
        // and only the alignment can confirm the quieter copy.
        Loop("noise.wav", 5, Path.Combine(Rips, "Artist Name - Song Two.wav"));
        Ffmpeg("-i", Path.Combine(Rips, "Artist Name - Song Two.wav"), "-af", "volume=0.8",
            Path.Combine(Old, "Artist Name - Song Two.wav"));

        // Song Three: the same opening at twice the length, 12 s against 24 s.
        Loop("chirp.wav", 4, Path.Combine(Rips, "Artist Name - Song Three.wav"));
        Loop("chirp.wav", 8, Path.Combine(Old, "Artist Name - Song Three.wav"));

        // Song Four: New's copy is not audio, so it cannot be decoded.
        Loop("sine-1khz-stereo.wav", 8, Path.Combine(Rips, "Artist Name - Song Four.wav"));
        File.Copy(Fixture("notaudio.txt"), Path.Combine(New, "Artist Name - Song Four.wav"));

        // Song Five: New's copy is the reference file byte for byte, so its fingerprint is identical.
        // Old's copy opens with 5 s of silence: the fingerprints still match, at an offset, but the
        // openings sit further apart than the alignment reaches.
        Loop("tones-b.wav", 8, Path.Combine(Rips, "Artist Name - Song Five.wav"));
        Ffmpeg("-i", Path.Combine(Rips, "Artist Name - Song Five.wav"), "-af", "adelay=5000:all=1",
            Path.Combine(Old, "Artist Name - Song Five.wav"));
        File.Copy(Path.Combine(Rips, "Artist Name - Song Five.wav"), Path.Combine(New, "Artist Name - Song Five.wav"));

        // A file to check that no reference file names, and a reference file that no copy names.
        Loop("sine-1khz.wav", 8, Path.Combine(Old, "Bonus Track.wav"));
        Loop("gap.wav", 8, Path.Combine(Rips, "Rips Only.wav"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
