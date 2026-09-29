"""AraTree03, the narrowest Aramon poplar, shaped against its own sprite.

A round-planned leafy column on a short bare trunk: nearly parallel sides
to 70 percent of the crown, widest about 40 percent up, closing to a leafy
point in the top 28 percent. The axis leans back a little toward the top so
the classic tip climbs toward the drawn one, and the crown's foot lifts at
the front so the drawn trunk shows under it in classic. The frame is only
50 px wide, so the crown is as wide as the frame allows and a little deeper.
"""
import math

import numpy as np

import _leafy as lf
import kit

CALIB = ("leaves", "core")
LIFT = 1.05

CROWN_ROW = 120            # the drawn crown's lowest row
X0, TIPX = -0.36, 0.2      # the drawn crown's centre at its widest and at the tip, cells
Y0, YTOP = 0.2, 1.8        # the axis leaning back toward the top
ZB, ZT = 1.3, 11.2         # the crown's foot at the back and sides, and its tip
FRONT = 1.8                # how far the foot lifts at the front
RX, RY = 1.2, 1.42         # widest radii, across and deep
# (share of the crown's height, share of the widest radius)
PROFILE = [(0.0, 0.78), (0.1, 0.88), (0.2, 0.94), (0.35, 1.0), (0.5, 1.0), (0.62, 0.96), (0.72, 0.88), (0.8, 0.66),
           (0.88, 0.42), (0.95, 0.18), (1.0, 0.0)]
BARK = (0.27, 0.25, 0.2)


def axis(z):
    u = (z - ZB) / (ZT - ZB)
    return X0 + (TIPX - X0) * float(lf.smooth(0.45, 1.0, u)), Y0 + (YTOP - Y0) * max(u, 0.0) ** 2


def build(spr, rng):
    lump = lf.bumps(np.random.default_rng(31), 18, (0.05, 0.85), 0.08)
    pu, pw = zip(*PROFILE)

    def radius(z, phi):
        u = (z - ZB) / (ZT - ZB)
        # narrower across low down, where the drawn crown is slim, deep all the way
        rx = RX * (0.8 + 0.2 * float(lf.smooth(0.0, 0.35, u)))
        ry = RY + (rx - RY) * float(lf.smooth(0.6, 0.95, u))
        r = float(np.interp(u, pu, pw)) / math.sqrt((math.cos(phi) / rx) ** 2 + (math.sin(phi) / ry) ** 2)
        return r * float(lump(u, phi))

    def under(phi):
        return ZB + FRONT * max(0.0, -math.sin(phi)) ** 1.5

    crown = lf.Crown(axis, radius, under, ZT, rb=0.6)
    sel = spr.mask.copy()
    sel[CROWN_ROW + 1:] = False
    dark, mid, light = kit.palette(spr, sel, 1.25)
    pal = (dark * 0.65, mid * 0.9, light)
    bright = kit.lin(spr.colour(sel, 0.9, 1.0), 1.25) * 1.4
    tex, MAIN, SPRAY, DOTS = kit.leaf_atlas("poplar", "sprig", 5)
    leaves, core, wood = kit.Geo(), kit.Geo(), kit.Geo()
    cs = 0.55
    P, N = crown.sample(0.25, np.random.default_rng(32))
    # the classic frame's sides: leaves near them shrink and carry no sprays
    xmin, xmax = -spr.hx / 16.0, (spr.w - spr.hx) / 16.0
    for p, n in zip(P, N):
        u = (p[2] - ZB) / (ZT - ZB)
        d_edge = min(p[0] - xmin, xmax - p[0])
        edge = float(np.clip(d_edge / 0.5, 0.35, 1.0))
        size = cs * rng.uniform(0.85, 1.2) * (1 - 0.3 * float(lf.smooth(0.72, 1.0, u))) * edge
        col = lf.leaf_colour(pal, rng, float(n[2]), 0.85 + 0.15 * min(1.0, 1.5 * u))
        lf.sprig(leaves, p + n * 0.02, n, size, col, rng, MAIN)
        for _ in range(rng.poisson(0.6)):
            lit = 0.75 + 0.25 * float(np.clip(n[2] + 0.5, 0, 1))
            lf.fleck(leaves, p, n, cs * 0.9 * rng.uniform(0.7, 1.2), bright * lit * rng.uniform(0.85, 1.1), rng, DOTS)
        if d_edge > 0.85 and abs(float(np.dot(n, kit.TO_CAM))) < 0.4 and rng.random() < 0.15:
            lf.spray(leaves, p, n, cs * 0.9 * rng.uniform(0.8, 1.2) * size / cs, col, rng, SPRAY)
    tip = np.array([*axis(ZT), ZT])
    lf.leader(leaves, tip, cs, pal[1] * 0.8, MAIN, rng, n=6)
    crown.core(core, lambda z: 0.8 - 0.38 * float(lf.smooth(0.65, 1.0, (z - ZB) / (ZT - ZB))), pal[0] * 0.55)
    trunk_sel = spr.mask.copy()
    trunk_sel[:CROWN_ROW + 1] = False
    btex, _ = kit.bark_from(spr, trunk_sel, "streak", gain=1.2)
    run = kit.trunk_run(spr, spr.hy - 3, spr.hx)
    r0 = 1.2 * (run[1] - run[0] + 1) / 32.0
    top = ZB + 3.0
    kit.flared_trunk(wood, np.array([0.0, 0.0, 0.0]), np.array([*axis(top), top]), r0, r0 * 0.6, 0.8, 4, rng,
                     kit.lin(BARK, 1.2), sides=10)
    parts = [leaves.to_object(spr.name + "_leaves", kit.material("leaves", kit.image("leaves", tex), cut=True, cull=False)),
             core.to_object(spr.name + "_core", kit.material("core")),
             wood.to_object(spr.name + "_wood", kit.material("bark", kit.image("bark", btex), repeat=True))]
    return parts, {"cards": len(P), "leaf_tris": leaves.tris(), "core_tris": core.tris(), "trunk_r": round(r0, 2)}
