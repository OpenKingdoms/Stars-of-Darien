"""Aramon divine lodestone: a seated colossus in polished plate on a carved
stone throne, hands on its knees, and for a head the divine lodestone
itself, a great faceted egg of dark olive emerald held in a pale gold
reliquary setting on its gorget.

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
    """sRGB 0-255 to linear 0-1, for colours sampled from the picture."""
    return tuple(((c / 255) / 12.92) if c <= 10 else (((c / 255) + 0.055) / 1.055) ** 2.4 for c in rgb)


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


def tube(name, p0, p1, r0, r1, mat, seg=10):
    """A tapered limb from p0 to p1."""
    p0, p1 = Vector(p0), Vector(p1)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r0, radius2=r1, depth=(p1 - p0).length)
    ob = hk._object(name, bm, mat)
    ob.rotation_euler = (p1 - p0).to_track_quat("Z", "Y").to_euler()
    ob.location = (p0 + p1) / 2
    return ob


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


def image(name, rgb):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    px = np.ones((h, w, 4), np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels[:] = px.ravel()
    img.pack()
    return img


def noise(n=128, beta=2.0, seed=1):
    rng = np.random.default_rng(seed)
    f = np.fft.fft2(rng.standard_normal((n, n)))
    fx, fy = np.fft.fftfreq(n)[:, None], np.fft.fftfreq(n)[None, :]
    r = np.sqrt(fx * fx + fy * fy)
    r[0, 0] = 1.0
    f = f / r ** (beta / 2)
    f[0, 0] = 0
    t = np.real(np.fft.ifft2(f))
    return (t - t.min()) / (t.max() - t.min())


def stone_mat(name, rgb, seed):
    n = noise(128, 2.1, seed)
    fine = noise(128, 0.8, seed + 7)
    v = 0.84 + 0.22 * n + 0.08 * (fine - 0.5)
    img = image(name + "_tex", v[..., None] * (np.array(rgb, np.float32) / 255)[None, None, :])
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = 0.85
    t = m.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = img
    m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    return m


def box_uv(ob, k=0.6):
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


def gem_egg(name, a, cu, cl, q, seg, nlo, nup, point, facet_colour):
    """A faceted egg centred on its widest ring: a round underside of depth
    cl, a slightly pointed top of height cu with a short point on it. The
    rings alternate by half a step so every facet is a triangle, like a cut
    stone. Each facet takes one texel of a (band, column) grid, whose base
    and glow colours facet_colour(direction, band, column) picks."""
    rings = [(a * math.cos(p), cl * math.sin(p))
             for p in (math.radians(-90 + 90 * k / nlo) for k in range(1, nlo + 1))]
    for k in range(1, nup):
        s = math.sin(math.radians(90 * k / nup))
        rings.append((a * max(0.0, 1 - s ** q) ** 0.5, cu * s))
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    vr = []
    for j, (r, z) in enumerate(rings):
        off = (j % 2) * math.pi / seg
        vr.append([bm.verts.new((r * math.cos(off + 2 * math.pi * i / seg),
                                 r * math.sin(off + 2 * math.pi * i / seg), z)) for i in range(seg)])
    bottom = bm.verts.new((0, 0, -cl))
    top = bm.verts.new((0, 0, cu + point))
    faces = []  # (face, band, column)
    for i in range(seg):
        faces.append((bm.faces.new((bottom, vr[0][(i + 1) % seg], vr[0][i])), 0, 2 * i))
    for j in range(len(vr) - 1):
        A, B = vr[j], vr[j + 1]
        for i in range(seg):
            i1 = (i + 1) % seg
            if j % 2 == 0:   # B sits half a step ahead of A
                faces.append((bm.faces.new((A[i], A[i1], B[i])), j + 1, 2 * i))
                faces.append((bm.faces.new((A[i1], B[i1], B[i])), j + 1, 2 * i + 1))
            else:            # A sits half a step ahead of B
                faces.append((bm.faces.new((B[i], B[i1], A[i])), j + 1, 2 * i))
                faces.append((bm.faces.new((B[i1], A[i1], A[i])), j + 1, 2 * i + 1))
    for i in range(seg):
        faces.append((bm.faces.new((vr[-1][i], vr[-1][(i + 1) % seg], top)), len(vr), 2 * i))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    W, H = 2 * seg, len(vr) + 1
    base = np.zeros((H, W, 3), np.float32)
    glow = np.zeros((H, W, 3), np.float32)
    for f, b, c in faces:
        cen = f.calc_center_median()
        d = Vector((cen.x / a, cen.y / a, cen.z / (cu if cen.z > 0 else cl))).normalized()
        base[b, c], glow[b, c] = facet_colour(d, b, c)
        for lp in f.loops:
            lp[uvl].uv = ((c + 0.5) / W, (b + 0.5) / H)
    ob = hk._object(name, bm)
    return ob, base, glow


def gem_mat(name, base, glow, rough=0.15, metal=0.3):
    """Polished stone: per-facet base and glow textures read texel by texel,
    a high specular with a little metal in it so the reflections take the
    olive tint, and a mirror clear coat on top for the hard glints."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    b.inputs["IOR"].default_value = 1.6
    b.inputs["Specular IOR Level"].default_value = 0.8
    b.inputs["Coat Weight"].default_value = 0.6
    b.inputs["Coat Roughness"].default_value = 0.03
    for img, sock in ((base, "Base Color"), (glow, "Emission Color")):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = img
        t.interpolation = "Closest"
        t.extension = "EXTEND"
        nt.links.new(t.outputs["Color"], b.inputs[sock])
    b.inputs["Emission Strength"].default_value = 1.0
    return m


