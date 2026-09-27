"""TARMANA, the Taros mana lodestone: a sprawl of glossy black tentacles on the
Taros ring, a short dark waist, and a clawed olive-bronze cup with a pale gold
rim gripping an erupting cluster of fire crystals over a white-hot core."""
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

# fire crystal: molten orange at the root, deep crimson up the shafts, red at the points
fire = tk.gem_material("tar_fire_crystal",
                       [(0.0, (236, 150, 50)), (0.10, (214, 76, 26)), (0.28, (166, 26, 15)), (0.70, (146, 18, 12)),
                        (0.88, (190, 40, 22)), (1.0, (216, 70, 38))],
                       [(0.0, (255, 170, 60)), (0.12, (170, 48, 12)), (0.30, (60, 6, 3)), (0.75, (54, 5, 3)),
                        (1.0, (120, 22, 8))],
                       strength=1.0, rough=0.22, spec=0.5, tint=(1.0, 0.3, 0.2), seed=3)
fire_rim = tk.glow("tar_fire_rim", (0.85, 0.09, 0.025), (0.80, 0.07, 0.02), 0.6, rough=0.12)
white_hot = tk.glow("tar_white_hot", (1.0, 0.95, 0.82), (1.0, 0.92, 0.72), 3.0, rough=0.2)
core = tk.glow("tar_molten_core", (1.0, 0.60, 0.16), (1.0, 0.55, 0.12), 1.1, rough=0.4)
FIRE = [fire, fire_rim]
# the short crystals in the bed glow molten from root to point
ember = tk.gem_material("tar_ember_crystal",
                        [(0.0, (250, 196, 90)), (0.45, (240, 128, 44)), (1.0, (206, 58, 28))],
                        [(0.0, (255, 180, 70)), (0.5, (200, 80, 20)), (1.0, (110, 20, 6))],
                        strength=1.0, rough=0.25, spec=0.5, tint=(1.0, 0.5, 0.25), seed=5)

# olive-khaki bronze for the cup, darker olive lobes for the claws, a pale gold rim
cup_bronze = hk.pbr("tar_cup_bronze", (0.26, 0.21, 0.09), rough=0.36, metal=0.45)
claw_bronze = hk.pbr("tar_claw_bronze", (0.20, 0.175, 0.10), rough=0.4, metal=0.4)
claw_edge = hk.pbr("tar_claw_edge", (0.42, 0.37, 0.21), rough=0.3, metal=0.4)
rim_gold = hk.pbr("tar_rim_gold", (0.80, 0.68, 0.34), rough=0.28, metal=0.55)
# glossy black for the tentacles, so they carry white highlights
gloss = hk.pbr("tar_gloss_black", (0.11, 0.10, 0.095), rough=0.16, metal=0.5)
gloss.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.8


def crystal(base, apex, r0, r1, tip, sides=6, mats=FIRE, **kw):
    axis = [apex[k] - base[k] for k in range(3)]
    length = math.sqrt(sum(c * c for c in axis))
    return tk.gem(base, axis, [(0.0, r0), (length - tip, r1)], tip, mats, sides=sides, **kw)


def seen(col, row, y):
    """The point at depth y that the classic camera shows at picture (col, row)."""
    return ((col - HOT[0]) / 16.0, y, (HOT[1] - row - 16.0 * y) / 8.0)


# the crystal burst, first so the joined mesh keeps its UV map. Each point is
# placed where the picture shows it; the depth chosen for it fans the burst out
parts = [crystal((0.24, 0.12, 4.1), seen(22, 29, 0.7), 0.22, 0.30, 0.9, spin=0.3,
                 jitter=[1.0, 0.86, 1.08, 0.92, 1.04, 0.88], angles=[0, 0.14, -0.1, 0.08, -0.12, 0.05],
                 skew=(0.05, -0.03), rim=0.016, name="crystal")]
