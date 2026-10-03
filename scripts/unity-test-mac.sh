#!/usr/bin/env bash
# Run the Unity EditMode and PlayMode tests headless on a Mac, as
# scripts/unity-test.ps1 does on Windows. Close the Unity editor on this
# project first, since a project opens in one editor at a time.
#   bash scripts/unity-test-mac.sh [EditMode] [PlayMode]
# Environment:
#   UNITY         the editor, by default the version ProjectVersion.txt
#                 names, where Unity Hub installs it
#   OKU_TEST_OUT  where results and logs go, ~/unity-test by default
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/unity"
version=$(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt")
# Hub puts the Apple silicon editor in <version> or <version>-arm64.
unity="${UNITY:-}"
if [ -z "$unity" ]; then
    for d in "$version" "$version-arm64"; do
        unity="/Applications/Unity/Hub/Editor/$d/Unity.app/Contents/MacOS/Unity"
        [ -x "$unity" ] && break
    done
fi
out="${OKU_TEST_OUT:-$HOME/unity-test}"
platforms=("$@")
[ ${#platforms[@]} -gt 0 ] || platforms=(EditMode PlayMode)
[ -x "$unity" ] || { echo "No Unity $version at $unity. Install it with Unity Hub, or set UNITY."; exit 1; }
mkdir -p "$out"

code=0
for platform in "${platforms[@]}"; do
    name=$(echo "$platform" | tr '[:upper:]' '[:lower:]')
    results="$out/unity-$name.xml"
    log="$out/unity-$name.log"
    rm -f "$results"
    exit_code=0
    "$unity" -batchmode -projectPath "$project" -runTests -testPlatform "$platform" \
        -testResults "$results" -logFile "$log" > /dev/null 2>&1 || exit_code=$?
    [ "$exit_code" = 0 ] || code=$exit_code
    # With a compile error Unity runs the last good assemblies and can
    # report a clean pass, so the log decides.
    if grep -q "Scripts have compiler errors" "$log" 2>/dev/null; then
        echo "Unity $platform: scripts have compiler errors, see $log"
        grep -m5 "error CS" "$log" | sed 's/^/  /'
        code=1
        continue
    fi
    if [ ! -f "$results" ]; then
        echo "Unity $platform: no results (exit $exit_code), see $log"
        [ "$code" != 0 ] || code=1
        continue
    fi
    run=$(grep -m1 -o '<test-run [^>]*>' "$results")
    count() { echo "$run" | sed -n "s/.* $1=\"\([0-9]*\)\".*/\1/p"; }
    echo "Unity $platform: $(count passed) passed, $(count failed) failed, $(count skipped) skipped, $(count total) total"
    grep -o '<test-case [^>]*result="Failed"[^>]*>' "$results" | sed -n 's/.* fullname="\([^"]*\)".*/  FAILED  \1/p' || true
done
echo "Results and logs in $out"
exit $code
