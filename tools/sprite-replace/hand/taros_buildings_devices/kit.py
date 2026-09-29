"""Parts for the Taros buildings and devices, run inside Blender.

Frame as handkit: 1 unit = 1 map cell, -Y toward the classic camera, Z up,
origin on the feature's anchor. A sprite point (x, y, z) lands on column
hx + 16x, row hy - 16y - 8z.

Surfaces get small textures made here from noise, in colours sampled from
the sprites, so no sprite pixel is baked into a model unless a part asks
for handkit.project_paint.
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.dirname(os.path.dirname(HERE))
if KIT not in sys.path:
    sys.path.insert(0, KIT)
import handkit as hk  # noqa: E402

FAMILY = "taros_buildings_devices"
ROOT = r"D:\OKReplace"
OUT = os.path.join(ROOT, "hand", FAMILY)
SPRITES = os.path.join(ROOT, "sprites")
CATALOG = {r["name"]: r for r in json.load(open(os.path.join(ROOT, "catalog.json")))}


# ---- colour and materials ---------------------------------------------------

def lin(c):
    """sRGB 0-255 to linear 0-1."""
    out = []
    for v in c[:3]:
        v = v / 255.0
        out.append(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4)
    return tuple(out)


LIFT = 1.0  # the sprites have their light painted in; the lit model needs a brighter albedo


# the buildings' pictures are darker than handkit's sun and sky light a
# roof; build.py sets this below 1 for them, scaling every colour
TONE = 1.0


def lift(c, k=None):
    k = (LIFT if k is None else k) * TONE
    return tuple(min(255, v * k) for v in c[:3])


_MATS = {}


def mat(name, srgb, rough=0.85, metal=0.0, spec=0.25, k=None):
    """A plain material in a sprite colour (sRGB 0-255)."""
    key = ("m", name)
    if key in _MATS:
        return _MATS[key]
    m = hk.pbr("tar_" + name, lin(lift(srgb, k)), rough=rough, metal=metal)
    m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = spec
    _MATS[key] = m
    return m


def tmat(name, img, rough=0.9, metal=0.0, spec=0.2):
    """A material showing a made texture through the part's UVs."""
    key = ("t", name)
    if key in _MATS:
        return _MATS[key]
    m = bpy.data.materials.new("tar_" + name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    b.inputs["Specular IOR Level"].default_value = spec
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img
    t.interpolation = "Linear"
    t.extension = "REPEAT"
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    _MATS[key] = m
    return m


def reset():
    global TONE
    hk.reset()
    _MATS.clear()
    TONE = 1.0


# ---- made textures ------------------------------------------------------------
# Each returns an (n, n, 3) sRGB 0-1 array that tiles.

def _rng(seed):
    return np.random.default_rng(seed)


def value_noise(n, res, rng):
    g = rng.random((res, res))
    t = np.arange(n) * res / n
    i0 = np.floor(t).astype(int) % res
    i1 = (i0 + 1) % res
    f = t - np.floor(t)
    f = f * f * (3 - 2 * f)
    top = g[i0][:, i0] * (1 - f)[None, :] + g[i0][:, i1] * f[None, :]
    bot = g[i1][:, i0] * (1 - f)[None, :] + g[i1][:, i1] * f[None, :]
    return top * (1 - f)[:, None] + bot * f[:, None]


def fbm(n, rng, octs=((4, 0.45), (8, 0.25), (16, 0.17), (32, 0.13))):
    v = sum(w * value_noise(n, r, rng) for r, w in octs)
    v = (v - v.min()) / max(1e-6, v.max() - v.min())
    return v


def _col(c):
    return np.array(c[:3], np.float32) / 255.0


def _mix(a, b, t):
    return a * (1 - t[..., None]) + b * t[..., None]


def voronoi(n, count, rng):
    """Distances to the nearest and second nearest seed and the nearest's id."""
    pts = rng.random((count, 2)) * n
    offs = np.array([(dx, dy) for dx in (-n, 0, n) for dy in (-n, 0, n)], np.float32)
    P = (pts[None, :, :] + offs[:, None, :]).reshape(-1, 2).astype(np.float32)
    d1 = np.zeros((n, n), np.float32)
    d2 = np.zeros((n, n), np.float32)
    idx = np.zeros((n, n), np.int32)
    xs = np.arange(n, dtype=np.float32) + 0.5
    for y0 in range(0, n, 16):
        ys = np.arange(y0, min(n, y0 + 16), dtype=np.float32) + 0.5
        d = np.sqrt((xs[None, :, None] - P[None, None, :, 0]) ** 2 + (ys[:, None, None] - P[None, None, :, 1]) ** 2)
        part = np.argpartition(d, 1, axis=-1)[..., :2]
        a = np.take_along_axis(d, part, -1)
        swap = a[..., 0] > a[..., 1]
        first = np.where(swap, part[..., 1], part[..., 0])
        d1[y0:y0 + 16] = np.minimum(a[..., 0], a[..., 1])
        d2[y0:y0 + 16] = np.maximum(a[..., 0], a[..., 1])
        idx[y0:y0 + 16] = first % count
    return d1, d2, idx


def tex_cobble(base, vein, n=256, count=70, seed=1, var=0.18, vein_w=2.2, dark=None):
    """Irregular stones with coloured joints, the Taros red-veined paving."""
    rng = _rng(seed)
    d1, d2, idx = voronoi(n, count, rng)
    shade = 1 + var * (rng.random(count)[idx] - 0.5) * 2
    noise = fbm(n, rng)
    img = _col(base)[None, None, :] * shade[..., None] * (0.82 + 0.36 * noise)[..., None]
    edge = np.clip(1 - (d2 - d1) / vein_w, 0, 1)
    img = _mix(img, _col(vein), edge)
    if dark is not None:
        core = np.clip(1 - (d2 - d1) / (vein_w * 0.45), 0, 1)
        img = _mix(img, _col(dark), core * 0.7)
    return np.clip(img, 0, 1)


def tex_courses(base, joint, n=256, rows=8, per_row=4, seed=2, var=0.16, jw=2.0, vert=False, moss=None,
                moss_amt=0.0):
    """Coursed blocks or pavers: rows of stones of uneven length, dark joints."""
    rng = _rng(seed)
    img = np.zeros((n, n, 3), np.float32)
    h = n / rows
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    noise = fbm(n, rng)
    base_c, joint_c = _col(base), _col(joint)
    jmask = np.zeros((n, n), np.float32)
    shade = np.ones((n, n), np.float32)
    for r in range(rows):
        y0, y1 = int(r * h), int((r + 1) * h)
        cuts = np.sort(rng.random(per_row - 1)) * n if per_row > 1 else np.array([])
        cuts = np.concatenate([[0], cuts, [n]]) + rng.random() * n
        for k in range(len(cuts) - 1):
            a, b = cuts[k], cuts[k + 1]
            s = 1 + var * (rng.random() - 0.5) * 2
            for off in (-n, 0):
                lo, hi = int(max(0, a + off)), int(min(n, b + off))
                if hi > lo:
                    shade[y0:y1, lo:hi] = s
                    for c in (a + off, b + off):
                        c = int(round(c))
                        if 0 <= c < n:
                            jmask[y0:y1, max(0, c - int(jw // 2)):min(n, c + int(math.ceil(jw / 2)))] = 1
        jmask[max(0, y0):min(n, y0 + int(jw)), :] = 1
    img = base_c[None, None, :] * shade[..., None] * (0.8 + 0.4 * noise)[..., None]
    img = _mix(img, joint_c, jmask * 0.9)
    if moss is not None:
        m = fbm(n, rng, ((4, 0.6), (8, 0.3), (32, 0.1)))
        img = _mix(img, _col(moss), np.clip((m - (1 - moss_amt)) * 4, 0, 1) * 0.8)
    if vert:
        img = np.transpose(img, (1, 0, 2))
    return np.clip(img, 0, 1)


def tex_speckle(base, dots, n=128, seed=3, density=0.35, size=2):
    """Gravel: a base colour sown thick with light and dark grains."""
    rng = _rng(seed)
    img = np.ones((n, n, 3), np.float32) * _col(base)
    m = n // size
    for c, p in dots:
        sel = rng.random((m, m)) < p * density
        sel = np.repeat(np.repeat(sel, size, 0), size, 1)[:n, :n]
        img[sel] = _col(c) * (0.85 + 0.3 * rng.random((sel.sum(), 1)))
    return np.clip(img, 0, 1)


def tex_mottle(base, dark, light=None, n=128, seed=4, amt=1.0, octs=None):
    """A mottled surface: rough stone, hide, soot."""
    rng = _rng(seed)
    v = fbm(n, rng) if octs is None else fbm(n, rng, octs)
    img = _mix(np.ones((n, n, 3), np.float32) * _col(dark), np.ones((n, n, 3), np.float32) * _col(base),
               np.clip(v * 1.6 * amt + (1 - amt) * 0.8, 0, 1))
    if light is not None:
        img = _mix(img, np.ones((n, n, 3), np.float32) * _col(light), np.clip((v - 0.7) * 3, 0, 1))
    return np.clip(img, 0, 1)


def tex_planks(base, gap, n=128, count=8, seed=5, var=0.2):
    """Boards running along V with dark gaps between them and grain."""
    rng = _rng(seed)
    w = rng.random(count) + 0.6
    edges = np.concatenate([[0], np.cumsum(w) / w.sum() * n])
    img = np.zeros((n, n, 3), np.float32)
    xx = np.arange(n)
    for k in range(count):
        a, b = edges[k], edges[k + 1]
        s = 1 + var * (rng.random() - 0.5) * 2
        sel = (xx >= a) & (xx < b)
        img[:, sel] = _col(base) * s
        lo, hi = int(a), int(min(n - 1, a + 1.5))
        img[:, lo:hi + 1] = _col(gap)
    streak = value_noise(n, 32, rng)
    streak = np.repeat(streak.mean(0, keepdims=True), n, 0) * 0.6 + value_noise(n, 8, rng) * 0.4
    img *= (0.75 + 0.45 * streak)[..., None]
    return np.clip(img, 0, 1)


_IMGS = {}


def image(name, arr):
    """A packed Blender image from an sRGB 0-1 array, top row first."""
    if name in bpy.data.images:
        return bpy.data.images[name]
    n_h, n_w = arr.shape[:2]
    im = bpy.data.images.new(name, n_w, n_h, alpha=True)
    rgba = np.ones((n_h, n_w, 4), np.float32)
    rgba[..., :3] = arr[::-1]
    im.pixels[:] = rgba.ravel()
    im.pack()
    # made here from noise and sampled colours, so it ships (okpaint.py)
    return hk.generated(im)


def texmat(name, arr_fn, rough=0.9, metal=0.0, spec=0.2):
    key = ("t", name)
    if key in _MATS:
        return _MATS[key]
    return tmat(name, image("tar_" + name, arr_fn() * TONE), rough, metal, spec)


# ---- meshes ---------------------------------------------------------------------

def mesh(name, verts, faces, mt=None, smooth=None, recalc=True):
    """recalc=False keeps the faces' winding, for open sheets built facing up."""
    bm = bmesh.new()
    vs = [bm.verts.new(v) for v in verts]
    for f in faces:
        try:
            bm.faces.new([vs[i] for i in f])
        except ValueError:
            pass
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    ob = hk._object(name, bm, mt)
    if smooth:
        hk.smooth(ob, smooth)
    return ob


def loft(rings, mt=None, name="loft", cap0=True, cap1=True, closed=True, smooth=None):
    """Faces between successive rings of equal length (a ring of one point
    is an apex). closed joins each ring's last point to its first."""
    verts, faces, idx = [], [], []
    for r in rings:
        idx.append(list(range(len(verts), len(verts) + len(r))))
        verts += [tuple(p) for p in r]
    for A, B in zip(idx, idx[1:]):
        n = max(len(A), len(B))
        m = n if closed else n - 1
        for s in range(m):
            a0, a1 = A[s % len(A)], A[(s + 1) % len(A)]
            b0, b1 = B[s % len(B)], B[(s + 1) % len(B)]
            q = [a0, a1, b1, b0]
            q = [v for k, v in enumerate(q) if v not in q[:k]]
            if len(q) >= 3:
                faces.append(q)
    if cap0 and len(idx[0]) > 2:
        faces.append(list(reversed(idx[0])))
    if cap1 and len(idx[-1]) > 2:
        faces.append(idx[-1])
    return mesh(name, verts, faces, mt, smooth)


def rect(cx, cy, w, d, rz=0.0, z=0.0):
    c, s = math.cos(math.radians(rz)), math.sin(math.radians(rz))
    out = []
    for px, py in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)):
        out.append((cx + px * c - py * s, cy + px * s + py * c, z))
    return out


def block(w, d, h, x=0.0, y=0.0, z=0.0, rz=0.0, mt=None, name="block", top=None, taper=1.0):
    """A box w x d x h standing on z, turned rz degrees about its own centre;
    taper scales the top face."""
    tw, td = (w * taper, d * taper) if top is None else top
    return loft([rect(x, y, w, d, rz, z), rect(x, y, tw, td, rz, z + h)], mt, name)


def prism(poly, z0, z1, mt=None, name="prism"):
    return loft([[(x, y, z0) for x, y in poly], [(x, y, z1) for x, y in poly]], mt, name)


def cyl(r0, h, r1=None, seg=12, x=0.0, y=0.0, z=0.0, mt=None, name="cyl", smooth=None):
    ob = hk.cylinder(r0, h, r_top=r1, seg=seg, z=z, x=x, y=y, mat=mt, name=name)
    if smooth:
        hk.smooth(ob, smooth)
    return ob


def ngon(r, seg, x=0.0, y=0.0, z=0.0, phase=0.0):
    return [(x + r * math.cos(phase + 2 * math.pi * i / seg), y + r * math.sin(phase + 2 * math.pi * i / seg), z)
            for i in range(seg)]


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


def tube(pts, radii, seg=6, sub=3, mt=None, name="tube", flat=1.0, up=(0, 0, 1), shade=60):
    """A tapered tube along a smoothed path; a radius of 0 makes a point."""
    path = _catmull(pts, sub)
    n = len(path)
    upv = Vector(up)
    rings = []
    prev = None
    for k, (p, u) in enumerate(path):
        a = path[max(0, k - 1)][0]
        b = path[min(n - 1, k + 1)][0]
        T = (b - a).normalized()
        if prev is None:
            N = upv - T * upv.dot(T)
            if N.length < 1e-4:
                N = Vector((1, 0, 0)) - T * T.x
            N.normalize()
        else:
            N = (prev - T * prev.dot(T)).normalized()
        prev = N
        B = T.cross(N)
        i = min(int(u), len(radii) - 2)
        f = u - i
        r = radii[i] * (1 - f) + radii[i + 1] * f
        if r < 1e-3:
            rings.append([tuple(p)])
        else:
            rings.append([tuple(p + (N * math.cos(2 * math.pi * s / seg) * flat + B * math.sin(2 * math.pi * s / seg)) * r)
                          for s in range(seg)])
    return loft(rings, mt, name, smooth=shade)


def lathe(profile, seg=16, mt=None, name="lathe", x=0.0, y=0.0, z=0.0, phase=0.0, smooth=None):
    """A solid of revolution from (r, z) points bottom to top; r 0 is on the axis."""
    rings = []
    for r, zz in profile:
        if r < 1e-4:
            rings.append([(x, y, z + zz)])
        else:
            rings.append(ngon(r, seg, x, y, z + zz, phase))
    return loft(rings, mt, name, smooth=smooth)


def torus(R, r, x=0.0, y=0.0, z=0.0, seg=24, rseg=6, mt=None, name="torus", axis="Z"):
    verts, faces = [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        for j in range(rseg):
            b = 2 * math.pi * j / rseg
            rr = R + r * math.cos(b)
            verts.append((rr * math.cos(a), rr * math.sin(a), r * math.sin(b)))
    for i in range(seg):
        for j in range(rseg):
            i2, j2 = (i + 1) % seg, (j + 1) % rseg
            faces.append([i * rseg + j, i2 * rseg + j, i2 * rseg + j2, i * rseg + j2])
    ob = mesh(name, verts, faces, mt, smooth=70)
    M = Matrix.Identity(4)
    if axis == "X":
        M = Matrix.Rotation(math.pi / 2, 4, "Y")
    elif axis == "Y":
        M = Matrix.Rotation(math.pi / 2, 4, "X")
    ob.data.transform(Matrix.Translation((x, y, z)) @ M)
    return ob


def rock(c, size, seed, mt=None, name="rock", squash=0.7, jitter=0.3):
    """A small jittered, beveled lump for rubble."""
    rnd = random.Random(seed)
    sx, sy, sz = (size if isinstance(size, (tuple, list)) else (size, size, size * squash))
    verts = []
    for vx in (-1, 1):
        for vy in (-1, 1):
            for vz in (-1, 1):
                verts.append((vx * sx / 2 * (1 + rnd.uniform(-jitter, jitter)),
                              vy * sy / 2 * (1 + rnd.uniform(-jitter, jitter)),
                              vz * sz / 2 * (1 + rnd.uniform(-jitter, jitter))))
    faces = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]]
    ob = mesh(name, verts, faces, mt)
    M = (Matrix.Translation(c) @ Matrix.Rotation(rnd.uniform(0, 6.3), 4, "Z")
         @ Matrix.Rotation(rnd.uniform(-0.4, 0.4), 4, "X") @ Matrix.Rotation(rnd.uniform(-0.4, 0.4), 4, "Y"))
    ob.data.transform(M)
    return ob


def heightfield(x0, x1, y0, y1, nx, ny, fn, mt=None, name="mound", base=-0.05):
    """A lump over a rectangle, its top z = fn(x, y), facing up. Cells whose
    corners are all at base are left out."""
    verts, faces = [], []
    grid = {}
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = x0 + (x1 - x0) * i / nx
            y = y0 + (y1 - y0) * j / ny
            z = max(base, fn(x, y))
            grid[i, j] = len(verts)
            verts.append((x, y, z))
    for j in range(ny):
        for i in range(nx):
            q = [grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]]
            if all(verts[k][2] <= base + 1e-4 for k in q):
                continue
            faces.append(q)
    ob = mesh(name, verts, faces, mt, smooth=50, recalc=False)
    return ob


