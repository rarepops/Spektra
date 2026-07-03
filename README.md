# Spektra

A desktop audio spectrum analyzer: drop in an audio file, see its spectrogram,
and (in upcoming versions) compare encodes side-by-side and get an automated
"is this really lossless?" verdict.





## Features

- Progressive spectrogram with time/frequency rulers and dB legend
- Zoom & pan: wheel = time zoom, Shift+wheel = frequency zoom, drag = pan,
  double-click = reset — zoomed spans re-render sharply via ffmpeg segment decode
- Tabs: open many files at once (dialog, drag-drop, or CLI args)
- Per-channel or mixdown analysis for multichannel files
- Recent files + window placement remembered across runs
- Compare two files: stacked spectrograms on a shared time axis, synced zoom/pan,
  manual + automatic (cross-correlation) time alignment, A/B flip, and a signed
  A−B difference view (diverging colormap)

## Requirements

- Windows (primary target; Avalonia keeps Linux/macOS possible)
- [ffmpeg + ffprobe](https://ffmpeg.org/) — found via the app folder,
  `%LOCALAPPDATA%\Spektra\ffmpeg`, or `PATH`. If missing, Spektra offers a
  one-click download. ffmpeg is invoked as a separate process and is not
  linked or bundled.

## Build & run

    dotnet run --project src/Spektra.App -- <optional-audio-file>

Compare two files directly (also available in-app via File → Compare…):

    dotnet run --project src/Spektra.App -- --compare <fileA> <fileB> [--auto] [--mode diff]

## Test

    dotnet test

## License

[PolyForm Strict 1.0.0](LICENSE.md). © Rares (rarepops).
