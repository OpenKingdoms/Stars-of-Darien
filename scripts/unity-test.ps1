# Run the Unity EditMode and PlayMode tests headless. Close the Unity
# editor on this project first, since a project opens in one editor at a time.
#   powershell -File scripts/unity-test.ps1 [-Unity D:\Unity\6000.3.25f1\Editor\Unity.exe]
param(
    [string]$Unity = "D:\Unity\6000.3.25f1\Editor\Unity.exe",
    [string]$Build = "D:\OKBuild\unity-test"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force $Build | Out-Null

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
    # With a compile error Unity runs the last good assemblies and can
    # report a clean pass, so the log decides.
    if ((Test-Path $log) -and (Select-String -Path $log -Pattern "Scripts have compiler errors" -Quiet)) {
        "Unity ${platform}: scripts have compiler errors, see $log"
        Select-String -Path $log -Pattern "error CS" | Select-Object -First 5 | ForEach-Object { "  " + $_.Line }
        $code = 1
        continue
    }
    if (Test-Path $results) {
        [xml]$x = Get-Content $results
        $r = $x.'test-run'
        "Unity ${platform}: $($r.passed) passed, $($r.failed) failed, $($r.total) total"
    } else {
        "Unity ${platform}: no results (exit $exit). See $log"
    }
}
exit $code
