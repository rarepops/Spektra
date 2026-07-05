# Changelog

All notable changes to Spektra are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[Unreleased]: https://github.com/rarepops/Spektra/compare/v0.8.2...HEAD
[0.8.2]: https://github.com/rarepops/Spektra/releases/tag/v0.8.2
[0.8.1]: https://github.com/rarepops/Spektra/releases/tag/v0.8.1
[0.8.0]: https://github.com/rarepops/Spektra/releases/tag/v0.8.0
[0.7.2]: https://github.com/rarepops/Spektra/releases/tag/v0.7.2
[0.7.1]: https://github.com/rarepops/Spektra/releases/tag/v0.7.1
[0.7.0]: https://github.com/rarepops/Spektra/releases/tag/v0.7.0
[0.6.0]: https://github.com/rarepops/Spektra/releases/tag/v0.6.0
[0.5.0]: https://github.com/rarepops/Spektra/releases/tag/v0.5.0
