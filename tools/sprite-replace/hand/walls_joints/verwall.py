"""The Veruna curtain wall, swept along a plan path.

From the ground up: a battered plinth of dark stone blocks laid in courses,
a grey string course, a band of dark timber with posts, a plaster wall
with loopholes, and a mono-pitch roof of terracotta tile courses over the
wall walk, its rafter ends showing under the high eave. The roof slopes
across the wall; high="L" puts its high side left of the path's travel.
"""
import json
import math
import os

from mathutils import Vector

import kit

# per model, the tile colour's correction found by comparing the classic
# render's roof with the drawing's (tilecal.py)
TILECAL = os.path.join(kit.HERE, "tilecal.json")
_CAL = None


def tile_gain(name):
    global _CAL
    if _CAL is None:
        _CAL = json.load(open(TILECAL)) if os.path.exists(TILECAL) else {}
    return _CAL.get(name, (1.0, 1.0, 1.0))

# the sturdy profile shared by every Veruna wall, joint and ruin: about 15
# percent lower and 20 percent thicker than a pixel-exact fit, so a run
# does not read as a thin tower from a low view
DEF = dict(
    foot=2.2,     # half-width of the plinth at the ground
    zb=1.6,       # top of the stone plinth
    ledge=0.2,    # string course height
    zt=2.75,      # top of the timber band
    W=1.2,        # half-width of the timber and plaster wall
    zlo=4.35,     # roof underside over the low face
    zhi=5.3,      # roof underside over the high face
    ov=0.24,      # roof overhang past the high face
    ovl=0.12,     # roof overhang past the low face
    cope=0.85,    # width of the pale coping under the high eave, 0 for none
    rt=0.14,      # roof slab thickness
    courses=4,    # plinth courses
    block=1.15,   # mean plinth block length
    col=0.56,     # tile column width
    course=0.46,  # tile course depth along the slope
    loops=1.4,    # loophole spacing, 0 for none
    rafters=1.0,  # rafter spacing under the high eave
)


def palette(m, roof=None, stone=None, plaster=None, timber=None):
    """Materials sampled from the sprite, by class of pixel, unless given."""
    def is_roof(r, g, b):
        return r > 1.6 * g and r > 90

    def is_stone(r, g, b):
        return 22 < 0.3 * r + 0.59 * g + 0.11 * b < 60

    def is_plaster(r, g, b):
        lum = 0.3 * r + 0.59 * g + 0.11 * b
        return 70 < lum < 160 and abs(r - g) < 25 and abs(g - b) < 30

    def is_timber(r, g, b):
        lum = 0.3 * r + 0.59 * g + 0.11 * b
        return r > g * 1.12 and r > b * 1.3 and 35 < lum < 100
    whole = (0, 0, m.w, m.h)

    def cal(c, k):
        # the preview's grey fill lifts darks and washes out the tiles
        return tuple(min(255.0, v * f) for v, f in zip(c, k))
    roof = cal(cal(roof or m.sample(*whole, pick=is_roof), (1.15, 0.95, 0.8)), tile_gain(m.name))
    stone = cal(stone or m.sample(*whole, pick=is_stone), (0.7, 0.66, 0.7))
    plaster = cal(plaster or m.sample(*whole, pick=is_plaster), (0.9, 0.95, 0.9))
    timber = cal(timber or m.sample(*whole, pick=is_timber), (0.75, 0.72, 0.76))
    m.col("tile", roof, 0.8)
    m.shade("tile2", "tile", 0.92, 0.8)
    m.shade("tile3", "tile", 1.05, 0.8)
    m.shade("tile_edge", "tile", 0.62, 0.85)
    m.col("stone", stone, 0.95)
    m.shade("stone2", "stone", 0.82, 0.95)
    m.shade("stone3", "stone", 1.22, 0.95)
    m.shade("mortar", "stone", 0.55, 1.0)
    m.col("plaster", plaster, 0.95)
    m.shade("ledge", "plaster", 0.95, 0.95)
    m.col("sill", (215, 190, 150), 0.9)
    m.col("timber", timber, 0.9)
    m.shade("post", "timber", 0.72, 0.9)
    m.col("rafter", (170, 140, 105), 0.9)
    m.col("hole", (18, 16, 14), 1.0)


