"""The round-4 judge's numbers for a mound, sprite against classic render, with the system Python.
    python judge.py Name,Name,...  [sprite]"""
import json
import sys

import numpy as np
from PIL import Image

OUT = "D:/OKReplace/hand/zhon_structures/renders"
CAT = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}


def stats(a, h):
    m = a[..., 3] > 127
    rgb = a[..., :3]
    lum = rgb @ np.array([0.299, 0.587, 0.114])
    rows = np.where(m.any(1))[0]
    cols = np.where(m.any(0))[0]
    top, bot = rows[0], rows[-1]

    def width(frac):
        r = int(round(frac * (h - 1)))
        c = np.where(m[r])[0]
        return int(c[-1] - c[0] + 1) if len(c) else 0
    L = lum[m]
    px = rgb[m]
    order = np.argsort(L)
    dark = px[order[:len(order) // 5]].mean(0)
    hi = px[(L >= np.percentile(L, 95)) & (L <= np.percentile(L, 99))].mean(0)
    return dict(top=int(top), bot=int(bot), left=int(cols[0]), right=int(cols[-1]),
                black=round(float((L < 20).mean()), 3), dark=tuple(int(v) for v in dark), hi=tuple(int(v) for v in hi),
                mean=tuple(int(v) for v in px.mean(0)),
                w=[width(f) for f in (0.1, 0.2, 0.4, 0.6, 0.8, 0.95, 1.0)])


for n in sys.argv[1].split(","):
    spr = Image.open("D:/OKReplace/sprites/%s.png" % n).convert("RGBA")
    s = stats(np.asarray(spr).astype(float), spr.height)
    print("%-12s sprite %s" % (n, s))
    if len(sys.argv) < 3:
        ren = Image.open("%s/%s_classic.png" % (OUT, n)).convert("RGBA").resize(spr.size, Image.LANCZOS)
        print("%-12s render %s" % (n, stats(np.asarray(ren).astype(float), spr.height)))
