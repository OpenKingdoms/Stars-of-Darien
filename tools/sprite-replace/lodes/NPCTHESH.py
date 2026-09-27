"""NPCTHESH, the Thesh stand: a round stone dais of weathered, speckled
pink and grey stone. A ring of chunky cushioned blocks makes the squat
lower tier, a thin recessed shadow band carries a round slab, and the
slab's face is carved with irregular radial streaks round a domed boss.

    blender -b --factory-startup --python tools/sprite-replace/lodes/NPCTHESH.py
"""
import math
import os
import random
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import misckit as mk  # noqa: E402
from misckit import hk  # noqa: E402

NAME = "NPCTHESH"
PIC = r"D:\OKReplace\lodes\sprites\NPCTHESH.png"
HOT = (31, 45)

hk.reset()
# picture tones: pink, grey-pink, olive-grey and dark red stone, near-black
# joints and shading
PINK, GREYPINK, OLIVE, DKRED, BLACK = (136, 88, 66), (114, 98, 82), (90, 84, 56), (80, 36, 28), (18, 14, 10)
TONES = (PINK, PINK, GREYPINK, OLIVE, DKRED)
stone = mk.vmat("nt_stone", (170, 150, 136), rough=0.9)
dark = hk.pbr("nt_dark", mk.srgb(*BLACK), rough=0.95)
shadow = hk.pbr("nt_shadow", mk.srgb(44, 26, 22), rough=0.95)

parts = []
rnd = random.Random(3)

Z_BASE = 2.08  # top of the lower tier
Z_CAP0, Z_CAP1 = 2.52, 3.06  # the slab


def speckle(seed, base_tone, dark_low=None, spread=0.45):
    """Per corner speckle: each vertex a random mix of the block's tone and
    the others, a few near-black flecks, darker toward dark_low (z)."""
    r = random.Random(seed)
    cache = {}

    def grain():
        u = r.random()
        c = mk.mixc(base_tone, r.choice(TONES), r.uniform(0.0, spread))
        c = mk.mixc(c, BLACK, r.uniform(0.0, 0.3))
        if u < 0.1:
            c = mk.mixc(c, BLACK, 0.7)
        elif u > 0.94:
            c = mk.mixc(c, (178, 150, 136), 0.5)
        return c

    def fn(p, co, n, li):
        key = (round(co.x, 4), round(co.y, 4), round(co.z, 4))
        if key not in cache:
            cache[key] = grain()
        # each corner half its vertex's colour and half its own, so the
        # grain breaks at every face instead of blending into blotches
        c = mk.mixc(cache[key], grain(), 0.2)
        if dark_low is not None and co.z < dark_low:
            c = mk.mixc(c, BLACK, 0.55 * (1.0 - co.z / dark_low))
        if n.z < -0.4:
            c = mk.mixc(c, BLACK, 0.6)
        return c
    return fn


def block(a0, a1, prof, bulge=0.015, name="block"):
    """One chunky block of the lower tier: a closed (r, z) outline swept
    from a0 to a1 degrees, its face nearly flat, its side edges and top
    rounded off so the joints between blocks open deep."""
    m = len(prof)
    ztop = max(z for r, z in prof)
    zlow = min(z for r, z in prof)
    verts, faces = [], []
    ts = (0.0, 0.05, 0.15, 0.3, 0.5, 0.7, 0.85, 0.95, 1.0)
    for t in ts:
        a = math.radians(a0 + (a1 - a0) * t)
        e = abs(2 * t - 1)
        edge = max(0.0, (e - 0.55) / 0.45) ** 2
        for (r, z) in prof:
            zb = (z - zlow) / (ztop - zlow)
            rr, zz = r, z
            if r > 1.1:
                rr = r * (1.0 + bulge * (1.0 - e ** 4)) - 0.09 * edge
            zz = z - 0.12 * edge * zb ** 2 + 0.05 * edge * (1.0 - zb) ** 6
            verts.append((rr * math.cos(a), rr * math.sin(a), zz))
    steps = len(ts) - 1
    for k in range(steps):
        c0, c1 = k * m, (k + 1) * m
        for j in range(m):
            j2 = (j + 1) % m
            faces.append((c0 + j, c1 + j, c1 + j2, c0 + j2))
    faces.append(tuple(range(m - 1, -1, -1)))
    faces.append(tuple(steps * m + j for j in range(m)))
    ob = mk.mesh(name, verts, faces, mat=stone)
    mk._fix_normals(ob)
    return ob