def place(ob, M):
    ob.data.transform(M)
    return ob


def rotz(ob, deg, pivot=(0, 0, 0)):
    p = Vector(pivot)
    return place(ob, Matrix.Translation(p) @ Matrix.Rotation(math.radians(deg), 4, "Z") @ Matrix.Translation(-p))


def move(ob, d):
    return place(ob, Matrix.Translation(Vector(d)))


# ---- sturdiness ------------------------------------------------------------------------
# The classic picture draws height at half scale, so a pixel-exact fit makes
# tall things spindly in true 3D. Tall parts are built wider and lower than
# the exact fit: GIRTH on their thickness, RISE on their height.
GIRTH = 1.18
RISE = 0.86


def sturdy(parts, girth=GIRTH, rise=RISE, cx=0.0, cy=0.0, z0=0.0):
    """Spreads parts girth times about (cx, cy) and lowers them rise times
    toward z0, for a whole prop that stands tall."""
    M = (Matrix.Translation((cx, cy, z0)) @ Matrix.Diagonal((girth, girth, rise, 1.0))
         @ Matrix.Translation((-cx, -cy, -z0)))
    for p in parts:
        p.data.transform(M)
    return parts


def uv_box(ob, k=0.25, ox=0.0, oy=0.0):
    """Box-projected UVs from the part's coordinates: k texture repeats per cell."""
    me = ob.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        n = p.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if ax == 2:
                u, v = co.x, co.y
            elif ax == 1:
                u, v = co.x, co.z
            else:
                u, v = co.y, co.z
            uv.data[li].uv = (u * k + ox, v * k + oy)
    return ob


