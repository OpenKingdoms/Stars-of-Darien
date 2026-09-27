"""TARLODE, the Taros lodestone: an iron ring gripped by bone claws, a spread
claw of black iron thorns, a horned skull and a tall blood-red crystal that
skewers the whole and bursts out low at the front."""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tarkit as tk  # noqa: E402
from tarkit import hk  # noqa: E402
from mathutils import Matrix  # noqa: E402

NAME = "TARLODE"
PIC = r"D:\OKReplace\lodes\sprites\TARLODE.png"
HOT = (31, 111)
GLB = r"D:\OKReplace\lodes\hand\models\TARLODE.glb"
OUT = r"D:\OKReplace\lodes\hand\renders"

hk.reset()
M = tk.materials()

# deep crimson body, a little brighter in the head, red-orange only at the point
blood = tk.gem_material("tar_blood_crystal",
                        [(0.0, (70, 6, 5)), (0.40, (100, 10, 8)), (0.70, (118, 14, 10)), (0.76, (150, 26, 14)),
                         (0.92, (180, 40, 20)), (1.0, (198, 62, 30))],
                        [(0.0, (10, 1, 1)), (0.5, (26, 2, 1)), (0.70, (34, 3, 2)), (0.78, (60, 8, 3)),
                         (1.0, (110, 28, 10))],
                        strength=1.0, rough=0.24, spec=0.5, tint=(1.0, 0.25, 0.18))
rim = tk.glow("tar_blood_rim", (0.52, 0.035, 0.012), (0.45, 0.03, 0.01), 0.45, rough=0.12)
gold = tk.glow("tar_blood_gold", (0.85, 0.55, 0.20), (0.90, 0.55, 0.16), 0.6, rough=0.2)
GEM = [blood, rim, gold]
socket = hk.pbr("tar_socket", (0.018, 0.012, 0.01), rough=0.9)

# the blood crystal, first so the joined mesh keeps its UV map; out of the skull
# up to 7.55 cells, uneven facets, a wider head stepping out under its point
cr = tk.gem((0, 0.06, 2.35), (0, 0, 1), [(0.0, 0.24), (2.45, 0.27), (2.85, 0.35), (3.62, 0.36)], 1.5, GEM,
            sides=6, spin=math.radians(-90), angles=[0.0, 0.18, -0.10, 0.07, -0.16, 0.10],
            jitter=[1.0, 0.86, 1.10, 0.93, 1.04, 0.84], skew=(-0.05, 0.02), gold_dir=(-1.0, -0.8, 0.3),
            gold_min=0.05, rim=0.016, name="crystal")
cr.name = NAME  # the joined object takes this name
parts = [cr]
# its lower end, bursting steeply out of the front of the hub to a point on the ground
low = tk.gem((0, -0.10, 0.98), (0, -0.55, -0.83), [(0.0, 0.17), (0.72, 0.19)], 0.45, GEM, sides=6,
             spin=math.radians(90), angles=[0.0, 0.12, -0.08, 0.1, -0.1, 0.06],
             jitter=[1.0, 0.85, 1.05, 0.9, 1.0, 0.92], rim=0.012, name="low_blade")
parts.append(low)

parts += tk.ring_base(M)

# the hub: a flattened faceted body on a short foot, under the claw
hub = tk.lathe([(0, 0.0), (0.32, 0.0), (0.34, 0.06), (0.24, 0.30), (0.26, 0.62), (0.42, 0.76), (0.47, 0.94),
                (0.40, 1.10), (0.22, 1.22), (0, 1.25)], seg=8, mat=M["iron"], name="hub")
tk.edged(hub, M["edge"], 0.025)
parts.append(hub)

# the claw: broad black thorns spread out nearly level, their points hooking down
# onto the ring; the front one is split so the crystal can burst through
STAR = [  # angle, reach, tip height, width
    (35, 1.12, 0.40, 0.27), (145, 1.12, 0.40, 0.27), (90, 0.96, 0.55, 0.25),
    (182, 1.08, 0.36, 0.28), (358, 1.08, 0.36, 0.28), (224, 0.98, 0.32, 0.27), (316, 0.98, 0.32, 0.27),
    (252, 0.72, 0.40, 0.17), (288, 0.72, 0.40, 0.17),
]
for a, reach, tz, w in STAR:
    pts = [tk.polar(0.16, a, 1.02), tk.polar(0.46, a, 1.08), tk.polar(0.74 * reach, a, 0.98),
           tk.polar(0.93 * reach, a, tz + 0.24), tk.polar(reach, a, tz)]
    th = tk.blade(pts, [w * 0.75, w, w * 0.8, w * 0.45, 0.0], thick=0.34, mat=M["iron"], name="thorn", sub=2)
    parts.append(tk.edged(th, M["edge"], 0.018))
