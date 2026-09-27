"""TARMANA, the Taros mana lodestone: a compact glossy black creature on the
Taros ring, six slim limbs splayed round it, gripping a near-black cup whose
olive horn claws flare out like wings round a packed cluster of fire crystals
burning over a molten core."""
import math
import os
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tarkit as tk  # noqa: E402
from tarkit import hk  # noqa: E402

NAME = "TARMANA"
PIC = r"D:\OKReplace\lodes\sprites\TARMANA.png"
HOT = (31, 111)
GLB = r"D:\OKReplace\lodes\hand\models\TARMANA.glb"
OUT = r"D:\OKReplace\lodes\hand\renders"

hk.reset()
M = tk.materials()

# fire crystal: each facet its own ramp, red and brown-red shading to orange
# and pink, with yellow fire burning up most of the hot facets and orange,
# yellow and pink patches mottled through the middle
HOTYEL, YELLOW, ORANGE, PINK = (255, 228, 120), (250, 204, 60), (250, 116, 50), (242, 62, 72)
RED, DEEPRED, BROWN = (206, 30, 14), (145, 26, 14), (132, 44, 24)
FACE = {
    "hot": [(0.0, HOTYEL), (0.3, YELLOW), (0.5, ORANGE), (0.66, PINK), (0.8, (200, 30, 20)), (1.0, (185, 25, 14))],
    "orange": [(0.0, YELLOW), (0.25, ORANGE), (0.6, (236, 100, 48)), (0.8, (200, 56, 30)), (1.0, (180, 30, 14))],
    "pink": [(0.0, ORANGE), (0.3, PINK), (0.7, (222, 56, 66)), (1.0, (170, 30, 30))],
    "red": [(0.0, ORANGE), (0.28, (222, 60, 26)), (0.5, (200, 30, 14)), (1.0, (176, 22, 10))],
    "brown": [(0.0, (190, 80, 34)), (0.3, BROWN), (1.0, (120, 26, 14))],
    "deep": [(0.0, (200, 70, 28)), (0.35, DEEPRED), (1.0, (118, 14, 8))],
}
ORDERS = [  # dark red, hot centre, brown-red, fire burning low
    ["deep", "red", "brown", "pink", "deep", "red"],
    ["hot", "orange", "pink", "hot", "pink", "orange"],
    ["brown", "deep", "red", "brown", "deep", "pink"],
    ["hot", "red", "deep", "pink", "brown", "orange"],
]


def fire(k):
    import random
    rnd = random.Random(40 + k)
    faces = [FACE[f] for f in ORDERS[k]]
    blots = []
    for f in range(6):
        for _ in range(2):
            blots.append((f, rnd.uniform(0.3, 0.72), rnd.uniform(0.06, 0.12),
                          rnd.choice([ORANGE, YELLOW, PINK, PINK]), rnd.uniform(0.45, 0.8)))
    return tk.fire_faces("tar_fire_%d" % k, faces, strength=1.0, seed=k, blots=blots)


FIRES = [fire(k) for k in range(4)]
edge = tk.glass("tar_fire_edge", (128, 22, 8), glow=0.85)
white_hot = tk.glow("tar_white_hot", (1.0, 0.95, 0.8), (1.0, 0.93, 0.74), 1.6, rough=0.2)
gold_top = tk.glass("tar_fire_gold", (250, 186, 64), glow=0.95)
core = tk.glow("tar_molten_core", (1.0, 0.78, 0.3), (1.0, 0.74, 0.26), 0.9, rough=0.4)
ember = tk.fire_faces("tar_ember", [[(0.0, (255, 200, 80)), (0.5, (246, 130, 24)), (1.0, (226, 48, 6))],
                                    [(0.0, (250, 150, 60)), (0.5, PINK), (1.0, RED)]] * 3, strength=0.95, seed=9)

# the cup is near black, the claws dark olive-black with pale olive ridges,
# the rim a thin grey band
cup_black = hk.pbr("tar_cup_black", (0.062, 0.057, 0.026), rough=0.34, metal=0.6)
cup_band = hk.pbr("tar_cup_band", (0.11, 0.10, 0.055), rough=0.3, metal=0.7)
claw_olive = hk.pbr("tar_claw_olive", (0.09, 0.087, 0.052), rough=0.4, metal=0.3)
membrane = hk.pbr("tar_wing_membrane", (0.085, 0.083, 0.05), rough=0.45, metal=0.2)
claw_ridge = hk.pbr("tar_claw_ridge", tk.lin((150, 150, 110)), rough=0.35, metal=0.3)
rim_grey = hk.pbr("tar_rim_grey", (0.30, 0.29, 0.24), rough=0.35, metal=0.5)
# jet black gloss for the creature under a clear coat, sharp white glints,
# pale grey streaks along the limb tops, dark red under the limbs
gloss = hk.pbr("tar_gloss_black", (0.01, 0.0095, 0.0095), rough=0.12, metal=0.1)
_b = gloss.node_tree.nodes["Principled BSDF"]
_b.inputs["Specular IOR Level"].default_value = 0.3
_b.inputs["Coat Weight"].default_value = 0.5
_b.inputs["Coat Roughness"].default_value = 0.06
sheen = hk.pbr("tar_gloss_streak", tk.lin((156, 156, 164)), rough=0.15, metal=0.2)
knob = hk.pbr("tar_gloss_knob", tk.lin((140, 140, 150)), rough=0.1, metal=0.3)
blood = hk.pbr("tar_limb_red", tk.lin((110, 10, 10)), rough=0.3, emit=tk.lin((110, 10, 10)), strength=0.35)


