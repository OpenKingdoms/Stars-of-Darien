"""Colour check: the sprite against the classic render over the same
rectangles, run with the system Python.

    python calib.py <Name> x0 y0 x1 y1 [x0 y0 x1 y1 ...]
"""
import os
import sys

import numpy as np
from PIL import Image

REN = "D:/OKReplace/hand/veruna_buildings/renders"
name = sys.argv[1]
spr = np.asarray(Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")).astype(float)
ren = Image.open(os.path.join(REN, name + "_classic.png")).convert("RGBA")
ren = np.asarray(ren.resize((spr.shape[1], spr.shape[0]), Image.BILINEAR)).astype(float)
v = [int(a) for a in sys.argv[2:]]
for k in range(0, len(v), 4):
    x0, y0, x1, y1 = v[k:k + 4]
    out = []
    for a in (spr, ren):
        r = a[y0:y1, x0:x1].reshape(-1, 4)
        r = r[r[:, 3] > 128][:, :3]
        out.append([int(c) for c in np.median(r, 0)] if len(r) else None)
    ratio = [round(s / max(1, r), 2) for s, r in zip(*out)] if out[0] and out[1] else None
    print((x0, y0, x1, y1), "sprite", out[0], "render", out[1], "ratio", ratio)
