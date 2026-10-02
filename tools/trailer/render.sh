#!/usr/bin/env bash
# Films the trailer's shots on the real engine, headless and offscreen:
# Unity in -batchmode with graphics, so the GPU draws into a render texture
# and no window opens. Needs the game files where the engine finds them
# (OK_GAME_DIR, or a usual install place).
#   bash tools/trailer/render.sh [scenes] [out dir]
# scenes is a comma separated list from TrailerScenes.cs, or all.
# Then: python tools/trailer/assemble.py --shots <out dir>/shots
set -u
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
SCENES=${1:-all}
OUT=${2:-D:/OKBuild/video/trailer-work}
UNITY=${UNITY:-D:/Unity/6000.3.25f1/Editor/Unity.exe}
FFMPEG=${FFMPEG:-$(command -v ffmpeg || echo D:/OKBuild/tools/vidvenv/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe)}
mkdir -p "$OUT"
stamp=$(date +%Y%m%d-%H%M%S)
OKU_TRAILER_DIR="$OUT" OKU_TRAILER_SCENES="$SCENES" OKU_TRAILER_FFMPEG="$FFMPEG" \
  "$UNITY" -batchmode -projectPath "$(cygpath -w "$root/unity" 2>/dev/null || echo "$root/unity")" \
  -runTests -testPlatform PlayMode -testFilter "OpenKingdomsUnity.Tests.Trailer.TrailerCaptures" \
  -testResults "$OUT/results-$stamp.xml" -logFile "$OUT/unity-$stamp.log"
code=$?
echo "unity exit $code, log $OUT/unity-$stamp.log"
grep -E "error CS|Scripts have compiler errors" "$OUT/unity-$stamp.log" | head -5
tail -n 40 "$OUT/director.log" 2>/dev/null
exit $code
