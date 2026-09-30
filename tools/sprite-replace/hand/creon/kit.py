"""The creon family's kit: reading the classic sprites and making meshes,
textures and materials for Creon's rocks, standing stones and buildings,
run inside Blender.

Same frame as carve.py: one unit is one map cell, x east, y north (away
from the classic camera), z up, the origin at the sprite's hotspot on the
ground. A point (x, y, z) lands on the picture at column hx + 16 x and row
hy - 16 y - 8 z, so the classic camera sees no east or west faces, only
the tops and the faces turned south, and a cell of depth moves a point on
the picture as far as two cells of height.

A Field is a heightfield on a ground grid of one cell per sprite column
across and 1/16 cell deep, which field_mesh turns into flat-topped solids
with walls down to the ground. A rock fills it with a dome over each
column (dome); stones.py fills it with the tops the classic camera saw.

Only numbers are read from the pictures (outlines, run lengths, mean
colours). Every texture is made here from noise and a few numbers
(okpaint.generated), and the models ship as geometry.
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.normpath(os.path.join(HERE, "..", ".."))
for p in (HERE, KIT):
    if p not in sys.path:
        sys.path.insert(0, p)

import handkit as hk  # noqa: E402

CELL = 16.0
TILT = 0.5
LUMA = np.array([0.299, 0.587, 0.114])
# OK_REPLACE names the work folder on another machine than the owner's.
ROOT = os.environ.get("OK_REPLACE", "D:/OKReplace")
SPRITES = ROOT + "/sprites"
CATALOG = ROOT + "/catalog.json"
OUT = ROOT + "/hand/creon"

_catalog = None


def record(name):
    global _catalog
    if _catalog is None:
        _catalog = {r["name"]: r for r in json.load(open(CATALOG))}
    return _catalog[name]


class Sprite:
    """A feature's picture as numbers: mask, colours in its own sRGB,
    brightness, hotspot, footprint and height, rows top-down."""

    def __init__(self, name):
        row = record(name)
        self.name = name
        self.path = os.path.join(SPRITES, name + ".png")
        img = bpy.data.images.load(self.path, check_existing=True)
        w, h = img.size
        a = np.empty(w * h * 4, np.float32)
        img.pixels.foreach_get(a)
        a = a.reshape(h, w, 4)[::-1].copy()
        self.w, self.h = w, h
        self.mask = a[..., 3] > 0.5
        self.rgb = a[..., :3]
        self.lum = self.rgb @ LUMA
        self.hx, self.hy = row["sprite"]["hotspot"]
        self.footprint = row["footprint"]
        self.height = row["height"]

    def mean(self, sel=None):
        sel = self.mask if sel is None else (sel & self.mask)
        return self.rgb[sel].mean(0)


def srgb_to_lin(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


# ---------------------------------------------------------------- pieces

def label(mask):
    """8-connected pieces of a mask: (labels, count), 0 for none."""
    H, W = mask.shape
    lab = np.zeros((H, W), np.int32)
    n = 0
    for y in range(H):
        for x in range(W):
            if mask[y, x] and not lab[y, x]:
                n += 1
                lab[y, x] = n
                stack = [(y, x)]
                while stack:
                    cy, cx = stack.pop()
                    for dy in (-1, 0, 1):
                        for dx in (-1, 0, 1):
                            yy, xx = cy + dy, cx + dx
                            if 0 <= yy < H and 0 <= xx < W and mask[yy, xx] and not lab[yy, xx]:
                                lab[yy, xx] = n
                                stack.append((yy, xx))
    return lab, n


def runs(col):
    """(start, end) of each True run in a 1-D mask, end exclusive."""
    out, start = [], None
    for i, v in enumerate(col):
        if v and start is None:
            start = i
        elif not v and start is not None:
            out.append((start, i))
            start = None
    if start is not None:
        out.append((start, len(col)))
    return out


# ---------------------------------------------------------------- fields

class Field:
    """Heights on a ground grid of one cell per sprite column across and
    1/16 cell deep, each cell's piece beside it."""

    def __init__(self, spr, y_lo, y_hi):
        self.spr = spr
        self.y0 = math.floor(y_lo * CELL) / CELL
        self.rows = int(math.ceil((y_hi - self.y0) * CELL)) + 1
        self.h = np.zeros((self.rows, spr.w), np.float32)
        self.piece = np.zeros((self.rows, spr.w), np.int32)
        self.front = np.zeros((self.rows, spr.w), bool)

    def fill(self, c, ya, yb, height, piece):
        ja = int(round((ya - self.y0) * CELL))
        jb = int(round((yb - self.y0) * CELL))
        ja, jb = max(ja, 0), min(max(jb, ja + 1), self.rows)
        self.h[ja:jb, c] = np.maximum(self.h[ja:jb, c], height)
        self.piece[ja:jb, c] = piece


