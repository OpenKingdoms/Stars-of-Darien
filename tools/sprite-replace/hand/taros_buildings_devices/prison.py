"""Tarbuild04 and its ruin: the Taros prison. A tall tower at the back
left and a keep with a chimney, both roofed in mossy flagstones inside
crenellated parapets and horned with pale tusks; a lower front wing, a
thick spiked wall and a front gatehouse round a tiled courtyard with a
well, barrels, a breaking wheel on its pole and two board racks, shuttered
windows looking onto it; a gabled lodge at the front left, a second
tower at the front right, a shaded walled terrace to the right and
barrels stacked outside. No cast shadow is modelled: the game lights it."""
import math
import random

import numpy as np

from mathutils import Matrix, Vector

import kit as K

FLAG = (98, 88, 80)
FLAG_JOINT = (34, 30, 24)
MOSS = (58, 70, 34)
WALL = (88, 88, 68)
WALL_JOINT = (30, 30, 20)
TILE = (104, 86, 56)
TILE_JOINT = (40, 32, 20)
LODGE = (70, 56, 42)
IRON = (128, 128, 132)
BONE = (150, 138, 108)
WOOD = (56, 42, 30)
HOOP = (30, 28, 26)
WATER = (40, 84, 76)
RACK = (92, 88, 78)
SHUTTER = (74, 46, 30)
TERRACE = (34, 32, 26)


def flag():
    return K.texmat("pr_flag", lambda: K.tex_cobble(FLAG, FLAG_JOINT, n=256, count=26, seed=71, var=0.25, vein_w=3.0),
                    rough=0.9)


def wall():
    return K.texmat("pr_wall", lambda: K.tex_courses(WALL, WALL_JOINT, n=256, rows=10, per_row=5, seed=72, var=0.25,
                                                      jw=2.5, moss=MOSS, moss_amt=0.35), rough=0.92)


def tile():
    return K.texmat("pr_tile", lambda: K.tex_courses(TILE, TILE_JOINT, n=128, rows=6, per_row=6, seed=73, var=0.12,
                                                      jw=2.0), rough=0.85)


def lodge_roof():
    return K.texmat("pr_lodge", lambda: K.tex_planks(LODGE, (24, 18, 12), n=128, count=10, seed=74), rough=0.9)


def terrace():
    return K.texmat("pr_terrace", lambda: K.tex_cobble(TERRACE, (14, 12, 10), n=128, count=20, seed=75, var=0.2,
                                                        vein_w=2.5), rough=0.95)


def dark_top():
    return K.texmat("pr_darktop", lambda: K.tex_cobble((44, 42, 34), (16, 14, 10), n=128, count=18, seed=76,
                                                        var=0.2, vein_w=2.5), rough=0.95)


def iron():
    return K.mat("pr_iron", IRON, rough=0.45, metal=0.15, spec=0.5)


def bone():
    return K.mat("pr_bone", BONE, rough=0.6)


def wood():
    return K.mat("pr_wood", WOOD, rough=0.85)


def hoop():
    return K.mat("pr_hoop", HOOP, rough=0.5, metal=0.4)


def grate_mat():
    return K.mat("pr_grate", (18, 16, 14), rough=0.8, metal=0.3)


# ---- parts ------------------------------------------------------------------------------

def crenels(p0, p1, z, t=0.55, h=0.3, merlon=0.75, gap=0.5, keep=None, name="crenel"):
    """A parapet walk: a low wall p0-p1 on z with merlons along it."""
    p0, p1 = Vector(p0[:2]), Vector(p1[:2])
    L = (p1 - p0).length
    ang = math.degrees(math.atan2(p1.y - p0.y, p1.x - p0.x))
    n = max(1, int(round(L / (merlon + gap))))
    parts = []
    if keep is None:
        parts += K.wall_run(p0, p1, z, h, t, wall(), top_mt=flag(), name=name)
    else:
        parts += K.wall_run(p0, p1, z, lambda f: h if keep(f) else 0.0, t, wall(), seg=1.0, top_mt=flag(), name=name)
    for i in range(n):
        f = (i + 0.5) / n
        if keep is not None and not keep(f):
            continue
        c = p0.lerp(p1, f)
        parts.append(K.block(merlon, t * 1.02, 0.45, c.x, c.y, z + h, ang, wall(), "merlon"))
    for p in parts:
        K.uv_box(p, 0.5)
    return parts


