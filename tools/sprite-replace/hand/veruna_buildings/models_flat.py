"""Flat-roofed Veruna town houses round a courtyard: VerBuild07 and 08 and
their ruins. Adobe blocks at several heights with parapets and beam ends
(vigas), jars and baskets on the roofs, a tiled courtyard, a market stall.
"""
import math

import kit
from kit import model
from models_compound import BURNT, burnt_joists, flat_holed, rect, ring, ruin_tones, tower_roof


def adobe(m, pts, top, wall="adobe", roof="flat", par="parapet", par_h=0.3, par_t=0.22, vigas="viga",
          faces="SEW", keep=None, prof=None, vstep=1.05):
    """A flat-roofed block over the outline (x, y points) whose parapet top
    is at top, with beam ends out of its walls just under the roof."""
    if kit._area(pts) < 0:
        pts = pts[::-1]
    h = top - par_h
    if keep is None:
        m.flat(wall, roof, pts, h, par_h=par_h, par_t=par_t, cap=par)
    else:
        # a burnt shell: ragged walls and what is left of the roof
        sh = ring(pts, 0.15)
        m.bwall(wall, sh + sh[:1], 0.3, prof or [(0, top), (1, top)], frac=True, jag=0.3, cap=par, step=0.45)
        if keep is not False:
            flat_holed(m, roof, ring(pts, 0.3), h, keep)
    if vigas:
        viga_row(m, pts, h - 0.28, vigas, faces, step=vstep)


def viga_row(m, pts, z, key, faces="SEW", step=1.05, out=0.28, w=0.16):
    n = len(pts)
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        dx, dy = b[0] - a[0], b[1] - a[1]
        L = math.hypot(dx, dy)
        if L < 0.8:
            continue
        nx, ny = dy / L, -dx / L  # outward for a counter-clockwise outline
        face = "S" if ny < -0.7 else "N" if ny > 0.7 else "E" if nx > 0 else "W"
        if face not in faces:
            continue
        k = max(1, int(L / step))
        for j in range(k):
            u = (j + 0.5) / k
            x, y = a[0] + dx * u + nx * out / 2, a[1] + dy * u + ny * out / 2
            m.obox(key, x, y, z, w if face in "SN" else out + 0.02, out + 0.02 if face in "SN" else w, w)


def jar(m, key, x, y, z0=0.0, r=0.42, h=0.85):
    m.lathe(key, x, y, [(r * 0.55, z0), (r, z0 + h * 0.4), (r * 0.95, z0 + h * 0.7), (r * 0.5, z0 + h * 0.92),
                        (r * 0.55, z0 + h)], seg=12)


def basket(m, key, x, y, z0=0.0, r=0.55, h=0.45, rim=None):
    m.lathe(key, x, y, [(r * 0.8, z0), (r, z0 + h * 0.6), (r * 1.02, z0 + h)], seg=14)
    if rim:
        m.cyl(rim, x, y, z0 + h - 0.06, r * 1.05, 0.08, seg=14)


# ---- VerBuild07 --------------------------------------------------------

def b07_colours(m):
    # adobe blocks up to four storeys: lower and deeper
    m.sturdy(0.88)
    m.col("adobe", (84, 74, 62))
    m.col("flat", (246, 229, 188))
    m.col("parapet", (236, 218, 180))
    m.col("viga", (70, 50, 34))
    m.col("dark", (30, 22, 15))
    m.col("sill", (230, 222, 200))
    m.col("floor", (188, 162, 94), paint=True)
    m.col("cloth", (104, 114, 128))
    m.col("rail", (50, 38, 28))
    m.col("jar", (74, 64, 55))
    m.col("basket", (160, 124, 70))
    m.col("rim", (120, 88, 50))
    m.col("bundle", (74, 76, 58))
    m.col("awn_a", (206, 212, 200))
    m.col("awn_b", (176, 194, 198))
    m.col("post", (80, 60, 40))
    m.col("crate", (100, 70, 40))
    m.col("red", (160, 50, 30))
    m.col("orange", (196, 120, 44))
    m.col("green", (86, 116, 52))


