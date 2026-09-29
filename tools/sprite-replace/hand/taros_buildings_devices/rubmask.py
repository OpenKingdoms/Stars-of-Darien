"""Where a ruin picture has rubble the intact picture has not, as a grid over
the ground, for heaping rubble where it was drawn. System Python:

    python rubmask.py <ruin> <intact> <z inside> <x0 y0 x1 y1 ...polygon>

Rubble reads as a speckle of near-black gaps between lit stones. The grid
covers x, y in -13..13 at a quarter cell; a point inside the polygon is
looked up at height z (a platform top), outside it on the ground. Writes
blobs/<ruin>_rubble.npy.
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
cat = {r["name"]: r for r in json.load(open(r"D:\OKReplace\catalog.json"))}
ruin, intact, zin = sys.argv[1], sys.argv[2], float(sys.argv[3])
poly = [float(v) for v in sys.argv[4:]]
poly = list(zip(poly[0::2], poly[1::2]))


def inside(x, y):
    c = False
    for i in range(len(poly)):
        (ax, ay), (bx, by) = poly[i], poly[(i + 1) % len(poly)]
        if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay + 1e-12) + ax:
            c = not c
    return c


def darkness(name):
    r = cat[name]
    a = np.array(Image.open(r"D:\OKReplace\sprites\%s.png" % name).convert("RGBA")).astype(float)
    L = a[..., :3] @ [0.3, 0.59, 0.11]
    al = a[..., 3] > 128
    d = Image.fromarray((((L < 16) & al) * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(3))
    m = Image.fromarray((al * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(2))
    return np.array(d).astype(float) / 255, np.array(m).astype(float) / 255, r["sprite"]["hotspot"]


dr, ar, hr = darkness(ruin)
di, ai, hi = darkness(intact)
S = 4
xs = np.arange(-13, 13 + 1e-6, 1.0 / S)
grid = np.zeros((len(xs), len(xs)), np.float32)  # [j (y), i (x)]


def look(img, hot, x, y, z):
    c = hot[0] + 16 * x
    r = hot[1] - 16 * y - 8 * z
    ci, ri = int(round(c)), int(round(r))
    if 0 <= ri < img.shape[0] and 0 <= ci < img.shape[1]:
        return img[ri, ci]
    return 0.0


for j, y in enumerate(xs):
    for i, x in enumerate(xs):
        z = zin if inside(x, y) else 0.0
        a_r = look(ar, hr, x, y, z)
        if a_r < 0.5:
            continue
        v = look(dr, hr, x, y, z) - look(di, hi, x, y, z) * 0.8
        if look(ai, hi, x, y, z) < 0.5:
            v = max(v, 0.35 + look(dr, hr, x, y, z))  # spilled past the intact outline
        grid[j, i] = max(0.0, v)
g = Image.fromarray(np.clip(grid * 255, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))
grid = np.array(g).astype(np.float32) / 255
os.makedirs(os.path.join(HERE, "blobs"), exist_ok=True)
np.save(os.path.join(HERE, "blobs", ruin + "_rubble.npy"), grid)
Image.fromarray(np.clip(grid[::-1] * 255 * 1.5, 0, 255).astype(np.uint8)).resize((len(xs) * 4, len(xs) * 4)).save(
    r"D:\OKReplace\hand\taros_buildings_devices\scratch\%s_rubgrid.png" % ruin)
print(ruin, "rubble cover %.2f" % (grid > 0.15).mean())
