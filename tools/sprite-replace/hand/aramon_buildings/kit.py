"""Parametric builders for the Aramon buildings: timber and stone houses,
thatched cottages, halls with wings and towers, granaries, sheds,
haystacks, and the ruins broken out of them. Run inside Blender.

Frame as handkit: one unit is one map cell, -Y toward the classic camera,
Z up, the origin at the feature's anchor. The classic view draws a point
at column hx + 16x and row hy - 16y - 8z, so the ground reads as a plan
and a height lifts a point half its size up the picture. px(col, row, z)
turns a sprite pixel back into a plan point at a height.

Parts gather per material in a Builder; each material is a flat colour
or a small texture generated here from colours sampled off the sprite
(roof tiles, thatch, planks, masonry, rubble), mapped per face with rows
running level so tiles and thatch lie the way a roof is laid.
"""
import json
import math
import os
import random
import sys
from contextlib import contextmanager

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(os.path.dirname(HERE))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)
import handkit as hk  # noqa: E402

DATA = "D:/OKReplace"
OUT = DATA + "/hand/aramon_buildings"
CAT = {r["name"]: r for r in json.load(open(DATA + "/catalog.json"))}
VIEW = Vector((0.0, 1.0, -2.0)).normalized()  # the classic camera's look

# ---------------------------------------------------------------- colour

# handkit's render of an up-facing face under its sun and sky (AgX):
# albedo to the sRGB value it comes out at, measured on flat plates
_HK = [(0.0, 0), (0.01, 34), (0.02, 42), (0.035, 55), (0.05, 63), (0.07, 75), (0.1, 89),
       (0.14, 104), (0.2, 123), (0.3, 143), (0.45, 163), (0.6, 175), (0.8, 186), (1.0, 194)]
# the game lights flat ground at 1.24 and lifts it a tenth of a stop
GAME_GAIN = 1.33


def _lin(v):
    v = v / 255.0
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def _srgb(v):
    v = min(1.0, max(0.0, v))
    return v * 12.92 if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055


def rgb(c):
    """'#rrggbb' or (r, g, b) as drawn, 0-255."""
    if isinstance(c, str):
        c = c.lstrip("#")
        return tuple(int(c[i:i + 2], 16) for i in (0, 2, 4))
    return tuple(c)


def albedo(c):
    """The lit colour a surface needs to come out near its drawn colour:
    between what handkit's render needs and what the game's light needs."""
    out = []
    for v in rgb(c):
        vs, al = [p[1] for p in _HK], [p[0] for p in _HK]
        a_hk = float(np.interp(v, vs, al)) if v < 194 else 1.0
        a_game = _lin(v) / GAME_GAIN
        out.append(min(1.0, math.sqrt(max(a_hk, 1e-5) * max(a_game, 1e-5))))
    return tuple(out)


def shade(c, k):
    r, g, b = rgb(c)
    return (min(255, r * k), min(255, g * k), min(255, b * k))


def mix(a, b, t):
    a, b = rgb(a), rgb(b)
    return tuple(a[i] * (1 - t) + b[i] * t for i in range(3))


# ---------------------------------------------------------------- textures

def _smooth(rng, size, cells):
    """Tileable value noise in 0..1, cells blobs across."""
    g = rng.random((cells, cells))
    t = np.arange(size) * cells / size
    i0 = np.floor(t).astype(int) % cells
    i1 = (i0 + 1) % cells
    f = t - np.floor(t)
    f = f * f * (3 - 2 * f)
    rows = g[i0] * (1 - f)[:, None] + g[i1] * f[:, None]
    return rows[:, i0] * (1 - f)[None, :] + rows[:, i1] * f[None, :]


def _to_image(name, disp):
    """disp: (h, w, 3) drawn colours 0-255, row 0 at the bottom (Blender's
    order); stored as the albedo each needs, sRGB encoded."""
    h, w = disp.shape[:2]
    flat = disp.reshape(-1, 3)
    vs = np.array([p[1] for p in _HK], float)
    al = np.array([p[0] for p in _HK], float)
    a_hk = np.where(flat < 194, np.interp(flat, vs, al), 1.0)
    v = flat / 255.0
    lin = np.where(v <= 0.04045, v / 12.92, ((v + 0.055) / 1.055) ** 2.4)
    a = np.sqrt(np.maximum(a_hk, 1e-5) * np.maximum(lin / GAME_GAIN, 1e-5))
    a = np.clip(a, 0, 1)
    s = np.where(a <= 0.0031308, a * 12.92, 1.055 * a ** (1 / 2.4) - 0.055)
    img = bpy.data.images.new(name, w, h, alpha=False)
    px = np.ones((h * w, 4), np.float32)
    px[:, :3] = s
    img.pixels[:] = px.ravel()
    img.pack()
    # made here from noise and sampled colours, so it ships (okpaint.py)
    return hk.generated(img)


def tex_tiles(name, base, var=0.14, rows=8, cols=6, gap=None, moss=None, moss_amt=0.0, seed=1,
              size=128, lip=0.25, grain=0.06):
    """Roof tiles or shingles in staggered courses: each course overlaps the
    one below, so a tile is dark under the course above and a dark line
    runs along its lower edge."""
    rng = np.random.default_rng(seed)
    base = np.array(rgb(base), float)
    gap = np.array(rgb(gap), float) if gap is not None else base * 0.45
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    rh, cw = size / rows, size / cols
    r = np.floor(vv / rh).astype(int)
    off = (r % 2) * cw * 0.5 + (r % 3) * cw * 0.17
    cu = (uu + off) / cw
    c = np.floor(cu).astype(int) % cols
    fu = cu - np.floor(cu)
    fv = vv / rh - r  # 0 at the course's lower edge (v runs up the slope)
    tshade = 1 + rng.normal(0, var, (rows, cols))
    tint = rng.normal(0, var * 0.35, (rows, cols, 3))
    k = tshade[r, c][..., None] * (1 + tint[r, c])
    # darker up under the course above, lighter at the exposed lip
    k = k * (1.08 - 0.28 * np.clip((fv - lip) / (1 - lip), 0, 1))[..., None]
    k = k * (1 + grain * (rng.random((size, size)) - 0.5))[..., None]
    col = base[None, None] * k
    line = (fv < 0.12) | (fu < 0.05)
    col[line] = col[line] * 0.4 + gap * 0.6 * (fv[line][:, None] * 0 + 1)
    if moss is not None and moss_amt > 0:
        n = _smooth(rng, size, 6) * 0.6 + _smooth(rng, size, 17) * 0.4
        m = np.clip((n - (1 - moss_amt)) / 0.12, 0, 1)[..., None]
        col = col * (1 - m) + np.array(rgb(moss), float) * m * (0.8 + 0.4 * rng.random((size, size, 1)))
    return _to_image(name, np.clip(col, 0, 255))


