"""Aramon timber halls under slab and shingle roofs: the stone-roofed
tower house, the pyramid-roofed halls and their ruins; the smithy, the
long hall and the manor build on the helpers here."""
import math
import random

import numpy as np

import kit
from kit import Mat, px
from models import P, model


def hall_mats(n, roof="#5a5040", wall="#3a342a", timber="#1e1a16", moss="#4a5226", moss_amt=0.2, rows=8, cols=6,
              seed=41, uv=2.4, var=0.09):
    return {"roof": Mat(n + "_roof", tex=kit.tex_tiles(n + "_roof", roof, rows=rows, cols=cols, moss=moss,
                                                       moss_amt=moss_amt, seed=seed, var=var), uv=uv),
            "wall": Mat(n + "_hwall", tex=kit.tex_mottle(n + "_hwall", wall, var=0.12, seed=seed + 1), uv=1.5),
            "timber": Mat(n + "_timber", tex=kit.tex_planks(n + "_timber", timber, boards=2, seed=seed + 2), uv=1.0),
            "dark": Mat(n + "_hdark", "#120e0a"),
            "stone": Mat(n + "_hstone", tex=kit.tex_stone(n + "_hstone", "#5a5448", rows=7, seed=seed + 3), uv=1.5),
            "cap": Mat(n + "_rcap", tex=kit.tex_planks(n + "_rcap", kit.shade(roof, 0.7), boards=2, seed=seed + 4),
                       uv=1.0)}


def tiled_body(B, m, p, ends=None, frame=None, gable_frame=True, cap=True):
    """Walls, a timber frame and a slab or shingle roof for body p (the
    keys fit.py fits); ends (west, east) as kit.roof takes them."""
    L, W, H, R = p["L"], p["W"], p["H"], p["R"]
    kind = "gable" if p["hip"] < 0 else "hip"
    ends = ends or (kind, kind)
    fr = dict(t=0.2, bays=1.3, braces=True, rail=None)
    fr.update(frame or {})
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        B.box(m["wall"], 0, 0, 0, L, W, H)
        # the frame stops under the roof shell so no timber breaks through it
        kit.framed_walls(B, None, m["timber"], L, W, H - p["T"] * 0.6, **fr)
        for e, t in zip((-1, 1), ends):
            if t == "hip":
                continue
            x = e * (L / 2 - 0.1)
            kit.gable_wall(B, m["wall"], x, W, H, R - 0.1, t=0.2, top=R * 0.5 - 0.05 if t == "half" else None)
            if gable_frame:
                xo = e * (L / 2 + 0.03)
                top = H + (R * 0.5 if t == "half" else R) - 0.45
                B.beam(m["timber"], (xo, 0, H - 0.1), (xo, 0, top), 0.2)
                B.beam(m["timber"], (xo, -W / 2 + 0.45, H - 0.1), (xo, 0, top - 0.1), 0.16)
                B.beam(m["timber"], (xo, W / 2 - 0.45, H - 0.1), (xo, 0, top - 0.1), 0.16)
        zr = kit.roof(B, m["roof"], L, W, H, R, kind=kind, over=p["over"], over_end=p["over_end"], T=p["T"],
                      hip=max(0.0, p["hip"]), ends=ends)
        if cap:
            rl = [L / 2 + p["over_end"] if t == "gable" else max(0.02, L / 2 - max(0.0, p["hip"])) for t in ends]
            kit.ridge_cap(B, m["cap"], -rl[0], rl[1], zr, w=0.3)
    return zr


# ---------------------------------------------------------------- the tower house

# a little wider and lower than the pixel fit, so it stands square in 3D
HUT03 = P(cx=-0.3, cy=-0.62, yaw=90.0, L=4.8, W=4.3, H=2.05, R=1.55, over=0.3, over_end=0.38, hip=-1, T=0.3)


@model("AraHut03")
def arahut03(B, r):
    n = r["name"]
    m = hall_mats(n, roof="#56503e", wall="#2e2820", timber="#1e1a16", moss="#4e5a2a", moss_amt=0.3, rows=8,
                  cols=6, uv=2.4)
    p = HUT03
    # the ridge runs toward the camera: the front gable is local west
    tiled_body(B, m, p, frame=dict(xbrace=True, bays=1.3))
    zr = p["H"] + p["R"]
    # a stout stone stack at the back of the ridge with a round pot, col 38
    bx, by = p["cx"], p["cy"] + p["L"] / 2 - 0.3
    B.box(m["stone"], bx, by, zr - 0.8, 0.9, 0.9, 1.35)
    B.cyl(m["stone"], bx, by, zr + 0.55, 0.42, 0.4, seg=10)
    B.cyl(m["dark"], bx, by, zr + 0.94, 0.3, 0.02, seg=10)
    # a lower lean-to against the east wall, cols 76-92
    x0, x1, y0, y1 = p["cx"] + p["W"] / 2, 3.05, -3.05, -0.85
    with B.at((x0 + x1) / 2, (y0 + y1) / 2):
        w, d = x1 - x0, y1 - y0
        B.box(m["wall"], 0, 0, 0, w, d, 1.25)
        for ey in (-1, 1):
            B.beam(m["timber"], (w / 2, ey * (d / 2 - 0.05), 0), (w / 2, ey * (d / 2 - 0.05), 1.25), 0.2)
        B.beam(m["timber"], (-w / 2, -d / 2 - 0.03, 0.1), (w / 2, -d / 2 - 0.03, 1.15), 0.15)
        B.slab(m["roof"], [(-w / 2 - 0.1, -d / 2 - 0.2, 1.9), (w / 2 + 0.25, -d / 2 - 0.2, 1.15),
                           (w / 2 + 0.25, d / 2 + 0.15, 1.15), (-w / 2 - 0.1, d / 2 + 0.15, 1.9)], 0.2)


# ---------------------------------------------------------------- ruins of halls

def wreck_mats(n, base="#2c2620", light="#6b6358", beam="#42311f", plank="#4a3c2c", slab="#5a5040", seed=51,
               slab_tex=None):
    m = {"heap": Mat(n + "_wheap", tex=kit.tex_rubble(n + "_wheap", base, light=light, seed=seed), uv=4.0),
         "beam": Mat(n + "_wbeam", tex=kit.tex_planks(n + "_wbeam", beam, boards=2, seed=seed + 1), uv=1.0),
         "plank": Mat(n + "_wplank", tex=kit.tex_planks(n + "_wplank", plank, boards=3, seed=seed + 2), uv=1.0),
         "stone": Mat(n + "_wstone", tex=kit.tex_mottle(n + "_wstone", "#5e5648", var=0.2, seed=seed + 3), uv=0.8),
         "straw": Mat(n + "_wstraw", "#6a5a30"),
         "dark": Mat(n + "_wdark", "#120e0a")}
    m["tile"] = slab_tex or Mat(n + "_wslab", tex=kit.tex_tiles(n + "_wslab", slab, rows=3, cols=3, seed=seed + 4),
                                uv=0.9)
    return m


