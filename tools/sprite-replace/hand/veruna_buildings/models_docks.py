"""Veruna docks and their sacked ruins: plank decks on piles over the water,
a shell dome on a ring, nets, floats, rope coils and a moored rowboat.

The anchor is the water surface; the deck stands zd over it on piles that
go on down into the water.
"""
import math
import random

import bmesh
from mathutils import Vector

from kit import model
from models_flat import basket


ZD = 1.5  # deck height over the water


def dock_colours(m):
    # the deck stands high on thin piles: lower and sturdier
    m.sturdy(0.9)
    m.col("deck_a", (132, 124, 114))
    m.col("deck_b", (114, 108, 100))
    m.col("deck_c", (150, 142, 128))
    m.col("pile", (62, 60, 52))
    m.col("joist", (80, 74, 62))
    m.col("dome", (178, 166, 138))
    m.col("ring", (150, 140, 112))
    m.col("rope", (170, 126, 60))
    m.col("coil", (104, 106, 104))
    m.col("net", (120, 112, 100))
    m.col("hull", (104, 80, 56))
    m.col("hull_in", (80, 62, 44))
    m.col("thwart", (130, 104, 72))
    m.col("f_red", (170, 70, 50))
    m.col("f_blue", (70, 90, 120))
    m.col("f_white", (168, 164, 150))
    m.col("fish", (190, 192, 184))
    m.col("float", (200, 150, 60))
    m.col("basket", (150, 120, 72))
    m.col("rim", (116, 90, 56))
    return ("deck_a", "deck_b", "deck_c")


def deck(m, x0, x1, y0, y1, zd=ZD, along="x", keep=None, posts=(), pile_bottom=-1.6, tones=None, t=0.1, ragged=0.1):
    """A plank deck over the rectangle, boards along x or y, on joists and
    on piles at the given (x, y) or (x, y, top) points. A holed deck (keep)
    is laid in short lengths, each kept or lost alone."""
    tones = tones or ("deck_a", "deck_b", "deck_c")
    L = (x1 - x0) if along == "x" else (y1 - y0)
    n = max(1, int(L / 1.1)) if keep is not None else 1
    for i in range(n):
        a, b = i / n, (i + 1) / n
        if along == "x":
            xa, xb = x0 + (x1 - x0) * a, x0 + (x1 - x0) * b
            q = ((xa, y0, zd), (xa, y1, zd), (xb, y1, zd), (xb, y0, zd))
        else:
            ya, yb = y0 + (y1 - y0) * a, y0 + (y1 - y0) * b
            q = ((x1, ya, zd), (x0, ya, zd), (x0, yb, zd), (x1, yb, zd))
        m.planks(tones[0], *q, w=0.3, t=t, ragged=ragged, keep=keep, keys=tones)
    span = (x1 - x0) if along == "x" else (y1 - y0)
    nj = max(2, int(span / 1.3) + 1)
    for i in range(nj):
        if along == "x":
            x = x0 + 0.15 + (x1 - x0 - 0.3) * i / (nj - 1)
            if keep is None or keep(x, (y0 + y1) / 2, zd):
                m.box("joist", x - 0.08, x + 0.08, y0 + 0.05, y1 - 0.05, zd - t - 0.18, zd - t)
        else:
            y = y0 + 0.15 + (y1 - y0 - 0.3) * i / (nj - 1)
            if keep is None or keep((x0 + x1) / 2, y, zd):
                m.box("joist", x0 + 0.05, x1 - 0.05, y - 0.08, y + 0.08, zd - t - 0.18, zd - t)
    for p in posts:
        x, y = p[0], p[1]
        top = p[2] if len(p) > 2 else zd - t
        m.post("pile", x, y, pile_bottom, top, 0.3)


