# Spektra CLI guide

The `spektra` command-line tool reuses the desktop app's analysis engine. It writes plain text to stdout (pipe/redirect friendly) and uses the exit code to signal problems, so it drops straight into scripts and CI. `spektra --version` prints the version.

    spektra <command> <file|folder> ... [--json|--csv] [--jobs N]

- A single **folder** argument recurses into every audio file beneath it (flac/mp3/wav/ogg/opus/m4a/aac/wma/ape/wv/aiff/alac); otherwise the arguments are taken as individual files.
- Folders are analyzed in parallel using about 80% of the CPU cores; cap the workers with `--jobs N` (or `-j N`). Output order always matches input order.
- **Exit codes:** `0` clean, `1` findings, `2` setup errors (e.g. ffmpeg missing). Findings per command: `report`/`scan` anything lossy or upsampled, `check` corruption, `audit` real problems only (a transcode, an upsample, or corruption; an honest lossy file is not a problem), `diff` the files differ. Requires ffmpeg + ffprobe on `PATH`.

## report: bandwidth verdict per file

    $ spektra report rip.flac
    rip.flac
      rip.flac — FLAC · 44.1 kHz · 16-bit · 2 ch · 3:41 · 1040 kbps
      Sharp cutoff at 16.0 kHz. Consistent with lossy encoding (MP3 128 / AAC ~128).

A hi-res file whose content stops at a lower standard rate is called out as upsampled instead of lossy:

    $ spektra report "vinyl-96k.flac"
    vinyl-96k.flac
      vinyl-96k.flac — FLAC · 96 kHz · 24-bit · 2 ch · 4:02 · 2116 kbps
      Bandwidth ends near 22.0 kHz; matches a 44.1 kHz source upsampled to 96 kHz.

