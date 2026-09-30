"""CreWell01, Creon's well, run inside Blender: a wide basin of basalt
blocks with dark water in it, under a gable roof of planks on four
posts, its ridge running north and south as the drawing's lit west slope
and shaded east slope show. Placed against the drawing's own rows: the
basin's front edge on its lowest row, the eaves on the row where the
planks stop, the ridge's far end at the top.
"""
import math
import zlib

import bmesh
import bpy
import numpy as np

import kit

WOOD = (0.42, 0.25, 0.12)
POST = (0.3, 0.2, 0.12)
STONE = (0.22, 0.21, 0.2)
WATER = (0.05, 0.12, 0.15)
LOG = (0.55, 0.45, 0.3)


def tex_wood(seed, size=256, planks=6):
    """Planks side by side along u, grain along v, dark joints between."""
    rng = np.random.default_rng(seed)
    u = (np.arange(size) + 0.5) / size
    k = np.floor(u * planks)
    shade = 0.8 + 0.2 * rng.random(planks)
    col = shade[k.astype(int)][None, :]
    # streaks blurred down the planks, over a fine speckle
    streak = kit.norm01(kit._blur_x(rng.standard_normal((size, size)).T, 8.0).T)
    grain = 0.6 * streak + 0.4 * kit.fbm(rng, size, [(0.8, 1.0)])
    joint = np.clip(1 - np.abs((u * planks) % 1 - 0.0) * 30, 0, 1) + np.clip(1 - np.abs((u * planks) % 1 - 1.0) * 30, 0, 1)
    g = np.clip(col * (0.75 + 0.3 * grain) * (1 - 0.6 * joint[None, :]), 0.2, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def _ob(name, bm, mat, rgb):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    me.materials.append(mat)
    lin = kit.srgb_to_lin(rgb)
    kit.paint_points(ob, lambda co, n: lin)
    return ob


def box(x0, x1, y0, y1, z0, z1):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x0 + (v.co.x + 0.5) * (x1 - x0)
        v.co.y = y0 + (v.co.y + 0.5) * (y1 - y0)
        v.co.z = z0 + (v.co.z + 0.5) * (z1 - z0)
    return bm


def slope(xa, xb, y0, y1, za, zb, t):
    """A roof slope from eave (xa, za) up to ridge (xb, zb), y0 to y1, t thick."""
    bm = bmesh.new()
    pts = [(xa, za), (xb, zb)]
    vs = []
    for (x, z) in pts:
        for y in (y0, y1):
            vs.append(bm.verts.new((x, y, z)))
            vs.append(bm.verts.new((x, y, z - t)))
    a0t, a0b, a1t, a1b, b0t, b0b, b1t, b1b = vs
    for f in ((a0t, b0t, b1t, a1t), (a0b, a1b, b1b, b0b), (a0t, a0b, b0b, b0t), (a1t, b1t, b1b, a1b),
              (a0t, a1t, a1b, a0b), (b0t, b0b, b1b, b1t)):
        bm.faces.new(f)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def build(spr, spec):
    seed = zlib.crc32(spr.name.encode())
    ys, xs = np.nonzero(spr.mask)
    left, right = (xs.min() - spr.hx) / kit.CELL, (xs.max() + 1 - spr.hx) / kit.CELL
    front = (spr.hy - ys.max() - 1) / kit.CELL           # the basin's front edge, on the ground
    eave_row, top_row = spec.get("eave_row", 28), ys.min()
    ridge_x = (spec.get("ridge_col", 25) - spr.hx) / kit.CELL
    z_eave, z_ridge = spec.get("z_eave", 1.05), spec.get("z_ridge", 1.75)
    # the eaves' front on eave_row, the ridge's far end on the top row
    y_front = (spr.hy - eave_row - 8 * z_eave) / kit.CELL
    y_back = (spr.hy - top_row - 8 * z_ridge) / kit.CELL
    stone = kit.material("basalt", kit.image(spr.name + "_basalt", kit.tex_stone("basalt", seed % 1000)), rough=0.9)
    wood = kit.material("planks", kit.image(spr.name + "_planks", tex_wood(seed % 1000)), rough=0.85)
    water = kit.material("water", None, rough=0.08)
    bx0, bx1 = left + 0.05, right - 0.05
    by0, by1 = front, min(y_back - 0.3, front + 1.4)
    parts = []
    # the basin: four walls of blocks round the water
    wall, hz = 0.36, 0.5
    for bm in (box(bx0, bx1, by0, by0 + wall, 0, hz), box(bx0, bx1, by1 - wall, by1, 0, hz),
               box(bx0, bx0 + wall, by0, by1, 0, hz), box(bx1 - wall, bx1, by0, by1, 0, hz)):
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.03, segments=1, affect="EDGES")
        parts.append(_ob("basin", bm, stone, STONE))
    parts.append(_ob("water", box(bx0 + wall, bx1 - wall, by0 + wall, by1 - wall, 0, hz - 0.18), water, WATER))
    # the pale log laid across the basin's front, as drawn
    lx0, lx1 = bx0 + 0.35 * (bx1 - bx0) * 0.4, bx0 + 0.62 * (bx1 - bx0)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.12, radius2=0.12, depth=lx1 - lx0)
    for v in bm.verts:
        v.co.x, v.co.z = v.co.z + (lx0 + lx1) / 2, -v.co.x + hz + 0.1
        v.co.y += by0 + wall * 0.6
    parts.append(_ob("log", bm, wood, LOG))
    # four posts under the eaves
    ex0, ex1 = left + 0.12, right - 0.12
    for x in (ex0 + 0.12, ex1 - 0.12):
        for y in (y_front + 0.15, y_back - 0.15):
            parts.append(_ob("post", box(x - 0.08, x + 0.08, y - 0.08, y + 0.08, 0, z_eave), wood, POST))
    # the gable roof: two slopes meeting on the ridge, and a ridge beam
    t = 0.1
    parts.append(_ob("roof_w", slope(ex0 - 0.1, ridge_x, y_front, y_back, z_eave, z_ridge, t), wood, WOOD))
    parts.append(_ob("roof_e", slope(ex1 + 0.1, ridge_x, y_front, y_back, z_eave, z_ridge, t), wood, WOOD))
    parts.append(_ob("ridge", box(ridge_x - 0.07, ridge_x + 0.07, y_front - 0.05, y_back + 0.05, z_ridge - 0.05, z_ridge + 0.08),
                     wood, POST))
    # the planks run down the slopes: u across, v down
    for ob in parts:
        me = ob.data
        uv = me.uv_layers.new(name="UVMap")
        for p in me.polygons:
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                n = p.normal
                if ob.name.startswith("roof"):
                    uv.data[li].uv = (co.y * 0.6, co.x * 0.6)
                elif abs(n.z) > 0.7:
                    uv.data[li].uv = (co.x * 0.5, co.y * 0.5)
                elif abs(n.y) > abs(n.x):
                    uv.data[li].uv = (co.x * 0.5, co.z * 0.5)
                else:
                    uv.data[li].uv = (co.y * 0.5, co.z * 0.5)
    return parts, {"roof": [round(y_front, 2), round(y_back, 2)], "basin": [round(by0, 2), round(by1, 2)]}
