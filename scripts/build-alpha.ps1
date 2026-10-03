# Builds the Windows alpha of Stars of Darien for playtesters and packs it as
# a zip: the game folder, README.txt, KNOWN-ISSUES.txt, LICENSE.txt and
# THIRD-PARTY-NOTICES.txt.
# Unity runs in batch mode, through the shared heavy lock when this machine
# has one. The content report and Unity's log go beside the zip, and so does
# flat-features.txt, the scenery that still draws flat (coverage.py).
#   powershell -File scripts/build-alpha.ps1 [-Out D:\OKBuild\alpha] [-Alpha "Alpha 1"] [-PackOnly]
# Close the Unity editor on this checkout first, since a project opens in one
# editor at a time. -PackOnly packs the last build of this commit again.
param(
    [string]$Unity = "D:\Unity\6000.3.25f1\Editor\Unity.exe",
    [string]$Out = "D:\OKBuild\alpha",
    [string]$Alpha = "Alpha 1",
    [switch]$PackOnly
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "unity"
$commit = (& git -C $root rev-parse --short HEAD).Trim()
$name = "StarsOfDarien-" + ($Alpha -replace '\s', '') + "-" + $commit
$stage = Join-Path $Out $name
$game = Join-Path $stage "Stars of Darien"
$report = Join-Path $Out "$name-content-report.txt"
$log = Join-Path $Out "$name-build.log"
New-Item -ItemType Directory -Force $Out | Out-Null

if (-not $PackOnly) {
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    $unityArgs = @("-batchmode", "-quit", "-buildTarget", "Win64", "-projectPath", $project,
        "-executeMethod", "OpenKingdomsUnity.Build.WindowsAlpha", "-okOut", $game, "-okAlpha", $Alpha,
        "-okReport", $report, "-logFile", $log)
    $bash = "C:\Program Files\Git\bin\bash.exe"
    $lock = "D:\Locks\heavylock.sh"
    "Building $name with Unity in batch mode, log in $log"
    if ((Test-Path $lock) -and (Test-Path $bash)) {
        # Every argument in single quotes for bash, the editor's path as bash writes it.
        $posix = { param($p) if ($p -match '^([A-Za-z]):[\\/](.*)$') { "/" + $Matches[1].ToLower() + "/" + ($Matches[2] -replace '\\', '/') } else { $p } }
        $quoted = ($unityArgs | ForEach-Object { "'" + ($_ -replace "'", "'\''") + "'" }) -join " "
        & $bash -lc ("source /d/Locks/heavylock.sh; with_heavy_lock '" + (& $posix $Unity) + "' " + $quoted)
    } else {
        & $Unity @unityArgs | Out-Null
    }
    $exit = $LASTEXITCODE
    $passed = (Test-Path $log) -and (Select-String -Path $log -Pattern "OKBUILD RESULT PASS" -Quiet)
    if ($exit -ne 0 -or -not $passed) {
        "Build FAILED (exit $exit). The lines that say why:"
        if (Test-Path $log) { Select-String -Path $log -Pattern "OKBUILD|error CS|Scripts have compiler errors" | Select-Object -Last 25 | ForEach-Object { "  " + $_.Line } }
        exit 1
    }
}
if (-not (Test-Path (Join-Path $game "Stars of Darien.exe"))) { "No build at $game"; exit 1 }

# The version as the build stamped it.
$version = "$Alpha ($commit)"
if (Test-Path $log) {
    $done = Select-String -Path $log -Pattern "OKBUILD done: Stars of Darien (.+)$" | Select-Object -Last 1
    if ($done) { $version = $done.Matches[0].Groups[1].Value.Trim() }
}

# Unity's debug information for Burst stays out of the zip.
Get-ChildItem $game -Directory -Filter "*_DoNotShip" | ForEach-Object {
    $aside = Join-Path $Out ("$name-" + $_.Name)
    if (Test-Path $aside) { Remove-Item -Recurse -Force $aside }
    Move-Item $_.FullName $aside
}

$utf8 = New-Object System.Text.UTF8Encoding $false
function Write-Text([string]$path, [string]$text) {
    $text = ($text -replace "`r`n", "`n") -replace "`n", "`r`n"
    [System.IO.File]::WriteAllText($path, $text, $utf8)
}
function Read-Text([string]$path) { [System.IO.File]::ReadAllText($path) }

$pack = Join-Path $root "packaging\alpha"
foreach ($doc in "README.txt", "KNOWN-ISSUES.txt") {
    Write-Text (Join-Path $stage $doc) ((Read-Text (Join-Path $pack $doc)) -replace "@VERSION@", $version)
}

$rule = "`n`n" + ("-" * 76) + "`n`n"
Write-Text (Join-Path $stage "LICENSE.txt") ((Read-Text (Join-Path $root "LICENSE.unity-exception")) + $rule + (Read-Text (Join-Path $root "LICENSE")))

$engineCommit = "(see engine/VERSION in the source)"
$ver = Read-Text (Join-Path $root "engine\VERSION")
if ($ver -match 'unity-embed ([0-9a-f]{7,40})') { $engineCommit = $Matches[1] }
$notices = (Read-Text (Join-Path $pack "THIRD-PARTY-NOTICES.txt")) -replace "@ENGINE_COMMIT@", $engineCommit
foreach ($font in "Cinzel", "EBGaramond", "UncialAntiqua") {
    $notices += $rule + "FONT: $font`n`n" + (Read-Text (Join-Path $project "Assets\Game\Resources\Fonts\$font-OFL.txt"))
}
Write-Text (Join-Path $stage "THIRD-PARTY-NOTICES.txt") $notices

$zip = Join-Path $Out "$name.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)

# Which scenery still draws flat over every map in the install, for
# hand-building later. It reports and never fails the build.
$flat = Join-Path $Out "flat-features.txt"
$python = Get-Command python -ErrorAction SilentlyContinue
if ($python) {
    & $python.Source (Join-Path $root "tools\sprite-replace\coverage.py") --models $game --out $flat --title "Stars of Darien $version" |
        ForEach-Object { "  $_" }
} else { "No python on the path, so no list of flat scenery" }

"Stars of Darien $version"
"Zip: $zip ($mb MB)"
"Content report: $report"
exit 0
