"""ZONGLYPH, the Zhon death totem with ring: a spine of dark bone driven
into the ground, a gold sunburst with a ruby at its top, and a pair of gold
horns rising from the sunburst, barbed at mid height and turning in across
the top with an open gap between their tips.

The picture puts the totem's foot a cell in front of the anchor, so it
stands there.

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
BONE_HI, BONE_MID, BONE_DK = (140, 134, 88), (44, 39, 27), (26, 19, 14)
bone = mk.vmat("zg_bone", (184, 172, 120), rough=0.55)
bone_dk = hk.pbr("zg_bone_dark", mk.srgb(38, 28, 20, 1.0), rough=0.8)
GOLD_HI, GOLD, GOLD_DK = (244, 234, 184), (222, 176, 88), (138, 84, 44)
GOLD_LOW = (184, 128, 62)  # the lower arms, browner as drawn
RAY_HI = (240, 208, 128)
gold = mk.vmat("zg_gold", GOLD_HI, rough=0.32, metal=0.6)
gold_dk = hk.pbr("zg_gold_dark", mk.srgb(*GOLD_DK), rough=0.4, metal=0.8)
ruby = hk.pbr("zg_ruby", (0.6, 0.02, 0.02), rough=0.08, emit=(1.0, 0.06, 0.04), strength=1.6)

parts = []
rnd = random.Random(12)


def shade_faces(ob, pred, mat=gold_dk):
    """Faces for which pred(centre, normal) holds take the dark gold."""
    ob.data.materials.append(mat)
    k = len(ob.data.materials) - 1
    for p in ob.data.polygons:
        if pred(p.center, p.normal):
            p.material_index = k
    return ob


def bone_paint(ob, seed, hi_edge=0.0):
    """Mid bone with pale yellow-green catching the rims and knobs, darker
    in the hollows, a little speckle."""
    r = random.Random(seed)
    noise = [r.random() for _ in ob.data.vertices]
    me = ob.data

    def fn(p, co, n, li):
        vi = me.loops[li].vertex_index
        u = noise[vi]
        rim = ob["rim"][vi] if "rim" in ob else 0.5
        c = mk.mixc(BONE_MID, BONE_HI, max(0.0, rim - 0.35) / 0.65 * 0.9 + (u - 0.5) * 0.3 + hi_edge)
        if n.z < -0.5:
            c = mk.mixc(c, BONE_DK, 0.5)
        if u < 0.08:
            c = mk.mixc(c, BONE_DK, 0.6)
        return c
    return mk.vpaint(ob, fn)


# the foot: the spine's tail, a jagged bone shard stuck in the ground,
# flaring to the left at its top and breaking at its lower end into a long
# point running down to the right and two short ones
fr = random.Random(3)
ring_n = 6
foot_rows = ((-0.08, 0.14), (0.22, 0.22), (0.5, 0.3), (0.8, 0.34))
verts, faces = [], []
for i, (z, rr) in enumerate(foot_rows):
    lean = 0.2 * (1.0 - (z + 0.08) / 0.88)
    for j in range(ring_n):
        a = 2 * math.pi * j / ring_n + fr.uniform(-0.25, 0.25)
        k = fr.uniform(0.65, 1.25) if i < 2 else fr.uniform(0.85, 1.12)
        if i >= 2 and math.cos(a) < -0.3:
            k *= 1.22
        zz = z + (fr.uniform(-0.1, 0.12) if i < 2 else fr.uniform(-0.03, 0.03))
        verts.append((lean + rr * k * math.cos(a), YP + rr * k * math.sin(a), zz))
for i in range(len(foot_rows) - 1):
    for j in range(ring_n):
        j2 = (j + 1) % ring_n
        faces.append((i * ring_n + j, i * ring_n + j2, (i + 1) * ring_n + j2, (i + 1) * ring_n + j))
tips = [len(verts), len(verts) + 1, len(verts) + 2]
verts += [(0.32, YP - 0.04, -0.36), (0.0, YP + 0.1, -0.24), (0.2, YP + 0.16, -0.14)]
owner = [0, 0, 1, 1, 2, 0]
for j in range(ring_n):
    j2 = (j + 1) % ring_n
    faces.append((j, tips[owner[j]], j2))
    if owner[j] != owner[j2]:
        faces.append((j2, tips[owner[j]], tips[owner[j2]]))
faces.append(tuple(tips))
top = (len(foot_rows) - 1) * ring_n
faces.append(tuple(top + j for j in range(ring_n)))
foot = mk.mesh("foot", verts, faces, mat=bone)
mk._fix_normals(foot)
foot["rim"] = [0.75 if v.co.x < -0.08 else 0.3 for v in foot.data.vertices]
bone_paint(foot, 5)
parts.append(mk.flat_shade(foot))

# the vertebrae: rounded bodies growing toward the top, unevenly spaced,
# each turned and tipped a little, an arch at the back; knobbed processes
# jut to the right, shorter stubs to the left and a spine down the back
n = 8
z0 = 0.72
sizes = [0.8 + 0.2 * (k / (n - 1)) for k in range(n)]
gaps = [sz * rnd.uniform(0.86, 1.14) for sz in sizes]
total = Z_POST - z0
pitches = [total * g / sum(gaps) for g in gaps]
z = z0


def lump(loc, size, seed, rim, name):
    ob = mk.ico(1.0, 1, loc=loc, scale=size, mat=bone, name=name, seed=seed, rough=0.08)
    ob["rim"] = [rim] * len(ob.data.vertices)
    return hk.smooth(bone_paint(ob, seed), 60)


def process(p0, d, L, rad, seed, name, blade=None):
    """A stubby, down-raking process with a knob at its end; given a blade
    side it is a tall, thin blade instead of a round stub."""
    p1 = p0 + d * L
    if blade is None:
        pr = mk.rod(tuple(p0), tuple(p1), rad, rad * 0.62, seg=6, mat=bone, name=name)
    else:
        mid = p0 + d * L * 0.5 + Vector((0, 0, -0.04))
        prof = [(math.cos(2 * math.pi * j / 6), 0.38 * math.sin(2 * math.pi * j / 6)) for j in range(6)]
        pr = mk.sweep([tuple(p0), tuple(mid), tuple(p1)], prof, side=tuple(blade),
                      scales=[(rad * 2.2, rad), (rad * 1.3, rad * 0.8), (rad * 0.6, rad * 0.6)], mat=bone, name=name)
    mk.jitter(pr, rad * 0.12, seed=seed)
    pr["rim"] = [0.6 + 0.4 * max(0.0, (v.co - p0).dot(d) / L) for v in pr.data.vertices]
    bone_paint(pr, seed)
    return [hk.smooth(pr, 40), lump(tuple(p1 + d * rad * 0.3), (rad * 1.15, rad * 1.0, rad * 0.9), seed + 1, 0.9,
                                    name + "knob")]


for k in range(n):
    s = sizes[k] * rnd.uniform(0.95, 1.05)
    pitch = pitches[k]
    h = pitch * 0.8
    R = 0.37 * s
    prof = [(0.0, 0.0), (0.68 * R, 0.0), (0.9 * R, 0.1 * h), (R, 0.34 * h), (0.98 * R, 0.66 * h),
            (0.9 * R, 0.9 * h), (0.68 * R, h), (0.0, h)]
    body = mk.lathe(prof, seg=10, mat=bone, name="vert%d" % k)
    for v in body.data.vertices:
        if v.co.y > 0:
            v.co.y *= 0.88  # flatter at the back, where the arch sits
    mk.jitter(body, 0.018, seed=20 + k)
    body["rim"] = [max(0.0, (math.hypot(v.co.x, v.co.y) / R - 0.8) / 0.2) * (0.6 if v.co.x > 0 else 1.0)
                   for v in body.data.vertices]
    twist = rnd.uniform(-16, 16)
    tipx, tipy = rnd.uniform(-5, 5), rnd.uniform(-5, 5)
    zc = z + pitch * 0.11
    xo = 0.035 * math.sin(2 * math.pi * k / n)  # a gentle sway down the column
    mk.xform(body, rot=(tipx, tipy, twist), loc=(xo, YP, zc))
    bone_paint(body, 30 + k)
    parts.append(hk.smooth(body, 50))
    parts.append(hk.cylinder(0.64 * R, pitch * 0.24, seg=10, x=xo, y=YP, z=z, mat=bone_dk,
                             name="disc%d" % k))
    zm = zc + h * 0.52
    tw = math.radians(twist)
    rot = Matrix.Rotation(tw, 3, "Z")
    # the arch at the back and its spine
    parts.append(lump((xo, YP + 0.78 * R, zm), (0.42 * R, 0.34 * R, 0.36 * h), 90 + k, 0.4, "arch%d" % k))
    parts += process(Vector((xo, YP + 0.85 * R, zm)),
                     (rot @ Vector((0.1, 1.0, -rnd.uniform(0.6, 1.0)))).normalized(),
                     0.27 * s * rnd.uniform(0.7, 1.2), 0.09 * s, 110 + k * 3, "spinous%d" % k,
                     blade=rot @ Vector((1.0, 0.0, 0.0)))
    # knobbly processes out to the right, short stubs to the left
    ar = math.radians(rnd.uniform(10, 32))
    parts += process(Vector((xo + 0.7 * R, YP + 0.15 * R, zm)),
                     (rot @ Vector((math.cos(ar), math.sin(ar), -0.4))).normalized(),
                     0.13 * rnd.uniform(0.8, 1.2), 0.105 * s, 130 + k * 3, "right%d" % k)
    al = math.radians(rnd.uniform(35, 60))
    parts += process(Vector((xo - 0.7 * R, YP + 0.2 * R, zm)),
                     (rot @ Vector((-math.cos(al), math.sin(al), -0.45))).normalized(),
                     0.08 * rnd.uniform(0.8, 1.2), 0.075 * s, 150 + k * 3, "left%d" % k)
    z += pitch

# a gold cup at the top of the column and a short stem up behind the sunburst
cup = mk.lathe([(0.0, 0.0), (0.32, 0.0), (0.44, 0.1), (0.48, 0.26), (0.36, 0.3), (0.0, 0.3)], seg=16,
               mat=gold_dk, name="cup")
mk.xform(cup, loc=(0.0, YP, Z_POST - 0.12))
parts.append(hk.smooth(hk.bevel(cup, 0.02, 1), 40))
parts.append(hk.smooth(mk.rod((0.0, YP, Z_POST), (0.0, YS + 0.2, Z_SUN), 0.2, 0.16, seg=10, mat=gold_dk,
                              name="stem"), 40))

# the sunburst: a gold boss tilted back to the sky, rays round it dark at
# the root and bright at the point, the ruby set in its face
TILT_BACK = 22.0
sun = []


def ray_paint(ob, r_in, r_out):
    return mk.vpaint(ob, lambda p, co, n, li: mk.mixc(GOLD_DK, RAY_HI, (math.hypot(co.x, co.y) - r_in) /
                                                         (r_out - r_in) * 1.8) if n.z > -0.2 else GOLD_DK)


boss = mk.lathe([(0.0, -0.08), (0.44, -0.08), (0.48, 0.0), (0.4, 0.08), (0.3, 0.12), (0.0, 0.14)], seg=20,
                mat=gold, name="boss")
mk.vpaint(boss, lambda p, co, n, li: GOLD if n.z > 0.2 else GOLD_DK)
sun.append(hk.smooth(boss, 35))
rays = 16
for k in range(rays):
    a = 2 * math.pi * k / rays + math.pi / 2
    ca, sa = math.cos(a), math.sin(a)
    long = k % 2 == 0
    L = (1.2 if long else 0.82) * (1.0 + 0.1 * (1.0 - abs(sa)))
    if sa < -0.5:
        L *= 0.62 if long else 0.7  # the post is below: shorter rays there
    w = 0.2 if long else 0.16
    verts = [(0.4 * ca - w * sa, 0.4 * sa + w * ca, 0.0), (0.4 * ca, 0.4 * sa, 0.06),
             (0.4 * ca + w * sa, 0.4 * sa - w * ca, 0.0), (0.4 * ca, 0.4 * sa, -0.05),
             (L * ca, L * sa, 0.0)]
    faces = [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4), (0, 3, 2, 1)]
    ray = mk.mesh("ray%d" % k, verts, faces, mat=gold)
    mk._fix_normals(ray)
    sun.append(ray_paint(ray, 0.4, L))
# a second, finer crown of rays between the first, set a little behind
for k in range(rays):
    a = 2 * math.pi * (k + 0.5) / rays + math.pi / 2
    ca, sa = math.cos(a), math.sin(a)
    L = 0.74 if sa > -0.5 else 0.52
    w = 0.1
    verts = [(0.36 * ca - w * sa, 0.36 * sa + w * ca, -0.03), (0.36 * ca, 0.36 * sa, 0.02),
             (0.36 * ca + w * sa, 0.36 * sa - w * ca, -0.03), (0.36 * ca, 0.36 * sa, -0.07),
             (L * ca, L * sa, -0.03)]
    ray = mk.mesh("fine%d" % k, verts, [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4), (0, 3, 2, 1)], mat=gold)
    mk._fix_normals(ray)
    sun.append(ray_paint(ray, 0.36, L))
gem = mk.ico(0.26, 1, loc=(0.0, 0.0, 0.12), scale=(1.0, 0.88, 0.55), mat=ruby, name="ruby")
sun.append(mk.flat_shade(gem))
setting = mk.lathe([(0.25, 0.06), (0.33, 0.08), (0.33, 0.15), (0.27, 0.17)], seg=20, mat=gold_dk,
                   name="setting")
sun.append(hk.smooth(setting, 40))
# the disc's own frame has z toward the viewer; stand it up facing -Y and
# lean it back
M = (Matrix.Translation((0.0, YS, Z_SUN)) @ Matrix.Rotation(math.radians(-TILT_BACK), 4, "X")
     @ Matrix.Rotation(math.radians(90), 4, "X"))
for p in sun:
    mk.matrix(p, M)
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


# the horns, left half as (x, z, in-plane half width, half depth): from the
# tip at the gap along a thin, deep top bar sloping down to the corner, down
# the side, then diagonally in to end behind the sunburst's boss
HORN = ((x_at(22.2), z_at(42.6, YR), 0.065, 0.15), (x_at(13), z_at(44.6, YR), 0.085, 0.15),
        (x_at(4.3), z_at(47.2, YR), 0.11, 0.14), (x_at(7.0), z_at(50.0, YR), 0.14, 0.13),
        (x_at(7.4), z_at(53.5, YR), 0.15, 0.12), (x_at(9.0), z_at(57.5, YR), 0.19, 0.12),
        (x_at(11.5), z_at(61.5, YR), 0.21, 0.12), (x_at(16.0), z_at(65.0, YR), 0.19, 0.12),
        (x_at(23.5), z_at(67.0, YR), 0.14, 0.11))
PROF = [(1.0, 0.0), (0.62, 1.0), (-0.62, 1.0), (-1.0, 0.0), (-0.62, -1.0), (0.62, -1.0)]
CENTRE = Vector((0.0, YR, z_at(52, YR)))
for side in (-1, 1):
    ctrl = [(side * h[0], YR - (0.14 if i == len(HORN) - 1 else 0.0), h[1]) for i, h in enumerate(HORN)]
    path = catmull(ctrl, per=3)
    ws = [(HORN[0][2], HORN[0][3])]
    for i in range(len(HORN) - 1):
        for st in range(1, 4):
            f = st / 3
            ws.append((HORN[i][2] + (HORN[i + 1][2] - HORN[i][2]) * f, HORN[i][3] + (HORN[i + 1][3] - HORN[i][3]) * f))
    horn = mk.sweep([tuple(p) for p in path], PROF, side=(0, 1, 0), scales=ws[:len(path)], mat=gold,
                    name="horn%d" % side)
    shade_faces(horn, lambda c, nn: nn.z < -0.35 or (Vector((CENTRE.x - c.x, 0, CENTRE.z - c.z)).normalized()
                                                     .dot(Vector((nn.x, 0, nn.z))) > 0.55))
    z_low = z_at(57.0, YR)
    mk.vpaint(horn, lambda p, co, n, li: mk.mixc(GOLD_HI if n.z > 0.3 else GOLD, GOLD_LOW,
                                                 min(1.0, max(0.0, (z_low - co.z) / 0.5))))
    parts.append(hk.smooth(horn, 40))
    # the tip: a flat blade turned in over the gap
    tx, tz = side * HORN[0][0], HORN[0][1]
    tipb = mk.mesh("tip%d" % side, [(tx - side * 0.2, YR - 0.16, tz - 0.05), (tx - side * 0.2, YR + 0.16, tz - 0.05),
                                    (tx + side * 0.055, YR + 0.12, tz + 0.05), (tx + side * 0.055, YR - 0.12, tz + 0.05),
                                    (tx - side * 0.2, YR - 0.15, tz - 0.13), (tx - side * 0.2, YR + 0.15, tz - 0.13),
                                    (tx + side * 0.055, YR + 0.09, tz - 0.02), (tx + side * 0.055, YR - 0.09, tz - 0.02)],
                   [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], mat=gold)
    mk._fix_normals(tipb)
    shade_faces(tipb, lambda c, nn: nn.z < -0.35)
    mk.vpaint(tipb, lambda p, co, n, li: GOLD_HI if n.z > 0.3 else GOLD)
    parts.append(hk.smooth(hk.bevel(tipb, 0.015, 1), 40))
    # the barbs at mid height: a forked pair of flat blades crossing the
    # side, one raking up and out, one down and out
    jx, jz = side * x_at(8.0), z_at(55.0, YR)
    out = 1.0 if jx > 0 else -1.0
    for ang, L, w, dep in ((20.0, 0.72, 0.16, 0.13), (-24.0, 0.68, 0.15, 0.12)):
        a = math.radians(ang)
        d = Vector((out * math.cos(a), 0.0, math.sin(a)))
        tv = Vector((-d.z, 0.0, d.x))
        b0 = Vector((jx, YR, jz)) - d * 0.12
        tp = b0 + d * L
        vs = [b0 + tv * w + Vector((0, -dep, 0)), b0 + tv * w + Vector((0, dep, 0)),
              b0 - tv * w + Vector((0, dep, 0)), b0 - tv * w + Vector((0, -dep, 0)),
              tp + tv * w * 0.25 + Vector((0, -dep * 0.4, 0)), tp + tv * w * 0.25 + Vector((0, dep * 0.4, 0)),
              tp - tv * w * 0.25 + Vector((0, dep * 0.4, 0)), tp - tv * w * 0.25 + Vector((0, -dep * 0.4, 0))]
        barb = mk.mesh("barb", [tuple(v) for v in vs],
                       [(0, 1, 2, 3), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0), (4, 7, 6, 5)],
                       mat=gold)
        mk._fix_normals(barb)
        shade_faces(barb, lambda c, nn: nn.z < -0.3)
        mk.vpaint(barb, lambda p, co, n, li: GOLD_HI if n.z > 0.3 else GOLD)
        parts.append(hk.smooth(hk.bevel(barb, 0.015, 1), 35))

mk.tidy(parts)
mk.whiten(parts)
parts[0].name = NAME  # the joined object, and the glTF node, take this name
ob = hk.finish(parts, r"D:\OKReplace\lodes\hand\models\ZONGLYPH.glb",
               {"replacesTexture": "zondeathtotemwithring", "replacesPiece": "zonglyph"})
print("MISCKIT_TRIS", NAME, mk.tris(ob))
# plain display transform, so glowing colours stay as saturated as a game shows them
bpy.context.scene.view_settings.view_transform = "Standard"
hk.renders(ob, r"D:\OKReplace\lodes\hand\renders", NAME, PIC, HOT, scale=4)
