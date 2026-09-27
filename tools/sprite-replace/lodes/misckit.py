"""Shared parts for ZONFIRE, ZONGLYPH and NPCTHESH, run inside Blender.

Frame as handkit: 1 unit = 1 cell, -Y toward the classic camera, Z up,
origin on the anchor. The picture maps a point to column hx + 16x and
row hy - 16y - 8z.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.dirname(HERE)
for p in (KIT, HERE):
    if p not in sys.path:
        sys.path.insert(0, p)
import handkit as hk  # noqa: E402


def srgb(r, g, b, gain=1.0):
    """A picture colour (0-255 sRGB) as a linear base colour, lifted by gain."""
    def one(c):
        c = min(1.0, c * gain / 255.0)
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return (one(r), one(g), one(b))


def mesh(name, verts, faces, mat=None, smooth=False):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.validate()
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    if mat is not None:
        me.materials.append(mat)
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    return ob


def from_bm(name, bm, mat=None):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    if mat is not None:
        me.materials.append(mat)
    return ob


def xform(ob, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), order="XYZ"):
    """Bakes a scale, then an Euler rotation (degrees), then a move into the mesh."""
    from mathutils import Euler
    S = Matrix.Diagonal((*scale, 1.0))
    R = Euler([math.radians(a) for a in rot], order).to_matrix().to_4x4()
    T = Matrix.Translation(loc)
    ob.data.transform(T @ R @ S)
    ob.data.update()
    return ob


def matrix(ob, M):
    ob.data.transform(M)
    ob.data.update()
    return ob


def rod(p0, p1, r0, r1=None, seg=8, mat=None, name="rod", roll=0.0):
    """A frustum from p0 to p1, radius r0 at p0 and r1 at p1 (0 makes a spike)."""
    r1 = r0 if r1 is None else r1
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    L = d.length
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r0, radius2=max(r1, 1e-4), depth=L)
    for v in bm.verts:
        v.co.z += L / 2
    ob = from_bm(name, bm, mat)
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    M = Matrix.Translation(p0) @ q.to_matrix().to_4x4() @ Matrix.Rotation(roll, 4, "Z")
    return matrix(ob, M)


def jitter(ob, amount, seed=1, axes=(1, 1, 1)):
    rnd = random.Random(seed)
    for v in ob.data.vertices:
        v.co.x += rnd.uniform(-amount, amount) * axes[0]
        v.co.y += rnd.uniform(-amount, amount) * axes[1]
        v.co.z += rnd.uniform(-amount, amount) * axes[2]
    ob.data.update()
    return ob


def rock(sx, sy, sz, cuts=1, rough=0.04, seed=1, mat=None, name="rock", taper=0.0):
    """A box sitting on z=0 with its faces broken up a little, taper narrows its top."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    if cuts:
        bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts, use_grid_fill=True)
    rnd = random.Random(seed)
    for v in bm.verts:
        z = v.co.z + 0.5
        k = 1.0 - taper * z
        v.co.x = v.co.x * sx * k + rnd.uniform(-rough, rough)
        v.co.y = v.co.y * sy * k + rnd.uniform(-rough, rough)
        v.co.z = z * sz + (rnd.uniform(-rough, rough) if z > 0.01 else 0.0)
    return from_bm(name, bm, mat)


