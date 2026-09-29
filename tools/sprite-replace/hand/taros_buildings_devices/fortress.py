"""Tarbuild03 and its ruins: a broad Taros fortress. A flat roof paved in
red-veined cobbles, crenellated walks, six square towers under red
pyramid spires, two sunken light wells floored in red tile, a hipped keep
at the back under the tallest spire, and at the front a half-domed gate
behind a crenellated arc, open to a forecourt. Thorns stand out from both
side walls."""
import math
import random

from mathutils import Matrix, Vector

import kit as K

COBBLE = (70, 69, 70)
COBBLE_VEIN = (124, 16, 12)
RED = (62, 4, 3)
RED_DARK = (30, 2, 2)
STONE = (60, 59, 58)
STONE_DARK = (30, 30, 30)
KEEP = (54, 48, 47)
VAULT = (104, 104, 104)
THORN = (28, 28, 28)
PALE = (120, 118, 116)


def cobble():
    return K.texmat("fo_cobble", lambda: K.tex_cobble(COBBLE, COBBLE_VEIN, n=256, count=40, seed=61, var=0.3,
                                                       vein_w=4.2, dark=(70, 6, 4)), rough=0.9)


def red_tile():
    return K.texmat("fo_tile", lambda: K.tex_courses(RED, RED_DARK, n=128, rows=10, per_row=4, seed=62, var=0.2,
                                                      jw=1.6), rough=0.7)


def red_roof():
    return K.texmat("fo_redroof", lambda: K.tex_mottle(RED, RED_DARK, (84, 10, 8), n=64, seed=63, amt=0.6),
                    rough=0.6)


def stone():
    return K.texmat("fo_stone", lambda: K.tex_courses(STONE, STONE_DARK, n=128, rows=4, per_row=2, seed=64, var=0.2,
                                                       jw=2), rough=0.9)


def keep_mat():
    return K.texmat("fo_keep", lambda: K.tex_courses(KEEP, (28, 26, 26), n=128, rows=8, per_row=4, seed=65,
                                                      var=0.15, jw=1.5), rough=0.9)


def vault_mat():
    return K.texmat("fo_vault", lambda: K.tex_courses(VAULT, (70, 70, 70), n=128, rows=8, per_row=5, seed=66,
                                                       var=0.1, jw=1.5), rough=0.85)


def thorn_mat():
    return K.mat("fo_thorn", THORN, rough=0.6, metal=0.3)


def pale():
    return K.mat("fo_pale", PALE, rough=0.8)


# ---- parts ------------------------------------------------------------------------------

def sawteeth(p0, p1, z, t=0.55, tooth=0.5, gap=0.1, h=0.5, skip=None):
    """A row of pointed sawtooth teeth along p0-p1 standing on z; skip(f)
    leaves a tooth out."""
    p0v, p1v = Vector(p0[:2]), Vector(p1[:2])
    L = (p1v - p0v).length
    n = max(1, int(L / (tooth + gap)))
    ang = math.degrees(math.atan2(p1v.y - p0v.y, p1v.x - p0v.x))
    out = []
    for i in range(n):
        f = (i + 0.5) / n
        if skip is not None and skip(f):
            continue
        c = p0v.lerp(p1v, f)
        m = K.loft([K.rect(c.x, c.y, tooth, t * 0.85, ang, z), K.rect(c.x, c.y, 0.05, t * 0.5, ang, z + h)], stone(),
                   "tooth")
        K.two_tone(m, pale(), nz=0.3)
        K.uv_box(m, 0.5)
        out.append(m)
    return out


def parapet(p0, p1, z, h=0.55, t=0.55, merlon=0.5, gap=0.1, height=None, name="parapet"):
    """A walk wall p0-p1 on z crested with sawtooth teeth; height(t) < h
    breaks it down."""
    parts = K.wall_run(p0, p1, z, h if height is None else height, t, stone(), seg=0.6, name=name)
    for p in parts:
        K.uv_box(p, 0.5)
    parts += sawteeth(p0, p1, z + h, t, merlon, gap, skip=None if height is None else (lambda f: height(f) < h * 0.9))
    return parts


def corner_horns(cx, cy, w, d, z, rim=0.45, size=1.0):
    """Four horns flaring up and out from a tower top's corners."""
    out = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = cx + sx * (w / 2 - rim / 2), cy + sy * (d / 2 - rim / 2)
            out.append(K.tube([(x - sx * 0.1, y - sy * 0.1, z), (x + sx * 0.12, y + sy * 0.12, z + 0.55 * size),
                               (x + sx * 0.4, y + sy * 0.4, z + 1.05 * size)], [0.34, 0.21, 0.0], seg=5, sub=2,
                              mt=stone(), name="corner_horn", shade=40))
    for p in out:
        K.uv_box(p, 0.5)
    return out