hk.reset()
steel = hk.pbr("ara_steel", lin((214, 214, 210)), rough=0.22, metal=0.9)
steel_dark = hk.pbr("ara_steel_dark", lin((158, 158, 156)), rough=0.3, metal=1.0)
olive = hk.pbr("ara_olive_steel", lin((178, 164, 124)), rough=0.3, metal=0.9)
olive_dark = hk.pbr("ara_olive_dark", lin((118, 110, 86)), rough=0.36, metal=0.9)
gold = hk.pbr("ara_gold", lin((216, 184, 112)), rough=0.26, metal=1.0)
pale_gold = hk.pbr("ara_pale_gold", lin((236, 228, 190)), rough=0.26, metal=1.0)
stone = stone_mat("ara_throne_stone", (132, 128, 116), 11)
stone_dark = stone_mat("ara_throne_stone_dark", (104, 100, 90), 13)

parts, smooth25, flat, stones = [], [], [], []
YC = 0.2  # the torso's centre, front to back

# ---- the throne: a moulded plinth, the seat block, a cornice slab, armrests
# with rolled fronts under the hands, and a panelled back with a gold cap ---
stones.append(rbox("plinth", 2.12, 1.46, 0.22, (0, 0.42, 0.11), stone_dark, 0.05, seg=1))
stones.append(rbox("seat", 1.9, 1.28, 1.5, (0, 0.42, 0.95), stone, 0.05, seg=1))
stones.append(rbox("seat_slab", 2.08, 1.4, 0.2, (0, 0.4, 1.76), stone, 0.07, seg=1))
stones.append(rbox("seat_band", 1.96, 1.34, 0.08, (0, 0.42, 1.6), stone_dark, 0.03, seg=1))
for sx in (-1, 1):
    stones.append(rbox("armside", 0.22, 1.3, 0.46, (sx * 1.2, 0.45, 2.08), stone_dark, 0.04, seg=1))
    stones.append(rbox("armrest", 0.3, 1.9, 0.14, (sx * 1.2, 0.1, 2.37), stone, 0.04, seg=1))
    roll = tube("armroll", (sx * 1.04, -0.84, 2.33), (sx * 1.36, -0.84, 2.33), 0.13, 0.13, stone, seg=12)
    stones.append(roll)
    smooth25.append(roll)
stones.append(rbox("back", 1.6, 0.3, 2.1, (0, 0.95, 2.95), stone, 0.06, seg=1))
stones.append(rbox("back_panel", 1.2, 0.34, 1.5, (0, 0.95, 2.9), stone_dark, 0.04, seg=1))
for sx in (-1, 1):
    stones.append(rbox("back_post", 0.22, 0.4, 2.2, (sx * 0.84, 0.95, 2.95), stone, 0.05, seg=1))
parts.append(rbox("back_cap", 1.8, 0.46, 0.12, (0, 0.95, 4.1), gold, 0.04))

