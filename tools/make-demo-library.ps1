<#
.SYNOPSIS
Builds the synthetic music library used for the README screenshots.

.DESCRIPTION
Generates a small fake library whose audit verdicts are real. Nothing here is
staged: the file that shows red is genuinely a transcode, the one that shows
violet genuinely holds 44.1 kHz of content in a 96 kHz container. The point is
that a screenshot of Spektra should be a screenshot of Spektra working.

The audio is never committed (a few hundred megabytes); this script is, so the
set can be rebuilt whenever the UI changes. Requires ffmpeg, on PATH or in
%LOCALAPPDATA%\Spektra\ffmpeg, exactly like tools/make-fixtures.ps1.

Roughly 250 MB and about seven minutes, most of it evaluating the synthesis
expression.

.PARAMETER OutRoot
Where the library goes. The default is deliberately short and impersonal
because these paths end up visible in published screenshots: the Duplicate
Detective window lists its scan root and the tab strip shows folder names, so a
library under a user profile would publish a username to the README.

.PARAMETER KeepSources
Keep the intermediate source WAVs. Off by default; they are large and only
useful when debugging the synthesis.
#>
[CmdletBinding()]
param(
  [string]$OutRoot = "D:\SpektraDemo",
  [switch]$KeepSources
)

$ErrorActionPreference = "Stop"

$ff = "ffmpeg"
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
  $ff = Join-Path $env:LOCALAPPDATA "Spektra\ffmpeg\ffmpeg.exe"
  if (-not (Test-Path $ff)) { throw "ffmpeg not found on PATH or in %LOCALAPPDATA%\Spektra\ffmpeg" }
}

# The root folder carries the "this is demo content" signal, because it is what
# the tab strip and window title show. That lets the album folders drop a
# repeated artist prefix which otherwise eats the grid's File column and
# truncates every track name in the screenshots.
$lib = Join-Path $OutRoot "Spektra Demo Library"
$src = Join-Path $OutRoot ".sources"
New-Item -ItemType Directory -Force $lib | Out-Null
New-Item -ItemType Directory -Force $src | Out-Null

# ---------------------------------------------------------------- synthesis --

# A harmonic stack whose every partial breathes on its own slow LFO. The
# separate LFO per partial is what makes the low bands shimmer rather than sit
# as flat rails, and the periods are mutually prime-ish so the pattern does not
# visibly repeat.
function Get-Drone {
  param([string]$F, [double[]]$Amps, [double[]]$Periods, [double]$Phase = 0)
  $terms = for ($i = 0; $i -lt $Amps.Length; $i++) {
    $n = $i + 1
    $p = $Periods[$i % $Periods.Length]
    "$($Amps[$i])*(0.5+0.5*sin(2*PI*$p*t+$i))*sin(2*PI*$n*($F)*t+$Phase)"
  }
  $terms -join "+"
}