def tex_thatch(name, base, dark=None, light=None, seed=2, size=128, bands=4, speck=None, speck_amt=0.015):
    """Thatch: straw fibres running down the slope in layers."""
    rng = np.random.default_rng(seed)
    base = np.array(rgb(base), float)
    dark = np.array(rgb(dark), float) if dark is not None else base * 0.55
    light = np.array(rgb(light), float) if light is not None else np.minimum(255, base * 1.3)
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    # fibres: a few pixels wide, each a run down the slope of random length
    cols = size // 3
    f = np.zeros((size, cols))
    for c in range(cols):
        v = 0
        while v < size:
            n = int(rng.integers(size // 6, size // 2))
            f[v:v + n, c] = rng.random()
            v += n
    f = np.repeat(f, 3, axis=1)
    f = np.concatenate([f, f[:, :size - f.shape[1]]], axis=1)[:, :size]
    t = np.clip(0.15 + f * 0.85 + (_smooth(rng, size, 5) - 0.5) * 0.5, 0, 1)
    band = ((vv / (size / bands)) % 1.0)
    t = t * (0.88 + 0.12 * band)  # each layer darker where the next one laps it
    col = dark[None, None] * (1 - t[..., None]) + light[None, None] * t[..., None]
    col = col * 0.5 + base[None, None] * 0.5 * (0.8 + 0.4 * t[..., None])
    if speck is not None:
        s = rng.random((size, size)) > 1.0 - speck_amt
        col[s] = np.array(rgb(speck), float)
    return _to_image(name, np.clip(col, 0, 255))


def tex_straw(name, dark, mid, light, speck=None, speck_amt=0.08, seed=2, size=128, gaps=0.25):
    """Loose straw, as haystacks are drawn: bright stalks down the slope
    with dark gaps between them and orange ends."""
    rng = np.random.default_rng(seed)
    dark, mid, light = (np.array(rgb(c), float) for c in (dark, mid, light))
    cols = size // 2
    f = np.zeros((size, cols))
    for c in range(cols):
        v = int(rng.integers(0, size))
        while v < 2 * size:
            n = int(rng.integers(size // 8, size // 3))
            val = 0.0 if rng.random() < gaps else rng.random()
            f[np.arange(v, v + n) % size, c] = val
            v += n
    f = np.repeat(f, 2, axis=1)[:, :size]
    t = f[..., None]
    col = np.where(t < 0.5, dark + (mid - dark) * (t * 2), mid + (light - mid) * (t * 2 - 1))
    col = col * (0.9 + 0.2 * _smooth(rng, size, 4)[..., None])
    if speck is not None:
        s = (rng.random((size, size)) < speck_amt) & (f > 0.3)
        col[s] = np.array(rgb(speck), float)
    return _to_image(name, np.clip(col, 0, 255))


def tex_planks(name, base, var=0.12, boards=6, seed=3, size=128, gap=None, vertical=False):
    """Boards with dark gaps and grain; they run level unless vertical."""
    rng = np.random.default_rng(seed)
    base = np.array(rgb(base), float)
    gap = np.array(rgb(gap), float) if gap is not None else base * 0.35
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    a, b = (uu, vv) if vertical else (vv, uu)
    bw = size / boards
    i = np.floor(a / bw).astype(int)
    fa = a / bw - i
    sh = 1 + rng.normal(0, var, boards)
    grain = np.sin(b / size * 2 * np.pi * rng.integers(3, 7, boards)[i] + fa * 5) * 0.05
    k = sh[i] * (1 + grain) * (1 + 0.06 * (rng.random((size, size)) - 0.5))
    col = base[None, None] * k[..., None]
    joint = (fa < 0.08)
    # butt joints along each board
    seg = np.floor((b + rng.integers(0, size, boards)[i]) / (size / 2)).astype(int)
    bj = np.abs(((b + rng.integers(0, size, boards)[i]) % (size / 2))) < 1.2
    col[joint | bj] = gap
    return _to_image(name, np.clip(col, 0, 255))


def tex_stone(name, base, var=0.16, rows=5, seed=4, size=128, mortar=None, moss=None, moss_amt=0.0):
    """Coursed rubble masonry: blocks of random length in level courses."""
    rng = np.random.default_rng(seed)
    base = np.array(rgb(base), float)
    mortar = np.array(rgb(mortar), float) if mortar is not None else base * 0.55
    col = np.zeros((size, size, 3))
    rh = size / rows
    for r in range(rows):
        y0, y1 = int(r * rh), int((r + 1) * rh)
        x = int(rng.integers(0, size))
        start = x
        while x < start + size:
            w = int(rng.integers(int(rh * 0.9), int(rh * 2.2)))
            k = 1 + rng.normal(0, var)
            t = rng.normal(0, var * 0.1, 3)
            xs = np.arange(x, x + w) % size
            col[y0:y1, xs] = base * k * (1 + t)
            col[y0:y1, xs[:2]] = mortar
            x += w
        col[y0:y0 + 2, :] = mortar
    col *= (1 + 0.12 * (rng.random((size, size)) - 0.5))[..., None]
    if moss is not None and moss_amt > 0:
        n = _smooth(rng, size, 5)
        m = np.clip((n - (1 - moss_amt)) / 0.15, 0, 1)[..., None]
        col = col * (1 - m) + np.array(rgb(moss), float) * m
    return _to_image(name, np.clip(col, 0, 255))


def tex_mottle(name, base, var=0.12, seed=5, size=64, cells=(4, 11), speck=None, speck_amt=0.0):
    """Plaster, earth or stone with soft blotches and a little grit."""
    rng = np.random.default_rng(seed)
    base = np.array(rgb(base), float)
    n = sum(_smooth(rng, size, c) for c in cells) / len(cells)
    k = 1 + (n - 0.5) * 2 * var + (rng.random((size, size)) - 0.5) * var * 0.6
    col = base[None, None] * k[..., None]
    if speck is not None and speck_amt > 0:
        s = rng.random((size, size)) < speck_amt
        col[s] = np.array(rgb(speck), float) * (0.8 + 0.4 * rng.random((int(s.sum()), 1)))
    return _to_image(name, np.clip(col, 0, 255))


def tex_rubble(name, base, light=None, dark=None, seed=6, size=128):
    """Broken plaster, stone and charred wood: speckled dark grey with pale
    chips, as the ruins are drawn."""
    rng = np.random.default_rng(seed)
    base = np.array(rgb(base), float)
    light = np.array(rgb(light), float) if light is not None else np.minimum(255, base * 2.2)
    dark = np.array(rgb(dark), float) if dark is not None else base * 0.4
    n = _smooth(rng, size, 9) * 0.5 + _smooth(rng, size, 23) * 0.5
    col = base[None, None] * (0.7 + 0.6 * n[..., None])
    r = rng.random((size, size))
    col[r < 0.12] = dark
    chip = r > 0.9
    col[chip] = light * (0.75 + 0.25 * rng.random((int(chip.sum()), 1)))
    # a few larger pale chunks
    for _ in range(30):
        x, y, w, h = rng.integers(0, size), rng.integers(0, size), rng.integers(2, 6), rng.integers(2, 5)
        k = 0.55 + 0.45 * rng.random()
        col[np.arange(y, y + h)[:, None] % size, np.arange(x, x + w)[None, :] % size] = light * k
    return _to_image(name, np.clip(col, 0, 255))


# ---------------------------------------------------------------- materials

class Mat:
    """A material and the world size its texture covers, in cells; ref is
    the drawn colour it stands for, for matching pieces to the sprite;
    uvfn names a plan mapping in UVFN (a roof map) instead of per-face UVs."""

    def __init__(self, name, colour=None, tex=None, uv=2.0, rough=0.9, both=False, metal=0.0, emit=None,
                 strength=0.0, ref=None, uvfn=None, glow=0.0):
        self.name, self.uv, self.uvfn = name, uv, uvfn
        self.ref = rgb(ref) if ref is not None else (rgb(colour) if colour is not None else None)
        c = albedo(colour) if colour is not None else (1.0, 1.0, 1.0)
        self.m = hk.pbr(name, c, rough=rough, metal=metal, emit=emit, strength=strength)
        # one-sided unless it is a card, so the game does not draw it twice
        self.m.use_backface_culling = not both
        if tex is not None:
            nt = self.m.node_tree
            t = nt.nodes.new("ShaderNodeTexImage")
            t.image = tex
            t.interpolation = "Linear"
            nt.links.new(t.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
            if glow:
                # embers: the texture's bright specks glow, its dark parts do not
                nt.links.new(t.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Emission Color"])
                nt.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = glow
        self.textured = tex is not None


# ---------------------------------------------------------------- geometry

def px(r, col, row, z=0.0):
    """The plan point at height z that the sprite of feature r draws at
    (col, row)."""
    hx, hy = r["sprite"]["hotspot"]
    return ((col - hx) / 16.0, (hy - row - 8.0 * z) / 16.0)


def screen(r, x, y, z):
    hx, hy = r["sprite"]["hotspot"]
    return hx + 16.0 * x, hy - 16.0 * y - 8.0 * z


def lay_uv(bm, size):
    """Each face's texture laid on its own plane, rows level: along the
    face's contour and up its slope, size cells to a repeat."""
    uvl = bm.loops.layers.uv.verify()
    s = 1.0 / size
    up = Vector((0, 0, 1))
    for f in bm.faces:
        n = f.normal
        if n.length < 1e-6:
            continue
        if abs(n.z) > 0.97:
            t, b = Vector((1, 0, 0)), Vector((0, 1, 0))
        else:
            t = up.cross(n).normalized()
            b = n.cross(t).normalized()
        for lp in f.loops:
            p = lp.vert.co
            lp[uvl].uv = (p.dot(t) * s, p.dot(b) * s)


UVFN = {}  # name: (x, y) -> (u, v), plan mappings for roof maps


def plan_uv(bm, fn):
    """UVs from a plan mapping of world x, y (a roof map seen from above)."""
    uvl = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for lp in f.loops:
            lp[uvl].uv = fn(lp.vert.co.x, lp.vert.co.y)


def relay_uv(ob):
    """Lay the texture again after a cut has made new faces."""
    if "uv" not in ob and "uvfn" not in ob:
        return
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    if ob.get("uvfn") in UVFN:
        plan_uv(bm, UVFN[ob["uvfn"]])
    else:
        lay_uv(bm, ob["uv"])
    bm.to_mesh(ob.data)
    bm.free()


class Builder:
    """Geometry gathered per material, under a stack of placements."""

    def __init__(self, seed=0):
        self.bms = {}
        self.mats = {}
        self.M = Matrix.Identity(4)
        self._stack = []
        self.rng = random.Random(seed)
        self.bevels = {}  # material name: (width, segments), rounded after

    # placement
    @contextmanager
    def at(self, x=0.0, y=0.0, z=0.0, yaw=0.0, pitch=0.0, roll=0.0):
        """Everything built inside is placed at (x, y, z) turned yaw degrees
        anticlockwise seen from above (then pitched about x, rolled about y)."""
        self._stack.append(self.M)
        self.M = self.M @ Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(yaw), 4, "Z") \
            @ Matrix.Rotation(math.radians(pitch), 4, "X") @ Matrix.Rotation(math.radians(roll), 4, "Y")
        try:
            yield
        finally:
            self.M = self._stack.pop()

    def _bm(self, mat):
        if mat.name not in self.bms:
            self.bms[mat.name] = bmesh.new()
            self.mats[mat.name] = mat
        return self.bms[mat.name]

    def mesh(self, mat, verts, faces, local=None):
        """Raw polygons; verts in the current placement."""
        bm = self._bm(mat)
        M = self.M @ (local or Matrix.Identity(4))
        vs = [bm.verts.new(M @ Vector(v)) for v in verts]
        out = []
        for f in faces:
            try:
                out.append(bm.faces.new([vs[i] for i in f]))
            except ValueError:
                pass
        return out

    def solid(self, mat, verts, faces, local=None):
        """A closed solid: its faces turned outward whatever their order."""
        fs = self.mesh(mat, verts, faces, local)
        bmesh.ops.recalc_face_normals(self._bm(mat), faces=fs)
        return fs

    def box(self, mat, x, y, z, sx, sy, sz, yaw=0.0, pitch=0.0, roll=0.0):
        """A box standing on z centred on (x, y), turned about its own base."""
        v = [(-.5, -.5, 0), (.5, -.5, 0), (.5, .5, 0), (-.5, .5, 0),
             (-.5, -.5, 1), (.5, -.5, 1), (.5, .5, 1), (-.5, .5, 1)]
        v = [(a * sx, b * sy, c * sz) for a, b, c in v]
        L = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(yaw), 4, "Z") \
            @ Matrix.Rotation(math.radians(pitch), 4, "X") @ Matrix.Rotation(math.radians(roll), 4, "Y")
        return self.solid(mat, v, [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], L)

    def beam(self, mat, a, b, w, h=None, twist=0.0):
        """A square timber from point a to point b (current placement)."""
        a, b = Vector(a), Vector(b)
        d = b - a
        L = d.length
        if L < 1e-6:
            return []
        h = w if h is None else h
        q = d.to_track_quat("Z", "Y" if abs(d.normalized().z) < 0.99 else "X")
        R = q.to_matrix().to_4x4() @ Matrix.Rotation(math.radians(twist), 4, "Z")
        v = [(-.5, -.5, 0), (.5, -.5, 0), (.5, .5, 0), (-.5, .5, 0),
             (-.5, -.5, 1), (.5, -.5, 1), (.5, .5, 1), (-.5, .5, 1)]
        v = [(p * w, q2 * h, r * L) for p, q2, r in v]
        return self.solid(mat, v, [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)],
                          Matrix.Translation(a) @ R)

    def prism(self, mat, pts, z0, z1):
        """A polygon in plan (anticlockwise or not) extruded from z0 to z1."""
        n = len(pts)
        v = [(x, y, z0) for x, y in pts] + [(x, y, z1) for x, y in pts]
        f = [tuple(range(n)), tuple(range(n, 2 * n))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
        return self.solid(mat, v, f)

    def slab(self, mat, pts, t):
        """A flat polygon in 3D (current placement) thickened by t along its normal."""
        P = [Vector(p) for p in pts]
        nrm = Vector((0, 0, 0))
        for i in range(len(P)):
            nrm += P[i].cross(P[(i + 1) % len(P)])
        nrm.normalize()
        n = len(P)
        v = [tuple(p) for p in P] + [tuple(p - nrm * t) for p in P]
        f = [tuple(range(n)), tuple(range(n, 2 * n))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
        return self.solid(mat, v, f)

    def cyl(self, mat, x, y, z, r, h, r_top=None, seg=12, cap=True, axis=None, rot=0.0):
        """A cylinder or cone standing on z at (x, y); with axis (a vector)
        it runs from (x, y, z) along axis for h instead."""
        r_top = r if r_top is None else r_top
        ring0 = [(r * math.cos(2 * math.pi * (i + rot) / seg), r * math.sin(2 * math.pi * (i + rot) / seg), 0) for i in range(seg)]
        if r_top > 1e-4:
            ring1 = [(r_top * math.cos(2 * math.pi * (i + rot) / seg), r_top * math.sin(2 * math.pi * (i + rot) / seg), h)
                     for i in range(seg)]
            v = ring0 + ring1
            f = [(i, (i + 1) % seg, seg + (i + 1) % seg, seg + i) for i in range(seg)]
            if cap:
                f += [tuple(range(seg)), tuple(range(seg, 2 * seg))]
        else:
            v = ring0 + [(0, 0, h)]
            f = [(i, (i + 1) % seg, seg) for i in range(seg)]
            if cap:
                f += [tuple(range(seg))]
        if axis is None:
            L = Matrix.Translation((x, y, z))
        else:
            ax = Vector(axis).normalized()
            L = Matrix.Translation((x, y, z)) @ ax.to_track_quat("Z", "Y" if abs(ax.z) < 0.99 else "X").to_matrix().to_4x4()
        return self.solid(mat, v, f, L)

    def lathe(self, mat, x, y, profile, seg=16, jitter=0.0, seed=0):
        """A solid of revolution: profile is (radius, z) from the bottom
        centre round to the top centre; jitter roughens the radius."""
        rng = random.Random(seed)
        v, rings = [], []
        for rr, z in profile:
            if rr <= 1e-5:
                rings.append([len(v)])
                v.append((0, 0, z))
            else:
                ring = []
                for i in range(seg):
                    a = 2 * math.pi * i / seg
                    k = 1 + jitter * (rng.random() - 0.5) * 2
                    ring.append(len(v))
                    v.append((rr * k * math.cos(a), rr * k * math.sin(a), z))
                rings.append(ring)
        f = []
        for a, b in zip(rings, rings[1:]):
            for i in range(seg):
                j = (i + 1) % seg
                q = [a[i % len(a)], a[j % len(a)], b[j % len(b)], b[i % len(b)]]
                q = [p for k, p in enumerate(q) if p not in q[:k]]
                if len(q) >= 3:
                    f.append(tuple(q))
        return self.solid(mat, v, f, Matrix.Translation((x, y, 0)))

    # ------------------------------------------------ assembly

    def objects(self):
        """One object per material, with each face's texture laid level."""
        obs = []
        for key, bm in self.bms.items():
            mat = self.mats[key]
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)

            def lay(bm_):
                if mat.uvfn in UVFN:
                    plan_uv(bm_, UVFN[mat.uvfn])
                else:
                    lay_uv(bm_, mat.uv)
            if mat.textured:
                lay(bm)
            me = bpy.data.meshes.new(key)
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(key, me)
            bpy.context.collection.objects.link(ob)
            me.materials.append(mat.m)
            if key in self.bevels:
                hk.bevel(ob, *self.bevels[key])
                if mat.textured:
                    bm = bmesh.new()
                    bm.from_mesh(ob.data)
                    lay(bm)
                    bm.to_mesh(ob.data)
                    bm.free()
            if mat.textured:
                ob["uv"] = mat.uv
                if mat.uvfn:
                    ob["uvfn"] = mat.uvfn
            obs.append(ob)
        self.bms = {}
        return obs


# ---------------------------------------------------------------- roofs

def roof(B, mat, L, W, z0, R, kind="gable", over=0.3, over_end=None, T=0.18, ridge=None, hip=None,
         skew=0.0, ends=None, half=0.5):
    """A roof over an L by W rectangle (L along local x) centred on the
    placement, its eaves at z0 and its ridge R higher, as a shell T thick.
    kind: gable, hip, pyramid, shed (high side north). hip is how far in
    from each end wall the ridge stops (default: as far as the sides, so
    all four slopes share a pitch); ridge overrides the ridge's half length.
    ends, (west, east), sets each end apart: gable, hip or half (a gable up
    to half the rise, a hip above). over is the overhang at the sides,
    over_end at the ends."""
    oe = over if over_end is None else over_end
    hx, hy = L / 2 + oe, W / 2 + over
    # the eave line drops with the overhang, at the roof's pitch
    k = R / (W / 2) if kind != "shed" else R / W
    ze = z0 - over * k
    Rt = R + over * k
    if kind == "shed":
        verts = [(-hx, -hy, ze), (hx, -hy, ze), (hx, hy, ze + Rt + over * k), (-hx, hy, ze + Rt + over * k)]
        faces = [(0, 1, 2, 3)]
        rim = [0, 1, 2, 3]
    elif kind == "pyramid" or (ridge is not None and ridge < 1e-4):
        zr = ze + Rt
        verts = [(-hx, -hy, ze), (hx, -hy, ze), (hx, hy, ze), (-hx, hy, ze), (0, skew, zr)]
        faces = [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)]
        rim = [0, 1, 2, 3]
    else:
        if ends is None:
            ends = ("gable", "gable") if kind == "gable" else ("hip", "hip")
        e_in = (W / 2) if hip is None else hip
        zr = ze + Rt
        zh = z0 + R * half
        yh = hy - (zh - ze) / k
        verts = []

        def V(p):
            verts.append(p)
            return len(verts) - 1
        c0, c1, c2, c3 = V((-hx, -hy, ze)), V((hx, -hy, ze)), V((hx, hy, ze)), V((-hx, hy, ze))
        rx = []
        for e, t in zip((-1, 1), ends):
            if t == "gable":
                rx.append(hx)
            elif ridge is not None:
                rx.append(ridge)
            else:
                rx.append(max(0.02, L / 2 - e_in))
        r0, r1 = V((-rx[0], skew, zr)), V((rx[1], skew, zr))
        clip = {}
        for e, t in zip((-1, 1), ends):
            if t == "half":
                clip[e] = (V((e * hx, -yh, zh)), V((e * hx, yh, zh)))
        front = [c0, c1] + ([clip[1][0]] if 1 in clip else []) + [r1, r0] + ([clip[-1][0]] if -1 in clip else [])
        back = [c2, c3] + ([clip[-1][1]] if -1 in clip else []) + [r0, r1] + ([clip[1][1]] if 1 in clip else [])
        faces = [tuple(front), tuple(back)]
        rim = [c0, c1]
        for e, t, rr, (fa, fb) in ((1, ends[1], r1, (c1, c2)), (-1, ends[0], r0, (c3, c0))):
            if t == "hip":
                faces.append((fa, fb, rr) if e > 0 else (fa, fb, rr))
            elif t == "half":
                lo, hi_ = clip[e] if e > 0 else (clip[e][1], clip[e][0])
                faces.append((lo, hi_, rr))
            if e > 0:
                if t == "gable":
                    rim += [r1]
                elif t == "half":
                    rim += [clip[1][0], clip[1][1]]
                rim += [c2, c3]
            else:
                if t == "gable":
                    rim += [r0]
                elif t == "half":
                    rim += [clip[-1][1], clip[-1][0]]
    # the shell: the same surface T lower, and a fascia round the rim
    n = len(verts)
    under = [(x, y, z - T) for x, y, z in verts]
    allv = verts + under
    fs = list(faces) + [tuple(n + i for i in reversed(f)) for f in faces]
    for i in range(len(rim)):
        a, b = rim[i], rim[(i + 1) % len(rim)]
        fs.append((a, b, n + b, n + a))
    B.solid(mat, allv, fs)
    return ze + Rt


def gable_wall(B, mat, x, W, z0, R, t=0.2, top=None):
    """The triangle of wall under a gable at local x, W wide, R tall; with
    top, cut level there (under a half hip)."""
    if top is None or top >= R:
        pts = [(-W / 2, z0), (W / 2, z0), (0, z0 + R)]
    else:
        a = W / 2 * (1 - top / R)
        pts = [(-W / 2, z0), (W / 2, z0), (a, z0 + top), (-a, z0 + top)]
    n = len(pts)
    v = [(x - t / 2, p[0], p[1]) for p in pts] + [(x + t / 2, p[0], p[1]) for p in pts]
    B.solid(mat, v, [tuple(range(n)), tuple(range(n, 2 * n))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)])


def ridge_cap(B, mat, x0, x1, z, w=0.22, y=0.0):
    """A capping piece along the ridge, a flat-topped wedge."""
    v = [(x0, y - w / 2, z - 0.05), (x1, y - w / 2, z - 0.05), (x1, y, z + 0.08), (x0, y, z + 0.08),
         (x0, y + w / 2, z - 0.05), (x1, y + w / 2, z - 0.05)]
    B.solid(mat, v, [(0, 1, 2, 3), (3, 2, 5, 4), (0, 3, 4), (1, 5, 2), (0, 4, 5, 1)])


# ---------------------------------------------------------------- walls

def framed_walls(B, wall, timber, L, W, H, t=0.14, bays=1.6, braces=True, sill=True, rail=None, sides="NSEW",
                 posts=True, xbrace=False):
    """Timbers proud of a wall box L by W by H centred on the placement:
    corner posts, posts every bays, a sill and a wall plate, a mid rail at
    rail (a fraction of H) and a brace in alternate bays."""
    if wall is not None:
        B.box(wall, 0, 0, 0, L, W, H)
    d = 0.04  # how far the timbers stand proud
    for side in sides:
        if side in "NS":
            n = L
            y = (W / 2 + d / 2) * (1 if side == "N" else -1)
            ax = "x"
        else:
            n = W
            y = (L / 2 + d / 2) * (1 if side == "E" else -1)
            ax = "y"

        def pt(s, z):
            return (s, y, z) if ax == "x" else (y, s, z)
        k = max(1, int(round(n / bays)))
        xs = [-n / 2 + t / 2 + i * (n - t) / k for i in range(k + 1)]
        th = t + d
        if posts:
            for s in xs:
                a, b = pt(s, 0), pt(s, H)
                B.beam(timber, a, b, t if ax == "x" else th, th if ax == "x" else t)
        if sill:
            B.beam(timber, pt(-n / 2, 0), pt(n / 2, 0), th if ax == "x" else t, t)
        B.beam(timber, pt(-n / 2, H - t), pt(n / 2, H - t), th if ax == "x" else t, t)
        if rail:
            B.beam(timber, pt(-n / 2, H * rail), pt(n / 2, H * rail), th if ax == "x" else t, t * 0.8)
        if xbrace:
            top = H * (rail if rail else 1.0) - t
            for i in range(k):
                B.beam(timber, pt(xs[i], t), pt(xs[i + 1], top), t * 0.8)
                B.beam(timber, pt(xs[i + 1], t), pt(xs[i], top), t * 0.8)
        elif braces:
            for i in range(k):
                if i % 2:
                    continue
                s0, s1 = xs[i], xs[i + 1]
                top = H * (rail if rail else 1.0) - t
                if (i // 2) % 2:
                    B.beam(timber, pt(s0, t), pt(s1, top), t * 0.8)
                else:
                    B.beam(timber, pt(s1, t), pt(s0, top), t * 0.8)


def opening(B, mat, frame, side, s, w, z0, h, L, W, depth=0.05, fr=0.08):
    """A door or window: a dark panel on a wall face with a timber frame.
    side N/S/E/W of an L by W wall box; s along the wall from its centre."""
    d = 0.06
    if side in "NS":
        y = (W / 2 + d / 2) * (1 if side == "N" else -1)
        B.box(mat, s, y, z0, w, d, h)
        if frame is not None:
            for e in (-1, 1):
                B.box(frame, s + e * (w / 2 + fr / 2), y, z0, fr, d + 0.03, h + fr)
            B.box(frame, s, y, z0 + h, w + 2 * fr, d + 0.03, fr)
    else:
        x = (L / 2 + d / 2) * (1 if side == "E" else -1)
        B.box(mat, x, s, z0, d, w, h)
        if frame is not None:
            for e in (-1, 1):
                B.box(frame, x, s + e * (w / 2 + fr / 2), z0, d + 0.03, fr, h + fr)
            B.box(frame, x, s, z0 + h, d + 0.03, w + 2 * fr, fr)


def chimney(B, mat, x, y, z0, z1, sx=0.6, sy=0.6, cap=None):
    B.box(mat, x, y, z0, sx, sy, z1 - z0)
    B.box(cap or mat, x, y, z1, sx * 1.18, sy * 1.18, 0.14)
    B.box(cap or mat, x, y, z1 + 0.14, sx * 0.8, sy * 0.8, 0.12)


def wheel(B, rim, spoke, x, y, z, r, axis=(1, 0, 0), spokes=8, w=0.1):
    """A spoked cart wheel centred at (x, y, z), its axle along axis."""
    ax = Vector(axis).normalized()
    Mw = Matrix.Translation((x, y, z)) @ ax.to_track_quat("Z", "Y" if abs(ax.z) < 0.99 else "X").to_matrix().to_4x4()
    seg = 14
    rt = r * 0.16
    v, f = [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        c, s = math.cos(a), math.sin(a)
        v += [(c * r, s * r, -w / 2), (c * r, s * r, w / 2), (c * (r - rt), s * (r - rt), w / 2),
              (c * (r - rt), s * (r - rt), -w / 2)]
    for i in range(seg):
        j = (i + 1) % seg
        for k in range(4):
            f.append((4 * i + k, 4 * j + k, 4 * j + (k + 1) % 4, 4 * i + (k + 1) % 4))
    B.solid(rim, v, f, Mw)
    with B.at():
        B.M = B.M @ Mw
        B.cyl(spoke, 0, 0, -w * 0.9, r * 0.2, w * 1.8, seg=8)
        for i in range(spokes):
            a = 2 * math.pi * (i + 0.5) / spokes
            B.beam(spoke, (0, 0, 0), (math.cos(a) * (r - rt * 0.5), math.sin(a) * (r - rt * 0.5), 0), w * 0.45)


# ---------------------------------------------------------------- ruins

def cut_view(obs, polys, r, mode="DIFFERENCE", zlo=-2.0, zhi=30.0):
    """Cut objects by outlines traced on the sprite of feature r (lists of
    (col, row)): a prism along the classic camera's line of sight through
    each outline, so what is cut is exactly what the sprite shows as gone
    (DIFFERENCE) or all but what it shows standing (INTERSECT)."""
    hx, hy = r["sprite"]["hotspot"]
    bm = bmesh.new()
    for poly in polys:
        n = len(poly)
        lo = [bm.verts.new(((c - hx) / 16.0, (hy - rw) / 16.0 - 0.5 * zlo, zlo)) for c, rw in poly]
        hi = [bm.verts.new(((c - hx) / 16.0, (hy - rw) / 16.0 - 0.5 * zhi, zhi)) for c, rw in poly]
        fs = [bm.faces.new(lo), bm.faces.new(hi)]
        for i in range(n):
            j = (i + 1) % n
            fs.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))
        bmesh.ops.recalc_face_normals(bm, faces=fs)
    me = bpy.data.meshes.new("cutter")
    bm.to_mesh(me)
    bm.free()
    cutter = bpy.data.objects.new("cutter", me)
    bpy.context.collection.objects.link(cutter)
    for ob in obs:
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = ob
        ob.select_set(True)
        mod = ob.modifiers.new("cut", "BOOLEAN")
        mod.operation, mod.solver, mod.object = mode, "EXACT", cutter
        mod.use_self = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
        relay_uv(ob)
    bpy.data.objects.remove(cutter, do_unlink=True)
    return obs


def drop_view(obs, polys, r):
    """Delete the faces of objects whose middle the sprite of feature r
    draws inside one of the outlines: a cheap cut for many small timbers."""
    for ob in obs:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        gone = []
        for f in bm.faces:
            c = f.calc_center_median()
            col, row = screen(r, c.x, c.y, c.z)
            if any(inside_poly(P, col, row) for P in polys):
                gone.append(f)
        bmesh.ops.delete(bm, geom=gone, context="FACES")
        bm.to_mesh(ob.data)
        bm.free()
    return obs


def jag(poly, amp=1.5, step=4.0, seed=0):
    """An outline made ragged: points every step px, pushed about amp px."""
    rng = random.Random(seed)
    out = []
    for i in range(len(poly)):
        a, b = Vector(poly[i]), Vector(poly[(i + 1) % len(poly)])
        d = b - a
        n = max(1, int(d.length / step))
        nrm = Vector((-d.y, d.x)).normalized() if d.length > 0 else Vector((0, 0))
        for k in range(n):
            p = a + d * (k / n)
            if k:
                p += nrm * (rng.random() - 0.5) * 2 * amp
            out.append((p.x, p.y))
    return out


def heap(B, mat, r, poly, top, z_at=0.4, cell=0.35, noise=0.25, seed=0, base=-0.05, edge=0.9, pic=None, grow=1,
         strict=False):
    """A rubble heap on the ground inside an outline traced on the sprite
    (read at height z_at), up to top cells high in its middle, a noisy
    surface of cell-sized facets; with pic, only where the sprite draws."""
    rng = random.Random(seed)
    P = [px(r, c, rw, z_at) for c, rw in poly]
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    nx, ny = max(2, int((x1 - x0) / cell) + 2), max(2, int((y1 - y0) / cell) + 2)

    def inside(x, y):
        c = False
        for i in range(len(P)):
            (ax, ay), (bx, by) = P[i], P[i - 1]
            if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay) + ax:
                c = not c
        if c and pic is not None:
            return pic.solid(*screen(r, x, y, z_at * 0.6), grow=grow)
        return c

    def dist(x, y):
        best = 1e9
        for i in range(len(P)):
            a, b = Vector(P[i]), Vector(P[i - 1])
            p = Vector((x, y))
            d = b - a
            t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
            best = min(best, (a + d * t - p).length)
        return best

    grid = {}
    verts, faces = [], []
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = x0 + (i - 0.5) * cell + (rng.random() - 0.5) * cell * 0.5
            y = y0 + (j - 0.5) * cell + (rng.random() - 0.5) * cell * 0.5
            if inside(x, y):
                h = top * min(1.0, dist(x, y) / edge) ** 0.7
                h = max(0.02, h + (rng.random() - 0.5) * 2 * noise * min(1.0, h / max(top, 1e-3) + 0.3))
            else:
                h = base
            grid[i, j] = len(verts)
            verts.append((x, y, h))
    for j in range(ny):
        for i in range(nx):
            q = [grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]]
            low = [verts[k][2] <= base + 1e-6 for k in q]
            if all(low) or (strict and sum(low) > 1):
                continue
            if strict and sum(low) == 1:
                # a torn edge: keep the three corners that are on the heap
                t = [k for k, lo in zip(q, low) if not lo]
                faces.append(tuple(t))
                continue
            faces.append((q[0], q[1], q[2]))
            faces.append((q[0], q[2], q[3]))
    B.mesh(mat, verts, faces)

    def surface(x, y):
        """The heap's height at a plan point, without its noise."""
        if not inside(x, y):
            return 0.0
        return top * min(1.0, dist(x, y) / edge) ** 0.7
    return surface


