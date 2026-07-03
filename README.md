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

## Requirements

- Windows (primary target; Avalonia keeps Linux/macOS possible)
- [ffmpeg + ffprobe](https://ffmpeg.org/) — found via the app folder,
  `%LOCALAPPDATA%\Spektra\ffmpeg`, or `PATH`. If missing, Spektra offers a
  one-click download. ffmpeg is invoked as a separate process and is not
  linked or bundled.

## Build & run

    dotnet run --project src/Spektra.App -- <optional-audio-file>

## Test

    dotnet test

## License

[PolyForm Strict 1.0.0](LICENSE.md). © Rares (rarepops).