# Everything moves on 3-40 second timescales on purpose. At four minutes across
# roughly 1300 pixels one pixel is 167 ms, so anything faster than about one
# event every two seconds aliases into flat texture instead of reading as
# structure. That constraint is why this sounds like ambient music.
function New-Source {
  param(
    [string]$Path,
    [double]$Root = 55,
    [double]$Mel = 440,
    [double]$ChordSecs = 8,
    [double]$MelSecs = 4,
    [double]$PercSecs = 3,
    # Interval sizes in semitones. These are what make two tracks genuinely
    # different songs rather than the same song transposed: the fingerprint
    # compares pitch classes, so varying only Root and Mel leaves every track
    # chromatically similar and Duplicate Detective pairs unrelated ones.
    [double]$ChordStep = 3,
    [double]$MelStep = 2,
    [int]$Rate = 44100,
    [int]$Seconds = 90
  )

  # 0.0578 is ln(2)/12, one semitone.
  $chord = "floor(t/$ChordSecs - 3*floor(t/$($ChordSecs*3)))"
  $rootF = "$Root*exp($(0.0578 * $ChordStep)*$chord)"
  $midF  = "$($Root*4)*exp($(0.0578 * $ChordStep)*$chord)"
  $mstep = "floor(t/$MelSecs - 8*floor(t/$($MelSecs*8)))"
  $melF  = "$Mel*exp($(0.0578 * $MelStep)*$mstep)"

  $L = (Get-Drone $rootF @(0.26,0.15,0.10,0.07,0.05,0.035,0.025,0.018) @(0.031,0.017,0.023,0.013,0.029,0.011,0.019,0.037)),
       (Get-Drone $midF  @(0.09,0.055,0.035,0.022,0.015,0.010) @(0.041,0.019,0.028,0.014,0.036,0.022)),
       (Get-Drone $melF  @(0.10,0.055,0.032,0.020,0.013,0.008,0.005) @(0.051,0.033,0.044,0.027,0.038,0.021,0.047))
  $R = (Get-Drone $rootF @(0.26,0.14,0.11,0.06,0.05,0.030,0.028,0.016) @(0.027,0.021,0.015,0.033,0.012,0.025,0.035,0.014) 0.4),
       (Get-Drone $midF  @(0.09,0.050,0.038,0.020,0.017,0.009) @(0.037,0.024,0.016,0.031,0.013,0.026) 0.6),
       (Get-Drone $melF  @(0.10,0.050,0.035,0.018,0.014,0.007,0.006) @(0.047,0.036,0.029,0.042,0.024,0.033,0.019) 0.35)
  $exprL = $L -join "+"
  $exprR = $R -join "+"

  # Two slow LFOs multiplied, so the top end is quiet most of the time and peaks
  # only when both align. That dynamic range is what makes a cutoff legible: a
  # constant air layer fills everything below the wall evenly and the wall reads
  # as the top of a solid block, whereas real music rises and falls, so the
  # encoder's low-pass shows up as stretches clipped dead flat against stretches
  # that fall away naturally. Peak-hold still sees the peaks, so the verdicts do
  # not move.
  $breathe = "(0.04+0.96*(0.5+0.5*sin(2*PI*0.023*t))*(0.5+0.5*sin(2*PI*0.014*t)))"
  $hit     = "exp(-9*(t - $PercSecs*floor(t/$PercSecs)))"

  # The air layer exists so the analyzer sees real content all the way to
  # Nyquist; without it a lossless file reads as "natural high-frequency
  # rolloff" and the whole demo turns ambiguous. Its level was found by sweep,
  # not taste: at -52 dBFS the verdict degrades to "could be lossy", at -42 it
  # reads full-band and still survives a 320k encode. Confining it above 11 kHz
  # keeps the musical region clean instead of washing the entire plot.
  $fc = "[0:a]volume=0.75[tone];" +
        "[1:a]highpass=f=11000,highpass=f=11000,volume=volume='$breathe':eval=frame,volume=0.011[air];" +
        "[2:a]lowpass=f=7000,highpass=f=900,volume=volume='$hit':eval=frame,volume=0.05[perc];" +
        # alimiter's auto-gain (level) defaults ON and would normalise this
        # whole quiet mix back up, undoing every level above. Safety only.
        "[tone][air][perc]amix=inputs=3:normalize=0,alimiter=limit=0.95:level=0[out]"

  & $ff -y -v error `
    -f lavfi -i "aevalsrc=$exprL|${exprR}:s=$Rate`:c=stereo:d=$Seconds" `
    -f lavfi -i "anoisesrc=color=white:sample_rate=$Rate`:duration=$Seconds`:amplitude=1.0" `
    -f lavfi -i "anoisesrc=color=white:sample_rate=$Rate`:duration=$Seconds`:amplitude=1.0" `
    -filter_complex $fc -map "[out]" -ac 2 -c:a pcm_s16le $Path
  if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed generating $Path" }
}

# ------------------------------------------------------------------ tracks ---

# Each entry varies root, melody, rhythm and interval sizes so no two
# spectrograms look alike and no two tracks fingerprint alike.
$tracks = @(
  @{ Name = "ascent";   Root = 55;    Mel = 440; Chord = 8;  Melo = 4;   Perc = 3;   CStep = 3; MStep = 2; Secs = 75 }
  @{ Name = "meridian"; Root = 61.7;  Mel = 494; Chord = 10; Melo = 5;   Perc = 4;   CStep = 4; MStep = 3; Secs = 90 }
  @{ Name = "nightfall";Root = 49;    Mel = 392; Chord = 12; Melo = 6;   Perc = 3.5; CStep = 5; MStep = 2; Secs = 180 }
  @{ Name = "undertow"; Root = 65.4;  Mel = 523; Chord = 7;  Melo = 3.5; Perc = 2.5; CStep = 2; MStep = 4; Secs = 65 }
  @{ Name = "halcyon";  Root = 58.3;  Mel = 466; Chord = 9;  Melo = 4.5; Perc = 5;   CStep = 7; MStep = 3; Secs = 80 }
  @{ Name = "lantern";  Root = 73.4;  Mel = 587; Chord = 6;  Melo = 3;   Perc = 3;   CStep = 3; MStep = 5; Secs = 70 }
  @{ Name = "ember";    Root = 46.2;  Mel = 370; Chord = 11; Melo = 5.5; Perc = 4;   CStep = 4; MStep = 4; Secs = 60 }
  @{ Name = "drift";    Root = 51.9;  Mel = 415; Chord = 9;  Melo = 4;   Perc = 3;   CStep = 2; MStep = 2; Secs = 70 }
)