def ruined_block(x0, x1, y0, y1, h, cut, roof, cell=1.0, seed=1, name="block"):
    """A block whose top is broken down by cut(x, y) cells: a solid base
    under the deepest break, then columns of masonry to their own torn
    heights, the broken ones topped with dark rubble; unbroken columns in
    a row are joined."""
    rnd = random.Random(seed)
    nx, ny = max(1, int(round((x1 - x0) / cell))), max(1, int(round((y1 - y0) / cell)))
    cw, cd = (x1 - x0) / nx, (y1 - y0) / ny
    tops = [[h - cut(x0 + cw * (i + 0.5), y0 + cd * (j + 0.5)) for i in range(nx)] for j in range(ny)]
    tops = [[t - (rnd.uniform(0.05, 0.4) if t < h - 0.05 else 0.0) for t in row] for row in tops]
    base = min(min(r) for r in tops) - 0.2
    b = K.block(x1 - x0, y1 - y0, base, (x0 + x1) / 2, (y0 + y1) / 2, 0, 0, wall(), name)
    K.two_tone(b, dark_top())
    K.uv_box(b, 1 / 2.5)
    parts = [b]
    for j, row in enumerate(tops):
        i = 0
        while i < nx:
            k = i + 1
            while k < nx and abs(row[k] - row[i]) < 1e-6:
                k += 1
            if row[i] > base + 0.05:
                c = K.block(cw * (k - i), cd, row[i] - base, x0 + cw * (i + k) / 2, y0 + cd * (j + 0.5), base, 0,
                            wall(), name + "_broken")
                K.two_tone(c, roof if row[i] >= h - 1e-6 else dark_top())
                K.uv_box(c, 1 / 2.5)
                parts.append(c)
            i = k
    return parts


def blockhouse(x0, x1, y0, y1, h, edges="fblr", roof=None, keep=None, name="block", cut=None):
    """A block of walling with a flagstone roof and crenellated edges;
    cut(x, y) breaks its top down, and only unbroken edges keep merlons."""
    t = 0.55
    if cut is None:
        b = K.block(x1 - x0, y1 - y0, h, (x0 + x1) / 2, (y0 + y1) / 2, 0, 0, wall(), name)
        K.two_tone(b, roof or flag())
        K.uv_box(b, 1 / 2.5)
        parts = [b]
    else:
        parts = ruined_block(x0, x1, y0, y1, h, cut, roof or flag(), seed=int(abs(x0 * 7 + y0 * 3)) + 1, name=name)
    runs = {"f": ((x0, y0 + t / 2), (x1, y0 + t / 2)), "b": ((x1, y1 - t / 2), (x0, y1 - t / 2)),
            "l": ((x0 + t / 2, y1), (x0 + t / 2, y0)), "r": ((x1 - t / 2, y0), (x1 - t / 2, y1))}
    for e in edges:
        a, c = runs[e]
        if cut is None:
            kf = None if keep is None else (lambda f, e=e: keep(e, f))
        else:
            def kf(f, e=e, a=a, c=c):
                x, y = a[0] + (c[0] - a[0]) * f, a[1] + (c[1] - a[1]) * f
                return cut(x, y) <= 0.0 and (keep is None or keep(e, f))
        parts += crenels(a, c, h, keep=kf)
    return parts


def tusk(x, y, z, dx, dy, h=2.8 * K.RISE):
    """A pale horn at a parapet corner, sweeping out and up, thicker and
    lower than the picture's."""
    return K.tube([(x, y, z - 0.4), (x + dx * 0.45, y + dy * 0.45, z + h * 0.45), (x + dx * 0.8, y + dy * 0.8, z + h)],
                  [0.74, 0.48, 0.0], seg=6, sub=3, mt=bone(), name="tusk")


def grate(x, y, z, w=1.4, d=1.0):
    parts = [K.block(w, d, 0.06, x, y, z, 0, grate_mat(), "grate")]
    for i in range(4):
        parts.append(K.block(0.07, d, 0.1, x - w / 2 + w * (i + 0.5) / 4, y, z + 0.04, 0, iron(), "bar"))
    return parts


def spikes(p0, p1, z, n, h=0.9, lean=(0, -0.3), seed=1):
    """A row of iron spikes set along a wall top."""
    rnd = random.Random(seed)
    p0, p1 = Vector(p0[:2]), Vector(p1[:2])
    out = []
    for i in range(n):
        c = p0.lerp(p1, (i + 0.5) / n)
        hh = h * rnd.uniform(0.75, 1.15)
        lx, ly = lean[0] + rnd.uniform(-0.2, 0.2), lean[1] + rnd.uniform(-0.2, 0.2)
        out.append(K.loft([K.rect(c.x, c.y, 0.26, 0.26, 45, z), [(c.x + lx, c.y + ly, z + hh)]], iron(), "spike"))
    return out