def torus(m, key, x, y, z, R, r, seg=16, mseg=6):
    bm = m.bm(key)
    rings = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        ring = []
        for j in range(mseg):
            b = 2 * math.pi * j / mseg
            rr = R + r * math.cos(b)
            ring.append(bm.verts.new((x + rr * math.cos(a), y + rr * math.sin(a), z + r + r * math.sin(b))))
        rings.append(ring)
    fs = []
    for i in range(seg):
        A, B = rings[i], rings[(i + 1) % seg]
        for j in range(mseg):
            k = (j + 1) % mseg
            f = bm.faces.new((A[j], B[j], B[k], A[k]))
            f.smooth = True
            fs.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=fs)


def float_ball(m, x, y, z0, r=0.5):
    """A striped round float or pot: dark foot, red band, pale crown."""
    prof = [(r * 0.5, 0.0), (r * 0.87, r * 0.5), (r, r)]
    m.lathe("f_blue", x, y, [(rr, z0 + zz) for rr, zz in prof], seg=14)
    m.lathe("f_red", x, y, [(r, z0 + r), (r * 0.87, z0 + r * 1.5)], seg=14)
    m.lathe("f_white", x, y, [(r * 0.87, z0 + r * 1.5), (r * 0.5, z0 + r * 1.87), (0.02, z0 + r * 2.0)], seg=14)


def net_pile(m, x, y, z0, r=0.6, seed=0):
    rng = random.Random("%s:n%d" % (m.name, seed))
    m.lathe("net", x, y, [(r, z0), (r * 0.85, z0 + 0.18), (r * 0.5, z0 + 0.3), (0.05, z0 + 0.34)], seg=10)
    for _ in range(6):
        a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0.1, r * 0.8)
        m.cyl("float", x + d * math.cos(a), y + d * math.sin(a), z0 + 0.3 - d * 0.35, 0.08, 0.06, seg=6)


def boat(m, cx, cy, yaw, L=3.6, W=1.45, H=0.45, z0=-0.1, keys=("hull", "hull_in", "thwart"), thwarts=3):
    """A rowboat: a hollow hull pointed at both ends, with thwarts."""
    hull, inner, thwart = keys
    bm = m.bm(hull)
    bi = m.bm(inner)
    n, k = 12, 6
    outer, inn = [], []
    for i in range(n + 1):
        u = -L / 2 + L * i / n
        e = abs(2 * u / L)
        hw = W / 2 * max(0.0, 1 - e ** 2.2) ** 0.5 + 0.02
        zk = z0 + 0.3 * H * e ** 2
        zg = z0 + H + 0.12 * e ** 2
        hwi = max(hw - 0.07, 0.01)
        zki = min(zk + 0.07, zg - 0.02)
        lo, li = [], []
        for j in range(k + 1):
            a = -math.pi / 2 + math.pi * j / k
            lo.append((u, hw * math.sin(a), zk + (zg - zk) * (1 - math.cos(a))))
            li.append((u, hwi * math.sin(a), zki + (zg - zki) * (1 - math.cos(a))))
        outer.append(lo)
        inn.append(li)
    c, s = math.cos(yaw), math.sin(yaw)

    def V(b, p):
        return b.verts.new((cx + c * p[0] - s * p[1], cy + s * p[0] + c * p[1], p[2]))
    O = [[V(bm, p) for p in lo] for lo in outer]
    Ii = [[V(bi, p) for p in li] for li in inn]
    fo, fi = [], []
    for i in range(n):
        for j in range(k):
            fo.append(bm.faces.new((O[i][j], O[i + 1][j], O[i + 1][j + 1], O[i][j + 1])))
            fi.append(bi.faces.new((Ii[i][j + 1], Ii[i + 1][j + 1], Ii[i + 1][j], Ii[i][j])))
    # the gunwales and the stem and stern close the shell
    for i in range(n):
        for j in (0, k):
            a, b = O[i][j].co, O[i + 1][j].co
            q = [a, b, Ii[i + 1][j].co, Ii[i][j].co]
            fo.append(bm.faces.new([bm.verts.new(p) for p in q]))
    for f in fo:
        f.smooth = True
    for f in fi:
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=fo)
    for f in fi:
        # the inside faces up into the boat
        if f.normal.z < 0:
            f.normal_flip()
    for t in range(thwarts):
        u = -L * 0.3 + L * 0.6 * t / max(1, thwarts - 1)
        e = abs(2 * u / L)
        hw = W / 2 * max(0.0, 1 - e ** 2.2) ** 0.5 - 0.05
        z = z0 + H - 0.1
        m.beam(thwart, (cx + c * u - s * -hw, cy + s * u + c * -hw, z), (cx + c * u - s * hw, cy + s * u + c * hw, z),
               0.22, 0.05)


