# Build the C core, copy the plugin in, and run the Unity EditMode and
# PlayMode tests headless. Close the Unity editor first: it holds okcore.dll open.
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

# EditMode checks the binding, PlayMode boots the demo and lets it play.
$code = 0
foreach ($platform in "EditMode", "PlayMode") {
    $name = $platform.ToLower()
    $results = "$Build\unity-$name.xml"
    $log = "$Build\unity-$name.log"
    if (Test-Path $results) { Remove-Item $results }
    & $Unity -batchmode -projectPath "$root\unity" -runTests -testPlatform $platform `
        -testResults $results -logFile $log | Out-Null
    $exit = $LASTEXITCODE
    if ($exit -ne 0) { $code = $exit }
    if (Test-Path $results) {
        [xml]$x = Get-Content $results
        $r = $x.'test-run'
        "Unity ${platform}: $($r.passed) passed, $($r.failed) failed, $($r.total) total"
    } else {
        "Unity ${platform}: no results (exit $exit). See $log"
    }
}
exit $code
