"""Parametric builders for trees and plants, run inside Blender.

Frame as handkit: one unit is one map cell, -Y toward the classic camera,
Z up, the origin at the feature's anchor on the ground. Geometry is built
in numpy into a Geo (vertices, faces, per-corner UVs, per-vertex colours
and optional normals) and turned into one Blender object per material.

Colours are the sprite's own sRGB numbers, sampled by sprite2d and turned
linear here. Leaf, needle and bark textures are generated, never taken
from the sprite, so every model ships as it is.
"""
import math

import numpy as np

import sprite2d as s2

TAU = 2 * math.pi
GAP = 0.006  # cells between the two sides of a card or blade
BARK_REP = 0.5  # bark texture repeats per cell of limb length

# The sturdiness pass: a pixel-exact fit draws tall things thin in true 3D,
# since the classic view shows height at half scale. Every model is widened
# by girth and shortened by height about its anchor, and tubes and trunks
# thickened by radius; build.py sets these per model before building.
SHAPE = {"girth": 1.0, "height": 1.0, "radius": 1.0}
STURDY = {"crown": (1.18, 0.85, 1.1), "fir": (1.18, 0.85, 1.1), "limbs": (1.16, 0.86, 1.12),
          "bulb": (1.08, 0.92, 1.0), "grass": (1.08, 0.92, 1.0), "rosette": (1.06, 1.0, 1.0)}


# ---------------------------------------------------------------- geometry

class Geo:
    def __init__(self):
        self.v, self.n, self.c, self.f, self.uv = [], [], [], [], []
        self.nv = 0
        self.has_n = False

    def add(self, verts, faces, uvs=None, cols=None, normals=None):
        """verts (k, 3); faces as vertex-index tuples local to verts; uvs a
        per-vertex (k, 2) array; cols (k, 3) linear or one colour."""
        verts = np.asarray(verts, np.float64).reshape(-1, 3)
        k = len(verts)
        self.v.append(verts)
        if cols is None:
            cols = (0.8, 0.8, 0.8)
        cols = np.asarray(cols, np.float64)
        if cols.ndim == 1:
            cols = np.tile(cols, (k, 1))
        self.c.append(cols)
        self.uv.append(np.zeros((k, 2)) if uvs is None else np.asarray(uvs, np.float64).reshape(-1, 2))
        if normals is not None:
            self.has_n = True
            self.n.append(np.asarray(normals, np.float64).reshape(-1, 3))
        else:
            self.n.append(np.zeros((k, 3)))
        base = self.nv
        self.f.extend(tuple(base + i for i in face) for face in faces)
        self.nv += k
        return base

    def tris(self):
        return sum(len(f) - 2 for f in self.f)

    def to_object(self, name, mat, smooth=True):
        import bpy
        V = np.concatenate(self.v) if self.v else np.zeros((0, 3))
        S = np.array([SHAPE["girth"], SHAPE["girth"], SHAPE["height"]])
        V = V * S
        C = np.concatenate(self.c) if self.c else np.zeros((0, 3))
        U = np.concatenate(self.uv) if self.uv else np.zeros((0, 2))
        me = bpy.data.meshes.new(name)
        me.from_pydata(V.tolist(), [], [list(f) for f in self.f])
        me.update()
        # per-vertex UVs and colours onto the corners
        li = np.empty(len(me.loops), np.int64)
        me.loops.foreach_get("vertex_index", li)
        uv = me.uv_layers.new(name="UVMap")
        uv.data.foreach_set("uv", U[li].astype(np.float32).ravel())
        ca = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
        rgba = np.concatenate([np.clip(C, 0, 1), np.ones((len(C), 1))], 1).astype(np.float32)
        ca.data.foreach_set("color", rgba.ravel())
        if smooth:
            me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
        if self.has_n:
            N = np.concatenate(self.n) / S
            ln = np.linalg.norm(N, axis=1, keepdims=True)
            N = np.where(ln > 1e-9, N / np.maximum(ln, 1e-9), 0)
            me.normals_split_custom_set_from_vertices(N.tolist())
        me.materials.append(mat)
        ob = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(ob)
        return ob


def frame(t):
    """Two unit vectors square to direction t."""
    t = t / (np.linalg.norm(t) + 1e-12)
    a = np.array([0.0, 0.0, 1.0]) if abs(t[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    u = np.cross(t, a)
    u /= np.linalg.norm(u)
    return u, np.cross(t, u)


def tube(geo, P, R, sides=6, col=None, cols=None, cap_tip=True, cap_base=False, vscale=1.0, vrep=None):
    """A tube along points P (n, 3) with radii R (n): rings carried along by
    parallel transport, a pointed tip, u round the tube and v along it (0 to
    vscale, or vrep repeats per cell of length for a repeating texture)."""
    P = np.asarray(P, np.float64)
    R = np.asarray(R, np.float64) * SHAPE["radius"]
    n = len(P)
    if n < 2:
        return
    T = np.zeros_like(P)
    T[1:-1] = P[2:] - P[:-2]
    T[0], T[-1] = P[1] - P[0], P[-1] - P[-2]
    T /= np.linalg.norm(T, axis=1, keepdims=True) + 1e-12
    u, w = frame(T[0])
    verts, uvs, cc = [], [], []
    # whole repeats round the tube (the textures tile), about vrep per cell
    urep = max(1, round(TAU * float(np.mean(R)) * vrep)) if vrep else 1
    seg = np.concatenate([[0.0], np.cumsum(np.linalg.norm(P[1:] - P[:-1], axis=1))])
    L = max(seg[-1], 1e-6)
    for i in range(n):
        if i:
            # parallel transport of the frame
            t0, t1 = T[i - 1], T[i]
            ax = np.cross(t0, t1)
            s = np.linalg.norm(ax)
            if s > 1e-8:
                ax /= s
                ang = math.atan2(s, float(np.dot(t0, t1)))
                u = _rot(u, ax, ang)
            u -= T[i] * np.dot(u, T[i])
            u /= np.linalg.norm(u) + 1e-12
            w = np.cross(T[i], u)
        for k in range(sides + 1):
            a = TAU * k / sides
            verts.append(P[i] + R[i] * (math.cos(a) * u + math.sin(a) * w))
            uvs.append((urep * k / sides, seg[i] * vrep if vrep else min(1.0, seg[i] / L * vscale)))
            cc.append(cols[i] if cols is not None else col)
    faces = []
    m = sides + 1
    for i in range(n - 1):
        for k in range(sides):
            a, b = i * m + k, i * m + k + 1
            faces.append((a, b, b + m, a + m))
    extra_v, extra_uv, extra_c = [], [], []
    base_len = len(verts)
    if cap_tip:
        extra_v.append(P[-1] + T[-1] * R[-1] * 1.2)
        extra_uv.append((0.5, seg[-1] * vrep if vrep else 1.0))
        extra_c.append(cc[-1])
        tip = base_len
        for k in range(sides):
            faces.append(((n - 1) * m + k, (n - 1) * m + k + 1, tip))
    if cap_base:
        extra_v.append(P[0] - T[0] * R[0] * 0.3)
        extra_uv.append((0.5, 0.0))
        extra_c.append(cc[0])
        b0 = base_len + len(extra_v) - 1
        for k in range(sides):
            faces.append((k + 1, k, b0))
    allv = np.array(verts + extra_v)
    geo.add(allv, faces, np.array(uvs + extra_uv), np.array(cc + extra_c))


def _rot(v, ax, ang):
    c, s = math.cos(ang), math.sin(ang)
    return v * c + np.cross(ax, v) * s + ax * np.dot(ax, v) * (1 - c)


def ellipsoid(geo, C, radii, seg=8, rings=5, col=(0.1, 0.2, 0.1), normals_from=None, jitter=0.0, rng=None, rep=None):
    """A low ellipsoid; normals_from gives a centre to point normals away
    from. UVs wrap round it (a seam column repeated), rep times per cell
    when given, for a repeating texture."""
    C = np.asarray(C, np.float64)
    R = np.asarray(radii, np.float64)
    nu = max(1, round(TAU * R[:2].mean() * rep)) if rep else 1
    nv = max(1, round(math.pi * R[2] * rep)) if rep else 1
    verts, uvs = [C + R * (0, 0, 1)], [(0.5 * nu, 0.0)]
    bump = {}
    for j in range(1, rings):
        th = math.pi * j / rings
        for k in range(seg + 1):
            kk = k % seg
            ph = TAU * kk / seg + (0.5 * TAU / seg if j % 2 else 0.0)
            d = np.array([math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)])
            if (j, kk) not in bump:
                bump[(j, kk)] = 1.0 + (jitter * (rng.random() - 0.5) if rng is not None else 0.0)
            verts.append(C + d * R * bump[(j, kk)])
            uvs.append((nu * k / seg, nv * j / rings))
    verts.append(C - R * (0, 0, 1))
    uvs.append((0.5 * nu, float(nv)))
    m = seg + 1
    faces = []
    for k in range(seg):
        faces.append((0, 1 + k + 1, 1 + k))
    for j in range(rings - 2):
        a0, b0 = 1 + j * m, 1 + (j + 1) * m
        for k in range(seg):
            faces.append((a0 + k, a0 + k + 1, b0 + k + 1, b0 + k))
    last = len(verts) - 1
    a0 = 1 + (rings - 2) * m
    for k in range(seg):
        faces.append((a0 + k, a0 + k + 1, last))
    faces = [tuple(reversed(f)) for f in faces]  # wound outward
    V = np.array(verts)
    N = None if normals_from is None else V - np.asarray(normals_from)
    geo.add(V, faces, np.array(uvs), col, N)


def card(geo, C, normal, up, w, h, uvrect=(0, 0, 1, 1), col=(1, 1, 1), N=None, both=False, anchor=0.5):
    """A textured quad centred on C (or hanging from it by anchor along up),
    facing normal, or turned to face N, the shading normal for every corner,
    when normal points away from it. One-sided, for a double-sided material:
    a second face behind it would shadow the first. both adds that face."""
    n = np.asarray(normal, np.float64)
    n /= np.linalg.norm(n) + 1e-12
    if N is not None and np.dot(n, N) < 0:
        n = -n
    up = np.asarray(up, np.float64)
    up = up - n * np.dot(up, n)
    up /= np.linalg.norm(up) + 1e-12
    rt = np.cross(up, n)
    C = np.asarray(C, np.float64) - up * h * (anchor - 0.5)
    u0, v0, u1, v1 = uvrect
    V = [C - rt * w / 2 - up * h / 2, C + rt * w / 2 - up * h / 2, C + rt * w / 2 + up * h / 2, C - rt * w / 2 + up * h / 2]
    UV = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    shade = n if N is None else np.asarray(N, np.float64)
    faces = [(0, 1, 2, 3)]
    if both:
        # the two sides sit a hair apart so each is the first one hit from its side
        V = [v + n * GAP for v in V] + [v - n * GAP for v in V]
        UV = UV + UV
        faces.append((7, 6, 5, 4))
    geo.add(np.array(V), faces, np.array(UV), col, np.tile(shade, (len(V), 1)))


def strip(geo, spine, widths, side, col=None, cols=None, N=None, both=False, crease=0.0, up=None, back=None):
    """A ribbon along spine (n, 3), widths (n), spread along side (n, 3):
    leaves and blades. crease lifts the midrib by that fraction of width.
    One-sided and wound to face N when given, for a double-sided material;
    both adds the other face, its colours scaled by back when given."""
    spine = np.asarray(spine, np.float64)
    n = len(spine)
    V, UV, CC = [], [], []
    three = crease != 0.0
    for i in range(n):
        s = np.asarray(side[i], np.float64)
        wv = widths[i]
        c = cols[i] if cols is not None else col
        t = i / max(1, n - 1)
        if three:
            lift = np.asarray(up[i]) * crease * wv
            V += [spine[i] - s * wv / 2, spine[i] + lift, spine[i] + s * wv / 2]
            UV += [(0, t), (0.5, t), (1, t)]
            CC += [c, c, c]
        else:
            V += [spine[i] - s * wv / 2, spine[i] + s * wv / 2]
            UV += [(0, t), (1, t)]
            CC += [c, c]
    m = 3 if three else 2
    faces = []
    for i in range(n - 1):
        for k in range(m - 1):
            a = i * m + k
            faces.append((a, a + 1, a + 1 + m, a + m))
    V = np.array(V)
    if N is not None:
        NN = np.repeat(np.asarray(N, np.float64), m, axis=0) if np.ndim(N) == 2 else np.tile(N, (len(V), 1))
        # wind each face to agree with its shading normal
        out = []
        for f in faces:
            a, b, c = V[f[0]], V[f[1]], V[f[2]]
            if np.dot(np.cross(b - a, c - b), NN[list(f)].sum(0)) < 0:
                f = tuple(reversed(f))
            out.append(f)
        faces = out
    else:
        NN = None
    if both:
        k0 = len(V)
        # the two sides sit a hair apart so each is the first one hit from its side
        fn = np.zeros_like(V)
        for f in faces:
            a, b, c = V[f[0]], V[f[1]], V[f[2]]
            nn = np.cross(b - a, c - b)
            for i in f:
                fn[i] += nn
        fn = _unit(fn)
        faces += [tuple(k0 + i for i in reversed(f)) for f in faces]
        V = np.concatenate([V + fn * GAP, V - fn * GAP])
        UV = UV + UV
        CC = CC + ([np.asarray(c) * back for c in CC] if back is not None else CC)
        if NN is not None:
            NN = np.concatenate([NN, NN])
    geo.add(V, faces, np.array(UV), np.array(CC), NN)


# ---------------------------------------------------------------- materials

def material(name, tex=None, cut=False, rough=0.9, cull=True, repeat=False):
    """Vertex colour, times a texture when given; cut makes the texture's
    alpha a hard mask (glTF MASK). cull=True exports single-sided."""
    import bpy
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    if tex is not None:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = tex
        t.interpolation = "Linear"
        t.extension = "REPEAT" if repeat else "EXTEND"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs[0].default_value = 1.0
        nt.links.new(t.outputs["Color"], mix.inputs[6])
        nt.links.new(vc.outputs["Color"], mix.inputs[7])
        nt.links.new(mix.outputs[2], b.inputs["Base Color"])
        if cut:
            gt = nt.nodes.new("ShaderNodeMath")
            gt.operation = "GREATER_THAN"
            gt.inputs[1].default_value = 0.5
            nt.links.new(t.outputs["Alpha"], gt.inputs[0])
            nt.links.new(gt.outputs[0], b.inputs["Alpha"])
    else:
        nt.links.new(vc.outputs["Color"], b.inputs["Base Color"])
    m.use_backface_culling = cull
    return m


def image(name, arr):
    """A packed Blender image from an (h, w, 4) array, rows top-down."""
    import bpy
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(arr[::-1], np.float32).ravel())
    img.pack()
    img.colorspace_settings.name = "sRGB"
    # made here from noise, so it ships (okpaint.py)
    img["okGenerated"] = True
    return img


def _blur(a, sy, sx):
    """Gaussian blur with wrap-around, sigma in pixels per axis."""
    out = a
    for axis, s in ((0, sy), (1, sx)):
        if s <= 0:
            continue
        n = out.shape[axis]
        f = np.fft.fftfreq(n)
        g = np.exp(-2 * (math.pi * f * s) ** 2)
        shape = [1] * out.ndim
        shape[axis] = n
        out = np.real(np.fft.ifft(np.fft.fft(out, axis=axis) * g.reshape(shape), axis=axis))
    return out


def _norm01(a):
    lo, hi = a.min(), a.max()
    return (a - lo) / max(hi - lo, 1e-9)


def tex_bark(style="streak", seed=1, size=(128, 128), levels=None):
    """Grey bark: long fibres (streak), dark and light patches (mottle) or
    fine speckle. With levels, sorted brightnesses 0 to 1 taken from the
    sprite, the pattern's values are remapped to them so the model carries
    the drawing's own spread of dark and light."""
    rng = np.random.default_rng(seed)
    h, w = size
    fib = _norm01(_blur(rng.random((h, w)), 12, 1.5))
    mot = _norm01(_blur(rng.random((h, w)), 7, 6))
    fine = _norm01(_blur(rng.random((h, w)), 2.2, 2.0))
    if style == "camo":
        # big pale and dark blotches, the Taros hide
        v = 0.8 * _norm01(_blur(rng.random((h, w)), 14, 10)) + 0.2 * fine
    elif style == "hide":
        # the Taros hide on round limbs: dark blotches and pale ridges along the limb
        v = 0.6 * _norm01(_blur(rng.random((h, w)), 10, 9)) + 0.4 * fib
    elif style == "mottle":
        v = 0.6 * mot + 0.4 * fine
    elif style == "speckle":
        v = 0.3 * mot + 0.7 * fine
    else:
        v = 0.5 * fib + 0.3 * mot + 0.2 * fine
    if levels is not None:
        ranks = np.argsort(np.argsort(v.ravel())) / (v.size - 1)
        v = np.interp(ranks, np.linspace(0, 1, len(levels)), levels).reshape(h, w)
    else:
        v = 0.55 + 0.45 * v
    a = np.ones((h, w, 4))
    a[..., 0] = a[..., 1] = a[..., 2] = np.clip(v, 0, 1)
    return a


def bark_from(spr, sel=None, style="streak", seed=3, gain=1.25, top=0.97):
    """A bark texture and its vertex colour from the pixels in sel: the
    texture holds brightness relative to the lit bark, the colour its hue."""
    sel = spr.mask if sel is None else (sel & spr.mask)
    lum = np.sort(spr.lum[sel])
    ref = max(float(np.quantile(lum, top)), 1e-3)
    levels = np.clip(np.quantile(lum, np.linspace(0.02, 0.98, 33)) / ref, 0.05, 1.0)
    col = spr.colour(sel, 0.85, 1.0)
    return tex_bark(style, seed, levels=levels), lin(col, gain)


def _leaf_shape(xx, yy, cx, cy, ang, length, width, tipsharp=1.4):
    """Mask and 0-1 along-leaf coordinate of one pointed leaf starting at
    (cx, cy) heading ang."""
    ca, sa = math.cos(ang), math.sin(ang)
    lx = (xx - cx) * ca + (yy - cy) * sa
    ly = -(xx - cx) * sa + (yy - cy) * ca
    t = lx / length
    half = width / 2 * np.clip(np.sin(np.clip(t, 0, 1) * math.pi) ** (1 / tipsharp), 0, 1)
    inside = (t >= 0) & (t <= 1) & (np.abs(ly) <= half)
    return inside, t, ly / np.maximum(half, 1e-6)


