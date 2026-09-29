"""Zhon old walls: long, low north-south runs of weathered sandstone laid
dry in three rough courses. Each stone is a flat-faced split slab with a
narrow chamfer and chipped corners, a little out of true; sizes vary, one,
two or three across a course, and the top course is mostly big capstones
spanning the whole width, some cracked in two, some gone. The dark core
behind the stones shows in the gaps. A stone takes the tone the drawing
shows where it lies, its dark crevices left out, so the shade in the
drawing (the darker south end) carries over.

The drawing's lit top is about a cell wide, its dark band to the east the
cast shadow; the wall is built a little wider than the lit top, sturdy,
and leaves the shadow to the game.
"""
import math

import kit
import rubble

T = {
    "ZonOldW03": dict(x=-0.16, y0=-6.06, y1=5.9, W=1.18, H=1.6, seed=3),
    "ZonOldW04": dict(x=-0.16, y0=-6.25, y1=5.9, W=1.18, H=1.65, seed=4),
}


def slab(m, key, poly, z0, z1, ch=0.045, tilt=0.03):
    """A flat-faced stone: the plan polygon poly (anticlockwise) stood from
    z0 to z1, its top edges chamfered by ch, its top a little out of level."""
    rng = m.rng
    n = len(poly)
    cx = sum(p[0] for p in poly) / n
    cy = sum(p[1] for p in poly) / n

    def inset(p, d):
        dx, dy = cx - p[0], cy - p[1]
        L = math.hypot(dx, dy) or 1.0
        return (p[0] + dx / L * d, p[1] + dy / L * d)
    tz = [rng.uniform(-tilt, tilt) for _ in range(n)]
    bot = [(x, y, z0) for x, y in poly]
    mid = [(x, y, z1 - ch + tz[i]) for i, (x, y) in enumerate(poly)]
    top = [(*inset(p, ch * 1.3), z1 + tz[i]) for i, p in enumerate(poly)]
    polys = [bot[::-1], top]
    for i in range(n):
        j = (i + 1) % n
        polys.append([bot[i], bot[j], mid[j], mid[i]])
        polys.append([mid[i], mid[j], top[j], top[i]])
    m.solid(key, polys)


def chipped(rng, xa, xb, ya, yb, chip=0.35):
    """The plan outline of a stone from xa..xb, ya..yb (anticlockwise), with
    some corners knocked off."""
    corners = [(xa, ya), (xb, ya), (xb, yb), (xa, yb)]
    out = []
    for i, (x, y) in enumerate(corners):
        if rng.random() < chip:
            px, py = corners[i - 1]
            nx, ny = corners[(i + 1) % 4]
            a = rng.uniform(0.06, 0.16)
            b = rng.uniform(0.06, 0.16)
            la = math.hypot(px - x, py - y) or 1.0
            lb = math.hypot(nx - x, ny - y) or 1.0
            out.append((x + (px - x) / la * min(a, la * 0.3), y + (py - y) / la * min(a, la * 0.3)))
            out.append((x + (nx - x) / lb * min(b, lb * 0.3), y + (ny - y) / lb * min(b, lb * 0.3)))
        else:
            out.append((x, y))
    return out


