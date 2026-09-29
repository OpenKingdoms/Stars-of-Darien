"""Veruna houses, huts and hovels and their ruins.

Flat-roofed white houses with awnings and outside stairs, barrel-vaulted
houses (some turned on the map), a log hut under a slate pyramid and a
plank hovel. Positions are read off each sprite with m.X, m.Y.
"""
import math

import kit
from kit import model
from models_compound import rect, ring
from models_flat import adobe, jar


def hut_colours(m):
    # houses of one and two storeys: a little lower and deeper than drawn
    m.sturdy(0.9)
    m.col("adobe", (84, 74, 62))
    m.col("flat", (236, 220, 190))
    m.col("parapet", (240, 228, 200))
    m.col("dark", (28, 22, 18))
    m.col("sill", (230, 222, 200))
    m.col("wood", (96, 70, 46))
    m.col("crate", (124, 96, 66))
    m.col("post", (80, 60, 40))


def goods(m, x0, x1, y, z, keys=("red", "blue", "brown")):
    """A row of small bundles and pots along a parapet."""
    m.col("red", (150, 60, 40))
    m.col("blue", (80, 90, 120))
    m.col("brown", (110, 80, 50))
    n = max(2, int((x1 - x0) / 0.45))
    for i in range(n):
        x = x0 + (x1 - x0) * (i + 0.5) / n
        k = keys[i % len(keys)]
        if i % 3 == 2:
            jar(m, k, x, y, z0=z, r=0.18, h=0.35)
        else:
            m.crate(k, x, y, s=0.32, h=0.25, yaw=0.2 * (i % 2), z0=z)


# ---- flat-roofed houses ------------------------------------------------

def hut01(m, dx=0, dy=0):
    F = m.frame(dx, dy)
    x0, x1 = F.X(33), F.X(85)
    y0, y1 = F.Y(93), F.Y(3, 2.25)
    return dict(F=F, box=rect(x0, x1, y0, y1), top=2.25, x0=x0, x1=x1, y0=y0, y1=y1,
                aw=((x0, F.Y(12, 2.0)), (x0, F.Y(72, 2.0))), aw_d=x0 - F.X(2))


@model("VerHut01")
def verhut01(m):
    hut_colours(m)
    m.col("awn_a", (232, 226, 206))
    m.col("awn_b", (206, 200, 180))
    P = hut01(m)
    F = P["F"]
    adobe(m, P["box"], P["top"], vigas=None)
    zr = P["top"] - 0.3
    # the striped awning off the west wall on two poles
    (ax, ay), (bx, by) = P["aw"]
    m.awning("awn_a", "awn_b", (ax, ay), (bx, by), P["aw_d"], 1.1, 2.0, stripes=5)
    for yy in (ay - 0.15, by + 0.15):
        m.post("post", ax - P["aw_d"] + 0.12, yy, 0.0, 0.9, 0.09)
    # the roof hatch with its ladder, the goods along the parapet, the crates
    hx0, hx1, hy0, hy1 = F.X(38), F.X(50), F.Y(65, zr), F.Y(45, zr)
    m.box("dark", hx0, hx1, hy0, hy1, zr - 0.02, zr + 0.02)
    m.ladder("wood", ((hx0 + hx1) / 2, hy0 + 0.3, zr - 0.6), ((hx0 + hx1) / 2, hy1 - 0.1, zr + 0.8), w=0.4)
    goods(m, F.X(54), F.X(80), F.Y(67, zr + 0.2), zr)
    m.crate("crate", F.X(75), F.Y(56, zr + 0.3), s=0.6, h=0.45, yaw=0.3, z0=zr)
    m.crate("crate", F.X(26), F.Y(76, 0.3), s=0.55, h=0.45, yaw=0.1)
    m.window("dark", F.X(46), P["y0"], 0.0, 0.6, 1.2)
    m.window("dark", F.X(68), P["y0"], 0.9, 0.45, 0.4, sill="sill")


def hut02(m, dx=0, dy=0):
    F = m.frame(dx, dy)
    x0, x1 = F.X(7), F.X(71)
    y0, y1 = F.Y(85), F.Y(20, 1.65)
    return dict(F=F, box=rect(x0, x1, y0, y1), top=1.65, x0=x0, x1=x1, y0=y0, y1=y1,
                aw=((F.X(64), y1), (F.X(5), y1)), aw_d=F.Y(0, 0.9) - y1)


@model("VerHut02")
def verhut02(m):
    hut_colours(m)
    m.col("awn_a", (176, 80, 50))
    m.col("awn_b", (236, 228, 204))
    m.col("green", (70, 100, 60))
    P = hut02(m)
    F = P["F"]
    adobe(m, P["box"], P["top"], vigas=None)
    zr = P["top"] - 0.3
    # the red and white awning off the back parapet, on poles
    (ax, ay), (bx, by) = P["aw"]
    m.awning("awn_a", "awn_b", (ax, ay), (bx, by), P["aw_d"], 0.6, 1.55, stripes=6)
    for xx in (ax - 0.1, bx + 0.1):
        m.post("post", xx, ay + P["aw_d"] - 0.1, 0.0, 0.95, 0.08)
    # the stair up the east wall
    m.stair("adobe", P["x1"], P["x1"] + 0.5, P["y0"] + 0.1, F.Y(28, 1.3), 0.0, 1.3, axis="y", up=1)
    m.crate("crate", F.X(18), F.Y(48, zr + 0.5), s=0.75, h=0.5, yaw=0.05, z0=zr)
    m.crate("crate", F.X(17), F.Y(56, zr + 0.9), s=0.65, h=0.4, yaw=-0.1, z0=zr + 0.5)
    m.crate("crate", F.X(60), F.Y(42, zr + 0.3), s=0.6, h=0.35, yaw=0.0, z0=zr)
    m.box("green", F.X(60) - 0.22, F.X(60) + 0.22, F.Y(42, zr + 0.3) - 0.22, F.Y(42, zr + 0.3) + 0.22, zr + 0.35,
          zr + 0.38)
    jar(m, "adobe", F.X(64), F.Y(60, zr + 0.3), z0=zr, r=0.25, h=0.45)
    for c in (14, 28):
        m.window("dark", F.X(c), P["y0"], 0.4, 0.45, 0.4, sill="sill")
    m.window("dark", F.X(50), P["y0"], 0.0, 0.6, 1.1)


# ---- barrel-vaulted houses ---------------------------------------------

def vaulted(m, cx, cy, L, W, spring, rise, yaw=0.0, door_end="S", keep=None, walls="plaster", roof="plaster",
            end="plaster"):
    """A barrel-vaulted house: side walls to the springing and a vault over
    them, laid along x about (cx, cy) and turned by yaw; one end has a door."""
    with m.rot(cx, cy, yaw):
        x0, x1, y0, y1 = cx - L / 2, cx + L / 2, cy - W / 2, cy + W / 2
        if keep is None:
            m.box(walls, x0, x1, y0, y1, 0, spring)
            m.vault(roof, x0, x1, y0, y1, spring, rise, axis="x", end=end, over=0.12)
        door_x = x0 if door_end == "W" else x1
        m.box("dark", door_x - 0.04, door_x + 0.04, cy - 0.45, cy + 0.45, 0.0, 1.15)


@model("VerHut03")
def verhut03(m):
    hut_colours(m)
    m.col("plaster", (236, 220, 186))
    m.col("orange", (200, 100, 40))
    m.col("sack", (70, 56, 40))
    y0 = m.Y(82)
    # the vault runs north from the door in its south end
    L, W = 3.9, m.X(58) - m.X(15)
    cx, cy = (m.X(15) + m.X(58)) / 2, y0 + L / 2
    m.col("end", (132, 120, 102))
    vaulted(m, cx, cy, L, W, 1.3, 1.2, yaw=-math.pi / 2, door_end="E", end="end", walls="end")
    m.box("crate", m.X(3), m.X(12), m.Y(78, 0.2), m.Y(62, 0.2), 0.0, 0.4)
    m.box("orange", m.X(3) + 0.06, m.X(12) - 0.06, m.Y(78, 0.2) + 0.06, m.Y(62, 0.2) - 0.06, 0.4, 0.47)
    m.lathe("sack", m.X(66), m.Y(76, 0.3), [(0.25, 0.0), (0.33, 0.2), (0.28, 0.45), (0.12, 0.6)], seg=10)


