"""Shared parts for the Zhon lodestones (ZONLODE, ZONMANA), run inside Blender.

Frame as handkit: 1 unit = 1 cell, -Y toward the classic camera, Z up,
origin on the anchor. Surfaces are built as ring grids with UVs, and their
colour and fine relief are painted into small textures from 3D noise at
build time, so no mottling rides on per-face material slots.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.dirname(HERE)
if KIT not in sys.path:
    sys.path.insert(0, KIT)
import handkit as hk  # noqa: E402

# the classic camera looks down at E; one cell of depth rises SY px, one of height SZ
E = math.atan(2.0)
SY, SZ = 16 * math.sin(E), 16 * math.cos(E)
VIEW = Vector((0.0, math.cos(E), -math.sin(E)))
SCREEN_UP = Vector((0.0, math.sin(E), math.cos(E)))


def screen(p, hot):
    return hot[0] + 16 * p.x, hot[1] - (SY * p.y + SZ * p.z)


def at(px, row, y, hot):
    """The world point at depth y that the camera sees at picture pixel (px, row)."""
    u = hot[1] - row
    return Vector(((px - hot[0]) / 16.0, y, (u - SY * y) / SZ))


def lin1(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(rgb):
    return tuple(lin1(c) for c in rgb)


def smoothstep(a, b, x):
    t = np.clip((np.asarray(x, dtype=float) - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def sstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ---- noise -----------------------------------------------------------------

class Noise:
    """3D value noise on numpy arrays, with fbm."""

    def __init__(self, seed):
        rng = np.random.default_rng(seed)
        self.p = np.concatenate([rng.permutation(256)] * 2)
        self.v = rng.random(256)

    def value(self, x, y, z):
        x, y, z = (np.asarray(c, dtype=float) for c in (x, y, z))
        xi, yi, zi = (np.floor(c).astype(np.int64) for c in (x, y, z))
        xf, yf, zf = x - xi, y - yi, z - zi
        u, v, w = (f * f * (3 - 2 * f) for f in (xf, yf, zf))
        p, val = self.p, self.v

        def h(a, b, c):
            return val[p[p[p[a & 255] + (b & 255)] + (c & 255)] & 255]
        x0 = h(xi, yi, zi) * (1 - u) + h(xi + 1, yi, zi) * u
        x1 = h(xi, yi + 1, zi) * (1 - u) + h(xi + 1, yi + 1, zi) * u
        x2 = h(xi, yi, zi + 1) * (1 - u) + h(xi + 1, yi, zi + 1) * u
        x3 = h(xi, yi + 1, zi + 1) * (1 - u) + h(xi + 1, yi + 1, zi + 1) * u
        return (x0 * (1 - v) + x1 * v) * (1 - w) + (x2 * (1 - v) + x3 * v) * w

    def fbm(self, x, y, z, octaves=4, lac=2.07, gain=0.5):
        tot, amp, norm, f = 0.0, 1.0, 0.0, 1.0
        for k in range(octaves):
            tot = tot + amp * self.value(x * f + 17.1 * k, y * f - 9.3 * k, z * f + 5.7 * k)
            norm += amp
            amp *= gain
            f *= lac
        return tot / norm


def mix(a, b, t):
    """Blends colour arrays (..., 3) by t (...)."""
    t = np.asarray(t, dtype=float)[..., None]
    return a * (1 - t) + b * t


def rgb(c):
    return np.array(c, dtype=float) / 255.0


# ---- images and materials --------------------------------------------------

def image(name, px, noncolor=False):
    """px is (H, W, 3) in 0..1 (sRGB for colour), row 0 at v = 0."""
    H, W = px.shape[:2]
    img = bpy.data.images.new(name, W, H, alpha=False)
    img["okGenerated"] = True  # made here, so it ships (okpaint.py)
    if noncolor:
        img.colorspace_settings.name = "Non-Color"
    buf = np.ones((H, W, 4), dtype=np.float32)
    buf[..., :3] = np.clip(px, 0.0, 1.0)
    img.pixels.foreach_set(buf.ravel())
    img.update()
    dump = os.environ.get("ZK_DUMP")
    if dump:  # a copy on disk to look at while tuning
        img.filepath_raw = os.path.join(dump, name + ".png")
        img.file_format = "PNG"
        img.save()
    img.pack()
    return img


def normal_image(name, h, du, dv, strength=1.0, wrap_u=True, wrap_v=False):
    """A tangent-space normal map from a height field h (H, W) in cells;
    du, dv are the texel sizes in cells along u and v."""
    gy, gx = np.gradient(h, dv, du)
    if wrap_u:
        gx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) / (2 * du)
    if wrap_v:
        gy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) / (2 * dv)
    n = np.stack([-gx * strength, -gy * strength, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return image(name, n * 0.5 + 0.5, noncolor=True)


def tex_mat(name, img, rough=0.85, metal=0.0, nimg=None, nstrength=1.0, rough_img=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    uvn = nt.nodes.new("ShaderNodeUVMap")  # bound explicitly or the exporter writes texCoord -1
    uvn.uv_map = "UVMap"
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img
    nt.links.new(uvn.outputs["UV"], t.inputs["Vector"])
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if nimg is not None:
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = nimg
        nt.links.new(uvn.outputs["UV"], tn.inputs["Vector"])
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.uv_map = "UVMap"
        nm.inputs["Strength"].default_value = nstrength
        nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m


# ---- meshes ----------------------------------------------------------------

def make(name, bm, mats):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def grid(bm, P, uv=None, top=None, bottom=None, mi=None, uvl=None):
    """Faces over rings of points P[j][i] (rings bottom to top, columns
    counter-clockwise seen from above, wrapping round). uv(j, i) with i up to
    ncol gives the seam its own u. top / bottom are optional apex points
    with their own uv(j) row index. Returns the uv layer."""
    nr, nc = len(P), len(P[0])
    V = [[bm.verts.new(P[j][i]) for i in range(nc)] for j in range(nr)]
    if uv is not None and uvl is None:
        uvl = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")

    def put(f, keys):
        if uv is None:
            return
        for lp, k in zip(f.loops, keys):
            lp[uvl].uv = uv(*k)
    for j in range(nr - 1):
        for i in range(nc):
            i2 = (i + 1) % nc
            f = bm.faces.new((V[j][i], V[j][i2], V[j + 1][i2], V[j + 1][i]))
            put(f, [(j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i)])
            if mi:
                f.material_index = mi(j, i)
    if top is not None:
        t = bm.verts.new(top)
        for i in range(nc):
            f = bm.faces.new((V[-1][i], V[-1][(i + 1) % nc], t))
            put(f, [(nr - 1, i), (nr - 1, i + 1), (nr, i + 0.5)])
            if mi:
                f.material_index = mi(nr - 1, i)
    if bottom is not None:
        t = bm.verts.new(bottom)
        for i in range(nc):
            f = bm.faces.new((V[0][(i + 1) % nc], V[0][i], t))
            put(f, [(0, i + 1), (0, i), (-1, i + 0.5)])
            if mi:
                f.material_index = mi(0, i)
    return uvl


def spline(ctrl, n=8):
    """Catmull-Rom through the control points, n samples per span."""
    P = [Vector(c) for c in ctrl]
    P = [2 * P[0] - P[1]] + P + [2 * P[-1] - P[-2]]
    out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for k in range(n):
            t = k / n
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t))
    out.append(P[-2].copy())
    return out


def resample(pts, step):
    """Even spacing along a polyline, about step apart."""
    L = [0.0]
    for i in range(1, len(pts)):
        L.append(L[-1] + (pts[i] - pts[i - 1]).length)
    n = max(2, int(round(L[-1] / step)) + 1)
    out, k = [], 0
    for m in range(n):
        s = L[-1] * m / (n - 1)
        while k < len(L) - 2 and L[k + 1] < s:
            k += 1
        t = (s - L[k]) / max(1e-9, L[k + 1] - L[k])
        out.append(pts[k].lerp(pts[k + 1], min(1.0, max(0.0, t))))
    return out


def frames(pts, up=(0, 0, 1)):
    """Rotation-minimising frames along a polyline."""
    T = []
    for i in range(len(pts)):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, len(pts) - 1)]
        T.append((b - a).normalized())
    u = Vector(up)
    if abs(u.dot(T[0])) > 0.95:
        u = Vector((1, 0, 0))
    N = [(u - T[0] * u.dot(T[0])).normalized()]
    for i in range(1, len(pts)):
        q = T[i - 1].rotation_difference(T[i])
        n = q @ N[-1]
        n = (n - T[i] * n.dot(T[i])).normalized()
        N.append(n)
    B = [T[i].cross(N[i]) for i in range(len(pts))]
    return T, N, B


def sweep(bm, pts, radius, seg=6, flat=1.0, ridge=0.0, ridge_k=3, ridge_twist=0.0, up=(0, 0, 1), mi=0,
          bumps=None, face_mi=None, a_off=0.0, ulen=None, frames_=None):
    """A tube along pts; radius(s) with s in 0..1 along the length. Ends
    close to a point where the radius is tiny, otherwise they are capped.
    ridge twists k strands round the tube (a plied rope); bumps(s, a)
    adds knobbles; face_mi(s, a) picks a face's material. With ulen the
    tube gets UVs: u runs one unit per ulen cells along it, v once round."""
    T, N, B = frames_ if frames_ else frames(pts, up)
    L = [0.0]
    for i in range(1, len(pts)):
        L.append(L[-1] + (pts[i] - pts[i - 1]).length)
    rings = []
    for i, p in enumerate(pts):
        s = L[i] / L[-1]
        r = radius(s)
        if r < 0.004:
            rings.append([bm.verts.new(p)])
            continue
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg + a_off
            rr = r * (1 + ridge * math.cos(ridge_k * a + ridge_twist * L[i]))
            if bumps:
                rr *= 1 + bumps(s, a)
            ring.append(bm.verts.new(p + N[i] * math.cos(a) * rr + B[i] * math.sin(a) * rr * flat))
        rings.append(ring)

    def fm(i, k):
        if face_mi is None:
            return mi
        return face_mi(L[i] / L[-1], 2 * math.pi * (k + 0.5) / seg + a_off)
    uvl = None
    if ulen:
        uvl = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        if len(a) == 1 and len(b) == 1:
            continue
        for k in range(seg):
            if len(a) == 1:
                f = bm.faces.new((a[0], b[k], b[(k + 1) % seg]))
                keys = [(i, k + 0.5), (i + 1, k), (i + 1, k + 1)]
            elif len(b) == 1:
                f = bm.faces.new((a[k], b[0], a[(k + 1) % seg]))
                keys = [(i, k), (i + 1, k + 0.5), (i, k + 1)]
            else:
                f = bm.faces.new((a[k], b[k], b[(k + 1) % seg], a[(k + 1) % seg]))
                keys = [(i, k), (i + 1, k), (i + 1, k + 1), (i, k + 1)]
            f.material_index = fm(i, k)
            if uvl is not None:
                for lp, (ii, kk) in zip(f.loops, keys):
                    lp[uvl].uv = (L[ii] / ulen, kk / seg)
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0]))).material_index = fm(0, 0)
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1]).material_index = fm(len(rings) - 2, 0)


def fix_normals(bm):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def smooth(ob, angle=50):
    return hk.smooth(ob, angle)


# ---- extra checks ------------------------------------------------------------

def grey_world(value=0.5, strength=1.0):
    """A plain mid-grey sky, roughly what a Unity skybox or probe gives."""
    w = bpy.data.worlds.new("zk_grey")
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (value, value, value, 1.0)
    bg.inputs["Strength"].default_value = strength
    return w


def grey_renders(ob, out_dir, name, sprite, hot, scale=4):
    """The classic and turned views again under a mid-grey world."""
    scene = bpy.context.scene
    old = scene.world
    scene.world = grey_world()
    hk.renders(ob, out_dir, name + "_grey", sprite, hot, scale)
    scene.world = old


def closeups(out_dir, name, target, size, views, res=384):
    """Orthographic close-ups of a region: views are (label, azimuth,
    elevation) in degrees, azimuth -90 facing the classic camera's side."""
    scene = bpy.context.scene
    cam = scene.camera
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = size
    scene.render.resolution_x = scene.render.resolution_y = res
    target = Vector(target)
    for label, az, el in views:
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)))
        cam.location = target + d * 60
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, "%s_%s.png" % (name, label))
        bpy.ops.render.render(write_still=True)


