---
name: spektra-release
description: 'Check, prepare, cut, push and verify a Spektra release: choose the semver bump, bump Directory.Build.props and the Claude plugin version, write the CHANGELOG entry, run every pre-tag gate (build, tests, format, vulnerable packages, plugin validation, publish smokes that mirror release.yml, isolated CLI and GUI launch smokes, the install-first MSI check), make the signed release commit and tag, push dev, main and the tag in the only safe order, then check the GitHub release, its checksums and the npm packages, stopping where the request does. Use it whenever someone wants to check, prepare, cut, release, ship, publish or tag a version of Spektra ("check whether we can release", "prepare the 0.26.0 release", "cut 0.26.0", "do a patch release", "push and release", "bump the version and tag it", "write the changelog for the release"), and when a release run or its npm publish failed and needs recovering. Not for ordinary commits or pushes to dev.'
---

# Releasing Spektra

Pushing a `vX.Y.Z` tag makes `.github/workflows/release.yml` build the installer, the archives and the CLI for four platforms, create the GitHub release and publish five npm packages. None of that can be taken back: the release page is public at once, and an npm version number can never be used again. So every check runs before the tag, the pushes go in one fixed order, and nothing public happens without the maintainer's go-ahead.

Work through the steps in order, as far as the request goes (see "Before you start"). [reference.md](reference.md) holds the rarer paths: building the MSI for the install-first check, two releases at once, a dry run that publishes nothing, and recovering a failed run or npm publish.

## Before you start

The maintainer's verb decides where you stop and report, so settle it before step 1:

