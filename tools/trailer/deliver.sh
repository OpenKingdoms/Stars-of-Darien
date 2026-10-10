#!/usr/bin/env bash
# Copies the destruction shots and contact sheets somewhere to watch them,
# the clips re-encoded small enough to keep on a nearly full drive.
#   bash tools/trailer/deliver.sh <work dir> <out dir> [crf]
set -eu
WORK=$1
OUT=$2
CRF=${3:-21}
FFMPEG=${FFMPEG:-$(command -v ffmpeg || echo D:/OKBuild/tools/vidvenv/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe)}
mkdir -p "$OUT/clips" "$OUT/sheets"
for f in "$WORK"/shots/*.mp4; do
  [ -e "$f" ] || continue
  "$FFMPEG" -y -loglevel error -i "$f" -c:v libx264 -preset slow -crf "$CRF" -pix_fmt yuv420p -movflags +faststart -an "$OUT/clips/$(basename "$f")"
done
python - "$WORK/sheets" "$OUT/sheets" <<'PY'
import os, sys
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
for name in sorted(os.listdir(src)) if os.path.isdir(src) else []:
    if name.endswith(".png"):
        Image.open(os.path.join(src, name)).convert("RGB").save(os.path.join(dst, name[:-4] + ".jpg"), quality=90)
PY
[ -f "$WORK/checks.log" ] && cp "$WORK/checks.log" "$OUT/"
du -sh "$OUT"
