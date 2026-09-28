"""TARLODE, the Taros lodestone: an iron ring gripped by bone claws, a spread
claw of black iron thorns, a horned skull and a tall blood-red crystal that
skewers the whole and bursts out low at the front. The sturdy version: the
approved build with a thicker, shorter crystal leaning a touch back, a broad
hub, a lower and larger skull on a heavy spine, and a heavier ring."""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
for p in (os.path.dirname(os.path.dirname(HERE)), HERE):
    if p not in sys.path:
        sys.path.insert(0, p)
import tarkit as tk  # noqa: E402
from tarkit import hk  # noqa: E402
from mathutils import Matrix  # noqa: E402

NAME = "TARLODE"
PIC = r"D:\OKReplace\lodes\sprites\TARLODE.png"
HOT = (31, 111)
GLB = r"D:\OKReplace\lodes\sturdy\models\TARLODE.glb"
OUT = r"D:\OKReplace\lodes\sturdy\renders"

# the approved model's upper parts made broader (G across, GD front to back)
# and lower (H), the crystal leaning back by LEAN so its head stays near
# where the picture draws it
G, GD, H = 1.2, 1.3, 0.82
LEAN = math.radians(2.5)
BASE_Z = 0.95  # the crystal's foot, inside the hub
AX = (0.0, math.sin(LEAN), math.cos(LEAN))


def up(x, y, z):
    """An approved point above the hub moved onto the lower, leaning spine."""
    z2 = BASE_Z + (z - 1.1) * H
    return (x, y + (z2 - BASE_Z) * math.tan(LEAN), z2)


hk.reset()
M = tk.materials()
tk.soften(M["iron"], 0.3)
# the claw is matte dark grey iron, not polished black
claw = tk.soften(hk.pbr("tar_claw_iron", tk.lin((36, 36, 35)), rough=0.72, metal=0.15), 0.15)
claw_edge = tk.soften(hk.pbr("tar_claw_edge", tk.lin((158, 158, 155)), rough=0.6, metal=0.15), 0.2)

# the blood crystal's faces, each keeping its painted colour: a bright gem head
# (pale gold upper left, salmon front, deep red right) on a dark maroon shaft
# lit orange-red down its left edge
G_ = [
    tk.glass("tar_head_gold", (235, 205, 120), glow=1.0),
    tk.glass("tar_head_pale", (250, 232, 150), glow=1.0),
    tk.glass("tar_head_salmon", (197, 88, 67), glow=1.0),
    tk.glass("tar_head_red", (170, 42, 26), glow=1.0),
    tk.glass("tar_head_deep", (95, 5, 5), glow=1.0),
    tk.glass("tar_shaft_lit", (165, 45, 25), glow=0.9),
    tk.glass("tar_shaft_streak", (125, 70, 40), glow=0.9),
    tk.glass("tar_shaft_body", (72, 17, 6), glow=0.9),
    tk.glass("tar_shaft_dark", (58, 3, 3), glow=0.9),
    tk.glass("tar_shaft_crimson", (112, 14, 8), glow=0.9),
    tk.glass("tar_head_orange", (196, 84, 62), glow=1.0),
    tk.glass("tar_head_step", (140, 88, 64), glow=0.95),
]
GOLD, PALE, SALMON, RED, DEEP, LIT, STREAK, BODY, DARK, CRIMSON, ORANGE, STEP = range(12)
# vertex angles: faces run back right, back, back left, front left, a narrow
# left-centre strip, front left of centre, front right of centre, front right
ANG = [0, 62, 118, 180, 232, 246, 270, 292]


def crystal_face(band, i):
    # gold only on the upper left point facet and a thin strip down the head
    if band == "tip":
        return [DEEP, DEEP, ORANGE, GOLD, PALE, SALMON, RED, DEEP][i]
    if band == "head":
        return [RED, DEEP, DEEP, ORANGE, GOLD, SALMON, STEP, DEEP][i]
    if band == "step":
        return [DEEP, DEEP, DEEP, RED, STEP, STEP, STEP, DEEP][i]
    if band == "shaft":
        return [CRIMSON, DARK, DARK, LIT, STREAK, BODY, BODY, DARK][i]
    return DARK


# the crystal, first so the joined object takes its name: a dark shaft from
# inside the hub up through the spine and skull, a short downward chamfer out
# to a wider gem head, and a short point whose apex sits toward the back
SECTIONS = [(0.0, 0.17, 0.14, "cap"), (1.5, 0.2, 0.16, "shaft"), (4.87, 0.29, 0.23, "shaft"),
            (4.97, 0.37, 0.24, "step"), (5.5, 0.365, 0.24, "head"), (5.85, 0.22, 0.15, "tip")]
