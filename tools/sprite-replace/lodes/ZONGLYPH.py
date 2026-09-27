"""ZONGLYPH, the Zhon death totem with ring: a spine of dark bone driven
into the ground by a sacrum-like shard, a gold sunburst with a ruby at its
top, and a pair of gold horns rising from the sunburst, barbed at mid
height and turning in across the top with an open gap between their tips.

The picture puts the totem's foot a cell in front of the anchor, so it
stands there. The game's shader has no metal, so the gold is in the base
and vertex colours: bright crests, olive-gold flanks, dark brown undersides.

    blender -b --factory-startup --python tools/sprite-replace/lodes/ZONGLYPH.py
"""
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import misckit as mk  # noqa: E402
from misckit import hk  # noqa: E402

NAME = "ZONGLYPH"
PIC = r"D:\OKReplace\lodes\sprites\ZONGLYPH.png"
HOT = (29, 90)

YP = -1.02  # the post's line, in front of the anchor
YS = YP - 0.24  # the sunburst, just in front of the post
YR = YP + 0.04  # the horns


def z_at(row, y):
    """Height that lands on picture row `row` at depth y."""
    return (HOT[1] - 16.0 * y - row) / 8.0


def x_at(col):
    return (col - HOT[0]) / 16.0


Z_POST = z_at(73, YP)  # top of the bone column
Z_SUN = z_at(68.5, YS)  # the ruby

hk.reset()
# the light the painted colours assume: from the upper left, in front for
# the bone, from high above for the gold, so a bar's crest is its top
KEY = Vector((-0.45, -0.55, 0.7)).normalized()
KEY_G = Vector((-0.35, -0.2, 0.92)).normalized()

BONE_HI, BONE_MID, BONE_DK = (104, 100, 62), (44, 41, 26), (12, 10, 6)
GLINT_A, GLINT_B, MAROON = (178, 172, 108), (206, 198, 132), (62, 4, 4)
bone = mk.vmat("zg_bone", (240, 228, 185), rough=0.8)
bone_dk = hk.pbr("zg_bone_dark", mk.srgb(8, 6, 4, 1.0), rough=0.8)
CREST, FLANK, FLANK_LO, UNDER = (230, 231, 202), (171, 171, 130), (168, 124, 70), (67, 55, 39)
RAY_GLINT = (255, 230, 160)
gold = mk.vmat("zg_gold", (255, 236, 206), rough=0.3)
# the horns' blades: matt, with little sheen, so their dark edges stay dark
horn = mk.vmat("zg_horn", (255, 236, 206), rough=0.65)
horn.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.25
bone.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.2
ruby = mk.vmat("zg_ruby", (240, 190, 190), rough=0.12, emit=(0.5, 0.02, 0.01), strength=0.25)

parts = []
rnd = random.Random(12)


SUN_TONES = ((255, 238, 176), (224, 192, 112), (160, 114, 58), (78, 56, 32))


def gold_tone(n, low=0.0, tones=(CREST, FLANK, FLANK_LO, UNDER), key=KEY_G):
    """Gold by how a face meets the key light: near-white crest, olive-gold
    flanks going brown, dark brown underneath; low browns it further."""
    crest, flank, flank_lo, under = tones
    f = n.dot(key)
    if f > 0.9:
        c = mk.mixc(flank, crest, (f - 0.9) / 0.07)
    elif f > 0.5:
        c = mk.mixc(flank_lo, flank, (f - 0.5) / 0.4)
    elif f > 0.1:
        c = mk.mixc(under, flank_lo, (f - 0.1) / 0.4)
    else:
        c = under
    return mk.mixc(c, mk.mixc(c, flank_lo, 0.7) if f > 0.5 else c, low)


