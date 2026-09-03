<#
.SYNOPSIS
    Checks that a version tag is safe to publish from, before anything is built.

.DESCRIPTION
    A published npm version is permanent and a GitHub release is public, so the
    three things that must be true about a tag are checked first:

      1. The version is a bare X.Y.Z. No prerelease, no build metadata, no
         leading zero, because that is all npm will ever be given.
      2. Directory.Build.props carries the same version, so the tag, the
         assemblies, the installer and the npm packages cannot disagree.
      3. The tagged commit is on main. main is the shop window (the README
         GitHub renders) and the release recipe pushes main before the tag, so
         a tag that is not on main means the recipe was not followed and the
         release page would describe code the default branch does not have.

    Run by release.yml on a tag, and runnable by hand before tagging:

        ./tools/validate-release.ps1 -Version 0.25.0
        ./tools/validate-release.ps1 -Version 0.25.0 -Sha (git rev-parse HEAD)

    Omit -Sha to skip the main check (nothing is tagged yet).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Version,
    [string] $Sha,
    [string] $Branch = 'main'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "'$Version' is not a release version. A release is a bare X.Y.Z: no prerelease, no build metadata, no leading zeros."
}

$propsPath = Join-Path $repo 'Directory.Build.props'
$props = Get-Content $propsPath -Raw
if ($props -notmatch '<Version>\s*([^<]+?)\s*</Version>') {
    throw "no <Version> element in $propsPath"
}
$declared = $Matches[1]
if ($declared -ne $Version) {
    throw "the tag says $Version but Directory.Build.props says $declared. Bump the props file, or tag the right commit."
}

if ($Sha) {
    # Fetch the branch explicitly rather than trusting whatever refs the
    # checkout happened to leave behind.
    git -C $repo fetch --no-tags --quiet origin "+refs/heads/${Branch}:refs/remotes/origin/${Branch}"
    if ($LASTEXITCODE -ne 0) { throw "could not fetch origin/$Branch" }

    git -C $repo merge-base --is-ancestor $Sha "refs/remotes/origin/$Branch"
    if ($LASTEXITCODE -ne 0) {
        throw "commit $Sha is not on $Branch. Push $Branch to the release commit before pushing the tag."
    }
    Write-Host "  on ${Branch}: yes"
}
else {
    Write-Host "  on ${Branch}: not checked (no -Sha given)"
}

Write-Host "  version: $Version, matching Directory.Build.props"
Write-Host "Release validation passed."
