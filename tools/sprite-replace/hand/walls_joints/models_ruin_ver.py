"""Veruna wall ruins: the intact piece's line and profile (its fitted
placement), the wall broken to a hand-read height profile (prof, t along
the plan path, which starts 4 cells before the front cut of a diagonal
and 6 before the vertex of a bend; heights in the sturdy profile), a
ragged top with pale torn plaster edges, torn sheets of the tiled roof
resting on it where the drawing shows them, burnt timbers on the top and
rubble at its foot.

A sheet is read off the drawing: base, two sprite pixels of its edge
resting on the broken top; tips, the rest of its torn outline in sprite
pixels, found on the plane that rises from the base at pitch degrees.
fold=(k, i, j) makes a sheet's base the edge i-j of sheet k instead, so
a torn roof can bend.
"""
import math

from mathutils import Vector

import kit
import models_ver as mv
import rubble
import verwall as vw

T = {
    # the drawing's sheets are a deep brick red and its broken top charred
    # near black: tile and soot given, not sampled
    "VerWall10a": dict(base="VerWall10", heap=0.8, spread=2.2, n=60, seed=10, cap="soot",
                       tile=(92, 31, 18), soot=(24, 19, 17), pale=0.3,
                       prof=[(4.0, 4.4), (4.8, 4.2), (5.3, 3.4), (6.0, 2.9), (6.8, 3.2), (7.4, 3.6), (8.0, 4.3),
                             (9.0, 4.5)],
                       sheets=[dict(base=[(15, 47), (50, 50)], pitch=40,
                                    tips=[(47, 42), (44, 34), (40, 26), (38, 22), (34, 26), (28, 32), (22, 38),
                                          (17, 42)]),
                               dict(base=[(94, 23), (101, 22)], pitch=38,
                                    tips=[(107, 16), (113, 9), (117, 3), (110, 2), (104, 1), (96, 3), (88, 4),
                                          (89, 10), (92, 16)])]),
    "VerWall11a": dict(base="VerWall11", heap=0.8, spread=2.2, n=60, seed=11, cap="soot",
                       prof=[(4.0, 4.2), (4.6, 4.0), (5.2, 3.6), (6.0, 3.8), (6.8, 3.4), (7.4, 3.9), (8.0, 4.4),
                             (8.8, 4.5)],
                       sheets=[dict(base=[(34, 26), (42, 25)], pitch=38,
                                    tips=[(46, 13), (44, 8), (43, 4), (36, 3), (31, 1), (22, 0), (17, 2), (11, 2),
                                          (14, 6), (18, 11), (22, 16), (30, 20)]),
                               dict(base=[(79, 55), (117, 52)], pitch=42,
                                    tips=[(111, 44), (106, 38), (100, 33), (96, 28), (92, 26), (86, 30),
                                          (81, 35)])]),
    "VerJoint08a": dict(base="VerJoint08", heap=1.5, spread=2.6, n=90, seed=8, cap="soot",
                        prof=[(3.6, 4.1), (4.6, 4.2), (5.4, 4.3), (6.0, 4.4), (6.6, 4.1), (7.2, 3.8), (7.7, 3.9),
                              (8.1, 3.4)],
                        sheets=[dict(base=[(51, 65), (67, 63)], pitch=12,
                                     tips=[(70, 32), (50, 34)]),
                                dict(fold=(0, 2, 3), pitch=40,
                                     tips=[(86, 22), (84, 12), (76, 8), (66, 6), (56, 5), (46, 6), (38, 3),
                                           (28, 1), (26, 4), (34, 9), (44, 13), (48, 22)])]),
}


def interp(prof):
    def f(t):
        if t <= prof[0][0]:
            return prof[0][1]
        for (a, ha), (b, hb) in zip(prof, prof[1:]):
            if a <= t <= b:
                return ha + (hb - ha) * (t - a) / (b - a)
        return prof[-1][1]
    return f


def on_top(m, pts, top, cr):
    """The world point drawn at sprite pixel cr lying on the broken top."""
    c, r = cr
    x = m.X(c)
    z = 4.0
    for _ in range(5):
        y = m.Y(r, z)
        z = top(nearest_t(pts, x, y)) + 0.03
    return z


def on_plane(m, cr, P, N):
    """The world point drawn at sprite pixel cr on the plane through P
    with normal N: along the view ray, (x, y0 - k, 2k) all draw at cr."""
    c, r = cr
    x = m.X(c)
    y0 = (m.hy - r) / 16.0
    k = (-N.x * (x - P.x) - N.y * (y0 - P.y) + N.z * P.z) / (-N.y + 2 * N.z)
    return Vector((x, y0 - k, 2 * k))