def staves():
    """Barrel staves with two dark iron hoops round them."""
    def make():
        img = K.tex_planks(WOOD, (22, 16, 10), n=128, count=12, seed=78)
        for v in (0.18, 0.78):
            a, b = int(v * 128), int(v * 128) + 10
            img[a:b] = K._col(HOOP)
        return img
    return K.texmat("pr_staves", make, rough=0.8)


def barrel(x, y, z=0.0, r=0.42, h=1.0, lying=False, yaw=0.0):
    b = K.lathe([(0, 0), (r * 0.85, 0), (r, h * 0.5), (r * 0.85, h), (0, h)], seg=10, mt=staves(), name="barrel",
                smooth=50)
    me = b.data
    uv = me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = ((math.atan2(co.y, co.x) / (2 * math.pi)) % 1.0, 1 - co.z / h)
    parts = [b]
    M = Matrix.Translation((x, y, z))
    if lying:
        M = M @ Matrix.Rotation(math.radians(yaw), 4, "Z") @ Matrix.Translation((0, 0, r)) \
            @ Matrix.Rotation(math.pi / 2, 4, "Y") @ Matrix.Translation((0, 0, -h / 2))
    for p in parts:
        K.place(p, M)
    return parts


def well(x, y, r=0.95):
    """A round stone well: a thick lipped kerb with two red-brown bands and
    teal water standing high inside."""
    stone = K.lathe([(0, 0), (r, 0), (r, 0.9), (r - 0.25, 0.9), (r - 0.25, 0.7), (0, 0.7)], seg=16, mt=wall(),
                    name="well", x=x, y=y)
    K.uv_box(stone, 0.5)
    water = K.cyl(r - 0.24, 0.05, seg=16, x=x, y=y, z=0.72, mt=K.mat("pr_water", WATER, rough=0.1, spec=0.7),
                  name="water")
    band = K.mat("pr_trim", (90, 34, 26))
    parts = [stone, water]
    for z in (0.3, 0.62):
        parts.append(K.torus(r + 0.02, 0.07, seg=12, rseg=3, x=x, y=y, z=z, mt=band, name="trim"))
    parts.append(K.torus(r - 0.12, 0.13, seg=14, rseg=3, x=x, y=y, z=0.92, mt=wall(), name="kerb"))
    return parts


def breaking_wheel(x, y, h=4.4 * K.RISE, r=0.75):
    """A spoked cartwheel laid flat on top of a stout pole, on a stone foot."""
    wd = wood()
    parts = [K.block(0.7, 0.7, 0.25, x, y, 0, 0, wall(), "wheel_foot"),
             K.block(0.26 * K.GIRTH, 0.26 * K.GIRTH, h, x, y, 0.2, 0, wd, "wheel_pole")]
    rim = K.torus(r, 0.08, seg=16, rseg=3, x=x, y=y, z=h + 0.12, mt=wd, name="wheel_rim")
    hub = K.cyl(0.18, 0.22, seg=8, x=x, y=y, z=h, mt=hoop(), name="wheel_hub")
    parts += [rim, hub]
    for i in range(4):
        parts.append(K.block(2 * r, 0.08, 0.07, x, y, h + 0.09, 45 * i, wd, "wheel_spoke"))
    for p in parts:
        K.uv_box(p, 0.5)
    return parts


def rack(foot, top, w=0.55, slats=4):
    """A board rack leaning where it was left: two rails and cross slats."""
    foot, top = Vector(foot), Vector(top)
    T = (top - foot).normalized()
    S = T.cross(Vector((0, 0, 1)))
    if S.length < 1e-4:
        S = Vector((1, 0, 0))
    S.normalize()
    N = S.cross(T)
    mt = K.mat("pr_rack", RACK, rough=0.85)
    parts = []
    for s in (-1, 1):
        o = S * s * w / 2
        parts.append(K.board(foot + o, top + o, 0.09, 0.09, mt, "rack_rail", up=tuple(N)))
    for i in range(slats):
        c = foot.lerp(top, (i + 0.6) / (slats + 0.2))
        parts.append(K.board(c - S * (w / 2 + 0.05), c + S * (w / 2 + 0.05), 0.2, 0.05, mt, "rack_slat", up=tuple(N)))
    return parts


