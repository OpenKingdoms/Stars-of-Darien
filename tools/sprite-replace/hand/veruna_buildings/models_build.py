"""The big Veruna buildings (VerBuild01 to 08) and their ruins.

Positions are read off each sprite: m.X(col), m.Y(row, z) turn a drawn
point back into cells.
"""
import math

from kit import model
from models_compound import rect, ring, streaks, tangle


# ---- VerBuild01: a U of tiled hip roofs round a back courtyard ----------

def b01_frame(m, dx=0, dy=0):
    """VerBuild01's plan in its own sprite's pixels; dx, dy shift a ruin's
    sprite onto it."""
    X = lambda c: m.X(c + dx)  # noqa: E731
    Y = lambda r, z=0.0: m.Y(r + dy, z)  # noqa: E731
    f = dict(
        h=2.9, rise=1.9,
        x0=X(8), x1=X(233),
        yf=Y(215), yb=Y(118, 2.9) - 0.2,
        lw0=X(8), lw1=X(80), lwb=Y(4, 2.9 + 1.9) - 0.0,
        rw0=X(163), rw1=X(233), rwb=Y(38, 2.9 + 1.9),
        wall_a=(X(80), X(122), Y(58)), wall_b=(X(122), Y(58), Y(92)), wall_c=(X(122), X(163), Y(92)),
    )
    return f


def b01_colours(m):
    # two storeys under a tall roof: build it lower and deeper
    m.sturdy(0.85)
    m.col("wall", (74, 66, 57))
    m.col("roof", (115, 41, 24), paint=True)
    m.col("dark", (30, 24, 20))
    m.col("sill", (200, 196, 180))
    m.col("coping", (120, 50, 30))
    m.col("water", (95, 110, 120), rough=0.3)
    m.col("trough", (140, 132, 120))
    m.col("pot", (150, 140, 110))


def b01_intact(m, f, keep=None, walls=True, rafters=None, cell=0.4):
    h, rise = f["h"], f["rise"]
    core = None if keep else "wall"
    # front block, hipped at both ends
    m.roof("roof", f["x0"], f["x1"], f["yf"], f["yb"], h, rise, "x", ("hip", "hip"), core=core, keep=keep,
           rafters=rafters, cell=cell)
    # the wings run back to gable ends
    m.roof("roof", f["lw0"], f["lw1"], (f["yf"] + f["yb"]) / 2, f["lwb"], h, rise, "y", ("gable", "gable"),
           core=core, keep=keep, rafters=rafters, cell=cell)
    m.roof("roof", f["rw0"], f["rw1"], (f["yf"] + f["yb"]) / 2, f["rwb"], h, rise, "y", ("gable", "gable"),
           core=core, keep=keep, rafters=rafters, cell=cell)
    if walls:
        m.box("wall", f["x0"], f["x1"], f["yf"], f["yb"], 0, h)
        # the wings start behind the front block: coplanar faces would shadow each other
        m.box("wall", f["lw0"] + 0.01, f["lw1"], f["yb"] - 0.3, f["lwb"], 0, h - 0.01)
        m.box("wall", f["rw0"], f["rw1"] - 0.01, f["yb"] - 0.3, f["rwb"], 0, h - 0.01)


def b01_yard(m, f):
    xa0, xa1, ya = f["wall_a"]
    xb, yb0, yb1 = f["wall_b"]
    xc0, xc1, yc = f["wall_c"]
    m.wall("wall", [(xa0, ya), (xa1, ya)], 0.3, 2.5, cap="coping", cap_h=0.15, cap_over=0.08)
    m.wall("wall", [(xb, yb0), (xb, yb1)], 0.3, 2.0, cap="coping", cap_h=0.15, cap_over=0.08)
    m.wall("wall", [(xc0, yc), (xc1, yc)], 0.3, 1.6, cap="coping", cap_h=0.15, cap_over=0.08)
    # a dark timber gate in the low wall
    m.window("dark", (xc0 + xc1) / 2, yc - 0.15, 0.05, 1.8, 1.2)


def b01_windows(m, f):
    for c in (38, 88, 212):
        for zc in (1.9, 0.95):
            x = m.X(c)
            m.window("dark", x, f["yf"], zc - 0.18, 0.8, 0.36, sill="sill")