# the lower tier: sixteen chunky blocks of varied width and height round a
# near-black core, the joints between them deep
Z_FOOT = 0.52  # the blocks sit on a dark, recessed plinth
FOOT = [(1.0, Z_FOOT), (1.44, Z_FOOT), (1.53, Z_FOOT + 0.07), (1.55, 0.8), (1.52, 1.16), (1.48, 1.46),
        (1.42, 1.68), (1.33, 1.84), (1.22, 1.95), (1.08, Z_BASE - 0.07), (1.0, Z_BASE - 0.07)]
widths = [rnd.uniform(0.8, 1.2) for _ in range(16)]
tot = sum(widths)
a = rnd.uniform(0, 20)
for k, w in enumerate(widths):
    span = 360.0 * w / tot
    gap = rnd.uniform(4.0, 6.5)
    hs = rnd.uniform(0.92, 1.06)
    prof = [(r * rnd.uniform(0.99, 1.01), Z_FOOT + (z - Z_FOOT) * hs) for r, z in FOOT]
    blk = block(a + gap / 2, a + span - gap / 2, prof, name="block%d" % k)
    mk.jitter(blk, 0.012, seed=k, axes=(1, 1, 0.5))
    mk.vpaint(blk, speckle(100 + k, rnd.choice(TONES), dark_low=0.85))
    parts.append(hk.smooth(blk, 50))
    a += span
parts.append(hk.cylinder(1.28, 1.8, seg=32, mat=dark, name="core"))
# the plinth the blocks sit on, low and in shadow
plinth = mk.lathe([(0.0, 0.0), (1.5, 0.0), (1.54, 0.06), (1.52, 0.3), (1.44, Z_FOOT + 0.02),
                   (0.0, Z_FOOT + 0.02)], seg=32, mat=stone, name="plinth")
mk.jitter(plinth, 0.012, seed=9, axes=(1, 1, 0.4))
mk.vpaint(plinth, lambda p, co, n, li: mk.mixc(DKRED, BLACK, 0.55 + 0.3 * (1.0 - co.z / Z_FOOT)))
parts.append(hk.smooth(plinth, 40))

# the neck: a plain recessed band in the slab's shadow
parts.append(hk.cylinder(1.08, Z_CAP0 - Z_BASE + 0.1, seg=32, z=Z_BASE - 0.05, mat=shadow, name="neck"))

# the slab: a round stone with a rounded lip, its side speckled like the blocks
slab = mk.lathe([(0.0, Z_CAP0), (1.1, Z_CAP0), (1.22, Z_CAP0 + 0.04), (1.29, Z_CAP0 + 0.14),
                 (1.31, Z_CAP0 + 0.3), (1.3, Z_CAP1 - 0.1), (1.26, Z_CAP1 - 0.03), (1.19, Z_CAP1)], seg=40,
                mat=stone, name="slab")
mk.jitter(slab, 0.01, seed=7, axes=(1, 1, 0.3))
mk.vpaint(slab, speckle(200, mk.mixc(PINK, GREYPINK, 0.5), dark_low=Z_CAP0 + 0.25, spread=0.6))
parts.append(hk.smooth(slab, 40))

# the face: a low dome with irregular ribs radiating from a busy boss,
# painted with the original's carving as the classic camera saw it
face = [mk.lathe([(0.0, Z_CAP1 + 0.07), (0.4, Z_CAP1 + 0.055), (0.8, Z_CAP1 + 0.03), (1.19, Z_CAP1)], seg=40,
                 mat=stone, name="face")]