def roof_z(p, high, u):
    """Height of the roof's top at offset u (the slope continues past the faces)."""
    s = 1.0 if high == "L" else -1.0
    zmid = (p["zlo"] + p["zhi"]) / 2 + p["rt"]
    k = (p["zhi"] - p["zlo"]) / (2 * p["W"])
    return zmid + s * k * u


def build(m, pts, high="L", closed=False, caps=True, gate=None, skip_loops=(), **over):
    """One wall along pts; gate opens a doorway (see passage). Returns the
    profile used."""
    p = dict(DEF)
    p.update(over)
    W, foot, zb = p["W"], p["foot"], p["zb"]
    s = 1.0 if high == "L" else -1.0
    L = kit.path_len(pts) if not closed else None

    # the plinth: a mortar core and courses of blocks, each course battered
    n = 1 if m.fast else p["courses"]
    hw = lambda z: foot + (W + 0.04 - foot) * z / zb  # noqa: E731
    m.sweep("mortar", pts, [(-hw(0) + 0.05, 0), (hw(0) - 0.05, 0), (hw(zb) - 0.05, zb), (-hw(zb) + 0.05, zb)],
            closed=closed, caps=caps)
    for c in range(n):
        z0, z1 = zb * c / n, zb * (c + 1) / n
        ch = min(0.07, (z1 - z0) * 0.2)
        a, b, bt = hw(z0), hw(z1 - ch), hw(z1)
        sec = [(-a, z0), (a, z0), (b, z1 - ch), (bt - ch - 0.02, z1), (-bt + ch + 0.02, z1), (-b, z1 - ch)]
        t = 0.0 if c % 2 == 0 else -p["block"] * 0.5
        while L is not None and t < L and not m.fast:
            ln = p["block"] * m.rng.uniform(0.7, 1.3)
            ta, tb = max(0.0, t), min(L, t + ln)
            parts = [(ta, tb)]
            for pa, pb in parts:
                if pb - pa < 0.08:
                    continue
                gap = 0.03
                sub = kit.sub_path(pts, pa + (gap if pa > 0 else 0), pb - (gap if pb < L else 0))
                d = m.rng.uniform(-0.03, 0.04)
                sec2 = [(u + (d if u > 0 else -d), z) for u, z in sec]
                key = m.rng.choice(("stone", "stone", "stone2", "stone3"))
                m.sweep(key, sub, sec2, caps=True)
            t += ln
    # the string course
    zl = zb + p["ledge"]
    m.sweep("ledge", pts, [(-W - 0.1, zb - 0.02), (W + 0.1, zb - 0.02), (W + 0.1, zl), (-W - 0.1, zl)],
            closed=closed, caps=caps)
    # timber band: a dark core, posts and a rail on both faces
    zt = p["zt"]
    m.sweep("timber", pts, [(-W - 0.03, zl), (W + 0.03, zl), (W + 0.03, zt), (-W - 0.03, zt)],
            closed=closed, caps=caps)
    if L is not None and not m.fast:
        k = max(1, int(round(L / 1.05)))
        for i in range(k + 1):
            pos, d, _ = kit.point_at(pts, L * i / k)
            nrm = Vector((-d.y, d.x))
            for side in (-1, 1):
                c = pos + nrm * side * (W + 0.05)
                m.obox("post", c.x, c.y, (zl + zt) / 2, 0.16, 0.08, zt - zl, yaw=math.atan2(d.y, d.x))
        for side in (-1, 1):
            m.sweep("post", pts, [(side * (W + 0.02), zl + (zt - zl) * 0.45),
                                  (side * (W + 0.07), zl + (zt - zl) * 0.45),
                                  (side * (W + 0.07), zl + (zt - zl) * 0.55),
                                  (side * (W + 0.02), zl + (zt - zl) * 0.55)], caps=True)
    # plaster up to the roof's underside, its top following the slope
    zr = lambda u: roof_z(p, high, u) - p["rt"]  # noqa: E731
    cw = p["cope"]
    if cw > 0:
        # a pale coping band on the wall top under the high eave, behind a
        # thin skin of plaster, that shows where the wall is cut
        a, b = s * (W - cw), s * (W - 0.14)
        lo_u, hi_u = min(a, b), max(a, b)
        ch = 0.42
        # anticlockwise round the section, the notch cut into the top on the high side
        plaster = [(-W, zt), (W, zt), (W, zr(W)), (hi_u, zr(hi_u)), (hi_u, zr(hi_u) - ch),
                   (lo_u, zr(lo_u) - ch), (lo_u, zr(lo_u)), (-W, zr(-W))]
        m.sweep("plaster", pts, plaster, closed=closed, caps=caps)
        m.sweep("sill", pts, [(lo_u, zr(lo_u) - ch), (hi_u, zr(hi_u) - ch), (hi_u, zr(hi_u)), (lo_u, zr(lo_u))],
                closed=closed, caps=caps)
    else:
        m.sweep("plaster", pts, [(-W, zt), (W, zt), (W, zr(W)), (-W, zr(-W))], closed=closed, caps=caps)
    # loopholes on both faces
    if L is not None and p["loops"] and not m.fast:
        k = max(1, int(L / p["loops"]))
        for side in (-1, 1):
            ztop = zr(side * W)
            if ztop - zt < 0.9:
                continue
            zc = zt + min(1.0, (ztop - zt) * 0.45)
            for i in range(k):
                t = L * (i + 0.5) / k
                if any(abs(t - q) < 0.4 for q in skip_loops):
                    continue
                pos, d, _ = kit.point_at(pts, t)
                nrm = Vector((-d.y, d.x)) * side
                yaw = math.atan2(d.y, d.x)
                c = pos + nrm * (W + 0.03)
                m.obox("sill", c.x, c.y, zc - 0.2, 0.44, 0.08, 0.08, yaw=yaw)
                m.obox("plaster", c.x, c.y, zc + 0.02, 0.44, 0.06, 0.36, yaw=yaw)
                c2 = pos + nrm * (W + 0.05)
                m.obox("hole", c2.x, c2.y, zc, 0.22, 0.06, 0.28, yaw=yaw)
    # the roof: a slab, tile courses on top, rafter ends under the high eave
    U = W + p["ov"]
    rt = p["rt"]
    ua, ub = sorted((s * U, -s * (W + p["ovl"])))
    m.sweep("tile_edge", pts, [(ua, roof_z(p, high, ua) - rt), (ub, roof_z(p, high, ub) - rt),
                               (ub, roof_z(p, high, ub) - 0.02), (ua, roof_z(p, high, ua) - 0.02)],
            closed=closed, caps=caps)
    if not m.fast:
        tile_roof(m, pts, p, high, closed)
    if L is not None and p["rafters"] and not m.fast:
        k = max(1, int(round(L / p["rafters"])))
        for i in range(k + 1):
            t = min(L - 0.1, max(0.1, L * i / k))
            pos, d, _ = kit.point_at(pts, t)
            nrm = Vector((-d.y, d.x)) * s
            a = pos + nrm * (W - 0.2)
            b = pos + nrm * (U + 0.12)
            za = roof_z(p, high, s * (W - 0.2)) - rt - 0.1
            zb2 = roof_z(p, high, s * (U + 0.12)) - rt - 0.1
            m.beam("rafter", (a.x, a.y, za), (b.x, b.y, zb2), 0.13, 0.15)
    if gate:
        passage(m, pts, p, gate)
    return p


