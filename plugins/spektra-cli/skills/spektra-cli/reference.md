# spektra-cli command reference

Per-command detail for the `spektra-cli` skill, describing spektra-cli releases after 0.25.1 (SKILL.md says what an older one gets wrong). `spektra-cli --help` prints the tool's own summary. Placeholders: `<folder>`, `<file>`, `<file-a>`, `<file-b>`, `<folder-a>`, `<folder-b>`, and `<scratch>` (a folder outside the library). Quote paths that contain spaces.

Contents: common options and exit codes · audit · report and scan · check · dupes (and what folder A has that folder B lacks) · diff · loudness · manifest · inventory · image · output formats.

## Common options and exit codes

- `--json` or `--csv` on every command except `image`.
- `-j N` or `--jobs N` caps the parallel workers (a whole number of 1 or more; default about 80% of the CPU cores). It matters for `audit`, `report`, `scan`, `check`, `loudness`, `dupes` and `inventory`.
- `spektra-cli --help` prints usage (exit 0); `spektra-cli --version` (or `-v`) prints `spektra-cli <version>`.
- A mistyped option is an error, not ignored: `spektra-cli audit <folder> --jso` exits 2 with `unknown option '--jso'`.

| Command | Exit 1 means |
|---|---|
| `audit` | at least one problem file (unreadable, upsampled, mixed, corrupt, lossy where it does not belong, or a lossy codec under a lossless file name) |
| `report`, `scan` | at least one `Lossy`, `Upsampled` or `Mixed` verdict, an honest MP3 included, or a file that could not be read |
| `check` | at least one `Corrupt` or unreadable file (`Suspect` alone exits 0) |
| `dupes` | at least one duplicate group |
| `diff` | the files differ |
| `loudness` | a file could not be measured |
| `manifest`, `inventory`, `image` | never: they exit 0, or 2 on an error |

Exit 2 always means the run itself failed: a bad option, a path that does not exist, a folder given together with other paths (for `audit`, `report`, `check` and `loudness`; both are caught before anything is analyzed), a missing folder for `scan`, `dupes`, `manifest` or `inventory`, the wrong number of arguments, ffmpeg not found, or (for `image`, `diff` and `--html`) a file that could not be read or written. `image` also exits 2 for an output it will not write (see image). A file that exists but cannot be read is a finding, not exit 2: `audit`, `report` and `loudness` return its row with `error` set and exit 1, and `check` gives it a `Corrupt` or `Error` status and exits 1.

## audit

    spektra-cli audit <folder> --json > <scratch>/audit.json
    spektra-cli audit <file-a> <file-b> --json
    spektra-cli audit <folder> --html <scratch>/audit.html

Options: `--json`, `--csv`, `--html <file>`, `--fresh`, `-j N`. One folder is searched recursively for flac, mp3, wav, ogg, opus, m4a, aac, wma, ape, wv, aiff, aif and alac files; several arguments are taken as files, and a path that does not exist, or a folder among other paths, exits 2 before anything is analyzed. Results are cached per file, keyed by size and modified time.

JSON fields: `file`, `codec`, `sampleRateHz`, `channels`, `bitrateBps`, `durationSeconds`, `bandwidth`, `cutoffHz`, `integrity`, `decodeErrors`, `dropouts`, `truncated`, `error`.

- `file` is relative to the folder with `/` separators (`Album/CD2/03.flac`), or the bare name for explicit files.
- `codec` is the real codec as ffprobe reports it (`flac`, `mp3`, `aac`, `pcm_s16le`), whatever the extension says.
- `cutoffHz` is where the content stops; `null` for full-band content.

Sample row (a FLAC made from an MP3):

```json
{
  "file": "transcode.flac",
  "codec": "flac",
  "sampleRateHz": 44100,
  "channels": 1,
  "bitrateBps": 522130,
  "durationSeconds": 3,
  "bandwidth": "Lossy",
  "cutoffHz": 16817.431640625,
  "integrity": "Ok",
  "decodeErrors": 0,
  "dropouts": 0,
  "truncated": false,
  "error": null
}
```

The text form prints one line per file (`bandwidth=Lossy 16.8k        integrity=Ok       transcode.flac`) and ends with `21 files, 2 with problems.` A lossy file under a lossless name ends its line with `· holds <codec>` (`renamed.flac · holds mp3`).

