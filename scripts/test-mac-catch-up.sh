#!/usr/bin/env bash
# Runs scripts/mac-catch-up.sh against throwaway repositories: a checkout
# behind GitHub, one with changes, one whose committed Mac library lags
# engine/VERSION while the mac-engine branch has the right one, and one
# with no Mac build anywhere. Runs on a Mac and in Git Bash.
#   bash scripts/test-mac-catch-up.sh
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
script="$here/scripts/mac-catch-up.sh"
work="$(mktemp -d "${TMPDIR:-/tmp}/catch-up-test.XXXXXX")"
trap 'rm -rf "$work"' EXIT

# The log, the launchd agent and git's settings stay in the sandbox.
export HOME="$work/home" SOD_GH="$work/no-gh" GIT_CONFIG_NOSYSTEM=1
mkdir -p "$HOME"
git config --global user.name test
git config --global user.email test@example.invalid
git config --global init.defaultBranch main
git config --global core.autocrlf false

fails=0
check() {
    if eval "$2"; then echo "ok   $1"; else echo "FAIL $1"; fails=$((fails + 1)); fi
}
version() {
    printf 'okengine-api23.dll  okengine API 23, unity-embed %s, published at openkingdoms-unity 0000000\n' "$1"
    printf 'SDL2.dll            SDL 2\n'
    [ -z "${2:-}" ] || printf 'okengine-api23.dylib  okengine API 23 for macOS arm64, unity-embed %s, published at openkingdoms-unity 0000000\n' "$2"
}
publish() {    # publish <seed dir> <dll embed> <dylib embed or empty> <dylib bytes or empty> <message>
    version "$2" "$3" > "$1/engine/VERSION"
    if [ -n "$4" ]; then printf '%s' "$4" > "$1/engine/okengine-api23.dylib"; else rm -f "$1/engine/okengine-api23.dylib"; fi
    git -C "$1" add -A && git -C "$1" commit -q -m "$5"
}
run() { (cd "$work" && SOD_DIR="$mac" bash "$script" "$@" > "$work/out.txt" 2>&1); }
plugin() { cat "$mac/unity/Assets/Plugins/macOS/libokengine.dylib" 2>/dev/null; }
record() { cat "$mac/unity/Library/OkEngine/installed.json" 2>/dev/null || true; }
sha() { printf '%s' "$1" | { sha256sum 2>/dev/null || shasum -a 256; } | cut -d' ' -f1; }

# GitHub: main with build A of the engine for both platforms.
git init -q --bare "$work/origin.git"
seed="$work/seed"
git init -q "$seed"
mkdir -p "$seed/unity/Assets/Engine" "$seed/unity/Assets/Game/Editor" "$seed/engine"
echo 'public const int ApiVersion = 23;' > "$seed/unity/Assets/Engine/OkEngine.cs"
cp "$here/unity/Assets/Game/Editor/EngineInstaller.cs" "$seed/unity/Assets/Game/Editor/"
printf 'unity/Assets/Plugins/macOS/\nunity/Assets/Plugins/macOS.meta\nunity/Library/\n' > "$seed/.gitignore"
echo readme > "$seed/README.md"
publish "$seed" aaaaaaa aaaaaaa "build A" "Build A"
git -C "$seed" remote add origin "$work/origin.git"
git -C "$seed" push -q origin main
mac="$work/mac"
git clone -q "$work/origin.git" "$mac"

echo "== A clean checkout behind GitHub"
publish "$seed" bbbbbbb bbbbbbb "build B" "Build B"
git -C "$seed" push -q origin main
run || true
check "it fast-forwards" '[ "$(git -C "$mac" rev-parse HEAD)" = "$(git -C "$seed" rev-parse HEAD)" ]'
check "it installs the committed library" '[ "$(plugin)" = "build B" ]'
check "the editor's record owns it" 'record | grep -q "\"libokengine.dylib\": \"$(sha "build B")\""'
check "it writes the editor's import settings" 'grep -q "guid: 8d2b6f0e4c1a4e7b9f3d5a6c0b2e1f47" "$mac/unity/Assets/Plugins/macOS/libokengine.dylib.meta"'
check "it says what it did" 'grep -q "unity-embed bbbbbbb" "$work/out.txt"'
check "it logs to a file" 'grep -q "unity-embed bbbbbbb" "$HOME/Library/Logs/stars-of-darien-catch-up.log"'
check "the checkout stays clean" '[ -z "$(git -C "$mac" status --porcelain)" ]'