def shell_dome(m, cx, cy, z, R=1.3, Rr=1.65, keep=None, ring_keep=None, low=None):
    """The shell dome on its flat ring; with keep or ring_keep, cut into
    cells so a sacked one can lose pieces. low colours its lower band."""
    if keep is None and ring_keep is None:
        m.lathe("ring", cx, cy, [(R - 0.05, z), (Rr, z), (Rr, z + 0.18), (R - 0.05, z + 0.18)], seg=32, smooth=False)
        prof = [(R * math.cos(math.pi / 2 * j / 7), z + 0.1 + R * math.sin(math.pi / 2 * j / 7)) for j in range(8)]
        if low:
            m.lathe(low, cx, cy, [(R, z + 0.05)] + prof[:3], seg=28, cap_top=False)
            m.lathe("dome", cx, cy, prof[2:-1] + [(0.01, z + 0.1 + R)], seg=28)
        else:
            m.lathe("dome", cx, cy, [(R, z + 0.05)] + prof[:-1] + [(0.01, z + 0.1 + R)], seg=28)
        return
    seg = 28
    for i in range(seg):
        a0, a1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
        am = (a0 + a1) / 2
        if ring_keep and not ring_keep(cx + (R + Rr) / 2 * math.cos(am), cy + (R + Rr) / 2 * math.sin(am), z):
            continue
        pts = [(cx + R * math.cos(a0), cy + R * math.sin(a0)), (cx + Rr * math.cos(a0), cy + Rr * math.sin(a0)),
               (cx + Rr * math.cos(a1), cy + Rr * math.sin(a1)), (cx + R * math.cos(a1), cy + R * math.sin(a1))]
        m.prism("ring", pts, z, z + 0.18)
    rings = 7
    for i in range(seg):
        for j in range(rings):
            b0, b1 = math.pi / 2 * j / rings, math.pi / 2 * (j + 1) / rings
            a0, a1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg

            def P(a, b, rr=R):
                return (cx + rr * math.cos(b) * math.cos(a), cy + rr * math.cos(b) * math.sin(a), z + 0.1 + rr * math.sin(b))
            mid = P((a0 + a1) / 2, (b0 + b1) / 2)
            if keep and not keep(*mid):
                continue
            bm = m.bm("dome")
            o = [bm.verts.new(P(a, b)) for a, b in ((a0, b0), (a1, b0), (a1, b1), (a0, b1))]
            ii = [bm.verts.new(P(a, b, R - 0.1)) for a, b in ((a0, b0), (a1, b0), (a1, b1), (a0, b1))]
            fs = [bm.faces.new(o), bm.faces.new(ii[::-1])]
            for q in range(4):
                r_ = (q + 1) % 4
                fs.append(bm.faces.new((o[q], ii[q], ii[r_], o[r_])))
            bmesh.ops.recalc_face_normals(bm, faces=fs)
            for f in fs[:1]:
                f.smooth = True


def fan(m, hx, hy, z, R, a0, a1, n=9, lift=0.5):
    """Poles spread in a fan from a hub, a net laid under them."""
    for i in range(n):
        a = a0 + (a1 - a0) * i / (n - 1)
        m.beam("joist", (hx, hy, z + 0.08), (hx + R * math.cos(a), hy + R * math.sin(a), z + lift), 0.08)
    pts = [(hx, hy, z + 0.06)] + [(hx + R * 0.95 * math.cos(a0 + (a1 - a0) * i / 6),
                                   hy + R * 0.95 * math.sin(a0 + (a1 - a0) * i / 6), z + lift * 0.9) for i in range(7)]
    for i in range(6):
        q = [pts[0], pts[i + 1], pts[i + 2]]
        m.slab("net", q if _ccw(q) else q[::-1], 0.03)


