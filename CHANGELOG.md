# Changelog

All notable changes to Spektra are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `spektra --diff "A" "B"` opens two folders straight into a folder diff: both become scan roots, the scan starts, and Only differences is already on. The folder diff was the one documented view with no way in from a command line, because `--compare` deep-links a file comparison and `--dupes` a duplicate scan, but a diff draws a column per scan root and `--dupes` takes exactly one folder. A one-off diff leaves your saved root list alone, the same way the Explorer "Find duplicates" verb does. Naming two spellings of the same folder gives one column rather than a pretend comparison.
- Compare two folders that are already open, from the Analyze menu. With a folder tab in front of you, Compare 'Album' with lists every other folder open in the window; picking one opens the pair as a folder diff with Only differences already ticked. Until now the second side could only be named by finding a folder on disk that was often already on screen, and the diff itself only by opening something called Duplicate Detective, which is not where anyone looks for it. A tab you have drilled into offers the subfolder you are looking at, matching what the rest of the Analyze menu acts on, and a folder is never offered against itself. The submenu ends with a browse item, so the command still works with one folder open or none.

### Changed
- A folder tab's progress bar shows how much of the folder has been analyzed, not how far along the current run is. It fills the moment the tab opens, from what the cache already knows, and stays there: a folder analyzed last week reads full before you touch anything, and clicking Analyze on it no longer looks like nothing happened. It used to measure progress through the work one run had to do, so analyzing ten stragglers in a hundred-file folder swept it from empty to full while the folder itself barely moved, and it sat at zero whenever nothing was running. Drilling into a subfolder re-reads it for that subfolder, and the severity filter does not affect it, because hiding clean rows does not make those files unanalyzed. The percentage and time remaining beside the bar are unchanged and still describe the run in progress.
- The File column in a folder tab stops growing with the window. It was the only column allowed to stretch, so it absorbed everything the other nine left over: on a wide window that reached roughly 1370 units for a path needing about 300, and the same greed pushed the verdict columns off the right edge of a narrower one. It now takes the slack up to a limit and leaves the rest as blank grid. A width you set by dragging is unaffected and still persists.

### Fixed
- File > Compare… works with nothing open. It used to insist that two files already be open as tabs, and refuse otherwise by writing one line to the status bar at the foot of the window, a long way from the menu that was just clicked. The item stayed enabled while doing so, in a menu where every other conditional command dims, and it kept the ellipsis that everywhere else means "this opens a dialog". It now opens one. Two files chosen there are compared without being opened as tabs first, which is what the `--compare` switch has always done from the command line; only this entry point insisted otherwise. Choosing a single file asks what to compare it with rather than refusing, since clicking one file and pressing Open is a fair reading of "choose two files", and choosing more than two says so instead of quietly comparing a pair you never named. With two or more files already open the existing chooser still appears, because picking from a list beats finding them on disk again.
- The folder diff fills the width of the window. Its columns were sized to their own contents and pinned to the left edge, leaving the rest of a wide window empty. The window gives its remaining space to the last of its stacked children, and which child that is depends on the order the two result views are written, not on which one is on screen: the ordinary group list happened to be written last, so it always filled and the diff never could.
- Duplicate Detective and Folder Manifest reopen at the size you left them, never maximized and never larger than most of the screen. Closing one maximized could record the full-screen size as the size to come back to, because maximizing can outrun the window's own bookkeeping, and the record then kept itself alive: reopening applied the size and the maximized state together, nothing could correct the remembered size while the window stayed maximized, and un-maximizing produced a full-screen "normal" window with no real size to return to. A tool window is transient besides, so coming back full-screen every session is rarely what anyone meant. The main window still reopens maximized when you leave it that way; it now just cannot mistake the maximized size for the one to un-maximize to.
- CSV exports neutralize spreadsheet formulas. A report's file names and tags are chosen by whoever made the files, and a cell starting with `=`, `+`, `-` or `@` is a formula to Excel and LibreOffice the moment the export is double-clicked, able to reach the network or, behind one generic warning, run a command; quoting does not defuse it, because the cell is evaluated after unquoting. Such cells now carry a leading apostrophe, the spreadsheet convention for "this is text, not a formula", so a hostile artist tag renders as its own characters. Only text columns are guarded, so numbers keep their minus signs, and the same name is guarded identically in every export, so joining an audit to an inventory on the path column still matches. A tag holding a bare carriage return also no longer bends the row shape. JSON exports are unchanged, having never had the problem.
- One unreadable probe can no longer take a whole run down. ffprobe is trusted to exit 0 and still emit something unusable: killed mid-write, or a different program answering to the name on PATH. Output that was not JSON at all already read as a per-file decode error, but output that was valid JSON in the wrong shape (a list where the report goes, a stream entry that is a bare number, a channel count written as text) threw an exception the analysis pipelines do not catch, which ended an inventory or audit mid-walk with a stack trace instead of finishing with one error row. Every wrong shape now reads as that file's own decode error, the same as a corrupt file, and the run carries on to the files that are fine.
- The update check is harder to turn against you and harder to crash. Its View release button launches whatever URL the GitHub API handed back; it now refuses anything that is not an https page on github.com, the only place a Spektra release can live, so a tampered or garbled payload yields a hidden button rather than the operating system opening an arbitrary target. A payload whose fields carry the wrong types (a number where the tag or the notes belong) now reads as a failed check instead of throwing from under the JSON reader and taking the app with it.
- Scans no longer wander through folder links. A junction or directory symlink inside a scanned folder was followed by every walk: analysis, Duplicate Detective, Folder Manifest, inventory. Following one widens the scan to files you never chose (a link to a whole drive pulls the drive in), lists the same file under two names so a track can look like its own duplicate, and a link pointing back at an ancestor recurses until Windows runs out of path length. Links are now listed but never entered: the manifest shows one with a link chip instead of pretending its contents live here, and the other walks skip past it. Choosing a link itself as the folder to scan still works, because that is you choosing its target. Files that are reparse points, such as OneDrive placeholders, are unaffected and keep appearing everywhere they did.

