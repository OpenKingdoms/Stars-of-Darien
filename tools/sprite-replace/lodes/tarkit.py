"""Shared parts for the Taros lodestones (TARLODE, TARMANA), run inside Blender.

Frame as handkit: 1 unit = 1 cell, -Y toward the classic camera, Z up,
origin on the anchor. The picture maps a point to column 31 + 16x and
row 111 - 16y - 8z.
"""
import math
import os
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


def _catmull(pts, sub):
    P = [Vector(p) for p in pts]
    n = len(P)
    out = []
    for i in range(n - 1):
        p0 = P[i - 1] if i > 0 else P[i] * 2 - P[i + 1]
        p1, p2 = P[i], P[i + 1]
        p3 = P[i + 2] if i + 2 < n else P[i + 1] * 2 - P[i]
        for s in range(sub):
            t = s / sub
            t2, t3 = t * t, t * t * t
            v = 0.5 * ((2 * p1) + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                       + (3 * p1 - p0 - 3 * p2 + p3) * t3)
            out.append((v, i + t))
    out.append((P[-1], float(n - 1)))
    return out


def tube(pts, radii, seg=6, sub=3, mat=None, name="tube", flat=1.0, up=(0, 0, 1), shade=70):
    """A tapered tube along a smoothed path; a radius of 0 at the end makes a point.
    flat squashes the section along the normal nearest `up`; sub=1 keeps sharp kinks."""
    path = _catmull(pts, sub)
    n = len(path)
    bm = bmesh.new()
    upv = Vector(up)
    rings = []
    prev_n = None
    for k, (p, u) in enumerate(path):
        a = path[max(0, k - 1)][0]
        b = path[min(n - 1, k + 1)][0]
        T = (b - a).normalized()
        if prev_n is None:
            N = upv - T * upv.dot(T)
            if N.length < 1e-4:
                N = Vector((1, 0, 0)) - T * T.x
            N.normalize()
        else:
            N = (prev_n - T * prev_n.dot(T)).normalized()
        prev_n = N
        B = T.cross(N)
        i = min(int(u), len(radii) - 2)
        f = u - i
        r = radii[i] * (1 - f) + radii[i + 1] * f
        if r < 1e-3:
            rings.append([bm.verts.new(p)])
            continue
        ring = []
        for s in range(seg):
            ang = 2 * math.pi * s / seg
            ring.append(bm.verts.new(p + (N * math.cos(ang) * flat + B * math.sin(ang)) * r))
        rings.append(ring)
    for A, B_ in zip(rings, rings[1:]):
        if len(A) == 1 and len(B_) == 1:
            continue
        if len(B_) == 1:
            for s in range(seg):
                bm.faces.new((A[s], A[(s + 1) % seg], B_[0]))
        elif len(A) == 1:
            for s in range(seg):
                bm.faces.new((A[0], B_[(s + 1) % seg], B_[s]))
        else:
            for s in range(seg):
                bm.faces.new((A[s], A[(s + 1) % seg], B_[(s + 1) % seg], B_[s]))
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    ob = hk._object(name, bm, mat)
    return hk.smooth(ob, shade)


def lathe(profile, seg=24, mat=None, name="lathe", phase=0.0, closed=False):
    """A surface of revolution from (r, z) points. An open profile should start
    and end on the axis (r 0); a closed one loops back to its first point."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r < 1e-4:
            rings.append([bm.verts.new((0.0, 0.0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(phase + 2 * math.pi * s / seg),
                                        r * math.sin(phase + 2 * math.pi * s / seg), z)) for s in range(seg)])
    pairs = list(zip(rings, rings[1:]))
    if closed:
        pairs.append((rings[-1], rings[0]))
    for A, B in pairs:
        if len(A) == 1 and len(B) == 1:
            continue
        if len(B) == 1:
            for s in range(seg):
                bm.faces.new((A[s], A[(s + 1) % seg], B[0]))
        elif len(A) == 1:
            for s in range(seg):
                bm.faces.new((A[0], B[(s + 1) % seg], B[s]))
        else:
            for s in range(seg):
                bm.faces.new((A[s], A[(s + 1) % seg], B[(s + 1) % seg], B[s]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return hk._object(name, bm, mat)


def torus(R, r, z, seg=32, rseg=6, mat=None, name="torus"):
    prof = [(R + r * math.cos(2 * math.pi * k / rseg), z + r * math.sin(2 * math.pi * k / rseg)) for k in range(rseg)]
    ob = lathe(prof, seg=seg, mat=mat, name=name, closed=True)
    return hk.smooth(ob, 60)


def bevel_mat(ob, width, segments=1, index=None, angle=30):
    """An angle-limited bevel whose new faces take material slot `index`, so
    crystal and claw edges can carry a lighter rim."""
    mod = ob.modifiers.new("bevel", "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(angle)
    if index is not None:
        mod.material = index
    hk._apply(ob)
    return ob


def flat(ob):
    for p in ob.data.polygons:
        p.use_smooth = False
    return ob


def edged(ob, edge_mat, width=0.02, angle=30):
    """Bevels the part's sharp edges into a lighter edge material, flat shaded."""
    ob.data.materials.append(edge_mat)
    bevel_mat(ob, width, 1, index=len(ob.data.materials) - 1, angle=angle)
    return flat(ob)