- "check" a version, or whether one can ship: steps 1 and 4 only, and no tracked file is edited. Step 2 is skipped (its validation needs the bumped props file), and the publish smokes pass the version themselves with `-p:Version`.
- "prepare" a version: steps 1 to 4, leaving exactly three files modified and uncommitted: `Directory.Build.props`, `CHANGELOG.md` and `plugins/spektra-cli/.claude-plugin/plugin.json`.
- "cut", "release" or "tag" a version: steps 1 to 6, then stop before any push and show the version and its reason, the gate results, and the tag's subject and body.
- "push", "publish" or "ship" a version: steps 1 to 8. This covers the whole release even when the request also says "release", "cut" or "tag", so "push and release" goes through in one go. The stops written into the steps (a failed gate, the dry run's go-ahead, the install-first report, a number that is not next in sequence, a number you raised above what was asked) still apply in every tier.
- If the verbs disagree, or it is unclear how far the maintainer meant, take the nearer stop and ask: a stop costs one sentence to lift, a push cannot be taken back.
- A later request continues where the last one stopped and does not redo finished steps, except that the step 4 gates run again after a check or a prepare (the tree has changed, or may have been edited, since), and that after a cut you first confirm the release commit and the tag still exist locally and `git tag -v vX.Y.Z` still verifies. A prepared CHANGELOG entry takes the date of the day it is committed, so re-date it if that day has moved on. An install-first report the maintainer already gave and a dry run that passed both stand unless something besides the three files of a prepare has changed since: repeating them would test nothing new, and the maintainer's time is the scarce resource.
- A go-ahead counts only when given in the current conversation and for this version; one given for an earlier release never carries over.

Then the setup:

- Work from the repository root, on Windows (the GUI smoke and the installer need it). `git branch --show-current` must print `dev`, the tree must be clean or hold only the three files of a prepare for this same version (all three at that version), and after `git fetch origin`, `git status -sb` must not show `dev` behind `origin/dev` (if it is, `git merge --ff-only origin/dev`). If any of that does not hold, stop and ask.
- Commands below are Git Bash. The repo's PowerShell scripts run through `powershell -NoProfile -ExecutionPolicy Bypass -File`: Windows PowerShell is enough (PowerShell 7 need not be installed), and `Bypass` lifts the execution policy for that one process only, without which a machine on the default `Restricted` policy refuses to run the scripts.
- Needed: the .NET SDK that `global.json` names; `ffmpeg` and `ffprobe` on PATH (the tests and both smokes decode audio); Node.js (`node` and `npm`); `gh` signed in (`gh auth status`); WiX, only for the install-first MSI. If `ffmpeg -version` fails, ask: `tools/get-ffmpeg.ps1` installs into the user's real app data and is not yours to run unasked.
- Placeholders: `X.Y.Z` is the new version; `<prev-tag>` the last release tag, from `git describe --tags --abbrev=0` run once in step 1 (after step 6 it would print the new tag); `<release-sha>` the full id of the release commit; `<run-id>` a workflow run id; `<scratch>` a new, empty folder outside the checkout for each attempt, written as a literal path such as the output of `echo "$(cygpath -m /tmp)/spektra-release-X.Y.Z-1"` (then `-2` for a second attempt, so a re-run never meets a warm cache, an old profile or an old capture). Shell variables do not survive between tool calls, so write the values out.
- Smoke builds go to `<scratch>`, never into the checkout: `dist/` is what the installer and the npm packaging read, and the tree should hold only what gets committed. The one exception is the install-first MSI, which WiX builds from `dist/` (see reference.md).
- The tests and the publishes take minutes: give those commands a long timeout, or run them in the background and wait for them to exit.

## 1. Choose the version

Note `<prev-tag>` now, then read what the release contains: `git log --oneline <prev-tag>..HEAD`. A new user-visible feature means a minor bump (`0.25.1` to `0.26.0`); only fixes, or nothing a user can see (refactors, tests, CI, dependency updates), mean a patch (`0.25.1` to `0.25.2`). A number the maintainer named stands, unless the content needs a bigger bump: a "patch" that carries a feature ships as a minor. Say which and why in one line, then carry on through the local steps without waiting for an answer. A number you raised above what was asked needs the maintainer's yes before step 7 on any request, because a version number can never be reused. If a named number is not the next one in sequence, stop and ask.

Check that the number was never used, because neither a pushed tag nor an npm version can be taken back: `git ls-remote --tags origin vX.Y.Z` must print nothing, and `npm view spektra-cli@X.Y.Z version` must fail with `E404`.

If a change alters how a verdict or integrity result is computed, `AuditCache.AnalysisVersion` must already have been bumped (`CONTRIBUTING.md`, "Branches and releases"), or every user's cached audit rows stay stale. `git diff --stat <prev-tag>..HEAD -- src/Spektra.Core` shows what to judge, and `git diff <prev-tag>..HEAD -- src/Spektra.Core/AuditCache.cs` whether it moved. If a bump was missed, stop and raise it.

## 2. Bump the version

- `Directory.Build.props`: set the one `<Version>` to `X.Y.Z`. Every project inherits it.
- `plugins/spektra-cli/.claude-plugin/plugin.json`: set `version` to `X.Y.Z`. The Claude Code plugin follows Spektra's version and nothing compares the two, so `grep -n '"version"' plugins/spektra-cli/.claude-plugin/plugin.json` must show `X.Y.Z`.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/validate-release.ps1 -Version X.Y.Z` must end with `Release validation passed.` (a bare X.Y.Z, equal to the props file; its on-main check comes in step 7).

## 3. Write the CHANGELOG entry

- Add `## [X.Y.Z] - YYYY-MM-DD` (today) directly above the newest version heading in `CHANGELOG.md`, with only the sections that have entries, in this order: `### Added`, `### Changed`, `### Fixed`.
- Describe the final state a user meets, not the road there: something renamed twice inside the release appears once, under its final name, and a bug introduced and fixed within the release is not a fix.
- Match the entries already there: plain words; what changed for the user and why it matters; UI labels in backticks; one line per bullet, never hard-wrapped; no em-dashes. Dependency updates get one bullet under `### Changed` naming each package and version. A release with nothing user-visible says so plainly first, as `0.25.1` does.
- At the bottom of the file, point `[Unreleased]` at the new tag and add the new link line right below it. The heading renders fine without its link line, which is why it is easy to forget, and it has been forgotten before.

```
[Unreleased]: https://github.com/rarepops/Spektra/compare/vX.Y.Z...HEAD
[X.Y.Z]: https://github.com/rarepops/Spektra/releases/tag/vX.Y.Z
```

This must print exactly three lines, the heading and both link lines:

```bash
grep -nE '^(## \[X.Y.Z\] - |\[Unreleased\]: .*/compare/vX.Y.Z\.\.\.HEAD$|\[X.Y.Z\]: .*/releases/tag/vX.Y.Z$)' CHANGELOG.md
```

## 4. Run the pre-tag gates

`release.yml` runs none of CI's checks: not the tests, not the format check, not the npm packaging tests. It builds and publishes whatever the tag points at, so a release can go out green while CI on the same commit is red, and the README's CI badge, which reports `main`, then shows that red on the landing page. Run each gate below as its own command and read its result. Any failure stops the release until it is fixed and the gates run again, in a fresh `<scratch>`.

**Build**, 0 warnings (`TreatWarningsAsErrors` makes any warning a failure): `dotnet build Spektra.slnx -c Release`

**Tests**, all passing: `dotnet test --project tests/Spektra.Tests -c Release`. Run it as written: never add `--nologo`, which some .NET 10 SDK bands reject in the Microsoft.Testing.Platform mode that `global.json` selects.

**Format**, exit 0: `dotnet format Spektra.slnx --verify-no-changes`

**npm packaging tests**, all passing. These are `ci.yml`'s `Test npm packaging` commands (copy them from there if they have changed). `release.yml` only runs these scripts after the GitHub release exists, so a broken one would leave a release without its npm packages.

```bash
for f in build publish smoke lib/registry lib/targets; do node --check "packaging/npm/$f.mjs" || { echo "check failed for $f"; exit 1; }; done && node --test packaging/npm/test/shim.test.cjs packaging/npm/test/registry.test.mjs packaging/npm/test/targets.test.mjs
```

**Plugin validation**, both passing. The release also ships the Claude Code plugin, and the two runs check different things: from the repository root, the first checks `.claude-plugin/marketplace.json` and each plugin it lists, but never opens a skill file; the second checks the plugin itself, including its skills' frontmatter, and `--strict` fails on warnings too. If the `claude` command is not available, say so and ask the maintainer rather than skipping this gate.

```bash
claude plugin validate .
```

```bash
claude plugin validate plugins/spektra-cli --strict
```

**Vulnerable packages**, every project printing `has no vulnerable packages`: `dotnet list Spektra.slnx package --vulnerable --include-transitive`

**Workflow and packaging changes.** If `git diff --stat <prev-tag>..HEAD -- .github/workflows packaging` lists anything, do the dry run in [reference.md](reference.md) ("A dry run that publishes nothing") before tagging: those files only ever run for real inside the release job.

**Publish smokes.** These are `release.yml`'s `Publish GUI` and `Publish CLI` commands (if those steps have changed, copy them from there, since these must match). `TreatWarningsAsErrors` applies to publish too and CI only ever builds, so a publish-only failure would otherwise first show inside the release job, after the tag is public.

```bash
dotnet publish src/Spektra.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=X.Y.Z -o <scratch>/gui-win-x64
```

```bash
for rid in win-x64 linux-x64 osx-x64 osx-arm64; do dotnet publish src/Spektra.Cli -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=X.Y.Z -o "<scratch>/cli-$rid" || { echo "publish failed for $rid"; exit 1; }; done
```

They pass when both exit 0 and `ls <scratch>/gui-win-x64/spektra.exe <scratch>/cli-*/spektra-cli*` lists five files.

**The published CLI, in an isolated profile.** This proves the single-file binary really loads its native SQLite library. When that library is missing from the package (it has been, and a dependency update can cause it again) the CLI still runs, but prints `cache unavailable` and re-analyzes everything on every run. Set all three variables and create the two folders first: .NET resolves these folders from `USERPROFILE` when they exist and falls back to `APPDATA` and `LOCALAPPDATA` when they do not, so any one variable alone can still reach the real profile. `ffmpeg` must be on PATH, because the isolated profile hides the app's own ffmpeg download folder. Run it as one block:

```bash
S=<scratch>; P="$S/profile-cli"; CLI="$S/cli-win-x64/spektra-cli.exe"
mkdir -p "$P/AppData/Roaming" "$P/AppData/Local"
iso() { USERPROFILE="$P" APPDATA="$P/AppData/Roaming" LOCALAPPDATA="$P/AppData/Local" "$@"; }
iso "$CLI" --version
time iso "$CLI" audit tests/fixtures > "$S/audit-1.txt" 2> "$S/audit-1.err"; echo "exit $?"
time iso "$CLI" audit tests/fixtures > "$S/audit-2.txt" 2> "$S/audit-2.err"; echo "exit $?"
cat "$S/audit-1.err" "$S/audit-2.err"; diff "$S/audit-1.txt" "$S/audit-2.txt" && echo "same rows"
find "$P" -type f
```

It passes when `--version` prints `spektra-cli X.Y.Z`; both audits print `exit 1` (1 means findings, and the fixtures hold deliberate transcodes and corruption; 2 would be a setup error); neither `.err` file mentions `cache unavailable`; both runs print the same rows; the second run is clearly faster, because it decodes nothing; and the listing shows `AppData/Roaming/Spektra/audit-cache.db` inside the scratch profile, which also proves the run stayed out of the real one.

**The published GUI.** First, `tasklist //FI "IMAGENAME eq spektra.exe" //NH` must answer `INFO: No tasks are running which match the specified criteria.` Spektra keeps one instance per signed-in user, and the isolated profile does not change who that is: with any Spektra window open, the launch would hand the fixture to that window, in the maintainer's real session. The smoke script refuses to start in that case too; this check only stops earlier. If one is running, ask the maintainer to close it; never close or kill it yourself. Then open a fixture in the published app and capture its window. `-Profile` must be a new, empty folder: the script refuses one that holds files, because a reused profile restores the last run's window. The `spektra-gui-smoke` skill explains the script in full.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/gui-smoke.ps1 -Exe <scratch>/gui-win-x64/spektra.exe -AudioFile tests/fixtures/chirp.wav -Profile <scratch>/profile-gui -OutPng <scratch>/gui-smoke.png
```

It passes when the script exits 0 (the PNG is written and the app closed normally), prints its `capture:` line and lists the files the run wrote into the profile (a warning that nothing landed there means isolation was not proven), and the PNG shows the app's window with that file's spectrogram, not an error, a blank window or an ffmpeg prompt. The script waits 8 seconds after the window appears; on a slow machine raise that with `-SettleSeconds`. The status bar of the first load ends `Done in ... · N columns`: the analysis writes it when it finishes, and the automatic integrity check leaves it alone, since its result is the banner. Any other ending is a failure worth looking at.

**Install first.** If the release changes anything that only exists once Spektra is installed (`packaging/spektra.wxs`, registry entries, the PATH entry, the Explorer verbs and the command lines they pass to the app, installed files), the maintainer installs a locally built MSI over their current install and checks it before the tag. Start from `git diff --stat <prev-tag>..HEAD -- packaging/spektra.wxs src/Spektra.Core/LaunchArgs.cs src/Spektra.App/SingleInstance.cs` (the installer, the parsing of the verbs' command lines, and the handoff to a running window) and the change list. No test reaches what Windows Installer and the shell do: an Explorer verb that never opened a single file once shipped with a fully green suite behind it, and cost a follow-up release. Build the MSI and hand over the checks as in [reference.md](reference.md) ("Install first: build the MSI"), then wait for the maintainer's report before tagging. Never install it yourself: it changes the machine and the maintainer's real profile. When nothing installed changed, say so in one line and move on.

## 5. Commit

```bash
git add Directory.Build.props CHANGELOG.md plugins/spektra-cli/.claude-plugin/plugin.json
git commit -S -m "chore(release): X.Y.Z"
git log -1 --format='%H %G? %s'
git show --stat --format= HEAD
```

Subject only: no body and no attribution lines. `%G?` must print `G`, a good signature, and the stat must list exactly those three files. If signing fails, waits for a passphrase the tool cannot type, or `%G?` prints anything but `G`, stop and hand the maintainer the exact commands to run: nothing unsigned goes out, and `--no-gpg-sign` is never the answer. The full id printed is `<release-sha>`.

## 6. Tag

```bash
git tag -s vX.Y.Z -m "Spektra X.Y.Z" -m "<one highlights paragraph>"
git tag -v vX.Y.Z
git tag -l --format='%(contents:subject)' vX.Y.Z
git tag -l --format='%(contents:body)' vX.Y.Z
```

`git tag -v` must print a line starting with `Good`. The subject must print exactly `Spektra X.Y.Z` and the body the highlights paragraph, because that is what `release.yml` reads: it publishes the tag's body at the top of the release notes and drops the subject as a duplicate of the release title. With a single `-m` the highlights become the subject, and the release goes out with generated notes only. Write the paragraph for users, in plain words, telling the CHANGELOG entry's story in brief; `git tag -l --format='%(contents:body)' <prev-tag>` shows the voice. If the paragraph holds backticks, `$` or double quotes, do not pass it with `-m`: inside double quotes bash runs whatever is in backticks. Write the message to `<scratch>/tag-message.txt` instead (first line `Spektra X.Y.Z`, then a blank line, then the paragraph) and tag with `git tag -s vX.Y.Z -F <scratch>/tag-message.txt`, which gives the same subject and body. Until it is pushed, a wrong tag can be deleted (`git tag -d vX.Y.Z`) and made again. A pushed tag is never moved.

## 7. Push, only with the go-ahead

Everything so far is local. From here every step is public and permanent: the tag starts the release, and an npm version can never be reused. Go on only on a push, publish or ship request for this version, given in this conversation, as "Before you start" defines it. On any other request you stopped after step 6 at the latest.

`git rev-list --count <release-sha>..dev` decides the form: 0 means the release commit is `dev`'s tip, so use the commands below; anything else means unreleased commits sit above it, so use the stacked form further down. Run the commands one at a time, each only after the one before succeeded. First `dev`, then `main` (the chain stops at the first failure and prints its last line only when every step passed):

```bash
git push --no-follow-tags origin dev &&
  git fetch origin &&
  git merge-base --is-ancestor origin/main dev &&
  git push --no-follow-tags origin dev:main &&
  echo "dev and main are pushed"
```

Then the validation `release.yml` will run, which must end with `Release validation passed.`:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/validate-release.ps1 -Version X.Y.Z -Sha <release-sha>
```

Only then the tag, which starts the release:

```bash
git push origin vX.Y.Z
```

Last, bring the local `main` along. Nothing reads it, so if only this fails (for example because `main` is checked out in another worktree), the release is unaffected:

```bash
git fetch . dev:main
```

- `main` goes before the tag because `release.yml` refuses a tag whose commit is not on `origin/main` (the validation script's third check), and `main` is the landing page GitHub shows.
- Both `main` moves refuse anything but a fast-forward, so nothing is ever forced. Do not use `git branch -f`: permission checks have blocked it. If a permission check refuses any push here, do not reword the command to get past it; hand the maintainer the exact commands to run instead.
- `--no-follow-tags` keeps a `push.followTags` setting from sending the new tag along with a branch, ahead of `main`.
- The stacked form, for when unreleased commits sit above the release commit: `main` goes to the release commit, not to `dev`'s tip, or the landing page describes work no release contains. Replace the first command with this, and the last with `git fetch . <release-sha>:main` (a fetch refspec needs the full id; `git rev-parse "vX.Y.Z^{commit}"` prints it):

```bash
git push --no-follow-tags origin dev &&
  git fetch origin &&
  git merge-base --is-ancestor origin/main <release-sha> &&
  git push --no-follow-tags origin <release-sha>:refs/heads/main &&
  echo "dev and main are pushed"
```

- If the chain stops at the ancestry check, force nothing. If the release commit is already on `origin/main` (`git merge-base --is-ancestor <release-sha> origin/main && echo "already on main"`), `main` needs no move; otherwise stop and ask the maintainer.
- Never run `gh release create`: the workflow creates the release, and a manual one collides with it.

## 8. Watch and verify

```bash
gh run list --workflow release.yml --limit 1 --json databaseId,headBranch --jq '.[0] | "\(.databaseId) \(.headBranch)"'
gh run watch <run-id> --exit-status
```

The listed run must name `vX.Y.Z`; a run listed before GitHub has registered the push is the previous release, so list again after a few seconds. The run (the release job, then an npm install smoke on four platforms) lasts longer than a foreground command may wait, so watch it in the background. If the watch dies, the run does not: list it and watch again.

When it is green, check what users get:

- `gh release view vX.Y.Z`: titled `Spektra X.Y.Z`, notes opening with the tag body, and exactly eight assets: `Spektra-X.Y.Z-Setup.msi`, `Spektra-X.Y.Z-Setup.zip`, `Spektra-X.Y.Z-win-x64.zip`, `spektra-cli-X.Y.Z-win-x64.zip`, `spektra-cli-X.Y.Z-linux-x64.zip`, `spektra-cli-X.Y.Z-osx-x64.zip`, `spektra-cli-X.Y.Z-osx-arm64.zip` and `SHA256SUMS.txt`.
- `gh release list --limit 3 --json tagName,isLatest`: `vX.Y.Z` has `isLatest` true.
- `gh release download vX.Y.Z --pattern SHA256SUMS.txt --output -`: one line for each of the seven other assets and nothing else. A line for a file the release does not carry makes `sha256sum -c` fail for every user who checks.
- npm, where every package must print `X.Y.Z`:

```bash
for p in spektra-cli @rarepops/spektra-cli-win32-x64 @rarepops/spektra-cli-linux-x64-glibc @rarepops/spektra-cli-darwin-x64 @rarepops/spektra-cli-darwin-arm64; do echo "$p $(npm view "$p" version)"; done
```

- CI is green on both branches: `gh run list --workflow ci.yml --branch dev --limit 1` and `gh run list --workflow ci.yml --branch main --limit 1`.

A transient HTTP 5xx from the GitHub API while checking is not a failed run: query again. If the run itself failed, see [reference.md](reference.md) ("When the release run fails").

Finish with a short report to the maintainer: the version and why, the run id and its result, the asset, checksum and npm checks, CI on both branches, and anything skipped or traded, with who decided it.