Write-Host "Synthesising sources (the slow part)..." -ForegroundColor Cyan
foreach ($t in $tracks) {
  $p = Join-Path $src "$($t.Name).wav"
  if (Test-Path $p) { Write-Host "  $($t.Name) (cached)"; continue }
  Write-Host "  $($t.Name) [$($t.Secs)s]"
  New-Source -Path $p -Root $t.Root -Mel $t.Mel -ChordSecs $t.Chord -MelSecs $t.Melo `
    -PercSecs $t.Perc -ChordStep $t.CStep -MelStep $t.MStep -Seconds $t.Secs
}

# Parallax is genuinely 96 kHz: synthesised at that rate, so its noise really
# does reach past 22 kHz. That is the only thing separating it from Drift, which
# is the same idea resampled up from 44.1 and must therefore read as Upsampled.
#
# Its intervals are deliberately far from Drift's. The two share an album and
# neither has a real duplicate anywhere, so if they resemble each other at all
# they pair up with nothing to outrank them: a first pass had them grouped as
# duplicates at 0.41 sameness, which would have put a false match in the README.
$parallax = Join-Path $src "parallax96.wav"
if (-not (Test-Path $parallax)) {
  Write-Host "  parallax [60s @ 96 kHz]"
  New-Source -Path $parallax -Root 43.7 -Mel 349 -ChordSecs 10 -MelSecs 5 -PercSecs 4 `
    -ChordStep 7 -MelStep 5 -Rate 96000 -Seconds 60
}

# ------------------------------------------------------------------ albums ---

# Kept short on purpose: every one of these prefixes the relative path in the
# audit grid's File column, and the whole grid has to fit a capture window.
$flacA = Join-Path $lib "Spectral Suite [FLAC]"
$mp3A  = Join-Path $lib "Spectral Suite [MP3]"
$hires = Join-Path $lib "Night Sessions [24-96]"
$field = Join-Path $lib "Field Notes"
$flacA, $mp3A, $hires, $field | ForEach-Object { New-Item -ItemType Directory -Force $_ | Out-Null }

function S([string]$n) { Join-Path $src "$n.wav" }

Write-Host "Encoding albums..." -ForegroundColor Cyan

# Spectral Suite [FLAC]: three honest lossless tracks and one real transcode.
& $ff -y -v error -i (S "ascent")   -c:a flac (Join-Path $flacA "01 - Ascent.flac")
& $ff -y -v error -i (S "meridian") -c:a flac (Join-Path $flacA "02 - Meridian.flac")
# Nightfall is the hero: encoded to 128k and back into FLAC, so its brick wall
# at ~16.5 kHz is real and the banner and the picture corroborate each other.
$tmp128 = Join-Path $src "nightfall-128.mp3"
& $ff -y -v error -i (S "nightfall") -b:a 128k $tmp128
& $ff -y -v error -i $tmp128 -c:a flac (Join-Path $flacA "03 - Nightfall.flac")
& $ff -y -v error -i (S "undertow") -c:a flac (Join-Path $flacA "04 - Undertow.flac")

# Spectral Suite [MP3]: the same four sources at 320k. Same audio, different
# container and name, which is exactly what Duplicate Detective should catch.
foreach ($p in @(
    @{ s = "ascent";    o = "01 - Ascent.mp3" }
    @{ s = "meridian";  o = "02 - Meridian.mp3" }
    @{ s = "nightfall"; o = "03 - Nightfall.mp3" }
    @{ s = "undertow";  o = "04 - Undertow.mp3" })) {
  & $ff -y -v error -i (S $p.s) -b:a 320k (Join-Path $mp3A $p.o)
}