def tower(cx, cy, w, d, z0, h, spire=3.5, rim=0.45, broken=None, seed=1, spire_w=None):
    """A square tower with a raised rim, horned at its corners, round a
    cobbled top under a steep red spire. broken=(depth, seed) opens its
    top: the spire gone, the horns snapped and the rim torn."""
    parts = []
    body = K.block(w, d, h, cx, cy, z0, 0, stone(), "tower")
    K.two_tone(body, cobble())
    K.uv_box(body, 1 / 3.0)
    parts.append(body)
    zt = z0 + h
    rnd = random.Random(seed)
    for (ax, ay), (bx, by) in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
        pa = (cx + ax * (w / 2 - rim / 2), cy + ay * (d / 2 - rim / 2))
        pb = (cx + bx * (w / 2 - rim / 2), cy + by * (d / 2 - rim / 2))
        hf = 0.35 if broken is None else (lambda t: rnd.choice((0.0, 0.1, 0.35, 0.35)))
        parts += K.wall_run(pa, pb, zt, hf, rim, stone(), seg=0.5, name="tower_rim")
    if broken is None:
        parts += corner_horns(cx, cy, w, d, zt + 0.2, rim)
        if spire:
            # a little lower than the picture's, so the spires stand squat in 3D
            sw, sd = spire_w or (w - 2 * rim + 0.1, d - 2 * rim + 0.1)
            parts.append(K.pyramid(cx, cy, zt, sw, sd, spire * SPIRE_RISE, 0, red_roof(), "spire"))
    else:
        # the spire's stump in a crater of its own red and black rubble
        parts.append(K.pyramid(cx, cy, zt, w - 2 * rim + 0.1, d - 2 * rim + 0.1, 0.5, 0, red_roof(), "spire_stump"))
        for k in range(6):
            a = rnd.uniform(0, 6.28)
            rr = rnd.uniform(0.2, min(w, d) / 2 - rim)
            parts.append(K.boulder((cx + rr * math.cos(a), cy + rr * math.sin(a), zt + 0.1),
                                   (rnd.uniform(0.35, 0.6), rnd.uniform(0.3, 0.5), rnd.uniform(0.25, 0.45)),
                                   rnd.randrange(10 ** 6), K.mat("fo_black", (12, 12, 12), rough=1.0) if k % 2
                                   else red_roof(), "crater_stone", seg=6))
    for p in parts[1:]:
        if not p.data.uv_layers:
            K.uv_box(p, 0.5)
    return parts


def well(cx, cy, w, d, z0, h=0.6):
    """A light well: a stepped stone frame round a sunken red tile floor."""
    parts = []
    for i, (inset, hh) in enumerate(((0.0, h), (0.45, h * 0.6))):
        ww, dd = w - 2 * inset, d - 2 * inset
        t = 0.45
        for (ax, ay, bx, by) in ((-1, -1, 1, -1), (1, -1, 1, 1), (1, 1, -1, 1), (-1, 1, -1, -1)):
            pa = (cx + ax * (ww / 2 - t / 2), cy + ay * (dd / 2 - t / 2))
            pb = (cx + bx * (ww / 2 - t / 2), cy + by * (dd / 2 - t / 2))
            parts += K.wall_run(pa, pb, z0, hh, t, stone(), name="well_frame")
    floor = K.block(w - 1.7, d - 1.7, 0.06, cx, cy, z0, 0, red_tile(), "well_floor")
    K.uv_box(floor, 0.5)
    parts.append(floor)
    for p in parts:
        K.uv_box(p, 0.5)
    return parts


def thorn(x, y, z, side, length=1.3):
    """A dark thorn standing out from a side wall and hooking down."""
    return K.tube([(x, y, z), (x + side * length * 0.55, y - 0.1, z + 0.15), (x + side * length, y - 0.25, z - 0.45)],
                  [0.3, 0.18, 0.0], seg=5, sub=2, mt=thorn_mat(), name="thorn", flat=0.6)


def finial(x, y, z, h=1.6):
    """A dark iron post with a spiked knob and a point, on a wall or the ground."""
    mt = thorn_mat()
    parts = [K.block(0.56, 0.56, h * 0.6, x, y, z, 45, mt, "finial"),
             K.lathe([(0, 0), (0.38, 0.05), (0.43, 0.25), (0.24, 0.45), (0, h * 0.4 + 0.2)], seg=6, mt=mt, x=x, y=y,
                     z=z + h * 0.6, name="finial_top")]
    for k in range(4):
        a = math.radians(45 + 90 * k)
        zz = z + h * 0.6 + 0.2
        parts.append(K.tube([(x + 0.25 * math.cos(a), y + 0.25 * math.sin(a), zz),
                             (x + 0.62 * math.cos(a), y + 0.62 * math.sin(a), zz + 0.12)], [0.1, 0.0], seg=4, sub=1,
                            mt=mt, name="finial_spike", shade=None))
    return parts