def b07_plan(F):
    R = lambda c0, c1, r0, r1, z: rect(F.X(c0), F.X(c1), F.Y(r1, z), F.Y(r0, z))  # noqa: E731
    xa1 = F.X(137)
    p = dict(
        A=(R(47, 137, 2, 88, 3.75), 3.75),
        B=(R(5, 47, 25, 80, 2.2), 2.2),
        C=(rect(F.X(10), F.X(58), F.Y(170, 1.75), F.Y(80, 2.2)), 1.75),
        D=([(xa1, F.Y(78, 3.45)), (F.X(180), F.Y(78, 3.45)), (F.X(180), F.Y(105, 3.45)), (F.X(235), F.Y(105, 3.45)),
            (F.X(235), F.Y(38, 3.45)), (xa1, F.Y(38, 3.45))], 3.45),
        E=(R(150, 182, 25, 38, 4.2), 4.2),
        F=(R(245, 290, 52, 120, 3.0), 3.0),
        G=(R(140, 186, 95, 170, 1.75), 1.75),
    )
    p["alley"] = (rect(F.X(235), F.X(245), F.Y(105, 3.45), F.Y(52, 3.0)), 1.3)
    p["court"] = (F.X(58), F.X(140), F.Y(170, 1.75), F.Y(88, 3.75))
    p["gate"] = (F.X(85), F.X(120))
    p["awning"] = (F.X(205), F.X(270), F.Y(105, 3.45) - 0.05, F.Y(157, 1.7))
    p["frame"] = F
    return p


def b07_blocks(m, P, keeps=None, skip=(), profs=None, vigas="viga", vstep=1.05):
    keeps = keeps or {}
    profs = profs or {}
    for k in ("A", "B", "C", "D", "E", "F", "G"):
        if k in skip:
            continue
        pts, top = P[k]
        adobe(m, pts, top, keep=keeps.get(k), prof=profs.get(k), vigas=vigas, vstep=vstep)
    pts, top = P["alley"]
    m.prism("adobe", pts, 0.0, top)
    # windows over the courtyard
    F = P["frame"]
    ya = P["A"][0][0][1]
    for c in (80, 91, 113):
        m.window("dark", F.X(c), ya, 1.6, 0.5 if c != 113 else 0.9, 0.35, sill="sill")


def b07_court(m, P, cloth=True, gate=True):
    F = P["frame"]
    x0, x1, y0, y1 = P["court"]
    m.box("floor", x0, x1, y0, y1, 0.0, 0.03)
    g0, g1 = P["gate"]
    # the front wall and its arched gate
    m.wall("adobe", [(x0, y0 + 0.15), (g0, y0 + 0.15)], 0.3, 1.75, cap="parapet", cap_h=0.05)
    m.wall("adobe", [(g1, y0 + 0.15), (x1, y0 + 0.15)], 0.3, 1.75, cap="parapet", cap_h=0.05)
    if gate:
        m.box("adobe", g0, g1, y0, y0 + 0.3, 1.25, 1.75)
        m.box("parapet", g0, g1, y0 - 0.02, y0 + 0.32, 1.75, 1.8)
        for s in (-1, 1):
            m.box("adobe", (g0 + g1) / 2 + s * 0.95, (g0 + g1) / 2 + s * 0.7, y0, y0 + 0.3, 0.9, 1.25)
        m.box("dark", g0 - 0.02, g1 + 0.02, y0 + 0.25, y0 + 0.32, 0.0, 1.25)
    if cloth:
        # drying rails with cloths over them across the courtyard
        for r, spans in ((138, ((70, 95), (100, 112), (115, 130))), (147, ((72, 96), (102, 122))),
                         (155, ((70, 88), (95, 110), (118, 128)))):
            y = F.Y(r, 1.0)
            m.beam("rail", (x0 + 0.1, y, 1.0), (x1 - 0.1, y, 1.0), 0.07)
            for xx in (x0 + 0.15, x1 - 0.15):
                m.obox("rail", xx, y, 0.5, 0.1, 0.1, 1.0)
            for c0, c1 in spans:
                m.box("cloth", F.X(c0), F.X(c1), y - 0.04, y + 0.04, 0.35, 1.02)


