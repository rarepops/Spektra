<#
.SYNOPSIS
    Opens one audio file in a Spektra build inside a throwaway profile,
    photographs the app's own window, and closes it normally.

.DESCRIPTION
    A launch check that needs nobody at the keyboard. It answers "does this
    build start, open a file and draw it?", and because two captures of the
    same first load match pixel for pixel outside the status bar, it also
    answers "did this change alter what the first load shows?": capture both
    builds and compare the two PNG hashes. The status bar's text can differ:
    a first load always ends on the analysis's "Done in 0.2s" and its column
    count, and that time is wall-clock time, which varies run to run.

    The safety rules, each the reason a hand-rolled launch is not good enough:
      - The app runs with USERPROFILE, APPDATA and LOCALAPPDATA pointed into
        -Profile. APPDATA alone does not move .NET's known folders; USERPROFILE
        does. The real settings, Recent list and audit cache are never read or
        written, and every file the run wrote into the profile is listed at the
        end as the proof.
      - It refuses to start while any Spektra is running. A second launch hands
        its file to the running window (the handoff is keyed by the Windows
        user, which no environment variable changes), so the file would open in
        that window and land in its real Recent list.
      - Nothing is clicked or typed: synthetic input goes to whichever window
        has focus, which may belong to someone else. The window is closed by a
        close message sent to it alone, as its own close button does, so the app
        shuts down normally and saves its state into the profile.
      - The capture is PrintWindow of the app's own window with
        PW_RENDERFULLCONTENT (without that flag an Avalonia window comes back
        black), never a grab of the screen, which would photograph whatever else
        is showing. It takes the client area, everything the app draws, and
        leaves out the title bar, whose colours follow focus. Menus, popups and
        dialogs are separate windows, so they are not in the picture.

    ffmpeg and ffprobe must sit next to -Exe or on PATH: the throwaway profile
    has no downloaded copy. Exits 0 when the PNG is written and the app closed
    normally; any failure exits non-zero.

.PARAMETER Exe
    The spektra.exe to launch: a Release build or a published one.

.PARAMETER AudioFile
    The file to open. Give every build being compared the same path.

.PARAMETER Profile
    A folder for this run only: created when missing, and refused when it
    already holds files. A reused profile restores the last run's window and
    Recent list, so the capture would no longer show a first launch. Also
    refused inside the repository this script belongs to.

.PARAMETER OutPng
    Where the capture is written. Refused inside the repository, like -Profile.

.PARAMETER SettleSeconds
    How long to wait after the window appears before capturing, so the
    analysis can finish drawing. Default 8.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\gui-smoke.ps1 -Exe src\Spektra.App\bin\Release\net10.0\spektra.exe -AudioFile tests\fixtures\sine-dual-channel.wav -Profile <scratch dir>\profile-1 -OutPng <scratch dir>\smoke-1.png
#>
[CmdletBinding()]
param(
    [string] $Exe,
    [string] $AudioFile,
    [string] $Profile,
    [string] $OutPng,
    [ValidateRange(1, 600)] [int] $SettleSeconds = 8
)

$ErrorActionPreference = 'Stop'

# Relative paths are resolved against PowerShell's location here: .NET resolves
# them against the process directory, which can differ.
function Resolve-FullPath([string] $Path) {
    [System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path))
}

