#!/usr/bin/env bash
# Publish an engine build for everyone who clones the project: copies
# okengine.dll into engine/ as okengine-api<N>.dll with SDL2.dll, and
# libokengine.dylib, the Mac build, as okengine-api<N>.dylib, where the
# editor's EngineInstaller finds them, removes older ones and rewrites
# VERSION. The folder may hold either build or both. N is
# OkEngine.ApiVersion, and each library's own okx_api_version must agree.
# A new build of the same version is fine: the installer replaces its own
# copy on every clone. Commit engine/ with the binding change that needs it.
#   bash scripts/publish-engine.sh [folder with the libraries] [note]
# The note says where the Windows build came from, VERSION.txt in the
# folder by default, and VERSION-macos.txt there says it for the Mac one.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="${1:-D:/OKBuild/okengine-published}"
note="${2:-}"
dll="$src/okengine.dll"
dylib="$src/libokengine.dylib"
[ -f "$dll" ] || [ -f "$dylib" ] || { echo "no okengine.dll or libokengine.dylib in $src"; exit 1; }
[ ! -f "$dll" ] || [ -f "$src/SDL2.dll" ] || { echo "no SDL2.dll beside okengine.dll in $src"; exit 1; }
api=$(grep -o 'ApiVersion = [0-9]*' "$root/unity/Assets/Engine/OkEngine.cs" | grep -o '[0-9]*$')

py=""
for p in python3 python py; do
    if command -v "$p" >/dev/null 2>&1 && "$p" -c "import struct" >/dev/null 2>&1; then py="$p"; break; fi
done
[ -n "$py" ] || { echo "publish-engine.sh needs Python on PATH to read the libraries' versions"; exit 1; }

# A library's version from one export: "mov eax, N; ret" in a release
# build, or "mov w0, #N; ret" on Apple silicon. Windows and Mac files.
version() {
"$py" - "$1" "$2" <<'EOF'
import struct, sys
d = open(sys.argv[1], 'rb').read()
want = sys.argv[2].encode()
def pe():
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
            return off(struct.unpack_from('<I', d, off(funcs) + 4 * struct.unpack_from('<H', d, off(ords) + 2 * i)[0])[0]), False
    return None, False
def macho():
    b = struct.unpack_from('>I', d, 16)[0] if d[:4] == b'\xca\xfe\xba\xbe' else 0
    magic, cpu = struct.unpack_from('<Ii', d, b)
    if magic != 0xfeedfacf: return None, False
    at, segs, sym = b + 32, [], None
    for _ in range(struct.unpack_from('<I', d, b + 16)[0]):
        cmd, size = struct.unpack_from('<II', d, at)
        if cmd == 0x19: segs.append(struct.unpack_from('<QQQQ', d, at + 24))
        if cmd == 0x2: sym = struct.unpack_from('<III', d, at + 8)
        at += size
    symoff, nsyms, stroff = sym
    for i in range(nsyms):
        strx, typ, _, _, value = struct.unpack_from('<IBBHQ', d, b + symoff + 16 * i)
        s = b + stroff + strx
        if typ & 0xee == 0x0e and d[s:d.index(b'\0', s)] == b'_' + want:
            for va, _, raw, size in segs:
                if va <= value < va + size: return b + raw + value - va, cpu == 0x0100000c
    return None, False
f, arm = pe() if d[:2] == b'MZ' else macho()
if f is None: print('?')
elif arm:
    mov, ret = struct.unpack_from('<II', d, f)
    print((mov >> 5) & 0xffff if mov & 0xffe0001f == 0x52800000 and ret == 0xd65f03c0 else '?')
else:
    print(struct.unpack_from('<i', d, f + 1)[0] if d[f] == 0xB8 and d[f + 5] == 0xC3 else '?')
EOF
}

for lib in "$dll" "$dylib"; do
    [ -f "$lib" ] || continue
    own=$(version "$lib" okx_api_version)
    if [ "$own" != "$api" ]; then
        echo "$(basename "$lib") reports API $own but OkEngine.ApiVersion is $api. Bump the binding, or build Release (a Debug build reads as ?)."
        exit 1
    fi
done

mkdir -p "$root/engine"
head=$(git -C "$root" rev-parse --short HEAD)
# VERSION keeps the line of a build this run leaves in place.
keep=$(grep -v -E '^(okengine-api[0-9]+\.dll|SDL2\.dll|okengine-api[0-9]+\.dylib) ' "$root/engine/VERSION" 2>/dev/null || true)
old_dll=$(grep -E '^(okengine-api[0-9]+\.dll|SDL2\.dll) ' "$root/engine/VERSION" 2>/dev/null || true)
old_mac=$(grep -E "^okengine-api$api\.dylib " "$root/engine/VERSION" 2>/dev/null || true)
if [ -f "$dll" ]; then
    rm -f "$root"/engine/okengine-api*.dll
    cp "$dll" "$root/engine/okengine-api$api.dll"
    cp "$src/SDL2.dll" "$root/engine/SDL2.dll"
    from=${note:-$(cat "$src/VERSION.txt" 2>/dev/null || echo "a local build")}
    old_dll="okengine-api$api.dll  okengine API $api, $from, published at openkingdoms-unity $head
SDL2.dll            SDL 2, the build okengine links against (zlib license)"
    echo "published engine/okengine-api$api.dll"
fi
if [ -f "$dylib" ]; then
    rm -f "$root"/engine/okengine-api*.dylib
    cp "$dylib" "$root/engine/okengine-api$api.dylib"
    from=$(cat "$src/VERSION-macos.txt" 2>/dev/null || echo "a local Mac build")
    old_mac="okengine-api$api.dylib  $from, published at openkingdoms-unity $head"
    echo "published engine/okengine-api$api.dylib"
elif ls "$root"/engine/okengine-api*.dylib > /dev/null 2>&1 && [ ! -f "$root/engine/okengine-api$api.dylib" ]; then
    # A Mac build of another API is no use to the binding.
    rm -f "$root"/engine/okengine-api*.dylib
    old_mac=""
    echo "removed the Mac build of an older API. Until a Mac build of API $api is published,"
    echo "a Mac makes its own with scripts/build-engine-mac.sh from the same unity-embed commit."
fi
printf '%s\n' "$old_dll" "$old_mac" "$keep" | grep -v '^$' > "$root/engine/VERSION.new"
mv "$root/engine/VERSION.new" "$root/engine/VERSION"
echo "Commit engine/ with the binding change."
