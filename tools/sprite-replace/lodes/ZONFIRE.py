"""ZONFIRE, the Zhon small sacred fire: seven mottled standing stones in a
ring round a heap of reddish-brown branches, a low tepee leaning in behind
and right of the middle, thin sticks criss-crossed over it and out between
the stones, the back right burnt to charcoal and a low bright fire at the
front.

    blender -b --factory-startup --python tools/sprite-replace/lodes/ZONFIRE.py
"""
import math
import os
import random
import sys

import bpy
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import misckit as mk  # noqa: E402
from misckit import hk  # noqa: E402

NAME = "ZONFIRE"
PIC = r"D:\OKReplace\lodes\sprites\ZONFIRE.png"
HOT = (27, 39)

hk.reset()
# picture colours: grey stone with cream and lichen, reddish-brown bark
# with charcoal only where it burns, fire from yellow to red
stone = mk.vmat("zf_stone", (200, 196, 176), rough=0.9)
bark = mk.vmat("zf_bark", (176, 116, 70), rough=0.92)
grain = mk.vmat("zf_endgrain", (196, 150, 92), rough=0.85)
soil = mk.vmat("zf_soil", (120, 96, 66), rough=1.0)
ember = mk.vmat("zf_ember", (240, 120, 36), rough=0.8, emit=(1.0, 0.25, 0.03), strength=1.6)
crust = mk.vmat("zf_crust", (40, 34, 30), rough=1.0)
fl_core = mk.vmat("zf_flame_core", (252, 232, 80), rough=0.6, emit=(1.0, 0.82, 0.12), strength=1.25)
fl_mid = mk.vmat("zf_flame", (250, 210, 48), rough=0.6, emit=(1.0, 0.6, 0.04), strength=1.25)
fl_tip = mk.vmat("zf_flame_tip", (244, 136, 36), rough=0.6, emit=(1.0, 0.3, 0.03), strength=1.2)

parts = []
rnd = random.Random(7)
FIRE = Vector((-0.1, -0.44, 0.0))  # the heart of the fire, at the front of the heap
APEX = Vector((0.2, 0.3, 0.84))  # the tepee's top, behind and right of the middle

CREAM, GREY, SIDE, SHADE, CRACK, FOOT = ((184, 181, 162), (146, 145, 138), (128, 127, 120), (98, 97, 92),
                                         (50, 49, 46), (48, 47, 44))
LICHEN, MOSS = (176, 170, 122), (128, 126, 90)


def stone_colour(st, k, lift, lichen):
    """Mottling from coherent noise, dark flecks by face, thin dark cracks
    where a ridged noise crosses zero, cream on the lit top and lichen
    patches, darkest at the foot."""
    r = random.Random(500 + k)
    me = st.data
    off = Vector((r.uniform(0, 50), r.uniform(0, 50), r.uniform(0, 50)))
    fleck = [r.random() for _ in me.polygons]

    def fn(p, co, n, li):
        q = co * 5.0 + off
        m = noise.noise(q) * 0.5 + 0.5 + noise.noise(q * 2.3) * 0.2 + noise.noise(q * 5.1) * 0.18
        if n.z > 0.5:
            c = mk.mixc(CREAM, GREY, m)
        else:
            c = mk.mixc(SIDE, SHADE, m)
        lic = noise.noise(co * 3.2 + off * 1.7)
        if lic > 0.25 - 0.45 * lichen:
            c = mk.mixc(c, LICHEN if n.z > 0.2 else MOSS, min(1.0, (lic - 0.25 + 0.45 * lichen) * 3.0) * 0.8)
        c = tuple(min(255, v * lift) for v in c)
        if lichen < 0:
            g = sum(c) / 3.0
            c = mk.mixc(c, (g, g, g + 2), -lichen)
        if abs(noise.noise(co * 3.0 + off * 0.5)) < 0.045:
            c = mk.mixc(c, CRACK, 0.85)
        f = fleck[p.index]
        if f < 0.05:
            c = mk.mixc(c, CRACK, 0.35 + 0.3 * f * 20)
        elif f > 0.96:
            c = mk.mixc(c, (206, 202, 186), 0.45)
        if n.z < -0.35:
            c = FOOT
        elif co.z < 0.16:
            c = mk.mixc(c, FOOT, 0.55 * (1.0 - co.z / 0.16))
        return c
    return fn