@model("VerHut04")
def verhut04(m):
    hut_colours(m)
    m.col("plaster", (170, 158, 135))
    m.col("side", (90, 85, 75))
    m.col("eave", (214, 204, 180))
    m.col("barrel", (78, 58, 38))
    m.col("band", (50, 42, 34))
    m.col("wheel", (190, 165, 120))
    # the vault lies across the map, its door end to the south-west; its long
    # south-east side is a dark wall under a pale eave
    cx, cy, L, W, yaw = -0.29, 0.46, 3.9, 2.4, math.radians(39)
    vaulted(m, cx, cy, L, W, 1.75, 0.6, yaw=yaw, door_end="W", walls="side", end="plaster")
    with m.rot(cx, cy, yaw):
        m.box("eave", cx - L / 2 - 0.12, cx + L / 2 + 0.12, cy - W / 2 - 0.16, cy - W / 2 + 0.02, 1.64, 1.77)
    # the barrels stand out to the north-west, clear of the vault
    for c, r in ((10, 12), (19, 19), (8, 25)):
        m.barrel("barrel", m.X(c) - 0.35, m.Y(r, 0.62) + 0.35, 0.3, 0.62, band="band")
    # a wooden wheel on its frame of splayed legs, and a small crate by the house
    m.col("wood", (64, 46, 32))
    x, y, zt = m.X(67), m.Y(56, 1.75), 1.55
    for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        m.beam("wood", (x + 0.22 * sx, y + 0.16 * sy, zt), (x + 0.7 * sx, y + 0.55 * sy, 0.0), 0.1)
    for sy in (-1, 1):
        m.beam("wood", (x - 0.4, y + 0.3 * sy, 0.5), (x + 0.4, y + 0.3 * sy, 0.5), 0.07)
    m.beam("wood", (x - 0.25, y, zt), (x + 0.25, y, zt), 0.1)
    m.post("wood", x, y, zt, zt + 0.3, 0.06)
    m.cyl("wheel", x, y - 0.05, zt + 0.1, 0.42, 0.08, seg=14, smooth=False, rot=(0.0, 0.0, -0.9))
    m.cyl("wood", x, y - 0.05, zt + 0.1, 0.12, 0.12, seg=6, smooth=False, rot=(0.0, 0.0, -0.9))
    m.crate("crate", m.X(35), m.Y(84, 0.2), s=0.5, h=0.4, yaw=0.4)


# ---- the log hut under a slate pyramid ------------------------------------

def hut07(m, dx=0, dy=0):
    F = m.frame(dx, dy)
    return dict(F=F, x0=F.X(6), x1=F.X(54), y0=F.Y(83), y1=F.Y(2, 2.0) - 0.3, h=2.0, rise=1.3)


@model("VerHut07")
def verhut07(m):
    # a log hut under a tall slate roof: squatter than drawn
    m.sturdy(0.88)
    m.col("slate", (48, 58, 60))
    m.col("log", (74, 62, 50))
    m.col("batten", (140, 120, 96))
    m.col("dark", (24, 20, 18))
    P = hut07(m)
    x0, x1, y0, y1, h = P["x0"], P["x1"], P["y0"], P["y1"], P["h"]
    m.box("log", x0, x1, y0, y1, 0, h)
    for z in (0.35, 0.8, 1.25, 1.7):
        m.box("batten", x0 - 0.04, x1 + 0.04, y0 - 0.04, y0 + 0.04, z, z + 0.08)
        m.box("batten", x0 - 0.04, x0 + 0.04, y0, y1, z, z + 0.08)
        m.box("batten", x1 - 0.04, x1 + 0.04, y0, y1, z, z + 0.08)
    m.window("dark", (x0 + x1) / 2 - 0.3, y0, 0.0, 0.7, 1.3)
    m.pyramid("slate", x0, x1, y0, y1, h, P["rise"], over=0.3, t=0.12, hole=0.1)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    m.cyl("dark", cx, cy, h + P["rise"] * 0.85, 0.2, 0.05, seg=10, smooth=False)


# ---- the plank hovel -------------------------------------------------------

def hut08(m, dx=0, dy=0):
    F = m.frame(dx, dy)
    return dict(F=F, x0=F.X(6), x1=F.X(87), yf=F.Y(66, 1.2) + 0.2, yb=F.Y(2, 1.2) - 0.2, ze=1.2, zr=2.2,
                lx0=F.X(40), lx1=F.X(68), ly=F.Y(98, 0.55))


def plank_tones(m):
    m.col("plank_a", (128, 120, 102))
    m.col("plank_b", (110, 104, 88))
    m.col("plank_c", (142, 134, 114))
    m.col("shed", (92, 86, 72))
    m.col("cap", (92, 108, 108))
    m.col("post", (70, 58, 44))
    return ("plank_a", "plank_b", "plank_c")


def hovel_roof(m, P, keep=None, lean=True, cap=True, over=0.25):
    tones = plank_tones(m)
    x0, x1, yf, yb, ze, zr = P["x0"], P["x1"], P["yf"], P["yb"], P["ze"], P["zr"]
    ym = (yf + yb) / 2
    k = over / (ym - yf)
    e = ze - (zr - ze) * k
    m.planks("plank_a", (x0 - 0.15, yf - over, e), (x1 + 0.15, yf - over, e), (x1 + 0.15, ym, zr), (x0 - 0.15, ym, zr),
             keep=keep, keys=tones)
    m.planks("plank_a", (x1 + 0.15, yb + over, e), (x0 - 0.15, yb + over, e), (x0 - 0.15, ym, zr), (x1 + 0.15, ym, zr),
             keep=keep, keys=tones)
    if cap:
        m.beam("cap", (x0 - 0.2, ym, zr + 0.02), (x1 + 0.2, ym, zr + 0.02), 0.75, 0.08)
    if lean:
        lx0, lx1, ly = P["lx0"], P["lx1"], P["ly"]
        m.planks("plank_a", (lx0, ly, 0.55), (lx1, ly, 0.55), (lx1, yf, 1.1), (lx0, yf, 1.1), keep=keep, keys=tones)
        for x in (lx0 + 0.1, lx1 - 0.1):
            m.post("post", x, ly + 0.15, 0.0, 0.55, 0.1)


@model("VerHut08")
def verhut08(m):
    m.sturdy(0.9)
    P = hut08(m)
    tones = plank_tones(m)
    m.col("dark", (24, 20, 18))
    x0, x1, yf, yb, ze = P["x0"], P["x1"], P["yf"], P["yb"], P["ze"]
    m.box("shed", x0, x1, yf, yb, 0, ze)
    # gable ends of boards
    gable(m, "shed", x0, yf, yb, ze, P["zr"])
    gable(m, "shed", x1, yf, yb, ze, P["zr"])
    hovel_roof(m, P)
    m.window("dark", P["x0"] + 1.0, yf, 0.0, 0.7, 1.0)
    # boards lying by the wall and a stake
    F = P["F"]
    for (c0, r0), (c1, r1), k in (((9, 82), (38, 90), 0), ((11, 90), (40, 97), 1), ((20, 78), (44, 87), 2)):
        m.beam(tones[k], (F.X(c0), F.Y(r0, 0.05), 0.05 + 0.03 * k), (F.X(c1), F.Y(r1, 0.05), 0.05 + 0.03 * k), 0.22,
               0.05)
    m.post("post", F.X(77), F.Y(86, 0.3), 0.0, 0.6, 0.08)


def gable(m, key, x, y0, y1, z0, z1, t=0.12):
    """A triangular end wall at x from the eaves up to the ridge."""
    ym = (y0 + y1) / 2
    bm = m.bm(key)
    a = [bm.verts.new((x - t / 2, y0, z0)), bm.verts.new((x - t / 2, y1, z0)), bm.verts.new((x - t / 2, ym, z1))]
    b = [bm.verts.new((x + t / 2, y0, z0)), bm.verts.new((x + t / 2, y1, z0)), bm.verts.new((x + t / 2, ym, z1))]
    fs = [bm.faces.new(a), bm.faces.new(b[::-1])]
    for i in range(3):
        j = (i + 1) % 3
        fs.append(bm.faces.new((a[i], b[i], b[j], a[j])))
    import bmesh
    bmesh.ops.recalc_face_normals(bm, faces=fs)


def hut05(m, dx=0, dy=0):
    F = m.frame(dx, dy)
    return dict(F=F, cx=F.X(49) + 0.05, cy=F.Y(46) - 0.05, yaw=math.radians(-14), u0=-2.5, u1=1.15, a1=2.95,
                w=2.5, spring=1.0, rise=1.1)


