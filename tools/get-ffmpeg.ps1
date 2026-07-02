# Installs a pinned ffmpeg build into %LOCALAPPDATA%\Spektra\ffmpeg (same
# location the in-app downloader uses). Not needed if ffmpeg is on PATH.
param([switch]$RecordHash)

$ErrorActionPreference = "Stop"
$Url = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-7.1.1-essentials_build.zip"
# Trust-on-first-use: run with -RecordHash once, paste the value below AND into
# FfmpegDownloader.PinnedSha256 in src/Spektra.App/FfmpegDownloader.cs.
$Sha256 = ""

$dest = Join-Path $env:LOCALAPPDATA "Spektra\ffmpeg"
$zip  = Join-Path $env:TEMP "spektra-ffmpeg.zip"

Invoke-WebRequest -Uri $Url -OutFile $zip
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($RecordHash) { Write-Host "SHA256: $actual"; exit 0 }
if ($Sha256 -and $actual -ne $Sha256) { throw "ffmpeg zip hash mismatch: $actual" }
if (-not $Sha256) { Write-Warning "No pinned hash yet; run with -RecordHash and pin it." }

$extract = Join-Path $env:TEMP "spektra-ffmpeg-extract"
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive $zip $extract
$bin = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $bin.DirectoryName "ffmpeg.exe") $dest -Force
Copy-Item (Join-Path $bin.DirectoryName "ffprobe.exe") $dest -Force
Write-Host "Installed to $dest"