def tile_roof(m, pts, p, high, closed=False):
    """Tile courses over each segment of the roof: rolls running down the
    slope and a lip at every course, as a height field on the slab's top."""
    P, M = kit.mitres(pts, closed)
    s = 1.0 if high == "L" else -1.0
    lo, hi = -s * (p["W"] + p["ovl"]), s * (p["W"] + p["ov"])
    n = len(P)
    segs = n if closed else n - 1
    for i in range(segs):
        j = (i + 1) % n
        z_lo, z_hi = kit_roof(p, high, lo), kit_roof(p, high, hi)
        A = Vector((*(P[i] + M[i] * lo), z_lo))
        B = Vector((*(P[j] + M[j] * lo), z_lo))
        C = Vector((*(P[j] + M[j] * hi), z_hi))
        D = Vector((*(P[i] + M[i] * hi), z_hi))
        # A-B is the eave, D-C the high edge; keep the surface facing up
        field(m, A, B, C, D, p["col"], p["course"])
        if i > 0 or closed:
            # hip or valley tiles along the mitre
            m.beam("tile_edge", tuple(A + Vector((0, 0, 0.05))), tuple(D + Vector((0, 0, 0.05))), 0.16, 0.1)


def kit_roof(p, high, u):
    return roof_z(p, high, u) - 0.02