def stone_piece(k, a, R, W, L, h, lean, lift, lichen):
    st = mk.rock(W, L, h, cuts=4, rough=0.03, seed=11 + k, mat=stone, name="stone%d" % k, taper=0.14)
    # a broken, sloping top: higher on one side, chipped at the corners
    sl = rnd.uniform(-0.12, 0.12)
    for v in st.data.vertices:
        if v.co.z > h * 0.6:
            v.co.z += sl * v.co.x / W + rnd.uniform(-0.03, 0.015)
    hk.bevel(st, width=0.05, segments=1)
    cr = random.Random(90 + k)
    for v in st.data.vertices:
        if v.co.z > h * 0.55 and (abs(v.co.x) > W * 0.36 or abs(v.co.y) > L * 0.36) and cr.random() < 0.3:
            v.co.z -= cr.uniform(0.03, 0.09)
            v.co.x *= 0.95
            v.co.y *= 0.95
    mk.jitter(st, 0.012, seed=70 + k)
    mk.xform(st, rot=(-lean, rnd.uniform(-4, 4), a - 90.0 + rnd.uniform(-5, 5)), order="XYZ")
    mk.xform(st, loc=(R * math.cos(math.radians(a)), R * math.sin(math.radians(a)), -0.04))
    hk.smooth(st, 60)
    return mk.vpaint(st, stone_colour(st, k, lift, lichen))


# the stone ring, long sides pointing out, leaning out a little; angles and
# distances measured off the picture; brightness and lichen per stone as
# the picture has them (the back right one sits in shade)
RING = ((92, 1.40, 0.92, -0.6), (45, 1.30, 0.5, 0.5), (-16, 1.31, 1.08, 0.2), (-69, 1.40, 1.0, 0.9),
        (-114, 1.42, 1.3, -0.9), (-164, 1.32, 1.02, 0.2), (140, 1.32, 1.14, 0.8))
for k, (a, R, lift, lichen) in enumerate(RING):
    south = math.sin(math.radians(a)) < -0.5
    W = 0.46 + rnd.uniform(-0.03, 0.03)
    L = 0.68 + (0.06 if south else 0.0) + rnd.uniform(-0.03, 0.03)
    h = 0.72 if k == 0 else 0.64 + rnd.uniform(-0.04, 0.06)
    parts.append(stone_piece(k, a, R, W, L, h, 8.0 + rnd.uniform(-2, 3), lift, lichen))

# the bed under the heap: brown trodden soil and ash, no bigger than the
# heap and the fire, its edge ragged
ar = random.Random(4)
BED = Vector((0.02, 0.02, 0.0))
NB = 24
rim = [0.6 + ar.uniform(-0.12, 0.1) for _ in range(NB)]
bverts, bfaces = [(BED.x, BED.y, 0.018)], []
for ring, (f, z) in enumerate(((0.35, 0.016), (0.7, 0.01), (1.0, 0.0))):
    for j in range(NB):
        a = 2 * math.pi * (j + (0.5 if ring == 2 and j % 2 else 0.0) * 0.6) / NB
        rr = rim[j] * f * (1.0 + ar.uniform(-0.08, 0.08)) * (1.25 if ring == 2 and j % 3 == 0 else 1.0)
        bverts.append((BED.x + rr * math.cos(a), BED.y + rr * math.sin(a), z))
for j in range(NB):
    bfaces.append((0, 1 + j, 1 + (j + 1) % NB))
for ring in range(2):
    for j in range(NB):
        a0, a1 = 1 + ring * NB + j, 1 + ring * NB + (j + 1) % NB
        bfaces.append((a0, a0 + NB, a1 + NB, a1))
bed = mk.mesh("bed", bverts, bfaces, mat=soil)
mk._fix_normals(bed)
bn = [ar.random() for _ in bed.data.vertices]
SOIL_DK, SOIL_MID, SOIL_EDGE = (40, 28, 12), (74, 56, 30), (104, 88, 52)


def bed_colour(p, co, n, li):
    vi = bed.data.loops[li].vertex_index
    d = math.hypot(co.x - BED.x, co.y - BED.y) / 0.6
    c = mk.mixc(SOIL_DK, SOIL_MID, (d - 0.3) / 0.5 + (bn[vi] - 0.5) * 0.4)
    return mk.mixc(c, SOIL_EDGE, (d - 0.7) / 0.3)


