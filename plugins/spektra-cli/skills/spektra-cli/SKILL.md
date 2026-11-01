---
name: spektra-cli
description: "Run the spektra-cli command-line tool to inspect audio files and music libraries. It finds fake lossless files (lossy transcodes and upsamples hiding in FLAC, WAV or hi-res files), corrupt or truncated downloads, and duplicate songs across folders and formats; compares two files or two folders; measures loudness (LUFS, true peak); lists tags and cover art; and renders spectrogram PNGs. Use it whenever the user asks, even without naming the tool, whether their FLACs or hi-res files are genuine, or wants to audit, check, clean up or deduplicate their music collection, compare two copies of an album or library, verify a rip or download, or run spektra-cli on their files. Covers which command answers which question, how to read verdicts and exit codes, working at library scale, and the safety rules (Spektra never changes files, and you must not either without the user's explicit go-ahead). Not for developing Spektra itself: building, testing or releasing Spektra from its source has its own project skills."
---

# spektra-cli

Spektra is an audio analyzer and `spektra-cli` is its command-line tool. It judges whether a file's audio is what its format claims (a FLAC made from an MP3 is not lossless), finds damage and duplicates, and measures loudness. It never modifies, moves or deletes the user's audio; the only files it writes are the reports and images you ask for, plus its own cache. Every command except `image` prints JSON with `--json`, so run it once, save the JSON, and reason from the file.

Read `reference.md` (beside this file) for each command's flags, JSON fields, sample output and the folder-difference recipe.

## Before the first command

1. Run `spektra-cli --version`; it prints `spektra-cli <version>`. It proves the CLI is installed and tells you what to expect: this skill describes releases after 0.25.1. With 0.25.1 or older, tell the user that version misses a lossy file renamed to a lossless extension, can replace a PNG that already sits beside the audio, lets some unreadable files and missing paths pass, and prints no count of the files `dupes --json` or `--csv` could not analyze; offer the update with the commands in step 2. Until it is updated, compare each file's extension with its `codec` yourself, always pass `-o` to `image`, look for rows with `error` set, and for `dupes` also save the text output and count its `! not analyzed` lines.
2. If the command is missing, tell the user and offer to install it rather than doing it unasked: `npm install -g spektra-cli` (Node 18 or newer; Windows x64, Linux x64 with glibc, macOS on Intel or Apple silicon), or a `spektra-cli-<version>-<os>.zip` from https://github.com/rarepops/Spektra/releases/latest (the Windows installer includes it too).
3. ffmpeg and ffprobe must be installed (on `PATH`), because Spektra bundles neither. They are checked when a real command runs: without them every command except `manifest` exits 2 with `ffmpeg/ffprobe not found` on stderr.

## Rules

- **Inside the Spektra source repository, this skill does not apply.** It runs the installed `spektra-cli` in the user's real profile, which can write the real audit cache; that repository's `CLAUDE.md` forbids running the app or the CLI against the user's real data and requires an isolated profile for every hand run, so follow it and its project skills instead.
- **Never act on a verdict by yourself.** Do not delete, move, copy, rename, overwrite, re-tag or re-encode anything in the user's library because a report, a verdict or a duplicate group suggests it. Present the findings, let the user decide, and act only on an explicit confirmation of that action on those files. Verdicts are evidence, not proof (that is why `Suspicious`, `Medium` and `Low` exist), and a deleted original rarely comes back.
- **Write outputs to a scratch folder, never into the library.** That covers JSON, CSV, HTML and PNG files, so always pass `-o <scratch>/...` to `image`. Without `-o` it writes the PNG beside the audio file, named after it with `.png` in place of its extension (`song.flac` gives `song.png`), and refuses to replace a file already there (exit 2, nothing written). An explicit `-o` does replace the file it names, though not the audio file itself (checked by path).
- **Keep library data out of the conversation.** Redirect folder-sized output to a file and read it with a short script that prints counts and a few examples. Summarise (counts, the folders where problems cluster, two or three examples) instead of pasting path lists: `dupes` and `manifest` print absolute paths, which often contain the user's name.
- **Leave `--fresh` alone unless the user asks** for a re-analysis from scratch: it ignores the cache and decodes every file again.