def hut05_body(m, P, keep=None):
    cx, cy, yaw = P["cx"], P["cy"], P["yaw"]
    u0, u1, a1, w = P["u0"], P["u1"], P["a1"], P["w"]
    zc = P["spring"] + P["rise"]
    with m.rot(cx, cy, yaw):
        x0, x1 = cx + u0, cx + u1
        y0, y1 = cy - w / 2, cy + w / 2
        if keep is None:
            # the vault stops short of the block, so its open east end shows as a deep dark arch
            xv = x1 - 0.5
            m.box("plaster", x0, xv, y0, y1, 0, P["spring"])
            m.vault("plaster", x0, xv, y0, y1, P["spring"], P["rise"], axis="x", over=0.1)
            hw = w / 2
            arch = [(cy - hw * math.cos(math.pi * i / 10), P["spring"] + (P["rise"] - 0.12) * math.sin(math.pi * i / 10))
                    for i in range(11)]
            m._arch_plate("end", arch, x0, 0.15, "x")
            m._arch_plate("dark", arch, xv + 0.02, 0.06, "x")
            # the deep shadow of the arch fills the space up to the block
            m.box("dark", xv - 0.01, x1 + 0.32, y0 + 0.25, y1 - 0.1, 0.0, P["spring"] + P["rise"] * 0.75)
            # a bundle of straw stowed in the arch, on the shadow's ledge
            zs = P["spring"] + P["rise"] * 0.75
            m.lathe("straw", xv + 0.35, cy - 0.35, [(0.3, zs), (0.3, zs + 0.2), (0.18, zs + 0.38), (0.04, zs + 0.44)],
                    seg=9)
        # the flat-roofed annex, run south to the drawing's lower edge, and the terrace stair
        adobe(m, rect(x1 + 0.3, cx + a1, y0 - 1.1, y1), zc + 0.25, wall="plaster", roof="flat", par="parapet",
              vigas=None, keep=keep)
        m.stair("plaster", cx - 0.6, x1 + 0.3, y0 - 1.1, y0, 0.0, zc - 0.05, axis="x", up=1)
        m.box("dark", x0 + 0.8, x0 + 1.5, y0 - 0.04, y0 + 0.02, 0.0, 0.8)


@model("VerHut05")
def verhut05(m):
    hut_colours(m)
    m.col("plaster", (205, 206, 204))
    m.col("end", (160, 160, 156))
    m.col("flat", (210, 210, 206))
    m.col("parapet", (216, 216, 212))
    m.col("orange", (200, 110, 40))
    m.col("straw", (196, 164, 96))
    P = hut05(m)
    hut05_body(m, P)
    x, y = m.X(11), m.Y(70, 0.3)
    with m.rot(x, y, 0.5):
        m.box("crate", x - 0.4, x + 0.4, y - 0.3, y + 0.3, 0.0, 0.35)
        m.box("orange", x - 0.35, x + 0.35, y - 0.25, y + 0.25, 0.35, 0.42)


# ---- the ruined plaster houses ---------------------------------------------

def ruin_setup(m, wall_rect=None):
    hut_colours(m)
    keys = m.ruin_palette()
    m.beam_share = 0.6
    m.paint_gain = 1.15
    if wall_rect:
        m.col("adobe", m.sample(*wall_rect))
    return keys


def pale(m, frac=0.45):
    """keep() for plaster still standing in the drawing: pale and grey."""
    return m.keep_class(frac=frac, pred=lambda c: max(c) > 0.42 and max(c) - min(c) < 0.25 and not m.roofish(c))


def sag_roof(m, key, x0, x1, y0, y1, z, sag, field, cell=0.2, t=0.14):
    """A roof slab over the rectangle sagging sag in the middle, broken
    where field <= 0."""
    nu, nv = max(2, int((x1 - x0) / cell)), max(2, int((y1 - y0) / cell))
    G = {}
    for i in range(nu + 1):
        for j in range(nv + 1):
            u, v = i / nu, j / nv
            G[i, j] = kit.Vector((x0 + (x1 - x0) * u, y0 + (y1 - y0) * v,
                                  z - sag * math.sin(math.pi * u) * math.sin(math.pi * min(1.0, v * 1.4))))
    down = kit.Vector((0, 0, -t))
    m.cut_grid(key, G, nu, nv, field, lambda p: down)


def litter(m, items, rng, stone=("stone", "stone2"), wood=("char2", "wood")):
    """Loose pieces read off the drawing: ("s", c, r, size) a stone,
    ("t", c0, r0, c1, r1, w) a timber, ("c", c, r, size, yaw) a crate,
    ("p", c0, r0, c1, r1) a plank."""
    for it in items:
        if it[0] == "s":
            _, c, r, s = it
            m.stone(rng.choice(stone), m.X(c), m.Y(r, s * 0.3), s, s * rng.uniform(0.6, 0.9), s * 0.55,
                    rng.uniform(0, 3))
        elif it[0] == "t":
            _, c0, r0, c1, r1, w = it
            m.beam(rng.choice(wood), (m.X(c0), m.Y(r0, w / 2), w / 2), (m.X(c1), m.Y(r1, w / 2), w / 2 + 0.05), w)
        elif it[0] == "p":
            _, c0, r0, c1, r1 = it
            m.beam(rng.choice(("plank", "wood")), (m.X(c0), m.Y(r0, 0.05), 0.05), (m.X(c1), m.Y(r1, 0.05), 0.08),
                   0.26, 0.06)
        else:
            _, c, r, s, yaw = it
            m.obox("crate", m.X(c), m.Y(r, s * 0.4), s * 0.4, s, s * 0.85, s * 0.8, yaw, 0.0, rng.uniform(-0.2, 0.2))


def plaster_tones(m, face, cap, soot):
    m.col("adobe", face)
    m.col("parapet", cap)
    m.col("psoot", soot)
    m.soot["parapet"] = ("psoot", 0.4)
    m.col("stone", (172, 162, 144))
    m.col("stone2", (132, 122, 106))
    m.col("plank", (120, 100, 76))


def fruit_crate(m, c, r, yaw, rng, n=10):
    """A crate of oranges tipped over, the fruit rolled out on its open side."""
    x, y = m.X(c), m.Y(r, 0.25)
    m.obox("crate", x, y, 0.22, 0.75, 0.55, 0.42, yaw, 0.0, 0.35)
    for _ in range(n):
        a, d = yaw + math.pi / 2 + rng.uniform(-1.0, 1.0), rng.uniform(0.3, 0.9)
        m.lathe("orange", x + d * math.cos(a), y + d * math.sin(a), [(0.06, 0.0), (0.11, 0.08), (0.06, 0.17)],
                seg=6)


@model("VerHut01a")
def verhut01a(m):
    import random
    rng = random.Random(m.name)
    keys = ruin_setup(m)
    tones = char_tones(m)
    plaster_tones(m, (104, 96, 84), (160, 148, 128), (112, 102, 88))
    m.col("flat", (150, 138, 118), paint=True)
    P = hut01(m, -2, -1)
    top = P["top"]
    h = top - 0.3
    x0, x1, y0, y1 = P["x0"], P["x1"], P["y0"], P["y1"]
    sh = ring(P["box"], 0.15)
    # the back and west walls stand, the front is broken low
    m.bwall("adobe", sh + sh[:1], 0.3, [(0, 1.0), (0.1, 0.7), (0.2, 1.1), (0.28, 1.5), (0.4, top - 0.2), (0.5, top),
                                        (0.71, top), (0.8, top - 0.3), (0.9, 1.6), (1, 1.0)],
            frac=True, jag=0.2, cap="parapet")
    # one big slab of the roof still rests on the north walls, sagging
    grey = m.pic_field(lambda c: max(c) > 0.33 and max(c) - min(c) < 0.2, sigma=2.5, bias=0.35)
    edge = m.region(ring(P["box"], 0.3), rag=0.0)
    ys = m.Y(56, h)
    sag_roof(m, "flat", x0 + 0.2, x1 - 0.2, ys, y1 - 0.2, h, 0.4,
             lambda x, y, z=0.0: grey(x, y, z) + 0.3 * max(0.0, 1.0 - edge(x, y) / 0.5))
    # the charred timber pile in the south half, embers still red in it
    cx, cy = (x0 + x1) / 2, y0 + 1.3
    char_heap(m, cx, cy, 0.9, 0.22, rng, key="char2", embers=10)
    lean(m, ("wood", "char2", "tan", "char2"), [((cx - 0.8, cy - 0.3, 0.05), (cx + 0.5, cy + 0.4, 0.35)),
                                      ((cx + 0.7, cy - 0.5, 0.05), (cx - 0.2, cy + 0.6, 0.3)),
                                      ((cx - 0.3, cy - 0.8, 0.05), (cx + 0.1, cy + 0.7, 0.4)),
                                      ((cx + 0.9, cy + 0.2, 0.05), (cx - 0.9, cy + 0.1, 0.25))], rng)
    lean(m, ("char", "wood", "char2"), [((x0 + 0.3, y0 + 0.4, 0.2), (cx + 0.4, cy + 0.5, 0.9)), ((x1 - 0.3, y0 + 0.3, 0.3),
                    (cx - 0.3, cy + 0.2, 0.8)), ((cx - 1.0, cy - 0.6, 0.1), (cx + 1.1, cy + 0.9, 0.6)),
                    ((x0 + 0.2, cy + 1.4, 1.4), (cx, cy, 0.4))], rng)
    litter(m, [("s", 18, 30, 0.35), ("s", 14, 40, 0.3), ("s", 21, 47, 0.3), ("s", 13, 86, 0.3), ("s", 19, 93, 0.28),
               ("t", 8, 58, 30, 80, 0.18), ("s", 38, 94, 0.3), ("s", 70, 92, 0.35), ("s", 78, 97, 0.3),
               ("s", 60, 99, 0.25), ("t", 42, 104, 64, 93, 0.16), ("t", 84, 34, 90, 40, 0.12),
               ("c", 85, 55, 0.45, 0.3)], rng)
    m.scatter(keys, max_area=40, skip=[(0, 0, 92, 106)])


