#!/usr/bin/env bash
# Build okengine (the OpenKingdoms engine as a library) for x64 and copy
# it, with SDL2.dll, into the Unity project's plugin folder. Windows, from
# Git Bash. Close the Unity editor first if it has run the engine, since
# it keeps the DLL open.
#   OK_ENGINE_SRC  an OpenKingdoms checkout with ok_embed.h (the unity-embed branch)
#   OK_ENGINE_BUILD  the x64 build tree, on a drive with room
#   OK_GAME_DIR, OK_DATA_DIR  the install the engine's own test reads
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="${OK_ENGINE_SRC:-C:/Projects/wt-embed}"
build="${OK_ENGINE_BUILD:-D:/OKBuild/embed64}"
game="${OK_GAME_DIR:-C:/GOG Games/Total Annihilation Kingdoms}"
data="${OK_DATA_DIR:-C:/Projects/TAK-RE/data/extracted}"
vcpkg="${VCPKG_ROOT:-C:/vcpkg}"
if [ ! -f "$build/CMakeCache.txt" ]; then
    cmake -S "$src" -B "$build" -G "Visual Studio 18 2026" -A x64 \
        -DCMAKE_TOOLCHAIN_FILE="$vcpkg/scripts/buildsystems/vcpkg.cmake" \
        -DVCPKG_TARGET_TRIPLET=x64-windows -DVCPKG_MANIFEST_NO_DEFAULT_FEATURES=ON \
        -DTAK_BUILD_EMBED=ON -DTAK_GAME_DIR="$game" -DTAK_DATA_DIR="$data"
fi
cmake --build "$build" --config Release --target okengine test_embed -- -m:2
dest="$root/unity/Assets/Plugins/x86_64"
mkdir -p "$dest"
cp "$build/src/Release/okengine.dll" "$build/src/Release/SDL2.dll" "$dest/"
echo "plugin: $dest/okengine.dll and SDL2.dll"
