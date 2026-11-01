# Installs the pinned ffmpeg build into %LOCALAPPDATA%\Spektra\ffmpeg (same
# location the in-app downloader uses). Not needed if ffmpeg is on PATH.
# gyan.dev drops old versioned builds when ffmpeg moves on, so this falls back
# to the always-current build when the pinned one is gone or its hash changes,
# mirroring FfmpegDownloader. The fallback warns with the reason.
#
# Every download is tried 3 times, waiting 10 s and then 30 s between tries, so
# an outage on gyan.dev that clears within about 40 s (a brief 503) does not by
# itself decide which build gets installed.
#
#   -RequirePinned  never fall back: stop with the reason instead of installing
#                   a build that is not integrity-pinned. CI passes it, because
#                   its ffmpeg cache is keyed on this script and must only ever
#                   hold the pinned build.
#   -RecordHash     download the pinned build, print its SHA-256 and exit, to
#                   mint $Sha256 below.
#
# Re-pin on a convenient release: run with -RecordHash and update $Url/$Sha256
# here and PinnedUrl/PinnedSha256 in src/Spektra.App/FfmpegDownloader.cs.
param([switch]$RecordHash, [switch]$RequirePinned)

$ErrorActionPreference = "Stop"
$Url = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip"
$LatestUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
$Sha256 = "db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec"

$dest = Join-Path $env:LOCALAPPDATA "Spektra\ffmpeg"
$zip  = Join-Path $env:TEMP "spektra-ffmpeg.zip"

# One download with the retries described above. The last failure is rethrown,
# so the caller decides what it means.
function Save-Download([string]$Uri, [string]$OutFile) {
    $tries = 3
    $waits = 10, 30
    for ($n = 1; $n -le $tries; $n++) {
        try {
            Invoke-WebRequest -Uri $Uri -OutFile $OutFile
            return
        } catch {
            Write-Warning "Download failed (attempt $n of $tries): ${Uri}: $($_.Exception.Message)"
            if ($n -eq $tries) { throw }
            Start-Sleep -Seconds $waits[$n - 1]
        }
    }
}

# Record mode: hash the pinned build and exit (used to mint $Sha256 above).
if ($RecordHash) {
    Save-Download $Url $zip
    Write-Host ("SHA256: " + (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())
    exit 0
}

# Try the pinned build and keep the reason when it cannot be used: it is gone
# (the last download error) or its bytes no longer match the pin.
$why = $null
try { Save-Download $Url $zip } catch { $why = $_.Exception.Message }
if (-not $why) {
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256) { $why = "hash mismatch (expected $Sha256, got $actual)" }
}
if ($why) {
    if ($RequirePinned) {
        throw "Pinned ffmpeg build unusable and -RequirePinned refuses the latest build: $why`nIf gyan.dev no longer serves it, re-pin: run tools/get-ffmpeg.ps1 -RecordHash, then update the URL and hash in that script and in src/Spektra.App/FfmpegDownloader.cs."
    }
    Write-Warning "Pinned ffmpeg build unusable, falling back to the latest build: $why"
    Save-Download $LatestUrl $zip
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
