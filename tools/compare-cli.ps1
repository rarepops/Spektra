<#
.SYNOPSIS
    Shows whether spektra-cli prints anything different at two commits.

.DESCRIPTION
    The bar for a change that claims to change no behaviour (a refactor, a
    move, a dependency bump): build spektra-cli at both commits, run both over
    the same fixed library with the same arguments, and compare everything
    they print. It cannot say the output is right, only that it did not
    change, which is exactly the claim such a change makes.

      1. Each commit is exported with `git archive` into the work folder and
         built there in Release. An export is reused when a later run names
         the same commit. Nothing in the checkout is touched, and uncommitted
         changes are not part of any export.
      2. A two-folder library is built once from this checkout's
         tests/fixtures, the files the matrix below names, and shared by both
         sides: the same files at the same paths is what makes two transcripts
         comparable byte for byte.
      3. Every line of $Matrix runs against each build, recording stdout,
         stderr and the exit code. Each side gets its own throwaway profile
         (USERPROFILE, APPDATA and LOCALAPPDATA all point into it; APPDATA
         alone does not move .NET's known folders), so no run reads or writes
         the real audit cache, settings or palettes, and both sides start
         cold. $NoCacheMatrix runs under a profile whose cache cannot be
         opened.
      4. What legitimately differs is normalised before comparing: the minute
         an HTML page was written, CRLF versus LF, the work folder's own
         absolute paths, and the version --version prints.
      5. IDENTICAL exits 0. Otherwise the first differing invocations are
         printed with their lines and it exits 1. A setup failure (an unknown
         commit, a failed build, a missing tool) exits 2.

    The marker table counts how often each print branch in $Markers fired. A
    zero under head is a branch the matrix never reaches: no transcript vouches
    for it, so check it by reading the code or add a matrix line that reaches
    it.

    Needs git, the .NET SDK, and ffmpeg + ffprobe on PATH: the throwaway
    profile hides any copy downloaded into the real one.

.PARAMETER Base
    The commit the change starts from, usually the branch point.

.PARAMETER Head
    The commit to judge, usually the branch tip.

.PARAMETER WorkDir
    Where exports, builds and transcripts go. Defaults to
    $env:TEMP\spektra-compare-cli. Never inside the repository: .NET tooling
    finds the exported project copies there and breaks.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\compare-cli.ps1 -Base dev -Head HEAD
#>

# A plain param block, not [CmdletBinding()]: an advanced script rejects a
# mistyped parameter before any of it runs, and PowerShell then exits 1, which
# reads as "different". Here a stray argument lands in $args and exits 2.
param(
    [string] $Base,
    [string] $Head,
    [string] $WorkDir
)

# One invocation per line, arguments separated by "|"; "(no args)" runs the
# tool bare, and blank lines and lines starting with # are skipped. Paths are
# relative to each side's run folder, which sits beside the shared library:
# ../lib/all holds every fixture, ../lib/A and ../lib/B are the two-folder
# library. Adding a verb, flag or output branch to the check is one line here.
$Matrix = @'
--help
(no args)
help
--version
-v
bogus
report|../lib/all
report|../lib/all|--json
report|../lib/all|--csv
report|../lib/all/sine-1khz.wav|../lib/all/chirp-mp3-64.mp3|../lib/all/nope.wav
report
report|../lib/all|--bogus
scan|../lib/all
scan|../lib/all|--json
scan|../lib/all|--csv|-j|2
scan|../lib
scan
scan|../lib/nope
scan|../lib/all|--jobs|0
check|../lib/all
check|../lib/all|--json
check|../lib/all|--csv
check|../lib/all/corrupt.flac|../lib/all/gap.wav|../lib/all/sine-1khz.wav|../lib/all/junk-appended.mp3
check
audit|../lib/all
audit|../lib/all|--json
audit|../lib/all|--csv
audit|../lib/all|--fresh
audit|../lib/all/sine-1khz.wav|../lib/all/transcode.flac|../lib/all/nope.wav
audit|../lib/all|--html|audit.html
audit|--html|audit2.html|../lib/A|--json
audit
audit|../lib/all|--html
audit|../lib/all|--bogus
dupes|../lib/A|../lib/B
dupes|../lib/A|../lib/B|--json
dupes|../lib/A|../lib/B|--csv
dupes|../lib/A|../lib/B|--fresh
dupes|../lib/A|../lib/B|--html|dupes.html
dupes|../lib/all
dupes|../lib
dupes
dupes|../lib/nope
dupes|../lib/A|--bogus
inventory|../lib/all
inventory|../lib/all|--json
inventory|../lib/all|--csv
inventory|../lib
inventory
inventory|../lib/A|../lib/B
manifest|../lib/all
manifest|../lib/all|--json
manifest|../lib/all|--csv
manifest|../lib/all|--html|manifest.html
manifest|../lib
manifest
manifest|../lib/nope
loudness|../lib/all/sine-1khz.wav|../lib/all/noise.wav
loudness|../lib/all/sine-1khz.wav|../lib/all/noise.wav|--json
loudness|../lib/all/sine-1khz.wav|--csv
loudness|../lib/all/notaudio.txt|../lib/all/sine-1khz.wav
loudness
diff|../lib/all/tones-a.wav|../lib/all/tones-a-128.mp3
diff|../lib/all/tones-a.wav|../lib/all/tones-a-128.mp3|--json
diff|../lib/all/tones-a.wav|../lib/all/tones-a-128.mp3|--csv
diff|../lib/all/chirp.wav|../lib/all/chirp-delay50ms.wav
diff|../lib/all/chirp.wav|../lib/all/chirp-delay50ms.wav|--offset|50
diff|--offset|-50|../lib/all/chirp-delay50ms.wav|../lib/all/chirp.wav
diff|../lib/all/chirp.wav|../lib/all/chirp-lp16k.wav|--threshold-db|20
diff|../lib/all/chirp.wav|../lib/all/chirp-pitchup.wav
diff|../lib/all/sine-1khz.wav|../lib/all/sine-1khz.wav
diff|../lib/all/chirp.wav
diff|--bogus|a|b
diff|../lib/all/chirp.wav|../lib/all/notaudio.txt
diff|../lib/all/chirp.wav|../lib/all/chirp-delay50ms.wav|--offset
diff|../lib/all/chirp.wav|../lib/all/chirp-delay50ms.wav|--offset|abc
image|../lib/all/sine-1khz.wav|-o|image1.png
image|../lib/all/sine-1khz.wav|--out|image2.png|--palette|magma|--gamma|1.2|--floor|-100|--fft|1024|--channel|1|--columns|512
image|../lib/all/sine-dual-channel.wav|-o|image3.png|--channel|2
image|../lib/all/sine-1khz.wav|-o|image4.png|--palette|nope
image|local.wav
image|../lib/all|--json
image|../lib/all
image|../lib/all/sine-1khz.wav|--gamma|0
image|../lib/all/sine-1khz.wav|--fft|8
image|../lib/all/sine-1khz.wav|--channel|0
image|../lib/all/sine-1khz.wav|--bogus
image|../lib/all/sine-1khz.wav|-o
image|../lib/all/notaudio.txt|-o|image5.png
image
# An --html write that fails (no such folder): the verb must say why on
# stderr and exit 2 before printing anything.
audit|../lib/A|--html|../lib/no-such-dir/a.html
audit|../lib/A|--html|../lib/no-such-dir/a.html|--json
dupes|../lib/A|../lib/B|--html|../lib/no-such-dir/d.html
manifest|../lib/all|--html|../lib/no-such-dir/m.html
# A file whose read throws, which check counts as corrupt.
check|../lib/all/notaudio.txt|../lib/all/sine-1khz.wav
check|../lib/all/notaudio.txt|../lib/all/sine-1khz.wav|--json
# The same undecodable file beside a good one: report prints an error row and
# audit an integrity=ERROR row. A missing path no longer reaches either, it
# exits 2 before anything is analyzed.
report|../lib/all/notaudio.txt|../lib/all/sine-1khz.wav
audit|../lib/all/notaudio.txt|../lib/all/sine-1khz.wav
# Path shapes refused before anything is analyzed (exit 2): a folder among
# other paths, and more than one folder for scan.
report|../lib/all/sine-1khz.wav|../lib/A
scan|../lib/A|../lib/B
'@

# Run under a profile whose cache cannot be opened: a folder sits where
# audit-cache.db belongs, which SQLite reports as unopenable rather than as
# corrupt, so nothing gets deleted. Each cached verb must say so and carry on.
$NoCacheMatrix = @'
audit|../lib/A
dupes|../lib/A|../lib/B
manifest|../lib/A
'@

# Text only one print branch writes. The table after a run counts each one
# per side; a 0 under head is a branch no matrix line reaches.
$Markers = @(
    '[LOSSLESS]', '[SUSPECT ]', '[LOSSY   ]', '[UPSAMPLE]', '[MIXED   ]', '[UNKNOWN ]', '[ERROR   ]' # scan
    '  error: '                                                      # report: a file that failed
    '[OK]', '[SUSPECT]', '[CORRUPT]'                                 # check
    'integrity=ERROR'                                                # audit: a file that failed
    'found by audio', '! not analyzed:'                              # dupes
    '[unreadable]', '(no artist or title)', '(untagged)', ' unreadable.' # inventory
    '(pinned)', 'warning: alignment confidence', 'note: alignment drifts' # diff
    'perfect null', 'SAME      null depth', 'DIFFERS   null depth'   # diff verdicts
    'unknown palette'                                                # image
    'cache unavailable'                                              # audit, dupes, manifest
)

$InvocationTimeoutSeconds = 300

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$utf8 = New-Object System.Text.UTF8Encoding $false

function Join-Arguments([string[]] $Arguments) {
    # ProcessStartInfo.ArgumentList does not exist on the .NET Framework under
    # Windows PowerShell 5.1, so the command line is built by hand with the
    # rules the child's parser undoes: quote anything empty or holding a space
    # or a quote, escape embedded quotes, and double the backslashes that end
    # up in front of a quote.
    $quoted = foreach ($a in $Arguments) {
        if ($a -ne '' -and $a -notmatch '[\s"]') { $a }
        else { '"' + (($a -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"' }
    }
    $quoted -join ' '
}

function Invoke-Program {
    # Through Process rather than the call operator: on Windows PowerShell 5.1,
    # redirecting a native command's stderr wraps every line in an error record
    # and sets $? to false even when the exit code is 0.
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [string] $WorkingDirectory,
        [hashtable] $Environment,
        [int] $TimeoutSeconds = 1800
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = Join-Arguments $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($Environment) { foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = $Environment[$k] } }
    $p = [System.Diagnostics.Process]::Start($psi)
    try {
        # Closed at once, so a program that reads stdin sees the end of it
        # instead of waiting forever.
        $p.StandardInput.Close()
        $out = New-Object System.IO.MemoryStream
        $err = New-Object System.IO.MemoryStream
        $outCopy = $p.StandardOutput.BaseStream.CopyToAsync($out)
        $errCopy = $p.StandardError.BaseStream.CopyToAsync($err)
        $exit = "timeout after $TimeoutSeconds s"
        if ($p.WaitForExit($TimeoutSeconds * 1000)) { $exit = $p.ExitCode }
        else { try { $p.Kill() } catch { } }
        # A grandchild that inherited the pipes can keep them open after the
        # program itself has gone; take what arrived rather than hang.
        [void] $outCopy.Wait(30000)
        [void] $errCopy.Wait(30000)
        [pscustomobject] @{
            Exit = $exit
            Out  = $utf8.GetString($out.ToArray())
            Err  = $utf8.GetString($err.ToArray())
        }
    }
    finally { $p.Dispose() }
}

function Resolve-Commit([string] $Name) {
    $r = Invoke-Program -FilePath git -Arguments @('-C', $repo, 'rev-parse', '--verify', '--quiet', '--short=12', "${Name}^{commit}") -WorkingDirectory $repo
    if ($r.Exit -ne 0) { throw "'$Name' is not a commit in this repository." }
    $r.Out.Trim()
}

function Get-Export([string] $Sha) {
    $dir = Join-Path $WorkDir "src-$Sha"
    if (Test-Path -LiteralPath $dir) { return $dir }
    # Unpacked under a temporary name and renamed once complete, so an
    # interrupted run never leaves a half export for a later run to reuse.
    $partial = "$dir.partial"
    $zip = "$dir.zip"
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Recurse -Force }
    Write-Host "export $Sha"
    $r = Invoke-Program -FilePath git -Arguments @('-C', $repo, 'archive', '--format=zip', '-o', $zip, $Sha) -WorkingDirectory $repo
    if ($r.Exit -ne 0) { throw "git archive failed for ${Sha}: $($r.Err.Trim())" }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $partial)
    Remove-Item -LiteralPath $zip
    Rename-Item -LiteralPath $partial -NewName (Split-Path -Leaf $dir)
    $dir
}

function Build-Cli([string] $Src, [string] $Sha) {
    Write-Host "build $Sha"
    $log = Join-Path $WorkDir "build-$Sha.log"
    $r = Invoke-Program -FilePath dotnet -Arguments @('build', 'src\Spektra.Cli\Spektra.Cli.csproj', '-c', 'Release', '--disable-build-servers', '-v:q') -WorkingDirectory $Src
    [System.IO.File]::WriteAllText($log, $r.Out + $r.Err, $utf8)
    if ($r.Exit -ne 0) {
        $tail = (($r.Out + $r.Err) -split '\r?\n' | Select-Object -Last 25) -join "`n"
        throw "the build of $Sha failed (exit $($r.Exit), log $log):`n$tail"
    }
    # Found rather than named: the apphost carries the assembly name, which
    # was spektra.exe before the CLI became spektra-cli.exe.
    $cli = Get-ChildItem -LiteralPath (Join-Path $Src 'src\Spektra.Cli\bin\Release') -Recurse -Filter '*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $cli) { throw "the build of $Sha left no exe under $Src\src\Spektra.Cli\bin\Release" }
    $cli.FullName
}

function Invoke-Ffmpeg([string[]] $Arguments) {
    $r = Invoke-Program -FilePath ffmpeg -Arguments (@('-nostdin', '-v', 'error', '-y') + $Arguments) -WorkingDirectory $lib
    if ($r.Exit -ne 0) { throw "ffmpeg failed building the library: $($r.Err.Trim())" }
}

function ConvertTo-Comparable([string] $Text, $Paths) {
    $t = $Text.Replace("`r`n", "`n")
    $t = [regex]::Replace($t, 'generated \d{4}-\d{2}-\d{2} \d{2}:\d{2}', 'generated TIME')
    $t = [regex]::Replace($t, '(?m)^(spektra(?:-cli)?) \d+\.\d+\.\d+\S*$', '$1 VERSION')
    # Most specific first; each in its plain and its JSON-escaped spelling.
    foreach ($kv in $Paths.GetEnumerator()) {
        $t = $t.Replace($kv.Value, $kv.Key).Replace($kv.Value.Replace('\', '\\'), $kv.Key)
    }
    $t
}

function Get-Sha256([byte[]] $Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { ([System.BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Invoke-Side([string] $Name, [string] $Cli, $Paths) {
    $run = $Paths['<run>']
    $profileDir = $Paths['<profile>']
    $noCache = $Paths['<profile-nocache>']
    # The known folders come back empty unless they exist, so they are made
    # first. The no-cache profile gets a folder where the database belongs.
    foreach ($d in $run, "$profileDir\AppData\Roaming", "$profileDir\AppData\Local",
        "$noCache\AppData\Roaming\Spektra\audit-cache.db", "$noCache\AppData\Local") {
        [void] [System.IO.Directory]::CreateDirectory($d)
    }
    Copy-Item -LiteralPath (Join-Path $lib 'all\sine-1khz.wav') -Destination (Join-Path $run 'local.wav')

    $sets = @(
        @{ Lines = $Matrix; Profile = $profileDir; Note = '' },
        @{ Lines = $NoCacheMatrix; Profile = $noCache; Note = '(cache unopenable) ' }
    )
    $sb = New-Object System.Text.StringBuilder
    $n = 0
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host -NoNewline "run $Name "
    foreach ($set in $sets) {
        $childEnv = @{
            USERPROFILE  = $set.Profile
            APPDATA      = Join-Path $set.Profile 'AppData\Roaming'
            LOCALAPPDATA = Join-Path $set.Profile 'AppData\Local'
        }
        foreach ($line in ($set.Lines -split '\r?\n')) {
            if ($line -eq '' -or $line.StartsWith('#')) { continue }
            $n++
            $cliArgs = @()
            if ($line -ne '(no args)') { $cliArgs = $line -split '\|' }
            $r = Invoke-Program -FilePath $Cli -Arguments $cliArgs -WorkingDirectory $run -Environment $childEnv -TimeoutSeconds $InvocationTimeoutSeconds
            [void] $sb.Append("### [$n] $($set.Note)spektra-cli $line`n")
            [void] $sb.Append("exit: $($r.Exit)`n--- stdout`n$($r.Out)--- stderr`n$($r.Err)`n")
            Write-Host -NoNewline '.'
        }
    }
    Write-Host " $n invocations in $([int]$clock.Elapsed.TotalSeconds) s"

    # The pages stamp the minute they were written, so they are hashed after
    # the same normalising as the transcript.
    [void] $sb.Append("### files written`n")
    foreach ($f in Get-ChildItem -LiteralPath $run -File | Where-Object { $_.Extension -in '.png', '.html' } | Sort-Object Name) {
        $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
        if ($f.Extension -eq '.html') { $bytes = $utf8.GetBytes((ConvertTo-Comparable ($utf8.GetString($bytes)) $Paths)) }
        [void] $sb.Append("$(Get-Sha256 $bytes)  $($f.Name)`n")
    }
    [pscustomobject] @{ Text = (ConvertTo-Comparable $sb.ToString() $Paths); Invocations = $n }
}

if ($args.Count -gt 0) {
    [Console]::Error.WriteLine("compare-cli: unexpected argument(s): $($args -join ' '). Usage: -Base <commit> -Head <commit> [-WorkDir <dir>]")
    exit 2
}

try {
    if (-not $Base -or -not $Head) { throw 'give both -Base <commit> and -Head <commit>.' }
    foreach ($tool in 'git', 'dotnet', 'ffmpeg', 'ffprobe') {
        if (-not (Get-Command $tool -CommandType Application -ErrorAction SilentlyContinue)) {
            throw "$tool is not on PATH. ffmpeg and ffprobe must be there because the CLI runs in a throwaway profile and cannot see a copy downloaded into the real one."
        }
    }
    $repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP 'spektra-compare-cli' }
    $WorkDir = [System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDir)).TrimEnd('\')
    if (($WorkDir + '\').StartsWith($repo.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "-WorkDir must be outside the repository, where .NET tooling would find the exported project copies: $WorkDir"
    }

    $baseSha = Resolve-Commit $Base
    $headSha = Resolve-Commit $Head
    [void] [System.IO.Directory]::CreateDirectory($WorkDir)
    Write-Host "base $Base = $baseSha, head $Head = $headSha, work folder $WorkDir"

    $baseSrc = Get-Export $baseSha
    $headSrc = Get-Export $headSha
    $baseCli = Build-Cli $baseSrc $baseSha
    $headCli = Build-Cli $headSrc $headSha

    $cmp = Join-Path $WorkDir ("compare-$baseSha-$headSha-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $lib = Join-Path $cmp 'lib'
    # The matrix lives in this script, so the files it names come from the
    # same checkout; the commits under test only supply the two builds.
    $fixtures = Join-Path $repo 'tests\fixtures'
    Write-Host "library $lib"
    foreach ($d in 'all', 'A', 'B') { [void] [System.IO.Directory]::CreateDirectory((Join-Path $lib $d)) }
    Get-ChildItem -LiteralPath $fixtures -File | Copy-Item -Destination (Join-Path $lib 'all')
    # Duplicate detection ignores anything under 20 s and every fixture is 3 s
    # long, so the two-folder library is made of 24 s loops of them.
    $loops = [ordered] @{
        'A\Tones Song.wav'   = 'tones-a.wav'
        'A\only in a.wav'    = 'noise.wav'
        'A\Shared Title.wav' = 'sine-1khz.wav'
        'B\Shared Title.wav' = 'tones-b.wav'
        'B\only in b.wav'    = 'chirp.wav'
    }
    foreach ($kv in $loops.GetEnumerator()) {
        Invoke-Ffmpeg @('-stream_loop', '7', '-i', (Join-Path $fixtures $kv.Value), '-c', 'copy', (Join-Path $lib $kv.Key))
    }
    Invoke-Ffmpeg @('-i', (Join-Path $lib 'A\Tones Song.wav'), '-codec:a', 'libmp3lame', '-b:a', '128k', (Join-Path $lib 'B\completely unrelated name.mp3'))
    Copy-Item -LiteralPath (Join-Path $fixtures 'corrupt.flac'), (Join-Path $fixtures 'notaudio.txt') -Destination (Join-Path $lib 'A')
    Copy-Item -LiteralPath (Join-Path $fixtures 'sine-1khz.mp3') -Destination (Join-Path $lib 'B')

    $sides = [ordered] @{}
    foreach ($side in @(@{ Name = 'base'; Cli = $baseCli; Src = $baseSrc }, @{ Name = 'head'; Cli = $headCli; Src = $headSrc })) {
        $paths = [ordered] @{
            '<profile-nocache>' = Join-Path $cmp "profile-$($side.Name)-nocache"
            '<profile>'         = Join-Path $cmp "profile-$($side.Name)"
            '<run>'             = Join-Path $cmp "run-$($side.Name)"
            '<src>'             = $side.Src
            '<cmp>'             = $cmp
            '<work>'            = $WorkDir
        }
        $sides[$side.Name] = Invoke-Side $side.Name $side.Cli $paths
    }
    $baseText = $sides['base'].Text
    $headText = $sides['head'].Text
    $baseOut = Join-Path $cmp 'transcript-base.txt'
    $headOut = Join-Path $cmp 'transcript-head.txt'
    [System.IO.File]::WriteAllText($baseOut, $baseText, $utf8)
    [System.IO.File]::WriteAllText($headOut, $headText, $utf8)

    # The isolation proof: everything the runs wrote into the profiles.
    Write-Host 'written into the throwaway profiles:'
    foreach ($name in 'base', 'head') {
        foreach ($prof in "profile-$name", "profile-$name-nocache") {
            $root = Join-Path $cmp $prof
            $files = @(Get-ChildItem -LiteralPath $root -Recurse -Force -File | ForEach-Object { $_.FullName.Substring($root.Length + 1) })
            $listed = '(nothing)'
            if ($files.Count -gt 0) { $listed = $files -join ', ' }
            Write-Host "  ${prof}: $listed"
        }
    }

    Write-Host 'output markers (base, head; 0 under head = a print branch no matrix line reaches):'
    foreach ($m in $Markers) {
        $inBase = [regex]::Matches($baseText, [regex]::Escape($m)).Count
        $inHead = [regex]::Matches($headText, [regex]::Escape($m)).Count
        $flag = ''
        if ($inHead -eq 0) { $flag = '   <- never reached' }
        Write-Host ('  {0,5} {1,5}  {2}{3}' -f $inBase, $inHead, $m, $flag)
    }

    Write-Host "transcripts: $baseOut"
    Write-Host "             $headOut"
    $invocations = $sides['head'].Invocations
    if ([string]::Equals($baseText, $headText, [StringComparison]::Ordinal)) {
        $lines = ($headText -split "`n").Count
        Write-Host "IDENTICAL: $invocations invocations, $lines lines (base $baseSha, head $headSha)"
        exit 0
    }

    $baseBlocks = @($baseText -split '(?m)^(?=### )' | Where-Object { $_ })
    $headBlocks = @($headText -split '(?m)^(?=### )' | Where-Object { $_ })
    $count = [Math]::Max($baseBlocks.Count, $headBlocks.Count)
    $differing = @(for ($i = 0; $i -lt $count; $i++) {
            $b = ''; $h = ''
            if ($i -lt $baseBlocks.Count) { $b = $baseBlocks[$i] }
            if ($i -lt $headBlocks.Count) { $h = $headBlocks[$i] }
            if (-not [string]::Equals($b, $h, [StringComparison]::Ordinal)) { , @($b, $h) }
        })
    Write-Host "DIFFERENT: $($differing.Count) of $count blocks differ (one per invocation, plus the files written; base $baseSha, head $headSha). The first ones, - base + head:"
    foreach ($pair in $differing | Select-Object -First 10) {
        $bLines = $pair[0] -split "`n"
        $hLines = $pair[1] -split "`n"
        Write-Host ''
        Write-Host (@($bLines + $hLines) | Where-Object { $_ } | Select-Object -First 1)
        $delta = Compare-Object -ReferenceObject $bLines -DifferenceObject $hLines -CaseSensitive
        $shown = @($delta | Where-Object { $_.SideIndicator -eq '<=' } | ForEach-Object { "  - $($_.InputObject)" }) +
            @($delta | Where-Object { $_.SideIndicator -eq '=>' } | ForEach-Object { "  + $($_.InputObject)" })
        $shown | Select-Object -First 12 | ForEach-Object { Write-Host $_ }
    }
    Write-Host ''
    Write-Host "full diff: git diff --no-index `"$baseOut`" `"$headOut`""
    exit 1
}
catch {
    [Console]::Error.WriteLine("compare-cli: $($_.Exception.Message)")
    exit 2
}