# ---- legs: olive steel greaves with dark bands, steel knees and thighs -----
for sx in (-1, 1):
    foot = rbox("foot", 0.48, 0.64, 0.28, (sx * 0.64, -0.6, 0.14), steel_dark, 0.1, seg=3)
    parts.append(foot)
    smooth25.append(foot)
    greave = lathe("greave", [(0, 0.2), (0.3, 0.2), (0.33, 0.7), (0.345, 1.2), (0.31, 1.9), (0, 1.9)],
                   olive, seg=12, sy=0.85)
    greave.location = (sx * 0.64, -0.58, 0)
    parts.append(greave)
    for z in (0.62, 1.08, 1.54):
        lame = lathe("lame", [(0.3, z - 0.05), (0.37, z - 0.03), (0.37, z + 0.03), (0.3, z + 0.05)],
                     olive_dark, seg=12, sy=0.85)
        lame.location = (sx * 0.64, -0.58, 0)
        parts.append(lame)
    cop = dome("knee", 0.3, 0.2, steel, seg=12, steps=3)
    cop.rotation_euler = (math.pi / 2, 0, 0)
    cop.location = (sx * 0.64, -0.8, 2.02)
    parts.append(cop)
    smooth25.append(cop)
    parts.append(tube("thigh", (sx * 0.55, 0.4, 2.12), (sx * 0.63, -0.72, 2.02), 0.38, 0.32, steel, seg=12))

    # pauldron in three lames with a gold rim
    for k in range(3):
        p = dome("pauldron", 0.42 - 0.02 * k, 0.32 - 0.04 * k, steel, seg=12, steps=4)
        p.rotation_euler = (0, sx * math.radians(28 + 14 * k), 0)
        p.location = (sx * (0.9 + 0.08 * k), YC, 4.9 - 0.17 * k)
        parts.append(p)
        smooth25.append(p)
    rim = lathe("pauldron_rim", [(0.38, -0.03), (0.44, -0.03), (0.44, 0.03), (0.38, 0.03)], gold, seg=12)
    rim.rotation_euler = (0, sx * math.radians(56), 0)
    rim.location = (sx * 1.1, YC, 4.55)
    parts.append(rim)

    # arm: upper arm hanging, a couter with a fan at the elbow, a vambrace
    # tapering to the wrist, a flared cuff and a heavy gauntlet whose fingers
    # curl over the armrest's roll
    sh, el, wr = (sx * 1.08, YC, 4.62), (sx * 1.2, YC - 0.04, 3.3), (sx * 1.16, -0.52, 2.64)
    parts.append(tube("upperarm", sh, el, 0.28, 0.24, steel, seg=12))
    elb = dome("couter", 0.27, 0.2, steel, seg=12, steps=3)
    elb.rotation_euler = (0, sx * math.pi / 2, 0)
    elb.location = (sx * 1.26, YC - 0.04, 3.28)
    parts.append(elb)
    smooth25.append(elb)
    fan = lathe("couter_fan", [(0, -0.025), (0.24, -0.025), (0.26, 0.0), (0.24, 0.025), (0, 0.025)], steel_dark,
                seg=12)
    fan.rotation_euler = (0, sx * math.pi / 2, 0)
    fan.location = (sx * 1.37, YC + 0.02, 3.3)
    parts.append(fan)
    parts.append(tube("vambrace", el, wr, 0.25, 0.17, steel, seg=12))
    wv = Vector(wr) - Vector(el)
    wv.normalize()
    c0 = Vector(wr) - wv * 0.06
    parts.append(tube("cuff", c0, c0 + wv * 0.22, 0.19, 0.27, gold, seg=12))
    parts.append(rbox("gauntlet", 0.4, 0.4, 0.24, (sx * 1.19, -0.76, 2.58), steel_dark, 0.08, seg=1))
    parts.append(rbox("knuckles", 0.42, 0.14, 0.14, (sx * 1.19, -0.94, 2.58), steel, 0.05, seg=1))
    for f in (-0.13, 0.0, 0.13):
        parts.append(rbox("finger", 0.1, 0.1, 0.22, (sx * 1.19 + f, -1.0, 2.44), steel_dark, 0.035, seg=1))

# ---- torso: an octagonal breastplate with a ridge, belt, buckle and tassets --
torso = lathe("torso", [(0, 2.25), (0.85, 2.25), (0.93, 2.9), (1.02, 3.6), (1.05, 4.3), (0.9, 4.9),
                        (0.5, 5.2), (0, 5.25)], steel, seg=8, rot=math.pi / 8, sy=0.62)
torso.location = (0, YC, 0)
parts.append(torso)
smooth25.append(torso)
belt = lathe("belt", [(0, 2.35), (0.88, 2.35), (0.9, 2.62), (0, 2.62)], gold, seg=8, rot=math.pi / 8, sy=0.66)
belt.location = (0, YC, 0)
parts.append(belt)
parts.append(rbox("buckle", 0.26, 0.08, 0.3, (0, YC - 0.6, 2.49), gold, 0.03, seg=1))
for sx in (-1, 1):
    parts.append(rbox("tasset", 0.62, 0.5, 0.1, (sx * 0.42, YC - 0.72, 2.42), steel, 0.04,
                      rot=(math.radians(-8), 0, 0), seg=1))
ridge = lathe("ridge", [(0.99, 3.42), (1.04, 3.46), (1.04, 3.54), (0.99, 3.58)], steel, seg=8, rot=math.pi / 8,
              sy=0.64)
ridge.location = (0, YC, 0)
parts.append(ridge)
fy = YC - 0.62 * 0.98 * math.cos(math.pi / 8) - 0.01
parts.append(rbox("cross_v", 0.16, 0.08, 1.2, (0, fy, 3.7), gold, 0.025, seg=1))
parts.append(rbox("cross_h", 0.72, 0.08, 0.16, (0, fy, 3.98), gold, 0.025, seg=1))

