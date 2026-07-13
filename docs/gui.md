# Spektra GUI guide

Everyday workflows for the desktop app. Keyboard shortcuts are listed in the [README](../README.md#keyboard-shortcuts).

## Inspect a file

Open files via **File → Open…** (Ctrl+O), drag-and-drop, or `spektra <file>` on the command line. Each file gets a tab (Ctrl+Tab switches, Ctrl+W or middle-click closes). The header shows codec · sample rate · bit depth · channels · duration · bitrate; hovering it shows the file's full path, and right-clicking it offers Copy path. The spectrogram paints progressively while the file is analyzed.

When the overview finishes, the **bandwidth banner** appears under the header:

- **Green (Lossless):** energy reaches the top of the band; no cutoff found.
- **Amber (Suspicious):** energy rolls off gradually; could be lossy or a natural master.
- **Red (Lossy):** a sharp cutoff at a codec-typical frequency, with a likely codec/bitrate guess.
- **Violet (Upsampled):** the container claims hi-res (e.g. 96 kHz) but the real bandwidth stops at a lower standard rate's limit (e.g. 22.05 kHz, a 44.1 kHz source). The codec guess is suppressed; the banner names the likely true source rate.

When a cutoff is detected, its frequency is also drawn as a thin line (with a matching tick on the frequency ruler) across the spectrogram in the verdict's color, so the wall is visible against the image; the line tracks zoom, pan, and the log/linear axis. The line can sit below where the picture seems to end: the detector counts a frequency as live only if it ever comes within 55 dB of the file's loudest point, while the display paints everything down to the dB floor (-120 by default), so faint speckle above the line (encoder noise, artifacts around decode errors) is visible but deliberately does not count as bandwidth.

Zoom with the wheel (time) and Shift+wheel (frequency), drag to pan, double-click to reset. Zoomed spans re-render sharply via an ffmpeg segment decode. Multichannel files get a channel selector (Mix / Ch 1 / Ch 2 / …); for stereo files the other views are precomputed in the background right after load, so switching is instant (Ctrl+Up / Ctrl+Down step through them). The integrity result sticks to the file across switches; loudness is remembered per channel. Ctrl+0 resets the view, F5 reloads the file, and the cursor crosshair with its time / frequency / dB readout can be hidden via View → Crosshair (Ctrl+H).

## Deeper checks (Analyze menu)

- **Check Integrity (Ctrl+I):** decode errors, interior silent gaps, and truncation: the classic partial-download failures. Findings are also marked on a thin lane along the time axis: cyan for each silent gap (informational), red for a truncated file's missing tail (damage). The lane zooms and pans with the spectrogram (decode errors carry no position, so they show in the banner only). Once results exist, Ctrl+I toggles them: press again to hide the banner and lane, again to bring them back without re-analyzing. Silent gaps are reported but never raise the verdict (silence is legal audio); one or two stray decode errors count as Suspect (worth a listen); Corrupt means provable damage (decode failure, three or more errors, or a file shorter than its own header promises).
- **Measure Loudness (Ctrl+L):** integrated LUFS, loudness range, true peak (EBU R128 via ffmpeg), crest factor, and a clipping hint. Like Ctrl+I, pressing it again hides the banner and again brings it back without re-measuring.

## Export a report

- **File → Export Report…** saves the current file's audit (metadata, bandwidth/upsampling verdict, integrity) as a one-row CSV or JSON file. If the integrity check hasn't been run yet, it runs first.
- **File → Export Folder Report…** picks a folder, analyzes every audio file in it in parallel (progress dialog with Cancel), then saves one row per file. Cancelling writes nothing.

The format follows the extension you choose in the save dialog: `.csv` or `.json`. The schema matches the CLI's `audit --csv/--json` exactly, so GUI and scripted sweeps are interchangeable.

## Folder audit

Drop a folder onto the window (or File > Open Folder..., Ctrl+Shift+O, or pass a folder on the command line: `spektra Music`) to browse and audit a library in place. The folder opens instantly as a browse tree in a left pane: folders and files each carry a checkbox and a severity marker dot, and each folder shows a rollup label ("12 files" before anything is analyzed, "5/12 · 2 problems" once verdicts exist). Nothing is analyzed on drop; any verdicts cached from earlier audits paint straight onto the tree markers and the grid, so you see what is already known right away. Tick individual files or whole folders to build a worklist (a folder's checkbox cascades to everything beneath it, and shows an indeterminate state when only part of its subtree is checked), then press Analyze (or F5) to analyze exactly the checked set; Shift+F5 (or Shift+click Analyze) re-analyzes even files that are already cached.

The grid on the right streams verdicts as the checked files are analyzed: bandwidth verdict, cutoff, codec, bitrate, length, and an integrity verdict with a severity dot in the last column. Transcodes and corrupt values show red, upsampled shows violet; an honest lossy file (an MP3 with the cutoff its bitrate predicts) is not a problem and stays neutral. Red bandwidth means the wall does not belong there: lossy content in a lossless format, or an mp3/aac far below its bitrate's expected cutoff. The severity filter (All files / Suspect + worse / Problems only) hides rows below the chosen tier; each tier includes everything worse, so a corrupt file always passes a bar a suspect file passes. The File column shows each row's path relative to the dropped folder (hover for the full path), so you can tell apart same-named tracks from different albums; sorting it groups rows by folder, and Export writes the same relative paths to the CSV or JSON. Double-click a row (or press Enter) to open that file as a normal tab. Opening a row whose integrity is Corrupt or Suspect runs the integrity check on the new tab automatically, so the time-axis lane marks the damaged regions as soon as you arrive. Select a folder in the tree and press Drilldown (in the row of actions above the tree, next to the check-all/none buttons) to scope the grid to that subtree (a "Scope: path" breadcrumb shows the focus); Up widens the scope one folder at a time, and Show all clears it.

Results are cached in `%APPDATA%\Spektra\audit-cache.db` (shared with the CLI, keyed by file size and modified time), so a file analyzed here is already cached for the next audit and for the `spektra audit` command. The progress bar is byte-weighted, with a live percentage, file count, and remaining-time estimate beside it once enough files have finished to make the estimate meaningful. The order files are scheduled in is a preference (Folder analysis order in Ctrl+E): folder order (top to bottom, as the tree shows them), smallest files first for quick early results, or largest files first. Analysis is parallel, but files are handed to the workers strictly in the scheduled order, so the chosen order is what you see: rows may finish a few positions apart, never from random corners of the tree. While a run is in flight, the tab's folder icon in the tab strip becomes a spinner, so a run left in the background stays visible; one analysis runs at a time across folder tabs, and starting another just names the tab that is busy in the status bar. Cancelling keeps the rows already analyzed, and finished files stay cached, so a later run reuses them; close the tab and reopen the folder to pick up files that changed on disk.

## Compare two encodes

Open both files, then **File → Compare…** (or `spektra --compare A B`). The comparison tab stacks A over B on a shared time axis with synced zoom/pan.

- **Align:** coarse/fine sliders or the ms box nudge B in time; **Auto** finds the offset by cross-correlation. Drift (clock-rate mismatch) is detected and flagged.
- **Modes:** Both / A / B / Diff (hotkeys: `T` flips A/B, `D` shows the difference, `A` auto-aligns, `Esc` returns to Both). The Diff view renders A−B on a diverging blue-white-red scale; a transcode shows up as a solid red band above its cutoff.
- **Cursor line:** a vertical line runs through both panes with a time tick on the shared ruler, so a feature in A can be matched to the same instant in B. Toggle it via View → Crosshair (Ctrl+H).
- **Null test:** time-domain A−B residual over the visible span, reported in dB (deeper = more identical).

## Save or copy the picture

**File → Save Image…** (Ctrl+S) writes a PNG of the visible spectrogram; **Copy Image** (Ctrl+Shift+C) puts it on the clipboard.

## Preferences (Ctrl+E)

FFT size (512-8192) and window function (Hann/Hamming/Blackman/Blackman-Harris) are analysis settings that re-analyze open tabs; the folder analysis order (folder order / smallest first / largest first) schedules the folder tab's Analyze and applies from the next run; palette (Turbo by default; Magma, Inferno, Plasma, Viridis, Cividis, Grayscale, single-hue MonoGreen/MonoAmber ramps where saturation tracks intensity, plus any custom palette JSON dropped in `%APPDATA%\Spektra\palettes` or a `palettes` folder next to the app - see the CLI guide for the format; the list refreshes when Preferences opens), tightness (a level curve: higher keeps quiet detail darker so peaks read tighter, lower blooms), dB floor, and linear/log frequency axis are display settings applied instantly. The once-a-day update check toggle also lives here; updates are notify-only (Help → Check for Updates).