## [0.20.0] - 2026-08-21

### Added
- `spektra inventory <folder>`, a machine-readable picture of a whole library: one row per file with its tags, its stream facts, and whether it carries embedded cover art. It exists so another program can do the librarian work Spektra deliberately does not, such as renaming from tags or finding albums with no artwork, without running its own probe across thousands of files. Nothing is decoded, so it runs in seconds whatever the size of the library. Files that are not audio get rows too, which is how a folder's `cover.jpg` turns up beside the tracks and one export answers both "does this track have a thumbnail" and "does this album have artwork on disk". Tags are normalized rather than passed through, because taggers disagree: `5/12` becomes a track and a total, the separate Vorbis `TOTALTRACKS` fills the total when the slash form is absent, any date shape becomes a plain year, and a tag that is present but blank reads as absent. Bandwidth and integrity verdicts are deliberately absent, since those need a full decode; `spektra audit` still has them and exports the same folder-relative path, so the two files join on one column.
- Only differences, a folder diff inside the Duplicate Detective window. Point it at two folders holding nearly the same music and tick the box beside the filter: every track the scan is confident both folders have disappears, and the results become a split view with one column per folder, listing the tracks only that side holds. A folder with nothing unique still gets a column saying so, because "this folder has no extras" and "this folder is not in the comparison" must not look alike. Matches too weak to take on trust sit underneath rather than being hidden, since that is the one call a diff cannot make for you. It ignores format and quality entirely, because a FLAC and the MP3 made from it are the same track; choosing which copy to keep is what the ordinary group view is for, one untick away. The footer counts what was hidden as well as what is shown, so an empty diff reads as "these folders hold the same music" instead of as a scan that found nothing.

### Changed
- **The command-line tool is now `spektra-cli`, and the installer ships it.** It was published only as a separate archive, while the MSI's "Add to PATH" option put the *app* on your PATH under the name `spektra`. So every command-line example in the README and the CLI guide, run on a normal install, opened a window and printed nothing. Renaming rather than shipping a second `spektra` is deliberate: the two programs share an argument vocabulary, since the app takes the dupes and manifest switches as launch arguments, so the same command line meant different things depending on which binary PATH happened to resolve to. Now `spektra` opens the app, `spektra-cli` runs the tool, the installer puts both in place, and the Options page says which is which. If you already had the CLI archive on your PATH, the binary inside it is renamed too, so scripts calling `spektra scan …` need `spektra-cli scan …`.

### Fixed
- The command-line tool's analysis cache works in released builds. Every published archive was missing the native SQLite library: the publish left it beside the executable and the packaging step copied only the executable itself. The tool then fell back to "cache unavailable" and re-analyzed everything from scratch on every run, whole libraries included, saying so in a single line on stderr that was easy to miss in a redirect. The library is now built into the executable, so an archive really is the one self-contained file it always claimed to be.
- Duplicate Detective no longer groups different songs that happen to share a key. Twelve of every fingerprint word's 32 bits compare pitches inside a single moment, so they describe a track's key and chord colour rather than its tune, and two unrelated songs in the same key agreed on most of them from start to finish: one reported group held five files that were really two different songs, every member scored 0.41. Each pair is now scored against how much it would agree by chance, measured by scoring the same pair a second time with one side played backwards and keeping only the excess. Backwards rather than time-shifted, because a sweep or a loop still resembles itself at any offset. On the same measurements a true copy held at 0.93 while same-key strangers fell from 0.29 to 0.10, widening the distance between a real copy and a coincidence from about three times to nine. Fingerprints themselves are unchanged, so nothing re-analyzes; rescanning an existing library simply reports the corrected numbers.
- The Duplicate Detective filter row no longer vanishes when a scan finds no duplicates. It appeared only once there was a group to narrow, which hid the Only differences box in exactly the case a folder diff is for: two folders that share nothing still differ, and loudly.

## [0.19.0] - 2026-08-03

### Added
- The Analyze menu knows which folder you are looking at. In a folder tab its three folder commands name the folder and act on it: Analyze 'Album', Find Duplicates in 'Album', Manifest of 'Album'. Drill down into a subfolder and the names follow, so the commands act on the drilldown scope rather than always on the whole tab. Duplicate Detective and Folder Manifest open already pointed at that folder instead of opening empty and asking you to pick it again. The two track-only items, Check Integrity and Measure Loudness, now dim in a folder tab: they were always inert there, but nothing said so, and clicking them looked like the app ignoring you.
- Rescan in the Folder Manifest window, as a button or F5. It lists the current folder again and picks up files added or removed on disk, which previously meant clearing the path box and choosing the folder a second time.
- Refresh in a folder tab, as a button beside All/None or Ctrl+F5. It re-reads the folder from disk and keeps the checkboxes you ticked: files that still exist stay checked, files added since appear unchecked, and deleted ones disappear. Cached verdicts repaint without re-analyzing anything. Closing the tab and dropping the folder again was the only way to do this before.