def lit_tone(m, tones, x, y, z, rad=3):
    """The palette tone nearest the drawing's colour where a stone lies,
    its dark crevice pixels left out."""
    import numpy as np
    c, r = m.scr(x, y, z)
    ci, ri = int(c), int(r)
    q = m.pix[max(0, ri - rad):ri + rad + 1, max(0, ci - rad):ci + rad + 1].reshape(-1, 4)
    q = q[q[:, 3] > 0.5][:, :3] * 255.0
    if len(q):
        lum = q @ np.array([0.3, 0.59, 0.11])
        if (lum > 55).sum() >= 3:
            q = q[lum > 55]
    if not len(q):
        return tones.keys[len(tones.keys) // 2]
    col = np.median(q, 0)
    # the darkest tone is kept for the crevices, never a whole stone
    return tones.keys[1 + int(((tones.cen[1:] - col[None]) ** 2).sum(1).argmin())]


def build(m, p):
    # the drawing's sandstone, darkened toward its warm brown
    tones = rubble.palette(m, k=5, prefix="sand", gain=0.9, floor=55, warm=(1.02, 0.98, 0.92))
    rng = m.rng
    x, y0, y1, W, H = p["x"], p["y0"], p["y1"], p["W"], p["H"]
    courses = [0.58, 0.54, H - 1.12]
    dark = m.pal[tones.keys[0]]["rgb"]
    m.col("bed", tuple(v * 0.45 for v in dark), 1.0)
    # a dark core behind the stones: the gaps read as shadow, not sky
    m.box("bed", x - W / 2 + 0.1, x + W / 2 - 0.1, y0 + 0.1, y1 - 0.1, 0.0, sum(courses[:2]) - 0.1)
    for c, ch in enumerate(courses):
        z0 = sum(courses[:c])
        top = c == len(courses) - 1
        y = y0 - (0.0 if c % 2 == 0 else rng.uniform(0.1, 0.35))
        while y < y1 - 0.1:
            ln = rng.uniform(0.55, 1.2) if top else rng.uniform(0.35, 0.95)
            ya, yb = max(y0, y), min(y1, y + ln)
            y += ln
            if yb - ya < (0.3 if top else 0.14):
                continue
            # the top course is broken: some stones gone
            if top and rng.random() < 0.24:
                continue
            u = rng.random()
            if top:
                cuts = [] if u < 0.6 else [rng.uniform(0.3, 0.7)]
            else:
                cuts = [] if u < 0.3 else [rng.uniform(0.3, 0.7)] if u < 0.8 else [rng.uniform(0.22, 0.38),
                                                                                      rng.uniform(0.6, 0.78)]
            edges = [0.0] + cuts + [1.0]
            for k in range(len(edges) - 1):
                # the outer faces ragged, stones standing proud or set back
                xa = x - W / 2 + W * edges[k] + (rng.uniform(-0.09, 0.06) if k == 0 else rng.uniform(-0.03, 0.03))
                xb = x - W / 2 + W * edges[k + 1] + (rng.uniform(-0.06, 0.09) if k == len(edges) - 2
                                                     else rng.uniform(-0.03, 0.03))
                gy = rng.uniform(0.035, 0.07)
                gx = rng.uniform(0.03, 0.06)
                sa, sb = ya + gy, yb - gy
                xa2, xb2 = xa + (gx if k > 0 else 0.0), xb - (gx if k < len(edges) - 2 else 0.0)
                sz = ch * rng.uniform(0.84, 1.0)
                key = lit_tone(m, tones, (xa2 + xb2) / 2, (sa + sb) / 2, z0 + sz)
                if top and not cuts and rng.random() < 0.4 and sb - sa > 0.6:
                    # a cracked capstone: two pieces with a slanted gap
                    f = rng.uniform(0.35, 0.65)
                    s = rng.uniform(-0.2, 0.2)
                    ym = sa + (sb - sa) * f
                    a = [(xa2, sa), (xb2, sa), (xb2, ym + s - 0.02), (xa2, ym - s - 0.02)]
                    b = [(xa2, ym - s + 0.02), (xb2, ym + s + 0.02), (xb2, sb), (xa2, sb)]
                    slab(m, key, a, z0, z0 + sz)
                    slab(m, key, b, z0, z0 + sz * rng.uniform(0.94, 1.0))
                else:
                    slab(m, key, chipped(rng, xa2, xb2, sa, sb), z0, z0 + sz)


for _n in T:
    kit.TABLES[_n] = T
    kit.FITKEYS[_n] = [("x", 0.1), ("y0", 0.2), ("y1", 0.2), ("W", 0.1)]

    @kit.model(_n)
    def _b(m):
        p = kit.params(m.name, T)
        m.rng.seed(p["seed"])
        build(m, p)