def broken_frame(B, timber, L, W, H, keep="", seed=0, bays=1.3, t=0.2, xbrace="", lo=0.3, sides="NSEW"):
    """The timber skeleton of a storey left standing: posts at each bay,
    snapped at random heights except on the sides in keep, which stand
    whole with their plate and braces (X braces on the sides in xbrace)."""
    g = random.Random(seed)
    for side in sides:
        n = L if side in "NS" else W
        if side in "NS":
            y = (W / 2) * (1 if side == "N" else -1)

            def pt(s, z):
                return (s, y, z)
        else:
            x = (L / 2) * (1 if side == "E" else -1)

            def pt(s, z):
                return (x, s, z)
        k = max(1, int(round(n / bays)))
        xs = [-n / 2 + t / 2 + i * (n - t) / k for i in range(k + 1)]
        whole = side in keep
        hs = [H if whole else H * g.uniform(lo, 1.0) for _ in xs]
        for s, h in zip(xs, hs):
            B.beam(timber, pt(s, 0), pt(s, h), t)
        B.beam(timber, pt(-n / 2, 0), pt(n / 2, 0), t)
        if whole:
            B.beam(timber, pt(-n / 2, H - t), pt(n / 2, H - t), t)
            for i in range(k):
                if side in xbrace:
                    B.beam(timber, pt(xs[i], t), pt(xs[i + 1], H - t), t * 0.8)
                    B.beam(timber, pt(xs[i + 1], t), pt(xs[i], H - t), t * 0.8)
                elif i % 2 == 0:
                    B.beam(timber, pt(xs[i + 1], t), pt(xs[i], H - t), t * 0.8)


@model("AraHut03a")
def arahut03a(B, r):
    n = r["name"]
    m = wreck_mats(n, base="#36302a", light="#8a7e6c", beam="#4a3220", slab="#6a6050")
    t = Mat(n + "_timber", tex=kit.tex_planks(n + "_timber", "#241c16", boards=2, seed=3), uv=1.0)
    p = HUT03
    # the storey's frame stands, the front whole with its X braces
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        broken_frame(B, t, p["L"], p["W"], 1.8, keep="W", xbrace="W", seed=2, t=0.22)
    with B.at(2.4, -1.95):
        broken_frame(B, t, 1.3, 2.2, 1.15, keep="S", seed=3, bays=1.1, t=0.22)
    # the slab roof and its timbers fell in and heaped up inside
    h1 = kit.heap(B, m["heap"], r, [(4, 12), (30, 5), (60, 6), (80, 10), (94, 30), (96, 55), (91, 78), (80, 86),
                                    (20, 86), (6, 72), (2, 44)], 2.0, z_at=1.3, cell=0.35, noise=0.35, seed=1,
                  edge=0.7)
    h2 = kit.heap(B, m["heap"], r, [(20, 20), (60, 14), (78, 30), (76, 60), (40, 66), (18, 52)], 2.6, z_at=2.4,
                  cell=0.35, noise=0.35, seed=2, edge=0.8)
    kit.debris(B, m, r, [(4, 14), (60, 6), (94, 30), (92, 78), (10, 80)], 120, z_at=1.8, size=(0.8, 2.6), seed=4,
               tilt=35, kinds={"beam": 6, "plank": 2, "tile": 4, "stone": 2}, ground=kit.ground_of(h1, h2))
    # one rafter still leans up out of the heap at the back, col 72
    kit.leaning(B, m["beam"], r, [(68, 30, 1.8, 74, 4, 3.6)], w=0.24)


# ---------------------------------------------------------------- the pyramid-roofed halls

def hall_geom(q):
    """The pyramid roof of a jettied hall as a RoofGeom."""
    return kit.RoofGeom(q["cx"], q["cy"], q["L"], q["W"], q["H"], q["R"], q["over"], kind="hip", T=0.3)


def jettied_hall(B, m, cx, cy, L, W, H, R, over=0.5, jetty=0.4, H1=2.0, T=0.3, finial=True, caps=True, t=0.2,
                 floor=True):
    """Two timber storeys, the upper jutting jetty past the lower on every
    side, under a pyramid roof R high with a stone vent at its apex and
    dark caps down its four hips."""
    L0, W0 = L - 2 * jetty, W - 2 * jetty
    with B.at(cx, cy):
        # ground storey, X braced; the frame is laid 0.01 out from each wall
        # face and the wall's ends are sunk, so no timber face lies in one
        B.box(m["wall"], 0, 0, -0.02, L0, W0, H1 - 0.03)
        kit.framed_walls(B, None, m["timber"], L0 + 0.02, W0 + 0.02, H1, t=t, bays=1.3, xbrace=True)
        # jetty joists and the upper storey, its frame on its own walls
        B.box(m["timber"], 0, 0, H1 - 0.15, L - 0.05, W - 0.05, 0.22)
        zu = H1 + 0.07
        B.box(m["wall"], 0, 0, zu - 0.02, L, W, H - zu + 0.02)
        with B.at(0, 0, zu):
            kit.framed_walls(B, None, m["timber"], L + 0.02, W + 0.02, H - zu - T * 0.6, t=t, bays=1.3, sill=False)
        zr = kit.roof(B, m["roof"], L, W, H, R, kind="hip", over=over, over_end=over, T=T, hip=L / 2)
        if finial:
            B.box(m["vent"], 0, 0, zr - 0.5, 0.9, 0.9, 1.0)
            kit.roof(B, m["vent"], 1.05, 1.05, zr + 0.5, 0.5, kind="pyramid", over=0.0, T=0.1)
    if caps:
        kit.hip_caps(B, m["cap"], hall_geom(dict(cx=cx, cy=cy, L=L, W=W, H=H, R=R, over=over)), w=0.26,
                     lift=0.02, ridge=False)
    return zr


def roof_patch(B, mat, r, g, col, row, w=0.5, d=0.35, lift=0.02):
    """A dark gap where slabs slipped: a thin dark sheet lying lift above
    roof g where the sprite draws it, bent to the slopes under it."""
    h = g.hit(r, col, row)
    if h is None:
        return
    u0, v0 = g.local(h[0], h[1])
    # its long side runs along the courses of the plane it lies on
    ax = (1.0, 0.0) if g.side(v0) <= g.end(u0) else (0.0, 1.0)
    k = 3
    V = []
    for j in range(k):
        for i in range(k):
            a, b = (i / (k - 1) - 0.5) * w, (j / (k - 1) - 0.5) * d
            u, v = u0 + a * ax[0] - b * ax[1], v0 + a * ax[1] + b * ax[0]
            u, v = max(-g.hx, min(g.hx, u)), max(-g.hy, min(g.hy, v))
            V.append(g.world(u, v, g.top_local(u, v) + lift))
    F = [(j * k + i, j * k + i + 1, (j + 1) * k + i + 1, (j + 1) * k + i) for j in range(k - 1) for i in range(k - 1)]
    (x0, y0, _), (x1, y1, _), (x2, y2, _) = V[0], V[1], V[k]
    if (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0) < 0:
        F = [tuple(reversed(f)) for f in F]
    B.mesh(mat, V, F)


