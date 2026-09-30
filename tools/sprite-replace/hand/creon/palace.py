"""CreBuild01, Creon's palace, run inside Blender. Read off its drawing
(hotspot 150, 169; a point (x, y, z) lands at column 150 + 16 x and row
169 - 16 y - 8 z):

- a marble terrace 4.4 cells high over x -6.6 to 8.4 and y -5.8 to 4.5,
  its front wall ribbed with pilasters, a checkered pink and white border
  round its top and a low crenellated parapet on its rim;
- a slender round tower with a green onion dome at each corner, its
  dome's top about 8 cells up;
- a wide flight of steps down its west side;
- at its heart, round (2.8, -0.4), octagons stepping in over the terrace,
  each with a ring of pointed gables, a drum, and a green octagonal spire whose
  gold finial ends 20.7 cells up, as the feature's height says;
- two slim octagonal towers with big green onion domes on the west side
  of the spire, at (-1.6, -2.5) and (-1.6, 1.9).

Every texture is made here: marble, tiles, the domes' copper ribs.
"""
import math
import zlib

import bmesh
import bpy
import numpy as np

import kit

MARBLE = (0.86, 0.83, 0.78)
TILE = (0.9, 0.8, 0.78)
COPPER = (0.2, 0.44, 0.37)
GOLD = (0.8, 0.62, 0.26)

X0, X1, Y0, Y1, H = -6.6, 8.4, -5.8, 4.5, 4.4
SPIRE = (2.8, -0.4)
ONIONS = ((-1.6, -2.5), (-1.6, 1.9))


# ---------------------------------------------------------------- textures