def _ccw(q):
    (ax, ay, _), (bx, by, _), (cx, cy, _) = q
    return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax) > 0


# ---- VerHut11 ------------------------------------------------------------

def d11_plan(m, dx=0, dy=0):
    F = m.frame(dx, dy)
    return dict(
        F=F,
        main=(F.X(2), F.X(115), F.Y(105, ZD), F.Y(2, ZD)),
        pier=(F.X(4), F.X(46), F.Y(150, ZD), F.Y(105, ZD)),
        dome=(F.X(58.5), F.Y(37.5, ZD)),
        boat=(F.X(88), F.Y(137, 0.1), math.radians(-22)),
    )


def d11_posts(P):
    x0, x1, y0, y1 = P["main"]
    px0, px1, py0, py1 = P["pier"]
    F = P["F"]
    posts = [(px0 + 0.12, py0 + 0.12), (px1 - 0.12, py0 + 0.12), (px0 + 0.12, y0 - 0.5), (px1 - 0.12, y0 - 0.5)]
    posts += [(F.X(83), y0 + 0.12), (x1 - 0.12, y0 + 0.12), (x0 + 0.12, y1 - 0.12), (x1 - 0.12, y1 - 0.12),
              (x0 + 0.12, (y0 + y1) / 2), (x1 - 0.12, (y0 + y1) / 2)]
    return posts


@model("VerHut11")
def verhut11(m):
    tones = dock_colours(m)
    P = d11_plan(m)
    F = P["F"]
    posts = d11_posts(P)
    deck(m, *P["main"], along="x", posts=posts, tones=tones)
    deck(m, *P["pier"], along="x", tones=tones)
    shell_dome(m, *P["dome"], ZD, R=1.4, Rr=1.68)
    fan(m, F.X(82), F.Y(52, ZD), ZD, 2.3, math.radians(15), math.radians(80))
    for r in (22, 48):
        torus(m, "rope", F.X(25), F.Y(r, ZD + 0.2), ZD, 0.4, 0.1)
    for c, r in ((15, 80), (30, 92), (15, 102)):
        float_ball(m, F.X(c), F.Y(r, ZD + 0.5), ZD, 0.5)
    # a low grating of slats, a net pile, fish on a board, a rope coil
    gx0, gx1, gy0, gy1 = F.X(62), F.X(100), F.Y(100, ZD + 0.3), F.Y(68, ZD + 0.3)
    for i in range(5):
        x = gx0 + (gx1 - gx0) * i / 4
        m.box("thwart", x - 0.05, x + 0.05, gy0, gy1, ZD, ZD + 0.3)
    for i in range(4):
        y = gy0 + (gy1 - gy0) * i / 3
        m.box("thwart", gx0, gx1, y - 0.05, y + 0.05, ZD + 0.3, ZD + 0.36)
    net_pile(m, F.X(105), F.Y(103, ZD + 0.2), ZD, 0.6)
    bx, by = F.X(25), F.Y(117, ZD + 0.1)
    m.box("thwart", bx - 0.8, bx + 0.8, by - 0.28, by + 0.28, ZD, ZD + 0.06)
    for i in range(6):
        m.obox("fish", bx - 0.6 + i * 0.24, by, ZD + 0.1, 0.1, 0.45, 0.06, 0.25)
    torus(m, "coil", F.X(27), F.Y(135, ZD + 0.2), ZD, 0.62, 0.14)
    bxx, byy, yaw = P["boat"]
    boat(m, bxx, byy, yaw)
    m.beam("rope", (F.X(50), F.Y(107, ZD), ZD), (bxx - 1.6 * math.cos(yaw), byy - 1.6 * math.sin(yaw), 0.4), 0.04)


# ---- VerHut12 ------------------------------------------------------------