def tex_leaves(kind="cluster", seed=2, size=128):
    """Alpha-cut foliage in grey: 'cluster' a spray of small leaves,
    'maple' one big lobed leaf, 'poplar' many tiny leaves, 'needles' a fir
    spray (twice as long as wide, the twig along +v), 'sprig' and 'spray'
    leafy twigs, 'star' a whorl of pointed leaves, 'twigs' a dead spur."""
    rng = np.random.default_rng(seed)
    if kind in ("needles", "sprig", "spray", "twigs"):
        h, w = size * 2, size
    else:
        h, w = size, size
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    alpha = np.zeros((h, w))
    val = np.zeros((h, w))

    def put(mask, shade):
        nonlocal alpha, val
        val = np.where(mask, shade, val)
        alpha = np.where(mask, 1.0, alpha)

    if kind == "maple":
        cx, cy = w / 2, h * 0.86
        for i, ang in enumerate(np.radians([-90, -145, -35, -190, 10])):
            L = w * (0.62 if i == 0 else 0.5 if i < 3 else 0.33)
            m, t, s = _leaf_shape(xx, yy, cx, cy, ang, L, L * 0.72, 1.0)
            # a toothed edge
            m &= (np.abs(s) < 0.93) | (np.sin(t * 40) > -0.2)
            put(m & (alpha == 0) | m, 0.9 + 0.1 * t - 0.12 * (np.abs(s) < 0.08))
        # veins from the stem, a darker rim
        val = np.where(alpha > 0, val * (0.92 + 0.08 * _norm01(_blur(rng.random((h, w)), 3, 3))), 0)
        inside = alpha > 0
        rim = inside & ~(np.roll(inside, 3, 0) & np.roll(inside, -3, 0) & np.roll(inside, 3, 1) & np.roll(inside, -3, 1))
        val = np.where(rim, val * 0.7, val)
        for ang in np.radians([-90, -145, -35, -190, 10]):
            vein = _seg_dist(xx, yy, cx, cy, cx + math.cos(ang) * w * 0.45, cy + math.sin(ang) * w * 0.45) < 1.2
            val = np.where(vein & inside, val * 0.8, val)
        stem = (np.abs(xx - cx) < 1.5) & (yy > cy) & (yy < h - 2)
        put(stem, 0.75)
    elif kind == "sprig":
        # an upright twig with small leaves climbing it, the tip at the top
        cx = w / 2
        twig = (np.abs(xx - cx - 3 * np.sin(yy / h * 5)) < 1.4) & (yy > h * 0.08) & (yy < h - 3)
        for i in range(26):
            y0 = rng.uniform(h * 0.1, h * 0.95)
            side = 1 if i % 2 else -1
            reach = (w / 2 - 6) * (0.45 + 0.55 * (y0 / h)) * rng.uniform(0.7, 1.0)
            ang = math.radians(-90 + side * rng.uniform(30, 60))
            L = reach / max(0.4, abs(math.cos(ang)))
            m, t, sd = _leaf_shape(xx, yy, cx + 3 * math.sin(y0 / h * 5), y0, ang, min(L, h * 0.3), min(L, h * 0.3) * 0.5, 1.2)
            put(m, rng.uniform(0.55, 1.0) * (0.9 + 0.1 * t))
        put(twig & (alpha == 0), 0.5)
    elif kind == "spray":
        # a spiky spray: narrow pointed leaves up a twig, longest low down
        cx = w / 2
        for i in range(22):
            y0 = rng.uniform(h * 0.12, h * 0.95)
            side = 1 if i % 2 else -1
            L = h * rng.uniform(0.16, 0.3) * (0.5 + 0.5 * y0 / h)
            ang = math.radians(-90 + side * rng.uniform(20, 45))
            m, t, sd = _leaf_shape(xx, yy, cx, y0, ang, L, L * 0.26, 0.8)
            put(m, rng.uniform(0.55, 1.0) * (0.85 + 0.15 * t) - 0.1 * (np.abs(sd) < 0.15))
        m, t, sd = _leaf_shape(xx, yy, cx, h * 0.3, math.radians(-90), h * 0.3, h * 0.07, 0.8)
        put(m, 0.95)
        put((np.abs(xx - cx) < 1.3) & (yy > h * 0.3) & (yy < h - 3) & (alpha == 0), 0.45)
    elif kind == "star":
        # a whorl of long pointed leaves from one point, a dark midrib each
        cx, cy = w / 2, h / 2
        order = rng.permutation(8)
        for i in order:
            ang = TAU * i / 8 + rng.uniform(-0.25, 0.25)
            L = w * rng.uniform(0.36, 0.48)
            m, t, sd = _leaf_shape(xx, yy, cx, cy, ang, L, L * 0.3, 0.9)
            shade = rng.uniform(0.6, 1.0)
            put(m, shade * (0.85 + 0.15 * t) - 0.15 * (np.abs(sd) < 0.12) * (t < 0.9))
    elif kind == "twigs":
        # a dead spur seen from a distance: a chunky stiff twig with short
        # thick side twigs, some forked, all a few texels to a drawn pixel
        cx = w / 2
        d = _seg_dist(xx, yy, cx, h - 3, cx + rng.uniform(-4, 4), 6)
        put(d < 7.0 - 3.0 * (1 - yy / h), 0.8)
        for i in range(9):
            y0 = h * (0.12 + 0.8 * i / 8) + rng.uniform(-6, 6)
            side = 1 if i % 2 else -1
            L = w * rng.uniform(0.3, 0.46)
            a = math.radians(rng.uniform(35, 65))
            x1, y1 = cx + side * L * math.sin(a), y0 - L * math.cos(a)
            put(_seg_dist(xx, yy, cx, y0, x1, y1) < rng.uniform(3.5, 5.0), rng.uniform(0.6, 1.0))
            if rng.random() < 0.6:
                x2, y2 = x1 + side * L * 0.25, y1 - L * 0.4
                put(_seg_dist(xx, yy, x1, y1, x2, y2) < 3.0, rng.uniform(0.6, 1.0))
    elif kind == "needles":
        cx = w / 2
        twig = (np.abs(xx - cx) < 1.6) & (yy > 4) & (yy < h - 2)
        for i in range(90):
            y0 = rng.uniform(6, h - 6)
            side = rng.choice([-1, 1])
            reach = (w / 2 - 4) * (0.55 + 0.45 * math.sin(math.pi * (y0 / h))) * rng.uniform(0.6, 1.0)
            ang = math.radians(rng.uniform(25, 60))
            x1 = cx + side * reach * math.sin(ang)
            y1 = y0 - reach * math.cos(ang)
            d = _seg_dist(xx, yy, cx, y0, x1, y1)
            put(d < rng.uniform(1.0, 1.6), rng.uniform(0.8, 1.0))
        put(twig, 0.6)
    else:
        n, lo, hi = (22, 0.22, 0.34) if kind == "cluster" else (60, 0.08, 0.14)
        cx, cy = w / 2, h / 2
        for i in range(n):
            ang = rng.uniform(0, TAU)
            r0 = rng.uniform(0, 0.28) * w
            x0, y0 = cx + r0 * math.cos(ang), cy + r0 * math.sin(ang)
            L = rng.uniform(lo, hi) * w
            m, t, s = _leaf_shape(xx, yy, x0, y0, ang + rng.uniform(-0.6, 0.6), L, L * 0.55, 1.2)
            # the poplar's tiny leaves flicker from dull to bright
            shade = rng.uniform(0.8, 1.0) if kind == "cluster" else rng.uniform(0.35, 1.0)
            put(m, shade * (0.92 + 0.08 * t) - 0.08 * (np.abs(s) < 0.12) * (t < 0.9))
    a = np.zeros((h, w, 4))
    for i in range(3):
        a[..., i] = np.clip(val, 0, 1)
    a[..., 3] = alpha
    # keep the edge texels clear so clamped UVs never smear an edge
    a[[0, -1], :, 3] = 0
    a[:, [0, -1], 3] = 0
    return a


def tex_brush(seed=4, size=128):
    """A dead twig brush on alpha, (2 size, size), the foot at the bottom
    middle: a stem forking again and again into ever finer twigs."""
    rng = np.random.default_rng(seed)
    h, w = size * 2, size
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    alpha = np.zeros((h, w))
    val = np.zeros((h, w))

    def grow(x0, y0, ang, L, wid, depth):
        x1, y1 = x0 + math.sin(ang) * L, y0 - math.cos(ang) * L
        m = _seg_dist(xx, yy, x0, y0, x1, y1) < wid
        val[m] = rng.uniform(0.7, 1.0)
        alpha[m] = 1.0
        if depth == 0 or L < 6:
            return
        for sgn in (-1, 1):
            if rng.random() < 0.85:
                grow(x1, y1, ang + sgn * rng.uniform(0.25, 0.6), L * rng.uniform(0.6, 0.78), max(wid * 0.72, 0.9),
                     depth - 1)

    grow(w / 2, h - 3, rng.uniform(-0.1, 0.1), h * 0.3, 3.2, 5)
    a = np.zeros((h, w, 4))
    for i in range(3):
        a[..., i] = val
    a[..., 3] = alpha
    a[[0, -1], :, 3] = 0
    a[:, [0, -1], 3] = 0
    return a


def _seg_dist(xx, yy, x0, y0, x1, y1):
    dx, dy = x1 - x0, y1 - y0
    L2 = dx * dx + dy * dy + 1e-9
    t = np.clip(((xx - x0) * dx + (yy - y0) * dy) / L2, 0, 1)
    return np.hypot(xx - (x0 + t * dx), yy - (y0 + t * dy))


# ---------------------------------------------------------------- colour

def lin(c, gain=1.0):
    return np.clip(np.array(s2.to_linear(np.clip(np.asarray(c) * gain, 0, 1))), 0, 1)


def mixc(a, b, t):
    return np.asarray(a) * (1 - t) + np.asarray(b) * t


# ---------------------------------------------------------------- limbs

def lift(spr, branches, spec, rng):
    """3D limbs from skeleton branches. Each point keeps the pixel it was
    drawn at; its depth grows along the branch by sigma per cell of drawn
    length, and its height follows from the classic projection.

    spec keys: mode 'limb' (sigma = a*up - b*across) or 'radial' (sigma =
    a*up - b), alpha, beta, jitter, lean (the trunk's sigma), trunk (branch
    ids kept upright), root_z (height of the root point), floor."""
    mode = spec.get("mode", "limb")
    alpha = spec.get("alpha", 0.35)
    beta = spec.get("beta", 0.12)
    jit = spec.get("jitter", 0.5)
    trunk = set(spec.get("trunk", [0]))
    lean = spec.get("lean", 0.0)
    root_z = spec.get("root_z", 0.0)
    rmin = spec.get("rmin", 0.5)
    rscale = spec.get("rscale", 1.0)
    fwd_share = spec.get("forward", 0.25)
    grounded = set()
    for gx, gy in spec.get("ground", []):
        tips = [(math.hypot(b["px"][-1][0] - gx, b["px"][-1][1] - gy), i) for i, b in enumerate(branches)]
        if tips:
            grounded.add(min(tips)[1])
    per = spec.get("branch", {})
    out = []
    for i, b in enumerate(branches):
        px = s2.smooth_path(b["px"], 2)
        r = np.array(b["r"], np.float64)
        rpx = np.minimum(np.maximum(rmin, (r - 0.5) * rscale), spec.get("rmax", 99.0) * s2.CELL)
        if b["parent"] is not None:
            rpx[0] = rpx[min(1, len(rpx) - 1)]
            rpx[:3] = np.minimum(rpx[:3], rpx[min(3, len(rpx) - 1)] * 1.5)
        # resample, finer where thin and curly
        step = float(np.clip(np.median(rpx) * 1.5, 2.5, 6.0))
        pts, t = s2.resample(px, step)
        s_orig = np.concatenate([[0.0], np.cumsum(np.hypot(*(px[1:] - px[:-1]).T))])
        rr = np.interp(t, s_orig, rpx)
        if per.get(i, {}).get("thick"):
            # heavy wood where it leaves its parent, easing off along it
            rr = rr * (1 + (per[i]["thick"] - 1) * np.exp(-t / (0.3 * max(float(t[-1]), 1e-6))))
        if b["parent"] is None:
            col, row = pts[0]
            y = (spr.hy - row - 8 * root_z) / s2.CELL
            start = None
        else:
            pb = out[b["parent"]]
            start = pb["P"][min(pb["map"][b["at"]], len(pb["P"]) - 1)]
            y = start[1]
        a_b = alpha * (1 + jit * rng.uniform(-1, 1))
        b_b = beta * (1 + jit * rng.uniform(-1, 1))
        # rise mode: the limb climbs at phi degrees, from phi0 at its start to
        # phi1 at its tip; limbs starting below root_level take root_phi
        start_z = root_z if start is None else start[2]
        ph0, ph1 = spec.get("root_phi", (-15, -5)) if start_z < spec.get("root_level", -1.0) else spec.get("phi", (40, 10))
        pj = spec.get("phi_jitter", 10.0)
        ph0 += rng.uniform(-pj, pj)
        ph1 += rng.uniform(-pj, pj)
        if i in grounded:
            ph0, ph1 = spec.get("leg_phi", (-25, -10))
        if i in per and "phi" in per[i]:
            ph0, ph1 = per[i]["phi"]
        s_tot = max(float(t[-1]), 1e-6)
        if mode == "limb" and rng.random() < fwd_share:
            a_b = -0.4 * a_b
        if i in per:
            a_b = per[i].get("alpha", a_b)
            b_b = per[i].get("beta", b_b)
        P = []
        for k, (col, row) in enumerate(pts):
            if k:
                dc, dr = col - pts[k - 1][0], row - pts[k - 1][1]
                dl = math.hypot(dc, dr) + 1e-9
                up, across = -dr / dl, abs(dc) / dl
                if i in trunk and (P[-1][2] if P else 0.0) < spec.get("trunk_top", 1e9):
                    sig = lean
                elif mode == "rise":
                    q = math.tan(math.radians(np.clip(ph0 + (ph1 - ph0) * t[k] / s_tot, -60, 60))) / 2
                    sig = _rise_step(dc, dr, q) / dl
                elif mode == "radial":
                    sig = a_b * up - b_b
                else:
                    sig = a_b * max(up, 0.0) + 0.5 * a_b * min(up, 0.0) - b_b * across
                y += sig * dl / s2.CELL
            x = (col + 0.5 - spr.hx) / s2.CELL
            z = (spr.hy - row - 0.5 - s2.CELL * y) / (s2.CELL * s2.TILT)
            zf = rr[k] / s2.CELL * 0.7
            if z < zf:
                z = zf
                y = (spr.hy - row - s2.CELL * s2.TILT * z) / s2.CELL
            P.append((x, y, z))
        P = np.array(P)
        if start is not None:
            P[0] = start
        if i in grounded and len(P) > 2:
            # bend the branch down so its tip rests on the ground
            col, row = pts[-1]
            zf = rr[-1] / s2.CELL * 0.7
            y_tip = (spr.hy - row - s2.CELL * s2.TILT * zf) / s2.CELL
            s_ = np.linspace(0, 1, len(P)) ** 1.5
            P[1:, 1] += (y_tip - P[-1, 1]) * s_[1:]
            for k in range(1, len(P)):
                col, row = pts[k]
                P[k, 2] = max(rr[k] / s2.CELL * 0.7, (spr.hy - row - s2.CELL * P[k, 1]) / (s2.CELL * s2.TILT))
                P[k, 1] = (spr.hy - row - s2.CELL * s2.TILT * P[k, 2]) / s2.CELL
        # where each original pixel index landed, for children
        idx = np.searchsorted(t, s_orig, side="left").clip(0, len(P) - 1)
        out.append({"P": P, "R": rr / s2.CELL, "map": idx, "parent": b["parent"], "depth": 0 if b["parent"] is None
                    else out[b["parent"]]["depth"] + 1})
    return out


def lift_smooth(spr, branches, spec, rng):
    """3D limbs from skeleton branches, every point kept on its drawn
    pixel's camera ray. Along a limb the height runs smoothly from its
    start to an end height chosen for the limb as a whole, and the depth
    follows from the projection, so no limb zigzags along the rays into a
    flat ribbon. Modes: 'swing' sends alternate limbs toward and away from
    the camera by psi degrees of their drawn length, those toward rising
    more steeply and those away drooping; 'balanced' gives every limb a
    gentle rise, raised or lowered just enough that no limb ends further
    than R cells from the root, so the spread is even all round.

    spec keys: mode, trunk (branch ids kept upright below trunk_top),
    root_z, psi (lo, hi degrees), rise (end height per drawn cell), R,
    p (the height profile's power: over 1 rises late, a curling tip),
    zfloor, rmin, rscale, rmax."""
    mode = spec.get("mode", "balanced")
    trunk = set(spec.get("trunk", []))
    root_z = spec.get("root_z", 0.0)
    rmin, rscale = spec.get("rmin", 0.5), spec.get("rscale", 1.0)
    rmax = spec.get("rmax", 99.0) * s2.CELL
    p = spec.get("p", 1.0)
    out = []
    root_xy = None
    # the limbs off the trunk, longest first on each side of the drawing,
    # swing toward and away from the camera by turns; the longest on each
    # side swing least, so they carry the width and the rest the depth, and
    # the crown spreads all round, not as one tilted fan
    first_side, rank = {}, {}
    for lr in (0, 1):
        tops = [(i, b) for i, b in enumerate(branches)
                if (b["parent"] is None or b["parent"] in trunk) and i not in trunk
                and ((b["px"][-1][0] < b["px"][0][0]) == (lr == 0))]
        tops.sort(key=lambda ib: -len(ib[1]["px"]))
        for k, (i, b) in enumerate(tops):
            first_side[i] = 1 if (k + lr) % 2 else -1
            rank[i] = k
    for i, b in enumerate(branches):
        px = s2.smooth_path(b["px"], 2)
        r = np.array(b["r"], np.float64)
        rpx = np.minimum(np.maximum(rmin, (r - 0.5) * rscale), rmax)
        par = b["parent"]
        if par is not None:
            rpx[0] = rpx[min(1, len(rpx) - 1)]
            rpx[:3] = np.minimum(rpx[:3], rpx[min(3, len(rpx) - 1)] * 1.5)
        step = float(np.clip(np.median(rpx) * 1.5, 2.5, 6.0))
        pts, t = s2.resample(px, step)
        s_orig = np.concatenate([[0.0], np.cumsum(np.hypot(*(px[1:] - px[:-1]).T))])
        rr = np.interp(t, s_orig, rpx)
        n = len(pts)
        cols, rows = pts[:, 0] + 0.5, pts[:, 1] + 0.5
        X = (cols - spr.hx) / s2.CELL
        if par is None:
            start = np.array([X[0], (spr.hy - rows[0] - 8 * root_z) / s2.CELL, root_z])
        else:
            pb = out[par]
            start = pb["P"][min(pb["map"][b["at"]], len(pb["P"]) - 1)].copy()
        if root_xy is None:
            root_xy = start[:2].copy()
        Y, Z = np.zeros(n), np.zeros(n)
        Y[0], Z[0] = start[1], start[2]
        k0 = 0
        if i in trunk:
            # upright below trunk_top: depth held, height from the rows
            for k in range(1, n):
                z = (spr.hy - rows[k] - s2.CELL * start[1]) / (s2.CELL * s2.TILT)
                if z > spec.get("trunk_top", 1e9):
                    break
                Y[k], Z[k] = start[1], z
                k0 = k
        side = 0
        mirror = None
        is_root = False
        if k0 < n - 1:
            sx = X[-1] - X[k0]
            sy = (rows[k0] - rows[-1]) / s2.CELL
            L = (t[-1] - t[k0]) / s2.CELL
            zk, yk = Z[k0], Y[k0]
            if mode == "swing":
                if par is None or par in trunk or out[par]["side"] == 0:
                    side = first_side.get(i, 1)
                else:
                    side = out[par]["side"] if rng.random() > 0.25 else -out[par]["side"]
                if spec.get("elev") and i in trunk:
                    # the trunk above trunk_top leans back, steeper than a limb
                    dY = elev_depth(sx, sy, rng.uniform(*spec.get("trunk_elev", (55, 70))))
                elif spec.get("elev") and sy < 0 and zk < spec.get("root_level", 1.5):
                    dY = 0.0  # a root drawn running down: kept in the screen plane
                    is_root = True
                elif spec.get("elev"):
                    dY = elev_depth(sx, sy, rng.uniform(*spec["elev"]))
                    if side < 0 and dY > 0 and spec.get("mirror"):
                        mirror = -spec["mirror"]
                else:
                    psi = math.radians(rng.uniform(*spec.get("psi", (40, 70))) * (0.7 if rank.get(i, 1) == 0 else 1.0))
                    dY = side * L * math.sin(psi)
                dZ = 2 * (sy - dY)
            else:
                gentle = spec.get("rise", 0.35) * L
                ex = X[-1] - root_xy[0]
                M = math.sqrt(max(spec.get("R", 3.0) ** 2 - ex * ex, 0.0))
                off = yk - root_xy[1]
                lo, hi = 2 * (sy - (M - off)), 2 * (sy - (-M - off))
                dZ = float(np.clip(gentle, lo, hi)) if lo <= hi else gentle
            # never into the ground
            dZ = max(dZ, spec.get("zfloor", 0.3) + rr[-1] / s2.CELL - zk)
            for k in range(k0 + 1, n):
                u = (t[k] - t[k0]) / max(t[-1] - t[k0], 1e-6)
                Z[k] = zk + dZ * u ** p
                Y[k] = (spr.hy - rows[k] - s2.CELL * s2.TILT * Z[k]) / s2.CELL
                zf = rr[k] / s2.CELL * 0.7
                if Z[k] < zf:
                    Z[k] = zf
                    Y[k] = (spr.hy - rows[k] - s2.CELL * s2.TILT * zf) / s2.CELL
        if mirror is not None:
            # a front limb: its depth off the ray, mirrored toward the camera
            Y[k0 + 1:] = Y[k0] + mirror * (Y[k0 + 1:] - Y[k0])
        if spec.get("reach") and side and i not in trunk and not is_root:
            # side limbs reaching further than drawn, the same way
            Y[k0 + 1:] = Y[k0] + spec["reach"] * (Y[k0 + 1:] - Y[k0])
            Z[k0 + 1:] = Z[k0] + spec["reach"] * (Z[k0 + 1:] - Z[k0])
            X = X.copy()
            X[k0 + 1:] = X[k0] + spec["reach"] * (X[k0 + 1:] - X[k0])
        P = np.stack([X, Y, Z], 1)
        P[0] = start
        if spec.get("taper"):
            # a smooth taper: the drawn widths averaged, never swelling outward
            rr = np.convolve(np.pad(rr, 2, mode="edge"), np.ones(5) / 5, mode="valid")
            for k in range(1, n):
                rr[k] = min(rr[k], rr[k - 1] * 1.02)
        idx = np.searchsorted(t, s_orig, side="left").clip(0, n - 1)
        mir = mirror is not None or (par is not None and out[par].get("mir", False))
        st = spec.get("straighten")
        if is_root and spec.get("root_slope") and n > 1:
            # a root drawn running down: a flare sloping into the ground
            # within root_slope cells of the trunk
            hd = np.array([P[-1, 0] - P[0, 0], P[-1, 1] - P[0, 1]])
            hd = hd / (np.linalg.norm(hd) + 1e-9)
            u = np.linspace(0, 1, n)
            reach = spec["root_slope"] / SHAPE["girth"]
            P = np.stack([P[0, 0] + hd[0] * reach * u, P[0, 1] + hd[1] * reach * u, P[0, 2] * (1 - u) ** 1.3 + 0.02], 1)
            rr = np.linspace(rr[0], max(rr[0] * 0.15, 0.5), n)
        elif st and par is not None and i not in trunk and not is_root and (st.get("all") or mir):
            pb = out[par]
            j = min(pb["map"][b["at"]], len(pb["P"]) - 1)
            T = pb["P"][j] - pb["P"][max(j - 1, 0)] if j > 0 else pb["P"][1] - pb["P"][0]
            lens = None
            if par is not None and pb.get("mir", False):
                # off its drawn rays: the drawn lengths, stretched
                lens = np.diff(t) / s2.CELL * st.get("stretch", 1.3)
            P = straighten(P, None if (par in trunk and not pb.get("mir")) else T, st, lens)
        out.append({"P": P, "R": rr / s2.CELL, "map": idx, "parent": par, "side": side, "mir": mir,
                    "depth": 0 if par is None else out[par]["depth"] + 1})
    return out


