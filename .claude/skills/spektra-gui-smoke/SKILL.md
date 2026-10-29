---
name: spektra-gui-smoke
description: Launch a Spektra desktop build on one audio file inside a throwaway profile, capture the app's own window with PrintWindow, close it normally, and compare captures byte for byte, all without clicking, typing or touching the user's settings. Use it for a pre-release launch check of the published app, after any change to what the first load of a file shows (the file details line, the integrity banner, the spectrogram, the layout), and to prove an old and a new build draw the same first load. Reach for it whenever someone asks to smoke test the GUI, check that the app still starts, screenshot Spektra, see what a change looks like in the app, or prove a refactor left the window unchanged.
---

# Spektra GUI smoke

`tools/gui-smoke.ps1` opens one file in a Spektra build, waits for it to draw, photographs everything the app draws in its window into a PNG, closes the app the way its close button does, and lists every file the run wrote into a throwaway profile. Two captures of the same first load match pixel for pixel outside the status bar (see below), so comparing them answers "did this change alter what the first load shows?" with nobody at the keyboard. The capture leaves out the title bar: Windows draws it in different colours for a focused and an unfocused window, and which one a launched window gets is up to the desktop.

## Rules

- Never synthesise input: no SendKeys, no mouse events, no injected keystrokes, not even once. Synthetic input lands in whatever window has focus, and on a shared desktop that can be someone else's meeting or document.
- Capture only the app's own window, which is what the script does with PrintWindow. Never grab the screen (CopyFromScreen or any screenshot tool): it photographs whatever is on the display, other people's windows included. There is no fallback to a screen grab.
- Menus, popups and dialogs are separate windows, so they are never in the capture. A check that needs one goes to the maintainer (see Limits).
- Give every run a fresh, empty profile folder in the scratch area (under `$env:TEMP`), never inside a checkout. The script refuses a profile that already holds files: a reused profile restores the last window and Recent list, so the capture would no longer show a first launch.
- Close every Spektra window first. The script refuses while Spektra runs, because a second launch hands its file to the running window, which would open the scratch file and add it to the real Recent list.
- Report what the run wrote into the profile: that list is the proof the run was isolated. Expect `AppData\Roaming\Spektra\settings.json` there; nothing may ever land in the real `%APPDATA%\Spektra` or `%LOCALAPPDATA%\Spektra`.

## Run it

1. Build the exe. For a change under review, `dotnet build src/Spektra.App -c Release` gives `src\Spektra.App\bin\Release\net10.0\spektra.exe`. For a pre-release check, publish it the way the release workflow does, into a scratch folder: `dotnet publish src/Spektra.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o <scratch dir>\gui`. For an old commit, export it with `git archive` into a scratch folder and build there, so the checkout stays untouched.
2. Run the smoke: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\gui-smoke.ps1 -Exe <exe> -AudioFile tests\fixtures\sine-dual-channel.wav -Profile <scratch dir>\profile-1 -OutPng <scratch dir>\smoke-1.png`. A Spektra window shows on the desktop for about ten seconds; tell the user before you start. ffmpeg and ffprobe must be on PATH or next to the exe, since the throwaway profile has no downloaded copy. Raise `-SettleSeconds` (default 8) for a long file or a slow machine.
3. Open the PNG with the Read tool and say what it shows. For `sine-dual-channel.wav` expect the file details line, the integrity banner, a complete spectrogram with one line near 1 kHz and one near 3 kHz, and a status bar ending on either `Done in ... · 128 columns` or `No integrity problems detected.` Neither ending is a failure: the analysis and the automatic integrity check each write the status bar when they finish, the later one wins, and which finishes last varies from run to run. A black, empty or half-drawn window is a failed run, not a pass.
4. Report the PNG path, its SHA-256 (the script prints it) and the files listed under "written into the profile".

## Compare two builds

- Give both runs the same `-AudioFile` path and a fresh profile each. The window shows the file, so a different path is a different picture.
- Compare with `Get-FileHash -LiteralPath <first.png>, <second.png> -Algorithm SHA256`. Equal hashes mean the first load is pixel for pixel the same.
- The status bar's text can differ between two captures of one first load: it ends on whichever job finished last (see step 3), and the analysis's `Done in 0.2s` is wall-clock time that changes whenever the analysis time rounds differently. When the hashes differ, find where with the script beside this file: `powershell -NoProfile -ExecutionPolicy Bypass -File .claude\skills\spektra-gui-smoke\compare-captures.ps1 -First <first.png> -Second <second.png>`. A box confined to the status bar's text is that race or the clock, not the change; a box anywhere outside the status bar is a finding, so open both PNGs and describe what changed.
- Before trusting a difference, capture one build twice: those two must agree everywhere outside the status bar. If they do not, the run is not deterministic: the analysis was still drawing (raise `-SettleSeconds`), or the pointer was over the window (the script warns; hover styles and the crosshair follow the pointer).
- A difference is a finding to explain, not noise to retry away.

## Limits: what goes to the maintainer

The smoke cannot click, so anything behind input (switching channels, zooming, keys, menus, dialogs) is the maintainer's to check by hand. Hand over a few named checks, each with the risk it would catch, and drop any that repeat what a capture already proved. For example:

- Channel switch on `sine-dual-channel.wav`: choosing the second (right-hand) channel leaves only the 3 kHz line. Catches a selector that still draws the mix.
- Zoom in, then back out: the spectrogram redraws sharp and returns to the full view. Catches a stale or misaligned redraw.

Say which parts were verified by a capture and which were only read in the code.
