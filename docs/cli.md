# Spektra CLI guide

The `spektra` command-line tool reuses the desktop app's analysis engine. It
writes plain text to stdout (pipe/redirect friendly) and uses the exit code to
signal problems, so it drops straight into scripts and CI. `spektra --version`
prints the version.

    spektra <command> <file|folder> ... [--json|--csv] [--jobs N]

- A single **folder** argument recurses into every audio file beneath it
  (flac/mp3/wav/ogg/opus/m4a/aac/wma/ape/wv/aiff/alac); otherwise the arguments
  are taken as individual files.
- Folders are analyzed in parallel using about 80% of the CPU cores; cap the
  workers with `--jobs N` (or `-j N`). Output order always matches input order.
- **Exit codes:** `0` clean, `1` anything likely lossy, upsampled, or corrupt
  (for `diff`: the files differ), `2` setup errors (e.g. ffmpeg missing).
  Requires ffmpeg + ffprobe on `PATH`.

## report: bandwidth verdict per file

    $ spektra report rip.flac
    rip.flac
      rip.flac — FLAC · 44.1 kHz · 16-bit · 2 ch · 3:41 · 1040 kbps
      Sharp cutoff at 16.0 kHz. Consistent with lossy encoding (MP3 128 / AAC ~128).

A hi-res file whose content stops at a lower standard rate is called out as
upsampled instead of lossy:

    $ spektra report "vinyl-96k.flac"
    vinyl-96k.flac
      vinyl-96k.flac — FLAC · 96 kHz · 24-bit · 2 ch · 4:02 · 2116 kbps
      Bandwidth ends near 22.0 kHz; matches a 44.1 kHz source upsampled to 96 kHz.

Possible verdicts: **Lossless** (full-band), **Suspicious** (rolloff that could
be natural), **Lossy** (sharp codec cutoff, with a codec/bitrate guess),
**Upsampled** (bandwidth matches a lower standard rate's Nyquist), **Unknown**
(too quiet / too band-limited to judge).

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

## audit: bandwidth + integrity together

    $ spektra audit Music
      bandwidth=Lossless           integrity=Ok       01 Intro.flac
      bandwidth=Lossy 16.0k        integrity=Ok       02 Transcode.flac
      bandwidth=Upsampled 22.0k    integrity=Ok       03 Fake96k.flac

    3 files, 2 with problems.

Audit results are cached per file in `%APPDATA%\Spektra\audit-cache.db`
(keyed by size and modified time), so repeat runs of the same library only
analyze new or changed files. Pass `--fresh` to ignore the cache and
re-analyze everything; results are written back either way. The cache is
disposable: deleting the file just means the next audit starts cold.

## loudness: LUFS, true peak, dynamics

    $ spektra loudness master.flac
    master.flac
      -9.8 LUFS integrated, LRA 4.2 LU, true peak -0.1 dBTP, crest 9.6 dB.

## diff: are two files the same recording?

Aligns the files automatically (cross-correlation, like the desktop app's Auto
button), runs a spectral diff and a time-domain null test over their
overlapping span, and turns the result into a SAME / DIFFERS verdict:

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

- **SAME** means the null depth (how far the A minus B residual sits below the
  signal) reaches the threshold, 40 dB by default; tune it with
  `--threshold-db <N>`. Exit code `0` for SAME, `1` for DIFFERS.
- Alignment is automatic; pin an exact offset instead with `--offset <ms>`
  (positive = B's content is later than A's, the same convention as the
  desktop app's Align box). A warning is printed when the alignment
  confidence is low, and a note when the files drift apart over time.
- `--json` / `--csv` emit one row: `fileA, fileB, offsetMs, alignConfidence,
  driftMs, overlapSeconds, meanAbsDb, rmsDb, maxAbsDb, withinTolerancePct,
  residualRmsDb, residualPeakDb, nullDepthDb, thresholdDb, same`.

```
# gate a release: the remaster must be transparent against the approved master
spektra diff approved.flac remaster.flac --threshold-db 60 || exit 1
```

## image: spectrogram PNG without a window

    $ spektra image rip.flac
    Wrote rip.png (2048x1025)

Renders the whole file's spectrogram headlessly: one pixel per analysis cell,
low frequencies at the bottom, colormapped exactly like the desktop app. The
image is the raw spectrogram (no axes or labels), sized width x (fft/2 + 1).

- `-o <out.png>` sets the output (default: the input name with `.png`).
- `--palette magma|viridis|inferno|grayscale`, `--floor <dB>` (default -120),
  `--fft <size>` (default 2048), `--channel <n>` (1-based; default mixdown),
  `--columns <n>` (width budget, default 2048; long files are peak-hold
  merged to fit, so any length stays whole-file).

## Machine-readable reports

Add `--json` or `--csv` to any command:

    spektra scan Music --csv > library.csv
    spektra audit Music --json | jq '.[] | select(.bandwidth != "Lossless")'

The columns are stable, ordered rows (`file, codec, sampleRateHz, …`); the same
schema the desktop app writes from **File → Export Report…**.

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