def bone_paint(ob, seed, glint=1.0):
    """Near-black olive bone in bands across the column as the picture
    paints it: yellow-olive glints down the lit left edge, olive going to
    near black past the middle, a dark maroon rim down the right; lit tops
    a shade lighter, undersides and hollows near black."""
    r = random.Random(seed)
    noise = [r.random() for _ in ob.data.vertices]
    me = ob.data
    OLIVE_LIT, OLIVE, BLACK = (104, 98, 58), (54, 50, 32), (16, 13, 8)

    def fn(p, co, n, li):
        vi = me.loops[li].vertex_index
        u = noise[vi]
        x = co.x + (u - 0.5) * 0.06
        if x < -0.27:
            c = mk.mixc(GLINT_A, GLINT_B, u) if u > 0.3 * glint else OLIVE_LIT
        elif x < -0.1:
            c = mk.mixc(OLIVE_LIT, OLIVE, (x + 0.27) / 0.17)
        elif x < 0.12:
            c = mk.mixc(OLIVE, BLACK, (x + 0.1) / 0.22 * 0.8)
        elif x < 0.25:
            c = mk.mixc(BLACK, MAROON, (x - 0.12) / 0.13 * 0.4)
        else:
            c = mk.mixc(MAROON, BLACK, u * 0.4)
        if n.z > 0.55 and x < 0.12:
            c = mk.mixc(c, BONE_HI, 0.1 + 0.15 * u)  # the lit tops of the bodies
        if n.z < -0.4:
            c = mk.mixc(c, BLACK, 0.75)
        if u < 0.1:
            c = mk.mixc(c, BLACK, 0.6)
        return c
    return mk.vpaint(ob, fn)


# the foot: the spine's tail as a sacrum-like shard, a wing flaring to the
# left at its top, then narrowing to a long point down at the right, driven
# a little into the ground
fr = random.Random(3)
LEVELS = ((1.36, -0.34, 0.4, 0.26), (1.1, -0.62, 0.43, 0.25), (0.86, -0.42, 0.37, 0.22),
          (0.56, -0.27, 0.27, 0.18), (0.28, -0.12, 0.27, 0.14), (0.02, 0.07, 0.26, 0.1))
ring_n = 8
verts, faces = [], []
for i, (z, xl, xr, hd) in enumerate(LEVELS):
    cx, hw = (xl + xr) / 2, (xr - xl) / 2
    for j in range(ring_n):
        a = 2 * math.pi * j / ring_n
        ca, sa = math.cos(a), math.sin(a)
        k = fr.uniform(0.9, 1.08)
        x = cx + hw * ca * k
        if i == 1 and j == ring_n // 2:
            x = xl  # the wing's point
        elif i == 1 and abs(ca) > 0.5 and ca < 0:
            x = cx + (hw * 0.62) * ca * k
        verts.append((x, YP + hd * sa * k, z + fr.uniform(-0.03, 0.03)))
for i in range(len(LEVELS) - 1):
    for j in range(ring_n):
        j2 = (j + 1) % ring_n
        faces.append((i * ring_n + j, i * ring_n + j2, (i + 1) * ring_n + j2, (i + 1) * ring_n + j))
tip = len(verts)
verts.append((0.2, YP - 0.02, -0.22))
last = (len(LEVELS) - 1) * ring_n
for j in range(ring_n):
    faces.append((last + j, last + (j + 1) % ring_n, tip))
faces.append(tuple(range(ring_n - 1, -1, -1)))
foot = mk.mesh("foot", verts, faces, mat=bone)
mk._fix_normals(foot)
bone_paint(foot, 5)
parts.append(mk.flat_shade(foot))

# the vertebrae: rounded bodies with a clear waist between them, growing
# toward the top and turned only a few degrees, each with transverse
# processes sweeping down on both sides and an arch and spine at the back
n = 8
z0 = 1.2
sizes = [0.86 + 0.14 * (k / (n - 1)) for k in range(n)]
gaps = [sz * rnd.uniform(0.9, 1.1) for sz in sizes]
total = Z_POST - z0
pitches = [total * g / sum(gaps) for g in gaps]
z = z0