parts[0].name = NAME  # the joined object takes this name
# the middle one is snapped off, its broken face white-hot toward the camera
parts.append(crystal((0.04, -0.10, 4.1), seen(28.5, 40, 0.75), 0.27, 0.35, 0.24,
                     mats=[fire, fire_rim, white_hot], spin=math.radians(-90), jitter=[1.0, 0.9, 1.06, 0.95, 1.0, 0.86],
                     broken=(0.1, 0.95), skew=(0.02, 0.12), gold_dir=(0.1, -0.6, 0.8), gold_min=0.8, rim=0.016,
                     name="crystal"))
BURST = [  # root, point, radius at root, radius under the point, point length, sides, broken slant
    ((-0.20, 0.14, 4.15), seen(41, 34, 0.6), 0.20, 0.27, 0.75, 6, None),   # tall, right, crossing the first
    ((-0.30, 0.02, 4.2), seen(14.5, 52.5, 0.75), 0.18, 0.26, 0.55, 6, None),  # leaning far out, left
    ((-0.26, 0.30, 4.15), seen(20, 37, 1.0), 0.15, 0.21, 0.5, 5, None),  # back left, leaning away
    ((0.12, 0.36, 4.15), seen(34, 35, 0.95), 0.17, 0.23, 0.10, 6, (0.3, 0.5)),  # back, snapped
    ((0.32, 0.10, 4.2), seen(43, 46, 1.05), 0.15, 0.21, 0.45, 5, None),  # right, leaning back
    # a bed of short, stubby ones round the core, leaning out over the rim
    ((-0.40, -0.22, 4.12), (-0.98, -0.62, 5.25), 0.13, 0.17, 0.32, 5, None),
    ((0.08, -0.44, 4.12), (0.30, -1.0, 5.2), 0.13, 0.17, 0.26, 5, None),
    ((0.42, -0.22, 4.12), (0.96, -0.52, 5.0), 0.12, 0.16, 0.09, 5, (-0.4, 0.5)),
]
for i, (b, a, r0, r1, tip, sides, brk) in enumerate(BURST):
    mats = FIRE if i < 5 else [ember, fire_rim]
    parts.append(crystal(b, a, r0, r1, tip, sides=sides, spin=0.4 * i, broken=brk, mats=mats,
                         jitter=[1.0, 0.88, 1.07, 0.93, 1.02, 0.9][:sides], rim=0.012, name="crystal"))

# the white-hot core the crystals grow from, filling the cup's mouth
parts.append(tk.ellipsoid((0, -0.04, 4.08), (0.56, 0.52, 0.16), seg=12, rings=5, mat=core, name="core"))

parts += tk.ring_base(M, seg=28)
parts += tk.bone_spikes(M, zc=1.1)

# the black body the tentacles sprawl from, and the short dark waist
body = tk.lathe([(0, 0.0), (0.46, 0.0), (0.64, 0.35), (0.68, 0.75), (0.56, 1.1), (0.36, 1.36), (0, 1.4)],
                seg=10, mat=gloss, name="body")
hk.smooth(body, 50)
parts.append(body)
waist = tk.lathe([(0, 1.3), (0.40, 1.3), (0.32, 1.85), (0.40, 2.05), (0.40, 2.18), (0.33, 2.36),
                  (0.40, 2.6), (0, 2.62)], seg=8, mat=M["iron"], name="waist")
tk.flat(waist)
parts.append(waist)

# the cup: a wide bronze bowl, open at the top, with a rolled pale gold rim
cup = tk.lathe([(0, 2.5), (0.42, 2.5), (0.56, 2.95), (0.74, 3.55), (0.87, 4.15), (0.90, 4.42), (0.82, 4.42),
                (0.76, 4.18), (0.55, 4.05), (0, 4.02)], seg=14, mat=cup_bronze, name="cup")
tk.bevel_mat(cup, 0.02, 1, angle=35)
hk.smooth(cup, 40)
parts.append(cup)
parts.append(tk.torus(0.87, 0.065, 4.44, seg=20, rseg=4, mat=rim_gold, name="rim"))