mk.vpaint(bed, bed_colour)
parts.append(mk.flat_shade(bed))

BARK, BARK_DK, RIDGE, SCORCH = (142, 86, 50), (98, 56, 30), (166, 116, 90), (66, 36, 16)
CHAR, CHAR_HI = (24, 15, 6), (52, 38, 20)
GRAIN, HEART = (196, 150, 92), (116, 76, 42)


def log(p0, p1, r0, r1, seed, char=0.7, bend=0.05, seg=8, grain_end=True, name="log"):
    """A tapering, slightly bent log from its outer end p0 to its inner end
    p1: reddish bark in streaks along it, scorched and then charcoal from
    `char` of the way in; pale end grain at p0."""
    r = random.Random(seed)
    P0, P1 = Vector(p0), Vector(p1)
    D = P1 - P0
    L = D.length
    T = D.normalized()
    side = T.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-3 else Vector((1, 0, 0))
    up = side.cross(T).normalized()
    if up.z < 0:
        up, side = -up, -side
    b1, b2 = r.uniform(-1, 1) * bend, r.uniform(-1, 1) * bend * 0.6
    nr = max(4, int(L / 0.12))
    ts = [i / nr for i in range(nr + 1)]
    angs = [2 * math.pi * j / seg for j in range(seg)]
    lump = [r.uniform(0.86, 1.08) for _ in angs]
    m = len(angs)
    verts = []
    for t in ts:
        c = P0 + D * t + up * (b1 * math.sin(math.pi * t) * L) + side * (b2 * math.sin(2 * math.pi * t))
        rad = (r0 + (r1 - r0) * t) * (0.95 + 0.07 * r.random())
        for j, a in enumerate(angs):
            verts.append(c + (side * math.cos(a) + up * math.sin(a)) * rad * lump[j])
    n = len(ts)
    faces = []
    for i in range(n - 1):
        for j in range(m):
            j2 = (j + 1) % m
            faces.append((i * m + j, i * m + j2, (i + 1) * m + j2, (i + 1) * m + j))
    for i, cap in ((0, P0), (n - 1, P1)):
        ci = len(verts)
        verts.append(P0 + D * ts[i] + up * (b1 * math.sin(math.pi * ts[i]) * L) - T * (0.01 if i == 0 else -0.02))
        for j in range(m):
            j2 = (j + 1) % m
            faces.append((i * m + j2, i * m + j, ci) if i == 0 else (i * m + j, i * m + j2, ci))
    ob = mk.mesh(name, verts, faces, mat=bark)
    mk._fix_normals(ob)
    ob.data.materials.append(grain)
    me = ob.data
    nring = n * m
    for p in me.polygons:
        if nring in p.vertices and grain_end:
            p.material_index = 1
    strip = [r.random() for _ in range(m)]
    noise_v = [r.random() for _ in me.vertices]

    def fn(p, co, nn, li):
        vi = me.loops[li].vertex_index
        u = noise_v[vi]
        if p.material_index == 1:
            d = (co - P0).length / r0
            return mk.mixc(HEART, GRAIN, min(1.0, d * 1.4)) if d < 0.9 else BARK_DK
        t = max(0.0, min(1.0, (co - P0).dot(T) / L))
        j = vi % m if vi < nring else 0
        c = mk.mixc(BARK, BARK_DK, strip[j] * 0.7 + u * 0.3)
        if strip[j] < 0.18:
            c = RIDGE
        if nn.z < -0.3:
            c = mk.mixc(c, BARK_DK, 0.5)
        burn = (t - char) / 0.1 + (u - 0.5) * 0.6
        if burn > 0.0:
            c = mk.mixc(c, SCORCH, min(1.0, burn * 1.5))
        if burn > 0.7:
            c = mk.mixc(c, mk.mixc(CHAR, CHAR_HI, u * 0.5), min(1.0, (burn - 0.7) * 1.5))
        return c
    mk.vpaint(ob, fn)
    return hk.smooth(ob, 40)