def drape(B, mat, P, ground, cell=0.3, lift=0.05, seed=0, jitter=0.3):
    """A skin laid over a surface ground(x, y) inside plan polygon P, lift
    above it: gravel or ash showing on a heap in a patch of its own."""
    rng = random.Random(seed)
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    nx, ny = max(2, int((x1 - x0) / cell) + 1), max(2, int((y1 - y0) / cell) + 1)
    verts, faces, grid, inn = [], [], {}, {}
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = x0 + (x1 - x0) * i / nx + (rng.random() - 0.5) * cell * jitter
            y = y0 + (y1 - y0) * j / ny + (rng.random() - 0.5) * cell * jitter
            inn[i, j] = inside_poly(P, x, y)
            grid[i, j] = len(verts)
            verts.append((x, y, max(0.0, ground(x, y)) + lift))
    for j in range(ny):
        for i in range(nx):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            if sum(inn[k] for k in q) < 3:
                continue
            ids = [grid[k] for k in q if inn[k]] if sum(inn[k] for k in q) == 3 else [grid[k] for k in q]
            if len(ids) == 3:
                faces.append(tuple(ids))
            else:
                faces.append((ids[0], ids[1], ids[2]))
                faces.append((ids[0], ids[2], ids[3]))
    B.mesh(mat, verts, faces)