The rows carry no "problem" flag. What `audit` counts, for the exit code and the `with problems` total: an unreadable file, an upsample, a `Mixed` verdict, corrupt integrity, a `Lossy` verdict where it does not belong (inside a lossless codec, or an MP3 or AAC whose cutoff sits far below what its bitrate should deliver, judged by a bitrate table), and a lossy `codec` under a lossless file name, whatever the bandwidth reads. The lossless-only names are `.flac`, `.wav`, `.aiff`, `.aif`, `.ape`, `.wv` and `.alac`; `.m4a`, `.ogg` and `.wma` hold either kind, so they claim nothing. To get Spektra's own per-file call, run `spektra-cli manifest <folder> --json > <scratch>/manifest.json` on the same folder after the audit: its `severity` comes from the audit cache and reads `Problem` (what `audit` counts, a renamed lossy file included), `Suspect` (suspect integrity, or a `Suspicious` verdict that is not just a high-bitrate lossy file's own wall at 20 kHz or above) or `Clean`. Its paths are absolute, not folder-relative.

## report and scan

    spektra-cli report <file> --json
    spektra-cli scan <folder> --json > <scratch>/scan.json

The bandwidth half of `audit`, without the cache. `report` takes files or one folder; `scan` takes exactly one folder. Options: `--json`, `--csv`, `-j N`.

JSON fields: `file` (bare file name, even inside subfolders), `codec`, `sampleRateHz`, `bitrateBps`, `durationSeconds`, `verdict`, `cutoffHz`, `codecGuess`, `error`. `codecGuess` names the likely source encoder for a lossy wall (`MP3 ~160-192`), otherwise `null`.

Text: `report` prints a format line and a sentence such as `Sharp cutoff at 16.8 kHz. Consistent with lossy encoding (MP3 ~160-192).`; `scan` prints `[LOSSY   ] 16.8k  transcode.flac` per file and a count line. An unreadable file gets `"verdict": "Error"` (`scan` prints `[ERROR   ]`) and makes the run exit 1.

## check

    spektra-cli check <folder> --json > <scratch>/check.json

Integrity only, no cache. Files or one folder. Options: `--json`, `--csv`, `-j N`.

JSON fields: `file` (bare name), `status` (`Ok`, `Suspect`, `Corrupt`, `Error`), `decodeErrors`, `dropouts` (interior silent gaps: reported, never raising the status), `decodedSeconds`, `expectedSeconds`, `truncated`, `detail` (the sentence, such as `Likely corrupt or incomplete · 3 decode errors, truncated (0:01 of 0:03).`), `error`.

An MP3 without a Xing header only has an estimated length, so it is never judged truncated.

## dupes

    spektra-cli dupes <folder> --json > <scratch>/dupes.json
    spektra-cli dupes <folder-a> <folder-b> --json > <scratch>/pairs.json
    spektra-cli dupes <folder> > <scratch>/dupes.txt
    spektra-cli dupes <folder> --html <scratch>/dupes.html

Finds the same recording by audio fingerprint across every folder given, whatever the names, tags or formats. Shares the audit cache. Options: `--json`, `--csv`, `--html <file>`, `--fresh`, `-j N`. Every folder must exist (exit 2 otherwise).

JSON: one row per group member, the winner first. Fields: `group`, `label`, `samenessTier`, `file`, `codec`, `cutoffKhz`, `bandwidth`, `integrity`, `sizeBytes`, `sameness`, `foundByAudio`, `winner`, `qualityConfidence`, `qualityReason`.

- `file` is an absolute path. `cutoffKhz` is in kHz here, unlike `cutoffHz` elsewhere.
- `samenessTier` is `High` (every member matched strongly across its full length) or `Medium` (a weaker match: check before trusting it). `sameness` is the member's score from 0 to 1.
- `winner` marks the copy worth keeping (a tie marks more than one). The ranking: full-band lossless, then lossless with a `Suspicious` wall at 20 kHz or above, then honest lossy by cutoff, then transcodes and upsamples; corrupt copies drop to the bottom.
- `qualityConfidence` is `High`, `Medium` or `Low`, explained in `qualityReason` (`winner is full-band lossless (flac), runner-up is mp3 cut at 16.5 kHz`). `Low` means the ranking itself is shaky, so look at those groups first; at any confidence, removing a copy still needs the user's explicit go-ahead.
- `foundByAudio` is true when the file's name shares no word with the group's `label` (a renamed copy).
- Files that were not analyzed (shorter than 20 s, or unreadable) are not rows. JSON and CSV count them in one line on stderr instead: `spektra-cli dupes: 2 file(s) were not analyzed and could not be matched (shorter than 20 s, or unreadable); run without --json or --csv to list them.` The line is on stderr only: stdout and the exit code carry no sign of it, so an empty `[]` with that line on stderr does not prove there are no duplicates.

