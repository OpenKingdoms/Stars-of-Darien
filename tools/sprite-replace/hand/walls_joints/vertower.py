"""The Veruna corner tower: a flat-topped block with one rounded outer
corner, in the wall's courses (battered plinth, timber band, plaster with
loopholes), a pale coping round a sandy deck and a stair hatch in the
inner corner. The two inner sides, where the walls meet it, are cut
straight on grid lines.

The plan is a rectangle x0..x1, y0..y1 at the plaster face; corner names
the rounded one (SW, SE, NE, NW).
"""
import math

from mathutils import Vector

import kit

# the bands and deck meet the sturdy wall profile (verwall.DEF)
DEF = dict(foot=0.5, bat=1.0, zb=1.6, ledge=0.2, zt=2.9, Hd=5.6, r=1.45, block=1.1, courses=4, rim=0.36)

# the rounded corner's quadrant: (sx, sy) of the corner, the arc's start angle
CORNERS = {"SW": (-1, -1), "SE": (1, -1), "NE": (1, 1), "NW": (-1, 1)}


def ring(p, d, ext=1.5):
    """The plan outline offset d outward, anticlockwise, run past the two
    inner sides by ext (they are cut away), as an open path that starts
    and ends on an inner side."""
    sx, sy = CORNERS[p["corner"]]
    x0, x1, y0, y1 = p["x0"], p["x1"], p["y0"], p["y1"]
    # outer edges get the offset, inner edges run long
    X = {-1: x0 - d, 1: x1 + d}
    Y = {-1: y0 - d, 1: y1 + d}
    Xi = {-1: x0 - ext, 1: x1 + ext}
    Yi = {-1: y0 - ext, 1: y1 + ext}
    xo, yo = X[sx], Y[sy]          # the outer lines
    xi, yi = Xi[-sx], Yi[-sy]      # the inner lines, run long
    r = p["r"] + d
    cx, cy = xo - sx * r, yo - sy * r
    a0 = math.atan2(0, sx)          # pointing out along x
    arc = []
    n = 10
    # from the x-side of the corner round to the y-side
    for i in range(n + 1):
        t = i / n
        ang = (0 if sx > 0 else math.pi) + t * (math.pi / 2) * (1 if sx * sy > 0 else -1)
        arc.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
    # arc runs from (xo, cy) to (cx, yo); close the outline through the inner corner
    pts = [(xo, yi)] + arc + [(xi, yo), (xi, yi)]
    if kit.area2(pts) < 0:
        pts = pts[::-1]
    # an open path starting and ending at the inner corner, which is cut away
    return pts + [pts[0]]


def build(m, p):
    """The tower; p holds the plan, corner and heights (DEF for the rest)."""
    q = dict(DEF)
    q.update(p)
    p = q
    sx, sy = CORNERS[p["corner"]]
    zb, zt, Hd, foot = p["zb"], p["zt"], p["Hd"], p["foot"]
    base = ring(p, 0.0)
    L = kit.path_len(base)
    # closed solids of the outline for every band; sections of the veneer
    # are in (u, z), u to the left of travel, which is inward
    # the whole outer wall leans in (bat over its height), the plinth more (foot)
    zc = Hd - 0.28
    d = lambda z: p["bat"] * (1 - z / zc) + foot * max(0.0, 1 - z / zb)  # noqa: E731
    loft(m, "mortar", [(ring(p, d(0) - 0.05)[:-1], 0.0), (ring(p, d(zb) - 0.03)[:-1], zb)])
    n = p["courses"]
    for c in range(0 if m.fast else n):
        z0, z1 = zb * c / n, zb * (c + 1) / n
        sec = [(-d(z0), z0), (0.35, z0), (0.35, z1 - 0.03), (-d(z1) + 0.04, z1 - 0.03)]
        t = 0.0 if c % 2 == 0 else -p["block"] * 0.5
        while t < L:
            ln = p["block"] * m.rng.uniform(0.7, 1.3)
            ta, tb = max(0.0, t), min(L, t + ln)
            if tb - ta > 0.1:
                sub = kit.sub_path(base, ta + 0.03, tb - 0.03)
                dd = m.rng.uniform(-0.03, 0.04)
                m.sweep(m.rng.choice(("stone", "stone", "stone2", "stone3")), sub,
                        [(u - dd if u < 0 else u, z) for u, z in sec])
            t += ln
    zl = zb + p["ledge"]
    loft(m, "ledge", [(ring(p, d(zb) + 0.1)[:-1], zb - 0.02), (ring(p, d(zl) + 0.1)[:-1], zl)])
    loft(m, "timber", [(ring(p, d(zl) + 0.03)[:-1], zl), (ring(p, d(zt) + 0.03)[:-1], zt)])
    loft(m, "plaster", [(ring(p, d(zt))[:-1], zt), (ring(p, 0.0)[:-1], zc)])
    rim = p["rim"]
    m.sweep("sill", base, [(-0.06, zc), (rim, zc), (rim, Hd), (-0.04, Hd)])
    # the deck, a little below the rim, with a dark line along its edge
    m.prism("deck", ring(p, -rim)[:-1], zc - 0.05, Hd - 0.12)
    m.sweep("hole", base, [(rim, Hd - 0.125), (rim + 0.09, Hd - 0.125), (rim + 0.09, Hd - 0.1), (rim, Hd - 0.1)])
    if not m.fast:
        posts_and_loops(m, p, d, zl, zt, zc)
        hatch(m, p)
    # cut the inner sides straight
    m.clip(p["x1"] if sx < 0 else p["x0"], 0.0, -sx, 0.0)
    m.clip(0.0, p["y1"] if sy < 0 else p["y0"], 0.0, -sy)


