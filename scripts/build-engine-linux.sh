#!/usr/bin/env bash
# Build okengine (the OpenKingdoms engine as a library) for Linux x86_64 as
# libokengine.so, with SDL2 built from its source and linked in, so the
# library needs nothing beyond glibc. No FFmpeg, as on Windows and the Mac.
# GitHub builds the published one on Ubuntu 22.04, so it runs on glibc 2.35
# or later. Runs the engine's own test_embed when the game files are there.
#   bash scripts/build-engine-linux.sh
# Environment:
#   OK_ENGINE_SRC    an OpenKingdoms checkout of the unity-embed branch, ~/Projects/OpenKingdoms
#   OK_ENGINE_BUILD  the build tree, ~/OKBuild/embed-linux
#   OK_GAME_DIR      the game test_embed reads, ~/Games/Total Annihilation Kingdoms
#   OK_DATA_DIR      extracted game data for test_embed, none by default
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="${OK_ENGINE_SRC:-$HOME/Projects/OpenKingdoms}"
build="${OK_ENGINE_BUILD:-$HOME/OKBuild/embed-linux}"
game="${OK_GAME_DIR:-$HOME/Games/Total Annihilation Kingdoms}"
data="${OK_DATA_DIR:-}"
sdl=2.32.10
sdl_sha256=5f5993c530f084535c65a6879e9b26ad441169b3e25d789d83287040a9ca5165

[ "$(uname)" = Linux ] || { echo "This builds the engine on Linux. On a Mac use scripts/build-engine-mac.sh."; exit 1; }
[ "$(uname -m)" = x86_64 ] || { echo "This builds the x86_64 library Unity loads, and this machine is $(uname -m)."; exit 1; }
command -v cmake > /dev/null 2>&1 || { echo "cmake is missing."; exit 1; }
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
jobs=$(nproc)
mkdir -p "$build"

# SDL2 static and position independent, so the engine is one file. Arch's
# sdl2 is sdl2-compat over SDL3, which must not end up in the library.
sdlroot="$build/sdl-$sdl"
if [ ! -f "$sdlroot/lib/libSDL2.a" ]; then
    tarball="$build/SDL2-$sdl.tar.gz"
    [ -f "$tarball" ] || curl -fsSL -o "$tarball" "https://github.com/libsdl-org/SDL/releases/download/release-$sdl/SDL2-$sdl.tar.gz"
    if ! echo "$sdl_sha256  $tarball" | sha256sum -c - > /dev/null 2>&1; then
        rm -f "$tarball"
        echo "SDL2-$sdl.tar.gz did not match its checksum. Run this again to download it afresh."
        exit 1
    fi
    echo "== Building SDL $sdl"
    rm -rf "$build/SDL2-$sdl" "$build/sdl-build"
    tar -xzf "$tarball" -C "$build"
    cmake -S "$build/SDL2-$sdl" -B "$build/sdl-build" "${gen[@]}" -DCMAKE_BUILD_TYPE=Release \
        -DSDL_SHARED=OFF -DSDL_STATIC=ON -DSDL_STATIC_PIC=ON -DSDL_TEST=OFF \
        -DCMAKE_INSTALL_PREFIX="$sdlroot" -DCMAKE_INSTALL_LIBDIR=lib > "$build/sdl-configure.log" 2>&1 \
        || { tail -20 "$build/sdl-configure.log"; exit 1; }
    cmake --build "$build/sdl-build" -j"$jobs" > "$build/sdl-build.log" 2>&1 || { tail -30 "$build/sdl-build.log"; exit 1; }
    cmake --install "$build/sdl-build" > /dev/null
fi
# SDL opens the sound servers at run time, but only the ones whose headers
# it was built with. Without them the engine would be silent.
drivers=$(sed -n 's/^#define SDL_AUDIO_DRIVER_\(PIPEWIRE\|PULSEAUDIO\|ALSA\) 1$/\1/p' "$sdlroot/include/SDL2/SDL_config.h" | tr '\n' ' ')
if [ -z "$drivers" ]; then
    echo "SDL was built without PipeWire, PulseAudio or ALSA. Install their headers (libpipewire-0.3-dev,"
    echo "libpulse-dev and libasound2-dev on Ubuntu), delete $sdlroot and run this again."
    exit 1
fi
echo "   SDL $sdl sound: $drivers"

echo "== Building okengine API $api from $src ($(git -C "$src" rev-parse --short HEAD 2>/dev/null || echo "not a git checkout"))"
tree="$build/engine"
# AVFORMAT_LIBRARY=OFF leaves FFmpeg out even where it is installed: a
# distribution's FFmpeg would tie the library to that distribution.
# --exclude-libs keeps SDL's own symbols out of the library's exports.
cmake -S "$src" -B "$tree" "${gen[@]}" -DCMAKE_BUILD_TYPE=Release -DTAK_BUILD_EMBED=ON \
    -DCMAKE_PREFIX_PATH="$sdlroot" -DSDL2_DIR="$sdlroot/lib/cmake/SDL2" -DAVFORMAT_LIBRARY=OFF \
    -DCMAKE_SHARED_LINKER_FLAGS="-Wl,--exclude-libs,ALL" \
    -DTAK_GAME_DIR="$game" -DTAK_DATA_DIR="$data" > "$build/engine-configure.log" 2>&1 \
    || { tail -20 "$build/engine-configure.log"; exit 1; }
cmake --build "$tree" --target okengine test_embed -j"$jobs" > "$build/engine-build.log" 2>&1 || { tail -40 "$build/engine-build.log"; exit 1; }
lib="$tree/src/libokengine.so"
other=$(readelf -d "$lib" | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p' \
    | grep -v -x -E 'libc\.so\.6|libm\.so\.6|libdl\.so\.2|libpthread\.so\.0|librt\.so\.1|libgcc_s\.so\.1|ld-linux-x86-64\.so\.2' || true)
if [ -n "$other" ]; then
    echo "libokengine.so needs libraries beyond glibc: $(echo $other)"
    exit 1
fi

if [ -f "$game/data.hpi" ]; then
    echo "== Running test_embed on $game"
    if (cd "$tree/src" && SDL_AUDIODRIVER=dummy SDL_VIDEODRIVER=dummy ./test_embed > "$build/test_embed.log" 2>&1); then
        grep '^Results:' "$build/test_embed.log" | tail -1 | sed 's/^/   test_embed: /'
    else
        tail -20 "$build/test_embed.log"
        echo "test_embed failed, see $build/test_embed.log."
        exit 1
    fi
else
    echo "== No game at $game, so test_embed did not run"
fi

# For scripts/publish-engine.sh: the library and where it came from.
mkdir -p "$build/publish"
cp "$lib" "$build/publish/libokengine.so"
sha=$(git -C "$src" rev-parse --short HEAD 2>/dev/null || echo "an unknown commit")
glibc=$(objdump -T "$lib" | grep -o 'GLIBC_[0-9.]*' | sed 's/GLIBC_//' | sort -uV | tail -1)
echo "okengine API $api for Linux x86_64 with SDL $sdl linked in, glibc $glibc or later, unity-embed $sha" > "$build/publish/VERSION-linux.txt"
echo "library: $build/publish/libokengine.so"
echo "check it: bash scripts/check-engine.sh $build/publish/libokengine.so"
echo "publish it: bash scripts/publish-engine.sh $build/publish"