def uv_cyl(ob, k=0.25, cx=0.0, cy=0.0):
    """Cylindrical UVs: around the axis by arc length, up by height; tops boxed."""
    me = ob.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        n = p.normal
        top = abs(n.z) > 0.7
        cs = [me.vertices[me.loops[li].vertex_index].co for li in p.loop_indices]
        angs = [math.atan2(c.y - cy, c.x - cx) for c in cs]
        ref = angs[0]
        for li, c, a in zip(p.loop_indices, cs, angs):
            if top:
                uv.data[li].uv = (c.x * k, c.y * k)
            else:
                while a - ref > math.pi:
                    a -= 2 * math.pi
                while a - ref < -math.pi:
                    a += 2 * math.pi
                r = math.hypot(c.x - cx, c.y - cy)
                uv.data[li].uv = (a * max(r, 0.5) * k, c.z * k)
    return ob


def two_tone(ob, hi, nz=0.6):
    """Upward faces of a part take a second material (a cap, a lit top)."""
    ob.data.materials.append(hi)
    idx = len(ob.data.materials) - 1
    for p in ob.data.polygons:
        if p.normal.z > nz:
            p.material_index = idx
    return ob


def teeth(p0, p1, z, w, h, d, n, mt=None, name="merlon", rz=None, taper=1.0):
    """n merlons evenly along p0-p1 (xy), standing on z."""
    p0, p1 = Vector(p0[:2]), Vector(p1[:2])
    ang = math.degrees(math.atan2(p1.y - p0.y, p1.x - p0.x)) if rz is None else rz
    out = []
    for i in range(n):
        t = (i + 0.5) / n
        c = p0.lerp(p1, t)
        out.append(block(w, d, h, c.x, c.y, z, ang, mt, name, taper=taper))
    return out


