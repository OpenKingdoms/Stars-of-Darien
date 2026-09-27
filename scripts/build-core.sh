#!/usr/bin/env bash
# Build the core, run its tests, and copy the plugin into the Unity
# project. Run from Git Bash on Windows or a shell on Linux/macOS.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
build="${OKCORE_BUILD:-$root/build/core}"
cmake -S "$root/core" -B "$build" -DCMAKE_BUILD_TYPE=Release
cmake --build "$build" --config Release
ctest --test-dir "$build" -C Release --output-on-failure
dest="$root/unity/Assets/Plugins/x86_64"
mkdir -p "$dest"
for f in "$build/Release/okcore.dll" "$build/libokcore.so" "$build/libokcore.dylib"; do
    [ -f "$f" ] && cp "$f" "$dest/" && echo "plugin: $dest/$(basename "$f")"
done