def loft(m, key, rings):
    """A closed solid through outlines of equal point count at heights."""
    polys = []
    for (a, za), (b, zb) in zip(rings, rings[1:]):
        n = len(a)
        for i in range(n):
            j = (i + 1) % n
            polys.append([(a[i][0], a[i][1], za), (a[j][0], a[j][1], za), (b[j][0], b[j][1], zb),
                          (b[i][0], b[i][1], zb)])
    polys.append([(x, y, rings[0][1]) for x, y in rings[0][0]][::-1])
    polys.append([(x, y, rings[-1][1]) for x, y in rings[-1][0]])
    m.solid(key, polys)


def posts_and_loops(m, p, d, zl, zt, zc):
    """Timber posts round the band and loopholes on the outer faces, each
    set on the leaning wall at its height."""
    zm = (zl + zt) / 2
    band = ring(p, d(zm) + 0.05)
    L = kit.path_len(band)
    k = max(1, int(round(L / 1.05)))
    for i in range(k + 1):
        pos, dv, _ = kit.point_at(band, L * i / k)
        m.obox("post", pos.x, pos.y, zm, 0.16, 0.1, zt - zl, yaw=math.atan2(dv.y, dv.x))
    zc2 = zt + 0.95
    row = ring(p, d(zc2))
    L = kit.path_len(row)
    t = 0.6
    sx, sy = CORNERS[p["corner"]]
    while t < L - 0.5:
        pos, dv, _ = kit.point_at(row, t)
        out = Vector((dv.y, -dv.x))
        # only on the outer faces and the round, clear of the cut sides
        xi, yi = (p["x1"] if sx < 0 else p["x0"]), (p["y1"] if sy < 0 else p["y0"])
        clear = (pos.x - xi) * sx > 0.5 and (pos.y - yi) * sy > 0.5
        if (out.x * sx > 0.3 or out.y * sy > 0.3) and clear:
            yaw = math.atan2(dv.y, dv.x)
            # set into the leaning face: a plaster surround, a sill, the dark slot sunk in them
            c1 = pos + out * 0.01
            m.obox("plaster", c1.x, c1.y, zc2 + 0.02, 0.44, 0.08, 0.36, yaw=yaw)
            c3 = pos + out * 0.02
            m.obox("sill", c3.x, c3.y, zc2 - 0.2, 0.46, 0.1, 0.08, yaw=yaw)
            c2 = pos - out * 0.03
            m.obox("hole", c2.x, c2.y, zc2, 0.22, 0.16, 0.28, yaw=yaw)
        t += 1.05


def hatch(m, p):
    """A stair hatch in the deck's inner corner: a dark well, steps going down."""
    sx, sy = CORNERS[p["corner"]]
    cx = (p["x1"] if sx < 0 else p["x0"]) - (-sx) * 0.62
    cy = (p["y1"] if sy < 0 else p["y0"]) - (-sy) * 0.62
    Hd = p["Hd"]
    s = 0.8
    m.box("hole", cx - s / 2, cx + s / 2, cy - s / 2, cy + s / 2, Hd - 0.3, Hd - 0.06)
    m.box("sill", cx - s / 2 - 0.08, cx + s / 2 + 0.08, cy - s / 2 - 0.08, cy + s / 2 + 0.08, Hd - 0.14, Hd - 0.08)
    # treads stepping down across the well
    for i in range(4):
        f = (i + 0.5) / 4
        yy = cy - s / 2 + f * s
        m.box("rafter", cx - s / 2 + 0.08, cx + s / 2 - 0.08, yy - 0.08, yy + 0.08, Hd - 0.12 - 0.18 * i - 0.05,
              Hd - 0.12 - 0.18 * i)