def _turn(a, b, ang):
    """Unit a turned toward b by ang radians (any way round if b is a)."""
    w = b - a * float(np.dot(a, b))
    if np.linalg.norm(w) < 1e-6:
        w = np.cross(a, [0.0, 0.0, 1.0]) if abs(a[2]) < 0.95 else np.array([1.0, 0.0, 0.0])
    w = _unit(w)
    return _unit(a * math.cos(ang) + w * math.sin(ang))


def _clamp_elev(d, lo, hi):
    h = math.hypot(d[0], d[1])
    e = math.atan2(d[2], h)
    if lo <= e <= hi:
        return d
    e = min(max(e, lo), hi)
    hd = d[:2] / h if h > 1e-9 else np.array([1.0, 0.0])
    return np.array([hd[0] * math.cos(e), hd[1] * math.cos(e), math.sin(e)])


def straighten(P, T, st, lengths=None):
    """A limb smoothed into dead wood, in the scaled frame: every segment
    climbing st 'elev' degrees, turning at most 'bend' degrees from the one
    before, and a fork leaving its parent's tangent T at 'fork' degrees
    toward the parent's tip. lengths replaces the segment lengths."""
    S = np.array([SHAPE["girth"], SHAPE["girth"], SHAPE["height"]])
    Q = P * S
    seg = Q[1:] - Q[:-1]
    ln = np.linalg.norm(seg, axis=1) if lengths is None else np.asarray(lengths) * SHAPE["girth"]
    lo, hi = (math.radians(a) for a in st.get("elev", (30, 45)))
    bend = math.radians(st.get("bend", 30))
    fl, fh = (math.radians(a) for a in st.get("fork", (30, 45)))
    out, prev = [Q[0]], None
    for k in range(len(seg)):
        d = _clamp_elev(_unit(seg[k]), lo, hi)
        if prev is None and T is not None:
            t = _unit(T * S)
            a = math.acos(float(np.clip(np.dot(d, t), -1, 1)))
            d = _clamp_elev(_turn(t, d, min(max(a, fl), fh)), lo, max(hi, math.radians(60)))
        elif prev is not None:
            a = math.acos(float(np.clip(np.dot(d, prev), -1, 1)))
            if a > bend:
                d = _turn(prev, d, bend)
        out.append(out[-1] + d * ln[k])
        prev = d
    return np.array(out) / S


def elev_depth(sx, sy, deg):
    """How far north a limb drawn sx cells across and sy up the screen
    reaches so it climbs at deg degrees once the sturdiness pass has
    scaled it: the one depth on its end pixel's camera ray."""
    T = min(math.tan(math.radians(deg)) * SHAPE["girth"] / SHAPE["height"], 1.95)
    a = 4 - T * T
    return (4 * sy - T * math.sqrt(sx * sx * a + 4 * sy * sy)) / a


def extra_limbs(limbs, spec, rng):
    """Rising limbs off the trunk toward and away from the camera, where a
    drawing shows none: {"front": n, "back": n, "z": (lo, hi) of the trunk's
    height, "len": (lo, hi) cells, "elev": (lo, hi) degrees, "r": base
    radius in cells}."""
    T = limbs[0]
    P, R = T["P"], T["R"]
    todo = [(sgn, k, n) for sgn, n in ((-1.0, spec.get("front", 3)), (1.0, spec.get("back", 3))) for k in range(n)]
    if spec.get("az"):
        # explicit azimuth ranges, degrees from +x (90 is straight back)
        todo = [(1.0 if (lo + hi) / 2 % 360 < 180 else -1.0, k, None) for k, (lo, hi) in enumerate(spec["az"])]
    for sgn, k, n in todo:
        if spec.get("zabs"):
            # join heights in cells once the sturdiness pass has scaled them
            zt = rng.uniform(*spec["zabs"]) / SHAPE["height"]
        else:
            zt = P[:, 2].max() * rng.uniform(*spec.get("z", (0.55, 0.85)))
        j = int(np.argmin(np.abs(P[:, 2] - zt)))
        if n is None:
            az = math.radians(rng.uniform(*spec["az"][k]))
        else:
            az = sgn * math.radians(90 + (k - (n - 1) / 2) * 110 / max(n - 1, 1) + rng.uniform(-12, 12))
        el = math.radians(rng.uniform(*spec.get("elev", (20, 40))))
        if spec.get("elev_post"):
            el = math.atan(math.tan(el) * SHAPE["girth"] / SHAPE["height"])
        L = rng.uniform(*spec.get("len", (1.5, 2.5)))
        d = np.array([math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)])
        m = 6
        Q = np.array([P[j] + d * L * t + np.array([0, 0, 0.25 * L * t * t]) for t in np.linspace(0, 1, m)])
        if spec.get("kink"):
            # crooked: the outer half turned off the inner half's line
            sd = np.cross(d, [0.0, 0.0, 1.0]) * rng.choice((-1.0, 1.0))
            d_out = _turn(d, sd, math.radians(rng.uniform(*spec["kink"])))
            for q, t in enumerate(np.linspace(0, 1, m)):
                if t > 0.45:
                    Q[q] = P[j] + d * L * 0.45 + d_out * L * (t - 0.45) + np.array([0, 0, 0.25 * L * t * t])
        r0 = spec.get("r", 0.2)
        limbs.append({"P": Q, "R": np.linspace(r0, r0 * 0.3, m), "map": np.arange(m), "parent": 0, "side": sgn,
                      "depth": 1})
        # a fork off its outer half
        f = int(rng.integers(2, m - 1))
        d2 = _unit(d + np.array([math.cos(az + sgn), math.sin(az + sgn), 0.6]) * 0.6)
        Q2 = np.array([Q[f] + d2 * L * spec.get("fork_len", 0.45) * t for t in np.linspace(0, 1, 4)])
        limbs.append({"P": Q2, "R": np.linspace(r0 * 0.5, r0 * 0.15, 4), "map": np.arange(4), "parent": len(limbs) - 1,
                      "side": sgn, "depth": 2})
    return limbs


def root_flare(geo, base, r, n, reach, rng, col, h=None):
    """n buttress roots leaving a trunk of radius r at base and running out
    reach cells beyond its bark to thin tips on the ground."""
    h = h or 1.2 * r
    ph = rng.uniform(0, TAU)
    for k in range(n):
        a = ph + TAU * k / n + rng.uniform(-0.25, 0.25)
        d = np.array([math.cos(a), math.sin(a), 0.0])
        L = r + reach * rng.uniform(0.75, 1.0)
        ts = np.linspace(0, 1, 6)
        Q = np.array([base + d * (0.35 * r + (L - 0.35 * r) * t) + np.array([0, 0, h * (1 - t) ** 1.8 + 0.03]) for t in ts])
        tube(geo, Q, np.linspace(0.5 * r, 0.05, 6) / SHAPE["radius"], sides=6, col=col * rng.uniform(0.85, 1.0), vrep=BARK_REP)


def buttress(geo, T, n, reach, rng, col, flare=0.6):
    """A trunk T's foot widened into n ridges, a root leaving each one a
    third of the trunk's radius thick and sloping down to a thin tip on
    the ground reach cells (once scaled) beyond the bark."""
    base = T["P"][0] * np.array([1, 1, 0])
    r = float(T["R"][0])
    rs = r * SHAPE["radius"]
    ph = rng.uniform(0, TAU) + TAU * np.arange(n) / n + rng.uniform(-0.25, 0.25, n)
    k = int(np.argmin(np.abs(T["P"][:, 2] - 3.0 * rs)))
    flared_trunk(geo, base, T["P"][k], r * 1.02, float(T["R"][k]), flare, n, rng, col, sides=14, rings=8, hf=1.1 * rs,
                 phases=ph)
    for a in ph:
        d = np.array([math.cos(a), math.sin(a), 0.0])
        L = rs + reach / SHAPE["girth"] * rng.uniform(0.85, 1.0)
        u = np.linspace(0, 1, 9)
        # out of the ridge half way in and high up, thick, wedging down
        Q = np.array([base + d * (0.5 * rs + (L - 0.5 * rs) * t) + np.array([0, 0, 1.3 * rs * (1 - t) ** 2 + 0.03])
                      for t in u])
        rad = 0.45 * rs * (1 - u) ** 1.1 + 0.03
        c = col * rng.uniform(0.85, 1.0)
        tube(geo, Q, rad / SHAPE["radius"], sides=7, col=c, vrep=BARK_REP)
        # a second, higher ridge along its inner half: a fin, not a spike
        tube(geo, Q + np.stack([0 * u, 0 * u, 0.7 * rad * (1 - u)], 1), 0.7 * rad * (1 - u) ** 1.5 / SHAPE["radius"] + 0.01,
             sides=6, col=c, vrep=BARK_REP)


def fit_prongs(limbs, pf, trunk_top):
    """Listed prongs refitted for the sturdiness pass: from where each
    leaves the trunk (on the trunk, above trunk_top) its height grows 'f'
    times the drawn height, and a lean back (plus 'keep' of its lifted
    depth) takes up the rest, so its classic tip lands on the drawn one.
    Limbs off a moved prong move with it."""
    g, h = SHAPE["girth"], SHAPE["height"]
    f, keep = pf.get("f", 0.85), pf.get("keep", 0.3)
    old = {}
    for i, L in enumerate(limbs):
        P = L["P"]
        if i in pf["branches"]:
            above = P[:, 2] > trunk_top
            if i == 0 and not above.any():
                continue
            ks = int(np.argmax(above)) if i == 0 else 0
            D = 16 * P[:, 1] + 8 * P[:, 2]
            dD = D[ks:] - D[ks]
            Q = P.copy()
            # drawn spread across, not the sturdiness pass's, and slimmer
            Q[ks:, 0] = P[ks, 0] + (P[ks:, 0] - P[ks, 0]) * pf.get("x", 1.0)
            L["R"] = L["R"].copy()
            L["R"][ks:] *= pf.get("r", 1.0)
            Q[ks:, 1] = P[ks, 1] + (1 - f) * dD / (16 * g) + keep * (P[ks:, 1] - P[ks, 1])
            Q[ks:, 2] = P[ks, 2] + (dD - 16 * g * (Q[ks:, 1] - P[ks, 1])) / (8 * h)
            old[i], L["P"] = P, Q
        elif L["parent"] in old:
            po, pn = old[L["parent"]], limbs[L["parent"]]["P"]
            j = int(np.argmin(np.linalg.norm(po - P[0], axis=1)))
            old[i], L["P"] = P, P + (pn[j] - po[j])


def blend_join(limbs, i, blend):
    """Limb i eased into its parent over its first blend cells: finely
    ringed there, its radius running smoothly from inside the parent's to
    its own, so the join shows no ledge."""
    L = limbs[i]
    P, R = L["P"], L["R"]
    par = limbs[L["parent"]]
    j = int(np.argmin(np.linalg.norm(par["P"] - P[0], axis=1)))
    Rp = float(par["R"][j])
    s = np.concatenate([[0.0], np.cumsum(np.linalg.norm(P[1:] - P[:-1], axis=1))])
    b = min(blend, 0.8 * s[-1])
    g = np.concatenate([np.linspace(0, b, 10), s[s > b + 1e-6]])
    P2 = np.stack([np.interp(g, s, P[:, k]) for k in range(3)], 1)
    R2 = np.interp(g, s, R)
    w = np.clip(g / b, 0, 1)
    w = w * w * (3 - 2 * w)
    R2 = R2 * w + min(0.85 * Rp, float(R2[0])) * (1 - w)
    L["P"], L["R"], L["sides"] = P2, R2, 16


def splinters(limbs, sp, rng):
    """Listed limbs ending in 2-3 jagged splinters: the limb's last quarter
    tapers to half, and thin spikes splay out of its end."""
    for i in sp.get("branches", []):
        if i >= len(limbs):
            continue
        L = limbs[i]
        P, R = L["P"], L["R"]
        u = np.linspace(0, 1, len(R))
        R = R * (1 - 0.5 * np.clip((u - 0.75) / 0.25, 0, 1))
        L["R"] = R
        T = _unit(P[-1] - P[max(len(P) - 3, 0)])
        for k in range(int(rng.integers(sp.get("n", (2, 3))[0], sp.get("n", (2, 3))[1] + 1))):
            a = rng.uniform(0, TAU)
            side = _unit(np.cross(T, [math.cos(a), math.sin(a), 0.3]))
            d = _turn(T, side, math.radians(rng.uniform(*sp.get("splay", (10, 28)))))
            ln = rng.uniform(*sp.get("len", (0.3, 0.6))) / SHAPE["girth"]
            p0 = P[-1] - T * 0.15
            Q = np.array([p0 + d * ln * t + rng.normal(0, 0.02, 3) * (0 < t < 1) for t in np.linspace(0, 1, 3)])
            limbs.append({"P": Q, "R": np.array([R[-1] * 0.55, R[-1] * 0.3, 0.012]) / 1.0, "map": np.arange(3),
                          "parent": i, "side": 0, "depth": L.get("depth", 1) + 1, "sides": 4})


def smooth_bole(geo, base, top, r, rng, col, flare=0.35, knots=3, sides=24, rings=14):
    """A knotted trunk in one smooth piece from the ground to top: a soft
    foot, a few rounded knots, widening at the top into the limbs it
    carries and closed by a dome. Normals come from the surface itself, so
    it shades smooth across the dome's seam."""
    r = r * SHAPE["radius"]
    H = float(top[2] - base[2])
    K = [(rng.uniform(0.2, 0.8) * H, rng.uniform(0, TAU), rng.uniform(0.15, 0.25)) for _ in range(knots)]
    ph = rng.uniform(0, TAU, 3)
    a = TAU * np.arange(sides + 1) / sides
    ring_dir = np.stack([np.cos(a), np.sin(a), np.zeros_like(a)], 1)
    G, Vz = [], []
    for t in np.linspace(0, 1, rings):
        z = t * H
        rr = r * (1 + flare * math.exp(-z / (0.45 * H)) + 0.15 * t ** 2)
        f = 1 + 0.06 * np.sin(2 * a + ph[0]) + 0.04 * np.sin(3 * a + ph[1])
        f += 0.6 * flare * math.exp(-z / (0.22 * H)) * ((1 + np.cos(5 * a + ph[2])) / 2) ** 3
        for kz, ka, amp in K:
            da = (a - ka + math.pi) % TAU - math.pi
            f += amp * np.exp(-((z - kz) / (0.12 * H)) ** 2 - (da / 0.7) ** 2)
        G.append(base + (top - base) * t + (rr * f)[:, None] * ring_dir)
        Vz.append(z)
    G[0] = G[0] - np.array([0, 0, 0.05])
    last = G[-1] - top
    hd = 0.6 * float(np.linalg.norm(last[:, :2], axis=1).mean())
    for th in np.radians((25, 50, 72)):
        G.append(top + last * math.cos(th) + np.array([0, 0, hd * math.sin(th)]))
    G = np.array(G)  # (rings + 3, sides + 1, 3)
    pole = top + np.array([0, 0, hd])
    # normals from the grid: across the rings and up them
    da_ = np.roll(G[:, :-1], -1, 1) - np.roll(G[:, :-1], 1, 1)
    da_ = np.concatenate([da_, da_[:, :1]], 1)
    up = np.empty_like(G)
    up[1:-1] = G[2:] - G[:-2]
    up[0], up[-1] = G[1] - G[0], pole - G[-2]
    N = _unit(np.cross(da_, up))
    urep = max(1, round(TAU * r * BARK_REP * 2))
    cols = [col * (0.8 + 0.2 * min(z / H, 1.0)) for z in Vz] + [col] * 3
    V, UV, CC, NN = [], [], [], []
    for i in range(rings):
        V += list(G[i])
        UV += [(urep * k / sides, Vz[i] * BARK_REP) for k in range(sides + 1)]
        CC += [cols[i]] * (sides + 1)
        NN += list(N[i])
    # the dome again from the top ring, mapped from above so the bark does
    # not pinch into a swirl; the seam shares the ring's normals
    for i in range(rings - 1, rings + 3):
        V += list(G[i])
        UV += [tuple((G[i, k, :2] - top[:2]) * BARK_REP * 2) for k in range(sides + 1)]
        CC += [cols[min(i, len(cols) - 1)]] * (sides + 1)
        NN += list(N[i])
    V.append(pole)
    UV.append((0.0, 0.0))
    CC.append(col)
    NN.append(np.array([0.0, 0.0, 1.0]))
    m = sides + 1
    quads = [(i, k) for i in range(rings - 1) for k in range(sides)] + [(i, k) for i in range(rings, rings + 3)
                                                                        for k in range(sides)]
    F = [(i * m + k, i * m + k + 1, (i + 1) * m + k + 1, (i + 1) * m + k) for i, k in quads]
    F += [((rings + 3) * m + k, (rings + 3) * m + k + 1, len(V) - 1) for k in range(sides)]
    geo.add(np.array(V), F, np.array(UV), np.array(CC), np.array(NN))