def b01_back(m):
    # a stone water trough and pots behind the right wing
    x0, x1, y = m.X(188), m.X(230), m.Y(44)
    m.box("trough", x0, x1, y, y + 1.0, 0, 0.45)
    m.box("water", x0 + 0.12, x1 - 0.12, y + 0.12, y + 0.88, 0.1, 0.47)
    for c, r in ((172, 48), (182, 45), (160, 60), (165, 68)):
        x, y = m.X(c), m.Y(r)
        m.lathe("pot", x, y, [(0.18, 0), (0.3, 0.2), (0.28, 0.45), (0.14, 0.6), (0.16, 0.68)], seg=10)


@model("VerBuild01")
def verbuild01(m):
    b01_colours(m)
    f = b01_frame(m)
    b01_intact(m, f)
    b01_yard(m, f)
    b01_windows(m, f)
    b01_back(m)


def b01_walls_path(f):
    """The U's outer walls, as named segments."""
    return {
        "west": [(f["lw0"], f["lwb"]), (f["lw0"], f["yf"])],
        "front": [(f["lw0"], f["yf"]), (f["x1"], f["yf"])],
        "east": [(f["x1"], f["yf"]), (f["x1"], f["rwb"])],
        "rback": [(f["rw1"], f["rwb"]), (f["rw0"], f["rwb"])],
        "rin": [(f["rw0"], f["rwb"]), (f["rw0"], f["yb"])],
        "fback": [(f["rw0"], f["yb"]), (f["lw1"], f["yb"])],
        "lin": [(f["lw1"], f["yb"]), (f["lw1"], f["lwb"])],
        "lback": [(f["lw1"], f["lwb"]), (f["lw0"], f["lwb"])],
    }


def _inset_frame(f, d):
    g = dict(f)
    for k in ("x0", "lw0", "rw0"):
        g[k] = f[k] + d
    for k in ("x1", "lw1", "rw1"):
        g[k] = f[k] - d
    g["yf"] = f["yf"] + d
    for k in ("yb", "lwb", "rwb"):
        g[k] = f[k] - d
    return g