### Changed
- Analysis is about 1.6 times faster per file. A five-minute track goes from roughly 396 ms to 250 ms of analysis, because the spectrogram now transforms real input at half size instead of running a full complex transform and discarding the mirrored half, and reads levels from power directly rather than taking a square root per frequency bin only to hand it to a logarithm. Levels are arithmetically the same but not bit-identical: on broadband material they agree to about 0.0003 dB, while individual near-silent bins of a pure tone can differ by a few tenths of a dB, because both paths reach those bins by cancelling large numbers against each other and they now do it in a different order. Every test fixture's verdict and cutoff is unchanged, but cached rows re-analyze once as insurance the first time you open a folder.
- Folder tabs and folder listings open far faster, especially over a network share. Both walks were asking Windows about every file twice, once to list the folder and once more for the size and timestamp the listing had already returned. Over a 3200-file tree that alone was the difference between 62 ms and 8 ms locally, and every one of those discarded questions was a round trip on a share, which is what made a large listing look like it had hung.
- Analyzing a folder writes its results roughly 28 times faster, and re-opening a folder whose files have moved no longer stalls. The analysis cache was flushing to disk once per file, and clearing out entries for files that had gone was flushing once per entry: 2500 of them took 3.7 seconds, paid every time the folder was opened or refreshed, and now takes about 12 ms.
- The Folder Manifest names the folder it is listing from the moment the walk starts, rather than leaving the previous folder in the path box until it finishes.
- Moving the pointer over a spectrogram no longer redraws the whole plot when the crosshair is switched off, and the average-spectrum overlay is no longer rebuilt from scratch on every frame while it is on.
- Duplicate Detective reports match progress in batches rather than once per compared pair, so the progress bar no longer costs more than the matching it is reporting on.

## [0.18.1] - 2026-07-28

### Changed
- The installer asks with checkboxes. Adding `spektra` to your PATH and adding Spektra to the Explorer right-click menu were already optional, but they lived in Windows Installer's feature tree, where switching one on means noticing that a small drive icon is a dropdown and choosing "Will be installed on local hard drive" from it. They are now two plain checkboxes on an Options page, so the Explorer integration added in 0.17.0 is actually findable. Explorer integration is still off unless you tick it, PATH is still on by default, and a silent install is unchanged. Choosing Modify in Installed apps opens the same page, which is now the way to add or drop either one later; it opens with your current choices ticked, so an update keeps what you asked for.

## [0.18.0] - 2026-07-28

### Fixed
- "Analyze with Spektra" opens the file you clicked. The verb handed the selection over as `%*`, which in a shell verb command expands to the arguments that follow the file rather than to the file itself, so it arrived empty: the app started with no command line at all and restored the previous session, which looks like an ordinary window that ignored the click. Every audio extension's verb was affected in 0.17.0. Installing this version rewrites the registry rows.

### Changed
- Spektra runs as a single instance. Launching it while it is already open hands the new command line to the running window instead of starting a second one: files arrive as extra tabs, a folder as another browse, `--dupes` and `--manifest` open their windows, and a bare launch brings the window to the front. This is also what puts a multiple selection into one window, because Windows invokes a right-click verb once per selected file and a command-line verb receives one file per invocation, so three files mean three launches.

## [0.17.0] - 2026-07-27

### Added
- Optional Explorer integration, off by default and switched on from the installer's Custom Setup page: an "Analyze with Spektra" verb on audio files (a multi-file selection opens as tabs in one window), a Spektra submenu on folders and folder backgrounds with Open in Spektra · Find duplicates · List folder contents, and Spektra in the Open With list. It never changes which program opens your audio files.
- `--dupes <folder>` and `--manifest <folder>` launch switches, opening Duplicate Detective or Folder Manifest straight onto a folder.
- An "Uninstall Spektra" Start Menu entry.

### Changed
- Uninstalling now removes the downloaded copy of ffmpeg, and offers to remove settings, custom palettes, and the analysis cache. Both were previously left behind.

## [0.16.0] - 2026-07-26

### Added
- Mixed, a bandwidth verdict for a file whose provenance changes partway through. A compilation, DJ mix, or continuous set can carry a transcoded track among genuine ones, and judging the file as a whole missed it: the genuine tracks supplied the high frequencies the transcoded one lacked, so the file read as clean. Spektra now scans in 30-second windows and reports Mixed, naming the suspect stretch's start and end, when a wall that does not belong there covers two consecutive windows. A file that already reads lossy or upsampled is returned as it was, so only an otherwise-clean or ambiguous read can be escalated, and each window is judged by the rule the row flag already used: inside a lossless container any lossy wall counts, while an mp3 or aac is measured against its own declared bitrate. Mixed appears everywhere a verdict does (the bandwidth banner, the audit grid, tree markers, HTML reports, `[MIXED]` in `scan`) and counts as a finding in the `audit`, `report`, and `scan` exit codes. Cached audits re-analyze once, so an existing library gets re-judged.

### Changed
- The command line rejects an unknown option instead of ignoring it, and an option that expects a value no longer takes the following flag as that value: `spektra image --palette --gamma 1.2 file.wav` now answers `--palette needs a value`, while a negative number such as `--floor -100` is still read as a value. A verb exits 2 when its arguments or environment are wrong, keeping exit 1 for what the analysis found.
- Cancelling a folder audit or a duplicate scan takes effect while a file's metadata is being read, instead of waiting for that read to finish.

### Fixed
- A folder audit, duplicate scan, or any other command that walks a folder (`report`, `scan`, `check`, `loudness`) no longer stops when it reaches a folder Windows will not let it read. The unreadable folder is skipped and the walk goes on, so one protected subfolder cannot cut a scan short.
- HTML reports no longer follow the machine's locale: cutoff and sameness numbers keep a dot decimal, so the sortable Cutoff column sorts by the true value rather than a truncated one, and the generated-on timestamp cannot shift its year on a machine with a non-Gregorian calendar. Byte sizes read the same everywhere in the app for the same reason.
- A malformed fingerprint in the cache can no longer pass its length check and ask for an enormous allocation.

## [0.15.1] - 2026-07-21

### Changed
- The Controls window (F1) is wider and lays its groups out in two columns, main-window basics (Files & tabs, View & zoom, Save & export) on the left and the deeper features (Analysis, Compare, Tools & right-click) on the right, so everything fits on screen without scrolling.

## [0.15.0] - 2026-07-21