def seen(col, row, y):
    """The point at depth y that the classic camera shows at picture (col, row)."""
    return ((col - HOT[0]) / 16.0, y, (HOT[1] - row - 16.0 * y) / 8.0)


def crystal(base, apex, r0, r1, tip, sides=6, fire=0, top=None, deep=1.4, jitter=None, spin=0.0, **kw):
    """A fire crystal from base to apex; r0 and r1 are its half widths across
    the picture, and deep stretches it front to back so it is chunky in 3D
    while keeping the painted width."""
    axis = [apex[k] - base[k] for k in range(3)]
    length = math.sqrt(sum(c * c for c in axis))
    mats = [FIRES[fire], edge] + ([top] if top else [])
    jit = []
    for k in range(sides):
        a = spin + 2 * math.pi * k / sides
        e = 1.0 / math.sqrt(math.cos(a) ** 2 + (math.sin(a) / deep) ** 2)
        jit.append(e * (jitter[k % len(jitter)] if jitter else 1.0))
    return tk.gem(base, axis, [(0.0, r0), (length - tip, r1)], tip, mats, sides=sides, jitter=jit, spin=spin, **kw)


J6 = [1.0, 0.88, 1.07, 0.93, 1.03, 0.9]
A6 = [0, 0.14, -0.1, 0.08, -0.12, 0.05]
# the burst, first so the joined mesh keeps its UV map: chunky crystals
# packed tight, tips where the picture shows them, the rear ones leaning
# well back (+Y) rather than rising high
parts = [crystal((-0.06, 0.26, 3.95), seen(22.5, 29.5, 2.1), 0.22, 0.25, 0.36, fire=0, spin=0.3, jitter=J6,
                 angles=A6, rim=0.012, deep=1.5, name="crystal")]
parts[0].name = NAME  # the joined object takes this name
# the chunky centre one, leaning back just so the front facet of its point
# faces the camera, white-hot
parts.append(crystal((0.0, -0.14, 3.95), (-0.16, 1.376, 6.375), 0.31, 0.34, 0.45, fire=1, top=white_hot,
                     spin=math.radians(-60), jitter=[1.0, 0.92, 1.05, 0.95, 1.0, 0.9],
                     gold_dir=(0.0, -0.447, 0.894), gold_min=0.5, gold_count=1, rim=0.014, deep=1.3,
                     name="crystal"))
BURST = [  # root, point, half width at root, under the point, point length, sides, broken slant, fire, spin
    ((0.16, 0.14, 4.0), seen(40.5, 34, 1.8), 0.2, 0.23, 0.32, 6, None, 2, 0.9),   # right, gold topped
    ((-0.45, -0.05, 4.1), seen(14, 52.5, 0.9), 0.22, 0.25, 0.3, 6, None, 3, 0.2),  # leaning out, left
    ((-0.26, 0.36, 4.0), seen(21, 43, 1.6), 0.21, 0.24, 0.1, 6, (0.35, 0.5), 2, 1.3),  # back left, snapped
    ((0.40, 0.30, 4.0), seen(42.5, 46, 1.5), 0.2, 0.23, 0.28, 6, None, 0, 2.1),  # back right
    ((0.08, 0.44, 4.0), seen(33.5, 33.5, 2.1), 0.2, 0.23, 0.3, 6, None, 3, 0.6),  # back, behind the centre
    ((-0.2, 0.1, 4.0), seen(25.5, 42, 1.5), 0.2, 0.23, 0.1, 6, (-0.3, 0.6), 1, 1.8),  # left of centre, snapped
    ((0.24, -0.10, 4.0), seen(36, 47, 1.2), 0.2, 0.23, 0.26, 6, None, 0, 2.6),  # right of centre
    ((-0.10, 0.20, 4.0), seen(27, 36, 1.9), 0.2, 0.23, 0.3, 6, None, 2, 0.4),  # back, between
]
for i, (b, a, r0, r1, tip, sides, brk, fire, spin) in enumerate(BURST):
    kw = {}
    if i == 0:
        kw = dict(top=gold_top, gold_dir=(-0.3, -0.5, 0.8), gold_min=0.55)
    parts.append(crystal(b, a, r0, r1, tip, sides=sides, fire=fire, spin=spin, broken=brk,
                         jitter=J6[:sides], rim=0.014, name="crystal", **kw))
