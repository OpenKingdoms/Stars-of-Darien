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
