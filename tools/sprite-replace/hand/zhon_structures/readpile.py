"""Reads a heap of boulders and sticks off a sprite into a data table, with the
system Python: grey blobs become boulders, brown streaks become sticks, and
the straw-coloured ground becomes the bed they lie on.

    python readpile.py <out json> Name,Name,...
"""
import json
import math
import sys

import numpy as np
from PIL import Image

KY = 16 * math.cos(math.atan(0.5))  # rows per cell of depth in the true ortho view
KZ = KY / 2                         # rows per cell of height
CAT = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}


def components(mask, min_area):
    """Four-connected regions of a boolean mask, as lists of (row, col)."""
    h, w = mask.shape
    seen = np.zeros_like(mask, bool)
    out = []
    for y in range(h):
        for x in range(w):
            if not mask[y, x] or seen[y, x]:
                continue
            stack, pix = [(y, x)], []
            seen[y, x] = True
            while stack:
                cy, cx = stack.pop()
                pix.append((cy, cx))
                for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                    if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            if len(pix) >= min_area:
                out.append(np.array(pix, float))
    return out


def dilate(m, n=1):
    for _ in range(n):
        m = m | np.roll(m, 1, 0) | np.roll(m, -1, 0) | np.roll(m, 1, 1) | np.roll(m, -1, 1)
    return m


def erode(m, n=1):
    return ~dilate(~m, n)


def read(name):
    r = CAT[name]
    hx, hy = r["sprite"]["hotspot"]
    a = np.asarray(Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")).astype(float)
    al = a[..., 3] > 127
    rgb = a[..., :3]
    R, G, B = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    lum = rgb @ [0.299, 0.587, 0.114]
    sat = rgb.max(-1) - rgb.min(-1)
    grey = al & (sat < 22) & (lum > 38)
    brown = al & (R > B + 22) & (R > G + 8) & (lum > 30) & (lum < 150) & ~grey
    straw = al & (G > B + 18) & (R - G < 22) & (lum > 70)
    pile = {"name": name, "boulders": [], "sticks": []}
    # boulders: grey blobs, opened so thin grey bits (blades, edges) drop out
    gm = dilate(erode(dilate(grey, 1), 1), 0)
    for pix in components(gm, 10):
        cy, cx = pix.mean(0)
        area = len(pix)
        rad = math.sqrt(area / math.pi) / 16.0 * 1.15
        zc = rad * 0.55
        col = rgb[pix[:, 0].astype(int), pix[:, 1].astype(int)].mean(0)
        top = pix[:, 0].min()
        pile["boulders"].append(dict(x=round((cx + 0.5 - hx) / 16, 3), y=round((hy - cy - KZ * zc) / KY, 3),
                                     r=round(rad, 3), lum=round(float(col @ [0.299, 0.587, 0.114]), 1),
                                     top=int(top)))
    # sticks: brown streaks, each fitted with a line
    for pix in components(brown, 6):
        c = pix.mean(0)
        d = pix - c
        w, v = np.linalg.eigh(d.T @ d / len(pix))
        ax = v[:, 1]  # (drow, dcol) of the long axis
        t = d @ ax
        L = t.max() - t.min()
        if L < 4:
            continue
        thick = max(1.5, len(pix) / L)
        e1, e2 = c + ax * t.min(), c + ax * t.max()
        if e1[0] < e2[0]:
            e1, e2 = e2, e1  # e1 is the lower end in the picture
        pile["sticks"].append(dict(low=[round(float(e1[1]), 1), round(float(e1[0]), 1)],
                                   high=[round(float(e2[1]), 1), round(float(e2[0]), 1)],
                                   thick=round(float(thick), 2)))
    ys, xs = np.nonzero(straw | grey | brown)
    ys2, xs2 = np.nonzero(al)
    pile["bed"] = dict(x0=round((np.percentile(xs, 4) - hx) / 16, 2), x1=round((np.percentile(xs, 96) - hx) / 16, 2),
                       front=round((hy - np.percentile(ys2, 98)) / KY, 2), back=round((hy - np.percentile(ys2, 30)) / KY, 2))
    pile["hot"] = [hx, hy]
    return pile


if __name__ == "__main__":
    out = {}
    for n in sys.argv[2].split(","):
        out[n] = read(n)
        print(n, len(out[n]["boulders"]), "boulders", len(out[n]["sticks"]), "sticks", out[n]["bed"])
    json.dump(out, open(sys.argv[1], "w"), indent=1)