def field(m, A, B, C, D, colw=0.56, course=0.46, a=0.055, b=0.06, keys=("tile", "tile2", "tile3"), keep=None):
    """The tiled top of a roof quad (eave A-B, top D-C): a triangle wave
    across (the rolls) and a sawtooth up the slope (each course's lip)."""
    ln = max((B - A).length, (C - D).length)
    nc = max(1, int(round(ln / colw)))
    depth = max((D - A).length, (C - B).length)
    nv = max(1, int(round(depth / course)))
    nrm = (B - A).cross(D - A)
    if nrm.z < 0:
        nrm = -nrm
    nrm.normalize()
    ns = 2 * nc + 1

    def pt(s, v, off):
        e = A.lerp(B, s)
        t = D.lerp(C, s)
        return e.lerp(t, v) + nrm * off
    for c in range(nv):
        v0, v1 = c / nv, (c + 1) / nv
        key = keys[(c * 7 + len(m.name)) % len(keys)] if c % 3 else keys[0]
        rows = []
        for v, lip in ((v0, b), (v1, 0.0)):
            row = []
            for k in range(ns):
                s = k / (ns - 1)
                roll = a if k % 2 else 0.0
                row.append(tuple(pt(s, v, lip + roll)))
            rows.append(row)
        polys = []
        for k in range(ns - 1):
            sc = (k + 0.5) / (ns - 1)
            if keep is not None and not keep(pt(sc, (v0 + v1) / 2, 0)):
                continue
            q = [rows[0][k], rows[0][k + 1], rows[1][k + 1], rows[1][k]]
            polys.append(q)
            # the lip: from this course's lower edge down to the slab
            e0 = tuple(pt(k / (ns - 1), v0, 0.0))
            e1 = tuple(pt((k + 1) / (ns - 1), v0, 0.0))
            polys.append([e0, e1, rows[0][k + 1], rows[0][k]])
        fix_up(m, key, polys, nrm)


def fix_up(m, key, polys, nrm):
    """Adds faces wound so they face along nrm or toward the eave."""
    out = []
    for q in polys:
        qa = [Vector(x) for x in q]
        fn = (qa[1] - qa[0]).cross(qa[2] - qa[1])
        if fn.length < 1e-9:
            fn = (qa[2] - qa[0]).cross(qa[3] - qa[0])
        if fn.dot(nrm) < -1e-6:
            q = q[::-1]
        out.append(q)
    m.faces(key, out)


def passage(m, pts, p, gate):
    """A doorway through the plinth: a stone panel lying flush on the
    battered face, a wooden arch on the panel, both leaning back with the
    batter, and a tunnel cut level through the wall. gate = (t, inner
    width, spring height, frame width, panel height, side) with side -1
    for the right face."""
    t, iw, spring, fw, ph, fs = gate
    pos, d, _ = kit.point_at(pts, t)
    nrm = Vector((-d.y, d.x)) * fs
    ri, ro = iw / 2, iw / 2 + fw
    foot, W, zb = p["foot"], p["W"], p["zb"]
    lean = (W + 0.04 - foot) / zb
    out = Vector((nrm.x * zb, nrm.y * zb, foot - W - 0.04)).normalized()

    def at(a, z, off=0.0):
        q = pos + d * a + nrm * (foot + lean * z)
        return tuple(Vector((q.x, q.y, z)) + out * off)
    half = ro + 0.3
    m.plate("panel", [at(-half, 0.0), at(half, 0.0), at(half, ph), at(-half, ph)], tuple(out * 0.04))
    seg = 10

    def arc(r):
        return [(math.cos(math.pi * i / seg) * r, spring + math.sin(math.pi * i / seg) * r) for i in range(seg + 1)]
    # the tunnel, level through the panel and the wall
    tun = [(ri - 0.02, -0.2)] + arc(ri - 0.02) + [(-ri + 0.02, -0.2)]
    deep = foot + 0.6
    face = []
    for a, z in tun:
        q = pos + d * a - nrm * deep
        face.append((q.x, q.y, z))
    m.cut_prism(face, tuple(nrm * (2 * deep)) + (0.0,))
    # the wooden frame: a horseshoe on the panel
    horse = [(ro, 0.0)] + arc(ro) + [(-ro, 0.0), (-ri, 0.0)] + arc(ri)[::-1] + [(ri, 0.0)]
    m.plate("arch", [at(a, z, 0.04) for a, z in horse], tuple(out * 0.08))