def window(x, yf, z, w=0.8, h=0.95):
    """A small shuttered window on a wall face at y = yf, looking toward
    -y: a dark barred opening in a brown frame, both shutters swung open,
    a little peaked hood over it."""
    sh = K.mat("pr_shutter", SHUTTER, rough=0.85)
    parts = [K.block(w, 0.16, h, x, yf - 0.06, z - h / 2, 0, sh, "win_frame"),
             K.block(w - 0.22, 0.05, h - 0.22, x, yf - 0.15, z - h / 2 + 0.11, 0, grate_mat(), "win_hole")]
    for i in range(2):
        bx = x - (w - 0.22) / 2 + (w - 0.22) * (i + 1) / 3
        parts.append(K.block(0.05, 0.05, h - 0.22, bx, yf - 0.18, z - h / 2 + 0.11, 0, iron(), "win_bar"))
    for s in (-1, 1):
        hx = x + s * w / 2
        a = math.radians(55)
        parts.append(K.board((hx, yf - 0.1, z - h / 2 + 0.05), (hx, yf - 0.1, z + h / 2 - 0.05), 0.36, 0.05, sh,
                             "win_shutter", up=(0, -1, 0)))
        K.rotz(parts[-1], -s * 55, (hx, yf - 0.1, 0))
    parts.append(K.pyramid(x, yf - 0.12, z + h / 2, w + 0.2, 0.36, 0.34, 0, sh, "win_hood"))
    for p in parts:
        K.uv_box(p, 0.5)
    return parts


def wheel(c, r, axis_yaw, mt):
    w = K.torus(r, 0.06, seg=16, rseg=4, mt=mt, name="wheel")
    parts = [w]
    for i in range(6):
        sp = K.block(r * 2, 0.06, 0.06, 0, 0, -0.03, i * 30, mt, "spoke")
        parts.append(sp)
    M = Matrix.Translation(c) @ Matrix.Rotation(math.radians(axis_yaw), 4, "Z") @ Matrix.Rotation(math.pi / 2, 4, "Y")
    for p in parts:
        K.place(p, M)
    return parts


def cart(x, y, yaw):
    """A two-wheeled cart with its shafts down on the flags."""
    parts = []
    body = K.block(1.4, 2.2, 0.5, 0, 0, 0.55, 0, wood(), "cart")
    parts.append(body)
    for s in (-1, 1):
        parts += wheel((s * 0.85, 0.1, 0.62), 0.62, 0, K.mat("pr_wheel", (44, 34, 26)))
        parts.append(K.board((s * 0.45, -1.1, 0.7), (s * 0.35, -3.3, 0.08), 0.12, 0.12, wood(), "shaft"))
    M = Matrix.Translation((x, y, 0)) @ Matrix.Rotation(math.radians(yaw), 4, "Z")
    for p in parts:
        K.place(p, M)
    return parts


def lodge(x0, x1, y0, y1, h, ridge, roof=True):
    """A gabled lodge, its gable end to the front; roof False leaves a
    shell with broken rafters."""
    parts = []
    b = K.block(x1 - x0, y1 - y0, h, (x0 + x1) / 2, (y0 + y1) / 2, 0, 0, wall(), "lodge")
    K.uv_box(b, 0.5)
    parts.append(b)
    cx = (x0 + x1) / 2
    o = 0.3
    if not roof:
        for i, (a, b) in enumerate((((x0, y0 + 0.4, h), (cx + 0.3, y0 + 0.5, ridge - 0.6)),
                                    ((x1, y0 + 1.4, h), (cx - 0.2, y0 + 1.2, ridge - 0.9)),
                                    ((x0, y1 - 0.3, h), (cx, y1 - 0.5, h + 0.3)))):
            parts.append(K.board(a, b, 0.18, 0.14, wood(), "rafter"))
        return parts
    for s in (-1, 1):
        xe = x0 - o if s < 0 else x1 + o
        pts = [(xe, y0 - o, h - 0.25), (xe, y1 + o, h - 0.25), (cx, y1 + o, ridge), (cx, y0 - o, ridge)]
        slab = K.loft([pts, [(p[0], p[1], p[2] + 0.18) for p in pts]], lodge_roof(), "lodge_roof")
        K.uv_box(slab, 0.5)
        parts.append(slab)
    gab = K.mesh("gable", [(x0, y0, h), (x1, y0, h), (cx, y0, ridge)], [[0, 1, 2]], wall())
    K.uv_box(gab, 0.5)
    parts.append(gab)
    return parts


# ---- the prison ----------------------------------------------------------------------

# the tall blocks' heights, a little under the picture's so the towers
# stand broad rather than tall in 3D (read off it: 12.5, 10, 8)
T1_H = 11.6
KEEP_H = 9.4
T2_H = 7.6

