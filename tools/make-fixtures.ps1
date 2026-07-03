# Regenerates the tiny committed audio fixtures. Requires ffmpeg (PATH or
# %LOCALAPPDATA%\Spektra\ffmpeg).
$ErrorActionPreference = "Stop"
$ff = "ffmpeg"
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
  $ff = Join-Path $env:LOCALAPPDATA "Spektra\ffmpeg\ffmpeg.exe"
}
$fx = Join-Path $PSScriptRoot "..\tests\fixtures"
New-Item -ItemType Directory -Force $fx | Out-Null

# aevalsrc keeps the amplitude explicit (lavfi's `sine` source is fixed at 1/8)
& $ff -y -v error -f lavfi -i "aevalsrc=0.9*sin(2*PI*1000*t):s=44100:d=3" -ac 1 -c:a pcm_s16le "$fx\sine-1khz.wav"
& $ff -y -v error -f lavfi -i "aevalsrc=0.9*sin(2*PI*1000*t):s=44100:d=3" -ac 1 -sample_fmt s16 -c:a flac "$fx\sine-1khz.flac"
& $ff -y -v error -f lavfi -i "aevalsrc=0.9*sin(2*PI*1000*t):s=44100:d=3" -ac 1 -b:a 128k "$fx\sine-1khz.mp3"
& $ff -y -v error -f lavfi -i "aevalsrc=0.8*sin(2*PI*3675*t*t):s=44100:d=3" -ac 1 -c:a pcm_s16le "$fx\chirp.wav"
& $ff -y -v error -f lavfi -i "anoisesrc=colour=white:sample_rate=44100:duration=3:amplitude=0.5" -ac 1 -c:a pcm_s16le "$fx\noise.wav"
# full-band chirp encoded at MP3 64k — a real lossy brick-wall cutoff (~16.8 kHz)
# for the cutoff/lossless verdict test. The chirp hits every frequency at full
# amplitude, so the encoder's low-pass shows as an unambiguous cliff.
& $ff -y -v error -f lavfi -i "aevalsrc=0.8*sin(2*PI*3675*t*t):s=44100:d=3" -ac 1 -b:a 64k "$fx\chirp-mp3-64.mp3"
& $ff -y -v error -f lavfi -i "aevalsrc=0.9*sin(2*PI*1000*t):s=44100:d=3" -ac 2 -c:a pcm_s16le "$fx\sine-1khz-stereo.wav"
# distinct tones per channel (L=1 kHz, R=3 kHz) so channel selection is verifiable
& $ff -y -v error -f lavfi -i "aevalsrc=0.9*sin(2*PI*1000*t)|0.9*sin(2*PI*3000*t):s=44100:c=stereo:d=3" -c:a pcm_s16le "$fx\sine-dual-channel.wav"
# low-passed chirp (rolled off ~16 kHz) — diff test: A full-band vs B rolled off
& $ff -y -v error -f lavfi -i "aevalsrc=0.8*sin(2*PI*3675*t*t):s=44100:d=3" -af "lowpass=f=16000" -ac 1 -c:a pcm_s16le "$fx\chirp-lp16k.wav"
# chirp delayed 50 ms — aligner test: recover a known offset
& $ff -y -v error -f lavfi -i "aevalsrc=0.8*sin(2*PI*3675*t*t):s=44100:d=3" -af "adelay=50" -ac 1 -c:a pcm_s16le "$fx\chirp-delay50ms.wav"
# deliberately corrupted FLAC — integrity test: keep the header (which still
# reports the full 3 s) but truncate the audio, as a partial download would
Copy-Item "$fx\sine-1khz.flac" "$fx\corrupt.flac" -Force
$cb = [System.IO.File]::ReadAllBytes((Resolve-Path "$fx\corrupt.flac"))
$keep = [int]($cb.Length * 0.55)
[System.IO.File]::WriteAllBytes((Join-Path (Resolve-Path $fx) "corrupt.flac"), $cb[0..($keep - 1)])
Set-Content "$fx\notaudio.txt" "this is not an audio file"
Get-ChildItem $fx