### Added
- Right-click menus across every listing: audit grid rows (the whole row), browse-tree files and folders, Duplicate Detective copies and groups, and Folder Manifest files and folders. The verbs are Spektra's own where the surface allows it (Open spectrogram, Re-analyze one file fresh without touching the checkbox worklist, Analyze this folder honoring the cache, Compare with winner, Audit this folder, Show in manifest), with Copy path and Reveal in Explorer below a separator everywhere.
- Compare with winner: a Duplicate Detective copy opens side by side with its group's best copy in the main window (the winner as A), connecting the dupes list to the comparison view. Right-clicking a group's box copies the loser paths (every copy except the winner, one per line) or all paths, for feeding another tool while Spektra itself stays read-only.
- A results filter in the Duplicate Detective once a scan has groups: every word must match a group's label or one copy's path, applied live, with the footer counting the groups shown.
- A launch policy in Preferences: Start new (the default) opens the app and its tool windows empty; Keep last reopens the previous session's tabs, Duplicate Detective folders, and manifest folder. Restored file tabs decode lazily on first selection, a launch with a file argument stays targeted either way, and layouts, column widths, placements, and recent files persist regardless of the choice.
- The Folder Manifest's path box became an address bar: it shows the folder being listed, Enter loads a typed one, Esc reverts an edit, and emptying it clears the listing. The Kind and Size columns resize by dragging their header edges (saved across runs), folder headlines carry the recursive size on disk next to the rollup, and huge listings can be cancelled: Browse becomes Cancel while the walk runs.
- Cross-tool hops: a manifest folder's menu offers Audit this folder (opens it as an audit tab in the main window), and an audit-tree folder's menu offers Show in manifest for the trip back.
- Multi-row selection in the audit grid: Copy path and Re-analyze act on the whole selection when the clicked row is part of it; Open spectrogram stays single-row.
- `spektra manifest <folder>` in the CLI: the Folder Manifest as a command, with cache-decorated chips, a rollup plus byte-total summary line, and `--csv`/`--json`/`--html` output; it never decodes, and it is the one command that works without ffmpeg installed.
- Clear all in the Duplicate Detective empties the scan-folder list in one click; like Remove, it leaves the last results on screen until the next scan replaces them.
- Duplicate member rows highlight under the pointer, and the highlight stays pinned while that row's context menu is open, so the file the verbs act on is always visible.
- The Controls window (F1) documents the right-click verbs, the audit grid's multi-select semantics, and the tool windows' path-box keys.

### Changed
- While a scan or listing runs, the inputs that would change it freeze with a footer note instead of silently desyncing: the Duplicate Detective's folder list (buttons, path box, and drops) during a scan, and the manifest's Browse during a load; refused drags show the no-entry cursor. Export in both tool windows dims until there is actually a result to write.
- The Folder Manifest disables Open spectrogram on non-audio files, judged by the same extension set the audit pipeline walks, so a jpg or nfo can no longer open a tab that could only error.
- Both tool windows' button columns are compact (26 px, matching their input boxes), and the manifest's header mirrors the Duplicate Detective's input/actions split with a vertical rule.

### Fixed
- Opening a file that is already open focuses its existing tab instead of adding a duplicate.
- Closing the Duplicate Detective mid-scan cancels the scan. It used to keep running with no cancel path left, holding most cores busy, and reopening the window could start a second scan beside it; completed analysis stays cached, so cancelling loses nothing.

## [0.14.0] - 2026-07-19

### Added
- Duplicate Detective, a new window that finds duplicate recordings across one or more folders by listening to the audio rather than trusting filenames or tags. A clean-room chroma fingerprint matches re-encodes, format conversions, and copies that differ only by padding or a small pitch shift; matches are grouped, and each group is ranked by quality (lossless over lossy, higher bitrate over lower) with a confidence and a plain-language reason, so the copy worth keeping is marked. Groups export to HTML, CSV, or JSON, and the command line gains a `dupes` verb.
- Folder Manifest, a new window that lays out a folder as a tree with a type chip per file (its real codec once analyzed, the extension until then), Name, Kind, and Size columns, and a per-folder rollup. An extension filter narrows the tree, and the export writes exactly the rows the filter is showing, to HTML, CSV, or JSON.
- Every opened file runs its integrity check automatically on load, so a corrupt or truncated file is flagged the moment it opens instead of waiting for Ctrl+I. A Preferences toggle turns this off.
- The spectrogram gained axis titles (Time along the bottom in minutes and seconds, Frequency up the left in kilohertz) and a labelled decibels-below-full-scale legend that sits shorter than the view. Tick density on the time, frequency, and legend scales adapts to the plot's pixel size, staying readable from a small window to full screen.
- Self-contained dark HTML reports for the folder audit, Duplicate Detective, and Folder Manifest, styled to match the app; the command line's `audit` and `dupes` verbs accept `--html`. Artist, title, and album tags are now read from the container where present.