def column_base(spr, bottom_row):
    """The ground y under a column whose lowest opaque row is bottom_row."""
    return (spr.hy - bottom_row - 1) / CELL


def dome(spr, lab, pieces, spec):
    """Rocks: each column's span across a dome over its depth, whose top is
    8 sqrt(D^2 + H^2) above the front edge; H over D from the feature's
    height at its tallest column, or spec 'ratio'."""
    todo = []
    y_lo, y_hi = 1e9, -1e9
    spans = []
    for c in range(spr.w):
        for a, b in runs(lab[:, c] > 0):
            p = int(np.bincount(lab[a:b, c][lab[a:b, c] > 0]).argmax())
            if p in pieces:
                spans.append((c, a, b, p))
    if not spans:
        return None
    ratio = spec.get("ratio")
    if ratio is None:
        smax = max(b - a for _, a, b, _ in spans)
        want = spec.get("height", spr.height / CELL) * spec.get("sturdy_h", 0.85)
        # 8 D (1 + sqrt(1 + r^2)) = smax, r D = want
        lo, hi = 0.2, 3.0
        for _ in range(40):
            r = (lo + hi) / 2
            d = smax / (8 * (1 + math.sqrt(1 + r * r)))
            lo, hi = (r, hi) if r * d < want else (lo, r)
        ratio = (lo + hi) / 2
    for c, a, b, p in spans:
        s = b - a
        d = s / (8 * (1 + math.sqrt(1 + ratio * ratio)))
        y = column_base(spr, b - 1)
        todo.append((c, y, d, ratio * d, p))
        y_lo, y_hi = min(y_lo, y), max(y_hi, y + d)
    f = Field(spr, y_lo, y_hi)
    for c, y, d, h, p in todo:
        n = max(1, int(round(d * CELL)))
        for k in range(n):
            t = (k + 0.5) / n * 2 - 1
            f.fill(c, y + d * k / n, y + d * (k + 1) / n, h * math.sqrt(max(0.0, 1 - t * t)) ** spec.get("flat", 1.0), p)
    return f


def _blur_x(a, s):
    """Gaussian blur along the rows' axis 1 alone, with wrap-around."""
    n = a.shape[1]
    g = np.exp(-2 * (math.pi * np.fft.fftfreq(n) * s) ** 2)
    return np.real(np.fft.ifft(np.fft.fft(a, axis=1) * g[None, :], axis=1))


def smooth_field(f, across=7, deep=3, sigma=1.0, depth_blur=True):
    """Evens each piece's heights over its neighbours, a median over across
    columns by deep rows then a light blur, so a stone's top is one surface
    rather than a comb of columns read a pixel apart. The plan stays."""
    from numpy.lib.stride_tricks import sliding_window_view
    h = f.h.astype(np.float64)
    out = h.copy()
    ry, rx = deep // 2, across // 2
    for p in np.unique(f.piece[f.h > 0]):
        sel = (f.piece == p) & (f.h > 0)
        a = np.where(sel, h, np.nan)
        pad = np.pad(a, ((ry, ry), (rx, rx)), constant_values=np.nan)
        win = sliding_window_view(pad, (deep, across))
        med = np.nanmedian(win.reshape(*a.shape, -1)[sel], axis=1)
        out[sel] = med
    if sigma > 0:
        filled = f.h > 0
        # across the columns only, unless depth_blur
        w = blur(filled.astype(np.float64), sigma) if depth_blur else _blur_x(filled.astype(np.float64), sigma)
        s = blur(np.where(filled, out, 0.0), sigma) if depth_blur else _blur_x(np.where(filled, out, 0.0), sigma)
        # blur only within each piece's filled cells, weighting by the filled share
        out = np.where(filled, np.where(w > 1e-3, s / np.maximum(w, 1e-3), out), 0.0)
    f.h = out.astype(np.float32)


