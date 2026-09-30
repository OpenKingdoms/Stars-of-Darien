#!/usr/bin/env bash
# Run the Unity EditMode and PlayMode tests on Linux in batch mode with the
# real engine and the game files, as a cloud session does. Each step is
# skipped when what it makes is already there:
#   1. the Linux editor the project asks for, under ~/unity, and a Unity
#      Personal seat for it
#   2. okengine from OpenKingdoms' unity-embed branch, built as
#      libokengine.so, checked with its own test_embed, and put in
#      unity/Assets/Plugins/x86_64 for the editor on Linux
#   3. the game files under ~/takdata
# then the suites, with their results XML, logs and a summary in the output
# folder. Exits non-zero when a suite fails or does not run.
#   bash scripts/cloud-unity-test.sh [EditMode] [PlayMode]
# Environment:
#   UNITY_EMAIL, UNITY_PASSWORD  the Unity account the seat is taken for
#   OK_GAME_TAR_URL  the game install as a .tar, and OK_DATA_TAR_URL the
#                    extracted data as a .tar.gz, fetched only when
#                    ~/takdata holds no game
#   OK_GAME_DIR, OK_DATA_DIR  the game and data folders, when not the ones
#                    found under ~/takdata
#   OKU_TEST_OUT     where results go, ~/unity-test by default
#   OKU_TEST_TIMEOUT how long a suite may run, in seconds, 3 hours by default
#   OKU_KEEP_SEAT=1  keep a seat this run took; it is returned at the end
#                    otherwise. A seat that was already there is left alone.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/unity"
out="${OKU_TEST_OUT:-$HOME/unity-test}"
takdata="$HOME/takdata"
src="$HOME/OpenKingdoms"
build="$HOME/okbuild/embed64"
platforms=("$@")
[ ${#platforms[@]} -gt 0 ] || platforms=(EditMode PlayMode)
mkdir -p "$out"

say() { echo "== $*"; }
# Runs a command with the account's email and password kept out of what it prints.
quiet() {
    local text code=0
    text=$("$@" 2>&1) || code=$?
    [ -n "${UNITY_EMAIL:-}" ] && text=${text//"$UNITY_EMAIL"/<email>}
    [ -n "${UNITY_PASSWORD:-}" ] && text=${text//"$UNITY_PASSWORD"/<password>}
    echo "$text" | sed 's/^/   /'
    return $code
}

# ---- 1. The editor and a seat ----
version=$(sed -n 's/^m_EditorVersionWithRevision: \([^ ]*\) (\([0-9a-f]*\))$/\1 \2/p' "$project/ProjectSettings/ProjectVersion.txt")
read -r uversion changeset <<< "$version"
editor="$HOME/unity/$uversion/Editor"
unity="$editor/Unity"
licensing="$editor/Data/Resources/Licensing/Client/Unity.Licensing.Client"
if [ -x "$unity" ]; then
    say "Unity $uversion is at $editor"
else
    say "Downloading Unity $uversion ($changeset) for Linux"
    mkdir -p "$HOME/unity/$uversion"
    archive="$HOME/unity/Unity-$uversion.tar.xz"
    curl -fsSL -o "$archive" "https://download.unity3d.com/download_unity/$changeset/LinuxEditorInstaller/Unity-$uversion.tar.xz"
    tar -xJf "$archive" -C "$HOME/unity/$uversion"
    rm -f "$archive"
fi

took_seat=0
return_seat() {
    if [ "$took_seat" = 1 ] && [ "${OKU_KEEP_SEAT:-}" != 1 ]; then
        say "Returning the Unity seat"
        quiet "$licensing" --deactivate-all --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" || echo "   the seat could not be returned"
    fi
}
trap return_seat EXIT
entitlements=$("$licensing" --showEntitlements 2>/dev/null || true)
if grep -q 'com.unity.editor.headless' <<< "$entitlements"; then
    say "A Unity seat is active"
else
    if [ -z "${UNITY_EMAIL:-}" ] || [ -z "${UNITY_PASSWORD:-}" ]; then
        echo "No Unity seat, and UNITY_EMAIL and UNITY_PASSWORD are not set to take one." >&2
        exit 1
    fi
    say "Activating a Unity seat"
    quiet "$licensing" --activate-all --include-personal --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD"
    took_seat=1
fi

# ---- 2. The engine ----
need=()
for p in libsdl2-dev libavcodec-dev libavformat-dev libswscale-dev ninja-build cmake gcc xvfb xauth libgl1-mesa-dri libglu1-mesa mesa-vulkan-drivers; do
    dpkg -s "$p" > /dev/null 2>&1 || need+=("$p")
done
if [ ${#need[@]} -gt 0 ]; then
    say "Installing ${need[*]}"
    sudo_=""; [ "$(id -u)" = 0 ] || sudo_=sudo
    $sudo_ apt-get update -qq
    DEBIAN_FRONTEND=noninteractive $sudo_ apt-get install -y -qq "${need[@]}" > /dev/null
fi
[ -d "$src/.git" ] || git clone --branch unity-embed https://github.com/OpenKingdoms/OpenKingdoms "$src"

# ---- 3. The game files ----
find_game() { local found; found=$(find "$takdata" -iname data.hpi -printf '%h\n' 2>/dev/null || true); echo "${found%%$'\n'*}"; }
if [ -z "${OK_GAME_DIR:-}" ] && [ -z "$(find_game)" ]; then
    if [ -z "${OK_GAME_TAR_URL:-}" ] || [ -z "${OK_DATA_TAR_URL:-}" ]; then
        echo "No game under $takdata. Set OK_GAME_TAR_URL and OK_DATA_TAR_URL, or OK_GAME_DIR." >&2
        exit 1
    fi
    say "Downloading the game files"
    mkdir -p "$takdata/gog" "$takdata/extracted"
    curl -fsSL "$OK_GAME_TAR_URL" | tar -x -C "$takdata/gog"
    curl -fsSL "$OK_DATA_TAR_URL" | tar -xz -C "$takdata/extracted"
fi
export OK_GAME_DIR="${OK_GAME_DIR:-$(find_game)}"
if [ -z "${OK_DATA_DIR:-}" ] && [ -d "$takdata/extracted" ]; then
    OK_DATA_DIR="$takdata/extracted"
    # The archive holds one folder, extracted/, with the files in it.
    [ -d "$OK_DATA_DIR/extracted" ] && OK_DATA_DIR="$OK_DATA_DIR/extracted"
fi
export OK_DATA_DIR="${OK_DATA_DIR:-}"
say "Game: $OK_GAME_DIR"
say "Data: ${OK_DATA_DIR:-none}"

# The engine's test reads the folders it was configured with, so they are
# set again whenever they change.
if [ ! -f "$build/CMakeCache.txt" ] || ! grep -qxF "TAK_GAME_DIR:PATH=$OK_GAME_DIR" "$build/CMakeCache.txt"; then
    say "Configuring okengine"
    # The Linux build compiles the Bink player against FFmpeg without
    # linking it, so the libraries are named for every link.
    cmake -S "$src" -B "$build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DTAK_BUILD_EMBED=ON \
        -DTAK_GAME_DIR="$OK_GAME_DIR" -DTAK_DATA_DIR="$OK_DATA_DIR" \
        -DCMAKE_C_STANDARD_LIBRARIES="-lavformat -lavcodec -lswscale -lswresample -lavutil" > "$out/engine-configure.log"
fi
say "Building okengine"
cmake --build "$build" --target okengine test_embed -j"$(nproc)" > "$out/engine-build.log"
lib="$build/src/libokengine.so"
if [ ! -f "$build/test_embed.passed" ] || [ "$lib" -nt "$build/test_embed.passed" ]; then
    say "Running test_embed"
    if (cd "$build/src" && SDL_AUDIODRIVER=dummy SDL_VIDEODRIVER=dummy ./test_embed > "$out/test_embed.log" 2>&1); then
        touch "$build/test_embed.passed"
    else
        echo "test_embed failed, see $out/test_embed.log" >&2
        exit 1
    fi
fi
grep '^Results:' "$out/test_embed.log" | tail -1 | sed 's/^/   test_embed: /'

plugins="$project/Assets/Plugins/x86_64"
mkdir -p "$plugins"
if ! cmp -s "$lib" "$plugins/libokengine.so"; then
    say "Putting libokengine.so in Assets/Plugins/x86_64"
    cp "$lib" "$plugins/libokengine.so"
fi
# Loaded by the editor and a player on Linux only. Written again when it
# differs, since Unity takes a meta it cannot read as a plugin for no platform.
meta=$(cat << 'EOF'
fileFormatVersion: 2
guid: 5b0c3f2e9d7a4c1e8f6b2a9d0e4c7b13
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      : Any
    second:
      enabled: 0
      settings:
        Exclude Editor: 0
        Exclude Linux64: 0
        Exclude OSXUniversal: 1
        Exclude Win: 1
        Exclude Win64: 1
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Linux
  - first:
      Standalone: Linux64
    second:
      enabled: 1
      settings:
        CPU: x86_64
  - first:
      Standalone: OSXUniversal
    second:
      enabled: 0
      settings:
        CPU: None
  - first:
      Standalone: Win
    second:
      enabled: 0
      settings:
        CPU: None
  - first:
      Standalone: Win64
    second:
      enabled: 0
      settings:
        CPU: None
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
)
[ "$(cat "$plugins/libokengine.so.meta" 2>/dev/null)" = "$meta" ] || echo "$meta" > "$plugins/libokengine.so.meta"

# ---- The suites ----
# With no GPU the editor draws through Mesa on a virtual display.
code=0
: > "$out/summary.txt"
for platform in "${platforms[@]}"; do
    name=$(echo "$platform" | tr '[:upper:]' '[:lower:]')
    results="$out/unity-$name.xml"
    log="$out/unity-$name.log"
    rm -f "$results"
    say "Unity $platform tests"
    exit_code=0
    SDL_AUDIODRIVER=dummy timeout --kill-after=60 "${OKU_TEST_TIMEOUT:-10800}" xvfb-run -a -s "-screen 0 1920x1080x24" \
        "$unity" -batchmode -projectPath "$project" -runTests -testPlatform "$platform" \
        -testResults "$results" -logFile "$log" > /dev/null 2>&1 || exit_code=$?
    [ "$exit_code" = 0 ] || code=$exit_code
    # With a compile error Unity runs the last good assemblies and can
    # report a clean pass, so the log decides.
    if grep -q "Scripts have compiler errors" "$log" 2>/dev/null; then
        { echo "Unity $platform: scripts have compiler errors, see $log"; grep -m5 "error CS" "$log" | sed 's/^/  /'; } | tee -a "$out/summary.txt"
        code=1
        continue
    fi
    if [ ! -f "$results" ]; then
        echo "Unity $platform: no results (exit $exit_code), see $log" | tee -a "$out/summary.txt"
        [ "$code" != 0 ] || code=1
        continue
    fi
    python3 - "$platform" "$results" << 'EOF' | tee -a "$out/summary.txt"
import sys, xml.etree.ElementTree as ET
platform, path = sys.argv[1], sys.argv[2]
run = ET.parse(path).getroot()
a = run.attrib
print(f"Unity {platform}: {a.get('passed')} passed, {a.get('failed')} failed, {a.get('skipped')} skipped, "
      f"{a.get('inconclusive', '0')} inconclusive, {a.get('total')} total")
def first(case, tag):
    m = case.find(f"{tag}/message")
    text = (m.text or "").strip() if m is not None else ""
    return text.splitlines()[0] if text else ""
for case in run.iter("test-case"):
    r = case.get("result")
    if r == "Failed":
        print(f"  FAILED  {case.get('fullname')}: {first(case, 'failure')}")
for case in run.iter("test-case"):
    if case.get("result") == "Skipped":
        print(f"  skipped {case.get('fullname')}: {first(case, 'reason')}")
EOF
done
say "Results in $out"
exit $code