def frames(pts):
    """frame(t): the point t along the path and the vector that offsets a
    point across it, mitred where the path turns."""
    P, M = kit.mitres(pts)
    acc = [0.0]
    for i in range(len(P) - 1):
        acc.append(acc[-1] + (P[i + 1] - P[i]).length)

    def frame(t):
        for i, a in enumerate(acc):
            if abs(t - a) < 1e-6:
                return P[i], M[i]
        pos, d, _ = kit.point_at(pts, t)
        return pos, Vector((-d.y, d.x))
    return frame, acc


def hexa(m, key, frame, ta, tb, u0, u1, z0, za, zb_):
    """A length of band from ta to tb along the path, u0 to u1 across it,
    from z0 up to a top at za (at ta) and zb_ (at tb)."""
    (pa, ma), (pb, mb) = frame(ta), frame(tb)

    def q(p, mm, u, z):
        c = p + mm * u
        return (c.x, c.y, z)
    A0, A1 = q(pa, ma, u0, z0), q(pa, ma, u1, z0)
    B0, B1 = q(pb, mb, u0, z0), q(pb, mb, u1, z0)
    A2, A3 = q(pa, ma, u1, za), q(pa, ma, u0, za)
    B2, B3 = q(pb, mb, u1, zb_), q(pb, mb, u0, zb_)
    m.solid(key, [[A0, A1, B1, B0], [A3, B3, B2, A2], [A0, A3, A2, A1], [B0, B1, B2, B3],
                  [A0, B0, B3, A3], [A1, A2, B2, B1]])


