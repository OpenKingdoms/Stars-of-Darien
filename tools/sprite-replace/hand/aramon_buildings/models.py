"""The Aramon buildings, one function per feature, read off its sprite.

Numbers are cells in the model's frame unless they are sprite pixels
(col, row), which kit.px turns into plan points. Colours are the drawn
colours sampled with sample.py.
"""
import math

import kit
from kit import Mat, px

MODELS = {}


def model(*names):
    def reg(fn):
        for n in names:
            MODELS[n] = fn
        return fn
    return reg


# ---------------------------------------------------------------- shared parts

def thatch_mats(name, base="#7d7422", dark="#4f4a14", light="#a39a36", seed=1):
    return Mat(name + "_thatch", tex=kit.tex_thatch(name + "_thatch", base, dark, light, seed=seed,
                                                     speck="#b07a2a"), uv=1.6)


def stone_wall_mat(name, base="#7c6e5a", seed=4):
    return Mat(name + "_wall", tex=kit.tex_stone(name + "_wall", base, rows=6, seed=seed,
                                                  mortar=kit.shade(base, 0.6)), uv=2.0)


def cottage(B, m, L, W, H, R, over=0.4, over_end=0.4, hip=0.4, T=0.35, kind="hip"):
    """Stone walls under a thatched roof, in the current placement."""
    B.box(m["wall"], 0, 0, 0, L, W, H)
    if kind == "gable":
        for e in (-1, 1):
            kit.gable_wall(B, m["wall"], e * (L / 2 - 0.1), W, H, R - 0.2, t=0.2)
    return kit.roof(B, m["thatch"], L, W, H, R, kind=kind, over=over, over_end=over_end, T=T, hip=hip)


# ---------------------------------------------------------------- thatched cottages

def P(**k):
    """A house body as fit.py fits it: centre, yaw, walls L by W by H, the
    ridge R above the walls, overhangs, hip (below 0 a gable), thickness."""
    d = {"cx": 0.0, "cy": 0.0, "yaw": 0.0, "L": 4.0, "W": 3.0, "H": 2.0, "R": 1.5, "over": 0.4,
         "over_end": 0.4, "hip": 0.3, "T": 0.35}
    d.update(k)
    return d


def roof_z(p, x, y):
    """Height of the roof's top over a plan point (inside the eaves)."""
    c, s = math.cos(math.radians(-p["yaw"])), math.sin(math.radians(-p["yaw"]))
    dx, dy = x - p["cx"], y - p["cy"]
    u, v = dx * c - dy * s, dx * s + dy * c
    k = p["R"] / (p["W"] / 2)
    z = p["H"] + p["R"] - abs(v) * k
    if p["hip"] >= 0:
        rl = max(0.0, p["L"] / 2 - p["hip"])
        run = p["L"] / 2 + p["over_end"] - rl
        ke = (p["R"] + p["over"] * k) / max(run, 1e-3)
        z = min(z, p["H"] + p["R"] - max(0.0, abs(u) - rl) * ke)
    return z


def through_roof(r, p, col, row, above=0.7):
    """Where a chimney whose top the sprite draws at (col, row) stands, and
    how high its top is: above the roof it comes through."""
    zt = p["H"] + p["R"]
    for _ in range(20):
        x, y = px(r, col, row, zt)
        zt = roof_z(p, x, y) + above
    return x, y, zt


def thatch_body(B, m, p, bevel=0.22, ends=None, half=0.5):
    """Walls under a thatched roof, placed and turned as p says; ends
    (west, east) as kit.roof takes them."""
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        kind = "gable" if p["hip"] < 0 else "hip"
        ends = ends or ((kind, kind))
        B.box(m["wall"], 0, 0, 0, p["L"], p["W"], p["H"])
        for e, t in zip((-1, 1), ends):
            if t != "hip":
                kit.gable_wall(B, m["wall"], e * (p["L"] / 2 - 0.1), p["W"], p["H"], p["R"] - 0.1, t=0.2,
                               top=p["R"] * half - 0.05 if t == "half" else None)
        kit.roof(B, m["thatch"], p["L"], p["W"], p["H"], p["R"], kind=kind, over=p["over"],
                 over_end=p["over_end"], T=p["T"], hip=max(0.0, p["hip"]), ends=ends, half=half)
    if bevel:
        B.bevels[m["thatch"].name] = (bevel, 3)


def cottage_mats(n, thatch="#7b7021", wall="#857662", seed=1):
    return {"thatch": thatch_mats(n, base=thatch, dark=kit.shade(thatch, 0.55), light=kit.shade(thatch, 1.3), seed=seed),
            "wall": stone_wall_mat(n, wall),
            "dark": Mat(n + "_dark", "#181008"), "timber": Mat(n + "_timber", "#2e2418"),
            "stone": Mat(n + "_chim", tex=kit.tex_stone(n + "_chim", "#5e5e54", rows=7, seed=9), uv=1.5),
            "wood": Mat(n + "_wood", "#6c5a3c"), "hay": Mat(n + "_hay", "#9a8a3a")}