# the tepee: four thin logs leaning in to the apex, their tops crossing and
# running a little past it; the back one burnt through
for k, (ang, dist, ext, rr, off, ch) in enumerate(((252, 0.8, 0.34, 0.075, (0.05, -0.03, 0.0), 0.72),
                                                   (338, 0.78, 0.16, 0.07, (-0.04, 0.02, 0.03), 0.66),
                                                   (74, 0.8, 0.24, 0.09, (0.03, 0.05, -0.02), 0.05),
                                                   (132, 0.86, 0.18, 0.08, (-0.05, -0.02, 0.02), 0.8))):
    g = math.radians(ang)
    base = Vector((APEX.x + dist * math.cos(g), APEX.y + dist * math.sin(g), 0.06))
    top = APEX + Vector(off)
    d = (top - base)
    tip = top + d.normalized() * ext
    parts.append(log(tuple(base), tuple(tip), rr, rr * 0.75, seed=20 + k, char=ch, bend=0.03, grain_end=False,
                     name="tepee%d" % k))

# the crossed logs: laid over each other at varied heights round the
# tepee, outer ends out between the stones, only the inner ends burning
CROSS = (((-1.42, 0.3, 0.1), (-0.14, 0.02, 0.36), 0.1, 0.7),   # the long one out to the left
         ((1.42, 0.38, 0.1), (0.4, -0.02, 0.3), 0.1, 0.68),  # out to the right, embers on it
         ((-0.64, 1.2, 0.09), (-0.02, 0.44, 0.44), 0.09, 0.7),  # in from the back left
         ((0.52, 1.3, 0.12), (0.42, 0.26, 0.42), 0.13, 0.05),  # the burnt one at the back right
         ((0.95, -0.86, 0.08), (0.24, -0.18, 0.22), 0.085, 0.66))   # the front right
for k, (p0, p1, rr, ch) in enumerate(CROSS):
    parts.append(log(p0, p1, rr, rr * 0.78, seed=40 + k, char=ch, name="cross%d" % k))

# thin branches: criss-crossed over the top of the heap and out toward the
# stones, some forked
BRANCH = (((-1.2, 0.62, 0.07), (0.3, 0.22, 0.5), 0.055, 0.72),
          ((-1.1, -0.12, 0.07), (0.28, 0.46, 0.46), 0.05, 0.75),
          ((1.2, -0.3, 0.07), (-0.3, 0.26, 0.5), 0.05, 0.72),
          ((1.18, 0.82, 0.07), (-0.2, 0.12, 0.42), 0.055, 0.35),
          ((-0.1, 1.28, 0.07), (0.22, -0.06, 0.52), 0.05, 0.4),
          ((-1.0, 1.0, 0.07), (0.14, -0.08, 0.4), 0.05, 0.74),
          ((0.5, -0.92, 0.05), (0.1, -0.2, 0.3), 0.045, 0.7),
          ((-0.76, -0.8, 0.05), (-0.2, -0.16, 0.28), 0.045, 0.72),
          ((-0.62, 0.64, 0.3), (0.3, 0.2, 0.36), 0.06, 0.8),
          ((-0.7, 0.1, 0.24), (0.22, 0.7, 0.4), 0.055, 0.82),
          ((0.1, 0.9, 0.2), (-0.5, 0.2, 0.34), 0.06, 0.76),
          ((-0.5, 0.95, 0.12), (-0.3, 0.1, 0.46), 0.05, 0.8),
          ((0.1, 0.62, 0.62), (0.98, 0.9, 0.08), 0.045, 0.1),
          ((-1.3, 0.1, 0.06), (0.0, 0.36, 0.56), 0.04, 0.8),
          ((1.1, 0.1, 0.06), (0.1, 0.5, 0.6), 0.04, 0.5))
br = random.Random(31)
for k, (p0, p1, rr, ch) in enumerate(BRANCH):
    parts.append(log(p0, p1, rr, rr * 0.7, seed=140 + k, char=ch, bend=0.11, seg=6, grain_end=False,
                     name="branch%d" % k))
    if k % 3 == 0:
        # a side twig forking off toward the outer end
        a, b = Vector(p0), Vector(p1)
        s = a + (b - a) * br.uniform(0.3, 0.45)
        d = (b - a).normalized()
        sd = d.cross(Vector((0, 0, 1))).normalized() * br.choice((-1, 1))
        e = s + (sd * 0.8 - d * 0.5).normalized() * br.uniform(0.3, 0.42) + Vector((0, 0, 0.03))
        parts.append(log(tuple(e), tuple(s), rr * 0.55, rr * 0.7, seed=170 + k, char=2.0, bend=0.05, seg=5,
                         grain_end=False, name="twig%d" % k))
