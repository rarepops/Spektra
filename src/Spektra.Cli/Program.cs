using System.Globalization;
using System.Reflection;
using Spektra.Core;
using CompareOptions = Spektra.Core.CompareOptions;

namespace Spektra.Cli;

/// Cross-platform command-line front end for Spektra's analysis engine. Writes
/// to stdout like a normal console program, so output pipes and redirects
/// cleanly. Exit code 1 when anything is judged likely lossy, 2 on setup errors.
internal static class Program
{
    public static int Main(string[] args)
    {
        // UTF-8 output regardless of the console's legacy codepage: summaries
        // and compare lines carry middots and Δ, which the OEM codepage
        // mangles in redirects and pipes.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
        catch (IOException) { /* no console to configure */ }

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            return Usage(args.Length == 0 ? 2 : 0);

        if (args[0] is "--version" or "-v")
        {
            Console.WriteLine($"spektra {Version()}");
            return 0;
        }

        // The manifest never decodes (it lists and reads cache), so it is the
        // one verb that must keep working without ffmpeg installed; it
        // dispatches before the gate.
        if (args[0] is "--manifest" or "manifest")
        {
            try
            {
                var (fmt, _, rest) = CliOptions.Take(args[1..], DefaultJobs);
                return Manifest(rest, fmt);
            }
            catch (OptionException ex)
            {
                Console.Error.WriteLine($"spektra: {ex.Message}");
                return 2;
            }
        }

        var ffmpeg = FfmpegLocator.LocateDefault();
        if (ffmpeg is null)
        {
            Console.Error.WriteLine(
                "spektra: ffmpeg/ffprobe not found. Install ffmpeg and ensure it is on PATH.");
            return 2;
        }

        try
        {
            var (fmt, jobs, rest) = CliOptions.Take(args[1..], DefaultJobs);
            return args[0] switch
            {
                "--report" or "report" => Report(ffmpeg, rest, fmt, jobs),
                "--scan" or "scan" => Scan(ffmpeg, rest, fmt, jobs),
                "--check" or "check" => Check(ffmpeg, rest, fmt, jobs),
                "--audit" or "audit" => Audit(ffmpeg, rest, fmt, jobs),
                "--dupes" or "dupes" => Dupes(ffmpeg, rest, fmt, jobs),
                "--inventory" or "inventory" => Inventory(ffmpeg, rest, fmt, jobs),
                "--loudness" or "loudness" => Loudness(ffmpeg, rest, fmt, jobs),
                "--diff" or "diff" => Diff(ffmpeg, rest, fmt),
                "--image" or "image" => Image(ffmpeg, rest, fmt),
                _ => Usage(2),
            };
        }
        catch (OptionException ex)
        {
            Console.Error.WriteLine($"spektra: {ex.Message}");
            return 2;
        }
    }

