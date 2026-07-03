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
& $ff -y -v error -f lavfi -i "aevalsrc=0.9*sin(2*PI*1000*t):s=44100:d=3" -ac 2 -c:a pcm_s16le "$fx\sine-1khz-stereo.wav"
# distinct tones per channel (L=1 kHz, R=3 kHz) so channel selection is verifiable
& $ff -y -v error -f lavfi -i "aevalsrc=0.9*sin(2*PI*1000*t)|0.9*sin(2*PI*3000*t):s=44100:c=stereo:d=3" -c:a pcm_s16le "$fx\sine-dual-channel.wav"
# low-passed chirp (rolled off ~16 kHz) — diff test: A full-band vs B rolled off
& $ff -y -v error -f lavfi -i "aevalsrc=0.8*sin(2*PI*3675*t*t):s=44100:d=3" -af "lowpass=f=16000" -ac 1 -c:a pcm_s16le "$fx\chirp-lp16k.wav"
# chirp delayed 50 ms — aligner test: recover a known offset
& $ff -y -v error -f lavfi -i "aevalsrc=0.8*sin(2*PI*3675*t*t):s=44100:d=3" -af "adelay=50" -ac 1 -c:a pcm_s16le "$fx\chirp-delay50ms.wav"
Set-Content "$fx\notaudio.txt" "this is not an audio file"
Get-ChildItem $fx
