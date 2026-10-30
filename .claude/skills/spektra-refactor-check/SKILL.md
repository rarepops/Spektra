---
name: spektra-refactor-check
description: Prove that a Spektra change which claims to change no behaviour really changed none, layer by layer, before calling it done or opening a PR. It diffs the CLI's output at the branch point and the branch tip with tools/compare-cli.ps1, runs new view-model tests against both the old and the new code, compares GUI captures byte for byte, and plants a bug for every new test to show it can fail. Use it for any refactor, file move, rename, extraction, cleanup, dependency or package bump, or "no functional change" commit, and whenever someone asks "did this change behaviour?", "is this a pure refactor?", "prove nothing changed", or "compare the old and new output".
---

# Spektra refactor check

A change that claims to change no behaviour is proved by comparison, not by a green test run: tests only check what they check. Compare the old and the new code on every layer the change can reach, and report the evidence layer by layer.

Commit the change first: every layer compares commits, and uncommitted code is in neither of them. The base is the branch point (`git merge-base dev HEAD`), the head is the branch tip.

## 1. The CLI: compare-cli

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/compare-cli.ps1 -Base <branch point> -Head <branch tip>`. It exports both commits with `git archive` into a scratch folder under `$env:TEMP`, builds the CLI in each, runs every line of the matrix at the top of the script over one shared library with a throwaway profile per side, and compares the transcripts. Exit 0 is IDENTICAL, 1 is different (the first differing invocations are printed with their lines), 2 is a setup error. The runs take under half a minute per side; the first use of a commit adds its export and build.

- IDENTICAL is the bar. Every difference is a behaviour change until shown otherwise: explain each one, and never widen the script's normalisation just to reach IDENTICAL.
- Before comparing, add matrix lines for any verb, flag or output branch the change touches that the matrix does not run yet (`$Matrix` at the top of the script, one invocation per line). The matrix and the fixture library come from the checkout you run the script from, so a new line works before it is committed, and it runs on both sides, so the old commit vouches for it. Keep the lines that would serve the next refactor.
- Read the marker table. Each row counts how often one print branch fired; a 0 under head is a branch no invocation reaches. If the change touches such a branch, compare the old and new code for it by reading, and say so in the report.
- To check the harness itself (after editing the matrix, say), compare a commit with itself: anything but IDENTICAL means some output is not deterministic.
- For the whole picture, run the `git diff --no-index` line it prints on a difference.
- The work folder keeps every export for reuse (about 70 MB each, built) and every comparison (about 16 MB), so it only grows; delete it whenever you like.

## 2. App view models: tests that pass on both sides

1. Write tests for the behaviour the change touches, against the new code. A test must never open the real audit cache or settings: when the code only knows its default path, add a seam (an optional path parameter that defaults to the real one) and point it at a temp folder.
2. Export the base into a scratch folder outside any checkout (`git archive --format=zip -o <scratch dir>/base.zip <base>`, then `powershell -NoProfile -Command "Expand-Archive -LiteralPath <scratch dir>/base.zip -DestinationPath <scratch dir>/base"`), copy the new test files in, and add only the seam the tests need, nothing else from the change. Run `dotnet test --project tests/Spektra.Tests -c Release` there. Run it plain: never add `--nologo`, which some .NET 10 SDK bands reject in the Microsoft.Testing.Platform mode that `global.json` selects, and a run with its output redirected to a file has hung with the test host idle.
3. The tests must pass on both sides. Passing on the base proves the behaviour they pin existed before the change; passing on the head proves it survived.
4. Confirm each new test by name in each run's results (TUnit also writes an HTML and a JSON report under `TestResults/`), not by the total: a test file that never compiled into the export, or a filter that matched nothing, still leaves a green run.

## 3. The GUI: byte-identical captures

If the change can reach what the app draws, build both commits and capture the same file from each with the spektra-gui-smoke skill, then compare the PNG hashes. Equal hashes prove the first load is unchanged. Unequal ones get the skill's pixel comparison: a difference confined to the status bar's text is a race between two background jobs or the clock, not the change. Anything behind input goes to the maintainer as a few named checks.

## 4. Prove every new test can fail

A test that cannot fail proves nothing, and tests written for behaviour that already exists pass on their first run. For each new assertion:

1. Plant a temporary bug in the production code that this assertion should catch, marked with a `// TEMP-RED-CHECK` comment. Never plant anything that points code at the real cache or settings.
2. Run the whole suite and record which tests fail and with what message. If nothing fails, the test is blind to that change: strengthen it.
3. Restore the file with `git restore -- <file>`, then confirm `git diff` is clean for it and `git grep TEMP-RED-CHECK` finds nothing before planting the next one.

## Report

Per layer, give the command, the result and what stayed uncovered:

- CLI: the IDENTICAL line (base and head commits, invocation count), any matrix lines added, and every zero-marker branch the change touches with how it was checked by reading.
- View models: the new tests by name, passing on base and on head.
- GUI: both PNG hashes, or why the change cannot reach the window.
- Red checks: each plant, the test that caught it, and its failure message.
