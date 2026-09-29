"""Finds the loose rubble pieces in a ruin sprite, for placing blocks on the
ground where the picture drew them. System Python:

    python findblobs.py <name> <kind> [x0 y0 x1 y1 exclusion box ...]

kind 'tan' finds sandstone chips, 'dark' soot-grey rocks. Writes
blobs/<name>.json as [[col, row, w, h, pixels, r, g, b], ...].
"""
import json
import os
import sys
from collections import deque

import numpy as np
from PIL import Image

name, kind = sys.argv[1], sys.argv[2]
ex = [int(v) for v in sys.argv[3:]]
a = np.array(Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")).astype(int)
H, W = a.shape[:2]
r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3] > 128
if kind == "tan":
    m = al & (r > 70) & (r > b + 25) & (g > b + 10)
elif kind == "iron":
    m = al & (abs(r - b) < 25) & (r < 140)
else:
    m = al
for i in range(0, len(ex), 4):
    x0, y0, x1, y1 = ex[i:i + 4]
    m[y0:y1, x0:x1] = False
seen = np.zeros_like(m)
out = []
for y in range(H):
    for x in range(W):
        if m[y, x] and not seen[y, x]:
            q = deque([(y, x)])
            seen[y, x] = True
            pts = []
            while q:
                cy, cx = q.popleft()
                pts.append((cy, cx))
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = cy + dy, cx + dx
                    if 0 <= ny < H and 0 <= nx < W and m[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        q.append((ny, nx))
            if 4 <= len(pts) <= 400:
                ys = [p[0] for p in pts]
                xs = [p[1] for p in pts]
                c = np.median(a[ys, xs, :3], 0)
                out.append([float(np.mean(xs)), float(np.mean(ys)), max(xs) - min(xs) + 1, max(ys) - min(ys) + 1,
                            len(pts), int(c[0]), int(c[1]), int(c[2])])
os.makedirs(os.path.join(os.path.dirname(os.path.abspath(__file__)), "blobs"), exist_ok=True)
json.dump(out, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "blobs", name + ".json"), "w"))
print(name, len(out), "blobs")