@model("VerHut02a")
def verhut02a(m):
    import random
    rng = random.Random(m.name)
    keys = ruin_setup(m)
    tones = char_tones(m)
    plaster_tones(m, (104, 96, 84), (170, 160, 140), (118, 108, 92))
    m.col("flat", (160, 150, 130), paint=True)
    m.col("awn_a", (170, 62, 40))
    m.col("awn_b", (222, 212, 192))
    m.col("green", (70, 100, 60))
    P = hut02(m, 6, -1)
    top = P["top"]
    h = top - 0.3
    x0, x1, y0, y1 = P["x0"], P["x1"], P["y0"], P["y1"]
    sh = ring(P["box"], 0.15)
    m.bwall("adobe", sh + sh[:1], 0.3, [(0, 1.2), (0.15, top), (0.3, top - 0.2), (0.4, 0.9), (0.5, top), (0.7, top),
                                        (0.8, 1.0), (1, 1.2)], frac=True, jag=0.2, cap="parapet")
    # a strip of the roof still spans the house on the diagonal
    grey = m.pic_field(lambda c: max(c) > 0.4 and max(c) - min(c) < 0.2, sigma=2.5, bias=0.4)
    edge = m.region(ring(P["box"], 0.3), rag=0.0)
    sag_roof(m, "flat", x0 + 0.2, x1 - 0.2, y0 + 0.2, y1 - 0.2, h, 0.3,
             lambda x, y, z=0.0: min(grey(x, y, z), edge(x, y)))
    m.debris(m.rubble_tones(stone=(150, 140, 122), ash=(62, 56, 48)), ring(P["box"], 0.3), 0.8, lo=0.4, hi=0.7,
             density=2.5, maxn=40, cell=0.45, mix=dict(char=0.4, stone=0.35, wood=0.2, tile=0.05), size=(0.2, 0.4))
    # the torn red and white awning draped back over the north parapet, its frame broken
    (ax, ay), (bx, by) = P["aw"]
    torn = m.pic_field(lambda c: m.roofish(c) or min(c) > 0.62, sigma=2.0, bias=0.3)
    m.drape(("awn_a", "awn_b"), (bx, ay - 0.2, top + 0.05), (ax - bx, 0, 0), (0, 1.3, 0), 6, 5,
            lambda a, b: -1.3 * b ** 1.3 + 0.12 * math.sin(7 * a), field=torn, t=0.04)
    m.post("post", bx + 0.3, ay + 1.2, 0.0, 1.1, 0.09, lean=0.4)
    m.beam("post", (ax - 0.2, ay + 1.1, 0.0), (ax - 0.8, ay + 0.2, 1.0), 0.09)
    # crates, boards and goods thrown out on every side
    litter(m, [("c", 80, 14, 0.55, 0.4), ("t", 66, 10, 90, 3, 0.1), ("c", 84, 34, 0.5, -0.3), ("p", 85, 52, 97, 60),
               ("p", 84, 57, 95, 62), ("c", 36, 83, 0.5, 0.2), ("c", 58, 85, 0.4, 0.8), ("p", 15, 76, 30, 84),
               ("p", 2, 62, 12, 78), ("p", 3, 42, 11, 47), ("s", 22, 88, 0.3), ("s", 70, 80, 0.3),
               ("t", 5, 70, 14, 80, 0.14)], rng)
    m.crate("crate", m.X(50), m.Y(34, h + 0.3), s=0.5, h=0.35, yaw=0.3, z0=h)
    m.box("green", m.X(50) - 0.18, m.X(50) + 0.18, m.Y(34, h + 0.3) - 0.18, m.Y(34, h + 0.3) + 0.18, h + 0.35, h + 0.4)
    m.scatter(keys, max_area=40, skip=[(0, 0, 99, 89)])


@model("VerHut03a")
def verhut03a(m):
    import random
    rng = random.Random(m.name)
    keys = ruin_setup(m)
    tones = char_tones(m)
    plaster_tones(m, (120, 110, 94), (150, 140, 120), (110, 100, 86))
    m.col("plaster", (140, 130, 112))
    m.col("end", (112, 104, 90))
    m.col("orange", (206, 110, 36))
    m.col("table", (112, 86, 58))
    F = m.frame(14, -3)
    y0 = F.Y(82)
    L = 3.9
    x0, x1 = F.X(15), F.X(58)
    # the side walls stand to the springing, the vault broken along ragged curves
    for x in (x0 + 0.15, x1 - 0.15):
        m.bwall("plaster", [(x, y0), (x, y0 + L)], 0.3, [(0, 0.9), (0.3, 1.3), (1, 1.3)], frac=True, jag=0.15)
    m.bwall("end", [(x0, y0 + 0.1), (x1, y0 + 0.1)], 0.25, [(0, 1.2), (0.3, 0.9), (0.4, 0), (0.62, 0), (0.7, 1.4),
                                                           (1, 1.0)], frac=True, jag=0.25)
    m.bwall("end", [(x0, y0 + L - 0.1), (x1, y0 + L - 0.1)], 0.25, [(0, 1.3), (0.5, 2.3), (1, 1.3)], frac=True,
            jag=0.1)
    vault = m.pic_field(lambda c: max(c) > 0.45 and max(c) - min(c) < 0.22, sigma=2.2, bias=0.4)
    m.cut_vault("plaster", x0, x1, y0, y0 + L, 1.3, 1.2, axis="y", field=vault, t=0.18, cell=0.18)
    T = m.rubble_tones(char=(42, 36, 30), wood=(96, 76, 54), stone=(150, 140, 122), ash=(70, 62, 54))
    m.debris(T, rect(x0 + 0.3, x1 - 0.3, y0 + 0.3, y0 + L - 0.3), 1.1, lo=0.45, hi=0.7, density=3.2, maxn=55,
             cell=0.4, mix=dict(stone=0.45, char=0.3, wood=0.25), size=(0.2, 0.42))
    # the orange crate tipped over on the west, the table broken at the south door
    fruit_crate(m, 12, 67, 0.5, rng)
    tx, ty = F.X(37), F.Y(92, 0.3)
    m.obox("table", tx, ty, 0.42, 1.1, 0.7, 0.07, 0.15, 0.35, 0.0)
    for dx, dy, hh, lean_ in ((-0.45, -0.28, 0.72, 0.0), (0.45, -0.28, 0.45, 0.5), (-0.45, 0.28, 0.72, 0.1)):
        m.post("table", tx + dx, ty + dy, 0.0, hh, 0.07, lean=lean_)
    m.beam("table", (tx + 0.5, ty + 0.4, 0.05), (tx + 0.9, ty - 0.1, 0.05), 0.07)
    litter(m, [("s", 24, 15, 0.35), ("s", 22, 28, 0.3), ("s", 26, 52, 0.35), ("s", 83, 48, 0.4), ("s", 86, 66, 0.35),
               ("s", 38, 86, 0.25), ("s", 66, 84, 0.3), ("t", 76, 84, 88, 80, 0.12)], rng)
    m.scatter(keys, max_area=40, skip=[(0, 0, 96, 103)])