def b07_props(m, P):
    F = P["frame"]
    for c, r, z in ((152, 118, 1.75), (152, 135, 1.75), (157, 150, 1.75), (143, 92, 1.75), (172, 97, 1.75)):
        jar(m, "jar", F.X(c), F.Y(r, z + 0.45), z0=z)
    for c, r in ((27, 137), (47, 146), (27, 152)):
        basket(m, "basket", F.X(c), F.Y(r, 2.0), z0=1.45, rim="rim")
    for c, r, yaw in ((24, 112, 0.2), (40, 111, -0.1), (32, 124, 0.4)):
        m.crate("bundle", F.X(c), F.Y(r, 1.9), s=0.8, h=0.55, yaw=yaw, z0=1.45)


def b07_stall(m, P, keep=None, crates=True):
    x0, x1, yb, yf = P["awning"]
    if keep is None:
        m.awning("awn_a", "awn_b", (x0, yb), (x1, yb), yb - yf, 0.6, 2.3, stripes=8)
    else:
        n = 8
        for i in range(n):
            xa, xb = x0 + (x1 - x0) * i / n, x0 + (x1 - x0) * (i + 1) / n
            if keep((xa + xb) / 2, (yb + yf) / 2, 2.0):
                m.awning("awn_a" if i % 2 == 0 else "awn_b", "awn_b", (xa, yb), (xb, yb), yb - yf, 0.6 + 0.3 * (i % 3),
                         2.3, stripes=1)
    for x in (x0 + 0.1, (x0 + x1) / 2, x1 - 0.1):
        m.obox("post", x, yf + 0.1, 0.85, 0.12, 0.12, 1.7)
    if crates:
        cols = ("red", "orange", "green", "red", "orange", "green")
        for i, c in enumerate(cols):
            x = x0 + 0.5 + i * (x1 - x0 - 1.0) / (len(cols) - 1)
            m.box("crate", x - 0.3, x + 0.3, yf + 0.15, yf + 0.75, 0.0, 0.38)
            m.box(c, x - 0.26, x + 0.26, yf + 0.19, yf + 0.71, 0.38, 0.46)


@model("VerBuild07")
def verbuild07(m):
    b07_colours(m)
    P = b07_plan(m.frame())
    b07_blocks(m, P)
    b07_court(m, P)
    b07_props(m, P)
    b07_stall(m, P)


# a room heaped to its wall tops: mostly charred timber, broken short
HEAPED = dict(char=0.5, stone=0.22, wood=0.13, tile=0.15)


def burnt_roofs(m, pred, sigma=3.5, bias=0.3):
    """The field of the flat roofs still standing: kept where the drawing
    is pale or sooty grey, holed where it shows char."""
    return m.pic_field(pred, sigma=sigma, bias=bias)


def burnt_block(m, pts, top, field, T, par_h=0.3, prof=None, rim=0.25, fill=0.55, seed=0, vigas="viga",
                vstep=2.4, faces="SEW", step=0.55, maxn=60, cell=0.2):
    """A flat-roofed block gutted by fire: its walls stand with a ragged
    parapet, its roof painted from the drawing and holed where field <= 0
    (the eave ring held by rim), and every hole is filled with fallen
    timbers and rubble at fill of its height."""
    if kit._area(pts) < 0:
        pts = pts[::-1]
    h = top - par_h
    sh = ring(pts, 0.15)
    m.bwall("adobe", sh + sh[:1], 0.3, prof or [(0, top), (0.3, top - 0.15), (0.6, top), (1, top)], frac=True,
            jag=0.18, cap="parapet", step=step)
    edge = m.region(pts, rag=0.0)

    def f(x, y, z=0.0):
        d = edge(x, y)
        return field(x, y, z) + rim * max(0.0, 1.0 - d / 0.6)
    inner = ring(pts, 0.3)
    tower_roof(m, "flat", inner, h, f, cell=cell)
    nz = m.noise("fill%d" % seed, 0.7)
    m.debris(T, inner, h, zfn=lambda x, y: h * fill * (1 + 0.2 * nz(x, y)), density=3.0, maxn=maxn, cell=0.45,
             mix=BURNT, size=(0.2, 0.45), beam_len=(0.6, 1.6), seed=seed,
             where=lambda x, y: f(x, y, h) < 0.05, bed_where=lambda x, y: f(x, y, h) < 0.3)
    burnt_joists(m, "viga", inner, h, f, n=2, seed=seed)
    if vigas:
        viga_row(m, pts, h - 0.28, vigas, faces, step=vstep)


