"""The Aramon curtain wall, swept along a plan path.

A wall of dark ashlar laid in courses, the pale mortar showing in the
joints; on top a sunken wall walk of pale flags between two parapets, each
a low wall with merlons capped in pale stone. Buttresses are battered
spurs set against a face.
"""
import math

from mathutils import Matrix, Vector

import kit

DEF = dict(
    D=1.4,        # thickness
    H=4.1,        # height of the wall walk
    ph=0.24,      # parapet height above the walk, under the merlons
    mh=0.64,      # merlon top above the walk
    mw=0.9,       # merlon length along the wall
    gap=0.5,      # crenel width
    tp=0.26,      # parapet thickness
    course=0.62,  # ashlar course height
    block=1.05,   # mean block length
)


def palette(m, stone=None, mortar=None, cap=None, walk=None):
    whole = (0, 0, m.w, m.h)

    def lum(r, g, b):
        return 0.3 * r + 0.59 * g + 0.11 * b

    def cal(c, k):
        return tuple(min(255.0, v * k) for v in c)
    stone = stone or m.sample(*whole, pick=lambda r, g, b: 25 < lum(r, g, b) < 60)
    mortar = mortar or m.sample(*whole, pick=lambda r, g, b: 70 < lum(r, g, b) < 110)
    cap = cap or m.sample(*whole, pick=lambda r, g, b: lum(r, g, b) > 125)
    m.col("stone", cal(stone, 0.92), 0.95)
    m.shade("stone2", "stone", 0.85, 0.95)
    m.shade("stone3", "stone", 1.18, 0.95)
    m.col("mortar", cal(mortar, 1.05), 1.0)
    m.col("cap", cal(cap, 0.92), 0.9)
    m.shade("cap2", "cap", 0.88, 0.9)
    m.col("walk", cal(walk or cap, 1.08), 0.7)
    m.shade("gutter", "walk", 0.6, 0.9)


# the owner's sturdy rule: a lower, thicker wall than the pixel-exact fit
STURDY_H, STURDY_D = 0.87, 1.18