# ---- implicit forms --------------------------------------------------------

def smin(a, b, k):
    """Polynomial smooth minimum: a union with a fillet about k wide."""
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


def surface_nets(f, lo, hi, h, project=2):
    """Polygonises the zero set of f (negative inside) over the box lo..hi
    with cell size h. Returns (verts (n, 3), quads (m, 4))."""
    axes = [np.arange(lo[i], hi[i] + h * 0.5, h) for i in range(3)]
    G = np.stack(np.meshgrid(*axes, indexing="ij"), axis=-1)
    F = f(G)
    nx, ny, nz = F.shape
    cor = [(0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (0, 0, 1), (1, 0, 1), (0, 1, 1), (1, 1, 1)]
    edg = [(0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7)]
    Fc = [F[i:nx - 1 + i, j:ny - 1 + j, k:nz - 1 + k] for i, j, k in cor]
    acc = np.zeros(Fc[0].shape + (3,))
    cnt = np.zeros(Fc[0].shape)
    for a, b in edg:
        fa, fb = Fc[a], Fc[b]
        m = (fa < 0) != (fb < 0)
        t = np.where(m, fa / np.where(m, fa - fb, 1.0), 0.0)
        pa, pb = np.array(cor[a], float), np.array(cor[b], float)
        acc += m[..., None] * (pa + t[..., None] * (pb - pa))
        cnt += m
    act = cnt > 0
    idx = -np.ones(act.shape, dtype=np.int64)
    idx[act] = np.arange(act.sum())
    cube = np.argwhere(act).astype(float)
    V = (cube + acc[act] / cnt[act][..., None]) * h + np.asarray(lo, float)
    quads = []
    inside = F < 0
    # an edge of the grid that crosses the surface gets the quad of the four cubes round it
    s = inside[:-1, 1:-1, 1:-1] != inside[1:, 1:-1, 1:-1]
    for i, j, k in np.argwhere(s):
        j, k = j + 1, k + 1
        q = [idx[i, j - 1, k - 1], idx[i, j, k - 1], idx[i, j, k], idx[i, j - 1, k]]
        quads.append(q if inside[i, j, k] else q[::-1])
    s = inside[1:-1, :-1, 1:-1] != inside[1:-1, 1:, 1:-1]
    for i, j, k in np.argwhere(s):
        i, k = i + 1, k + 1
        q = [idx[i - 1, j, k - 1], idx[i - 1, j, k], idx[i, j, k], idx[i, j, k - 1]]
        quads.append(q if inside[i, j, k] else q[::-1])
    s = inside[1:-1, 1:-1, :-1] != inside[1:-1, 1:-1, 1:]
    for i, j, k in np.argwhere(s):
        i, j = i + 1, j + 1
        q = [idx[i - 1, j - 1, k], idx[i, j - 1, k], idx[i, j, k], idx[i - 1, j, k]]
        quads.append(q if inside[i, j, k] else q[::-1])
    for _ in range(project):  # pull the vertices onto the surface
        e = h * 0.25
        g = np.stack([(f(V + np.array(d) * e) - f(V - np.array(d) * e)) / (2 * e)
                      for d in ((1, 0, 0), (0, 1, 0), (0, 0, 1))], axis=-1)
        V = V - (f(V) / np.maximum((g * g).sum(-1), 1e-9))[..., None] * g
    return V, np.array(quads, dtype=np.int64)


def mesh_from(name, V, Q, mats):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in V], [], [tuple(int(i) for i in q) for q in Q])
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    for m in mats:
        me.materials.append(m)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return ob