### Changed
- Export is a dropdown that lists HTML, CSV, and JSON directly and opens on hover, on the toolbar buttons and in the File menu alike. The File menu splits Export (the current file) from Audit Folder and greys out the current-file entries when no file tab is open.
- The folder audit's Export writes exactly the rows on screen: it honors the active severity filter and any drilldown scope, and greys out when nothing is shown, so what you export is what you see.
- Double-clicking a file in the folder tree opens it as a spectrogram tab, the same as double-clicking a results row, and works on files that have not been analyzed yet (previously a tree double-click only jumped to the file's row in the grid). Hovering a tree file shows its full path.
- Footers, dividers, palette shades, and spacing are aligned across the Duplicate Detective, Folder Manifest, and main windows: full-width separators, a lighter-grey footer band of matching height, and a separator between a window's top controls and its content.

### Fixed
- The folder audit's Export no longer produces an empty report or an invalid file name when the audited folder is a drive root, whose `:` and `\` are now sanitized for the platform.

## [0.13.6] - 2026-07-15

### Added
- A sharp bandwidth cutoff at or above 20 kHz now reads Suspicious (amber banner, yellow dot) instead of clean, even in a lossless file: MP3-320-class encodes and honestly band-limited masters look identical up there, so it is worth a look rather than a pass or an accusation. A wall in the last few percent below Nyquist still counts as the file's own anti-alias filter and stays green, an honest lossy file's own high wall stays neutral in the folder grid, and a 44.1 kHz recording resampled to 48 kHz is no longer accused as a red transcode.

### Changed
- The Drilldown button above the folder tree is greyed out until a folder is selected.
- The audit cache invalidates itself once (analysis version bump): cached bandwidth verdicts change under the new high-cutoff rule, so the next audit re-analyzes instead of replaying stale ones.

### Fixed
- A file tab could hang on "Analyzing…" forever: when a channel overview failed with something other than a decode error, the failure resurfaced inside the next re-analysis (channel switch, FFT change) and silently killed it.

## [0.13.5] - 2026-07-13

### Added
- Double-clicking a file in the folder tree jumps to its row in the grid, when the row exists and the current filter and scope show it (the reverse of double-clicking a row to open the file as a tab).
- The folder grid's columns can be resized by dragging their header edges, and the layout (column widths plus the tree pane width) is saved when the app closes and restored on the next start.

### Changed
- The integrity summary separates its lead with a middot instead of a second colon ("Integrity: Worth a listen · 2 decode errors.") and counts pluralize properly instead of "error(s)".
- The cutoff marker's tick on the frequency ruler is now thicker and longer than the ruler's own ticks, so the marker reads as an anchor instead of one more tick.
- The wordiest tooltips across the folder view, the FFT selector, the compare strip, and Preferences are trimmed to their point.

### Fixed
- The Bandwidth and Integrity columns no longer clip a long verdict when the first screenful of rows happens to hold only short ones; both columns now have a minimum width that fits their widest value.
- CLI output is written as UTF-8 regardless of the console codepage, so middots and the compare view's delta glyph survive redirects and pipes.

## [0.13.4] - 2026-07-13

### Added
- The folder grid's File column now leads with the same marker dot the tree shows for that file: the row's whole verdict, the worst of bandwidth and integrity, violet for upsampled. The Integrity column's dot keeps coloring integrity alone, so a transcode that decodes cleanly reads as red overall next to green integrity instead of looking like a tree/grid mismatch.

### Fixed
- Audit cache keys no longer depend on how the folder argument was spelled: auditing `C:/Music` (forward slashes, or a relative path) used to write cache entries that a later run on the canonical path silently missed, re-analyzing everything from scratch.

## [0.13.3] - 2026-07-13

### Added
- The single-file tab's header now shows the file's full path on hover, and right-clicking it offers Copy path (with a status-bar confirmation), so the file behind a spectrogram can be found again and pasted elsewhere.
- A folder tab whose analysis is running swaps its folder icon for a spinner in the tab strip, so a run left in the background stays visible from any tab.

### Changed
- Only one folder analysis runs at a time across tabs: starting Analyze elsewhere names the busy tab in the status bar instead of competing with the running analysis for the same CPU cores.

## [0.13.2] - 2026-07-13

### Changed
- Folder audits now report each file's path relative to the audited folder instead of the bare name: in the folder tab's File column (hover for the full path; sorting groups rows by folder), in its Export, in File > Export Folder Report, and in CLI `audit` runs on a folder. Same-named tracks from different albums can finally be told apart, and a row in a report can be located again. CLI audits of explicit file arguments keep the bare name.

### Fixed
- The folder analysis order preference now controls what you actually see. Parallel analysis used to hand each worker its own contiguous chunk of the worklist, so several cursors crawled distant parts of the tree at once and every schedule looked random; files are now dispatched strictly in the scheduled order, and "Folder order (top to bottom)" follows the tree exactly as shown, at the same speed.
- Cached verdicts hydrate into the grid in tree order when a folder opens, and the live progress readout no longer sits flush against the severity filter.

## [0.13.1] - 2026-07-13

### Added
- The order the folder tab analyzes checked files in is now a preference (Ctrl+E, "Folder analysis order"): folder order (top to bottom, the default), smallest files first for quick early results, or largest files first so the time estimate settles sooner. Analysis stays parallel; the choice applies from the next Analyze.
- A live readout beside the folder tab's progress bar shows the percentage done, the file count, and the remaining-time estimate while analysis runs; the status bar keeps just the final summary.

### Fixed
- The integrity check no longer marks healthy files as corrupt when an old or sloppy encoder left harmless quirks in every frame (mp3 padding slop, bogus frame CRCs); whole libraries of older rips were flagged even though they decode and play cleanly. Decode errors are now counted with ffmpeg's default error detection, which still catches real damage (resync failures, invalid data, truncation). Cached audit rows re-analyze once after updating.

## [0.13.0] - 2026-07-13

### Added
- The folder tab is now a browse-first workspace: dropping a folder (or Ctrl+Shift+O, or a folder on the command line) instantly shows a checkbox tree of its files and folders instead of starting a scan, with any verdicts cached from earlier audits painted onto the tree and grid right away. Tick files or whole folders (folder checkboxes cascade, and show a partial state when only part of a subtree is checked) and press Analyze (or F5) to audit exactly the checked set; Shift+F5 or Shift+click Analyze re-analyzes even cached files.
- Tree markers and rollups: every file and folder in the tree carries a severity dot (not analyzed, clean, suspect, problem, or upsampled), and each folder shows a live "5/12 · 2 problems" style summary while analysis runs.
- Drilldown and Up: scope the grid to one folder's subtree (a "Scope:" breadcrumb shows the focus, Show all clears it), then widen back one folder at a time.
- The detected bandwidth cutoff is drawn on the spectrogram as a thin line in the verdict's color, with a matching tick on the frequency ruler, so a lossy wall is visible at a glance; the line tracks zoom, pan, and the log/linear axis.
- Folders now appear in File > Open Recent alongside files, and reopen as folder tabs.
- Folder-audit tabs show a small folder glyph in the tab strip, and the folder view gained tooltips throughout (including the Shift-to-re-analyze hint on Analyze).

### Changed
- Dropping a folder no longer analyzes anything by itself; analysis is explicit via Analyze or F5. The severity filter, export, double-click to open, byte-weighted progress, and remaining-time estimate all work as before over the analyzed set, and cancelling still keeps the rows already finished.
- The integrity verdict moved to the grid's last column and gained a severity dot so problem files read at a glance.

### Fixed
- Pruning stale audit-cache rows now works when the audited folder is a bare drive root (for example Z:\); it silently skipped such folders before.

## [0.12.0] - 2026-07-12

### Added
- A dedicated Controls window, opened from Help > Controls or by pressing F1, listing every keyboard and mouse shortcut grouped by task (files and tabs, view and zoom, analysis, save and export, compare).

### Changed
- The About window is now a diagnostics panel. It shows the .NET runtime and the detected ffmpeg build (with where it was found), adds a Copy info button that copies those details to the clipboard for bug reports, and links to the project and its license. The keyboard and mouse reference it used to list moved to the new Controls window.

## [0.11.3] - 2026-07-12

### Changed
- Analysis runs faster and allocates far less memory. The FFT behind the spectrogram, cutoff detection, and compare-view alignment was rewritten to run without per-transform allocations, so the transform itself allocates nothing, spectrogram generation allocates a fraction of what it used to, and the compare/alignment path no longer churns the large-object heap. The first audit after updating re-analyzes each cached file once.

### Fixed
- A corrupt row in the audit cache is treated as a cache miss and re-analyzed, instead of making the whole lookup fail.
- The built-in ffmpeg download aborts with a clear error if it stalls (no data for 30 seconds) instead of hanging indefinitely.

## [0.11.2] - 2026-07-11

### Fixed
- The built-in ffmpeg download works again: the pinned build URL had gone stale (a 404), so first-run setup and `tools/get-ffmpeg.ps1` both failed. They now install a current ffmpeg build with its SHA-256 verified against the source, and fall back to the latest build automatically if the pinned one is ever removed.
- Reading a file's metadata no longer risks hanging when ffprobe emits many warnings about it: its output and error streams are now drained together instead of one after the other.
- A malformed custom-palette JSON (for example a non-numeric `at` or `db` position) is skipped with a reason instead of crashing the app on startup, when opening Preferences, or in `spektra image`.

### Changed
- The spectrogram surfaces allocate less while drawing (the legend ramps and axis labels are cached), for smoother interaction and lower memory churn.

## [0.11.1] - 2026-07-11

### Changed
- Folder audits are faster: each file is decoded twice instead of three times, with the bandwidth and silent-gap scans sharing one decode.
- When ffmpeg is auto-downloaded and no integrity pin is configured, the downloaded SHA-256 is reported instead of the check being skipped silently.

### Fixed
- Sorting a folder-audit column no longer opens the selected file: opening a row requires a double-click that lands on a row, not two quick clicks on a column header.
- Check for Updates reports connection and parse failures honestly instead of silently reading as up to date.
- Healthy files no longer show a spurious "failed to decode" error from a race that killed ffmpeg at a clean end-of-stream.
- Malformed ffprobe output is read as a per-file decode error instead of derailing a whole folder audit.
- The audit cache is rebuilt only on genuine database corruption, not on any transient SQLite error.
- The integrity and loudness passes cancel promptly instead of running on after you close or switch away from a file.
- The log-frequency axis now applies in the compare view (A / B / Diff), matching the single-file view.
- The average-spectrum overlay refreshes on a channel switch or reload instead of lingering from the previous document.
- Saving or copying the spectrogram, and exporting a report, surface write failures in the status bar instead of crashing.
- Settings saving fails soft on an unwritable location instead of taking the app down.
- The CLI rejects malformed options (missing or non-numeric values) with a clear message and exit code 2.
- Closing a comparison tab releases its bitmaps instead of holding them in memory.
- Two-part version numbers display as 1.2.0 rather than 1.2.-1.

## [0.11.0] - 2026-07-10

### Added
- Folder audit grid: drop a folder on the window (or File > Open Folder..., Ctrl+Shift+O, or `spektra <folder>`) to triage a whole library in a live sortable grid with byte-weighted progress and a remaining-time estimate. A tiered severity filter (All files / Suspect + worse / Problems only) hides rows below the chosen bar, double-click opens a row as a normal tab, and Export saves the grid as CSV/JSON.
- Persistent audit cache: results are cached per file (size + modified time) in `%APPDATA%\Spektra\audit-cache.db`, shared by the app and the CLI, so re-scans only analyze new or changed files. F5 rescans from cache, Shift+F5 or `--fresh` re-analyzes, cancelling keeps completed work.
- Integrity lane: silent gaps (cyan, informational) and a truncated file's missing tail (red) are marked on a thin lane along the time axis that zooms and pans with the spectrogram. Opening a Corrupt/Suspect grid row runs the check automatically so the lane is populated on arrival.
- Custom palettes: drop `{ "name", "anchors" }` JSON files in `%APPDATA%\Spektra\palettes` or a `palettes` folder next to the app. Anchors are hex colors spread evenly, or stops pinned to a position (`at`) or an absolute level (`db`); dB-pinned colors stay glued to their level when the display floor changes.
- New built-in palettes: Plasma, Cividis, Turbo, and MonoGreen/MonoAmber phosphor ramps where saturation tracks intensity.
- Tightness: a level-curve slider in Preferences (and `--gamma` on `spektra image`) controls how fast quiet detail brightens; higher keeps the low end darker so peaks read tighter, lower blooms.
- `spektra image` follows the palette and tightness saved in the app settings; `--palette`/`--gamma` override.
- Ctrl+I and Ctrl+L toggle their results once they exist: press again to hide the banner (and lane), again to bring them back without re-analyzing.
- The audit report row gains a `channels` column.

### Changed
- A lossy verdict is a problem only when it should not be there: lossy content in a lossless container, or an mp3/aac far below its bitrate's expected cutoff. An honest MP3 is just an MP3; it stays neutral in the grid and `spektra audit` exits 0.
- Integrity verdicts got harder to fool: mp3 `bits_left` spec-deviation noise no longer counts as decode errors, one or two stray errors mean Suspect ("worth a listen") instead of Corrupt, files whose header duration is only a bitrate estimate are never judged truncated, and interior silent gaps are reported without raising the verdict.
- Turbo is the default palette, and every built-in now opens at true black so zero signal renders as nothing.
- Avalonia updated to 12.1.0.

### Fixed
- A transitive dependency advisory (SQLitePCLRaw e_sqlite3) is resolved by pinning the patched bundle.

## [0.10.0] - 2026-07-10

### Added
- `spektra diff <fileA> <fileB>`: compares two files from the command line the
  way the app's compare view does. It aligns them automatically (or takes a
  pinned `--offset <ms>`), runs a spectral diff and a time-domain null test
  over the overlapping span, and prints a SAME or DIFFERS verdict; the exit
  code (0 same, 1 differs) makes "is this transcode transparent?" scriptable.
  `--threshold-db <N>` tunes how deep the null must be to count as SAME, and
  `--json` / `--csv` emit the numbers as a machine-readable row.