@model("VerBuild01a")
def verbuild01a(m):
    b01_colours(m)
    keys = m.ruin_palette()
    # the drawn wall's sooted brown, a shade under it: the renderer lifts a face toward the camera
    m.col("wall", (58, 48, 39))
    f = b01_frame(m, dx=19, dy=1)
    m.beam_share = 0.7
    m.col("wtop", m.class_colours().get("plaster", (180, 170, 150)))
    keep = m.keep_class(frac=0.45, pred=m.roofish)
    b01_intact(m, f, keep=keep, walls=False, cell=0.44)
    h, rise = f["h"], f["rise"]
    t = 0.35
    w = _inset_frame(f, t / 2)
    P = b01_walls_path(w)
    full = [(0, h), (1, h)]
    for k in ("west", "east", "rback", "rin", "fback", "lin", "lback"):
        m.bwall("wall", P[k], t, full, frac=True, jag=0.08, cap="wtop")
    # the front wall stands to its eaves with a sooted top, a ragged dip at
    # its east end where the rubble spills over
    m.col("wtopf", (140, 123, 103))
    dip = [(0.86, h), (0.885, h - 0.6), (0.91, h - 1.15), (0.935, h - 0.8), (0.955, h - 1.0), (0.975, h - 0.3)]
    m.bwall("wall", P["front"], t, [(0, h)] + dip + [(1, h)], frac=True, jag=0.08, cap="wtopf")
    b01_windows_part(m, f, upto=0.5)
    # the collapsed half of the front block: charred rafters, plaster lumps
    # and tile shards heaped to half the wall height, against the east wall
    xm = f["x0"] + 0.5 * (f["x1"] - f["x0"])
    tile = m.class_colours().get("tile", (120, 50, 35))
    T = m.rubble_tones(char=(40, 32, 26), tile=tile, wood=(70, 48, 34), stone=(196, 184, 160), ash=(46, 29, 19))
    x1i, yfi = f["x1"] - t, f["yf"] + t
    heap = [(xm, yfi), (x1i, yfi), (x1i, f["yb"]), (xm, f["yb"])]
    nz = m.noise("heap01a", 0.8)

    def zheap(x, y):
        # half the wall height, banked up against the east wall
        return h * (0.42 + 0.12 * nz(x, y) + 0.2 * math.exp(-(x1i - x) / 0.7))
    m.debris(T, heap, h, zfn=zheap, density=2.0, maxn=44, cell=0.6, seed=1, size=(0.25, 0.55),
             mix=dict(char=0.25, stone=0.4, tile=0.3, wood=0.05), beam_len=(1.0, 2.2))
    tangle(m, ("r_char", "r_char", "r_char2"), heap, 1.25, 1.9, 15, seed=1, L=(1.8, 3.2), w=(0.14, 0.2))
    # the spill over the dip, down the face of the front wall
    s0, s1 = f["x0"] + 0.87 * (f["x1"] - f["x0"]), f["x0"] + 0.975 * (f["x1"] - f["x0"])
    yo = f["yf"] - 1.1
    m.debris(T, [(s0, yo), (s1, yo), (s1, yfi), (s0, yfi)], h,
             zfn=lambda x, y: max(0.05, 1.7 * (y - yo) / (yfi - yo)) * max(0.2, 1 - abs(2 * (x - s0) / (s1 - s0) - 1)),
             density=6.0, maxn=20, cell=0.4, seed=2, size=(0.2, 0.45), mix=dict(stone=0.5, tile=0.3, char=0.2),
             bed=False)
    # under the standing roofs, the charred timbers seen through the holes
    m.fallen(keys, [(f["x0"], f["yf"]), (xm, f["yf"]), (xm, f["yb"]), (f["x0"], f["yb"])], cell=0.65)
    m.fallen(keys, [(f["lw0"], f["yb"]), (f["lw1"], f["yb"]), (f["lw1"], f["lwb"]), (f["lw0"], f["lwb"])], cell=0.65)
    m.fallen(keys, [(f["rw0"], f["yb"]), (f["rw1"], f["yb"]), (f["rw1"], f["rwb"]), (f["rw0"], f["rwb"])], cell=0.65)
    # the yard walls: the coped jog still stands, the rest is broken
    xa0, xa1, ya = f["wall_a"]
    xb, yb0, yb1 = f["wall_b"]
    xc0, xc1, yc = f["wall_c"]
    m.bwall("wall", [(xa0, ya), (xa1, ya)], 0.3, [(0, 2.2), (0.3, 1.2), (0.6, 1.8), (1, 1.0)], frac=True)
    m.wall("wall", [(xb, yb0), (xb, yb1)], 0.3, 2.0, cap="coping", cap_h=0.15, cap_over=0.08)
    m.bwall("wall", [(xc0, yc), (xc1, yc)], 0.3, [(0, 1.0), (0.4, 0.5), (0.5, 0), (0.8, 0), (0.85, 0.6),
                                                  (1, 0.9)], frac=True)
    b01_back(m)
    m.scatter(keys, max_area=150)


def b01_windows_part(m, f, upto=1.0):
    for c in (38, 88, 212):
        x = m.X(c) + (f["x0"] - m.X(8))
        if (x - f["x0"]) / (f["x1"] - f["x0"]) > upto:
            continue
        for zc in (1.9, 0.95):
            m.window("dark", x, f["yf"], zc - 0.18, 0.8, 0.36, sill="sill")