def torn_awning(m, x0, x1, yb, yf, z, drop, field, keys=("awn_a", "awn_b"), sag=0.5, frame="post"):
    """A striped market awning torn and sagging on its frame: cloth from
    the back beam at z out to the front, dropping drop, torn where field <= 0."""
    m.drape(keys, (x0, yb, z), (x1 - x0, 0, 0), (0, yf - yb, 0), 8, 6,
            lambda a, b: -drop * b - sag * math.sin(math.pi * a) * math.sin(math.pi * b) * 1.2, field=field,
            t=0.04)
    m.beam(frame, (x0, yb, z), (x1, yb, z), 0.14)
    for x in (x0 + 0.1, x1 - 0.1):
        m.post(frame, x, yf + 0.1, 0.0, z - drop, 0.12)
        m.post(frame, x, yb, 0.0, z, 0.12)
    m.beam(frame, (x0 + 0.1, yf + 0.1, z - drop - 0.05), ((x0 + x1) / 2, yf + 0.3, z - drop - 0.6), 0.1)


def spilled_crates(m, spots, seed=0):
    """Produce crates knocked over: (col, row, yaw, fruit key) each."""
    import random
    rng = random.Random("%s:c%d" % (m.name, seed))
    for c, r, yaw, fruit in spots:
        x, y = m.X(c), m.Y(r, 0.2)
        roll = rng.uniform(-0.5, 0.5)
        m.obox("crate", x, y, 0.2, 0.6, 0.55, 0.38, yaw, 0.0, roll)
        for _ in range(4):
            a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0.3, 0.7)
            m.cyl(fruit, x + d * math.cos(a), y + d * math.sin(a), 0.0, 0.09, 0.16, seg=6)


@model("VerBuild07a")
def verbuild07a(m):
    b07_colours(m)
    keys = m.ruin_palette()
    T = ruin_tones(m, (166, 147, 118), (84, 74, 62), (110, 98, 80), stone=(140, 126, 104), ash=(62, 54, 44))
    m.col("adobe", (80, 70, 58))
    m.col("parapet", (166, 147, 118))
    m.col("flat", (166, 147, 118), paint=True)
    P = b07_plan(m.frame())
    roofs = burnt_roofs(m, lambda c: max(c) > 0.3 and max(c) - min(c) < 0.2)
    for i, k in enumerate("ABCDEFG"):
        pts, top = P[k]
        burnt_block(m, pts, top, roofs, T, seed=i, step=1.0, maxn=24, cell=0.28, vstep=3.6)
    pts, top = P["alley"]
    m.prism("adobe", pts, 0.0, top)
    F = P["frame"]
    ya = P["A"][0][0][1]
    for c in (80, 91, 113):
        m.window("dark", F.X(c), ya, 1.6, 0.5 if c != 113 else 0.9, 0.35, sill="sill")
    b07_court(m, P, cloth=True)
    x0, x1, y0, y1 = P["court"]
    m.debris(T, rect(x0 + 0.2, x1 - 0.2, (y0 + y1) / 2, y1 - 0.2), 0.6, zfn=lambda x, y: 0.35, density=1.6,
             maxn=28, mix=BURNT, size=(0.2, 0.45), seed=8)
    # the market stall: its awning torn and sagging on the frame, the produce spilled in front
    ax0, ax1, yb, yf = P["awning"]
    ax1 = F.X(288)
    blue = m.pic_field(lambda c: c[2] > 0.33 and c[2] >= c[0] * 0.95 and c[1] >= c[0], sigma=3.0, bias=0.07)
    torn_awning(m, ax0, ax1, yb, yf, 2.2, 0.7, blue)
    spilled_crates(m, [(200, 150, 0.3, "red"), (214, 156, 1.1, "orange"), (229, 148, -0.4, "green"),
                       (242, 157, 0.7, "red")])
    m.scatter(keys, rect=(0, 0, 296, 191), max_area=160, skip=[(185, 95, 296, 175)])


