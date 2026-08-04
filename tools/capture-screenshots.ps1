<#
.SYNOPSIS
Captures the four README screenshots from a running Spektra.

.DESCRIPTION
Drives the app into each state and photographs its window. Every state is
reachable straight from the command line, because LaunchArgs already understands
--dupes, --manifest and --compare; no input is synthesised at all. The folder
audit needs only a warm cache, since a folder tab renders the verdicts it
already knows the moment it opens.

Capture is PrintWindow with PW_RENDERFULLCONTENT. The plain flag returns a black
rectangle for an Avalonia window (verified), so the flag is not optional.

Run tools/make-demo-library.ps1 first, and check the shots afterwards: nothing
here can tell a half-rendered window from a finished one.

.PARAMETER Library
The demo library root, matching make-demo-library.ps1's -OutRoot plus \Music.

.PARAMETER Exe
The spektra.exe to photograph. Defaults to the installed one.

.PARAMETER OutDir
Where the PNGs go. Defaults to the repo's assets folder.
#>
[CmdletBinding()]
param(
  [string]$Library = "D:\SpektraDemo\Spektra Demo Library",
  [string]$Exe     = "C:\Program Files\Spektra\spektra.exe",
  [string]$OutDir  = (Join-Path $PSScriptRoot "..\assets")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Exe))     { throw "spektra.exe not found at $Exe" }
if (-not (Test-Path $Library)) { throw "demo library not found at $Library (run make-demo-library.ps1)" }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# Stage a layout sized for the capture window, then put the real one back.
#
# Spektra persists folder-tab column widths and the tree split, so capturing
# under whatever is saved photographs one person's monitor: the first attempt
# here restored a File column 1389 px wide needing 2556 px of layout, and the
# screenshot showed that column and nothing else, every verdict off to the right.
# Redirecting APPDATA does not help, because the path comes from
# GetFolderPath(ApplicationData), which reads the known-folder registry entry
# rather than the environment variable.
#
# This is safe to do in place because the app only harvests layout when its
# window closes, and every shot below ends in Kill(), so it never writes back
# over the staged file. The original is restored in the finally block.
$settingsPath = Join-Path $env:APPDATA "Spektra\settings.json"
$backupPath   = "$settingsPath.capture-backup"
$hadSettings  = Test-Path $settingsPath

# These are LOGICAL units, not pixels, and the difference matters: this script
# is DPI-aware so it sizes the window in physical pixels, while the app lays out
# in logical units. On a 150% display a 1440-pixel window is only 960 units
# wide, so an earlier 340-unit tree came back 510 pixels and every column
# overflowed. Budget is therefore ~950, not ~1424.
#
# At 1920x1000 physical on a 150% display that is 1280x667 logical, so the
# budget is ~1265. 250 + 998 = 1248. The extra width over the old 1600 is what
# lets the tree show whole album names and Integrity spell "Corrupt" rather than
# "Corrup", on the one row that column exists to show. Each narrow column is
# sized by its HEADER, not its values: "Dropouts" needs far more room than the 0
# underneath it, and a header abbreviated to "D" is worse than a wider column.
# File is 310 against a longest relative path of 41 characters.
$captureLayout = @{
  folderTreeWidth = 250
  folderColumnWidths = @{
    "File" = 310; "Bandwidth" = 96; "Cutoff kHz" = 98; "Codec" = 76
    "kbps" = 62; "Hz" = 56; "Length" = 72; "Errors" = 64
    "Dropouts" = 76; "Integrity" = 88
  }
}

if ($hadSettings) { Copy-Item $settingsPath $backupPath -Force }
New-Item -ItemType Directory -Force (Split-Path $settingsPath) | Out-Null
# WriteAllText, not Set-Content -Encoding utf8: PowerShell 5.1 writes a BOM,
# System.Text.Json rejects it, and SettingsStore.Load swallows the exception and
# returns defaults. The staged layout then silently does nothing.
[System.IO.File]::WriteAllText($settingsPath, ($captureLayout | ConvertTo-Json -Depth 4))
Write-Host "Staged capture layout (yours is backed up at $backupPath)" -ForegroundColor DarkGray