def ground_of(*fns):
    """The highest of several heaps' surfaces."""
    return lambda x, y: max(f(x, y) for f in fns)


def debris(B, mats, r, poly, n, z_at=0.4, size=(0.3, 1.6), seed=0, zmax=0.6, tilt=35, kinds=None, ground=None):
    """Broken timbers, planks and stones strewn inside an outline traced on
    the sprite: mats is {'beam': m, 'plank': m, 'stone': m} and kinds how
    often each comes up."""
    rng = random.Random(seed)
    P = [px(r, c, rw, z_at) for c, rw in poly]
    xs, ys = [p[0] for p in P], [p[1] for p in P]

    def inside(x, y):
        c = False
        for i in range(len(P)):
            (ax, ay), (bx, by) = P[i], P[i - 1]
            if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay) + ax:
                c = not c
        return c
    kinds = kinds or {"beam": 3, "plank": 2, "stone": 3}
    bag = [k for k, w in kinds.items() for _ in range(w)]
    placed = 0
    tries = 0
    while placed < n and tries < n * 50:
        tries += 1
        x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
        if not inside(x, y):
            continue
        k = rng.choice(bag)
        z = rng.uniform(0, zmax)
        if ground is not None:
            # on the heap's surface, some half sunk into it
            z = ground(x, y) + rng.uniform(-0.12, 0.1)
        yaw = rng.uniform(0, 180)
        if k == "beam":
            L = rng.uniform(*size)
            w = rng.uniform(0.1, 0.18)
            # most lie flat; a few rest on something at a slant
            a = rng.uniform(-tilt, tilt) if rng.random() < 0.3 else rng.uniform(-8, 8)
            z = z if ground is not None else min(z, zmax * 0.6)
            with B.at(x, y, z, yaw=yaw):
                B.beam(mats["beam"], (-L / 2 * math.cos(math.radians(a)), 0, -L / 2 * math.sin(math.radians(a))),
                       (L / 2 * math.cos(math.radians(a)), 0, L / 2 * math.sin(math.radians(a))), w, w * rng.uniform(0.8, 1.3))
        elif k == "plank":
            L = rng.uniform(size[0], size[1] * 0.8)
            with B.at(x, y, z, yaw=yaw, pitch=rng.uniform(-tilt, tilt), roll=rng.uniform(-10, 10)):
                B.box(mats["plank"], 0, 0, 0, L, rng.uniform(0.2, 0.35), 0.05)
        elif k == "straw":
            L = rng.uniform(0.2, 0.5)
            with B.at(x, y, z, yaw=yaw, pitch=rng.uniform(-tilt, tilt) * 0.5, roll=rng.uniform(-tilt, tilt) * 0.5):
                B.box(mats["straw"], 0, 0, 0, L, L * rng.uniform(0.3, 0.6), 0.04)
        elif k == "tile":
            L = rng.uniform(0.25, 0.6)
            with B.at(x, y, z, yaw=yaw, pitch=rng.uniform(-tilt, tilt), roll=rng.uniform(-tilt, tilt)):
                B.box(mats["tile"], 0, 0, 0, L, L * rng.uniform(0.6, 1.0), 0.05)
        else:
            s = rng.uniform(0.15, 0.4)
            with B.at(x, y, z - s * 0.3, yaw=yaw, pitch=rng.uniform(-20, 20)):
                B.box(mats["stone"], 0, 0, 0, s * rng.uniform(0.9, 1.6), s, s * rng.uniform(0.5, 0.9))
        placed += 1


def broken_wall(B, mat, top_mat, a, b, t, profile, z0=0.0, step=0.28, jitter=0.18, seed=0, notch=0.25):
    """A wall from plan point a to b, t thick, standing to a ragged top:
    profile is [(f, h)] along it (f from 0 at a to 1 at b), heights
    between are interpolated, then roughened stone by stone. The broken
    top faces take top_mat."""
    rng = random.Random(seed)
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    d = b - a
    L = d.length
    if L < 1e-4:
        return
    d.normalize()
    n = Vector((-d.y, d.x, 0))
    k = max(2, int(L / step) + 1)
    fs = [i / k for i in range(k + 1)]
    pf = sorted(profile)
    hs = []
    for i, f in enumerate(fs):
        h = float(np.interp(f, [q[0] for q in pf], [q[1] for q in pf]))
        if 0 < i < k and h > z0 + 0.05:
            h += (rng.random() - 0.5) * 2 * jitter
            if rng.random() < notch:
                h -= rng.random() * jitter * 2.5
        hs.append(max(z0 + 0.04, h))
    A = [a + d * (f * L) + n * (t / 2) for f in fs]
    Bv = [a + d * (f * L) - n * (t / 2) for f in fs]
    V = []
    for i in range(k + 1):
        V.append((A[i].x, A[i].y, hs[i]))
    for i in range(k + 1):
        V.append((Bv[i].x, Bv[i].y, hs[i]))
    ia, ib = 0, k + 1
    V += [(A[0].x, A[0].y, z0), (A[k].x, A[k].y, z0), (Bv[0].x, Bv[0].y, z0), (Bv[k].x, Bv[k].y, z0)]
    a0b, a1b, b0b, b1b = 2 * (k + 1), 2 * (k + 1) + 1, 2 * (k + 1) + 2, 2 * (k + 1) + 3
    side_a = [a0b] + [ia + i for i in range(k + 1)] + [a1b]
    side_b = [b1b] + [ib + i for i in range(k, -1, -1)] + [b0b]
    faces = [tuple(side_a), tuple(side_b), (a0b, b0b, ib, ia), (b1b, a1b, ia + k, ib + k), (a0b, a1b, b1b, b0b)]
    B.mesh(mat, V, faces)
    B.mesh(top_mat, V, [(ia + i, ib + i, ib + i + 1, ia + i + 1) for i in range(k)])


def ring_wall(B, mat, top_mat, x, y, R, t, heights, seg=20, seed=0, jitter=0.2):
    """A round wall broken down: heights is [(angle in degrees, height)]
    round the ring (angle 0 east, anticlockwise), interpolated between."""
    hs = sorted(heights)
    ang = [a for a, _ in hs] + [hs[0][0] + 360]
    val = [h for _, h in hs] + [hs[0][1]]

    def h_at(a):
        a = (a - ang[0]) % 360 + ang[0]
        return float(np.interp(a, ang, val))
    for i in range(seg):
        a0, a1 = 360.0 * i / seg, 360.0 * (i + 1) / seg
        p0 = (x + R * math.cos(math.radians(a0)), y + R * math.sin(math.radians(a0)))
        p1 = (x + R * math.cos(math.radians(a1)), y + R * math.sin(math.radians(a1)))
        h0, h1 = h_at(a0), h_at(a1)
        if max(h0, h1) < 0.05:
            continue
        broken_wall(B, mat, top_mat, p0, p1, t, [(0, h0), (1, h1)], seed=seed + i, jitter=jitter, step=0.3)


def circle_px(r, x, y, R, z=0.0, k=18):
    """A circle in plan as the sprite of feature r draws it at height z."""
    return [screen(r, x + R * math.cos(2 * math.pi * i / k), y + R * math.sin(2 * math.pi * i / k), z)
            for i in range(k)]