foreach ($name in 'Exe', 'AudioFile', 'Profile', 'OutPng') {
    if (-not (Get-Variable -Name $name -ValueOnly)) { throw "-$name is required." }
}
$Exe = Resolve-FullPath $Exe
$AudioFile = Resolve-FullPath $AudioFile
$Profile = (Resolve-FullPath $Profile).TrimEnd('\')
$OutPng = Resolve-FullPath $OutPng

# Scratch output never belongs in a checkout: a profile there fills it with the
# app's settings and a GPU shader cache, and a capture is one careless add away
# from being committed. The same test compare-cli.ps1 applies to -WorkDir, and
# it runs before anything is created or launched.
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
foreach ($name in 'Profile', 'OutPng') {
    $value = Get-Variable -Name $name -ValueOnly
    if (($value + '\').StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "-$name must be outside the repository (scratch output never belongs in a checkout): $value"
    }
}

if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "no such exe: $Exe" }
if (-not (Test-Path -LiteralPath $AudioFile -PathType Leaf)) { throw "no such audio file: $AudioFile" }
if ((Test-Path -LiteralPath $Profile) -and (Get-ChildItem -LiteralPath $Profile -Recurse -Force -File | Select-Object -First 1)) {
    throw "-Profile already holds files; give each run a fresh folder: $Profile"
}

$exeName = [System.IO.Path]::GetFileNameWithoutExtension($Exe)
$running = @(Get-Process -Name 'spektra', $exeName -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw "Spektra is already running (PID $($running[0].Id)). Close it first: it would take this launch's file into its own window and Recent list."
}

$exeDir = Split-Path -Parent $Exe
$besideExe = (Test-Path -LiteralPath (Join-Path $exeDir 'ffmpeg.exe')) -and (Test-Path -LiteralPath (Join-Path $exeDir 'ffprobe.exe'))
$onPath = (Get-Command ffmpeg -CommandType Application -ErrorAction SilentlyContinue) -and (Get-Command ffprobe -CommandType Application -ErrorAction SilentlyContinue)
if (-not ($besideExe -or $onPath)) {
    throw 'ffmpeg and ffprobe must be next to the exe or on PATH; the throwaway profile cannot see a copy downloaded into the real one.'
}

# The known folders come back empty unless they exist, so they are made first.
foreach ($d in "$Profile\AppData\Roaming", "$Profile\AppData\Local", (Split-Path -Parent $OutPng)) {
    [void] [System.IO.Directory]::CreateDirectory($d)
}

Add-Type -AssemblyName System.Drawing
if (-not ('SpektraSmokeNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SpektraSmokeNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
}
'@
}
# Without this a scaled display reports the window in virtual pixels while
# PrintWindow draws physical ones, and the capture comes out cropped.
[void] [SpektraSmokeNative]::SetProcessDPIAware()

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Exe
$psi.Arguments = '"' + $AudioFile + '"'
$psi.UseShellExecute = $false
$psi.WorkingDirectory = $Profile
$psi.EnvironmentVariables['USERPROFILE'] = $Profile
$psi.EnvironmentVariables['APPDATA'] = "$Profile\AppData\Roaming"
$psi.EnvironmentVariables['LOCALAPPDATA'] = "$Profile\AppData\Local"

$closedNormally = $false
$p = [System.Diagnostics.Process]::Start($psi)
try {
    $deadline = (Get-Date).AddSeconds(60)
    while ($p.MainWindowHandle -eq [IntPtr]::Zero) {
        if ($p.HasExited) { throw "spektra exited with code $($p.ExitCode) before showing a window." }
        if ((Get-Date) -gt $deadline) { throw 'spektra showed no window within 60 s.' }
        Start-Sleep -Milliseconds 250
        $p.Refresh()
    }
    $handle = $p.MainWindowHandle
    Start-Sleep -Seconds $SettleSeconds

    # The client area only: the title bar is drawn by Windows in different
    # colours for a focused and an unfocused window, and which one a launched
    # window gets is up to the desktop, so it would make two identical first
    # loads differ. The file name is still there, in the file details line.
    $c = New-Object SpektraSmokeNative+RECT
    if (-not [SpektraSmokeNative]::GetClientRect($handle, [ref] $c)) { throw 'GetClientRect failed.' }
    $width = $c.Right
    $height = $c.Bottom
    $bmp = New-Object System.Drawing.Bitmap $width, $height
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $g.GetHdc()
        $ok = [SpektraSmokeNative]::PrintWindow($handle, $hdc, 3) # PW_CLIENTONLY | PW_RENDERFULLCONTENT
        $g.ReleaseHdc($hdc)
        $g.Dispose()
        if (-not $ok) { throw 'PrintWindow failed.' }
        $bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bmp.Dispose() }

    # The pointer can change the picture too: with it over the window, hover
    # styles and the crosshair are drawn.
    $r = New-Object SpektraSmokeNative+RECT
    $pt = New-Object SpektraSmokeNative+POINT
    if ([SpektraSmokeNative]::GetWindowRect($handle, [ref] $r) -and
        [SpektraSmokeNative]::GetCursorPos([ref] $pt) -and
        $pt.X -ge $r.Left -and $pt.X -lt $r.Right -and $pt.Y -ge $r.Top -and $pt.Y -lt $r.Bottom) {
        Write-Warning 'The mouse pointer was over the window, so hover effects and the crosshair may be in the capture; two captures compare cleanly only with the pointer elsewhere.'
    }
}
finally {
    if ($p.HasExited) {
        $closeProblem = "spektra exited by itself (code $($p.ExitCode)) before it was asked to close."
    }
    else {
        [void] $p.CloseMainWindow()
        $closedNormally = $p.WaitForExit(15000)
        if (-not $closedNormally) {
            $p.Kill()
            [void] $p.WaitForExit(5000)
            $closeProblem = 'The window did not close within 15 s of the close message and was killed.'
        }
    }
    $p.Dispose()
}

Write-Host "capture: $OutPng (${width}x${height}, SHA-256 $((Get-FileHash -LiteralPath $OutPng -Algorithm SHA256).Hash))"
Write-Host "written into the profile ($Profile):"
$written = @(Get-ChildItem -LiteralPath $Profile -Recurse -Force -File)
if ($written.Count -eq 0) {
    Write-Warning 'Nothing landed in the profile, so this run proves no isolation; check that the app really started from -Exe.'
}
foreach ($f in $written) {
    Write-Host ('  {0}  ({1} bytes)' -f $f.FullName.Substring($Profile.Length + 1), $f.Length)
}
if (-not $closedNormally) { throw $closeProblem }