@model("VerBuild01b")
def verbuild01b(m):
    b01_colours(m)
    keys = m.ruin_palette()
    m.col("wall", (62, 53, 42))
    m.col("wtop", (128, 116, 98))
    m.col("wsoot", (70, 60, 48))
    m.soot["wtop"] = ("wsoot", 0.5)
    f = b01_frame(m, dx=19, dy=-20)
    t = 0.35
    w = _inset_frame(f, t / 2)
    P = b01_walls_path(w)
    # the partitions between the wings and the centre run on to the front wall
    P["lpart"] = [(w["lw1"], w["yf"]), (w["lw1"], w["yb"])]
    P["rpart"] = [(w["rw0"], w["yf"]), (w["rw0"], w["yb"])]
    low = 1.3
    prof = {
        "west": [(0, 1.6), (0.3, 1.2), (0.6, 1.5), (1, 1.3)],
        "front": [(0, 1.4), (0.2, 1.2), (0.4, 1.0), (0.6, 1.3), (0.8, 1.1), (1, 1.5)],
        "east": [(0, 1.5), (0.5, 1.2), (1, 1.6)],
        "rback": [(0, 1.7), (0.5, 1.4), (1, 1.6)],
        "rin": [(0, 1.5), (0.4, 1.2), (1, low)],
        "fback": [(0, 1.0), (0.3, 0.8), (0.4, 0), (0.7, 0), (0.75, 0.9), (1, 1.1)],
        "lin": [(0, 1.3), (0.5, 1.5), (1, 1.7)],
        "lback": [(0, 1.8), (0.5, 1.5), (1, 1.9)],
        "lpart": [(0, 1.2), (0.4, 1.4), (0.7, 1.0), (1, 1.3)],
        "rpart": [(0, 1.3), (0.5, 1.1), (1, 1.4)],
    }
    for k, pr in prof.items():
        m.bwall("wall", P[k], t, pr, frac=True, jag=0.25, cap="wtop")
    # three burnt-out rooms heaped with their fallen floors and roofs
    T = m.rubble_tones(stone=(108, 93, 74), ash=(27, 22, 17), wood=(88, 52, 34))
    d = t
    rooms = (
        [(f["lw0"] + d, f["yf"] + d), (f["lw1"] - d, f["yf"] + d), (f["lw1"] - d, f["lwb"] - d),
         (f["lw0"] + d, f["lwb"] - d)],
        [(f["lw1"] + d, f["yf"] + d), (f["rw0"] - d, f["yf"] + d), (f["rw0"] - d, f["yb"] - d),
         (f["lw1"] + d, f["yb"] - d)],
        [(f["rw0"] + d, f["yf"] + d), (f["rw1"] - d, f["yf"] + d), (f["rw1"] - d, f["rwb"] - d),
         (f["rw0"] + d, f["rwb"] - d)])
    for i, pts in enumerate(rooms):
        m.debris(T, pts, 1.45, lo=0.3, hi=0.6, seed=i, density=2.6, cell=0.6, maxn=400)
    xa0, xa1, ya = f["wall_a"]
    xb, yb0, yb1 = f["wall_b"]
    m.bwall("wall", [(xa0, ya), (xa1, ya), (xb, yb1)], 0.3, [(0, 1.3), (0.5, 1.0), (0.7, 1.4), (1, 0.6)],
            frac=True, cap="wtop")
    m.scatter(keys, max_area=260)


# ---- VerBuild02 and 04: round halls with a low cone roof in a walled yard --

def hall_colours(m, wall_rect, drum_rect):
    m.sturdy(0.9)
    m.col("roof", (120, 50, 30), paint=True)
    # the yard walls' faces and their pale tops, as drawn
    m.col("yard", (146, 134, 114))
    m.col("yardtop", (222, 212, 186))
    m.col("drum", m.sample(*drum_rect))
    m.col("dark", (22, 18, 16))
    m.col("wood", (96, 66, 44))
    m.col("band", (60, 50, 40))


def round_hall(m, H, keep=None, keys=None, broken=None):
    """H: cx, cy, r, ry, ze, rise, hole, apex, drum. A broken hall has a
    hollow drum (auto-read when broken) and its fallen roof inside."""
    cx, cy, ze = H["cx"], H["cy"], H["ze"]
    if keep is None:
        m.cyl("drum", cx, cy, 0, H["drum"], ze + 0.05, seg=28)
        ax, ay = H["apex"]
        m.cyl("dark", ax, ay, ze + 0.05, H["hole"] + 0.15, 0.06, seg=12, smooth=False)
    else:
        ring = m.circle(cx, cy, H["drum"] - 0.15, n=28)
        if broken:
            m.auto_wall("drum", ring + ring[:1], 0.3, ze, cap="drumtop", hidden=broken)
        else:
            m.wall("drum", ring, 0.3, ze, closed=True)
        m.fallen(keys, m.circle(cx, cy, H["drum"] - 0.2, n=20), dz=0.8)
    m.cone("roof", cx, cy, H["r"], ze, H["rise"], hole=H["hole"], ry=H.get("ry"), apex=H["apex"], keep=keep,
           seg=36, rings=6 if keep else 1)
    # the cone logs no roof_z: give fallen() the cone's height
    m.roof_log.append(("cone", cx, cy, H["r"], ze, H["rise"]))


def gable_wing(m, x0, x1, y0, y1, ze, rise, axis, ends, wall="drum", keep=None):
    if keep is None:
        m.box(wall, x0, x1, y0, y1, 0, ze)
    else:
        m.wall(wall, [(x0, y0), (x1, y0), (x1, y1), (x0, y1)], 0.3, ze, closed=True)
    m.roof("roof", x0, x1, y0, y1, ze, rise, axis, ends, over=0.15, core=None if keep else wall, keep=keep)