def sweep(path, profile, side=(0, 1, 0), closed=False, scales=None, mat=None, name="sweep", cap=True):
    """Sweeps a closed 2D profile (u, v) along a 3D polyline. u runs along the
    in-plane normal (tangent x side), v along `side`; scales tapers each ring,
    a number for both or a (u, v) pair."""
    P = [Vector(p) for p in path]
    n = len(P)
    B = Vector(side).normalized()
    verts, faces = [], []
    m = len(profile)
    for i, p in enumerate(P):
        if closed:
            T = (P[(i + 1) % n] - P[i - 1])
        else:
            T = P[min(n - 1, i + 1)] - P[max(0, i - 1)]
        T.normalize()
        N = T.cross(B).normalized()
        Bi = N.cross(T).normalized()
        s = scales[i] if scales else 1.0
        su, sv = s if isinstance(s, tuple) else (s, s)
        for (u, v) in profile:
            verts.append(p + N * (u * su) + Bi * (v * sv))
    rings = n if closed else n - 1
    for i in range(rings):
        a, b = i * m, ((i + 1) % n) * m
        for j in range(m):
            j2 = (j + 1) % m
            faces.append((a + j, a + j2, b + j2, b + j))
    if not closed and cap:
        faces.append(tuple(range(m - 1, -1, -1)))
        faces.append(tuple((n - 1) * m + j for j in range(m)))
    ob = mesh(name, verts, faces, mat)
    _fix_normals(ob)
    return ob


def _fix_normals(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def lathe(profile, seg=24, a0=0.0, a1=360.0, mat=None, name="lathe", cap_ends=True):
    """Revolves an (r, z) profile, listed bottom to top, around the Z axis;
    a partial sweep gets flat end caps."""
    full = abs(a1 - a0) >= 359.99
    steps = seg if full else max(1, seg)
    verts, faces = [], []
    m = len(profile)
    cols = steps if full else steps + 1
    for k in range(cols):
        a = math.radians(a0 + (a1 - a0) * k / steps)
        for (r, z) in profile:
            verts.append((r * math.cos(a), r * math.sin(a), z))
    for k in range(steps):
        c0, c1 = k * m, ((k + 1) % cols) * m
        for j in range(m - 1):
            faces.append((c0 + j, c1 + j, c1 + j + 1, c0 + j + 1))
    if not full and cap_ends:
        faces.append(tuple(range(m - 1, -1, -1)))
        faces.append(tuple(steps * m + j for j in range(m)))
    ob = mesh(name, verts, faces, mat)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
    return ob


def wedge(profile, a0, a1, steps=3, mat=None, name="wedge"):
    """Revolves a closed (r, z) outline from a0 to a1 degrees round Z and caps
    both ends: one block of a segmented ring."""
    m = len(profile)
    verts, faces = [], []
    for k in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * k / steps)
        for (r, z) in profile:
            verts.append((r * math.cos(a), r * math.sin(a), z))
    for k in range(steps):
        c0, c1 = k * m, (k + 1) * m
        for j in range(m):
            j2 = (j + 1) % m
            faces.append((c0 + j, c1 + j, c1 + j2, c0 + j2))
    faces.append(tuple(range(m - 1, -1, -1)))
    faces.append(tuple(steps * m + j for j in range(m)))
    ob = mesh(name, verts, faces, mat)
    _fix_normals(ob)
    return ob


def ico(r, subdiv=1, loc=(0, 0, 0), scale=(1, 1, 1), mat=None, name="ico", seed=None, rough=0.0):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    ob = from_bm(name, bm, mat)
    if seed is not None and rough:
        jitter(ob, rough, seed)
    return xform(ob, loc=loc, scale=scale)


def flame(base, height, r, seg=5, lean=(0.0, 0.0), twist=40.0, rings=4, mat=None, name="flame", seed=1):
    """A faceted tongue of fire: a twisted, leaning cone that bulges low and
    ends in a point."""
    rnd = random.Random(seed)
    verts, faces = [], []
    bx, by, bz = base
    for k in range(rings):
        t = k / rings
        rad = r * (0.75 + 1.1 * t) * (1.0 - t) ** 0.9 * 1.4 if k else r * 0.8
        cx = bx + lean[0] * t * t * height
        cy = by + lean[1] * t * t * height
        cz = bz + t * height
        ph = math.radians(twist * t) + rnd.uniform(0, 0.3)
        for j in range(seg):
            a = ph + 2 * math.pi * j / seg
            verts.append((cx + rad * math.cos(a), cy + rad * math.sin(a), cz))
    tip = len(verts)
    verts.append((bx + lean[0] * height, by + lean[1] * height, bz + height))
    for k in range(rings - 1):
        for j in range(seg):
            j2 = (j + 1) % seg
            faces.append((k * seg + j, k * seg + j2, (k + 1) * seg + j2, (k + 1) * seg + j))
    last = (rings - 1) * seg
    for j in range(seg):
        faces.append((last + j, last + (j + 1) % seg, tip))
    faces.append(tuple(range(seg - 1, -1, -1)))
    ob = mesh(name, verts, faces, mat)
    _fix_normals(ob)
    return ob


