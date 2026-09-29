"""TARMANA, the Taros mana lodestone: a compact glossy black creature on the
Taros ring, six slim limbs splayed round it, gripping a near-black cup whose
horn claws flare out like wings round a packed cluster of blood-red crystals
burning over a molten core. Everything about it is barbed: a crown of iron
teeth on the cup, hooks on the claws, iron thorns and a skull on the ring."""
import math
import os
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tarkit as tk  # noqa: E402
from tarkit import hk  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

NAME = "TARMANA"
PIC = r"D:\OKReplace\lodes\sprites\TARMANA.png"
HOT = (31, 111)
GLB = r"D:\OKReplace\lodes\hand\models\TARMANA.glb"
OUT = r"D:\OKReplace\lodes\hand\renders"

hk.reset()
M = tk.materials()
# blacker iron than the Taros kit's defaults
M["ring"].node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*tk.lin((58, 50, 46)), 1.0)
M["iron"] = hk.pbr("tar_mana_iron", (0.014, 0.013, 0.013), rough=0.28, metal=0.6)
iron_edge = hk.pbr("tar_mana_iron_edge", tk.lin((96, 88, 84)), rough=0.3, metal=0.8)

# blood crystal: each facet its own ramp, ember hot deep in the cup, blood red
# through the middle and darkening to near black crimson at the points
EMBER, BLOODHOT, BLOOD, CRIMSON = (255, 150, 40), (236, 58, 18), (196, 18, 12), (120, 6, 8)
TIP, BLACKTIP = (62, 3, 6), (30, 2, 4)
FACE = {
    "hot": [(0.0, (255, 184, 70)), (0.16, EMBER), (0.36, BLOODHOT), (0.56, BLOOD), (0.8, CRIMSON), (1.0, TIP)],
    "orange": [(0.0, (255, 150, 44)), (0.22, (246, 96, 24)), (0.45, (214, 32, 14)), (0.75, (130, 8, 8)),
               (1.0, BLACKTIP)],
    "pink": [(0.0, (250, 110, 40)), (0.3, (220, 34, 28)), (0.62, (160, 10, 14)), (1.0, TIP)],
    "red": [(0.0, (252, 124, 30)), (0.26, (216, 40, 14)), (0.52, (170, 14, 10)), (0.82, (96, 4, 6)),
            (1.0, BLACKTIP)],
    "brown": [(0.0, (214, 76, 22)), (0.3, (132, 18, 10)), (1.0, BLACKTIP)],
    "deep": [(0.0, (232, 80, 22)), (0.32, (128, 8, 6)), (1.0, (24, 1, 3))],
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
            blots.append((f, rnd.uniform(0.2, 0.55), rnd.uniform(0.06, 0.12),
                          rnd.choice([EMBER, BLOODHOT, BLOODHOT, (255, 190, 70)]), rnd.uniform(0.4, 0.75)))
    return tk.fire_faces("tar_fire_%d" % k, faces, strength=1.0, seed=k, blots=blots)


FIRES = [fire(k) for k in range(4)]
edge = tk.glass("tar_fire_edge", (70, 6, 5), glow=0.7)
hot_facet = tk.glass("tar_hot_facet", (255, 84, 36), glow=1.3, rough=0.2)
dark_facet = tk.glass("tar_dark_facet", (92, 6, 8), glow=0.8, rough=0.15)
core = tk.glow("tar_molten_core", (1.0, 0.24, 0.04), (1.0, 0.2, 0.03), 1.8, rough=0.4)
ember = tk.fire_faces("tar_ember", [[(0.0, (255, 170, 60)), (0.5, (226, 50, 14)), (1.0, (90, 4, 4))],
                                    [(0.0, (250, 120, 40)), (0.5, (190, 16, 12)), (1.0, (50, 2, 4))]] * 3,
                      strength=0.95, seed=9)

# the cup is black iron, the claws black horn with dull ridges, the rim a
# thin dark steel band
cup_black = hk.pbr("tar_cup_black", (0.014, 0.013, 0.012), rough=0.26, metal=0.75)
cup_band = hk.pbr("tar_cup_band", (0.04, 0.036, 0.033), rough=0.28, metal=0.85)
claw_horn = hk.pbr("tar_claw_horn", (0.03, 0.028, 0.02), rough=0.34, metal=0.4)
membrane = hk.pbr("tar_wing_membrane", (0.05, 0.006, 0.006), rough=0.5, metal=0.1,
                  emit=(0.12, 0.004, 0.002), strength=0.25)
claw_ridge = hk.pbr("tar_claw_ridge", tk.lin((112, 106, 84)), rough=0.35, metal=0.3)
rim_steel = hk.pbr("tar_rim_steel", (0.09, 0.085, 0.08), rough=0.3, metal=0.7)
# jet black gloss for the creature under a clear coat, sharp white glints,
# pale grey streaks along the limb tops, dark red under the limbs
gloss = hk.pbr("tar_gloss_black", (0.01, 0.0095, 0.0095), rough=0.12, metal=0.1)
_b = gloss.node_tree.nodes["Principled BSDF"]
_b.inputs["Specular IOR Level"].default_value = 0.3
_b.inputs["Coat Weight"].default_value = 0.5
_b.inputs["Coat Roughness"].default_value = 0.06
sheen = hk.pbr("tar_gloss_streak", tk.lin((156, 156, 164)), rough=0.15, metal=0.2)
eye = tk.glow("tar_eye", tk.lin((200, 16, 8)), tk.lin((255, 30, 10)), 2.2, rough=0.1)
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
SHARP = 1.5  # the points run longer than a clean crystal's, to a needle
# the burst, first so the joined mesh keeps its UV map: chunky crystals
# packed tight, tips where the picture shows them, the rear ones leaning
# well back (+Y) rather than rising high
parts = [crystal((-0.06, 0.26, 3.95), seen(22.5, 29.5, 2.1), 0.22, 0.25, 0.36 * SHARP, fire=0, spin=0.3, jitter=J6,
                 angles=A6, rim=0.012, deep=1.5, name="crystal")]
parts[0].name = NAME  # the joined object takes this name
# the chunky centre one, leaning back just so the front facet of its point
# faces the camera, burning hot under a dark point
parts.append(crystal((0.0, -0.14, 3.95), (-0.16, 1.376, 6.375), 0.31, 0.34, 0.45 * SHARP, fire=1, top=hot_facet,
                     spin=math.radians(-60), jitter=[1.0, 0.92, 1.05, 0.95, 1.0, 0.9],
                     gold_dir=(0.0, -0.447, 0.894), gold_min=0.5, gold_count=1, rim=0.014, deep=1.3,
                     name="crystal"))
BURST = [  # root, point, half width at root, under the point, point length, sides, broken slant, fire, spin
    ((0.16, 0.14, 4.0), seen(40.5, 34, 1.8), 0.2, 0.23, 0.32, 6, None, 2, 0.9),   # right, dark topped
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
        kw = dict(top=dark_facet, gold_dir=(-0.3, -0.5, 0.8), gold_min=0.55)
    parts.append(crystal(b, a, r0, r1, tip if brk else tip * SHARP, sides=sides, fire=fire, spin=spin, broken=brk,
                         jitter=J6[:sides], rim=0.014, name="crystal", **kw))
# a few short, stubby embers round the core, leaning out over the rim
BED = [((-0.40, -0.20, 4.1), (-0.92, -0.55, 5.0), 0.15, 0.18, 0.26, None),
       ((0.40, -0.18, 4.1), (0.90, -0.46, 4.9), 0.14, 0.17, 0.2, None)]
for b, a, r0, r1, tip, brk in BED:
    axis = [a[k] - b[k] for k in range(3)]
    length = math.sqrt(sum(c * c for c in axis))
    parts.append(tk.gem(b, axis, [(0.0, r0), (length - tip, r1)], tip * SHARP, [ember, edge], sides=5, broken=brk,
                        jitter=J6[:5], rim=0.01, name="crystal"))

# the molten core the crystals grow from, sitting back in the cup's mouth so
# a dark band of inner wall shows under it
parts.append(tk.ellipsoid((0, 0.08, 4.02), (0.52, 0.50, 0.18), seg=14, rings=6, mat=core, name="core"))

parts += tk.ring_base(M, seg=28)
parts += tk.bone_spikes(M, zc=1.2, widths=(0.07, 0.056, 0.04, 0.022, 0.0))


def iron_thorn(pts, widths, up=(0, 0, 1), mat=None, name="thorn"):
    """A black iron thorn, rhombus in section, a worn edge along its top."""
    th = tk.tube(pts, widths, seg=4, sub=2, mat=mat or M["iron"], name=name, flat=0.55, up=up)
    th.data.materials.append(iron_edge)
    tk.ridge(th, 0, 4, 0.012, 1)
    return tk.flat(th)


def ring_at(r, deg, z):
    x, y, _ = tk.polar(r, deg, 0.0)
    return (x, y * tk.RING_DEEP + tk.RING_Y, z)


# iron thorns round the back and sides of the ring, leaning out, the tall
# ones hooking outward at the tip
for deg, h in [(-22.5, 0.42), (0, 0.24), (22.5, 0.5), (45, 0.26), (67.5, 0.44), (90, 0.28), (112.5, 0.44),
               (135, 0.26), (157.5, 0.5), (180, 0.24), (202.5, 0.42)]:
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    r = 1.2
    pts = [ring_at(r, deg, tk.RING_TOP - 0.02), ring_at(r + 0.05, deg, tk.RING_TOP + h * 0.55),
           ring_at(r + 0.16, deg, tk.RING_TOP + h)]
    parts.append(iron_thorn(pts, [0.075, 0.045, 0.0], up=(c, s, 0.0), name="ring_thorn"))
# barbs off the ring's outer face, pointing out and down
for deg in range(-10, 200, 30):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    parts.append(iron_thorn([ring_at(1.31, deg, 0.14), ring_at(1.45, deg, 0.12), ring_at(1.55, deg, 0.04)],
                            [0.05, 0.03, 0.0], up=(0, 0, 1), name="ring_barb"))


def skull(place, mat, socket, name="skull"):
    """A small skull built facing -Y, its crown at about z 0.4, moved by place."""
    sk = tk.ellipsoid((0, 0.05, 0.10), (0.26, 0.30, 0.31), seg=12, rings=9, mat=mat, name=name)
    tk.boolean(sk, tk.ellipsoid((0, -0.12, -0.12), (0.20, 0.19, 0.22), seg=10, rings=7, mat=mat), "UNION")
    for side in (-1, 1):
        tk.boolean(sk, tk.ellipsoid((side * 0.17, -0.15, -0.09), (0.07, 0.10, 0.055), seg=8, rings=5, mat=mat),
                   "UNION")
    for side in (-1, 1):
        # slanted sockets under a heavy brow, angry rather than round
        eye_pts = [(0.17, 0.05), (0.03, -0.01), (0.03, -0.06), (0.09, -0.1), (0.17, -0.05)]
        tk.boolean(sk, tk.prism_cutter([(side * x, z) for x, z in (eye_pts if side > 0 else eye_pts[::-1])],
                                       -0.7, -0.17, mat=socket))
    nose = [(0.0, 0.045), (0.035, -0.03), (0.02, -0.05), (0.0, -0.035), (-0.02, -0.05), (-0.035, -0.03)]
    tk.boolean(sk, tk.prism_cutter([(x, z - 0.15) for x, z in reversed(nose)], -0.7, -0.22, mat=socket))
    out = [sk]
    for i in range(5):
        x = -0.08 + 0.04 * i
        yf = -0.12 - 0.2 * math.sqrt(max(0.05, 0.51 - (x / 0.22) ** 2))
        out.append(hk.box(0.032, 0.05, 0.07, x=x, y=yf + 0.02, z=-0.31, mat=mat, name="tooth"))
    jaw = tk.tube([(-0.17, 0.05, -0.17), (-0.16, -0.08, -0.3), (-0.09, -0.18, -0.35), (0, -0.21, -0.365),
                   (0.09, -0.18, -0.35), (0.16, -0.08, -0.3), (0.17, 0.05, -0.17)],
                  [0.04, 0.046, 0.048, 0.05, 0.048, 0.046, 0.04], seg=6, sub=2, mat=mat, name="jaw", flat=0.7,
                  up=(0, 1, 0))
    out.append(jaw)
    for p in out:
        tk.place(p, place)
    return out


socket = hk.pbr("tar_socket", (0.012, 0.004, 0.004), rough=0.9)
old_bone = hk.pbr("tar_old_bone", tk.lin((196, 176, 132)), rough=0.5)
# a skull fixed to the front of the bone arc, glaring up at the camera
SKULL = (Matrix.Translation(ring_at(1.2, -90, 0.52)) @ Matrix.Rotation(math.radians(-24), 4, "X")
         @ Matrix.Scale(0.62, 4))
parts += skull(SKULL, old_bone, socket)

# more bone thorns: a pair low on the body raking forward and out, and a
# row of spines down its back
for side in (-1, 1):
    pts = [(side * 0.34, -0.05, 0.95), (side * 0.62, -0.26, 1.02), (side * 0.84, -0.44, 1.2),
           (side * 0.92, -0.56, 1.46), (side * 0.9, -0.6, 1.66)]
    parts.append(tk.blade(pts, [0.065, 0.052, 0.038, 0.02, 0.0], thick=0.8, mat=M["bone"], name="spike", sub=3))
for z, lean, ln in ((0.95, 0.5, 0.42), (1.35, 0.55, 0.5), (1.75, 0.6, 0.46), (2.12, 0.65, 0.36)):
    pts = [(0.0, 0.42, z), (0.0, 0.42 + ln * 0.6, z + ln * lean * 0.8), (0.0, 0.42 + ln * 1.05, z + ln * lean * 1.5)]
    parts.append(tk.blade(pts, [0.06, 0.035, 0.0], thick=0.5, mat=M["bone"], name="spine", sub=2, up=(1, 0, 0)))

# the creature: a compact glossy black body under the cup, two burning eyes
# on its front, fingers gripping the cup's foot
body = tk.lathe([(0, 0.0), (0.2, 0.0), (0.3, 0.25), (0.4, 0.6), (0.5, 1.0), (0.57, 1.45), (0.58, 1.85),
                 (0.55, 2.2), (0.5, 2.5), (0, 2.6)], seg=18, mat=gloss, name="body")
hk.smooth(body, 60)
parts.append(body)
for side in (-1, 1):
    parts.append(tk.ellipsoid((side * 0.21, -0.51, 1.86), (0.1, 0.08, 0.075), seg=10, rings=6, mat=eye,
                              name="knob", rot=Matrix.Rotation(math.radians(side * -18), 4, "Y")))
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
    hk.smooth(ob, 60)
    out = [ob]
    for i in (1, 2):
        # black thorns along the limb's top, raking toward its tip
        T = (Vector(pts[i + 1]) - Vector(pts[i - 1])).normalized()
        U = Vector((0, 0, 1))
        U = (U - T * U.dot(T)).normalized()
        base = Vector(pts[i]) + U * radii[i] * 0.7
        th = iron_thorn([tuple(base), tuple(base + U * 0.1 + T * 0.04), tuple(base + U * 0.17 + T * 0.13)],
                        [0.05, 0.03, 0.0], up=tuple(T), mat=gloss, name="limb_thorn")
        out.append(th)
    return out


# six slim limbs: a pair splaying up and out to the ring's back, a pair out
# over its sides, and two front legs in a V down to the ground
LIMB = [0.18, 0.18, 0.16, 0.11, 0.0]
for side in (-1, 1):
    back = 0.6 if side < 0 else 0.42  # the left one reaches further back
    parts += tentacle([(side * 0.35, 0.2, 1.72), (side * 0.62, back * 0.66, 1.38),
                       (side * 0.88, back * 0.92, 0.95), (side * 1.04, back, 0.6), (side * 1.12, back, 0.44)], LIMB)
    parts += tentacle([(side * 0.4, -0.12, 1.3), (side * 0.66, -0.18, 0.9), (side * 0.9, -0.2, 0.62),
                       (side * 1.12, -0.2, 0.44), (side * 1.3, -0.24, 0.4)], [0.19, 0.19, 0.18, 0.13, 0.0])
    parts += tentacle([(side * 0.1, -0.4, 1.35), (side * 0.16, -0.58, 1.05), (side * 0.26, -0.72, 0.72),
                       (side * 0.38, -0.8, 0.42), (side * 0.45, -0.82, 0.2), (side * 0.47, -0.83, 0.1)],
                      [0.18, 0.19, 0.19, 0.17, 0.11, 0.0])

# the cup: a wide black iron bowl straight on the body, open at the top, a
# thin dark rim round its mouth
cup = tk.lathe([(0, 2.4), (0.52, 2.4), (0.64, 2.8), (0.78, 3.4), (0.88, 4.05), (0.90, 4.40), (0.83, 4.40),
                (0.78, 4.18), (0.58, 3.92), (0, 3.9)], seg=12, mat=cup_black, name="cup", phase=math.radians(15))
tk.bevel_mat(cup, 0.02, 1, angle=25)
hk.smooth(cup, 20)
parts.append(cup)
parts.append(tk.torus(0.87, 0.05, 4.42, seg=24, rseg=5, mat=rim_steel, name="rim"))
parts.append(tk.torus(0.80, 0.045, 3.62, seg=24, rseg=5, mat=cup_band, name="band"))
parts.append(tk.torus(0.63, 0.04, 2.86, seg=24, rseg=5, mat=cup_band, name="band"))

# a crown of uneven iron teeth round the cup's mouth, leaning out
TEETH = [0.42, 0.18, 0.32, 0.13, 0.5, 0.2, 0.36, 0.15, 0.44, 0.16, 0.34, 0.13, 0.52, 0.19, 0.3, 0.14]
for k, h in enumerate(TEETH):
    deg = 360.0 / len(TEETH) * k + (7 if k % 2 else 0)
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    r = 0.87
    pts = [(r * c, r * s, 4.36), ((r + 0.05) * c, (r + 0.05) * s, 4.42 + h * 0.5),
           ((r + 0.13) * c, (r + 0.13) * s, 4.42 + h)]
    parts.append(iron_thorn(pts, [0.085, 0.05, 0.0], up=(c, s, 0.0), mat=cup_black, name="tooth"))
# hooked barbs off the lower band, pointing down
for deg in (-150, -120, -90, -60, -30, 70, 110):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    parts.append(iron_thorn([(0.64 * c, 0.64 * s, 2.9), (0.82 * c, 0.82 * s, 2.86), (0.9 * c, 0.9 * s, 2.7)],
                            [0.05, 0.03, 0.0], up=(0, 0, 1), mat=cup_black, name="barb"))

# a collar of short thorns just under the rim, out through the claws
for k in range(12):
    deg = 30.0 * k + 15
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    parts.append(iron_thorn([(0.86 * c, 0.86 * s, 4.1), (1.0 * c, 1.0 * s, 4.14), (1.12 * c, 1.12 * s, 4.22)],
                            [0.05, 0.03, 0.0], up=(0, 0, 1), mat=cup_black, name="collar"))


def cup_r(z):
    """The cup's outer radius at height z."""
    prof = [(2.4, 0.52), (2.8, 0.64), (3.4, 0.78), (4.05, 0.88), (4.4, 0.9)]
    for (z0, r0), (z1, r1) in zip(prof, prof[1:]):
        if z <= z1:
            return r0 + (r1 - r0) * max(0.0, z - z0) / (z1 - z0)
    return prof[-1][1]


steel = hk.pbr("tar_chain", (0.3, 0.28, 0.26), rough=0.28, metal=0.9)


def chain(a0, a1, z, sag, n, name="chain"):
    """A chain swag hung on the upper band between two angles, the links
    turning a quarter each and kept clear of the cup wall."""
    ends = [Vector(tk.polar(0.86, a, z)) for a in (a0, a1)]

    def at(t):
        p = ends[0].lerp(ends[1], t)
        p.z -= 4 * sag * t * (1 - t)
        rr = math.hypot(p.x, p.y)
        want = max(rr, cup_r(p.z) + 0.07)
        p.x, p.y = p.x * want / rr, p.y * want / rr
        return p
    out = []
    for i in range(n):
        t = (i + 0.5) / n
        p = at(t)
        T = (at(min(1.0, t + 0.02)) - at(max(0.0, t - 0.02))).normalized()
        N = Vector((p.x, p.y, 0.0)).normalized()
        N = (N - T * N.dot(T)).normalized()
        Z = N if i % 2 else T.cross(N)
        Y = Z.cross(T)
        R = Matrix(((T.x, Y.x, Z.x, p.x), (T.y, Y.y, Z.y, p.y), (T.z, Y.z, Z.z, p.z), (0, 0, 0, 1)))
        link = tk.torus(0.052, 0.016, 0.0, seg=8, rseg=4, mat=steel, name=name)
        link.matrix_world = R @ Matrix.Diagonal((1.45, 1.0, 1.0, 1.0))
        out.append(link)
    return out


# two chain swags across the front of the cup, hung from studs on the band
for a0, a1 in ((-145, -90), (-90, -35)):
    parts += chain(a0, a1, 3.6, 0.36, 8)
for a in (-145, -90, -35):
    x, y, z = tk.polar(0.84, a, 3.62)
    parts.append(tk.ellipsoid((x, y, z), (0.05, 0.05, 0.05), seg=8, rings=4, mat=cup_band, name="stud"))

# black horn wings off the cup's foot: on each side two horn ribs flare out
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
    ob = tk.tube(pts, RIB, seg=6, sub=3, mat=claw_horn, name="claw", flat=0.7, up=(c, s, 0.0), shade=50)
    ob.data.materials.append(claw_ridge)
    # a dull ridge along the outer and inner edges only
    tk.ridge(ob, 0, 6, 0.02, 1)
    tk.ridge(ob, 3, 6, 0.016, 1)
    hk.smooth(ob, 50)
    out = [ob]
    # hooked barbs down the outer edge, raking back down toward the ground
    for i, ln in ((2, 0.26), (3, 0.32), (4, 0.26)):
        r, z = path[i]
        out.append(iron_thorn([((r + 0.06) * c, (r + 0.06) * s, z), ((r + 0.2) * c, (r + 0.2) * s, z + 0.02),
                               ((r + ln) * c, (r + ln) * s, z - 0.14)], [0.055, 0.032, 0.0], up=(0, 0, 1),
                              mat=claw_horn, name="hook"))
    return out, pts


def web(A, B, upto, sag=0.14, thick=0.03, dip=0.3):
    """A membrane between two rib paths, sagging in toward the cup, its free
    edge torn into points between the rib tips."""
    import bmesh
    from mathutils import Vector
    pa = [p for p, _ in tk._catmull(A, 3)]
    pb = [p for p, _ in tk._catmull(B, 3)]

    def at(path, u):
        x = u * (len(path) - 1)
        i = min(int(x), len(path) - 2)
        return path[i].lerp(path[i + 1], x - i)

    bm = bmesh.new()
    cols, rows = 9, 16
    grid = []
    for i in range(rows):
        row = []
        for j in range(cols):
            t = j / (cols - 1)
            tear = 0.14 * (j % 2) * (i / (rows - 1)) ** 3
            u = 0.05 + (upto - 0.05) * i / (rows - 1) * (1.0 - dip * math.sin(math.pi * t) - tear)
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
    parts += fa + fb + [web(pf, pb, 0.86)]


def underglow(radius=1.25, z=0.012, peak=0.7):
    """A faint blood-red glow on the ground round the foot, fading well inside
    the ring. Its colour and fade are one small generated image."""
    import bmesh
    import bpy
    n = 48
    img = bpy.data.images.new("tar_underglow", n, n, alpha=True)
    img["okGenerated"] = True  # made here, so it ships (okpaint.py)
    px = []
    for j in range(n):
        for i in range(n):
            x, y = (i + 0.5) / n * 2 - 1, (j + 0.5) / n * 2 - 1
            a = peak * max(0.0, 1.0 - math.hypot(x, y)) ** 2.2
            px += [1.0, 0.07, 0.02, a]
    img.pixels[:] = px
    img.pack()
    m = bpy.data.materials.new("tar_underglow")
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], b.inputs["Base Color"])
    nt.links.new(tex.outputs["Color"], b.inputs["Emission Color"])
    nt.links.new(tex.outputs["Alpha"], b.inputs["Alpha"])
    b.inputs["Emission Strength"].default_value = 1.6
    b.inputs["Roughness"].default_value = 1.0
    b.inputs["Specular IOR Level"].default_value = 0.0
    m.surface_render_method = "BLENDED"
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=True, segments=32, radius=radius)
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        f.normal_update()
        if f.normal.z < 0:
            f.normal_flip()
        for lp in f.loops:
            lp[uv].uv = (lp.vert.co.x / radius * 0.5 + 0.5, lp.vert.co.y / radius * 0.5 + 0.5)
    for v in bm.verts:
        v.co.z = z
    return hk._object("underglow", bm, m)


