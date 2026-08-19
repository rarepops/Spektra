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

# Tonal fixtures for the fingerprint matcher, added 2026-08-06. The chirp is a
# poor matcher fixture: the sweep leaves the fingerprint's 55-3520 Hz chroma
# band at ~0.5 s, so five-sixths of it is spectrally empty for the matcher and
# its old codec-survival pass rode on the sparse-word floor that
# chance-corrected similarity now subtracts. These stay in band throughout.
#
# tones-a: bass 110 Hz + fifth, melody stepping UP 2 semitones every 0.25 s.
# tones-b: the SAME bass drone (same key, same static chroma profile) with a
#          different melody: down 3 semitones every 0.35 s from a different
#          start. This is the owner-library false positive of 2026-08-05 as a
#          permanent fixture: profile-similar strangers must never group.
#
# The encode is 128k, not 64k, and the choice is measured: at 64k so few exact
# 32-bit words survive on content this rich (4 hits over 3 s) that the vote
# stage never finds the alignment at all. That is a pre-existing property of
# exact-word voting, not of the chance correction; the survives-lossy guarantee
# is pinned where matching genuinely works.
# Each carries a quiet air layer above 11 kHz, and that layer is load-bearing
# twice over. Without it the tones stop around 1.8 kHz, so the bandwidth
# analyzer has nothing to judge and reports whichever noise floor happens to
# reach higher: the WAV measured a 9.8 kHz rolloff against the MP3's 10.1 kHz,
# and the quality ranker duly crowned the MP3 over its own lossless source. With
# it the WAV reads full-band and the encode reads a real wall. The air cannot
# disturb matching because the fingerprint only looks at 55-3520 Hz; different
# seeds keep the two tracks' noise independent anyway.
$melA = "440*exp(0.1155*floor(t/0.25 - 8*floor(t/2)))"
$melB = "554*exp(-0.1733*floor(t/0.35 - 6*floor(t/2.1)))"
$bass = "0.5*sin(2*PI*110*t)+0.25*sin(2*PI*165*t)"
$air  = "[1:a]highpass=f=11000,highpass=f=11000,volume=0.011[air];" +
        "[0:a][air]amix=inputs=2:normalize=0,alimiter=limit=0.95:level=0[out]"
& $ff -y -v error `
  -f lavfi -i "aevalsrc=$bass+0.35*sin(2*PI*$melA*t)+0.15*sin(2*PI*2*$melA*t):s=44100:d=3" `
  -f lavfi -i "anoisesrc=color=white:sample_rate=44100:duration=3:amplitude=1.0:seed=101" `
  -filter_complex $air -map "[out]" -ac 1 -c:a pcm_s16le "$fx\tones-a.wav"
& $ff -y -v error `
  -f lavfi -i "aevalsrc=$bass+0.35*sin(2*PI*$melB*t)+0.15*sin(2*PI*2*$melB*t):s=44100:d=3" `
  -f lavfi -i "anoisesrc=color=white:sample_rate=44100:duration=3:amplitude=1.0:seed=202" `
  -filter_complex $air -map "[out]" -ac 1 -c:a pcm_s16le "$fx\tones-b.wav"
& $ff -y -v error -i "$fx\tones-a.wav" -b:a 128k "$fx\tones-a-128.mp3"


# A tagged FLAC carrying embedded cover art, for the metadata reader. The art
# is what makes it worth committing: an attached picture is a VIDEO stream, so
# a probe filtered to audio cannot see one, and no synthetic JSON can prove the
# real probe arguments let it through.
#
# The tags are deliberately in their awkward real-world shapes. "5/12" carries
# its own total while TOTALTRACKS is the separate Vorbis convention ffprobe
# does not fold in, and a full ISO date stands in for the year, so the file
# exercises the normalizing rather than the happy path.
$art = Join-Path $env:TEMP "spektra-fixture-cover.png"
& $ff -y -v error -f lavfi -i "color=c=teal:s=600x600:d=1" -frames:v 1 $art
& $ff -y -v error `
  -f lavfi -i "sine=frequency=440:sample_rate=44100:duration=3" -i $art `
  -map 0:a -map 1:v -c:a flac -c:v copy -disposition:v attached_pic `
  -metadata artist="Aurora" -metadata album_artist="Aurora" `
  -metadata album="First Light" -metadata title="Intro" `
  -metadata track="5/12" -metadata TOTALTRACKS="12" -metadata disc="1/2" `
  -metadata date="2019-04-12" -metadata genre="Ambient" `
  "$fx\tagged-with-art.flac"
Remove-Item $art -ErrorAction SilentlyContinue

Get-ChildItem $fx