# thick bronze claws gripping the cup from below, bulging out as wings, points hooked in
CLAWS = [  # angle, path of (r, z), widths
    (200, [(0.40, 2.35), (0.78, 2.9), (1.12, 3.7), (1.30, 4.5), (1.22, 5.0), (1.02, 5.35)],
     [0.12, 0.24, 0.30, 0.27, 0.18, 0.0]),
    (163, [(0.40, 2.35), (0.80, 2.9), (1.18, 3.7), (1.36, 4.45), (1.20, 4.95), (0.96, 5.2)],
     [0.12, 0.24, 0.30, 0.27, 0.18, 0.0]),
    (343, [(0.40, 2.35), (0.80, 2.9), (1.18, 3.7), (1.36, 4.5), (1.28, 5.1), (1.08, 5.6)],
     [0.12, 0.24, 0.30, 0.27, 0.18, 0.0]),
    (18, [(0.40, 2.35), (0.82, 2.9), (1.22, 3.7), (1.40, 4.5), (1.22, 5.3), (0.96, 6.1), (0.74, 6.75)],
     [0.12, 0.25, 0.31, 0.29, 0.22, 0.13, 0.0]),
    (236, [(0.38, 2.35), (0.66, 2.9), (0.92, 3.6), (1.0, 4.3), (0.86, 4.95)], [0.11, 0.2, 0.22, 0.17, 0.0]),
    (304, [(0.38, 2.35), (0.66, 2.9), (0.92, 3.6), (1.0, 4.3), (0.86, 4.95)], [0.11, 0.2, 0.22, 0.17, 0.0]),
    (118, [(0.40, 2.35), (0.8, 2.9), (1.12, 3.7), (1.2, 4.5), (1.0, 5.5)], [0.11, 0.2, 0.24, 0.18, 0.0]),
    (62, [(0.40, 2.35), (0.8, 2.9), (1.12, 3.7), (1.2, 4.5), (1.0, 5.5)], [0.11, 0.2, 0.24, 0.18, 0.0]),
]
for ang, path, widths in CLAWS:
    c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    pts = [(r * c, r * s, z) for r, z in path]
    claw = tk.tube(pts, widths, seg=5, sub=2, mat=claw_bronze, name="claw", flat=0.55, up=(c, s, 0.0))
    parts.append(tk.edged(claw, claw_edge, 0.02, angle=35))

# glossy black tentacles sprawling wide and low over the ring, tips curling
TENTACLES = [  # angle, reach, width
    (180, 1.24, 0.25), (0, 1.24, 0.25), (214, 1.10, 0.25), (326, 1.10, 0.25),
    (253, 0.84, 0.27), (287, 0.84, 0.27), (132, 1.02, 0.21), (48, 1.02, 0.21),
]
for ang, reach, w in TENTACLES:
    curl = 16 if ang in (180, 214, 253, 132) else -16
    pts = [tk.polar(0.40, ang, 1.08), tk.polar(0.66, ang, 0.86), tk.polar(0.84 * reach, ang, 0.56),
           tk.polar(reach, ang - curl * 0.3, 0.40), tk.polar(reach + 0.17, ang + curl, 0.50)]
    parts.append(tk.tube(pts, [w, w * 0.92, w * 0.72, w * 0.45, 0.0], seg=6, sub=2, mat=gloss,
                         name="tentacle", flat=0.72))

tris = Counter()
for p in parts:
    tris[p.name.split(".")[0]] += sum(len(f.vertices) - 2 for f in p.data.polygons)
print("PARTS", dict(tris))
ob = hk.finish(parts, GLB, {"replacesTexture": "tarmana_tarosmeetsdivine", "replacesPiece": "tarlode"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
tk.standard_view()
hk.renders(ob, OUT, NAME, PIC, HOT, scale=4)