parts += tk.bone_spikes(M, zc=0.95)

# a short neck with two vertebrae up to the skull
neck = tk.lathe([(0, 1.15), (0.22, 1.15), (0.17, 1.38), (0.25, 1.46), (0.25, 1.54), (0.16, 1.63), (0.14, 1.95),
                 (0, 1.97)], seg=8, mat=M["iron"], name="neck")
tk.edged(neck, M["edge"], 0.018)
parts.append(neck)

# the skull, built facing -Y, a little taller than wide, tipped up 30 degrees
B = M["skull"]
sk = tk.ellipsoid((0, 0.05, 0.10), (0.26, 0.30, 0.31), seg=14, rings=10, mat=B, name="cranium")
tk.boolean(sk, tk.ellipsoid((0, -0.12, -0.12), (0.20, 0.19, 0.22), seg=12, rings=8, mat=B), "UNION")
for side in (-1, 1):
    # cheekbones
    tk.boolean(sk, tk.ellipsoid((side * 0.17, -0.15, -0.09), (0.07, 0.10, 0.055), seg=8, rings=5, mat=B),
               "UNION")
for side in (-1, 1):
    # small deep sockets under a brow
    tk.boolean(sk, tk.ellipsoid((side * 0.095, -0.30, 0.0), (0.06, 0.12, 0.058), seg=10, rings=6, mat=socket))
tk.boolean(sk, tk.ellipsoid((0, -0.32, -0.14), (0.028, 0.10, 0.05), seg=8, rings=5, mat=socket))
skull = [sk]
for i in range(6):
    # the upper tooth row
    x = -0.1 + 0.04 * i
    yf = -0.12 - 0.2 * math.sqrt(max(0.05, 0.51 - (x / 0.22) ** 2))
    skull.append(hk.box(0.034, 0.05, 0.085, x=x, y=yf + 0.02, z=-0.34, mat=B, name="tooth"))
jaw = tk.tube([(-0.18, 0.05, -0.16), (-0.17, -0.08, -0.34), (-0.1, -0.18, -0.42), (0, -0.215, -0.44),
               (0.1, -0.18, -0.42), (0.17, -0.08, -0.34), (0.18, 0.05, -0.16)],
              [0.042, 0.055, 0.06, 0.064, 0.06, 0.055, 0.042], seg=6, sub=2, mat=B, name="jaw", flat=0.8,
              up=(0, 1, 0))
skull.append(jaw)
for i in range(4):
    x = -0.066 + 0.044 * i
    skull.append(hk.box(0.032, 0.045, 0.07, x=x, y=-0.195 + 0.25 * x * x, z=-0.41, mat=B, name="tooth"))
SKULL = (Matrix.Translation((0, 0.12, 2.36)) @ Matrix.Rotation(math.radians(-30), 4, "X")
         @ Matrix.Scale(1.28, 4))
for p in skull:
    tk.place(p, SKULL)
parts += skull

# the horns: thin crescents out and up from the crown, points curling back in
for side in (-1, 1):
    pts = [(side * 0.21, 0.26, 2.72), (side * 0.47, 0.20, 3.05), (side * 0.67, 0.10, 3.55),
           (side * 0.76, 0.04, 4.10), (side * 0.70, 0.0, 4.50), (side * 0.56, -0.03, 4.80)]
    parts.append(tk.tube(pts, [0.095, 0.085, 0.066, 0.045, 0.022, 0.0], seg=7, sub=3, mat=M["bone"],
                         name="horn"))

from collections import Counter  # noqa: E402
_tris = Counter()
for p in parts:
    _tris[p.name.split(".")[0]] += sum(len(f.vertices) - 2 for f in p.data.polygons)
print("PARTS", dict(_tris))
ob = hk.finish(parts, GLB, {"replacesTexture": "Tarlode_tarosianstoneofevilfun", "replacesPiece": "tarlode"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
tk.standard_view()
hk.renders(ob, OUT, NAME, PIC, HOT, scale=4)
