"""The Volcano sacred stones (CReMana01, CREMana02, CREMana03), run inside
Blender: a round basalt floor laid flat on the ground, two raised rings
of stone with a crack of glowing lava between them, and at the heart an
eight-pointed star of lava round a white-hot core, where the drawing has
its pale star. Sized to the drawn disc: flat on the ground, a circle
there is a circle to the classic camera.
"""
import math
import zlib

import bmesh
import bpy
import numpy as np

import kit

BASALT = (0.25, 0.23, 0.21)      # sRGB
LAVA = (1.0, 0.22, 0.02)         # linear glow
CORE = (1.0, 0.55, 0.12)


def _mesh(name, bm, mat, rgb):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    me.materials.append(mat)
    lin = kit.srgb_to_lin(rgb)
    kit.paint_points(ob, lambda co, n: lin)
    return ob


def annulus(r0, r1, h, z0=0.0, seg=64, lift=0.0):
    """A flat-topped ring from r0 to r1, h tall from z0, its top rounded up
    by lift at the middle of its width."""
    bm = bmesh.new()
    rings = []
    for (r, z) in ((r0, z0), (r0, z0 + h), ((r0 + r1) / 2, z0 + h + lift), (r1, z0 + h), (r1, z0)):
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / seg), r * math.sin(2 * math.pi * k / seg), z))
                      for k in range(seg)])
    for a, b in zip(rings, rings[1:]):
        for k in range(seg):
            k1 = (k + 1) % seg
            bm.faces.new((a[k], a[k1], b[k1], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def disc(r, h, seg=64, z0=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r * 0.97, depth=h)
    for v in bm.verts:
        v.co.z += z0 + h / 2
    return bm


def star(r_in, r_out, h, points=8, z0=0.0, twist=0.0):
    """An eight-pointed star plate, points at r_out, notches at r_in."""
    bm = bmesh.new()
    top, bot = [], []
    for k in range(points * 2):
        a = math.pi * k / points + twist
        r = r_out if k % 2 == 0 else r_in
        top.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z0 + h)))
        bot.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z0)))
    ct = bm.verts.new((0, 0, z0 + h * 1.4))
    n = len(top)
    for k in range(n):
        k1 = (k + 1) % n
        bm.faces.new((top[k], top[k1], ct))
        bm.faces.new((bot[k], bot[k1], top[k1], top[k]))
    bm.faces.new(list(reversed(bot)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def build(spr, spec):
    seed = zlib.crc32(spr.name.encode())
    ys, xs = np.nonzero(spr.mask)
    R = ((xs.max() - xs.min() + 1) + (ys.max() - ys.min() + 1)) / 4 / kit.CELL
    cx = ((xs.max() + xs.min() + 1) / 2 - spr.hx) / kit.CELL
    cy = (spr.hy - (ys.max() + ys.min() + 1) / 2) / kit.CELL
    stone = kit.material("basalt", kit.image(spr.name + "_basalt", kit.tex_stone("basalt", seed % 1000)), rough=0.9)
    lava = kit.material("lava", None, rough=0.6, emit=LAVA, strength=spec.get("glow", 1.6))
    core = kit.material("core", None, rough=0.4, emit=CORE, strength=spec.get("core", 3.0))
    parts = [
        _mesh("floor", disc(R, 0.12), stone, BASALT),
        _mesh("ring_out", annulus(0.74 * R, 0.96 * R, 0.12, 0.12, lift=0.05), stone, (0.32, 0.29, 0.26)),
        _mesh("ring_in", annulus(0.46 * R, 0.62 * R, 0.1, 0.12, lift=0.04), stone, (0.3, 0.27, 0.24)),
        _mesh("crack", annulus(0.64 * R, 0.72 * R, 0.02, 0.12), lava, (0.9, 0.35, 0.08)),
        _mesh("star", star(0.13 * R, 0.42 * R, 0.08, z0=0.12, twist=math.pi / 8), lava, (0.95, 0.4, 0.08)),
        _mesh("core", disc(0.1 * R, 0.1, 24, z0=0.16), core, (1.0, 0.75, 0.35)),
    ]
    for ob in parts:
        for v in ob.data.vertices:
            v.co.x += cx
            v.co.y += cy
    kit.box_uv(parts[0], 0.5)
    kit.box_uv(parts[1], 0.5)
    kit.box_uv(parts[2], 0.5)
    return parts, {"radius": round(R, 2)}