- `spektra image <file>`: renders a file's whole spectrogram to a PNG with no
  window: one pixel per analysis cell, low frequencies at the bottom, the same
  colormaps as the app. Options: `-o`, `--palette`, `--floor`, `--fft`,
  `--channel`, `--columns`. Long files are merged to fit the width budget, so
  any length comes out whole-file.
- `spektra --version` prints the version.

### Changed
- Subtle separator lines set the menu bar and the status bar off from the
  content, and the FFT size dropdown is more compact.

## [0.9.0] - 2026-07-07

### Added
- Upsampling detection: a hi-res file whose real bandwidth stops at a lower
  standard rate's limit (a 96 kHz container holding only 22.05 kHz of content)
  is flagged Upsampled, naming the likely true source rate. Shows a violet
  banner in the app and an `[UPSAMPLE]` tag, counter, and non-zero exit code in
  the CLI.
- Export the bandwidth and integrity audit for the current file or a whole
  folder as CSV or JSON (File menu). The folder export runs in parallel with a
  progress dialog and Cancel.
- A cursor line in the compare view runs through both panes with a time tick on
  the shared ruler, so a feature in A can be matched to the same instant in B.
- More keyboard shortcuts: Ctrl+0 resets the view, Ctrl+1 to Ctrl+9 jump to a
  tab, Ctrl+Up / Ctrl+Down switch channel, Ctrl+D compares, Ctrl+Shift+S
  exports a report, F5 reloads the file, and A auto-aligns in the compare view.
