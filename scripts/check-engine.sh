#!/usr/bin/env bash
# Checks an engine library before it is published: it exports every okx_
# function OkEngine.cs imports and needs nothing a player's machine lacks.
# A Linux library needs only glibc, OK_GLIBC_MAX (2.35) at most, and keeps
# SDL's symbols to itself. A Windows library needs only Windows, its C
# runtime and the SDL2.dll beside it, which must export every SDL function
# it imports. Needs binutils, and runs on Linux.
#   bash scripts/check-engine.sh <libokengine.so | okengine.dll [SDL2.dll]>
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
lib="$1"
glibc_max="${OK_GLIBC_MAX:-2.35}"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
fail() { echo "::error::$(basename "$lib"): $*"; exit 1; }

grep -oE 'extern [^(]* okx_[A-Za-z0-9_]+\(' "$root/unity/Assets/Engine/OkEngine.cs" | grep -oE 'okx_[A-Za-z0-9_]+' | sort -u > "$tmp/imports"
# The export table of a PE file, as objdump prints it.
pe_exports() { objdump -p "$1" | sed -n '/^\[Ordinal\/Name Pointer\] Table/,/^$/p' | awk '/^\t\[/ { print $NF }'; }

case "$(head -c 4 "$lib" | od -An -tx1 | tr -d ' \n')" in
7f454c46)
    file "$lib"
    nm -D --defined-only "$lib" | awk '{ print $NF }' | grep '^okx_' | sort -u > "$tmp/exports" || true
    needed=$(readelf -d "$lib" | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p')
    echo "needs: $(echo $needed)"
    other=$(echo "$needed" | grep -v -x -E 'libc\.so\.6|libm\.so\.6|libdl\.so\.2|libpthread\.so\.0|librt\.so\.1|libgcc_s\.so\.1|ld-linux-x86-64\.so\.2' || true)
    [ -z "$other" ] || fail "needs libraries beyond glibc: $(echo $other)"
    glibc=$(objdump -T "$lib" | grep -o 'GLIBC_[0-9.]*' | sed 's/GLIBC_//' | sort -uV | tail -1)
    echo "newest glibc symbol: $glibc, allowed up to $glibc_max"
    [ "$(printf '%s\n%s\n' "$glibc" "$glibc_max" | sort -V | tail -1)" = "$glibc_max" ] || fail "needs glibc $glibc, newer than $glibc_max"
    sdl=$(nm -D --defined-only "$lib" | awk '$NF ~ /^SDL_/' | wc -l)
    [ "$sdl" = 0 ] || fail "exports $sdl SDL functions, which another SDL in the process could clash with"
    ;;
4d5a*)
    objdump -p "$lib" > "$tmp/pe"
    grep -q 'file format pei-x86-64' "$tmp/pe" || fail "not a 64-bit Windows library: $(sed -n 2p "$tmp/pe")"
    pe_exports "$lib" | grep '^okx_' | sort -u > "$tmp/exports" || true
    needed=$(sed -n 's/^\tDLL Name: //p' "$tmp/pe")
    echo "needs: $(echo $needed)"
    other=$(echo "$needed" | grep -v -i -x -E 'SDL2\.dll|(kernel32|user32|gdi32|winmm|ws2_32|advapi32|shell32|ole32|shlwapi|bcrypt|vcruntime140|vcruntime140_1)\.dll|api-ms-win-.*\.dll' || true)
    [ -z "$other" ] || fail "needs libraries beyond Windows, its C runtime and SDL2.dll: $(echo $other)"
    sdl="${2:-$(dirname "$lib")/SDL2.dll}"
    if echo "$needed" | grep -q -i -x 'SDL2\.dll'; then
        [ -f "$sdl" ] || fail "imports SDL2.dll and there is none at $sdl"
        sed -n '/^\tDLL Name: SDL2\.dll/,/^$/p' "$tmp/pe" | awk '$NF ~ /^SDL_/ { print $NF }' | sort -u > "$tmp/sdl-imports"
        pe_exports "$sdl" | sort -u > "$tmp/sdl-exports"
        gone=$(comm -23 "$tmp/sdl-imports" "$tmp/sdl-exports")
        [ -z "$gone" ] || fail "$sdl lacks SDL functions it imports: $(echo $gone)"
        echo "$sdl ($(sha256sum < "$sdl" | cut -c1-12)) exports all $(wc -l < "$tmp/sdl-imports") SDL functions it imports"
    fi
    ;;
*)
    fail "neither an ELF nor a PE file"
    ;;
esac

missing=$(comm -23 "$tmp/imports" "$tmp/exports")
echo "The binding imports $(wc -l < "$tmp/imports") okx_ functions and the library exports $(wc -l < "$tmp/exports")."
[ -z "$missing" ] || fail "lacks functions the binding imports: $(echo $missing)"
sha256sum "$lib"
