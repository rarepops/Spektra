# Spektra

Spektra is a desktop audio spectrum analyzer (C#, .NET 10, Avalonia) with a cross-platform command-line tool, `spektra-cli`: it shows a file's spectrogram, gives an "is this really lossless?" verdict, checks integrity and finds duplicates. The repository is public and source-available (`LICENSE.md`).

## Layout

- `src/Spektra.Core`: the analysis engine. No UI; new analysis logic and its tests start here.
- `src/Spektra.App`: the Avalonia GUI (MVVM).
- `src/Spektra.Cli`: `spektra-cli`.
- `tests/Spektra.Tests`: the TUnit suite. `tests/fixtures` holds small committed audio files made by `tools/make-fixtures.ps1`.
- `bench/`: BenchmarkDotNet benchmarks, built with the solution, run only on demand.
- `tools/`: PowerShell helpers (fixtures, ffmpeg, release validation, smoke checks).
- `docs/`: user documentation for the GUI and the CLI. `packaging/`: the WiX installer and the npm packages.

## Clean room

Spektra is clean-room code. Never open, read, search, quote or reference the source of the GPL program Spek or of any fork of it, not even to compare: behaviour may only be compared by running the released program. This holds for every task, whichever skill is loaded.

## Gates

Run all three before calling a code change done. CI runs the same ones.

- Build: `dotnet build Spektra.slnx -c Release`. `TreatWarningsAsErrors` is on, so a single warning fails it.
- Tests: `dotnet test --project tests/Spektra.Tests -c Release`. `global.json` opts into the Microsoft.Testing.Platform runner, whose `dotnet test` has its own option set and rejects options it does not know: run it as written, and never add `--nologo`, which some .NET 10 SDK bands reject in this mode. The tests decode audio, so `ffmpeg` and `ffprobe` must be on PATH. `tools/get-ffmpeg.ps1` downloads the pinned build into `%LOCALAPPDATA%\Spektra\ffmpeg` (that folder then goes on PATH); it writes into the user's real app data, so ask before running it.
- Format: `dotnet format Spektra.slnx --verify-no-changes`.

## Tests and the user's data

- Tests target `Spektra.Core`, and `Spektra.App` view models where that makes sense. Windows and rendering are checked by hand.
- Nothing may touch the user's real settings, audit cache or downloads. A view model that opens a store takes an optional path that tests pass instead, as `DuplicatesViewModel` does with `cachePath`.
- Never assert on an Avalonia brush in a test: a brush belongs to the thread that created it, and the test's thread cannot read it.
- To run the app or the CLI by hand, point `USERPROFILE`, `APPDATA` and `LOCALAPPDATA` for that process at a scratch folder whose `AppData\Roaming` and `AppData\Local` already exist. Any one of the three alone can still resolve to the real profile. Launch the app only when no Spektra is running: a running instance takes over any new launch, isolated or not, and opens its file in the user's own session.
- Never send synthetic mouse or keyboard input: it lands in whatever window has focus, which may be the user's. To see the GUI, use the `spektra-gui-smoke` skill.

## Branches and releases

- Work on `dev`, or on a feature branch merged into it. `main` moves only at releases.
- Never push, tag or release without the maintainer's go-ahead in the current conversation; the `spektra-release` skill says which request covers which step.

## Conventions

- Conventional Commits (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`, `build:`, `ci:`). Commits are signed; never pass `--no-gpg-sign` or `--no-verify`. No AI attribution lines (`Co-Authored-By` or similar).
- No em-dashes in anything written (code, comments, docs, commit messages): use a comma, a colon, parentheses or a middot. Markdown is one line per paragraph or bullet, never hard-wrapped. LF line endings everywhere (`.gitattributes`, `.editorconfig`).
- The repository is public: never commit personal paths, names, emails, or details of anyone's files or music library.
- Bump `AuditCache.AnalysisVersion` only when how a verdict or integrity result is computed changes, so cached audit rows re-analyze; changes that only alter when files are analyzed leave it alone, and a change to the problem rule alone needs no bump either, because the problem flag is derived from the cached row on read (see `CONTRIBUTING.md`).
- Everything else, from code style to fixtures, is in `CONTRIBUTING.md`.

## Skills

- `spektra-release`: cutting, pushing, verifying or recovering a release.
- `spektra-gui-smoke`: launching the app on a file in an isolated profile and capturing its window, to see a change in the real GUI.
- `spektra-refactor-check`: proving that a change which claims no behaviour change really changed none: `spektra-cli` output compared between two commits, view-model tests run on the old and the new code, GUI captures, and a planted bug for each new test.