@model("VerHut04a")
def verhut04a(m):
    import random
    rng = random.Random(m.name)
    keys = ruin_setup(m)
    tones = char_tones(m)
    plaster_tones(m, (90, 82, 70), (140, 130, 112), (100, 92, 80))
    m.col("plaster", (150, 140, 120))
    m.col("end", (120, 110, 96))
    m.col("side", (78, 72, 64))
    m.col("wheel", (150, 128, 96))
    cx, cy, yaw, L, W = -0.29 + 1 / 16, 0.46, math.radians(39), 3.9, 2.4
    T = m.rubble_tones(char=(40, 34, 28), wood=(86, 66, 46), stone=(150, 140, 120), ash=(96, 88, 76))
    with m.rot(cx, cy, yaw):
        x0, x1, y0, y1 = cx - L / 2, cx + L / 2, cy - W / 2, cy + W / 2
        # the dark south-east side wall still stands; the vault fell in between its end arches
        m.bwall("side", [(x0, y0 + 0.15), (x1, y0 + 0.15)], 0.3, [(0, 0.7), (0.3, 1.0), (0.6, 0.8), (1, 0.9)],
                frac=True, jag=0.15)
        m.bwall("plaster", [(x0, y1 - 0.15), (x1, y1 - 0.15)], 0.3, [(0, 0.8), (0.5, 0.5), (1, 0.7)], frac=True)
        m.bwall("end", [(x1 - 0.15, y0), (x1 - 0.15, y1)], 0.3, [(0, 1.0), (0.5, 2.0), (1, 1.0)], frac=True, jag=0.12)
        m.bwall("end", [(x0 + 0.15, y0), (x0 + 0.15, y1)], 0.3, [(0, 0.8), (0.4, 1.3), (0.7, 0.5), (1, 0.9)],
                frac=True, jag=0.25)
        # only the north-east end arch holds, its broken edge ragged
        end = lambda x, y, z=0.0: (x - cx - L * 0.3) + 0.14 * math.sin(7 * y + 4 * z) + 0.08 * math.sin(13 * z)  # noqa
        m.cut_vault("plaster", x0, x1, y0, y1, 1.1, 1.1, axis="x", field=end, t=0.18, cell=0.2)
        nz = m.noise("m", 0.6)
        m.debris(T, rect(x0 + 0.2, x1 - 0.2, y0 + 0.2, y1 - 0.2), 1.2,
                 zfn=lambda x, y: 1.0 * (1 - ((y - cy) / (W / 2)) ** 2) * (1 + 0.3 * nz(x, y)) + 0.15,
                 density=3.5, maxn=70, cell=0.35, mix=dict(stone=0.6, char=0.25, wood=0.15), size=(0.25, 0.5))
    # burnt timbers, the broken wheel frame at the south and scattered wood
    lean(m, tones, [((m.X(5), m.Y(10, 0.2), 0.2), (m.X(25), m.Y(18, 0.3), 0.4)),
                    ((m.X(12), m.Y(8, 0.1), 0.1), (m.X(22), m.Y(22, 0.1), 0.1)),
                    ((m.X(66), m.Y(58, 0.1), 0.1), (m.X(90), m.Y(62, 0.2), 0.2)),
                    ((m.X(72), m.Y(54, 0.1), 0.1), (m.X(84), m.Y(66, 0.1), 0.1))], rng)
    wx, wy = m.X(62), m.Y(80, 0.3)
    for a in (0.4, 2.4, 4.2):
        m.beam("wood", (wx, wy, 0.55), (wx + 0.5 * math.cos(a), wy + 0.5 * math.sin(a), 0.0), 0.08)
    m.cyl("wheel", wx + 0.2, wy - 0.1, 0.05, 0.38, 0.07, seg=12, smooth=False, rot=(0.3, 1.2, 0.0))
    m.beam("wood", (wx - 0.6, wy - 0.3, 0.05), (wx + 0.3, wy - 0.6, 0.05), 0.08)
    litter(m, [("t", 2, 66, 14, 76, 0.12), ("s", 8, 58, 0.3), ("s", 84, 30, 0.3), ("t", 80, 76, 90, 84, 0.1)], rng)
    m.scatter(keys, max_area=40, skip=[(0, 0, 90, 86)])


@model("VerHut05a")
def verhut05a(m):
    import random
    rng = random.Random(m.name)
    keys = ruin_setup(m)
    tones = char_tones(m)
    plaster_tones(m, (150, 150, 146), (175, 174, 169), (110, 108, 104))
    m.col("plaster", (160, 160, 156))
    m.col("end", (120, 120, 116))
    m.col("flat", (170, 169, 164))
    m.col("orange", (206, 110, 36))
    m.col("burn", (70, 66, 62))
    P = hut05(m, 3, -7)
    cx, cy, yaw = P["cx"], P["cy"], P["yaw"]
    u0, u1, a1, w = P["u0"], P["u1"], P["a1"], P["w"]
    zc = P["spring"] + P["rise"]
    T = m.rubble_tones(char=(40, 36, 32), wood=(90, 72, 52), stone=(170, 168, 162), ash=(110, 108, 102))
    vault = m.pic_field(lambda c: max(c) > 0.5 and max(c) - min(c) < 0.15, sigma=2.2, bias=0.45)
    with m.rot(cx, cy, yaw):
        x0, x1 = cx + u0, cx + u1
        y0, y1 = cy - w / 2, cy + w / 2
        for yy in (y0 + 0.15, y1 - 0.15):
            m.bwall("plaster", [(x0, yy), (x1, yy)], 0.3, [(0, 1.0), (0.4, 0.6), (0.6, 0.9), (1, 1.0)], frac=True,
                    jag=0.25)
        m.bwall("end", [(x0 + 0.15, y0), (x0 + 0.15, y1)], 0.3, [(0, 1.0), (0.5, 1.9), (1, 1.0)], frac=True, jag=0.2)
        # the block keeps its corners, broken down between them
        bx0, bx1 = x1 + 0.3, cx + a1
        box = ring(rect(bx0, bx1, y0, y1), 0.15)
        m.bwall("plaster", box + box[:1], 0.3, [(0, zc), (0.12, zc - 0.4), (0.2, 1.0), (0.3, 0.7), (0.42, zc),
                                                (0.55, zc - 0.2), (0.65, 1.1), (0.78, zc), (1, zc)],
                frac=True, jag=0.3, cap="parapet")
        m.stair("plaster", cx - 0.6, x1 + 0.3, y0 - 0.55, y0, 0.0, zc * 0.6, axis="x", up=1)
        m.debris(T, rect(x0 + 0.3, bx1 - 0.3, y0 + 0.3, y1 - 0.3), 1.0, lo=0.4, hi=0.7, density=3.0, maxn=70,
                 cell=0.4, mix=dict(stone=0.5, char=0.3, wood=0.2), size=(0.2, 0.45))
    # the vault, turned with the house: its field is read where each point lands once turned
    ca, sa = math.cos(yaw), math.sin(yaw)
    with m.rot(cx, cy, yaw):
        m.cut_vault("plaster", x0, x1, y0, y1, 1.0, 1.1, axis="x", t=0.16, cell=0.2,
                    field=lambda x, y, z=0.0: vault(cx + (x - cx) * ca - (y - cy) * sa, cy + (x - cx) * sa + (y - cy) * ca,
                                                    z))
    # soot patches, the fruit crate at the south-west, timbers
    for c, r in ((64, 30), (80, 46), (40, 55)):
        m.shard("burn", m.X(c), m.Y(r, 1.0), 0.05, 0.9, 0.7, rng.uniform(0, 3), 0.0, 0.0, t=0.02, rag=0.4, seed=c)
    fruit_crate(m, 13, 60, -0.4, rng, n=12)
    litter(m, [("t", 26, 8, 42, 22, 0.14), ("t", 30, 18, 46, 12, 0.12), ("s", 6, 22, 0.35), ("s", 40, 76, 0.3),
               ("s", 72, 80, 0.25), ("t", 82, 70, 96, 76, 0.12)], rng)
    m.scatter(keys, max_area=40, skip=[(0, 0, 105, 84)])


# ---- the charred timber frames -------------------------------------------

def char_tones(m, tan=(128, 108, 84)):
    """Charred timber, weathered timber, the tan of boards the fire spared,
    ember red and the ash of the small heaps."""
    m.col("char", (40, 34, 29))
    m.col("char2", (58, 50, 42))
    m.col("wood", (92, 80, 64))
    m.col("tan", tan)
    m.col("ember", (140, 46, 24))
    m.col("ash", (52, 46, 40))
    return ("char", "char", "char2", "wood")


