# Changelog

All notable changes to Spektra are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[Unreleased]: https://github.com/rarepops/Spektra/compare/v0.12.0...HEAD
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