def b02_plan(m, F):
    H = dict(cx=F.X(143), cy=-0.25 + (F.Y(0) - m.Y(0)), r=3.3, ry=3.05, ze=1.75, rise=0.8, hole=0.45,
             drum=3.0)
    H["apex"] = (F.X(148), F.Y(76, H["ze"] + H["rise"]))
    wallpts = [F.P(128, 141, 1.1), F.P(30, 150, 1.1), F.P(4, 95, 1.1), F.P(52, 12, 1.1), F.P(198, 5, 1.1),
               F.P(231, 130, 1.1), F.P(165, 137, 1.1)]
    return H, wallpts


def b02_wings(m, F, H, keep=None):
    # the entrance wing to the south, gable end out
    gable_wing(m, F.X(129), F.X(165), F.Y(178), H["cy"] - 1.5, 1.35, 0.9, "y", ("gable", "gable"), keep=keep)
    m.window("dark", (F.X(129) + F.X(165)) / 2, F.Y(178), 0.0, 0.8, 1.0)
    # a small wing out to the north-west
    with m.rot(H["cx"], H["cy"], math.radians(150)):
        gable_wing(m, H["cx"] + 2.0, H["cx"] + 4.0, H["cy"] - 0.8, H["cy"] + 0.8, 1.4, 0.7, "x",
                   ("hip", "gable"), keep=keep)


def b02_props(m, F):
    for c, r in ((104, 170), (112, 172), (121, 168)):
        m.barrel("wood", F.X(c), F.Y(r), 0.28, 0.65, band="band")
    with m.rot(F.X(184), F.Y(166), math.radians(-60)):
        m.cyl("wood", F.X(184), F.Y(166), 0.3, 0.3, 0.75, seg=10, rot=(0, math.pi / 2, 0))


@model("VerBuild02")
def verbuild02(m):
    hall_colours(m, (40, 100, 50, 140), (90, 100, 100, 130))
    F = m.frame()
    H, wallpts = b02_plan(m, F)
    round_hall(m, H)
    b02_wings(m, F, H)
    m.wall("yard", wallpts, 0.35, 1.1, cap="yardtop", cap_h=0.05, cap_over=0.01)
    b02_props(m, F)


def ruin_hall_colours(m, yard, yardtop, soot=None):
    m.sturdy(0.9)
    m.paint_gain = 1.15
    m.col("yard", yard)
    m.col("yardtop", yardtop)
    if soot:
        m.col("yardsoot", soot)
        m.soot["yardtop"] = ("yardsoot", 0.35)
    m.col("char", (46, 37, 30))
    m.col("drumtop", (96, 88, 76))
    m.col("tile_a", (108, 46, 32))
    m.col("tile_b", (84, 38, 28))


def hall_damaged(m, H, field, keys, T):
    """The hall standing, its cone holed where the drawing shows burns:
    bare rafters over the holes, the fallen roof and timbers under them."""
    cx, cy, ze = H["cx"], H["cy"], H["ze"]
    m.wall("drum", m.circle(cx, cy, H["drum"] - 0.15, n=32), 0.3, ze, closed=True)
    r, ry = H["r"], H.get("ry") or H["r"]
    burnt = field

    def field(x, y, z=0.0):
        # the eave ring holds: the holes open inside it
        d = math.hypot((x - cx) / r, (y - cy) / ry)
        return burnt(x, y, z) + 0.35 * min(1.0, max(0.0, (d - 0.8) / 0.12))
    kw = dict(hole=H["hole"], ry=H.get("ry"), apex=H["apex"])
    m.cut_cone("roof", cx, cy, H["r"], ze, H["rise"], field=field, seg=48, rings=12, **kw)
    m.cone_rafters("char", cx, cy, H["r"], ze, H["rise"], field, n=14, w=0.11, **kw)
    inner = m.circle(cx, cy, H["drum"] - 0.3, n=24)
    # rubble banked against the foot of the drum
    R = H["drum"]
    m.debris(T, m.circle(cx, cy, R + 0.7, n=28), 0.6, zfn=lambda x, y: 0.55 * max(0.0, 1 - (math.hypot(x - cx, y - cy)
                                                                                          - R) / 0.7),
             where=lambda x, y: math.hypot(x - cx, y - cy) > R - 0.05, density=1.6, maxn=70, cell=0.35,
             mix=dict(stone=0.55, char=0.25, wood=0.1, tile=0.1), size=(0.2, 0.45), seed=7)
    m.fallen(keys, inner, dz=0.75, cell=0.4)
    zr = lambda x, y: m.roof_z(x, y) or ze  # noqa: E731
    m.debris(T, inner, 1.0, zfn=lambda x, y: max(0.2, zr(x, y) - 0.8), bed=False, density=3.0, maxn=90,
             where=lambda x, y: field(x, y, zr(x, y)) <= 0.04, beam_len=(0.6, 1.5), size=(0.2, 0.45))