def build(m, pts, closed=False, buttresses=(), top=True, hfn=None, cap=False, groove=0.0, **over):
    """One wall along pts. buttresses: (t, side, width at the ground, width
    at the top, depth at the ground, height) spurs, side +1 on the left.
    hfn(t), for a ruin, is how high the broken wall stands t along the path:
    blocks above it are gone and there is no walk. groove, for a ruin,
    splits its top course in two, the old walk left as a dark channel that
    wide down the middle."""
    p = dict(DEF)
    p.update(over)
    D, H = p["D"], p["H"]
    h = D / 2
    L = kit.path_len(pts)
    # the mortar core, a hair inside the blocks
    if hfn is None:
        m.sweep("mortar", pts, [(-h + 0.012, 0), (h - 0.012, 0), (h - 0.012, H - 0.05), (-h + 0.012, H - 0.05)],
                closed=closed)
    else:
        top = False
        t = 0.0
        while t < L:
            tb = min(L, t + 0.4)
            zc = min(H, hfn((t + tb) / 2)) - 0.3
            if groove:
                # the core stays under the channel's floor
                zc = min(zc, top_course(H, hfn((t + tb) / 2), p["course"])[0] + 0.02)
            if zc > 0.1:
                m.sweep("mortar", kit.sub_path(pts, t, tb), [(-h + 0.03, 0), (h - 0.03, 0), (h - 0.03, zc),
                                                             (-h + 0.03, zc)])
            t = tb
    n = max(1, int(round((H - 0.05) / p["course"])))
    if m.fast:
        n = 0
    for c in range(n):
        z0, z1 = (H - 0.05) * c / n, (H - 0.05) * (c + 1) / n
        t = 0.0 if c % 2 == 0 else -p["block"] * 0.5
        while t < L:
            ln = p["block"] * m.rng.uniform(0.75, 1.3)
            ta, tb = max(0.0, t), min(L, t + ln)
            gap = 0.035
            if hfn is not None and z0 > hfn((ta + tb) / 2) - 0.1:
                t += ln
                continue
            if tb - ta > 0.1:
                sub = kit.sub_path(pts, ta + (gap if ta > 0 else 0), tb - (gap if tb < L else 0))
                d = m.rng.uniform(0.0, 0.03)
                split = groove and z0 >= top_course(H, hfn((ta + tb) / 2), p["course"])[0] - 0.01
                if split:
                    g = groove / 2
                    for s0, s1 in ((-h - d, -g), (g, h + d)):
                        sec = [(s0, z0 + 0.035), (s1, z0 + 0.035), (s1, z1 - 0.035), (s0, z1 - 0.035)]
                        m.sweep(m.rng.choice(("stone", "stone", "stone2", "stone3")), sub, sec)
                    # the channel's dark floor, and its sides in shadow
                    m.sweep("groove", sub, [(-g - 0.01, z0 + 0.02), (g + 0.01, z0 + 0.02), (g + 0.01, z0 + 0.09),
                                            (-g - 0.01, z0 + 0.09)])
                    for s0, s1 in ((-g, -g + 0.025), (g - 0.025, g)):
                        m.sweep("groove", sub, [(s0, z0 + 0.05), (s1, z0 + 0.05), (s1, z1 - 0.06), (s0, z1 - 0.06)])
                else:
                    sec = [(-h - d, z0 + 0.035), (h + d, z0 + 0.035), (h + d, z1 - 0.035), (-h - d, z1 - 0.035)]
                    m.sweep(m.rng.choice(("stone", "stone", "stone2", "stone3")), sub, sec)
            t += ln
    for b in buttresses:
        buttress(m, pts, p, *b)
    if hfn is not None and cap:
        # a broken top shows the pale rubble fill and walk flags, flush
        # with the top course still standing there
        t = 0.0
        while t < L - 1e-6:
            tb = min(L, t + 0.35)
            zt = ruin_top(H, hfn((t + tb) / 2), p["course"])
            if zt > 0.5:
                m.sweep("walk", kit.sub_path(pts, t, tb), [(-h + 0.06, zt - 0.05), (h - 0.06, zt - 0.05),
                                                           (h - 0.06, zt + 0.03), (-h + 0.06, zt + 0.03)])
            t = tb
    if not top:
        return p
    # the walk: pale flags dipping to a gutter down the middle
    wi = h - p["tp"]
    m.sweep("walk", pts, [(-wi, H - 0.12), (0.0, H - 0.12), (0.0, H - 0.02), (-wi, H + 0.06)], closed=closed)
    m.sweep("walk", pts, [(0.0, H - 0.12), (wi, H - 0.12), (wi, H + 0.06), (0.0, H - 0.02)], closed=closed)
    # parapets: a low wall along each edge, merlons on it
    for s in (-1, 1):
        u0, u1 = s * (h - p["tp"]), s * h
        lo, hi = min(u0, u1), max(u0, u1)
        m.sweep("stone2", pts, [(lo, H - 0.1), (hi, H - 0.1), (hi, H + p["ph"]), (lo, H + p["ph"])], closed=closed)
        m.sweep("cap2", pts, [(lo - 0.03, H + p["ph"]), (hi + 0.03, H + p["ph"]), (hi + 0.03, H + p["ph"] + 0.07),
                              (lo - 0.03, H + p["ph"] + 0.07)], closed=closed)
        if m.fast:
            m.sweep("stone", pts, [(lo, H + p["ph"]), (hi, H + p["ph"]), (hi, H + p["mh"]), (lo, H + p["mh"])],
                    closed=closed)
            continue
        step = p["mw"] + p["gap"]
        t = (step - p["mw"]) / 2 + (step / 2 if s > 0 else 0.0)
        while t < L:
            ta, tb = t, min(L, t + p["mw"])
            if tb - ta > 0.2:
                sub = kit.sub_path(pts, ta, tb)
                m.sweep("stone3", sub, [(lo, H + p["ph"]), (hi, H + p["ph"]), (hi, H + p["mh"] - 0.1),
                                        (lo, H + p["mh"] - 0.1)])
                m.sweep("cap", sub, [(lo - 0.05, H + p["mh"] - 0.1), (hi + 0.05, H + p["mh"] - 0.1),
                                     (hi + 0.03, H + p["mh"]), (lo - 0.03, H + p["mh"])])
            t += step
    return p


