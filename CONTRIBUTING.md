# Contributing to Spektra

Thanks for your interest in Spektra. Bug reports, feature ideas, and pull
requests are all welcome.

## Ways to help

- **Report a bug**: open an issue with steps to reproduce, the file type/codec
  involved (a spectrogram screenshot helps), and your OS.
- **Suggest a feature**: open an issue describing the use case.
- **Send a pull request**: for anything beyond a small fix, please open an issue
  first so we can agree on the approach.

## Development setup

Prerequisites:

- [.NET SDK 10](https://dotnet.microsoft.com/) (the exact version is pinned in
  `global.json`).
- [ffmpeg + ffprobe](https://ffmpeg.org/) on `PATH`. They decode audio at runtime
  and are needed by the tests, which analyze small committed fixtures.

Clone, build, and test:

    git clone git@github.com:rarepops/Spektra.git
    cd Spektra
    dotnet build Spektra.slnx -c Release
    dotnet test tests/Spektra.Tests -c Release

Run the app or the CLI:

    dotnet run --project src/Spektra.App                 # GUI
    dotnet run --project src/Spektra.Cli -- report a.flac # CLI

## Project layout

- `src/Spektra.Core` — the pure analysis engine (decode options, FFT, palettes,
  cutoff/verdict, diff, aligner). No UI. Fully unit-tested.
- `src/Spektra.App` — the Avalonia GUI (MVVM).
- `src/Spektra.Cli` — the cross-platform command-line tool.
- `tests/Spektra.Tests` — xUnit tests. **Tests target `Spektra.Core` only**;
  UI is verified manually.
- `tools/make-fixtures.ps1` — regenerates the committed audio fixtures.

## Conventions

- **Core is test-first.** New analysis logic goes in `Spektra.Core` with tests.
  Keep it free of Avalonia and UI concerns so it stays easy to test.
- **Style**: file-scoped namespaces, records for data, and the existing
  `Set(ref field, value)` pattern in view models. Match the surrounding code.
- **Commits**: [Conventional Commits](https://www.conventionalcommits.org/)
  (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`, `build:`, `ci:`).
- **Fixtures**: if a test needs new audio, add it to `tools/make-fixtures.ps1`,
  regenerate, and commit the (small) result.

## Branches and releases

- Work happens on `dev` (or feature branches merged into it).
- `main` is the release branch. CI (build + test) runs on every push and PR.
- Releases are cut by the maintainer: merge to `main`, then push a `vX.Y.Z` tag.
  A GitHub Actions workflow builds the installer and binaries and publishes the
  release. Update `CHANGELOG.md` as part of the release.
- If verdict or integrity analysis changed, bump `AuditCache.AnalysisVersion`
  so cached audit rows re-analyze.
- **Only pushing the `vX.Y.Z` tag publishes a release.** Running the release
  workflow manually (`workflow_dispatch`) builds the same artifacts for
  inspection but does not create a GitHub release, so it is a safe dry run.

## License

Spektra is source-available under [PolyForm Perimeter 1.0.1](LICENSE.md). By
submitting a contribution you agree that it is provided under that same license
and may be included in the project.