def wing_damaged(m, x0, x1, y0, y1, ze, rise, axis, field, keys, wall="drum"):
    """A gabled wing with its roof holed as drawn, rafters bare over the holes."""
    m.wall(wall, ring(rect(x0, x1, y0, y1), 0.15), 0.3, ze, closed=True)
    m.gables(wall, x0, x1, y0, y1, ze, rise, axis, t=0.25)
    m.cut_roof("roof", x0, x1, y0, y1, ze, rise, axis, ("gable", "gable"), over=0.15, field=field,
               rafters="char", spacing=0.45, rafter_w=0.11)
    m.fallen(keys, ring(rect(x0, x1, y0, y1), 0.3), dz=0.6, cell=0.35)


# a burnt heap: more charred timber than the stone of a wall's fall
HEAP = dict(char=0.45, stone=0.2, wood=0.2, tile=0.15)


def hall_razed(m, H, T, prof, top, spill=0.5, seed=0, mix=None, density=3.2):
    """The drum burnt down to a low broken ring round a heap of its roof."""
    cx, cy = H["cx"], H["cy"]
    R = H["drum"] - 0.15
    rim = m.circle(cx, cy, R, n=30)
    m.bwall("drum", rim + rim[:1], 0.35, prof, frac=True, jag=0.18, cap="drumtop")
    nz = m.noise("heap%d" % seed, 0.9)

    def zfn(x, y):
        d = math.hypot(x - cx, y - cy) / R
        return max(0.0, top * (0.3 + 0.7 * max(0.0, 1 - d * d)) * (1 + 0.3 * nz(x, y)))
    m.debris(T, m.circle(cx, cy, R - 0.15, n=26), top, zfn=zfn, density=density, maxn=330, cell=0.4,
             beam_len=(0.8, 2.0), seed=seed, mix=mix or HEAP)
    # the heap spills out over the broken ring
    m.debris(T, m.circle(cx, cy, R + spill, n=26), top, zfn=lambda x, y: 0.12, density=0.6, maxn=60, bed=False,
             where=lambda x, y: math.hypot(x - cx, y - cy) > R, seed=seed + 1, size=(0.2, 0.45))


def wing_razed(m, x0, x1, y0, y1, h, slabs, T, seed=0):
    """A wing down to ragged stubs full of rubble, slabs of its tiled roof
    lying tilted over them: slabs are (x, y, z, sx, sy, yaw, pitch, roll)."""
    box = ring(rect(x0, x1, y0, y1), 0.15)
    m.bwall("drum", box + box[:1], 0.28, [(0, h), (0.2, h * 0.5), (0.4, h), (0.6, h * 0.4), (0.8, h * 0.9),
                                           (1, h)], frac=True, jag=0.2, cap="drumtop")
    m.debris(T, ring(rect(x0, x1, y0, y1), 0.3), h, lo=0.5, hi=0.8, density=3.0, maxn=60, cell=0.35, seed=seed,
             mix=HEAP)
    for i, (x, y, z, sx, sy, yaw, pitch, roll) in enumerate(slabs):
        m.tile_slab(("tile_a", "tile_b"), x, y, z, sx, sy, yaw, pitch, roll, seed=seed * 10 + i)


