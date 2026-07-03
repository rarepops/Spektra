<p align="center">
  <img src="assets/spektra-logo.png" alt="Spektra" width="180" />
</p>

# Spektra

A desktop audio spectrum analyzer: drop in an audio file, see its spectrogram,
compare encodes side by side, and get an automated "is this really lossless?"
verdict.

## Features

- Progressive spectrogram with time/frequency rulers and dB legend
- Automated bandwidth verdict: detects a lossy low-pass cutoff and reports
  Lossless / Suspicious / Lossy with a likely codec/bitrate guess
- Zoom & pan: wheel = time zoom, Shift+wheel = frequency zoom, drag = pan,
  double-click = reset (zoomed spans re-render sharply via ffmpeg segment decode)
- Cursor readout (time, frequency, dB) and a toggleable average-spectrum overlay
  (peak-hold + time-average)
- Preferences: FFT size, window function (Hann/Hamming/Blackman/Blackman-Harris),
  color palette (Magma/Viridis/Inferno/Grayscale), dynamic-range floor, and a
  linear or logarithmic frequency axis
- Save the spectrogram to PNG (Ctrl+S) or copy it to the clipboard (Ctrl+Shift+C)
- Tabs: open many files at once (dialog, drag-drop, or CLI args)
- Per-channel or mixdown analysis for multichannel files
- Recent files + window placement remembered across runs
- Compare two files: stacked spectrograms on a shared time axis, synced zoom/pan,
  manual + automatic (cross-correlation) time alignment, A/B flip, and a signed
  A−B difference view (diverging colormap) with a numeric diff score
- Null test (time-domain A−B residual) and drift detection for misaligned encodes

## Requirements

- Windows (primary target; Avalonia keeps Linux/macOS possible)
- [ffmpeg + ffprobe](https://ffmpeg.org/), found via the app folder,
  `%LOCALAPPDATA%\Spektra\ffmpeg`, or `PATH`. If missing, Spektra offers a
  one-click download. ffmpeg is invoked as a separate process and is not
  linked or bundled.

## Build & run

    dotnet run --project src/Spektra.App -- <optional-audio-file>

Compare two files directly (also available in-app via File → Compare…):

    dotnet run --project src/Spektra.App -- --compare <fileA> <fileB> [--auto] [--mode diff]

## Command line

Spektra ships a small cross-platform companion CLI (`spektra`) that reuses the
analysis engine. It writes to stdout and exits 1 when anything looks lossy:

    spektra report <file> [<file> ...]   Print each file's bandwidth verdict.
    spektra scan <folder>                Scan a library and flag suspected transcodes.

(`--report` / `--scan` are accepted too.) Build it with
`dotnet publish src/Spektra.Cli -c Release`.

## Test

    dotnet test

## License

[PolyForm Strict 1.0.0](LICENSE.md). © Rares (rarepops).