def decimate(ob, tris):
    """Collapses the mesh to about tris triangles."""
    now = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if now > tris:
        mod = ob.modifiers.new("dec", "DECIMATE")
        mod.ratio = tris / now
        hk._apply(ob)
    return ob


def unwrap(ob, margin=0.004):
    """Smart UV project into 'UVMap'; returns cells per UV unit."""
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    if "UVMap" not in ob.data.uv_layers:
        ob.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=margin, area_weight=0.0,
                             scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    me = ob.data
    uv = me.uv_layers["UVMap"].data
    a3 = a2 = 0.0
    for p in me.polygons:
        lo = list(p.loop_indices)
        for i in range(1, len(lo) - 1):
            P = [me.vertices[me.loops[lo[k]].vertex_index].co for k in (0, i, i + 1)]
            U = [uv[lo[k]].uv for k in (0, i, i + 1)]
            a3 += ((P[1] - P[0]).cross(P[2] - P[0])).length
            a2 += abs((U[1] - U[0]).cross(U[2] - U[0]))
    return math.sqrt(a3 / max(a2, 1e-12))


def raster(ob, W, H):
    """Texel centres covered by the mesh's UVs: returns (rows, cols, P, N)
    with the surface point and smooth normal behind each covered texel."""
    me = ob.data
    me.calc_loop_triangles()
    nt = len(me.loop_triangles)
    li = np.zeros(nt * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("loops", li)
    vi = np.zeros(nt * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("vertices", vi)
    uv = np.zeros(len(me.loops) * 2)
    me.uv_layers["UVMap"].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)[li].reshape(nt, 3, 2) * (W, H)
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)[vi].reshape(nt, 3, 3)
    nv = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", nv)
    nv = nv.reshape(-1, 3)[vi].reshape(nt, 3, 3)
    R, C, P, N = [], [], [], []
    for t in range(nt):
        a, b, c = uv[t]
        x0, x1 = int(math.floor(min(a[0], b[0], c[0]))), int(math.ceil(max(a[0], b[0], c[0])))
        y0, y1 = int(math.floor(min(a[1], b[1], c[1]))), int(math.ceil(max(a[1], b[1], c[1])))
        xs, ys = np.meshgrid(np.arange(max(x0, 0), min(x1 + 1, W)) + 0.5,
                             np.arange(max(y0, 0), min(y1 + 1, H)) + 0.5)
        if xs.size == 0:
            continue
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        l0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / d
        l1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / d
        l2 = 1 - l0 - l1
        m = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        if not m.any():
            continue
        L = np.stack([l0[m], l1[m], l2[m]], -1)
        R.append((ys[m] - 0.5).astype(np.int64))
        C.append((xs[m] - 0.5).astype(np.int64))
        P.append(L @ co[t])
        n = L @ nv[t]
        N.append(n / np.linalg.norm(n, axis=-1, keepdims=True))
    return np.concatenate(R), np.concatenate(C), np.concatenate(P), np.concatenate(N)


