#!/usr/bin/env bash
# Build okengine (the OpenKingdoms engine as a library) on a Mac as
# libokengine.dylib, with SDL2 built from its source and linked in, and put
# it in unity/Assets/Plugins/macOS, where the editor loads it on its next
# start. Needs Xcode's command line tools and cmake. Runs the engine's own
# test_embed when the game files are there.
#   bash scripts/build-engine-mac.sh
# Environment:
#   OK_ENGINE_SRC    an OpenKingdoms checkout of the unity-embed branch, ~/dev/OpenKingdoms
#   OK_ENGINE_BUILD  the build tree, ~/okbuild/embed-mac
#   OK_GAME_DIR      the game test_embed reads, ~/Games/Total Annihilation Kingdoms
#   OK_DATA_DIR      extracted game data for test_embed, none by default
#   OK_MAC_ARCH      arm64 or x86_64, this Mac's own by default
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="${OK_ENGINE_SRC:-$HOME/dev/OpenKingdoms}"
build="${OK_ENGINE_BUILD:-$HOME/okbuild/embed-mac}"
game="${OK_GAME_DIR:-$HOME/Games/Total Annihilation Kingdoms}"
data="${OK_DATA_DIR:-}"
# A shell under Rosetta says x86_64, so ask the hardware.
arch="${OK_MAC_ARCH:-$([ "$(sysctl -n hw.optional.arm64 2>/dev/null)" = 1 ] && echo arm64 || echo x86_64)}"
sdl=2.32.10
sdl_sha256=5f5993c530f084535c65a6879e9b26ad441169b3e25d789d83287040a9ca5165

[ "$(uname)" = Darwin ] || { echo "This builds the engine on a Mac. On Windows use scripts/build-engine.sh."; exit 1; }
xcrun --find clang > /dev/null 2>&1 || { echo "Xcode's command line tools are missing. Install them with: xcode-select --install"; exit 1; }
command -v cmake > /dev/null 2>&1 || { echo "cmake is missing. Get the macOS .tar.gz from https://cmake.org/download, unpack it under ~/tools and put its CMake.app/Contents/bin on PATH."; exit 1; }
if [ ! -f "$src/include/ok_embed.h" ]; then
    echo "$src is not an OpenKingdoms checkout of the unity-embed branch. Get one with:"
    echo "  git clone -b unity-embed https://github.com/OpenKingdoms/OpenKingdoms.git $src"
    exit 1
fi
api=$(sed -n 's/^#define OKX_API_VERSION \([0-9]*\).*/\1/p' "$src/include/ok_embed.h")
want=$(grep -o 'ApiVersion = [0-9]*' "$root/unity/Assets/Engine/OkEngine.cs" | grep -o '[0-9]*$')
if [ "$api" != "$want" ]; then
    echo "$src builds okengine API $api, and this project's binding expects API $want."
    echo "Check out the unity-embed commit that engine/VERSION names, then run this again."
    exit 1
fi

gen=()
command -v ninja > /dev/null 2>&1 && gen=(-G Ninja)
jobs=$(sysctl -n hw.ncpu)
mkdir -p "$build"
# Homebrew under /usr/local is an Intel one on many Apple silicon Macs, and
# none of its libraries may end up in a library Unity loads.
common=(-DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES="$arch" -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0
    -DCMAKE_IGNORE_PREFIX_PATH="/usr/local;/opt/homebrew;/opt/local"
    -DCMAKE_SYSTEM_IGNORE_PREFIX_PATH="/usr/local;/opt/homebrew;/opt/local")