# twig ends and grass stalks lying in the gaps between the lower stones
STRAW, STRAW_DK = (120, 104, 56), (76, 62, 34)
for k, (p0, p1) in enumerate((((-1.0, -0.8, 0.03), (-0.5, -0.36, 0.12)), ((0.05, -1.12, 0.03), (0.0, -0.62, 0.08)),
                              ((-0.48, 1.0, 0.04), (0.36, 0.96, 0.1)), ((-0.34, -1.16, 0.03), (-0.2, -0.7, 0.06)),
                              ((0.46, -1.16, 0.03), (0.3, -0.7, 0.06)), ((1.02, -0.52, 0.03), (0.66, -0.42, 0.08)),
                              ((-0.12, -1.2, 0.02), (0.22, -0.9, 0.04)), ((0.28, -1.08, 0.02), (-0.08, -0.84, 0.04)))):
    st = log(p0, p1, 0.03, 0.024, seed=80 + k, char=2.0, seg=5, grain_end=False, name="stick%d" % k)
    sr = random.Random(700 + k)
    # dry twigs and grass stalks, olive brown
    mk.vpaint(st, lambda p, co, n, li, sr=sr: mk.mixc(STRAW, STRAW_DK, sr.random()))
    parts.append(st)

# lumps of charcoal heaped at the back right, where the fire has burnt longest
for k, (x, y, sx, sy, sz) in enumerate(((0.55, 0.78, 0.34, 0.26, 0.24), (0.8, 0.46, 0.28, 0.22, 0.18),
                                        (0.26, 1.0, 0.26, 0.2, 0.16), (0.64, 0.2, 0.22, 0.18, 0.14),
                                        (0.42, 1.14, 0.2, 0.16, 0.14))):
    ch = mk.rock(sx, sy, sz, cuts=1, rough=0.03, seed=600 + k, mat=bark, name="coal%d" % k, taper=0.3)
    mk.xform(ch, rot=(0, 0, 40 * k + 15), loc=(x, y, -0.01))
    cr = random.Random(610 + k)
    mk.vpaint(ch, lambda p, co, n, li, cr=cr: mk.mixc(CHAR, CHAR_HI, cr.random() * (0.4 if n.z > 0.4 else 0.15)))
    parts.append(mk.flat_shade(ch))

# the ember floor under the fire: flat angular chips, black crust between
er = random.Random(11)
for k in range(48):
    a = er.uniform(0, 2 * math.pi)
    d = er.uniform(0.0, 1.0) ** 0.7
    x = FIRE.x + 0.1 + d * 0.66 * math.cos(a)
    y = FIRE.y + 0.24 + d * 0.46 * math.sin(a)
    if math.hypot(x - BED.x, y - BED.y) > 0.62:
        continue
    hot = er.random() < 0.55
    s = er.uniform(0.04, 0.07)
    chip = mk.rock(s * er.uniform(1.0, 1.8), s * er.uniform(0.8, 1.4), er.uniform(0.02, 0.04), cuts=0,
                   rough=s * 0.25, seed=200 + k, mat=ember if hot else crust, name="chip%d" % k)
    mk.xform(chip, rot=(er.uniform(-10, 10), er.uniform(-10, 10), er.uniform(0, 180)), loc=(x, y, -0.005))
    if hot:
        cr = random.Random(300 + k)
        mk.vpaint(chip, lambda p, co, n, li, cr=cr: mk.mixc((120, 26, 6), (236, 110, 30), cr.random() ** 1.3))
        mk.up_normals(chip)
    else:
        mk.flat_shade(chip)
    parts.append(chip)


def ember_patch(centre, normal, size, seed, name):
    """A small irregular glowing patch laid on a log: a jagged fan of
    red-orange, hotter in the middle."""
    r = random.Random(seed)
    c = Vector(centre)
    nrm = Vector(normal).normalized()
    t1 = nrm.orthogonal().normalized()
    t2 = nrm.cross(t1)
    verts = [tuple(c + nrm * 0.012)]
    NS = 7
    for j in range(NS):
        a = 2 * math.pi * j / NS + r.uniform(-0.3, 0.3)
        rr = size * r.uniform(0.45, 1.0)
        verts.append(tuple(c + (t1 * math.cos(a) + t2 * math.sin(a) * 0.7) * rr))
    faces = [(0, 1 + j, 1 + (j + 1) % NS) for j in range(NS)]
    ob = mk.mesh(name, verts, faces, mat=ember)
    mk._fix_normals(ob)
    cols = [(236, 118, 30)] + [mk.mixc((150, 32, 8), (220, 80, 18), r.random()) for _ in range(NS)]
    mk.vpaint(ob, lambda p, co, n, li: cols[ob.data.loops[li].vertex_index])
    return mk.up_normals(ob)