def d12_plan(m, dx=0, dy=0):
    F = m.frame(dx, dy)
    return dict(
        F=F,
        main=(F.X(38), F.X(150), F.Y(115, ZD), F.Y(5, ZD)),
        side=(F.X(5), F.X(40), F.Y(45, ZD), F.Y(5, ZD)),
        dome=(F.X(116), F.Y(54, ZD)),
        boat=(F.X(19), F.Y(82, 0.1), math.radians(77)),
    )


def d12_posts(P):
    x0, x1, y0, y1 = P["main"]
    sx0, sx1, sy0, sy1 = P["side"]
    F = P["F"]
    posts = [(F.X(c), y0 + 0.12) for c in (36, 72, 112)] + [(x1 - 0.12, y0 + 0.12), (x1 - 0.12, y1 - 0.12),
                                                              (x0 + 0.12, y1 - 0.12)]
    posts += [(sx0 + 0.12, sy0 + 0.12), (sx0 + 0.12, sy1 - 0.12), (x0 + 0.12, (y0 + sy0) / 2)]
    return posts


@model("VerHut12")
def verhut12(m):
    tones = dock_colours(m)
    P = d12_plan(m)
    F = P["F"]
    deck(m, *P["main"], along="y", posts=d12_posts(P), tones=tones)
    deck(m, *P["side"], along="x", tones=tones)
    # the golden shell dome, darker round its lower side
    m.col("dome", (165, 140, 98))
    m.col("dome_low", (128, 106, 72))
    shell_dome(m, *P["dome"], ZD, R=1.35, Rr=1.64, low="dome_low")
    # mooring piles standing above the deck along the north edge
    x0, x1, y0, y1 = P["main"]
    for c in (40, 88, 116, 146):
        m.post("pile", F.X(c), y1 - 0.12, -1.6, ZD + 0.5, 0.24)
    # two spars crossed by the dome, orange nets bundled beside them
    m.beam("joist", (F.X(98), F.Y(4, ZD + 0.5), ZD + 0.55), (F.X(122), F.Y(28, ZD + 0.05), ZD + 0.05), 0.1)
    m.beam("joist", (F.X(121), F.Y(3, ZD + 0.5), ZD + 0.55), (F.X(100), F.Y(27, ZD + 0.05), ZD + 0.05), 0.1)
    m.col("onet", (176, 112, 48))
    for c, r in ((110, 24), (132, 28)):
        m.lathe("onet", F.X(c), F.Y(r, ZD + 0.2), [(0.42, ZD), (0.36, ZD + 0.15), (0.2, ZD + 0.26), (0.04, ZD + 0.3)],
                seg=9)
    for c, r in ((48, 12), (68, 90), (55, 103)):
        float_ball(m, F.X(c), F.Y(r, ZD + 0.5), ZD, 0.5)
    for c, r in ((130, 82), (143, 82), (140, 97)):
        basket(m, "basket", F.X(c), F.Y(r, ZD + 0.3), z0=ZD, r=0.42, rim="rim")
    for c, r, s in ((47, 97, 0), (105, 22, 1), (132, 28, 2)):
        net_pile(m, F.X(c), F.Y(r, ZD + 0.2), ZD, 0.55, seed=s)
    torus(m, "coil", F.X(20), F.Y(25, ZD + 0.2), ZD, 0.5, 0.12)
    bx, by, yaw = P["boat"]
    boat(m, bx, by, yaw)
    m.beam("rope", (F.X(40), F.Y(43, ZD), ZD), (bx + 1.6 * math.cos(yaw), by + 1.6 * math.sin(yaw), 0.4), 0.04)


# ---- the sacked docks ------------------------------------------------------

def drawn(m, frac=0.4):
    """keep() for deck still drawn above the water: not the green-tinted
    wreckage that lies under it."""
    return m.keep_class(frac=frac, pred=lambda c: max(c) > 0.1 and not (c[1] > c[0] * 1.08 and c[1] > c[2] * 0.98))