def frame_posts(m, x0, x1, y0, y1, h, tones, rng, mid=1.2, low=0.45, plates=0.6):
    """Posts round a timber frame, the corners standing tallest, and what
    is left of the top plates along each side."""
    corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    for i in range(4):
        (ax, ay), (bx, by) = corners[i], corners[(i + 1) % 4]
        L = math.hypot(bx - ax, by - ay)
        n = max(1, int(L / mid))
        for k in range(n):
            u = k / n
            x, y = ax + (bx - ax) * u, ay + (by - ay) * u
            ph = h * (rng.uniform(0.85, 1.0) if k == 0 else rng.uniform(low, 1.0))
            if k and rng.random() < 0.2:
                continue
            m.post(rng.choice(tones), x, y, 0.0, ph, 0.16, lean=rng.uniform(-0.06, 0.06))
        if rng.random() < plates:
            u0, u1 = sorted((rng.uniform(0, 0.3), rng.uniform(0.55, 1.0)))
            m.beam(rng.choice(tones), (ax + (bx - ax) * u0, ay + (by - ay) * u0, h * rng.uniform(0.9, 1.0)),
                   (ax + (bx - ax) * u1, ay + (by - ay) * u1, h * rng.uniform(0.7, 1.0)), 0.15)


def boarding(m, x0, x1, y0, y1, h, keys, rng, share=0.5, z0=0.0, sides=(0, 1, 2, 3), top=(0.25, 0.75)):
    """Broken wall boards standing along the sides of a frame."""
    corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    for i in sides:
        (ax, ay), (bx, by) = corners[i], corners[(i + 1) % 4]
        L = math.hypot(bx - ax, by - ay)
        yaw = math.atan2(by - ay, bx - ax)
        nb = int(L / 0.3)
        for k in range(nb):
            if rng.random() > share:
                continue
            u = (k + 0.5) / nb
            bh = h * rng.uniform(*top)
            m.obox(rng.choice(keys), ax + (bx - ax) * u, ay + (by - ay) * u, z0 + bh / 2, 0.27, 0.06, bh, yaw, 0.0,
                   rng.uniform(-0.1, 0.1))


def char_heap(m, x, y, r, h, rng, key="ash", embers=0, ember_key="ember"):
    """A small low heap of ash and char, a few embers glowing in it."""
    h = min(h, r * 0.3)
    prof = [(r, 0.0), (r * 0.75, h * 0.6), (r * 0.35, h * 0.92), (0.02, h)]
    m.lathe(key, x, y, prof, seg=7, smooth=False)
    for _ in range(embers):
        a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0.1, r * 0.7)
        zz = h * (1 - d / r) * 0.8
        m.obox(ember_key, x + d * math.cos(a), y + d * math.sin(a), zz + 0.03, rng.uniform(0.12, 0.25), 0.09, 0.07,
               rng.uniform(0, math.pi))


def lean(m, keys, pts, rng, w=(0.11, 0.16)):
    """Timbers leaning or lying between given 3D points."""
    for p0, p1 in pts:
        m.beam(rng.choice(keys), p0, p1, rng.uniform(*w))


def board_panel(m, keys, e0, e1, t1, t0, ragged=0.2, keep=None):
    """A fallen panel of roof boards from the edge e0-e1 up to t0-t1."""
    m.planks(keys[0], e0, e1, t1, t0, w=0.28, t=0.07, ragged=ragged, keep=keep, keys=keys)


@model("VerHut06a")
def verhut06a(m):
    import random
    m.sturdy(0.9)
    rng = random.Random(m.name)
    tones = char_tones(m)
    keys = m.ruin_palette()
    m.col("sill", (72, 68, 60))
    m.col("sill_top", (98, 92, 82))
    x0, x1, y0, y1, h = m.X(13), m.X(82), m.Y(70), m.Y(8, 1.5), 1.5
    # a low stone sill along the front, the frame on it
    m.wall("sill", [(x0 - 0.1, y0), (x1 + 0.1, y0)], 0.34, 0.32, cap="sill_top", cap_h=0.05)
    frame_posts(m, x0, x1, y0, y1, h, tones, rng, mid=1.0, low=0.6)
    boarding(m, x0, x1, y0, y1, h, ("tan", "tan", "wood", "char2"), rng, share=0.45, z0=0.0,
             sides=(1, 2, 3), top=(0.3, 0.8))
    boarding(m, x0, x1, y0 + 0.02, y1, h, ("tan", "wood"), rng, share=0.35, z0=0.32, sides=(0,), top=(0.2, 0.5))
    # the rafters lean across the middle from the side walls
    ym = (y0 + y1) / 2
    xs = [x0 + (x1 - x0) * u for u in (0.12, 0.3, 0.45, 0.62, 0.8, 0.92)]
    pairs = []
    for i, x in enumerate(xs):
        side = y0 if i % 2 == 0 else y1
        other = y1 if i % 2 == 0 else y0
        pairs.append(((x, side, h * rng.uniform(0.8, 1.0)),
                      (x + rng.uniform(-0.8, 0.8), ym + (other - ym) * rng.uniform(0.2, 0.7), rng.uniform(0.1, 0.5))))
    for i in range(9):
        a = rng.uniform(0, math.pi)
        cx, cy = rng.uniform(x0 + 0.5, x1 - 0.5), rng.uniform(y0 + 0.4, y1 - 0.4)
        pairs.append(((cx - 0.9 * math.cos(a), cy - 0.9 * math.sin(a), 0.1),
                      (cx + 0.9 * math.cos(a), cy + 0.9 * math.sin(a), rng.uniform(0.5, 1.1))))
    lean(m, tones, pairs, rng)
    # tan boards fallen in against the frame
    for c, r in ((25, 25), (50, 55), (74, 30), (15, 35), (38, 12), (62, 42), (80, 20)):
        m.shard("tan", m.X(c), m.Y(r, 0.5), 0.5, 0.8, 0.5, rng.uniform(0, 3), rng.uniform(-0.6, 0.6), 0.5, t=0.06,
                rag=0.3, seed=c)
    for x, y, r in ((x0 + 1.2, ym + 0.3, 0.4), (x1 - 1.4, ym - 0.2, 0.45)):
        char_heap(m, x, y, r, 0.15, rng, key="char2", embers=1)
    # the fallen log in front
    m.beam("wood", (m.X(14), m.Y(78, 0.15), 0.15), (m.X(80), m.Y(69, 0.15), 0.15), 0.28)
    m.scatter(keys, max_area=60)


def log_wall(m, keys, a, b, prof, r=0.14, over=0.18, rng=None):
    """A wall of logs laid in courses from corner a to corner b, broken to
    prof(u) high along it (u from 0 at a to 1 at b); the ends run past the
    corners as in a log house."""
    ax, ay = a
    bx, by = b
    L = math.hypot(bx - ax, by - ay)
    ux, uy = (bx - ax) / L, (by - ay) / L
    hmax = max(prof(i / 20) for i in range(21))
    n = int(hmax / (2 * r * 0.92))
    for k in range(n):
        z = r + k * 2 * r * 0.92
        ok = [prof(i / 40) > z + r * 0.5 for i in range(41)]
        for j0, j1 in kit._runs(ok):
            s0 = L * j0 / 40 - (over if j0 == 0 else 0.0)
            s1 = L * (j1 - 1) / 40 + (over if j1 == 41 else 0.0)
            if s1 - s0 < 0.3:
                continue
            key = keys[(k + j0) % len(keys)] if rng is None else rng.choice(keys)
            m.cyl(key, ax + ux * s0, ay + uy * s0, z, r, s1 - s0, seg=6,
                  rot=(math.atan2(uy, ux), math.pi / 2, 0.0))