def ruin(m, pts, high, hfn, step=0.4, cap="soot", pale=0.55, **over):
    """A broken wall along pts: the plinth in blocks where it stands, the
    upper bands in short lengths whose tops run from one ragged station
    height to the next, capped in cap (soot, or pale broken stone), the
    plaster's torn edges pale at a share pale of stations. No roof stays
    up: slab() lays torn pieces of it on the top. Returns the profile and
    top(t), the height the broken wall stands at t."""
    p = dict(DEF)
    p.update(over)
    W, foot, zb, zt = p["W"], p["foot"], p["zb"], p["zt"]
    L = kit.path_len(pts)
    zl = zb + p["ledge"]
    zcap = roof_z(p, high, 0.0) - p["rt"]
    hw = lambda z: foot + (W + 0.04 - foot) * z / zb  # noqa: E731
    rng = m.rng
    frame, corners = frames(pts)
    # stations: a steady step, and every corner of the path
    ts = sorted(set([round(i * step, 6) for i in range(int(L / step) + 1)] + [round(c, 6) for c in corners]
                    + [round(L, 6)]))
    hs = [max(0.0, min(zcap, hfn(t) + rng.uniform(-0.35, 0.3))) for t in ts]

    def top(t):
        for i in range(len(ts) - 1):
            if ts[i] <= t <= ts[i + 1]:
                u = (t - ts[i]) / max(1e-9, ts[i + 1] - ts[i])
                return hs[i] * (1 - u) + hs[i + 1] * u
        return hs[0] if t < ts[0] else hs[-1]
    # the plinth: blocks where the wall still stands
    n = p["courses"]
    for c in range(n):
        z0, z1 = zb * c / n, zb * (c + 1) / n
        ch = min(0.07, (z1 - z0) * 0.2)
        a, b, bt = hw(z0), hw(z1 - ch), hw(z1)
        sec = [(-a, z0), (a, z0), (b, z1 - ch), (bt - ch - 0.02, z1), (-bt + ch + 0.02, z1), (-b, z1 - ch)]
        t = 0.0 if c % 2 == 0 else -p["block"] * 0.5
        while t < L:
            ln = p["block"] * rng.uniform(0.7, 1.3)
            ta, tb = max(0.0, t), min(L, t + ln)
            t += ln
            if tb - ta < 0.08 or z1 > min(top(ta), top(tb), top((ta + tb) / 2)) + 0.05:
                continue
            sub = kit.sub_path(pts, ta + (0.03 if ta > 0 else 0), tb - (0.03 if tb < L else 0))
            d = rng.uniform(-0.03, 0.04)
            m.sweep(rng.choice(("stone", "stone", "stone2", "stone3")), sub,
                    [(u + (d if u > 0 else -d), z) for u, z in sec])
    # the bands, station to station: a battered core, the string course,
    # the timber and the plaster, each up to the ragged top
    bands = [("mortar", 0.0, zb, None), ("ledge", zb - 0.02, zl, W + 0.1), ("timber", zl, zt, W + 0.03),
             ("plaster", zt, zcap, W)]
    for i in range(len(ts) - 1):
        ta, tb = ts[i], ts[i + 1]
        if tb - ta < 1e-4:
            continue
        ha, hb = hs[i], hs[i + 1]
        for key, lo, hi, half in bands:
            if max(ha, hb) <= lo + 0.05:
                break
            za, zb_ = max(lo + 0.02, min(hi, ha)), max(lo + 0.02, min(hi, hb))
            if half is None:
                # the core inside the blocks, battered like them
                zz = min(za, zb_) - 0.05
                if zz > 0.1:
                    m.sweep("mortar", kit.sub_path(pts, ta, tb),
                            [(-hw(0) + 0.05, 0), (hw(0) - 0.05, 0), (hw(zz) - 0.05, zz), (-hw(zz) + 0.05, zz)])
                continue
            hexa(m, key, frame, ta, tb, -half, half, lo, za, zb_)
            broken = min(ha, hb) < hi
            if broken:
                # the broken top: a skin of soot, the plaster's torn edges pale
                hexa(m, cap, frame, ta, tb, -half + 0.01, half - 0.01, min(za, zb_) - 0.04, za + 0.03, zb_ + 0.03)
                if key == "plaster":
                    for s0, s1 in ((-half - 0.01, -half + 0.12), (half - 0.12, half + 0.01)):
                        if rng.random() < pale:
                            hexa(m, "pale", frame, ta, tb, s0, s1, min(za, zb_) - 0.1, za + 0.04, zb_ + 0.04)
            if key == "timber" and min(za, zb_) > zl + 0.5:
                pos, d, _ = kit.point_at(pts, (ta + tb) / 2)
                nrm = Vector((-d.y, d.x))
                for side in (-1, 1):
                    c = pos + nrm * side * (W + 0.05)
                    hp = min(za, zb_) - zl
                    m.obox("post", c.x, c.y, zl + hp / 2, 0.16, 0.08, hp, yaw=math.atan2(d.y, d.x))
            if broken:
                break
    # loopholes where the plaster still stands high enough round them
    zc = zt + 0.8
    if p["loops"]:
        k = max(1, int(L / p["loops"]))
        for i in range(k):
            t = L * (i + 0.5) / k
            if min(top(t - 0.3), top(t + 0.3)) < zc + 0.35:
                continue
            pos, d, _ = kit.point_at(pts, t)
            yaw = math.atan2(d.y, d.x)
            for side in (-1, 1):
                nrm = Vector((-d.y, d.x)) * side
                c = pos + nrm * (W + 0.03)
                m.obox("sill", c.x, c.y, zc - 0.2, 0.44, 0.08, 0.08, yaw=yaw)
                m.obox("plaster", c.x, c.y, zc + 0.02, 0.44, 0.06, 0.36, yaw=yaw)
                c2 = pos + nrm * (W + 0.05)
                m.obox("hole", c2.x, c2.y, zc, 0.22, 0.06, 0.28, yaw=yaw)
    return p, top


def sheet(m, p, poly, rt=0.12):
    """A torn flat sheet of tiled roof: poly is its outline, 3D points in
    one plane, the first edge its eave. A slab with the tiles on top."""
    P = [Vector(v) for v in poly]
    e = P[1] - P[0]
    N = Vector((0, 0, 0))
    for i in range(1, len(P) - 1):
        N += (P[i] - P[0]).cross(P[i + 1] - P[0])
    N.normalize()
    if N.z < 0:
        N = -N
    S = N.cross(e).normalized()
    if sum((v - P[0]).dot(S) for v in P) < 0:
        S = -S
    el = e.length
    loc = [((v - P[0]).dot(e) / el, (v - P[0]).dot(S)) for v in P]
    m.plate("tile_edge", [tuple(v - N * rt) for v in P], tuple(N * rt))
    # a skin of tile colour cut to the torn outline, so the tear reads
    # jagged even where the tile rolls stop a little short of it
    m.plate("tile2", [tuple(v) for v in P], tuple(N * 0.025))
    s0, s1 = min(a for a, _ in loc), max(a for a, _ in loc)
    qm = max(b for _, b in loc)
    ee = e / el
    A, B = P[0] + ee * s0, P[0] + ee * s1

    def keep(pt):
        v = pt - P[0]
        return inside(loc, v.dot(ee), v.dot(S))
    field(m, A, B, B + S * qm, A + S * qm, p["col"], p["course"], keep=keep)