@model("VerBuild07b")
def verbuild07b(m):
    b07_colours(m)
    keys = m.ruin_palette()
    T = ruin_tones(m, (132, 119, 98), (72, 64, 54), (84, 74, 62), stone=(110, 98, 80), ash=(38, 32, 27),
                   wood=(70, 46, 32))
    m.col("adobe", (74, 66, 56))
    m.col("parapet", (132, 119, 98))
    m.soot["parapet"] = ("wsoot", 0.4)
    P = b07_plan(m.frame(10, -10))
    for i, (k, z) in enumerate((("A", 2.2), ("B", 1.6), ("C", 1.3), ("D", 2.0), ("E", 2.4), ("F", 1.6),
                                ("G", 1.2))):
        pts, top = P[k]
        if kit._area(pts) < 0:
            pts = pts[::-1]
        prof = [(0, z), (0.2, z * 0.6), (0.35, z), (0.5, z * 0.7), (0.7, z * 1.05), (0.85, z * 0.5), (1, z)]
        sh = ring(pts, 0.15)
        m.bwall("adobe", sh + sh[:1], 0.3, prof, frac=True, jag=0.3, cap="parapet", step=0.8)
        m.debris(T, ring(pts, 0.3), z, lo=0.6, hi=0.8, density=3.2, maxn=60, cell=0.7, mix=HEAPED,
                 size=(0.2, 0.45), seed=i, beam_len=(0.5, 1.4))
    x0, x1, y0, y1 = P["court"]
    m.debris(T, rect(x0 + 0.2, x1 - 0.2, y0 + 0.4, y1 - 0.1), 1.0, lo=0.5, hi=0.8, density=3.0, maxn=70, cell=0.7,
             mix=BURNT, size=(0.2, 0.45), seed=9)
    m.bwall("adobe", [(x0, y0 + 0.15), (x1, y0 + 0.15)], 0.3, [(0, 1.2), (0.3, 0.6), (0.4, 0), (0.55, 0), (0.6, 0.9),
                                                             (1, 1.1)], frac=True, jag=0.3, cap="parapet", step=0.45)
    # the heap spills out past the south and east walls
    F = P["frame"]
    m.debris(T, [(F.X(20), F.Y(178)), (F.X(210), F.Y(178)), (F.X(290), F.Y(130)), (F.X(290), F.Y(100)),
                 (F.X(200), F.Y(165)), (F.X(20), F.Y(168))], 0.4, zfn=lambda x, y: 0.1, bed=False, density=1.0,
             maxn=50, mix=BURNT, size=(0.2, 0.4), seed=11)
    # the torn pale-blue awning scraps and its crates at the east end
    ax0, ax1, yb, yf = P["awning"]
    for i, (u, v, sx, sy, yaw) in enumerate(((0.2, 0.3, 1.2, 0.9, 0.3), (0.55, 0.5, 1.4, 1.0, -0.4),
                                             (0.85, 0.2, 1.0, 0.8, 0.9))):
        m.shard("awn_a" if i % 2 == 0 else "awn_b", ax0 + (ax1 - ax0) * u, yb + (yf - yb) * v, 0.6 + 0.2 * i, sx, sy,
                yaw, 0.35, -0.3, t=0.04, rag=0.45, seed=i)
    m.post("post", ax0 + 0.2, yf + 0.1, 0.0, 1.1, 0.12, lean=0.3)
    m.post("post", ax1 - 0.3, yf + 0.3, 0.0, 0.8, 0.12, lean=-0.4)
    spilled_crates(m, [(240, 140, 0.4, "red"), (262, 132, -0.6, "orange"), (275, 142, 1.0, "green")])
    m.scatter(keys, max_area=160)