@model("VerHut07a")
def verhut07a(m):
    import random
    m.sturdy(0.88)
    rng = random.Random(m.name)
    tones = char_tones(m)
    keys = m.ruin_palette()
    m.col("log_a", (70, 60, 50))
    m.col("log_b", (54, 46, 40))
    m.col("log_c", (88, 76, 62))
    m.col("slate", (58, 76, 84))
    m.col("slate2", (46, 60, 66))
    P = hut07(m, 4, -6)
    x0, x1, y0, y1, h = P["x0"], P["x1"], P["y0"], P["y1"], P["h"]
    logs = ("log_a", "log_b", "log_c")
    # the log courses stand highest at the corners, broken down between them
    tall = {0: 1.5, 1: 1.3, 2: 1.8, 3: 1.6}
    corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    dips = (0.35, 0.8, 0.6, 0.7)
    for i in range(4):
        ha, hb, d = tall[i], tall[(i + 1) % 4], dips[i]

        def prof(u, ha=ha, hb=hb, d=d):
            base = ha + (hb - ha) * u
            return base - (base - d) * math.sin(math.pi * u) ** 0.7
        log_wall(m, logs, corners[i], corners[(i + 1) % 4], prof, rng=rng)
    # a few rafters still reach up toward the old ridge, slates hanging off them
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    az = h + P["rise"]
    for (px, py), f in zip(corners, (0.55, 0.0, 0.8, 0.65)):
        if f <= 0:
            continue
        top = (px + (cx - px) * f, py + (cy - py) * f, h + (az - h) * f * 0.8)
        m.beam(rng.choice(tones), (px, py, tall[corners.index((px, py))]), top, 0.12)
        for k in range(2):
            u = rng.uniform(0.3, 0.9)
            sx, sy = px + (top[0] - px) * u, py + (top[1] - py) * u
            sz = tall[corners.index((px, py))] + (top[2] - tall[corners.index((px, py))]) * u
            m.shard(rng.choice(("slate", "slate2")), sx, sy, sz - 0.3, 0.55, 0.45, rng.uniform(0, 3),
                    rng.uniform(0.8, 1.2), rng.uniform(-0.3, 0.3), t=0.04, rag=0.3, seed=k + int(px * 10))
    for k in range(4):
        m.shard(rng.choice(("slate", "slate2")), rng.uniform(x0 + 0.5, x1 - 0.5), rng.uniform(y0 + 0.5, y1 - 0.5),
                0.12, 0.5, 0.4, rng.uniform(0, 3), rng.uniform(-0.3, 0.3), 0.2, t=0.04, rag=0.3, seed=20 + k)
    char_heap(m, cx, cy - 0.2, 0.45, 0.15, rng, key="char2", embers=1)
    lean(m, tones, [((x0 + 0.3, y1 - 0.4, 0.1), (cx + 0.3, cy, 0.9)), ((x1 - 0.3, y0 + 0.5, 0.2), (cx - 0.4, cy + 0.6, 0.7)),
                    ((x0, y0 + 0.6, 1.2), (cx + 0.6, cy + 0.9, 0.3)), ((x1, y1 - 0.3, 1.5), (cx - 0.7, cy - 0.8, 0.2)),
                    ((x0 + 0.4, y0, 0.9), (x1 - 0.4, y1, 1.3)), ((cx, y0, 0.6), (cx + 0.3, y1, 1.1)),
                    ((x0, cy, 1.0), (x1, cy + 0.4, 0.5))], rng)
    for k in range(4):
        m.shard(rng.choice(("slate", "slate2")), rng.uniform(x0 + 0.4, x1 - 0.4), rng.uniform(y0 + 0.4, y1 - 0.4),
                rng.uniform(0.5, 1.1), 0.5, 0.4, rng.uniform(0, 3), rng.uniform(0.6, 1.2), rng.uniform(-0.3, 0.3), t=0.04,
                rag=0.3, seed=40 + k)
    m.cyl("log_a", m.X(16), m.Y(82, 0.14), 0.14, 0.14, 1.2, seg=6, rot=(0.05, math.pi / 2, 0.0))
    m.scatter(keys, max_area=60, skip=[(0, 0, 62, 80)])


@model("VerHut08a")
def verhut08a(m):
    import random
    m.sturdy(0.9)
    rng = random.Random(m.name)
    tones = char_tones(m)
    keys = m.ruin_palette()
    boards = plank_tones(m)
    # the weathered grey of the planks the fire spared, in the pile
    m.col("plank_a", (64, 60, 52))
    m.col("plank_b", (52, 50, 44))
    m.col("plank_c", (78, 74, 64))
    m.col("plank_d", (44, 40, 36))
    # the frame and the fallen panels charred nearly black, warm, patched darker
    m.col("burnt_a", (52, 41, 28), rough=1.0)
    m.col("burnt_b", (42, 33, 22), rough=1.0)
    m.col("burnt_c", (62, 49, 34), rough=1.0)
    m.col("black", (27, 21, 14), rough=1.0)
    m.col("char", (42, 36, 29), rough=1.0)
    m.col("char2", (54, 47, 38), rough=1.0)
    m.col("wood", (60, 51, 41), rough=1.0)
    m.col("ember8", (110, 45, 30), rough=1.0)
    m.col("sill8", (68, 60, 50), rough=1.0)
    m.col("sill_pale", (150, 138, 116))
    P = hut08(m, -1, -1)
    x0, x1, yf, yb = P["x0"], P["x1"], P["yf"], P["yb"]
    ym = (yf + yb) / 2
    frame_posts(m, x0, x1, yf, yb, 1.1, tones, rng, mid=1.3, low=0.5, plates=0.5)
    burnt = ("burnt_a", "burnt_b", "burnt_c", "black")
    boarding(m, x0, x1, yf, yb, 1.1, burnt, rng, share=0.4, top=(0.25, 0.6))
    # a row of charred boards still stands along the north side, leaning
    nb = int((x1 - x0) / 0.3)
    for k in range(nb):
        if rng.random() < 0.2:
            continue
        L = rng.uniform(0.95, 1.15)
        th = math.radians(rng.uniform(10, 24) if rng.random() < 0.75 else -rng.uniform(4, 12))
        x = x0 + (x1 - x0) * (k + 0.5) / nb + rng.uniform(-0.04, 0.04)
        m.obox(rng.choice(burnt + ("char2",)), x, yb + 0.12 + L / 2 * math.sin(th), L / 2 * math.cos(th), 0.27,
               0.07, L, rng.uniform(-0.08, 0.08), rng.uniform(-0.1, 0.1), -th)
    # the roof came down in four solid panels of boards, turned off the
    # square and lapped over one another, a few small embers in dark patches
    B = burnt + ("black",)
    xa, xb, xc = x0 + 0.1, x0 + (x1 - x0) * 0.45, x1 - 0.1
    panels = [((xa, yb - 0.1, 0.95), (xb, yb - 0.1, 0.9), (xb + 0.3, ym, 0.3), (xa, ym + 0.1, 0.35), 9, ()),
              ((xb + 0.4, yb - 0.1, 0.8), (xc, yb - 0.1, 0.95), (xc, ym + 0.2, 0.25), (xb + 0.5, ym - 0.1, 0.4), -12,
               ((0.35, 0.55), (0.75, 0.3))),
              ((xc, yf + 0.1, 0.9), (xb + 0.8, yf + 0.1, 0.7), (xb + 0.6, ym - 0.3, 0.3), (xc, ym - 0.2, 0.2), 7,
               ((0.5, 0.6),)),
              ((xb - 0.2, yf + 0.1, 0.6), (xa, yf + 0.1, 0.85), (xa, ym - 0.4, 0.45), (xb - 0.4, ym - 0.2, 0.2), -10,
               ((0.3, 0.45), (0.7, 0.7)))]
    for k, (e0, e1, t1, t0, yaw, embers) in enumerate(panels):
        Q = [kit.Vector(p) for p in (e0, e1, t1, t0)]
        C = sum(Q, kit.Vector((0, 0, 0))) / 4
        Q = [C + kit.Vector(((q - C).x * 1.2, (q - C).y * 1.2, (q - C).z)) for q in Q]
        with m.rot(C.x, C.y, math.radians(yaw)):
            under = [tuple(q - kit.Vector((0, 0, 0.04))) for q in Q]
            if kit._area([(p[0], p[1]) for p in under]) < 0:
                under = under[::-1]
            m.slab("black", under, 0.06)
            board_panel(m, B, *Q, ragged=0.35 if k == 3 else 0.2)
            for u, v in embers:
                p = Q[0].lerp(Q[1], u).lerp(Q[3].lerp(Q[2], u), v)
                m.shard("black", p.x, p.y, p.z + 0.035, 0.36, 0.3, rng.uniform(0, 3), t=0.03, rag=0.4, seed=10 + k)
                m.shard("ember8", p.x + 0.03, p.y, p.z + 0.06, rng.uniform(0.1, 0.16), rng.uniform(0.07, 0.11),
                        rng.uniform(0, 3), t=0.02, rag=0.45, n=6, seed=20 + k)
    # the blue ridge beam lies across it all
    m.beam("cap", (m.X(10), m.Y(25, 1.0), 1.0), (m.X(66), m.Y(56, 0.35), 0.35), 0.3, 0.2)
    # the heavy beam from the north-east corner down to the middle of the south side
    m.beam("char", (x1 - 0.1, yb - 0.15, 1.05), ((x0 + x1) / 2 - 0.1, yf + 0.2, 0.12), 0.3, 0.26)
    # the sill beam along the south side, its top edge worn pale
    m.beam("sill8", (x0 - 0.1, yf - 0.02, 0.14), (x1 + 0.1, yf - 0.02, 0.14), 0.3, 0.26)
    m.beam("sill_pale", (x0 - 0.05, yf - 0.15, 0.3), (x1 + 0.05, yf - 0.15, 0.3), 0.1, 0.05)
    char_heap(m, (x0 + x1) / 2 - 0.5, ym, 0.8, 0.2, rng, key="char2", embers=0)
    pile = boards + ("plank_d",)
    # the pile of thick planks by the south-west corner
    for (c0, r0), (c1, r1), z in (((6, 76), (34, 90), 0.06), ((14, 92), (50, 100), 0.06), ((26, 100), (48, 83), 0.18),
                                  ((4, 88), (28, 80), 0.18), ((18, 84), (44, 92), 0.3), ((30, 76), (22, 102), 0.36)):
        m.beam(rng.choice(pile), (m.X(c0), m.Y(r0, z), z), (m.X(c1), m.Y(r1, z), z), 0.3, 0.1)
    m.beam("post", (m.X(74), m.Y(99, 0.05), 0.05), (m.X(95), m.Y(79, 0.05), 0.05), 0.08)
    m.scatter(keys, max_area=60, skip=[(0, 70, 55, 107)])