@model("VerBuild02a")
def verbuild02a(m):
    hall_colours(m, (40, 100, 50, 140), (90, 100, 100, 130))
    ruin_hall_colours(m, (150, 139, 120), (212, 202, 178))
    keys = m.ruin_palette()
    T = m.rubble_tones(char=(44, 35, 28), tile=(112, 50, 34), wood=(84, 56, 38), stone=(120, 108, 92),
                       ash=(52, 44, 37))
    F = m.frame(0, 4)
    H, wallpts = b02_plan(m, F)
    roof = m.pic_field(m.roofish, sigma=4.0, bias=0.27)
    hall_damaged(m, H, roof, keys, T)
    wing_damaged(m, F.X(129), F.X(165), F.Y(178), H["cy"] - 1.5, 1.35, 0.9, "y", roof, keys)
    m.window("dark", (F.X(129) + F.X(165)) / 2, F.Y(178), 0.0, 0.8, 1.0)
    with m.rot(H["cx"], H["cy"], math.radians(150)):
        gable_wing(m, H["cx"] + 2.0, H["cx"] + 4.0, H["cy"] - 0.8, H["cy"] + 0.8, 1.4, 0.7, "x", ("hip", "gable"))
    m.auto_wall("yard", wallpts, 0.35, 1.1, cap="yardtop")
    b02_props(m, F)
    m.scatter(keys, max_area=200)


@model("VerBuild02b")
def verbuild02b(m):
    hall_colours(m, (40, 100, 50, 140), (90, 100, 100, 130))
    ruin_hall_colours(m, (108, 99, 85), (150, 139, 120), soot=(104, 96, 82))
    # the drum ring's stones sooted dark, the heap a dark bed of char
    m.col("drum", (60, 52, 44))
    m.col("drumtop", (70, 61, 50))
    keys = m.ruin_palette()
    T = m.rubble_tones(char=(30, 24, 20), tile=(116, 48, 32), wood=(74, 48, 34), stone=(72, 64, 54),
                       ash=(34, 24, 18))
    F = m.frame(0, 4)
    H, wallpts = b02_plan(m, F)
    hall_razed(m, H, T, [(0, 0.5), (0.15, 0.8), (0.3, 0.35), (0.45, 0.7), (0.6, 0.3), (0.75, 0.8), (0.9, 0.45),
                         (1, 0.5)], 1.15, mix=dict(char=0.55, tile=0.25, wood=0.12, stone=0.08), density=4.0)
    # charred beams heaped across the top of it, as on 04b
    R = H["drum"] - 0.3
    tangle(m, ("r_char", "r_char", "r_char2", "r_wood"), m.circle(H["cx"], H["cy"], R, n=20),
           lambda x, y: 0.5 + 0.5 * max(0.0, 1 - math.hypot(x - H["cx"], y - H["cy"]) / R), 1.1, 20, seed=2,
           L=(1.2, 2.4))
    # the porch: stubs under its fallen roof, slumped in two slabs
    px0, px1, py0, py1 = F.X(129), F.X(165), F.Y(178), H["cy"] - 1.5
    pc = (px0 + px1) / 2
    wing_razed(m, px0, px1, py0, py1, 0.7, [
        (pc - 0.15, py0 + 0.9, 0.85, 2.2, 1.9, 0.1, 0.35, 0.1),
        (pc + 0.1, py0 + 2.5, 0.95, 2.1, 1.8, -0.15, -0.25, -0.12)], T, seed=3)
    # the north-west wing's roof lies across its stubs
    with m.rot(H["cx"], H["cy"], math.radians(150)):
        wx0, wx1, wy0, wy1 = H["cx"] + 2.0, H["cx"] + 4.0, H["cy"] - 0.8, H["cy"] + 0.8
        wing_razed(m, wx0, wx1, wy0, wy1, 0.6, [((wx0 + wx1) / 2, H["cy"] - 0.2, 0.85, 2.3, 1.5, 0.2, 0.3, 0.15)],
                   T, seed=4)
    # the yard wall stands in long runs: the north and east almost whole,
    # the west in three long pieces
    runs = [
        [(0, 1.0), (0.45, 0.9), (0.8, 1.0), (0.84, 0.6), (0.86, 0), (1, 0)],
        [(0, 0), (0.12, 0), (0.15, 0.7), (0.4, 0.9), (0.44, 0), (0.56, 0), (0.6, 0.8), (1, 1.0)],
        [(0, 0.9), (0.18, 1.0), (0.22, 0), (0.55, 0), (0.6, 0.8), (1, 1.0)],
        [(0, 1.0), (0.35, 0.8), (0.5, 1.0), (1, 1.05)],
        [(0, 1.05), (0.5, 1.0), (0.53, 0), (0.6, 0), (0.63, 0.8), (0.76, 0.9), (0.79, 0), (0.83, 0), (0.86, 0.9),
         (1, 1.0)],
        [(0, 1.0), (0.6, 0.9), (1, 0.8)],
    ]
    for i, prof in enumerate(runs):
        m.bwall("yard", [wallpts[i], wallpts[i + 1]], 0.35, prof, frac=True, jag=0.2, cap="yardtop")
    m.scatter(keys, max_area=200)


