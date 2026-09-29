"""Aramon farm pieces: haystacks, the open shed, the granaries and the
stone tower house, read off their sprites as models.py does."""
import math

import kit
from kit import Mat, px
from models import model


# ---------------------------------------------------------------- haystacks

def straw_mats(n, seed=1):
    body = Mat(n + "_hay", tex=kit.tex_straw(n + "_hay", "#1c1a0a", "#7b7024", "#b4aa38", speck="#d78f3e",
                                             speck_amt=0.1, seed=seed), uv=1.0)
    c = [Mat(n + "_s%d" % i, col, both=True) for i, col in enumerate(("#a89e34", "#8a7e26", "#d08c3c", "#3a3412"))]
    # mostly pale stalks, some orange ends and a few dark ones
    return body, [c[0], c[0], c[1], c[1], c[2], c[3]]


HAY_PROF = [(0.95, 0.0), (1.0, 0.12), (1.0, 0.28), (0.95, 0.48), (0.82, 0.66), (0.62, 0.82), (0.36, 0.94), (0.0, 1.0)]


def haystack(B, r, cx, cy, rx, ry, h, n_strands, loose, seed=1, prof=None):
    n = r["name"]
    body, cards = straw_mats(n, seed)
    surf = kit.dome(B, body, cx, cy, rx, ry, h, prof=prof or HAY_PROF, seg=22, jitter=0.05, seed=seed)
    kit.strands(B, cards, surf, n_strands, seed=seed + 1)
    # straw pulled off the stack lies in low tufts round it
    for i, (poly, k) in enumerate(loose):
        c0 = sum(p[0] for p in poly) / len(poly)
        r0 = sum(p[1] for p in poly) / len(poly)
        core = [(c + (c0 - c) * 0.45, rw + (r0 - rw) * 0.45) for c, rw in poly]
        kit.heap(B, body, r, core, 0.06, z_at=0.0, cell=0.2, noise=0.03, seed=seed + 20 + i, edge=0.25)
        kit.loose_straw(B, cards, r, poly, k, seed=seed + 10 + i, z=0.06)


def blob(c, rw, rc, rr, seed=0, k=10):
    """A ragged ellipse of sprite pixels round (c, rw)."""
    import random
    g = random.Random(seed)
    return [(c + rc * math.cos(2 * math.pi * i / k) * g.uniform(0.7, 1.15),
             rw + rr * math.sin(2 * math.pi * i / k) * g.uniform(0.7, 1.15)) for i in range(k)]


@model("Aracrop02")
def aracrop02(B, r):
    haystack(B, r, 0.0, 0.03, 1.92, 1.88, 2.4, 300, [
        (blob(6, 50, 6, 8, 1), 16),
        (blob(24, 68, 16, 6, 2), 30),
        (blob(66, 58, 10, 7, 3), 22),
    ], seed=3)


@model("Aracrop03")
def aracrop03(B, r):
    haystack(B, r, 0.05, 0.22, 2.45, 2.3, 2.1, 400, [
        (blob(6, 46, 6, 9, 1), 12),
        (blob(25, 80, 15, 9, 2), 34),
        (blob(57, 80, 7, 5, 3), 10),
        (blob(85, 67, 10, 12, 4), 28),
    ], seed=5)


# ---------------------------------------------------------------- the open shed

def shed_mats(n):
    import models
    return {"thatch": models.thatch_mats(n, base="#716a1e", dark="#443e14", light="#90862c", seed=7),
            "post": Mat(n + "_post", tex=kit.tex_planks(n + "_post", "#4a3c24", boards=2, seed=3, vertical=True), uv=1.0),
            "barrel": Mat(n + "_barrel", tex=kit.tex_planks(n + "_barrel", "#4a3a22", boards=8, seed=4, vertical=True),
                          uv=1.0),
            "hoop": Mat(n + "_hoop", "#1c1812"),
            "lid": Mat(n + "_lid", "#2a2218"),
            "log": Mat(n + "_log", tex=kit.tex_planks(n + "_log", "#6a665a", boards=3, seed=5), uv=0.8),
            "end": Mat(n + "_end", "#8a806a"),
            "ground": Mat(n + "_floor", tex=kit.tex_mottle(n + "_floor", "#3a3222", var=0.2, seed=6), uv=1.5)}