@model("VerHut09a")
def verhut09a(m):
    import random
    m.sturdy(0.9)
    rng = random.Random(m.name)
    tones = char_tones(m)
    keys = m.ruin_palette()
    boards = plank_tones(m)
    m.col("plank_a", (72, 68, 58))
    m.col("plank_b", (58, 56, 48))
    m.col("plank_c", (86, 80, 68))
    T = m.rubble_tones(char=(44, 38, 32), tile=(120, 60, 40), wood=(80, 70, 56), stone=(150, 140, 120),
                       ash=(128, 118, 102))
    x0, x1, y0, y1, h = m.X(37), m.X(92), m.Y(100), m.Y(6, 1.5), 1.5
    # a few thick posts left standing, broken off at different heights
    for u, v, ph in ((0, 0, 0.8), (1, 0, 0.6), (1, 1, 1.35), (0, 1, 1.2), (0.5, 1, 0.7), (1, 0.55, 0.9), (0, 0.5, 0.5)):
        m.post(rng.choice(("char", "char2")), x0 + (x1 - x0) * u, y0 + (y1 - y0) * v, 0.0, h * ph / 1.5 * 1.1, 0.2,
               lean=rng.uniform(-0.08, 0.08))
    # the pale rubble of the south wall and floor heaped across the south third
    ys = y0 + (y1 - y0) / 3
    nz = m.noise("mound", 0.7)
    m.debris(T, [(x0 - 0.2, y0 - 0.2), (x1 + 0.1, y0 - 0.2), (x1 + 0.1, ys), (x0 - 0.2, ys)], 0.7,
             zfn=lambda x, y: max(0.0, 0.55 * math.sin(math.pi * min(1, max(0, (y - y0 + 0.2) / (ys - y0 + 0.2)))) *
                                  (1 + 0.3 * nz(x, y))),
             density=3.0, maxn=70, cell=0.4, mix=dict(stone=0.7, char=0.15, wood=0.15), size=(0.2, 0.45))
    # the roof fell in over the north half: panels of charred boards
    # tilted 20 to 40 degrees, timbers across them, little ground between
    m.col("pan_a", (38, 35, 29), rough=1.0)
    m.col("pan_b", (30, 28, 23), rough=1.0)
    m.col("pan_c", (46, 42, 35), rough=1.0)
    m.col("black", (16, 15, 13), rough=1.0)
    B = ("pan_a", "pan_b", "pan_c", "black")
    xm = (x0 + x1) / 2
    board_panel(m, B, (x0 + 0.05, ys + 0.15, 1.0), (x0 + 0.05, y1 - 0.1, 1.05), (xm + 0.1, y1 - 0.2, 0.2),
                (xm, ys + 0.2, 0.15), ragged=0.25)
    board_panel(m, B, (x1 - 0.05, y1 - 0.1, 1.1), (x1 - 0.05, ys + 0.1, 0.9), (xm - 0.2, ys + 0.3, 0.25),
                (xm - 0.1, y1 - 0.3, 0.3), ragged=0.25)
    board_panel(m, B, (x1 - 0.3, y1 - 0.05, 1.2), (x0 + 0.3, y1 - 0.05, 1.15), (x0 + 0.4, y1 - 1.6, 0.35),
                (x1 - 0.4, y1 - 1.5, 0.3), ragged=0.3)
    board_panel(m, B, (x0 + 0.4, ys + 0.1, 0.3), (x1 - 0.4, ys + 0.1, 0.35), (x1 - 0.6, ys + 1.3, 0.95),
                (x0 + 0.6, ys + 1.2, 0.9), ragged=0.3)
    import models_compound as mc
    mc.tangle(m, ("char", "char", "black", "char2"), rect(x0 + 0.2, x1 - 0.2, ys + 0.2, y1 - 0.2), 0.55, 1.0, 9,
              seed=9, L=(1.2, 2.2), w=(0.12, 0.17))
    # the blue ridge beam fell across, the rafters with it
    m.beam("cap", (m.X(41), m.Y(6, 1.4), 1.4), (m.X(66), m.Y(70, 0.35), 0.35), 0.3, 0.2)
    ym = (ys + y1) / 2
    lean(m, tones, [((x0, y1 - 0.5, h * 0.9), (x0 + 1.5, ym, 0.2)), ((x1, y1 - 1.0, h), (x1 - 1.6, ym + 0.3, 0.3)),
                    ((x1, ym, h * 0.8), (x0 + 2.0, ym - 0.8, 0.2)), ((x0 + 0.8, y1, h), (x0 + 1.4, ym - 0.5, 0.25))], rng)
    char_heap(m, (x0 + x1) / 2 + 0.3, ym + 0.4, 0.5, 0.15, rng, key="char2", embers=2)
    # timbers and boards spilled out past the west side
    for (c0, r0), (c1, r1), w, hh in (((5, 40), (30, 55), 0.3, 0.1), ((7, 45), (30, 60), 0.28, 0.1),
                                      ((15, 20), (40, 29), 0.14, 0.14), ((20, 72), (40, 80), 0.26, 0.1),
                                      ((12, 64), (30, 74), 0.14, 0.14), ((30, 88), (45, 95), 0.25, 0.08)):
        m.beam(rng.choice(boards + ("char2",)), (m.X(c0), m.Y(r0, 0.08), 0.08), (m.X(c1), m.Y(r1, 0.08), 0.12), w, hh)
    m.beam("ember", (m.X(76), m.Y(106, 0.05), 0.05), (m.X(97), m.Y(95, 0.05), 0.05), 0.09)
    m.scatter(keys, max_area=60, skip=[(0, 10, 45, 100)])


@model("VerHut10a")
def verhut10a(m):
    import random
    m.sturdy(0.9)
    rng = random.Random(m.name)
    tones = char_tones(m)
    keys = m.ruin_palette()
    m.col("board_a", (52, 48, 40))
    m.col("board_b", (42, 40, 34))
    m.col("board_c", (62, 58, 48))
    m.col("shard", (92, 108, 112))
    m.col("shard2", (72, 86, 92))
    x0, x1, y0, y1, h = m.X(6), m.X(80), m.Y(70), m.Y(6, 1.3), 1.3
    frame_posts(m, x0, x1, y0, y1, 1.0, tones, rng, mid=1.4, low=0.35, plates=0.3)
    B = ("board_a", "board_b", "board_c")
    # the roof fell in whole and lies in a heap of boards across the plan, turned on the diagonal
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    with m.rot(cx, cy, math.radians(-32)):
        board_panel(m, B, (cx - 2.4, cy - 1.3, 0.25), (cx + 2.4, cy - 1.3, 0.2), (cx + 2.4, cy + 0.1, 0.75),
                    (cx - 2.4, cy + 0.1, 0.7), ragged=0.35)
        board_panel(m, B, (cx + 2.3, cy + 1.5, 0.2), (cx - 2.3, cy + 1.5, 0.3), (cx - 2.3, cy + 0.1, 0.85),
                    (cx + 2.3, cy + 0.1, 0.8), ragged=0.35)
    lean(m, tones, [((x0, y1, 1.0), (cx - 0.3, cy + 0.2, 0.8)), ((x1, y0, 0.9), (cx + 0.5, cy - 0.3, 0.7)),
                    ((x0 + 0.3, y0 + 0.2, 0.15), (x0 + 1.8, y0 + 1.2, 0.5))], rng)
    for c, r in ((12, 50), (65, 62), (15, 10), (50, 32), (70, 70), (20, 55), (60, 52), (35, 15)):
        m.shard(rng.choice(("shard", "shard2")), m.X(c), m.Y(r, 0.6), rng.uniform(0.35, 0.8), 0.45, 0.35,
                rng.uniform(0, 3), rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), t=0.05, rag=0.3, seed=c)
    m.beam("wood", (m.X(1), m.Y(76, 0.05), 0.05), (m.X(20), m.Y(66, 0.05), 0.05), 0.12)
    m.scatter(keys, max_area=60, skip=[(0, 0, 83, 76)])