def fissures(z=0.02):
    """Thin cracks in the ground glowing ember red, running out from under the
    creature between its limbs and dying out short of the ring."""
    import random
    import bmesh
    rnd = random.Random(7)
    crack = tk.glow("tar_crack", tk.lin((226, 44, 12)), tk.lin((255, 54, 14)), 2.0, rough=0.9)
    bm = bmesh.new()
    for deg in (-62, -118, -20, -160, 22, 160, 90):
        a0 = math.radians(deg)
        pts = []
        for k in range(7):
            r = 0.34 + 0.11 * k
            a = a0 + rnd.uniform(-0.16, 0.16) * (1 if k else 0)
            pts.append((r * math.cos(a), r * math.sin(a)))
        for k in range(len(pts) - 1):
            (x0, y0), (x1, y1) = pts[k], pts[k + 1]
            dx, dy = x1 - x0, y1 - y0
            ln = math.hypot(dx, dy)
            nx, ny = -dy / ln, dx / ln
            w0 = 0.045 * (1 - k / (len(pts) - 1))
            w1 = 0.045 * (1 - (k + 1) / (len(pts) - 1))
            q = [(x0 + nx * w0, y0 + ny * w0), (x1 + nx * w1, y1 + ny * w1),
                 (x1 - nx * w1, y1 - ny * w1), (x0 - nx * w0, y0 - ny * w0)]
            if w1 < 1e-4:
                q = [q[0], (x1, y1), q[3]]
            f = bm.faces.new([bm.verts.new((x, y * tk.RING_DEEP + tk.RING_Y * 0.5, z)) for x, y in q])
            f.normal_update()
            if f.normal.z < 0:
                f.normal_flip()
    return hk._object("crack", bm, crack)


parts.append(underglow())
parts.append(fissures())

tris = Counter()
for p in parts:
    tris[p.name.split(".")[0]] += sum(len(f.vertices) - 2 for f in p.data.polygons)
print("PARTS", dict(tris))
ob = hk.finish(parts, GLB, {"replacesTexture": "tarmana_tarosmeetsdivine", "replacesPiece": "tarlode"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
tk.standard_view()
hk.renders(ob, OUT, NAME, PIC, HOT, scale=4)
