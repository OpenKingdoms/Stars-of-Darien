"""Fit a house body (walls under a gable, hip or pyramid roof, possibly
turned) to a sprite's silhouette, with the system Python.

    python fit.py <name> key=value ... [free=cx,cy,yaw,...] [mask=x0,y0,x1,y1;...] [show=out.png]

Keys: cx cy yaw L W H R over over_end hip T. The body is convex, so its
classic silhouette is the hull of its corners; the fit moves the free keys
to cover the sprite's opaque pixels while spilling little past them.
mask boxes (sprite pixels) are left out of the score, for props and
chimneys the body should not stretch to reach.
"""
import json
import math
import sys

import numpy as np
from PIL import Image, ImageDraw

CAT = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}


def corners(p):
    """The body's corners in plan and height."""
    c, s = math.cos(math.radians(p["yaw"])), math.sin(math.radians(p["yaw"]))

    def P(u, v, z):
        return (p["cx"] + u * c - v * s, p["cy"] + u * s + v * c, z)
    L, W, H, R = p["L"], p["W"], p["H"], p["R"]
    o, oe, T = p["over"], p["over_end"], p["T"]
    k = R / (W / 2)
    ze = H - o * k
    pts = []
    for u in (-L / 2, L / 2):
        for v in (-W / 2, W / 2):
            pts += [P(u, v, 0), P(u, v, H)]
    for u in (-L / 2 - oe, L / 2 + oe):
        for v in (-W / 2 - o, W / 2 + o):
            pts += [P(u, v, ze), P(u, v, ze - T)]
    # hip below 0 is a gable; else the ridge stops hip short of each end wall
    rl = L / 2 + oe if p["hip"] < 0 else max(0.0, L / 2 - p["hip"])
    for u in (-rl, rl):
        pts.append(P(u, 0, H + R))
    return pts


def hull(pts):
    pts = sorted(set(pts))
    if len(pts) < 3:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, hi = [], []
    for q in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], q) <= 0:
            lo.pop()
        lo.append(q)
    for q in reversed(pts):
        while len(hi) >= 2 and cross(hi[-2], hi[-1], q) <= 0:
            hi.pop()
        hi.append(q)
    return lo[:-1] + hi[:-1]


def outline(r, p):
    hx, hy = r["sprite"]["hotspot"]
    return hull([(hx + 16 * x, hy - 16 * y - 8 * z) for x, y, z in corners(p)])


def mask_of(r, p, k=2):
    w, h = r["sprite"]["w"], r["sprite"]["h"]
    im = Image.new("L", (w * k, h * k), 0)
    ImageDraw.Draw(im).polygon([(x * k, y * k) for x, y in outline(r, p)], fill=255)
    return np.array(im.resize((w, h), Image.BOX)) > 127


def score(r, p, spr, keep, spill=0.7):
    m = mask_of(r, p)
    miss = (spr & ~m & keep).sum()
    over = (m & ~spr & keep).sum()
    return miss + spill * over


def main():
    name = sys.argv[1]
    r = CAT[name]
    p = {"cx": 0.0, "cy": 0.0, "yaw": 0.0, "L": 4.0, "W": 3.0, "H": 2.0, "R": 1.5, "over": 0.4,
         "over_end": 0.4, "hip": 0.0, "T": 0.3}
    free, masks, show, spill = [], [], None, 0.7
    for a in sys.argv[2:]:
        k, v = a.split("=", 1)
        if k == "free":
            free = v.split(",")
        elif k == "mask":
            masks = [[int(t) for t in b.split(",")] for b in v.split(";")]
        elif k == "show":
            show = v
        elif k == "spill":
            spill = float(v)
        else:
            p[k] = float(v)
    spr = np.array(Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA"))[..., 3] > 127
    keep = np.ones_like(spr)
    for x0, y0, x1, y1 in masks:
        keep[y0:y1, x0:x1] = False
    steps = {"cx": 0.4, "cy": 0.4, "yaw": 8.0, "L": 0.5, "W": 0.5, "H": 0.5, "R": 0.5, "over": 0.15,
             "over_end": 0.15, "hip": 0.3, "T": 0.1}
    best = score(r, p, spr, keep, spill)
    for rnd in range(40):
        improved = False
        for k in free:
            for d in (1, -1):
                q = dict(p)
                q[k] += d * steps[k]
                if k in ("L", "W", "H", "R") and q[k] < 0.2:
                    continue
                if k in ("over", "over_end", "T") and q[k] < 0:
                    continue
                s = score(r, q, spr, keep, spill)
                if s < best:
                    best, p, improved = s, q, True
        if not improved:
            for k in free:
                steps[k] *= 0.5
            if not free or max(steps[k] for k in free) < 0.01:
                break
    m = mask_of(r, p)
    cov = (m & spr & keep).sum() / max(1, (spr & keep).sum())
    ovr = (m & ~spr & keep).sum() / max(1, (m & keep).sum())
    print(" ".join("%s=%.3f" % (k, v) for k, v in p.items()))
    print("cover %.3f over %.3f" % (cov, ovr))
    if show:
        im = Image.open("D:/OKReplace/sprites/%s.png" % name).convert("RGBA")
        bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
        bg.alpha_composite(im)
        k = 5
        bg = bg.resize((im.width * k, im.height * k), Image.NEAREST)
        d = ImageDraw.Draw(bg)
        ol = outline(r, p)
        d.line([(x * k, y * k) for x, y in ol + ol[:1]], fill=(255, 0, 0, 255), width=2)
        hx, hy = r["sprite"]["hotspot"]
        c, s = math.cos(math.radians(p["yaw"])), math.sin(math.radians(p["yaw"]))

        def S(u, v, z):
            x, y = p["cx"] + u * c - v * s, p["cy"] + u * s + v * c
            return ((hx + 16 * x) * k, (hy - 16 * y - 8 * z) * k)
        L, W, H = p["L"], p["W"], p["H"]
        base = [S(-L / 2, -W / 2, 0), S(L / 2, -W / 2, 0), S(L / 2, W / 2, 0), S(-L / 2, W / 2, 0)]
        d.line(base + base[:1], fill=(0, 255, 255, 255), width=1)
        ko = p["R"] / (W / 2)
        ze = H - p["over"] * ko
        o, oe = p["over"], p["over_end"]
        eave = [S(-L / 2 - oe, -W / 2 - o, ze), S(L / 2 + oe, -W / 2 - o, ze), S(L / 2 + oe, W / 2 + o, ze),
                S(-L / 2 - oe, W / 2 + o, ze)]
        d.line(eave + eave[:1], fill=(255, 255, 0, 255), width=1)
        rl = L / 2 + oe if p["hip"] < 0 else max(0.0, L / 2 - p["hip"])
        d.line([S(-rl, 0, H + p["R"]), S(rl, 0, H + p["R"])], fill=(255, 0, 255, 255), width=2)
        bg.save(show)


if __name__ == "__main__":
    main()
