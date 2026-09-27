"""Aramon lodestone: a light grey hexagonal stone plinth with a thin gold
trim on its cornice, a carved gold panel on each face, a row of amethysts
round its foot, and a tall faceted emerald seated in a slim gold collar.

    blender -b --factory-startup --python tools/sprite-replace/lodes/ARALODE.py
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import handkit as hk  # noqa: E402

NAME = "ARALODE"
PIC = r"D:\OKReplace\lodes\sprites\ARALODE.png"
HOT = (27, 39)
OUT = r"D:\OKReplace\lodes\hand"


def lin(rgb):
    """sRGB 0-255 to linear 0-1, for colours sampled from the picture."""
    return tuple(((c / 255) / 12.92) if c <= 10 else (((c / 255) + 0.055) / 1.055) ** 2.4 for c in rgb)


def lathe_bm(prof, seg=6, rot=0.0, tiles=False):
    """A bmesh solid of revolution with seg flat sides from (r, z) pairs that
    start and end on the axis, or a closed loop (a ring). Each facet gets u
    across it from 0 to 1 and v up the whole piece; with tiles, facet i takes
    the i-th of seg side-by-side tiles instead."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = []
    for r, z in prof:
        if r < 1e-6:
            rings.append([bm.verts.new((0.0, 0.0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(rot + 2 * math.pi * i / seg),
                                        r * math.sin(rot + 2 * math.pi * i / seg), z)) for i in range(seg)])
    z0, z1 = min(z for _, z in prof), max(z for _, z in prof)
    closed = len(rings[0]) > 1 and len(rings[-1]) > 1
    n = len(rings)
    for k in range(n if closed else n - 1):
        a, b = rings[k], rings[(k + 1) % n]
        for i in range(seg):
            j = (i + 1) % seg
            if len(a) == 1:
                vs, us = (a[0], b[i], b[j]), (0.5, 0.0, 1.0)
            elif len(b) == 1:
                vs, us = (a[i], a[j], b[0]), (0.0, 1.0, 0.5)
            else:
                vs, us = (a[i], a[j], b[j], b[i]), (0.0, 1.0, 1.0, 0.0)
            f = bm.faces.new(vs)
            for lp, u in zip(f.loops, us):
                u = (i + u) / seg if tiles else u
                lp[uvl].uv = (u, (lp.vert.co.z - z0) / max(1e-6, z1 - z0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def lathe(name, prof, mat, seg=6, rot=0.0):
    return hk._object(name, lathe_bm(prof, seg, rot), mat)


def box_uv(ob, k=0.8):
    """World-scale box mapping, so a tiling stone texture keeps its grain size."""
    me = ob.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    mw = ob.matrix_world
    for p in me.polygons:
        n = (mw.to_3x3() @ p.normal).normalized()
        for li in p.loop_indices:
            co = mw @ me.vertices[me.loops[li].vertex_index].co
            if abs(n.z) > 0.7:
                uv.data[li].uv = (co.x * k, co.y * k)
            else:
                t = (-n.y, n.x)
                d = math.hypot(*t) or 1.0
                uv.data[li].uv = ((co.x * t[0] + co.y * t[1]) / d * k, co.z * k)


def image(name, rgb):
    """A packed image from an (h, w, 3) array of sRGB values in 0-1."""
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


def textured(name, base_img=None, base_rgb=None, rough=0.8, metal=0.0, emit_img=None, strength=0.0,
             ior=1.5, spec=0.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    b.inputs["IOR"].default_value = ior
    b.inputs["Specular IOR Level"].default_value = spec
    if base_img is not None:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = base_img
        nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    else:
        b.inputs["Base Color"].default_value = (*base_rgb, 1.0)
    if emit_img is not None:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = emit_img
        t.extension = "EXTEND"
        nt.links.new(t.outputs["Color"], b.inputs["Emission Color"])
        b.inputs["Emission Strength"].default_value = strength
    return m


def stone_img(name, rgb, seed):
    n = noise(128, 2.1, seed)
    fine = noise(128, 0.8, seed + 7)
    v = 0.84 + 0.22 * n + 0.08 * (fine - 0.5)
    base = np.array(rgb, np.float32) / 255
    return image(name, v[..., None] * base[None, None, :])


def crystal_glow(name, facets, n=48, h=64, seed=4):
    """The emerald's inner light, one tile per facet: each facet has its own
    colour at its foot and at its tip, like the fire in a cut stone, so the
    facets read apart under any light. A facet may carry a rim colour that
    it takes on toward its far edge. A faint lift at the facet edges and a
    soft mottle keep it from looking like paint."""
    v = np.linspace(0, 1, h)[:, None, None]
    u = np.linspace(0, 1, n)[None, :, None]
    mottle = noise(128, 1.6, seed)
    tiles = []
    fine = noise(128, 0.4, seed + 3)
    rs = np.random.default_rng(seed + 5).random(64)
    stria = np.interp(np.arange(256) / 4.0, np.arange(64), rs)
    for i, (lo, hi, v0, v1, rim) in enumerate(facets):
        t = np.clip((v - v0) / (v1 - v0), 0, 1)
        t = t * t * (3 - 2 * t)
        c = (np.array(lo, np.float32) * (1 - t) + np.array(hi, np.float32) * t) / 255
        # the light gathers at the point, seen from any side
        tip = np.clip((v - 0.84) / 0.16, 0, 1) ** 1.5 * 0.55
        c = c * (1 - tip) + np.array((96, 210, 84), np.float32) / 255 * tip
        if rim is not None:
            w = np.clip((u - 0.55) / 0.45, 0, 1) ** 1.5
            c = c * (1 - w) + (np.array(rim, np.float32) / 255) * w
        edge = 1.0 + 0.25 * np.abs(2 * u - 1) ** 6
        m = 0.85 + 0.3 * mottle[:h, i * 16:i * 16 + n, None]
        # faint striations running up the stone, leaning a little
        cols = (np.arange(n)[None, :] + (np.arange(h)[:, None] * 0.2).astype(int) + 40 * i) % 256
        m = m * (0.88 + 0.24 * stria[cols])[..., None]
        # sparse green flecks deep in the stone
        fl = np.clip((fine[:h, i * 16:i * 16 + n, None] - 0.78) / 0.22, 0, 1)
        tiles.append(c * edge * m + fl * np.array((24, 80, 26), np.float32) / 255)
    return image(name, np.concatenate(tiles, axis=1))


def crystal(name, prof, body, edge, width=0.012, seg=6, rot=0.0):
    """A faceted spire: flat facets in the body material, and every facet
    edge chamfered into a thin face of the edge material that catches light."""
    bm = lathe_bm(prof, seg, rot, tiles=True)
    zb, zt = min(z for _, z in prof), max(z for _, z in prof)
    # the seat ring sits in the collar and stays sharp, and so does the
    # point, which a chamfer would blunt
    edges = [e for e in bm.edges
             if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(6)
             and min(v.co.z for v in e.verts) > zb + 1e-4 and max(v.co.z for v in e.verts) < zt - 1e-4]
    bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type="OFFSET", segments=1, profile=0.5,
                    affect="EDGES", clamp_overlap=True, material=1)
    ob = hk._object(name, bm, body)
    ob.data.materials.append(edge)
    return ob


hk.reset()
stone = textured("ara_stone", stone_img("ara_stone_tex", (166, 160, 140), 3), rough=0.8)
stone_dark = textured("ara_stone_dark", stone_img("ara_stone_dark_tex", (124, 120, 108), 5), rough=0.88)
gold = hk.pbr("ara_gold", lin((200, 160, 80)), rough=0.3, metal=1.0)
gold_dark = hk.pbr("ara_gold_dark", lin((176, 136, 64)), rough=0.5, metal=0.55)
amethyst = hk.pbr("ara_amethyst", lin((104, 56, 150)), rough=0.1, emit=lin((90, 40, 140)), strength=0.3)
# facets counted from the back right round to the front right: the front
# left one is the lit facet, the front right is the shadow with a cool rim
FIRE = [
    ((4, 14, 8), (30, 90, 40), 0.2, 1.0, None),            # back right
    ((3, 10, 6), (10, 34, 16), 0.3, 1.0, None),            # back
    ((8, 28, 10), (60, 150, 56), 0.25, 0.9, None),         # back left
    ((10, 34, 12), (120, 235, 90), 0.3, 0.72, None),       # front left, the lit facet
    ((6, 22, 10), (50, 126, 50), 0.25, 1.0, None),         # front
    ((3, 12, 12), (14, 50, 46), 0.3, 1.0, (8, 26, 56)),    # front right, shadow with a cool rim
]
emerald = textured("ara_emerald", base_rgb=lin((14, 54, 26)), rough=0.08, ior=1.6, spec=0.6,
                   emit_img=crystal_glow("ara_emerald_fire", FIRE), strength=1.0)
# the facet edges catch the light, paler and brighter toward the point
ramp = np.linspace(0, 1, 64)[:, None, None] ** 1.5
edge_img = image("ara_emerald_edge_glow", np.broadcast_to(
    (np.array((70, 150, 90)) * (1 - ramp) + np.array((210, 255, 200)) * ramp) / 255, (64, 4, 3)).copy())
emerald_edge = textured("ara_emerald_edge", base_rgb=lin((110, 200, 130)), rough=0.1, emit_img=edge_img,
                        strength=0.45)

parts = []
stones = []
R = 1.19        # the plinth's corner radius, from the picture's width
APO = math.cos(math.pi / 6)
ZT = 1.02       # the plinth's top face

# plinth: foot course, recessed stone body, stone cornice with a gold bead
# round its top edge, a low stone step round the crystal's seat
foot = lathe("foot", [(0, 0), (R, 0), (R, 0.2), (R - 0.05, 0.25), (0, 0.25)], stone_dark)
body = lathe("body", [(0, 0.23), (R - 0.07, 0.23), (R - 0.07, 0.86), (0, 0.86)], stone)
cornice = lathe("cornice", [(0, 0.84), (R - 0.07, 0.84), (R, 0.9), (R, ZT - 0.02), (R - 0.03, ZT), (0, ZT)], stone)
step = lathe("step", [(0.5, ZT - 0.02), (0.92, ZT - 0.02), (0.92, ZT + 0.02), (0.86, ZT + 0.06), (0.5, ZT + 0.06)],
             stone)
for p, w in ((foot, 0.03), (body, 0.03), (cornice, 0.025), (step, 0.02)):
    hk.bevel(p, w, 2)
parts += [foot, body, cornice, step]
stones += [foot, body, cornice, step]

# the gold bead on the cornice's top edge: a rounded profile so some of it
# always faces the light
bead = [(R - 0.1, ZT - 0.012), (R - 0.02, ZT - 0.012)]
bead += [(R - 0.02 + 0.03 * math.cos(t), ZT + 0.018 + 0.03 * math.sin(t))
         for t in (math.radians(a) for a in (-60, -20, 20, 60, 100))]
bead += [(R - 0.1, ZT + 0.03)]
trim = lathe("trim", bead, gold)
parts.append(trim)

# the emerald: a hexagonal spire with a blunt point and gently bowed sides,
# seated in a slim gold collar
spike = crystal("crystal", [(0, 0.96), (0.72, 0.96), (0.745, 1.22), (0.67, 1.65), (0.56, 2.25), (0.44, 2.85),
                            (0.325, 3.35), (0.225, 3.78), (0.14, 4.12), (0, 4.47)], emerald, emerald_edge)
parts.append(spike)
collar = [(0.72, ZT + 0.02), (0.84, ZT + 0.02)]
collar += [(0.81 + 0.035 * math.cos(t), ZT + 0.09 + 0.05 * math.sin(t))
           for t in (math.radians(a) for a in (-50, -10, 30, 70))]
collar += [(0.76, ZT + 0.16), (0.73, ZT + 0.13)]
ring = lathe("collar", collar, gold)
parts.append(ring)

# a carved gold panel in the middle of each face: a bevelled frame with a
# lozenge boss and two smaller lozenges, grey stone either side
for k in range(6):
    a = math.pi / 6 + k * math.pi / 3
    nx, ny = math.cos(a), math.sin(a)
    ap = (R - 0.07) * APO

    def on_face(ob, s, off, z, rx=0.0):
        ob.rotation_euler = (rx, 0.0, a - math.pi / 2)
        ob.location = (nx * (ap + off) - ny * s, ny * (ap + off) + nx * s, z)
        return ob

    field = hk.box(0.64, 0.03, 0.36, z=-0.18, mat=gold_dark, name="field")
    parts.append(on_face(field, 0.0, 0.0, 0.55))
    for s, z, w, h in ((0.0, 0.73, 0.7, 0.05), (0.0, 0.37, 0.7, 0.05), (-0.33, 0.55, 0.05, 0.41),
                       (0.33, 0.55, 0.05, 0.41)):
        bar = hk.box(w, 0.05, h, z=-h / 2, mat=gold, name="frame")
        hk.bevel(bar, 0.012, 1)
        parts.append(on_face(bar, s, 0.015, z))
    for s, size in ((0.0, 0.2), (-0.18, 0.11), (0.18, 0.11)):
        boss = hk.box(size, 0.07, size, z=-size / 2, mat=gold, name="boss")
        hk.bevel(boss, 0.015, 1)
        on_face(boss, s, 0.02, 0.55)
        boss.rotation_euler = (0.0, math.pi / 4, a - math.pi / 2)
        parts.append(boss)
    # amethysts set into the foot course
    for s in (-0.36, -0.18, 0.0, 0.18, 0.36):
        gem = lathe("gem", [(0, -0.05), (0.06, 0.0), (0.045, 0.035), (0, 0.045)], amethyst, seg=6)
        gem.rotation_euler = (math.pi / 2, 0.0, a + math.pi / 2)
        gem.location = (nx * (R * APO - 0.01) - ny * s, ny * (R * APO - 0.01) + nx * s, 0.11)
        parts.append(gem)

for p in stones:
    box_uv(p)
for p in parts:
    if p is not spike:  # the crystal stays flat shaded, every facet crisp
        hk.smooth(p, 35)
hk.smooth(trim, 80)
hk.smooth(ring, 80)

ob = hk.finish(parts, os.path.join(OUT, "models", NAME + ".glb"),
               {"replacesTexture": "araplainlode", "replacesPiece": "aralode"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
hk.renders(ob, os.path.join(OUT, "renders"), NAME, PIC, HOT, scale=4)