def burnt_dome(m, cx, cy, z, R, Rr, frac=1.0, gap=0.0, seed=0, pieces=12):
    """What is left of a shell dome after the fire: its flat ring, frac of
    the circle with the gap centred on angle gap, and inside it a dark crater
    of char and broken shell pieces, a few curved shards still at the rim."""
    import random
    rng = random.Random("%s:d%d" % (m.name, seed))
    seg = 32
    for i in range(seg):
        a0, a1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
        am = (a0 + a1) / 2
        d = math.atan2(math.sin(am - gap), math.cos(am - gap))
        if abs(d) < math.pi * (1 - frac):
            continue
        pts = [(cx + R * math.cos(a0), cy + R * math.sin(a0)), (cx + Rr * math.cos(a0), cy + Rr * math.sin(a0)),
               (cx + Rr * math.cos(a1), cy + Rr * math.sin(a1)), (cx + R * math.cos(a1), cy + R * math.sin(a1))]
        m.prism("ring", pts, z, z + 0.18)
    # the crater: a dark dished heap inside the ring
    m.lathe("crater", cx, cy, [(R - 0.02, z + 0.02), (R * 0.7, z + 0.16), (R * 0.35, z + 0.08), (0.02, z + 0.03)],
            seg=16, smooth=False)
    for k in range(pieces):
        a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0.1, R * 0.8)
        m.shard(rng.choice(("dome", "dome_dark")), cx + d * math.cos(a), cy + d * math.sin(a),
                z + 0.15 + rng.uniform(0, 0.15), rng.uniform(0.35, 0.7), rng.uniform(0.3, 0.55), rng.uniform(0, 3),
                rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6), t=0.06, rag=0.35, seed=seed * 50 + k)
    for k in range(5):
        a = gap + math.pi + rng.uniform(-2.2, 2.2)
        m.beam(rng.choice(("pile", "joist")), (cx + R * 0.9 * math.cos(a), cy + R * 0.9 * math.sin(a), z + 0.2),
               (cx + R * 0.1 * math.cos(a + 2.5), cy + R * 0.1 * math.sin(a + 2.5), z + 0.1), 0.1)
    # curved shards of the shell still standing on the ring
    for k in range(4):
        a = gap + math.pi + rng.uniform(-2.0, 2.0)
        m.shard("dome", cx + (R - 0.1) * math.cos(a), cy + (R - 0.1) * math.sin(a), z + 0.45, 0.7, 0.5,
                a + math.pi / 2, 0.0, 1.1, t=0.06, rag=0.3, seed=seed * 50 + 30 + k)


def charred_deck(m, tone):
    """Deck boards burnt toward tone, some charred black."""
    r, g, b = tone
    m.col("deck_a", (r, g, b))
    m.col("deck_b", (r * 0.82, g * 0.82, b * 0.82))
    m.col("deck_c", (r * 1.15, g * 1.15, b * 1.15))
    m.col("burnt", (30, 29, 26))
    m.col("ring", (130, 120, 98))
    m.col("dome", (150, 136, 108))
    m.col("dome_dark", (100, 90, 72))
    m.col("crater", (36, 33, 29))
    return ("deck_a", "deck_b", "deck_c", "burnt", "burnt")


def sunk_colours(m):
    m.col("hull", (40, 52, 46))
    m.col("hull_in", (30, 40, 36))
    m.col("thwart", (54, 66, 58))
    m.col("wet_a", (64, 76, 66))
    m.col("wet_b", (78, 92, 82))
    m.col("wet_c", (54, 64, 56))


def flotsam(m, spots, rng, keys=("wet_a", "wet_b", "wet_c")):
    """Planks and wreckage floating in the water: (c0, r0, c1, r1, w) each,
    read off the drawing at the water line."""
    for c0, r0, c1, r1, w in spots:
        m.beam(rng.choice(keys), (m.X(c0), m.Y(r0), 0.02), (m.X(c1), m.Y(r1), 0.04), w, 0.08)


