# Publishing spektra-cli to npm

Five packages go out together: the dispatcher `spektra-cli`, and one binary
package per platform (`spektra-cli-win32-x64`, `spektra-cli-linux-x64`,
`spektra-cli-darwin-x64`, `spektra-cli-darwin-arm64`). The bare name `spektra`
belongs to an unrelated project and is never used.

Releases publish themselves from `.github/workflows/release.yml` using npm
**trusted publishing**: npm exchanges the workflow's OIDC token for a
short-lived credential and attaches provenance itself. There is no publish
token anywhere, in a secret or otherwise.

Three things have to be set up by hand first, in this order. Doing them out of
order leaves the release job unable to publish.

---

## 1. Bootstrap: claim the five names (one time)

Trusted publishing is configured per package, and a package has to exist
before it can be configured, so the first publish is a manual one.

Check the names are still free. Each should answer `E404`:

```sh
npm view spektra-cli
npm view spektra-cli-win32-x64
npm view spektra-cli-linux-x64
npm view spektra-cli-darwin-x64
npm view spektra-cli-darwin-arm64
```

Build all five from one set of release binaries. Publish every runtime
identifier first, from the same source tree, because `dist/` is gitignored and
keeps whatever was last built: the assembler ignores a binary older than the
newest change under `src/`, and refuses to produce an incomplete set.

```sh
./tools/validate-release.ps1 -Version <X.Y.Z>

foreach ($rid in 'win-x64','linux-x64','osx-x64','osx-arm64') {
  dotnet publish src/Spektra.Cli -c Release -r $rid --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -p:Version=<X.Y.Z> -o "dist/cli-$rid"
}

node packaging/npm/build.mjs --version <X.Y.Z>
node packaging/npm/publish.mjs --dry-run
```

Then log in and publish for real:

```sh
npm login --auth-type=web
node packaging/npm/publish.mjs
```

`publish.mjs` packs each package into a tarball, publishes that exact file,
and does the four platform packages before the dispatcher.

It takes no `--provenance` flag, deliberately. Provenance attests to a build
that a workflow run can vouch for, and this one is a laptop, so the bootstrap
publish goes out without it; every publish after this gets provenance
automatically from trusted publishing, with no flag involved.

## 2. Configure the trusted publisher (one time, per package)

On npmjs.com, for **each of the five packages**, Settings to Trusted Publisher:

| Field | Value |
| --- | --- |
| Publisher | GitHub Actions |
| Organization or user | `rarepops` |
| Repository | `Spektra` |
| Workflow filename | `release.yml` |
| Environment | leave blank |
| Allowed action | `npm publish` |

Leave Environment blank unless a GitHub environment of that name is actually
added to the workflow job; a value here that does not match one on the job
makes every publish fail.

## 3. Require 2FA and disallow token bypass (one time, per package)

```sh
npm access set mfa=publish spektra-cli
npm access set mfa=publish spektra-cli-win32-x64
npm access set mfa=publish spektra-cli-linux-x64
npm access set mfa=publish spektra-cli-darwin-x64
npm access set mfa=publish spektra-cli-darwin-arm64
```

Verify on each package's Settings page that it now reads **"Require
two-factor authentication and disallow bypass 2FA tokens"**. Trusted
publishing keeps working under that setting: OIDC is not a bypass token, which
is the point of using it.

Do this **after** step 2. With 2FA required and no trusted publisher
configured, nothing can publish at all.

---

## Every release after that

Pushing a `vX.Y.Z` tag runs `release.yml`, which:

1. Validates the tag (`tools/validate-release.ps1`): a bare X.Y.Z, matching
   `Directory.Build.props`, on `main`.
2. Builds the installer, the archives and the CLI for all four runtime
   identifiers, and creates the GitHub release.
3. Assembles the npm packages (`build.mjs`), which also checks that the
   Windows binary reports the version being stamped.
4. Publishes them (`publish.mjs`) through trusted publishing.
5. Runs `npm-smoke.yml`: installs the published version on Windows x64, Linux
   x64, macOS x64 and macOS arm64, and proves the right binary came with it
   and reports the right version.

npm comes after the GitHub release deliberately: the release page is the shop
window, and a problem with a second distribution channel must not be able to
stop it appearing.

A manual `workflow_dispatch` run publishes nothing. It packs the same tarballs
and uploads them with their integrity strings as workflow artifacts, so the
packaging can be inspected without a tag.

## When something goes wrong

**A publish failed part way through.** Re-run it. `publish.mjs` compares each
tarball against the registry's recorded integrity and skips only what is
already published with those exact bytes, so a re-run finishes the job.

**"already on the registry with different contents".** A version that is
public is never overwritten; release the fix as the next version. Note that npm
does not guarantee a byte-identical tarball across npm versions, so this can
also mean "same files, different packer": compare the file lists before
concluding the code differs.

**403 or 404 on publish, from CI.** The trusted publisher is not configured
for that package, or one of its fields does not match. Check the workflow
filename is exactly `release.yml` and the Environment field is blank.

**Smoke-testing a version by hand.** Run the `npm-smoke` workflow with a
version, or locally:

```sh
node packaging/npm/smoke.mjs --version <X.Y.Z>
```