Possible verdicts: **Lossless** (full-band), **Suspicious** (rolloff that could be natural), **Lossy** (sharp codec cutoff, with a codec/bitrate guess), **Upsampled** (bandwidth matches a lower standard rate's Nyquist), **Unknown** (too quiet / too band-limited to judge).

## scan: compact library sweep

    $ spektra scan Music
    Scanning 1247 audio file(s) under Music ...
      [LOSSLESS]        Album/01 Intro.flac
      [LOSSY   ] 16.0k  Album/02 Transcode.flac
      [UPSAMPLE] 22.0k  HiRes/03 Fake96k.flac
      [SUSPECT ] 19.2k  Album/04 OldMaster.flac
    ...
    1247 files: 1180 lossless, 12 suspect, 31 likely lossy, 4 upsampled, 18 unknown, 2 errors.

## check: integrity (corruption / missing data)

    $ spektra check download.flac
      [CORRUPT] download.flac - Likely corrupt or incomplete: 3 decode error(s), truncated (1:02 of 3:45).

    1 files: 0 ok, 0 suspect, 1 corrupt.

Suspect means worth a listen: one or two stray decode errors (an isolated damaged frame, or junk bytes appended by a bad tagger). Corrupt means provable damage: decode failure, three or more errors, or a file shorter than the length its own header declares. Interior silent gaps are reported in the summary but never raise the verdict (silence is legal audio). Files whose header duration is only a bitrate estimate (an mp3 without a Xing header) are never judged truncated, since the estimate proves nothing.

## audit: bandwidth + integrity together

    $ spektra audit Music
      bandwidth=Lossless           integrity=Ok       01 Intro.flac
      bandwidth=Lossy 16.0k        integrity=Ok       02 Transcode.flac
      bandwidth=Upsampled 22.0k    integrity=Ok       03 Fake96k.flac

    3 files, 2 with problems.

A `Lossy` verdict counts as a problem only when it should not be there: lossy content inside a lossless format (`Transcode.flac` above), or an mp3/aac whose cutoff sits far below what its bitrate should deliver (a 320 kbps MP3 walling at 16 kHz was re-encoded from a ~128 kbps source). An MP3 with the cutoff its bitrate predicts is just an MP3 and exits `0`.

When the argument is a folder, the `File` column holds each file's path relative to it (nested rows read `Album/CD2/03.flac`), in text, CSV, and JSON output alike; explicit file arguments keep the bare name.

Audit results are cached per file in `%APPDATA%\Spektra\audit-cache.db` (keyed by size and modified time), so repeat runs of the same library only analyze new or changed files. Pass `--fresh` to ignore the cache and re-analyze everything; results are written back either way. The cache is disposable: deleting the file just means the next audit starts cold. The `audit` command analyzes immediately and in full, while the desktop app's folder tab is the fine-grained counterpart (browse, pick which files to check, then analyze); both share this same on-disk cache, so a file analyzed in one is already cached for the other.

## loudness: LUFS, true peak, dynamics

    $ spektra loudness master.flac
    master.flac
      -9.8 LUFS integrated, LRA 4.2 LU, true peak -0.1 dBTP, crest 9.6 dB.

## diff: are two files the same recording?

Aligns the files automatically (cross-correlation, like the desktop app's Auto button), runs a spectral diff and a time-domain null test over their overlapping span, and turns the result into a SAME / DIFFERS verdict:

    $ spektra diff track.wav track-copy.wav
    A: track.wav — PCM_S16LE · 44.1 kHz · 16-bit · 2 ch · 0:03 · 1411 kbps
    B: track-copy.wav — PCM_S16LE · 44.1 kHz · 16-bit · 2 ch · 0:03 · 1411 kbps
    Aligned +0 ms (confidence 1.00) · overlap 0:03
    Spectral: mean |Δ| 0.0 dB · RMS 0.0 dB · max 0.0 dB · 100% within tolerance
    Null:     Perfect null: identical samples over this span.
    SAME      perfect null (identical samples)

    $ spektra diff track.wav track-128k.mp3
    A: track.wav — PCM_S16LE · 44.1 kHz · 16-bit · 2 ch · 0:03 · 1411 kbps
    B: track-128k.mp3 — MP3 · 44.1 kHz · 2 ch · 0:03 · 128 kbps
    Aligned +0 ms (confidence 1.00) · overlap 0:03
    Spectral: mean |Δ| 1.6 dB · RMS 8.3 dB · max 113.6 dB · 91% within tolerance
    Null:     Residual -9.4 dB RMS (6.0 dB below signal), peak 0.0 dB.
    DIFFERS   null depth 6.0 dB < threshold 40.0 dB

- **SAME** means the null depth (how far the A minus B residual sits below the signal) reaches the threshold, 40 dB by default; tune it with `--threshold-db <N>`. Exit code `0` for SAME, `1` for DIFFERS.
- Alignment is automatic; pin an exact offset instead with `--offset <ms>` (positive = B's content is later than A's, the same convention as the desktop app's Align box). A warning is printed when the alignment confidence is low, and a note when the files drift apart over time.
- `--json` / `--csv` emit one row: `fileA, fileB, offsetMs, alignConfidence, driftMs, overlapSeconds, meanAbsDb, rmsDb, maxAbsDb, withinTolerancePct, residualRmsDb, residualPeakDb, nullDepthDb, thresholdDb, same`.

```
# gate a release: the remaster must be transparent against the approved master
spektra diff approved.flac remaster.flac --threshold-db 60 || exit 1
```

## image: spectrogram PNG without a window

    $ spektra image rip.flac
    Wrote rip.png (2048x1025)

Renders the whole file's spectrogram headlessly: one pixel per analysis cell, low frequencies at the bottom, colormapped exactly like the desktop app. The image is the raw spectrogram (no axes or labels), sized width x (fft/2 + 1).

- `-o <out.png>` sets the output (default: the input name with `.png`).
- `--palette magma|viridis|inferno|grayscale|plasma|cividis|turbo|monogreen|monoamber` or the name of a custom palette (below); defaults to the palette saved in the app's settings (Turbo out of the box).
- `--gamma <g>` - the level curve the app calls Tightness (default: your app setting): above 1 keeps quiet detail darker so peaks read tighter, below 1 blooms.
- `--floor <dB>` (default -120), `--fft <size>` (default 2048), `--channel <n>` (1-based; default mixdown), `--columns <n>` (width budget, default 2048; long files are peak-hold merged to fit, so any length stays whole-file).

### Custom palettes

Drop JSON files in `%APPDATA%\Spektra\palettes` (per-user, always writable) or in a `palettes` folder next to the executable (portable installs, shipped presets) and both the desktop app (Preferences) and `--palette <name>` pick them up; on a name collision the user folder wins. Anchors are either hex colors spread evenly across the display range:

    { "name": "Nightfall", "anchors": ["#000000", "#3D2E6B", "#F5B7A5"] }

or stops that pin a color to a position (`at`, 0..1 of the display range) or to an absolute level (`db`), one form per file:

    { "name": "Pinned", "anchors": [
        { "color": "#000000", "db": -120 },
        { "color": "#00AAFF", "db": -40 },
        { "color": "#FFFFFF", "db": 0 } ] }

`db` stops stay glued to their level when the display floor changes. Invalid files are skipped (the app lists them in the status bar when Preferences opens); a name that collides with a built-in is ignored.

## Machine-readable reports

Add `--json` or `--csv` to any command:

    spektra scan Music --csv > library.csv
    spektra audit Music --json | jq '.[] | select(.bandwidth != "Lossless")'

The columns are stable, ordered rows (`file, codec, sampleRateHz, …`); the same schema the desktop app writes from **File → Export Report…**.

## Scripting examples

    # fail a CI step if a release folder contains transcodes or upsamples
    spektra scan release-audio || exit 1

    # find every problem file in a library, 4 workers
    spektra audit Music -j 4 --csv > audit.csv

    # verify a purchase really is lossless before archiving it
    spektra report "purchase.flac" && cp "purchase.flac" archive/

    # is the "remaster" the same recording as my old rip? (exit 0 = effectively identical)
    spektra diff my-rip.flac remaster.flac

    # spectrogram PNG next to every file in an album folder
    for f in Album/*.flac; do spektra image "$f"; done