# ember spots: on the right log out by the stones, and high on the heap
for k, (cen, nrm, s) in enumerate((((0.92, 0.22, 0.2), (0.0, -0.4, 1.0), 0.07),
                                   ((1.04, 0.28, 0.17), (0.0, -0.5, 1.0), 0.05),
                                   ((-0.06, 0.5, 0.8), (-0.3, -0.5, 1.0), 0.06),
                                   ((0.02, 0.58, 0.74), (0.2, -0.6, 1.0), 0.045),
                                   ((0.44, 0.9, 0.3), (0.0, -0.3, 1.0), 0.05))):
    parts.append(ember_patch(cen, nrm, s, seed=900 + k, name="emberspot%d" % k))


C0, C1, C2, C3 = (250, 222, 44), (246, 196, 26), (236, 116, 18), (194, 40, 10)
FLECK = ((230, 84, 16), (212, 50, 12), (240, 146, 22))


def fire_colour(t):
    if t < 0.3:
        return mk.mixc(C0, C1, t / 0.3)
    if t < 0.5:
        return C1
    if t < 0.78:
        return mk.mixc(C1, C2, (t - 0.5) / 0.28)
    return mk.mixc(C2, C3, (t - 0.78) / 0.22)


def tint(ob, colours):
    """Stores sRGB colours per vertex as ratios to each face's flame ref."""
    me = ob.data
    refs = [m["ref"] for m in me.materials]
    at = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for p in me.polygons:
        ref = refs[p.material_index]
        for li in p.loop_indices:
            c = colours[me.loops[li].vertex_index]
            at.data[li].color = tuple(min(1.0, mk.lin(c[i]) / max(1e-4, mk.lin(ref[i]))) for i in range(3)) + (1.0,)


def tongue(base, h, w, lean, curl, twist, seed, rings=7, seg=6, name="tongue"):
    """A thin flame tongue: a flattened, twisting section swept up a curved
    path, swelling low and drawn out to a point, yellow at the root, red at
    the tip, flecked red-orange; its normals point up so it lights evenly."""
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
    for p in ob.data.polygons:
        tm = sum(ts[i] for i in p.vertices) / len(p.vertices)
        p.material_index = 0 if tm < 0.26 else 1 if tm < 0.7 else 2
    cols = []
    for t in ts:
        c = fire_colour(t)
        if 0.12 < t < 0.85 and r.random() < 0.22:
            c = r.choice(FLECK)
        cols.append(c)
    tint(ob, cols)
    return mk.up_normals(ob)


def glow_bed(centre, rx, ry, hgt, seed, name="glowbed"):
    """The low bright blob at the base of the flames: a flattened, lumpy
    dome, yellow in the middle going orange and red at its edge."""
    r = random.Random(seed)
    NS, NR = 16, 4
    cx, cy, cz = centre
    verts, cols = [(cx, cy, cz + hgt)], [C0]
    for i in range(1, NR + 1):
        f = i / NR
        for j in range(NS):
            a = 2 * math.pi * (j + 0.5 * (i % 2)) / NS
            k = f * r.uniform(0.85, 1.12)
            verts.append((cx + rx * k * math.cos(a), cy + ry * k * math.sin(a),
                          cz + hgt * (1.0 - f ** 2) * r.uniform(0.7, 1.2)))
            c = mk.mixc(C0, C1, f * 1.6) if f < 0.7 else mk.mixc(C1, C2, (f - 0.6) / 0.4)
            if r.random() < 0.2:
                c = r.choice(FLECK)
            cols.append(c)
    faces = [(0, 1 + j, 1 + (j + 1) % NS) for j in range(NS)]
    for i in range(NR - 1):
        for j in range(NS):
            a0, a1 = 1 + i * NS + j, 1 + i * NS + (j + 1) % NS
            faces.append((a0, a0 + NS, a1 + NS, a1))
    ob = mk.mesh(name, verts, faces)
    mk._fix_normals(ob)
    for m in (fl_core, fl_mid, fl_tip):
        ob.data.materials.append(m)
    for p in ob.data.polygons:
        p.material_index = 1
    tint(ob, cols)
    return mk.up_normals(ob)