def ruin_walls(B, mat, top_mat, p, tops, t=0.3, seed=0, **kw):
    """The four walls of house body p (fit.py's keys) standing to ragged
    tops: tops is {side: profile} for sides S, E, N, W (the long faces are
    S and N, at -v and +v; the ends W and E), each run anticlockwise;
    a side left out is down."""
    L, W = p["L"], p["W"]
    hl, hw = L / 2 - t / 2, W / 2 - t / 2
    runs = {"S": ((-L / 2, -hw), (L / 2, -hw)), "E": ((hl, -W / 2), (hl, W / 2)),
            "N": ((L / 2, hw), (-L / 2, hw)), "W": ((-hl, W / 2), (-hl, -W / 2))}
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        for i, (side, prof) in enumerate(sorted(tops.items())):
            a, b = runs[side]
            broken_wall(B, mat, top_mat, a, b, t, prof, seed=seed + i, **kw)


def leaning(B, mat, r, sticks, w=0.14):
    """Timbers read off the sprite: each (col0, row0, z0, col1, row1, z1),
    the two ends where the sprite draws them at those heights."""
    for c0, r0, z0, c1, r1, z1 in sticks:
        a = px(r, c0, r0, z0) + (z0,)
        b = px(r, c1, r1, z1) + (z1,)
        B.beam(mat, a, b, w)


# ---------------------------------------------------------------- straw

def inside_poly(P, x, y):
    c = False
    for i in range(len(P)):
        (ax, ay), (bx, by) = P[i], P[i - 1]
        if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay) + ax:
            c = not c
    return c


def dome(B, mat, x, y, rx, ry, h, prof=None, seg=24, jitter=0.04, seed=0, yaw=0.0):
    """A haystack or beehive dome on the ground: prof is (radius, height)
    as fractions from the base round to the top; returns surf(a, t), the
    point and outward normal at angle a and height fraction t."""
    prof = prof or [(0.86, 0.0), (0.97, 0.12), (1.0, 0.28), (0.96, 0.48), (0.84, 0.66), (0.64, 0.82),
                    (0.38, 0.94), (0.0, 1.0)]
    rng = random.Random(seed)
    ks = [[1 + jitter * (rng.random() - 0.5) * 2 for _ in range(seg)] for _ in prof]
    c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))

    def P(k, i, rr, zf):
        a = 2 * math.pi * i / seg
        u, v = rr * rx * ks[k][i] * math.cos(a), rr * ry * ks[k][i] * math.sin(a)
        return (x + u * c - v * s_, y + u * s_ + v * c, zf * h)
    v, rings = [], []
    for k, (rr, zf) in enumerate(prof):
        if rr < 1e-4:
            rings.append([len(v)])
            v.append((x, y, zf * h))
        else:
            rings.append(list(range(len(v), len(v) + seg)))
            v += [P(k, i, rr, zf) for i in range(seg)]
    f = [tuple(reversed(rings[0]))]
    for a_, b_ in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            q = [a_[i % len(a_)], a_[j % len(a_)], b_[j % len(b_)], b_[i % len(b_)]]
            q = [p for n_, p in enumerate(q) if p not in q[:n_]]
            if len(q) >= 3:
                f.append(tuple(q))
    B.solid(mat, v, f)
    zs = [q[1] for q in prof]
    rs = [q[0] for q in prof]

    def surf(a, t):
        rr = float(np.interp(t, zs, rs))
        u, w = rr * rx * math.cos(a), rr * ry * math.sin(a)
        p = Vector((x + u * c - w * s_, y + u * s_ + w * c, t * h))
        # the normal from the profile's slope
        dt = 0.02
        r2 = float(np.interp(min(1.0, t + dt), zs, rs))
        dr = (r2 - rr) * (rx + ry) / 2
        dz = dt * h
        out = Vector((math.cos(a) * c - math.sin(a) * s_, math.cos(a) * s_ + math.sin(a) * c, 0))
        n = (out * dz - Vector((0, 0, 1)) * dr).normalized()
        return p, n, out
    return surf


def card(B, mat, a, b, w, n):
    """A thin straw card from a to b, w wide, lying across normal n."""
    a, b = Vector(a), Vector(b)
    d = b - a
    if d.length < 1e-5:
        return
    side = d.cross(n)
    if side.length < 1e-6:
        side = d.cross(Vector((0, 0, 1)))
    side = side.normalized() * (w / 2)
    B.mesh(mat, [tuple(a - side), tuple(a + side), tuple(b + side * 0.3), tuple(b - side * 0.3)], [(0, 1, 2, 3)])


def strands(B, mats, surf, n, t0=0.05, t1=0.97, length=(0.25, 0.55), w=(0.05, 0.09), lift=0.35, seed=0):
    """Straw ends standing out of a dome and hanging down its sides: each
    rooted on the surface and running down the slope and a little out."""
    rng = random.Random(seed)
    for _ in range(n):
        a = rng.uniform(0, 2 * math.pi)
        t = t0 + (t1 - t0) * math.sqrt(rng.random())
        p, nrm, out = surf(a, t)
        down = (nrm.cross(out.cross(Vector((0, 0, 1)))))
        if down.z > 0:
            down = -down
        down = (down.normalized() + nrm * rng.uniform(0.1, lift) + out.cross(Vector((0, 0, 1))) *
                rng.uniform(-0.3, 0.3)).normalized()
        L = rng.uniform(*length)
        root = p - nrm * 0.03
        tip = root + down * L
        tip.z = max(0.02, tip.z)
        card(B, rng.choice(mats), root, tip, rng.uniform(*w), nrm)


def loose_straw(B, mats, r, poly, n, z_at=0.0, length=(0.25, 0.6), w=(0.05, 0.1), seed=0, z=0.03):
    """Wisps of straw lying on the ground inside an outline traced on the
    sprite."""
    rng = random.Random(seed)
    P = [px(r, c, rw, z_at) for c, rw in poly]
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    placed = tries = 0
    while placed < n and tries < n * 60:
        tries += 1
        x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
        if not inside_poly(P, x, y):
            continue
        a = rng.uniform(0, math.pi)
        L = rng.uniform(*length)
        d = Vector((math.cos(a), math.sin(a), rng.uniform(-0.1, 0.25))) * (L / 2)
        c = Vector((x, y, z + rng.uniform(0, 0.08)))
        card(B, rng.choice(mats), c - d, c + d, rng.uniform(*w), Vector((0, 0, 1)))
        placed += 1


# ---------------------------------------------------------------- props