def roughen(limbs, rng, knobs=0.0, wiggle=0.0, tip_thin=1.0):
    """Gnarls lifted limbs: knobs swell the radius in a few random places,
    wiggle kinks the outer part of each limb side to side, and tip_thin
    under 1 tapers the tips finer."""
    for L in limbs:
        P, R = L["P"], L["R"]
        n = len(P)
        if n < 3:
            continue
        s = np.concatenate([[0.0], np.cumsum(np.linalg.norm(P[1:] - P[:-1], axis=1))])
        u = s / max(s[-1], 1e-6)
        if knobs:
            bump = np.zeros(n)
            for _ in range(max(1, int(s[-1] / 0.6))):
                c = rng.uniform(0, s[-1])
                bump += rng.uniform(0.5, 1.0) * np.exp(-((s - c) / 0.18) ** 2)
            R *= 1 + knobs * bump
        if tip_thin < 1.0:
            R *= 1 - (1 - tip_thin) * u ** 2
        if wiggle:
            T = np.gradient(P, axis=0)
            T /= np.linalg.norm(T, axis=1, keepdims=True) + 1e-9
            a = rng.uniform(0, TAU)
            w0 = _unit(np.cross(T[n // 2], [0.0, 0.0, 1.0]) + 1e-6)
            freq = rng.uniform(9, 14)
            amp = wiggle * np.clip((u - 0.4) / 0.6, 0, 1) ** 1.5 * min(1.0, s[-1] / 1.5)
            P += w0[None, :] * (amp * np.sin(freq * u + a))[:, None]
            P[:, 2] += (0.5 * amp * np.sin(freq * 0.7 * u + 2 * a))
        L["P"], L["R"] = P, R
    return limbs


def bole(geo, base, top, r, flare, rng, col, knots=0.12, sides=14, rings=10, nl=5):
    """A knotted, buttressed trunk from the ground up to top: a flared
    trunk whose rings swell and pinch at random, capped by a burl."""
    V0 = len(geo.v)
    flared_trunk(geo, base, top, r * 1.05, r * 0.9, flare, nl, rng, col, sides=sides, rings=rings, hf=r * 1.2)
    v = geo.v[V0]
    m = sides + 1
    for j in range(1, rings):
        c = base + (top - base) * (np.linspace(0, 1, rings) ** 1.8)[j]
        ring = v[j * m:(j + 1) * m]
        k = 1 + knots * rng.normal(0, 1) + knots * 0.6 * rng.normal(0, 1, len(ring))
        k[-1] = k[0]
        ring[:, :2] = c[:2] + (ring[:, :2] - c[:2]) * k[:, None]
    ellipsoid(geo, top, (r * 1.15, r * 1.1, r * 0.75), seg=12, rings=6, col=col * 0.9, normals_from=top, jitter=0.2, rng=rng,
              rep=BARK_REP)


def px3(spr, col, row, y):
    """The point drawn at pixel (col, row) that lies y cells north."""
    return np.array([(col + 0.5 - spr.hx) / 16, y, (spr.hy - row - 0.5 - 16 * y) / 8])


def loft(geo, P, RX, RY, sides, rng, amp=None, ridges=12, twist=0.4, col=(0.1, 0.1, 0.1), groove=0.5, vrep=1.0):
    """A closed body along spine P with elliptical rings (RX along world x,
    RY along world y), in vertical fibre ridges of amp (per ring) times the
    radius; grooves take groove times the colour. Ends are pointed caps.
    Returns the per-vertex ridge value for the caller's use."""
    P = np.asarray(P, np.float64)
    n = len(P)
    T = np.zeros_like(P)
    T[1:-1] = P[2:] - P[:-2]
    T[0], T[-1] = P[1] - P[0], P[-1] - P[-2]
    T = _unit(T)
    u, w = frame(T[0])
    s = np.concatenate([[0.0], np.cumsum(np.linalg.norm(P[1:] - P[:-1], axis=1))])
    amp = np.zeros(n) if amp is None else np.asarray(amp, np.float64)
    # each ridge its own height and a slow wander, so the fibres are uneven
    ph1, ph2 = rng.uniform(0, TAU, 2)
    rk = rng.uniform(0.5, 1.3, ridges)
    V, UV, CC = [], [], []
    col = np.asarray(col, np.float64)
    for i in range(n):
        if i:
            t0, t1 = T[i - 1], T[i]
            ax = np.cross(t0, t1)
            sn = np.linalg.norm(ax)
            if sn > 1e-8:
                u = _rot(u, ax / sn, math.atan2(sn, float(np.dot(t0, t1))))
            u -= T[i] * np.dot(u, T[i])
            u /= np.linalg.norm(u) + 1e-12
            w = np.cross(T[i], u)
        for k in range(sides + 1):
            a = TAU * (k % sides) / sides
            aa = a + twist * s[i] + 0.15 * math.sin(1.7 * s[i] + ph1)
            g = math.cos(ridges * aa)
            g = 0.75 * math.copysign(abs(g) ** 0.7, g) + 0.25 * math.cos((2 * ridges + 1) * aa + ph2)
            g *= rk[int((aa % TAU) / TAU * ridges) % ridges]
            d = math.cos(a) * u + math.sin(a) * w
            off = d * np.array([RX[i], RY[i], 0.5 * (RX[i] + RY[i])])
            V.append(P[i] + off * (1 + amp[i] * g))
            UV.append((round(TAU * float(np.mean(RX)) * vrep) * k / sides, s[i] * vrep))
            # the drawn fibres: thin bright ridges over wide dark grooves
            lit = float(np.clip(0.5 + 0.5 * g, 0, 1)) ** 3
            CC.append(col * (groove + (1 - groove) * lit))
    m = sides + 1
    F = []
    for i in range(n - 1):
        for k in range(sides):
            a0, b0 = i * m + k, i * m + k + 1
            F.append((a0, b0, b0 + m, a0 + m))
    base = len(V)
    V += [P[0] - T[0] * 0.02, P[-1] + T[-1] * 0.02]
    UV += [(0.5, 0.0), (0.5, s[-1] * vrep)]
    CC += [col * groove, col * groove]
    for k in range(sides):
        F.append((k + 1, k, base))
        F.append(((n - 1) * m + k, (n - 1) * m + k + 1, base + 1))
    geo.add(np.array(V), F, np.array(UV), np.array(CC))


def fibre_body(geo, spr, body, rng, col):
    """A swollen trunk in one piece, a ball on the ground running up into a
    neck, its bark in vertical fibre ridges. body: {"ball": ((col, row), y,
    (rx, ry, rz)), "neck": [((col, row), y, r), ...], "ridges", "amp"}.
    Returns the top of the neck (or ball), where a tuft is seated."""
    (bc, br_), by, (rx, ry, rz) = body["ball"]
    C = px3(spr, bc, br_, by)
    neck = body.get("neck", [])
    th1 = math.radians(body.get("shoulder", 118)) if neck else math.pi - 0.05
    ths = np.linspace(0.08, th1, body.get("ball_rings", 11))
    P = [C + np.array([0, 0, -rz * math.cos(t)]) for t in ths]
    RX = [rx * math.sin(t) for t in ths]
    RY = [ry * math.sin(t) for t in ths]
    top = C + np.array([0, 0, rz])
    if neck:
        # the neck: from the ball's shoulder through the listed points, the
        # spine leaving the shoulder straight up and bending over smoothly
        K = [P[-1]] + [px3(spr, c, r, y) for (c, r), y, _ in neck]
        KR = [(RX[-1], RY[-1])] + [(rr, rr) for _, _, rr in neck]
        per = body.get("neck_rings", 5)
        # Catmull-Rom through the knots, a phantom point below the shoulder
        # so the spine leaves it going straight up
        G = [K[0] - np.array([0, 0, float(np.linalg.norm(K[1] - K[0]))])] + K + [2 * K[-1] - K[-2]]
        for q in range(len(K) - 1):
            p0, p1, p2, p3 = G[q], G[q + 1], G[q + 2], G[q + 3]
            for j in range(1, per + 1):
                t = j / per
                p = 0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                           + (3 * p1 - p0 - 3 * p2 + p3) * t ** 3)
                e = t * t * (3 - 2 * t)
                P.append(p)
                RX.append(KR[q][0] * (1 - e) + KR[q + 1][0] * e)
                RY.append(KR[q][1] * (1 - e) + KR[q + 1][1] * e)
        top = P[-1].copy()
        # a rounded end
        tdir = _unit(P[-1] - P[-2])
        for f in (0.75, 0.4):
            P.append(P[-1] + tdir * RX[-1] * 0.35)
            RX.append(RX[-1] * f)
            RY.append(RY[-1] * f)
    else:
        ths2 = np.linspace(th1, math.pi - 0.08, 3)[1:]
        for t in ths2:
            P.append(C + np.array([0, 0, -rz * math.cos(t)]))
            RX.append(rx * math.sin(t))
            RY.append(ry * math.sin(t))
    P = np.array(P)
    n = len(P)
    # ridges fade toward the foot pole and the ends
    amp = np.full(n, body.get("amp", 0.2))
    amp[:3] *= np.array([0.2, 0.5, 0.8])
    loft(geo, P, np.array(RX), np.array(RY), body.get("sides", 36), rng, amp, body.get("ridges", 9),
         body.get("twist", 0.35), col, body.get("groove", 0.05), vrep=BARK_REP * 2)
    return top, (P, np.array(RX), np.array(RY))


def inside_body(p, spine):
    """Whether point p lies within the lofted body (spine, x radii, y radii)."""
    P, RX, RY = spine
    d = np.linalg.norm(P - p, axis=1)
    i = int(np.argmin(d))
    T = _unit(P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)])
    q = p - P[i]
    along = float(np.dot(q, T))
    q = q - T * along
    step = float(np.linalg.norm(P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]))
    return (q[0] / max(RX[i], 1e-3)) ** 2 + (q[1] / max(RY[i], 1e-3)) ** 2 + (q[2] / max(RX[i], 1e-3)) ** 2 < 0.8 \
        and abs(along) <= step


def root_ball(geo, spr, ball, rng, col, spike_col):
    """A lumpy black mass of roots: a knobbed ellipsoid bristling with short
    root spikes over its lower half, a few roots coiling over it.
    ball: ((col, row), y, (rx, ry, rz)) plus spec keys spikes, coils."""
    (bc, br_), by, R = ball["at"]
    C = px3(spr, bc, br_, by)
    R = np.asarray(R, np.float64)
    seg, rings = 22, 12
    # lumps: bumps on the sphere of directions
    bumps = [(_unit(rng.normal(0, 1, 3)), rng.uniform(0.12, 0.26), rng.uniform(0.3, 0.5)) for _ in range(20)]

    def lump(d):
        return 1 + sum(a * math.exp(-(1 - float(np.dot(d, c))) / (w * w)) for c, a, w in bumps) - 0.12

    V, F, CC, UV = [], [], [], []
    V.append(C + R * np.array([0, 0, lump(np.array([0, 0, 1.0]))]))
    UV.append((0.5, 0.0))
    CC.append(col)
    for j in range(1, rings):
        th = math.pi * j / rings
        for k in range(seg + 1):
            ph = TAU * (k % seg) / seg + (0.5 * TAU / seg if j % 2 else 0.0)
            d = np.array([math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)])
            ln = lump(d) * (1 + 0.05 * rng.normal())
            V.append(C + d * R * ln)
            UV.append((3 * k / seg, 1.5 * j / rings))
            CC.append(col * (0.7 + 0.5 * (ln - 0.9)) * (0.75 + 0.25 * max(d[2], 0)))
    V.append(C - R * np.array([0, 0, 1.0]))
    UV.append((0.5, 1.5))
    CC.append(col * 0.6)
    m = seg + 1
    for k in range(seg):
        F.append((0, 1 + k, 1 + k + 1))
    for j in range(rings - 2):
        a0, b0 = 1 + j * m, 1 + (j + 1) * m
        for k in range(seg):
            F.append((a0 + k + 1, a0 + k, b0 + k, b0 + k + 1))
    last = len(V) - 1
    a0 = 1 + (rings - 2) * m
    for k in range(seg):
        F.append((a0 + k + 1, a0 + k, last))
    geo.add(np.array(V), F, np.array(UV), np.array(CC))

    def surf(d):
        d = _unit(d)
        return C + d * R * lump(d)

    # short root spikes over the lower half, out and a little down
    for k in range(ball.get("spikes", 26)):
        ph = TAU * k / ball.get("spikes", 26) + rng.uniform(-0.12, 0.12)
        dz = rng.uniform(-0.7, 0.35)
        d = _unit(np.array([math.cos(ph), math.sin(ph), 0.0]) * math.sqrt(1 - dz * dz) + np.array([0, 0, dz]))
        p0 = surf(d) - d * 0.12
        if p0[2] + 0.15 < 0:
            continue
        L = rng.uniform(0.45, 0.95) * ball.get("spike_len", 1.0)
        out = _unit(d + np.array([0, 0, -0.35]) + rng.normal(0, 0.15, 3))
        p1 = p0 + out * L * 0.5 + _unit(rng.normal(0, 1, 3)) * 0.05
        p2 = p0 + out * L
        p2[2] = max(p2[2], 0.02)
        r0 = rng.uniform(0.1, 0.16)
        tube(geo, np.array([p0, p1, p2]), np.array([r0, r0 * 0.6, r0 * 0.15]), sides=5,
             cols=np.array([spike_col, spike_col * 0.9, spike_col * 0.8]) * rng.uniform(0.85, 1.15))
    # roots coiling over the surface from the top down to the ground
    for k in range(ball.get("coils", 4)):
        ph0 = rng.uniform(0, TAU)
        turn = rng.choice([-1, 1]) * rng.uniform(0.6, 1.2)
        pts = []
        for t in np.linspace(0, 1, 12):
            th = 0.35 + t * 2.2
            ph = ph0 + turn * t * math.pi
            d = np.array([math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)])
            pts.append(surf(d) + d * 0.04)
        pts = np.array(pts)
        pts[:, 2] = np.maximum(pts[:, 2], 0.03)
        r0 = rng.uniform(0.07, 0.1)
        tube(geo, pts, np.linspace(r0, r0 * 0.35, len(pts)), sides=5, col=spike_col * rng.uniform(0.9, 1.2))


def _rise_step(dc, dr, q):
    """How far north (px, 16 to a cell) a limb goes over a drawn step (dc,
    dr) when it climbs q/2 of its horizontal run: solves -Y - dr =
    q * hypot(dc, Y), the step staying on the classic camera's rays."""
    a = 1 - q * q
    root = q * math.sqrt(max(dr * dr + a * dc * dc, 0.0))
    best, err = 0.0, 1e18
    for Y in ((-dr + root) / a, (-dr - root) / a):
        e = abs(-Y - dr - q * math.hypot(dc, Y))
        if e < err:
            best, err = Y, e
    return best


def limbs_geo(geo, limbs, bark_col, rng, tip_col=None, base_dark=0.7, sides=None, min_len=0.0, sides_min=3):
    """Tubes for lifted limbs, darker toward the ground, tips toward tip_col."""
    for L in limbs:
        P, R = L["P"], L["R"]
        if len(P) < 2:
            continue
        if np.linalg.norm(P[-1] - P[0]) < min_len:
            continue
        rm = float(R.max())
        n = L.get("sides") or sides or max(sides_min, 9 if rm > 0.45 else 7 if rm > 0.25 else 5 if rm > 0.12 else 4 if rm > 0.05
                                            else 3)
        z = P[:, 2]
        shade = np.clip(base_dark + (1 - base_dark) * np.clip(z / 2.0, 0, 1), 0, 1)
        jit = 1 + 0.12 * (rng.random() - 0.5)
        cols = [bark_col * s * jit for s in shade]
        if tip_col is not None:
            tt = np.linspace(0, 1, len(P)) ** 2 * min(1.0, L["depth"] * 0.5)
            cols = [mixc(c, tip_col, t) for c, t in zip(cols, tt)]
        tube(geo, P, np.maximum(R, 0.025), sides=n, cols=np.array(cols), cap_tip=True,
             cap_base=L["parent"] is None, vrep=BARK_REP)


# ---------------------------------------------------------------- builders

def build_limbs(spr, spec, rng):
    """A tree of tube limbs lifted from the sprite's skeleton: dead trees,
    stilt roots, tentacles. Options: 'stem' a trunk from the ground up to a
    raised root point, 'bulbs' an ellipsoid trunk (centre px, radii cells),
    'body' a fibre-ridged trunk in one piece (fibre_body), 'balls' spiked
    root masses (root_ball), 'cut' circles left out of the skeleton, 'arm_r'
    the radius cap of limbs climbing off the body, 'smooth_r' an even
    taper, 'twigs' a spray of fine twigs round the listed pixel points or
    seated in the body's top, 'foot' a heavier flared trunk base, 'roots'
    buttress roots, 'extra' rising limbs toward and away from the camera."""
    mask = spr.mask
    if spec.get("close"):
        mask = s2.close(mask, spec["close"])
    yy, xx = np.mgrid[0:spr.h, 0:spr.w]
    for c, r, rad in spec.get("cut", []):
        # regions a body or tuft models, left out of the skeleton
        mask = mask & ~((xx - c) ** 2 + (yy - r) ** 2 <= rad * rad)
    root = spec.get("root", (spr.hx, spr.hy))
    br = s2.skeleton_tree(mask, root, prune=spec.get("prune", (3.0, 1.2)), bridge=spec.get("bridge", 10.0))
    lspec = dict(spec.get("lift", {}))
    lspec.setdefault("rscale", 1.25)
    if lspec.get("mode") in ("swing", "balanced"):
        limbs = lift_smooth(spr, br, lspec, rng)
    else:
        limbs = lift(spr, br, lspec, rng)
    if limbs and spec.get("prong_fit"):
        fit_prongs(limbs, spec["prong_fit"], lspec.get("trunk_top", 1e9))
    if limbs and spec.get("extra"):
        for ex in (spec["extra"] if isinstance(spec["extra"], list) else [spec["extra"]]):
            extra_limbs(limbs, ex, rng)
    if limbs and spec.get("limb_taper"):
        # limbs beyond the trunk at their own radius, not the sturdiness
        # pass's, tapering to 'tip' of their base radius
        lt = spec["limb_taper"]
        for L in limbs[1:]:
            R = L["R"] * lt.get("radius", 1.0) / SHAPE["radius"]
            L["R"] = np.minimum(R, R[0] * (1 - (1 - lt.get("tip", 0.5)) * np.linspace(0, 1, len(R))))
    for i, cfg in spec.get("lift", {}).get("branch", {}).items():
        if cfg.get("blend") and i < len(limbs):
            blend_join(limbs, i, cfg["blend"])
    if limbs and spec.get("splinter"):
        splinters(limbs, spec["splinter"], rng)
    if limbs and spec.get("foot"):
        # a heavier trunk foot: the base radius (cells, after the
        # sturdiness pass) easing to the drawn one halfway up
        ft = spec["foot"]
        T = limbs[0]
        r0 = ft["r"] / (SHAPE["girth"] * SHAPE["radius"])
        zt = float(T["P"][:, 2].max())
        T["R"] = np.maximum(T["R"], r0 * np.clip(1 - T["P"][:, 2] / (ft.get("up", 0.5) * zt), 0, 1) ** 0.7)
    if spec.get("smooth_r"):
        # an even taper: the drawn widths averaged, never swelling outward
        for L in limbs:
            R = np.convolve(np.pad(L["R"], 3, mode="edge"), np.ones(7) / 7, mode="valid")
            for k in range(1, len(R)):
                R[k] = min(R[k], R[k - 1] * 1.01)
            L["R"] = R
    if spec.get("arm_r"):
        # limbs climbing off the body are thin crooked branches, not horns
        for L in limbs:
            if L["parent"] is not None and L["P"][-1][2] > L["P"][0][2] + 0.6:
                L["R"] = np.minimum(L["R"], spec["arm_r"] * np.linspace(1.0, 0.35, len(L["R"])))
    body, top = Geo(), None
    if spec.get("body"):
        # its ridges take the drawn body's lit fibres, its grooves near black
        ridge = lin(spr.colour(None, 0.8, 0.95), spec["body"].get("gain", 1.15))
        top, spine = fibre_body(body, spr, spec["body"], rng, ridge)
        # the skeleton runs through the drawn body too: thin those limbs
        # inside it, so only the roots and arms leaving it show
        for L in limbs:
            ins = np.array([inside_body(p, spine) for p in L["P"]])
            L["R"] = np.where(ins, np.minimum(L["R"], 0.18), L["R"])
    if spec.get("gnarl"):
        roughen(limbs, rng, **spec["gnarl"])
    tex, bark = bark_from(spr, None, spec.get("bark", "mottle"), gain=spec.get("gain", 1.25))
    geo = Geo()
    limbs_geo(geo, limbs, bark, rng, None, base_dark=spec.get("base_dark", 0.8), sides_min=spec.get("sides_min", 3))
    for bl in spec.get("balls", []):
        root_ball(geo, spr, bl, rng, bark * spec.get("bulb_shade", 0.4), bark * spec.get("spike_shade", 0.3))
    if limbs and spec.get("foot"):
        ft = spec["foot"]
        b0 = limbs[0]["P"][0] * np.array([1, 1, 0])
        k = int(np.argmin(np.abs(limbs[0]["P"][:, 2] - ft.get("h", 1.2))))
        flared_trunk(geo, b0, limbs[0]["P"][k], float(limbs[0]["R"][0]), float(limbs[0]["R"][k]), ft.get("flare", 0.5),
                     ft.get("n", 5), rng, bark * 0.8, sides=10, rings=7)
    if limbs and spec.get("roots", {}).get("buttress"):
        ro = spec["roots"]
        buttress(geo, limbs[0], ro.get("n", 6), ro.get("reach", 1.2), rng, bark * 0.8, ro.get("flare", 0.6))
    elif limbs and spec.get("roots"):
        ro = spec["roots"]
        rt = float(limbs[0]["R"][0]) * SHAPE["radius"]
        root_flare(geo, limbs[0]["P"][0] * np.array([1, 1, 0]), rt, ro.get("n", 6), ro.get("reach", 1.2) / SHAPE["girth"],
                   rng, bark * 0.8)
    if limbs and spec.get("bole", {}).get("smooth"):
        bo = spec["bole"]
        top = limbs[0]["P"][0]
        smooth_bole(geo, np.array([top[0], top[1], 0.0]), top, bo.get("r", 0.55), rng, bark * 0.9, bo.get("flare", 0.35),
                    bo.get("knots", 3))
    elif limbs and spec.get("bole"):
        # a knotted trunk from the ground up to the root the limbs spread from
        bo = spec["bole"]
        top = limbs[0]["P"][0]
        bole(geo, np.array([top[0], top[1], 0.0]), top, bo.get("r", 0.5), bo.get("flare", 0.8), rng, bark * 0.9,
             knots=bo.get("knots", 0.12))
    if limbs and spec.get("stem"):
        # a trunk from the ground to the raised root point
        top = limbs[0]["P"][0]
        r0 = float(limbs[0]["R"][:3].max()) * spec.get("stem", 1.0)
        base = np.array([top[0], top[1], 0.0])
        P = np.array([base + (top - base) * k / 4 for k in range(5)])
        tube(geo, P, np.linspace(r0 * 1.3, r0, 5), sides=10, col=bark * 0.85, cap_base=True, vrep=BARK_REP)
    for bl in spec.get("bulbs", []):
        (col, row), y, R = bl
        C = np.array([(col + 0.5 - spr.hx) / 16, y, (spr.hy - row - 0.5 - 16 * y) / 8])
        ellipsoid(geo, C, R, seg=16, rings=10, col=bark * spec.get("bulb_shade", 0.9), normals_from=C, jitter=0.06,
                  rng=rng, rep=BARK_REP)
    twigs, brush = Geo(), Geo()
    for tw in spec.get("twigs", []):
        if tw.get("seat") and top is not None:
            # seated in the top of the body, so no gap shows from any side
            tw = dict(tw, P=top - np.array([0, 0, tw.get("sink", 0.25)]))
        twig_spray(twigs, spr, tw, bark, rng, brush)
    mat = material("bark", image("bark", tex), repeat=True, rough=spec.get("rough", 0.9))
    parts = [geo.to_object(spr.name + "_limbs", mat)]
    if body.f:
        # the body's own fibre bark: long streaks along it
        # the ridges carry the dark and light, the texture only fine streaks
        btex = tex_bark("streak", 9)
        parts.append(body.to_object(spr.name + "_body", material("body", image("fibre", btex), repeat=True,
                                                                rough=spec.get("rough", 0.9))))
    if twigs.f:
        # plain colour: the bark texture would darken the pale twigs
        parts.append(twigs.to_object(spr.name + "_twigs", material("twigs", rough=0.9)))
    if brush.f:
        parts.append(brush.to_object(spr.name + "_brush", material("brush", image("brush", tex_brush()), cut=True,
                                                                  cull=False)))
    return parts, {"limbs": len(limbs), "tris": geo.tris() + body.tris() + twigs.tris() + brush.tris()}