# eave and ridge of the lean-to: x across, front eave y and z, back y and z
# a touch deeper and lower than the pixel fit
SHED = {"x0": -2.0, "x1": 1.9, "yf": -1.45, "zf": 1.8, "yb": 1.0, "zb": 2.8}


def shed_frame(B, m, s, posts=True):
    """Posts and the two plates under the lean-to's roof."""
    x0, x1, yf, zf, yb, zb = s["x0"], s["x1"], s["yf"], s["zf"], s["yb"], s["zb"]
    k = (zb - zf) / (yb - yf)
    pf, pb = yf + 0.3, yb - 0.25
    zpf, zpb = zf + 0.3 * k - 0.3, zb - 0.25 * k - 0.3
    xs = [x0 + 0.25, -0.5, 0.75, x1 - 0.25]
    for x in xs:
        if posts:
            B.beam(m["post"], (x, pf, 0), (x, pf, zpf), 0.22)
            B.beam(m["post"], (x, pb, 0), (x, pb, zpb), 0.22)
        B.beam(m["post"], (x, pf - 0.1, zpf - 0.02), (x, pb + 0.1, zpb + 0.02), 0.15)
    B.beam(m["post"], (x0 + 0.05, pf, zpf - 0.14), (x1 - 0.05, pf, zpf - 0.14), 0.18)
    B.beam(m["post"], (x0 + 0.05, pb, zpb - 0.14), (x1 - 0.05, pb, zpb - 0.14), 0.18)


def shed_roof(B, m, s, seed=1, inset=0.5, drop=0.9):
    """The lean-to's thatch: one slope to the front, the ends hipped in by
    inset at the back and falling drop to the side eaves."""
    x0, x1, yf, zf, yb, zb = s["x0"], s["x1"], s["yf"], s["zf"], s["yb"], s["zb"]
    zs = zb - drop
    B.slab(m["thatch"], [(x0, yf, zf), (x1, yf, zf), (x1 - inset, yb, zb), (x0 + inset, yb, zb)], 0.3)
    B.slab(m["thatch"], [(x1, yf, zf), (x1 + 0.02, yb, zs), (x1 - inset, yb, zb)], 0.25)
    B.slab(m["thatch"], [(x0, yf, zf), (x0 + inset, yb, zb), (x0 - 0.02, yb, zs)], 0.25)
    # thatch hangs over the front and the two ends
    kit.fringe(B, m["thatch"], (x0, yf + 0.05, zf - 0.05), (x1, yf + 0.05, zf - 0.05), 0.42, seed=seed,
               out=(0, -0.25, 0))
    kit.fringe(B, m["thatch"], (x0 + 0.05, yb, zs - 0.05), (x0 + 0.05, yf, zf - 0.05), 0.45, seed=seed + 1,
               out=(-0.25, 0, 0))
    kit.fringe(B, m["thatch"], (x1 - 0.05, yf, zf - 0.05), (x1 - 0.05, yb, zs - 0.05), 0.4, seed=seed + 2,
               out=(0.25, 0, 0))