## Which command answers which question

Quote paths that contain spaces. `<scratch>` is a folder outside the library, such as a temp directory. Redirect only stdout to the file: stderr carries errors and warnings.

| The user wants to know | Run |
|---|---|
| Are my lossless files real, or transcodes and upsamples? | `spektra-cli audit <folder> --json > <scratch>/audit.json` |
| Is anything corrupt or truncated? | `spektra-cli check <folder> --json > <scratch>/check.json` (`audit` covers this too) |
| What do I have twice? | `spektra-cli dupes <folder> --json > <scratch>/dupes.json` |
| What does folder A have that folder B lacks? | `spektra-cli dupes <folder-a> <folder-b> --json > <scratch>/pairs.json`, then subtract (recipe in reference.md) |
| Are these two files the same master? | `spektra-cli diff <file-a> <file-b> --json` |
| How loud is it? | `spektra-cli loudness <file> --json` (a folder works too; redirect it) |
| What is in this folder: tags, cover art, file types? | `spektra-cli inventory <folder> --json > <scratch>/inventory.json` for tags and cover art, or `spektra-cli manifest <folder> --json > <scratch>/manifest.json` for each file's type and, where an earlier `audit` or `dupes` analysed it, its verdict (`manifest` also runs without ffmpeg) |
| Show me | `spektra-cli image <file> -o <scratch>/spectrogram.png`, then open the PNG and look at it |

Prefer `audit` for any question about a folder's quality. It runs the bandwidth and integrity checks together, caches each file's result so a repeat run only analyses new or changed files, counts only real problems, and keeps folder-relative paths. `report` and `scan` give the bandwidth half alone, count every lossy file (an honest MP3 included) as a finding, and their JSON keeps only bare file names.

Give `audit`, `report`, `check` and `loudness` either ONE folder (searched recursively) or a list of files. A folder together with other paths, or a path that does not exist, exits 2 before anything is analyzed. `dupes` is the command that takes several folders.

## Reading the results

**Exit codes:** `0` clean, `1` findings, `2` usage or setup error (a bad option, no ffmpeg, a path that does not exist, a folder given together with other paths, a missing folder for `scan`, `dupes`, `manifest` or `inventory`). A mistyped path is exit 2 with a message on stderr and nothing on stdout, so check the exit code before parsing a redirected file. For `audit`, `report`, `scan`, `check` and `loudness`, a file that exists but cannot be read is a finding, exit 1: its row has `error` set (for `check`, a `Corrupt` or `Error` status), so look for those rows before calling a folder clean. Exit 1 is a result, not a failure, so read the output. What counts as a finding differs per command (see reference.md); `manifest` and `inventory` always exit 0 when they run.

**Bandwidth verdicts** (`bandwidth` in `audit` JSON, `verdict` in `report` and `scan`):

- `Lossless`: full-band content, as a lossless file should have.
- `Lossy` (with `cutoffHz`): a sharp codec cutoff. Normal for MP3, AAC, Ogg or Opus: an honest lossy file is not a problem. It is a problem inside a lossless container (FLAC, WAV, ALAC, APE, WavPack), which means a transcode, or in an MP3 or AAC whose cutoff sits far below what its bitrate delivers (re-encoded from a lower bitrate). A wall near 16 kHz is the classic MP3 128 kbps signature.
- `Upsampled`: the content stops at a lower standard rate's limit, so a 96 kHz file holds a 44.1 kHz master. Always a problem: the file is bigger, not better.
- `Mixed`: only part of the file looks transcoded, typical of compilations and continuous mixes. Counted as a problem, but name it apart from whole-file transcodes.
- `Suspicious`: a rolloff that could be natural, or a wall at 20 kHz or above. Inside a lossless file that wall can be a transcode from a high-bitrate MP3 or simply a band-limited master; Spektra cannot tell them apart, so `audit` does not count it. Show these to the user as worth a look, with a spectrogram, rather than calling them fake.
- `Unknown`: too quiet or too narrow-band to judge (test tones, near-silent or very narrow-band material). Neither good nor bad.
- `Error`: the file could not be read; the `error` field says why.