def standard_view():
    vs = bpy.context.scene.view_settings
    try:
        vs.view_transform = "Standard"
        vs.look = "None"
    except TypeError:
        pass


def tris(parts):
    return sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in parts)


def finish(name, parts, painted=()):
    """Joins, exports and renders one model into the family's folders."""
    r = CATALOG[name]
    n = tris(parts)
    for p in parts:
        if not p.data.uv_layers:
            p.data.uv_layers.new(name="UVMap")
    extras = {"feature": name, "handBuilt": FAMILY}
    if painted:
        extras["projectPaint"] = list(painted)
    glb = os.path.join(OUT, "models", name + ".glb")
    parts[0].name = name
    ob = hk.finish(parts, glb, extras)
    print("HANDKIT_TRIS", name, n)
    scene = bpy.context.scene
    standard_view()
    hk.renders(ob, os.path.join(OUT, "renders"), name, os.path.join(SPRITES, name + ".png"),
               tuple(r["sprite"]["hotspot"]), scale=2)
    return ob, n


# ---- reading the picture ------------------------------------------------------

class Pic:
    """The sprite's frame: column hx + 16x, row hy - 16y - 8z."""

    def __init__(self, name):
        r = CATALOG[name]
        self.name = name
        self.hx, self.hy = r["sprite"]["hotspot"]
        self.w, self.h = r["sprite"]["w"], r["sprite"]["h"]
        self.fx, self.fz = r["footprint"]

    def at(self, col, row, y=0.0):
        """The point drawn at (col, row), taken at depth y."""
        return ((col - self.hx) / 16.0, y, (self.hy - row - 16.0 * y) / 8.0)

    def ground(self, col, row, z=0.0):
        """The point drawn at (col, row), taken at height z."""
        return ((col - self.hx) / 16.0, (self.hy - row - 8.0 * z) / 16.0, z)

    def path(self, pts):
        """[(col, row, y), ...] to points; a 4th value fixes z instead of y."""
        out = []
        for p in pts:
            if len(p) == 4 and p[3] is not None:
                out.append(self.ground(p[0], p[1], p[3]))
            else:
                out.append(self.at(p[0], p[1], p[2]))
        return out


def blob_cache(name):
    """Rubble pieces findblobs.py read off the sprite: [col, row, w, h, n, r, g, b]."""
    p = os.path.join(HERE, "blobs", name + ".json")
    return json.load(open(p)) if os.path.exists(p) else []


# ---- timber and cloth ------------------------------------------------------------

