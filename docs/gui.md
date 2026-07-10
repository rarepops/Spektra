# Spektra GUI guide

Everyday workflows for the desktop app. Keyboard shortcuts are listed in the
[README](../README.md#keyboard-shortcuts).

## Inspect a file

Open files via **File → Open…** (Ctrl+O), drag-and-drop, or `spektra <file>` on
the command line. Each file gets a tab (Ctrl+Tab switches, Ctrl+W or
middle-click closes). The header shows codec · sample rate · bit depth ·
channels · duration · bitrate, and the spectrogram paints progressively while
the file is analyzed.

When the overview finishes, the **bandwidth banner** appears under the header:

- **Green (Lossless):** energy reaches the top of the band; no cutoff found.
- **Amber (Suspicious):** energy rolls off gradually; could be lossy or a
  natural master.
- **Red (Lossy):** a sharp cutoff at a codec-typical frequency, with a likely
  codec/bitrate guess.
- **Violet (Upsampled):** the container claims hi-res (e.g. 96 kHz) but the
  real bandwidth stops at a lower standard rate's limit (e.g. 22.05 kHz,
  a 44.1 kHz source). The codec guess is suppressed; the banner names the
  likely true source rate.

Zoom with the wheel (time) and Shift+wheel (frequency), drag to pan,
double-click to reset. Zoomed spans re-render sharply via an ffmpeg segment
decode. Multichannel files get a channel selector (Mix / Ch 1 / Ch 2 / …);
for stereo files the other views are precomputed in the background right
after load, so switching is instant (Ctrl+Up / Ctrl+Down step through them).
The integrity result sticks to the file across switches; loudness is
remembered per channel. Ctrl+0 resets the view, F5 reloads the file, and the
cursor crosshair with its time / frequency / dB readout can be hidden via
View → Crosshair (Ctrl+H).

## Deeper checks (Analyze menu)

- **Check Integrity (Ctrl+I):** decode errors, interior silent gaps, and
  truncation: the classic partial-download failures.
- **Measure Loudness (Ctrl+L):** integrated LUFS, loudness range, true peak
  (EBU R128 via ffmpeg), crest factor, and a clipping hint.

## Export a report

- **File → Export Report…** saves the current file's audit (metadata,
  bandwidth/upsampling verdict, integrity) as a one-row CSV or JSON file. If
  the integrity check hasn't been run yet, it runs first.
- **File → Export Folder Report…** picks a folder, analyzes every audio file
  in it in parallel (progress dialog with Cancel), then saves one row per file.
  Cancelling writes nothing.

The format follows the extension you choose in the save dialog: `.csv` or
`.json`. The schema matches the CLI's `audit --csv/--json` exactly, so GUI and
scripted sweeps are interchangeable.

## Folder audit

Drop a folder onto the window (or File > Open Folder..., Ctrl+Shift+O, or
pass a folder on the command line: `spektra Music`) to
audit a whole library in place. Files stream into a sortable grid as they
are analyzed: bandwidth verdict, cutoff, integrity, codec, bitrate, length.
Transcodes and corrupt values show red, upsampled shows violet; an honest
lossy file (an MP3 with the cutoff its bitrate predicts) is not a problem
and stays neutral. Red bandwidth means the wall does not belong there:
lossy content in a lossless format, or an mp3/aac far below its bitrate's
expected cutoff. Check "Problems only" to hide clean files, double-click a
row (or press Enter) to open that file as a normal tab, and use Export to
save the grid as CSV or JSON.

Results are cached in `%APPDATA%\Spektra\audit-cache.db` keyed by file size
and modified time, so rescanning a big library only analyzes new or changed
files ("1,850 from cache" in the status bar). F5 rescans using the cache;
Shift+F5 (or Shift+click Rescan) re-analyzes everything. The progress bar is
byte-weighted and the remaining-time estimate appears once enough files have
finished to make it meaningful. Cancelling keeps the rows already scanned,
and finished files stay cached, so the next scan resumes cheaply.

## Compare two encodes

Open both files, then **File → Compare…** (or `spektra --compare A B`). The
comparison tab stacks A over B on a shared time axis with synced zoom/pan.

- **Align:** coarse/fine sliders or the ms box nudge B in time; **Auto** finds
  the offset by cross-correlation. Drift (clock-rate mismatch) is detected and
  flagged.
- **Modes:** Both / A / B / Diff (hotkeys: `T` flips A/B, `D` shows the
  difference, `A` auto-aligns, `Esc` returns to Both). The Diff view renders
  A−B on a diverging blue-white-red scale; a transcode shows up as a solid
  red band above its cutoff.
- **Cursor line:** a vertical line runs through both panes with a time tick
  on the shared ruler, so a feature in A can be matched to the same instant
  in B. Toggle it via View → Crosshair (Ctrl+H).
- **Null test:** time-domain A−B residual over the visible span, reported in
  dB (deeper = more identical).

## Save or copy the picture

**File → Save Image…** (Ctrl+S) writes a PNG of the visible spectrogram;
**Copy Image** (Ctrl+Shift+C) puts it on the clipboard.

## Preferences (Ctrl+E)

FFT size (512-8192) and window function (Hann/Hamming/Blackman/
Blackman-Harris) are analysis settings that re-analyze open tabs; palette
(Magma/Viridis/Inferno/Grayscale), dB floor, and linear/log frequency axis are
display settings applied instantly. The once-a-day update check toggle also
lives here; updates are notify-only (Help → Check for Updates).
