"""Reads each mound sprite for the bespoke scripts, with the system Python: the
top and bottom outline per pixel column (px above the hotspot), the pale column
tips and the tones of three bands. Writes sprites.json."""
import json
import os

import numpy as np
from PIL import Image

CAT = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}


def tone(px, lum, q):
    lo, hi = np.percentile(lum, max(0, q - 10)), np.percentile(lum, min(100, q + 10))
    return [int(v) for v in px[(lum >= lo) & (lum <= hi)].mean(0)]


out = {}
for i in range(1, 7):
    n = "ZonTmound0%d" % i
    hx, hy = CAT[n]["sprite"]["hotspot"]
    a = np.asarray(Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")).astype(float)
    m = a[..., 3] > 127
    lum = a[..., :3] @ np.array([0.299, 0.587, 0.114])
    h, w = m.shape
    top = [int(hy - np.nonzero(m[:, c])[0][0]) if m[:, c].any() else None for c in range(w)]
    bot = [int(hy - np.nonzero(m[:, c])[0][-1]) if m[:, c].any() else None for c in range(w)]
    body = m.copy()
    body[int(h * 0.82):] = False  # the skirt's pale grit is not tips
    hot = body & (lum > np.percentile(lum[body], 93))
    seen = np.zeros_like(hot)
    nubs = []
    for y, x in zip(*np.nonzero(hot)):
        if seen[y, x]:
            continue
        stack, pts = [(y, x)], []
        seen[y, x] = True
        while stack:
            cy, cx = stack.pop()
            pts.append((cy, cx))
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = cy + dy, cx + dx
                    if 0 <= yy < h and 0 <= xx < w and hot[yy, xx] and not seen[yy, xx]:
                        seen[yy, xx] = True
                        stack.append((yy, xx))
        if len(pts) >= 3:
            ys, xs = zip(*pts)
            nubs.append((round(float(np.mean(xs)) - hx, 1), round(hy - float(np.min(ys)), 1), len(pts)))
    bands = {}
    for lab, r0, r1 in (("upper", 0.1, 0.45), ("lower", 0.45, 0.8), ("skirt", 0.85, 1.0)):
        sel = m.copy()
        sel[:int(h * r0)] = False
        sel[int(h * r1):] = False
        bands[lab] = [tone(a[..., :3][sel], lum[sel], q) for q in (10, 50, 90)]
    L = lum[m]
    hi = a[..., :3][m][(L >= np.percentile(L, 95)) & (L <= np.percentile(L, 99))].mean(0)
    out[n] = dict(hx=hx, hy=hy, w=w, h=h, top=top, bot=bot, nubs=sorted(nubs), bands=bands,
                  hi=[int(v) for v in hi])
    print(n, "nubs", len(nubs), "hi", out[n]["hi"])
json.dump(out, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "sprites.json"), "w"))
