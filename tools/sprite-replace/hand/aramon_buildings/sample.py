"""Median drawn colours of sprite regions, for material colours.

    python sample.py <name> x0 y0 x1 y1 [x0 y0 x1 y1 ...]

Prints the median, and the 25th and 75th percentile by brightness, of the
opaque pixels in each box (x1, y1 exclusive).
"""
import sys

import numpy as np
from PIL import Image

name = sys.argv[1]
a = np.array(Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")).astype(float)
v = [int(x) for x in sys.argv[2:]]
for i in range(0, len(v), 4):
    x0, y0, x1, y1 = v[i:i + 4]
    p = a[y0:y1, x0:x1].reshape(-1, 4)
    p = p[p[:, 3] > 127][:, :3]
    if not len(p):
        print((x0, y0, x1, y1), "empty")
        continue
    lum = p @ [0.299, 0.587, 0.114]
    o = np.argsort(lum)
    q = lambda f: p[o[int(f * (len(o) - 1))]]
    med = np.median(p, 0)
    h = lambda c: "#%02x%02x%02x" % tuple(int(round(x)) for x in c)
    print((x0, y0, x1, y1), "n=%d" % len(p), "median", h(med), "p25", h(q(0.25)), "p75", h(q(0.75)))