def lump(loc, size, seed, name):
    ob = mk.ico(1.0, 1, loc=loc, scale=size, mat=bone, name=name, seed=seed, rough=0.08)
    return hk.smooth(bone_paint(ob, seed, glint=0.6), 60)


def process(pts, rad, seed, name, flat=0.6, blade=None):
    """A tapering process swept through pts, flattened, ending blunt."""
    prof = [(math.cos(2 * math.pi * j / 6), flat * math.sin(2 * math.pi * j / 6)) for j in range(6)]
    side = blade if blade is not None else (0, 0, 1)
    pr = mk.sweep([tuple(p) for p in pts], prof, side=tuple(side),
                  scales=[rad * (1.0 - 0.55 * i / (len(pts) - 1)) for i in range(len(pts))], mat=bone, name=name)
    mk.jitter(pr, rad * 0.1, seed=seed)
    bone_paint(pr, seed)
    return hk.smooth(pr, 40)


for k in range(n):
    s = sizes[k] * rnd.uniform(0.95, 1.05)
    pitch = pitches[k]
    h = pitch * 0.74
    R = 0.37 * s
    prof = [(0.0, 0.0), (0.5 * R, 0.0), (0.8 * R, 0.06 * h), (0.96 * R, 0.2 * h), (R, 0.45 * h),
            (0.98 * R, 0.72 * h), (0.86 * R, 0.92 * h), (0.52 * R, h), (0.0, h)]
    body = mk.lathe(prof, seg=12, mat=bone, name="vert%d" % k)
    for v in body.data.vertices:
        v.co.x *= 1.08  # wider than deep, flatter at the back where the arch sits
        v.co.y *= 0.8 if v.co.y > 0 else 0.92
    mk.jitter(body, 0.022, seed=20 + k)
    twist = rnd.uniform(-3, 3)
    tipx, tipy = rnd.uniform(-3, 3), rnd.uniform(-3, 3)
    zc = z + pitch * 0.13
    xo = 0.03 * math.sin(2 * math.pi * k / n)  # a gentle sway down the column
    mk.xform(body, rot=(tipx, tipy, twist), loc=(xo, YP, zc))
    bone_paint(body, 30 + k)
    parts.append(hk.smooth(body, 50))
    # the waist between bodies: a narrow dark disc
    parts.append(hk.cylinder(0.4 * R, pitch * 0.3, seg=12, x=xo, y=YP, z=z - pitch * 0.04, mat=bone_dk,
                             name="disc%d" % k))
    zm = zc + h * 0.5
    rot = Matrix.Rotation(math.radians(twist), 3, "Z")
    # the arch at the back and its spine
    parts.append(lump((xo, YP + 0.78 * R, zm), (0.42 * R, 0.34 * R, 0.36 * h), 90 + k, "arch%d" % k))
    p0 = Vector((xo, YP + 0.85 * R, zm))
    d = (rot @ Vector((0.05, 1.0, -rnd.uniform(0.6, 0.9)))).normalized()
    L = 0.26 * s * rnd.uniform(0.8, 1.15)
    parts.append(process([p0, p0 + d * L * 0.5 + Vector((0, 0, -0.03)), p0 + d * L], 0.09 * s, 110 + k * 3,
                         "spinous%d" % k, flat=0.4, blade=rot @ Vector((1.0, 0.0, 0.0))))
    # transverse processes on both sides, out then sweeping down
    for sd in (-1, 1):
        L = 0.2 * s * rnd.uniform(0.8, 1.15)
        q0 = Vector((xo + sd * 0.75 * R, YP + 0.12 * R, zm + 0.03))
        q1 = q0 + rot @ Vector((sd * L * 0.5, L * 0.12, -L * 0.14))
        q2 = q0 + rot @ Vector((sd * L * 0.78, L * 0.28, -L * 0.72))
        parts.append(process([q0, q1, q2], 0.095 * s, 130 + k * 3 + sd, "trans%d_%d" % (k, sd), flat=0.42))
    z += pitch

