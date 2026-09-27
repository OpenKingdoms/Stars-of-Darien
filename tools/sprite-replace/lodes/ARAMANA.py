"""Aramon divine lodestone: a hulking seated knight in battle-worn olive
gunmetal plate on a carved stone throne, its fingers curled over the rolled
ends of the armrests, and for a head the divine lodestone itself: a great egg
of polished olive bronze cut into long gore facets that meet at a sharp
point, held in an olive brass setting on its gorget.

    blender -b --factory-startup --python tools/sprite-replace/lodes/ARAMANA.py
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import handkit as hk  # noqa: E402

NAME = "ARAMANA"
PIC = r"D:\OKReplace\lodes\sprites\ARAMANA.png"
HOT = (31, 110)
OUT = r"D:\OKReplace\lodes\hand"


def lin(rgb):
    """sRGB 0-255 to linear 0-1."""
    return tuple(((c / 255) / 12.92) if c <= 10 else (((c / 255) + 0.055) / 1.055) ** 2.4 for c in rgb)


def enc(x):
    """Linear 0-1 to the sRGB encoding a byte image stores."""
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, 12.92 * x, 1.055 * x ** (1 / 2.4) - 0.055)


# the stage's AgX view: emitted linear light against the grey it shows as
AGX_L = [0, 0.001, 0.002, 0.004, 0.007, 0.01, 0.014, 0.02, 0.03, 0.04, 0.05, 0.06, 0.08, 0.1, 0.2, 0.3, 0.45,
         0.6, 0.8, 1.0, 1.5, 2.0, 3.0, 5.0, 7.0, 10.0]
AGX_D = [0, 1, 3, 6, 13, 19, 25, 32, 42, 50, 58, 65, 77, 86, 124, 146, 165, 177, 189, 196, 210, 217, 227, 237,
         243, 249]


def glow(rgb):
    """The emitted linear light that shows as this 0-255 colour on the stage."""
    return np.interp(np.asarray(rgb, np.float64), AGX_D, AGX_L)


def lathe(name, prof, mat, seg=12, rot=0.0, sy=1.0):
    """A solid of revolution with seg flat sides from (r, z) pairs that start
    and end on the axis, or a closed loop (a ring); sy squashes it front to
    back."""
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        if r < 1e-6:
            rings.append([bm.verts.new((0.0, 0.0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(rot + 2 * math.pi * i / seg),
                                        sy * r * math.sin(rot + 2 * math.pi * i / seg), z)) for i in range(seg)])
    closed = len(rings[0]) > 1 and len(rings[-1]) > 1
    n = len(rings)
    for k in range(n if closed else n - 1):
        a, b = rings[k], rings[(k + 1) % n]
        for i in range(seg):
            j = (i + 1) % seg
            if len(a) == 1:
                bm.faces.new((a[0], b[i], b[j]))
            elif len(b) == 1:
                bm.faces.new((a[i], a[j], b[0]))
            else:
                bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hk._object(name, bm, mat)


def place(ob, origin, axis, spin=0.0):
    """Turns the part's +Z onto axis and puts its origin at origin."""
    axis = Vector(axis).normalized()
    q = axis.to_track_quat("Z", "Y")
    ob.rotation_euler = (q.to_matrix().to_4x4() @ Matrix.Rotation(spin, 4, "Z")).to_euler()
    ob.location = origin
    return ob


def tube(name, p0, p1, r0, r1, mat, seg=12):
    """A tapered limb from p0 to p1."""
    p0, p1 = Vector(p0), Vector(p1)
    ob = lathe(name, [(0, 0), (r0, 0), (r1, (p1 - p0).length), (0, (p1 - p0).length)], mat, seg)
    return place(ob, p0, p1 - p0)


def lame(name, r_bot, r_top, h, mat, seg=12, t=0.05, lip=0.02, sy=1.0, rot=0.0):
    """One plate of a laminated defence: a short frustum shell standing on
    z = 0, a little wider at its foot, with a bevelled lower edge."""
    prof = [(r_bot - t, 0.0), (r_bot - lip, 0.0), (r_bot, lip), (r_bot, 0.35 * h), (r_top, h), (r_top - t, h)]
    return lathe(name, prof, mat, seg, rot, sy)


def band(name, R, th0, th1, mat, seg=16, t=0.05, lip=0.025, steps=4):
    """A spherical band of radius R between polar angles th0 and th1 (degrees)
    about +Z, as a shell t thick with a rolled lip on its lower edge. From
    th0 = 0 it is a cap."""
    outer = [(R * math.sin(math.radians(a)), R * math.cos(math.radians(a)))
             for a in np.linspace(th0, th1, steps + 1)]
    s1, c1 = math.sin(math.radians(th1)), math.cos(math.radians(th1))
    tip = [((R + lip) * s1, (R + lip) * c1 - 0.01)]
    inner = [((R - t) * math.sin(math.radians(a)), (R - t) * math.cos(math.radians(a)))
             for a in np.linspace(th1, th0, steps + 1)]
    if th0 < 1e-6:
        prof = [(0.0, R)] + outer[1:] + tip + inner[:-1] + [(0.0, R - t)]
    else:
        prof = outer + tip + inner
    return lathe(name, prof, mat, seg)


