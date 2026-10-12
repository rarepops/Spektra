using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    /// Everything one folder holds, machine-readable: tags and embedded art
    /// per file, never decoding. Verdicts are deliberately absent, because
    /// they need a full decode; `audit` has them and exports the same
    /// folder-relative path, so the two files join on one column.
    ///
    /// Exit code is 0 unless the run itself failed. An inventory makes no
    /// judgement, so it has no findings to fail on: broken files are reported
    /// in their own rows rather than in the exit status.
    private static int Inventory(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        CliOptions.RejectUnknownFlags(paths);
        // Exactly one root: Path is relative to it, so two roots would let two
        // different files share a path string and collide in a join.
        if (paths.Length != 1 || !Directory.Exists(paths[0]))
        {
            Console.Error.WriteLine("spektra-cli inventory: give one existing folder.");
            return 2;
        }

        var rows = Spektra.Core.Inventory.Run(ffmpeg, paths[0], jobs);
        if (fmt != OutFormat.Text)
        {
            Emit(rows, fmt);
            return 0;
        }

        foreach (var r in rows)
        {
            if (r.Error is { } err) { Console.WriteLine($"  [unreadable] {r.Path} - {err}"); continue; }
            if (!r.IsAudio) { Console.WriteLine($"  {r.Path}  [{r.Ext}]"); continue; }
            // Album artist stands in for artist: a file tagged only at album
            // level is tagged, and calling it untagged would be a small lie.
            var who = string.Join(" - ", new[] { r.Artist ?? r.AlbumArtist, r.Title }.Where(s => s is not null));
            var art = r.HasEmbeddedArt is true ? $" · art {r.ArtWidth}x{r.ArtHeight}" : " · no art";
            Console.WriteLine($"  {r.Path}  [{r.Codec}] {(who.Length > 0 ? who : Untagged(r))}{art}");
        }

        var audio = rows.Count(r => r.IsAudio && r.Error is null);
        var withArt = rows.Count(r => r.HasEmbeddedArt is true);
        var tagged = rows.Count(IsTagged);
        var bad = rows.Count(r => r.Error is not null);
        Console.WriteLine(
            $"{rows.Count} files: {audio} audio, {rows.Count - audio - bad} other" +
            (bad > 0 ? $", {bad} unreadable" : "") + ".");
        if (audio > 0)
            Console.WriteLine($"Audio: {tagged} tagged, {withArt} with embedded art.");
        return 0;
    }

    /// A file with an album and a track number but no artist is tagged; only
    /// a file with nothing at all is untagged.
    private static bool IsTagged(InventoryRow r) =>
        r.Artist is not null || r.AlbumArtist is not null
        || r.Title is not null || r.Album is not null
        || r.Track is not null || r.Year is not null || r.Genre is not null;

    /// Says which it is, so "(partly tagged)" does not read as a bug when the
    /// line above shows no name.
    private static string Untagged(InventoryRow r) =>
        IsTagged(r) ? "(no artist or title)" : "(untagged)";
}