# the low glow at the root of the fire
parts.append(glow_bed((FIRE.x + 0.05, FIRE.y + 0.12, 0.03), 0.56, 0.4, 0.2, seed=17))

# the fire: a low body of tongues over the front of the heap drawing in
# toward each other, a few taller ones climbing into the tepee, and low
# ones at its edge licking out over the logs
fr = random.Random(5)
spots = []
while len(spots) < 16:
    u, v = fr.uniform(-1, 1), fr.uniform(-1, 1)
    if u * u + v * v > 0.6:
        continue
    spots.append((u, v))
spots.sort(key=lambda s: s[0] ** 2 + s[1] ** 2)
for k, (u, v) in enumerate(spots):
    x = FIRE.x + u * 0.72
    y = FIRE.y + v * 0.44
    d = math.hypot(u, v) / 0.78
    h = max(0.34, min(0.78, (0.84 - 0.5 * d) * fr.uniform(0.82, 1.0)))
    w = (0.23 - 0.07 * d) * fr.uniform(0.85, 1.15)
    lean = (-u * 0.14 * h + fr.uniform(-0.1, 0.1), -v * 0.12 * h + fr.uniform(-0.08, 0.08))
    z0 = 0.05 + fr.uniform(0.0, 0.05)
    tw = fr.choice((-1, 1)) * fr.uniform(70, 160)
    parts.append(tongue((x, y, z0), h, w, lean, fr.uniform(0.04, 0.1), tw, seed=k + 3, rings=6,
                        seg=6 if w > 0.12 else 5, name="tongue%d" % k))
# the taller tongues near the middle, reaching up into the tepee
for k, (x, y, h, w) in enumerate(((0.0, -0.26, 0.86, 0.2), (-0.18, -0.34, 0.76, 0.18),
                                  (0.14, -0.38, 0.72, 0.17), (-0.04, -0.16, 0.8, 0.16))):
    parts.append(tongue((x, y, 0.08), h, w, (0.06 * (k % 2) - 0.02, 0.1), 0.05, fr.choice((-1, 1)) * 120,
                        seed=120 + k, rings=7, seg=6, name="tall%d" % k))
for k in range(9):
    # licks round the front and both sides of the patch, out over the logs
    ar = math.radians(90 + k * 30 + fr.uniform(-8, 8))
    u, v = math.cos(ar), math.sin(ar)
    x = FIRE.x + u * 0.7 * fr.uniform(0.85, 1.0)
    y = FIRE.y + v * 0.44 * fr.uniform(0.85, 1.0)
    h = fr.uniform(0.26, 0.42)
    lean = (u * 0.16 + fr.uniform(-0.05, 0.05), v * 0.12 + fr.uniform(-0.04, 0.04))
    parts.append(tongue((x, y, 0.1 + fr.uniform(0.0, 0.08)), h, fr.uniform(0.08, 0.12), lean, 0.04,
                        fr.choice((-1, 1)) * 90, seed=60 + k, rings=5, seg=5, name="edge%d" % k))
# small flames on the right log, and one high among the back logs
for k, (base, h, w, lean) in enumerate((((0.6, 0.06, 0.26), 0.3, 0.09, (0.08, 0.02)),
                                         ((0.48, 0.12, 0.28), 0.22, 0.07, (0.05, -0.02)),
                                         ((-0.05, 0.6, 0.5), 0.22, 0.06, (0.0, -0.03)))):
    parts.append(tongue(base, h, w, lean, 0.02, 80, seed=40 + k, rings=5, seg=5, name="lick%d" % k))

mk.tidy(parts)
mk.whiten(parts)
parts[0].name = NAME  # the joined object, and the glTF node, take this name
ob = hk.finish(parts, r"D:\OKReplace\lodes\hand\models\ZONFIRE.glb",
               {"replacesTexture": "zonsmallsacredfire", "replacesPiece": "zonfire"})
print("MISCKIT_TRIS", NAME, mk.tris(ob))
# plain display transform, so glowing colours stay as saturated as a game shows them
bpy.context.scene.view_settings.view_transform = "Standard"
hk.renders(ob, r"D:\OKReplace\lodes\hand\renders", NAME, PIC, HOT, scale=4)
mk.game_look(ob, NAME, PIC, HOT)