def clip_band(poly, q0, q1):
    """The part of a polygon of (s, q) points with q0 <= q <= q1."""
    def cut(pl, q, keep_above):
        out = []
        n = len(pl)
        for i in range(n):
            a, b = pl[i], pl[(i + 1) % n]
            ia = (a[1] >= q) if keep_above else (a[1] <= q)
            ib = (b[1] >= q) if keep_above else (b[1] <= q)
            if ia:
                out.append(a)
            if ia != ib:
                f = (q - a[1]) / (b[1] - a[1])
                out.append((a[0] + (b[0] - a[0]) * f, q))
        return out
    return cut(cut(poly, q0, True), q1, False)


def inside(poly, s, q):
    c = False
    n = len(poly)
    for i in range(n):
        (x0, y0), (x1, y1) = poly[i], poly[(i + 1) % n]
        if (y0 > q) != (y1 > q) and s < x0 + (q - y0) * (x1 - x0) / (y1 - y0):
            c = not c
    return c


def torn(rng, apex=0.5, width=1.0, jag=0.12, n=5):
    """A torn piece of roof as (s, q) points: the full hinge edge along
    q = 0, ragged sides narrowing to a broken tip at q = 1."""
    left, right = [], []
    for k in range(1, n):
        f = k / n
        w = width * (1 - f) * 0.5 + 0.06
        sl = apex - w * (apex / 0.5) + rng.uniform(-jag, jag) * (1 - f * 0.5)
        sr = apex + w * ((1 - apex) / 0.5) + rng.uniform(-jag, jag) * (1 - f * 0.5)
        left.append((max(0.0, min(apex - 0.03, sl)), f + rng.uniform(-0.04, 0.04)))
        right.append((min(1.0, max(apex + 0.03, sr)), f + rng.uniform(-0.04, 0.04)))
    tip = [(apex + rng.uniform(-0.06, 0.02), 1.0), (apex + rng.uniform(0.02, 0.08), 0.94)]
    return [(0.0, 0.0), (1.0, 0.0)] + right + tip[::-1] + left[::-1]


def slab(m, p, A, B, up, segs, outline, rt=0.12):
    """A torn sheet of tiled roof whose hinge edge A-B rests on the broken
    wall top. It leaves the hinge toward the horizontal direction up and
    is bent along its length: segs is [(pitch in degrees, length), ...].
    outline is its shape as (s, q) points, s along the hinge 0..1 and q
    along the sheet 0..1 of its whole length."""
    A, B = Vector(A), Vector(B)
    e = B - A
    up = Vector((up[0], up[1], 0.0)).normalized()
    total = sum(ln for _, ln in segs)
    q0 = 0.0
    O = A.copy()
    for pitch, ln in segs:
        a = math.radians(pitch)
        S = up * math.cos(a) * ln + Vector((0, 0, math.sin(a) * ln))
        q1 = q0 + ln / total
        part = clip_band(outline, q0, q1)
        if len(part) >= 3:
            nrm = e.cross(S).normalized()
            if nrm.z < 0:
                nrm = -nrm

            def at(sq, O=O, S=S, qa=q0, qb=q1):
                return O + e * sq[0] + S * ((sq[1] - qa) / (qb - qa))
            face = [at(x) for x in part]
            m.plate("tile_edge", [tuple(f - nrm * rt) for f in face], tuple(nrm * rt))

            def keep(pt, O=O, S=S, qa=q0, qb=q1):
                v = pt - O
                s_ = v.dot(e) / e.length_squared
                qq = qa + (qb - qa) * v.dot(S) / S.length_squared
                return inside(outline, s_, qq)
            field(m, O, O + e, O + e + S, O + S, p["col"], p["course"], keep=keep)
        O = O + S
        q0 = q1
