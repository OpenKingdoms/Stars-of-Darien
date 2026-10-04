#!/usr/bin/env bash
# Films the destruction scenes on the real engine, headless and offscreen,
# as render.sh films the trailer, then lays each shot's stills out as a
# contact sheet. Needs the game files where the engine finds them.
#   bash tools/trailer/destruction.sh [scenes] [out dir] [1080|1440]
# scenes is a comma separated list from DestructionScenes.cs, or all.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
SCENES=${1:-all}
OUT=${2:-D:/OKBuild/video/destruction-work}
SIZE=${3:-1080}
UNITY=${UNITY:-D:/Unity/6000.3.25f1/Editor/Unity.exe}
FFMPEG=${FFMPEG:-$(command -v ffmpeg || echo D:/OKBuild/tools/vidvenv/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe)}
mkdir -p "$OUT"
stamp=$(date +%Y%m%d-%H%M%S)
OKU_DESTRUCTION_DIR="$OUT" OKU_DESTRUCTION_SCENES="$SCENES" OKU_DESTRUCTION_SIZE="$SIZE" OKU_TRAILER_FFMPEG="$FFMPEG" \
  "$UNITY" -batchmode -projectPath "$(cygpath -w "$root/unity" 2>/dev/null || echo "$root/unity")" \
  -runTests -testPlatform PlayMode -testFilter "OpenKingdomsUnity.Tests.Trailer.DestructionCaptures" \
  -testResults "$OUT/results-$stamp.xml" -logFile "$OUT/unity-$stamp.log"
code=$?
echo "unity exit $code, log $OUT/unity-$stamp.log"
grep -E "error CS|Scripts have compiler errors" "$OUT/unity-$stamp.log" | head -5
python "$here/sheets.py" "$OUT"
tail -n 30 "$OUT/checks.log" 2>/dev/null
exit $code