def flat_shade(ob):
    for p in ob.data.polygons:
        p.use_smooth = False
    return ob


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def vmat(name, ref, rough=0.85, metal=0.0, emit=None, strength=0.0, alpha=None):
    """A material whose colour is ref (sRGB 0-255) times the 'Col' vertex
    colours; glTF keeps ref as the factor and the colours as COLOR_0, and an
    emit colour is multiplied by them too."""
    rgb = tuple(lin(c) for c in ref)
    m = hk.pbr(name, rgb, rough, metal, emit, strength)
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    ca = nt.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"

    def mul(colour, socket):
        mx = nt.nodes.new("ShaderNodeMix")
        mx.data_type, mx.blend_type = "RGBA", "MULTIPLY"
        mx.inputs["Factor"].default_value = 1.0
        mx.inputs[6].default_value = (*colour, 1.0)
        nt.links.new(ca.outputs["Color"], mx.inputs[7])
        nt.links.new(mx.outputs[2], b.inputs[socket])
    mul(rgb, "Base Color")
    if emit is not None:
        mul(emit, "Emission Color")
    if alpha is not None:
        b.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = "BLENDED"
        except AttributeError:
            pass
    m["ref"] = list(ref)
    return m


def vpaint(ob, fn):
    """Sets the 'Col' vertex colours: fn(poly, co, normal, loop) gives an sRGB
    0-255 colour, stored as its ratio to the face material's ref colour.
    Faces of a plain material stay white, since the game multiplies every
    material by the vertex colour."""
    me = ob.data
    refs = [[max(1e-4, lin(c)) for c in m["ref"]] if m is not None and "ref" in m else None
            for m in me.materials] or [None]
    at = me.color_attributes.get("Col") or me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for p in me.polygons:
        rl = refs[min(p.material_index, len(refs) - 1)]
        for li in p.loop_indices:
            if rl is None:
                at.data[li].color = (1.0, 1.0, 1.0, 1.0)
                continue
            co = me.vertices[me.loops[li].vertex_index].co
            c = fn(p, co, p.normal, li)
            at.data[li].color = tuple(min(1.0, lin(c[i]) / rl[i]) for i in range(3)) + (1.0,)
    return ob


def whiten(parts):
    """Every part gets 'Col' (white where unpainted): joining fills a missing
    attribute with black, and glTF would carry that black to the game."""
    for p in parts:
        if p.data.color_attributes.get("Col") is None:
            at = p.data.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
            for d in at.data:
                d.color = (1.0, 1.0, 1.0, 1.0)
    return parts


def tidy(parts):
    """Drops working custom properties from the parts, so the joined glTF
    node carries only the extras finish() gives it."""
    for p in parts:
        for k in list(p.keys()):
            del p[k]
    return parts


def join(parts, name):
    """Joins parts into one object (for painting them as one)."""
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    return ob


def mixc(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def build(name, parts, texture, piece, sprite_dir=r"D:\OKReplace\lodes\sprites",
          out=r"D:\OKReplace\lodes\hand", hotspot=None):
    tidy(parts)
    whiten(parts)
    ob = hk.finish(parts, os.path.join(out, "models", name + ".glb"),
                   {"replacesTexture": texture, "replacesPiece": piece})
    print("MISCKIT_TRIS", name, tris(ob))
    hk.renders(ob, os.path.join(out, "renders"), name, os.path.join(sprite_dir, name + ".png"), hotspot, scale=4)
    return ob