def log_pile(B, m, r, c0, r0, c1, r1, n=5, rad=0.14, seed=0):
    """Split logs stacked along the line the sprite draws from (c0, r0) to
    (c1, r1): a bottom row and fewer above."""
    import random
    g = random.Random(seed)
    a = Vector(px(r, c0, r0, 0.15) + (0.0,))
    b = Vector(px(r, c1, r1, 0.15) + (0.0,))
    d = (b - a)
    side = Vector((-d.y, d.x, 0)).normalized()
    rows = [(0, n - n // 2), (1, n // 2)]
    for level, k in rows:
        for i in range(k):
            off = (i - (k - 1) / 2) * rad * 2.05
            s0 = g.uniform(-0.1, 0.1)
            p0 = a + d * s0 + side * off
            p1 = b + d * g.uniform(-0.1, 0.1) + side * off
            z = rad + level * rad * 1.75
            kit.log(B, m["log"], (p0.x, p0.y, z), (p1.x, p1.y, z), rad * g.uniform(0.85, 1.1), end=m["end"])


from mathutils import Vector  # noqa: E402


def shaggy_roof(B, m, s, seed=1):
    """The shed's thatch as a rounded shaggy mass over the lean-to's slope:
    rounded corners, a body that bulges past the eaves and curls under,
    straw ends hanging off its edges; and a lower tier of thatch hung
    across the front left, as the sprite draws it down to row 55."""
    x0, x1, yf, zf, yb, zb = s["x0"], s["x1"], s["yf"], s["zf"], s["yb"], s["zb"]
    k = (zb - zf) / (yb - yf)

    def plane(x, y):
        return zf + (y - yf) * k
    cx, cy = (x0 + x1) / 2, (yf + yb) / 2
    prof = [(0.95, -0.28), (1.0, -0.12), (1.0, 0.04), (0.94, 0.16), (0.82, 0.26), (0.58, 0.33), (0.3, 0.36),
            (0.0, 0.37)]
    surf = kit.pillow(B, m["thatch"], cx, cy, (x1 - x0) / 2 * 0.95, (yb - yf) / 2 * 0.95, plane, prof, p=5.0,
                      seg=40, jitter=0.06, seed=seed)
    kit.strands(B, m["cards"], surf, 220, t0=0.0, t1=0.45, length=(0.25, 0.6), w=(0.06, 0.11), lift=0.2,
                seed=seed + 1)
    kit.strands(B, m["cards"], surf, 90, t0=0.45, t1=0.95, length=(0.12, 0.22), w=(0.05, 0.08), lift=0.3,
                seed=seed + 2)
    # the lower tier across the front left, cols 4-34, its hem at row 55
    tx0, tx1 = x0 + 0.05, -0.1
    ty0, ty1 = yf - 0.2, yf + 0.5

    def tier(x, y):
        return 1.55 + (y - ty0) * 0.5
    tp = [(0.95, -0.35), (1.05, -0.15), (1.02, 0.08), (0.85, 0.2), (0.0, 0.24)]
    ts = kit.pillow(B, m["thatch"], (tx0 + tx1) / 2, (ty0 + ty1) / 2, (tx1 - tx0) / 2, (ty1 - ty0) / 2, tier, tp,
                    p=4.0, seg=24, jitter=0.08, seed=seed + 3)
    kit.strands(B, m["cards"], ts, 90, t0=0.0, t1=0.4, length=(0.2, 0.45), w=(0.06, 0.1), lift=0.15, seed=seed + 4)


def shed_walls(B, m, s):
    """Boards closing the back and ends under the roof, so its inside
    reads dark between the posts and no ground shows through."""
    x0, x1, yf, zf, yb, zb = s["x0"], s["x1"], s["yf"], s["zf"], s["yb"], s["zb"]
    k = (zb - zf) / (yb - yf)
    B.box(m["inside"], (x0 + x1) / 2, yb - 0.3, 0, x1 - x0 - 0.4, 0.1, zb - 0.35 * k - 0.4)
    for x in (x0 + 0.25, x1 - 0.25):
        y0, y1 = yf + 0.6, yb - 0.3
        B.solid(m["inside"], [(x - 0.05, y0, 0), (x - 0.05, y1, 0), (x - 0.05, y1, zf + (y1 - yf) * k - 0.4),
                              (x - 0.05, y0, zf + (y0 - yf) * k - 0.4),
                              (x + 0.05, y0, 0), (x + 0.05, y1, 0), (x + 0.05, y1, zf + (y1 - yf) * k - 0.4),
                              (x + 0.05, y0, zf + (y0 - yf) * k - 0.4)],
                [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)])
    B.box(m["floor"], (x0 + x1) / 2, (yf + yb) / 2 + 0.2, 0, x1 - x0 - 0.3, yb - yf - 0.4, 0.02)


@model("AraHut09")
def arahut09(B, r):
    n = r["name"]
    m = shed_mats(n)
    m["inside"] = Mat(n + "_inside", tex=kit.tex_planks(n + "_inside", "#2a2216", boards=6, seed=8, vertical=True),
                      uv=1.2)
    m["floor"] = Mat(n + "_dfloor", "#221c12")
    body, _ = straw_mats(n + "_t", seed=4)
    m["cards"] = [Mat(n + "_c%d" % i, c, both=True) for i, c in enumerate(("#8e8224", "#6e6418", "#a89a2c",
                                                                            "#b0782a", "#3e3810"))]
    s = SHED
    shed_frame(B, m, s)
    shed_walls(B, m, s)
    shaggy_roof(B, m, s)
    # a barrel lying under the front eave, its end to the camera, col 44 row 60
    x, y = px(r, 44, 60, 0.42)
    kit.barrel(B, m["barrel"], m["hoop"], x, y + 0.5, r=0.42, h=1.0, lying=True)
    # split logs stacked by the front right corner, cols 48-76
    log_pile(B, m, r, 52, 68, 74, 56, n=4, rad=0.11, seed=2)


def char_mats(n, seed=21):
    return {"heap": Mat(n + "_char", tex=kit.tex_rubble(n + "_char", "#292521", light="#6a645a", dark="#0e0c0a",
                                                        seed=seed), uv=1.2),
            "lump": Mat(n + "_lump", tex=kit.tex_mottle(n + "_lump", "#5a4832", var=0.25, seed=seed + 1), uv=0.8),
            "beam": Mat(n + "_cbeam", tex=kit.tex_planks(n + "_cbeam", "#241c14", boards=2, seed=seed + 2), uv=1.0),
            "plank": Mat(n + "_cplank", tex=kit.tex_planks(n + "_cplank", "#302418", boards=3, seed=seed + 3), uv=1.0),
            "stone": Mat(n + "_cstone", tex=kit.tex_mottle(n + "_cstone", "#4a4238", var=0.2, seed=seed + 4), uv=0.8),
            "log": Mat(n + "_clog", tex=kit.tex_planks(n + "_clog", "#3e3c36", boards=3, seed=seed + 5), uv=0.8),
            "end": Mat(n + "_cend", "#5a5850")}


@model("AraHut09a")
def arahut09a(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    m = char_mats(n)
    m["heap"] = Mat(n + "_char", tex=kit.tex_grit(n + "_char", "#1a1814", light="#8a8680", dark="#080706", uv=1.5,
                                                   chips=0.14, darks=0.3, seed=2), uv=1.5)
    # charcoal chunks, black and grey with white ash on some, as drawn
    coal = [Mat(n + "_coal%d" % i, tex=kit.tex_grit(n + "_coal%d" % i, c, light=l, dark="#060605", uv=0.6,
                                                     chips=ch, darks=0.25, seed=10 + i), uv=0.6, ref=ref)
            for i, (c, l, ch, ref) in enumerate((("#161412", "#5a5854", 0.08, "#161412"),
                                                 ("#2a2826", "#8a8884", 0.15, "#2c2a28"),
                                                 ("#4a4846", "#b0aea8", 0.25, "#5a5856"),
                                                 ("#6a6864", "#c8c6c0", 0.3, "#7e7c78")))]
    core = [(10, 5), (44, 5), (49, 9), (57, 13), (58, 20), (66, 24), (62, 29), (56, 33), (58, 39), (49, 42),
            (41, 45), (34, 44), (20, 43), (11, 43), (6, 36), (4, 24), (8, 12)]
    # the thatch burnt where it fell: a low black bed over the floor
    h = kit.heap(B, m["heap"], r, core, 0.55, z_at=0.3, cell=0.3, noise=0.05, seed=2, edge=0.9, pic=pic)
    kit.cover(B, {"chunk": coal, "stone": coal}, r, kit.plan_of(r, core, 0.3), h, 150, {"chunk": 3, "stone": 2},
              pic=pic, chunk=(0.14, 0.3), stone=(0.18, 0.34), seed=3, tilt=25, grow=0, q=0.6, jumble=0.08)
    # scorched brown lumps of the walls' daub all round its rim
    for i, (c, rw, w) in enumerate(((7, 25, 0.9), (4, 32, 0.6), (16, 6, 0.8), (27, 5, 0.85), (38, 6, 0.7),
                                    (48, 9, 0.6), (57, 14, 0.5), (48, 27, 0.7), (53, 33, 0.55), (42, 36, 0.6),
                                    (29, 36, 0.8), (18, 38, 0.6), (9, 36, 0.5), (8, 16, 0.6))):
        x, y = px(r, c, rw, 0.3)
        kit.rock(B, m["lump"], x, y, 0.05, w, w * 0.8, w * 0.5, seed=i, seg=6)
    # a few charred post stubs
    for c, rw, hh in ((12, 12, 0.9), (47, 11, 0.7)):
        x, y = px(r, c, rw, hh)
        B.box(m["beam"], x, y, 0, 0.16, 0.16, hh, yaw=12, pitch=6)
    # the charred barrel at the front, its end to the camera, col 40 row 54
    x, y = px(r, 40, 54, 0.4)
    burnt = Mat(n + "_bbarrel", tex=kit.tex_grit(n + "_bbarrel", "#241c16", light="#5a4a3a", dark="#0a0806", uv=0.8,
                                                  chips=0.1, darks=0.3, seed=5), uv=0.8)
    kit.barrel(B, burnt, m["beam"], x, y + 0.45, r=0.4, h=0.9, lying=True)
    # the log pile burnt to charcoal where it lay, cols 45-72
    logs = {"log": Mat(n + "_clog", tex=kit.tex_grit(n + "_clog", "#2a2826", light="#8a8884", dark="#0a0908", uv=0.8,
                                                      chips=0.2, darks=0.25, seed=6), uv=0.8),
            "end": Mat(n + "_cend", "#4a4846")}
    log_pile(B, logs, r, 48, 56, 70, 45, n=4, rad=0.11, seed=4)


# ---------------------------------------------------------------- granaries

def granary_mats(n, seed=31):
    return {"wall": Mat(n + "_gwall", tex=kit.tex_mottle(n + "_gwall", "#584e3b", var=0.22, seed=seed,
                                                         speck="#6a5e48", speck_amt=0.05), uv=1.5),
            "board": Mat(n + "_board", tex=kit.tex_planks(n + "_board", "#6e6454", boards=2, var=0.14, seed=seed + 1,
                                                          vertical=True), uv=0.9),
            "under": Mat(n + "_under", "#2a2016"),
            "straw": Mat(n + "_gstraw", "#8a8028", both=True),
            "cap": Mat(n + "_cap", tex=kit.tex_mottle(n + "_cap", "#48261a", var=0.25, seed=seed + 2, speck="#9a4a26",
                                                      speck_amt=0.1), uv=0.8),
            "rail": Mat(n + "_rail", tex=kit.tex_planks(n + "_rail", "#7a6a56", boards=2, seed=seed + 3), uv=1.0),
            "plank": Mat(n + "_gplank", tex=kit.tex_planks(n + "_gplank", "#7a7064", boards=5, var=0.16, seed=seed + 4,
                                                           vertical=True), uv=1.2),
            "dark": Mat(n + "_gdark", "#141008")}


def board_cone(B, m, x, y, z_e, R_e, z_top, r_top, n=18, seed=0, gap=0.14, over=0.3):
    """A conical roof of radial boards over a dark underlay: each board a
    tapering slab from the eave to near the top, lapped a little."""
    import random
    g = random.Random(seed)
    B.cyl(m["under"], x, y, z_e - 0.05, R_e - 0.1, z_top - z_e + 0.05, r_top=0.2, seg=n)
    for i in range(n):
        a = 2 * math.pi * (i + g.uniform(-0.1, 0.1)) / n
        w_e = 2 * math.pi * R_e / n * (1 - gap) * g.uniform(0.9, 1.05)
        w_t = 2 * math.pi * r_top / n * (1 - gap)
        Re = R_e + g.uniform(-0.05, 0.12)
        u = Vector((math.cos(a), math.sin(a), 0))
        s = Vector((-math.sin(a), math.cos(a), 0))
        lo = Vector((x, y, z_e)) + u * Re
        hi = Vector((x, y, z_top)) + u * r_top
        d = (lo - hi).normalized()
        lo = lo + d * g.uniform(0.0, over)
        lift = Vector((0, 0, 0.04))
        B.slab(m["board"], [tuple(lo - s * w_e / 2 + lift), tuple(lo + s * w_e / 2 + lift),
                            tuple(hi + s * w_t / 2 + lift), tuple(hi - s * w_t / 2 + lift)], 0.07)
    # straw poking out between the boards at the eave
    for i in range(n):
        a = 2 * math.pi * (i + 0.5) / n
        u = Vector((math.cos(a), math.sin(a), 0))
        p = Vector((x, y, z_e)) + u * (R_e - 0.1)
        kit.card(B, m["straw"], tuple(p), tuple(p + u * 0.35 + Vector((0, 0, -0.25))), 0.1, Vector((0, 0, 1)))


def ladder(B, m, a, b, w=0.7, step=0.33, rail=0.1):
    """Two rails from a (foot) to b (top) and rungs between."""
    a, b = Vector(a), Vector(b)
    d = b - a
    side = d.cross(Vector((0, 0, 1)))
    side = side.normalized() * (w / 2) if side.length > 1e-6 else Vector((w / 2, 0, 0))
    for e in (-1, 1):
        B.beam(m["rail"], tuple(a + side * e), tuple(b + side * e + d.normalized() * 0.2), rail)
    k = int(d.length / step)
    for i in range(1, k + 1):
        p = a + d * (i / (k + 0.5))
        B.beam(m["rail"], tuple(p - side * 1.1), tuple(p + side * 1.1), rail * 0.7)


# the granaries stand broader and lower than the pixel fit: the wall's
# radius 2.1 becomes 2.4 and the top 7 cells becomes 6.2, so the drum reads
# as a squat store and not a tower from low down
GR = dict(R=2.4, H=4.4, R_e=2.72, z_top=6.2)


def granary(B, m, x, y, R=GR["R"], H=GR["H"], R_e=GR["R_e"], z_top=GR["z_top"], seed=0):
    B.cyl(m["wall"], x, y, 0, R, H + 0.3, r_top=R * 0.97, seg=24)
    board_cone(B, m, x, y, H, R_e, z_top - 0.3, 1.05, n=16, seed=seed, gap=0.08, over=0.35)
    B.lathe(m["cap"], x, y, [(0, z_top - 0.9), (1.45, z_top - 0.9), (1.4, z_top - 0.55), (1.1, z_top - 0.25),
                             (0.6, z_top - 0.05), (0, z_top)], seg=14, jitter=0.06, seed=seed)


@model("AraHut12")
def arahut12(B, r):
    n = r["name"]
    m = granary_mats(n)
    gx, gy = 0.03, 0.75
    granary(B, m, gx, gy, seed=3)
    # the ladder up the front to a hatch under the eave, cols 42-55
    fx, fy = px(r, 48.5, 141)
    top_y = gy - math.sqrt(GR["R"] ** 2 - (fx - gx) ** 2) - 0.12
    B.box(m["dark"], fx, top_y + 0.06, 3.35, 0.85, 0.1, 0.9)
    ladder(B, m, (fx, fy - 0.2, 0), (fx, top_y, 3.75), w=0.75, rail=0.13)


@model("AraHut11")
def arahut11(B, r):
    n = r["name"]
    m = granary_mats(n, seed=37)
    gx, gy = 0.42, 1.15
    granary(B, m, gx, gy, seed=5)
    # a plank porch built against the front, turned 30 degrees, a lean-to
    # roof climbing to the granary wall with the ladder up through it
    cx, cy, yaw = -0.95, -1.45, -30.0
    L, W, H = 3.2, 2.2, 2.2
    with B.at(cx, cy, yaw=yaw):
        B.box(m["plank"], 0, 0, 0, L, W, H)
        # the roof boards run up the slope from the front eave to the wall
        B.slab(m["plank"], [(-L / 2 - 0.15, -W / 2 - 0.25, H - 0.1), (L / 2 + 0.15, -W / 2 - 0.25, H - 0.1),
                            (L / 2 + 0.15, W / 2 + 0.3, H + 1.3), (-L / 2 - 0.15, W / 2 + 0.3, H + 1.3)], 0.1)
        for e in (-1, 1):
            B.slab(m["plank"], [(e * L / 2, -W / 2, H), (e * L / 2, W / 2, H), (e * L / 2, W / 2, H + 1.2)], 0.1)
        # corner posts and a door
        for ex in (-1, 1):
            for ey in (-1, 1):
                B.box(m["rail"], ex * (L / 2 - 0.05), ey * (W / 2 - 0.05), 0, 0.22, 0.22, H + (0.1 if ey < 0 else 1.2))
        B.box(m["dark"], 0.55, -W / 2 - 0.03, 0, 0.8, 0.06, 1.5)
        # the hatch the ladder climbs out of
        B.box(m["dark"], 0.55, 0.35, H + 0.5, 0.9, 0.7, 0.12)
    fx, fy = px(r, 40, 104, 2.6)
    top_y = gy - math.sqrt(GR["R"] ** 2 - (fx - gx) ** 2) - 0.1
    ladder(B, m, (fx, fy - 0.3, 2.3), (fx + 0.1, top_y, 3.9), w=0.72, rail=0.13)


# ---------------------------------------------------------------- granary ruins

def granary_ruin_mats(n, seed=71):
    m = granary_mats(n, seed=seed)
    m["wtop"] = Mat(n + "_gwtop", tex=kit.tex_mottle(n + "_gwtop", "#7a6a50", var=0.2, seed=seed + 5), uv=1.0)
    m["lump"] = Mat(n + "_glump", tex=kit.tex_mottle(n + "_glump", "#6e5c44", var=0.25, seed=seed + 6), uv=0.8)
    m["capbit"] = Mat(n + "_capbit", tex=kit.tex_mottle(n + "_capbit", "#5a2e1c", var=0.25, seed=seed + 7,
                                                        speck="#9a4a26", speck_amt=0.1), uv=0.8)
    return m


def boulder_heap(B, r, pic, poly, top, z_at, seed=0, n_boulders=30, n_pieces=40, edge=1.4, gravel=None):
    """A granary fallen into a heap: its wall's big dark boulders piled
    over a bed of grey gravel, timbers and staves among them; gravel is an
    outline where the bed shows through, as a streak or a patch."""
    n = r["name"]
    bed = Mat(n + "_bed", tex=kit.tex_grit(n + "_bed", "#2e2820", light="#6a6258", dark="#0e0c0a", uv=1.5, chips=0.06,
                                            darks=0.3, seed=seed), uv=1.5)
    h = kit.heap(B, bed, r, kit.jag(poly, 2.0, 4.0, seed), top, z_at=z_at, cell=0.35, noise=0.06, seed=seed + 1,
                 edge=edge, pic=pic)
    if gravel is not None:
        grey = Mat(n + "_gravel", tex=kit.tex_grit(n + "_gravel", "#2e2c2a", light="#a8a49c", dark="#0c0b0a", uv=1.2,
                                                    chips=0.16, darks=0.35, seed=seed + 4), uv=1.2)
        # the gravel lies a little proud of the bed where it shows
        kit.drape(B, grey, kit.plan_of(r, kit.jag(gravel, 2.0, 3.0, seed), z_at), h, cell=0.25, lift=0.06, seed=seed)
    cols = kit.palette(pic, k=6, lit=0.4)
    rocks = kit.swatches(n + "_bo", cols, "mottle", seed=seed + 10, gain=1.05, sat=1.2)
    wood = kit.swatches(n + "_tw", [c for c in cols if c[0] >= c[2]][:3] or cols, "planks", seed=seed + 20,
                        gain=1.1, sat=0.8)
    dm = {"boulder": rocks, "stone": rocks, "beam": wood, "board": wood}
    keep = poly if gravel is None else poly
    P = kit.plan_of(r, keep, z_at)
    if gravel is not None:
        G = kit.plan_of(r, gravel, z_at)
        ground = lambda x, y: -1e9 if kit.inside_poly(G, x, y) else h(x, y)  # noqa: E731
    else:
        ground = h
    kit.cover(B, dm, r, P, ground, n_boulders, {"boulder": 1}, pic=pic, boulder=(0.7, 1.25), seed=seed + 2,
              tilt=15, grow=0, q=0.75)
    kit.cover(B, dm, r, P, h, n_pieces, {"beam": 3, "stone": 2, "board": 1}, pic=pic, length=(1.2, 2.6),
              width=(0.12, 0.2), stone=(0.2, 0.4), seed=seed + 3, tilt=25, grow=0, jumble=0.25, stick=0.3)
    return h, dm


@model("AraHut12a")
def arahut12a(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    m = granary_ruin_mats(n)
    # an oval heap, taller than wide, the grey gravel running down its middle
    heap = [(6, 22), (18, 6), (40, 2), (62, 8), (72, 30), (72, 62), (64, 86), (46, 96), (22, 94), (6, 78), (2, 50)]
    streak = [(28, 12), (48, 12), (52, 50), (50, 92), (26, 92), (24, 50)]
    boulder_heap(B, r, pic, heap, 2.4, 1.0, seed=3, n_boulders=50, n_pieces=30, edge=2.2, gravel=streak)
    # the ladder fell outward at the front right, cols 55-70
    a = px(r, 56, 66, 0.2)
    b = px(r, 70, 62, 0.2)
    ladder(B, m, (a[0], a[1], 0.1), (b[0] + 0.2, b[1] - 1.3, 0.25), w=0.7)


@model("AraHut11a")
def arahut11a(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    m = granary_ruin_mats(n, seed=77)
    # a dark mounded pile of boulders and timbers, grey gravel at its top
    heap = [(8, 14), (30, 2), (58, 6), (76, 22), (80, 48), (74, 80), (62, 102), (48, 100), (42, 72), (14, 62),
            (0, 40)]
    patch = [(38, 14), (58, 16), (60, 40), (44, 46), (36, 30)]
    h, dm = boulder_heap(B, r, pic, heap, 2.2, 1.0, seed=5, n_boulders=55, n_pieces=50, edge=2.0, gravel=patch)
    # the plank porch's side walls still stand at the lower left, grey boards
    import random
    g = random.Random(9)
    board = Mat(n + "_gboard", tex=kit.tex_planks(n + "_gboard", "#6a6862", boards=2, var=0.14, seed=9,
                                                    vertical=True), uv=0.9)
    # its boards stand leaning back against the heap, read off the
    # sprite: feet along row 100 or so, tops up to row 58
    for i, c in enumerate(range(6, 54, 4)):
        foot = px(r, c + g.uniform(-1, 1), 101 - (c - 6) * 0.15 + g.uniform(-2, 2), 0.0)
        zt = g.uniform(1.4, 2.0)
        top = px(r, c + g.uniform(-2, 3), g.uniform(56, 72), zt)
        B.beam(board, foot + (0.0,), top + (zt,), 0.26, 0.07, twist=g.uniform(-20, 20))
    # lumps rolled off at the lower right and the bottom, a plank at the right
    lumps = [(72, 70, 0.4, 0.9, "#4a2d1d"), (58, 94, 0.2, 0.9, "#5a5448"), (78, 58, 0.3, 0.6, "#3e3228"),
             (44, 100, 0.1, 0.7, "#3a3028")]
    for i, (c, rw, z, s, col) in enumerate(lumps):
        x, y = px(r, c, rw, z)
        mat = Mat(n + "_lump%d" % i, tex=kit.tex_mottle(n + "_lump%d" % i, col, var=0.2, seed=40 + i), uv=0.8)
        kit.rock(B, mat, x, y, 0.0, s, s * 0.85, s * 0.6, seed=50 + i, seg=6)
    dark = Mat(n + "_dplank", tex=kit.tex_planks(n + "_dplank", "#2a2622", boards=2, seed=11), uv=1.0)
    kit.leaning(B, dark, r, [(72, 52, 0.6, 98, 56, 0.1), (68, 86, 0.1, 64, 104, 0.1)], w=0.2)