def _gradient(name, stops, w=16, h=128, streak=0.0, seed=1):
    """An sRGB gradient image up v, stops (v, (r, g, b) 0-255), faint vertical streaks."""
    import random
    rnd = random.Random(seed)
    cols = [1.0 - streak * rnd.random() for _ in range(w)]
    img = bpy.data.images.new(name, w, h, alpha=False)
    px = []
    for j in range(h):
        v = j / (h - 1)
        for i, (a, b) in enumerate(zip(stops, stops[1:])):
            if v <= b[0] or i == len(stops) - 2:
                t = 0.0 if b[0] == a[0] else min(1.0, max(0.0, (v - a[0]) / (b[0] - a[0])))
                rgb = [a[1][c] * (1 - t) + b[1][c] * t for c in range(3)]
                break
        for i in range(w):
            k = cols[i] if v < 0.93 else 1.0
            px += [rgb[0] * k / 255.0, rgb[1] * k / 255.0, rgb[2] * k / 255.0, 1.0]
    img.pixels[:] = px
    img.pack()
    return img


def gem_material(name, stops, emit_stops, strength=1.0, rough=0.2, spec=0.5, seed=1, tint=None):
    """A crystal body: base colour and glow each run up their own gradient (v up
    the crystal), so the body can stay deep while the root or point burns.
    tint colours the specular so grey sky reflections do not wash the red out."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    for key, st, streak in (("Base Color", stops, 0.18), ("Emission Color", emit_stops, 0.0)):
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = _gradient(name + ("_base" if key == "Base Color" else "_glow"), st, streak=streak, seed=seed)
        tex.interpolation = "Linear"
        nt.links.new(tex.outputs["Color"], b.inputs[key])
    b.inputs["Emission Strength"].default_value = strength
    b.inputs["Roughness"].default_value = rough
    b.inputs["Specular IOR Level"].default_value = spec
    if tint is not None:
        b.inputs["Specular Tint"].default_value = (*tint, 1.0)
    return m


def glow(name, rgb, emit, strength, rough=0.25, metal=0.0):
    return hk.pbr(name, rgb, rough=rough, metal=metal, emit=emit, strength=strength)


def gem(base, axis, profile, tip, mats, sides=6, spin=0.0, angles=None, jitter=None, skew=(0.0, 0.0),
        broken=None, foot=0.0, gold_dir=None, gold_min=0.3, rim=0.02, name="gem"):
    """A flat-shaded crystal: rings of (height, radius) up its axis and a point
    `tip` above the last. angles (per side offsets, radians) and jitter (per side
    radius factors) make the facets uneven; broken=(sx, sy) slants the top ring
    for a snapped end. mats = [body, rim, gold]: the bevelled edges take the rim
    material, and point facets facing gold_dir (world) take the gold one."""
    bm = bmesh.new()
    offs = angles or [0.0] * sides
    rings = []
    for h, r in profile:
        ring = []
        for s in range(sides):
            a = spin + 2 * math.pi * s / sides + offs[s % len(offs)]
            rr = r * (jitter[s % len(jitter)] if jitter else 1.0)
            ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), h)))
        rings.append(ring)
    top_h = profile[-1][0]
    if broken:
        for v in rings[-1]:
            v.co.z += broken[0] * v.co.x + broken[1] * v.co.y
    apex = bm.verts.new((skew[0], skew[1], top_h + tip))
    for lo, hi in zip(rings, rings[1:]):
        for s in range(sides):
            bm.faces.new((lo[s], lo[(s + 1) % sides], hi[(s + 1) % sides], hi[s]))
    top = rings[-1]
    for s in range(sides):
        bm.faces.new((top[s], top[(s + 1) % sides], apex))
    if foot > 0:
        bot = bm.verts.new((0.0, 0.0, profile[0][0] - foot))
        for s in range(sides):
            bm.faces.new((rings[0][(s + 1) % sides], rings[0][s], bot))
    else:
        bm.faces.new(list(reversed(rings[0])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    total = top_h + tip
    z0 = profile[0][0] - foot
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        cx = sum(l.vert.co.x for l in f.loops) / len(f.loops)
        cy = sum(l.vert.co.y for l in f.loops) / len(f.loops)
        u = (math.atan2(cy, cx) - spin) / (2 * math.pi) % 1.0
        for l in f.loops:
            l[uv].uv = (u, min(1.0, max(0.0, (l.vert.co.z - z0) / (total - z0))))
    bm.verts.index_update()
    apex_i = apex.index
    ob = hk._object(name, bm, mats[0])
    for m in mats[1:]:
        ob.data.materials.append(m)
    ax = Vector(axis).normalized()
    q = Vector((0, 0, 1)).rotation_difference(ax)
    R = q.to_matrix()
    if gold_dir is not None and len(mats) > 2:
        g = Vector(gold_dir).normalized()
        for p in ob.data.polygons:
            if apex_i in p.vertices and (R @ p.normal).dot(g) > gold_min:
                p.material_index = 2
    if rim and len(mats) > 1:
        bevel_mat(ob, rim, 1, index=1, angle=25)
    flat(ob)
    ob.matrix_world = Matrix.Translation(Vector(base)) @ R.to_4x4()
    return ob


def ellipsoid(c, r, seg=12, rings=8, mat=None, name="ell", rot=None):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * r[0], v.co.y * r[1], v.co.z * r[2]))
    ob = hk._object(name, bm, mat)
    M = Matrix.Translation(Vector(c))
    if rot is not None:
        M = M @ rot
    ob.matrix_world = M
    return hk.smooth(ob, 80)


def apply(ob):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def boolean(ob, cutter, op="DIFFERENCE"):
    apply(ob)
    apply(cutter)
    mod = ob.modifiers.new("bool", "BOOLEAN")
    mod.operation = op
    mod.object = cutter
    mod.solver = "EXACT"
    try:
        mod.material_mode = "TRANSFER"
    except (AttributeError, TypeError):
        pass
    hk._apply(ob)
    bpy.data.objects.remove(cutter, do_unlink=True)
    return ob


def polar(r, deg, z):
    a = math.radians(deg)
    return (r * math.cos(a), r * math.sin(a), z)


def place(ob, M):
    """Moves an object by M on top of its own transform."""
    ob.matrix_world = M @ ob.matrix_world
    return ob


# ---- materials --------------------------------------------------------------

def materials():
    return {
        # bone for horns, claws and tendrils, and a brighter cream for the skull
        "bone": hk.pbr("tar_bone", (0.76, 0.68, 0.47), rough=0.45),
        "skull": hk.pbr("tar_skull_bone", (0.94, 0.91, 0.74), rough=0.42),
        # blackened iron that keeps grey edge highlights without strong reflections
        "iron": hk.pbr("tar_black_iron", (0.11, 0.10, 0.095), rough=0.32, metal=0.5),
        "edge": hk.pbr("tar_iron_edge", (0.34, 0.33, 0.31), rough=0.28, metal=0.5),
        "ring": hk.pbr("tar_ring_iron", (0.37, 0.35, 0.32), rough=0.32, metal=0.55),
    }


def standard_view():
    """Plain sRGB output so the renders compare colour for colour with the picture."""
    vs = bpy.context.scene.view_settings
    try:
        vs.view_transform = "Standard"
        vs.look = "None"
    except TypeError:
        pass


# ---- the shared base: iron ring, bone claws, bone tendrils -------------------

RING_Y = -0.07  # the picture's ring sits a touch toward the camera
RING_DEEP = 1.06  # and is drawn a little deeper than wide
RING_TOP = 0.30


def ring_base(M, rivets=8, seg=32):
    parts = []
    # a thick band with a chamfered outer edge and a raised, rounded inner lip
    prof = [(0.94, 0.0), (1.33, 0.0), (1.33, 0.22), (1.29, 0.28), (1.08, 0.28), (1.05, 0.31),
            (1.00, 0.345), (0.95, 0.33), (0.925, 0.29), (0.925, 0.04)]
    ring = lathe(prof, seg=seg, mat=M["ring"], name="ring", closed=True)
    ring.location.y = RING_Y
    ring.scale.y = RING_DEEP
    bevel_mat(ring, 0.015, 1, angle=40)
    hk.smooth(ring, 35)
    parts.append(ring)
    for k in range(rivets):
        a = 360.0 / rivets * (k + 0.5)
        x, y, z = polar(1.19, a, RING_TOP)
        parts.append(ellipsoid((x, y * RING_DEEP + RING_Y, z), (0.07, 0.07, 0.05), seg=6, rings=3,
                               mat=M["ring"], name="rivet"))
    # the bone arc over the front of the ring, and its claws
    arc = [polar(1.16, a, RING_TOP + 0.07) for a in range(-148, -31, 9)]
    arc = [(x, y * RING_DEEP + RING_Y, z) for x, y, z in arc]
    radii = [0.035] + [0.075] * (len(arc) - 2) + [0.035]
    parts.append(tube(arc, radii, seg=6, sub=2, mat=M["bone"], name="bone_arc"))
    for side in (-1, 1):
        # a bone claw over the ring's front: a broad palm, a long toe, a short one
        k = (side * 0.64, -1.02 * RING_DEEP + RING_Y, RING_TOP + 0.10)
        palm = [k, (side * 0.78, -1.22, 0.42), (side * 0.92, -1.36, 0.30)]
        parts.append(tube(palm, [0.10, 0.11, 0.10], seg=6, sub=2, mat=M["bone"], name="palm", flat=0.7))
        toes = [
            [(side * 0.92, -1.36, 0.30), (side * 1.00, -1.50, 0.15), (side * 1.05, -1.64, 0.0)],
            [(side * 0.84, -1.36, 0.32), (side * 0.72, -1.48, 0.17), (side * 0.63, -1.54, 0.0)],
        ]
        for t in toes:
            parts.append(tube(t, [0.085, 0.055, 0.0], seg=6, sub=2, mat=M["bone"], name="toe"))
    # jagged bone roots out to both sides, lying on the ground, kinked sharply
    left = [(-1.22, 0.02, 0.16), (-1.44, 0.10, 0.11), (-1.53, -0.02, 0.09), (-1.63, -0.24, 0.08),
            (-1.81, -0.25, 0.08), (-1.88, -0.13, 0.09), (-1.97, -0.09, 0.09)]
    right = [(1.26, -0.30, 0.16), (1.40, -0.10, 0.11), (1.50, 0.15, 0.09), (1.69, 0.20, 0.08),
             (1.78, 0.08, 0.08), (1.88, 0.02, 0.09), (1.97, 0.06, 0.09)]
    for pts in (left, right):
        parts.append(tube(pts, [0.10, 0.09, 0.08, 0.07, 0.055, 0.035, 0.0], seg=5, sub=1,
                          mat=M["bone"], name="tendril", shade=50))
    return parts


# ---- thorns and spikes ------------------------------------------------------

def blade(pts, width, thick=0.45, mat=None, name="blade", sub=2, up=(0, 0, 1)):
    """A thorn blade: a rhombus section tube, flat shaded so its edges stay crisp."""
    ob = tube(pts, width, seg=4, sub=sub, mat=mat, name=name, flat=thick, up=up)
    return flat(ob)


def bone_spikes(M, zc, widths=(0.075, 0.062, 0.048, 0.03, 0.0)):
    """Two thin bone thorns running out to the sides and turning up."""
    parts = []
    for side in (-1, 1):
        pts = [(side * 0.30, 0.30, zc + 0.10), (side * 0.62, 0.36, zc + 0.20), (side * 0.86, 0.42, zc + 0.38),
               (side * 0.96, 0.48, zc + 0.64), (side * 0.93, 0.50, zc + 0.90)]
        parts.append(blade(pts, list(widths), thick=0.8, mat=M["bone"], name="spike", sub=3))
    return parts