def prison(damage=None, seed=1):
    d = damage or {}
    parts = []
    gone = d.get("gone", ())

    def keep_fn(block):
        k = d.get("crenels", {}).get(block)
        return None if k is None else (lambda e, f: k(e, f))

    def cut_fn(block):
        return d.get("cuts", {}).get(block)

    # the tall tower, back left
    if "t1" not in gone:
        parts += blockhouse(-10.3, -3.7, 3.25, 8.7, d.get("t1_h", T1_H), keep=keep_fn("t1"), name="tower1",
                            cut=cut_fn("t1"))
        h1 = d.get("t1_h", T1_H)
        if cut_fn("t1") is None or cut_fn("t1")(-6.6, 6.4) <= 0:
            parts += grate(-6.6, 6.4, h1)
        # two shuttered windows high on its front face
        for wx in (-8.1, -5.9):
            parts += window(wx, 3.25, h1 - 1.9)
        tk = d.get("t1_tusks", (0, 1, 2))
        for i, (x, y, dx, dy) in enumerate(((-10.1, 8.5, -0.5, 0.4), (-3.9, 8.5, 0.5, 0.4), (-3.9, 3.4, 0.6, -0.3))):
            if i in tk:
                parts.append(tusk(x, y, h1 + 0.5, dx, dy))
    # the keep: a high block with a chimney and a lower shoulder to its left
    hk_ = d.get("keep_h", KEEP_H)
    parts += blockhouse(0.3, 5.2, -2.8, 7.2, hk_, edges="blr", keep=keep_fn("keep"), name="keep", cut=cut_fn("keep"))
    parts += blockhouse(-3.7, 0.3, 3.6, 8.3, 7.0, edges="b", keep=keep_fn("shoulder"), name="shoulder",
                        cut=cut_fn("shoulder"))
    if "chimney" not in gone:
        ch = K.block(1.1, 1.0, 2.2, 3.7, 6.4, hk_, 0, wall(), "chimney")
        K.uv_box(ch, 0.5)
        parts.append(ch)
    parts += grate(1.9, 2.0, hk_) + grate(3.3, -0.2, hk_, 0.8, 0.8)
    # the front wing across the back of the courtyard
    parts += blockhouse(-7.1, 0.3, 3.1, 4.2, 5.0, edges="f", keep=keep_fn("wing"), name="wing", cut=cut_fn("wing"))
    # shuttered windows along its courtyard face
    for wx in (-6.3, -4.6, -2.6, -0.6):
        parts += window(wx, 3.1, 2.5)
    # the thick left wall with its iron spikes
    parts += blockhouse(-10.3, -7.6, -5.2, 3.1, 5.0, edges="", roof=dark_top(), name="left_wall")
    if "left_spikes" not in gone:
        parts += spikes((-9.4, -4.2), (-9.4, 3.0), 5.0, 18, 1.4, lean=(-0.5, 0.0), seed=seed)
        parts += spikes((-9.9, -4.0), (-9.9, 2.8), 5.0, 14, 1.2, lean=(-0.6, -0.1), seed=seed + 7)
    # the courtyard
    floor = K.block(7.9, 9.3, 0.05, -3.65, -1.55, 0, 0, tile(), "yard")
    K.uv_box(floor, 1 / 1.5)
    parts.append(floor)
    parts += well(-0.45, 1.35, 0.78)
    if "yard_props" not in gone:
        for (x, y, lying, yaw) in ((-5.6, 2.3, False, 0), (-4.7, 2.5, False, 0), (-5.3, 1.3, True, 20),
                                   (-4.4, 0.5, True, 70)):
            parts += barrel(x, y, lying=lying, yaw=yaw)
    # the breaking wheel high on its pole, and the board racks by the keep's wall
    parts += breaking_wheel(-4.2, -2.3)
    parts += rack((-1.5, -1.05, 0.0), (-2.0, -0.45, 1.75))
    parts += rack((-0.45, -2.7, 0.0), (-1.3, -2.35, 1.05), slats=3)
    if "yard_props" not in gone:
        parts.append(K.block(0.9, 0.8, 0.7, -3.4, 2.4, 0, 12, wood(), "crate"))
    # the front gatehouse wall with spikes along its face
    parts += blockhouse(-7.1, 0.3, -7.75, -6.2, 3.6, edges="", roof=dark_top(), name="gatehouse")
    if "front_spikes" not in gone:
        parts += spikes((-6.6, -7.0), (0.0, -7.0), 3.6, 18, 1.4, lean=(0, -0.5), seed=seed + 1)
    # the gabled lodge, front left
    parts += lodge(-10.5, -7.1, -7.75, -5.2, 5.5, 7.2, roof="lodge_roof" not in gone)
    # the front tower, right
    h2 = d.get("t2_h", T2_H)
    parts += blockhouse(0.3, 6.1, -7.75, -2.8, h2, keep=keep_fn("t2"), name="tower2", cut=cut_fn("t2"))
    if cut_fn("t2") is None or cut_fn("t2")(3.2, -4.8) <= 0:
        parts += grate(3.2, -4.8, h2)
    t2k = d.get("t2_tusks", (0, 1))
    for i, (x, y, dx, dy) in enumerate(((6.0, -3.0, 0.6, 0.4), (6.0, -7.5, 0.7, -0.3))):
        if i in t2k:
            parts.append(tusk(x, y, h2 + 0.5, dx, dy))
    # the walled terrace to the right, in the keep's shade
    parts += blockhouse(5.2, 9.5, -4.5, 8.3, 4.0, edges="rb", roof=terrace(), keep=keep_fn("terrace"),
                        name="terrace")
    # barrels stacked outside, front right
    for (x, y, lying, yaw) in ((6.9, -5.0, False, 0), (7.8, -4.9, False, 0), (7.3, -6.0, True, 80),
                               (8.6, -5.9, False, 0), (8.2, -7.1, True, 20)):
        parts += barrel(x, y, lying=lying, yaw=yaw)
    parts.append(K.board((9.0, -4.8, 0.3), (9.6, -6.4, 0.9), 0.25, 0.08, wood(), "plank"))
    return parts