def top_course(H, hs, course=DEF["course"]):
    """The bottom and top of the highest course of a broken wall standing hs high."""
    n = max(1, int(round((H - 0.05) / course)))
    ch = (H - 0.05) / n
    k = max(0, int((min(H, hs) - 0.1) / ch))
    return k * ch, (k + 1) * ch


def ruin_top(H, hs, course=DEF["course"]):
    """The top of the highest course of a broken wall standing hs high."""
    n = max(1, int(round((H - 0.05) / course)))
    ch = (H - 0.05) / n
    k = int((min(H, hs) - 0.1) / ch)
    return (k + 1) * ch - 0.035 if hs > 0.1 else 0.0


def buttress(m, pts, p, t, side, w0, w1, depth, bh):
    """A battered spur against the face: wide and deep at the ground, as
    wide as w1 and flush with the wall at height bh."""
    pos, d, _ = kit.point_at(pts, t)
    nrm = Vector((-d.y, d.x)) * side
    face = p["D"] / 2 - 0.02

    def P(a, off, z):
        q = pos + d * a + nrm * (face + off)
        return (q.x, q.y, z)
    A, B = P(-w0 / 2, 0, 0), P(w0 / 2, 0, 0)
    C, Dd = P(w1 / 2, depth, 0), P(-w1 / 2, depth, 0)
    E, F = P(-w1 / 2, 0, bh), P(w1 / 2, 0, bh)
    # a little flat on top where the spur meets the wall
    E2, F2 = P(-w1 / 2, 0.22, bh - 0.12), P(w1 / 2, 0.22, bh - 0.12)
    m.solid("stone", [[A, B, C, Dd], [Dd, C, F2, E2], [E2, F2, F, E], [A, Dd, E2, E], [B, F, F2, C], [A, E, F, B]])
    # courses on the spur's front: a few pale joints
    if not m.fast:
        for k in range(1, 5):
            f = k / 5.0
            z = bh * f - 0.12 * f
            off = depth * (1 - f) + 0.22 * f + 0.01
            q0, q1 = P(-w1 / 2, off, z), P(w1 / 2, off, z)
            m.beam("mortar", q0, q1, 0.05, 0.05)


def section(m, zfn, cx, cy, yaw, pitch, roll, L, D, hh=0.7, sink=0.1):
    """A fallen length of wall top: a course of ashlar, the walk and both
    parapets with their merlons, built level along x, turned and tilted,
    then dropped onto the heap (zfn) so it lies on it."""
    import bmesh  # noqa: F401
    before = {k: len(bm.verts) for k, bm in m.groups.items()}
    build(m, [(-L / 2, 0.0), (L / 2, 0.0)], H=hh, D=D)
    M = kit._mat4((cx, cy, 0.0), (yaw, pitch, roll), (1.0, 1.0, 1.0)) @ Matrix.Translation((0, 0, -hh / 2))
    new = []
    for k, bm in m.groups.items():
        bm.verts.ensure_lookup_table()
        new.extend(bm.verts[before.get(k, 0):])
    for v in new:
        v.co = M @ v.co
    lift = max(max(0.0, zfn(v.co.x, v.co.y)) - v.co.z for v in new) - sink
    for v in new:
        v.co.z += lift