def on_wall(B, p, side, s, fn):
    """Build fn() on one face of the body: side S/N (the long faces, -v/+v)
    or W/E (the ends, -u/+u), s along the face from its middle."""
    L, W = p["L"], p["W"]
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        fn(L, W)


# the cottages are broader and lower than the pixel fit (walls about 15
# percent wider, ridges about 12 percent lower), so they sit squat in 3D
HUT08 = P(cx=-0.3, cy=0.16, L=5.5, W=3.7, H=2.2, R=1.9, hip=0.2)
HUT04 = P(cx=-0.225, cy=-0.3, yaw=72.6, L=5.8, W=4.9, H=1.8, R=1.95, hip=0.3)
HUT06 = P(cx=0.0, cy=-0.5, yaw=101.0, L=5.6, W=4.9, H=2.0, R=2.3, hip=0.28)


@model("AraHut08")
def arahut08(B, r):
    n = r["name"]
    m = cottage_mats(n, thatch="#7d7422")
    p = HUT08
    thatch_body(B, m, p)
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        # the door left of middle (cols 38-47) and a small window
        dx = px(r, 42.5, 0)[0] - p["cx"]
        kit.opening(B, m["dark"], m["timber"], "S", dx, 0.58, 0.0, 1.6, p["L"], p["W"])
        kit.opening(B, m["dark"], m["timber"], "S", dx + 1.9, 0.5, 0.9, 0.55, p["L"], p["W"])
        kit.opening(B, m["dark"], m["timber"], "S", -2.1, 0.45, 0.9, 0.55, p["L"], p["W"])
        # a stone chimney stack against the back wall, cols 70-77
        chx = px(r, 73.5, 0)[0] - p["cx"]
        kit.chimney(B, m["stone"], chx, p["W"] / 2 + 0.2, 0.0, 3.8, 0.72, 0.68)
    # two cart wheels lying by the east end, rims seen from above
    for c, rw, tilt in ((102, 65, 18), (102, 79, 8)):
        x, y = px(r, c, rw, 0.2)
        with B.at(x, y, 0.22, yaw=90, roll=tilt):
            kit.wheel(B, m["timber"], m["timber"], 0, 0, 0, 0.42, axis=(0, 0, 1), spokes=6)
    # a small bench with a basket of produce in front, cols 61-78
    bx, by = px(r, 69.5, 93)
    by += 0.25
    for e in (-1, 1):
        for f in (-1, 1):
            B.box(m["timber"], bx + e * 0.45, by + f * 0.12, 0, 0.1, 0.1, 0.45)
    B.box(m["wood"], bx, by, 0.42, 1.05, 0.4, 0.08)
    B.box(m["hay"], bx, by, 0.49, 0.9, 0.28, 0.12)


@model("AraHut04")
def arahut04(B, r):
    n = r["name"]
    m = cottage_mats(n, thatch="#7b7021", wall="#6a5e48", seed=3)
    p = HUT04
    thatch_body(B, m, p, ends=("half", "hip"), half=0.45)
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        # the front end faces south-south-west (local -u): a door on the
        # left and a small window, as drawn at cols 12-30 and 44-50
        L, W = p["L"], p["W"]
        with B.at(-L / 2, 0, yaw=-90):
            kit.opening(B, m["dark"], m["timber"], "S", -1.25, 0.8, 0.0, 1.5, W, 0.0)
            kit.opening(B, m["dark"], m["timber"], "S", 0.6, 0.5, 0.8, 0.5, W, 0.0)
    x, y, zt = through_roof(r, p, 37, 14, above=0.7)
    kit.chimney(B, m["stone"], x, y, p["H"], zt, 0.6, 0.6)


@model("AraHut06")
def arahut06(B, r):
    n = r["name"]
    m = cottage_mats(n, thatch="#817221", wall="#5a5040", seed=5)
    p = HUT06
    thatch_body(B, m, p, ends=("half", "hip"), half=0.45)
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        # the front end faces south-south-east (local -u): a door and two
        # windows under the thatch, cols 38-48, 57-67, 72-82
        L, W = p["L"], p["W"]
        with B.at(-L / 2, 0, yaw=-90):
            kit.opening(B, m["dark"], m["timber"], "S", -1.2, 0.55, 0.9, 0.45, W, 0.0)
            kit.opening(B, m["dark"], m["timber"], "S", 0.1, 0.7, 0.0, 1.5, W, 0.0)
            kit.opening(B, m["dark"], m["timber"], "S", 1.3, 0.55, 0.9, 0.45, W, 0.0)
    x, y, zt = through_roof(r, p, 71, 60, above=0.65)
    kit.chimney(B, m["stone"], x, y, p["H"], zt, 0.6, 0.6)


# ---------------------------------------------------------------- cottage ruins