def dilate(img, mask, steps=6):
    """Spreads painted texels into the unpainted ones round them, so
    island edges don't bleed dark when the texture is filtered."""
    img, mask = img.copy(), mask.copy()
    for _ in range(steps):
        acc = np.zeros_like(img)
        cnt = np.zeros(mask.shape)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            m = np.roll(np.roll(mask, dy, 0), dx, 1)
            acc += np.roll(np.roll(img, dy, 0), dx, 1) * m.reshape(m.shape + (1,) * (img.ndim - 2))
            cnt += m
        new = (~mask) & (cnt > 0)
        img[new] = (acc[new].T / cnt[new]).T
        mask = mask | new
    return img, mask


def normals_from_height(h, du, dv, strength=1.0):
    """Tangent-space normal colours (H, W, 3) from a height field in cells."""
    gy, gx = np.gradient(h, dv, du)
    n = np.stack([-gx * strength, -gy * strength, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def frames_facing(pts, toward):
    """Sweep frames whose first normal leans toward the direction toward,
    so a tube's texture keeps its v = 0 ridge on that side."""
    T = []
    for i in range(len(pts)):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, len(pts) - 1)]
        T.append((b - a).normalized())
    R = Vector(toward).normalized()
    N, prev = [], None
    for t in T:
        n = R - t * R.dot(t)
        if n.length < 0.15 and prev is not None:
            n = prev - t * prev.dot(t)
        n = n.normalized()
        if prev is not None and n.dot(prev) < 0.2:
            n = (prev - t * prev.dot(t)).normalized().lerp(n, 0.3).normalized()
        N.append(n)
        prev = n
    B = [T[i].cross(N[i]) for i in range(len(pts))]
    return T, N, B