The text form adds what JSON and CSV leave out: `! not analyzed: <path> (shorter than 20 s)` lines (an unreadable file gets its reason in the brackets instead), and a closing line such as `1 group(s) · 2 files · reclaimable 626.2 KB · 5 scanned`. "Reclaimable" is what deleting every non-winner would free: a statistic for the user, not a plan for you.

Limits by design: copies must be within 15 seconds of each other's length; files under 20 seconds are never compared; the match must cover most of the shorter file, so a shared intro or sample does not pair; a remix, live take or edit is a different recording; two differently mastered releases may not pair.

### What does folder A have that folder B lacks?

`dupes` prints matches, not differences, so derive the difference:

1. `spektra-cli dupes <folder-a> <folder-b> --json > <scratch>/pairs.json`
2. `spektra-cli inventory <folder-a> --json > <scratch>/a.json` and `spektra-cli inventory <folder-b> --json > <scratch>/b.json` for both full file lists (fast: nothing is decoded).
3. A track is in both folders when it sits in a group that also has a member under B. To match the two outputs, make each `dupes` path relative to A's absolute path and turn `\` into `/` (compare case-insensitively on Windows). Sanity check: every A member of a group must then match a row in a.json; if none do, the conversion is wrong.
4. Sort A's audio rows (`isAudio` true) that sit in no such group: `error` set means "unreadable, not compared"; `durationSeconds` under 20 means "too short to compare"; the rest are candidates for "B lacks this". A file in a `Medium` group with a B member is "possibly in B, worth a check".
5. Before calling a candidate missing, look for it by name in b.json (the `title` tag, or the file name): a match there usually means B holds another master or encode that the audio did not pair, so report it as "probably in B under another version", not as missing.

Swap A and B for the other direction. A user with the Spektra desktop app can see the same comparison as a view with `spektra --diff "<folder-a>" "<folder-b>"` (Duplicate Detective with Only differences ticked), which also matches tracks by name where the audio alone did not.

## diff

    spektra-cli diff <file-a> <file-b> --json
    spektra-cli diff <file-a> <file-b> --threshold-db 60
    spektra-cli diff <file-a> <file-b> --offset 50

Exactly two files. It aligns them automatically (cross-correlation), then runs a spectral diff and a null test over the overlap. Options: `--json`, `--csv`, `--offset <ms>` (pin the alignment; positive means B's content starts later than A's), `--threshold-db <dB>` (default 40).

JSON (an array with one object): `fileA`, `fileB`, `offsetMs`, `alignConfidence`, `driftMs`, `overlapSeconds`, `meanAbsDb`, `rmsDb`, `maxAbsDb`, `withinTolerancePct`, `residualRmsDb`, `residualPeakDb`, `nullDepthDb`, `thresholdDb`, `same`.

- `same` is true when `nullDepthDb` (how far the A minus B residual sits below the signal) reaches `thresholdDb`, or when the samples are identical (the text says `perfect null`). Exit 0 for SAME, 1 for DIFFERS.
- The text form warns when `alignConfidence` is below 0.3 (the offset may be wrong) and notes when `driftMs` exceeds 20 (one offset cannot align the whole file). With a pinned `--offset`, `alignConfidence` is `null`.
- DIFFERS does not mean a different recording: a lossy copy of the very same master differs too (a 128 kbps MP3 of a WAV comes out DIFFERS). `diff` answers "are these the same samples"; for "is this the same song", use `dupes`.

## loudness

    spektra-cli loudness <file> --json
    spektra-cli loudness <folder> --json > <scratch>/loudness.json

EBU R128 measurement through ffmpeg. Files or one folder. Options: `--json`, `--csv`, `-j N`.

JSON fields: `file` (bare name), `integratedLufs`, `loudnessRangeLu`, `truePeakDbtp`, `peakDbfs`, `rmsDbfs`, `crestFactorDb`, `clippedSamples`, `error`. Text: `-3.9 LUFS integrated, LRA 20.0 LU, true peak -0.9 dBTP, crest 3.0 dB.` There is no loudness verdict; exit 1 only when a file could not be measured.

## manifest

    spektra-cli manifest <folder> --json > <scratch>/manifest.json
    spektra-cli manifest <folder> --html <scratch>/manifest.html

Lists every file under one folder, audio or not, recursively. It never decodes and is the one command that works without ffmpeg. Options: `--json`, `--csv`, `--html <file>`.

JSON fields: `path` (absolute), `name`, `kind`, `severity`, `sizeBytes`.

- `kind` is the real codec when the audit cache knows the file (`pcm_s16le` for a WAV), otherwise the extension (`jpg`, `txt`).
- `severity` is `Problem`, `Suspect` or `Clean` from the cache, and `null` for files never analysed and for non-audio files.