def tarbuild04(name="Tarbuild04"):
    return prison()


# ---- the ruin -------------------------------------------------------------------------

def ruin_rubble():
    """Broken grey-brown masonry: squared stones with dark gaps and only a
    faint green tint of moss."""
    def make():
        img = K.tex_boulders((56, 53, 42), (8, 7, 5), (86, 82, 66), n=256, count=120, seed=77, var=0.3)
        rng = K._rng(78)
        m = K.fbm(256, rng, ((8, 0.5), (16, 0.3), (32, 0.2)))
        return K._mix(img, img * 0 + K._col((58, 66, 42)), np.clip((m - 0.6) * 2, 0, 0.25))
    return K.texmat("pr_ruin", make, rough=0.95)


def rubble_stones():
    return [K.mat("pr_stone_a", (62, 59, 48), rough=0.9), K.mat("pr_stone_b", (42, 40, 32), rough=0.9),
            K.mat("pr_stone_c", (54, 54, 42), rough=0.9), wall()]


def beams(heaps, count, seed=1):
    """Dark timbers bedded at all angles in the heaps: heaps are
    (surface fn, inside fn, (x0, x1, y0, y1)) triples."""
    rnd = random.Random(seed)
    mt = [K.mat("pr_timber", (62, 46, 32), rough=0.85), K.mat("pr_timber_b", (84, 62, 42), rough=0.85)]
    out = []
    k = 0
    while len(out) < count and k < count * 40:
        k += 1
        fn, ins, (x0, x1, y0, y1) = heaps[len(out) % len(heaps)]
        x, y = rnd.uniform(x0, x1), rnd.uniform(y0, y1)
        if not ins(x, y):
            continue
        L, a = rnd.uniform(1.4, 2.8), rnd.uniform(0, math.pi)
        dx, dy = math.cos(a) * L / 2, math.sin(a) * L / 2
        if not (ins(x - dx * 0.8, y - dy * 0.8) and ins(x + dx * 0.8, y + dy * 0.8)):
            continue
        # sunk into the stones, one end often sticking up out of them
        za, zb = fn(x - dx, y - dy) - 0.05, fn(x + dx, y + dy) - 0.05
        if rnd.random() < 0.5:
            za += rnd.uniform(0.3, 0.8)
        b = K.board((x - dx, y - dy, za), (x + dx, y + dy, zb), rnd.uniform(0.24, 0.32), rnd.uniform(0.18, 0.24),
                    mt[len(out) % 2], "beam", up=(rnd.uniform(-0.5, 0.5), rnd.uniform(-0.5, 0.5), 1))
        out.append(b)
    return out