def twig_spray(geo, spr, tw, bark, rng, brush=None):
    """Fine twigs fanning up and out from a point: the brush crowns of the
    stilt trees. tw: {"at": (col, row), "y": depth, "n": count, "len": cells,
    "cards": crossed pairs of branching twig cards added to brush, for the
    fine fractal ends a tube cannot afford}."""
    if "P" in tw:
        C = np.asarray(tw["P"], np.float64)
    else:
        col, row = tw["at"]
        y = tw.get("y", 0.0)
        C = np.array([(col + 0.5 - spr.hx) / 16, y, (spr.hy - row - 0.5 - 16 * y) / 8])
    L0 = tw.get("len", 1.2)
    pale = lin(tw["rgb"], 1.2) if "rgb" in tw else bark
    # the tuft's twigs run from the drawing's dark ones to its pale ones,
    # most of them dark as drawn
    lo, t0 = (lin(tw["dark"]), 0.0) if "dark" in tw else (bark, 0.3)
    if "dark" in tw:
        pale = lin(tw["rgb"])
    for k in range(tw.get("n", 24)):
        d = _unit(rng.normal(0, 1, 3) + np.array([0, 0, tw.get("up", 1.0)]))
        L = L0 * min(rng.uniform(0.5, 1.0), tw.get("trim", 1.0))
        bend = _unit(rng.normal(0, 1, 3)) * 0.25
        P = np.array([C + (d + bend * t * t) * L * t for t in np.linspace(0, 1, 4)])
        tube(geo, P, np.array([0.05, 0.04, 0.03, 0.015]) * tw.get("r", 1.0), sides=3, col=mixc(lo, pale, rng.uniform(t0, 1.0) ** (2 if "dark" in tw else 1)),
             vrep=BARK_REP)
        # a fork or two near the end
        for j in range(2):
            t0 = rng.uniform(0.4, 0.8)
            p0 = C + (d + bend * t0 * t0) * L * t0
            d2 = _unit(d + rng.normal(0, 0.7, 3))
            p1 = p0 + d2 * L * rng.uniform(0.2, 0.4)
            tube(geo, np.array([p0, (p0 + p1) / 2, p1]), np.array([0.03, 0.022, 0.012]) * tw.get("r", 1.0), sides=3,
                 col=mixc(lo, pale, rng.uniform(t0, 1.0) ** (2 if "dark" in tw else 1)), vrep=BARK_REP)
    if brush is None:
        return
    for k in range(tw.get("cards", 0)):
        d = _unit(rng.normal(0, 1, 3) + np.array([0, 0, tw.get("up", 1.0) * 0.8]))
        L = L0 * min(rng.uniform(0.55, 1.05), tw.get("trim", 1.05))
        side = _unit(np.cross(d, rng.normal(0, 1, 3)))
        base = C + d * rng.uniform(0.0, 0.25) * L0
        col = mixc(lo, pale, rng.uniform(max(t0, 0.2), 1.0) ** (2 if "dark" in tw else 1)) * rng.uniform(0.85, 1.1)
        for turn in (0.0, math.pi / 2):
            nrm = _rot(side, d, turn)
            card(brush, base, nrm, d, L * 0.8, L, col=col, N=_unit(nrm * 0.3 + d * 0.3 + np.array([0, 0, 1.0])),
                 anchor=0.0)


def _fib_dirs(n, rng):
    """n roughly even unit directions, randomly turned."""
    i = np.arange(n) + 0.5
    phi = np.arccos(1 - 2 * i / n)
    th = math.pi * (1 + 5 ** 0.5) * i + rng.uniform(0, TAU)
    d = np.stack([np.cos(th) * np.sin(phi), np.sin(th) * np.sin(phi), np.cos(phi)], 1)
    return d + rng.normal(0, 0.12, d.shape)


def _unit(v):
    v = np.asarray(v, np.float64)
    return v / (np.linalg.norm(v, axis=-1, keepdims=True) + 1e-12)


def palette(spr, sel, gain):
    """Dark, mid and light colours of the pixels in sel, linear."""
    return (lin(spr.colour(sel, 0.03, 0.3), gain), lin(spr.colour(sel, 0.3, 0.7), gain),
            lin(spr.colour(sel, 0.7, 0.98), gain))


def trunk_run(spr, row, near):
    """The drawn run of pixels on row nearest column near, (first, last)."""
    r = int(row)
    if not (0 <= r < spr.h and spr.mask[r].any()):
        return None
    xs = np.nonzero(spr.mask[r])[0]
    c = xs[np.argmin(np.abs(xs - near))]
    a = b = c
    while a > 0 and spr.mask[r, a - 1]:
        a -= 1
    while b < spr.w - 1 and spr.mask[r, b + 1]:
        b += 1
    return a, b


CAM_D = np.array([0.0, 1.0, -2.0]) / math.sqrt(5.0)  # the classic camera looks along this
TO_CAM = -CAM_D


def ray_hits(lobes, X, S):
    """Where the classic camera's ray through each screen point first meets
    a lobe. X is the point's x in cells, S its (hy - row) / 16, so the ray
    is (X, t, 2S - 2t). Returns t (inf for a miss) and the lobe index."""
    X, S = np.asarray(X, np.float64), np.asarray(S, np.float64)
    best = np.full(X.shape, np.inf)
    idx = np.full(X.shape, -1)
    for i, (C, R) in enumerate(lobes):
        A = 1 / R[1] ** 2 + 4 / R[2] ** 2
        w = 2 * S - C[2]
        B = -2 * C[1] / R[1] ** 2 - 4 * w / R[2] ** 2
        K = ((X - C[0]) / R[0]) ** 2 + (C[1] / R[1]) ** 2 + (w / R[2]) ** 2 - 1
        disc = B * B - 4 * A * K
        ok = disc >= 0
        t = np.where(ok, (-B - np.sqrt(np.maximum(disc, 0))) / (2 * A), np.inf)
        better = t < best
        best = np.where(better, t, best)
        idx = np.where(better, i, idx)
    return best, idx


def crown_lobes(spr, crown, spec):
    """Lumps filling the drawn crown: one ellipsoid per packed circle, set
    back on the crown's envelope so the whole reads as one rounded mass.
    With 'clumps' the crown is that many big overlapping clumps instead,
    each grown to take in the drawn pixels nearest it."""
    hx, hy = spr.hx, spr.hy
    if spec.get("profile", {}).get("flame"):
        return flame_lobes(spr, crown, spec)
    dt = s2.edt(crown)
    if spec.get("profile"):
        circles = column_profile(spr, crown, dt, spec)
    elif spec.get("column"):
        # a spire: one lump every few pixels up the axis, as wide as the
        # drawing's inscribed circle there, all standing on one upright axis
        circles = []
        ys, xs = np.nonzero(crown)
        for row in np.arange(ys.min() + 2, ys.max() - 1, spec.get("column_step", 3.0)):
            r = int(row)
            span = spr.row_span(r, crown)
            if span is None:
                continue
            c0 = (span[0] + span[1]) / 2.0
            lo, hi = max(0, int(c0) - 3), min(spr.w, int(c0) + 4)
            j = lo + int(np.argmax(dt[r, lo:hi]))
            R = float(dt[r, j])
            if R >= 2.0:
                circles.append((float(j), float(r), R))
    elif spec.get("clumps"):
        # the drawn crown split into k patches of about equal size, each
        # patch one clump just big enough to take it in
        ys, xs = np.nonzero(crown)
        X = np.stack([xs, ys], 1).astype(np.float64)
        C, lab = kmeans(X, spec["clumps"], np.random.default_rng(7), it=20)
        circles = []
        for k in range(len(C)):
            mine = X[lab == k]
            if len(mine) < 8:
                continue
            R = float(np.quantile(np.hypot(*(mine - C[k]).T), 0.85)) * spec.get("clump_scale", 1.0)
            circles.append((float(C[k][0]), float(C[k][1]), R))
    else:
        circles = s2.pack_circles(crown, dt, min_r=spec.get("min_r", 3.0), cover=0.99, max_n=90)
    ys, xs = np.nonzero(crown)
    left, right, top, bottom = xs.min(), xs.max(), ys.min(), ys.max()
    a = (right - left + 1) / 32.0
    xc = ((left + right + 1) / 2 - hx) / 16.0
    Hh = (bottom - top + 1) / 2.0
    zc = (hy - (top + bottom + 1) / 2) / 8.0
    b = min(a, 0.8 * Hh / 16) * spec.get("depth", 1.0)
    c = math.sqrt(max(Hh ** 2 - (16 * b) ** 2, 16.0)) / 8
    env = (np.array([xc, 0.0, zc]), np.array([a, b, c]))
    shrink = spec.get("card", 0.8) * spec.get("shrink", 0.25)
    lobes = []
    fill = 0.0 if spec.get("column") else spec.get("fill", 0.5)
    A = 1 / (b * b) + 4 / (c * c)
    floor = spec.get("profile", {}).get("floor")
    for col, row, R in circles:
        x = (col + 0.5 - hx) / 16
        z0 = (hy - row - 0.5) / 8
        y = 0.0 if spec.get("column") else 2 * (z0 - zc) * b * b / (c * c + 4 * b * b)
        rx = max(R / 16 - shrink, R / 16 * 0.5)
        if floor:
            # the crown's underside rounds off just above the floor
            rx = max(min(rx, (z0 - 0.85 * floor / SHAPE["height"]) / 0.9), 0.12)
        Rl = np.array([rx, rx * spec.get("lobe_depth", 0.95), rx * 0.9])
        # the lumps on one ray's midpoints make a slab tilted along the
        # camera's rays; each is split along its own ray into one before and
        # one behind, which the classic view cannot tell apart, so the crown
        # fills its envelope's depth and is round from the side
        K = ((x - xc) / a) ** 2 + ((z0 - zc) / c) ** 2 - 1 - A * y * y
        hw = math.sqrt(max(-K / A, 0.0))
        if fill and hw > 0.5 * rx:
            # a middle one too where the chord is long, so no waist shows
            for sg in ((-1.0, 0.0, 1.0) if fill * hw > 0.9 * rx else (-1.0, 1.0)):
                yy = y + sg * fill * hw
                lobes.append((np.array([x, yy, z0 - 2 * yy]), Rl))
        else:
            lobes.append((np.array([x, y, z0 - 2 * y]), Rl))
    if spec.get("lift_low"):
        # lumps hanging near the ground slide up their camera rays, so the
        # trunk shows under the crown and the classic view is unchanged
        zmax, dz = spec["lift_low"]
        lobes = [(C + np.array([0.0, -dz / 2, dz]) if C[2] - R[2] < zmax else C, R) for C, R in lobes]
    return lobes, env


def column_profile(spr, crown, dt, spec):
    """A spire's lumps up its drawn axis, (col, row, radius px). The drawn
    inscribed radius, its top third held to a cone closing to a point (no
    bare leader, no waist) and, with floor, the lower crown kept full down
    to floor cells above the ground, where the trunk takes over."""
    pr = spec["profile"]
    ys, xs = np.nonzero(crown)
    top, bot = int(ys.min()), int(ys.max())
    floor = pr.get("floor")
    last = spr.hy - 8 * floor / SHAPE["height"] if floor else bot - 1
    rows = np.arange(top + 1, last, spec.get("column_step", 3.0))
    cols, Rs = [], []
    for row in rows:
        r = int(row)
        span = spr.row_span(r, crown)
        if span is None:
            cols.append(np.nan)
            Rs.append(0.0)
            continue
        c0 = (span[0] + span[1]) / 2.0
        lo, hi = max(0, int(c0) - 3), min(spr.w, int(c0) + 4)
        j = lo + int(np.argmax(dt[r, lo:hi]))
        cols.append(float(j))
        Rs.append(float(dt[r, j]))
    cols = np.array(cols)
    ok = ~np.isnan(cols)
    cols = np.interp(np.arange(len(cols)), np.nonzero(ok)[0], cols[ok])
    cols = np.convolve(np.pad(cols, 2, mode="edge"), np.ones(5) / 5, mode="valid")
    Rs = np.convolve(np.pad(np.array(Rs), 1, mode="edge"), np.ones(3) / 3, mode="valid")
    Rmax = float(Rs.max())
    u = (rows - top) / max(float(rows[-1] - top), 1.0)
    tf = pr.get("taper", 1 / 3.0)
    cone = Rmax * np.clip(u / tf, 0.0, 1.0) ** pr.get("power", 1.8)
    R = np.where(u < tf, np.clip(Rs, pr.get("min", 0.6) * cone, cone), Rs)
    if floor:
        k = int(np.argmax(Rs))
        R[k:] = np.maximum(R[k:], Rmax * pr.get("fill", 0.8))
    R = np.maximum(R, pr.get("tip", 2.0))
    return [(float(c), float(r), float(q)) for c, r, q in zip(cols, rows, R)]


def flame_lobes(spr, crown, spec):
    """A spire's crown as a flame: lumps up the drawn axis, widest 'widest'
    of the way up and tapering straight to the tip, deeper than wide below
    a shallow top third. Its bottom lands on the drawn crown's bottom row
    once the sturdiness pass has shortened it, and the lowest lumps pull
    back from the front so the drawn trunk shows under them in classic."""
    pr = spec["profile"]
    g, h = SHAPE["girth"], SHAPE["height"]
    hx, hy = spr.hx, spr.hy
    ys, xs = np.nonzero(crown)
    top, cb = int(ys.min()), int(ys.max())
    W = float(np.quantile(crown.sum(1)[top:cb + 1], 0.9))
    rows = np.arange(top, cb + 1)
    cols = np.array([xs[ys == r].mean() if (ys == r).any() else np.nan for r in rows])
    ok = ~np.isnan(cols)
    cols = np.interp(np.arange(len(cols)), np.nonzero(ok)[0], cols[ok])
    cols = np.convolve(np.pad(cols, 8, mode="edge"), np.ones(17) / 17, mode="valid")
    if pr.get("straight"):
        # one straight axis from the crown's foot to the drawn tip
        cols = np.linspace(cols[0], cols[-1], len(cols))
    zt, zb = (hy - top) / 8.0, (hy - cb) / (8.0 * h)
    Rw = (0.5 * W * pr.get("width", 1.1) - pr.get("overhang", 4.0)) / (16.0 * g)
    uw = 1 - pr.get("widest", 0.4)
    D, Dt = pr.get("depth", 1.3), pr.get("top_depth", 0.8)
    floor = pr.get("floor", 0.0) / h
    limit = cb - pr.get("show", 0)

    def radius(u):
        return Rw * (u / uw if u < uw else 1 - pr.get("round", 0.3) * ((u - uw) / (1 - uw)) ** pr.get("round_p", 2.0))

    z_lo = zb + pr.get("sink", 0.5) * 0.9 * radius(1.0)
    lobes = []
    for z0 in np.arange(zt - 0.1, z_lo - 1e-6, -spec.get("column_step", 3.0) / 8.0):
        u = (zt - z0) / (zt - zb)
        rx = max(radius(u), pr.get("tip", 2.0) / 16.0)
        ry = rx * (Dt + (D - Dt) * float(np.clip((u - 0.2) / 0.3, 0, 1)))
        rz = rx * 0.9
        if floor:
            rz = min(rz, max(z0 - floor, 0.3 * rx))
        x = (float(np.interp(top + u * (cb - top), rows, cols)) + 0.5 - hx) / 16.0
        Y = 0.0
        # the front pulled back, the back kept, till the classic bottom clears
        while hy - 16 * g * Y - 8 * h * z0 + math.hypot(16 * g * ry, 8 * h * rz) > limit and ry > 0.3 * rx:
            Y += 0.02
            ry -= 0.02
        lobes.append((np.array([x, Y, z0]), np.array([rx, ry, rz])))
    Cs = np.array([C for C, R in lobes])
    Rs = np.array([R for C, R in lobes])
    lo, hi = (Cs - Rs).min(0), (Cs + Rs).max(0)
    return lobes, ((lo + hi) / 2, (hi - lo) / 2)