# a few short, stubby embers round the core, leaning out over the rim
BED = [((-0.40, -0.20, 4.1), (-0.92, -0.55, 5.0), 0.15, 0.18, 0.26, None),
       ((0.40, -0.18, 4.1), (0.90, -0.46, 4.9), 0.14, 0.17, 0.2, None)]
for b, a, r0, r1, tip, brk in BED:
    axis = [a[k] - b[k] for k in range(3)]
    length = math.sqrt(sum(c * c for c in axis))
    parts.append(tk.gem(b, axis, [(0.0, r0), (length - tip, r1)], tip, [ember, edge], sides=5, broken=brk,
                        jitter=J6[:5], rim=0.01, name="crystal"))

# the molten core the crystals grow from, sitting back in the cup's mouth so
# a dark band of inner wall shows under it
parts.append(tk.ellipsoid((0, 0.08, 4.02), (0.52, 0.50, 0.18), seg=14, rings=6, mat=core, name="core"))

parts += tk.ring_base(M, seg=28)
parts += tk.bone_spikes(M, zc=1.2)

# the creature: a compact glossy black body under the cup, two glossy knobs
# on its front, fingers gripping the cup's foot
body = tk.lathe([(0, 0.0), (0.2, 0.0), (0.3, 0.25), (0.4, 0.6), (0.5, 1.0), (0.57, 1.45), (0.58, 1.85),
                 (0.55, 2.2), (0.5, 2.5), (0, 2.6)], seg=18, mat=gloss, name="body")
hk.smooth(body, 60)
parts.append(body)
for side in (-1, 1):
    parts.append(tk.ellipsoid((side * 0.21, -0.51, 1.86), (0.1, 0.08, 0.09), seg=10, rings=6, mat=knob,
                              name="knob"))
for ang in (58, 122, 238, 302):
    parts.append(tk.tube([tk.polar(0.44, ang, 2.45), tk.polar(0.68, ang, 2.72), tk.polar(0.76, ang, 3.02),
                          tk.polar(0.82, ang, 3.28), tk.polar(0.84, ang, 3.42)],
                         [0.11, 0.1, 0.08, 0.045, 0.0], seg=7, sub=3, mat=gloss, name="finger", flat=0.6,
                         up=(math.cos(math.radians(ang)), math.sin(math.radians(ang)), 0.0), shade=60))


def tentacle(pts, radii, red_from=0.78, name="tentacle"):
    """A slim glossy black limb tapering to a point, a pale sheen streak along
    its top; the underside and the last stretch to the tip are dark red."""
    ob = tk.tube(pts, radii, seg=8, sub=3, mat=gloss, name=name, flat=0.8, shade=60)
    ob.data.materials.append(blood)
    ob.data.materials.append(sheen)
    tip = pts[-1]
    total = sum(math.dist(p, q) for p, q in zip(pts, pts[1:]))
    for p in ob.data.polygons:
        c = p.center
        near = math.dist(tuple(c), tip) < (1.0 - red_from) * total
        if near or p.normal.z < -0.4:
            p.material_index = 1
    tk.ridge(ob, 0, 8, 0.034, 2)
    return hk.smooth(ob, 60)


# six slim limbs: a pair splaying up and out to the ring's back, a pair out
# over its sides, and two front legs in a V down to the ground
LIMB = [0.18, 0.18, 0.16, 0.11, 0.0]
for side in (-1, 1):
    back = 0.6 if side < 0 else 0.42  # the left one reaches further back
    parts.append(tentacle([(side * 0.35, 0.2, 1.72), (side * 0.62, back * 0.66, 1.38),
                           (side * 0.88, back * 0.92, 0.95), (side * 1.04, back, 0.6), (side * 1.12, back, 0.44)],
                          LIMB))
    parts.append(tentacle([(side * 0.4, -0.12, 1.3), (side * 0.66, -0.18, 0.9), (side * 0.9, -0.2, 0.62),
                           (side * 1.12, -0.2, 0.44), (side * 1.3, -0.24, 0.4)], [0.19, 0.19, 0.18, 0.13, 0.0]))
    parts.append(tentacle([(side * 0.1, -0.4, 1.35), (side * 0.16, -0.58, 1.05), (side * 0.26, -0.72, 0.72),
                           (side * 0.38, -0.8, 0.42), (side * 0.45, -0.82, 0.2), (side * 0.47, -0.83, 0.1)],
                          [0.18, 0.19, 0.19, 0.17, 0.11, 0.0]))