def slats(p0, p1, z0, h, n, mt, thick=0.12, gap=0.25, jitter=0.25, seed=0, name="slat", tops=None, lean=0.0):
    """A wall of n upright boards from p0 to p1 (xy), standing on z0, h tall,
    each a little uneven; gap is the fraction of each board's share left
    open. tops(t) can give a height per board along the run."""
    rnd = random.Random(seed)
    p0, p1 = Vector(p0[:2]), Vector(p1[:2])
    L = (p1 - p0).length
    ang = math.degrees(math.atan2(p1.y - p0.y, p1.x - p0.x))
    w = L / n * (1 - gap)
    out = []
    for i in range(n):
        t = (i + 0.5) / n
        c = p0.lerp(p1, t)
        hh = (tops(t) if tops else h) * (1 + rnd.uniform(-jitter, jitter) * 0.3)
        b = block(w * rnd.uniform(0.85, 1.1), thick, hh, c.x, c.y, z0 - 0.05, ang + rnd.uniform(-3, 3), mt, name)
        if lean:
            place(b, Matrix.Translation((c.x, c.y, z0)) @ Matrix.Rotation(rnd.uniform(-lean, lean), 4,
                                                                       Vector((p1 - p0).normalized()).to_3d())
                  @ Matrix.Translation((-c.x, -c.y, -z0)))
        out.append(b)
    return out


