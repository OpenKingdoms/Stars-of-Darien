"""Numbers for a classic render against its sprite, with the system Python.
    python metrics.py Name,Name,...   (or 'all')
cover: share of the sprite the render covers once the ortho squeeze is undone;
extra: share of the render outside the sprite; lum: median luminance of
sprite and render; seams: see-through pixels inside the render's silhouette."""
import json
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = "D:/OKReplace/hand/zhon_structures/renders"
CAT = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}


def _grow(m, n=1):
    for _ in range(n):
        g = m.copy()
        g[1:] |= m[:-1]
        g[:-1] |= m[1:]
        g[:, 1:] |= m[:, :-1]
        g[:, :-1] |= m[:, 1:]
        m = g
    return m


def _flood(seed, allowed):
    m = seed & allowed
    while True:
        g = _grow(m) & allowed
        if g.sum() == m.sum():
            return g
        m = g


def unsqueezed(name, spr):
    hy = CAT[name]["sprite"]["hotspot"][1]
    ren = Image.open(os.path.join(OUT, name + "_classic.png")).convert("RGBA").resize(spr.size, Image.LANCZOS)
    k = 1 / math.cos(math.atan(0.5))
    tall = ren.resize((spr.width, int(round(spr.height * k))), Image.LANCZOS)
    top = int(round(hy * k - hy))
    canvas = Image.new("RGBA", spr.size, (0, 0, 0, 0))
    canvas.alpha_composite(tall.crop((0, top, spr.width, top + spr.height)))
    return canvas


def lum(a):
    return a[..., :3] @ np.array([0.299, 0.587, 0.114])


def measure(name):
    spr = Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")
    s = np.asarray(spr).astype(float)
    r = np.asarray(unsqueezed(name, spr)).astype(float)
    sm, rm = s[..., 3] > 127, r[..., 3] > 127
    cover = (sm & rm).sum() / max(1, sm.sum())
    extra = (rm & ~sm).sum() / max(1, rm.sum())
    full = np.asarray(Image.open(os.path.join(OUT, name + "_classic.png")).convert("RGBA")).astype(float)
    fa = full[..., 3]
    border = np.zeros(fa.shape, bool)
    border[0], border[-1], border[:, 0], border[:, -1] = True, True, True, True
    outside = _flood(border, fa < 128)
    inner = ~_grow(outside, 3)
    seams = int((inner & (fa < 250)).sum())
    ls = float(np.median(lum(s)[sm]))
    lr = float(np.median(lum(full)[fa > 127]))
    return dict(cover=round(cover, 3), extra=round(extra, 3), lum_sprite=round(ls, 1), lum_render=round(lr, 1),
                seams=seams)


if __name__ == "__main__":
    sys.path.insert(0, HERE)
    if sys.argv[1] == "all":
        import models
        names = list(models.MODELS)
    else:
        names = sys.argv[1].split(",")
    for n in names:
        m = measure(n)
        print("%-13s cover %.3f extra %.3f lum %5.1f / %5.1f seams %d" % (n, m["cover"], m["extra"], m["lum_sprite"],
                                                                      m["lum_render"], m["seams"]))