def accent_colour(spr, sel, name, gain):
    """A leaf colour the drawing flecks its crown with: 'orange' or 'cream'."""
    r, g = spr.rgb[..., 0], spr.rgb[..., 1]
    lum = spr.lum
    ls = lum[sel & spr.mask]
    if name == "orange":
        m = sel & (lum > np.quantile(ls, 0.6)) & (r > 1.3 * g)
    else:
        m = sel & (lum > np.quantile(ls, 0.9)) & (r < 1.3 * g)
    if m.sum() < 3:
        m = sel & (lum > np.quantile(ls, 0.9))
    return lin(spr.rgb[m].mean(0), gain)


def leaf_colour(pal, rng, up, ao, accents=()):
    """A leaf's colour: lighter where it faces the sky, darker inside, now
    and then one of the accents (colour, chance)."""
    dark, mid, light = pal
    t = float(np.clip(0.5 + 0.3 * up + rng.normal(0, 0.2), 0, 1))
    base = mixc(dark, mid, t * 2) if t < 0.5 else mixc(mid, light, t * 2 - 1)
    for acc, p in accents:
        if rng.random() < p * (0.6 + 0.4 * max(0.0, up)):
            base = acc * rng.uniform(0.85, 1.1)
            break
    return base * ao * rng.uniform(0.9, 1.1)


def _ray_lobes(lobes, O, D):
    """Entry and exit distances of the rays O + tD through each lobe, (n, k)
    arrays with inf and -inf where a ray misses."""
    O, D = np.asarray(O, np.float64), np.asarray(D, np.float64)
    tin = np.full((len(O), len(lobes)), np.inf)
    tout = np.full((len(O), len(lobes)), -np.inf)
    for j, (C, R) in enumerate(lobes):
        o, d = (O - C) / R, D / R
        a = (d * d).sum(1)
        b = 2 * (o * d).sum(1)
        c = (o * o).sum(1) - 1
        disc = b * b - 4 * a * c
        ok = disc > 0
        sq = np.sqrt(np.maximum(disc, 0))
        tin[ok, j] = ((-b - sq) / (2 * a))[ok]
        tout[ok, j] = ((-b + sq) / (2 * a))[ok]
    return tin, tout


def crown_surface(lobes, spacing, rng, axis, under=1.6):
    """Points spread evenly over the outside of the lobes' union, all round:
    rings of level rays out from the crown's axis, a grid of rays down onto
    the top and a sparser one up onto the underside. (point, normal, lobe)."""
    Cs = np.array([C for C, R in lobes])
    Rs = np.array([R for C, R in lobes])
    lo, hi = (Cs - Rs).min(0), (Cs + Rs).max(0)
    ax = np.asarray(axis, np.float64)
    O, D, kind = [], [], []
    for z in np.arange(lo[2] + 0.4 * spacing, hi[2], 0.75 * spacing):
        dz = np.clip((z - Cs[:, 2]) / Rs[:, 2], -1, 1)
        reach = float((np.hypot(Cs[:, 0] - ax[0], Cs[:, 1] - ax[1]) + Rs[:, :2].max(1) * np.sqrt(1 - dz * dz)).max())
        m = max(6, int(math.ceil(TAU * reach / spacing)))
        ph0 = rng.uniform(0, TAU)
        for k in range(m):
            ph = ph0 + TAU * (k + rng.uniform(-0.3, 0.3)) / m
            O.append((ax[0], ax[1], z + rng.uniform(-0.3, 0.3) * spacing))
            D.append((math.cos(ph), math.sin(ph), 0.0))
            kind.append(0)
    for sgn, sp in ((-1.0, spacing), (1.0, spacing * under)):
        for x in np.arange(lo[0] + 0.3 * sp, hi[0], sp):
            for y in np.arange(lo[1] + 0.3 * sp, hi[1], sp):
                O.append((x + rng.uniform(-0.35, 0.35) * sp, y + rng.uniform(-0.35, 0.35) * sp,
                          hi[2] + 1 if sgn < 0 else lo[2] - 1))
                D.append((0.0, 0.0, sgn))
                kind.append(1 if sgn < 0 else 2)
    O, D = np.array(O), np.array(D)
    tin, tout = _ray_lobes(lobes, O, D)
    out = []
    for i in range(len(O)):
        if kind[i] == 0:
            ok = tout[i] > 0
            if not ok.any():
                continue
            j = int(np.argmax(np.where(ok, tout[i], -np.inf)))
            t = tout[i, j]
        else:
            if not np.isfinite(tin[i]).any():
                continue
            j = int(np.argmin(tin[i]))
            t = tin[i, j]
        p = O[i] + D[i] * t
        C, R = lobes[j]
        n = _unit((p - C) / R ** 2)
        if (kind[i] == 1 and n[2] < 0.4) or (kind[i] == 2 and n[2] > -0.4):
            continue
        out.append((p, n, j))
    return out


def leaf_atlas(kind, edge="sprig", seed=5, size=128):
    """The crown's leaf texture beside its edge spray, with a tile of
    scattered dots under a square leaf for flecks: (texture, main uv rect,
    spray uv rect, dots uv rect)."""
    main = tex_leaves(kind, seed, size)
    spray = tex_leaves(edge, seed + 1, size)
    a = np.zeros((2 * size, 2 * size, 4))
    a[:main.shape[0], :size] = main
    a[:spray.shape[0], size:] = spray
    dots = None
    if main.shape[0] == size:
        a[size:, :size] = tex_dots(seed + 2, size)
        dots = (0.0, 0.0, 0.5, 0.5)
    return (a, (0.0, 1.0 - main.shape[0] / (2.0 * size), 0.5, 1.0), (0.5, 1.0 - spray.shape[0] / (2.0 * size), 1.0, 1.0),
            dots)


def tex_dots(seed, size=128, n=8):
    """A few small bright specks, for flecking a crown with light."""
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    a = np.zeros((size, size, 4))
    for _ in range(n):
        cx, cy = rng.uniform(12, size - 12, 2)
        r = rng.uniform(4.0, 7.0)
        m = ((xx - cx) / r) ** 2 + ((yy - cy) / (r * rng.uniform(0.6, 1.0))) ** 2 < 1
        a[m, :3] = rng.uniform(0.85, 1.0)
        a[m, 3] = 1.0
    return a


STYLE = {"poplar": "sprig", "sprig": "sprig", "maple": "shingle"}


def build_crown(spr, spec, rng):
    """A leafy tree: trunk and limbs under a crown of lumps. Leaf cards
    stand on the lumps' outer surface all round, plus a front layer along
    the classic camera's rays through the drawn crown so that view is
    covered, over a core in a leaf shade. Styles: 'sprig' upright sprays
    (poplars), 'cluster' leaf bunches facing out, 'shingle' big leaves
    hanging in overlapping tiers. Edge sprays jut from the rim the classic
    view sees, and 'crevice' darkens leaves where two clumps meet."""
    hx, hy = spr.hx, spr.hy
    cb = spec.get("crown_bottom", spr.bottom)
    crown = spr.mask.copy()
    crown[cb + 1:] = False
    crown = s2.close(crown, spec.get("close", 2))
    lobes, (Cenv, Renv) = crown_lobes(spr, crown, spec)
    xc, zc, c = Cenv[0], Cenv[2], Renv[2]
    cs = spec.get("card", 0.8)
    gain = spec.get("gain", 1.25)
    sel = crown & spr.mask
    dark, mid, light = palette(spr, sel, gain)
    pal = (dark * spec.get("dark_gain", 1.0), mid * spec.get("mid_gain", 1.0), light * spec.get("light_gain", 1.0))
    accents = []
    if spec.get("accent") == "auto":
        # the drawing's brightest leaves, as speckles
        accents.append((lin(spr.colour(sel, 0.9, 1.0), gain), spec.get("p_accent", 0.25)))
    for name, p in spec.get("accents", []):
        accents.append((accent_colour(spr, sel, name, gain * spec.get("accent_gain", 1.0)), p))
    style = spec.get("style", STYLE.get(spec.get("leaf"), "cluster"))
    up_bias = spec.get("up_bias", 0.5)
    tilt = spec.get("tilt_noise", 0.4)
    face_cam = spec.get("face_cam", 0.6)
    crevice = spec.get("crevice", 0.0)
    # flecks: tiny cards of the drawing's brightest leaf colours strewn
    # over the crown, its fine speckle, (colour, chance per shell card)
    flecks = []
    for name, p in spec.get("flecks", []):
        acc = lin(spr.colour(sel, 0.9, 1.0), gain) if name == "bright" else accent_colour(spr, sel, name, gain)
        flecks.append((acc * spec.get("fleck_gain", 1.0), p))
    tex, MAIN, SPRAY, DOTS = leaf_atlas(spec.get("leaf", "cluster"), spec.get("edge", "sprig"), 5)
    if style == "shingle":
        MAIN = (MAIN[0], MAIN[3], MAIN[2], MAIN[1])  # the stalk at the top edge
    leaves, core = Geo(), Geo()
    grown = s2._grow(s2._grow(s2._grow(crown)))
    rx_top = max(L[1][0] for L in lobes)

    def fit(j):
        # a spire's cards shrink with its lumps, so the top closes to a point
        if not spec.get("profile"):
            return 1.0
        return float(np.clip(lobes[j][1][0] / (0.45 * rx_top), spec["profile"].get("fit_min", 0.7), 1.0))

    def shade(p, n, j):
        q = float((((p - Cenv) / Renv) ** 2).sum())
        ao = float(np.clip(0.7 + 0.3 * math.sqrt(min(q, 1.0)), 0.6, 1.0))
        ao *= 0.85 + 0.15 * float(np.clip((p[2] - (zc - c)) / (2 * c), 0, 1))
        if crevice and len(lobes) > 1:
            qo = min(math.sqrt(float((((p - C2) / R2) ** 2).sum())) for k, (C2, R2) in enumerate(lobes) if k != j)
            ao *= 1 - crevice * (1 - float(np.clip((qo - 1) / spec.get("crevice_band", 0.35), 0, 1)))
        en = _unit((p - Cenv) / Renv ** 2)
        return leaf_colour(pal, rng, float(n[2]) if crevice else float(en[2]), ao, accents)

    def put(p, n, j, size, fc):
        en = _unit((p - Cenv) / Renv ** 2)
        Nsh = _unit(0.5 * en + 0.5 * n + np.array([0, 0, spec.get("flat_light", 0.25)]))
        col = shade(p, n, j)
        if style == "shingle":
            h = np.array([n[0], n[1], 0.0])
            if np.linalg.norm(h) < 0.25:
                h = np.array([p[0] - xc, p[1] - Cenv[1], 0.0]) + rng.normal(0, 0.3, 3) * np.array([1, 1, 0])
            h = _unit(h)
            a = math.radians(float(np.clip(spec.get("droop", 40) + 45 * max(float(n[2]), 0.0) + rng.normal(0, 10), 10, 85)))
            fn = h * math.cos(a) + np.array([0.0, 0.0, math.sin(a)])
            fn = _unit(fn + TO_CAM * fc * 0.5)
            # hanging: the card's down edge the steepest way down its plane
            down = _rot(_unit(np.array([0.0, 0.0, -1.0]) + fn * fn[2]), fn, rng.normal(0, 0.35))
            card(leaves, p, fn, -down, size, size, uvrect=MAIN, col=col, N=_unit(Nsh + fn), anchor=0.85)
        elif style == "sprig":
            fn = _unit(n + TO_CAM * fc + np.array([0, 0, up_bias]) + rng.normal(0, tilt, 3))
            upv = _unit(np.array([0, 0, 1.0]) + np.array([n[0], n[1], 0]) * 0.6 + rng.normal(0, 0.2, 3))
            card(leaves, p - upv * size * 0.35, fn, upv, size * 0.6, size * 1.2, uvrect=MAIN, col=col, N=Nsh, anchor=0.0)
        else:
            fn = _unit(n + TO_CAM * fc + np.array([0, 0, up_bias]) + rng.normal(0, tilt, 3))
            card(leaves, p, fn, _unit(rng.normal(0, 1, 3)), size, size, uvrect=MAIN, col=col, N=Nsh)

    def spray(p, n, j, size):
        # jutting out of the rim, turned toward the classic camera
        out = _unit(n + rng.normal(0, 0.25, 3) + np.array([0, 0, 0.15]))
        fn = _unit(TO_CAM - out * float(np.dot(TO_CAM, out)) + rng.normal(0, 0.3, 3))
        card(leaves, p - out * size * 0.25, fn, out, size * 0.6, size * 1.2, uvrect=SPRAY, col=shade(p, n, j),
             N=_unit(n + np.array([0, 0, 0.4])), anchor=0.0)

    # front layer: a jittered grid over the drawn crown, each point's card
    # where the camera's ray meets the lumps (or beside the nearest lump)
    step = 16 * cs * spec.get("front_step", 0.5)
    ys, xs = np.nonzero(crown)
    gy = np.arange(ys.min() + step * 0.3, ys.max() + 1, step)
    gx = np.arange(xs.min() + step * 0.3, xs.max() + 1, step)
    pts = [(x + rng.uniform(-0.4, 0.4) * step, y + rng.uniform(-0.4, 0.4) * step) for y in gy for x in gx]
    pts = [(x, y) for x, y in pts if spr.inside(x, y, crown)]
    n_front = 0
    if pts:
        P = np.array(pts)
        X = (P[:, 0] - hx) / 16
        S = (hy - P[:, 1]) / 16
        T, I = ray_hits(lobes, X, S)
        for (x_, s_, t, i) in zip(X, S, T, I):
            if np.isfinite(t):
                C, R = lobes[i]
                p = np.array([x_, t, 2 * s_ - 2 * t])
            elif spec.get("profile", {}).get("flame"):
                continue  # the flame, not the drawn outline, is the silhouette
            else:
                # the nearest lump on screen; the ray's point closest to it
                d2 = [(16 * (C[0] - x_)) ** 2 + (16 * C[1] + 8 * C[2] - 16 * s_) ** 2 for C, R in lobes]
                i = int(np.argmin(d2))
                C, R = lobes[i]
                t = C[1] + (2 * s_ - C[2] - 2 * C[1]) * 0.4
                p = np.array([x_, t, 2 * s_ - 2 * t])
            ln = _unit((p - C) / R ** 2)
            put(p + ln * 0.04, ln, int(i), cs * rng.uniform(0.85, 1.2) * fit(int(i)), face_cam)
            n_front += 1
    # the shell, all round
    n_shell = n_spray = n_fleck = 0
    for p, n, j in crown_surface(lobes, cs * spec.get("spacing", 0.6), rng, (xc, Cenv[1])):
        if not spec.get("profile") and not spr.inside(hx + 16 * p[0], hy - 16 * p[1] - 8 * p[2], grown):
            continue
        put(p + n * 0.02, n, j, cs * rng.uniform(0.85, 1.2) * fit(j), 0.0)
        n_shell += 1
        for acc, pf in flecks:
            for _ in range(rng.poisson(pf)):
                q = p + n * 0.05 + rng.normal(0, 0.12, 3)
                lit = 0.75 + 0.25 * float(np.clip(n[2] + 0.5, 0, 1))
                fs = cs * spec.get("fleck_size", 0.9) * rng.uniform(0.7, 1.2)
                card(leaves, q, _unit(n + TO_CAM + rng.normal(0, 0.3, 3)), _unit(rng.normal(0, 1, 3)), fs, fs,
                     uvrect=DOTS or MAIN, col=acc * lit * rng.uniform(0.85, 1.1), N=_unit(n + np.array([0, 0, 1.0])))
                n_fleck += 1
        if spec.get("sprays") and abs(float(np.dot(n, TO_CAM))) < 0.4 and rng.random() < spec["sprays"]:
            spray(p, n, j, cs * spec.get("spray_size", 1.0) * rng.uniform(0.8, 1.2) * fit(j))
            n_spray += 1
    if spec.get("profile"):
        # a spire's thin top: cards all round each small lump, which the
        # level rays from the crown's axis mostly miss
        for j, (C, R) in enumerate(lobes):
            if R[0] > 0.5 * rx_top:
                continue
            for _ in range(max(8, int(TAU * R[0] / (cs * spec.get("spacing", 0.6)) * 4))):
                a, e = rng.uniform(0, TAU), rng.uniform(-0.3, 0.9)
                n = np.array([math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)])
                put(C + n * R + n * 0.02, n, j, cs * rng.uniform(0.85, 1.2) * fit(j), 0.0)
                n_shell += 1
        # the leader: upright sprigs crossed on the top lump
        C, R = min(lobes, key=lambda L: -L[0][2])
        lw, lh = spec["profile"].get("leader", (0.6, 1.5))
        for k in range(3):
            a = math.pi * k / 3 + rng.uniform(-0.2, 0.2)
            card(leaves, C - np.array([0, 0, 0.3]), np.array([math.cos(a), math.sin(a), 0.0]), np.array([0, 0, 1.0]),
                 cs * lw, cs * lh, uvrect=MAIN, col=pal[1] * 0.8, N=_unit(np.array([0, -0.3, 1.0])), anchor=0.0)
    # the core, in a leaf shade
    pool = lobes[::2] if spec.get("profile") else lobes
    big = sorted(pool, key=lambda L: -L[1][0])[:spec.get("cores", 12)]
    core_col = {"dark": pal[0] * 0.55, "mid": mixc(pal[0], pal[1], 0.7)}.get(spec.get("core"), pal[1] * spec.get("core_light", 0.8))
    for C, R in big:
        ellipsoid(core, C, R * spec.get("core_size", 0.85), seg=8, rings=5, col=core_col, normals_from=Cenv)
    if spec.get("profile"):
        # a spire's thin top: one dark cone up through its small lumps
        tip = sorted([L for L in lobes if L[1][0] < 0.6 * rx_top], key=lambda L: L[0][2])[:-2]
        if len(tip) > 1:
            P = np.array([C for C, R in tip])
            Rt = np.array([R[0] for C, R in tip]) * 0.6 / SHAPE["radius"]
            tube(core, P, Rt, sides=8, col=core_col, cap_tip=True)
    wood, btex = crown_wood(spr, spec, rng, cb, Cenv, Renv, big)
    leaf_mat = material("leaves", image("leaves", tex), cut=True, cull=False)
    parts = [leaves.to_object(spr.name + "_leaves", leaf_mat),
             core.to_object(spr.name + "_core", material("core")),
             wood.to_object(spr.name + "_wood", material("bark", image("bark", btex), repeat=True))]
    return parts, {"lobes": len(lobes), "front": n_front, "shell": n_shell, "sprays": n_spray, "flecks": n_fleck,
                   "leaf_tris": leaves.tris(),
                   "core_tris": core.tris(), "wood_tris": wood.tris()}


def flared_trunk(geo, base, tip, r0, r1, flare, nl, rng, col, sides=12, rings=9, hf=None, dark=0.8, phases=None):
    """A trunk from base to tip, radius r0 to r1, its foot swelling into nl
    buttress roots that spread flare times the radius at the ground, at
    the given phases (radians) or at random."""
    L = float(np.linalg.norm(tip - base))
    r0, r1 = r0 * SHAPE["radius"], r1 * SHAPE["radius"]
    hf = hf or 2.2 * r0
    ts = np.linspace(0, 1, rings) ** 1.8
    if phases is None:
        ph = rng.uniform(0, TAU) + TAU * np.arange(nl) / nl + rng.uniform(-0.3, 0.3, nl)
        amp = rng.uniform(0.7, 1.3, nl)
    else:
        ph = np.asarray(phases, np.float64)
        nl = len(ph)
        amp = np.full(nl, 1.0)
    urep = max(1, round(TAU * r0 * BARK_REP * 2))
    V, UV, CC = [], [], []
    for i, t in enumerate(ts):
        P = base + (tip - base) * t
        if i:
            P = P + np.array([0.08 * math.sin(i * 1.3), 0.06 * math.cos(i), 0]) * r0
        else:
            P = P - np.array([0, 0, 0.06])
        z = t * L
        r = r0 + (r1 - r0) * t
        F = flare * math.exp(-z / hf)
        for k in range(sides + 1):
            a = TAU * (k % sides) / sides
            d = (a - ph + math.pi) % TAU - math.pi
            lob = float((amp * np.exp(-(d / 0.4) ** 2)).max())
            rr = r * (1 + F * lob)
            V.append(P + rr * np.array([math.cos(a), math.sin(a), 0.0]))
            UV.append((urep * k / sides, z * BARK_REP))
            CC.append(col * (dark + (1 - dark) * min(1.0, z / 2.0)))
    m = sides + 1
    F_ = []
    for i in range(rings - 1):
        for k in range(sides):
            a0, b0 = i * m + k, i * m + k + 1
            F_.append((a0, b0, b0 + m, a0 + m))
    top = len(V)
    V.append(tip + (tip - base) / max(L, 1e-6) * r1)
    UV.append((0.5, L * BARK_REP))
    CC.append(CC[-1])
    for k in range(sides):
        F_.append(((rings - 1) * m + k, (rings - 1) * m + k + 1, top))
    geo.add(np.array(V), F_, np.array(UV), np.array(CC))


