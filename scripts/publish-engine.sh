#!/usr/bin/env bash
# Publish an okengine build for everyone who clones the project: copies it
# into engine/ as okengine-api<N>.dll with SDL2.dll, where the editor's
# EngineInstaller finds it, removes older ones and rewrites VERSION. N is
# OkEngine.ApiVersion, and the library's own okx_api_version must agree.
# An okcore.dll in the same folder is published as okcore-abi<N>.dll the
# same way, against OkNative.AbiVersion. Commit engine/ with the binding
# change that needs it.
#   bash scripts/publish-engine.sh [folder with okengine.dll and SDL2.dll] [note]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="${1:-D:/OKBuild/okengine-published}"
note="${2:-}"
dll="$src/okengine.dll"
[ -f "$dll" ] || { echo "no okengine.dll in $src"; exit 1; }
[ -f "$src/SDL2.dll" ] || { echo "no SDL2.dll in $src"; exit 1; }
api=$(grep -o 'ApiVersion = [0-9]*' "$root/unity/Assets/Engine/OkEngine.cs" | grep -o '[0-9]*$')
abi=$(grep -o 'AbiVersion = [0-9]*' "$root/unity/Assets/Scripts/OkSim.cs" | grep -o '[0-9]*$')

# A library's version from one export, "mov eax, N; ret" in a release build.
version() {
python - "$1" "$2" <<'EOF'
import struct, sys
d = open(sys.argv[1], 'rb').read()
want = sys.argv[2].encode()
pe = struct.unpack_from('<I', d, 0x3c)[0]
nsec, optsz = struct.unpack_from('<H', d, pe + 6)[0], struct.unpack_from('<H', d, pe + 20)[0]
opt = pe + 24
exp = struct.unpack_from('<I', d, opt + (112 if struct.unpack_from('<H', d, opt)[0] == 0x20b else 96))[0]
secs = [struct.unpack_from('<IIII', d, opt + optsz + 40 * i + 8) for i in range(nsec)]
def off(rva):
    for vsz, va, rsz, raw in secs:
        if va <= rva < va + max(vsz, rsz): return rva - va + raw
e = off(exp)
n, funcs, names, ords = struct.unpack_from('<I', d, e + 24)[0], *struct.unpack_from('<III', d, e + 28)
for i in range(n):
    s = off(struct.unpack_from('<I', d, off(names) + 4 * i)[0])
    if d[s:d.index(b'\0', s)] == want:
        f = off(struct.unpack_from('<I', d, off(funcs) + 4 * struct.unpack_from('<H', d, off(ords) + 2 * i)[0])[0])
        print(struct.unpack_from('<i', d, f + 1)[0] if d[f] == 0xB8 and d[f + 5] == 0xC3 else '?')
EOF
}

own=$(version "$dll" okx_api_version)
if [ "$own" != "$api" ]; then
    echo "okengine.dll reports API $own but OkEngine.ApiVersion is $api. Bump the binding or pick the right build."
    exit 1
fi
core=""
if [ -f "$src/okcore.dll" ]; then
    core=$(version "$src/okcore.dll" ok_sim_abi_version)
    if [ "$core" != "$abi" ]; then
        echo "okcore.dll reports ABI $core but OkNative.AbiVersion is $abi. Bump the binding or pick the right build."
        exit 1
    fi
fi

mkdir -p "$root/engine"
rm -f "$root"/engine/okengine-api*.dll
cp "$dll" "$root/engine/okengine-api$api.dll"
cp "$src/SDL2.dll" "$root/engine/SDL2.dll"
if [ -n "$core" ]; then
    rm -f "$root"/engine/okcore-abi*.dll
    cp "$src/okcore.dll" "$root/engine/okcore-abi$abi.dll"
fi
head=$(git -C "$root" rev-parse --short HEAD)
from=${note:-$(cat "$src/VERSION.txt" 2>/dev/null || echo "a local build")}
{
    echo "okengine-api$api.dll  okengine API $api, $from, published at openkingdoms-unity $head"
    if [ -n "$core" ]; then
        echo "okcore-abi$abi.dll     okcore ABI $abi, $from, published at openkingdoms-unity $head"
    else
        grep '^okcore' "$root/engine/VERSION" 2>/dev/null || true
    fi
    echo "SDL2.dll            SDL 2, the build okengine links against (zlib license)"
} > "$root/engine/VERSION.new"
mv "$root/engine/VERSION.new" "$root/engine/VERSION"
echo "published engine/okengine-api$api.dll${core:+ and engine/okcore-abi$abi.dll}. Commit engine/ with the binding change."
