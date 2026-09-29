"""Silhouette extents and colour samples of a sprite, in cells about the hotspot."""
import json
import sys
import numpy as np
from PIL import Image

cat = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}


def load(n):
    a = np.asarray(Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")).astype(float)
    return a


def report(n, step=8):
    r = cat[n]
    hx, hy = r["sprite"]["hotspot"]
    a = load(n)
    al = a[..., 3] > 127
    ys, xs = np.nonzero(al)
    print("%s  w%d h%d hot(%d,%d) fp%s  cols %d..%d  rows %d..%d" % (n, a.shape[1], a.shape[0], hx, hy, r["footprint"],
          xs.min(), xs.max(), ys.min(), ys.max()))
    print("  x cells %.2f..%.2f  top row %d (=%.2f cells up at y0)  bottom row %d (y=%.2f front at z0)" % (
        (xs.min() - hx) / 16, (xs.max() + 1 - hx) / 16, ys.min(), (hy - ys.min()) / 8, ys.max(), -(ys.max() + 1 - hy) / 16))
    for y in range(ys.min(), ys.max() + 1, step):
        row = np.nonzero(al[y])[0]
        if len(row):
            c = a[y, row, :3].mean(0)
            print("   row %3d  x %3d..%3d (%.2f..%.2f) n%3d  rgb %3d %3d %3d" % (y, row.min(), row.max(), (row.min() - hx) / 16,
                  (row.max() + 1 - hx) / 16, len(row), *c))


for n in sys.argv[1].split(","):
    report(n, int(sys.argv[2]) if len(sys.argv) > 2 else 8)