def _break(cx, cy, depth, radius, floor=0.0, seed=1, keep=None):
    """A cut function: the top broken down depth cells at (cx, cy), less
    further out, to nothing at radius (never less than floor), with a
    torn edge; keep(x, y) holds a spot whole."""
    rnd = random.Random(seed)
    ph = [rnd.uniform(0, 6.3) for _ in range(4)]

    def cut(x, y):
        if keep is not None and keep(x, y):
            return 0.0
        d = math.hypot(x - cx, y - cy) / radius
        d *= 1 + 0.18 * math.sin(3 * math.atan2(y - cy, x - cx) + ph[0]) + 0.1 * math.sin(2.3 * x + ph[1])
        v = depth * max(0.0, 1 - d) ** 0.8
        v = max(v, floor * (1 + 0.5 * math.sin(1.9 * x + ph[2]) * math.cos(1.7 * y + ph[3])))
        return v if v > 0.12 else 0.0
    return cut


def tarbuild04a(name="Tarbuild04a"):
    def ragged(seed, cut):
        rnd = random.Random(seed)
        return lambda e, f: not (e in cut and cut[e][0] < f < cut[e][1]) and rnd.random() > 0.15

    def parapet_gone(seed):
        # the front tower's parapet knocked off but for a few merlons in ones and twos
        rnd = random.Random(seed)
        return lambda e, f: e in "fr" and rnd.random() > 0.6

    def back_left(x, y):
        return (y > 7.3 and x < -5.8) or (x > -4.6 and y > 7.7)

    cuts = {"t1": _break(-4.3, 3.9, 3.0, 5.6, seed=1, keep=back_left),
            "keep": _break(0.5, 0.8, 4.0, 3.3, seed=2),
            "shoulder": _break(-1.2, 4.4, 2.2, 2.9, seed=3),
            "wing": _break(-0.6, 3.5, 1.6, 2.2, seed=4),
            "t2": _break(0.9, -3.3, 2.0, 3.8, seed=5)}
    damage = {"gone": ("lodge_roof", "yard_props"), "t1_tusks": (1,), "t2_tusks": (0,), "cuts": cuts,
              "crenels": {"t1": ragged(1, {"r": (0.0, 1.0), "f": (0.0, 1.0)}),
                          "keep": ragged(2, {"l": (0.0, 1.0), "b": (0.5, 1.0)}),
                          "wing": ragged(3, {"f": (0.5, 1.0)}), "shoulder": ragged(4, {"b": (0.0, 1.0)}),
                          "t2": parapet_gone(5), "terrace": ragged(6, {})}}
    parts = prison(damage)
    # the ground the fallen masonry lies on: each block's broken top
    blocks = [(-10.3, -3.7, 3.25, 8.7, T1_H, "t1"), (0.3, 5.2, -2.8, 7.2, KEEP_H, "keep"),
              (-3.7, 0.3, 3.6, 8.3, 7.0, "shoulder"), (-7.1, 0.3, 3.1, 4.2, 5.0, "wing"),
              (0.3, 6.1, -7.75, -2.8, T2_H, "t2"), (-10.3, -7.6, -5.2, 3.1, 5.0, None),
              (-7.1, 0.3, -7.75, -6.2, 3.6, None), (5.2, 9.5, -4.5, 8.3, 4.0, None)]

    def ground(x, y):
        z = 0.0
        for x0, x1, y0, y1, h, nm in blocks:
            if x0 <= x <= x1 and y0 <= y <= y1:
                z = max(z, h - (cuts[nm](x, y) if nm else 0.0) - 0.2)
        return z

    # the slope of broken masonry: from the tall tower's torn top down over
    # the shoulder and the wing, through the keep's broken left end and
    # down into the courtyard against the keep's wall
    slope = [(-5.4, 4.9, 1.9, 1.8, 0.8, 30), (-3.5, 5.0, 1.5, 1.7, 1.6, 0, (-0.5, 0.0)),
             (-1.4, 4.3, 1.9, 1.5, 1.4, 20), (-0.9, 2.1, 1.9, 1.7, 2.9, 0, (0.3, 0.6)),
             (1.3, 1.0, 1.5, 2.3, 1.0, 10), (-0.7, -0.6, 1.3, 2.4, 2.0, 0, (0.7, 0.0)),
             (1.3, -3.4, 1.3, 1.0, 0.7, 0)]
    rub, stones = ruin_rubble(), rubble_stones()
    p, fn0, ins0 = K.boulder_heaps(slope, rub, stones, ground, seed=80, per_area=0.45, size=(0.4, 0.8), rings=4,
                                   ring_seg=11, power=1.1)
    parts += p

    def over(x, y):
        # the ground under the tower's rubble: the highest broken course
        # nearby, so the heaps bury the stepped cut instead of tracing it
        return max(ground(x + dx, y + dy) for dx in (-0.6, 0.0, 0.6) for dy in (-0.6, 0.0, 0.6))

    # mossy rubble heaped over the whole of the tall tower's broken top
    top = [(-8.6, 7.45, 1.35, 0.95, 0.8), (-6.0, 7.45, 1.5, 0.95, 0.9), (-8.6, 5.25, 1.35, 1.35, 1.0),
           (-5.8, 5.2, 1.7, 1.55, 1.3, 0, (0.3, -0.3)), (-7.2, 6.3, 1.6, 1.3, 1.1)]
    p, fn1, ins1 = K.boulder_heaps(top, rub, stones, over, seed=84, per_area=0.8, size=(0.5, 0.9), rings=5,
                                   ring_seg=12, power=1.1)
    parts += p
    # the courtyard's right half buried under a slope of masonry falling
    # from the keep's broken end, lowest out toward the middle of the yard
    yard = [(-1.7, 1.25, 2.2, 1.45, 2.6, 0, (0.8, 0.3)), (-1.9, -1.0, 2.0, 1.45, 1.8, 0, (0.8, 0.0)),
            (-3.2, 2.2, 1.1, 0.85, 0.7)]
    p, fn2, ins2 = K.boulder_heaps(yard, rub, stones, ground, seed=88, per_area=0.6, size=(0.45, 0.85), rings=4,
                                   ring_seg=12, power=1.1)
    parts += p

    def fn(x, y):
        return max(fn0(x, y) if ins0(x, y) else 0.0, fn1(x, y) if ins1(x, y) else 0.0,
                   fn2(x, y) if ins2(x, y) else 0.0)

    def ins(x, y):
        return ins0(x, y) or ins1(x, y) or ins2(x, y)

    lying = [(fn, ins, (-7.3, 2.8, -4.5, 6.8))]
    tm = K.mat("pr_timber", (62, 46, 32), rough=0.85)
    # beams jutting up out of the tower's rubble, and lying across the yard's
    for (ax, ay), (bx, by), up in (((-7.6, 6.1), (-6.3, 4.5), 1.3), ((-8.9, 6.9), (-9.8, 5.3), 1.0),
                                   ((-5.9, 7.3), (-4.5, 8.0), 1.1), ((-6.8, 5.0), (-7.9, 3.8), 0.9)):
        parts.append(K.board((ax, ay, fn(ax, ay) - 0.3), (bx, by, fn(ax, ay) + up), 0.3, 0.22, tm, "beam"))
    for (ax, ay), (bx, by) in (((-3.7, 0.5), (-0.9, 1.7)), ((-3.2, -1.7), (-1.0, 0.3)), ((-2.3, 2.9), (-0.5, 2.0))):
        parts.append(K.board((ax, ay, fn(ax, ay) - 0.05), (bx, by, fn(bx, by) - 0.05), 0.3, 0.22, tm, "beam"))
    # the yard's crate and one barrel half buried, the other barrels tipped over
    crate = K.block(0.9, 0.8, 0.7, 0, 0, -0.35, 0, wood(), "crate")
    K.place(crate, Matrix.Translation((-3.3, 2.3, fn(-3.3, 2.3) - 0.1)) @ Matrix.Rotation(math.radians(20), 4, "Z")
            @ Matrix.Rotation(math.radians(18), 4, "Y") @ Matrix.Rotation(math.radians(-10), 4, "X"))
    parts.append(crate)
    parts += barrel(-2.9, 1.2, fn(-2.9, 1.2) - 0.5, lying=True, yaw=35)
    for (x, y, yaw) in ((-5.6, 2.2, 30), (-5.3, 1.2, 20), (-4.4, 0.4, 70)):
        parts += barrel(x, y, lying=True, yaw=yaw)
    # small heaps at the foot of the walls outside
    p, _, _ = K.boulder_heaps([(-10.8, -1.2, 0.6, 1.6, 0.5, 0, (-0.3, 0.0)), (-10.6, -8.1, 0.8, 0.5, 0.4, 25),
                               (2.3, -8.3, 1.2, 0.6, 0.5, 0, (0.0, -0.3)), (9.9, 1.5, 0.6, 1.4, 0.45, 0, (0.3, 0.0)),
                               (-4.2, -8.3, 1.0, 0.5, 0.4)], rub, stones, None, seed=95, per_area=0.5,
                              size=(0.3, 0.55), rings=3, ring_seg=12)
    parts += p
    parts += beams(lying, 18, seed=91)
    return parts