    // Default worker count: about 80% of the logical cores, leaving headroom for
    // the OS and for ffmpeg's own decode threads. Override with --jobs / -j.
    private static readonly int DefaultJobs = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));

    // Informational version (the csproj <Version>), minus any +buildmetadata.
    private static string Version()
    {
        var asm = typeof(Program).Assembly;
        var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion
                ?? asm.GetName().Version?.ToString() ?? "unknown";
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    // Analyzes files concurrently, capped at `jobs` workers, and returns results
    // in input order. Each file spawns its own ffmpeg/ffprobe and runs its own
    // FFT, so the work is CPU-bound and safe to run in parallel; ffmpeg streams
    // each file from disk itself, which is why we parallelize whole files rather
    // than prefetching bytes into a shared queue.
    private static T[] MapParallel<T>(IReadOnlyList<string> files, int jobs, Func<string, T> analyze)
    {
        var results = new T[files.Count];
        Parallel.For(0, files.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs) },
            i => results[i] = analyze(files[i]));
        return results;
    }

    // A single folder argument recurses into its audio files; otherwise the
    // arguments are taken as individual files.
    private static IReadOnlyList<string> ResolveInputs(string[] paths)
    {
        CliOptions.RejectUnknownFlags(paths);
        return paths.Length == 1 && Directory.Exists(paths[0])
            ? BandwidthReport.FindAudioFiles(paths[0]).ToList()
            : paths;
    }

    private static void Emit<T>(IReadOnlyList<T> rows, OutFormat fmt)
    {
        if (fmt == OutFormat.Json) Console.WriteLine(Reporting.ToJson(rows));
        else Console.Write(Reporting.ToCsv(rows));
    }

    private static int Report(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra report: give one or more audio files or a folder.");
            return 2;
        }
        var reports = MapParallel(files, jobs, f => BandwidthReport.Analyze(ffmpeg, f));

        if (fmt != OutFormat.Text)
            Emit(reports.Select(Reporting.ToBandwidthRow).ToList(), fmt);
        else
            foreach (var r in reports)
            {
                Console.WriteLine(Path.GetFileName(r.Path));
                if (r.Error is not null) { Console.WriteLine($"  error: {r.Error}"); continue; }
                Console.WriteLine("  " + r.Metadata!.ToDisplayLine(Path.GetFileName(r.Path)));
                Console.WriteLine("  " + r.Verdict!.Summary);
            }
        return reports.Any(r => r.Verdict?.Kind is VerdictKind.Lossy or VerdictKind.Upsampled or VerdictKind.Mixed) ? 1 : 0;
    }

    private static int Scan(FfmpegPaths ffmpeg, string[] args, OutFormat fmt, int jobs)
    {
        CliOptions.RejectUnknownFlags(args);
        if (args.Length == 0 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("spektra scan: give an existing folder to scan.");
            return 2;
        }
        var root = args[0];
        var files = BandwidthReport.FindAudioFiles(root).ToList();
        var reports = MapParallel(files, jobs, f => BandwidthReport.Analyze(ffmpeg, f));
        // One exit-code rule for both output paths: 1 when any file is likely
        // lossy or upsampled (computed once so the two branches can't drift).
        var findings = reports.Any(r => r.Verdict?.Kind is VerdictKind.Lossy or VerdictKind.Upsampled or VerdictKind.Mixed) ? 1 : 0;

        if (fmt != OutFormat.Text)
        {
            Emit(reports.Select(Reporting.ToBandwidthRow).ToList(), fmt);
            return findings;
        }

        Console.WriteLine($"Scanning {files.Count} audio file(s) under {root} ...");
        int lossless = 0, suspect = 0, lossy = 0, upsampled = 0, mixed = 0, unknown = 0, errors = 0;
        foreach (var r in reports)
        {
            Console.WriteLine($"  {Tag(r)}  {Path.GetRelativePath(root, r.Path)}");
            switch (r.Verdict?.Kind)
            {
                case VerdictKind.Lossless: lossless++; break;
                case VerdictKind.Suspicious: suspect++; break;
                case VerdictKind.Lossy: lossy++; break;
                case VerdictKind.Upsampled: upsampled++; break;
                case VerdictKind.Mixed: mixed++; break;
                default: if (r.Error is not null) errors++; else unknown++; break;
            }
        }
        Console.WriteLine(
            $"{Environment.NewLine}{files.Count} files: {lossless} lossless, {suspect} suspect, " +
            $"{lossy} likely lossy, {upsampled} upsampled, {mixed} mixed, {unknown} unknown, {errors} errors.");
        return findings;
    }

    private static int Check(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra check: give one or more audio files or a folder.");
            return 2;
        }
        var computed = MapParallel(files, jobs, path =>
        {
            try
            {
                var meta = new AnalysisSession(ffmpeg).ReadMetadata(path);
                return (ir: (IntegrityReport?)new IntegrityScanner(ffmpeg).Check(path, meta), err: (string?)null);
            }
            catch (Exception ex) when (ex is AudioDecodeException or IOException)
            {
                return (ir: (IntegrityReport?)null, err: (string?)ex.Message);
            }
        });

        var rows = new List<IntegrityRow>();
        int ok = 0, suspect = 0, corrupt = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var path = files[i];
            var (ir, err) = computed[i];
            rows.Add(Reporting.ToIntegrityRow(path, ir, err));

            if (err is not null || ir?.Status == IntegrityStatus.Corrupt) corrupt++;
            else if (ir?.Status == IntegrityStatus.Suspect) suspect++;
            else ok++;

            if (fmt == OutFormat.Text)
            {
                if (ir?.Status == IntegrityStatus.Ok)
                    Console.WriteLine($"  [OK]      {Path.GetFileName(path)}");
                else
                {
                    var tag = err is not null ? "CORRUPT" : ir!.Status.ToString().ToUpperInvariant();
                    Console.WriteLine($"  [{tag}] {Path.GetFileName(path)} - {err ?? ir!.Summary}");
                }
            }
        }
        if (fmt != OutFormat.Text) Emit(rows, fmt);
        else Console.WriteLine(
            $"{Environment.NewLine}{files.Count} files: {ok} ok, {suspect} suspect, {corrupt} corrupt.");
        return corrupt > 0 ? 1 : 0;
    }

    private static int Audit(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var fresh = paths.Contains("--fresh");
        paths = paths.Where(p => p != "--fresh").ToArray();
        var html = CliOptions.TakeHtml(ref paths);
        CliOptions.RejectUnknownFlags(paths);
        var folder = paths.Length == 1 && Directory.Exists(paths[0]) ? paths[0] : null;
        var targets = folder is not null
            ? FolderAudit.CollectTargets(folder)
            : paths.Select(p =>
            {
                var info = new FileInfo(p);
                return new AuditTarget(p, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
            }).ToArray();
        if (targets.Length == 0)
        {
            Console.Error.WriteLine("spektra audit: give one or more audio files or a folder.");
            return 2;
        }

        var cache = AuditCache.TryOpen(out var cacheError);
        if (cacheError is not null)
            Console.Error.WriteLine($"spektra audit: cache unavailable ({cacheError}); analyzing everything.");

        AuditEntry[] results;
        try
        {
            results = FolderAudit.Run(ffmpeg, targets, jobs, cache, fresh);
            if (folder is not null)
                cache?.PruneFolder(folder, targets.Select(t => t.Path).ToList());
        }
        finally { cache?.Dispose(); }

        // Folder audits report folder-relative paths (matching `scan`), so a
        // row in a deep tree can be located; explicit file args keep the name.
        AuditRow RowFor(AuditEntry r) => folder is null
            ? r.Row
            : r.Row with { File = Reporting.RelativeFile(folder, r.Target.Path) };

        var rows = results.Select(RowFor).ToList();
        if (html is not null)
        {
            try { File.WriteAllText(html, HtmlReport.AuditDocument(rows, "Spektra audit")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"spektra audit: {ex.Message}");
                return 2;
            }
        }

        if (fmt != OutFormat.Text)
        {
            Emit(rows, fmt);
            return results.Any(r => r.HasProblem) ? 1 : 0;
        }

        foreach (var row in rows)
        {
            var bw = row.Error is not null ? "error"
                : $"{row.Bandwidth}{(row.CutoffHz is { } hz ? $" {hz / 1000:0.0}k" : "")}";
            var integ = row.Error is not null ? "ERROR" : row.Integrity;
            Console.WriteLine($"  bandwidth={bw,-18} integrity={integ,-8} {row.File}");
        }
        Console.WriteLine($"{Environment.NewLine}{targets.Length} files, {results.Count(r => r.HasProblem)} with problems.");
        return results.Any(r => r.HasProblem) ? 1 : 0;
    }

    private static int Dupes(FfmpegPaths ffmpeg, string[] args, OutFormat fmt, int jobs)
    {
        var fresh = args.Contains("--fresh");
        var roots = args.Where(a => a is not "--fresh").ToArray();
        var html = CliOptions.TakeHtml(ref roots);
        CliOptions.RejectUnknownFlags(roots);
        if (roots.Length == 0 || !roots.All(Directory.Exists))
        {
            Console.Error.WriteLine("spektra dupes: give one or more existing folders.");
            return 2;
        }

        var cache = AuditCache.TryOpen(out var cacheError);
        if (cacheError is not null)
            Console.Error.WriteLine($"spektra dupes: cache unavailable ({cacheError}); analyzing everything.");

        DupesResult result;
        try
        {
            var progress = new Progress<DupesProgress>(p =>
            {
                if (!Console.IsErrorRedirected)
                    Console.Error.Write($"\r{p.Phase} {p.Done}/{p.Total}    ");
            });
            result = DuplicateScan.Run(ffmpeg, roots, jobs, cache, fresh, progress);
            if (!Console.IsErrorRedirected) Console.Error.Write("\r                        \r");
        }
        finally { cache?.Dispose(); }

        if (html is not null)
        {
            try { File.WriteAllText(html, HtmlReport.DupesDocument(result, "Spektra Duplicate Detective")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"spektra dupes: {ex.Message}");
                return 2;
            }
        }

        if (fmt != OutFormat.Text)
        {
            Emit(DuplicateScan.ToRows(result), fmt);
            return result.Groups.Count > 0 ? 1 : 0;
        }

        foreach (var g in result.Groups)
        {
            Console.WriteLine(
                $"Group {g.Group.Id} · {g.Group.Label} · {g.Group.Members.Count} files · " +
                $"sameness {g.Group.SamenessTier} · reclaim {Reporting.FormatBytes(g.ReclaimableBytes)}");
            foreach (var m in g.Group.Members)
            {
                var row = g.Rows[m.Path];
                var mark = g.Quality.Winners.Contains(m.Path) ? "*" : " ";
                var audio = m.FoundByAudio ? "  found by audio" : "";
                Console.WriteLine(
                    $"  {mark} {m.Path}  [{row.Codec} · {row.Bandwidth}" +
                    $"{(row.CutoffHz is { } c ? $" {c / 1000.0:0.0}k" : "")} · {row.Integrity}] " +
                    $"sameness {m.Sameness:0.00}{audio}");
            }
            Console.WriteLine($"    quality {g.Quality.Confidence}: {g.Quality.Reason}");
        }
        foreach (var na in result.NotAnalyzed)
            Console.WriteLine($"  ! not analyzed: {na.Path} ({na.Reason})");
        Console.WriteLine(
            $"{result.Groups.Count} group(s) · {result.Groups.Sum(g => g.Group.Members.Count)} files · " +
            $"reclaimable {Reporting.FormatBytes(result.ReclaimableBytes)} · {result.FilesScanned} scanned");
        return result.Groups.Count > 0 ? 1 : 0;
    }

    // The GUI's Folder Manifest as a command: EVERYTHING in one folder with an
    // honest chip per file, codec/severity chips from the audit cache when a
    // file was analyzed before, never decoding anything.
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
            Console.Error.WriteLine("spektra inventory: give one existing folder.");
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

    private static int Manifest(string[] paths, OutFormat fmt)
    {
        var html = CliOptions.TakeHtml(ref paths);
        CliOptions.RejectUnknownFlags(paths);
        if (paths.Length != 1 || !Directory.Exists(paths[0]))
        {
            Console.Error.WriteLine("spektra manifest: give one existing folder.");
            return 2;
        }

        var cache = AuditCache.TryOpen(out var cacheError);
        if (cacheError is not null)
            Console.Error.WriteLine($"spektra manifest: cache unavailable ({cacheError}); extension chips only.");

        ManifestFolder root;
        try { root = FolderManifest.Build(paths[0], cache); }
        finally { cache?.Dispose(); }

        if (root.Unreadable)
        {
            Console.Error.WriteLine($"spektra manifest: could not read {paths[0]}.");
            return 2;
        }

        if (html is not null)
        {
            try { File.WriteAllText(html, HtmlReport.ManifestDocument(root, "Spektra Folder Manifest")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"spektra manifest: {ex.Message}");
                return 2;
            }
        }

        var rows = FolderManifest.ToRows(root);
        if (fmt != OutFormat.Text)
        {
            Emit(rows, fmt);
            return 0;
        }

        foreach (var row in rows)
        {
            var severity = row.Severity is { } s ? $" · {s}" : "";
            Console.WriteLine($"{row.Kind,-6} {Reporting.FormatBytes(row.SizeBytes),10}  {row.Path}{severity}");
        }
        Console.WriteLine($"{rows.Count} file(s) · {root.Rollup} · {Reporting.FormatBytes(root.TotalBytes)}");
        return 0;
    }

    private static int Loudness(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra loudness: give one or more audio files or a folder.");
            return 2;
        }
        var computed = MapParallel(files, jobs, path =>
        {
            try { return (r: (LoudnessReport?)new LoudnessMeasurer(ffmpeg).Measure(path), err: (string?)null); }
            catch (Exception ex) when (ex is AudioDecodeException or IOException) { return (r: (LoudnessReport?)null, err: (string?)ex.Message); }
        });

        var rows = new List<LoudnessRow>();
        for (var i = 0; i < files.Count; i++)
        {
            var path = files[i];
            var (r, err) = computed[i];
            rows.Add(Reporting.ToLoudnessRow(path, r, err));
            if (fmt == OutFormat.Text)
            {
                Console.WriteLine(Path.GetFileName(path));
                Console.WriteLine("  " + (err ?? r!.Summary));
            }
        }
        if (fmt != OutFormat.Text) Emit(rows, fmt);
        // Loudness has no "problem" verdict; the only failure signal is a file
        // that could not be measured (mirrors check/audit: any error -> 1).
        return computed.Any(c => c.err is not null) ? 1 : 0;
    }

    private static int Diff(FfmpegPaths ffmpeg, string[] args, OutFormat fmt)
    {
        double? offsetMs = null;
        var thresholdDb = 40f;
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--offset") offsetMs = CliOptions.Double(a, args, ref i);
            else if (a is "--threshold-db") thresholdDb = CliOptions.Float(a, args, ref i);
            else if (a.StartsWith('-')) throw new OptionException($"unknown option '{a}'.");
            else files.Add(a);
        }
        if (files.Count != 2)
        {
            Console.Error.WriteLine("spektra diff: give exactly two audio files.");
            return 2;
        }

        var options = new CompareOptions(OffsetSeconds: offsetMs / 1000, ThresholdDb: thresholdDb);
        CompareReport report;
        try { report = new AudioCompare(ffmpeg).Run(files[0], files[1], options); }
        catch (Exception ex) when (ex is AudioDecodeException or IOException)
        {
            Console.Error.WriteLine($"spektra diff: {ex.Message}");
            return 2;
        }

        if (fmt != OutFormat.Text)
        {
            Emit(new[] { Reporting.ToCompareRow(report) }, fmt);
            return report.IsSame ? 0 : 1;
        }

        Console.WriteLine("A: " + report.MetaA.ToDisplayLine(Path.GetFileName(report.PathA)));
        Console.WriteLine("B: " + report.MetaB.ToDisplayLine(Path.GetFileName(report.PathB)));
        var overlap = AudioMetadata.FormatDuration(TimeSpan.FromSeconds(report.OverlapSeconds));
        var offsetText = $"{report.OffsetSeconds * 1000:+0.###;-0.###} ms";
        Console.WriteLine(report.AlignConfidence is { } conf
            ? $"Aligned {offsetText} (confidence {conf:0.00}) · overlap {overlap}"
            : $"Offset {offsetText} (pinned) · overlap {overlap}");
        if (report.LowConfidence)
            Console.WriteLine(
                $"warning: alignment confidence {report.AlignConfidence:0.00} is low; the offset may be wrong");
        if (report.HasDrift)
            Console.WriteLine(
                $"note: alignment drifts about {report.DriftSeconds * 1000:0} ms across the file; " +
                "a single offset can't fully align it");
        Console.WriteLine("Spectral: " + report.Diff.Summary);
        Console.WriteLine("Null:     " + report.Null.Summary);
        Console.WriteLine(VerdictLine(report));
        return report.IsSame ? 0 : 1;
    }

    private static string VerdictLine(CompareReport r)
    {
        if (r.Null.ResidualRmsDb <= Db.Floor + 1f)
            return "SAME      perfect null (identical samples)";
        return r.IsSame
            ? $"SAME      null depth {r.Null.NullDepthDb:0.0} dB >= threshold {r.ThresholdDb:0.0} dB"
            : $"DIFFERS   null depth {r.Null.NullDepthDb:0.0} dB < threshold {r.ThresholdDb:0.0} dB";
    }

    private static int Image(FfmpegPaths ffmpeg, string[] args, OutFormat fmt)
    {
        if (fmt != OutFormat.Text)
        {
            Console.Error.WriteLine("spektra image: --json/--csv do not apply to image.");
            return 2;
        }

        string? outPath = null;
        string? paletteName = null;
        double? gamma = null;
        var options = new ImageOptions();
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "-o" or "--out") outPath = CliOptions.Value(a, args, ref i);
            else if (a is "--palette") paletteName = CliOptions.Value(a, args, ref i);
            else if (a is "--gamma")
            {
                var g = CliOptions.Double(a, args, ref i);
                gamma = g > 0 ? g : throw new OptionException($"--gamma must be greater than 0, got '{g}'.");
            }
            else if (a is "--floor") options = options with { FloorDb = CliOptions.Float(a, args, ref i) };
            else if (a is "--fft") options = options with { WindowSize = CliOptions.Int(a, args, ref i, min: 16) };
            else if (a is "--channel") options = options with { Channel = CliOptions.Int(a, args, ref i, min: 1) - 1 };
            else if (a is "--columns") options = options with { MaxColumns = CliOptions.Int(a, args, ref i, min: 1) };
            else if (a.StartsWith('-')) throw new OptionException($"unknown option '{a}'.");
            else files.Add(a);
        }
        if (files.Count != 1 || Directory.Exists(files[0]))
        {
            Console.Error.WriteLine("spektra image: give one audio file (folders are not supported).");
            return 2;
        }

        // The render follows the app's saved theme (palette + tightness);
        // --palette/--gamma override. Resolved after the loop so a later
        // --floor still shapes db-pinned custom palettes correctly.
        var saved = SettingsStore.Load(SettingsStore.DefaultPath);
        var palettes = PaletteRegistry.LoadWithCustom();
        if (paletteName is not null && !palettes.Has(paletteName))
            Console.Error.WriteLine(
                $"spektra image: unknown palette '{paletteName}'; using turbo. " +
                $"Available: {string.Join(", ", palettes.Names)}.");
        options = options with
        {
            PaletteLut = palettes.BakeLut(
                paletteName ?? saved.Palette, options.FloorDb, gamma: gamma ?? saved.PaletteGamma),
        };

        var input = files[0];
        outPath ??= Path.ChangeExtension(input, ".png");
        try
        {
            var (w, h, rgb) = new SpectrogramImage(ffmpeg).Render(input, options);
            using var stream = File.Create(outPath);
            PngWriter.Write(stream, w, h, rgb);
            Console.WriteLine($"Wrote {outPath} ({w}x{h})");
            return 0;
        }
        catch (Exception ex) when (
            ex is AudioDecodeException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"spektra image: {ex.Message}");
            return 2;
        }
    }

    private static string Tag(FileReport r)
    {
        if (r.Error is not null) return "[ERROR   ]      ";
        var kind = r.Verdict!.Kind switch
        {
            VerdictKind.Lossless => "[LOSSLESS]",
            VerdictKind.Suspicious => "[SUSPECT ]",
            VerdictKind.Lossy => "[LOSSY   ]",
            VerdictKind.Upsampled => "[UPSAMPLE]",
            VerdictKind.Mixed => "[MIXED   ]",
            _ => "[UNKNOWN ]",
        };
        var cut = r.Verdict.CutoffHz is { } hz ? $" {hz / 1000:0.0}k".PadRight(6) : "      ";
        return kind + cut;
    }

    private static int Usage(int exitCode)
    {
        Console.WriteLine("""
            Spektra - audio bandwidth / integrity analyzer

            Usage:
              spektra report <file|folder> ...   Bandwidth verdict per file.
              spektra scan <folder>              Compact bandwidth scan of a library.
              spektra check <file|folder> ...    Integrity check (corruption / missing data).
              spektra audit <file|folder> ...    Bandwidth + integrity together (cached).
              spektra dupes <folder> ...         Find duplicate songs; mark the best copy (cached).
              spektra manifest <folder>          List EVERYTHING in a folder with type chips (no decoding).
              spektra inventory <folder>         Tags + embedded art per file, machine-readable (no decoding).
              spektra loudness <file|folder> ... Loudness (LUFS), true peak, and dynamics.
              spektra diff <fileA> <fileB>       Compare two files: align, spectral diff, null test.
              spektra image <file>               Render the spectrogram to a PNG (no window).
              spektra --version                  Print the version.
              spektra --help                     Show this help.

            Add --json or --csv to any command for a machine-readable report,
            e.g. spektra scan Music --csv > report.csv

            Folders are analyzed in parallel. By default Spektra uses about 80% of
            the CPU cores; cap it with --jobs N (or -j N), e.g. spektra scan Music -j 4

            audit caches results per file (keyed by size + mtime) in the app data
            folder, so repeat runs only analyze new or changed files; --fresh re-analyzes.
            Add --html out.html to also write the results as a self-contained HTML page.

            dupes finds the same recording across folders and formats (audio
            fingerprint match, not filename), rides the same cache as audit, and
            marks each group's best copy; give two or more folders to also find
            duplicates that live in different libraries. --fresh re-analyzes.
            Add --html out.html to also write the groups as a self-contained HTML page.

            manifest lists every file, audio or not, with a type chip per file;
            files the audit has seen before get their real codec and verdict
            chips from the cache. It never decodes, works without ffmpeg, and
            --html out.html writes the collapsible tree page.

            diff aligns the files automatically; pin an offset with --offset <ms>
            (positive = B later than A) and tune the verdict with --threshold-db <N>
            (default 40: the null depth needed to count as SAME).

            image options: -o <out.png>, --palette <name>, --gamma <g> (the app's
            Tightness), --floor <dB>, --fft <size>, --channel <n>, --columns <max
            width>. Palette and gamma default to your app settings; the rest to
            -120, 2048, mix, 2048. --palette also accepts custom palettes: JSON
            files dropped in %APPDATA%\Spektra\palettes or a palettes folder next
            to the app (see docs/cli.md for the format).

            Exit code is 1 on findings (report/scan: lossy, upsampled, or mixed; check:
            corrupt files; audit: a transcode, an upsample, or corruption - an
            honest lossy file is not a problem; dupes: one or more duplicate
            groups found; loudness: a file could not be measured; diff: the
            files differ), 2 on setup errors (an unknown or malformed
            option, or no existing file or folder given).
            Requires ffmpeg + ffprobe on PATH.
            """);
        return exitCode;
    }
}