echo "== A checkout with changes"
publish "$seed" ccccccc ccccccc "build C" "Build C"
git -C "$seed" push -q origin main
before=$(git -C "$mac" rev-parse HEAD)
echo edited >> "$mac/README.md"
run || true
check "it pulls nothing" '[ "$(git -C "$mac" rev-parse HEAD)" = "$before" ]'
check "it installs nothing" '[ "$(plugin)" = "build B" ]'
check "the change stays" 'grep -q edited "$mac/README.md"'
check "it says why" 'grep -q "changes" "$work/out.txt"'
git -C "$mac" checkout -q -- README.md

echo "== The Mac library waits on the mac-engine branch"
publish "$seed" ddddddd ccccccc "build C" "Windows build D"
git -C "$seed" push -q origin main
git -C "$seed" checkout -q -b mac-engine
publish "$seed" ddddddd ddddddd "build D" "Mac build D"
git -C "$seed" push -q origin mac-engine
git -C "$seed" checkout -q main
run || true
check "it fast-forwards" '[ "$(git -C "$mac" rev-parse HEAD)" = "$(git -C "$seed" rev-parse main)" ]'
check "it installs the branch's library" '[ "$(plugin)" = "build D" ]'
check "the editor leaves it alone" '! record | grep -q libokengine.dylib'
check "the checkout stays clean" '[ -z "$(git -C "$mac" status --porcelain)" ]'

echo "== The branch lands on main"
git -C "$seed" merge -q --ff-only mac-engine
git -C "$seed" push -q origin main
run || true
check "the library stays" '[ "$(plugin)" = "build D" ]'
check "the editor owns it again" 'record | grep -q "\"libokengine.dylib\": \"$(sha "build D")\""'

echo "== No Mac build of the engine anywhere"
publish "$seed" eeeeeee ddddddd "build D" "Windows build E"
git -C "$seed" push -q origin main
run || true
check "it keeps the committed library" '[ "$(plugin)" = "build D" ]'
check "it says how to get one" 'grep -q "build-engine-mac.sh" "$work/out.txt"'

echo "== A local build is kept aside"
echo "a local build" > "$mac/unity/Assets/Plugins/macOS/libokengine.dylib"
publish "$seed" fffffff fffffff "build F" "Build F"
git -C "$seed" push -q origin main
run || true
check "it installs the committed library" '[ "$(plugin)" = "build F" ]'
check "the local build is kept" 'grep -l "a local build" "$mac"/unity/Library/OkEngine/replaced-*-libokengine.dylib > /dev/null 2>&1'

if [ "$(uname)" = Darwin ]; then
    echo "== The launchd agent"
    SOD_DIR="$mac" SOD_LAUNCHCTL=true bash "$script" install-auto > "$work/out.txt" 2>&1 || true
    agent="$HOME/Library/LaunchAgents/net.openkingdoms.stars-of-darien.catch-up.plist"
    check "it writes a valid agent" 'plutil -lint "$agent" > /dev/null'
    check "the agent runs this script for this checkout" 'grep -q "$script" "$agent" && grep -q "<string>$mac</string>" "$agent"'
    SOD_DIR="$mac" SOD_LAUNCHCTL=true bash "$script" remove-auto > "$work/out.txt" 2>&1 || true
    check "remove-auto takes it away" '[ ! -f "$agent" ]'
fi

if [ "$fails" -gt 0 ]; then echo "$fails failed. The last run said:"; cat "$work/out.txt"; exit 1; fi
echo "all passed"