# ---------------------------------------------------------------- meshes

def field_mesh(f, name, bottom=False):
    """A closed-topped mesh over the filled cells, corner heights the mean
    of the cells they touch, walls down to the ground round the edge."""
    spr = f.spr
    R, C = f.h.shape
    filled = f.h > 1e-4
    xs = (np.arange(C + 1) - spr.hx) / CELL
    ys = f.y0 + np.arange(R + 1) / CELL
    tot = np.zeros((R + 1, C + 1))
    cnt = np.zeros((R + 1, C + 1))
    for dj in (0, 1):
        for dc in (0, 1):
            tot[dj:R + dj, dc:C + dc] += np.where(filled, f.h, 0)
            cnt[dj:R + dj, dc:C + dc] += filled
    zc = np.where(cnt > 0, tot / np.maximum(cnt, 1), 0.0)
    bm = bmesh.new()
    top, ground = {}, {}

    def T(j, c):
        k = (j, c)
        if k not in top:
            top[k] = bm.verts.new((xs[c], ys[j], zc[j, c]))
        return top[k]

    def G(j, c):
        k = (j, c)
        if k not in ground:
            ground[k] = bm.verts.new((xs[c], ys[j], 0.0))
        return ground[k]

    for j in range(R):
        for c in range(C):
            if not filled[j, c]:
                continue
            bm.faces.new((T(j, c), T(j, c + 1), T(j + 1, c + 1), T(j + 1, c)))
            if bottom:
                bm.faces.new((G(j, c), G(j + 1, c), G(j + 1, c + 1), G(j, c + 1)))
            if j == 0 or not filled[j - 1, c]:
                bm.faces.new((G(j, c), G(j, c + 1), T(j, c + 1), T(j, c)))
            if j == R - 1 or not filled[j + 1, c]:
                bm.faces.new((G(j + 1, c + 1), G(j + 1, c), T(j + 1, c), T(j + 1, c + 1)))
            if c == 0 or not filled[j, c - 1]:
                bm.faces.new((G(j + 1, c), G(j, c), T(j, c), T(j + 1, c)))
            if c == C - 1 or not filled[j, c + 1]:
                bm.faces.new((G(j, c + 1), G(j + 1, c + 1), T(j + 1, c + 1), T(j, c + 1)))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def roughen(ob, amp, freq, seed=0, keep_ground=True, fade=0.0):
    """Moves every vertex a little by smooth noise, the ground ring kept on
    the ground; with fade the push grows from nothing at the ground to amp
    at that height, so a stone's foot stays where it was drawn."""
    off = Vector((seed * 7.31, seed * 3.17, seed * 1.93))
    for v in ob.data.vertices:
        d = noise.noise_vector(v.co * freq + off)
        if keep_ground and v.co.z < 1e-4:
            v.co.x += d.x * amp
            v.co.y += d.y * amp
            continue
        k = min(1.0, v.co.z / fade) if fade > 0 else 1.0
        v.co += d * amp * k
        v.co.z = max(v.co.z, 0.0)


def modifier(ob, kind, **kw):
    m = ob.modifiers.new(kind.lower(), kind)
    for k, v in kw.items():
        setattr(m, k, v)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.modifier_apply(modifier=m.name)


def decimate(ob, tris):
    """Collapses the mesh to about tris triangles."""
    n = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if n > tris:
        modifier(ob, "DECIMATE", ratio=tris / n, use_collapse_triangulate=True)


def shade(ob, angle):
    for p in ob.data.polygons:
        p.use_smooth = True
    try:
        ob.data.set_sharp_from_angle(angle=math.radians(angle))
    except AttributeError:
        pass


# ---------------------------------------------------------------- textures

def blur(a, s):
    """Gaussian blur with wrap-around, sigma s pixels, over the first two axes."""
    if s <= 0:
        return a
    out = a
    for axis in (0, 1):
        n = out.shape[axis]
        g = np.exp(-2 * (math.pi * np.fft.fftfreq(n) * s) ** 2)
        shape = [1] * out.ndim
        shape[axis] = n
        out = np.real(np.fft.ifft(np.fft.fft(out, axis=axis) * g.reshape(shape), axis=axis))
    return out


def norm01(a):
    lo, hi = float(a.min()), float(a.max())
    return (a - lo) / max(hi - lo, 1e-9)