**Integrity** (`integrity` in `audit`, `status` in `check`): `Ok`; `Suspect` (one or two stray decode errors, worth a listen, often junk bytes appended by a tagger); `Corrupt` (decode failure, three or more errors, or shorter than its own header says, as a truncated download is); `Error` (unreadable). Silent gaps are reported but never raise the verdict.

**What `audit` counts as a problem:** unreadable, upsampled, mixed, corrupt, `Lossy` where it does not belong (see above), or a lossy `codec` under a lossless extension (`.flac`, `.wav`, `.aiff`, `.aif`, `.ape`, `.wv`, `.alac`), whatever its `bandwidth` says: an MP3 renamed `.flac` is counted, and its text line ends `· holds mp3`. Honest lossy files and `Suspicious` ones are not counted. The JSON rows carry no problem flag; `manifest` on the same folder afterwards gives Spektra's own per-file call (see reference.md).

## Working at library scale

- The first `audit` or `dupes` of a big library decodes every file and can take a long time, so tell the user and run it in the background or with a long timeout. `audit` prints nothing until it finishes, and `dupes` shows progress only in an interactive terminal. Later runs reuse a per-user cache (keyed by file size and modified time) and only analyse new or changed files. `audit`, `dupes`, `manifest` and the desktop app share that cache.
- Run once with `--json` into a file under `<scratch>`, then answer follow-up questions by filtering that file with a short script. Do not re-run the scan for each question.
- `-j N` (or `--jobs N`) caps the worker count; the default uses about 80% of the CPU cores. Offer it when the user wants the machine to stay responsive.
- `--html <scratch>/report.html` on `audit`, `dupes` or `manifest` also writes a self-contained page for a person to open. It is for the user, not for you to parse.

## Traps

- `dupes` skips files shorter than 20 seconds and files it cannot read. The text output lists them as `! not analyzed`; `--json` and `--csv` keep their rows as they were and print one line on stderr instead, `spektra-cli dupes: <n> file(s) were not analyzed and could not be matched (shorter than 20 s, or unreadable); run without --json or --csv to list them.` An empty `[]` does not prove there are no duplicates, so read stderr. To see which files, save the text output to a file as well and read those lines, or check durations from `inventory`.
- `dupes` matches recordings, not songs: a remix, a live take, an edit more than 15 seconds longer or shorter, or a differently mastered release will not pair with the original.
- `manifest` and `inventory` never decode audio, so they are fast but add nothing about quality. `inventory` has no verdict columns at all; `manifest` shows a verdict only for files an earlier `audit` or `dupes` analysed.
- Path forms differ: `audit` (on a folder) and `inventory` give paths relative to that folder with `/` separators; `dupes` and `manifest` give absolute paths; `report`, `scan`, `check` and `loudness` give bare file names.
- Prefer `--json` to `--csv` for parsing: CSV prefixes cells that start with `=`, `+`, `-` or `@` with an apostrophe, a guard against spreadsheet formula injection.

## Example: "find the fake FLACs in my Music folder"

1. `spektra-cli --version`, to confirm the CLI is installed. A missing ffmpeg shows up in the next step as exit 2.
2. `spektra-cli audit <folder> --json > <scratch>/audit.json`, with `<folder>` the user's Music folder. Expect exit 1 when anything is found, and a long first run on a big library.
3. A mistyped folder exits 2 and leaves `audit.json` empty, so check the exit code first. Then look for rows with `error` set (unreadable files; a filter on `codec` would drop them silently). Sort the rows with a short script, never by printing them all. Likely fakes: a lossless `codec` (`flac`, `alac`, `ape`, `wavpack`, `pcm_s16le` and other `pcm_*`) with `bandwidth` `Lossy` or `Upsampled`. Name apart: a lossy `codec` (`mp3`, `aac`) behind a lossless extension (`audit` counts these, but they are renamed files, not transcodes), `Mixed` (part of the file), `Suspicious` inside a lossless file (worth a look, not proof), and `Corrupt` integrity (damaged, a different finding).
4. Report counts, where the likely fakes cluster (often whole albums from one source) and their cutoffs. Offer a spectrogram of one example as evidence, or `--html` for a page the user can browse.
5. Stop there. If the user wants to replace or remove files, confirm exactly which files and which action first.