# ---- VerBuild08 --------------------------------------------------------

def b08_plan(F):
    R = lambda c0, c1, r0, r1, z: rect(F.X(c0), F.X(c1), F.Y(r1, z), F.Y(r0, z))  # noqa: E731
    ys = F.Y(95, 1.6)
    p = dict(
        N=([F.P(8, 45, 2.2), F.P(72, 45, 2.2), F.P(72, 28, 2.2), F.P(86, 28, 2.2), F.P(86, 5, 2.2),
            F.P(8, 5, 2.2)], 2.2),
        N2=(R(86, 136, 2, 30, 2.6), 2.6),
        E=([(F.X(72), ys), (F.X(115), ys), (F.X(115), F.Y(105, 2.2)), (F.X(150), F.Y(105, 2.2)),
            (F.X(150), F.Y(28, 2.2)), (F.X(72), F.Y(28, 2.2))], 2.2),
        SW=(R(5, 55, 112, 150, 1.8), 1.8),
        S=(R(50, 115, 95, 185, 1.6), 1.6),
        SE=(R(125, 138, 112, 152, 1.2), 1.2),
        T=(rect(F.X(115), F.X(150), F.Y(160, 0.6), F.Y(105, 2.2)), 0.6),
        court=(F.X(10), F.X(72), F.Y(112, 1.8), F.Y(45, 2.2)),
        frame=F,
    )
    return p


def b08_blocks(m, P, keeps=None, profs=None, vigas="viga", vstep=1.05):
    keeps = keeps or {}
    profs = profs or {}
    for k in ("N", "N2", "E", "SW", "S", "SE"):
        pts, top = P[k]
        adobe(m, pts, top, keep=keeps.get(k), prof=profs.get(k), vigas=vigas, vstep=vstep)
    F = P["frame"]
    yf = P["S"][0][0][1]
    m.window("dark", F.X(86), yf, 0.0, 0.7, 1.05)
    for c in (62, 106):
        m.window("dark", F.X(c), yf, 0.7, 0.45, 0.35, sill="sill")


def b08_court(m, P, cloth=True):
    F = P["frame"]
    x0, x1, y0, y1 = P["court"]
    m.box("floor", x0, x1, y0, y1, 0.0, 0.03)
    m.wall("adobe", [(x0 - 0.1, y1), (x0 - 0.1, y0)], 0.3, 1.9, cap="parapet", cap_h=0.05, cap_over=0.02)
    if cloth:
        # rails running north and south with rugs over them
        for c, spans in ((24, ((58, 80), (86, 104))), (31, ((62, 76), (84, 108))), (44, ((56, 70), (78, 100)))):
            x = F.X(c)
            m.beam("rail", (x, y0 + 0.2, 1.0), (x, y1 - 0.2, 1.0), 0.07)
            for yy in (y0 + 0.25, y1 - 0.25):
                m.obox("rail", x, yy, 0.5, 0.1, 0.1, 1.0)
            for r0, r1 in spans:
                m.box("cloth" if c != 31 else "rug", x - 0.04, x + 0.04, F.Y(r1, 1.0), F.Y(r0, 1.0), 0.35, 1.02)


