# spektra-cli

Find out what is actually in your music library: files sold as lossless that
were made from an MP3, CDs upsampled to hi-res, corrupt or truncated tracks,
and the same song sitting in four folders in three formats.

`spektra-cli` is the command-line half of [Spektra](https://github.com/rarepops/Spektra),
a Windows desktop spectrum analyzer. Same analysis engine, no window, machine
readable output. It is a self-contained native binary, so this package needs
no .NET runtime.

## Install

    npm install -g spektra-cli

Or run it without installing:

    npx spektra-cli audit ~/Music

## ffmpeg is required

Decoding is done by [ffmpeg](https://ffmpeg.org/), which this package does not
bundle. Install it separately and make sure `ffmpeg` and `ffprobe` are on your
`PATH`:

    winget install ffmpeg          # Windows
    brew install ffmpeg            # macOS
    sudo apt install ffmpeg        # Debian, Ubuntu

ffmpeg is run as a separate process, never linked or bundled. The one command
that works without it is `manifest`, which only lists files.

## Commands

    spektra-cli report <file|folder> ...   Bandwidth verdict per file.
    spektra-cli scan <folder>              Compact bandwidth scan of a library.
    spektra-cli check <file|folder> ...    Integrity check (corruption, missing data).
    spektra-cli audit <file|folder> ...    Bandwidth and integrity together (cached).
    spektra-cli dupes <folder> ...         Find duplicate songs across folders and formats.
    spektra-cli manifest <folder>          List a folder with type chips (no decoding).
    spektra-cli inventory <folder>         Tags and embedded cover art per file.
    spektra-cli loudness <file|folder> ... Loudness (LUFS), true peak, and dynamics.
    spektra-cli diff <fileA> <fileB>       Compare two files: align, spectral diff, null test.
    spektra-cli image <file>               Render the spectrogram to a PNG.

Add `--json` or `--csv` to any command for machine-readable output:

    spektra-cli scan ~/Music --csv > library.csv

Exit code 1 means findings (a transcode, an upsample, or corruption; an honest
lossy file is fine), 2 a setup error, 0 a clean run, so a check fits straight
into a script or a continuous integration job:

    spektra-cli audit ~/Music || echo "something in there is not what it claims"

Full reference with sample output: [docs/cli.md](https://github.com/rarepops/Spektra/blob/main/docs/cli.md).

## Platforms

Prebuilt binaries cover Windows x64, Linux x64 (glibc), and macOS on both
Intel and Apple silicon. npm installs only the one your machine can run.

Windows on ARM gets the x64 build and runs it under emulation. Alpine and
other musl systems are not covered: the Linux build needs glibc, and
`spektra-cli` says so rather than failing at the loader. No Linux ARM build
yet, so a Raspberry Pi or an ARM server needs a build from source.

## License

[PolyForm Perimeter 1.0.1](https://github.com/rarepops/Spektra/blob/main/LICENSE.md).
Free to use, including at work; you may not use it to build a competing
product. Not an Open Source Initiative approved license, so a dependency
scanner set to allow only approved licenses will flag it.