Text: one line per file (`flac     191.2 KB  <path> · Problem`), then a line with the file count, the composition by kind and the total size.

## inventory

    spektra-cli inventory <folder> --json > <scratch>/inventory.json

Tags, stream facts and embedded cover art for everything in exactly one folder, recursively. It reads with ffprobe but never decodes, so it is fast on any size of library and carries no verdicts. Options: `--json`, `--csv`, `-j N`.

JSON fields: `path`, `name`, `ext`, `sizeBytes`, `isAudio`, `codec`, `sampleRateHz`, `channels`, `bitsPerSample`, `bitrateBps`, `durationSeconds`, `artist`, `albumArtist`, `album`, `title`, `track`, `trackTotal`, `disc`, `discTotal`, `year`, `genre`, `hasEmbeddedArt`, `artFormat`, `artWidth`, `artHeight`, `error`.

- `path` is relative to the folder with `/` separators, the same form `audit` uses for that folder, so the two outputs join on it.
- Non-audio files (a `cover.jpg`) get rows too, with `isAudio` false.
- Tags are normalised: `5/12` becomes `track` 5 and `trackTotal` 12, any date becomes a plain `year`, and a blank tag reads as `null`.
- A file ffprobe cannot read (truncated, zero bytes) keeps its row with `error` set, so damage never reads as absence.

Sample audio row:

```json
{
  "path": "tagged-with-art.flac",
  "name": "tagged-with-art.flac",
  "ext": "flac",
  "sizeBytes": 47051,
  "isAudio": true,
  "codec": "flac",
  "sampleRateHz": 44100,
  "channels": 1,
  "bitsPerSample": 16,
  "bitrateBps": 125469,
  "durationSeconds": 3,
  "artist": "Aurora",
  "albumArtist": "Aurora",
  "album": "First Light",
  "title": "Intro",
  "track": 5,
  "trackTotal": 12,
  "disc": 1,
  "discTotal": 2,
  "year": 2019,
  "genre": "Ambient",
  "hasEmbeddedArt": true,
  "artFormat": "png",
  "artWidth": 600,
  "artHeight": 600,
  "error": null
}
```

## image

    spektra-cli image <file> -o <scratch>/spectrogram.png
    spektra-cli image <file> -o <scratch>/spectrogram.png --palette grayscale --fft 4096

One audio file; folders and `--json` or `--csv` exit 2. It prints `Wrote <path> (<width>x<height>)`. Without `-o` the PNG goes beside the input and `image` never replaces a file it was not given: if `<name>.png` is already there it writes nothing and exits 2 with `spektra-cli: 'song.png' already exists; pass -o <out.png> to write somewhere else, or -o 'song.png' to replace it.` With `-o` it replaces the file it names, except the input itself (compared by path): `-o` naming the input exits 2 with `spektra-cli: -o 'song.flac' is the input file itself; pass another output file.`, and a blank `-o` (empty or only whitespace) exits 2 with `spektra-cli: -o needs a file path.`

Options: `-o <png>` or `--out <png>` (always pass it, into `<scratch>`: the default is the input path with `.png`, inside the library); `--palette <name>` (`magma`, `viridis`, `inferno`, `grayscale`, `plasma`, `cividis`, `turbo`, `monogreen`, `monoamber`, or a custom palette; an unknown name falls back to turbo with a warning); `--gamma <g>` (the app's Tightness); `--floor <dB>` (default -120); `--fft <size>` (default 2048); `--channel <n>` (1-based; default a mixdown); `--columns <n>` (the maximum width, default 2048). Palette and gamma default to the desktop app's saved settings.

Reading the PNG: it is the raw spectrogram with no axes or labels. Time runs left to right across the whole file. Frequency runs linearly from 0 Hz at the bottom row to half the sample rate at the top, and the height is FFT size / 2 + 1 rows. A transcode shows a hard, flat ceiling across the whole file with darkness above it; on a 44.1 kHz file, a ceiling about 73% of the way up sits near 16 kHz. An upsample shows empty space above the original rate's limit (22.05 kHz for a 44.1 kHz source).

## Output formats

- JSON is an indented array of objects with camelCase keys, enums as strings and `null` for missing values; `diff` emits an array of one.
- CSV has a header row with PascalCase names in the same order. String cells starting with `=`, `+`, `-` or `@` get a leading apostrophe (a formula-injection guard), so prefer JSON for parsing.
- In Windows PowerShell 5.1, `>` writes UTF-16 and `Out-File -Encoding utf8` adds a byte-order mark, either of which trips a strict JSON parser; redirect from bash or cmd instead, or decode the file as UTF-16 or `utf-8-sig`.