def b08_props(m, P, canopy=True):
    F = P["frame"]
    for c, r, rr in ((22, 13, 0.42), (38, 13, 0.42), (31, 23, 0.42), (58, 12, 0.34), (68, 12, 0.34), (78, 12, 0.34)):
        basket(m, "basket", F.X(c), F.Y(r, 2.5), z0=1.9, r=rr, rim="rim")
    for c, r, s, yaw in ((105, 52, 0.9, 0.35), (92, 71, 1.0, -0.3)):
        m.crate("bundle", F.X(c), F.Y(r, 2.6), s=s, h=0.75, yaw=yaw, z0=1.9)
    for c, r in ((131, 40), (139, 39), (134, 48), (141, 50)):
        jar(m, "jar", F.X(c), F.Y(r, 2.4), z0=1.9, r=0.3, h=0.6)
    if canopy:
        cx, cy = F.P(30, 126, 2.5)
        with m.rot(cx, cy, math.radians(12)):
            m.box("awn_a", cx - 1.0, cx + 1.0, cy - 0.85, cy + 0.85, 2.45, 2.5)
            for sx in (-0.9, 0.9):
                for sy in (-0.75, 0.75):
                    m.obox("post", cx + sx, cy + sy, 2.0, 0.08, 0.08, 0.95)


@model("VerBuild08")
def verbuild08(m):
    b07_colours(m)
    m.col("rug", (150, 70, 40))
    P = b08_plan(m.frame())
    b08_blocks(m, P)
    b08_court(m, P)
    b08_props(m, P)


@model("VerBuild08a")
def verbuild08a(m):
    b07_colours(m)
    m.col("rug", (150, 70, 40))
    keys = m.ruin_palette()
    T = ruin_tones(m, (149, 134, 112), (80, 72, 60), (100, 90, 76), stone=(130, 118, 98), ash=(56, 48, 40))
    m.col("adobe", (76, 68, 58))
    m.col("parapet", (149, 134, 112))
    m.col("flat", (149, 134, 112), paint=True)
    P = b08_plan(m.frame())
    # this roof is sooted warm grey, so brightness alone tells it from the char
    roofs = burnt_roofs(m, lambda c: max(c) > 0.32 and max(c) - min(c) < 0.22 and not m.roofish(c), bias=0.32)
    for i, k in enumerate(("N", "N2", "E", "SW", "S", "SE")):
        pts, top = P[k]
        burnt_block(m, pts, top, roofs, T, seed=i)
    F = P["frame"]
    yf = P["S"][0][0][1]
    m.window("dark", F.X(86), yf, 0.0, 0.7, 1.05)
    b08_court(m, P, cloth=True)
    m.scatter(keys, max_area=160)


@model("VerBuild08b")
def verbuild08b(m):
    b07_colours(m)
    keys = m.ruin_palette()
    T = ruin_tones(m, (130, 115, 94), (70, 62, 52), (82, 72, 60), stone=(106, 94, 78), ash=(38, 32, 27),
                   wood=(70, 46, 32))
    m.col("adobe", (72, 64, 54))
    m.col("parapet", (130, 115, 94))
    m.soot["parapet"] = ("wsoot", 0.4)
    P = b08_plan(m.frame(2, -5))
    for i, (k, z) in enumerate((("N", 1.4), ("N2", 1.6), ("E", 1.5), ("SW", 1.2), ("S", 1.1), ("SE", 0.9))):
        pts, top = P[k]
        if kit._area(pts) < 0:
            pts = pts[::-1]
        prof = [(0, z), (0.2, z * 0.6), (0.35, z), (0.5, z * 0.7), (0.7, z * 1.05), (0.85, z * 0.5), (1, z)]
        sh = ring(pts, 0.15)
        m.bwall("adobe", sh + sh[:1], 0.3, prof, frac=True, jag=0.3, cap="parapet", step=0.45)
        m.debris(T, ring(pts, 0.3), z, lo=0.55, hi=0.8, density=3.4, maxn=90, cell=0.55, mix=HEAPED,
                 size=(0.2, 0.45), seed=i, beam_len=(0.5, 1.4))
    x0, x1, y0, y1 = P["court"]
    m.debris(T, rect(x0 + 0.1, x1 - 0.1, y0 + 0.1, y1 - 0.1), 0.9, lo=0.5, hi=0.8, density=3.0, maxn=110, cell=0.5,
             mix=BURNT, size=(0.2, 0.45), seed=9)
    m.scatter(keys, max_area=160)