def dome(name, r, h, mat, seg=12, steps=4):
    prof = [(0, 0)] + [(r * math.cos(t), h * math.sin(t))
                       for t in (i * (math.pi / 2) / steps for i in range(steps))] + [(0, h)]
    return lathe(name, prof, mat, seg)


def rbox(name, sx, sy, sz, loc, mat, bev=0.04, rot=(0.0, 0.0, 0.0), seg=2):
    """A bevelled box centred on loc."""
    ob = hk.box(sx, sy, sz, z=-sz / 2, mat=mat, name=name)
    hk.bevel(ob, bev, seg)
    ob.rotation_euler = rot
    ob.location = loc
    return ob


def arch(name, R, span, length, t, mat, seg=8, lip=0.02):
    """A plate bent round the Y axis: an arc of span degrees centred on +Z,
    R out from the axis, t thick and length long from y = 0, with its outer
    face a little proud along both long edges."""
    bm = bmesh.new()
    rows = []
    for y in (0.0, length):
        ring = []
        for k in range(seg + 1):
            a = math.radians(-span / 2 + span * k / seg)
            r = R + (lip if k in (0, seg) else 0.0)
            ring.append((bm.verts.new((r * math.sin(a), y, r * math.cos(a))),
                         bm.verts.new(((R - t) * math.sin(a), y, (R - t) * math.cos(a)))))
        rows.append(ring)
    a0, a1 = rows
    for k in range(seg):
        bm.faces.new((a0[k][0], a0[k + 1][0], a1[k + 1][0], a1[k][0]))
        bm.faces.new((a0[k][1], a1[k][1], a1[k + 1][1], a0[k + 1][1]))
        bm.faces.new((a0[k][0], a0[k][1], a0[k + 1][1], a0[k + 1][0]))
        bm.faces.new((a1[k][0], a1[k + 1][0], a1[k + 1][1], a1[k][1]))
    for k in (0, seg):
        bm.faces.new((a0[k][0], a1[k][0], a1[k][1], a0[k][1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hk._object(name, bm, mat)


def image(name, rgb):
    """A packed image from an (h, w, 3) array of sRGB-encoded values in 0-1."""
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    px = np.ones((h, w, 4), np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels[:] = px.ravel()
    img.pack()
    return img


def noise(n=128, beta=2.0, seed=1):
    """Tileable 1/f noise in 0-1."""
    rng = np.random.default_rng(seed)
    f = np.fft.fft2(rng.standard_normal((n, n)))
    fx, fy = np.fft.fftfreq(n)[:, None], np.fft.fftfreq(n)[None, :]
    r = np.sqrt(fx * fx + fy * fy)
    r[0, 0] = 1.0
    f = f / r ** (beta / 2)
    f[0, 0] = 0
    t = np.real(np.fft.ifft2(f))
    return (t - t.min()) / (t.max() - t.min())


def textured(name, img, rough, metal, spec=0.5, coat=0.0, rm=None, coat_rough=0.2):
    """A material with a colour map, and optionally a map carrying roughness
    in green and metalness in blue, as glTF packs them."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    b.inputs["Specular IOR Level"].default_value = spec
    b.inputs["Coat Weight"].default_value = coat
    b.inputs["Coat Roughness"].default_value = coat_rough
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    if rm is not None:
        rm.colorspace_settings.name = "Non-Color"
        r = nt.nodes.new("ShaderNodeTexImage")
        r.image = rm
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(r.outputs["Color"], sep.inputs["Color"])
        nt.links.new(sep.outputs["Green"], b.inputs["Roughness"])
        nt.links.new(sep.outputs["Blue"], b.inputs["Metallic"])
    return m


def stone_mat(name, rgb, seed):
    n = noise(128, 2.1, seed)
    fine = noise(128, 0.8, seed + 7)
    v = 0.84 + 0.22 * n + 0.08 * (fine - 0.5)
    return textured(name, image(name + "_tex", v[..., None] * (np.array(rgb, np.float32) / 255)[None, None, :]),
                    0.85, 0.0)


def worn_mat(name, rgb, seed, rough, metal, grime=0.35, coat=0.0, soot=0.4, scratches=30, matte=0.3,
             coat_rough=0.2, beta=3.0):
    """Battle-worn plate: broad soft clouds of soot that are darker, rougher
    and less metallic than the bare steel, fine pitting, and a scatter of
    short bright scratches."""
    blot = noise(128, beta, seed)
    fine = noise(128, 0.7, seed + 3)
    w = smooth01((blot - soot) / 0.5)
    v = 1.0 - grime * w - 0.08 * (fine - 0.5)
    rng = np.random.default_rng(seed + 11)
    for _ in range(scratches):
        x, y = rng.integers(0, 128, 2)
        a = rng.uniform(0, math.pi)
        for s in range(int(rng.integers(3, 8))):
            v[int(y + s * math.sin(a)) % 128, int(x + s * math.cos(a)) % 128] += 0.12
    rgb = np.clip(v, 0.3, 1.2)[..., None] * (np.array(rgb, np.float32) / 255)[None, None, :]
    rm = np.zeros((128, 128, 3))
    rm[..., 0] = 1.0
    rm[..., 1] = np.clip(rough + matte * w + 0.06 * (fine - 0.5), 0.05, 1.0)
    rm[..., 2] = metal * (1.0 - 0.4 * w)
    return textured(name, image(name + "_tex", rgb), rough, metal, coat=coat, rm=image(name + "_rm", rm),
                    coat_rough=coat_rough)


def ridged(ob, amt, sy, p=10):
    """Pushes a lathed part out along its front, into a medial ridge."""
    for v in ob.data.vertices:
        r = math.hypot(v.co.x, v.co.y / sy)
        if r < 1e-6:
            continue
        w = max(0.0, -v.co.y / sy / r) ** p
        v.co.x *= 1 + amt * w
        v.co.y *= 1 + amt * w
    return ob


def box_uv(ob, k=0.6):
    """World-scale box mapping, so the wear keeps one grain size."""
    me = ob.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    mw = ob.matrix_world
    for p in me.polygons:
        n = (mw.to_3x3() @ p.normal).normalized()
        for li in p.loop_indices:
            co = mw @ me.vertices[me.loops[li].vertex_index].co
            if abs(n.z) > 0.7:
                uv.data[li].uv = (co.x * k, co.y * k)
            elif abs(n.x) > abs(n.y):
                uv.data[li].uv = (co.y * k, co.z * k)
            else:
                uv.data[li].uv = (co.x * k, co.z * k)


# ---------------------------------------------------------------- the egg --

def cut_egg(A, HU, CL, q, seg, phi0, crown, nlo=3):
    """The lodestone: seg long gore facets rising from the girdle, the widest
    ring, to a sharp point, bowed out like an ogive through the crown rings,
    and a rounded underside of nlo rings to a point below. Every facet has
    its own tile in a (band, column) grid, u across it and v up it."""
    prof = [(0.0, -CL)]
    for k in range(1, nlo + 1):
        p = math.radians(-90 + 90 * k / (nlo + 1))
        prof.append((A * math.cos(p), CL * math.sin(p)))
    prof.append((A, 0.0))
    for s in crown:
        prof.append((A * (1 - s ** q) ** 0.5, HU * s))
    prof.append((0.0, HU))
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = []
    for r, z in prof:
        if r < 1e-6:
            rings.append([bm.verts.new((0.0, 0.0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(phi0 + 2 * math.pi * i / seg),
                                        r * math.sin(phi0 + 2 * math.pi * i / seg), z)) for i in range(seg)])
    nb = len(rings) - 1
    facets = []
    e = 1.0 / 16  # keep a texel clear of the tile's edge
    for b in range(nb):
        lo, hi = rings[b], rings[b + 1]
        for i in range(seg):
            j = (i + 1) % seg
            if len(lo) == 1:
                vs, uv = (lo[0], hi[j], hi[i]), ((0.5, e), (1 - e, 1 - e), (e, 1 - e))
            elif len(hi) == 1:
                vs, uv = (lo[i], lo[j], hi[0]), ((e, e), (1 - e, e), (0.5, 1 - e))
            else:
                vs, uv = (lo[i], lo[j], hi[j], hi[i]), ((e, e), (1 - e, e), (1 - e, 1 - e), (e, 1 - e))
            f = bm.faces.new(vs)
            for lp, (u, v) in zip(f.loops, uv):
                lp[uvl].uv = ((i + u) / seg, (b + v) / nb)
            facets.append((f, b, i))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm, facets, nb


def smooth01(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


def egg_paint(n, V):
    """The light the polished egg shows the classic camera, as 0-255 colour:
    the reflection of a bright left and an olive sky above the front, dark
    toward the ground in front and to the right, brown-black underneath."""
    R = 2 * n.dot(V) * n - V
    dark = np.array((18, 17, 10), np.float64)
    left = np.array((66, 66, 36), np.float64) if R.z > -0.3 else np.array((66, 58, 40), np.float64)
    sky = np.array((46, 48, 24), np.float64)
    w_left = smooth01((-R.x - 0.2) / 0.35) * min(1.0, max(0.15, 1.0 - 2.5 * R.y))
    w_sky = smooth01((R.z - 0.05) / 0.2) * smooth01((0.6 - R.x) / 0.4)
    c = dark
    c = c + (left - c) * w_left
    c = c + (sky - c) * w_sky
    return c


hk.reset()
# blackened plate: olive gunmetal under soft soot, a clear coat for the
# glints, and polished rims and bevels for the brightest of them
PLATE = dict(rough=0.45, metal=0.8, grime=0.4, coat=0.6, matte=0.35, coat_rough=0.35, beta=2.6)
CHEST = dict(rough=0.38, metal=0.65, grime=0.45, coat=0.6, matte=0.35, coat_rough=0.35, beta=2.5)
GREAVE = dict(rough=0.4, metal=0.45, grime=0.3, coat=0.3, matte=0.3, coat_rough=0.35)
steel = worn_mat("ara_plate", (126, 125, 110), 21, **PLATE)
steel_dark = worn_mat("ara_plate_dark", (106, 105, 92), 25, **PLATE)
steel_bright = worn_mat("ara_plate_bright", (184, 182, 164), 27, **PLATE)
steel_chest = worn_mat("ara_plate_chest", (176, 175, 157), 33, **CHEST)
polished = worn_mat("ara_plate_polished", (204, 202, 184), 35, **CHEST)
sabaton_mat = worn_mat("ara_sabaton", (100, 97, 80), 31, rough=0.55, metal=0.6, grime=0.3, matte=0.2)
steel_hi = hk.pbr("ara_plate_edge", lin((206, 202, 176)), rough=0.22, metal=1.0)
black = hk.pbr("ara_under", lin((14, 13, 12)), rough=0.8, metal=0.2)
greave_a = worn_mat("ara_greave", (148, 144, 116), 23, **GREAVE)
greave_b = worn_mat("ara_greave_dark", (120, 117, 94), 29, **GREAVE)
bronze = hk.pbr("ara_bronze", lin((118, 106, 66)), rough=0.46, metal=0.9)
brass = hk.pbr("ara_brass", lin((198, 194, 128)), rough=0.3, metal=0.9)
brass_dark = hk.pbr("ara_brass_dark", lin((118, 112, 66)), rough=0.45, metal=0.9)
leather = hk.pbr("ara_leather", lin((58, 44, 30)), rough=0.7, metal=0.0)
stone = stone_mat("ara_throne_stone", (132, 128, 116), 11)
stone_dark = stone_mat("ara_throne_stone_dark", (104, 100, 90), 13)
stone_shade = stone_mat("ara_throne_stone_shade", (52, 50, 45), 17)
pale_gold = brass   # the gorget, the cross and the egg's setting

parts, stones, flat = [], [], []
YC = 0.28   # the torso's centre, front to back
SYT = 0.7   # the torso's depth against its width

# ---- the throne: a moulded plinth, the seat block with a dark apron between
# the knight's shins, a cornice slab, armrests with rolled fronts under the
# hands, and a panelled back that narrows to a low crest behind the gorget --
stones.append(rbox("plinth", 2.12, 1.2, 0.22, (0, 0.62, 0.11), stone_dark, 0.05, seg=1))
stones.append(rbox("seat", 1.9, 1.06, 1.5, (0, 0.6, 0.95), stone, 0.05, seg=1))
stones.append(rbox("seat_apron", 1.18, 0.54, 1.66, (0, -0.06, 0.83), stone_shade, 0.03, seg=1))
stones.append(rbox("apron_foot", 1.24, 0.6, 0.12, (0, -0.06, 0.06), stone_shade, 0.03, seg=1))
stones.append(rbox("seat_slab", 2.08, 1.16, 0.2, (0, 0.6, 1.76), stone, 0.07, seg=1))
stones.append(rbox("seat_band", 1.96, 1.1, 0.08, (0, 0.6, 1.6), stone_dark, 0.03, seg=1))
for sx in (-1, 1):
    stones.append(rbox("armside", 0.22, 1.3, 0.46, (sx * 1.2, 0.45, 2.08), stone_dark, 0.04, seg=1))
    stones.append(rbox("armrest", 0.3, 1.9, 0.14, (sx * 1.2, 0.1, 2.37), stone, 0.04, seg=1))
    roll = tube("armroll", (sx * 1.04, -0.84, 2.33), (sx * 1.36, -0.84, 2.33), 0.13, 0.13, stone, seg=12)
    stones.append(roll)
stones.append(rbox("back", 1.6, 0.3, 1.72, (0, 1.12, 2.76), stone, 0.06, seg=1))
stones.append(rbox("back_panel", 1.2, 0.34, 1.3, (0, 1.12, 2.72), stone_dark, 0.04, seg=1))
stones.append(rbox("back_crest", 1.0, 0.28, 0.5, (0, 1.12, 3.82), stone, 0.05, seg=1))
for sx in (-1, 1):
    stones.append(rbox("back_post", 0.22, 0.4, 1.56, (sx * 0.84, 1.12, 2.62), stone, 0.05, seg=1))
    stones.append(rbox("post_cap", 0.28, 0.46, 0.1, (sx * 0.84, 1.12, 3.43), stone_dark, 0.03, seg=1))
parts.append(rbox("back_cap", 1.1, 0.36, 0.1, (0, 1.12, 4.1), bronze, 0.03))

for sx in (-1, 1):
    # ---- legs: low sabatons as wide as the greaves, greaves of eight thin
    # lames over a black core down to the ground, knee cops with a wing, and
    # cuisses with two lames above the knee ---------------------------------
    LX, LY = sx * 0.62, -0.64
    toe = lathe("sabaton", [(0, 0), (0.3, 0.0), (0.29, 0.07), (0.24, 0.13), (0.12, 0.17), (0, 0.18)], sabaton_mat,
                seg=14, sy=1.12)
    toe.location = (LX - sx * 0.05, LY - 0.1, 0)
    parts.append(toe)
    core = lathe("greave_core", [(0, 0.05), (0.27, 0.05), (0.27, 1.9), (0, 1.9)], black, seg=12, sy=0.9)
    core.location = (LX, LY, 0)
    parts.append(core)
    for k in range(8):
        z0 = 0.05 + 0.22 * k
        g = lame("greave", 0.33 - 0.002 * k, 0.312 - 0.002 * k, 0.18, greave_a if k % 2 == 0 else greave_b,
                 seg=16, t=0.05, lip=0.025, sy=0.9)
        g.location = (LX, LY, z0)
        ridged(g, 0.08, 0.9, p=6)
        parts.append(g)
    th0, th1 = Vector((sx * 0.55, 0.4, 2.12)), Vector((sx * 0.63, -0.66, 2.02))
    parts.append(tube("thigh", th0, th1, 0.37, 0.31, steel_dark, seg=14))
    ax = (th1 - th0).normalized()
    for k, f in enumerate((0.52, 0.72)):
        c = lame("cuisse_lame", 0.4 - 0.02 * k, 0.36 - 0.02 * k, 0.2, steel_dark, seg=14, t=0.06)
        parts.append(place(c, th0 + (th1 - th0) * f, ax))
    cop = band("knee", 0.3, 0, 70, steel_dark, seg=14, t=0.06, lip=0.03)
    parts.append(place(cop, (LX, -0.62, 2.04), (0, -1, 0.15)))
    wing = lathe("knee_wing", [(0, -0.03), (0.22, -0.03), (0.26, 0.0), (0.22, 0.03), (0, 0.03)], steel_hi, seg=12)
    parts.append(place(wing, (sx * 0.88, -0.72, 2.02), (sx, -0.2, 0)))

    # ---- pauldron: a domed cap over the shoulder and three lames stacked
    # down the outside of the arm, each tucked under the one above --------
    P = Vector((sx * 0.8, YC, 4.42))
    pax = Vector((sx * math.sin(math.radians(32)), -0.2, math.cos(math.radians(32))))
    for k, (R, a0, a1) in enumerate(((0.5, 0, 50), (0.48, 46, 70), (0.46, 66, 90), (0.44, 86, 112))):
        p = band("pauldron", R, a0, a1, steel_bright if k < 2 else steel, seg=18, t=0.06, lip=0.03,
                 steps=5 if k == 0 else 3)
        parts.append(place(p, P, pax))
    ridge = band("pauldron_ridge", 0.52, 0, 8, steel_hi, seg=18, t=0.02, lip=0.0, steps=1)
    parts.append(place(ridge, P, pax))
    under = lathe("pauldron_core", [(0, -0.1), (0.3, -0.1), (0.4, 0.15), (0, 0.4)], black, seg=12)
    parts.append(place(under, P + pax * 0.02 + Vector((sx * 0.06, 0, 0)), pax))

    # ---- arm: rerebrace with lames, a big couter with a fan, a vambrace
    # flaring to a bevelled edge at the wrist ------------------------------
    sh, el, wr = Vector((sx * 1.02, 0.24, 4.5)), Vector((sx * 1.18, 0.16, 3.3)), Vector((sx * 1.16, -0.52, 2.64))
    parts.append(tube("upperarm", sh, el, 0.26, 0.23, steel, seg=12))
    aax = (el - sh).normalized()
    for k, f in enumerate((0.45, 0.7)):
        c = lame("rerebrace", 0.3 - 0.01 * k, 0.26 - 0.01 * k, 0.2, steel, seg=12, t=0.05)
        parts.append(place(c, sh + (el - sh) * f, -aax))
    elb = band("couter", 0.3, 0, 72, steel, seg=14, t=0.06, lip=0.03)
    parts.append(place(elb, el + Vector((-sx * 0.02, 0.06, 0)), (sx * 0.3, 0.95, 0.1)))
    fan = lathe("couter_fan", [(0, 0.05), (0.22, -0.02), (0.27, -0.03), (0.24, 0.0), (0, 0.09)], steel_hi, seg=14)
    parts.append(place(fan, el + Vector((sx * 0.21, 0.06, 0.0)), (sx, 0.1, 0.1)))
    fv = (wr - el).normalized()
    parts.append(tube("vambrace", el, wr, 0.22, 0.25, steel, seg=12))
    rim = lathe("vambrace_rim", [(0.22, -0.035), (0.29, -0.01), (0.29, 0.02), (0.22, 0.035)], steel_hi, seg=12)
    parts.append(place(rim, wr - fv * 0.1, fv))
    rim2 = lathe("vambrace_rim", [(0.2, -0.03), (0.26, -0.01), (0.26, 0.02), (0.2, 0.03)], steel_hi, seg=12)
    parts.append(place(rim2, el + fv * 0.18, fv))

    # ---- gauntlet: a flared dark cuff with a dull bronze rim, a plate over
    # the back of the hand, and four fingers of three lames each curled over
    # the armrest's roll, with the thumb along its inner end ---------------
    HX = sx * 1.19
    c0 = wr - fv * 0.05
    cuff = lathe("cuff", [(0.19, 0.0), (0.24, 0.0), (0.29, 0.2), (0.26, 0.21), (0.2, 0.2)], steel, seg=12)
    parts.append(place(cuff, c0, fv))
    cuff_rim = lathe("cuff_rim", [(0.25, 0.17), (0.3, 0.19), (0.3, 0.22), (0.25, 0.225)], bronze, seg=12)
    parts.append(place(cuff_rim, c0, fv))
    palm = rbox("palm", 0.3, 0.34, 0.12, (HX, -0.66, 2.5), black, 0.03, seg=1)
    parts.append(palm)
    hand = arch("hand_plate", 0.22, 104, 0.36, 0.045, steel_dark, seg=8, lip=0.015)
    hand.rotation_euler = (-0.18, 0.0, math.pi)
    hand.location = (HX, -0.5, 2.38)
    parts.append(hand)
    parts.append(tube("knuckles", (HX - 0.17, -0.84, 2.53), (HX + 0.17, -0.84, 2.53), 0.04, 0.04, steel_dark, seg=8))
    RC, RR = Vector((HX, -0.84, 2.33)), 0.13
    for fx in (-0.126, -0.042, 0.042, 0.126):
        for j, (t0, t1) in enumerate(((0, 48), (52, 96), (100, 140))):
            tm = math.radians((t0 + t1) / 2)
            th = 0.058 - 0.008 * j
            rr = RR + th / 2 + 0.005
            ln = rr * math.radians(t1 - t0)
            loc = RC + Vector((fx, -rr * math.sin(tm), rr * math.cos(tm)))
            parts.append(rbox("finger", 0.074 - 0.006 * j, ln, th, loc, steel_dark, 0.018, rot=(tm, 0.0, 0.0), seg=2))
    for j, (t0, t1) in enumerate(((-20, 30), (34, 80))):
        tm = math.radians((t0 + t1) / 2)
        rr = RR + 0.035
        loc = RC + Vector((-sx * 0.2, -rr * math.sin(tm), rr * math.cos(tm)))
        parts.append(rbox("thumb", 0.07, rr * math.radians(t1 - t0), 0.06 - 0.01 * j, loc, steel_dark, 0.018,
                          rot=(tm, 0.0, 0.0), seg=2))

# ---- torso: a black arming coat under a ridged breastplate, a belt, three
# fauld lames flaring over the hips, a cod plate between the thighs, tassets
core = lathe("torso_core", [(0, 2.25), (0.8, 2.25), (0.84, 3.0), (0.86, 4.3), (0.76, 4.86), (0.42, 5.16), (0, 5.16)],
             black, seg=8, rot=math.pi / 8, sy=SYT)
core.location = (0, YC, 0)
parts.append(core)
# the breastplate's lower edge hangs over the plackart, which rises from
# the belt to tuck under it, both with a medial ridge down the front
breast = lathe("breastplate", [(0, 3.96), (0.9, 3.96), (0.99, 3.99), (1.02, 4.2), (0.99, 4.42), (0.9, 4.64),
                               (0.76, 4.86), (0.58, 5.04), (0.4, 5.16), (0, 5.2)], steel_chest, seg=16, sy=SYT)
breast.location = (0, YC, 0)
parts.append(ridged(breast, 0.07, SYT))
plack = lathe("plackart", [(0, 3.28), (0.87, 3.28), (0.91, 3.31), (0.93, 3.6), (0.9, 4.1), (0, 4.1)], steel,
              seg=16, sy=SYT)
plack.location = (0, YC, 0)
parts.append(ridged(plack, 0.06, SYT))
edge = lathe("plackart_edge", [(0.84, 3.26), (0.91, 3.28), (0.91, 3.34), (0.84, 3.36)], steel_hi, seg=16, sy=SYT)
edge.location = (0, YC, 0)
parts.append(ridged(edge, 0.06, SYT))
belt = lathe("belt", [(0.84, 3.0), (0.9, 3.02), (0.9, 3.2), (0.84, 3.22)], leather, seg=8, rot=math.pi / 8,
             sy=SYT + 0.03)
belt.location = (0, YC, 0)
parts.append(belt)
for k in range(3):
    z0 = 2.78 - 0.24 * k
    f = lame("fauld", 0.9 + 0.035 * k, 0.86 + 0.035 * k, 0.26, steel_dark, seg=8, t=0.06, lip=0.03, sy=SYT,
             rot=math.pi / 8)
    f.location = (0, YC, z0)
    parts.append(f)
parts.append(rbox("buckle", 0.24, 0.08, 0.24, (0, YC - 0.92 * SYT * 0.94, 3.11), brass, 0.03, seg=1))
# the cod plate hangs from under the lowest lame, its face turned up to the sky
cod = arch("cod_plate", 0.34, 72, 0.56, 0.05, polished, seg=8, lip=0.02)
for v in cod.data.vertices:
    v.co.x *= 1.0 - 0.12 * (1.0 - v.co.y / 0.56)   # narrower at its foot
    if abs(v.co.x) < 1e-4:
        v.co.z += 0.03   # a medial ridge
cod_rim = arch("cod_rim", 0.35, 74, 0.04, 0.07, steel_hi, seg=8, lip=0.0)
cod_rim.rotation_euler = (math.radians(56), 0.0, 0.0)
cod_rim.location = (0, -0.16, 1.32)
parts.append(cod_rim)
cod.rotation_euler = (math.radians(56), 0.0, 0.0)
cod.location = (0, -0.16, 1.32)
parts.append(cod)
for sx in (-1, 1):
    th0, th1 = Vector((sx * 0.55, 0.4, 2.12)), Vector((sx * 0.63, -0.66, 2.02))
    ax = (th1 - th0).normalized()
    for k in range(2):
        t = arch("tasset", 0.44 - 0.02 * k, 120, 0.3, 0.05, steel_dark, seg=8, lip=0.025)
        t.rotation_euler = (math.asin(ax.z), 0.0, math.atan2(-ax.x, ax.y))
        t.location = th0 + (th1 - th0) * (0.08 + 0.22 * k) + Vector((0, 0, 0.02))
        parts.append(t)
parts.append(rbox("cross_v", 0.12, 0.12, 1.1, (0, -0.44, 4.17), pale_gold, 0.025, seg=1))
parts.append(rbox("cross_h", 0.5, 0.12, 0.12, (0, -0.46, 4.42), pale_gold, 0.025, seg=1))

# ---- the gorget: two olive brass lames rising over the top of the breastplate
GY = 0.5
for k, (z0, r0, r1) in enumerate(((4.86, 0.76, 0.66), (5.1, 0.62, 0.4))):
    g = lame("gorget", r0, r1, 0.3, pale_gold, seg=16, t=0.08, lip=0.03, sy=0.8)
    g.location = (0, GY, z0)
    parts.append(g)
    # a rolled rim on its top edge, which always has a face to the light
    roll = lathe("gorget_roll", [(r1 - 0.01 + 0.045 * math.cos(a), 0.3 + 0.045 * math.sin(a))
                                 for a in (2 * math.pi * j / 8 for j in range(8))], pale_gold, seg=24, sy=0.8)
    roll.location = (0, GY, z0)
    parts.append(roll)
neck = lathe("neck", [(0, 4.8), (0.5, 4.8), (0.46, 5.5), (0, 5.5)], brass_dark, seg=12, sy=0.8)
neck.location = (0, GY, 0)
parts.append(neck)

# ---- the divine lodestone ------------------------------------------------
A, HU, CL, Q = 0.94, 2.6, 1.2, 1.6
SEG, PHI0, CROWN = 10, math.radians(-106), (0.32, 0.64)
LEAN = math.radians(7)
C = Vector((0.0, 0.72, 6.62))   # the girdle's centre
STREAK = {(6, 9), (5, 9)}       # the long lit facet on the front left and the one below it
FLECK = (6, 1)
EGG_BASE, STREAK_BASE = (104, 102, 52), (128, 124, 72)
SKY = 0.08   # the stage's grey sky, which the metal gives back tinted

rot = Matrix.Translation(C) @ Matrix.Rotation(-LEAN, 4, "X")
bm, facets, NB = cut_egg(A, HU, CL, Q, SEG, PHI0, CROWN)
E = math.atan(1.0 / hk.TILT)
V = Vector((0.0, -math.cos(E), math.sin(E)))
T = 16
body_px = np.zeros((NB * T, SEG * T, 3))
streak_px = np.zeros((T, 2 * T, 3))
uu = (np.arange(T) + 0.5) / T
vv = (np.arange(T) + 0.5) / T
for f, b, i in facets:
    n = (rot.to_3x3() @ f.normal).normalized()
    if (b, i) in STREAK:
        k = 0 if b == 6 else 1
        f.material_index = 1
        # white at the middle of the long facet, pale gold toward its foot,
        # fading out down the facet below it; a little dimmer at the edges
        if k == 0:
            stops = [(0.0, (248, 246, 200)), (0.35, (253, 253, 222)), (0.65, (255, 255, 250)),
                     (1.0, (232, 234, 222))]
            edgef = 1.0 - 0.28 * np.abs(2 * uu - 1) ** 3
        else:
            stops = [(0.0, (62, 58, 36)), (0.4, (68, 64, 40)), (0.6, (108, 108, 76)), (0.8, (190, 188, 146)),
                     (1.0, (240, 236, 190))]
            edgef = 1.0 - 0.45 * uu ** 1.5   # the light leaves by the facet's left edge
        col = np.array([[np.interp(v, [s for s, _ in stops], [c[ch] for _, c in stops]) for ch in range(3)]
                        for v in vv])
        tile = col[:, None, :] * edgef[None, :, None]
        tile = np.maximum(0.0, glow(tile) * 1.4 - SKY * np.array(lin(STREAK_BASE))) / 16.0
        streak_px[:, k * T:(k + 1) * T] = enc(tile)
        for lp in f.loops:
            uv = lp[bm.loops.layers.uv["UVMap"]].uv
            lp[bm.loops.layers.uv["UVMap"]].uv = (((uv.x * SEG - i) + k) / 2, uv.y * NB - b)
        continue
    c = egg_paint(n, V)
    tile = np.broadcast_to(c, (T, T, 3)).copy()
    if (b, i) == FLECK:
        # one small fleck of green deep in the stone
        d = np.hypot((uu[None, :] - 0.3) * 1.0, (vv[:, None] - 0.45) * 1.6)
        w = smooth01((0.16 - d) / 0.08)[..., None]
        tile = tile * (1 - w) + np.array((24, 54, 4)) * w
    body_px[b * T:(b + 1) * T, i * T:(i + 1) * T] = enc(np.maximum(0.0, glow(tile) - SKY * np.array(lin(EGG_BASE))))
egg = hk._object("egg", bm)


def gem_mat(name, img, strength, base, rough=0.34, metal=1.0):
    """Polished olive bronze: a metal tint in the reflections, a clear coat
    for hard glints, and the facet light as emission."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*lin(base), 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    b.inputs["Coat Weight"].default_value = 0.5
    b.inputs["Coat Roughness"].default_value = 0.05
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img
    t.extension = "EXTEND"
    nt.links.new(t.outputs["Color"], b.inputs["Emission Color"])
    b.inputs["Emission Strength"].default_value = strength
    return m


egg.data.materials.append(gem_mat("ara_lodestone", image("ara_lodestone_light", body_px), 1.0, EGG_BASE))
egg.data.materials.append(gem_mat("ara_lodestone_streak", image("ara_lodestone_streak_light", streak_px), 16.0,
                                  STREAK_BASE))
flat.append(egg)
setting = [egg]

# the setting: a cup under the egg with a beaded rim, six short claws up
# its underside, and a stem down into the gorget
zr = -CL * math.sqrt(1 - (0.56 / A) ** 2)   # where the egg is 0.56 wide
cup = lathe("cup", [(0.12, -CL - 0.4), (0.3, -CL - 0.38), (0.34, -CL - 0.12), (0.5, -CL + 0.02), (0.63, zr - 0.02),
                    (0.66, zr + 0.04), (0.6, zr + 0.06), (0.46, -CL + 0.08), (0.2, -CL - 0.02), (0.12, -CL - 0.02)],
            pale_gold, seg=14)
setting.append(cup)
bead = lathe("cup_bead", [(0.66 + 0.045 * math.cos(t), zr + 0.05 + 0.045 * math.sin(t))
                          for t in (2 * math.pi * k / 6 for k in range(6))], pale_gold, seg=16)
setting.append(bead)
for i in range(6):
    t = 2 * math.pi * i / 6
    ca, sa = math.cos(t), math.sin(t)
    path = [(0.64, zr + 0.04)] + [(A * math.cos(math.radians(p)) + 0.04, -CL * math.sin(math.radians(p)))
                                  for p in (48, 34)]
    for (ra, za), (rb, zb), (w0, w1) in zip(path, path[1:], ((0.07, 0.055), (0.055, 0.035))):
        setting.append(tube("claw", (ra * ca, ra * sa, za), (rb * ca, rb * sa, zb), w0, w1, pale_gold, seg=6))
bpy.context.view_layer.update()
for p in setting:
    p.matrix_world = rot @ p.matrix_world
parts += setting

bpy.context.view_layer.update()
for p in stones + parts:
    if p is not egg:
        box_uv(p)
# hk.finish joins onto the first part; it must carry the UV map or the joined
# mesh is left without an active one and every texture reads one texel
parts = stones + parts
for p in parts:
    if p in flat:
        continue
    hk.smooth(p, 35)

ob = hk.finish(parts, os.path.join(OUT, "models", NAME + ".glb"),
               {"replacesTexture": "aramanadivinelodestone", "replacesPiece": "aramana"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
hk.renders(ob, os.path.join(OUT, "renders"), NAME, PIC, HOT, scale=4)
