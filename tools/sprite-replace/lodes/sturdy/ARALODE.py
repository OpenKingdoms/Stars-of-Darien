"""Aramon lodestone: a light grey hexagonal stone plinth with a thin gold
trim on its cornice, a carved gold panel on each face, a row of amethysts
round its foot, and a tall faceted emerald seated in a slim gold collar.
The sturdy cut: wider and deeper, the emerald lower and broader, the foot
course flared out to ground it.

    blender -b --factory-startup --python tools/sprite-replace/lodes/sturdy/ARALODE.py
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
import handkit as hk  # noqa: E402

NAME = "ARALODE"
PIC = r"D:\OKReplace\lodes\sprites\ARALODE.png"
HOT = (27, 39)
OUT = r"D:\OKReplace\lodes\sturdy"
GIRTH = 1.17    # width and depth over the approved fit
GEM_W = 1.2     # the emerald's girth
GEM_H = 0.84    # and its height
LIFT = 0.06     # a taller plinth


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
    img = hk.generated(bpy.data.images.new(name, w, h, alpha=False))
    img["okGenerated"] = True  # made here, so it ships (okpaint.py)
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


def polish(m, coat=0.0, coat_rough=0.03, spec=None, rough=None):
    """A clear lacquer over a material, and optionally a stronger specular."""
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Coat Weight"].default_value = coat
    b.inputs["Coat Roughness"].default_value = coat_rough
    if spec is not None:
        b.inputs["Specular IOR Level"].default_value = spec
    if rough is not None:
        b.inputs["Roughness"].default_value = rough
    return m


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


def stone_img(name, rgb, seed, lift=1.0):
    n = noise(128, 2.1, seed)
    fine = noise(128, 0.8, seed + 7)
    v = (0.84 + 0.22 * n + 0.08 * (fine - 0.5)) * lift
    base = np.array(rgb, np.float32) / 255
    return image(name, v[..., None] * base[None, None, :])


def crystal_glow(name, facets, prof, cuts=(), glints=(), sky=(), gain=1.0, n=96, h=128, seed=4):
    """The emerald's inner light, one tile per facet: each facet has its own
    colour at its foot and at its tip, like the fire in a cut stone, so the
    facets read apart under any light. A facet may carry a rim colour that
    it takes on toward its far edge. A faint lift at the facet edges and a
    soft mottle keep it from looking like paint. Cuts split a facet along a
    line from u0 at its foot to u1 at its tip into two tones, the facets
    behind seen through the stone, (facet, u0, u1, left, right). Glints are
    sharp-sided streaks of reflected light in a soft halo,
    (facet, u, half width, v0, v1, peak, rgb). Sky is the bright sky seen in
    a polished facet above a crisp horizon running from v0 at its near edge to
    v1 at its far edge, (facet, v0, v1, amount, rgb). A tile is the facet laid
    flat (see crystal), so u is the fraction across the facet at each height."""
    zs = [z for _, z in prof[1:]]
    rs = [r for r, _ in prof[1:]]
    rmax, zb, zt = max(rs), prof[0][1], prof[-1][1]
    v = ((np.arange(h) + 0.5) / h)[:, None, None]
    x = ((np.arange(n) + 0.5) / n - 0.5)[None, :, None] * rmax
    r = np.interp(zb + v * (zt - zb), zs, rs)
    u = np.clip(x / np.maximum(r, 1e-3) + 0.5, 0, 1)
    du = 1.5 * rmax / n / np.maximum(r, 1e-3)  # a texel and a half, across
    mottle = noise(256, 1.6, seed)
    tiles = []
    fine = noise(256, 0.4, seed + 3)
    rng = np.random.default_rng(seed + 5).random(64)
    stria = np.interp(np.arange(256) / 4.0, np.arange(64), rng)
    for i, (lo, hi, v0, v1, rim) in enumerate(facets):
        t = np.clip((v - v0) / (v1 - v0), 0, 1)
        t = t * t * (3 - 2 * t)
        c = (np.array(lo, np.float32) * (1 - t) + np.array(hi, np.float32) * t) / 255
        # the light gathers at the point, seen from any side
        tip = np.clip((v - 0.84) / 0.16, 0, 1) ** 1.5 * 0.6
        c = c * (1 - tip) + np.array((120, 235, 104), np.float32) / 255 * tip
        if rim is not None:
            w = np.clip((u - 0.55) / 0.45, 0, 1) ** 1.5
            c = c * (1 - w) + (np.array(rim, np.float32) / 255) * w
        edge = 1.0 + 0.25 * np.abs(2 * u - 1) ** 6
        m = 0.85 + 0.3 * mottle[:h, i * 32:i * 32 + n, None]
        # faint striations running up the stone, leaning a little
        cols = ((np.arange(n)[None, :] * 0.5 + np.arange(h)[:, None] * 0.1).astype(int) + 40 * i) % 256
        m = m * (0.88 + 0.24 * stria[cols])[..., None]
        # sparse green flecks deep in the stone
        fl = np.clip((fine[:h, i * 32:i * 32 + n, None] - 0.78) / 0.22, 0, 1)
        c = gain * (c * edge * m + fl * np.array((24, 80, 26), np.float32) / 255)
        for f, u0, u1, left, right in cuts:
            if f == i:
                side = np.clip((u - (u0 + (u1 - u0) * v)) / du + 0.5, 0, 1)
                c = c * (left * (1 - side) + right * side)
        for f, h0, h1, amt, rgb in sky:
            if f == i:
                above = v - (h0 + (h1 - h0) * u)
                edge = np.clip(above * h + 0.5, 0, 1)
                c = c + edge * np.clip(1 - above / 0.4, 0.3, 1) * amt * np.array(rgb, np.float32) / 255
        for f, uc, hw, g0, g1, peak, rgb in glints:
            if f != i:
                continue
            core = np.clip(1 - (np.abs(u - uc) / hw) ** 8, 0, 1)
            halo = np.exp(-((u - uc) / (3 * hw)) ** 2)
            along = np.clip((v - g0) / 0.1, 0, 1) * np.clip((g1 - v) / 0.25, 0, 1)
            # a green bloom round a pale core
            c = c + halo * along * peak * np.array((10, 60, 12), np.float32) / 255
            a = peak * core * along
            c = c * (1 - a) + np.array(rgb, np.float32) / 255 * a
        # past full brightness, scale the colour down rather than clip it
        tiles.append(c / np.maximum(1.0, c.max(axis=2, keepdims=True)))
    return image(name, np.concatenate(tiles, axis=1))


def edge_glow(name, verts, rings, h=64, w=4):
    """The chamfers' light, one column per 30 degrees round the crystal:
    even columns are the upright edges, odd ones the ring chamfers across a
    facet. Each is a (foot, tip) colour pair."""
    v = np.linspace(0, 1, h)[:, None, None] ** 1.5
    cols = []
    for j in range(12):
        lo, hi = (verts[j // 2] if j % 2 == 0 else rings[j // 2])
        c = (np.array(lo, np.float32) * (1 - v) + np.array(hi, np.float32) * v) / 255
        cols.append(np.broadcast_to(c, (h, w, 3)))
    return image(name, np.concatenate(cols, axis=1))


def crystal(name, prof, body, edge, width=0.012, seg=6, rot=0.0):
    """A faceted spire: flat facets in the body material, and every facet
    edge chamfered into a thin face of the edge material that catches light.
    Each facet's tile is the facet laid flat, u straight across it and v up,
    so a line painted on it stays straight on the facet."""
    bm = lathe_bm(prof, seg, rot, tiles=True)
    zb, zt = min(z for _, z in prof), max(z for _, z in prof)
    rmax = max(r for r, _ in prof)
    # the seat ring sits in the collar and stays sharp, and so does the
    # point, which a chamfer would blunt
    edges = [e for e in bm.edges
             if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(6)
             and min(v.co.z for v in e.verts) > zb + 1e-4 and max(v.co.z for v in e.verts) < zt - 1e-4]
    bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type="OFFSET", segments=1, profile=0.5,
                    affect="EDGES", clamp_overlap=True, material=1)
    # a chamfer reads the column of the edge texture for where it faces
    # round the crystal, a facet its own tile
    uvl = bm.loops.layers.uv["UVMap"]
    for f in bm.faces:
        c = f.calc_center_median()
        if f.material_index == 1:
            j = round(math.degrees(math.atan2(c.y, c.x) - rot) / 30) % 12
            for lp in f.loops:
                lp[uvl].uv = ((j + 0.5) / 12, (lp.vert.co.z - zb) / (zt - zb))
        else:
            i = int(((math.atan2(c.y, c.x) - rot) % (2 * math.pi)) // (2 * math.pi / seg))
            phi = rot + (i + 0.5) * 2 * math.pi / seg
            tx, ty = -math.sin(phi), math.cos(phi)
            for lp in f.loops:
                x = lp.vert.co.x * tx + lp.vert.co.y * ty
                lp[uvl].uv = ((i + 0.5 + x / rmax) / seg, (lp.vert.co.z - zb) / (zt - zb))
    ob = hk._object(name, bm, body)
    ob.data.materials.append(edge)
    return ob


hk.reset()
stone_tex = stone_img("ara_stone_tex", (166, 160, 140), 3)
stone_dark_tex = stone_img("ara_stone_dark_tex", (124, 120, 108), 5)
stone = textured("ara_stone", stone_tex, rough=0.8)
stone_dark = textured("ara_stone_dark", stone_dark_tex, rough=0.88)
# the courses' dressed tops are polished under a clear lacquer, and their
# rounded top edges, rubbed paler, catch the light
HONED = {stone: polish(textured("ara_stone_honed", stone_tex, rough=0.3, spec=1.0), coat=0.8, coat_rough=0.03),
         stone_dark: polish(textured("ara_stone_dark_honed", stone_dark_tex, rough=0.32, spec=1.0), coat=0.7,
                            coat_rough=0.04)}
ARRIS = {stone: polish(textured("ara_stone_arris", stone_img("ara_stone_arris_tex", (166, 160, 140), 3, 1.2),
                                rough=0.3, spec=1.0), coat=1.0, coat_rough=0.02),
         stone_dark: polish(textured("ara_stone_dark_arris", stone_img("ara_stone_dark_arris_tex", (124, 120, 108), 5,
                                                                       1.2), rough=0.3, spec=1.0),
                            coat=1.0, coat_rough=0.02)}
# polished gold, its rounded edges drawing a bright line of sun
gold = polish(hk.pbr("ara_gold", lin((252, 210, 112)), rough=0.24, metal=1.0), coat=1.0, coat_rough=0.15, spec=1.0)
gold_dark = hk.pbr("ara_gold_dark", lin((170, 122, 48)), rough=0.3, metal=1.0)
amethyst = polish(hk.pbr("ara_amethyst", lin((110, 58, 158)), rough=0.06, emit=lin((96, 42, 150)), strength=0.4),
                  coat=1.0, coat_rough=0.01, spec=1.0)
# facets counted from the back right round to the front right: the front
# left one is the lit facet, the front right is the shadow with a cool rim
FIRE = [
    ((8, 28, 12), (42, 118, 50), 0.2, 1.0, None),          # back right
    ((6, 22, 10), (20, 60, 28), 0.3, 1.0, None),           # back
    ((12, 46, 15), (76, 178, 64), 0.25, 0.9, None),        # back left
    ((16, 66, 20), (130, 250, 96), 0.3, 0.72, None),       # front left, the lit facet
    ((10, 42, 15), (64, 158, 60), 0.25, 1.0, None),        # front
    ((4, 22, 20), (18, 68, 60), 0.3, 1.0, (10, 34, 70)),   # front right, shadow with a cool rim
]
# the light the cut faces throw back: a broad streak down the lit facet and
# thinner ones beside it, each a near-white core in a green bloom
GLINTS = [
    (3, 0.3, 0.05, 0.08, 0.98, 1.0, (240, 255, 232)),
    (3, 0.53, 0.018, 0.3, 0.92, 0.8, (215, 255, 205)),
    (4, 0.1, 0.03, 0.18, 0.96, 0.75, (200, 255, 190)),
    (4, 0.72, 0.012, 0.45, 0.9, 0.5, (170, 245, 165)),
    (2, 0.78, 0.04, 0.4, 0.98, 0.75, (200, 255, 190)),
    (5, 0.22, 0.02, 0.3, 0.9, 0.5, (130, 220, 210)),
]
# the pale sky in the polished faces above a crisp horizon, strongest on
# the lit facet
SKY = [
    (3, 0.5, 0.58, 0.24, (150, 240, 140)),
    (4, 0.56, 0.5, 0.21, (120, 215, 130)),
    (5, 0.6, 0.55, 0.1, (60, 130, 150)),
    (2, 0.52, 0.6, 0.14, (100, 190, 110)),
]
# the facets behind, seen through the stone, split each face in two tones
CUTS = [
    (2, 0.4, 0.5, 0.85, 1.15),
    (3, 0.62, 0.5, 1.0, 0.72),
    (4, 0.45, 0.5, 1.2, 0.8),
    (5, 0.3, 0.45, 1.25, 0.9),
]
# the crystal's materials carry "crystal" in their names: the game pulses
# their glow
# the emerald: a hexagonal spire with a blunt point and gently bowed sides
CRYSTAL = [(0, 0.96), (0.72, 0.96), (0.745, 1.22), (0.67, 1.65), (0.56, 2.25), (0.44, 2.85), (0.325, 3.35),
           (0.225, 3.78), (0.14, 4.12), (0, 4.47)]
CRYSTAL = [(r * GEM_W, 0.96 + LIFT + (z - 0.96) * GEM_H) for r, z in CRYSTAL]
emerald = polish(textured("ara_crystal", base_rgb=lin((14, 54, 26)), rough=0.01, ior=1.58, spec=1.0,
                          emit_img=crystal_glow("ara_crystal_fire", FIRE, CRYSTAL, CUTS, GLINTS, SKY),
                          strength=1.9),
                 coat=1.0, coat_rough=0.0)
# the facet edges catch the light, paler and brighter toward the point, and
# brightest round the lit facet: upright edges from 0 degrees round, then
# the ring chamfers facet by facet
EDGE_VERTS = [((50, 96, 104), (140, 205, 195)), ((80, 156, 100), (185, 240, 185)), ((80, 156, 100), (185, 240, 185)),
              ((130, 230, 140), (235, 255, 228)), ((175, 250, 175), (250, 255, 245)), ((105, 200, 125), (215, 255, 205))]
EDGE_RINGS = [((60, 124, 80), (155, 222, 165)), ((60, 124, 80), (155, 222, 165)), ((105, 200, 125), (215, 255, 205)),
              ((165, 245, 165), (245, 255, 240)), ((105, 200, 125), (215, 255, 205)), ((50, 96, 104), (140, 205, 195))]
emerald_edge = polish(textured("ara_crystal_edge", base_rgb=lin((120, 210, 140)), rough=0.01, spec=1.0,
                               emit_img=edge_glow("ara_crystal_edge_glow", EDGE_VERTS, EDGE_RINGS), strength=1.6),
                      coat=1.0, coat_rough=0.0)

parts = []
stones = []
R = 1.19 * GIRTH  # the plinth's corner radius
APO = math.cos(math.pi / 6)
ZT = 1.02 + LIFT  # the plinth's top face
RF = R + 0.1    # the foot course, flared past the body

# plinth: foot course, recessed stone body, stone cornice with a gold bead
# round its top edge, a low stone step round the crystal's seat
foot = lathe("foot", [(0, 0), (RF, 0), (RF, 0.2), (R - 0.03, 0.3), (0, 0.3)], stone_dark)
body = lathe("body", [(0, 0.23), (R - 0.07, 0.23), (R - 0.07, 0.86 + LIFT), (0, 0.86 + LIFT)], stone)
cornice = lathe("cornice", [(0, 0.84 + LIFT), (R - 0.07, 0.84 + LIFT), (R, 0.9 + LIFT), (R, ZT - 0.02), (R - 0.03, ZT),
                            (0, ZT)], stone)
step = lathe("step", [(0.5 * GEM_W, ZT - 0.02), (0.92 * GEM_W, ZT - 0.02), (0.92 * GEM_W, ZT + 0.02),
                      (0.86 * GEM_W, ZT + 0.06), (0.5 * GEM_W, ZT + 0.06)], stone)
# the rounded edges are what catch the sun on the polished tops
for p, w, s in ((foot, 0.03, 3), (body, 0.03, 2), (cornice, 0.025, 2), (step, 0.022, 3)):
    hk.bevel(p, w, s)
parts += [foot, body, cornice, step]
stones += [foot, body, cornice, step]

# the gold bead on the cornice's top edge: a rounded profile so some of it
# always faces the light, in a few broad facets that each catch a band of sun
bead = [(R - 0.1, ZT - 0.012), (R - 0.02, ZT - 0.012)]
bead += [(R - 0.02 + 0.03 * math.cos(t), ZT + 0.018 + 0.03 * math.sin(t))
         for t in (math.radians(a) for a in (-60, -20, 20, 60, 100))]
bead += [(R - 0.1, ZT + 0.03)]
trim = lathe("trim", bead, gold)
parts.append(trim)

# the emerald, seated in a slim gold collar
spike = crystal("crystal", CRYSTAL, emerald, emerald_edge, width=0.015)
parts.append(spike)
collar = [(0.72 * GEM_W, ZT + 0.02), (0.84 * GEM_W, ZT + 0.02)]
collar += [(0.81 * GEM_W + 0.035 * math.cos(t), ZT + 0.09 + 0.05 * math.sin(t))
           for t in (math.radians(a) for a in (-50, -10, 30, 70))]
collar += [(0.76 * GEM_W, ZT + 0.16), (0.73 * GEM_W, ZT + 0.13)]
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

    field = hk.box(0.64 * GIRTH, 0.03, 0.36, z=-0.18, mat=gold_dark, name="field")
    parts.append(on_face(field, 0.0, 0.0, 0.55 + LIFT / 2))
    for s, z, w, h in ((0.0, 0.73, 0.7 * GIRTH, 0.05), (0.0, 0.37, 0.7 * GIRTH, 0.05),
                       (-0.33 * GIRTH, 0.55, 0.05, 0.41), (0.33 * GIRTH, 0.55, 0.05, 0.41)):
        z += LIFT / 2
        bar = hk.box(w, 0.05, h, z=-h / 2, mat=gold, name="frame")
        hk.bevel(bar, 0.015, 3)
        parts.append(on_face(bar, s, 0.015, z))
    for s, size in ((0.0, 0.2), (-0.18 * GIRTH, 0.11), (0.18 * GIRTH, 0.11)):
        boss = hk.box(size, 0.07, size, z=-size / 2, mat=gold, name="boss")
        hk.bevel(boss, 0.018, 2)
        on_face(boss, s, 0.02, 0.55 + LIFT / 2)
        boss.rotation_euler = (0.0, math.pi / 4, a - math.pi / 2)
        parts.append(boss)
    # amethysts set into the foot course
    for s in (-0.36 * GIRTH, -0.18 * GIRTH, 0.0, 0.18 * GIRTH, 0.36 * GIRTH):
        gem = lathe("gem", [(0, -0.05), (0.06, 0.0), (0.045, 0.035), (0, 0.045)], amethyst, seg=6)
        gem.rotation_euler = (math.pi / 2, 0.0, a + math.pi / 2)
        gem.location = (nx * (RF * APO - 0.01) - ny * s, ny * (RF * APO - 0.01) + nx * s, 0.11)
        parts.append(gem)

for p in stones:
    box_uv(p)
    base = p.data.materials[0]
    p.data.materials.append(HONED[base])
    p.data.materials.append(ARRIS[base])
    for f in p.data.polygons:
        if f.normal.z > 0.92:
            f.material_index = 1
        elif f.normal.z > 0.15:
            f.material_index = 2
for p in parts:
    if p is not spike:  # the crystal stays flat shaded, every facet crisp
        hk.smooth(p, 35)
hk.smooth(trim, 80)
hk.smooth(ring, 80)

ob = hk.finish(parts, os.path.join(OUT, "models", NAME + ".glb"),
               {"replacesTexture": "araplainlode", "replacesPiece": "aralode"})
print("TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))
hk.renders(ob, os.path.join(OUT, "renders"), NAME, PIC, HOT, scale=4)
