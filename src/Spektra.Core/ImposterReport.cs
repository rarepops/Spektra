using System.Text;

namespace Spektra.Core;

/// One row of `spektra-cli imposters` as JSON or CSV. Kind is "reference" (a song's reference
/// file), "copy" (a checked file graded against it), "no-reference" (a checked file no reference
/// file names) or "reference-only" (a reference file no checked file names). Verdict and Grade
/// hold enum names.
public sealed record ImposterRow(
    string Kind, string? Song, string? Verdict, string Root, string Path,
    string? Grade, double? AlignConfidence, double? Seconds,
    string? ReferencePath, double? ReferenceSeconds, bool Best, string? Quality, string? SameAudioAs);

/// What Imposter Buster found, as `spektra-cli imposters` prints it: the songs in the order
/// ImposterView lists them, then the checked files no reference file names, then the reference
/// files no copy chose. Every word about a grade, a verdict or a best copy is ImposterView's, so
/// the CLI and the window cannot drift apart.
public static class ImposterReport
{
    /// Every song, the all-clear ones included, as its reference row followed by its copies' rows;
    /// then a row per checked file with no reference, then per reference file not in the checked
    /// folders.
    public static IReadOnlyList<ImposterRow> Rows(ImposterResult result) =>
    [
        .. ImposterView.Ordered(result.Songs, []).SelectMany(song => song.Copies
            .Select(copy => Row("copy", copy.File, song, copy))
            .Prepend(Row("reference", song.Reference, song))),
        .. result.NoReference.Select(file => Row("no-reference", file)),
        .. result.ReferenceOnly.Select(file => Row("reference-only", file)),
    ];

    /// The report as text, each line ending in \n: a summary line, the songs with no copy
    /// confirmed, the songs where a copy differs (numbered on across both), then the two lists. A
    /// section with nothing in it is left out, and the all-clear songs are counted in the summary
    /// only.
    public static string Text(ImposterResult result)
    {
        var (referenceRoot, roots) = (result.ReferenceRoot, result.Roots);
        var text = new StringBuilder()
            .Append($"Imposter Buster · reference {ImposterView.FolderLabel(referenceRoot, referenceRoot, roots)}")
            .Append($" · {ImposterView.Summary(result)}\n");

        // Ordered puts the songs with no copy confirmed first and those where a copy differs next,
        // so grouping them in that order gives the two sections.
        var number = 0;
        foreach (var section in ImposterView.Ordered(result.Songs, [])
                     .Where(s => s.Verdict != SongVerdict.AllConfirmed)
                     .GroupBy(s => s.Verdict))
        {
            text.Append($"{ImposterView.VerdictText(section.Key)} · {section.Count()}\n");
            foreach (var song in section)
            {
                var reference = song.Reference;
                text.Append($"  {++number}. {ImposterView.SongName(song)}\n")
                    .Append($"     reference: {reference.Path} · {ImposterView.Length(reference.Seconds)}")
                    // A file the scan could not analyze has no quality to show.
                    .Append(reference.Row is null ? "\n" : $" · {ImposterView.Quality(reference.Row)}\n");
                foreach (var copy in song.Copies)
                {
                    text.Append($"     {ImposterView.FolderLabel(copy.File.Root, referenceRoot, roots)}: {copy.File.Path}")
                        .Append($" · {ImposterView.Length(copy.File.Seconds)} · {ImposterView.GradeText(copy.Grade)}\n");
                    if (ImposterView.SameAudioNote(copy, referenceRoot, roots) is { } note) text.Append($"          {note}\n");
                }
                text.Append($"     best copy: {ImposterView.BestText(song, referenceRoot, roots)}\n");
            }
        }

        if (result.NoReference.Count > 0) text.Append($"No reference by name · {result.NoReference.Count} file(s)\n");
        foreach (var file in result.NoReference) text.Append($"     {file.Path}\n");
        if (result.ReferenceOnly.Count > 0)
            text.Append($"Not in the checked folders · {result.ReferenceOnly.Count} reference file(s)\n");
        foreach (var file in result.ReferenceOnly) text.Append($"     {file.Path}\n");
        return text.ToString();
    }

    /// Whether a checked copy is not the reference recording. A checked file with no reference and
    /// a reference file with no copy say nothing about a copy, so neither counts.
    public static bool HasFindings(ImposterResult result) =>
        result.Songs.Any(song => song.Copies.Any(copy => copy.Grade != CopyGrade.Confirmed));

    // A file's row: the song's columns when it belongs to a song, the grade's when it is a copy.
    private static ImposterRow Row(string kind, ScannedFile file, ImposterSong? song = null, GradedCopy? copy = null) => new(
        Kind: kind,
        Song: song is null ? null : ImposterView.SongName(song),
        Verdict: song?.Verdict.ToString(),
        Root: file.Root,
        Path: file.Path,
        Grade: copy?.Grade.ToString(),
        AlignConfidence: copy?.AlignConfidence is { } confidence ? Math.Round(confidence, 3) : null,
        Seconds: file.Seconds,
        ReferencePath: copy is null ? null : song?.Reference.Path,
        ReferenceSeconds: copy is null ? null : song?.Reference.Seconds,
        Best: song?.Best?.Winners.Contains(file.Path) == true,
        Quality: file.Row is null ? null : ImposterView.Quality(file.Row),
        SameAudioAs: copy?.SameAudioAs);
}