# Night Sessions: Drift is 44.1 kHz content resampled into a 96 kHz container
# (Upsampled, violet); Parallax was synthesised at 96 kHz and is honest hi-res.
#
# Both soxr and the explicit cutoff are required, and each was found the hard
# way. CutoffAnalyzer only tests for an upsample behind a "sharp wall" gate of
# 18 dB within 900 Hz, and ffmpeg's default resampler leaves a ~14 dB/kHz skirt
# needing ~1.3 kHz to fall that far, so the file never reaches the upsample test
# at all. soxr is sharp enough, but its default 0 dB point sits below the source
# Nyquist and put the edge at 21.1 kHz: 4.3% below 22.05 kHz and so just outside
# the analyzer's 4% match tolerance. cutoff=0.99 puts it at 21.9 kHz, which
# matches. Same shape a real hi-res upsample has.
& $ff -y -v error -i (S "drift") -af "aresample=96000:resampler=soxr:precision=28:cutoff=0.99" `
  -c:a flac -sample_fmt s32 -bits_per_raw_sample 24 (Join-Path $hires "01 - Drift.flac")
& $ff -y -v error -i $parallax   -c:a flac -sample_fmt s32 -bits_per_raw_sample 24 (Join-Path $hires "02 - Parallax.flac")

# Field Notes: a high-bitrate transcode (yellow, "worth a listen"), an honest
# 320k MP3 (neutral), and a truncated file for the integrity dot.
$tmp320 = Join-Path $src "halcyon-320.mp3"
& $ff -y -v error -i (S "halcyon") -b:a 320k $tmp320
& $ff -y -v error -i $tmp320 -c:a flac (Join-Path $field "01 - Halcyon.flac")
& $ff -y -v error -i (S "lantern") -b:a 320k (Join-Path $field "02 - Lantern.mp3")

$ember = Join-Path $field "03 - Ember.flac"
& $ff -y -v error -i (S "ember") -c:a flac $ember
# Truncated the way a partial download is: the header still promises the full
# length, the audio simply stops.
$bytes = [System.IO.File]::ReadAllBytes($ember)
[System.IO.File]::WriteAllBytes($ember, $bytes[0..([int]($bytes.Length * 0.55) - 1)])

# Non-audio, so the manifest rollups and type chips have something to say.
& $ff -y -v error -f lavfi -i "color=c=0x1d2b3a:s=600x600" -frames:v 1 (Join-Path $flacA "cover.jpg")
& $ff -y -v error -f lavfi -i "color=c=0x2a1f33:s=600x600" -frames:v 1 (Join-Path $field "cover.jpg")
Set-Content -Encoding utf8 (Join-Path $field "folder.nfo") "Spektra Demo - Field Notes (2022)`r`nSynthetic audio generated by tools/make-demo-library.ps1. Not a real release."

# ------------------------------------------------------------- folder diff --

# A near-identical pair of albums for the folder-diff shot, built by copying
# finished tracks rather than encoding more: the diff compares audio, so all
# that matters is that four tracks appear on both sides and one on each side
# has no counterpart.
#
# Kept OUTSIDE the library, because anything added inside it changes the file
# counts the audit and duplicate shots are framed around.
#
# The two extras come from Field Notes, a different source again, so each is
# genuinely unique to its side rather than a rename of a track the other folder
# also holds (which the fingerprint would pair up regardless of the name).
$diffRoot = Join-Path $OutRoot "Diff Demo"
$diffA = Join-Path $diffRoot "Album [FLAC]"
$diffB = Join-Path $diffRoot "Album [MP3]"
$diffA, $diffB | ForEach-Object { New-Item -ItemType Directory -Force $_ | Out-Null }
# -LiteralPath, not -Path: these folder names end in "[FLAC]" and "[MP3]", and
# Get-ChildItem reads brackets as a wildcard character class, so -Path matches
# nothing here and would leave the diff folders quietly empty.
Get-ChildItem -LiteralPath $flacA -Filter *.flac | Copy-Item -Destination $diffA
Get-ChildItem -LiteralPath $mp3A -Filter *.mp3 | Copy-Item -Destination $diffB
Copy-Item (Join-Path $field "01 - Halcyon.flac") (Join-Path $diffA "05 - Extra.flac")
Copy-Item (Join-Path $field "02 - Lantern.mp3") (Join-Path $diffB "05 - Bonus.mp3")

if (-not $KeepSources) { Remove-Item -Recurse -Force $src }

$size = (Get-ChildItem $lib -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ""
Write-Host "Library at $lib ($([math]::Round($size / 1MB)) MB)" -ForegroundColor Green
Get-ChildItem $lib | Select-Object -ExpandProperty Name
Write-Host ""
Write-Host "Diff pair at $diffRoot" -ForegroundColor Green
Write-Host ""
Write-Host "Verify before capturing:  spektra-cli audit `"$lib`"" -ForegroundColor Yellow