def board(a, b, w, t, mt, name="board", up=(0, 0, 1)):
    """A plank from point a to point b, w wide and t thick, its face toward up."""
    a, b = Vector(a), Vector(b)
    T = (b - a).normalized()
    U = Vector(up)
    N = (U - T * U.dot(T))
    if N.length < 1e-4:
        N = Vector((0, 1, 0)) - T * T.y
    N.normalize()
    S = T.cross(N)
    pts = []
    for e in (a, b):
        pts.append([tuple(e + S * sx * w / 2 + N * nz * t / 2) for sx, nz in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    return loft(pts, mt, name)


def sheet(x0, x1, y0, y1, nx, ny, fn, mt, name="sheet", thick=0.0):
    """A draped surface z = fn(x, y) over a rectangle (a hide, a tarp);
    thick > 0 gives it an underside."""
    verts, faces = [], []
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = x0 + (x1 - x0) * i / nx
            y = y0 + (y1 - y0) * j / ny
            verts.append((x, y, fn(x, y)))
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            faces.append([a, a + 1, a + nx + 2, a + nx + 1])
    if thick > 0:
        m = len(verts)
        verts += [(x, y, z - thick) for x, y, z in verts]
        faces += [[m + k for k in reversed(f)] for f in faces[:]]
    return mesh(name, verts, faces, mt, smooth=60, recalc=False)


# ---- masonry and ruin parts ------------------------------------------------------

def ribbon(A, B, mt, want, name="ribbon", smooth=None):
    """Quads between two point runs of equal length, each face turned to
    agree with want(centre) (an outward direction)."""
    bm = bmesh.new()
    va = [bm.verts.new(p) for p in A]
    vb = [bm.verts.new(p) for p in B]
    for i in range(len(A) - 1):
        q = [va[i], va[i + 1], vb[i + 1], vb[i]]
        q = [v for k, v in enumerate(q) if all((v.co - u.co).length > 1e-6 for u in q[:k])]
        if len(q) < 3:
            continue
        try:
            f = bm.faces.new(q)
        except ValueError:
            continue
        f.normal_update()
        c = f.calc_center_median()
        if f.normal.dot(Vector(want(c))) < 0:
            f.normal_flip()
    ob = hk._object(name, bm, mt)
    if smooth:
        hk.smooth(ob, smooth)
    return ob


def pyramid(cx, cy, z, w, d, h, rz=0.0, mt=None, name="pyramid", apex=None):
    """A four-sided spire on a w x d base at height z."""
    ax, ay = (cx, cy) if apex is None else apex
    return loft([rect(cx, cy, w, d, rz, z), [(ax, ay, z + h)]], mt, name)


def jagged(t, amp, seed, base=0.0, freq=5.0):
    """A ragged 0..1 profile along t for broken wall tops."""
    rnd = random.Random(seed)
    ph = [rnd.uniform(0, 6.3) for _ in range(3)]
    v = (math.sin(freq * t * 6.28 + ph[0]) * 0.5 + math.sin(freq * 2.3 * t * 6.28 + ph[1]) * 0.3
         + math.sin(freq * 5.1 * t * 6.28 + ph[2]) * 0.2)
    return base + amp * v


def wall_run(p0, p1, z0, height, thick, mt, n=None, seg=0.5, top_mt=None, name="wall", seed=0, out=None):
    """A straight wall p0-p1 standing on z0. height is a number or a function
    of t (0..1 along the run); a varying height is built from short
    sections, so a broken top steps and tears instead of sloping."""
    p0, p1 = Vector(p0[:2]), Vector(p1[:2])
    L = (p1 - p0).length
    ang = math.degrees(math.atan2(p1.y - p0.y, p1.x - p0.x))
    if not callable(height):
        b = block(L, thick, height, (p0.x + p1.x) / 2, (p0.y + p1.y) / 2, z0, ang, mt, name)
        if top_mt is not None:
            two_tone(b, top_mt)
        return [b]
    n = n or max(2, int(round(L / seg)))
    rnd = random.Random(seed)
    parts = []
    for i in range(n):
        t = (i + 0.5) / n
        h = height(t)
        if h <= 0.05:
            continue
        c = p0.lerp(p1, t)
        b = block(L / n * 1.02, thick * rnd.uniform(0.9, 1.05), h, c.x, c.y, z0, ang, mt, name)
        if top_mt is not None:
            two_tone(b, top_mt)
        parts.append(b)
    return parts


def mound(x0, x1, y0, y1, bumps, mt, cut=0.08, nx=None, ny=None, grit=0.08, seed=1, name="mound", z0=0.0,
          lumps=1.0, cell=0.65):
    """A heap from gaussian bumps [(x, y, rx, ry, h), ...], roughened with
    smaller lumps; the ground around it is cut away where the heap falls
    under `cut`, so its edge follows the bumps. x0 None sizes the box to
    the bumps. Returns (object, height fn)."""
    rnd = random.Random(seed)
    if x0 is None:
        x0 = min(bx - 2.6 * rx for bx, by, rx, ry, h in bumps)
        x1 = max(bx + 2.6 * rx for bx, by, rx, ry, h in bumps)
        y0 = min(by - 2.6 * ry for bx, by, rx, ry, h in bumps)
        y1 = max(by + 2.6 * ry for bx, by, rx, ry, h in bumps)
    small = []
    for bx, by, rx, ry, h in bumps:
        for _ in range(int(rx * ry * 2.5 * lumps)):
            a, r = rnd.uniform(0, 6.283), math.sqrt(rnd.random())
            small.append((bx + r * rx * 1.2 * math.cos(a), by + r * ry * 1.2 * math.sin(a), rnd.uniform(0.3, 0.7),
                          rnd.uniform(0.3, 0.7), rnd.uniform(0.12, 0.35) * min(1.0, h) * (1 if rnd.random() < 0.7 else -1)))
    g = [(rnd.uniform(2.5, 6), rnd.uniform(2.5, 6), rnd.uniform(0, 6.3)) for _ in range(3)]

    def fn(x, y):
        z = 0.0
        for bx, by, rx, ry, h in bumps:
            z += h * math.exp(-(((x - bx) / rx) ** 2 + ((y - by) / ry) ** 2))
        k = min(1.0, z * 2)
        for bx, by, rx, ry, h in small:
            dx, dy = (x - bx) / rx, (y - by) / ry
            if dx * dx + dy * dy < 6:
                z += h * math.exp(-(dx * dx + dy * dy)) * k
        z += grit * sum(math.sin(a * x + c) * math.sin(b * y + c * 1.3) for a, b, c in g) / 3 * k
        return z0 + z - cut

    nx = nx or max(4, int((x1 - x0) / cell))
    ny = ny or max(4, int((y1 - y0) / cell))
    ob = heightfield(x0, x1, y0, y1, nx, ny, fn, mt, name, base=z0 - 0.06)
    uv_box(ob, 0.45)
    return ob, fn, (x0, x1, y0, y1)


def tex_rubble(base, gap, light=None, n=256, count=90, seed=7, var=0.35):
    """Packed broken stones: cells of varied shade, dark gaps, a lit edge."""
    rng = _rng(seed)
    d1, d2, idx = voronoi(n, count, rng)
    shade = 1 + var * (rng.random(count)[idx] - 0.5) * 2
    noise = fbm(n, rng)
    img = _col(base)[None, None, :] * shade[..., None] * (0.75 + 0.5 * noise)[..., None]
    if light is not None:
        up = np.clip((np.roll(d1, 2, 0) - d1) * 0.6, 0, 1)
        img = _mix(img, _col(light), up * 0.8)
    edge = np.clip(1 - (d2 - d1) / 4.5, 0, 1) ** 0.7
    img = _mix(img, _col(gap), edge)
    return np.clip(img, 0, 1)


def scatter(fn, box, count, size, mats, seed=1, zmin=0.05, squash=(0.45, 0.8), inside=None, name="stone",
            sink=0.35):
    """Stones strewn over a surface z = fn(x, y) inside box (x0, x1, y0, y1);
    size is (min, max) cells, mats a list picked at random."""
    rnd = random.Random(seed)
    x0, x1, y0, y1 = box
    out = []
    tries = 0
    while len(out) < count and tries < count * 20:
        tries += 1
        x, y = rnd.uniform(x0, x1), rnd.uniform(y0, y1)
        if inside is not None and not inside(x, y):
            continue
        z = fn(x, y)
        if z < zmin:
            continue
        s = rnd.uniform(*size)
        r = rock((x, y, z + s * (0.5 - sink) * 0.6), (s, s * rnd.uniform(0.6, 1.0), s * rnd.uniform(*squash)),
                 rnd.randrange(10 ** 6), rnd.choice(mats), name)
        uv_box(r, 0.5, rnd.random(), rnd.random())
        out.append(r)
    return out


def inside_poly(P, x, y):
    c = False
    n = len(P)
    for i in range(n):
        (ax, ay), (bx, by) = P[i][:2], P[(i + 1) % n][:2]
        if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay + 1e-12) + ax:
            c = not c
    return c


def load_grid(name):
    """A rubble grid from rubmask.py, as amount(x, y) (0..1), or None."""
    p = os.path.join(HERE, "blobs", name + "_rubble.npy")
    if not os.path.exists(p):
        return None
    g = np.load(p)
    S, X0 = 4, -13.0

    def amount(x, y):
        fi, fj = (x - X0) * S, (y - X0) * S
        i, j = int(math.floor(fi)), int(math.floor(fj))
        if i < 0 or j < 0 or i + 1 >= g.shape[1] or j + 1 >= g.shape[0]:
            return 0.0
        u, v = fi - i, fj - j
        return float(g[j, i] * (1 - u) * (1 - v) + g[j, i + 1] * u * (1 - v) + g[j + 1, i] * (1 - u) * v
                     + g[j + 1, i + 1] * u * v)
    return amount


def rubble_field(amount, ground, height, mt, box=(-13, 13, -13, 13), cell=0.5, thresh=0.12, seed=1,
                 name="rubble"):
    """Rubble heaped where amount(x, y) says, on ground(x, y): height at a
    full amount, roughened. Returns (object, surface fn)."""
    rnd = random.Random(seed)
    waves = [(rnd.uniform(3, 7), rnd.uniform(3, 7), rnd.uniform(0, 6.3)) for _ in range(4)]

    def fn(x, y):
        a = amount(x, y)
        rough = sum(math.sin(p * x + c) * math.sin(q * y + c * 1.7) for p, q, c in waves) / 4
        return ground(x, y) + max(0.0, a - thresh * 0.5) * height * (1 + 0.45 * rough) - 0.04

    x0, x1, y0, y1 = box
    nx, ny = int((x1 - x0) / cell), int((y1 - y0) / cell)
    verts, faces, grid = [], [], {}
    amt = {}
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = x0 + (x1 - x0) * i / nx + rnd.uniform(-0.08, 0.08)
            y = y0 + (y1 - y0) * j / ny + rnd.uniform(-0.08, 0.08)
            grid[i, j] = len(verts)
            amt[i, j] = amount(x, y)
            verts.append((x, y, fn(x, y)))
    for j in range(ny):
        for i in range(nx):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            if max(amt[k] for k in q) < thresh:
                continue
            faces.append([grid[k] for k in q])
    used = sorted({v for f in faces for v in f})
    remap = {v: k for k, v in enumerate(used)}
    ob = mesh(name, [verts[v] for v in used], [[remap[v] for v in f] for f in faces], mt, smooth=45, recalc=False)
    uv_box(ob, 0.45)
    return ob, fn


def strew(amount, fn, count, size, mats, box=(-13, 13, -13, 13), seed=1, thresh=0.25, name="stone"):
    """Stones where the rubble lies, thicker where there is more of it."""
    rnd = random.Random(seed)
    out = []
    tries = 0
    x0, x1, y0, y1 = box
    while len(out) < count and tries < count * 60:
        tries += 1
        x, y = rnd.uniform(x0, x1), rnd.uniform(y0, y1)
        a = amount(x, y)
        if a < thresh or rnd.random() > a:
            continue
        s = rnd.uniform(*size)
        z = fn(x, y)
        r = rock((x, y, z + s * 0.08), (s, s * rnd.uniform(0.6, 1.0), s * rnd.uniform(0.4, 0.75)),
                 rnd.randrange(10 ** 6), rnd.choice(mats), name)
        uv_box(r, 0.5, rnd.random(), rnd.random())
        out.append(r)
    return out


# ---- heaps of rounded boulders ------------------------------------------------------
# Rubble is built as separate lumpy heaps, each a mesh of rings round its
# own centre out to a noise-displaced outline, so no edge follows a grid.

def tex_boulders(base, gap, light=None, n=256, count=60, seed=7, var=0.3):
    """Rounded stones packed tight: each cell domed, lit from above, with
    dark gaps between; reads as a boulder heap from far off."""
    rng = _rng(seed)
    d1, d2, idx = voronoi(n, count, rng)
    f = np.clip(2 * d1 / (d1 + d2 + 1e-6), 0, 1)
    dome = np.sqrt(np.clip(1 - f * f, 0, 1))
    up = np.clip(np.roll(dome, 3, 0) - dome, -1, 1)
    shade = 1 + var * (rng.random(count)[idx] - 0.5) * 2
    noise = fbm(n, rng, ((16, 0.4), (32, 0.35), (64, 0.25)))
    img = _col(base)[None, None, :] * (shade * (0.55 + 0.5 * dome) * (0.85 + 0.3 * noise))[..., None]
    if light is not None:
        img = _mix(img, np.ones((n, n, 3), np.float32) * _col(light), np.clip(-up * 3, 0, 1) * 0.6)
    img = _mix(img, np.ones((n, n, 3), np.float32) * _col(gap), np.clip((f - 0.86) * 7, 0, 1))
    return np.clip(img, 0, 1)


def outline_fn(seed, amp=0.2, lobes=(2, 3, 5, 8, 13)):
    """A closed ragged outline as a radius factor of the angle."""
    rnd = random.Random(seed)
    comps = [(k, rnd.uniform(0, 6.283), amp * rnd.uniform(0.5, 1.0) / math.sqrt(i + 1)) for i, k in enumerate(lobes)]
    return lambda a: 1 + sum(m * math.sin(k * a + ph) for k, ph, m in comps)


def heap(cx, cy, rx, ry, h, mt, ground=None, seed=1, rings=7, seg=30, amp=0.22, lump=0.18, peak=(0.0, 0.0),
         power=1.4, rot=0.0, name="heap", sink=0.06, uvk=0.45):
    """One lumpy heap on ground(x, y): radius rx by ry turned rot degrees,
    h high in the middle, leaning its bulk toward peak (a direction in
    fractions of the radii), falling to nothing at a ragged outline.
    Returns (object, surface fn, inside fn)."""
    ground = ground or (lambda x, y: 0.0)
    rnd = random.Random(seed)
    edge = outline_fn(seed, amp)
    bumps = [(rnd.uniform(-0.8, 0.8), rnd.uniform(-0.8, 0.8), rnd.uniform(0.2, 0.45), rnd.uniform(-1, 1))
             for _ in range(9)]
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    px_, py_ = peak

    def local(x, y):
        dx, dy = x - cx, y - cy
        return (dx * c + dy * s) / rx, (-dx * s + dy * c) / ry

    def rho(u, v):
        a = math.atan2(v, u)
        return math.hypot(u, v) / edge(a)

    def height(u, v):
        r = rho(u, v)
        if r >= 1:
            return 0.0
        base = (1 - r * r) ** power * max(0.25, 1 + 0.7 * (u * px_ + v * py_))
        k = sum(m * math.exp(-((u - bu) ** 2 + (v - bv) ** 2) / (bs * bs)) for bu, bv, bs, m in bumps)
        return h * base * (1 + lump * k)

    def fn(x, y):
        u, v = local(x, y)
        return ground(x, y) + height(u, v)

    def inside(x, y):
        u, v = local(x, y)
        return rho(u, v) < 1

    verts, faces, uvs = [], [], []
    verts.append((cx, cy, 0.0))
    for i in range(1, rings + 1):
        f = i / rings
        f = 1 - (1 - f) ** 1.3
        for j in range(seg):
            a = 2 * math.pi * j / seg
            e = edge(a)
            u = f * e * math.cos(a) * 0.999
            v = f * e * math.sin(a) * 0.999
            x = cx + u * rx * c - v * ry * s
            y = cy + u * rx * s + v * ry * c
            verts.append((x, y, 0.0))
    for k, (x, y, _) in enumerate(verts):
        z = fn(x, y)
        if k >= 1 + (rings - 1) * seg:
            z = ground(x, y) - sink
        verts[k] = (x, y, z)
    for j in range(seg):
        faces.append([0, 1 + j, 1 + (j + 1) % seg])
    for i in range(rings - 1):
        a0, b0 = 1 + i * seg, 1 + (i + 1) * seg
        for j in range(seg):
            j2 = (j + 1) % seg
            faces.append([a0 + j, b0 + j, b0 + j2, a0 + j2])
    ob = mesh(name, verts, faces, mt, smooth=55, recalc=False)
    # face up
    for p in ob.data.polygons:
        if p.normal.z < 0:
            p.flip()
    uv_box(ob, uvk, rnd.random(), rnd.random())
    return ob, fn, inside


def boulder(c, size, seed, mt=None, name="boulder", seg=8, rings=4, rough=0.22):
    """A rounded boulder: a low sphere pushed out of true, squashed to size
    (sx, sy, sz) and set with its base a little sunk at c."""
    rnd = random.Random(seed)
    sx, sy, sz = size if isinstance(size, (tuple, list)) else (size, size, size * 0.7)
    ph = [rnd.uniform(0, 6.3) for _ in range(4)]
    rs = []
    for i in range(1, rings):
        t = math.pi * i / rings
        ring = []
        for j in range(seg):
            a = 2 * math.pi * j / seg
            k = 1 + rough * (math.sin(2 * a + ph[0]) * math.cos(t * 2 + ph[1]) * 0.6
                             + math.sin(3 * a + ph[2]) * 0.4 * math.sin(t + ph[3]))
            ring.append((math.cos(a) * math.sin(t) * k * sx / 2, math.sin(a) * math.sin(t) * k * sy / 2,
                         -math.cos(t) * k * sz / 2))
        rs.append(ring)
    rs = [[(0, 0, -sz / 2)]] + rs + [[(0, 0, sz / 2 * (1 + rnd.uniform(-0.1, 0.1)))]]
    ob = loft(rs, mt, name, smooth=80)
    M = (Matrix.Translation(Vector(c) + Vector((0, 0, sz * 0.32))) @ Matrix.Rotation(rnd.uniform(0, 6.3), 4, "Z")
         @ Matrix.Rotation(rnd.uniform(-0.3, 0.3), 4, "X"))
    ob.data.transform(M)
    return ob


def boulders(fn, inside, box, count, size, mats, seed=1, zmin=0.08, name="boulder", seg=8, bias=1.0,
             centre=None):
    """Rounded boulders set into a heap's surface; bias > 1 crowds them
    toward centre (x, y)."""
    rnd = random.Random(seed)
    x0, x1, y0, y1 = box
    out = []
    tries = 0
    while len(out) < count and tries < count * 40:
        tries += 1
        x, y = rnd.uniform(x0, x1), rnd.uniform(y0, y1)
        if not inside(x, y):
            continue
        if centre is not None and bias > 1:
            d = math.hypot((x - centre[0]) / max(1e-3, (x1 - x0) / 2), (y - centre[1]) / max(1e-3, (y1 - y0) / 2))
            if rnd.random() < min(1.0, d) ** (1 / bias) * 0.8:
                continue
        z = fn(x, y)
        if z < zmin:
            continue
        s = rnd.uniform(*size)
        b = boulder((x, y, z - s * 0.35), (s, s * rnd.uniform(0.7, 1.0), s * rnd.uniform(0.55, 0.8)),
                    rnd.randrange(10 ** 6), rnd.choice(mats), name, seg=seg)
        uv_box(b, 0.5, rnd.random(), rnd.random())
        out.append(b)
    return out


def boulder_heaps(specs, mt, stones, ground=None, seed=1, per_area=1.6, size=(0.4, 0.9), seg=5, name="heap",
                  rings=4, ring_seg=20, power=1.4):
    """Several heaps [(cx, cy, rx, ry, h[, rot[, peak]]), ...] each with
    boulders set into it. Returns (parts, surface fn over all heaps,
    inside fn)."""
    ground = ground or (lambda x, y: 0.0)
    parts, fns = [], []
    for i, sp in enumerate(specs):
        cx, cy, rx, ry, h = sp[:5]
        rot = sp[5] if len(sp) > 5 else 0.0
        peak = sp[6] if len(sp) > 6 else (0.0, 0.0)
        ob, fn, ins = heap(cx, cy, rx, ry, h, mt, ground, seed=seed + 7 * i, rot=rot, peak=peak, name=name,
                           rings=rings, seg=max(14, int(ring_seg * min(1.0, 0.45 + max(rx, ry) / 4))), power=power)
        parts.append(ob)
        fns.append((fn, ins))
        R = max(rx, ry) * 1.25
        n = int(per_area * rx * ry * min(1.5, 0.5 + h))
        parts += boulders(fn, ins, (cx - R, cx + R, cy - R, cy + R), n, size, stones, seed=seed + 7 * i + 3,
                          zmin=0.05, seg=seg)

    def surf(x, y):
        return max([fn(x, y) for fn, ins in fns if ins(x, y)] or [ground(x, y)])

    def inside(x, y):
        return any(ins(x, y) for fn, ins in fns)
    return parts, surf, inside


def tex_soot(base, dark, n=128, seed=9):
    """Char and ash: blotches of near black over a sooty ground."""
    rng = _rng(seed)
    f = fbm(n, rng, ((4, 0.5), (8, 0.3), (32, 0.2)))
    t = np.clip((f - 0.35) * 3.0, 0, 1)
    return np.clip(_mix(np.ones((n, n, 3), np.float32) * _col(base), np.ones((n, n, 3), np.float32) * _col(dark),
                        t), 0, 1)


def soot(cx, cy, rx, ry, ground, mats, seed=1, seg=22, rot=0.0, lift_z=0.025, name="soot", clip=None):
    """A scorch lying on ground(x, y): a ragged dark core in mats[0] inside
    a ragged fringe in mats[1], a hair above the ground. clip(x, y) -> (x, y)
    pulls points back onto the surface it may lie on."""
    edge = outline_fn(seed, 0.3)
    edge2 = outline_fn(seed + 1, 0.22)
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    out = []
    for k, (mt, f, e) in enumerate(((mats[1], 1.0, edge), (mats[0], 0.62, edge2))):
        verts = [(cx, cy)]
        for j in range(seg):
            a = 2 * math.pi * j / seg
            u, v = f * e(a) * rx * math.cos(a), f * e(a) * ry * math.sin(a)
            verts.append((cx + u * c - v * s, cy + u * s + v * c))
        if clip is not None:
            verts = [clip(x, y) for x, y in verts]
        verts = [(x, y, ground(x, y) + lift_z * (1 + k)) for x, y in verts]
        faces = [[0, 1 + j, 1 + (j + 1) % seg] for j in range(seg)]
        ob = mesh(name, verts, faces, mt, recalc=False)
        for p in ob.data.polygons:
            if p.normal.z < 0:
                p.flip()
        uv_box(ob, 0.4, random.Random(seed).random(), 0.3)
        out.append(ob)
    return out