# the shaft thickened more than the head, so no thin stalk shows under it
WIDE = [(1.45, 1.5), (1.4, 1.45), (1.25, 1.3), (1.2, 1.3), (1.2, 1.3), (1.2, 1.3)]
cr = tk.facet_crystal([(h * H, rx * wx, ry * wy, b) for (h, rx, ry, b), (wx, wy) in zip(SECTIONS, WIDE)], ANG,
                      (-0.02 * G, 0.09 * GD, 6.18 * H), G_, crystal_face, base=(0, 0.06, BASE_Z), axis=AX,
                      name="crystal")
cr.name = NAME  # the joined object takes this name
parts = [cr]


# its lower end, the same shaft running on out of the hub centre and down
# the front to a point in the ground; angles mirrored so its lit faces look up
def low_face(band, i):
    return [DARK, DARK, DARK, LIT, STREAK, BODY, BODY, DARK][i] if band == "tip" else crystal_face(band, i)


low = tk.facet_crystal([(0.0, 0.26 * G, 0.21 * GD, "cap"), (0.42, 0.24 * G, 0.19 * GD, "shaft")],
                       [-a for a in ANG], (0.0, 0.0, 1.1), G_, low_face, base=(0, -0.02, 0.92),
                       axis=(0, -0.551, -0.834), name="low_blade")
parts.append(low)

parts += tk.ring_base(M)

# the hub: a broad flattened body on a wide foot, under the claw, matte dark iron
hub = tk.lathe([(0, 0.0), (0.46, 0.0), (0.48, 0.07), (0.40, 0.26), (0.38, 0.52), (0.50, 0.66), (0.55, 0.83),
                (0.47, 0.98), (0.27, 1.08), (0, 1.11)], seg=10, mat=claw, name="hub")
hk.smooth(hub, 50)
parts.append(hub)

# the claw: an open star of six iron thorns off the hub, an X of two long
# diagonals with a pair out to the sides; the back ones hook onto the ring's
# rear, the side ones onto its inner lip, the front ones into the ground
ARMS = [  # path, widths
    ([(-0.18, 0.0, 0.9), (-0.46, 0.28, 0.86), (-0.68, 0.5, 0.7), (-0.85, 0.74, 0.54), (-0.92, 0.86, 0.46)],
     [0.21, 0.25, 0.2, 0.11, 0.0]),
    ([(-0.18, -0.02, 0.9), (-0.46, -0.06, 0.76), (-0.68, -0.1, 0.54), (-0.88, -0.12, 0.44), (-1.02, -0.12, 0.40)],
     [0.21, 0.25, 0.2, 0.1, 0.0]),
    ([(-0.13, -0.14, 0.9), (-0.3, -0.28, 0.78), (-0.44, -0.38, 0.54), (-0.54, -0.43, 0.28), (-0.6, -0.45, 0.04)],
     [0.19, 0.21, 0.17, 0.09, 0.0]),
]
for pts, widths in ARMS:
    for side in (-1, 1):
        p = [(-side * x, y, z) for x, y, z in pts]
        if side > 0 and pts is ARMS[0][0]:
            p = [(x * 1.02, y * 1.05, z) for x, y, z in p]  # the right one reaches a touch further back
        th = tk.tube(p, widths, seg=4, sub=3, mat=claw, name="thorn", flat=0.5)
        th.data.materials.append(claw_edge)
        tk.ridge(th, 0, 4, 0.028, 1)  # a worn, lighter edge along the top
        parts.append(tk.flat(th))
# two pale verdigris studs on the hub's crown
stud = hk.pbr("tar_stud", tk.lin((150, 180, 158)), rough=0.35, metal=0.6)
for x, y in ((-0.34, -0.24), (0.40, -0.26)):
    parts.append(tk.ellipsoid((x, y, 0.97), (0.07, 0.07, 0.052), seg=8, rings=4, mat=stud, name="stud"))
# two bone thorns out of the spine, running back and out, then turning up
for side in (-1, 1):
    pts = [(side * 0.08, 0.1, 1.44), (side * 0.34, 0.28, 1.42), (side * 0.62, 0.36, 1.5), (side * 0.86, 0.42, 1.68),
           (side * 0.96, 0.48, 1.94), (side * 0.93, 0.50, 2.2)]
    pts = [up(x * 1.15, y * 1.1, z) for x, y, z in pts]
    parts.append(tk.blade(pts, [0.1, 0.094, 0.078, 0.06, 0.038, 0.0], thick=0.8, mat=M["bone"], name="spike", sub=3))

# heavy iron vertebrae threaded on the crystal between the claw and the skull
for z, r in ((1.34, 0.38), (1.63, 0.35), (1.92, 0.34), (2.21, 0.34)):
    x0, y0, z0 = up(0.0, 0.06, z)
    v = tk.lathe([(0.24, z0 - 0.125), (r, z0 - 0.075), (r + 0.03, z0), (r, z0 + 0.075), (0.24, z0 + 0.125)], seg=10,
                 mat=claw, name="vertebra", closed=True)
    v.location.y = y0 - 0.06
    parts.append(hk.smooth(v, 50))