function Restore-Settings {
  if ($script:hadSettings) {
    if (Test-Path $script:backupPath) {
      Move-Item $script:backupPath $script:settingsPath -Force
      Write-Host "Restored your settings." -ForegroundColor DarkGray
    } else {
      Write-Warning "Backup missing; your Spektra settings were NOT restored: $script:backupPath"
    }
  } elseif (Test-Path $script:settingsPath) {
    Remove-Item $script:settingsPath -Force
  }
}

try {

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class SpektraCap {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr h);
  [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
  [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
  [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

  /// Windows refuses a bare SetForegroundWindow from a process that is not
  /// already foreground, which is exactly our situation. Attaching to the
  /// current foreground thread's input queue makes the call legal, which is the
  /// standard remedy; the attach is always undone.
  public static bool Focus(IntPtr target) {
    ShowWindow(target, 9); // SW_RESTORE
    var fg = GetForegroundWindow();
    var fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero);
    var me = GetCurrentThreadId();
    var attached = fgThread != me && AttachThreadInput(me, fgThread, true);
    try {
      BringWindowToTop(target);
      SetForegroundWindow(target);
      SetActiveWindow(target);
    } finally {
      if (attached) AttachThreadInput(me, fgThread, false);
    }
    return GetForegroundWindow() == target;
  }

  private delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
  [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

  /// Visible top-level windows of one process, as "handle|title". A Spektra
  /// process owns several (main plus Duplicate Detective plus Folder Manifest),
  /// and Process.MainWindowHandle only ever reports the first.
  public static string[] WindowsOf(int pid) {
    var found = new List<string>();
    EnumWindows((h, p) => {
      int owner; GetWindowThreadProcessId(h, out owner);
      if (owner != pid || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512);
      GetWindowText(h, sb, sb.Capacity);
      if (sb.Length > 0) found.Add(h.ToInt64() + "|" + sb.ToString());
      return true;
    }, IntPtr.Zero);
    return found.ToArray();
  }
}
"@

# Without this the window is measured in virtual pixels on a scaled display and
# the capture comes out cropped.
[SpektraCap]::SetProcessDPIAware() | Out-Null

# Wide, the shape a desktop app is actually used at. Widened rather than
# shortened: at 900 tall two of the thirteen audit rows fall out of frame,
# while 1080 leaves a band of empty grid under the last one.
$WIDTH = 1920; $HEIGHT = 1000

function Stop-Spektra {
  Get-Process spektra -ErrorAction SilentlyContinue | ForEach-Object {
    $_.Kill(); $_.WaitForExit(5000) | Out-Null
  }
  Start-Sleep -Milliseconds 400
}

function Start-Shot {
  param([string[]]$SpektraArgs, [int]$SettleSeconds = 6)
  # A running instance would swallow this command line via SingleInstance and
  # leave us with no window of our own.
  Stop-Spektra
  $p = Start-Process $Exe -ArgumentList $SpektraArgs -PassThru
  $h = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) {
    Start-Sleep -Milliseconds 500
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break }
  }
  if ($h -eq [IntPtr]::Zero) { $p.Kill(); throw "no window after 40s: $($SpektraArgs -join ' ')" }
  [SpektraCap]::SetWindowPos($h, [IntPtr]::Zero, 60, 40, $WIDTH, $HEIGHT, 0x0040) | Out-Null
  [SpektraCap]::Focus($h) | Out-Null
  Start-Sleep -Seconds $SettleSeconds
  @{ Proc = $p; Handle = $h }
}

function Save-Shot {
  param([IntPtr]$Handle, [string]$Name)
  $r = New-Object SpektraCap+RECT
  [SpektraCap]::GetWindowRect($Handle, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  # 2 = PW_RENDERFULLCONTENT. Flag 0 yields solid black here.
  $ok = [SpektraCap]::PrintWindow($Handle, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  if (-not $ok) { $bmp.Dispose(); throw "PrintWindow failed for $Name" }

  # Cheap sanity check: a failed composited capture is uniformly black, which is
  # otherwise indistinguishable from a legitimately dark spectrogram until
  # someone opens the file.
  $lit = 0; $step = 9
  for ($y = 0; $y -lt $h; $y += $step) {
    for ($x = 0; $x -lt $w; $x += $step) {
      $c = $bmp.GetPixel($x, $y)
      if ($c.R -gt 24 -or $c.G -gt 24 -or $c.B -gt 24) { $lit++ }
    }
  }
  $pct = [math]::Round(100 * $lit / ([math]::Ceiling($h / $step) * [math]::Ceiling($w / $step)), 1)

  $path = Join-Path $OutDir $Name
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  if ($pct -lt 2) { Write-Warning "$Name is $pct% non-black; PrintWindow probably failed" }
  Write-Host ("  {0,-30} {1}x{2}  {3}% lit" -f $Name, $w, $h, $pct)
}

$flac    = Join-Path $Library "Spectral Suite [FLAC]"
$mp3     = Join-Path $Library "Spectral Suite [MP3]"
$hero    = Join-Path $flac "03 - Nightfall.flac"
$cmpA    = Join-Path $flac "01 - Ascent.flac"
$cmpB    = Join-Path $mp3  "01 - Ascent.mp3"

# Warm the audit cache before anything is photographed. The folder tab shows
# verdicts it already knows and analyses nothing by itself, so with a cold cache
# its grid comes out empty. A Duplicate Detective scan writes bandwidth and
# integrity results into the same store, so this one throwaway pass is enough
# and needs no separate CLI (the MSI puts the GUI on PATH as `spektra`, not a
# command-line build).
Write-Host "Warming the audit cache..." -ForegroundColor Cyan
$warm = Start-Shot -SpektraArgs @("--dupes", "`"$Library`"") -SettleSeconds 50
Stop-Spektra

Write-Host "Capturing..." -ForegroundColor Cyan

# 1. Hero. The transcode rather than a clean file: a "Lossless" banner
#    demonstrates nothing, while this one names the problem and the spectrogram
#    behind it shows the very wall the banner is describing.
$s = Start-Shot -SpektraArgs @("`"$hero`"") -SettleSeconds 12
Save-Shot -Handle $s.Handle -Name "shot-spectrogram.png"

# 2. Folder audit. No input needed: a folder tab fills its grid from the audit
#    cache on open, which the warm-up above populated. This replaced an attempt
#    to drive the Analyze menu with synthesised keystrokes, which never worked;
#    Alt+A did not reach the menu at all (it landed in the FFT box, which
#    quietly changed the FFT size), and the shots that looked correct were
#    reading a cache warmed earlier by hand. Renaming the library invalidated
#    those cache entries and exposed it as an empty grid.
$s = Start-Shot -SpektraArgs @("`"$Library`"") -SettleSeconds 14
Save-Shot -Handle $s.Handle -Name "shot-folder-audit.png"

# 3. Duplicate Detective. EnsureDupesWindow(root) scans immediately, so the only
#    wait is the scan itself. The window we want is the dupes one, and
#    MainWindowHandle would hand back the main window instead.
$s = Start-Shot -SpektraArgs @("--dupes", "`"$Library`"") -SettleSeconds 45
$dupes = [IntPtr]::Zero
foreach ($w in [SpektraCap]::WindowsOf($s.Proc.Id)) {
  $parts = $w.Split("|", 2)
  Write-Host "    window: $($parts[1])"
  if ($parts[1] -like "*Duplicate Detective*") { $dupes = [IntPtr][int64]$parts[0] }
}
if ($dupes -eq [IntPtr]::Zero) { throw "Duplicate Detective window not found" }
[SpektraCap]::SetWindowPos($dupes, [IntPtr]::Zero, 60, 40, $WIDTH, $HEIGHT, 0x0040) | Out-Null
[SpektraCap]::Focus($dupes) | Out-Null
Start-Sleep -Seconds 3
Save-Shot -Handle $dupes -Name "shot-duplicates.png"

# 4. Compare. Fully described by the command line: align and mode both ride in.
$s = Start-Shot -SpektraArgs @("--compare", "`"$cmpA`"", "`"$cmpB`"", "--auto", "--mode", "diff") -SettleSeconds 20
Save-Shot -Handle $s.Handle -Name "shot-compare.png"

Stop-Spektra
Write-Host ""
Write-Host "Wrote 4 PNGs to $OutDir" -ForegroundColor Green
Write-Host "Now open them: check each is fully drawn and shows no username in any path." -ForegroundColor Yellow

}
finally {
  Stop-Spektra
  Restore-Settings
}
