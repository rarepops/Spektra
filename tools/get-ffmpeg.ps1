# Installs the pinned ffmpeg build into %LOCALAPPDATA%\Spektra\ffmpeg (same
# location the in-app downloader uses). Not needed if ffmpeg is on PATH.
# gyan.dev drops old versioned builds when ffmpeg moves on, so this falls back
# to the always-current build when the pinned one is gone or its hash changes,
# mirroring FfmpegDownloader. Re-pin on a convenient release: run with
# -RecordHash and update $Url/$Sha256 here and PinnedUrl/PinnedSha256 in
# src/Spektra.App/FfmpegDownloader.cs.
param([switch]$RecordHash)

$ErrorActionPreference = "Stop"
$Url = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip"
$LatestUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
$Sha256 = "db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec"

$dest = Join-Path $env:LOCALAPPDATA "Spektra\ffmpeg"
$zip  = Join-Path $env:TEMP "spektra-ffmpeg.zip"

# Record mode: hash the pinned build and exit (used to mint $Sha256 above).
if ($RecordHash) {
    Invoke-WebRequest -Uri $Url -OutFile $zip
    Write-Host ("SHA256: " + (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())
    exit 0
}

# Try the pinned build; fall back to the current build if it is gone or changed.
$pinned = $true
try { Invoke-WebRequest -Uri $Url -OutFile $zip } catch { $pinned = $false }
if ($pinned) {
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256) {
        Write-Warning "Pinned ffmpeg hash mismatch ($actual); using the latest build instead."
        $pinned = $false
    }
}
if (-not $pinned) {
    Invoke-WebRequest -Uri $LatestUrl -OutFile $zip
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Warning "Using the latest ffmpeg build, not integrity-pinned; SHA-256 $actual."
}

$extract = Join-Path $env:TEMP "spektra-ffmpeg-extract"
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive $zip $extract
$bin = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $bin.DirectoryName "ffmpeg.exe") $dest -Force
Copy-Item (Join-Path $bin.DirectoryName "ffprobe.exe") $dest -Force
Write-Host "Installed to $dest"