def apse(cx, cy, r, h, seg=12, rings=5, mt=None, depth=None):
    """A half dome open toward -y, r wide each side and depth deep: its
    crown at the front edge, falling away to the back and sides."""
    mt = mt or vault_mat()
    depth = r if depth is None else depth
    rows = []
    for i in range(rings + 1):
        phi = (math.pi / 2) * i / rings  # 0 at the base, pi/2 at the crown
        rr = r * math.cos(phi)
        zz = h * math.sin(phi)
        if rr < 1e-3:
            rows.append([(cx + 0.0, cy, zz)] * (seg + 1))
            continue
        rows.append([(cx + rr * math.cos(math.pi * k / seg), cy + rr * depth / r * math.sin(math.pi * k / seg), zz)
                     for k in range(seg + 1)])
    verts, faces = [], []
    for row in rows:
        verts += row
    n = seg + 1
    for i in range(rings):
        for k in range(seg):
            a, b = i * n + k, i * n + k + 1
            c, d = (i + 1) * n + k + 1, (i + 1) * n + k
            faces.append([a, b, c, d])
    ob = K.mesh("apse", verts, faces, mt, smooth=50)
    # make sure the dome faces up and out
    me = ob.data
    for p in me.polygons:
        c = p.center
        if p.normal.dot(Vector((c.x - cx, (c.y - cy) * r / depth, c.z))) < 0:
            p.flip()
    K.uv_box(ob, 0.5)
    return ob


def arc_points(cx, cy, r, a0, a1, n):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n)))
            for i in range(n + 1)]


def curved_wall(cx, cy, r, a0, a1, z0, h, t, mt, top=None, n=6, name="curved_wall"):
    """A wall standing along an arc of a circle."""
    outer = arc_points(cx, cy, r + t / 2, a0, a1, n)
    inner = arc_points(cx, cy, r - t / 2, a1, a0, n)
    ob = K.prism(outer + inner, z0, z0 + h, mt, name)
    if top is not None:
        K.two_tone(ob, top)
    K.uv_box(ob, 0.5)
    return ob


# ---- the fortress ----------------------------------------------------------------------

Z0 = 2.5  # the main roof
SPIRE_RISE = 0.9
GX = -0.15  # the gate's middle
BODY = [(-9.6, 6.3), (9.6, 6.3), (9.6, -7.9), (2.1, -7.9), (2.1, -3.1), (-2.4, -3.1), (-2.4, -7.9), (-9.6, -7.9)]
TOWERS = {  # name: (cx, cy, w, d, h above the roof, spire)
    "back_left": (-7.85, 6.35, 4.0, 3.2, 1.2, 3.5),
    "back_right": (8.55, 6.2, 3.2, 3.4, 1.2, 3.5),
    "front_left": (-8.6, -6.7, 3.1, 2.8, 0.6, 3.5),
    "front_right": (8.7, -7.4, 2.9, 3.2, 0.6, 3.5),
    "inner_left": (-4.85, -5.35, 3.1, 3.5, 0.8, 3.5),
    "inner_right": (5.1, -5.6, 3.1, 3.5, 0.8, 3.5),
}
WELLS = [(-5.1, 0.55, 3.25, 4.6), (5.5, 0.1, 3.4, 4.4)]
KEEP_AT = (0.05, 2.9)  # the hipped roof's middle
KEEP_TOP = 3.5
KEEP_TOWER = (0.1, 3.75, 3.6, 2.6, 4.8)  # the small tower behind it: middle, size, top


def oct_ring(cx, cy, w, d, c, z):
    """An octagon w x d with corners cut back by c, at height z."""
    return [(cx - w / 2 + c, cy - d / 2, z), (cx + w / 2 - c, cy - d / 2, z), (cx + w / 2, cy - d / 2 + c, z),
            (cx + w / 2, cy + d / 2 - c, z), (cx + w / 2 - c, cy + d / 2, z), (cx - w / 2 + c, cy + d / 2, z),
            (cx - w / 2, cy + d / 2 - c, z), (cx - w / 2, cy - d / 2 + c, z)]


def keep(tower_up=True):
    """The keep: a low octagonal hipped roof, and behind it at the top a
    small square tower, horned at its corners, under a steep red spire."""
    kx, ky = KEEP_AT
    parts = []
    k = K.loft([oct_ring(kx, ky, 6.1, 5.8, 1.9, Z0), oct_ring(kx, ky, 6.1, 5.8, 1.9, Z0 + 0.1),
                oct_ring(kx, ky + 0.5, 3.0, 2.2, 0.7, KEEP_TOP)], keep_mat(), "keep")
    K.uv_box(k, 0.5)
    parts.append(k)
    if tower_up:
        tx, ty, tw, td, tz = KEEP_TOWER
        parts += tower(tx, ty, tw, td, Z0 - 0.1, tz - Z0 + 0.1, 5.0 * K.RISE / SPIRE_RISE, rim=0.4,
                       spire_w=(2.3 * K.GIRTH, 1.7 * K.GIRTH))
    return parts