@model("VerHut11a")
def verhut11a(m):
    import random
    dock_colours(m)
    keys = m.ruin_palette()
    tones = charred_deck(m, (63, 59, 50))
    P = d11_plan(m, 1, 16)
    F = P["F"]
    keep = drawn(m)
    posts = d11_posts(P)
    # the piles stand, some snapped short
    rng = random.Random(m.name)
    posts = [(x, y, ZD - 0.1 - (rng.uniform(0.3, 1.2) if rng.random() < 0.35 else 0)) for x, y in posts]
    deck(m, *P["main"], along="x", posts=posts, keep=keep, tones=tones, ragged=0.35)
    deck(m, *P["pier"], along="x", keep=keep, tones=tones, ragged=0.35)
    # the whole ring still lies where it was, the smashed shell inside it
    burnt_dome(m, m.X(62), m.Y(46, ZD), ZD, 1.4, 1.68, frac=1.0, seed=1)
    torus(m, "rope", F.X(25), F.Y(40, ZD + 0.2), ZD, 0.4, 0.1)
    net_pile(m, F.X(105), F.Y(103, ZD + 0.2), ZD, 0.6)
    # what is left of the fish rack: a few slats of its grating
    gx0, gx1, gy0, gy1 = F.X(62), F.X(100), F.Y(100, ZD + 0.3), F.Y(68, ZD + 0.3)
    for i in (0, 2, 3):
        x = gx0 + (gx1 - gx0) * i / 4
        m.box("thwart", x - 0.05, x + 0.05, gy0, gy1 - rng.uniform(0.3, 1.0), ZD, ZD + 0.3 - 0.1 * i)
    m.beam("thwart", (gx0, gy0 + 0.6, ZD + 0.3), (gx1 - 0.5, gy0 + 0.9, ZD + 0.1), 0.08)
    # the boat went down by the pier, its wreckage and planks afloat to the south-west
    sunk_colours(m)
    boat(m, m.X(90), m.Y(187, -0.3), math.radians(-8), z0=-0.55)
    flotsam(m, [(6, 150, 30, 144, 0.28), (8, 156, 34, 150, 0.26), (12, 162, 40, 152, 0.3), (20, 146, 42, 158, 0.24),
                (52, 150, 72, 156, 0.26), (55, 146, 70, 160, 0.22), (15, 182, 40, 186, 0.26), (20, 188, 44, 192, 0.22),
                (18, 125, 26, 150, 0.14), (32, 160, 44, 176, 0.14)], rng)
    m.scatter(keys, max_area=220, lift=0.0, skip=[(0, 135, 80, 200)])


@model("VerHut12a")
def verhut12a(m):
    import random
    dock_colours(m)
    keys = m.ruin_palette()
    tones = charred_deck(m, (70, 77, 68))
    P = d12_plan(m, 52, 0)
    F = P["F"]
    x0, x1, y0, y1 = P["main"]
    xm = F.X(73)
    rng = random.Random(m.name)
    # the east end still stands on its piles, charred
    posts = [(F.X(c), y0 + 0.12, ZD - 0.1) for c in (112,)] + [(x1 - 0.12, y0 + 0.12), (x1 - 0.12, y1 - 0.12)]
    deck(m, xm, x1, y0, y1, along="y", posts=posts, keep=drawn(m), tones=tones, ragged=0.35)
    # three quarters of the ring round a dark crater of shell and char
    burnt_dome(m, *P["dome"], ZD, 1.35, 1.64, frac=0.75, gap=math.radians(200), seed=2)
    # the west end broke off and hangs down into the water
    sunk_colours(m)
    wet = ("wet_a", "wet_b", "wet_c")
    xs = F.X(10)
    m.planks("wet_a", (xs, y0 + 0.6, -0.25), (xm, y0 + 0.6, ZD - 0.2), (xm, y1 - 0.8, ZD - 0.2),
             (xs, y1 - 0.8, -0.25), w=0.3, t=0.1, ragged=0.4,
             keep=m.keep_class(frac=0.3, pred=lambda c: max(c) > 0.2), keys=wet)
    for c in (36, 72):
        m.obox("pile", F.X(c) + rng.uniform(-0.2, 0.2), y0 + 0.4, -0.3, 0.26, 0.26, 2.6, 0, rng.uniform(-0.25, 0.25))
    boat(m, m.X(22), m.Y(95, -0.3), math.radians(28), z0=-0.55)
    m.scatter(keys, max_area=220)