def fbm(rng, size, scales):
    """Tileable noise: blurred white noise at each (sigma, weight)."""
    out = np.zeros((size, size))
    for s, w in scales:
        out += w * norm01(blur(rng.standard_normal((size, size)), s))
    return norm01(out)


def tex_stone(kind, seed, size=256):
    """A grey tile in 0-1 the vertex colour tints. 'basalt': dark grain,
    small gas holes and faint pale mottling; 'tuff': soft layered grain
    with coarse pale grit."""
    rng = np.random.default_rng(seed)
    if kind == "basalt":
        base = 0.55 + 0.25 * fbm(rng, size, [(2.0, 0.5), (6.0, 0.3), (18.0, 0.4)])
        # pale lichen and ash over the dark stone: patches, and flecks in them
        mottle = fbm(rng, size, [(16.0, 1.0), (6.0, 0.4)])
        base += 0.35 * np.clip((mottle - 0.55) * 3.0, 0, 1)
        flecks = blur((rng.random((size, size)) > 0.975).astype(float), 2.0)
        base += 0.45 * norm01(flecks) * np.clip((mottle - 0.3) * 2.0, 0, 1)
        holes = blur((rng.random((size, size)) > 0.996).astype(float), 1.5)
        base -= 0.4 * norm01(holes)
    else:
        yy = np.arange(size)[:, None] / size
        layers = 0.5 + 0.5 * np.sin(2 * math.pi * (yy * 5 + 0.15 * fbm(rng, size, [(20.0, 1.0)])))
        base = 0.74 + 0.12 * layers + 0.16 * fbm(rng, size, [(1.0, 0.5), (2.5, 0.4), (8.0, 0.4)])
        grit = blur((rng.random((size, size)) > 0.985).astype(float), 0.6)
        base += 0.25 * norm01(grit)
    g = np.clip(base, 0.3, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def spiral_mask(size, kind, turns=3.2, width=0.09):
    """A carved groove as a 0-1 depth over a square tile: a round spiral, a
    square meander or concentric rings, as the drawn stones carry."""
    u = (np.arange(size) + 0.5) / size * 2 - 1
    X, Y = np.meshgrid(u, u)
    if kind == "round":
        r = np.hypot(X, Y)
        th = np.arctan2(Y, X) / (2 * math.pi) % 1.0
        # r grows by 1/turns each loop: distance to the nearest loop
        k = (r * turns - th)
        d = np.abs(k - np.round(k)) / turns
        g = np.clip(1 - d / (width / 2), 0, 1) * (r < 0.9)
    elif kind == "square":
        # t runs once round each square ring: right, top, left, bottom side
        r = np.maximum(np.maximum(np.abs(X), np.abs(Y)), 1e-6)
        right, top, left = X >= np.abs(Y), Y >= np.abs(X), -X >= np.abs(Y)
        t = np.where(right, 0.125 * (1 + Y / r),
                     np.where(top, 0.25 + 0.125 * (1 - X / r),
                              np.where(left, 0.5 + 0.125 * (1 - Y / r), 0.75 + 0.125 * (1 + X / r))))
        k = (r * turns - t)
        d = np.abs(k - np.round(k)) / turns
        g = np.clip(1 - d / (width / 2), 0, 1) * (r < 0.9)
    else:
        r = np.hypot(X, Y)
        k = r * turns
        d = np.abs(k - np.round(k)) / turns
        g = np.clip(1 - d / (width / 2), 0, 1) * (r < 0.9) * (r > 0.12)
    return g


def tex_carved(seed, size=512):
    """The ring's carving: a 2 by 2 grid of motifs (round spirals, square
    meanders, rings) cut into tuff, the grooves dark with a lit lip toward
    the light (north-west) and a shadowed one away from it."""
    rng = np.random.default_rng(seed)
    stone = tex_stone("tuff", seed, size)[..., 0]
    depth = np.zeros((size, size))
    half = size // 2
    kinds = ["round", "square", "square", "rings"]
    rng.shuffle(kinds)
    for i in range(2):
        for j in range(2):
            m = spiral_mask(half, kinds[2 * i + j], turns=rng.uniform(2.6, 3.6))
            depth[i * half:(i + 1) * half, j * half:(j + 1) * half] = m
    depth = blur(depth, 1.2)
    # emboss: light from up-left in the tile
    gy, gx = np.gradient(depth)
    lip = np.clip(-(gx + gy) * 6, -0.5, 0.5)
    g = np.clip(stone * (1 - 0.75 * norm01(depth)) + 0.5 * lip, 0.12, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def image(name, arr):
    """A packed Blender image from an (h, w, 4) array, rows top-down, marked
    as made here (okpaint.generated)."""
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(arr[::-1], np.float32).ravel())
    img.pack()
    img.colorspace_settings.name = "sRGB"
    img["okGenerated"] = True
    return img


def material(name, tex=None, rough=0.9, emit=None, strength=0.0):
    """Vertex colour times a repeating texture; emit an (r, g, b) glow."""
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
        t.extension = "REPEAT"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs[0].default_value = 1.0
        nt.links.new(t.outputs["Color"], mix.inputs[6])
        nt.links.new(vc.outputs["Color"], mix.inputs[7])
        nt.links.new(mix.outputs[2], b.inputs["Base Color"])
    else:
        nt.links.new(vc.outputs["Color"], b.inputs["Base Color"])
    if emit is not None:
        b.inputs["Emission Color"].default_value = (*emit, 1.0)
        b.inputs["Emission Strength"].default_value = strength
    return m


def box_uv(ob, scale):
    """UVs by box projection in cells times scale: tops take x, y, faces
    turned north or south x, z, east or west y, z."""
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
            uv.data[li].uv = (u * scale, v * scale)


def paint_points(ob, colour_of):
    """A point colour attribute 'Col' from colour_of(co, normal) -> linear rgb."""
    me = ob.data
    ca = me.color_attributes.get("Col") or me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    for i, v in enumerate(me.vertices):
        r, g, b = colour_of(v.co, v.normal)
        ca.data[i].color = (min(r, 1.0), min(g, 1.0), min(b, 1.0), 1.0)


def scale_colours(ob, f):
    ca = ob.data.color_attributes.get("Col")
    c = np.empty(len(ca.data) * 4, np.float32)
    ca.data.foreach_get("color", c)
    c = c.reshape(-1, 4)
    c[:, :3] = np.clip(c[:, :3] * np.asarray(f, np.float32), 0, 1)
    ca.data.foreach_set("color", c.ravel())


# ---------------------------------------------------------------- renders

def classic(spr, path, scale=1, samples=16):
    """The classic view alone, framed as handkit.renders frames it."""
    scene, cam = hk._stage(samples)
    P = CELL * scale
    e = math.atan(1.0 / TILT)
    d = Vector((0.0, math.cos(e), -math.sin(e)))
    up = Vector((0.0, math.sin(e), math.cos(e)))
    W, H = spr.w * scale, spr.h * scale
    C = -Vector((1.0, 0.0, 0.0)) * ((spr.hx * scale - W / 2.0) / P) - up * ((H / 2.0 - spr.hy * scale) / P)
    scene.render.resolution_x, scene.render.resolution_y = int(W), int(H)
    cam.data.ortho_scale = max(W, H) / P
    cam.location = C - d * 60
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    scene.cycles.samples = 32


def fit(spr, path):
    """How the classic render at path covers the drawing: mean sRGB of the
    render's own opaque pixels, the drawing's share it covers, its own share
    outside the drawing, and the two outlines' intersection over union."""
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    a = a.reshape(h, w, 4)[::-1]
    k = w // spr.w
    a = a[:spr.h * k, :spr.w * k].reshape(spr.h, k, spr.w, k, 4).mean((1, 3))
    own = a[..., 3] > 0.5
    if own.sum() < 5:
        return None
    rgb = (a[..., :3][own] / a[..., 3][own][:, None]).mean(0)
    both = (own & spr.mask).sum()
    return {"render": rgb, "cover": both / spr.mask.sum(), "spill": (own & ~spr.mask).sum() / spr.mask.sum(),
            "iou": both / (own | spr.mask).sum()}


def calibrate(ob, spr, target, work):
    """Scales the vertex colours so the classic render's mean colour meets
    target (sRGB). Returns the fit before."""
    os.makedirs(work, exist_ok=True)
    path = os.path.join(work, spr.name + "_cal.png")
    classic(spr, path)
    m = fit(spr, path)
    if m is None:
        return None
    ratio = np.clip(np.asarray(target) / np.maximum(m["render"], 1e-3), 0.3, 3.0)
    scale_colours(ob, ratio ** 2.2)
    return m
