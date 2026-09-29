"""Silhouette fit of built models against their sprites (system Python).

    python fit.py [names...]

Rasterises renders/<Name>_proj.json (the model in the exact classic
projection) and compares it with the sprite's drawn pixels, the cast
shadow left out: cover is the share of the drawing the model covers, spill
the share of the model outside the drawing. Writes renders/<Name>_fit.png:
grey both, red drawing only, blue model only, dark the cast shadow.
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

OUT = "D:/OKReplace/hand/walls_joints/renders"
SP = "D:/OKReplace/sprites/"


def shadow_mask(a):
    """Cast shadow: near-black, low-saturation pixels joined to the drawing's
    right or lower edge. A crude test that is good enough for a fit number."""
    rgb = a[..., :3].astype(float)
    lum = rgb @ np.array([0.3, 0.59, 0.11])
    return (a[..., 3] > 128) & (lum < 24)


def fit(name, save=True):
    a = np.array(Image.open(SP + name + ".png").convert("RGBA"))
    h, w = a.shape[:2]
    d = json.load(open(os.path.join(OUT, name + "_proj.json")))
    im = Image.new("L", (w, h), 0)
    dr = ImageDraw.Draw(im)
    for t in d["tris"]:
        dr.polygon([tuple(p) for p in t], fill=255)
    mdl = np.array(im) > 127
    drawn = a[..., 3] > 128
    sh = shadow_mask(a)
    body = drawn & ~sh
    cover = (mdl & body).sum() / max(1, body.sum())
    spill = (mdl & ~drawn).sum() / max(1, mdl.sum())
    if save:
        vis = np.zeros((h, w, 3), np.uint8) + 40
        vis[sh] = (70, 70, 70)
        vis[body & ~mdl] = (220, 40, 40)
        vis[mdl & ~drawn] = (40, 80, 230)
        vis[mdl & drawn] = (170, 170, 170)
        Image.fromarray(vis).resize((w * 3, h * 3), Image.NEAREST).save(os.path.join(OUT, name + "_fit.png"))
    return cover, spill


if __name__ == "__main__":
    names = sys.argv[1:] or sorted(os.path.basename(p)[:-10] for p in os.listdir(OUT) if p.endswith("_proj.json"))
    for n in names:
        c, s = fit(n)
        print("%-12s cover %.2f spill %.2f" % (n, c, s))
