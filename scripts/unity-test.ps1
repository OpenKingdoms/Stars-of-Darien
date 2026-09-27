# Build the C core, copy the plugin in, and run the Unity EditMode tests
# headless. Close the Unity editor first: it holds okcore.dll open.
#   powershell -File scripts/unity-test.ps1 [-Unity D:\Unity\6000.3.25f1\Editor\Unity.exe]
param(
    [string]$Unity = "D:\Unity\6000.3.25f1\Editor\Unity.exe",
    [string]$Build = "D:\OKBuild\okcore"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
cmake -S "$root\core" -B $Build -A x64 | Out-Null
cmake --build $Build --config Release
if ($LASTEXITCODE -ne 0) { exit 1 }
ctest --test-dir $Build -C Release --output-on-failure
if ($LASTEXITCODE -ne 0) { exit 1 }
$plugins = "$root\unity\Assets\Plugins\x86_64"
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item "$Build\Release\okcore.dll" $plugins -Force

$results = "$Build\unity-editmode.xml"
$log = "$Build\unity-editmode.log"
& $Unity -batchmode -projectPath "$root\unity" -runTests -testPlatform EditMode `
    -testResults $results -logFile $log | Out-Null
$code = $LASTEXITCODE
if (Test-Path $results) {
    [xml]$x = Get-Content $results
    $r = $x.'test-run'
    "Unity EditMode: $($r.passed) passed, $($r.failed) failed, $($r.total) total"
} else {
    "No results. See $log"
}
exit $code