# ---- the gorget: a pale gold collar rising over the top of the breastplate -
gorget = lathe("gorget", [(0, 4.8), (0.7, 4.8), (0.76, 5.0), (0.7, 5.18), (0.56, 5.3), (0.36, 5.38), (0, 5.4)],
               pale_gold, seg=16)
gorget.location = (0, YC + 0.04, 0)
parts.append(gorget)
smooth25.append(gorget)

# ---- the divine lodestone: a faceted olive-emerald egg in a gold setting ---
A, CU, CL, Q, POINT = 0.94, 1.8, 1.2, 1.7, 0.36
TILT = math.radians(14)     # leant back a little, gazing up
C = Vector((0.0, 0.46, 6.56))  # the egg's widest ring
rng = np.random.default_rng(7)
GLINT = Vector((0.35, -0.35, 0.87)).normalized()   # high on the front, right of centre


def facet_colour(d, band, col):
    """Near-black olive, each facet a shade apart, a faint green fire in a
    few facets as in the Aramon lodestone, and a small cluster of green
    facets high on the front right."""
    base = np.array((34, 34, 15)) * (0.88 + 0.24 * rng.random())
    g = max(0.0, d.dot(GLINT))
    fire = np.array((2, 7, 1)) * rng.random() + np.array((30, 110, 12)) * max(0.0, (g - 0.9) / 0.1) ** 1.5
    if rng.random() < 0.07:
        fire = fire + np.array((6, 26, 4)) * (0.5 + rng.random())
    return base / 255, fire / 255


egg, base_px, glow_px = gem_egg("egg", A, CU, CL, Q, 16, 6, 9, POINT, facet_colour)
egg.data.materials.append(gem_mat("ara_lodestone", image("ara_lodestone_base", base_px),
                                  image("ara_lodestone_fire", glow_px)))
flat.append(egg)
setting = [egg]

# the setting: a cup under the egg with a beaded rim, six claws that follow
# the egg's curve up its sides, and a short stem down into the gorget
zr = -CL * math.sqrt(1 - (0.56 / A) ** 2)   # where the egg is 0.56 wide
cup = lathe("cup", [(0.12, -CL - 0.32), (0.3, -CL - 0.3), (0.34, -CL - 0.1), (0.5, -CL + 0.02), (0.63, zr - 0.02),
                    (0.66, zr + 0.04), (0.6, zr + 0.06), (0.46, -CL + 0.08), (0.2, -CL - 0.02), (0.12, -CL - 0.02)],
            pale_gold, seg=14)
setting.append(cup)
smooth25.append(cup)
bead = lathe("cup_bead", [(0.66 + 0.045 * math.cos(t), zr + 0.05 + 0.045 * math.sin(t))
                          for t in (2 * math.pi * k / 6 for k in range(6))], pale_gold, seg=16)
setting.append(bead)
smooth25.append(bead)
for i in range(6):
    t = 2 * math.pi * (i + 0.5) / 6
    ca, sa = math.cos(t), math.sin(t)
    path = [(0.64, zr + 0.04)] + [(A * math.cos(math.radians(p)) + 0.05, -CL * math.sin(math.radians(p)))
                                  for p in (40, 22)]
    for (ra, za), (rb, zb), (w0, w1) in zip(path, path[1:], ((0.075, 0.06), (0.06, 0.04))):
        claw = tube("claw", (ra * ca, ra * sa, za), (rb * ca, rb * sa, zb), w0, w1, pale_gold, seg=6)
        setting.append(claw)
        smooth25.append(claw)
    rb, zb = path[-1]
    tip = lathe("claw_tip", [(0, -0.07), (0.05, -0.03), (0.04, 0.03), (0, 0.08)], pale_gold, seg=6)
    tip.location = (rb * ca, rb * sa, zb)
    setting.append(tip)
bpy.context.view_layer.update()
rot = Matrix.Translation(C) @ Matrix.Rotation(-TILT, 4, "X")
for p in setting:
    p.matrix_world = rot @ p.matrix_world
parts += setting

for p in stones:
    box_uv(p)
# hk.finish joins onto the first part; it must carry the UV map or the joined
# mesh is left without an active one and every texture reads one texel
parts = stones + parts
for p in parts:
    if p in flat:
        continue
    hk.smooth(p, 25 if p in smooth25 else 35)

ob = hk.finish(parts, os.path.join(OUT, "models", NAME + ".glb"),
               {"replacesTexture": "aramanadivinelodestone", "replacesPiece": "aramana"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
hk.renders(ob, os.path.join(OUT, "renders"), NAME, PIC, HOT, scale=4)
