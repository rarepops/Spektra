# Changelog

All notable changes to Spektra are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
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

[Unreleased]: https://github.com/rarepops/Spektra/compare/v0.6.0...HEAD
[0.6.0]: https://github.com/rarepops/Spektra/releases/tag/v0.6.0
[0.5.0]: https://github.com/rarepops/Spektra/releases/tag/v0.5.0