def gate(dome=2.2, stubs=True, arc_keep=None):
    """The gate: a half dome over an arched opening at the back of the
    forecourt, curved wall stubs at its mouth, and a wide arc wall crested
    with sawteeth sweeping over it between the inner front towers."""
    parts = []
    gy = -5.0
    if dome:
        r = 2.25
        parts.append(apse(GX, gy, r, dome, depth=3.3))
        inner = apse(GX, gy + 0.02, r - 0.2, dome - 0.2, mt=K.mat("fo_dark", (14, 14, 14), rough=1.0), depth=3.1)
        for p in inner.data.polygons:
            p.flip()
        parts.append(inner)
    if stubs:
        for scx, a0, a1 in ((-2.7, 245, 290), (2.4, 250, 295)):
            parts.append(curved_wall(scx, -3.0, 4.7, a0, a1, 0, 2.2, 0.5, vault_mat(), top=pale(), n=6,
                                     name="gate_stub"))
    # the arc: a crested wall between the inner towers
    pts = arc_points(GX + 0.35, -5.9, 5.0, 44, 136, 16)
    for i in range(len(pts) - 1):
        if arc_keep is not None and not arc_keep(i / (len(pts) - 1)):
            continue
        parts += parapet(pts[i], pts[i + 1], Z0, h=0.75, t=0.65, merlon=0.4, gap=0.06, name="arc")
    return parts


def fortress(towers_broken=None, wells_broken=None, keep_tower=True, parapet_height=None, arc_keep=None,
             gate_dome=2.2, thorns=True, seed=1, walks_gone=(), finials_keep=None, stubs=True):
    """The fortress; the arguments let the ruins take pieces away."""
    towers_broken = towers_broken or {}
    parts = []
    body = K.prism(BODY, 0, Z0, stone(), "body")
    K.two_tone(body, cobble())
    K.uv_box(body, 1 / 3.0)
    parts.append(body)
    ph = parapet_height or (lambda name: None)
    # sawtoothed walks along both sides, the back, and the short cross walls
    walks = {
        "left": ((-7.9, -4.2), (-7.9, 4.5)), "right": ((7.8, -4.3), (7.8, 4.4)),
        "back_l": ((-5.8, 5.4), (-2.9, 5.4)), "back_r": ((3.4, 5.4), (6.6, 5.4)),
        "cross_ul": ((-7.6, 4.0), (-5.2, 4.0)), "cross_ur": ((5.3, 3.7), (7.5, 3.7)),
        "cross_ll": ((-7.6, -1.9), (-5.9, -1.9)), "cross_lr": ((5.2, -2.2), (7.2, -2.2)),
    }
    for nm, (a, b) in walks.items():
        if nm in walks_gone:
            continue
        parts += parapet(a, b, Z0, height=ph(nm))
    # outer rims along the side edges
    for x in (-9.3, 9.3):
        parts += K.wall_run((x, -4.5), (x, 4.8), Z0, 0.35, 0.6, stone(), name="rim")
    for nm, (cx, cy, w, d, h, sp) in TOWERS.items():
        dmg = towers_broken.get(nm)
        if dmg == "gone":
            continue
        parts += tower(cx, cy, w, d, 0, Z0 + h, sp, broken=dmg, seed=seed + len(nm))
    for i, (cx, cy, w, d) in enumerate(WELLS):
        if wells_broken and i in wells_broken:
            continue
        parts += well(cx, cy, w, d, Z0)
    parts += keep(keep_tower)
    parts += gate(gate_dome, stubs, arc_keep)
    fk = finials_keep or (lambda x, y: True)
    for i, y in enumerate([4.1, 2.6, 1.0, -0.6, -2.2, -3.8]):
        for side in (-1, 1):
            if thorns is True or (thorns and thorns(side, i)):
                parts.append(thorn(side * 9.6, y - (0.3 if side > 0 else 0), Z0 - 0.4, side))
    for x in (-6.75, -4.5, 4.0, 6.7):
        if fk(x, -7.6):
            parts += finial(x, -8.1, 0, 1.6)
    for x in (-4.3, -2.1, 2.3, 4.5):
        if fk(x, 5.9):
            parts += finial(x, 5.9, Z0, 1.8)
    return parts


def tarbuild03(name="Tarbuild03"):
    return fortress()
