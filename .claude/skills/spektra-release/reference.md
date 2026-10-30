# spektra-release: the rarer paths

Open the section you need. Placeholders mean what they mean in `SKILL.md`.

## Install first: build the MSI

`packaging/spektra.wxs` takes its two binaries from `dist/gui-win-x64` and `dist/cli-win-x64` (gitignored) and stamps the installer with `spektra.exe`'s four-part file version, so this is the one build that goes into the checkout. If either folder already exists, ask before replacing it: it may hold the maintainer's own build. Copy the smoke-tested win-x64 publishes there and run `release.yml`'s own WiX command:

```bash
mkdir -p dist/gui-win-x64 dist/cli-win-x64 dist/msi-X.Y.Z
cp <scratch>/gui-win-x64/spektra.exe dist/gui-win-x64/
cp <scratch>/cli-win-x64/spektra-cli.exe dist/cli-win-x64/
wix build packaging/spektra.wxs -arch x64 -pdbtype none -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -o dist/msi-X.Y.Z/Spektra-X.Y.Z-Setup.msi
ls dist/msi-X.Y.Z
```

`wix` and its two extensions must be the versions that `release.yml`'s `Build MSI installer` step pins (compare `wix --version` and `wix extension list -g`), or the installer tested here is not the one CI builds. If they are missing or differ, ask before installing tools on the maintainer's machine.

Give the maintainer the MSI's path and these checks. Each one catches something nothing else does:

1. Install it over the current version. The Options page's `Add Spektra to my PATH` and `Add Spektra to the Explorer right-click menu` start from what is installed now; the Explorer one is off unless chosen, so tick it when the release touches the verbs. Installed apps then lists Spektra `X.Y.Z.0` (the installer carries the exe's four-part version), once. Catches a broken upgrade or version stamp.
2. With the PATH box ticked, in a new terminal, `where.exe spektra spektra-cli` finds both in the install folder, and `spektra-cli --version` prints `spektra-cli X.Y.Z`. Catches the PATH entry and the installed CLI.
3. The changed installed-only behaviour works from its real entry point (for an Explorer verb: right-click a file or folder and choose it). Catches what only the shell runs.
4. Their settings, palettes and analysis cache in `%APPDATA%\Spektra`, and the app's downloaded ffmpeg in `%LOCALAPPDATA%\Spektra\ffmpeg`, survived the upgrade. Catches uninstall-time cleanup firing during an upgrade.

Tag only after the maintainer reports back. If they choose to tag without installing, that is their call: record it in the final report. Then remove what this section put in the checkout (`rm -r dist/gui-win-x64 dist/cli-win-x64 dist/msi-X.Y.Z`): fresh copies left there would pass the npm assembler's staleness check on a later hand-run.

The local MSI carries the release's own version, and under `MajorUpgrade`'s defaults a same-version install is not an upgrade: Windows Installer would keep both as two separate products. To move that machine onto the official `X.Y.Z` build, uninstall the local one first, leaving `Also remove settings, palettes, and the analysis cache` unticked; otherwise the next release upgrades it normally.

## Two releases in one session

When two versions are ready at once (say a patch, with the next minor already committed above it on `dev`), run steps 7 and 8 completely for the older one first, moving `main` with step 7's stacked form (to the older release commit), and only then for the newer one. The release created last is the one GitHub marks Latest, and `main` steps through each release in turn.

## A dry run that publishes nothing

`gh workflow run release.yml --ref dev` builds everything a tag would, stamped `0.0.0-dev` unless `-f version=...` says otherwise, but skips the validation, the GitHub release and the npm publish, and uploads the artifacts and the packed npm tarballs for inspection instead. It runs what is on `origin/dev`, so the workflow and packaging changes must be pushed there first, and that push needs the maintainer's go-ahead like any other. The run itself publishes nothing, but it is still an action on the public repository, so ask before starting it too.

Find it with `gh run list --workflow release.yml --limit 1`, watch it with `gh run watch <run-id> --exit-status`, and fetch its output with `gh run download <run-id> --dir <scratch>/dry-run` (one folder per artifact). It passes when the run is green, the `spektra-<version>` artifact holds the eight files step 8 lists (named for the dry-run version), and `spektra-npm-<version>` holds five `.tgz` files and an `INTEGRITY.txt` of five lines.

## When the release run fails

Read the failed step first: `gh run view <run-id> --log-failed`.