# the top vertebra runs on as a bone neck into the back of the sunburst
neck = mk.lathe([(0.0, 0.0), (0.26, 0.0), (0.24, 0.12), (0.17, 0.36), (0.16, 0.55), (0.21, 0.74), (0.24, 0.86),
                 (0.16, 0.98), (0.0, 1.02)], seg=12, mat=bone, name="neck")
top_c = Vector((0.0, YS + 0.2, Z_SUN - 0.02))
base_c = Vector((0.0, YP, Z_POST - 0.06))
axis = top_c - base_c
mk.matrix(neck, Matrix.Translation(base_c) @ Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix()
          .to_4x4() @ Matrix.Diagonal((1.0, 1.0, axis.length, 1.0)))
mk.jitter(neck, 0.012, seed=77)
bone_paint(neck, 78)
parts.append(hk.smooth(neck, 50))

# the sunburst: a gold boss tilted back to the sky, thin diamond-section
# needles round it with a crease that catches the light, long ones longest
# to the left and right, the ruby set in its face
TILT_BACK = 22.0
M = (Matrix.Translation((0.0, YS, Z_SUN)) @ Matrix.Rotation(math.radians(-TILT_BACK), 4, "X")
     @ Matrix.Rotation(math.radians(90), 4, "X"))
SUN_C = Vector((0.0, YS, Z_SUN))
sun = []

# the gem sits in a low pinkish-cream glow, no bezel, the rays springing
# straight from it
boss = mk.lathe([(0.0, -0.06), (0.31, -0.06), (0.33, 0.0), (0.3, 0.06), (0.25, 0.1), (0.0, 0.12)], seg=24,
                mat=gold, name="boss")
sun.append(hk.smooth(boss, 35))


def needle(a, L, w, t, r0, name):
    """A ray in the disc's plane: a diamond section of half width w and
    ridge height t at the root r0, running to a point at L."""
    ca, sa = math.cos(a), math.sin(a)
    root = Vector((r0 * ca, r0 * sa, 0.0))
    mid = Vector(((r0 + (L - r0) * 0.3) * ca, (r0 + (L - r0) * 0.3) * sa, 0.0))
    side = Vector((-sa, ca, 0.0))
    vs = [root - side * w, root + Vector((0, 0, t)), root + side * w, root - Vector((0, 0, t * 0.6)),
          mid - side * w * 0.7, mid + Vector((0, 0, t * 0.7)), mid + side * w * 0.7, mid - Vector((0, 0, t * 0.4)),
          Vector((L * ca, L * sa, 0.0))]
    fs = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 8), (5, 6, 8), (6, 7, 8), (7, 4, 8),
          (0, 3, 2, 1)]
    ob = mk.mesh(name, [tuple(v) for v in vs], fs, mat=gold)
    mk._fix_normals(ob)
    return mk.flat_shade(ob)


rays = []
for k in range(20):
    a = 2 * math.pi * k / 20
    ca, sa = math.cos(a), math.sin(a)
    long = k % 2 == 0
    if long:
        L = 0.92 + 0.1 * abs(ca) + 0.05 * rnd.uniform(-1, 1)
        w, t = 0.1, 0.08
    else:
        L = 0.7 + 0.06 * rnd.uniform(-1, 1)
        w, t = 0.08, 0.065
    if sa < -0.75:
        L *= 0.72  # the post is below: shorter rays there
    rays.append(needle(a + rnd.uniform(-0.04, 0.04), L, w, t, 0.27, "ray%d" % k))
# a crown of darker rays set behind, filling the gaps with shadow
back = []
for k in range(20):
    a = 2 * math.pi * (k + 0.5) / 20
    L = (0.9 + 0.12 * abs(math.cos(a))) * (0.7 if math.sin(a) < -0.75 else 1.0) * rnd.uniform(0.92, 1.05)
    nb = needle(a, L, 0.12, 0.05, 0.3, "back%d" % k)
    mk.xform(nb, loc=(0.0, 0.0, -0.07))
    back.append(nb)