def ruin_mats(n, wall="#4a4234", top="#7e705a", heap="#3a3020", seed=11):
    return {"wall": Mat(n + "_rwall", tex=kit.tex_stone(n + "_rwall", wall, rows=6, seed=seed,
                                                         mortar=kit.shade(wall, 0.55)), uv=2.0),
            "top": Mat(n + "_rtop", tex=kit.tex_mottle(n + "_rtop", top, var=0.2, seed=seed + 1), uv=1.0),
            "heap": Mat(n + "_heap", tex=kit.tex_rubble(n + "_heap", heap, seed=seed + 2), uv=1.5),
            "beam": Mat(n + "_beam", tex=kit.tex_planks(n + "_beam", "#3a2a1c", boards=2, seed=seed + 3), uv=1.0),
            "plank": Mat(n + "_plank", tex=kit.tex_planks(n + "_plank", "#5a4630", boards=3, seed=seed + 4), uv=1.0),
            "stone": Mat(n + "_stone", tex=kit.tex_mottle(n + "_stone", "#6a6050", var=0.2, seed=seed + 5), uv=0.8),
            "straw": Mat(n + "_straw", "#a07a2c"),
            "dark": Mat(n + "_rdark", "#140e08"),
            "chim": Mat(n + "_rchim", tex=kit.tex_stone(n + "_rchim", "#4e4a40", rows=7, seed=seed + 6), uv=1.5)}


def broken_chimney(B, m, x, y, z0, z1, sx, sy, seed=0):
    """A chimney stack snapped off at z1, its top a ragged step."""
    B.box(m["chim"], x, y, z0, sx, sy, z1 - z0 - 0.25)
    for i, (dx, dy, dz) in enumerate(((-0.25, -0.25, 0.25), (0.25, -0.25, 0.1), (-0.25, 0.25, 0.18), (0.25, 0.25, 0.0))):
        B.box(m["chim"], x + dx * sx, y + dy * sy, z1 - 0.25, sx / 2, sy / 2, dz + 0.05)


@model("AraHut08a")
def arahut08a(B, r):
    n = r["name"]
    m = ruin_mats(n)
    m8 = cottage_mats(n, thatch="#7d7422")
    p = HUT08
    kit.ruin_walls(B, m["wall"], m["top"], p, {
        "S": [(0, 0.9), (0.2, 1.5), (0.45, 1.2), (0.6, 1.7), (0.85, 1.8), (1, 2.0)],
        "E": [(0, 2.0), (0.4, 2.2), (0.7, 2.3), (1, 2.25)],
        "N": [(0, 2.2), (0.3, 2.0), (0.5, 1.6), (0.8, 1.1), (1, 0.7)],
        "W": [(0, 0.6), (0.5, 0.35), (1, 0.8)],
    }, t=0.3, seed=3)
    # the chimney stack still stands against the back wall, snapped at 2.9
    chx = px(r, 73.5, 0)[0]
    broken_chimney(B, m, chx, p["cy"] + p["W"] / 2 + 0.2, 0.0, 2.7, 0.72, 0.68)
    # the roof fell in: its thatch and timbers heaped inside, highest where
    # the west end came down over its wall
    kit.heap(B, m["heap"], r, [(4, 34), (12, 22), (32, 16), (46, 30), (42, 64), (20, 76), (6, 70)], 1.3,
             z_at=0.7, cell=0.4, noise=0.2, seed=1, edge=1.2)
    # spill past the east wall
    kit.heap(B, m["heap"], r, [(90, 14), (96, 16), (96, 58), (90, 60)], 0.5, z_at=0.3, cell=0.3, seed=5, edge=0.3)
    kit.heap(B, m["heap"], r, [(38, 16), (86, 12), (88, 58), (60, 64), (36, 60)], 0.8, z_at=0.4,
             cell=0.45, noise=0.2, seed=2, edge=0.9)
    kit.debris(B, m, r, [(4, 20), (86, 12), (88, 60), (10, 74)], 90, z_at=0.6, size=(0.6, 2.0), seed=4,
               zmax=0.9, tilt=30, kinds={"beam": 4, "plank": 2, "stone": 3, "straw": 4})
    # roof timbers fallen in from the east wall and the back
    kit.leaning(B, m["beam"], r, [(86, 30, 2.1, 60, 44, 0.6), (84, 46, 1.9, 58, 54, 0.5),
                                  (70, 14, 1.9, 56, 34, 0.7), (30, 12, 1.6, 36, 40, 0.5)])
    # the bench in front and the cart wheels by the east end survived
    bx, by = px(r, 68, 83)
    by += 0.25
    for e in (-1, 1):
        for f in (-1, 1):
            B.box(m8["timber"], bx + e * 0.45, by + f * 0.12, 0, 0.07, 0.07, 0.45)
    B.box(m8["wood"], bx, by, 0.42, 1.05, 0.36, 0.07, roll=6)
    for c, rw, tilt in ((102, 55, 18), (102, 68, 8)):
        x, y = px(r, c, rw, 0.2)
        with B.at(x, y, 0.22, yaw=90, roll=tilt):
            kit.wheel(B, m8["timber"], m8["timber"], 0, 0, 0, 0.42, axis=(0, 0, 1), spokes=6)


# the other groups register themselves here
import m_props  # noqa: E402,F401
import m_halls  # noqa: E402,F401
import m_ruins  # noqa: E402,F401
import m_smithy  # noqa: E402,F401
import m_longhall  # noqa: E402,F401
import m_manor  # noqa: E402,F401