def tex_marble(seed, size=256):
    rng = np.random.default_rng(seed)
    base = 0.86 + 0.08 * kit.fbm(rng, size, [(2.0, 0.5), (8.0, 0.5)])
    v = kit.fbm(rng, size, [(6.0, 1.0), (20.0, 0.6)])
    veins = np.clip(1 - np.abs(v - 0.5) * 30, 0, 1)
    g = np.clip(base - 0.12 * veins, 0.5, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def tex_tiles(size=128, n=2):
    """A checker of n by n squares a tile, pale and paler, with thin joints."""
    u = (np.arange(size) + 0.5) / size * n
    a = (np.floor(u)[None, :] + np.floor(u)[:, None]) % 2
    g = np.where(a > 0, 0.8, 1.0)
    fu = u % 1
    joint = (np.minimum(fu, 1 - fu)[None, :] < 0.04) | (np.minimum(fu, 1 - fu)[:, None] < 0.04)
    g = np.where(joint, 0.7, g)
    return np.dstack([g, g, g, np.ones_like(g)])


def tex_ribs(seed, size=256, ribs=16):
    """Copper plates round a dome: u runs round it, ribs lit on one side."""
    rng = np.random.default_rng(seed)
    u = (np.arange(size) + 0.5) / size * ribs
    f = u % 1
    rib = 0.75 + 0.3 * np.sin(math.pi * f) - 0.25 * (f > 0.93)
    g = rib[None, :] * (0.9 + 0.1 * kit.fbm(rng, size, [(3.0, 1.0)]))
    return np.dstack([g, g, g, np.ones_like(g)])


# ---------------------------------------------------------------- shapes

def lathe(profile, seg, cx, cy, z0, name, cap=True):
    """A surface of revolution round (cx, cy) from rows of (r, z) bottom up;
    u runs round it, v up it."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = []
    for r, z in profile:
        rings.append([bm.verts.new((cx + r * math.cos(2 * math.pi * k / seg), cy + r * math.sin(2 * math.pi * k / seg), z0 + z))
                      for k in range(seg)])
    nz = len(profile)
    for i in range(nz - 1):
        for k in range(seg):
            k1 = (k + 1) % seg
            f = bm.faces.new((rings[i][k], rings[i][k1], rings[i + 1][k1], rings[i + 1][k]))
            for l, (uu, vv) in zip(f.loops, ((k, i), (k + 1, i), (k + 1, i + 1), (k, i + 1))):
                l[uvl].uv = (uu / seg, vv / (nz - 1))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        if profile[-1][0] > 1e-3:
            bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def prism(x0, x1, y0, y1, z0, z1):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x0 + (v.co.x + 0.5) * (x1 - x0)
        v.co.y = y0 + (v.co.y + 0.5) * (y1 - y0)
        v.co.z = z0 + (v.co.z + 0.5) * (z1 - z0)
    return bm


def octagon(cx, cy, r, z0, z1):
    return lathe([(r, 0.0), (r, z1 - z0)], 8, cx, cy, z0, "oct")


def onion(cx, cy, R, z0, H):
    """A bulb swelling out past its drum and drawn to a point."""
    prof = [(0.55, 0.0), (0.85, 0.12), (1.0, 0.3), (0.97, 0.45), (0.8, 0.62), (0.5, 0.78), (0.25, 0.9), (0.08, 0.98), (0.0, 1.0)]
    return lathe([(R * r, H * z) for r, z in prof], 24, cx, cy, z0, "onion")


def finial(cx, cy, z0, s):
    """A gold neck, knob and spike, s the knob's radius."""
    return lathe([(0.35 * s, 0), (0.35 * s, 0.6 * s), (s, 1.1 * s), (0.9 * s, 1.6 * s), (0.25 * s, 2.0 * s),
                  (0.12 * s, 2.3 * s), (0.02, 4.5 * s), (0.0, 4.6 * s)], 12, cx, cy, z0, "finial")


def gable(cx, cy, ang, r, z0, w, h, d):
    """A pointed gable (kokoshnik) facing out at angle ang on a ring r."""
    bm = bmesh.new()
    ca, sa = math.cos(ang), math.sin(ang)
    tx, ty = -sa, ca
    pts = []
    for off in (0.0, -d):
        base = (cx + ca * (r + off), cy + sa * (r + off))
        pts.append([bm.verts.new((base[0] + tx * u, base[1] + ty * u, z0 + z)) for u, z in
                    ((-w / 2, 0), (w / 2, 0), (w / 2, h * 0.55), (0.0, h), (-w / 2, h * 0.55))])
    a, b = pts
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    for k in range(5):
        k1 = (k + 1) % 5
        bm.faces.new((a[k], b[k], b[k1], a[k1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


# ---------------------------------------------------------------- build

def build(spr, spec):
    seed = zlib.crc32(spr.name.encode())
    mats = {
        "marble": kit.material("marble", kit.image("palace_marble", tex_marble(seed % 1000)), rough=0.5),
        "tiles": kit.material("tiles", kit.image("palace_tiles", tex_tiles()), rough=0.4),
        "copper": kit.material("copper", kit.image("palace_copper", tex_ribs(seed % 1000)), rough=0.45),
        "gold": kit.material("gold", None, rough=0.3),
    }
    for m, metal in (("gold", 0.8), ("copper", 0.3)):
        mats[m].node_tree.nodes["Principled BSDF"].inputs["Metallic"].default_value = metal
    colours = {"marble": MARBLE, "tiles": TILE, "copper": COPPER, "gold": GOLD}
    parts = []

    def add(bm, mat, uv=None):
        me = bpy.data.meshes.new("palace_" + mat)
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(me.name, me)
        bpy.context.collection.objects.link(ob)
        me.materials.append(mats[mat])
        if uv is not None:
            kit.box_uv(ob, uv)
        lin = kit.srgb_to_lin(colours[mat])
        kit.paint_points(ob, lambda co, n: lin)
        parts.append(ob)
        return ob

    # the terrace, its front wall ribbed with pilasters
    add(prism(X0, X1, Y0, Y1, 0.0, H), "marble", 0.4)
    n = 11
    for i in range(n):
        x = X0 + 0.8 + i * (X1 - X0 - 1.6) / (n - 1)
        add(prism(x - 0.18, x + 0.18, Y0 - 0.18, Y0, 0.25, H - 0.4), "marble", 0.4)
    add(prism(X0 - 0.12, X1 + 0.12, Y0 - 0.25, Y0 + 0.1, H - 0.45, H - 0.25), "marble", 0.4)
    # the checkered border round its top
    b = 1.0
    for x0, x1, y0, y1 in ((X0, X1, Y0, Y0 + b), (X0, X1, Y1 - b, Y1), (X0, X0 + b, Y0 + b, Y1 - b), (X1 - b, X1, Y0 + b, Y1 - b)):
        add(prism(x0, x1, y0, y1, H, H + 0.02), "tiles", 1.0)
    # the parapet, crenellated
    t, hp = 0.22, 0.35
    for x0, x1, y0, y1 in ((X0, X1, Y0, Y0 + t), (X0, X1, Y1 - t, Y1), (X0, X0 + t, Y0, Y1), (X1 - t, X1, Y0, Y1)):
        add(prism(x0, x1, y0, y1, H, H + hp), "marble", 0.4)
    step = 0.7
    for x in np.arange(X0 + 0.4, X1 - 0.2, step):
        add(prism(x - 0.15, x + 0.15, Y0, Y0 + t, H + hp, H + hp + 0.25), "marble", 0.4)
        add(prism(x - 0.15, x + 0.15, Y1 - t, Y1, H + hp, H + hp + 0.25), "marble", 0.4)
    # the steps down the west side
    s_y0, s_y1, run = -3.6, 1.1, 2.8
    k = 14
    for i in range(k):
        z = H * (k - i) / k
        add(prism(X0 - run * (i + 1) / k, X0 - run * i / k + 0.01, s_y0, s_y1, 0.0, z), "marble", 0.4)
    for y in (s_y0 - 0.25, s_y1):
        add(prism(X0 - run, X0, y, y + 0.25, 0.0, 0.5), "marble", 0.4)
    # the corner towers
    for cx, cy in ((X0 - 0.3, Y0), (X1 + 0.3, Y0), (X0 - 0.3, Y1), (X1 + 0.3, Y1)):
        add(lathe([(0.62, 0), (0.62, H + 1.5), (0.72, H + 1.65), (0.72, H + 1.8), (0.5, H + 1.8)], 16, cx, cy, 0.0, "tower"), "marble", 0.5)
        add(onion(cx, cy, 0.62, H + 1.8, 1.2), "copper")
        add(finial(cx, cy, H + 1.8 + 1.15, 0.09), "gold")
    # the heart: octagons stepping in, a ring of gables, drum, spire, finial
    sx, sy = SPIRE
    add(octagon(sx, sy, 3.6, H, H + 1.8), "marble", 0.5)
    add(octagon(sx, sy, 3.75, H + 1.8, H + 2.0), "marble", 0.5)
    add(octagon(sx, sy, 2.7, H + 2.0, H + 4.2), "marble", 0.5)
    for i in range(8):
        add(gable(sx, sy, math.pi / 8 + i * math.pi / 4, 2.7, H + 3.1, 1.6, 1.5, 0.25), "marble", 0.5)
        add(gable(sx, sy, math.pi / 8 + i * math.pi / 4, 3.6, H + 0.9, 2.2, 1.3, 0.25), "marble", 0.5)
    add(octagon(sx, sy, 2.0, H + 4.2, H + 5.4), "marble", 0.5)
    add(octagon(sx, sy, 2.15, H + 5.4, H + 5.6), "gold")
    add(lathe([(2.0, 0.0), (0.2, 6.3), (0.0, 6.5)], 8, sx, sy, H + 5.6, "spire"), "copper")
    add(finial(sx, sy, H + 11.7, 0.42), "gold")
    # the long gold spike over it, to the drawing's top
    add(lathe([(0.07, 0.0), (0.05, 2.0), (0.0, 2.7)], 8, sx, sy, H + 13.6, "spike"), "gold")
    # the onion towers west of it
    for cx, cy in ONIONS:
        add(octagon(cx, cy, 1.15, H, H + 3.6), "marble", 0.5)
        for i in range(8):
            add(gable(cx, cy, math.pi / 8 + i * math.pi / 4, 1.15, H + 2.6, 0.8, 0.9, 0.15), "marble", 0.5)
        add(octagon(cx, cy, 0.85, H + 3.6, H + 4.4), "marble", 0.5)
        add(octagon(cx, cy, 0.9, H + 4.4, H + 4.6), "gold")
        add(onion(cx, cy, 1.25, H + 4.6, 2.3), "copper")
        add(finial(cx, cy, H + 4.6 + 2.25, 0.18), "gold")
    return parts, {"parts": len(parts)}