sun += back
# short fine needles between them
for k in range(20):
    if k % 4 == 3:
        continue
    a = 2 * math.pi * (k + 0.5) / 20
    rays.append(needle(a, 0.58 if math.sin(a) > -0.5 else 0.48, 0.05, 0.045, 0.34, "fine%d" % k))
sun += rays

# the ruby: a faceted, domed cabochon in deep crimson
gem = mk.lathe([(0.25, 0.1), (0.24, 0.15), (0.19, 0.21), (0.1, 0.25), (0.0, 0.265)], seg=10, mat=ruby,
               name="ruby")
bmg = gem.data
for v in bmg.vertices:
    v.co.y *= 0.88
sun.append(mk.flat_shade(gem))

for p in sun:
    mk.matrix(p, M)
# paint in the world frame, so the key light falls the same on every part
LOCAL_Z = (M.to_3x3() @ Vector((0, 0, 1))).normalized()
HL = (M.to_3x3() @ Vector((-0.45, 0.5, 0.74))).normalized()  # the ruby's highlight facet
GLOW_IN, GLOW_OUT = (244, 196, 184), (236, 214, 164)
mk.vpaint(boss, lambda p, co, n, li: mk.mixc(GLOW_IN, GLOW_OUT, ((co - SUN_C).length - 0.18) / 0.15))
BACK_TONES = ((150, 110, 58), (104, 72, 38), (64, 40, 24), (36, 18, 12))
for nb in back:
    mk.vpaint(nb, lambda p, co, n, li: gold_tone(n, 0.0, BACK_TONES))
for k, rb in enumerate(rays):
    rr = random.Random(500 + k)

    def ray_fn(p, co, n, li, rr=rr):
        d = (co - SUN_C).length
        c = gold_tone(n, 0.0, SUN_TONES)
        if n.dot(LOCAL_Z) > 0.2 and d < 0.56 and rr.random() < 0.8:
            c = mk.mixc(c, RAY_GLINT, 0.85)  # glints at the roots
        return c
    mk.vpaint(rb, ray_fn)
DEEP, CRIMSON, PINK = (132, 22, 16), (178, 38, 26), (240, 186, 184)
gm = random.Random(9)
mk.vpaint(gem, lambda p, co, n, li: PINK if n.dot(HL) > 0.9 else
          mk.mixc(DEEP, CRIMSON, max(0.0, n.dot(KEY)) + gm.uniform(-0.1, 0.1)))
parts += sun