- View, Crosshair (Ctrl+H) toggles the cursor crosshair and its readout in both
  the single and compare views, and the choice is remembered across runs.
- Command-line and desktop guides under `docs/`, linked from the README.

### Changed
- Switching channels (Mix / Ch 1 / Ch 2) is now instant: each channel's
  overview is computed once and cached, and for stereo files the remaining
  channels are precomputed in the background right after load. The integrity
  result now stays with the file across channel switches, and loudness is
  remembered per channel.
- Error and guard messages in the status bar are now shown in red.
- Relicensed under PolyForm Perimeter 1.0.1 (previously PolyForm Strict 1.0.0),
  which permits commercial use, modification, and redistribution except for
  building a competing product.

## [0.8.2] - 2026-07-05

### Added
- The Windows installer can optionally add Spektra to your PATH (a toggle on the
  Custom Setup page), so you can launch it by typing `spektra` in a terminal.
- Releases now publish a `SHA256SUMS.txt` so downloads can be verified.

## [0.8.1] - 2026-07-05

### Changed
- Upgraded the desktop UI framework to Avalonia 12. The drag-drop, clipboard
  image copy, and high-DPI render-scaling code moved to Avalonia 12's new
  DataTransfer API.

## [0.8.0] - 2026-07-05

### Added
- CLI folder operations (`report`, `scan`, `check`, `audit`, `loudness`) now
  analyze files in parallel through a bounded worker pool, using about 80% of
  the CPU cores by default. Cap the worker count with `--jobs N` (or `-j N`).
  Output stays in input order.

### Fixed
- The bottom-left status text no longer leaves a few stale glyph pixels behind
  after a "Check for Updates" run clears it.

## [0.7.2] - 2026-07-04

### Changed
- Check for Updates (Help menu) now shows a popup with the outcome (up to date,
  update available with a link to the release, or a connection error) instead of
  a quiet status-bar line. The green banner is now reserved for the optional
  once-a-day check on startup.
- Documented the keyboard shortcuts: expanded the in-app About dialog reference
  and added a shortcuts table to the README.

## [0.7.1] - 2026-07-04

### Fixed
- Windows installer: setup now shows a wizard (welcome, license, install
  progress, and a finish page with an option to launch Spektra) instead of
  installing silently with no visible feedback, and it installs to the 64-bit
  Program Files folder instead of the x86 one.

## [0.7.0] - 2026-07-04

### Added
- Loudness & dynamics: integrated LUFS, loudness range, and true peak (EBU R128
  via ffmpeg) plus crest factor and a clipping hint. In the app (Analyze,
  Ctrl+L) and the CLI (`spektra loudness <file|folder>`).
- Check for updates (Help menu): compares the installed version against the
  latest GitHub release and links to it when a newer one exists. Optional
  once-a-day check on startup (off by default, toggle in Preferences).