def b04_plan(m, F):
    H = dict(cx=F.X(109), cy=F.Y(145) - 2.95 + 0.0, r=3.2, ze=1.74, rise=1.5, hole=0.5, drum=2.95)
    H["cy"] = F.Y(145) + 2.95
    H["apex"] = (F.X(110), F.Y(72, H["ze"] + H["rise"]))
    wallpts = [F.P(152, 122, 1.0), F.P(130, 187, 1.0), F.P(60, 187, 1.0), F.P(2, 105, 1.0), F.P(50, 3, 1.0),
               F.P(179, 39, 1.0), F.P(167, 88, 1.0)]
    return H, wallpts


def b04_wings(m, F, H, keep=None):
    gable_wing(m, H["cx"] + 1.8, F.X(177), F.Y(125, 1.3), F.Y(95, 1.3), 1.3, 0.7, "x", ("gable", "gable"),
               keep=keep)


@model("VerBuild04")
def verbuild04(m):
    hall_colours(m, (10, 110, 20, 150), (80, 130, 120, 142))
    F = m.frame()
    H, wallpts = b04_plan(m, F)
    round_hall(m, H)
    b04_wings(m, F, H)
    m.wall("yard", wallpts, 0.35, 1.0, cap="yardtop", cap_h=0.05, cap_over=0.01)


@model("VerBuild04a")
def verbuild04a(m):
    hall_colours(m, (10, 110, 20, 150), (80, 130, 120, 142))
    ruin_hall_colours(m, (150, 139, 120), (205, 192, 166))
    keys = m.ruin_palette()
    T = m.rubble_tones(char=(42, 33, 27), tile=(104, 50, 36), wood=(80, 54, 38), stone=(118, 106, 90),
                       ash=(50, 42, 35))
    F = m.frame(0, 0)
    H, wallpts = b04_plan(m, F)
    roof = m.pic_field(m.roofish, sigma=4.0, bias=0.25)
    hall_damaged(m, H, roof, keys, T)
    wing_damaged(m, H["cx"] + 1.8, F.X(177), F.Y(125, 1.3), F.Y(95, 1.3), 1.3, 0.7, "x", roof, keys)
    m.auto_wall("yard", wallpts, 0.35, 1.0, cap="yardtop")
    # a slab of the annex roof slid off to the south
    m.tile_slab(("tile_a", "tile_b"), F.X(141), F.Y(152, 0.25), 0.25, 1.3, 1.0, 0.3, 0.3, 0.2)
    m.scatter(keys, max_area=200)


@model("VerBuild04b")
def verbuild04b(m):
    hall_colours(m, (10, 110, 20, 150), (80, 130, 120, 142))
    ruin_hall_colours(m, (118, 106, 90), (190, 175, 151), soot=(120, 110, 94))
    m.col("drum", (80, 70, 60))
    keys = m.ruin_palette()
    T = m.rubble_tones(char=(36, 29, 24), tile=(108, 46, 32), wood=(80, 52, 36), stone=(98, 88, 74),
                       ash=(40, 34, 28))
    F = m.frame(0, -8)
    H, wallpts = b04_plan(m, F)
    hall_razed(m, H, T, [(0, 0.6), (0.12, 0.35), (0.25, 0.8), (0.4, 0.5), (0.55, 0.3), (0.7, 0.75), (0.85, 0.4),
                         (1, 0.6)], 1.2, seed=5)
    x0, x1, y0, y1 = H["cx"] + 1.8, F.X(177), F.Y(125, 1.3), F.Y(95, 1.3)
    xc, yc = (x0 + x1) / 2, (y0 + y1) / 2
    wing_razed(m, x0, x1, y0, y1, 0.55, [(xc - 0.2, yc - 0.3, 0.55, 1.5, 1.2, 0.2, 0.3, 0.1),
                                         (xc + 0.4, yc + 0.5, 0.6, 1.3, 1.1, -0.3, -0.3, 0.2)], T, seed=6)
    m.tile_slab(("tile_a", "tile_b"), F.X(141), F.Y(152, 0.25), 0.25, 1.3, 1.0, 0.3, 0.3, 0.2)
    m.auto_wall("yard", wallpts, 0.35, 1.0, cap="yardtop")
    m.scatter(keys, max_area=200)