def hall_mats2(n, seed):
    m = hall_mats(n, roof="#4c463a", wall="#302a24", timber="#141210", moss="#4a5426", moss_amt=0.35, rows=7,
                  cols=5, uv=2.8, seed=seed)
    m["cap"] = Mat(n + "_rcap", tex=kit.tex_planks(n + "_rcap", "#2a2620", boards=2, seed=seed + 4), uv=1.0)
    m["vent"] = Mat(n + "_vent", tex=kit.tex_stone(n + "_vent", "#5e6660", rows=5, seed=seed + 7), uv=1.2)
    m["board"] = Mat(n + "_hboard", tex=kit.tex_planks(n + "_hboard", "#8a8274", boards=6, var=0.15, seed=seed + 8,
                                                       vertical=True), uv=1.6)
    m["door"] = Mat(n + "_door", tex=kit.tex_planks(n + "_door", "#2a1a12", boards=4, seed=seed + 9, vertical=True),
                    uv=1.0)
    return m


def charred_mats(n, seed, roof):
    """A jettied hall after the fire: its own sooty roof map, blackened
    walls and timbers, and charred stuff for the holes' floors."""
    m = hall_mats2(n, seed)
    m["roof"] = roof
    m["wall"] = Mat(n + "_hwall", tex=kit.tex_mottle(n + "_hwall", "#26221c", var=0.15, seed=seed + 1), uv=1.5)
    m["timber"] = Mat(n + "_timber", tex=kit.tex_planks(n + "_timber", "#120e0c", boards=2, seed=seed + 2), uv=1.0)
    m["cap"] = Mat(n + "_rcap", tex=kit.tex_planks(n + "_rcap", "#1e1a16", boards=2, seed=seed + 4), uv=1.0)
    m["char"] = Mat(n + "_char", tex=kit.tex_grit(n + "_char", "#1a1511", light="#5a524a", dark="#070504", uv=2.0,
                                                   chips=0.05, darks=0.3, seed=seed + 11), uv=2.0)
    return m


# walls a touch lower than the pixel fit, the timbers heavier
B02 = dict(cx=-0.06, cy=-0.22, L=6.0, W=5.9, H=3.85, R=1.6, over=0.5, jetty=0.35, H1=1.95)
B03 = dict(cx=0.1, cy=0.8, L=6.3, W=6.5, H=3.6, R=2.4, over=0.5, jetty=0.35, H1=1.85)


def hall(B, m, q, **kw):
    return jettied_hall(B, m, q["cx"], q["cy"], q["L"], q["W"], q["H"], q["R"], over=q["over"], jetty=q["jetty"],
                        H1=q["H1"], **kw)


def front_y(q):
    """The ground storey's front face."""
    return q["cy"] - q["W"] / 2 + q["jetty"]


@model("Arabuild02")
def arabuild02(B, r):
    n = r["name"]
    m = hall_mats2(n, 81)
    q = B02
    hall(B, m, q)
    # a plank door in the middle of the front, cols 45-65
    B.box(m["door"], px(r, 55, 0)[0], front_y(q) - 0.04, 0, 1.1, 0.08, 1.6)
    for c, rw in ((16, 56), (90, 54), (74, 98)):
        roof_patch(B, m["dark"], r, hall_geom(q), c, rw)


@model("Arabuild03")
def arabuild03(B, r):
    n = r["name"]
    m = hall_mats2(n, 91)
    q = B03
    hall(B, m, q)
    for c, rw in ((28, 36), (60, 20), (82, 40)):
        roof_patch(B, m["dark"], r, hall_geom(q), c, rw)
    shed(B, m, r, q)


def shed(B, m, r, q):
    """The low shed of pale boards against Arabuild03's front, cols 55-100."""
    x0, x1 = px(r, 55, 0)[0], px(r, 100, 0)[0]
    y1 = front_y(q)
    y0 = -4.55
    with B.at((x0 + x1) / 2, (y0 + y1) / 2):
        w, d = x1 - x0, y1 - y0
        B.box(m["board"], 0, 0, -0.02, w - 0.2, d, 1.17)
        B.slab(m["board"], [(-w / 2, -d / 2 - 0.1, 1.2), (w / 2, -d / 2 - 0.1, 1.2), (w / 2, d / 2, 1.55),
                            (-w / 2, d / 2, 1.55)], 0.08)
        for e in (-1, 1):
            B.box(m["timber"], e * (w / 2 - 0.19), -d / 2 + 0.05, 0, 0.2, 0.2, 1.2)


# ---------------------------------------------------------------- damaged roofs

RoofGeom = kit.RoofGeom


def clipped_beam(B, mat, r, polys, a, b, w, n=24):
    """A beam from a to b (world points) kept only where the sprite of r
    draws it inside one of the outlines."""
    from mathutils import Vector
    a, b = Vector(a), Vector(b)
    P = [[tuple(q) for q in poly] for poly in polys]
    ins = []
    for i in range(n + 1):
        p = a + (b - a) * (i / n)
        c, rw = kit.screen(r, p.x, p.y, p.z)
        ins.append(any(kit.inside_poly(q, c, rw) for q in P))
    i = 0
    while i <= n:
        if ins[i]:
            j = i
            while j + 1 <= n and ins[j + 1]:
                j += 1
            if j > i:
                B.beam(mat, tuple(a + (b - a) * (i / n)), tuple(a + (b - a) * (j / n)), w)
            i = j + 1
        else:
            i += 1


def roof_frame(B, mat, r, g, polys, spacing=0.55, lath=0.45, w=0.14, lath_w=0.08):
    """Rafters, hip rafters and battens under roof g, where the slabs have
    gone: only the parts the sprite shows inside the hole outlines. Each
    end of g is hipped or gabled as g.ends says."""
    d = g.T + w / 2
    W_ = g.world
    hips = [i for i in (0, 1) if g.ends[i] != "gable"]
    # rafters on the long slopes, up to the ridge or the hips
    nx = int(2 * g.hx / spacing)
    for i in range(nx + 1):
        x = -g.hx + 2 * g.hx * i / nx
        stop = max(0.0, g.hy - (g.end(x) - g.ze) / g.k) if g.end(x) < 1e8 else 0.0
        for e in (-1, 1):
            if stop >= g.hy - 0.05:
                continue
            clipped_beam(B, mat, r, polys, W_(x, e * g.hy, g.ze - d), W_(x, e * stop, g.side(stop) - d), w)
    ny = int(2 * g.hy / spacing)
    for i in hips:
        ex = -1 if i == 0 else 1
        rx, ke = g.rxs[i], g.kes[i]
        for j in range(ny + 1):
            y = -g.hy + 2 * g.hy * j / ny
            stop = max(rx, g.hx - (g.side(y) - g.ze) / ke)
            if stop >= g.hx - 0.05:
                continue
            clipped_beam(B, mat, r, polys, W_(ex * g.hx, y, g.ze - d), W_(ex * stop, y, g.end(ex * stop) - d), w)
        for ey in (-1, 1):
            clipped_beam(B, mat, r, polys, W_(ex * g.hx, ey * g.hy, g.ze - d), W_(ex * rx, 0, g.zr - d), w * 1.3)
    # battens across the rafters
    nb = int(g.hy / lath)
    for j in range(1, nb):
        y = g.hy - j * lath
        z = g.side(y) - g.T + lath_w / 2
        xs = [g.hx if g.ends[i] == "gable" else g.hx - (g.hy - y) * g.k / g.kes[i] for i in (0, 1)]
        if min(xs) <= 0.05:
            continue
        for e in (-1, 1):
            clipped_beam(B, mat, r, polys, W_(-xs[0], e * y, z), W_(xs[1], e * y, z), lath_w)
    for i in hips:
        ex = -1 if i == 0 else 1
        rx, ke = g.rxs[i], g.kes[i]
        for k in range(1, int(g.hx / lath)):
            x = g.hx - k * lath
            if x <= rx:
                break
            z = g.end(ex * x) - g.T + lath_w / 2
            ye = g.hy - (g.hx - x) * ke / g.k
            if ye <= 0.05:
                continue
            clipped_beam(B, mat, r, polys, W_(ex * x, -ye, z), W_(ex * x, ye, z), lath_w)