ribs = 21
for k in range(ribs):
    ang = 2 * math.pi * (k + rnd.uniform(-0.3, 0.3)) / ribs
    ca, sa = math.cos(ang), math.sin(ang)
    r0, r1 = rnd.uniform(0.3, 0.5), rnd.uniform(0.92, 1.16)
    w0, w1 = rnd.uniform(0.02, 0.035), rnd.uniform(0.045, 0.085)
    h = rnd.uniform(0.012, 0.028)
    bend = rnd.uniform(-0.06, 0.06)

    def dome(r):
        return Z_CAP1 + 0.07 * (1.0 - (r / 1.19) ** 2) - 0.004

    verts = []
    for i, t in enumerate((0.0, 0.5, 1.0)):
        r = r0 + (r1 - r0) * t
        w = w0 + (w1 - w0) * t
        off = bend * math.sin(math.pi * t)
        z = dome(r)
        for (du, dz) in ((-w, 0.0), (-w * 0.45, h), (w * 0.45, h), (w, 0.0)):
            u = off + du
            verts.append((r * ca - u * sa, r * sa + u * ca, z + dz * (1.0 - 0.5 * t if i else 0.6)))
    fcs = []
    for i in range(2):
        for j in range(3):
            fcs.append((i * 4 + j, i * 4 + j + 1, (i + 1) * 4 + j + 1, (i + 1) * 4 + j))
    fcs += [(0, 1, 2, 3), (11, 10, 9, 8)]
    rb = mk.mesh("rib%d" % k, verts, fcs, mat=stone)
    mk._fix_normals(rb)
    face.append(rb)
boss = mk.lathe([(0.0, Z_CAP1 + 0.07), (0.3, Z_CAP1 + 0.07), (0.28, Z_CAP1 + 0.11), (0.2, Z_CAP1 + 0.14),
                 (0.09, Z_CAP1 + 0.155), (0.0, Z_CAP1 + 0.16)], seg=18, mat=stone, name="boss")
mk.jitter(boss, 0.012, seed=5)
face.append(boss)
for k in range(7):
    ang = 2 * math.pi * k / 7 + rnd.uniform(-0.2, 0.2)
    rr = rnd.uniform(0.3, 0.4)
    face.append(mk.ico(rnd.uniform(0.045, 0.07), 1, loc=(rr * math.cos(ang), rr * math.sin(ang), Z_CAP1 + 0.07),
                       scale=(1.2, 0.9, 0.45), mat=stone, name="knot%d" % k, seed=40 + k, rough=0.01))
top = mk.join(face, "top")
hk.project_paint(top, PIC, HOT)
# the painting times a dimming vertex colour: the picture's light is lifted
# for relighting, and this carving sits in the slab's own shade
nt = top.data.materials[0].node_tree
tex = [nd for nd in nt.nodes if nd.type == "TEX_IMAGE"][0]
ca = nt.nodes.new("ShaderNodeVertexColor")
ca.layer_name = "Col"
mx = nt.nodes.new("ShaderNodeMix")
mx.data_type, mx.blend_type = "RGBA", "MULTIPLY"
mx.inputs["Factor"].default_value = 1.0
nt.links.new(tex.outputs["Color"], mx.inputs[6])
nt.links.new(ca.outputs["Color"], mx.inputs[7])
nt.links.new(mx.outputs[2], nt.nodes["Principled BSDF"].inputs["Base Color"])
tr = random.Random(8)
at = top.data.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
for d in at.data:
    g = tr.uniform(0.52, 0.66)
    d.color = (g, g * 0.97, g * 0.95, 1.0)
# first, so the joined mesh keeps the painted face's UV map
parts.insert(0, hk.smooth(top, 45))

mk.tidy(parts)
mk.whiten(parts)
parts[0].name = NAME  # the joined object, and the glTF node, take this name
ob = hk.finish(parts, r"D:\OKReplace\lodes\hand\models\NPCTHESH.glb",
               {"replacesTexture": "theshstand", "replacesPiece": "base"})
print("MISCKIT_TRIS", NAME, mk.tris(ob))
# plain display transform, as for the other misc models
bpy.context.scene.view_settings.view_transform = "Standard"
hk.renders(ob, r"D:\OKReplace\lodes\hand\renders", NAME, PIC, HOT, scale=4)