def crown_wood(spr, spec, rng, cb, Cenv, Renv, big):
    """Trunk with a flared foot, limbs into the biggest lumps, any drawn stems."""
    hx, hy = spr.hx, spr.hy
    xc, zc, c = Cenv[0], Cenv[2], Renv[2]
    tb = spec.get("trunk", {})
    trunk_sel = spr.mask.copy()
    trunk_sel[:cb + 1] = False
    if trunk_sel.sum() < 6:
        # the foot is drawn under the crown: the pixels round the anchor
        trunk_sel = np.zeros_like(spr.mask)
        trunk_sel[max(0, hy - 14):, max(0, hx - 14):hx + 15] = True
        trunk_sel &= spr.mask
    tex, barkc = bark_from(spr, trunk_sel, tb.get("style", "streak"), gain=tb.get("gain", 1.2))
    if "rgb" in tb:
        barkc = lin(tb["rgb"], tb.get("gain", 1.2))
    run = trunk_run(spr, hy - 3, hx)
    tr = tb.get("r", ((run[1] - run[0] + 1) / 32.0) if run else 0.25)
    tr = float(np.clip(tr, 0.06, 0.8))
    wood = Geo()
    top_z = tb.get("top", max(zc - 0.2 * c, 1.0))
    base = np.array([tb.get("x", 0.0), tb.get("y", 0.0), 0.0])
    tip = np.array([tb.get("tip_x", xc * 0.4), tb.get("tip_y", 0.0), top_z])
    flared_trunk(wood, base, tip, tr, tr * tb.get("taper", 0.6), tb.get("flare", 1.0), tb.get("buttress", 5), rng, barkc,
                 sides=14 if tr > 0.3 else 10)
    for C, R in big[:tb.get("limbs", 5)]:
        s0 = tip * 0.85 + base * 0.15
        end = C + (s0 - C) * 0.25
        m = 5
        L = np.array([s0 + (end - s0) * (k / m) + np.array([0, 0, 0.3 * math.sin(math.pi * k / m)]) for k in range(m + 1)])
        tube(wood, L, np.linspace(tr * 0.5, tr * 0.2, m + 1), sides=6, col=barkc * 0.9)
    for st in spec.get("stems", []):
        # limbs drawn under the crown: pixel points lifted at the given depths
        pts = []
        for (col, row), y in zip(st["px"], st.get("y", [0.0] * len(st["px"]))):
            pts.append(((col + 0.5 - hx) / 16, y, (hy - row - 0.5 - 16 * y) / 8))
        P = np.array(pts)
        scol = lin(st["rgb"], 1.2) if "rgb" in st else barkc
        tube(wood, P, np.linspace(st["r"], st["r"] * 0.5, len(P)), sides=7, col=scol)
    return wood, tex