- CLI `audit` command (bandwidth + integrity in one pass) and `--json` / `--csv`
  output for `report`, `scan`, `check`, and `audit`, so results can be saved as
  a report. `check` now also accepts a folder and recurses it.

## [0.6.0] - 2026-07-04

### Added
- Integrity check: detects corrupt frames (via ffmpeg error detection), missing
  data that decodes to interior digital silence (as in a partial download), and
  truncated files. Available in the app (Analyze, Ctrl+I) and the CLI
  (`spektra check <file>`).

## [0.5.0] - 2026-07-03

### Added
- Automated bandwidth verdict: detects a lossy low-pass cutoff and labels a file
  Lossless, Suspicious, Lossy, or band-limited, with a likely codec/bitrate guess.
- Preferences window (Ctrl+E): FFT size, window function
  (Hann/Hamming/Blackman/Blackman-Harris), color palette
  (Magma/Viridis/Inferno/Grayscale), dynamic-range floor, and a linear or
  logarithmic frequency axis. Settings persist between runs.
- Cursor readout showing time, frequency, and dB at the pointer.
- Toggleable average-spectrum overlay (peak-hold + time-average), Ctrl+R.
- Save the spectrogram to PNG (Ctrl+S) or copy it to the clipboard (Ctrl+Shift+C).
- Compare: a numeric diff score, a time-domain null test (A minus B residual),
  and drift detection that warns when a single offset cannot fully align two files.
- Standalone cross-platform command-line tool (`spektra report` / `spektra scan`)
  for headless bandwidth checks and library transcode scans.
- Windows MSI installer and a GitHub Actions release pipeline that builds the
  GUI and CLI on a version tag.

### Changed
- The headless CLI modes moved out of the GUI into a dedicated `Spektra.Cli`
  console app, so output pipes and redirects cleanly on every platform.

## [0.4.0] - 2026-07-03

### Added
- Help menu with an About dialog showing the app version.
- Application logo and window/executable icon.

### Fixed
- Compare panes and the difference view now share one frequency axis, so a given
  frequency sits at the same height in both panes (no vertical jump on A/B flip).

## [0.3.0] - 2026-07-03

### Added
- Compare tab: two files stacked on a shared time axis with synchronized zoom/pan.
- Manual (coarse/fine sliders, typeable offset) and automatic (FFT
  cross-correlation) time alignment, plus an A/B flip.
- Signed A minus B spectral difference view with a diverging colormap.
- Launch-time compare options: `--compare`, `--auto`, `--mode`.
- Per-pane decode error reporting and a processing overlay during align/diff.

## [0.2.0] - 2026-07-03

### Added
- Zoom and pan, with sharp re-rendering of zoomed spans via ffmpeg segment decode.
- Tabs for opening many files at once.
- Per-channel or mixdown analysis for multichannel files.
- User-selectable FFT size.
- Recent files and window placement remembered across runs.

## [0.1.0] - 2026-07-02

### Added
- Initial spectrogram viewer on .NET 10 / Avalonia: drop or open an audio file
  and see its spectrogram with time/frequency rulers and a dB legend.
- Streaming ffmpeg-backed decoder and ffprobe metadata reader.
- Streaming spectrogram engine (Hann window, FFT power spectrum, peak-hold
  aggregation) with a magma colormap.

[Unreleased]: https://github.com/rarepops/Spektra/compare/v0.20.0...HEAD
[0.20.0]: https://github.com/rarepops/Spektra/releases/tag/v0.20.0
[0.19.0]: https://github.com/rarepops/Spektra/releases/tag/v0.19.0
[0.18.1]: https://github.com/rarepops/Spektra/releases/tag/v0.18.1
[0.18.0]: https://github.com/rarepops/Spektra/releases/tag/v0.18.0
[0.17.0]: https://github.com/rarepops/Spektra/releases/tag/v0.17.0
[0.16.0]: https://github.com/rarepops/Spektra/releases/tag/v0.16.0
[0.15.1]: https://github.com/rarepops/Spektra/releases/tag/v0.15.1
[0.15.0]: https://github.com/rarepops/Spektra/releases/tag/v0.15.0
[0.14.0]: https://github.com/rarepops/Spektra/releases/tag/v0.14.0
[0.13.6]: https://github.com/rarepops/Spektra/releases/tag/v0.13.6
[0.13.5]: https://github.com/rarepops/Spektra/releases/tag/v0.13.5
[0.13.4]: https://github.com/rarepops/Spektra/releases/tag/v0.13.4
[0.13.3]: https://github.com/rarepops/Spektra/releases/tag/v0.13.3
[0.13.2]: https://github.com/rarepops/Spektra/releases/tag/v0.13.2
[0.13.1]: https://github.com/rarepops/Spektra/releases/tag/v0.13.1
[0.13.0]: https://github.com/rarepops/Spektra/releases/tag/v0.13.0
[0.12.0]: https://github.com/rarepops/Spektra/releases/tag/v0.12.0
[0.11.3]: https://github.com/rarepops/Spektra/releases/tag/v0.11.3
[0.11.2]: https://github.com/rarepops/Spektra/releases/tag/v0.11.2
[0.11.1]: https://github.com/rarepops/Spektra/releases/tag/v0.11.1
[0.8.2]: https://github.com/rarepops/Spektra/releases/tag/v0.8.2
[0.8.1]: https://github.com/rarepops/Spektra/releases/tag/v0.8.1
[0.8.0]: https://github.com/rarepops/Spektra/releases/tag/v0.8.0
[0.7.2]: https://github.com/rarepops/Spektra/releases/tag/v0.7.2
[0.7.1]: https://github.com/rarepops/Spektra/releases/tag/v0.7.1
[0.7.0]: https://github.com/rarepops/Spektra/releases/tag/v0.7.0
[0.6.0]: https://github.com/rarepops/Spektra/releases/tag/v0.6.0
[0.5.0]: https://github.com/rarepops/Spektra/releases/tag/v0.5.0
