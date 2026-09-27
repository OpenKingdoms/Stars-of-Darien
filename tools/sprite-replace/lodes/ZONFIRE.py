"""ZONFIRE, the Zhon small sacred fire: seven standing stones in a ring
around a star of logs, the fire burning low at the front left of the pit
and licking out over the logs there.

    blender -b --factory-startup --python tools/sprite-replace/lodes/ZONFIRE.py
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import misckit as mk  # noqa: E402
from misckit import hk  # noqa: E402

NAME = "ZONFIRE"
PIC = r"D:\OKReplace\lodes\sprites\ZONFIRE.png"
HOT = (27, 39)

hk.reset()
# picture colours: cream stone highlights, mid grey faces, dark sides; a
# reddish brown bark going to char; fire from white-yellow to red
stone = mk.vmat("zf_stone", (184, 180, 156), rough=0.9)
bark = mk.vmat("zf_bark", (150, 100, 58), rough=0.92)
grain = mk.vmat("zf_endgrain", (196, 150, 92), rough=0.85)
ash = mk.vmat("zf_ash", (104, 98, 90), rough=1.0)
ember = mk.vmat("zf_ember", (236, 120, 36), rough=0.8, emit=(1.0, 0.22, 0.02), strength=1.6)
crust = mk.vmat("zf_crust", (40, 34, 30), rough=1.0)
fl_core = mk.vmat("zf_flame_core", (255, 250, 215), rough=0.6, emit=(1.0, 0.9, 0.55), strength=1.25)
fl_mid = mk.vmat("zf_flame", (255, 222, 70), rough=0.6, emit=(1.0, 0.62, 0.06), strength=1.25)
fl_tip = mk.vmat("zf_flame_tip", (244, 136, 36), rough=0.6, emit=(1.0, 0.3, 0.03), strength=1.2)

parts = []
rnd = random.Random(7)
N = 7
FIRE = Vector((-0.3, -0.5, 0.0))  # the heart of the fire, front left of the pit

CREAM, GREY, SIDE, SHADE, CRACK, FOOT = ((178, 176, 156), (140, 139, 132), (110, 109, 104), (82, 81, 77),
                                         (52, 51, 48), (40, 39, 36))


def stone_colour(st, k):
    """Speckle per corner: each vertex a random grey, a few dark flecks and
    cracks, cream on the lit top, the body a shade darker, darkest at the foot."""
    r = random.Random(500 + k)
    me = st.data
    rv = [r.random() for _ in me.vertices]
    fleck = [r.random() for _ in me.vertices]

    def fn(p, co, n, li):
        vi = me.loops[li].vertex_index
        u, f = rv[vi], fleck[vi]
        if n.z > 0.5:
            c = mk.mixc(CREAM, GREY, u * 0.9)
            if f < 0.12:
                c = mk.mixc(GREY, CRACK, 0.4 + 0.5 * u)
        elif n.z < -0.35:
            c = FOOT
        else:
            c = mk.mixc(SIDE, SHADE, u)
            if f < 0.1:
                c = CRACK
            elif f > 0.93:
                c = mk.mixc(GREY, CREAM, u * 0.5)
            if co.z < 0.2:
                c = mk.mixc(c, FOOT, 0.6 * (1.0 - co.z / 0.2))
        return c
    return fn


def stone_piece(k, a, R, W, L, h, lean):
    st = mk.rock(W, L, h, cuts=2, rough=0.035, seed=11 + k, mat=stone, name="stone%d" % k, taper=0.14)
    # a broken, sloping top: higher on one side, chipped at the corners
    sl = rnd.uniform(-0.12, 0.12)
    for v in st.data.vertices:
        if v.co.z > h * 0.6:
            v.co.z += sl * v.co.x / W + rnd.uniform(-0.035, 0.02)
    hk.bevel(st, width=0.05, segments=1)
    # chips: knock a few top corners and edges in
    cr = random.Random(90 + k)
    for v in st.data.vertices:
        if v.co.z > h * 0.55 and (abs(v.co.x) > W * 0.36 or abs(v.co.y) > L * 0.36) and cr.random() < 0.3:
            v.co.z -= cr.uniform(0.03, 0.09)
            v.co.x *= 0.95
            v.co.y *= 0.95
    mk.jitter(st, 0.012, seed=70 + k)
    mk.xform(st, rot=(-lean, rnd.uniform(-4, 4), a - 90.0 + rnd.uniform(-5, 5)), order="XYZ")
    mk.xform(st, loc=(R * math.cos(math.radians(a)), R * math.sin(math.radians(a)), -0.04))
    hk.smooth(st, 32)
    return mk.vpaint(st, stone_colour(st, k))


# the stone ring, long sides pointing out, leaning out a little; angles and
# distances measured off the picture
RING = ((92, 1.40), (45, 1.30), (-16, 1.31), (-69, 1.40), (-114, 1.42), (-164, 1.32), (140, 1.32))
for k, (a, R) in enumerate(RING):
    south = math.sin(math.radians(a)) < -0.5
    W = 0.46 + rnd.uniform(-0.03, 0.03)
    L = 0.68 + (0.06 if south else 0.0) + rnd.uniform(-0.03, 0.03)
    h = 0.72 if k == 0 else 0.64 + rnd.uniform(-0.04, 0.06)
    parts.append(stone_piece(k, a, R, W, L, h, 8.0 + rnd.uniform(-2, 3)))

# the ash bed: dark in the middle, grey ash out to the rim
bed = mk.lathe([(0.0, 0.035), (0.3, 0.032), (0.55, 0.025), (0.74, 0.0)], seg=20, mat=ash, name="pit")
ar = random.Random(4)
mk.vpaint(bed, lambda p, co, n, li: mk.mixc((70, 58, 46), (100, 92, 82),
                                              math.hypot(co.x, co.y) / 0.7 + ar.uniform(-0.25, 0.25)))
parts.append(bed)

BARK, BARK_DK, RIDGE, CHAR, CHAR_HOT = (142, 92, 52), (98, 62, 34), (162, 110, 66), (30, 25, 20), (70, 30, 14)
GRAIN, GRAIN_DK, HEART = (196, 150, 92), (150, 104, 58), (116, 76, 42)


def log(p0, p1, r0, r1, seed, bend=0.04, seg=7, rings=7, knots=1, end=True, name="log"):
    """A tapering, slightly bent log with faceted bark, a knot or two and
    split end grain at p0; charred toward the fire."""
    r = random.Random(seed)
    P0, P1 = Vector(p0), Vector(p1)
    D = P1 - P0
    T = D.normalized()
    side = T.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-3 else Vector((1, 0, 0))
    up = side.cross(T).normalized()
    b1, b2 = r.uniform(-1, 1) * bend, r.uniform(-1, 1) * bend * 0.6
    path = [P0 + D * t + up * (b1 * math.sin(math.pi * t) * D.length) + side * (b2 * math.sin(2 * math.pi * t))
            for t in [i / (rings - 1) for i in range(rings)]]
    prof = [(math.cos(2 * math.pi * j / seg) * r.uniform(0.86, 1.08),
             math.sin(2 * math.pi * j / seg) * r.uniform(0.86, 1.08)) for j in range(seg)]
    scales = [(r0 + (r1 - r0) * (i / (rings - 1))) * r.uniform(0.93, 1.05) for i in range(rings)]
    ob = mk.sweep(path, prof, side=(0, 0, 1), scales=scales, mat=bark, name=name)
    ob.data.materials.append(grain)
    # the p0 cap: an inset ring of pale wood round a darker heart, one split
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    cap = min((f for f in bm.faces if len(f.verts) == seg), key=lambda f: (f.calc_center_median() - P0).length)
    cap.material_index = 1
    res = bmesh.ops.inset_individual(bm, faces=[cap], thickness=r0 * 0.12, depth=-r0 * 0.06)
    for f in res["faces"]:
        f.material_index = 1
    poked = bmesh.ops.poke(bm, faces=[cap])
    for f in poked["faces"]:
        f.material_index = 1
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
    crack = r.randrange(seg)
    hk.smooth(ob, 38)
    fire_xy = FIRE.xy
    strips = [r.random() for _ in range(seg)]
    noise = [r.random() for _ in ob.data.vertices]

    def fn(p, co, n, li):
        if p.material_index == 1:
            if len(p.vertices) == 3 and p.index % seg == crack:
                return (46, 30, 18)  # the split
            d = (co - P0).length / r0
            c = mk.mixc(HEART, GRAIN, min(1.0, d * 1.5))
            if d > 0.95:
                c = BARK_DK
            return c
        near = (co.xy - fire_xy).length
        mid = co.xy.length
        # 1 in the fire and the pit's middle, fading out along the log
        burn = max(1.0 - (near - 0.4) / 0.6, 1.0 - (mid - 0.22) / 0.6)
        vi = ob.data.loops[li].vertex_index
        strip = strips[(vi % seg)]
        c = mk.mixc(BARK, BARK_DK, strip * 0.8 + noise[vi] * 0.3)
        if strip < 0.15:
            c = RIDGE
        if n.z < -0.2:
            c = mk.mixc(c, BARK_DK, 0.6)
        c = mk.mixc(c, CHAR, burn + noise[vi] * 0.3 - 0.15)
        if burn > 0.55 and noise[vi] > 0.9:
            c = CHAR_HOT
        return c
    mk.vpaint(ob, fn)
    out = [ob]
    for kn in range(knots):
        t = r.uniform(0.25, 0.6)
        a = r.uniform(0, 2 * math.pi)
        base = P0 + D * t + up * (b1 * math.sin(math.pi * t) * D.length)
        rr = r0 + (r1 - r0) * t
        dirn = (up * math.cos(a) + side * math.sin(a)).normalized()
        k = mk.rod(tuple(base + dirn * rr * 0.6), tuple(base + dirn * (rr + 0.05) + T * 0.02), rr * 0.4, rr * 0.22,
                   seg=5, mat=bark, name=name + "knot")
        mk.vpaint(k, lambda p, co, n, li: BARK_DK if n.dot(dirn) < 0.7 else (70, 44, 26))
        out.append(k)
    return out


# the logs: one in each gap between stones, their inner ends propped on each
# other over the pit
for k in range(N):
    a0, a1 = RING[k][0], RING[(k + 1) % N][0]
    mid_a = (a0 + ((a1 - a0 + 180.0) % 360.0 - 180.0) / 2.0)
    g = math.radians(mid_a + rnd.uniform(-5, 5))
    rr = 0.13 + rnd.uniform(-0.015, 0.015)
    r_out = 1.22 + rnd.uniform(-0.06, 0.1)
    if k == 0:
        rr, r_out = 0.17, 1.5
    r_in = 0.12 + rnd.uniform(0.0, 0.06)
    z_in = 0.42 + rnd.uniform(0.0, 0.16)
    p0 = (r_out * math.cos(g), r_out * math.sin(g), rr * 0.9)
    p1 = (r_in * math.cos(g), r_in * math.sin(g), z_in)
    parts += log(p0, p1, rr, rr * 0.7, seed=40 + k, knots=1 if k % 2 else 2, name="log%d" % k)
# two logs laid across the star
for k, (a, off) in enumerate(((168, 0.25), (20, -0.2))):
    g = math.radians(a)
    c, sn = math.cos(g), math.sin(g)
    ox, oy = -sn * off, c * off
    p0 = (ox - c * 0.8, oy - sn * 0.8, 0.14)
    p1 = (ox + c * 0.8, oy + sn * 0.8, 0.3)
    parts += log(p0, p1, 0.12, 0.1, seed=60 + k, knots=1, name="cross%d" % k)
# kindling sticks leaning in
for k, a in enumerate((150, 35, 255)):
    g = math.radians(a)
    p0 = (0.75 * math.cos(g), 0.75 * math.sin(g), 0.05)
    p1 = (0.05 * math.cos(g), 0.05 * math.sin(g), 0.62)
    parts += log(p0, p1, 0.05, 0.03, seed=80 + k, seg=5, rings=4, knots=0, name="stick%d" % k)

# the ember bed: flat angular chips set into the ash under the fire, black
# crust between them
er = random.Random(11)
for k in range(60):
    a = er.uniform(0, 2 * math.pi)
    d = er.uniform(0.0, 1.0) ** 0.7
    x = FIRE.x + 0.14 + d * 0.7 * math.cos(a)
    y = FIRE.y + 0.24 + d * 0.56 * math.sin(a)
    if math.hypot(x, y) > 0.66:
        continue
    hot = er.random() < 0.6
    s = er.uniform(0.04, 0.075)
    chip = mk.rock(s * er.uniform(1.0, 1.8), s * er.uniform(0.8, 1.4), er.uniform(0.025, 0.05), cuts=0,
                   rough=s * 0.25, seed=200 + k, mat=ember if hot else crust, name="chip%d" % k)
    mk.xform(chip, rot=(er.uniform(-12, 12), er.uniform(-12, 12), er.uniform(0, 180)), loc=(x, y, -0.01))
    mk.flat_shade(chip)
    if hot:
        cr = random.Random(300 + k)
        mk.vpaint(chip, lambda p, co, n, li, cr=cr: mk.mixc((96, 18, 5), (220, 96, 26), cr.random() ** 1.5)
                  if n.z > 0.4 else (70, 14, 4))
    parts.append(chip)


C0, C1, C2, C3 = (255, 250, 215), (255, 214, 56), (240, 128, 26), (180, 40, 12)


def fire_colour(t):
    if t < 0.3:
        return mk.mixc(C0, C1, t / 0.3)
    if t < 0.5:
        return C1
    if t < 0.78:
        return mk.mixc(C1, C2, (t - 0.5) / 0.28)
    return mk.mixc(C2, C3, (t - 0.78) / 0.22)


def tongue(base, h, w, lean, curl, twist, seed, rings=7, seg=6, name="tongue"):
    """A thin flame tongue: a flattened, twisting section swept up a curved
    path, swelling low and drawn out to a point, white-yellow at the root
    and red at the tip."""
    r = random.Random(seed)
    bx, by, bz = base
    lx, ly = lean
    ll = math.hypot(lx, ly) or 1.0
    px, py = -ly / ll, lx / ll
    ph = r.uniform(0, math.pi)
    verts, ts = [], []
    for k in range(rings):
        t = k / rings
        cx = bx + lx * t ** 1.5 + px * curl * math.sin(1.6 * math.pi * t)
        cy = by + ly * t ** 1.5 + py * curl * math.sin(1.6 * math.pi * t)
        cz = bz + h * t
        rad = w * (0.6 + 1.7 * t) * (1.0 - t) ** 1.15
        ang = ph + math.radians(twist) * t
        for j in range(seg):
            a = 2 * math.pi * j / seg
            u, v = math.cos(a) * rad, math.sin(a) * rad * 0.42
            verts.append((cx + u * math.cos(ang) - v * math.sin(ang), cy + u * math.sin(ang) + v * math.cos(ang),
                          cz + r.uniform(-0.01, 0.01) * h))
            ts.append(t)
    tip = len(verts)
    verts.append((bx + lx + px * curl * math.sin(1.6 * math.pi), by + ly + py * curl * math.sin(1.6 * math.pi),
                  bz + h))
    ts.append(1.0)
    faces = []
    for k in range(rings - 1):
        for j in range(seg):
            j2 = (j + 1) % seg
            faces.append((k * seg + j, k * seg + j2, (k + 1) * seg + j2, (k + 1) * seg + j))
    last = (rings - 1) * seg
    for j in range(seg):
        faces.append((last + j, last + (j + 1) % seg, tip))
    faces.append(tuple(range(seg - 1, -1, -1)))
    ob = mk.mesh(name, verts, faces)
    mk._fix_normals(ob)
    for m in (fl_core, fl_mid, fl_tip):
        ob.data.materials.append(m)
    refs = [m["ref"] for m in (fl_core, fl_mid, fl_tip)]
    for p in ob.data.polygons:
        tm = sum(ts[i] for i in p.vertices) / len(p.vertices)
        p.material_index = 0 if tm < 0.26 else 1 if tm < 0.7 else 2
    me = ob.data
    at = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for p in me.polygons:
        ref = refs[p.material_index]
        for li in p.loop_indices:
            c = fire_colour(ts[me.loops[li].vertex_index])
            at.data[li].color = tuple(min(1.0, mk.lin(c[i]) / max(1e-4, mk.lin(ref[i]))) for i in range(3)) + (1.0,)
    return hk.smooth(ob, 60)


# the fire: a body of tongues over the front left of the pit drawing in
# toward each other as they rise, the tallest in the middle, and low ones
# at its edge licking out over the logs
fr = random.Random(5)
spots = []
while len(spots) < 17:
    u, v = fr.uniform(-1, 1), fr.uniform(-1, 1)
    if u * u + v * v > 0.6:
        continue
    spots.append((u, v))
spots.sort(key=lambda s: s[0] ** 2 + s[1] ** 2)
for k, (u, v) in enumerate(spots):
    x = FIRE.x + u * 0.72
    y = FIRE.y + v * 0.46
    d = math.hypot(u, v) / 0.78
    h = max(0.45, min(1.1, (1.15 - 0.6 * d) * fr.uniform(0.82, 1.0)))
    w = (0.22 - 0.07 * d) * fr.uniform(0.85, 1.15)
    lean = (-u * 0.14 * h + fr.uniform(-0.1, 0.1), -v * 0.12 * h + fr.uniform(-0.08, 0.08))
    z0 = 0.03 + fr.uniform(0.0, 0.05)
    tw = fr.choice((-1, 1)) * fr.uniform(70, 160)
    parts.append(tongue((x, y, z0), h, w, lean, fr.uniform(0.04, 0.1), tw, seed=k + 3, rings=6,
                        seg=6 if w > 0.12 else 5, name="tongue%d" % k))
for k in range(8):
    # licks round the front and left of the patch, out over the logs
    ar = math.radians(105 + k * 26 + fr.uniform(-8, 8))
    u, v = math.cos(ar), math.sin(ar)
    x = FIRE.x + u * 0.72 * fr.uniform(0.85, 1.0)
    y = FIRE.y + v * 0.46 * fr.uniform(0.85, 1.0)
    h = fr.uniform(0.3, 0.5)
    lean = (u * 0.16 + fr.uniform(-0.05, 0.05), v * 0.12 + fr.uniform(-0.04, 0.04))
    parts.append(tongue((x, y, 0.12 + fr.uniform(0.0, 0.1)), h, fr.uniform(0.08, 0.12), lean, 0.04,
                        fr.choice((-1, 1)) * 90, seed=60 + k, rings=5, seg=5, name="edge%d" % k))
# the small flame on the east log, and one behind among the back logs
for k, (base, h, w, lean) in enumerate((((0.45, 0.0, 0.2), 0.36, 0.08, (0.08, 0.02)),
                                         ((0.36, 0.08, 0.22), 0.24, 0.06, (0.05, -0.02)),
                                         ((-0.32, 0.6, 0.36), 0.3, 0.07, (0.0, -0.03)))):
    parts.append(tongue(base, h, w, lean, 0.02, 80, seed=40 + k, rings=5, seg=5, name="lick%d" % k))
# red embers on the east log's outer half
for k in range(4):
    e = mk.rock(0.05, 0.035, 0.03, cuts=0, rough=0.01, seed=400 + k, mat=ember, name="glint%d" % k)
    mk.xform(e, rot=(0, 0, 30 * k), loc=(0.96 + 0.07 * k, 0.2 + 0.03 * (k % 2), 0.2 + 0.015 * k))
    mk.vpaint(e, lambda p, co, n, li: (200, 60, 14))
    parts.append(mk.flat_shade(e))

mk.tidy(parts)
mk.whiten(parts)
parts[0].name = NAME  # the joined object, and the glTF node, take this name
ob = hk.finish(parts, r"D:\OKReplace\lodes\hand\models\ZONFIRE.glb",
               {"replacesTexture": "zonsmallsacredfire", "replacesPiece": "zonfire"})
print("MISCKIT_TRIS", NAME, mk.tris(ob))
# plain display transform, so glowing colours stay as saturated as a game shows them
bpy.context.scene.view_settings.view_transform = "Standard"
hk.renders(ob, r"D:\OKReplace\lodes\hand\renders", NAME, PIC, HOT, scale=4)