def catmull(pts, per=4):
    """A smooth path through pts, per points to each span."""
    P = [Vector(p) for p in pts]
    P = [P[0] * 2 - P[1]] + P + [P[-1] * 2 - P[-2]]
    out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for s in range(per):
            t = s / per
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t +
                              (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(P[-2])
    return out


# a blade's section, (across, depth) with +1 across the lit side: a dark
# rim at each sharp edge, a crest just inside the lit one, the front face
# falling from lit to dark over a low middle ridge
BLADE = ((1.0, 0.0), (0.9, -0.75), (0.72, -1.0), (0.0, -1.12), (-0.7, -1.0), (-1.0, 0.0), (-0.7, 1.0),
         (0.7, 1.0))
LIGHT_IN = Vector((-0.8, 0.0, 0.6))  # the painted light, from the left and above
BLADE_J = {}


def blade(pts, widths, depth, name, per=3):
    """A flat gold blade in the horns' plane, swept through the picture
    points pts ((column, row) pairs) with in-plane half widths per point."""
    path = catmull([(x_at(c), YR, z_at(r, YR)) for c, r in pts], per=per)
    ws = []
    for i in range(len(pts) - 1):
        for s in range(per):
            ws.append(widths[i] + (widths[i + 1] - widths[i]) * s / per)
    ws.append(widths[-1])
    m = len(BLADE)
    verts, faces, js = [], [], []
    for i, p in enumerate(path):
        T = (path[min(len(path) - 1, i + 1)] - path[max(0, i - 1)]).normalized()
        U = Vector((T.z, 0.0, -T.x)).normalized()
        if U.dot(LIGHT_IN) < 0:
            U = -U
        for j, (u, d) in enumerate(BLADE):
            verts.append(p + U * (u * ws[i]) + Vector((0, 1, 0)) * (d * depth))
            js.append((j, i / (len(path) - 1)))
    for i in range(len(path) - 1):
        for j in range(m):
            j2 = (j + 1) % m
            faces.append((i * m + j, i * m + j2, (i + 1) * m + j2, (i + 1) * m + j))
    faces.append(tuple(range(m - 1, -1, -1)))
    faces.append(tuple((len(path) - 1) * m + j for j in range(m)))
    ob = mk.mesh(name, [tuple(v) for v in verts], faces, mat=horn)
    mk._fix_normals(ob)
    BLADE_J[ob.name] = js
    return mk.flat_shade(ob)


def blade_paint(ob, pal, grade=None):
    """Colours a blade by where each corner sits on its section: a dark rim
    on the lit edge, the crest inside it, the front face lit to dark, the
    lower chamfer, the back and the end caps dark. grade(t) may blend
    toward the dark colour along the blade."""
    rim, crest, lit, mid, dark, edge, back = pal
    js = BLADE_J[ob.name]
    me = ob.data
    rnd_v = random.Random(len(js))
    speck = [rnd_v.random() for _ in js]
    FACE = {(0, 1): rim, (1, 2): crest, (4, 5): edge}

    def fn(p, co, n, li):
        vi = me.loops[li].vertex_index
        j, t = js[vi]
        pj = tuple(sorted(set(js[v][0] for v in p.vertices)))
        if len(p.vertices) > 4:
            c = edge  # an end cap
        elif pj in FACE:
            c = FACE[pj]
        elif pj == (2, 3):
            c = lit if j == 2 else mid
        elif pj == (3, 4):
            c = mid if j == 3 else dark
        else:
            c = back
        if grade is not None:
            c = mk.mixc(c, mk.mixc(c, edge, 0.7), grade(t))
        if speck[vi] < 0.08 and c in (lit, mid):
            c = mk.mixc(c, dark, 0.5)
        return c
    return mk.vpaint(ob, fn)


# the horns: flat angular gold blades in a bracket, each arm a broad
# diagonal from behind the sunburst out to a sharp elbow, a triangular barb
# flaring outward there, a shaft pinched at the waist rising to the top
# bar, and the bar running from a flared outer tip to a point at the gap.
# Picture (column, row) at the horns' depth, half widths in cells.
# palettes: rim, crest, lit, mid, dark, edge, back; the left arm ochre
# gold, the right one redder and in more shade
BAR_L = ((50, 46, 38), (240, 238, 208), (216, 202, 140), (192, 158, 88), (132, 94, 50), (52, 36, 22), (40, 34, 28))
BAR_R = ((44, 38, 32), (220, 210, 168), (176, 150, 108), (148, 112, 76), (102, 70, 46), (44, 30, 20), (36, 30, 26))
SHAFT_L = ((42, 44, 34), (196, 172, 100), (188, 148, 72), (174, 132, 58), (130, 82, 34), (54, 30, 16), (40, 30, 20))
SHAFT_R = ((46, 36, 28), (200, 154, 118), (178, 130, 82), (164, 114, 64), (130, 72, 40), (52, 24, 14), (40, 28, 20))
BARB_L = ((56, 50, 38), (246, 230, 150), (210, 180, 96), (180, 134, 54), (124, 84, 38), (34, 22, 14), (40, 30, 20))
BARB_R = ((44, 36, 28), (212, 170, 136), (180, 128, 90), (150, 102, 66), (98, 60, 38), (32, 20, 12), (38, 26, 20))
DIAG_L = ((64, 56, 38), (222, 190, 112), (212, 166, 82), (182, 130, 62), (70, 44, 22), (28, 16, 10), (34, 24, 16))
DIAG_R = ((52, 44, 32), (214, 178, 124), (190, 142, 82), (150, 106, 58), (54, 32, 18), (24, 14, 10), (34, 24, 16))
ARMS = (
    # left: bar, shaft, barb, diagonal
    (BAR_L, (((23.2, 41.2), (19.5, 42.3), (15.0, 43.5), (10.0, 44.6), (5.5, 45.3), (1.6, 45.7)),
             (0.03, 0.14, 0.19, 0.21, 0.23, 0.13))),
    (SHAFT_L, (((7.6, 46.0), (7.9, 48.4), (8.0, 50.4), (8.0, 52.4), (8.4, 54.6)),
              (0.26, 0.2, 0.15, 0.19, 0.26))),
    (BARB_L, (((10.8, 55.3), (6.0, 55.3), (2.2, 55.4), (-0.6, 55.5)), (0.36, 0.3, 0.2, 0.03))),
    (DIAG_L, (((9.2, 55.4), (10.6, 58.6), (13.4, 61.6), (17.4, 63.6), (21.6, 64.8)),
              (0.2, 0.2, 0.2, 0.18, 0.16))),
    # right
    (BAR_R, (((37.6, 41.4), (41.5, 42.6), (46.0, 43.7), (51.0, 44.4), (54.5, 44.6), (57.6, 44.7)),
             (0.03, 0.14, 0.19, 0.21, 0.22, 0.12))),
    (SHAFT_R, (((51.2, 45.8), (51.4, 48.2), (51.5, 50.2), (51.3, 52.2), (50.6, 54.4)),
              (0.26, 0.19, 0.15, 0.19, 0.26))),
    (BARB_R, (((48.2, 54.8), (52.5, 54.8), (56.2, 54.8), (58.6, 54.9)), (0.36, 0.29, 0.19, 0.03))),
    (DIAG_R, (((49.6, 55.2), (47.0, 57.6), (43.4, 59.6), (39.6, 61.4), (36.0, 63.0)),
              (0.2, 0.22, 0.22, 0.2, 0.16))),
)
for k, (pal, (pts, ws)) in enumerate(ARMS):
    bl = blade(pts, ws, 0.07 if k % 4 == 0 else 0.055, "horn%d" % k)
    # the diagonals darken toward the sunburst, where they pass behind it
    blade_paint(bl, pal, (lambda t: 0.4 * t * t) if k % 4 == 3 else None)
    parts.append(bl)



def lean_left(ob):
    """Turns the column's left-facing normals up toward the viewer, so the
    painted glints down its left edge catch the light as in the picture."""
    me = ob.data
    to = Vector((-0.25, -0.55, 0.8)).normalized()
    ns = []
    for li in range(len(me.loops)):
        nv = Vector(me.corner_normals[li].vector)
        if nv.x < -0.2:
            nv = nv.lerp(to, min(1.0, (-nv.x - 0.2) * 1.6) * 0.75).normalized()
        ns.append(nv)
    me.normals_split_custom_set(ns)


for pt in parts:
    if pt.name.startswith(("vert", "arch", "spinous", "trans", "neck")):
        lean_left(pt)

mk.tidy(parts)
mk.whiten(parts)
parts[0].name = NAME  # the joined object, and the glTF node, take this name
ob = hk.finish(parts, r"D:\OKReplace\lodes\hand\models\ZONGLYPH.glb",
               {"replacesTexture": "zondeathtotemwithring", "replacesPiece": "zonglyph"})
print("MISCKIT_TRIS", NAME, mk.tris(ob))
# plain display transform, so colours stay as saturated as a game shows them
bpy.context.scene.view_settings.view_transform = "Standard"
hk.renders(ob, r"D:\OKReplace\lodes\hand\renders", NAME, PIC, HOT, scale=4)
mk.game_look(ob, NAME, PIC, HOT)