def rock(B, mat, x, y, z, sx, sy, sz, seed=0, seg=7, yaw=None):
    """A rough stone or lump: a jittered squat solid sx by sy by sz."""
    rng = random.Random(seed)
    yaw = rng.uniform(0, 360) if yaw is None else yaw
    rings = [(0.55, 0.0), (0.95, 0.25), (1.0, 0.55), (0.7, 0.85), (0.0, 1.0)]
    v, idx = [], []
    for rr, zf in rings:
        if rr < 1e-4:
            idx.append([len(v)])
            v.append((rng.uniform(-0.1, 0.1) * sx, rng.uniform(-0.1, 0.1) * sy, zf * sz))
            continue
        ring = []
        for i in range(seg):
            a = 2 * math.pi * (i + rng.uniform(-0.2, 0.2)) / seg
            k = rng.uniform(0.75, 1.15)
            ring.append(len(v))
            v.append((rr * k * sx / 2 * math.cos(a), rr * k * sy / 2 * math.sin(a), zf * sz * rng.uniform(0.85, 1.1)))
        idx.append(ring)
    f = [tuple(reversed(idx[0]))]
    for a_, b_ in zip(idx, idx[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            q = [a_[i % len(a_)], a_[j % len(a_)], b_[j % len(b_)], b_[i % len(b_)]]
            q = [p for n_, p in enumerate(q) if p not in q[:n_]]
            f.append(tuple(q))
    with B.at(x, y, z, yaw=yaw):
        B.solid(mat, v, f)


def lump(B, mat, x, y, z, sx, sy, sz, seed=0):
    """A cheap rough stone: a squat jittered octahedron, eight faces."""
    rng = random.Random(seed)
    a = rng.uniform(0, 2 * math.pi)
    v = []
    for i in range(4):
        t = a + math.pi / 2 * i + rng.uniform(-0.3, 0.3)
        k = rng.uniform(0.8, 1.1)
        v.append((math.cos(t) * sx / 2 * k, math.sin(t) * sy / 2 * k, sz * rng.uniform(0.3, 0.5)))
    v.append((rng.uniform(-0.1, 0.1) * sx, rng.uniform(-0.1, 0.1) * sy, sz))
    v.append((0.0, 0.0, 0.0))
    f = [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4), (1, 0, 5), (2, 1, 5), (3, 2, 5), (0, 3, 5)]
    with B.at(x, y, z):
        B.solid(mat, v, f)


def fringe(B, mat, a, b, drop, t=0.12, step=0.18, jag=0.35, seed=0, out=None):
    """Thatch hanging below an eave: a strip from a to b (3D points on the
    eave) hanging drop down, its lower edge ragged; out leans it outward."""
    rng = random.Random(seed)
    a, b = Vector(a), Vector(b)
    d = b - a
    k = max(2, int(d.length / step))
    top, bot = [], []
    o = Vector(out) if out is not None else Vector((0, 0, 0))
    for i in range(k + 1):
        p = a + d * (i / k)
        top.append(p)
        dd = drop * (1 - rng.uniform(0, jag)) if 0 < i < k else drop * (1 - jag / 2)
        bot.append(p + Vector((0, 0, -dd)) + o * dd)
    n = d.cross(Vector((0, 0, 1)))
    n = n.normalized() * t if n.length > 1e-6 else Vector((0, t, 0))
    V = [tuple(p) for p in top] + [tuple(p) for p in bot] + [tuple(p + n) for p in top] + [tuple(p + n) for p in bot]
    m = k + 1
    f = []
    for i in range(k):
        f.append((i, i + 1, m + i + 1, m + i))
        f.append((2 * m + i, 3 * m + i, 3 * m + i + 1, 2 * m + i + 1))
        f.append((m + i, m + i + 1, 3 * m + i + 1, 3 * m + i))
    f.append((0, m, 3 * m, 2 * m))
    f.append((k, 2 * m + k, 3 * m + k, m + k))
    B.solid(mat, V, f)


def barrel(B, wood, hoop, x, y, z=0.0, r=0.4, h=1.0, lying=False, yaw=0.0, lid=None):
    """A bellied barrel with two dark hoops; lying on its side if asked."""
    prof = [(0.0, 0.0), (r * 0.84, 0.0), (r * 0.95, h * 0.25), (r, h * 0.5), (r * 0.95, h * 0.75), (r * 0.84, h),
            (0.0, h)]
    with B.at(x, y, z + (r if lying else 0), yaw=yaw, pitch=90 if lying else 0):
        if lying:
            B.M = B.M @ Matrix.Translation((0, 0, -h / 2))
        B.lathe(wood, 0, 0, prof, seg=12)
        for zf in (0.18, 0.82):
            rr = r * (0.92 if zf < 0.5 else 0.92) + 0.015
            B.cyl(hoop, 0, 0, h * zf - 0.03, rr, 0.06, seg=12)
        if lid is not None:
            B.cyl(lid, 0, 0, h - 0.02, r * 0.8, 0.04, seg=12)


def log(B, mat, a, b, r, seg=7, end=None):
    """A round log from a to b, its cut ends in end's material."""
    a, b = Vector(a), Vector(b)
    d = b - a
    B.cyl(mat, a.x, a.y, a.z, r, d.length, seg=seg, axis=tuple(d), cap=end is None)
    if end is not None:
        for p, s in ((a, -1), (b, 1)):
            B.cyl(end, p.x, p.y, p.z, r * 0.9, 0.02, seg=seg, axis=tuple(d * s))


# ---------------------------------------------------------------- the sprite as data

class Picture:
    """A sprite's pixels, rows top down, read inside Blender: its drawn
    colours for choosing materials and its silhouette for keeping pieces
    inside what the original drew."""

    def __init__(self, r):
        img = bpy.data.images.load(DATA + "/sprites/%s.png" % r["name"], check_existing=True)
        w, h = img.size
        a = np.array(img.pixels[:], np.float32).reshape(h, w, 4)[::-1] * 255.0
        self.r, self.w, self.h = r, w, h
        self.rgb = a[..., :3]
        self.alpha = a[..., 3] > 127

    def _win(self, col, row, rad):
        c, rw = int(math.floor(col)), int(math.floor(row))
        return max(0, c - rad), min(self.w, c + rad + 1), max(0, rw - rad), min(self.h, rw + rad + 1)

    def solid(self, col, row, grow=1):
        x0, x1, y0, y1 = self._win(col, row, grow)
        return x0 < x1 and y0 < y1 and bool(self.alpha[y0:y1, x0:x1].any())

    def colour(self, col, row, rad=2, q=None):
        """The median drawn colour round (col, row), or with q the colour at
        that brightness quantile: the lit face of a piece, not the gaps."""
        x0, x1, y0, y1 = self._win(col, row, rad)
        if x0 >= x1 or y0 >= y1:
            return None
        m = self.alpha[y0:y1, x0:x1]
        if not m.any():
            return None
        p = self.rgb[y0:y1, x0:x1][m]
        if q is None:
            return np.median(p, 0)
        lum = p @ np.array([0.299, 0.587, 0.114])
        o = np.argsort(lum)
        return p[o[int(q * (len(o) - 1))]]

    def sees(self, p, grow=1):
        return self.solid(*screen(self.r, p[0], p[1], p[2]), grow=grow)


def tex_grit(name, base, light=None, dark=None, uv=2.0, chips=0.1, darks=0.2, blotch=0.3, seed=7, ppc=16):
    """Ash, charcoal or gravel speckled at the sprite's own pixel size (ppc
    texels a cell before enlarging), so the speckle still reads at the
    classic scale; uv is the cells one repeat covers."""
    rng = np.random.default_rng(seed)
    n = max(8, int(round(uv * ppc)))
    base = np.array(rgb(base), float)
    light = np.array(rgb(light), float) if light is not None else np.minimum(255, base * 2.4)
    dark = np.array(rgb(dark), float) if dark is not None else base * 0.35
    noise = _smooth(rng, n, max(2, n // 8)) * 0.6 + _smooth(rng, n, max(3, n // 3)) * 0.4
    col = base[None, None] * (1 - blotch / 2 + blotch * noise)[..., None]
    rr = rng.random((n, n))
    col[rr < darks] = dark
    ch = rr > 1 - chips
    col[ch] = light * (0.6 + 0.4 * rng.random((int(ch.sum()), 1)))
    k = max(1, 256 // n)
    col = np.repeat(np.repeat(col, k, 0), k, 1)
    return _to_image(name, np.clip(col, 0, 255))


def palette(pic, k=6, seed=0, box=None, iters=15, lit=0.35):
    """The k colours the sprite is mostly drawn in (k-means over its opaque
    pixels, or those inside box = (x0, y0, x1, y1)), dark to light; the
    darkest lit fraction is left out, as gaps the geometry makes itself."""
    a = pic.alpha.copy()
    if box is not None:
        m = np.zeros_like(a)
        x0, y0, x1, y1 = box
        m[max(0, y0):y1, max(0, x0):x1] = True
        a &= m
    X = pic.rgb[a].astype(float)
    if lit and len(X) > 10:
        L = X @ np.array([0.299, 0.587, 0.114])
        X = X[L >= np.quantile(L, lit)]
    if len(X) < k:
        return [tuple(X.mean(0))] if len(X) else [(60.0, 55.0, 45.0)]
    lum = X @ np.array([0.299, 0.587, 0.114])
    o = np.argsort(lum)
    C = X[o[((np.arange(k) + 0.5) / k * len(o)).astype(int)]]
    for _ in range(iters):
        d = ((X[:, None, :] - C[None]) ** 2).sum(-1)
        lab = d.argmin(1)
        for j in range(k):
            if (lab == j).any():
                C[j] = X[lab == j].mean(0)
    C = C[np.argsort(C @ np.array([0.299, 0.587, 0.114]))]
    return [tuple(c) for c in C]


def saturate(c, k=1.3):
    """A drawn colour with its chroma raised by k about its brightness, to
    make up for the render's view transform greying colours."""
    c = np.array(rgb(c), float)
    L = c @ np.array([0.299, 0.587, 0.114])
    return tuple(np.clip(L + (c - L) * k, 0, 255))


def swatches(name, cols, kind="mottle", seed=0, uv=0.8, sat=1.3, gain=1.0):
    """Materials in the given drawn colours (chroma raised by sat), each
    with a little grain: mottle for stone and slabs, planks for timber, grit
    for ash; each keeps the drawn colour as its ref."""
    out = []
    for i, c0 in enumerate(cols):
        c = tuple(min(255.0, v * gain) for v in saturate(c0, sat))
        nm = "%s_%s%d" % (name, kind[:2], i)
        if kind == "planks":
            t = tex_planks(nm, c, boards=2, var=0.1, seed=seed + i)
        elif kind == "grit":
            t = tex_grit(nm, c, uv=uv, seed=seed + i)
        else:
            t = tex_mottle(nm, c, var=0.14, seed=seed + i)
        out.append(Mat(nm, tex=t, uv=uv, ref=c0))
    return out


def plan_of(r, poly, z=0.0):
    """Sprite pixels as plan points read at height z."""
    return [px(r, c, rw, z) for c, rw in poly]


def _pick(rng, mats, kinds, col):
    """A (kind, Mat) drawn by the kinds' weights and, given the sprite's
    colour where the piece lies, by how near each Mat's colour is to it."""
    cands, wts = [], []
    for k, w in kinds.items():
        ms = mats.get(k, [])
        for m in ms:
            s = 1.0
            if col is not None and m.ref is not None:
                d = float(np.linalg.norm(np.array(m.ref, float) - col))
                s = math.exp(-(d / 24.0) ** 2)
            cands.append((k, m))
            wts.append(w * s / len(ms) + 1e-12)
    t = rng.random() * sum(wts)
    for c, w in zip(cands, wts):
        t -= w
        if t <= 0:
            return c
    return cands[-1]


def cover(B, mats, r, P, ground, n, kinds, pic=None, size=(0.4, 1.0), length=(1.0, 2.4), width=(0.1, 0.18),
          seed=0, lift=0.03, tilt=10, stick=0.0, grow=1, thick=(0.05, 0.1), stone=(0.2, 0.45), chunk=(0.1, 0.25),
          tries=60, q=0.7, jumble=0.0, boulder=(0.6, 1.1)):
    """Pieces lying on a surface ground(x, y) inside plan polygon P, each
    turned to the slope under it: slabs, tiles, sheets, timbers, boards,
    stones and chunks. mats is {kind: [Mat]}, kinds how often each comes
    up. With pic (a Picture), a piece favours the Mat nearest the sprite's
    colour where it is drawn and is dropped if a corner or end falls
    outside the sprite's silhouette. Returns how many were laid."""
    rng = random.Random(seed)
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    placed = k = 0

    def gz(x, y, dflt):
        z = ground(x, y)
        return dflt if z < -50 else z
    while placed < n and k < n * tries:
        k += 1
        x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
        if not inside_poly(P, x, y):
            continue
        z0 = ground(x, y)
        if z0 < -50:
            continue
        e = 0.12
        gx = (gz(x + e, y, z0) - gz(x - e, y, z0)) / (2 * e)
        gy = (gz(x, y + e, z0) - gz(x, y - e, z0)) / (2 * e)
        nrm = Vector((-max(-2.5, min(2.5, gx)), -max(-2.5, min(2.5, gy)), 1.0)).normalized()
        tt = math.tan(math.radians(tilt))
        nrm = (nrm + Vector((rng.uniform(-1, 1) * tt, rng.uniform(-1, 1) * tt, 0))).normalized()
        a = rng.uniform(0, 2 * math.pi)
        d = Vector((math.cos(a), math.sin(a), 0))
        t1 = (d - nrm * d.dot(nrm)).normalized()
        t2 = nrm.cross(t1)
        lf = lift + rng.uniform(0, jumble)
        c = Vector((x, y, z0)) + nrm * lf
        col = pic.colour(*screen(r, x, y, z0), q=q) if pic is not None else None
        kind, mat = _pick(rng, mats, kinds, col)
        build = None
        if kind in ("slab", "tile", "sheet"):
            w, h = rng.uniform(*size), rng.uniform(*size) * 0.85
            m_ = 4
            pts = []
            for i in range(m_):
                an = 2 * math.pi * (i + rng.uniform(-0.2, 0.2)) / m_
                rr = rng.uniform(0.8, 1.15) * 0.7
                pts.append(c + t1 * (math.cos(an) * w * rr) + t2 * (math.sin(an) * h * rr))
            ends = pts
            th = rng.uniform(*thick)
            if kind == "sheet":
                rim = mats["sheet_rim"][0]
                big = [c + (q - c) * 1.25 - nrm * 0.02 for q in pts]

                def build(mat=mat, pts=pts, big=big, th=th, rim=rim):
                    B.slab(rim, [tuple(q) for q in big], th)
                    B.slab(mat, [tuple(q + nrm * 0.03) for q in pts], th)
            else:
                def build(mat=mat, pts=pts, th=th):
                    B.slab(mat, [tuple(q) for q in pts], th)
        elif kind in ("beam", "board"):
            L = rng.uniform(*length) * (1.0 if kind == "beam" else 0.8)
            w = rng.uniform(*width) if kind == "beam" else rng.uniform(0.22, 0.34)
            h = w * rng.uniform(0.8, 1.2) if kind == "beam" else 0.05
            p0, p1 = c - t1 * (L / 2), c + t1 * (L / 2)
            p0.z = gz(p0.x, p0.y, z0) + h / 2 + lf
            p1.z = gz(p1.x, p1.y, z0) + h / 2 + lf
            zm = gz(x, y, z0) + h / 2 + lf
            dz = zm - (p0.z + p1.z) / 2
            if dz > 0:
                p0.z += dz
                p1.z += dz
            if stick and rng.random() < stick:
                p1.z += rng.uniform(0.4, 1.2)
            ends = [p0, p1]
            tw = rng.uniform(-15, 15)

            def build(mat=mat, p0=p0, p1=p1, w=w, h=h, tw=tw):
                B.beam(mat, tuple(p0), tuple(p1), w, h, twist=tw)
        elif kind == "boulder":
            s = rng.uniform(*boulder)
            sd = rng.randint(0, 99999)
            ends = [c + t1 * (s / 2), c - t1 * (s / 2), c + t2 * (s / 2), c - t2 * (s / 2)]
            sq = (rng.uniform(0.7, 1.0), rng.uniform(0.5, 0.8))

            def build(mat=mat, s=s, sd=sd, sq=sq):
                rock(B, mat, x, y, z0 - s * sq[1] * 0.35, s, s * sq[0], s * sq[1], seed=sd, seg=6)
        elif kind == "stone":
            s = rng.uniform(*stone)
            sd = rng.randint(0, 99999)
            ends = [c + t1 * (s / 2), c - t1 * (s / 2), c + t2 * (s / 2), c - t2 * (s / 2)]
            sq = (rng.uniform(0.7, 1.0), rng.uniform(0.45, 0.75))

            def build(mat=mat, s=s, sd=sd, sq=sq):
                lump(B, mat, x, y, z0 - s * sq[1] * 0.3, s, s * sq[0], s * sq[1], seed=sd)
        else:  # chunk
            s = rng.uniform(*chunk)
            ends = [c + t1 * (s / 2), c - t1 * (s / 2)]
            ang = (rng.uniform(0, 360), rng.uniform(-30, 30), rng.uniform(-30, 30))
            dims = (s * rng.uniform(0.8, 1.6), s, s * rng.uniform(0.5, 0.9))

            def build(mat=mat, s=s, ang=ang, dims=dims):
                B.box(mat, x, y, z0 - s * 0.3, dims[0], dims[1], dims[2], yaw=ang[0], pitch=ang[1], roll=ang[2])
        if pic is not None and not all(pic.sees(q, grow) for q in ends):
            continue
        build()
        placed += 1
    return placed


def jagged_heap(B, mat, r, poly, top, z_at=0.3, amp=2.0, step=4.0, seed=0, **kw):
    """heap() inside a ragged version of an outline."""
    return heap(B, mat, r, jag(poly, amp, step, seed), top, z_at=z_at, seed=seed, **kw)


# ---------------------------------------------------------------- roofs as geometry and maps

class RoofGeom:
    """The planes of a hip, pyramid or gable roof as kit.roof lays it, in
    the world: centre, yaw, walls L by W under eaves at H, rise R."""

    def __init__(self, cx, cy, L, W, H, R, over, over_end=None, yaw=0.0, kind="hip", hip=None, T=0.3, ends=None):
        self.cx, self.cy, self.yaw, self.kind, self.T = cx, cy, yaw, kind, T
        oe = over if over_end is None else over_end
        self.hx, self.hy = L / 2 + oe, W / 2 + over
        self.k = R / (W / 2)
        self.ze = H - over * self.k
        self.zr = H + R
        hip = L / 2 if hip is None else hip
        self.ends = tuple(ends) if ends else (("gable", "gable") if kind == "gable" else ("hip", "hip"))
        self.rxs = tuple(self.hx if t == "gable" else max(0.02, L / 2 - hip) for t in self.ends)
        self.kes = tuple((self.zr - self.ze) / max(1e-3, self.hx - rx) for rx in self.rxs)
        # the west end's, for callers that take one of each
        i = 0 if self.ends[0] != "gable" else 1
        self.rx, self.ke = self.rxs[i], self.kes[i]

    @classmethod
    def of(cls, p, kind=None, ends=None):
        """From a house body's keys (fit.py's)."""
        kind = kind or ("gable" if p["hip"] < 0 else "hip")
        return cls(p["cx"], p["cy"], p["L"], p["W"], p["H"], p["R"], p["over"], p["over_end"], yaw=p["yaw"],
                   kind=kind, hip=max(0.0, p["hip"]), T=p["T"], ends=ends)

    def side(self, y):
        return self.ze + (self.hy - abs(y)) * self.k

    def end(self, x):
        i = 0 if x < 0 else 1
        return 1e9 if self.ends[i] == "gable" else self.ze + (self.hx - abs(x)) * self.kes[i]

    def end_np(self, u):
        out = np.full_like(u, 1e9)
        for i, sel in ((0, u < 0), (1, u >= 0)):
            if self.ends[i] != "gable":
                out = np.where(sel, self.ze + (self.hx - np.abs(u)) * self.kes[i], out)
        return out

    def world(self, x, y, z):
        c, s = math.cos(math.radians(self.yaw)), math.sin(math.radians(self.yaw))
        return (self.cx + x * c - y * s, self.cy + x * s + y * c, z)

    def local(self, x, y):
        c, s = math.cos(math.radians(self.yaw)), math.sin(math.radians(self.yaw))
        dx, dy = x - self.cx, y - self.cy
        return dx * c + dy * s, -dx * s + dy * c

    def top_local(self, u, v):
        if abs(u) > self.hx or abs(v) > self.hy:
            return None
        return min(self.side(v), self.end(u))

    def top(self, x, y):
        return self.top_local(*self.local(x, y))

    def normal(self, x, y):
        """The world normal of the roof plane over (x, y)."""
        u, v = self.local(x, y)
        if self.side(v) <= self.end(u):
            n = (0.0, math.copysign(self.k, v), 1.0)
        else:
            n = (math.copysign(self.kes[0 if u < 0 else 1], u), 0.0, 1.0)
        c, s = math.cos(math.radians(self.yaw)), math.sin(math.radians(self.yaw))
        return Vector((n[0] * c - n[1] * s, n[0] * s + n[1] * c, n[2])).normalized()

    def hit(self, r, col, row):
        """The point of the roof the sprite of r draws at (col, row), or None:
        the highest point along the classic camera's ray under the roof."""
        z, step = self.zr + 0.3, 0.05
        prev = None
        while z > -0.1:
            x, y = px(r, col, row, z)
            t = self.top(x, y)
            if t is not None and t >= z:
                lo, hi = z, z + step if prev is not None else z
                for _ in range(20):
                    mid = (lo + hi) / 2
                    xm, ym = px(r, col, row, mid)
                    tm = self.top(xm, ym)
                    if tm is not None and tm >= mid:
                        lo = mid
                    else:
                        hi = mid
                x, y = px(r, col, row, lo)
                return x, y, lo
            prev = z
            z -= step
        return None


def _hash2(a, b, p=0):
    h = np.sin(a * 12.9898 + b * 78.233 + p * 37.719) * 43758.5453
    return h - np.floor(h)


def tex_roofmap(name, g, base, course=0.28, tile_w=0.8, var=0.12, gap=None, lip=0.25, grain=0.06, moss=None,
                moss_amt=0.0, soot=0.0, swirl=3.0, soot_col="#16120e", halos=(), dark=1.0, seed=1, ppc=40,
                maxsize=1024, slipped=0.0, width=0.4, core=0.0, core_col="#060504"):
    """A whole roof's slabs or tiles drawn as one picture seen from above:
    courses laid level up each plane of roof g from its eaves, with soot in
    broad swirling streaks (soot is how much, swirl how many bands) and
    haloes round given points [(x, y, radius, strength)] in the world.
    Registers the plan mapping under name; use Mat(..., uvfn=name)."""
    rng = np.random.default_rng(seed)
    S = min(float(ppc), maxsize / (2 * max(g.hx, g.hy)))
    nu, nv = max(16, int(2 * g.hx * S)), max(16, int(2 * g.hy * S))
    u = (-g.hx + (np.arange(nu) + 0.5) / nu * 2 * g.hx)[None, :].repeat(nv, 0)
    v = (-g.hy + (np.arange(nv) + 0.5) / nv * 2 * g.hy)[:, None].repeat(nu, 1)
    zs = g.ze + (g.hy - np.abs(v)) * g.k
    ze_ = g.end_np(u)
    on_side = zs <= ze_
    kend = np.where(u < 0, g.kes[0], g.kes[1])
    dist = np.where(on_side, (g.hy - np.abs(v)) * math.sqrt(1 + g.k ** 2), (g.hx - np.abs(u)) * np.sqrt(1 + kend ** 2))
    along = np.where(on_side, u * np.sign(v + 1e-9), v * np.sign(u + 1e-9))
    plane = np.where(on_side, np.where(v < 0, 0, 1), np.where(u < 0, 2, 3))
    ri = np.floor(dist / course)
    fv = dist / course - ri
    off = (ri % 2) * 0.5 * tile_w + (ri % 3) * 0.17 * tile_w
    cu = (along + off) / tile_w
    ci = np.floor(cu)
    fu = cu - ci
    basev = np.array(rgb(base), float)
    gapv = np.array(rgb(gap), float) if gap is not None else basev * 0.45
    ts = 1 + (_hash2(ri, ci, plane) - 0.5) * 2 * var * 1.7
    tint = (np.stack([_hash2(ri + 3.1, ci, plane), _hash2(ri, ci + 5.3, plane), _hash2(ri + 1.7, ci + 2.9, plane)],
                     -1) - 0.5) * var * 0.7
    kk = ts[..., None] * (1 + tint)
    kk = kk * (1.08 - 0.28 * np.clip((fv - lip) / (1 - lip), 0, 1))[..., None]
    kk = kk * (1 + grain * (rng.random((nv, nu)) - 0.5))[..., None]
    col = basev[None, None] * kk
    line = (fv < 0.12) | (fu < 0.05)
    col[line] = col[line] * 0.4 + gapv * 0.6
    if slipped > 0:
        # a few slabs gone dark where they slid out of their course
        gone = _hash2(ri + 9.7, ci + 4.1, plane) < slipped
        col[gone] = col[gone] * 0.35
    Sq = max(nu, nv)
    if moss is not None and moss_amt > 0:
        nm = (_smooth(rng, Sq, 6) * 0.6 + _smooth(rng, Sq, 17) * 0.4)[:nv, :nu]
        m = np.clip((nm - (1 - moss_amt)) / 0.12, 0, 1)[..., None]
        col = col * (1 - m) + np.array(rgb(moss), float) * m * (0.8 + 0.4 * rng.random((nv, nu, 1)))
    col = col * dark
    tot = np.zeros((nv, nu))
    if soot > 0:
        n1 = (_smooth(rng, Sq, 3) * 0.65 + _smooth(rng, Sq, 7) * 0.35)[:nv, :nu]
        band = 0.5 + 0.5 * np.cos(2 * math.pi * n1 * swirl)
        streak = np.clip((band - (1 - width)) / (width * 0.7), 0, 1) ** 1.2
        cov = np.clip((_smooth(rng, Sq, 2)[:nv, :nu] - 0.25) / 0.35, 0, 1)
        tot = np.maximum(tot, soot * streak * (0.35 + 0.65 * cov))
    swirl_tot = tot.copy()
    for hx_, hy_, rad, st in halos:
        lu, lv = g.local(hx_, hy_)
        d = np.sqrt((u - lu) ** 2 + (v - lv) ** 2)
        tot = np.maximum(tot, st * np.clip(1 - d / rad, 0, 1) ** 0.8)
    sc = np.array(rgb(soot_col), float)
    t = np.clip(tot, 0, 1)[..., None]
    col = col * (1 - t) + sc * t * (0.8 + 0.4 * rng.random((nv, nu, 1)))
    if core > 0:
        # the thickest soot of the swirls burnt near black, the haloes left
        tc = core * np.clip((swirl_tot - 0.18) / 0.22, 0, 1)[..., None]
        col = col * (1 - tc) + np.array(rgb(core_col), float) * tc
    img = _to_image(name, np.clip(col, 0, 255))
    hx, hy = g.hx, g.hy

    def fn(x, y, g=g, hx=hx, hy=hy):
        lu, lv = g.local(x, y)
        return ((lu + hx) / (2 * hx), (lv + hy) / (2 * hy))
    UVFN[name] = fn
    return img


def roof_mat(name, g, base, **kw):
    """A material for roof g painted as a roof map (tex_roofmap's keys)."""
    img = tex_roofmap(name, g, base, **kw)
    return Mat(name, tex=img, uvfn=name, ref=base)


def hip_caps(B, mat, g, w=0.2, lift=0.05, ridge=True):
    """Capping timbers down the hips of roof g to its ridge, and along the
    ridge, so the hip lines read."""
    for i, ex in enumerate((-1, 1)):
        if g.ends[i] == "gable":
            continue
        for ey in (-1, 1):
            B.beam(mat, g.world(ex * g.hx, ey * g.hy, g.ze + lift), g.world(ex * g.rxs[i], 0, g.zr + lift), w, w * 0.7)
    if ridge and min(g.rxs) > 0.05:
        B.beam(mat, g.world(-g.rxs[0], 0, g.zr + lift), g.world(g.rxs[1], 0, g.zr + lift), w, w * 0.7)


def hole_centre(r, g, poly):
    """The roof point at the middle of a hole traced on the sprite, and the
    hole's rough radius in cells."""
    c = sum(p[0] for p in poly) / len(poly)
    rw = sum(p[1] for p in poly) / len(poly)
    h = g.hit(r, c, rw)
    if h is None:
        return None
    pts = [g.hit(r, a, b) for a, b in poly]
    pts = [p for p in pts if p is not None]
    rad = max(math.hypot(p[0] - h[0], p[1] - h[1]) for p in pts) if pts else 1.0
    return h[0], h[1], rad


def sag_fill(B, mat, r, g, poly, sag=0.5, cell=0.3, noise=0.06, seed=0, edge=0.8):
    """What lies inside a hole broken through roof g where the sprite of r
    draws outline poly: charred stuff sagging from the hole's edges to sag
    below the roof plane in its middle, so the hole shows a floor and not a
    void and nothing rises through the roof. Returns its height function
    (-1e9 off it) and the plan outline."""
    rng = random.Random(seed)
    Pp = []
    for c, rw in poly:
        h = g.hit(r, c, rw)
        if h is not None:
            Pp.append((h[0], h[1]))
    if len(Pp) < 3:
        return (lambda x, y: -1e9), Pp
    xs, ys = [p[0] for p in Pp], [p[1] for p in Pp]
    x0, x1, y0, y1 = min(xs) - cell, max(xs) + cell, min(ys) - cell, max(ys) + cell
    nx, ny = max(2, int((x1 - x0) / cell) + 1), max(2, int((y1 - y0) / cell) + 1)

    def dist(x, y):
        best = 1e9
        p = Vector((x, y))
        for i in range(len(Pp)):
            a, b = Vector(Pp[i]), Vector(Pp[i - 1])
            d = b - a
            t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
            best = min(best, (a + d * t - p).length)
        return best

    def ins(x, y):
        t = g.top(x, y)
        if t is None:
            return False, None
        return inside_poly(poly, *screen(r, x, y, t)), t

    def fill(x, y):
        i, t = ins(x, y)
        if not i:
            return -1e9
        return t - g.T * 0.5 - sag * min(1.0, dist(x, y) / edge) ** 0.8
    verts, faces, grid, inn = [], [], {}, {}
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = x0 + i * (x1 - x0) / nx + (rng.random() - 0.5) * cell * 0.3
            y = y0 + j * (y1 - y0) / ny + (rng.random() - 0.5) * cell * 0.3
            ok, t = ins(x, y)
            if ok:
                z = fill(x, y) + (rng.random() - 0.5) * 2 * noise
            else:
                z = (t if t is not None else g.ze) - g.T * 0.6
            grid[i, j] = len(verts)
            inn[i, j] = ok
            verts.append((x, y, z))
    for j in range(ny):
        for i in range(nx):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            if not any(inn[k] for k in q):
                continue
            ids = [grid[k] for k in q]
            faces.append((ids[0], ids[1], ids[2]))
            faces.append((ids[0], ids[2], ids[3]))
    B.mesh(mat, verts, faces)
    return fill, Pp


# ---------------------------------------------------------------- props and thatch

def open_tub(B, wall, hoop, water, x, y, r=0.8, h=0.55, t=0.08, fill=0.1, seg=18, burnt=None):
    """An open round tub: staved wall with a rim you can see, two hoops,
    and water standing a little below the rim (or a charred floor)."""
    zw = h - fill
    prof = [(0.0, 0.0), (r * 0.93, 0.0), (r, h), (r - t, h), (r - t * 1.1, zw - 0.02), (0.0, zw - 0.02)]
    B.lathe(wall, x, y, prof, seg=seg)
    B.cyl(burnt or water, x, y, zw - 0.03, r - t * 1.1 + 0.01, 0.03, seg=seg)
    for zf in (0.15, 0.78):
        rr = r * 0.93 + r * 0.07 * zf + 0.015
        B.cyl(hoop, x, y, h * zf - 0.03, rr, 0.06, seg=seg)


def pillow(B, mat, cx, cy, hx, hy, zfn, prof, p=4.0, seg=32, jitter=0.0, seed=0):
    """A rounded mass over a rounded rectangle in plan (a superellipse of
    power p, half sizes hx by hy): prof is [(scale, dz)] from its lower
    edge round to its top middle, each ring at zfn(x, y) + dz. Returns
    surf(a, t) as dome's: the point, normal and outward at angle a and
    fraction t of the way up the profile."""
    rng = random.Random(seed)

    def ring(a):
        c, s = math.cos(a), math.sin(a)
        return (hx * math.copysign(abs(c) ** (2 / p), c), hy * math.copysign(abs(s) ** (2 / p), s))
    v, rings = [], []
    nk = len(prof)
    for k, (sc, dz) in enumerate(prof):
        if sc < 1e-4:
            rings.append([len(v)])
            v.append((cx, cy, zfn(cx, cy) + dz))
            continue
        ids = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            ex, ey = ring(a)
            j = 1 + jitter * (rng.random() - 0.5) * 2 * (1 - k / nk)
            x, y = cx + ex * sc * j, cy + ey * sc * j
            ids.append(len(v))
            v.append((x, y, zfn(x, y) + dz + jitter * 0.5 * (rng.random() - 0.5)))
        rings.append(ids)
    f = [tuple(reversed(rings[0]))]
    for a_, b_ in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            q = [a_[i % len(a_)], a_[j % len(a_)], b_[j % len(b_)], b_[i % len(b_)]]
            q = [pp for n_, pp in enumerate(q) if pp not in q[:n_]]
            if len(q) >= 3:
                f.append(tuple(q))
    B.solid(mat, v, f)
    ts = np.linspace(0, 1, nk)

    def surf(a, t):
        sc = float(np.interp(t, ts, [q[0] for q in prof]))
        dz = float(np.interp(t, ts, [q[1] for q in prof]))
        ex, ey = ring(a)
        x, y = cx + ex * sc, cy + ey * sc
        pnt = Vector((x, y, zfn(x, y) + dz))
        t2 = min(1.0, t + 0.03)
        sc2 = float(np.interp(t2, ts, [q[0] for q in prof]))
        dz2 = float(np.interp(t2, ts, [q[1] for q in prof]))
        x2, y2 = cx + ex * sc2, cy + ey * sc2
        up = Vector((x2, y2, zfn(x2, y2) + dz2)) - pnt
        out = Vector((ex, ey, 0)).normalized()
        side = out.cross(Vector((0, 0, 1)))
        nrm = side.cross(up).normalized() if up.length > 1e-6 else Vector((0, 0, 1))
        if nrm.dot(out) + nrm.z < 0:
            nrm = -nrm
        return pnt, nrm, out
    return surf


# ---------------------------------------------------------------- fire damage, shared by the damaged halls

def debris_mats(n, pic, seed=0, box=None, k=6, gain=1.0, sat=1.3, lit=0.35):
    """Timbers, slabs, boards and stones in the colours the sprite draws its
    rubble in (all of it, or inside box)."""
    cols = palette(pic, k=k, seed=seed, box=box, lit=lit)
    slab = swatches(n + "_ds", cols, "mottle", seed=seed, gain=gain, sat=sat)
    wood = swatches(n + "_dw", [c for c in cols if c[0] >= c[2]] or cols, "planks", seed=seed + 20, gain=gain,
                    sat=sat)
    return {"slab": slab, "tile": slab, "stone": slab, "chunk": slab, "beam": wood, "board": wood,
            "boulder": slab}


def sooty_roof(name, g, r, base, holes=(), halo=1.6, **kw):
    """A roof map for roof g darkened, streaked with soot in broad swirls
    and haloed black round the holes (outlines on the sprite of r)."""
    halos = []
    for q in holes:
        h = hole_centre(r, g, q)
        if h is not None:
            halos.append((h[0], h[1], h[2] * halo + 0.9, 0.95))
    d = dict(course=0.27, tile_w=0.85, var=0.15, lip=0.18, soot=0.9, swirl=2.5, width=0.45, dark=0.85)
    d.update(kw)
    return roof_mat(name, g, base, halos=halos, **d)


def hole_insides(B, r, pic, char, dm, g, holes, sag=0.8, n=18, seed=0, kinds=None, size=(0.3, 0.7), cell=0.4):
    """Each hole broken through roof g: a charred floor sagging below the
    roof plane (never a void, never rising through it) with n charred
    pieces lying in it."""
    for i, q in enumerate(holes):
        fill, Pp = sag_fill(B, char, r, g, q, sag=sag, cell=cell, noise=0.05, seed=seed + i, edge=0.9)
        if len(Pp) >= 3 and n:
            cover(B, dm, r, Pp, fill, n, kinds or {"beam": 3, "board": 2, "slab": 3, "stone": 2}, pic=pic,
                  size=size, length=(0.8, 1.8), seed=seed + 10 + i, lift=0.04, tilt=20, grow=0,
                  stone=(0.2, 0.4), jumble=0.15)


def bed(B, mat, r, pic, poly, top, z_at=0.3, cell=0.5, edge=1.0, seed=0, gaps=True, amp=2.0):
    """A low bed of ash under a debris field, only where the sprite draws
    something, so the ground shows through its gaps as it does there."""
    return heap(B, mat, r, jag(poly, amp, 4.0, seed), top, z_at=z_at, cell=cell, noise=0.04, seed=seed + 1,
                edge=edge, pic=pic, grow=0 if gaps else 1, strict=gaps)


def radial_slabs(B, mats, r, pic, ground, centre, P, n, size=(0.7, 1.3), seed=0, lap=0.12, q=0.7, step=0.012):
    """Roof slabs slid down a sagged roof: each lies on ground(x, y) inside
    plan polygon P with its long side pointing at centre (the finial), the
    ones nearer the centre lapping over the outer ones."""
    rng = random.Random(seed)
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    cx, cy = centre
    placed = k = 0
    pieces = []
    while placed < n and k < n * 60:
        k += 1
        x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
        if not inside_poly(P, x, y):
            continue
        z0 = ground(x, y)
        if z0 < -50:
            continue
        d = Vector((x - cx, y - cy, 0))
        if d.length < 0.3:
            continue
        a = math.atan2(d.y, d.x) + math.radians(rng.uniform(-20, 20))
        t1 = Vector((math.cos(a), math.sin(a), 0))
        t2 = Vector((-t1.y, t1.x, 0))
        L, W = rng.uniform(*size), rng.uniform(*size) * 0.6
        e = 0.1
        # the slab lies along the surface's slope under it
        pts = []
        for u, v in ((-L / 2, -W / 2), (L / 2, -W / 2), (L / 2, W / 2), (-L / 2, W / 2)):
            px_, py_ = x + t1.x * u + t2.x * v, y + t1.y * u + t2.y * v
            gz = ground(px_, py_)
            pts.append(Vector((px_, py_, (gz if gz > -50 else z0) + e)))
        # nearer the centre is higher in the stack: the outer end tucks under
        dist = d.length
        lift = lap * (1.0 / (1.0 + dist)) + rng.uniform(0, 0.05)
        pts[0].z += lift
        pts[3].z += lift
        if pic is not None and not all(pic.sees(tuple(p), 0) for p in pts):
            continue
        col = pic.colour(*screen(r, x, y, z0), q=q) if pic is not None else None
        _, mat = _pick(rng, {"slab": mats}, {"slab": 1}, col)
        pieces.append((dist, mat, [tuple(p) for p in pts], rng.uniform(0.05, 0.09)))
        placed += 1
    # each slab rides a little above the ones under it and is tipped a
    # touch at random, so no two slab faces lie in one plane
    laid = []
    for dist, mat, pts, th in sorted(pieces, key=lambda p: -p[0]):
        c = Vector((sum(p[0] for p in pts) / 4, sum(p[1] for p in pts) / 4, 0))
        rad = max((Vector((p[0], p[1], 0)) - c).length for p in pts)
        off = max([o + step for c2, r2, o in laid if (c - c2).length < (rad + r2) * 0.85] + [0.0])
        laid.append((c, rad, off))
        B.slab(mat, [(p[0], p[1], p[2] + off + rng.uniform(0.0, 0.02)) for p in pts], th)
    return placed


# ---------------------------------------------------------------- output

def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def export_projection(ob, r, path):
    """The model's triangles as the classic camera draws them, in the
    sprite's pixels, for measuring how well the silhouette covers it."""
    me = ob.data
    me.calc_loop_triangles()
    hx, hy = r["sprite"]["hotspot"]
    T = []
    for t in me.loop_triangles:
        q = []
        for vi in t.vertices:
            x, y, z = ob.matrix_world @ me.vertices[vi].co
            q += [round(hx + 16 * x, 2), round(hy - 16 * y - 8 * z, 2)]
        T.append(q)
    json.dump({"name": r["name"], "w": r["sprite"]["w"], "h": r["sprite"]["h"], "tris": T}, open(path, "w"))


def build_one(name, fn, render=True):
    """Build one feature with fn(B, r) -> list of extra objects, export it
    and render the compare views."""
    hk.reset()
    r = CAT[name]
    B = Builder(seed=sum(map(ord, name)))
    extra = fn(B, r) or []
    obs = B.objects() + list(extra)
    obs = [o for o in obs if o.type == "MESH" and len(o.data.polygons)]
    if os.environ.get("AB_TRIS"):
        for o in sorted(obs, key=tris, reverse=True)[:12]:
            print("AB_PART", name, o.name, tris(o), flush=True)
    glb = os.path.join(OUT, "models", name + ".glb")
    ob = hk.finish(obs, glb)
    ob.name = name
    n = tris(ob)
    export_projection(ob, r, os.path.join(OUT, "renders", name + "_proj.json"))
    if render:
        hk.renders(ob, os.path.join(OUT, "renders"), name, os.path.join(DATA, "sprites", name + ".png"),
                   tuple(r["sprite"]["hotspot"]), scale=2)
    print("AB_BUILT", name, "tris", n, "size %.2f x %.2f x %.2f" % tuple(ob.dimensions), flush=True)
    return ob