# the cup: a wide near-black bowl straight on the body, open at the top, a
# thin grey rim round its mouth
cup = tk.lathe([(0, 2.4), (0.52, 2.4), (0.64, 2.8), (0.78, 3.4), (0.88, 4.05), (0.90, 4.40), (0.83, 4.40),
                (0.78, 4.18), (0.58, 3.92), (0, 3.9)], seg=18, mat=cup_black, name="cup")
tk.bevel_mat(cup, 0.02, 1, angle=35)
hk.smooth(cup, 40)
parts.append(cup)
parts.append(tk.torus(0.87, 0.05, 4.42, seg=24, rseg=5, mat=rim_grey, name="rim"))
parts.append(tk.torus(0.80, 0.045, 3.62, seg=24, rseg=5, mat=cup_band, name="band"))
parts.append(tk.torus(0.63, 0.04, 2.86, seg=24, rseg=5, mat=cup_band, name="band"))

# dark olive wings off the cup's foot: on each side two horn ribs flare out
# and back, bulging wide at mid height, their points curling in over the
# rim, with a dark membrane stretched between them
RIB = [0.10, 0.13, 0.13, 0.12, 0.10, 0.065, 0.0]
WINGS = [  # front rib (angle, path of (r, z)), back rib; they hug the cup low
    # down and are widest a little above its middle
    ((180, [(0.5, 2.6), (0.72, 3.0), (0.98, 3.5), (1.22, 4.05), (1.36, 4.6), (1.30, 5.1), (1.08, 5.45)]),
     (140, [(0.5, 2.6), (0.9, 3.0), (1.15, 3.5), (1.28, 4.1), (1.26, 4.7), (1.1, 5.2), (0.9, 5.5)])),
    ((0, [(0.5, 2.6), (0.72, 3.2), (1.05, 3.7), (1.26, 4.3), (1.24, 4.9), (1.04, 5.45), (0.82, 5.8)]),
     (40, [(0.5, 2.6), (0.9, 3.0), (1.1, 3.55), (1.15, 4.2), (1.1, 4.85), (0.96, 5.5), (0.74, 6.15)])),
]

def rib(ang, path):
    c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    pts = [(r * c, r * s, z) for r, z in path]
    ob = tk.tube(pts, RIB, seg=6, sub=3, mat=claw_olive, name="claw", flat=0.7, up=(c, s, 0.0), shade=50)
    ob.data.materials.append(claw_ridge)
    # pale olive along the outer and inner ridges only
    tk.ridge(ob, 0, 6, 0.02, 1)
    tk.ridge(ob, 3, 6, 0.016, 1)
    hk.smooth(ob, 50)
    return ob, pts


def web(A, B, upto, sag=0.14, thick=0.03, dip=0.3):
    """A membrane between two rib paths, sagging in toward the cup, its free
    edge dipping between the rib points."""
    import bmesh
    from mathutils import Vector
    pa = [p for p, _ in tk._catmull(A, 3)]
    pb = [p for p, _ in tk._catmull(B, 3)]

    def at(path, u):
        x = u * (len(path) - 1)
        i = min(int(x), len(path) - 2)
        return path[i].lerp(path[i + 1], x - i)

    bm = bmesh.new()
    cols, rows = 7, 16
    grid = []
    for i in range(rows):
        row = []
        for j in range(cols):
            t = j / (cols - 1)
            u = 0.05 + (upto - 0.05) * i / (rows - 1) * (1.0 - dip * math.sin(math.pi * t))
            p = at(pa, u).lerp(at(pb, u), t)
            out = Vector((p.x, p.y, 0.0))
            if out.length > 1e-4:
                p = p - out.normalized() * sag * math.sin(math.pi * t) * min(1.0, 3 * i / rows)
            row.append(bm.verts.new(p))
        grid.append(row)
    for r0, r1 in zip(grid, grid[1:]):
        for j in range(cols - 1):
            bm.faces.new((r0[j], r0[j + 1], r1[j + 1], r1[j]))
    ob = hk._object("membrane", bm, membrane)
    mod = ob.modifiers.new("solid", "SOLIDIFY")
    mod.thickness = thick
    mod.offset = 0.0
    hk._apply(ob)
    return hk.smooth(ob, 60)


for front, back in WINGS:
    fa, pf = rib(*front)
    fb, pb = rib(*back)
    parts += [fa, fb, web(pf, pb, 0.86)]

tris = Counter()
for p in parts:
    tris[p.name.split(".")[0]] += sum(len(f.vertices) - 2 for f in p.data.polygons)
print("PARTS", dict(tris))
ob = hk.finish(parts, GLB, {"replacesTexture": "tarmana_tarosmeetsdivine", "replacesPiece": "tarlode"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
tk.standard_view()
hk.renders(ob, OUT, NAME, PIC, HOT, scale=4)