# the skull, built facing -Y, a little taller than wide, tipped up 30 degrees
B = M["skull"]
socket = hk.pbr("tar_socket", (0.018, 0.012, 0.01), rough=0.9)
sk = tk.ellipsoid((0, 0.05, 0.10), (0.26, 0.30, 0.31), seg=16, rings=12, mat=B, name="cranium")
tk.boolean(sk, tk.ellipsoid((0, -0.12, -0.12), (0.20, 0.19, 0.22), seg=14, rings=9, mat=B), "UNION")
for side in (-1, 1):
    # cheekbones
    tk.boolean(sk, tk.ellipsoid((side * 0.17, -0.15, -0.09), (0.07, 0.10, 0.055), seg=8, rings=5, mat=B),
               "UNION")
for side in (-1, 1):
    # large angular sockets under a brow that dips toward the nose
    eye = [(0.165, 0.035), (0.04, 0.0), (0.03, -0.055), (0.085, -0.09), (0.16, -0.06)]
    tk.boolean(sk, tk.prism_cutter([(side * x, z) for x, z in (eye if side > 0 else eye[::-1])], -0.7, -0.17,
                                   mat=socket))
# an inverted-heart nasal cavity: a point at the top, two lobes below
nose = [(0.0, 0.045), (0.03, 0.0), (0.043, -0.035), (0.028, -0.052), (0.008, -0.042), (0.0, -0.03),
        (-0.008, -0.042), (-0.028, -0.052), (-0.043, -0.035), (-0.03, 0.0)]
tk.boolean(sk, tk.prism_cutter([(x, z - 0.15) for x, z in reversed(nose)], -0.7, -0.22, mat=socket))
skull = [sk]
for i in range(6):
    # the upper tooth row
    x = -0.1 + 0.04 * i
    yf = -0.12 - 0.2 * math.sqrt(max(0.05, 0.51 - (x / 0.22) ** 2))
    skull.append(hk.box(0.034, 0.05, 0.06, x=x, y=yf + 0.02, z=-0.3, mat=B, name="tooth"))
# a short jaw tucked up under the teeth, so the chin stays compact
jaw = tk.tube([(-0.18, 0.05, -0.16), (-0.17, -0.08, -0.29), (-0.1, -0.18, -0.34), (0, -0.215, -0.355),
               (0.1, -0.18, -0.34), (0.17, -0.08, -0.29), (0.18, 0.05, -0.16)],
              [0.04, 0.048, 0.05, 0.052, 0.05, 0.048, 0.04], seg=6, sub=2, mat=B, name="jaw", flat=0.7,
              up=(0, 1, 0))
skull.append(jaw)
for i in range(4):
    x = -0.066 + 0.044 * i
    skull.append(hk.box(0.032, 0.045, 0.045, x=x, y=-0.195 + 0.25 * x * x, z=-0.335, mat=B, name="tooth"))
SK_C = (0.0, 0.10, 2.75)  # the approved skull's centre
SK_S = 1.15  # and how much bigger this one is
SK = up(*SK_C)
SKULL = (Matrix.Translation(SK) @ Matrix.Rotation(math.radians(-30), 4, "X") @ Matrix.Scale(1.45 * SK_S, 4))
for p in skull:
    tk.place(p, SKULL)
parts += skull


def on_skull(x, y, z, gx=1.12):
    """An approved point near the skull, kept in place on the bigger, lower one."""
    return (x * gx, SK[1] + (y - SK_C[1]) * SK_S, SK[2] + (z - SK_C[2]) * SK_S)


horn = hk.pbr("tar_horn", (0.50, 0.40, 0.24), rough=0.45)
# the horns: bull horns off the crown, out to the sides then sweeping back
# and a little up, the points curling in; their rise on screen is mostly depth
for side in (-1, 1):
    pts = [(side * 0.21, 0.26, 2.72), (side * 0.52, 0.30, 2.84), (side * 0.80, 0.42, 2.95),
           (side * 0.85, 0.62, 3.02), (side * 0.77, 0.80, 3.06), (side * 0.62, 0.93, 3.06)]
    parts.append(tk.tube([on_skull(*q) for q in pts], [0.145, 0.135, 0.114, 0.086, 0.05, 0.0], seg=8, sub=3,
                         mat=horn, name="horn"))

from collections import Counter  # noqa: E402
_tris = Counter()
for p in parts:
    _tris[p.name.split(".")[0]] += sum(len(f.vertices) - 2 for f in p.data.polygons)
print("PARTS", dict(_tris))
ob = hk.finish(parts, GLB, {"replacesTexture": "Tarlode_tarosianstoneofevilfun", "replacesPiece": "tarlode"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
tk.standard_view()
hk.renders(ob, OUT, NAME, PIC, HOT, scale=4)