def sheets(m, q, pts, top, specs):
    """Builds the torn roof sheets; returns their outlines."""
    done = []
    for sp in specs:
        if "fold" in sp:
            k, i, j = sp["fold"]
            P1, P2 = done[k][i], done[k][j]
        else:
            z = max(on_top(m, pts, top, cr) for cr in sp["base"])
            P1, P2 = [Vector((m.X(c), m.Y(r, z), z)) for c, r in sp["base"]]
        e = P2 - P1
        h = Vector((-e.y, e.x, 0.0)).normalized()
        a = math.radians(sp["pitch"])
        best = None
        for sgn in (1, -1):
            V = h * sgn * math.cos(a) + Vector((0, 0, math.sin(a)))
            N = e.cross(V).normalized()
            tips = [on_plane(m, cr, P1, N) for cr in sp["tips"]]
            ahead = sum((t - P1).dot(V) for t in tips)
            if best is None or ahead > best[0]:
                best = (ahead, tips)
        poly = [P1, P2] + best[1]
        vw.sheet(m, q, [tuple(v) for v in poly])
        done.append(poly)
    return done


def build(m, r):
    p = kit.params(r["base"], mv.T)
    vw.palette(m)
    if r.get("tile"):
        # a deeper tile with its sheen dulled, so the sky does not wash it pink
        m.col("tile", r["tile"], 0.8, spec=r.get("tile_spec", 0.1))
        m.shade("tile2", "tile", 0.92, 0.8)
        m.shade("tile3", "tile", 1.05, 0.8)
        m.shade("tile_edge", "tile", 0.62, 0.85)
    m.col("char", (34, 28, 24), 1.0)
    m.col("soot", r.get("soot", (52, 46, 40)), 1.0)
    # torn plaster catching the light: the plaster a shade paler
    m.shade("pale", "plaster", 1.35, 0.95)
    tones = rubble.palette(m, k=5, prefix="rub", gain=1.0, floor=25)
    pts, _ = mv.frame(m, p)
    prof = mv.profile(p, cope=0.0)
    q = dict(vw.DEF)
    q.update(prof)
    q, top = vw.ruin(m, pts, p["high"], interp(r["prof"]), cap=r["cap"], pale=r.get("pale", 0.55), **prof)
    W = q["W"]
    # torn sheets of roof resting on the broken top
    sheets(m, q, pts, top, r["sheets"])
    # rubble at the wall's foot, kept inside the drawing
    line = pts

    def heap(x, y):
        dd = rubble.line_dist(line, x, y)
        return r["heap"] * max(0.0, 1.0 - dd / r["spread"])
    xs = [pt[0] for pt in pts]
    ys = [pt[1] for pt in pts]
    box = (max(min(xs), m.X(0) - 1), min(max(xs), m.X(m.w) + 1), max(min(ys), m.Y(m.h) - 1), min(max(ys), m.Y(0) + 1))
    Z = rubble.mound(m, tones, box, heap, cell=0.3, seed=r["seed"])
    rubble.blocks(m, tones, box, r["n"], size=(0.12, 0.38), zfn=lambda x, y: rubble.height_at(Z, x, y),
                  seed=r["seed"])

    # debris lying on the broken top, resting on it
    def over_top(x, y):
        return rubble.line_dist(pts, x, y) < W * 0.8

    def top_z(x, y):
        return top(nearest_t(pts, x, y)) + 0.03
    rubble.blocks(m, tones, box, r["n"] // 2, size=(0.1, 0.28), zfn=top_z, near=over_top, seed=r["seed"] + 1)
    rng = m.rng
    placed = 0
    for _ in range(400):
        if placed >= 5:
            break
        x0, x1, y0, y1 = box
        x, y = rng.uniform(x0, x1), rng.uniform(y0, y1)
        if not over_top(x, y):
            continue
        t = nearest_t(pts, x, y)
        if top(t) < q["zt"] or not rubble.drawn(m, x, y, top(t)):
            continue
        # a burnt timber lying across the top, on its highest point
        a = rng.uniform(0, math.pi)
        ln = rng.uniform(0.7, 1.4)
        ex, ey = x + math.cos(a) * ln, y + math.sin(a) * ln
        if not over_top(ex, ey):
            continue
        zz = max(top(nearest_t(pts, x + math.cos(a) * ln * f, y + math.sin(a) * ln * f)) for f in
                 (0, 0.25, 0.5, 0.75, 1.0)) + 0.03 + 0.06
        m.beam("char", (x, y, zz), (ex, ey, zz), 0.12, 0.12, roll=rng.uniform(0, 1))
        placed += 1


def nearest_t(pts, x, y):
    """Distance along the path to the point nearest (x, y)."""
    from mathutils import Vector
    best, bt, acc = 1e9, 0.0, 0.0
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1])
        d = b - a
        L = d.length
        u = max(0.0, min(1.0, (Vector((x, y)) - a).dot(d) / max(1e-9, L * L)))
        dist = (a + d * u - Vector((x, y))).length
        if dist < best:
            best, bt = dist, acc + u * L
        acc += L
    return bt


for _n in T:
    kit.TABLES[_n] = T
    kit.FITKEYS[_n] = []

    @kit.model(_n)
    def _b(m):
        build(m, kit.params(m.name, T))