def build_fir(spr, spec, rng):
    """A fir: a trunk and whorls of branches, each whorl as wide as the
    drawing's inscribed circle at its height, so the classic view covers
    the drawn crown. Living: two needle sprays crossed along every branch,
    whorls evenly round at every height, a leader spray on top, over a
    near-black cone. Dead: short stiff spurs all the way up, each carrying
    crossed twig cards, and where the drawing runs on below the anchor the
    lowest limbs reach forward and down to the ground to cover it."""
    hx, hy = spr.hx, spr.hy
    dead = spec.get("dead", False)
    mask = s2.close(spr.mask, spec.get("close", 2 if dead else 1))
    dt = s2.edt(mask)
    H = spec.get("H", (hy - spr.top) / 8.0)
    ax = spec.get("axis", hx)

    def half_width(row, col=None):
        """The drawn half width in cells at a row, round a column."""
        row = int(round(row))
        if not 0 <= row < spr.h:
            return 0.1
        c = ax if col is None else col
        lo, hi = max(0, int(c) - 2), min(spr.w, int(c) + 3)
        return float(dt[row, lo:hi].max()) / 16.0 if hi > lo else 0.1

    def Rz(z):
        return half_width(hy - 8 * z)

    gain = spec.get("gain", 1.25)
    dz = spec.get("dz", 0.33)
    zs = np.arange(spec.get("skirt", 0.25), H - 0.2, dz)
    reach = spec.get("reach", 1.0)
    droop = spec.get("droop", 0.35)
    xa = (ax + 0.5 - hx) / 16.0
    wood = Geo()
    tex, barkc = bark_from(spr, None, "speckle" if dead else "streak", gain=gain)
    tr = spec.get("trunk_r", 0.12 if not dead else 0.1)
    if not dead:
        n = 10
        P = np.array([[xa, 0, (H - 0.4) * k / n] for k in range(n + 1)])
        tube(wood, P, np.linspace(tr, 0.02, n + 1), sides=6, col=barkc * 0.5, cap_base=True)
        needles, core = Geo(), Geo()
        dark, mid, light = palette(spr, spr.mask, gain)
        cross = math.radians(spec.get("cross", 50))
        zup = np.array([0.0, 0.0, 1.0])
        prof = []

        def spray(spine, widths, side, cols, sh):
            # two sprays crossed along the branch, so it is full from any side
            for sgn in (-1.0, 1.0):
                sd = side * math.cos(cross) + zup * math.sin(cross) * sgn
                strip(needles, spine, widths, [sd] * len(spine), cols=cols, N=[sh] * len(spine))

        for z in zs:
            R = Rz(z) * reach
            prof.append((z, R))
            if R < 0.12:
                continue
            nb = int(np.clip(round(TAU * R / spec.get("spacing", 0.5)), 5, spec.get("max_branches", 20)))
            ph = rng.uniform(0, TAU)
            for k in range(nb):
                ang = ph + TAU * k / nb + rng.uniform(-0.15, 0.15)
                L = R * rng.uniform(0.85, 1.05)
                h = np.array([math.cos(ang), math.sin(ang), 0])
                side = np.array([-math.sin(ang), math.cos(ang), 0])
                ts = np.array([0.0, 0.55, 1.0])
                spine = np.array([np.array([xa, 0, z]) + h * (0.05 + L * t) + np.array([0, 0, 0.12 * L * t - droop * L * t * t])
                                  for t in ts])
                w = float(np.clip(0.35 + 0.35 * L, 0.35, 0.95)) * spec.get("spray", 1.0)
                sh = _unit(h * 0.5 + np.array([0, 0, 0.9]))
                ao = 0.7 + 0.3 * min(1.0, z / H + 0.3)
                jit = rng.uniform(0.85, 1.12)
                cols = [mixc(dark, mid, t / 0.7) * ao * jit if t < 0.7 else mixc(mid, light, (t - 0.7) / 0.3 * 0.6) * ao * jit
                        for t in ts]
                spray(spine, [w * 0.5, w, w * 0.75], side, cols, sh)
        # the leader: upright sprays round the top, hiding the cone's tip
        for k in range(3):
            ang = math.pi * k / 3 + rng.uniform(-0.2, 0.2)
            side = np.array([-math.sin(ang), math.cos(ang), 0])
            spine = np.array([[xa, 0, H - 0.9], [xa, 0, H - 0.4], [xa, 0, H + 0.02]])
            strip(needles, spine, [0.5, 0.42, 0.12], [side] * 3, cols=[mid * 0.8, mixc(mid, light, 0.4), light * 0.9],
                  N=[_unit(np.array([0, -0.3, 1.0]))] * 3)
        # a near-black cone inside the branches
        ring = 10
        V, F, N = [], [], []
        pr = [(0.15, prof[0][1] * 0.55)] + [(z, R * 0.55) for z, R in prof if z < H - 0.5] + [(H - 0.4, 0.0)]
        for j, (z, R) in enumerate(pr):
            for k in range(ring):
                a = TAU * k / ring
                V.append((xa + R * math.cos(a), R * math.sin(a), z))
                N.append((math.cos(a), math.sin(a), 0.7))
        for j in range(len(pr) - 1):
            for k in range(ring):
                a0, b0 = j * ring + k, j * ring + (k + 1) % ring
                F.append((a0, b0, b0 + ring, a0 + ring))
        core.add(np.array(V), F, None, dark * spec.get("core_dark", 0.4), np.array(N))
        nm = material("needles", image("needles", tex_leaves("needles", 7)), cut=True, cull=False)
        parts = [needles.to_object(spr.name + "_needles", nm), core.to_object(spr.name + "_core", material("core")),
                 wood.to_object(spr.name + "_wood", material("bark", image("bark", tex)))]
        return parts, {"whorls": len(zs), "needle_tris": needles.tris()}
    # dead: a trunk on a flared foot, spurs all the way up
    cards = Geo()
    ttex = tex_leaves("twigs", 11)
    flared_trunk(wood, np.array([xa, 0.0, 0.0]), np.array([xa, 0.0, H]), tr, 0.035, spec.get("flare", 1.2), 4, rng, barkc,
                 sides=6, rings=8, hf=0.35)
    n_spur = 0

    def spur(p0, d, L, col):
        """A stiff twig spur with two twig cards crossed along it."""
        nonlocal n_spur
        d = _unit(d)
        p1 = p0 + d * L
        rb = float(np.clip(0.035 + 0.025 * L, 0.045, 0.07)) * spec.get("thick", 1.0)
        tube(wood, np.array([p0, p1]), np.array([rb, rb * 0.4]), sides=3, col=col)
        side = _unit(np.cross(d, [0.0, 0.0, 1.0])) if abs(d[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
        upn = np.cross(side, d)
        w = L * spec.get("card_w", 0.55)
        for sgn in (-1.0, 1.0):
            nrm = _unit(upn * math.cos(0.8) + side * math.sin(0.8) * sgn)
            card(cards, p0 + d * L * 0.05, nrm, d, w, L * 1.05, col=col * rng.uniform(0.9, 1.15),
                 N=_unit(nrm * 0.3 + np.array([0, 0, 1.0])), anchor=0.0)
        n_spur += 1

    def extents(row):
        """How far the drawn column runs left and right of the axis at a row."""
        r = int(round(row))
        if not 0 <= r < spr.h or not mask[r].any():
            return 0.1, 0.1
        xs = np.nonzero(mask[r])[0]
        c = int(xs[np.argmin(np.abs(xs - ax))])
        lo = hi = c
        while lo > 0 and mask[r, lo - 1]:
            lo -= 1
        while hi < spr.w - 1 and mask[r, hi + 1]:
            hi += 1
        return max(ax - lo + 0.5, 1.0) / 16.0, max(hi - ax + 0.5, 1.0) / 16.0

    below = max((spr.bottom - hy) / 16.0, 0.0)
    z_skirt = spec.get("skirt_top", 1.0) if below > 0.3 else 0.0
    hw_crown = float(np.median([sum(extents(hy - 8 * z)) / 2 for z in zs]))
    # 'ragged': whorl heights jittered by a share of their spacing, spurs
    # drooping (degrees) at varied lengths and thinned out above a height,
    # so no two whorls line up as bands in the classic view
    rag = spec.get("ragged")
    for w, z in enumerate(zs):
        if z < z_skirt:
            continue
        if rag:
            z = z + dz * rng.uniform(-1, 1) * rag.get("jit", 0.3)
        le, ri = extents(hy - 8 * z)
        nb = int(np.clip(round(TAU * (le + ri) / 2 / spec.get("spacing", 0.45)), 4, spec.get("max_branches", 8)))
        ph = rng.uniform(0, TAU)
        for k in range(nb):
            ang = ph + TAU * k / nb + rng.uniform(-0.3, 0.3)
            c, sn = math.cos(ang), math.sin(ang)
            # the reach to the side is the drawn column's; toward and away
            # from the camera, where a spur would draw as a vertical stroke,
            # they are shorter, so the column reads as sideways spurs
            side = (le if c < 0 else ri) * reach
            L = 1.0 / math.sqrt((c / side) ** 2 + (sn / (side * spec.get("depth", 0.6))) ** 2) * rng.uniform(0.8, 1.0)
            d = np.array([c, sn, rng.uniform(0.05, 0.3)])
            if rag:
                if z > rag.get("thin_above", 0.0) * H and rng.random() > rag.get("keep", 0.7):
                    continue
                d[2] = -math.tan(math.radians(rng.uniform(*rag.get("droop", (15, 30)))))
                L *= rng.uniform(*rag.get("len", (0.6, 1.0)))
                if rag.get("up_len") and z > 0.5 * H:
                    # the upper crown spiky and see-through: short spurs,
                    # about half of them turned up
                    L *= rng.uniform(*rag["up_len"]) / rng.uniform(*rag.get("len", (0.6, 1.0)))
                    if rng.random() < rag.get("up_share", 0.5):
                        d[2] = math.tan(math.radians(rng.uniform(*rag.get("up_tilt", (10, 30)))))
            spur(np.array([xa, 0.0, z + rng.uniform(-0.1, 0.1) * (2 if rag else 1)]), d, L, barkc * rng.uniform(0.8, 1.15))
    # the skirt: the lowest limbs drooping to the ground all round, longest
    # toward the camera where the drawing runs on below the anchor, as
    # short to the sides as the drawn column is narrow
    # (straight at and away from the camera the limbs draw as the trunk
    # running on down and up, the rest go out near the sides)
    for w_, z0 in enumerate(np.linspace(0.3, z_skirt, spec.get("skirt_whorls", 2)) if z_skirt else []):
        if rag:
            z0 = z0 * rng.uniform(1 - rag.get("jit", 0.3), 1 + rag.get("jit", 0.3))
        angles = (-90, 90, -25, -155, 25, 155) if w_ == 0 else (-90, -40, -140, 40, 140)
        if rag and rag.get("skirt_extra"):
            angles = angles + tuple(rag["skirt_extra"][w_ % 2])
        for a_deg in angles:
            ang = math.radians(a_deg + (0 if abs(a_deg) == 90 else rng.uniform(-8, 8)))
            c, sn = math.cos(ang), math.sin(ang)
            le, ri = extents(hy - 8 * z0)
            side = (le if c < 0 else ri) * 1.1
            D = below if sn < 0 else below * 0.7
            L = 1.0 / math.sqrt((c / side) ** 2 + (sn / D) ** 2)
            if rag and rag.get("skirt_cap"):
                L = min(L, rag["skirt_cap"] * hw_crown)
            if sn < -0.9:
                L = below * 1.04  # straight at the camera: down to the drawn foot
            short = bool(rag and a_deg in rag.get("skirt_extra", ((), ()))[w_ % 2])
            if short:
                # the extra front limbs: short thin ones, spiky, not legs
                L *= rag.get("extra_len", 0.5)
            h = np.array([c, sn, 0.0])
            ts = np.linspace(0, 1, 5)
            P = np.array([np.array([xa, 0.0, 0.0]) + h * L * t + np.array([0, 0, z0 * (1 - t) ** 1.5 + 0.04]) for t in ts])
            r0 = 0.045 if short else tr * 0.9 if abs(sn) > 0.9 else 0.07
            tube(wood, P, np.linspace(r0, 0.035, 5), sides=5, col=barkc * 0.85)
            if rag and rag.get("alternate"):
                # single spurs by turns left and right; the camera-facing
                # limb, which draws as a vertical stroke, carries at most
                # three, angled forward along it so no pair draws as a rung
                sg0 = rng.choice((-1.0, 1.0))
                face = sn < -0.7
                ts_ = np.sort(rng.uniform(0.25, 0.9, rag.get("face_spurs", 3))) if face else (0.35, 0.6, 0.85)
                for q, t in enumerate(ts_):
                    t = float(np.clip(t + rng.uniform(-0.09, 0.09), 0.15, 0.98))
                    p = np.array([np.interp(t, ts, P[:, i]) for i in range(3)])
                    sgn = sg0 * (1 if q % 2 == 0 else -1)
                    perp = np.array([-sn, c, 0.0]) * sgn
                    le2, ri2 = extents(hy - 16 * p[1] - 8 * p[2])
                    w = max((le2 if perp[0] < 0 else ri2) - abs(p[0] - xa), 0.12)
                    if face:
                        a = math.radians(rng.uniform(30, 50))
                        dd = h * math.cos(a) + perp * math.sin(a)
                        dd[2] = math.tan(math.radians(rng.uniform(-30, 40)))
                        Ls = max(w, 0.12) * 0.8 * rng.uniform(0.4, 0.8)
                    else:
                        dd = np.array([sgn * abs(sn) + c * 0.3, -sgn * c * 0.5, 0.0])
                        dd[2] = -math.tan(math.radians(rng.uniform(*rag.get("droop", (15, 30))))) * np.linalg.norm(dd[:2])
                        Ls = w * rng.uniform(*rag.get("len", (0.6, 1.0)))
                    spur(p + np.array([0, 0, rng.uniform(-0.12, 0.12)]), dd, Ls, barkc * rng.uniform(0.8, 1.1))
                continue
            for t in ((0.25, 0.45, 0.65, 0.85, 0.98) if sn < -0.9 else (0.35, 0.6, 0.85)):
                if rag:
                    t = float(np.clip(t + rng.uniform(-0.09, 0.09), 0.15, 0.98))
                p = np.array([np.interp(t, ts, P[:, i]) for i in range(3)])
                le2, ri2 = extents(hy - 16 * p[1] - 8 * p[2])
                for sgn in (-1.0, 1.0):
                    w = (le2 if sgn < 0 else ri2) - abs(p[0] - xa)
                    if rag:
                        # drooping, each side at its own height and length
                        dd = np.array([sgn * abs(sn) + c * 0.3, -sgn * c * 0.5, 0.0])
                        dd[2] = -math.tan(math.radians(rng.uniform(*rag.get("droop", (15, 30))))) * np.linalg.norm(dd[:2])
                        spur(p + np.array([0, 0, rng.uniform(-0.12, 0.12)]), dd,
                             max(w, 0.12) * rng.uniform(*rag.get("len", (0.6, 1.0))), barkc * rng.uniform(0.8, 1.1))
                        continue
                    spur(p, np.array([sgn * abs(sn) + c * 0.3, -sgn * c * 0.5, rng.uniform(0.1, 0.3)]),
                         max(w, 0.12) * rng.uniform(0.8, 1.0), barkc * rng.uniform(0.8, 1.1))
    twm = material("twigs", image("twigs", ttex), cut=True, cull=False)
    parts = [wood.to_object(spr.name + "_twigs", material("bark", image("bark", tex), repeat=True)),
             cards.to_object(spr.name + "_sprays", twm)]
    return parts, {"whorls": len(zs), "spurs": n_spur, "wood_tris": wood.tris(), "card_tris": cards.tris()}


def kmeans(X, k, rng, it=12):
    C = X[rng.choice(len(X), k, replace=False)].copy()
    lab = np.zeros(len(X), np.int64)
    for _ in range(it):
        lab = ((X[:, None, :] - C[None]) ** 2).sum(-1).argmin(1)
        for j in range(k):
            if (lab == j).any():
                C[j] = X[lab == j].mean(0)
    return C, lab


def components(mask):
    """8-connected pieces of a mask, each a list of (row, col)."""
    seen = np.zeros_like(mask)
    out = []
    for r0, c0 in zip(*np.nonzero(mask)):
        if seen[r0, c0]:
            continue
        comp, stack = [], [(r0, c0)]
        seen[r0, c0] = True
        while stack:
            r, c = stack.pop()
            comp.append((r, c))
            for dy, dx in s2.NB8:
                q = (r + dy, c + dx)
                if 0 <= q[0] < mask.shape[0] and 0 <= q[1] < mask.shape[1] and mask[q] and not seen[q]:
                    seen[q] = True
                    stack.append(q)
        out.append(comp)
    return out


def build_grass(spr, spec, rng):
    """A tuft of blades standing on the drawing's strokes: every small
    stroke becomes a blade from its lowest to its highest pixel, and the
    clumps get blades strewn over their pixels. Blades are two-sided with
    the sky above both faces, so no side renders darker than the drawing's
    darker strokes, and coloured by the strokes they stand on."""
    hx, hy = spr.hx, spr.hy
    rows, cols = np.nonzero(spr.mask)
    X = spr.rgb[rows, cols]
    C, lab = kmeans(X, spec.get("colours", 5), rng)
    gain = spec.get("gain", 1.2)
    floor = lin(spr.colour(None, 0.3, 0.5), gain)
    labels = np.full(spr.mask.shape, -1)
    labels[rows, cols] = lab
    wid = spec.get("width", 0.08)
    geo = Geo()
    zup = np.array([0.0, 0.0, 1.0])

    def blade(base, tip, cc, w):
        mid = base + (tip - base) * 0.5 + np.array([0, 0, 0.05 * np.linalg.norm(tip - base)])
        spine = np.array([base, mid, tip])
        out = np.array([tip[0] - base[0], tip[1] - base[1], 0.0]) + rng.normal(0, 0.05, 3) * np.array([1, 1, 0])
        out = _unit(out) if np.linalg.norm(out) > 1e-6 else _unit(rng.normal(0, 1, 3) * np.array([1, 1, 0]))
        side = _unit(np.cross(out, zup) + rng.normal(0, 0.4, 3))
        cols3 = [np.maximum(cc * 0.85, floor), np.maximum(cc, floor), np.maximum(cc * 1.08, floor)]
        sh = _unit(zup + out * 0.25)
        strip(geo, spine, [w, w * 0.7, w * 0.12], [side] * 3, cols=cols3, N=[sh] * 3, both=True)

    def colour_of(pix):
        ls = [labels[r, c] for r, c in pix if labels[r, c] >= 0]
        k = max(set(ls), key=ls.count) if ls else 0
        return lin(C[k], gain)

    # one blade up every small stroke, from its lowest to its highest pixel
    n_stroke = 0
    big = np.zeros_like(spr.mask)
    for comp in components(spr.mask):
        rr = np.array([p[0] for p in comp])
        cc_ = np.array([p[1] for p in comp])
        if rr.max() - rr.min() > spec.get("stroke_h", 10) or cc_.max() - cc_.min() > 7:
            for r, c in comp:
                big[r, c] = True
            continue
        b = comp[int(np.argmax(rr + rng.uniform(0, 0.1, len(rr))))]
        t = comp[int(np.argmin(rr + rng.uniform(0, 0.1, len(rr))))]
        y0 = (hy - b[0] - 0.5 - (2 if len(comp) < 3 else 0)) / 16
        h = max(b[0] - t[0] + 1.0, 3.0) / 8
        ly = rng.uniform(-0.3, 0.3) * h * 0.5
        base = np.array([(b[1] + 0.5 - hx) / 16, y0, 0.0])
        tip = np.array([(t[1] + 0.5 - hx) / 16, y0 + ly, max(h - 2 * ly, 0.15)])
        blade(base, tip, colour_of(comp), wid * 1.4)
        n_stroke += 1
    # the clumps: blades strewn over their pixels, the upper part of each
    # on the pixel it was drawn from
    br, bc = np.nonzero(big)
    n = int(len(br) * spec.get("density", 0.6))
    h0 = spec.get("height", 0.45)
    if len(br):
        gx = ((bc + 0.5 - hx) / 16).mean()
        gy = ((hy - br - 0.5) / 16).mean()
        for i in rng.choice(len(br), n, replace=True):
            col, row = bc[i] + rng.uniform(0, 1), br[i] + rng.uniform(0, 1)
            h = h0 * rng.uniform(0.6, 1.25)
            x0, y0 = (col - hx) / 16, (hy - row) / 16
            out = _unit(np.array([x0 - gx, y0 - gy, 0.0]) + rng.normal(0, 0.6, 3) * np.array([1, 1, 0]))
            lean = out * h * rng.uniform(0.15, 0.45)
            base = np.array([x0 - 0.65 * lean[0], y0 - 0.65 * lean[1] - 0.65 * h * 0.5, 0.0])
            tip = base + lean + np.array([0, 0, h])
            blade(base, tip, lin(C[labels[br[i], bc[i]]], gain), wid)
    return [geo.to_object(spr.name + "_blades", material("blades", rough=0.8, cull=False))], {"strokes": n_stroke, "blades": n}


def build_rosette(spr, spec, rng):
    """A ground rosette: many narrow pointed leaves in a spiral from a dark
    heart, the young inner ones standing steep, the old outer ones arching
    out and down to the ground; each leaf dark at its foot and pale green
    at its tip. The leaves are one face on a two-sided material, so their
    undersides take the shade side and read near-black. Dead: broken,
    curled leaf fragments strewn flat round a small dark heart."""
    if spec.get("dead"):
        return build_debris(spr, spec, rng)
    hx, hy = spr.hx, spr.hy
    ccol, crow = spec.get("centre", (hx, hy))
    h0 = spec.get("heart", 0.12)
    cx, cy = (ccol + 0.5 - hx) / 16, (hy - crow - 0.5 - 8 * h0) / 16
    Rg = spec.get("radius", (spr.right - spr.left + 1) / 32.0)
    Hh = spec.get("height", 0.9)
    gain = spec.get("gain", 1.25)
    dark, mid, light = palette(spr, spr.mask, gain)
    pale = lin(spr.colour(None, 0.93, 1.0), gain)
    geo, heart = Geo(), Geo()
    n = spec.get("leaves", 90)
    ns = spec.get("segments", 6)
    golden = math.pi * (3 - math.sqrt(5))
    W0 = spec.get("width", 0.4) * min(1.0, Rg / 1.5) ** 0.5
    C0 = np.array([cx, cy, h0])
    for i in range(n):
        f = (i + 0.5) / n  # 0 the youngest, at the heart; 1 the oldest, outermost
        ang = i * golden + rng.normal(0, 0.08)
        h = np.array([math.cos(ang), math.sin(ang), 0.0])
        rt = Rg * (0.25 + 0.85 * f ** 0.75) * rng.uniform(0.9, 1.08)
        zt = Hh * (1 - f) ** 1.4 * rng.uniform(0.85, 1.1) + 0.03
        # a quadratic arch: up out of the heart, over, down to the tip
        ctrl = C0 + h * rt * (0.3 + 0.2 * f) + np.array([0, 0, max(zt, h0) + rt * 0.45 * f + 0.1])
        tip = C0 * np.array([1, 1, 0]) + h * rt + np.array([0, 0, zt])
        base = C0 + h * 0.04
        ts = np.linspace(0, 1, ns + 1)
        spine = np.array([(1 - t) ** 2 * base + 2 * (1 - t) * t * ctrl + t * t * tip for t in ts])
        spine[:, 2] = np.maximum(spine[:, 2], 0.02)
        W = W0 * rng.uniform(0.8, 1.15) * (0.75 + 0.35 * f)
        widths = [W * (0.45 + 0.55 * min(1.0, t * 3.5)) * (1 - t) ** 0.8 + 0.004 for t in ts]
        side = np.array([-math.sin(ang), math.cos(ang), 0.0])
        T = _unit(np.gradient(spine, axis=0))
        upn = [_unit(np.cross(side, t_)) for t_ in T]
        upn = [u if u[2] > 0 else -u for u in upn]
        lift_ = 0.7 + 0.45 * (1 - f)
        jit = rng.uniform(0.8, 1.2)
        cols = [(mixc(dark * 0.3, mid, t / 0.2) if t < 0.2 else mixc(mid, pale * 1.15, ((t - 0.2) / 0.8) ** 0.9)) * jit * lift_
                for t in ts]
        strip(geo, spine, widths, [side] * len(ts), cols=cols, N=[_unit(u + np.array([0, 0, 0.6])) for u in upn],
              crease=spec.get("crease", 0.35), up=upn)
    ellipsoid(heart, np.array([cx, cy, 0.0]), (Rg * 0.16, Rg * 0.16, Hh * 0.18), seg=10, rings=5, col=dark * 0.6,
              normals_from=np.array([cx, cy, -0.5]))
    tex = image("strap", tex_strap(rng))
    parts = [geo.to_object(spr.name + "_leaves", material("leaves", tex, rough=0.7, cull=False)),
             heart.to_object(spr.name + "_heart", material("heart"))]
    return parts, {"tris": geo.tris() + heart.tris()}


def build_debris(spr, spec, rng):
    """A withered rosette: short broken leaf fragments, curled and lying
    almost flat, strewn with gaps where the drawing has its flecks, grey
    brown from the pixels they lie on, round a small dark heart."""
    hx, hy = spr.hx, spr.hy
    gain = spec.get("gain", 1.2)
    geo, heart = Geo(), Geo()
    rows, cols = np.nonzero(spr.mask)
    cx, cy = (hx + 0.5 - hx) / 16, (hy - hy - 0.5) / 16
    n = spec.get("fragments", 70)
    for i in rng.choice(len(rows), min(n, len(rows)), replace=False):
        r, c = rows[i], cols[i]
        p = np.array([(c + rng.uniform(0, 1) - hx) / 16, (hy - r - rng.uniform(0, 1)) / 16, 0.0])
        out = np.array([p[0] - cx, p[1] - cy, 0.0])
        d = np.linalg.norm(out)
        out = out / d if d > 1e-3 else _unit(np.array([1.0, 0.3, 0.0]))
        # mostly lying along the spokes, some knocked askew
        ang = math.atan2(out[1], out[0]) + rng.normal(0, 0.5)
        h = np.array([math.cos(ang), math.sin(ang), 0.0])
        side = np.array([-h[1], h[0], 0.0])
        L = rng.uniform(0.1, 0.3)
        curl = rng.uniform(-1.2, 1.2)
        ts = np.linspace(0, 1, 4)
        spine = np.array([p - h * L / 2 + (h * math.cos(curl * t) + side * math.sin(curl * t)) * L * t
                          + np.array([0, 0, 0.02 + 0.05 * math.sin(math.pi * t) * rng.uniform(0.3, 1.0)]) for t in ts])
        W = rng.uniform(0.05, 0.1)
        col = lin(spr.rgb[r, c], gain) * rng.uniform(0.85, 1.15)
        strip(geo, spine, [W * 0.7, W, W * 0.8, W * 0.3], [side] * 4, cols=[col * 0.8, col, col, col * 1.1],
              N=[np.array([0.0, 0.0, 1.0])] * 4, both=True)
    # the heart: a low dark lump with a few broken stubs
    ellipsoid(heart, np.array([cx, cy, 0.0]), (0.22, 0.22, 0.12), seg=8, rings=4, col=lin(spr.colour(None, 0.0, 0.15), gain),
              normals_from=np.array([cx, cy, -0.3]))
    for k in range(6):
        a = TAU * k / 6 + rng.uniform(-0.3, 0.3)
        h = np.array([math.cos(a), math.sin(a), 0.0])
        P = np.array([[cx, cy, 0.08], [cx, cy, 0.12] + h * 0.12, [cx, cy, 0.05] + h * rng.uniform(0.2, 0.3)])
        tube(heart, P, np.array([0.04, 0.03, 0.01]), sides=4, col=lin(spr.colour(None, 0.1, 0.4), gain))
    parts = [geo.to_object(spr.name + "_leaves", material("leaves", rough=0.8, cull=False)),
             heart.to_object(spr.name + "_heart", material("heart"))]
    return parts, {"tris": geo.tris() + heart.tris()}


def tex_strap(rng, size=(64, 32)):
    """A strap leaf across u, along v: a pale midrib, darker edges, fine
    streaks along its length."""
    h, w = size
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    u = (xx + 0.5) / w
    val = 0.3 + 0.7 * np.exp(-((u - 0.5) / 0.16) ** 2) - 0.15 * (np.abs(u - 0.5) > 0.38)
    val *= 0.9 + 0.1 * _norm01(_blur(rng.random((h, w)), 6, 0.6))
    a = np.ones((h, w, 4))
    for i in range(3):
        a[..., i] = np.clip(val, 0, 1)
    return a


def build_bulb(spr, spec, rng):
    """The Taros bulb: a ribbed dome half sunk in the ground, a skirt of
    dark spines round its foot, a neck up to a red spiked flower with pale
    tendrils hanging from under it."""
    hx, hy = spr.hx, spr.hy
    gain = spec.get("gain", 1.25)
    bc, br_, Rpx = spec["bulb"]  # centre column and row of the dome's ground centre, radius px
    Rb = Rpx / 16.0
    bx = (bc + 0.5 - hx) / 16
    by = (hy - br_ - 0.5) / 16
    sink = spec.get("sink", 0.15)
    dome, spines, pale, red = Geo(), Geo(), Geo(), Geo()
    dark_sel = spr.hue_mask(lambda r, g, b: (r < 0.45) & (np.abs(r - g) < 0.08))
    dcol = lin(spr.colour(dark_sel, 0.3, 0.9), gain)
    nr = spec.get("ribs", 16)
    seg, rings = nr * 2, 9
    V, F, C, N, UV = [], [], [], [], []
    m1 = seg + 1
    for j in range(rings + 1):
        th = (math.pi / 2 + sink) * j / rings
        for k in range(seg + 1):
            ph = TAU * (k % seg) / seg
            rib = 1 + 0.06 * math.cos(nr * ph)
            d = np.array([math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)])
            p = np.array([bx, by, -Rb * math.sin(sink)]) + d * Rb * np.array([rib, rib, 1.0])
            V.append(p)
            N.append(d)
            C.append(dcol * (0.75 + 0.25 * (0.5 + 0.5 * math.cos(nr * ph))))
            # the drawn ribs are finer than the modelled ones: one strip of
            # the ribbed texture per drawn rib, from crown to foot
            UV.append((spec.get("tex_ribs", 40) * k / seg, j / rings))
    for j in range(rings):
        for k in range(seg):
            a0, b0 = j * m1 + k, j * m1 + k + 1
            F.append((a0, a0 + m1, b0 + m1, b0))
    dome.add(np.array(V), F, np.array(UV), np.array(C), np.array(N))
    ns = spec.get("spines", 90)
    grey = lin(spr.colour(dark_sel, 0.8, 1.0), gain)
    for k in range(ns):
        ph = TAU * k / ns + rng.uniform(-0.05, 0.05)
        th = math.radians(rng.uniform(55, 95))
        d = np.array([math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)])
        p0 = np.array([bx, by, -Rb * math.sin(sink)]) + d * Rb * 0.97
        out = np.array([math.cos(ph), math.sin(ph), 0.0])
        L = Rb * rng.uniform(0.45, 0.85) * spec.get("spine_len", 1.0)
        # out from the dome, then bending down toward the ground
        p1 = p0 + (_unit(d + out) * 0.6 + out * 0.4) * L * 0.55
        p2 = p0 + out * L + np.array([0, 0, -max(p0[2], 0) * 0.7])
        p2[2] = max(p2[2], 0.03)
        tube(spines, np.array([p0, p1, p2]), np.array([0.08, 0.05, 0.015]) * spec.get("spine_r", 1.0), sides=3,
             cols=np.array([dcol * 0.8, mixc(dcol, grey, 0.5), grey]) * rng.uniform(0.85, 1.15))
    fc, fr = spec["flower"]
    fy = spec.get("flower_y", 0.3)
    F3 = np.array([(fc + 0.5 - hx) / 16, fy, (hy - fr - 0.5 - 16 * fy) / 8])
    # the stalk rises from well inside the dome, flaring into it, so it is
    # joined from every side
    top = np.array([bx, by, Rb * (1 - math.sin(sink)) - spec.get("neck_sink", 0.5)])
    m = 7
    neck = np.array([top + (F3 - top) * (k / m) + np.array([0, 0, 0.2 * math.sin(math.pi * k / m)]) for k in range(m + 1)])
    neck_r = np.linspace(Rb * 0.28, Rb * 0.12, m + 1)
    neck_r[:3] *= np.array([1.6, 1.3, 1.08])
    tube(dome, neck, neck_r, sides=8, col=dcol * 0.9, cap_base=True)
    pale_sel = spr.hue_mask(lambda r, g, b: (r > 0.45) & (np.abs(r - g) < 0.08) & (np.abs(g - b) < 0.1))
    pcol = lin(spr.colour(pale_sel, 0.3, 0.95), gain)
    nt = spec.get("tendrils", 22)
    dome_c = np.array([bx, by, -Rb * math.sin(sink)])
    for k in range(nt):
        ph = TAU * k / nt + rng.uniform(-0.2, 0.2)
        out = np.array([math.cos(ph), math.sin(ph), 0.0])
        L = spec.get("tendril_len", 2.0) * rng.uniform(0.7, 1.1)
        droop = rng.uniform(0.6, 1.1)
        pts = []
        for t in np.linspace(0, 1, 9):
            # curling from side to side as it reaches out and droops
            wig = np.array([-out[1], out[0], 0]) * 0.2 * math.sin(t * 11 + k * 1.7)
            p = F3 - np.array([0, 0, 0.1]) + out * L * t + wig + np.array([0, 0, 0.25 * L * t - droop * L * t * t])
            # draped over the dome, not through it
            dv = p - dome_c
            if np.linalg.norm(dv) < Rb * 1.04:
                p = dome_c + _unit(dv) * Rb * 1.04
            pts.append(p)
        pts = np.array(pts)
        pts[:, 2] = np.maximum(pts[:, 2], 0.05)
        tr = spec.get("tendril_r", 0.085)
        tube(pale, pts, np.linspace(tr, tr * 0.45, 9), sides=4, col=pcol * rng.uniform(0.85, 1.1))
        for e in (pts[3], pts[6]):
            f2 = e + _unit(out + np.array([-out[1], out[0], 0]) * rng.choice([-0.9, 0.9]) + np.array([0, 0, 0.3])) * L * 0.2
            tube(pale, np.array([e, (e + f2) / 2 + np.array([0, 0, 0.05]), f2]), np.array([tr * 0.6, tr * 0.45, tr * 0.2]),
                 sides=3, col=pcol * rng.uniform(0.85, 1.1))
    red_sel = spr.hue_mask(lambda r, g, b: (r > g * 1.8) & (r > 0.25))
    rcol = lin(spr.colour(red_sel, 0.2, 0.95), gain)
    rdark = lin(spr.colour(red_sel, 0.0, 0.3), gain)
    for k in range(spec.get("petals", 52)):
        d = _unit(rng.normal(0, 1, 3) + np.array([0, 0, 0.7]))
        L = spec.get("petal_len", 1.1) * rng.uniform(0.6, 1.1)
        tube(red, np.array([F3, F3 + d * L * 0.5, F3 + d * L]), np.array([0.11, 0.07, 0.012]), sides=3,
             cols=np.array([rdark, rcol, rcol * 1.1]))
    ellipsoid(red, F3, (0.3, 0.3, 0.2), seg=8, rings=4, col=rdark, normals_from=F3)
    tex = image("dome", tex_ribs(rng))
    mats = [("dome", dome, material("dome", tex, repeat=True)), ("spines", spines, material("spines")),
            ("tendrils", pale, material("tendrils")), ("flower", red, material("flower", rough=0.6))]
    parts = [g.to_object(spr.name + "_" + nm, m) for nm, g, m in mats]
    return parts, {nm: g.tris() for nm, g, m in mats}


def tex_ribs(rng, size=(128, 32)):
    """One rib of the bulb, crown (top) to foot: a lit ridge down the middle,
    dark grooves at the sides, pale flecks."""
    h, w = size
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    u = (xx + 0.5) / w
    val = 0.45 + 0.55 * np.sin(math.pi * u) ** 1.2
    fleck = _norm01(_blur(rng.random((h, w)), 1.5, 0.8))
    val = val * (0.75 + 0.25 * fleck) + 0.35 * (fleck > 0.8)
    a = np.ones((h, w, 4))
    for i in range(3):
        a[..., i] = np.clip(val, 0, 1)
    return a


# the parts whose colour the build calibrates against the drawing (None: all)
CALIB_PARTS = {"crown": ("leaves", "core"), "fir": ("needles", "core", "twigs", "sprays"), "rosette": ("leaves",),
               "limbs": ("limbs", "body")}

BUILDERS = {"limbs": build_limbs, "crown": build_crown, "fir": build_fir, "grass": build_grass,
            "rosette": build_rosette, "bulb": build_bulb}