- **Before `Create GitHub release`** (validation, a publish, WiX, packaging): nothing is public except the tag. If the cause lies outside the tagged commit (`main` not pushed yet, a runner or network hiccup), fix that and run `gh run rerun <run-id>`, which builds the same tag again. If the tagged commit itself is wrong, ask the maintainer; the usual answer is the next patch version, because a pushed tag is never moved.
- **Inside `Create GitHub release`, or in the npm steps before any tarball was packed** (the release has missing assets, or the run kept no `spektra-npm-X.Y.Z` artifact): stop and ask the maintainer. Nothing here can be finished by hand, and a re-run rebuilds binaries that differ from what is already public; with nothing on npm, the usual answer is the next patch version.
- **After `Create GitHub release`, with the npm tarballs kept**: the release page is complete and stays. Never re-run the job: it would rebuild the binaries, .NET single-file output is never byte-identical, and the npm publisher would then refuse every package that had already landed, leaving a new version number as the only way out. Recover npm as in the next section.
- **Only the `smoke` job**: the packages are published. `packaging/npm/PUBLISHING.md` ("When something goes wrong") explains each failure, and `node packaging/npm/smoke.mjs --version X.Y.Z` repeats the check on this machine. That file's "Re-run it" advice is for a publish run by hand; for the release job it means the next section, never a re-run.

A run recovered by hand stays red, and so does the README's `Release build` badge until the next tag. That is expected and no reason to re-run.

## Recovering a failed npm publish

`Publish to npm` failed and the GitHub release exists. The run kept the exact tarballs it packed (`Upload npm packages` runs even after a failed publish), and those, not a fresh build, are what gets published by hand.

1. Why it failed: `gh run view <run-id> --log-failed`. A 403 or 404 from CI means that package's trusted publisher on npmjs.com is missing or wrong, which the maintainer fixes first. "npm accepted ... and is still not serving it" is not fixed by publishing again: `packaging/npm/PUBLISHING.md` describes the new-package-name route, and taking it is the maintainer's decision.
2. What landed. A package that has the version prints its integrity; one that does not answers with an `E404` error (`No match found for version X.Y.Z`) and an empty integrity:

```bash
for p in spektra-cli @rarepops/spektra-cli-win32-x64 @rarepops/spektra-cli-linux-x64-glibc @rarepops/spektra-cli-darwin-x64 @rarepops/spektra-cli-darwin-arm64; do echo "$p $(npm view "$p@X.Y.Z" dist.integrity)"; done
```

3. The run's tarballs: `gh run download <run-id> --name spektra-npm-X.Y.Z --dir <scratch>/npm`. Each line of `INTEGRITY.txt` is an integrity and a `<tarball>` file name, the four platform packages first and the dispatcher last. The `<package>` behind a tarball: `rarepops-spektra-cli-<platform>-X.Y.Z.tgz` is `@rarepops/spektra-cli-<platform>`, and `spektra-cli-X.Y.Z.tgz` is `spektra-cli`. Every package that already landed must show the same integrity on the registry as its line; if one does not, stop and ask. Before handing a tarball over, check its bytes against its line, since a damaged file would burn the version: this must print exactly that line's integrity.

```bash
node -e "console.log('sha512-'+require('crypto').createHash('sha512').update(require('fs').readFileSync(process.argv[1])).digest('base64'))" <scratch>/npm/<tarball>
```

4. The maintainer publishes each missing tarball, in `INTEGRITY.txt` order, from a real terminal: npm's login with a security key is an interactive browser ceremony that fails in a tool-run shell, and it needs npm 11.5.1 or newer. Hand them these commands, the last two once per missing tarball:

```bash
npm login --auth-type=web
cd <scratch>/npm
npm publish <tarball> --access public
npm view <package>@X.Y.Z dist.integrity
```

After each publish, `npm view` must print that tarball's `INTEGRITY.txt` value before the next one goes out. It can take minutes to appear, so wait rather than publish again: npm can accept a publish and never serve it, and a dispatcher that goes out before its platform packages are served installs a tool that cannot run. Packages published by hand carry no provenance attestation.

5. The `smoke` job never ran, because it needs the release job. Start it with `gh workflow run npm-smoke.yml -f version=X.Y.Z`, find it with `gh run list --workflow npm-smoke.yml --limit 1`, and `gh run watch <run-id> --exit-status`.

`node packaging/npm/publish.mjs` is the hand-run publisher for packages assembled on this machine (the first-time bootstrap in `PUBLISHING.md`), and running it again finishes its own half-finished publish, because it skips whatever the registry already holds with the same bytes. It cannot finish a CI publish: it packs from locally built binaries, which never match the bytes CI already published, so it refuses those packages. Even when nothing landed, the run's tarballs are the better source, since they hold the same binaries as the release's zips.
