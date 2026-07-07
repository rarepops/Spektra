# Spektra CLI guide

The `spektra` command-line tool reuses the desktop app's analysis engine. It
writes plain text to stdout (pipe/redirect friendly) and uses the exit code to
signal problems, so it drops straight into scripts and CI.

    spektra <command> <file|folder> ... [--json|--csv] [--jobs N]

- A single **folder** argument recurses into every audio file beneath it
  (flac/mp3/wav/ogg/opus/m4a/aac/wma/ape/wv/aiff/alac); otherwise the arguments
  are taken as individual files.
- Folders are analyzed in parallel using about 80% of the CPU cores; cap the
  workers with `--jobs N` (or `-j N`). Output order always matches input order.
- **Exit codes:** `0` clean, `1` anything likely lossy, upsampled, or corrupt,
  `2` setup errors (e.g. ffmpeg missing). Requires ffmpeg + ffprobe on `PATH`.

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

## loudness: LUFS, true peak, dynamics

    $ spektra loudness master.flac
    master.flac
      -9.8 LUFS integrated, LRA 4.2 LU, true peak -0.1 dBTP, crest 9.6 dB.

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