def cut_roofs(obs, r, polys, key="_roof"):
    """Cut the roof objects (and their caps) along the classic camera's
    line of sight through outlines traced on the sprite."""
    roofs = [o for o in obs if key in o.name or o.name.endswith("_rcap")]
    kit.cut_view(roofs, [list(p) for p in polys], r)
    return obs


def attic_rubble(B, r, m, polys, H, top=0.6, n=25, seed=0):
    """Rubble on the attic floor seen through holes in the roof (the old
    way; the damaged roofs now use kit.hole_insides, which never rises
    through the roof)."""
    with B.at(0, 0, H):
        for i, poly in enumerate(polys):
            h = kit.heap(B, m["heap"], r, poly, top, z_at=H + top * 0.5, cell=0.42, noise=0.2, seed=seed + i,
                         edge=0.6)
            kit.debris(B, m, r, poly, n, z_at=H + top * 0.5, size=(0.4, 1.6), seed=seed + 10 + i, tilt=35,
                       kinds={"beam": 3, "plank": 1, "tile": 4, "stone": 1}, ground=h)


def rafter_mat(n, col="#4a3e32", seed=0):
    return Mat(n + "_rafter", tex=kit.tex_planks(n + "_rafter", col, boards=2, seed=seed), uv=1.0)


@model("Arabuild02a")
def arabuild02a(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    q = B02
    g = hall_geom(q)
    # the black hole just below the vent, the bare rafters to its right and
    # a small hole at the lower left
    hole = [(36, 60), (50, 55), (62, 58), (70, 68), (68, 86), (58, 98), (40, 100), (30, 90), (30, 72)]
    strip = [(76, 20), (98, 16), (108, 28), (108, 76), (96, 84), (82, 76), (76, 52)]
    small = [(8, 94), (26, 92), (28, 106), (10, 108)]
    roof = kit.sooty_roof(n + "_roof", g, r, "#4a4436", holes=[hole, small, strip], moss="#3a4220", moss_amt=0.22,
                          dark=1.0, course=0.34, tile_w=0.9, soot=0.35, halo=0.9)
    m = charred_mats(n, 81, roof)
    hall(B, m, q)
    B.box(m["dark"], px(r, 55, 0)[0], front_y(q) - 0.04, 0, 1.2, 0.08, 1.6)
    obs = B.objects()
    jagged = [kit.jag(hole, 2.0, 3.0, 1), kit.jag(strip, 1.5, 3.0, 2), kit.jag(small, 1.5, 3.0, 3)]
    cut_roofs(obs, r, jagged)
    dm = kit.debris_mats(n, pic, seed=3, gain=0.9)
    rafter = rafter_mat(n, "#3a3026", 84)
    # the battens and rafters left bare where the slabs slid off, and two
    # charred rafters still across the big hole
    roof_frame(B, rafter, r, g, [strip], spacing=0.5, lath=0.42)
    roof_frame(B, rafter, r, g, [hole], spacing=1.5, lath=20.0, w=0.18)
    kit.hole_insides(B, r, pic, m["char"], dm, g, [jagged[0], jagged[2]], sag=1.0, n=10, seed=20)
    kit.hole_insides(B, r, pic, m["char"], dm, g, [jagged[1]], sag=0.5, n=0, seed=30)
    # a few slabs slid off onto the ground in front
    flat = lambda x, y: 0.0  # noqa: E731
    kit.cover(B, dm, r, kit.plan_of(r, [(4, 128), (108, 128), (108, 140), (4, 140)], 0.0), flat, 8,
              {"slab": 3, "beam": 1}, pic=pic, size=(0.4, 0.8), length=(0.8, 1.4), seed=7, tilt=15, grow=1,
              tries=300)
    return obs


# ---------------------------------------------------------------- fallen halls

def surface_block(B, mat, x0, x1, y0, y1, zfn, cell=0.5, z0=0.0, seed=0, jitter=0.25):
    """A solid over the plan rectangle x0..x1, y0..y1 whose top is
    zfn(x, y) and whose sides drop straight to z0: a fallen roof or floor
    lying on what is left of its walls."""
    rng = random.Random(seed)
    nx, ny = max(2, int((x1 - x0) / cell)), max(2, int((y1 - y0) / cell))
    V, top = [], {}
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = x0 + (x1 - x0) * i / nx
            y = y0 + (y1 - y0) * j / ny
            if 0 < i < nx and 0 < j < ny:
                x += (rng.random() - 0.5) * cell * jitter
                y += (rng.random() - 0.5) * cell * jitter
            top[i, j] = len(V)
            V.append((x, y, zfn(x, y)))
    F = [(top[i, j], top[i + 1, j], top[i + 1, j + 1], top[i, j + 1]) for j in range(ny) for i in range(nx)]
    rim = [(i, 0) for i in range(nx)] + [(nx, j) for j in range(ny)] + [(i, ny) for i in range(nx, 0, -1)] + \
          [(0, j) for j in range(ny, 0, -1)]
    base = []
    for k in rim:
        x, y, _ = V[top[k]]
        base.append(len(V))
        V.append((x, y, z0))
    for a in range(len(rim)):
        b = (a + 1) % len(rim)
        F.append((top[rim[a]], top[rim[b]], base[b], base[a]))
    F.append(tuple(reversed(base)))
    B.solid(mat, V, F)


def open_storey(B, timber, dark, L, W, H, sides="NSEW", t=0.22, bays=1.3, xbrace=True, inset=0.35):
    """A ground storey whose infill burnt out: its timber frame standing
    open before a shadowed inside."""
    if dark is not None:
        B.box(dark, 0, 0, 0, L - 2 * inset, W - 2 * inset, H)
    kit.framed_walls(B, None, timber, L, W, H, t=t, bays=bays, xbrace=xbrace, sides=sides)


def lifted(h, dz):
    """A heap's surface raised by dz, -1e9 off the heap."""
    def f(x, y):
        s = h(x, y)
        return s + dz if s > 0 else -1e9
    return f


@model("Arabuild02b")
def arabuild02b(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    q = dict(B02, cx=B02["cx"] + 0.2)
    L, W, H1 = q["L"], q["W"], q["H1"]
    L0, W0 = L - 2 * q["jetty"], W - 2 * q["jetty"]
    timber = Mat(n + "_otimber", tex=kit.tex_planks(n + "_otimber", "#5a4a36", boards=2, var=0.25, seed=5), uv=1.0,
                 ref="#5a4a36")
    dark = Mat(n + "_inside", "#16120e")
    dm = kit.debris_mats(n, pic, seed=6, k=7, gain=1.25, sat=1.1)
    with B.at(q["cx"], q["cy"]):
        # the ground storey stands as an open frame, the front and west
        # whole, the back and east snapped short
        open_storey(B, timber, dark, L0, W0, H1, sides="SW")
        broken_frame(B, timber, L0, W0, H1, seed=4, t=0.22, lo=0.5, sides="NE")
        # the upper floor still spans it on its jetty beams
        B.box(timber, 0, 0, H1 - 0.12, L - 0.1, W - 0.1, 0.24)
        # the upper storey's west frame stands with its corner posts at the
        # back, the tallest things left
        with B.at(0, 0, H1 + 0.12):
            kit.framed_walls(B, None, timber, L - 0.2, W - 0.2, 1.7, t=0.22, bays=1.5, sides="W", xbrace=True)
        for x, y, h in ((-L / 2 + 0.2, W / 2 - 0.2, 4.2), (L / 2 - 0.5, W / 2 - 0.2, 3.7), (-L / 2 + 1.6, W / 2 - 0.2,
                                                                                            3.1)):
            B.beam(timber, (x, y, H1), (x, y, h), 0.26)
        B.beam(timber, (-L / 2 + 0.2, W / 2 - 0.2, 3.6), (-L / 2 + 1.6, W / 2 - 0.2, 3.0), 0.2)
    # the roof came down onto the upper floor: slabs and timbers heaped on it
    poly = [(10, 8), (96, 10), (100, 30), (98, 96), (60, 100), (12, 98), (8, 50)]
    zf = H1 + 0.12
    with B.at(0, 0, zf):
        h = kit.heap(B, Mat(n + "_bed", tex=kit.tex_grit(n + "_bed", "#2a241c", light="#6a6050", dark="#0e0c0a",
                                                         uv=1.5, chips=0.06, darks=0.25, seed=8), uv=1.5),
                     r, kit.jag(poly, 1.5, 4.0, 3), 0.9, z_at=zf + 0.5, cell=0.45, noise=0.08, seed=9, edge=1.4)
    ground = lifted(h, zf)
    P = kit.plan_of(r, poly, zf + 0.5)
    kit.cover(B, dm, r, P, ground, 150, {"slab": 5, "beam": 3, "board": 1}, pic=pic, size=(0.6, 1.2),
              length=(1.4, 3.0), width=(0.14, 0.22), seed=10, tilt=25, grow=1, jumble=0.25, stick=0.08, q=0.75)
    # the stone vent lies tilted in the middle of the heap, col 55 rows 60-84
    x, y = px(r, 55, 72, zf + 0.8)
    m = hall_mats2(n, 85)
    with B.at(x, y, ground(x, y) - 0.2 if ground(x, y) > 0 else zf + 0.5, yaw=20, pitch=55):
        B.box(m["vent"], 0, 0, 0, 0.9, 0.9, 1.0)
        kit.roof(B, m["vent"], 1.05, 1.05, 1.0, 0.5, kind="pyramid", over=0.0, T=0.1)


@model("Arabuild03a")
def arabuild03a(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    q = B03
    g = hall_geom(q)
    # the lower front plane fell in along a ragged line, rafters broken down
    # into it in Vs; the top left corner slumped
    band = [(16, 116), (24, 98), (40, 84), (58, 68), (74, 74), (90, 88), (104, 102), (108, 116), (90, 118),
            (74, 108), (58, 114), (42, 106), (30, 118)]
    corner = [(-4, -4), (40, -4), (34, 12), (22, 22), (8, 34), (-4, 40)]
    roof = kit.sooty_roof(n + "_roof", g, r, "#4a4436", holes=[band], moss="#3a4220", moss_amt=0.2, dark=1.0,
                          course=0.34, tile_w=0.9, soot=0.35, halo=0.9)
    m = charred_mats(n, 91, roof)
    hall(B, m, q)
    obs = B.objects()
    jb, jc = kit.jag(band, 2.0, 3.0, 5), kit.jag(corner, 2.0, 3.0, 4)
    cut_roofs(obs, r, [jb, jc])
    dm = kit.debris_mats(n, pic, seed=4, gain=0.95)
    fill, Pp = kit.sag_fill(B, m["char"], r, g, jb, sag=1.1, cell=0.3, noise=0.05, seed=5, edge=0.9)
    kit.cover(B, dm, r, Pp, fill, 24, {"beam": 3, "board": 2, "slab": 3, "stone": 1}, pic=pic, size=(0.4, 0.8),
              length=(0.8, 1.8), seed=6, lift=0.04, tilt=20, grow=0, jumble=0.15)
    # broken rafters from the collapse's upper edge down into it, in Vs
    rafter = rafter_mat(n, "#3a3026", 94)
    for top, ends in (((58, 72), ((42, 108), (74, 106))), ((88, 90), ((76, 112), (100, 112))),
                      ((30, 100), ((22, 114), (44, 112))), ((72, 80), ((62, 110), (84, 104)))):
        a = g.hit(r, *top)
        if a is None:
            continue
        a = (a[0], a[1], a[2] - g.T * 0.5)
        for c, rw in ends:
            for z in [x / 10 for x in range(45, 20, -1)]:
                x, y = px(r, c, rw, z)
                f = fill(x, y)
                if f > -50 and f >= z - 0.1:
                    B.beam(rafter, a, (x, y, f + 0.05), 0.18, 0.15, twist=10)
                    break
    # the slumped top left corner: the cut-off roof lying lower, tipped in
    inner = [g.hit(r, c, rw) for c, rw in ((38, 2), (32, 13), (21, 23), (8, 34), (2, 38))]
    inner = [p for p in inner if p is not None]
    if len(inner) >= 3:
        cx_, cy_, _ = g.world(-g.hx, g.hy, g.ze)
        pts = [(p[0], p[1], p[2] - 0.35) for p in inner] + [(cx_ + 0.3, cy_ - 0.3, g.ze - 1.3)]
        B.slab(roof, pts, 0.25)
    # the board shed came down in a dark pile, its door frame standing at
    # the left end with the door leant on it
    x0, x1 = px(r, 58, 0)[0], px(r, 104, 0)[0]
    shed_poly = [(58, 128), (104, 128), (106, 170), (62, 174)]
    flat = lambda x, y: 0.0  # noqa: E731
    sm = kit.debris_mats(n + "_sh", pic, seed=7, box=(60, 128, 106, 175), k=4, gain=1.0)
    hh = kit.heap(B, m["char"], r, shed_poly, 0.45, z_at=0.2, cell=0.4, noise=0.05, seed=8, edge=0.6, pic=pic)
    kit.cover(B, sm, r, kit.plan_of(r, shed_poly, 0.2), kit.ground_of(hh, flat), 30, {"board": 3, "beam": 2},
              pic=pic, length=(1.2, 2.4), seed=9, tilt=35, grow=1, jumble=0.3, stick=0.2)
    door = Mat(n + "_sdoor", tex=kit.tex_planks(n + "_sdoor", "#86704e", boards=4, seed=11, vertical=True), uv=1.0,
               ref="#86704e")
    fx = x0 - 0.1
    for y in (-4.3, -2.6):
        B.beam(m["timber"], (fx, y, 0), (fx, y, 1.9), 0.2)
    B.beam(m["timber"], (fx, -4.4, 1.9), (fx, -2.5, 1.9), 0.18)
    with B.at(fx + 0.25, -3.45, 0, yaw=90, pitch=-30):
        B.box(door, 0, 0, 0, 1.5, 0.1, 1.8)
    return obs


@model("Arabuild03b")
def arabuild03b(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    q = B03
    L, W = q["L"], q["W"]
    L0, W0 = L - 2 * q["jetty"], W - 2 * q["jetty"]
    timber = Mat(n + "_otimber", tex=kit.tex_planks(n + "_otimber", "#2a2218", boards=2, var=0.2, seed=5), uv=1.0)
    dm = kit.debris_mats(n, pic, seed=8, k=7, gain=1.1, sat=1.0)
    # the back frame still stands across the top: posts, a rail at the
    # jetty and a snapped plate
    with B.at(q["cx"], q["cy"]):
        yb = W0 / 2
        for x, h in ((-2.0, 3.3), (-1.0, 3.1), (2.4, 3.0), (2.9, 2.5)):
            B.beam(timber, (x, yb, 0), (x, yb, h), 0.24)
        B.beam(timber, (-L0 / 2, yb, q["H1"]), (L0 / 2, yb, q["H1"]), 0.24)
        B.beam(timber, (-2.2, yb, 2.9), (-0.8, yb, 3.0), 0.2)
        broken_frame(B, timber, L0, W0, 1.2, seed=13, t=0.22, lo=0.2, sides="WE")
    # a low sagged pile of the roof's slabs and the floors' beams over the
    # whole footprint and the shed in front of it
    poly = [(10, 24), (60, 20), (112, 24), (118, 70), (116, 118), (108, 156), (60, 158), (40, 140), (14, 112),
            (6, 60)]
    bed = Mat(n + "_bed", tex=kit.tex_grit(n + "_bed", "#3a3024", light="#6a6050", dark="#16120e", uv=1.5,
                                            chips=0.06, darks=0.25, seed=8), uv=1.5)
    h = kit.heap(B, bed, r, kit.jag(poly, 2.0, 4.0, 3), 1.3, z_at=0.6, cell=0.5, noise=0.08, seed=9, edge=1.6,
                 pic=pic)
    P = kit.plan_of(r, poly, 0.6)
    kit.cover(B, dm, r, P, h, 190, {"slab": 5, "beam": 2, "board": 1}, pic=pic, size=(0.7, 1.3), seed=10, tilt=25,
              grow=1, jumble=0.25, q=0.7)
    # long beams lying across it side to side, as the sprite draws them
    for c0, rw0, c1, rw1 in ((14, 90, 108, 86), (24, 108, 116, 110), (30, 60, 96, 56)):
        a, b = px(r, c0, rw0, 0.9), px(r, c1, rw1, 0.9)
        za, zb = max(0.1, h(*a)), max(0.1, h(*b))
        B.beam(timber, (a[0], a[1], za + 0.1), (b[0], b[1], zb + 0.1), 0.22)
    # the stone vent in the middle of the heap, col 60 row 62
    x, y = px(r, 60, 62, 1.2)
    m = hall_mats2(n, 95)
    with B.at(x, y, h(x, y) - 0.25, yaw=-15, pitch=30):
        B.box(m["vent"], 0, 0, 0, 0.9, 0.9, 1.0)
        kit.roof(B, m["vent"], 1.05, 1.05, 1.0, 0.5, kind="pyramid", over=0.0, T=0.1)


def roof_plates(B, mat, r, g, n, polys=None, seed=0, size=(0.6, 1.1), lift=0.12, spin=35):
    """Slabs slipped out of their courses, lying skewed on roof g's
    slopes; only where the sprite draws them inside polys, if given."""
    from mathutils import Vector
    rng = random.Random(seed)
    O = Vector(g.world(0, 0, 0))

    def vec(x, y, z):
        return Vector(g.world(x, y, z)) - O
    k = placed = 0
    while placed < n and k < n * 40:
        k += 1
        u, v = rng.uniform(-g.hx, g.hx), rng.uniform(-g.hy, g.hy)
        zs, ze_ = g.side(v), g.end(u)
        if zs <= ze_:
            z = zs
            up = Vector((0, -math.copysign(1, v), g.k)).normalized()
            along = Vector((1, 0, 0))
        else:
            z = ze_
            up = Vector((-math.copysign(1, u), 0, g.ke)).normalized()
            along = Vector((0, 1, 0))
        p = Vector(g.world(u, v, z))
        if polys is not None:
            c, rw = kit.screen(r, p.x, p.y, p.z)
            if not any(kit.inside_poly(q, c, rw) for q in polys):
                continue
        a = math.radians(rng.uniform(-spin, spin))
        t1 = vec(*(along * math.cos(a) + up * math.sin(a)))
        t2 = vec(*(up * math.cos(a) - along * math.sin(a)))
        nrm = t1.cross(t2).normalized()
        if nrm.z < 0:
            nrm = -nrm
        w, h = rng.uniform(*size), rng.uniform(size[0] * 0.8, size[1] * 0.8)
        lf = rng.uniform(0, lift)
        c0 = p + nrm * 0.05
        pts = [c0 - t1 * w / 2 - t2 * h / 2, c0 + t1 * w / 2 - t2 * h / 2 + nrm * lf,
               c0 + t1 * w / 2 + t2 * h / 2 + nrm * lf, c0 - t1 * w / 2 + t2 * h / 2]
        B.slab(mat, [tuple(q) for q in pts], 0.06)
        placed += 1


def cross_finial(B, m, x, y, z):
    """The iron cross on a post at the apex of the Arabuild01 hall."""
    B.box(m["vent"], x, y, z - 0.4, 0.34, 0.34, 1.0)
    for a in (45, -45):
        B.box(m["vent"], x, y, z + 0.4, 1.3, 0.24, 0.24, yaw=a)
        B.box(m["vent"], x, y, z + 0.4, 0.24, 0.24, 0.9, yaw=a, pitch=0, roll=a)


B01 = dict(cx=-1.25, cy=-0.3, L=6.4, W=5.7, H=4.45, R=2.0, over=0.5, jetty=0.35, H1=2.0)


def b01_mats(n, seed):
    m = hall_mats(n, roof="#6a5c46", wall="#262420", timber="#141210", moss="#4a5426", moss_amt=0.2, rows=7,
                  cols=5, uv=2.8, seed=seed)
    m["vent"] = Mat(n + "_vent", "#4e5c5c", rough=0.5, metal=0.3)
    m["plank"] = Mat(n + "_lplank", tex=kit.tex_planks(n + "_lplank", "#2e2a24", boards=6, seed=seed + 8), uv=1.5)
    m["slip"] = Mat(n + "_slip", tex=kit.tex_tiles(n + "_slip", "#3a3228", rows=2, cols=2, var=0.12, seed=seed + 9,
                                                   moss="#3a4220", moss_amt=0.2), uv=1.0)
    m["cap"] = Mat(n + "_rcap", tex=kit.tex_planks(n + "_rcap", "#1e1a16", boards=2, seed=seed + 4), uv=1.0)
    return m


def b01_leanto(B, m, broken=False):
    x0 = B01["cx"] + B01["L"] / 2 - 0.1
    x1, y0, y1 = 4.3, -2.4, 0.9
    with B.at((x0 + x1) / 2, (y0 + y1) / 2):
        w, d = x1 - x0, y1 - y0
        B.box(m["plank"], 0, 0, 0, w - 0.2, d, 2.0)
        if broken:
            B.slab(m["plank"], [(-w / 2, -d / 2 - 0.2, 2.8), (w / 2 + 0.2, -d / 2 - 0.2, 1.9),
                                (w / 2 + 0.2, 0.1, 1.9), (-w / 2, 0.3, 2.8)], 0.1)
            B.box(m["plank"], 0.3, d / 2 - 0.4, 1.9, 1.6, 0.3, 0.06, yaw=30, pitch=20)
        else:
            B.slab(m["plank"], [(-w / 2, -d / 2 - 0.2, 3.1), (w / 2 + 0.2, -d / 2 - 0.2, 2.0),
                                (w / 2 + 0.2, d / 2 + 0.2, 2.0), (-w / 2, d / 2 + 0.2, 3.1)], 0.1)


@model("Arabuild01a")
def arabuild01a(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    q = B01
    g = hall_geom(q)
    # the scorched hole up and to the right of the cross, a smaller hole on
    # the left slope showing its rafters
    scorch = [(60, 8), (80, 6), (90, 18), (86, 34), (72, 40), (62, 30)]
    low = [(16, 60), (36, 58), (42, 86), (20, 92)]
    roof = kit.sooty_roof(n + "_roof", g, r, "#5a5040", holes=[scorch, low], moss="#3e4624", moss_amt=0.15,
                          dark=0.92, course=0.36, tile_w=0.95, soot=0.45, halo=1.0)
    m = b01_mats(n, 191)
    m["roof"] = roof
    m["wall"] = Mat(n + "_hwall", tex=kit.tex_mottle(n + "_hwall", "#221e1a", var=0.15, seed=192), uv=1.5)
    m["char"] = Mat(n + "_char", tex=kit.tex_grit(n + "_char", "#16120e", light="#4a443c", dark="#060504", uv=2.0,
                                                   chips=0.05, darks=0.3, seed=195), uv=2.0)
    zr = hall(B, m, q, finial=False)
    cross_finial(B, m, q["cx"], q["cy"], zr)
    b01_leanto(B, m, broken=True)
    obs = B.objects()
    jagged = [kit.jag(scorch, 2.0, 4.0, 1), kit.jag(low, 2.0, 4.0, 2)]
    cut_roofs(obs, r, jagged)
    dm = kit.debris_mats(n, pic, seed=5, gain=0.95)
    roof_frame(B, rafter_mat(n, "#3a3026", 194), r, g, jagged, spacing=0.7, lath=0.8)
    kit.hole_insides(B, r, pic, m["char"], dm, g, jagged, sag=0.9, n=6, seed=12)
    # a handful of slabs slipped out of their courses, and a rafter lying
    # across the right slope, cols 72-116
    roof_plates(B, m["slip"], r, g, 12, seed=6, size=(0.6, 1.0), spin=25)
    a, b = g.hit(r, 72, 42), g.hit(r, 114, 56)
    if a and b:
        B.beam(m["timber"], (a[0], a[1], a[2] + 0.1), (b[0], b[1], b[2] + 0.1), 0.2)
    return obs


def stub_runs(rng, length, lo, hi, gap=(0.25, 0.7), run=(0.5, 1.3)):
    """Wall stubs along a side length long: [(t0, t1, [(f, h)])] with
    ragged gaps between them."""
    out, t = [], rng.uniform(0.0, 0.2)
    while t < length - 0.3:
        t1 = min(length, t + rng.uniform(*run))
        out.append((t, t1, [(0, rng.uniform(lo, hi)), (0.5, rng.uniform(lo, hi)), (1, rng.uniform(lo, hi))]))
        t = t1 + rng.uniform(*gap)
    return out


@model("Arabuild01b")
def arabuild01b(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    rng = random.Random(7)
    # the ruin's walls as its own sprite draws them, narrower than the hall
    X0, X1, Y0, Y1 = -3.3, 1.9, -2.4, 2.75
    # side: start, end, outward normal, stub heights, spill margin, how much
    # of a stub's height the spill at its foot comes up
    SIDES = {"S": ((X0, Y0), (X1, Y0), (0, -1), (0.4, 1.1), 0.62, 0.75),
             "W": ((X0, Y1), (X0, Y0), (-1, 0), (0.6, 1.3), 0.6, 0.75),
             "N": ((X1, Y1), (X0, Y1), (0, 1), (1.0, 1.6), 0.35, 0.4),
             "E": ((X1, Y0), (X1, Y1), (1, 0), (0.8, 1.6), 0.3, 0.4)}
    runs = {}
    for k, (a, b, _, hs, _, _) in SIDES.items():
        runs[k] = stub_runs(rng, math.hypot(b[0] - a[0], b[1] - a[1]), *hs)
    # the back plate still up on its posts over the west half of the back
    plate = (X0, -0.4, 2.45)
    GAP = 0.3

    def along(k, x, y):
        a, b = SIDES[k][0], SIDES[k][1]
        L_ = math.hypot(b[0] - a[0], b[1] - a[1])
        return ((x - a[0]) * (b[0] - a[0]) + (y - a[1]) * (b[1] - a[1])) / L_

    def rim(k, t):
        """How high the side stands at t along it: a stub's top, the gap's
        debris, or the plate at the back."""
        h = GAP
        for t0, t1, prof in runs[k]:
            if t0 - 0.15 <= t <= t1 + 0.15:
                f = min(1.0, max(0.0, (t - t0) / max(1e-3, t1 - t0)))
                h = max(h, float(np.interp(f, [q[0] for q in prof], [q[1] for q in prof])))
        if k == "N":
            x = X1 - t
            if plate[0] <= x <= plate[1]:
                h = max(h, plate[2])
        return h

    def in_run(k, t):
        return any(t0 <= t <= t1 for t0, t1, _ in runs[k])

    def jag_m(k, t):
        return SIDES[k][4] * (0.75 + 0.25 * math.sin(t * 2.3 + len(k)) + 0.15 * math.sin(t * 5.1 + 2 * len(k)))

    def surf(x, y):
        """The fallen roof: sagged toward the middle, up on the stubs and the
        plate at the edges, spilling over them down to the ground outside."""
        if X0 <= x <= X1 and Y0 <= y <= Y1:
            ds = {"S": y - Y0, "N": Y1 - y, "W": x - X0, "E": X1 - x}
            ws = {k: 1.0 / (d + 0.3) ** 2 for k, d in ds.items()}
            h = sum(ws[k] * rim(k, along(k, x, y)) for k in ds) / sum(ws.values())
            dm = min(ds.values())
            return max(0.4, h + 0.06 - 0.75 * min(1.0, dm / 1.9) ** 0.9)
        cx_, cy_ = min(X1, max(X0, x)), min(Y1, max(Y0, y))
        ox, oy = x - cx_, y - cy_
        if abs(oy) >= abs(ox):
            k = "S" if oy < 0 else "N"
        else:
            k = "W" if ox < 0 else "E"
        t = along(k, cx_, cy_)
        e = math.hypot(ox, oy)
        m_ = jag_m(k, t)
        if e >= m_:
            return -0.05
        kf = 1.0 if not in_run(k, t) else SIDES[k][5]
        return rim(k, t) * kf * (1 - e / m_) ** 1.3
    char = Mat(n + "_char", tex=kit.tex_grit(n + "_char", "#241e18", light="#5a5046", dark="#0a0806", uv=2.0,
                                              chips=0.05, darks=0.3, seed=3), uv=2.0)
    # the debris the roof came down on, one surface from spill to spill
    cell = 0.3
    gx0, gx1, gy0, gy1 = X0 - 0.8, X1 + 0.45, Y0 - 0.85, Y1 + 0.5
    nx, ny = int((gx1 - gx0) / cell), int((gy1 - gy0) / cell)
    V, F, idx = [], [], {}
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = gx0 + (gx1 - gx0) * i / nx + (rng.random() - 0.5) * cell * 0.3
            y = gy0 + (gy1 - gy0) * j / ny + (rng.random() - 0.5) * cell * 0.3
            idx[i, j] = len(V)
            V.append((x, y, surf(x, y)))
    for j in range(ny):
        for i in range(nx):
            q_ = [idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]]
            if all(V[k][2] <= 0 for k in q_):
                continue
            F += [(q_[0], q_[1], q_[2]), (q_[0], q_[2], q_[3])]
    B.mesh(char, V, F)
    # the broken wall stubs, charred boards, ragged and gapped
    stub = Mat(n + "_stub", tex=kit.tex_planks(n + "_stub", "#2a2018", boards=5, seed=13, vertical=True), uv=1.2)
    for k, (a, b, nrm, _, _, _) in SIDES.items():
        L_ = math.hypot(b[0] - a[0], b[1] - a[1])
        d = ((b[0] - a[0]) / L_, (b[1] - a[1]) / L_)
        for t0, t1, prof in runs[k]:
            p0 = (a[0] + d[0] * t0, a[1] + d[1] * t0)
            p1 = (a[0] + d[0] * t1, a[1] + d[1] * t1)
            kit.broken_wall(B, stub, char, p0, p1, 0.2, prof, step=0.2, jitter=0.12, seed=rng.randint(0, 999))
    # the cross lies tilted in the sag, where the sprite draws it
    fx, fy = px(r, 49, 58, 1.0)
    for _ in range(3):
        fx, fy = px(r, 49, 58, surf(fx, fy) + 0.2)
    # overlapping brown slabs radiating from the cross, over the spills too
    cols = [c for c in kit.palette(pic, k=7, seed=2, lit=0.3) if sum(c) < 400]
    slabs = kit.swatches(n + "_rs", cols, "mottle", seed=10, sat=1.2)
    Pr = [(X0 - 0.55, Y0 - 0.6), (X1 + 0.15, Y0 - 0.6), (X1 + 0.15, Y1 + 0.1), (X0 - 0.55, Y1 + 0.1)]
    kit.radial_slabs(B, slabs, r, pic, surf, (fx, fy), Pr, 185, size=(0.7, 1.2), seed=5)
    m = b01_mats(n, 201)
    with B.at(fx, fy, surf(fx, fy) + 0.05, yaw=25, pitch=20):
        cross_finial(B, m, 0, 0, 0.4)
    # charred posts at the corners and along the sides, the back ones
    # tallest, carrying what is left of the plate
    post = Mat(n + "_post", tex=kit.tex_planks(n + "_post", "#1a1612", boards=2, seed=6), uv=1.0)
    for x, y, h in ((X0, Y0, 0), (X1, Y0, 0), (X0, Y1, 3.0), (X1, Y1, 0)):
        top = h or surf(x, y) + rng.uniform(0.5, 1.0)
        B.beam(post, (x, y, -0.03), (x + (0 if h else rng.uniform(-0.08, 0.08)), y, top), 0.26)
    for k, (a, b, nrm, _, _, _) in SIDES.items():
        L_ = math.hypot(b[0] - a[0], b[1] - a[1])
        if k == "N":
            continue
        for i in range(1, int(L_ / 1.1)):
            if rng.random() > 0.7:
                continue
            t = L_ * i / int(L_ / 1.1) + rng.uniform(-0.2, 0.2)
            x, y = a[0] + (b[0] - a[0]) * t / L_, a[1] + (b[1] - a[1]) * t / L_
            top = rim(k, t) + rng.uniform(0.3, 1.0)
            B.beam(post, (x, y, -0.03), (x + rng.uniform(-0.1, 0.1), y + rng.uniform(-0.1, 0.1), top), 0.24)
    for x, h in ((-1.9, 2.8), (-0.5, 2.6), (0.5, 3.2), (1.3, 2.4)):
        B.beam(post, (x, Y1, -0.03), (x, Y1, h), 0.26)
    B.beam(post, (plate[0] - 0.1, Y1, plate[2] + 0.08), (plate[1], Y1, plate[2] - 0.1), 0.26)
    B.beam(post, (plate[1] - 0.1, Y1, plate[2] - 0.1), (plate[1] + 0.9, Y1 - 0.4, 1.7), 0.22)
    # charred beams lying across the slabs and down the spills
    dm = kit.debris_mats(n, pic, seed=9, box=(0, 0, 100, 125), k=4)
    kit.cover(B, {"beam": dm["beam"]}, r, Pr, surf, 22, {"beam": 1}, pic=pic, length=(1.2, 2.6), width=(0.16, 0.24),
              seed=14, lift=0.12, tilt=15, grow=1, jumble=0.15, stick=0.15)
    # the lean-to on the east end fell with it: boards and timbers
    dm = kit.debris_mats(n, pic, seed=9, box=(96, 20, 134, 110), k=4)
    lean = [(98, 22), (132, 34), (134, 90), (120, 112), (98, 104)]
    h = kit.heap(B, char, r, lean, 0.8, z_at=0.5, cell=0.45, noise=0.06, seed=11, edge=0.8, pic=pic)
    kit.cover(B, dm, r, kit.plan_of(r, lean, 0.5), h, 50, {"beam": 3, "board": 2, "slab": 2}, pic=pic,
              length=(1.2, 2.6), seed=12, tilt=30, grow=0, jumble=0.2, stick=0.25)
