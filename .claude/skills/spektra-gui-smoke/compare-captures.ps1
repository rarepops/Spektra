<#
.SYNOPSIS
    Says where two gui-smoke captures differ: how many pixels, and the box
    around them.

.DESCRIPTION
    Equal file hashes need no help. Two captures of the same first load can
    still differ in the status bar: it always ends on the analysis's "Done in
    0.2s" and its column count, and that time is wall-clock time. A box
    confined to the status bar's text is the clock, not a change; a box
    anywhere else is a finding. Exits 0 when the pixels are identical, 1 when
    they differ.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .claude\skills\spektra-gui-smoke\compare-captures.ps1 -First <first.png> -Second <second.png>
#>
param([string] $First, [string] $Second)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$imageA = New-Object System.Drawing.Bitmap -ArgumentList (Resolve-Path -LiteralPath $First).Path
$imageB = New-Object System.Drawing.Bitmap -ArgumentList (Resolve-Path -LiteralPath $Second).Path
try {
    if ($imageA.Width -ne $imageB.Width -or $imageA.Height -ne $imageB.Height) {
        Write-Host "sizes differ: $($imageA.Width)x$($imageA.Height) and $($imageB.Width)x$($imageB.Height)"
        exit 1
    }
    $count = 0; $left = $imageA.Width; $top = $imageA.Height; $right = -1; $bottom = -1
    for ($y = 0; $y -lt $imageA.Height; $y++) {
        for ($x = 0; $x -lt $imageA.Width; $x++) {
            if ($imageA.GetPixel($x, $y).ToArgb() -ne $imageB.GetPixel($x, $y).ToArgb()) {
                $count++
                if ($x -lt $left) { $left = $x }
                if ($x -gt $right) { $right = $x }
                if ($y -lt $top) { $top = $y }
                if ($y -gt $bottom) { $bottom = $y }
            }
        }
    }
    if ($count -eq 0) {
        Write-Host 'pixels identical'
        exit 0
    }
    Write-Host "$count pixels differ, all within x $left..$right and y $top..$bottom (pixels from the top left of a $($imageA.Width)x$($imageA.Height) image)"
    exit 1
}
finally {
    $imageA.Dispose()
    $imageB.Dispose()
}