# SDL2 static, so the engine is one file. Homebrew's sdl2 is sdl2-compat over SDL3.
sdlroot="$build/sdl-$sdl-$arch"
if [ ! -f "$sdlroot/lib/libSDL2.a" ]; then
    tarball="$build/SDL2-$sdl.tar.gz"
    [ -f "$tarball" ] || curl -fsSL -o "$tarball" "https://github.com/libsdl-org/SDL/releases/download/release-$sdl/SDL2-$sdl.tar.gz"
    if ! echo "$sdl_sha256  $tarball" | shasum -a 256 -c - > /dev/null 2>&1; then
        rm -f "$tarball"
        echo "SDL2-$sdl.tar.gz did not match its checksum. Run this again to download it afresh."
        exit 1
    fi
    echo "== Building SDL $sdl for $arch"
    rm -rf "$build/SDL2-$sdl" "$build/sdl-build-$arch"
    tar -xzf "$tarball" -C "$build"
    cmake -S "$build/SDL2-$sdl" -B "$build/sdl-build-$arch" "${gen[@]}" "${common[@]}" \
        -DSDL_SHARED=OFF -DSDL_STATIC=ON -DSDL_TEST=OFF -DCMAKE_INSTALL_PREFIX="$sdlroot" > "$build/sdl-configure.log" 2>&1 \
        || { tail -20 "$build/sdl-configure.log"; exit 1; }
    cmake --build "$build/sdl-build-$arch" -j"$jobs" > "$build/sdl-build.log" 2>&1 || { tail -30 "$build/sdl-build.log"; exit 1; }
    cmake --install "$build/sdl-build-$arch" > /dev/null
fi

echo "== Building okengine API $api for $arch from $src ($(git -C "$src" rev-parse --short HEAD 2>/dev/null || echo "not a git checkout"))"
tree="$build/engine-$arch"
cmake -S "$src" -B "$tree" "${gen[@]}" "${common[@]}" -DTAK_BUILD_EMBED=ON \
    -DCMAKE_PREFIX_PATH="$sdlroot" -DSDL2_DIR="$sdlroot/lib/cmake/SDL2" \
    -DTAK_GAME_DIR="$game" -DTAK_DATA_DIR="$data" > "$build/engine-configure.log" 2>&1 \
    || { tail -20 "$build/engine-configure.log"; exit 1; }
cmake --build "$tree" --target okengine test_embed -j"$jobs" > "$build/engine-build.log" 2>&1 || { tail -40 "$build/engine-build.log"; exit 1; }
lib="$tree/src/libokengine.dylib"
if otool -L "$lib" | tail -n +2 | grep -v -E '^[[:space:]]+(/usr/lib/|/System/Library/|@rpath/libokengine)' | grep -q .; then
    echo "libokengine.dylib links something outside macOS itself:"
    otool -L "$lib"
    exit 1
fi

if [ -f "$game/data.hpi" ]; then
    echo "== Running test_embed on $game"
    if (cd "$tree/src" && SDL_AUDIODRIVER=dummy SDL_VIDEODRIVER=dummy ./test_embed > "$build/test_embed.log" 2>&1); then
        grep '^Results:' "$build/test_embed.log" | tail -1 | sed 's/^/   test_embed: /'
    else
        tail -20 "$build/test_embed.log"
        echo "test_embed failed, see $build/test_embed.log. The library was not installed."
        exit 1
    fi
else
    echo "== No game at $game, so test_embed did not run"
fi

# For scripts/publish-engine.sh: the library and where it came from.
mkdir -p "$build/publish"
cp "$lib" "$build/publish/libokengine.dylib"
sha=$(git -C "$src" rev-parse --short HEAD 2>/dev/null || echo "an unknown commit")
echo "okengine API $api for macOS $arch with SDL $sdl linked in, unity-embed $sha" > "$build/publish/VERSION-macos.txt"

# A new file rather than new bytes in the old one: macOS kills a process
# whose loaded library changes under it.
dest="$root/unity/Assets/Plugins/macOS"
mkdir -p "$dest"
rm -f "$dest/libokengine.dylib"
cp "$lib" "$dest/libokengine.dylib"
echo "plugin: $dest/libokengine.dylib, used from the editor's next start"
echo "to publish it for every Mac clone: bash scripts/publish-engine.sh $build/publish"
