"""Creon's destroyed buildings, group 1: the palace (CreBuild01), the great
hall (CreBuild02) and the senate (CreBuild03), each as a ruin (the 'a'
stage, destructible again) and a burnt-out shell (the 'b' stage, the end).
Run inside Blender:

    OK_REPLACE=<folder with sprites/ and catalog.json> \
    blender -b --factory-startup --python g1.py -- [Name ...] [--out DIR] [--norender]

Each model goes to DIR/<Name>.glb (DIR defaults to $OK_STAGE_OUT, else
$OK_REPLACE/hand/creon_stages), its classic and turned renders to
DIR/renders. build(spr, spec) builds one stage for hand/creon/build.py.

Same frame as the creon kit: one unit is one map cell, -Y toward the
classic camera, Z up, the origin at the anchor. A ruin is its intact
building broken: the palace comes from hand/creon/palace.py's own shapes
and palette, the hall and the senate from intact builders here (hall_parts,
senate_parts) read off their intact pictures, and every stage of one
building shares that building's frame, so walls that still stand sit where
the intact's do. What the stage picture shows gone is cut away along the
classic camera's line of sight (the Aramon kit's cut_view) with outlines
traced on the stage picture in its own pixels, and rubble is heaped only
inside outlines traced there.

Materials are the intact's, darkened by soot through the vertex colour
creon kit.material multiplies, and big flat tops carry a plan-mapped tile with
their soot drawn in. Faces that lay inside the intact hall or senate (under
its roof, off its outer faces) darken further, so a broken dome or vault
opens onto a burnt-out interior. Every texture is made here from noise and
numbers.

The heaps follow the pictures' masonry: a dark bed strewn with pale chips,
pieces leaning to the lit tones, and burns that only lightly touch a
piece's top. Pieces placed at a picture pixel must land inside their heap,
and a build gives the same file every run.
"""
import copy
import importlib.util
import json
import math
import os
import random
import sys
import time
import traceback
import zlib

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
CREON = os.path.join(TOOLS, "hand", "creon")
for p in (CREON, TOOLS):
    if p not in sys.path:
        sys.path.insert(0, p)

import handkit as hk  # noqa: E402
import kit  # noqa: E402
import palace as pal  # noqa: E402


def _load_akit():
    """The Aramon kit under its own name (both kits are kit.py), pointed at
    the creon sprites and catalog."""
    spec = importlib.util.spec_from_file_location("akit", os.path.join(TOOLS, "hand", "aramon_buildings", "kit.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    m.DATA = kit.ROOT
    m.OUT = kit.ROOT + "/hand/creon_stages"
    m.CAT.update({r["name"]: r for r in json.load(open(kit.CATALOG))})
    return m


akit = _load_akit()

# pieces laid per cell of heap and shards per heap, scaled together to
# keep a building stage near the scenery budget
DENSITY = 0.64
NAMES = ("CreBuild01a", "CreBuild01b", "CreBuild02a", "CreBuild02b", "CreBuild03a", "CreBuild03b")
LUMA = np.array([0.299, 0.587, 0.114])


def lin(c):
    """A drawn colour (#rrggbb, 0-255 or 0-1 triple) as linear rgb."""
    if isinstance(c, str):
        c = akit.rgb(c)
    c = np.asarray(c, float)
    if c.max() > 1.0:
        c = c / 255.0
    return kit.srgb_to_lin(c)


def to_srgb(a):
    a = np.clip(a, 0, 1)
    return np.where(a <= 0.0031308, a * 12.92, 1.055 * a ** (1 / 2.4) - 0.055)


def albedo(c, tex_mean=1.0):
    """The vertex colour that brings a surface out near drawn colour c in
    handkit's light, over a tile whose mean (linear) is tex_mean."""
    return np.clip(np.asarray(akit.albedo(akit.rgb(c) if isinstance(c, str) else tuple(c))) / tex_mean, 0, 1)


# ---------------------------------------------------------------- materials

class Mat:
    """A creon material (vertex colour times a generated tile) dressed as
    the Aramon kit's Mat, so its builders can lay pieces in it. colour is
    linear, ref the drawn colour it stands for (to match the picture),
    soot how strongly the soot field darkens it. lay=False keeps the UVs a
    piece brings (a dome's ribs run round it)."""

    def __init__(self, name, tex=None, colour=(1, 1, 1), uv=2.5, rough=0.9, metal=0.0, ref=None, lay=True,
                 soot=1.0, share=None):
        self.name, self.uv, self.uvfn = name, uv, None
        if share is not None:
            # the same material as share, its tone carried by the vertex colour
            self.m, tex = share.m, True
        else:
            self.m = kit.material(name, tex, rough=rough)
            if metal:
                self.m.node_tree.nodes["Principled BSDF"].inputs["Metallic"].default_value = metal
        self.textured = tex is not None and lay
        self.colour = np.asarray(colour, float)
        self.ref = akit.rgb(ref) if ref is not None else tuple(to_srgb(self.colour) * 255)
        self.soot = soot


def tex_char(seed, size=256):
    """Charred planks: grain darkened to about 0.55 with the checked cracks
    of burnt wood across it."""
    rng = np.random.default_rng(seed)
    u = (np.arange(size) + 0.5) / size
    k = np.floor(u * 4)
    shade = 0.8 + 0.2 * rng.random(4)
    streak = kit.norm01(kit._blur_x(rng.standard_normal((size, size)).T, 8.0).T)
    g = shade[k.astype(int)][None, :] * (0.75 + 0.3 * streak)
    v = (np.arange(size) + 0.5) / size
    checks = np.clip(1 - np.abs((v[:, None] * 22 + 0.3 * kit.fbm(rng, size, [(4.0, 1.0)])) % 1 - 0.5) * 14, 0, 1)
    g = np.clip(0.55 * g - 0.25 * checks + 0.12 * kit.fbm(rng, size, [(1.0, 1.0)]), 0.08, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def tex_rubble(seed, size=256, chips=78):
    """Broken masonry seen close, for heaps' skins: packed stones three to
    six sprite pixels across (a repeat spans about 1.6 cells) covering about
    half the tile, each lit on its upper side and ringed by a dark joint,
    over dark grit."""
    rng = np.random.default_rng(seed)
    g = 0.22 + 0.1 * kit.fbm(rng, size, [(1.5, 0.6), (5.0, 0.4)])
    yy, xx = np.mgrid[0:size, 0:size]
    for _ in range(chips):
        cx, cy = rng.uniform(0, size, 2)
        rad = rng.uniform(15.0, 30.0)
        a = rng.uniform(0, math.pi)
        sq = rng.uniform(0.55, 0.9)
        dx = (xx - cx + size / 2) % size - size / 2
        dy = (yy - cy + size / 2) % size - size / 2
        u, v = dx * math.cos(a) + dy * math.sin(a), -dx * math.sin(a) + dy * math.cos(a)
        d = np.abs(u) / rad + np.abs(v) / (rad * sq)
        g = np.where(d < 1.18, 0.1, g)
        lit = rng.uniform(0.62, 1.0)
        g = np.where(d < 1.0, lit - 0.22 * np.clip(dy / rad, 0, 1) - 0.08 * d, g)
    g = np.clip(g, 0.05, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def tex_tiles(seed, size=256, rows=8, cols=8, gap=0.06):
    """Glazed roof tiles in a square grid, each a little different, dark
    joints between: the hall's and the senate's roofs."""
    rng = np.random.default_rng(seed)
    u = (np.arange(size) + 0.5) / size
    iu, iv = np.floor(u * cols).astype(int), np.floor(u * rows).astype(int)
    fu, fv = (u * cols) % 1, (u * rows) % 1
    shade = 0.82 + 0.18 * rng.random((rows, cols))
    g = shade[iv[:, None], iu[None, :]]
    g = g * (0.92 + 0.12 * fv[:, None])  # each course lit at its lower lip
    joint = (np.minimum(fu, 1 - fu)[None, :] < gap) | (np.minimum(fv, 1 - fv)[:, None] < gap * 1.2)
    g = np.where(joint, g * 0.45, g) * (0.92 + 0.1 * kit.fbm(rng, size, [(2.0, 1.0)]))
    return np.dstack([g, g, g, np.ones_like(g)])


def tex_mean(arr):
    """A tile's mean linear grey."""
    return float(kit.srgb_to_lin(arr[..., 0]).mean())


# ---------------------------------------------------------------- soot

class Soot:
    """How dark soot leaves a point: burns are (x, y, z, radius, strength)
    halos round breaches and heaps, zones are (plan polygon, strength,
    softness) darkened as a whole. Blotches and streaks running up from
    openings come from smooth noise. Returns a factor in (0.1, 1]."""

    def __init__(self, seed, base=0.9, burns=(), zones=(), blotch=0.35, streak=0.3, freq=0.45, inside=None, gut=0.0,
                 top_cap=None):
        self.base, self.burns, self.zones = base, list(burns), list(zones)
        self.blotch, self.streak, self.freq = blotch, streak, freq
        # inside(co, n) -> 0..1, how far a point lies in the gutted interior
        self.inside, self.gut = inside, gut
        # the most any burn, zone or blotch darkens a face turned up to the sky
        self.top_cap = top_cap
        r = random.Random(seed)
        self.off = Vector((r.uniform(0, 100), r.uniform(0, 100), r.uniform(0, 100)))

    def __call__(self, co, n=None):
        f = self.base
        cap = self.top_cap if (self.top_cap is not None and n is not None and n.z > 0.5) else 1.0
        if self.inside is not None and n is not None and self.gut:
            f *= 1 - self.gut * self.inside(co, n)
        for x, y, z, rad, s in self.burns:
            d2 = (co.x - x) ** 2 + (co.y - y) ** 2 + ((co.z - z) * 0.7) ** 2
            f *= 1 - min(s, cap) * math.exp(-d2 / (rad * rad))
        for P, s, soft in self.zones:
            d = poly_dist(P, co.x, co.y)
            k = 1.0 if akit.inside_poly(P, co.x, co.y) else math.exp(-(d / soft) ** 2)
            f *= 1 - min(s, cap) * k
        b = noise.noise(Vector((co.x, co.y, co.z)) * self.freq + self.off)
        f *= 1 - min(self.blotch, cap) * min(1.0, max(0.0, b * 1.6 + 0.25))
        st = noise.noise(Vector((co.x * 1.7, co.y * 1.7, co.z * 0.22)) + self.off * 1.3)
        f *= 1 - self.streak * min(1.0, max(0.0, st * 2.0))
        return max(0.08, min(1.0, f))

    def plane(self, X, Y, z):
        """The same field over plan grids X, Y at height z, as an array."""
        f = np.full(X.shape, self.base)
        for x, y, zz, rad, s in self.burns:
            d2 = (X - x) ** 2 + (Y - y) ** 2 + ((z - zz) * 0.7) ** 2
            f *= 1 - s * np.exp(-d2 / (rad * rad))
        for P, s, soft in self.zones:
            ins = raster_poly(P, X, Y)
            d = poly_dist_grid(P, X, Y)
            k = np.where(ins, 1.0, np.exp(-(d / max(soft, 1e-3)) ** 2))
            f *= 1 - s * k
        b = np.vectorize(lambda a, c: noise.noise(Vector((a, c, z)) * self.freq + self.off))(X, Y)
        f *= 1 - self.blotch * np.clip(b * 1.6 + 0.25, 0, 1)
        # a flat top may go as black as the shadows the pictures draw on it
        return np.clip(f, 0.01, 1.0)


def poly_dist(P, x, y):
    best = 1e9
    for i in range(len(P)):
        ax, ay = P[i]
        bx, by = P[i - 1]
        dx, dy = bx - ax, by - ay
        L2 = dx * dx + dy * dy
        t = 0.0 if L2 < 1e-12 else max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / L2))
        best = min(best, math.hypot(ax + dx * t - x, ay + dy * t - y))
    return best


def poly_dist_grid(P, X, Y):
    """poly_dist over grids X, Y."""
    best = np.full(X.shape, 1e9)
    for i in range(len(P)):
        ax, ay = P[i]
        bx, by = P[i - 1]
        dx, dy = bx - ax, by - ay
        L2 = max(dx * dx + dy * dy, 1e-12)
        t = np.clip(((X - ax) * dx + (Y - ay) * dy) / L2, 0, 1)
        best = np.minimum(best, np.hypot(ax + dx * t - X, ay + dy * t - Y))
    return best


def raster_poly(P, X, Y):
    """Which points of grids X, Y lie inside plan polygon P."""
    ins = np.zeros(X.shape, bool)
    for i in range(len(P)):
        ax, ay = P[i]
        bx, by = P[i - 1]
        cond = (ay > Y) != (by > Y)
        xi = (bx - ax) * (Y - ay) / np.where(by - ay == 0, 1e-12, by - ay) + ax
        ins ^= cond & (X < xi)
    return ins


# ---------------------------------------------------------------- geometry

def put(B, mat, bm, keep_uv=False, M=None):
    """A creon kit bmesh (palace shapes) laid into Builder B in mat."""
    dst = B._bm(mat)
    T = B.M @ (M or Matrix.Identity(4))
    bm.verts.index_update()
    vs = [dst.verts.new(T @ v.co) for v in bm.verts]
    src_uv = bm.loops.layers.uv.active if keep_uv else None
    dst_uv = dst.loops.layers.uv.verify() if keep_uv else None
    for f in bm.faces:
        try:
            nf = dst.faces.new([vs[v.index] for v in f.verts])
        except ValueError:
            continue
        if src_uv is not None:
            for la, lb in zip(f.loops, nf.loops):
                lb[dst_uv].uv = la[src_uv].uv
    bm.free()


def objs(B):
    """B's objects, each with its Mat."""
    mats = [B.mats[k] for k in B.bms]
    return list(zip(B.objects(), mats))


def dice_box(x0, x1, y0, y1, z0, z1, step, top=True):
    """A box cut into a grid about step cells apart, so soot painted on
    its vertices can vary over its faces. top=False leaves its lid off."""
    bm = pal.prism(x0, x1, y0, y1, z0, z1)
    for axis, a, b in ((0, x0, x1), (1, y0, y1), (2, z0, z1)):
        n = max(1, int(round((b - a) / step)))
        for i in range(1, n):
            co = [0.0, 0.0, 0.0]
            no = [0.0, 0.0, 0.0]
            co[axis] = a + (b - a) * i / n
            no[axis] = 1.0
            geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    if not top:
        bm.normal_update()
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(v.co.z > z1 - 1e-4 for v in f.verts)],
                         context="FACES_ONLY")
    return bm


def stump(B, mat, cx, cy, r, h, jag=0.35, seed=0, seg=16, z0=0.0, sink=0.25):
    """A round tower broken off: its top ring torn to uneven heights and
    sunk toward the middle as rubble fills its shaft."""
    rng = random.Random(seed)
    v = []
    for k in range(seg):
        a = 2 * math.pi * k / seg
        v.append((cx + r * math.cos(a), cy + r * math.sin(a), z0))
    for k in range(seg):
        a = 2 * math.pi * k / seg
        hh = h + rng.uniform(-1, 1) * jag - (jag if rng.random() < 0.25 else 0)
        v.append((cx + r * math.cos(a), cy + r * math.sin(a), z0 + max(0.2, hh)))
    v.append((cx, cy, z0 + h - sink))
    f = [tuple(range(seg - 1, -1, -1))]
    f += [(k, (k + 1) % seg, seg + (k + 1) % seg, seg + k) for k in range(seg)]
    f += [(seg + k, seg + (k + 1) % seg, 2 * seg) for k in range(seg)]
    B.solid(mat, v, f)


def oct_ring(B, mat, cx, cy, r, t, z0, heights, seed=0, jitter=0.15, notch=0.2, step=0.6):
    """A hollow octagon (vertices at k 45 degrees, as palace.octagon makes
    it) whose eight sides stand to the heights given, broken tops ragged,
    the outer faces sit where the solid octagon's do."""
    rin = r - t / 2
    for k, h in enumerate(heights):
        if h <= 0.05:
            continue
        a0, a1 = math.pi / 4 * k, math.pi / 4 * (k + 1)
        p0 = (cx + rin * math.cos(a0), cy + rin * math.sin(a0))
        p1 = (cx + rin * math.cos(a1), cy + rin * math.sin(a1))
        prof = h if isinstance(h, (list, tuple)) else [(0, h), (1, h)]
        akit.broken_wall(B, mat, mat, p0, p1, t * 1.08, [(f, z0 + hh) for f, hh in prof], z0=z0, step=step,
                         jitter=jitter, seed=seed + k, notch=notch)


def first_hit(r, col, row, zfn, ztop=24.0, step=0.04):
    """Where the line of sight through picture pixel (col, row) first meets
    the surface zfn(x, y) coming down from above: (x, y, z)."""
    z = ztop
    while z > -2.0:
        x, y = akit.px(r, col, row, z)
        g = zfn(x, y)
        if g > -50 and g >= z:
            # back up to the crossing between this step and the last
            lo, hi = z, z + step
            for _ in range(12):
                mid = (lo + hi) / 2
                xm, ym = akit.px(r, col, row, mid)
                gm = zfn(xm, ym)
                if gm > -50 and gm >= mid:
                    lo = mid
                else:
                    hi = mid
            x, y = akit.px(r, col, row, lo)
            return x, y, lo
        z -= step
    x, y = akit.px(r, col, row, 0.0)
    return x, y, 0.0


def plan(r, poly, zfn):
    """Picture pixels as plan points where each one's line of sight first
    meets zfn(x, y) (the surface a heap lies on plus its height). A point
    given as (col, row, z) is read at height z instead."""
    out = []
    for p in poly:
        if len(p) == 3:
            out.append(akit.px(r, p[0], p[1], p[2]))
            continue
        x, y, _ = first_hit(r, p[0], p[1], zfn)
        out.append((x, y))
    return out


class Field:
    """A heap's surface over a plan polygon: heights on a grid, to lay
    pieces on (cover) and to soot by."""

    def __init__(self, x0, y0, cell, Z, ins, mesh=None, on=None):
        self.x0, self.y0, self.cell, self.Z, self.ins = x0, y0, cell, Z, ins
        # the grid points the bed is built on and where it may lie: no piece
        # lies where the bed is not
        self.mesh = ins if mesh is None else mesh
        self.on = on

    def __call__(self, x, y):
        if self.on is not None and not self.on(x, y):
            return -100.0
        i = (x - self.x0) / self.cell
        j = (y - self.y0) / self.cell
        i0, j0 = int(math.floor(i)), int(math.floor(j))
        ny, nx = self.Z.shape
        if not (0 <= i0 < nx - 1 and 0 <= j0 < ny - 1):
            return -100.0
        if not (self.ins[j0, i0] or self.ins[j0, i0 + 1] or self.ins[j0 + 1, i0] or self.ins[j0 + 1, i0 + 1]):
            return -100.0
        if not (self.mesh[j0, i0] and self.mesh[j0, i0 + 1] and self.mesh[j0 + 1, i0] and self.mesh[j0 + 1, i0 + 1]):
            return -100.0
        fi, fj = i - i0, j - j0
        Z = self.Z
        return float(Z[j0, i0] * (1 - fi) * (1 - fj) + Z[j0, i0 + 1] * fi * (1 - fj)
                     + Z[j0 + 1, i0] * (1 - fi) * fj + Z[j0 + 1, i0 + 1] * fi * fj)


# the steepest a heap's bed may stand, rise over run: 40 degrees between
# points, so no face of it passes 45, as loose rubble rests
SLOPE = 0.85
N4 = ((1, 0), (-1, 0), (0, 1), (0, -1))
N8 = N4 + ((1, 1), (1, -1), (-1, 1), (-1, -1))


def crossing(fn, a, b, n=10):
    """The point on the segment from a (where fn(x, y) holds) to b (where
    it does not) at which it stops holding."""
    lo, hi = 0.0, 1.0
    for _ in range(n):
        m = (lo + hi) / 2
        if fn(a[0] + (b[0] - a[0]) * m, a[1] + (b[1] - a[1]) * m):
            lo = m
        else:
            hi = m
    m = (lo + hi) / 2
    return a[0] + (b[0] - a[0]) * m, a[1] + (b[1] - a[1]) * m


def mound(B, mat, P, base, top, cell=0.6, edge=None, rough=0.2, seed=0, keep=None, lumps=0.5, on=None, lean=None,
          zmax=None, slope=SLOPE):
    """A rubble heap over plan polygon P lying on the surface base(x, y):
    top cells high in its middle, lumpy, and nowhere steeper than slope.
    It lies only where on(x, y) holds (a terrace's top) and ends at that
    region's rim on the level it lies on. Where lean(x, y) holds it meets a
    wall and runs up to it at its own height. zmax(x, y) is a ceiling (a
    broken drum's rim and the slope up from it). keep(x, y, z) drops points
    the picture does not draw, so the ground shows through its gaps. Its
    edge lies on the outline. Returns its surface."""
    rng = random.Random(seed)
    off = Vector((rng.uniform(0, 50), rng.uniform(0, 50), 0))
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    x0, y0 = min(xs) - cell, min(ys) - cell
    nx, ny = int((max(xs) - x0) / cell) + 3, int((max(ys) - y0) / cell) + 3
    X = x0 + np.arange(nx) * cell
    Y = y0 + np.arange(ny) * cell
    GX, GY = np.meshgrid(X, Y)
    # it rises over about its own height from the outline, the slope cap holding it to 40 degrees
    edge = max(0.5, top) if edge is None else edge

    def leans(x, y):
        return lean is not None and lean(x, y)

    def lies(x, y):
        return akit.inside_poly(P, x, y) and (on is None or on(x, y)) and not leans(x, y)

    def low(x, y):
        b = base(x, y)
        return b if zmax is None else min(b, zmax(x, y) - 0.12)

    cells = [(j, i) for j in range(ny) for i in range(nx)]
    Bz = np.zeros((ny, nx))
    ins = np.zeros((ny, nx), bool)
    wall = np.zeros((ny, nx), bool)
    N1, N2, U = (np.zeros((ny, nx)) for _ in range(3))
    for j, i in cells:
        x, y = X[i], Y[j]
        Bz[j, i] = low(x, y)
        ins[j, i] = lies(x, y)
        wall[j, i] = leans(x, y)
        N1[j, i] = noise.noise(Vector((x * 0.9, y * 0.9, 0)) + off)
        N2[j, i] = noise.noise(Vector((x * 1.7, y * 1.7, 3.0)) + off)
        U[j, i] = rng.uniform(-1, 1)
    JX = np.array([[rng.uniform(-0.25, 0.25) for i in range(nx)] for j in range(ny)]) * cell
    JY = np.array([[rng.uniform(-0.25, 0.25) for i in range(nx)] for j in range(ny)]) * cell

    def surface(ins):
        # height from the open edge (a wall it leans on is no edge), lumpy
        out = ~(ins | wall)
        D = np.full(ins.shape, 1e3)
        if out.any() and ins.any():
            d = np.sqrt((GX[ins][:, None] - GX[out][None]) ** 2 + (GY[ins][:, None] - GY[out][None]) ** 2).min(1)
            D[ins] = np.maximum(0.0, d - cell / 2)
        t = np.clip(D / edge, 0, 1)
        k = t * t * (3 - 2 * t)
        hump = top * k * (1 + lumps * N1) + (0.22 * N2 + rough * U) * np.minimum(1.0, k + 0.3) * min(1.0, top)
        return np.where(ins, Bz + np.maximum(0.03, hump), Bz - 0.06)

    if keep is not None:
        Z0 = surface(ins)
        for j, i in cells:
            if ins[j, i] and not keep(X[i], Y[j], Z0[j, i]):
                ins[j, i] = False
    # no point of the bed stands alone: each keeps two neighbours, or a wall, beside it
    for _ in range(4):
        Sp = np.pad(ins | wall, 1)
        cnt = Sp[1:-1, :-2].astype(int) + Sp[1:-1, 2:] + Sp[:-2, 1:-1] + Sp[2:, 1:-1]
        lone = ins & (cnt < 2)
        if not lone.any():
            break
        ins &= ~lone
    Z = surface(ins)
    VX, VY = GX.copy(), GY.copy()
    for j, i in cells:
        if ins[j, i] and lies(GX[j, i] + JX[j, i], GY[j, i] + JY[j, i]):
            VX[j, i] += JX[j, i]
            VY[j, i] += JY[j, i]
    # 1 bed, 2 its edge on the outline (or a rim), 3 its edge against a wall
    kind = np.where(ins, 1, 0)
    for j, i in cells:
        if ins[j, i]:
            continue
        nb = [(i + di, j + dj) for di, dj in N4 if 0 <= i + di < nx and 0 <= j + dj < ny and ins[j + dj, i + di]]
        if not nb:
            continue
        pts, zs = [], []
        b = (GX[j, i], GY[j, i])
        for a_, b_ in nb:
            a = (VX[b_, a_], VY[b_, a_])
            if wall[j, i]:
                c = crossing(lambda x, y: not leans(x, y), a, b)
                L = math.hypot(b[0] - a[0], b[1] - a[1]) or 1.0
                pts.append((c[0] + (b[0] - a[0]) / L * 0.08, c[1] + (b[1] - a[1]) / L * 0.08))
                continue
            c = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2) if lies(*b) else crossing(lies, a, b)
            pts.append(c)
            # the level of what it lies on, read just inside its edge
            zs.append(low(c[0] + (a[0] - c[0]) * 0.3, c[1] + (a[1] - c[1]) * 0.3) - 0.05)
        VX[j, i] = sum(p[0] for p in pts) / len(pts)
        VY[j, i] = sum(p[1] for p in pts) / len(pts)
        if wall[j, i]:
            kind[j, i] = 3
        else:
            kind[j, i] = 2
            Z[j, i] = min(zs)
    act = kind == 1
    Bv = Bz.copy()
    for j, i in cells:
        if act[j, i]:
            Bv[j, i] = low(VX[j, i], VY[j, i])
            if zmax is not None:
                Z[j, i] = min(Z[j, i], zmax(VX[j, i], VY[j, i]) - 0.05)
    # no face steeper than slope: no point higher over a neighbour than slope times the run
    Zv = np.where(act | (kind == 2), Z, np.inf)
    Xp, Yp = np.pad(VX, 1, mode="edge"), np.pad(VY, 1, mode="edge")
    for _ in range(200):
        Zp = np.pad(Zv, 1, constant_values=np.inf)
        cap = np.full(Zv.shape, np.inf)
        for di, dj in N8:
            sl = (slice(1 + dj, 1 + dj + ny), slice(1 + di, 1 + di + nx))
            cap = np.minimum(cap, Zp[sl] + slope * np.hypot(VX - Xp[sl], VY - Yp[sl]))
        new = np.where(act, np.minimum(Zv, cap), Zv)
        if np.allclose(new, Zv, atol=1e-4):
            break
        Zv = new
    Zv = np.where(act, np.maximum(Zv, Bv + 0.02), Zv)
    for j, i in cells:
        if kind[j, i] == 3:
            Zv[j, i] = max(Zv[j + dj, i + di] for di, dj in N4
                           if 0 <= i + di < nx and 0 <= j + dj < ny and act[j + dj, i + di])
    vid = -np.ones((ny, nx), int)
    verts = []
    for j, i in cells:
        if kind[j, i]:
            vid[j, i] = len(verts)
            verts.append((VX[j, i], VY[j, i], Zv[j, i]))

    def ok(tri):
        ks = [kind[q[1], q[0]] for q in tri]
        return min(ks) > 0 and 1 in ks
    faces = []
    for j in range(ny - 1):
        for i in range(nx - 1):
            a, b, c, d = (i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)
            if not any(kind[q[1], q[0]] == 1 for q in (a, b, c, d)):
                continue
            t1 = [t for t in ((a, b, c), (a, c, d)) if ok(t)]
            t2 = [t for t in ((a, b, d), (b, c, d)) if ok(t)]
            for t in (t1 if len(t1) >= len(t2) else t2):
                faces.append(tuple(vid[q[1], q[0]] for q in t))
                if os.environ.get("G1_STEEP"):
                    zz = [Zv[q[1], q[0]] for q in t]
                    if max(zz) - min(zz) > 2.0:
                        print("G1_STEEP", [(int(kind[q[1], q[0]]), round(float(VX[q[1], q[0]]), 2),
                                            round(float(VY[q[1], q[0]]), 2), round(float(Zv[q[1], q[0]]), 2)) for q in t],
                              flush=True)
    B.mesh(mat, verts, faces)
    Zf = np.where(np.isfinite(Zv) & (kind > 0), Zv, Bz - 0.06)
    return Field(x0, y0, cell, Zf, act, kind > 0, on)


def rim_cap(cx, cy, r, pts, drop=0.25, slope=SLOPE, n=48):
    """A ceiling for a heap filling a broken ring round (cx, cy): from the
    ring's broken top (pts, [(angle, z)]) at radius r it rises inward no
    steeper than slope, so the fill pours over a breach and stands no
    higher than the ring holds it."""
    fn = heights(pts)
    Q = [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a)), fn(a) - drop)
         for a in (360.0 * k / n for k in range(n))]

    def zmax(x, y):
        return min(z + slope * math.hypot(x - qx, y - qy) for qx, qy, z in Q)
    return zmax


def outside_circle(cx, cy, r):
    return lambda x, y: math.hypot(x - cx, y - cy) > r


def chip(B, mat, x, y, z, s, rng):
    """A small broken stone about s across: a squat tetrahedron, four
    triangles, sunk a little into what it lies on."""
    a = rng.uniform(0, 2 * math.pi)
    V = [(s * 0.6 * math.cos(a + k * 2.1 + rng.uniform(-0.3, 0.3)), s * 0.6 * math.sin(a + k * 2.1), 0.0)
         for k in range(3)]
    V.append((rng.uniform(-0.15, 0.15) * s, rng.uniform(-0.15, 0.15) * s, s * rng.uniform(0.4, 0.65)))
    with B.at(x, y, z - s * 0.12, pitch=rng.uniform(-12, 12), roll=rng.uniform(-12, 12)):
        B.solid(mat, V, [(0, 2, 1), (0, 1, 3), (1, 2, 3), (2, 0, 3)])


def wedge(B, mat, x, y, z, s, rng, yaw=None, pitch=None, roll=None):
    """A broken block about s across: a skewed triangular prism, eight
    triangles, sunk a little into what it lies on."""
    L, w, h = s * rng.uniform(0.8, 1.4), s * rng.uniform(0.7, 1.1), s * rng.uniform(0.45, 0.8)
    sk = rng.uniform(-0.3, 0.3) * w
    e0, e1 = rng.uniform(0.7, 1.0), rng.uniform(0.6, 1.0)
    V = [(-L / 2, -w / 2, 0), (-L / 2, w / 2, 0), (-L / 2, sk, h),
         (L / 2, -w / 2 * e0, 0), (L / 2, w / 2 * e0, 0), (L / 2, sk * e0, h * e1)]
    F = [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)]
    yaw = rng.uniform(0, 360) if yaw is None else yaw
    pitch = rng.uniform(-18, 18) if pitch is None else pitch
    roll = rng.uniform(-18, 18) if roll is None else roll
    with B.at(x, y, z - h * 0.25, yaw=yaw, pitch=pitch, roll=roll):
        B.solid(mat, V, F)


def masonry(B, mats, r, P, ground, n, pic, seed, size=(0.3, 0.6), rim=False, q=0.9):
    """n small broken blocks (wedges) lying on a heap inside P, or along its
    outline with rim, so the edge ends in stones rather than a ragged sheet.
    Each takes the piece tone nearest the picture's colour where it lies."""
    rng = random.Random(seed)
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    cx, cy = sum(xs) / len(xs), sum(ys) / len(ys)
    per = [math.hypot(P[k][0] - P[k - 1][0], P[k][1] - P[k - 1][1]) for k in range(len(P))]
    total = sum(per)
    placed = tries = 0
    while placed < n and tries < n * 60:
        tries += 1
        if rim:
            t = rng.uniform(0, total)
            k = 0
            while k < len(per) - 1 and t > per[k]:
                t -= per[k]
                k += 1
            f = t / max(per[k], 1e-9)
            ax, ay = P[k - 1]
            bx, by = P[k]
            x, y = ax + (bx - ax) * f, ay + (by - ay) * f
            # stepped in off the outline toward the heap's middle
            d = math.hypot(cx - x, cy - y) or 1.0
            s_in = rng.uniform(0.1, 0.45)
            x, y = x + (cx - x) / d * s_in, y + (cy - y) / d * s_in
        else:
            x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
            if not akit.inside_poly(P, x, y):
                continue
        z = ground(x, y)
        if z < -50 or (pic is not None and not pic.sees((x, y, z + 0.15), 1)):
            continue
        col = pic.colour(*akit.screen(r, x, y, z), q=q) if pic is not None else None
        _, m = akit._pick(rng, {"stone": mats}, {"stone": 1}, col)
        sz = rng.uniform(*size)
        # most are chips, four triangles each, so the budget buys more of them
        (chip if sz < 0.5 else wedge)(B, m, x, y, z, sz, rng)
        placed += 1
    return placed


def strew(B, mats, r, P, ground, n, kinds, pic, seed, **kw):
    """Pieces of the building laid on a heap (the Aramon kit's cover),
    sized as masonry rather than dust."""
    d = dict(size=(0.5, 1.1), stone=(0.35, 0.7), chunk=(0.45, 0.95), boulder=(0.6, 1.0), length=(1.0, 2.2),
             width=(0.12, 0.2), lift=0.02, tilt=14, grow=1, q=0.92, jumble=0.15, thick=(0.08, 0.16))
    d.update(kw)
    return akit.cover(B, mats, r, P, ground, n, kinds, pic=pic, seed=seed, **d)


def scree(B, mats, r, P, F, n, pic, seed, steep=0.6):
    """Blocks and lumps lying on a heap's steep faces only, where it spills
    over a wall or slumps to the ground, so no slope reads as a bare ramp."""
    e = 0.2

    def sloped(x, y):
        z = F(x, y)
        if z < -50:
            return z
        zs = [F(x + e, y), F(x - e, y), F(x, y + e), F(x, y - e)]
        if min(zs) < -50:
            return -100.0
        g = math.hypot(zs[0] - zs[1], zs[2] - zs[3]) / (2 * e)
        return z if g > steep else -100.0
    return strew(B, mats, r, P, sloped, n, {"chunk": 5, "stone": 3, "slab": 1}, pic, seed, tries=120,
                 chunk=(0.35, 0.8), stone=(0.3, 0.6))


def shards(B, mat, r, pic, ground, P, n, seed, size=(0.5, 1.1), curve=0.25):
    """Curved pieces of a broken dome's shell lying on a heap: each a
    small patch of a sphere, thick, tipped as it fell."""
    rng = random.Random(seed)
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    placed = tries = 0
    while placed < n and tries < n * 60:
        tries += 1
        x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
        if not akit.inside_poly(P, x, y):
            continue
        z = ground(x, y)
        if z < -50 or (pic is not None and not pic.sees((x, y, z + 0.2), 1)):
            continue
        w, h = rng.uniform(*size), rng.uniform(*size) * 0.8
        R = max(w, h) / max(curve, 0.05)
        nu, nv = 2, 1
        V, F = [], []
        for side in (0, 1):
            for jv in range(nv + 1):
                for iu in range(nu + 1):
                    u = (iu / nu - 0.5) * w
                    v = (jv / nv - 0.5) * h
                    bulge = (u * u + v * v) / (2 * R)
                    V.append((u, v, -bulge - side * 0.09))
        o = (nu + 1) * (nv + 1)
        for jv in range(nv):
            for iu in range(nu):
                a = jv * (nu + 1) + iu
                F.append((a, a + 1, a + nu + 2, a + nu + 1))
                F.append((o + a, o + a + nu + 1, o + a + nu + 2, o + a + 1))
        ring = [k for k in range(nu + 1)] + [(k + 1) * (nu + 1) - 1 for k in range(1, nv + 1)] + \
               [nv * (nu + 1) + k for k in range(nu - 1, -1, -1)] + [k * (nu + 1) for k in range(nv - 1, 0, -1)]
        for k in range(len(ring)):
            a, b = ring[k], ring[(k + 1) % len(ring)]
            F.append((b, a, o + a, o + b))
        with B.at(x, y, z + 0.12, yaw=rng.uniform(0, 360), pitch=rng.uniform(-35, 35), roll=rng.uniform(-35, 35)):
            B.solid(mat, V, F)
        placed += 1
    return placed


def broken_onion(R, H, cut=0.62, seg=20, jag=0.12, seed=0):
    """An onion dome (palace.onion's profile) torn open: its shell up to cut
    of its height, the rim ragged, standing at the origin."""
    rng = random.Random(seed)
    prof = [(0.55, 0.0), (0.85, 0.12), (1.0, 0.3), (0.97, 0.45), (0.8, 0.62), (0.5, 0.78), (0.25, 0.9)]
    prof = [(r_, z_) for r_, z_ in prof if z_ <= cut + 1e-6]
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = []
    for i, (r_, z_) in enumerate(prof):
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            dz = -rng.uniform(0, jag * 3) * H if i == len(prof) - 1 and rng.random() < 0.6 else 0.0
            ring.append(bm.verts.new((R * r_ * math.cos(a), R * r_ * math.sin(a), max(0.0, H * z_ + dz))))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for k in range(seg):
            k1 = (k + 1) % seg
            f = bm.faces.new((rings[i][k], rings[i][k1], rings[i + 1][k1], rings[i + 1][k]))
            for l_, (uu, vv) in zip(f.loops, ((k, i), (k + 1, i), (k + 1, i + 1), (k, i + 1))):
                l_[uvl].uv = (uu / seg, vv / 8)
    bm.faces.new(list(reversed(rings[0])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def lying(B, mat, bm, x, y, z, yaw, tip, keep_uv=True):
    """A palace piece (built standing at the origin) fallen over at (x, y, z)."""
    M = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(yaw), 4, "Z") @ \
        Matrix.Rotation(math.radians(tip), 4, "Y")
    put(B, mat, bm, keep_uv=keep_uv, M=M)


def gold_onion(cx, cy, z0, R, H, seg=8):
    """A tower's gilded onion (palace.onion's profile) in few faces."""
    prof = [(0.45, 0.0), (0.85, 0.12), (1.0, 0.3), (0.97, 0.45), (0.8, 0.62), (0.5, 0.78), (0.25, 0.9), (0.1, 1.0)]
    return pal.lathe([(R * r, H * z) for r, z in prof], seg, cx, cy, z0, "gold_onion")


def finial(cx, cy, z0, s, seg=6):
    """palace.finial's neck, knob and spike in few faces, for a piece a few
    pixels across."""
    return pal.lathe([(0.35 * s, 0), (s, 1.1 * s), (0.25 * s, 2.0 * s), (0.03, 4.5 * s), (0.0, 4.6 * s)], seg, cx, cy,
                     z0, "finial")


def _prof_at(prof, z):
    """The point of a profile [(r, z)] rising in z at height z, clamped to its ends."""
    if z <= prof[0][1]:
        return prof[0]
    for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
        if z <= z1:
            f = 0.0 if z1 - z0 < 1e-9 else (z - z0) / (z1 - z0)
            return (r0 + (r1 - r0) * f, z)
    return prof[-1]


def broken_shell(B, mat, cx, cy, prof, t, top, seg=24, a0=0.0, a1=360.0, z0=0.0, jag=0.3, notch=0.3, seed=0):
    """shell() broken off at a ragged rim: top(a) is the height over z0 the
    shell stands to at angle a (degrees, 0 east, anticlockwise), or None
    where it stands whole, roughened stone by stone. Each column of the
    shell is cut along its own curve, so it keeps its curvature, and the rim
    between the outer and inner faces shows its thickness from any side."""
    rng = random.Random(seed)
    full = abs(a1 - a0) >= 359.9
    n = seg if full else max(2, int(round(seg * (a1 - a0) / 360.0)))
    angs = [a0 + (a1 - a0) * k / n for k in range(n if full else n + 1)]
    inner = []
    for i, (r, z) in enumerate(prof):
        j0, j1 = max(0, i - 1), min(len(prof) - 1, i + 1)
        dr, dz = prof[j1][0] - prof[j0][0], prof[j1][1] - prof[j0][1]
        L = math.hypot(dr, dz) or 1.0
        inner.append((max(0.02, r - dz / L * t), z + dr / L * t))
    zt = prof[-1][1]
    cuts = []
    for a in angs:
        h = top(a % 360.0)
        if h is not None:
            h += rng.uniform(-jag, jag)
            if rng.random() < notch:
                h -= rng.uniform(0.5, 2.0) * jag
            if h >= zt - 1e-3:
                h = None
        cuts.append(h)
    rows = [(prof, i) for i in range(len(prof))] + [(inner, i) for i in reversed(range(len(inner)))]
    bm = bmesh.new()
    vs = []
    for P, i in rows:
        row = []
        for a, h in zip(angs, cuts):
            r, z = P[i] if h is None or P[i][1] <= h else _prof_at(P, max(h, P[0][1]))
            ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
            row.append(bm.verts.new((cx + r * ca, cy + r * sa, z0 + z)))
        vs.append(row)
    m = len(angs)
    span = m if full else m - 1
    R = len(rows)
    # a column broken right down leaves nothing, not a flat ring at the foot
    gone = [h is not None and h <= prof[0][1] + 1e-6 for h in cuts]
    for i in range(R):
        i1 = (i + 1) % R
        for k in range(span):
            k1 = (k + 1) % m
            if gone[k] and gone[k1]:
                continue
            try:
                bm.faces.new((vs[i][k], vs[i][k1], vs[i1][k1], vs[i1][k]))
            except ValueError:
                pass
    if not full:
        np_ = len(prof)
        for side in (0, m - 1):
            for i in range(np_ - 1):
                q = [vs[i][side], vs[i + 1][side], vs[R - 2 - i][side], vs[R - 1 - i][side]]
                try:
                    bm.faces.new(q if side else list(reversed(q)))
                except ValueError:
                    pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-4, edges=bm.edges)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_area() < 1e-6], context="FACES_ONLY")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    put(B, mat, bm)


def heights(pts, wrap=True):
    """top() for broken_shell from [(angle, height)] control points,
    interpolated round the ring."""
    hs = sorted(pts)
    ang = [a for a, _ in hs] + ([hs[0][0] + 360] if wrap else [])
    val = [h for _, h in hs] + ([hs[0][1]] if wrap else [])

    def top(a):
        a = (a - ang[0]) % 360 + ang[0] if wrap else a
        return float(np.interp(a, ang, val))
    return top


def thin(B, names, keep, seed):
    """Keeps about keep of the loose pieces in B's materials named."""
    rng = random.Random(seed)
    for nm in names:
        bm = B.bms.get(nm)
        if bm is None:
            continue
        bm.verts.ensure_lookup_table()
        seen, gone = set(), []
        for v in bm.verts:
            if v.index in seen:
                continue
            isl, stack = [], [v]
            seen.add(v.index)
            while stack:
                u = stack.pop()
                isl.append(u)
                for e in u.link_edges:
                    w = e.other_vert(u)
                    if w.index not in seen:
                        seen.add(w.index)
                        stack.append(w)
            if rng.random() > keep:
                gone += isl
        bmesh.ops.delete(bm, geom=gone, context="VERTS")


def islands(bm):
    """The loose pieces of a bmesh, each a list of its vertices."""
    bm.verts.ensure_lookup_table()
    seen, out = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        isl, stack = [], [v]
        seen.add(v.index)
        while stack:
            u = stack.pop()
            isl.append(u)
            for e in u.link_edges:
                w = e.other_vert(u)
                if w.index not in seen:
                    seen.add(w.index)
                    stack.append(w)
        out.append(isl)
    return out


def drop_hidden(B, hidden):
    """Removes the loose pieces of every material in B for which
    hidden(centre, top) holds: pieces the classic camera cannot see. Returns
    how many went."""
    n = 0
    for bm in B.bms.values():
        gone = []
        for isl in islands(bm):
            c = sum((v.co for v in isl), Vector()) / len(isl)
            if hidden(c, max(v.co.z for v in isl)):
                gone += isl
                n += 1
        bmesh.ops.delete(bm, geom=gone, context="VERTS")
    return n


def plate(B, mat, x, y, z, w, h, yaw=0.0, pitch=0.0, roll=0.0, curve=0.2, t=0.12, nu=2, nv=1):
    """A curved plate w by h and t thick, centred on (x, y, z) and tipped as
    it fell: a piece of a dome's or a vault's shell."""
    R = max(w, h) / max(curve, 0.05)
    V, F = [], []
    for side in (0, 1):
        for jv in range(nv + 1):
            for iu in range(nu + 1):
                u = (iu / nu - 0.5) * w
                v = (jv / nv - 0.5) * h
                V.append((u, v, -(u * u + v * v) / (2 * R) - side * t))
    o = (nu + 1) * (nv + 1)
    for jv in range(nv):
        for iu in range(nu):
            a = jv * (nu + 1) + iu
            F.append((a, a + 1, a + nu + 2, a + nu + 1))
            F.append((o + a, o + a + nu + 1, o + a + nu + 2, o + a + 1))
    ring = [k for k in range(nu + 1)] + [(k + 1) * (nu + 1) - 1 for k in range(1, nv + 1)] + \
           [nv * (nu + 1) + k for k in range(nu - 1, -1, -1)] + [k * (nu + 1) for k in range(nv - 1, 0, -1)]
    for k in range(len(ring)):
        a, b = ring[k], ring[(k + 1) % len(ring)]
        F.append((b, a, o + a, o + b))
    with B.at(x, y, z, yaw=yaw, pitch=pitch, roll=roll):
        B.solid(mat, V, F)


def at_pixel(r, col, row, zfn, lift=0.0, P=None, what=""):
    """The plan point and height where a piece lying on zfn shows at (col,
    row): the first place its line of sight meets that surface. With P
    (the heap's outline) the point must land inside it."""
    x, y, z = first_hit(r, col, row, lambda x, y: zfn(x, y) + lift)
    if P is not None and not akit.inside_poly(P, x, y):
        msg = "%s at pixel (%s, %s) lands at (%.2f, %.2f, %.2f), outside its heap" % (what, col, row, x, y, z)
        if os.environ.get("G1_SOFT"):
            # the nearest pixels that do land inside, to trace from
            near = [(abs(dc) + abs(dr), col + dc, row + dr) for dc in range(-14, 15, 2) for dr in range(-14, 15, 2)
                    if akit.inside_poly(P, *first_hit(r, col + dc, row + dr, lambda x, y: zfn(x, y) + lift)[:2])]
            print("G1_WARN", msg, "inside near", sorted(near)[:4], flush=True)
            return x, y, z
        raise ValueError(msg)
    return x, y, z


def rafters(B, mat, r, P, ground, n, seed, length=(2.0, 3.5), poke=(0.3, 1.4), w=(0.16, 0.24), pic=None):
    """Charred rafters lying across a heap inside plan polygon P, one end
    resting on it and the other poking up out of it."""
    rng = random.Random(seed)
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    placed = tries = 0
    while placed < n and tries < n * 80:
        tries += 1
        x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
        if not akit.inside_poly(P, x, y) or ground(x, y) < -50:
            continue
        L = rng.uniform(*length)
        a = rng.uniform(0, 2 * math.pi)
        dx, dy = math.cos(a) * L / 2, math.sin(a) * L / 2
        za, zb = ground(x - dx, y - dy), ground(x + dx, y + dy)
        if za < -50 or zb < -50:
            continue
        zm = ground(x, y)
        bw = rng.uniform(*w)
        za, zb = za + bw * 0.3, zb + bw * 0.3 + rng.uniform(*poke)
        sag = zm + bw * 0.5 - (za + zb) / 2
        if sag > 0:
            za, zb = za + sag, zb + sag
        if abs(zb - za) > 0.6 * L:
            continue  # a timber does not stand on end in a heap
        p0, p1 = (x - dx, y - dy, za), (x + dx, y + dy, zb)
        if pic is not None and not (pic.sees(p0, 1) and pic.sees(p1, 1)):
            continue
        B.beam(mat, p0, p1, bw, bw * rng.uniform(0.8, 1.1), twist=rng.uniform(-12, 12))
        placed += 1
    return placed


def stable_noise_vector(p):
    """Smooth vector noise from three readings of noise.noise, which gives
    the same values in every Blender run (noise_vector in Blender 5.2 does
    not)."""
    return Vector((noise.noise(p), noise.noise(p + Vector((31.7, 0.0, 0.0))), noise.noise(p + Vector((0.0, 47.3, 0.0)))))


def rough_pieces(pairs, amp=0.08, freq=2.3, seed=0):
    """Breaks the true lines of cut pieces: each vertex pushed by smooth
    noise, so blocks read as broken stone, not crates."""
    off = Vector((seed * 0.37, seed * 0.11, seed * 0.53))
    for ob, m in pairs:
        if not m.name.startswith(("m_", "s_")):
            continue
        for v in ob.data.vertices:
            v.co += stable_noise_vector(v.co * freq + off) * amp


PIECES = ("m_", "c_", "t_", "g_", "s_")
# a building stage's triangles, the scenery guide for its size
BUDGET = 5950


def tri_count(objects=(), builders=()):
    n = sum(len(p.vertices) - 2 for o in objects for p in o.data.polygons)
    return n + sum(len(f.verts) - 2 for b in builders for bm in b.bms.values() for f in bm.faces)


def fit_budget(R, standing, seed):
    """Thins the loose masonry (the m_ and s_ pieces in R) evenly so the
    stage keeps to BUDGET triangles; the beds, shards, gold and timbers
    stay whole."""
    total = tri_count(standing, [R])
    if total <= BUDGET:
        return total
    names = [k for k in R.bms if k.startswith(("s_", "m_"))]
    small = sum(len(f.verts) - 2 for k in names for f in R.bms[k].faces)
    if small:
        thin(R, names, max(0.2, 1.0 - 1.08 * (total - BUDGET) / small), seed)
    if os.environ.get("G1_TRIS"):
        print("G1_BUDGET", total, "small", small, "after", tri_count(standing, [R]), flush=True)
    return tri_count(standing, [R])


def paint_rubble(pairs, seed, burns):
    """Heaps and the pieces on them: the beds and timbers take the burns in
    full, the pieces' lit tops only lightly, so the chips stay pale."""
    beds = [(o, m) for o, m in pairs if not m.name.startswith(PIECES)]
    bits = [(o, m) for o, m in pairs if m.name.startswith(PIECES)]
    paint(beds, Soot(seed, base=1.0, burns=burns, blotch=0.3, streak=0.0))
    paint(bits, Soot(seed, base=1.0, burns=burns, blotch=0.3, streak=0.0, top_cap=0.15))


def paint(pairs, soot, extra=None):
    """Vertex colours: each Mat's colour, darkened by soot as far as the
    Mat takes it, and by extra(ob, co) if given."""
    for ob, m in pairs:
        c = m.colour

        def f(co, n, c=c, m=m, ob=ob):
            k = soot(co, n)
            k = 1 - m.soot * (1 - k)
            if extra is not None:
                k *= extra(ob, co)
            return c * k
        kit.paint_points(ob, f)


def plan_tex(name, x0, x1, y0, y1, z, ppc, colour, soot):
    """A tile for one flat top, mapped in plan over x0..x1, y0..y1: colour
    (X, Y) -> linear rgb grids, darkened by the soot field at height z."""
    W = int(round((x1 - x0) * ppc))
    H = int(round((y1 - y0) * ppc))
    xs = x0 + (np.arange(W) + 0.5) / ppc
    ys = y1 - (np.arange(H) + 0.5) / ppc  # rows top down: north first
    X, Y = np.meshgrid(xs, ys)
    rgb = colour(X, Y) * soot.plane(X, Y, z)[..., None]
    a = np.dstack([to_srgb(rgb), np.ones((H, W))])
    img = kit.image(name, a)
    if os.environ.get("G1_DEBUG"):
        img.filepath_raw = os.path.join(os.environ["G1_DEBUG"], name + ".png")
        img.file_format = "PNG"
        img.save()
    return img


def deck(B, mat, x0, x1, y0, y1, z, step=1.0):
    """A flat top mapped in plan to its own tile (plan_tex), in quads about
    step cells across."""
    nx, ny = max(1, int(round((x1 - x0) / step))), max(1, int(round((y1 - y0) / step)))
    V = [(x0 + (x1 - x0) * i / nx, y0 + (y1 - y0) * j / ny, z) for j in range(ny + 1) for i in range(nx + 1)]
    F = [(j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i)
         for j in range(ny) for i in range(nx)]
    fs = B.mesh(mat, V, F)
    bm = B._bm(mat)
    uvl = bm.loops.layers.uv.verify()
    for f in fs:
        for lp in f.loops:
            lp[uvl].uv = ((lp.vert.co.x - x0) / (x1 - x0), (lp.vert.co.y - y0) / (y1 - y0))


# ---------------------------------------------------------------- the palace

X0, X1, Y0, Y1, H = pal.X0, pal.X1, pal.Y0, pal.Y1, pal.H
SX, SY = pal.SPIRE
S_Y0, S_Y1, RUN = -3.6, 1.1, 2.8  # the west steps, as palace.build lays them
CORNERS = ((X0 - 0.3, Y0), (X1 + 0.3, Y0), (X0 - 0.3, Y1), (X1 + 0.3, Y1))  # SW, SE, NW, NE


def palace_base(x, y):
    """The ground a heap on the palace lies on: the terrace, its steps, the plain."""
    if X0 <= x <= X1 and Y0 <= y <= Y1:
        return H
    if X0 - RUN <= x < X0 and S_Y0 <= y <= S_Y1:
        return max(0.0, H * (x - (X0 - RUN)) / RUN)
    return 0.0


def on_terrace(x, y):
    return X0 <= x <= X1 and Y0 <= y <= Y1


def palace_built(x, y):
    """The terrace and its west steps: what a heap at their foot leans on."""
    return on_terrace(x, y) or (X0 - RUN - 0.05 <= x <= X0 and S_Y0 - 0.3 <= y <= S_Y1 + 0.3)


def flat_base(x, y):
    return 0.0


def perimeter(P):
    return sum(math.hypot(P[k][0] - P[k - 1][0], P[k][1] - P[k - 1][1]) for k in range(len(P)))


def foot_outlines(P, reach, seed, sides=("s", "w", "e")):
    """Where heap outline P meets the terrace's south, west or east wall:
    the outline of a heap at that wall's foot, as long as the stretch of
    wall P covers and reach cells out from it, its inner edge run into the
    terrace it leans on. Yields (side, outline); the steps get none."""
    rng = random.Random(seed)
    for side in sides:
        if side == "s":
            u0, u1, line = X0 - 0.3, X1 + 0.3, (lambda u, d: (u, Y0 - d))
        elif side == "w":
            u0, u1, line = Y0, Y1, (lambda u, d: (X0 - d, u))
        else:
            u0, u1, line = Y0, Y1, (lambda u, d: (X1 + d, u))
        us = np.arange(u0, u1 + 1e-6, 0.2)
        cov = [(akit.inside_poly(P, *line(u, 0.1)) or akit.inside_poly(P, *line(u, -0.3)))
               and not (side == "w" and S_Y0 - 0.5 <= u <= S_Y1 + 0.5) for u in us]
        k = 0
        while k < len(us):
            if not cov[k]:
                k += 1
                continue
            k1 = k
            while k1 + 1 < len(us) and cov[k1 + 1]:
                k1 += 1
            a, b = us[k], us[k1]
            k = k1 + 1
            if b - a < 1.0:
                continue
            n = max(4, int((b - a) / 0.45))
            pts = [line(a + (b - a) * i / n, reach * max(0.0, math.sin(math.pi * i / n)) ** 0.5 * rng.uniform(0.8, 1.1))
                   for i in range(n + 1)]
            yield side, pts + [line(b, -0.6), line(a, -0.6)]


def onion_shell(R, H, cut, a0, a1, t=0.14, seg=16, jag=0.1, seed=0):
    """Part of an onion dome's shell (palace.onion's profile) between angles
    a0 and a1 (degrees) and up to cut of its height, t thick, its torn top
    ragged, standing at the origin, its ribs mapped round it."""
    rng = random.Random(seed)
    prof = [(0.55, 0.0), (0.85, 0.12), (1.0, 0.3), (0.97, 0.45), (0.8, 0.62), (0.5, 0.78), (0.25, 0.9)]
    prof = [(r_, z_) for r_, z_ in prof if z_ <= cut + 1e-6]
    n = max(2, int(round(seg * (a1 - a0) / 360.0)))
    angs = [math.radians(a0 + (a1 - a0) * k / n) for k in range(n + 1)]
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    outer, inner = [], []
    for i, (r_, z_) in enumerate(prof):
        ro, ri = [], []
        for a in angs:
            dz = -rng.uniform(0, jag * 3) * H if i == len(prof) - 1 and rng.random() < 0.6 else 0.0
            z = max(0.0, H * z_ + dz)
            ro.append(bm.verts.new((R * r_ * math.cos(a), R * r_ * math.sin(a), z)))
            ri.append(bm.verts.new(((R * r_ - t) * math.cos(a), (R * r_ - t) * math.sin(a), z)))
        outer.append(ro)
        inner.append(ri)

    def quad(vs, uvs):
        try:
            f = bm.faces.new(vs)
        except ValueError:
            return
        for lp, uv in zip(f.loops, uvs):
            lp[uvl].uv = uv
    m = len(prof)
    for i in range(m - 1):
        for k in range(n):
            uv = [(k / seg, i / 8), ((k + 1) / seg, i / 8), ((k + 1) / seg, (i + 1) / 8), (k / seg, (i + 1) / 8)]
            quad((outer[i][k], outer[i][k + 1], outer[i + 1][k + 1], outer[i + 1][k]), uv)
            quad((inner[i + 1][k], inner[i + 1][k + 1], inner[i][k + 1], inner[i][k]), uv)
    for k in range(n):
        uv = [(k / seg, 0.0), ((k + 1) / seg, 0.0), ((k + 1) / seg, 0.05), (k / seg, 0.05)]
        quad((outer[-1][k], inner[-1][k], inner[-1][k + 1], outer[-1][k + 1]), uv)
        quad((outer[0][k + 1], inner[0][k + 1], inner[0][k], outer[0][k]), uv)
    for k in (0, n):
        for i in range(m - 1):
            quad((outer[i][k], outer[i + 1][k], inner[i + 1][k], inner[i][k]), [(0, 0), (0.05, 0), (0.05, 0.1), (0, 0.1)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def split_onion(B, M, x, y, z, R0, Hd, seed):
    """A fallen onion dome split in two along its axis: a lit ribbed half
    on its back to the west and a crushed dark half pitched into the heap
    to the east, torn tops outward, its gilded neck between them."""
    for k, (dx, yaw, dip, sq, mat) in enumerate(((-0.1, 8, 10, 1.0, M["copper_lit"]),
                                                  (0.2, 196, 28, 0.65, M["copper_dark"]))):
        bm = onion_shell(R0, Hd, 0.62, -90, 90, t=0.16, seed=seed + k)
        T = Matrix.Translation((x + dx, y, z)) @ Matrix.Rotation(math.radians(yaw), 4, "Z") @             Matrix.Rotation(math.radians(-90 - dip), 4, "Y") @ Matrix.Diagonal((sq, 1.0, 1.0, 1.0))
        put(B, mat, bm, keep_uv=True, M=T)
    lying(B, M["gold"], pal.lathe([(0.5 * R0 / 1.6, 0), (0.5 * R0 / 1.6, 0.3), (0.32, 0.42)], 8, 0, 0, 0, "neck"),
          x + 0.05, y, z + 0.4, 100, 90)


def hollow_stump(B, mat, floor, cx, cy, r, h, seed, t=0.2, jag=0.35, seg=16):
    """A round tower broken off to a ragged ring h high, open on top: its
    shaft's dark inside shows, choked with rubble a cell down."""
    rng = random.Random(seed)
    pts = [(a, h + rng.uniform(-jag, jag) - (1.5 * jag if rng.random() < 0.3 else 0.0)) for a in range(0, 360, 45)]
    broken_shell(B, mat, cx, cy, [(r, 0.0), (r, h + 2 * jag + 0.5)], t, heights(pts), seg=seg, jag=0.12, notch=0.2,
                 seed=seed)
    rr = r - t - 0.02
    B.mesh(floor, [(cx + rr * math.cos(2 * math.pi * k / 8), cy + rr * math.sin(2 * math.pi * k / 8), h - 1.0)
                   for k in range(8)], [tuple(range(8))])


# a heap's bed between its pieces, as dark as the pictures' gaps
BED = "#403b35"
# the marble pieces' drawn tones, lit chips to sooted
TONES = {"chip": "#d8cdbe", "pale": "#b9ae9f", "mid": "#90887c", "soot": "#635d54"}


def pale_weighted(pieces, prefix="m_"):
    """A piece list leaning to the lit tones: besides the four tones the
    picture picks between by its colour where a piece lies, a chip, a pale
    and a mid one come up whatever it draws there, as the pictures' heaps are
    mostly lit masonry over dark gaps."""
    free = []
    for nm in ("chip", "pale", "mid"):
        m = copy.copy(next(p for p in pieces if p.name == prefix + nm))
        m.ref = None
        free.append(m)
    return pieces + free


def palace_mats(name, pic, seed):
    """The intact palace's materials (its own textures and colours) and the
    pieces a ruin breaks into, coloured as the stage picture draws them."""
    pseed = zlib.crc32(b"CreBuild01")
    marble_t = pal.tex_marble(pseed % 1000)
    tm = tex_mean(marble_t)
    marble_i = kit.image("palace_marble", marble_t)
    ribs_t = pal.tex_ribs(pseed % 1000)
    ribs_i = kit.image("palace_copper", ribs_t)
    rub_t = tex_rubble(seed % 1000)
    m = {
        "marble": Mat("marble", marble_i, lin(pal.MARBLE), uv=2.5, rough=0.5),
        "copper": Mat("copper", ribs_i, lin(pal.COPPER), rough=0.45, metal=0.3, lay=False, soot=0.6),
        "gold": Mat("gold", None, lin(pal.GOLD), rough=0.3, metal=0.8, soot=0.5),
        "rubble": Mat("rubble", kit.image(name + "_rubble", rub_t), albedo(BED, tex_mean(rub_t)), uv=1.6, soot=0.6),
        "char": Mat("char", kit.image(name + "_char", tex_char(seed % 1000)), albedo("#3a2e26", 0.2), uv=1.2,
                    soot=0.3),
    }
    # the pieces: marble in the drawn tones from lit chips to sooted, copper
    # shards and gold from the spire and domes, burnt timbers
    tones = TONES
    pieces = [Mat("m_" + k, marble_i, albedo(c, tm), uv=1.5, ref=c, soot=0.35, share=m["marble"])
              for k, c in tones.items()]
    cop = [Mat("c_" + k, ribs_i, albedo(c, tex_mean(ribs_t)), uv=1.0, ref=c, soot=0.3, share=m["copper"])
           for k, c in (("lit", "#355a44"), ("dark", "#2f4f44"))]
    # a fallen dome's halves, their ribs running round them as on the intact
    m["copper_lit"] = Mat("copper_lit", None, albedo("#3f6a55", tex_mean(ribs_t)), ref="#3f6a55", lay=False, soot=0.3,
                          share=m["copper"])
    m["copper_dark"] = Mat("copper_dark", None, albedo("#2a4238", tex_mean(ribs_t)), ref="#2a4238", lay=False,
                           soot=0.3, share=m["copper"])
    gold = [Mat("g_bits", None, albedo("#9a7a32"), ref="#9a7a32", soot=0.2, share=m["gold"])]
    m["pieces"] = pieces
    m["coppers"] = cop
    m["golds"] = gold
    # the small masonry in the lit tones only: a sooted stone is lost on the dark bed
    m["stones"] = pale_weighted([Mat("s_" + k, marble_i, albedo(c, tm), uv=1.5, ref=c, soot=0.35, share=m["marble"])
                                 for k, c in tones.items() if k != "soot"], "s_")
    pw = pale_weighted(pieces)
    m["debris"] = {"slab": pw, "stone": pw, "chunk": pw, "boulder": pw,
                   "beam": [m["char"]], "board": [m["char"]], "tile": cop}
    m["debris_gold"] = dict(m["debris"], chunk=pw + gold)
    m["all"] = [m[k] for k in ("marble", "copper", "gold", "rubble", "char")] + pieces + cop + gold
    return m


def palace_deck_colour(soot):
    """The terrace's top as the intact draws it: marble, and the checkered
    border round it."""
    pseed = zlib.crc32(b"CreBuild01")
    mt = kit.srgb_to_lin(pal.tex_marble(pseed % 1000)[..., 0])
    tt = kit.srgb_to_lin(pal.tex_tiles()[..., 0])
    mc, tc = lin(pal.MARBLE), lin(pal.TILE)

    def colour(X, Y):
        ui = (np.floor((X * 0.4 % 1) * mt.shape[1])).astype(int) % mt.shape[1]
        vi = (np.floor((Y * 0.4 % 1) * mt.shape[0])).astype(int) % mt.shape[0]
        out = mt[vi, ui][..., None] * mc
        b = 1.0
        border = (X < X0 + b) | (X > X1 - b) | (Y < Y0 + b) | (Y > Y1 - b)
        ut = (np.floor((X % 1) * tt.shape[1])).astype(int) % tt.shape[1]
        vt = (np.floor((Y % 1) * tt.shape[0])).astype(int) % tt.shape[0]
        out = np.where(border[..., None], tt[vt, ut][..., None] * tc, out)
        return out
    return colour


def palace_corner_tower(B, M, c, keep, h=None, seed=0):
    """Corner tower c whole (keep) or broken to a stump h high."""
    cx, cy = CORNERS[c]
    if keep:
        put(B, M["marble"], pal.lathe([(0.62, 0), (0.62, H + 1.5), (0.72, H + 1.65), (0.72, H + 1.8), (0.5, H + 1.8)],
                                      16, cx, cy, 0.0, "tower"))
        put(B, M["copper"], pal.onion(cx, cy, 0.62, H + 1.8, 1.2), keep_uv=True)
        put(B, M["gold"], finial(cx, cy, H + 1.8 + 1.15, 0.09))
    elif h > H + 0.6:
        # standing clear of the terrace, its broken top open on the dark shaft
        hollow_stump(B, M["marble"], M["rubble"], cx, cy, 0.62, h, seed + c)
    else:
        stump(B, M["marble"], cx, cy, 0.62, h, jag=0.3, seed=seed + c)


def palace_onion_tower(B, M, j, whole=True, heights=None, gables=(), seed=0):
    """An onion tower west of the spire: whole, or its octagon broken to
    the eight heights over the terrace with only the gables named kept."""
    cx, cy = pal.ONIONS[j]
    if whole:
        put(B, M["marble"], pal.octagon(cx, cy, 1.15, H, H + 3.6))
        for i in range(8):
            put(B, M["marble"], pal.gable(cx, cy, math.pi / 8 + i * math.pi / 4, 1.15, H + 2.6, 0.8, 0.9, 0.15))
        put(B, M["marble"], pal.octagon(cx, cy, 0.85, H + 3.6, H + 4.4))
        put(B, M["gold"], pal.octagon(cx, cy, 0.9, H + 4.4, H + 4.6))
        put(B, M["copper"], pal.onion(cx, cy, 1.25, H + 4.6, 2.3), keep_uv=True)
        put(B, M["gold"], finial(cx, cy, H + 4.6 + 2.25, 0.18, seg=8))
        return
    oct_ring(B, M["marble"], cx, cy, 1.15, 0.4, H, heights, seed=seed)
    for i in gables:
        put(B, M["marble"], pal.gable(cx, cy, math.pi / 8 + i * math.pi / 4, 1.15, H + 2.6, 0.8, 0.9, 0.15))


def pediment(cx, cy, ang, r, z0, z1, z2, w, d=0.3, ribs=2, roof=1.6, drop=0.5):
    """An octagon's gable grown into a pediment facing out at ang on ring r:
    a wall w wide standing to z1 at its sides and z2 at its apex, its face
    stepped with ribs concentric arches, a pitched roof running roof cells
    back from it."""
    ca, sa = math.cos(ang), math.sin(ang)
    tx, ty = -sa, ca
    bm = bmesh.new()

    def prism(pts, v0, v1):
        n = len(pts)
        a = [bm.verts.new((cx + ca * (r + v0) + tx * u, cy + sa * (r + v0) + ty * u, z)) for u, z in pts]
        b = [bm.verts.new((cx + ca * (r + v1) + tx * u, cy + sa * (r + v1) + ty * u, z)) for u, z in pts]
        bm.faces.new(a)
        bm.faces.new(list(reversed(b)))
        for k in range(n):
            k1 = (k + 1) % n
            bm.faces.new((a[k], b[k], b[k1], a[k1]))

    def outline(f):
        return [(-w / 2 * f, z0), (w / 2 * f, z0), (w / 2 * f, z0 + (z1 - z0) * f), (0.0, z0 + (z2 - z0) * f),
                (-w / 2 * f, z0 + (z1 - z0) * f)]
    prism(outline(1.0), 0.0, -d)
    for k in range(1, ribs + 1):
        prism(outline(1.0 - 0.2 * k), 0.07 * k, 0.0)
    if roof:
        n = 3
        a = [(-w / 2, z1), (w / 2, z1), (0.0, z2)]
        A = [bm.verts.new((cx + ca * (r - d) + tx * u, cy + sa * (r - d) + ty * u, z)) for u, z in a]
        Bk = [bm.verts.new((cx + ca * (r - d - roof) + tx * u, cy + sa * (r - d - roof) + ty * u,
                            z - (drop if k == 2 else 0.0))) for k, (u, z) in enumerate(a)]
        bm.faces.new(A)
        bm.faces.new(list(reversed(Bk)))
        for k in range(n):
            k1 = (k + 1) % n
            bm.faces.new((A[k], Bk[k], Bk[k1], A[k1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def palace_heart(B, M, oct1, oct2, g1, g2, drum=None, peds=(), seed=0, S=None, band=(0.0, 360.0)):
    """The spire's octagons broken: oct1 and oct2 the heights of the lower
    and upper rings' eight sides (side k faces 22.5 + 45 k degrees), g1 and
    g2 the gables still standing on each, drum the stump of the drum, peds
    the upper ring's sides that still stand under their pediments (side,
    apex height). The gables go into S (B if not given), out of the
    line-of-sight cut that only the rings need. band is the arc (degrees)
    the floor between the tiers still spans."""
    S = B if S is None else S
    oct_ring(B, M["marble"], SX, SY, 3.6, 0.55, H, oct1, seed=seed, step=1.3)
    # the band and the floor between the tiers, under the upper ring's standing sides
    if band[1] - band[0] >= 359.9:
        put(B, M["marble"], pal.octagon(SX, SY, 3.75, H + 1.8, H + 2.0))
    elif band[1] > band[0]:
        shell(B, M["marble"], SX, SY, [(3.75, H + 1.8), (3.75, H + 2.0)], 3.7, seg=8, a0=band[0], a1=band[1])
    oct_ring(B, M["marble"], SX, SY, 2.7, 0.5, H + 2.0, oct2, seed=seed + 20, step=1.3)
    for i in g2:
        put(S, M["marble"], pal.gable(SX, SY, math.pi / 8 + i * math.pi / 4, 2.7, H + 3.1, 1.6, 1.5, 0.25))
    for i in g1:
        # palace.gable's kokoshnik with its ribs and the roof running back onto the band
        put(S, M["marble"], pediment(SX, SY, math.pi / 8 + i * math.pi / 4, 3.6, H + 0.9, H + 1.6, H + 2.2, 2.2,
                                     d=0.25, ribs=2, roof=0.9, drop=0.3))
    for i, apex, w in peds:
        put(S, M["marble"], pediment(SX, SY, math.pi / 8 + i * math.pi / 4, 2.75, H + 2.0, H + 4.2, apex, w,
                                     d=0.45, ribs=3, roof=1.3, drop=0.5))
    if drum:
        # the drum's stump stands on the upper ring's floor, where the heap holds it
        oct_ring(B, M["marble"], SX, SY, 2.0, 0.4, H + 2.0, [h + 2.2 for h in drum], seed=seed + 40, step=1.3)


def palace_shell(B, M, stage, rng, seed):
    """The terrace, its wall ribbed with pilasters, the cornice, the steps,
    the parapet broken where the stage's heaps lie (gaps), its crenels thinned."""
    terrace = dice_box(X0, X1, Y0, Y1, 0.0, H, 1.5, top=False)
    put(B, M["marble"], terrace)
    n = 11
    for i in range(n):
        x = X0 + 0.8 + i * (X1 - X0 - 1.6) / (n - 1)
        put(B, M["marble"], pal.prism(x - 0.18, x + 0.18, Y0 - 0.18, Y0, 0.25, H - 0.4))
    put(B, M["marble"], pal.prism(X0 - 0.12, X1 + 0.12, Y0 - 0.25, Y0 + 0.1, H - 0.45, H - 0.25))
    k = 14
    for i in range(k):
        z = H * (k - i) / k
        put(B, M["marble"], pal.prism(X0 - RUN * (i + 1) / k, X0 - RUN * i / k + 0.01, S_Y0, S_Y1, 0.0, z))
    for y in (S_Y0 - 0.25, S_Y1):
        put(B, M["marble"], pal.prism(X0 - RUN, X0, y, y + 0.25, 0.0, 0.5))


def palace_parapet(B, M, gaps, rng, keep_crenel=0.8, low=0.0):
    """The parapet round the terrace's rim, broken off inside gaps (plan
    polygons), its crenels kept by chance."""
    t, hp = 0.22, 0.35

    def broken(x, y):
        return any(akit.inside_poly(P, x, y) for P in gaps)

    runs = (((X0, Y0 + t / 2), (X1, Y0 + t / 2)), ((X1 - t / 2, Y0), (X1 - t / 2, Y1)),
            ((X1, Y1 - t / 2), (X0, Y1 - t / 2)), ((X0 + t / 2, Y1), (X0 + t / 2, Y0)))
    for (ax, ay), (bx, by) in runs:
        L = math.hypot(bx - ax, by - ay)
        n = max(2, int(L / 0.7))
        prof = []
        for i in range(n + 1):
            f = i / n
            x, y = ax + (bx - ax) * f, ay + (by - ay) * f
            prof.append((f, H + (low if broken(x, y) else hp)))
        akit.broken_wall(B, M["marble"], M["marble"], (ax, ay), (bx, by), t, prof, z0=H, step=1.1, jitter=0.04,
                         seed=rng.randint(0, 9999), notch=0.05)
    for x in np.arange(X0 + 0.4, X1 - 0.2, 0.7):
        for y0, y1 in ((Y0, Y0 + t), (Y1 - t, Y1)):
            if broken(x, (y0 + y1) / 2) or rng.random() > keep_crenel:
                continue
            put(B, M["marble"], pal.prism(x - 0.15, x + 0.15, y0, y1, H + hp, H + hp + 0.25))


def palace_ruin(spr, spec):
    name = spr.name
    stage = name[-1]
    r = akit.CAT[name]
    pic = akit.Picture(r)
    seed = zlib.crc32(name.encode())
    rng = random.Random(seed)
    M = palace_mats(name, pic, seed)
    B = akit.Builder(seed)  # what stands whole
    C = akit.Builder(seed + 1)  # the spire's octagons, cut to what the picture shows
    R = akit.Builder(seed + 2)  # heaps and the pieces on them
    T = PALACE[stage]

    def on(h):
        return lambda x, y: palace_base(x, y) + h

    def on_top(h):
        # the terrace read as if it ran on behind its walls, so a heap traced
        # on its back rows stays on it rather than falling behind the back wall
        def z(x, y):
            if X0 - 1.0 <= x <= X1 + 1.0 and y >= Y0:
                return H + h
            return palace_base(x, y) + h
        return z

    heaps = {}
    poly_of = {key: poly for key, poly, h in T["heaps"]}
    for key, poly, h in T["heaps"]:
        if poly is None:
            # the spire's rings' inside, filled to h over the terrace
            heaps[key] = ([(SX + 3.3 * math.cos(math.pi / 8 + k * math.pi / 4),
                            SY + 0.3 + 3.3 * math.sin(math.pi / 8 + k * math.pi / 4)) for k in range(8)], h)
        elif key in T.get("on_top", ()):
            P = plan(r, poly, on_top(h * 0.7))
            heaps[key] = ([(x, min(y, Y1 - 0.15)) if X0 - 1.0 <= x <= X1 + 1.0 else (x, y) for x, y in P], h)
        else:
            heaps[key] = (plan(r, poly, on(h * 0.7)), h)
    gaps = [P for key, (P, h) in heaps.items() if key in T["gaps"]]
    palace_shell(B, M, stage, rng, seed)
    palace_parapet(B, M, gaps, rng, keep_crenel=T["crenels"])
    for c in range(4):
        palace_corner_tower(B, M, c, T["towers"][c] is None, T["towers"][c], seed)
    for j in range(2):
        o = T["onions"][j]
        if o is None:
            palace_onion_tower(B, M, j)
        else:
            palace_onion_tower(B, M, j, False, o[0], o[1], seed + 100 * j)
    palace_heart(C, M, T["oct1"], T["oct2"], T["g1"], T["g2"], T.get("drum"), T.get("peds", ()), seed, S=B,
                 band=T.get("band", (0.0, 360.0)))
    cut = [akit.jag(p, 2.5, 5.0, seed + i) for i, p in enumerate(T["open"])]
    standing = objs(B)
    heart = objs(C)
    if os.environ.get("G1_TRIS"):
        print("G1_HEART before cut", sum(len(p.vertices) - 2 for o, m in heart for p in o.data.polygons), flush=True)
    if cut:
        akit.cut_view([o for o, m in heart], cut, r, zlo=H + 0.5)
    # heaps on the terrace end at its rim. Where one meets a wall the picture
    # draws rubble over, a heap of its own lies at that wall's foot, leaning
    # on it, so no bed hangs down a wall. The picture's outline decides
    # where the ground shows between stones.
    fields = {}
    ref = np.asarray(albedo(BED))
    beds = {k: Mat("rubble_" + k, colour=M["rubble"].colour * np.asarray(albedo(c)) / ref, ref=c, uv=1.6,
                   share=M["rubble"], soot=0.6)
            for k, c in T.get("beds", {}).items()}
    for key, (P, h) in heaps.items():
        keep = (lambda x, y, z: pic.solid(*akit.screen(r, x, y, z), grow=1)) if key in T["gappy"] else None
        kw = dict(cell=0.6, edge=T["edge"].get(key, 0.5), seed=seed + len(fields), keep=keep,
                  lumps=T.get("lumps", {}).get(key, 0.5), rough=T.get("rough", {}).get(key, 0.2))
        base, top = palace_base, h
        if poly_of[key] is None:
            # the spire's rings hold the heart, a cell of lumps over its fill,
            # and it pours down where they are broken
            base, top = (lambda x, y, z=H + h - 1.0: z), 1.0
            kw.update(lean=outside_circle(SX, SY + 0.3, 3.0),
                      zmax=rim_cap(SX, SY, 3.25, [(22.5 + 45 * k, H + v) for k, v in enumerate(T["oct1"])], drop=0.1))
        else:
            kw.update(on=on_terrace)
        fields[key] = (mound(R, beds.get(key, M["rubble"]), P, base, top, **kw), P)
    feet = []
    for key, (reach, fh) in T["feet"].items():
        for side, Q in foot_outlines(heaps[key][0], reach, seed + len(feet)):
            k2 = key + "_" + side
            # a straight slope from its toe up to the wall
            fields[k2] = (mound(R, beds.get(key, M["rubble"]), Q, flat_base, fh, cell=0.45, edge=0.3,
                                seed=seed + 50 + len(feet), lean=palace_built, lumps=0.4), Q)
            feet.append((k2, key, side, Q))
    for i, (key, (F, P)) in enumerate(fields.items()):
        src = key.split("_")[0]
        n = int(DENSITY * T["pieces"] * T.get("more", {}).get(src, 1.0) * akit_area(P))
        kinds = T.get("kinds", {}).get(key, {"slab": 1, "chunk": 7, "stone": 3, "beam": 1})
        strew(R, M["debris_gold"] if key in T["golden"] else M["debris"], r, P, F, n, kinds, pic, seed + 300 + i,
              **T.get("sizes", {}).get(key, {}))
        if key in T.get("scree", {}):
            scree(R, M["debris"], r, P, F, T["scree"][key], pic, seed + 600 + i)
        if key in T["copper"]:
            n, size = T["copper"][key]
            shards(R, M["coppers"][i % 2], r, pic, F, P, int(round(n * DENSITY)), seed + 400 + i, size=size,
                   curve=0.45)
        if key in T.get("beams", {}):
            rafters(R, M["char"], r, P, F, T["beams"][key], seed + 500 + i, pic=pic)
        # small stones on the heap and along its edge
        st = T.get("stones", {}).get(src, 0.45)
        g = (lambda F: lambda x, y: max(F(x, y), palace_base(x, y)) if F(x, y) > -50 else -100.0)(F)
        masonry(R, M["stones"], r, P, g, int(DENSITY * st * akit_area(P)), pic, seed + 700 + i)
        masonry(R, M["stones"], r, P, g, int(T.get("rim", 0.8) * perimeter(P)), pic, seed + 800 + i, rim=True)

    def ground(key):
        F, P = fields[key]
        return lambda x, y: max(F(x, y), palace_base(x, y))
    # the copper roof slabs and the gold where the picture draws them
    for key, col, row, w, h, yaw, pitch, roll, k, *cv in T.get("slabs", ()):
        x, y, z = at_pixel(r, col, row, ground(key), 0.1, fields[key][1], "slab")
        plate(R, M["coppers"][k], x, y, z, w, h, yaw, pitch, roll, curve=cv[0] if cv else 0.15, t=0.14,
              nu=3 if cv else 2, nv=2 if cv else 1)
    for key, col, row, yaw, tip, s in T.get("finials", ()):
        x, y, z = at_pixel(r, col, row, ground(key), 0.1, fields[key][1], "finial")
        lying(R, M["gold"], finial(0, 0, 0, s), x, y, z, yaw, tip)
    for key, col, row, s in T.get("glints", ()):
        x, y, z = at_pixel(r, col, row, ground(key), 0.0, fields[key][1], "glint")
        R.box(M["golds"][0], x, y, z - 0.1, s, s * 0.8, s * 0.6, yaw=30, pitch=20)
    for key, col, row, R0, Hd, sink in T.get("domes", ()):
        x, y, z = at_pixel(r, col, row, ground(key), 0.0, fields[key][1], "dome")
        split_onion(R, M, x, y, z - sink, R0, Hd, seed)
    thin(R, ["m_soot"], T.get("soot_keep", 0.33), seed + 11)
    # nothing the classic camera cannot see: pieces behind the back wall, below its top
    hid = drop_hidden(R, lambda c, top: c.y > Y1 + 0.05 and top < H + 0.3)
    fit_budget(R, [o for o, m in standing + heart], seed + 13)
    if os.environ.get("G1_TRIS"):
        print("G1_HIDDEN", hid, flush=True)
    rubble = objs(R)
    rough_pieces(rubble, seed=seed % 97)
    burns = [(SX, SY + 1.5, H + 3.0, 3.5, 0.4)] + list(T.get("burns", ()))
    zones = []
    for key, (P, h) in heaps.items():
        cx, cy = sum(p[0] for p in P) / len(P), sum(p[1] for p in P) / len(P)
        burns.append((cx, cy, palace_base(cx, cy) + h, 2.5, 0.35))
        zones.append((P, 0.35, 1.3))
    for k2, key, side, Q in feet:
        # the wall over a heap at its foot sooted, as the picture draws rubble down it
        (ax, ay), (bx, by) = Q[-1], Q[-2]
        span = math.hypot(bx - ax, by - ay)
        burns.append(((ax + bx) / 2, (ay + by) / 2, H * 0.6, max(1.4, min(2.8, span / 2)), 0.5))
    for c in range(4):
        h = T["towers"][c]
        if h is not None and h > H + 0.6:
            burns.append((CORNERS[c][0], CORNERS[c][1], h - 0.2, 0.5, 0.75))
    # the zones (the dark the pictures keep where the intact's spire cast
    # its shadow) soot the terrace's top only, the walls round them keep their colour
    soot = Soot(seed, base=T["base"], burns=burns, zones=zones, blotch=0.25, streak=0.25)
    dzones = [(P, 0.85, 0.6) for P, h in heaps.values()]
    dzones += [(plan(r, P, lambda x, y: H), s, soft) for P, s, soft in T["zones"] + T["smudges"]]
    dsoot = Soot(seed + 5, base=1.0, burns=burns[:1], zones=dzones, blotch=0.3, streak=0.0, freq=0.3)
    deckm = Mat("terrace_top", plan_tex(name + "_deck", X0, X1, Y0, Y1, H, 24, palace_deck_colour(dsoot), dsoot),
                (1, 1, 1), rough=1.0, lay=False, soot=0.0)
    D = akit.Builder(seed + 3)
    # one quad: its soot is in its tile, not its vertices
    deck(D, deckm, X0, X1, Y0, Y1, H, step=100.0)
    top = objs(D)
    clean = T.get("steps_clean", 1.0)

    spans = []
    for k2, key, side, Q in feet:
        u = (lambda p: p[0]) if side == "s" else (lambda p: p[1])
        spans.append((side, min(u(Q[-1]), u(Q[-2])), max(u(Q[-1]), u(Q[-2]))))

    def wall_face(co, n):
        # the terrace's wall over a heap at its foot, sooted dark in streaks
        # where the picture draws rubble down it
        k = 1.0
        for side, a, b in spans:
            if side == "s":
                u, d, out = co.x, Y0 - co.y, -n.y
            elif side == "w":
                u, d, out = co.y, X0 - co.x, -n.x
            else:
                u, d, out = co.y, co.x - X1, n.x
            if -0.05 < d < 0.5 and co.z < H + 0.45 and out > 0.3:
                t = min(1.0, (u - a + 0.4) / 0.8, (b + 0.4 - u) / 0.8)
                if t > 0:
                    st = noise.noise(Vector((u * 1.9, co.z * 0.25, 7.0)))
                    k *= 1 - t * min(0.85, 0.6 + 0.3 * st)
        return k

    def soot_w(co, n):
        # the west steps stay the picture's palest marble, out of the burns
        f = soot(co, n)
        if co.x < X0 - 0.02 and S_Y0 - 0.4 <= co.y <= S_Y1 + 0.4 and co.z <= H + 0.05:
            return max(f, clean)
        return f * wall_face(co, n)
    paint(standing + heart + top, soot_w)
    paint_rubble(rubble, seed + 7, burns)
    parts = [o for o, m in standing + heart + top + rubble if len(o.data.polygons)]
    return parts, {"heaps": len(heaps)}


def akit_area(P):
    a = 0.0
    for i in range(len(P)):
        a += P[i - 1][0] * P[i][1] - P[i][0] * P[i - 1][1]
    return abs(a) / 2


# Each stage of the palace as its picture draws it. Outlines are in that
# picture's pixels (col, row). A heap is (key, outline, height), towers
# give a corner tower's stump height or None for whole (SW, SE, NW, NE),
# onions give for the front and back onion tower None (whole) or the
# eight sides' heights and the gables kept, oct1, oct2 the spire's rings.
FULL1, FULL2 = 1.8, 2.2
PALACE = {
    "a": dict(
        base=0.92, crenels=0.6, pieces=1.0,
        towers=(2.9, 2.6, None, 0.4 + H),
        onions=(([1.0, 0.7, 0.6, 0.7, 1.5, 2.8, 2.6, 1.6], (5,)), None),
        # the rings' north-east is down into the heap, where the picture
        # draws the spire's dark ribs and its shadow; the floor between the
        # tiers is left only under the standing south sides
        oct1=[0.0, 0.0, 0.4, 0.6, 1.0, FULL1, FULL1, FULL1],
        oct2=[0.0, 0.0, 0.0, 0.0, 0.7, FULL2, FULL2, FULL2],
        g1=(4, 5, 6, 7), g2=(7,), peds=((5, 11.0, 3.0), (6, 10.3, 2.3)), band=(180.0, 360.0),
        drum=[-0.8, -0.6, -1.4, -1.6, -1.2, 0.8, 1.0, 1.1],
        # what the picture shows fallen out of the spire's rings: the heap's top behind them
        open=[[(150, 32), (176, 26), (204, 30), (206, 48), (204, 66), (198, 80), (186, 85), (170, 86),
               (156, 84), (148, 66)]],
        heaps=[
            ("west", [(6, 66), (20, 58), (40, 54), (58, 50), (80, 60), (100, 58), (112, 72), (110, 96),
                      (100, 114), (82, 124), (60, 118), (46, 114), (26, 108), (8, 104), (2, 88)], 1.3),
            ("onion", [(100, 84), (120, 80), (142, 86), (154, 98), (156, 116), (146, 126), (128, 126),
                       (110, 120), (100, 104)], 1.2),
            ("frontw", [(40, 168), (62, 160), (86, 164), (102, 172), (114, 186), (120, 202), (116, 220),
                        (108, 234), (88, 241), (66, 242), (52, 236), (46, 220), (40, 196)], 1.1),
            ("fronte", [(150, 190), (176, 184), (206, 189), (236, 191), (256, 196), (258, 216), (250, 232),
                        (226, 240), (196, 244), (170, 240), (154, 232), (146, 212)], 1.0),
            ("east", [(270, 170), (290, 166), (303, 180), (306, 200), (298, 214), (284, 210), (274, 198)], 0.9),
            ("ne", [(250, 30), (270, 24), (293, 32), (299, 46), (292, 60), (280, 58), (266, 46), (254, 40)], 0.8),
            ("back", [(148, 28), (176, 22), (205, 26), (222, 30), (224, 46), (206, 52), (180, 50), (160, 52)], 0.9),
            ("heart", None, 4.2),
        ],
        gaps=("west", "frontw", "fronte", "east", "ne", "back"),
        gappy=("west", "frontw", "fronte", "east"),
        on_top=("back", "ne", "onion"),
        # the heaps at the walls' foot, under the rubble the picture draws down
        # them: how far out from the wall and how high
        feet={"frontw": (1.7, 1.6), "fronte": (1.7, 1.6), "east": (1.5, 1.4), "west": (1.8, 1.6)},
        more={"frontw": 2.5, "fronte": 2.5, "east": 1.8, "ne": 1.4, "back": 1.4},
        scree={"frontw": 10, "fronte": 8, "east": 5, "west": 5},
        # the upper ring's crown sooted, as the picture keeps it in shadow
        burns=[(SX, SY - 2.0, H + 4.2, 2.0, 0.35)],
        edge={"heart": 2.2},
        golden=("onion", "heart", "frontw"),
        copper={"onion": (4, (0.6, 1.1)), "heart": (4, (0.7, 1.2)), "frontw": (3, (0.5, 0.9)),
                "west": (2, (0.5, 0.9))},
        # the spire's copper plates lying across the heap, its finial flat in the onion heap
        # the spire's shell in big curved ribbed pieces
        slabs=[("heart", 174, 72, 2.0, 1.6, 20, 25, -18, 0, 0.5), ("heart", 162, 74, 1.5, 1.2, -40, -20, 12, 0, 0.5),
               ("onion", 142, 95, 1.4, 1.1, -30, -20, 15, 1), ("onion", 133, 116, 1.3, 1.0, 60, 18, 22, 0),
               # the spire's dark ribs standing slantwise out of the heap's north-east
               ("heart", 206, 66, 0.5, 2.6, -30, 58, 0, 1), ("heart", 218, 60, 0.45, 2.4, -25, 62, 0, 1),
               ("heart", 226, 76, 0.5, 2.2, -35, 55, 0, 1)],
        finials=[("onion", 128, 95, 206, 86, 0.42)],
        glints=[("back", 216, 38, 0.3)],
        zones=[([(222, 40), (288, 40), (288, 168), (240, 168), (228, 110)], 0.99, 1.0)],
        smudges=[([(62, 144), (96, 142), (100, 162), (66, 166)], 0.75, 0.6),
                 ([(40, 124), (56, 124), (58, 144), (42, 144)], 0.7, 0.5),
                 ([(118, 158), (140, 158), (142, 174), (120, 174)], 0.7, 0.5),
                 ([(100, 76), (150, 74), (152, 104), (104, 106)], 0.8, 0.6),
                 ([(140, 30), (226, 30), (226, 110), (150, 110)], 0.8, 0.6)],
    ),
}



# the shell: the ruin with more fallen, from the shell's own picture
PALACE["b"] = dict(
    base=0.8, crenels=0.35, pieces=0.95, soot_keep=0.25,
    towers=(1.9, 1.8, 5.3, 3.2),
    onions=(([0.6, 0.4, 0.4, 0.5, 0.9, 1.5, 1.3, 0.8], ()), ([1.4, 1.0, 0.8, 0.9, 1.2, 1.0, 0.8, 1.1], ())),
    oct1=[0.0, 0.0, 0.3, 0.4, 0.6, FULL1, FULL1, FULL1],
    oct2=[0.0, 0.0, 0.0, 0.0, 0.3, FULL2, FULL2, 1.7],
    g1=(5, 6, 7), g2=(), peds=((5, 10.7, 3.0), (6, 9.9, 2.3)), band=(180.0, 360.0),
    drum=[-0.8, -0.6, -1.6, -1.6, -1.4, 0.6, 1.2, 1.3],
    open=[[(118, 22), (148, 13), (176, 7), (204, 11), (206, 29), (204, 47), (198, 61), (186, 66), (170, 67),
           (150, 66), (130, 60), (118, 46)]],
    heaps=[
        ("back", [(2, 55), (15, 40), (40, 25), (60, 12), (100, 8), (140, 10), (180, 8), (220, 6), (260, 10),
                  (290, 20), (304, 40), (302, 70), (290, 86), (262, 88), (238, 82), (224, 62), (206, 42),
                  (176, 36), (152, 36), (160, 52), (158, 72), (130, 78), (104, 86), (88, 96), (60, 98), (30, 100),
                  (10, 96), (2, 75)], 1.7),
        # the north-west onion tower's heap, run north-east under the fallen dome
        ("onion", [(100, 62), (116, 44), (140, 34), (170, 36), (188, 46), (190, 66), (172, 84), (160, 100),
                   (140, 112), (118, 110), (102, 96)], 1.4),
        ("frontw", [(38, 155), (60, 146), (90, 148), (115, 156), (140, 166), (150, 184), (146, 205),
                    (130, 216), (100, 221), (70, 223), (50, 216), (42, 195)], 1.3),
        ("fronte", [(150, 168), (180, 163), (215, 167), (250, 170), (272, 173), (288, 185), (284, 206),
                    (266, 213), (230, 221), (195, 223), (165, 219), (150, 200)], 1.2),
        ("east", [(284, 148), (300, 146), (312, 165), (310, 190), (298, 198), (288, 186)], 0.9),
        ("heart", None, 4.0),
    ],
    gaps=("back", "frontw", "fronte", "east", "onion"),
    gappy=("back", "frontw", "fronte", "east"),
    on_top=("back", "onion"),
    feet={"frontw": (1.7, 1.6), "fronte": (1.7, 1.6), "east": (1.5, 1.4), "back": (1.8, 1.6)},
    more={"frontw": 2.4, "fronte": 2.4, "east": 1.8, "back": 1.1},
    lumps={"back": 0.8}, rough={"back": 0.35},
    scree={"frontw": 14, "fronte": 10, "east": 6, "back": 8},
    beds={"back": "#57514a"},
    edge={"heart": 2.2, "back": 1.5},
    golden=("onion", "heart", "back"),
    copper={"onion": (4, (0.6, 1.1)), "heart": (4, (0.7, 1.2)), "frontw": (4, (0.5, 0.9)),
            "back": (5, (0.6, 1.1))},
    slabs=[("onion", 140, 82, 1.4, 1.1, -30, -20, 15, 1), ("onion", 132, 100, 1.3, 1.0, 60, 18, 22, 0)],
    finials=[("onion", 118, 84, 206, 86, 0.42)],
    glints=[("back", 166, 42, 0.3)],
    # the surviving onion dome split in two in the heap, its gold neck between the halves
    domes=[("onion", 155, 56, 1.6, 2.9, 0.3)],
    # coarse masonry: blocks up to a cell and slabs of one to two lying tilted
    kinds={"back": {"chunk": 6, "stone": 3, "slab": 2}},
    sizes={"back": dict(chunk=(0.55, 1.1), stone=(0.45, 0.8), size=(0.9, 1.8), thick=(0.2, 0.4), tilt=22)},
    beams={"back": 9, "fronte": 2, "frontw": 2},
    zones=[([(240, 86), (288, 86), (288, 150), (246, 150)], 0.99, 1.0)],
    smudges=[([(62, 124), (98, 122), (100, 146), (64, 148)], 0.75, 0.6),
             ([(118, 140), (146, 140), (148, 156), (120, 156)], 0.7, 0.5),
             ([(20, 98), (60, 98), (60, 112), (22, 112)], 0.6, 0.6)],
)


# ---------------------------------------------------------------- shells

def shell(B, mat, cx, cy, prof, t, seg=24, a0=0.0, a1=360.0, z0=0.0):
    """A thick surface of revolution round (cx, cy): prof is the outer
    (r, z) from the bottom up, the inner face t inside it, so a cut shows
    the shell's thickness. a0..a1 limits it to an arc (an apse's half-dome),
    closing the cut ends."""
    full = abs(a1 - a0) >= 359.9
    n = seg if full else max(2, int(round(seg * (a1 - a0) / 360.0)))
    angs = [math.radians(a0 + (a1 - a0) * k / n) for k in range(n if full else n + 1)]
    inner = []
    for i, (r, z) in enumerate(prof):
        j0, j1 = max(0, i - 1), min(len(prof) - 1, i + 1)
        dr, dz = prof[j1][0] - prof[j0][0], prof[j1][1] - prof[j0][1]
        L = math.hypot(dr, dz) or 1.0
        nr, nz = dz / L, -dr / L  # outward normal of the profile
        inner.append((max(0.02, r - nr * t), z - nz * t))
    rows = list(prof) + list(reversed(inner))
    V, F = [], []
    m = len(angs)
    for r, z in rows:
        for a in angs:
            V.append((cx + r * math.cos(a), cy + r * math.sin(a), z0 + z))
    R = len(rows)
    span = m if full else m - 1
    for i in range(R - 1):
        for k in range(span):
            k1 = (k + 1) % m
            F.append((i * m + k, i * m + k1, (i + 1) * m + k1, (i + 1) * m + k))
    for k in range(span):
        k1 = (k + 1) % m
        F.append(((R - 1) * m + k, (R - 1) * m + k1, k1, k))
    if not full:
        # close the arc's ends with strips from the outer face to the inner,
        # so the shell stays a closed solid that cuts cleanly
        for side in (0, m - 1):
            for i in range(len(prof) - 1):
                a, b = i, i + 1
                c, d = R - 2 - i, R - 1 - i
                q = [a * m + side, b * m + side, c * m + side, d * m + side]
                F.append(tuple(q if side else reversed(q)))
    B.solid(mat, V, F)


def barrel(B, mat, u0, u1, c, z0, r, t, seg=12, step=1.0, axis="x"):
    """A barrel vault, a thick half-cylinder from u0 to u1 along x (axis x,
    centred on y = c) or along y (centred on x = c), springing at z0 and
    diced every step along its length so soot can mark it."""
    n = max(1, int(round(abs(u1 - u0) / step)))
    V, F = [], []
    prof = [(math.cos(math.pi * k / seg), math.sin(math.pi * k / seg)) for k in range(seg + 1)]
    rows = [(r * a, r * b) for a, b in prof] + [((r - t) * a, (r - t) * b) for a, b in reversed(prof)]
    R = len(rows)
    for i in range(n + 1):
        u = u0 + (u1 - u0) * i / n
        for a, b in rows:
            V.append((u, c + a, z0 + b) if axis == "x" else (c + a, u, z0 + b))
    for i in range(n):
        for k in range(R):
            k1 = (k + 1) % R
            F.append((i * R + k, i * R + k1, (i + 1) * R + k1, (i + 1) * R + k))
    # the ends, strips from the outer arc to the inner
    for i in (0, n):
        for k in range(seg):
            q = [i * R + k, i * R + k + 1, i * R + R - 2 - k, i * R + R - 1 - k]
            F.append(tuple(q if i else reversed(q)))
    B.solid(mat, V, F)


def dome_prof(R, rise, n=8, r_top=0.12):
    """A dome's outer profile, an ellipse of radius R and rise, stopped
    short of its apex where the finial sits."""
    return [(max(r_top, R * math.cos(math.pi / 2 * k / n)), rise * math.sin(math.pi / 2 * k / n)) for k in range(n + 1)]


def tympanum(B, mat, u, c, z0, r, t, seg=12, axis="x"):
    """The half-disc wall closing a barrel vault's end, t thick from u."""
    pts = [(c + r * math.cos(math.pi * k / seg), z0 + r * math.sin(math.pi * k / seg)) for k in range(seg + 1)]
    V = []
    for off in (0.0, t):
        for a, b in pts:
            V.append((u + off, a, b) if axis == "x" else (a, u + off, b))
    m = len(pts)
    F = [tuple(range(m)), tuple(m + k for k in reversed(range(m)))]
    for k in range(m - 1):
        F.append((k, k + 1, m + k + 1, m + k))
    F.append((m - 1, 0, m, 2 * m - 1))
    B.solid(mat, V, F)


def drum_bands(D, M, cx, cy, arcs, bands):
    """A broken drum's rings and window band, kept only over the arcs
    (degrees) where the drum still stands to them."""
    for key, (a0, a1) in arcs.items():
        rr, z0, z1, t, mat = bands[key]
        shell(D, mat, cx, cy, [(rr, z0), (rr, z1)], t, seg=24, a0=a0, a1=a1)


def broken_drum(D, mar, cx, cy, r, z0, z1, top, bands, seed, seg=20, jag=0.35, t=0.35):
    """A drum t thick broken to the heights top gives round it ([(angle,
    z)]), its stone in courses so soot can mark it, and its bands (r, z0,
    z1, t, mat) broken off with it where it stands lower than they do."""
    fn = heights(top)
    n = max(2, int(round((z1 - z0) / 2.3)))
    prof = [(r, z0 + (z1 - z0) * i / n) for i in range(n + 1)]
    broken_shell(D, mar, cx, cy, [(a, b - z0) for a, b in prof], t, lambda a: fn(a) - z0, seg=seg, z0=z0,
                 jag=jag, seed=seed)
    for rr, b0, b1, t, mat in bands:
        broken_shell(D, mat, cx, cy, [(rr, 0.0), (rr, b1 - b0)], t, lambda a, b0=b0: fn(a) - b0, seg=seg, z0=b0,
                     jag=jag, seed=seed)


def akit_profile(prof, f, dflt):
    """A broken wall's height at fraction f of its run."""
    if not prof:
        return dflt
    pf = sorted(prof)
    return float(np.interp(f, [q[0] for q in pf], [q[1] for q in pf]))


# ---------------------------------------------------------------- the great hall

GREEN = "#2a5e4e"  # the hall's tiles as the intact draws them, lit
HALL = dict(
    nave=(-7.6, 6.6, -2.4, 3.4), wall=3.4, t=0.4, yc=0.5,
    apse=(-7.6, 0.5, 2.9),
    dome=(-3.4, 0.5), drum=(2.75, 3.4, 10.4), eave=3.05, dome_r=2.8, rise=3.0,
    porch=(-6.1, -0.7, -4.1, -2.4, 3.9),
    towers=((7.4, -1.65), (7.4, 2.45)), tower_r=1.05,
    # the towers' shaft and dome, and the gilded onion's scale on it: the
    # crown and spike reach the picture's top row
    tower_h=6.6, tower_rise=2.9, onion_k=1.4,
)


def stone_mats(name, seed, roof, roof_name, tones_roof):
    """Marble walls (the palace's own tile and colour), a glazed roof tile in
    roof's drawn colour, gold, and the pieces a ruin breaks into."""
    pseed = zlib.crc32(b"CreBuild01")
    marble_t = pal.tex_marble(pseed % 1000)
    tm = tex_mean(marble_t)
    marble_i = kit.image("stone_marble", marble_t)
    tiles_t = tex_tiles(zlib.crc32(roof_name.encode()) % 1000)
    tiles_i = kit.image(roof_name + "_tiles", tiles_t)
    tt = tex_mean(tiles_t)
    rub_t = tex_rubble(seed % 1000)
    m = {
        "marble": Mat("marble", marble_i, lin(pal.MARBLE), uv=2.5, rough=0.5),
        # glazed, but a sheen, not a glare: a glossier tile lights a vault's
        # crest as a pale band the pictures never draw
        "tiles": Mat("tiles", tiles_i, albedo(roof, tt), uv=1.2, rough=0.62, metal=0.08),
        "gold": Mat("gold", None, lin(pal.GOLD), rough=0.3, metal=0.8, soot=0.5),
        "window": Mat("window", None, albedo("#3a2a40"), rough=0.6, soot=0.3),
        "rubble": Mat("rubble", kit.image(name + "_rubble", rub_t), albedo(BED, tex_mean(rub_t)), uv=1.6, soot=0.6),
        "char": Mat("char", kit.image(name + "_char", tex_char(seed % 1000)), albedo("#3a2e26", 0.2), uv=1.2,
                    soot=0.3),
    }
    tones = TONES
    pieces = [Mat("m_" + k, marble_i, albedo(c, tm), uv=1.5, ref=c, soot=0.35, share=m["marble"])
              for k, c in tones.items()]
    shards_ = [Mat("t_" + k, tiles_i, albedo(c, tt), uv=0.8, ref=c, soot=0.3, share=m["tiles"])
               for k, c in tones_roof.items()]
    gold = [Mat("g_bits", None, albedo("#9a7a32"), ref="#9a7a32", soot=0.2, share=m["gold"])]
    m["pieces"], m["shards"], m["golds"] = pieces, shards_, gold
    # the small masonry in the lit tones only: a sooted stone is lost on the dark bed
    m["stones"] = [Mat("s_" + k, marble_i, albedo(c, tm), uv=1.5, ref=c, soot=0.35, share=m["marble"])
                   for k, c in tones.items() if k != "soot"]
    m["stones"].append(Mat("s_tile", tiles_i, albedo(tones_roof["lit"], tt), uv=0.8, ref=tones_roof["lit"], soot=0.3,
                           share=m["tiles"]))
    m["stones"] = pale_weighted(m["stones"], "s_")
    pw = pale_weighted(pieces)
    m["debris"] = {"slab": pw + shards_, "stone": pw, "chunk": pw, "beam": [m["char"]],
                   "board": [m["char"]], "tile": shards_}
    m["debris_gold"] = dict(m["debris"], chunk=pw + gold)
    return m


def hall_parts(G, M, st=None):
    """The great hall, whole or broken as the stage dict st says. G is a
    dict of Builders by group: 'walls', 'roof' (vaults and the apse's
    half-dome), 'dome' (drum, dome and finial), 'east' (the towers)."""
    st = st or {}
    P = HALL
    x0, x1, y0, y1 = P["nave"]
    wh, t, yc = P["wall"], P["t"], P["yc"]
    mar, til, gold = M["marble"], M["tiles"], M["gold"]
    W, Rf, D, E = G["walls"], G["roof"], G["dome"], G["east"]
    seed = st.get("seed", 0)
    walls = st.get("walls", {})

    def wall(key, a, b, base_h=wh):
        prof = walls.get(key, [(0, base_h), (1, base_h)])
        akit.broken_wall(W, mar, mar, a, b, t, prof, step=0.8, jitter=0.15 if key in walls else 0.0,
                         seed=seed + sum(map(ord, key)), notch=0.25 if key in walls else 0.0)

    px0, px1, py0, py1, ph = P["porch"]
    wall("front_w", (x0, y0 + t / 2), (px0, y0 + t / 2))
    wall("front_e", (px1, y0 + t / 2), (x1, y0 + t / 2))
    wall("back", (x1, y1 - t / 2), (x0, y1 - t / 2))
    wall("east", (x1 - t / 2, y0), (x1 - t / 2, y1))
    for x in np.arange(px1 + 0.6, x1 - 0.3, 1.5):
        top = min(wh, akit_profile(walls.get("front_e"), (x - px1) / (x1 - px1), wh))
        if top > 1.2:
            put(W, mar, pal.prism(x - 0.16, x + 0.16, y0 - 0.14, y0 + 0.05, 0.0, top - 0.15))
            put(W, M["window"], pal.prism(x + 0.3, x + 1.2, y0 - 0.02, y0 + 0.02, 0.9, min(top - 0.6, 2.6)))
    if st.get("cornice", True):
        put(W, mar, pal.prism(px1, x1, y0 - 0.2, y0 + 0.1, wh - 0.25, wh))
        put(W, mar, pal.prism(x0, px0, y0 - 0.2, y0 + 0.1, wh - 0.25, wh))
    rv = (y1 - y0) / 2
    if st.get("vault", True):
        barrel(Rf, til, x0, x1, yc, wh, rv, 0.3, seg=8, step=2.4)
    if st.get("tympanum", True):
        tympanum(W, mar, x1 + 0.03, yc, wh, rv, -0.38)
    ax, ay, ar = P["apse"]
    if "apse_wall" in st:
        akit.ring_wall(W, mar, mar, ax, ay, ar - t / 2, t, st["apse_wall"], seg=14, seed=seed + 5)
    else:
        shell(W, mar, ax, ay, [(ar, 0.0), (ar, wh)], t, seg=14, a0=90, a1=270)
        put(W, mar, pal.lathe([(ar + 0.12, 0), (ar + 0.12, 0.22)], 14, ax, ay, wh - 0.22, "cornice"))
    if st.get("apse_dome", True):
        shell(Rf, til, ax, ay, dome_prof(ar, ar * 0.95, 7), 0.3, seg=14, a0=90, a1=270, z0=wh)
    pw = (px1 - px0) / 2
    pcx = (px0 + px1) / 2
    wall("porch_s", (px0, py0 + t / 2), (px1, py0 + t / 2), ph)
    wall("porch_w", (px0 + t / 2, py1), (px0 + t / 2, py0), ph)
    wall("porch_e", (px1 - t / 2, py0), (px1 - t / 2, py1), ph)
    for x in np.linspace(px0 + 0.4, px1 - 0.4, 5):
        top = min(ph, akit_profile(walls.get("porch_s"), (x - px0) / (px1 - px0), ph))
        if top > 1.2:
            put(W, mar, pal.prism(x - 0.2, x + 0.2, py0 - 0.18, py0 + 0.05, 0.0, top - 0.1))
    if st.get("porch_vault", True):
        barrel(Rf, til, py0, yc, pcx, ph, pw, 0.3, seg=9, step=1.4, axis="y")
        tympanum(W, mar, py0 - 0.03, pcx, ph, pw, 0.33, axis="y")
        put(W, mar, pal.prism(px0 - 0.12, px1 + 0.12, py0 - 0.25, py0 + 0.1, ph - 0.25, ph))
    dx, dy = P["dome"]
    dr, dz0, dz1 = P["drum"]
    drum = st.get("drum")
    if drum is None:
        shell(D, mar, dx, dy, [(dr, dz0), (dr, dz1)], 0.35, seg=20)
        shell(D, mar, dx, dy, [(dr + 0.15, 8.0), (dr + 0.15, 8.3)], 0.5, seg=20)
        shell(D, M["window"], dx, dy, [(dr + 0.02, 8.7), (dr + 0.02, 9.8)], 0.1, seg=20)
        shell(D, mar, dx, dy, [(P["eave"], dz1 - 0.2), (P["eave"], dz1 + 0.05)], 0.6, seg=20)
    elif drum:
        broken_drum(D, mar, dx, dy, dr, st.get("drum_z0", dz0), dz1, drum,
                    [(dr + 0.15, 8.0, 8.3, 0.5, mar), (dr + 0.02, 8.7, 9.8, 0.1, M["window"]),
                     (P["eave"], dz1 - 0.2, dz1 + 0.05, 0.6, mar)], seed + 9)
    dprof = dome_prof(P["dome_r"], P["rise"], 6, 0.35)
    if st.get("dome_break"):
        # the dome broken open to a ragged thick rim, curved as it was
        broken_shell(D, til, dx, dy, dprof, 0.3, heights(st["dome_break"]), seg=20, z0=dz1, jag=0.3, seed=seed + 4)
    elif st.get("dome", True):
        shell(D, til, dx, dy, dprof, 0.3, seg=20, z0=dz1)
    if st.get("dome_frag"):
        # what is left of the dome's shell, sunk onto the broken drum
        a0, a1, z, top = st["dome_frag"]
        broken_shell(D, til, dx, dy, dprof, 0.3, heights(top, wrap=False), seg=24, a0=a0, a1=a1, z0=z, jag=0.3,
                     seed=seed + 5)
    if st.get("finial", True):
        za = dz1 + P["rise"] - 0.05
        put(D, gold, pal.finial(dx, dy, za, 0.9))
        put(D, gold, pal.lathe([(0.1, 0.0), (0.07, 2.4), (0.0, 3.6)], 8, dx, dy, za + 4.1, "spike"))
    for i, (tx, ty) in enumerate(P["towers"]):
        state = st.get("towers", (None, None))[i]
        tr = P["tower_r"]
        if state is None:
            th, rise, k = P["tower_h"], P["tower_rise"], P["onion_k"]
            put(E, mar, pal.lathe([(tr, 0), (tr, th)], 16, tx, ty, 0.0, "tower"))
            put(E, mar, pal.lathe([(tr + 0.14, 0), (tr + 0.14, 0.3)], 16, tx, ty, 2.85, "cornice"))
            put(E, mar, pal.lathe([(tr + 0.12, 0), (tr + 0.12, 0.25)], 16, tx, ty, th - 0.15, "collar"))
            shell(E, til, tx, ty, dome_prof(tr, rise, 6, 0.18), 0.2, seg=16, z0=th)
            # the gilded onion on its neck, and the spike with its small cross
            zn = th + rise - 0.05
            put(E, gold, pal.lathe([(0.2 * k, 0.0), (0.2 * k, 0.25 * k)], 8, tx, ty, zn, "neck"))
            zo = zn + 0.2 * k
            put(E, gold, gold_onion(tx, ty, zo, 0.55 * k, 1.35 * k))
            put(E, gold, pal.lathe([(0.07 * k, 0.0), (0.04 * k, 1.3 * k), (0.0, 1.4 * k)], 6, tx, ty,
                                   zo + 1.3 * k, "spike"))
            zc = zo + 2.15 * k
            put(E, gold, pal.prism(tx - 0.22 * k, tx + 0.22 * k, ty - 0.04 * k, ty + 0.04 * k, zc, zc + 0.08 * k))
        else:
            stump(E, mar, tx, ty, tr, state, jag=0.4, seed=seed + 31 * i, sink=0.6)
            if state > 3.2:
                put(E, mar, pal.lathe([(tr + 0.14, 0), (tr + 0.14, 0.3)], 16, tx, ty, 2.85, "cornice"))


def intact(parts_fn, mats_fn, roof, roof_name, tones):
    def build_intact(spr, spec):
        """An intact building, for checking against its picture (not shipped)."""
        seed = zlib.crc32(spr.name.encode())
        M = mats_fn(spr.name, seed, roof, roof_name, tones)
        G = {k: akit.Builder(seed + i) for i, k in enumerate(("walls", "roof", "dome", "east"))}
        parts_fn(G, M)
        pairs = []
        for b in G.values():
            pairs += objs(b)
        paint(pairs, Soot(seed, base=1.0, blotch=0.0, streak=0.0))
        return [o for o, m in pairs if len(o.data.polygons)], {}
    return build_intact


HALL_TONES = {"lit": "#3f7d68", "dark": "#284a40"}


# ---------------------------------------------------------------- a stage from its intact

GROUPS = ("walls", "roof", "dome", "east")


def outline(r, item, zread):
    """A heap's plan outline: traced pixels read at height zread (a number,
    or a surface zread(x, y) met by each pixel's line of sight), or
    ('circle', x, y, radius) in plan."""
    if item[0] == "circle":
        _, x, y, rad = item
        return [(x + rad * math.cos(2 * math.pi * k / 12), y + rad * math.sin(2 * math.pi * k / 12)) for k in range(12)]
    if item[0] == "plan":
        return list(item[1])
    return plan(r, item, zread if callable(zread) else (lambda x, y: zread))


def stage_ruin(spr, T, parts_fn, mats_fn, roof, roof_name, tones, inside=None):
    """A ruin or shell built as its intact (parts_fn) broken as T says:
    T['state'] for what stands, T['cuts'] (group, outlines, zlo) cut along
    the classic camera's line of sight, T['heaps'] (key, outline, height,
    read height) heaped on the ground, a roof or a drum's floor, pieces,
    roof shards and fallen gold strewn on them, and soot round it all."""
    name = spr.name
    r = akit.CAT[name]
    pic = akit.Picture(r)
    seed = zlib.crc32(name.encode())
    M = mats_fn(name, seed, roof, roof_name, tones)
    G = {k: akit.Builder(seed + i) for i, k in enumerate(GROUPS)}
    st = dict(T["state"], seed=seed)
    parts_fn(G, M, st)
    groups = {k: objs(b) for k, b in G.items()}
    for i, (g, polys, zlo) in enumerate(T["cuts"]):
        obs = [o for o, m in groups[g] if len(o.data.polygons)]
        if obs:
            akit.cut_view(obs, [akit.jag(p, 2.5, 7.0, seed + 17 * i + j) for j, p in enumerate(polys)], r, zlo=zlo)
    R = akit.Builder(seed + 9)
    fields = {}
    bases = T.get("bases", {})
    ref = np.asarray(albedo(BED))
    beds = {k: Mat("rubble_" + k, colour=M["rubble"].colour * np.asarray(albedo(c)) / ref, ref=c, uv=1.6,
                   share=M["rubble"], soot=0.6) for k, c in T.get("beds", {}).items()}
    for P in T.get("floors", ()):
        # the gutted inside's floor, a bed of rubble, so no breach shows through to nothing
        R.mesh(M["rubble"], [(x, y, 0.03) for x, y in P], [tuple(range(len(P)))])
    for key, (cx, cy, rr, floor) in T.get("fills", {}).items():
        # a drum's fill: a cell of lumps over a floor, held in by the drum
        # and pouring down to its broken rim where it stands low
        bases[key] = (lambda cx, cy, rr, fl: lambda x, y: fl if math.hypot(x - cx, y - cy) < rr + 0.3 else 0.0)(
            cx, cy, rr, floor)
    for i, (key, item, h, zread) in enumerate(T["heaps"]):
        if zread is None:
            # read on the surface the heap lies on, part way up the heap
            zread = (lambda b, k: (lambda x, y: b(x, y) + k))(bases.get(key, flat_base), 0.7 * h)
        P = outline(r, item, zread)
        if key in T.get("clamp", {}):
            P = T["clamp"][key](P)
        keep = (lambda x, y, z: pic.solid(*akit.screen(r, x, y, z), grow=1)) if key in T.get("gappy", ()) else None
        kw = dict(on=T.get("on", {}).get(key), lean=T.get("lean", {}).get(key), zmax=T.get("zmax", {}).get(key))
        if key in T.get("lean_in", {}):
            # inside the building, beyond its outline, the heap runs on under the roof
            kw["lean"] = (lambda fn, P: lambda x, y: fn(x, y) and not akit.inside_poly(P, x, y))(T["lean_in"][key], P)
        if key in T.get("fills", {}):
            cx, cy, rr, floor = T["fills"][key]
            rim = T.get("fill_rims", {}).get(key, T["state"]["drum"])
            kw.update(lean=outside_circle(cx, cy, rr * 0.95), zmax=rim_cap(cx, cy, rr, rim, 0.3))
        F = mound(R, beds.get(key, M["rubble"]), P, bases.get(key, flat_base), h,
                  cell=T.get("cells", {}).get(key, T.get("cell", 0.7)), edge=T.get("edge", {}).get(key),
                  seed=seed + i, keep=keep, lumps=T.get("lumps", {}).get(key, 0.5), **kw)
        fields[key] = (F, P, h)
        if os.environ.get("G1_TRIS"):
            zs = F.Z[F.mesh]
            print("G1_HEAP", key, "points", int(F.ins.sum()), "z %.2f..%.2f" % (zs.min(), zs.max()) if zs.size else "", flush=True)
    for i, (key, (F, P, h)) in enumerate(fields.items()):
        n = int(DENSITY * T["pieces"] * T.get("more", {}).get(key, 1.0) * akit_area(P))
        kinds = T.get("kinds", {"slab": 2, "chunk": 6, "stone": 3, "beam": 1})
        dm = M["debris_gold"] if key in T.get("golden", ()) else M["debris"]
        strew(R, dm, r, P, F, n, kinds, pic, seed + 300 + i)
        # small stones on the heap and along its edge
        base = bases.get(key, flat_base)
        g = (lambda F, base: lambda x, y: max(F(x, y), base(x, y)) if F(x, y) > -50 else -100.0)(F, base)
        masonry(R, M["stones"], r, P, g, int(DENSITY * T.get("stones", {}).get(key, 0.45) * akit_area(P)), pic,
                seed + 700 + i)
        masonry(R, M["stones"], r, P, g, int(T.get("rim", 0.8) * perimeter(P)), pic, seed + 800 + i, rim=True)
        if key in T.get("scree", {}):
            scree(R, M["debris"], r, P, F, T["scree"][key], pic, seed + 600 + i)
        if key in T.get("shards", {}):
            k, size = T["shards"][key]
            shards(R, M["shards"][i % len(M["shards"])], r, pic, F, P, int(round(k * DENSITY)), seed + 400 + i,
                   size=size)
        if key in T.get("beams", {}):
            rafters(R, M["char"], r, P, F, T["beams"][key], seed + 500 + i, pic=pic, poke=(0.2, 0.8))

    def ground(key):
        F, P, h = fields[key]
        base = bases.get(key, flat_base)
        return lambda x, y: max(F(x, y), base(x, y))
    for key, col, row, yaw, tip, s, *shape in T.get("bulbs", ()):
        x, y, z = at_pixel(r, col, row, ground(key), 0.05, fields[key][1], "bulb")
        bm = gold_onion(0, 0, 0, s * 1.3, s * 3.2) if shape == ["onion"] else finial(0, 0, 0, s)
        lying(R, M["gold"], bm, x, y, z, yaw, tip)
    for key, col, row, w, h, yaw, pitch, roll, k, *prop in T.get("plates", ()):
        # big pieces of the roof's own tiles lying where the picture draws them
        x, y, z = at_pixel(r, col, row, ground(key), 0.12, fields[key][1], "plate")
        if prop:
            # propped up: its foot in the heap at the pixel, leaning back on what stands behind it
            cp, a = math.cos(math.radians(pitch)), math.radians(yaw)
            x, y, z = (x - math.sin(a) * cp * h / 2, y + math.cos(a) * cp * h / 2,
                       z - 0.3 + math.sin(math.radians(pitch)) * h / 2)
        plate(R, M["shards"][k] if isinstance(k, int) else M[k], x, y, z, w, h, yaw, pitch, roll, curve=0.3,
              t=0.26, nu=3, nv=2)
    for z, x, y, yaw, tip, R0, Hd in T.get("domes", ()):
        # a tower's dome broken open, sat back on its stump at z
        lying(R, M["tiles"], broken_onion(R0, Hd, cut=0.55, seed=seed), x, y, z, yaw, tip, keep_uv=False)
    for nm, k in T.get("thin", {}).items():
        n0 = len(R.bms[nm].faces) if nm in R.bms else 0
        thin(R, [nm], k, seed + len(nm))
        if os.environ.get("G1_TRIS"):
            print("G1_THIN", nm, n0, len(R.bms[nm].faces) if nm in R.bms else 0, flush=True)
    fit_budget(R, [o for g in GROUPS for o, m in groups[g]], seed + 13)
    rubble = objs(R)
    rough_pieces(rubble, seed=seed % 97)
    burns, zones = [], []
    for key, (F, P, h) in fields.items():
        cx, cy = sum(p[0] for p in P) / len(P), sum(p[1] for p in P) / len(P)
        burns.append((cx, cy, h, 2.2 + 0.4 * h, 0.4))
    for (x, y, z, rad, s_) in T.get("burns", ()):
        burns.append((x, y, z, rad, s_))
    soot = Soot(seed, base=T.get("base", 0.95), burns=burns, zones=zones, blotch=0.35, streak=0.35, inside=inside,
                gut=T.get("gut", 0.7))
    standing = [p for g in GROUPS for p in groups[g]]
    clean = T.get("clean")
    if clean:
        # the outer faces clean names (a drum's lit bands) keep their marble:
        # no gutting, and the burns together take off no more than cap
        test, cbase, cap = clean
        csoot = Soot(seed + 3, base=cbase, burns=burns, blotch=0.2, streak=0.2)
        paint(standing, lambda co, n: max(csoot(co, n), cbase * (1 - cap)) if test(co, n) else soot(co, n))
    else:
        paint(standing, soot)
    paint_rubble(rubble, seed + 7, burns)
    parts = [o for o, m in standing + rubble if len(o.data.polygons)]
    return parts, {"heaps": len(fields)}


# Each stage of the great hall from its own picture (pixels col, row).
# state: what of the intact stands (walls are broken_wall profiles along
# each run, towers a stump height or None for whole), cuts remove what the
# picture shows fallen, heaps are (key, outline, height, read at).
def hall_vault(x, y):
    """The top of the hall's nave vault in plan, the ground beside it."""
    x0, x1, y0, y1 = HALL["nave"]
    rv, yc = (y1 - y0) / 2, HALL["yc"]
    if x0 <= x <= x1 and abs(y - yc) < rv:
        return HALL["wall"] + math.sqrt(rv * rv - (y - yc) ** 2)
    return 0.0


def dome_top(rho, R, rise):
    """The height of a dome_prof dome's outer face over its springing at radius rho."""
    return rise * math.sqrt(max(0.0, 1 - (rho / R) ** 2)) if rho < R else -1.0


def gutted(co, n, env):
    """How far a point lies in a burnt-out interior: under the intact's roof
    (env, its outer face's height there or None outside), off its outer faces
    and not facing the sky (a vertex on a broken rim leans half up, so only
    a point facing almost straight up counts), so a wall's inner face and a
    vault's or a dome's underside darken while what stood outside keeps its
    colour."""
    if n.z > 0.8:
        return 0.0
    z = env(co.x, co.y)
    if z is None:
        return 0.0
    return min(1.0, max(0.0, (z - 0.12 - co.z) / 0.15))


def hall_env(x, y):
    """The intact hall's roof over (x, y) inside its walls (nave, porch,
    apse, drum and dome), None outside them."""
    P = HALL
    x0, x1, y0, y1 = P["nave"]
    t, wh, yc = P["t"] * 0.6, P["wall"], P["yc"]
    zs = []
    rv = (y1 - y0) / 2
    if x0 + t < x < x1 - t and y0 + t < y < y1 - t:
        zs.append(wh + math.sqrt(max(0.0, rv * rv - (y - yc) ** 2)))
    px0, px1, py0, py1, ph = P["porch"]
    pw, pcx = (px1 - px0) / 2, (px0 + px1) / 2
    if px0 + t < x < px1 - t and py0 + t < y < yc:
        zs.append(ph + math.sqrt(max(0.0, pw * pw - (x - pcx) ** 2)))
    ax, ay, ar = P["apse"]
    rho = math.hypot(x - ax, y - ay)
    if x <= ax and rho < ar - t:
        zs.append(wh + dome_top(rho, ar, ar * 0.95))
    dx, dy = P["dome"]
    dr, dz0, dz1 = P["drum"]
    rho = math.hypot(x - dx, y - dy)
    if rho < dr - 0.2:
        zs.append(dz1 + max(0.0, dome_top(rho, P["dome_r"], P["rise"])))
    return max(zs) if zs else None


def hall_inside(co, n):
    return gutted(co, n, hall_env)


def hall_drum_out(co, n):
    """A point on the hall drum's outer face or its bands' outer and upper
    faces, above the vault round it."""
    dx, dy = HALL["dome"]
    dr = HALL["drum"][0]
    vx, vy = co.x - dx, co.y - dy
    rho = math.hypot(vx, vy)
    if rho < dr - 0.12 or rho > HALL["eave"] + 0.2 or co.z < 5.4:
        return False
    return (n.x * vx + n.y * vy) / max(rho, 1e-6) > 0.2 or n.z > 0.5


def hall_roof(x, y):
    """The top of the intact hall's vaults and the apse's half-dome over
    (x, y), the ground elsewhere: what a heap on its roof lies on."""
    P = HALL
    x0, x1, y0, y1 = P["nave"]
    wh, yc = P["wall"], P["yc"]
    z = hall_vault(x, y)
    px0, px1, py0, py1, ph = P["porch"]
    pw, pcx = (px1 - px0) / 2, (px0 + px1) / 2
    if px0 <= x <= px1 and py0 <= y <= yc:
        z = max(z, ph + math.sqrt(max(0.0, pw * pw - (x - pcx) ** 2)))
    ax, ay, ar = P["apse"]
    rho = math.hypot(x - ax, y - ay)
    if x <= ax and rho < ar:
        z = max(z, wh + dome_top(rho, ar, ar * 0.95))
    return z


def sector(cx, cy, r0, r1, a0, a1, n=8):
    """A plan outline: the ring sector between radii r0 and r1 from a0 to a1 degrees."""
    angs = [math.radians(a0 + (a1 - a0) * k / n) for k in range(n + 1)]
    return ([(cx + r0 * math.cos(a), cy + r0 * math.sin(a)) for a in angs]
            + [(cx + r1 * math.cos(a), cy + r1 * math.sin(a)) for a in reversed(angs)])


def on_hall_roof(x, y, k=0.72):
    """On the hall's vaults and the apse's half-dome where they slope no
    steeper than a heap's bed may lie (within k of their half-span)."""
    if hall_roof(x, y) <= 0.5:
        return False
    P = HALL
    x0, x1, y0, y1 = P["nave"]
    px0, px1, py0, py1, ph = P["porch"]
    ax, ay, ar = P["apse"]
    if x0 <= x <= x1 and abs(y - P["yc"]) <= k * (y1 - y0) / 2:
        return True
    if px0 <= x <= px1 and py0 <= y <= P["yc"] and abs(x - (px0 + px1) / 2) <= k * (px1 - px0) / 2:
        return True
    return x <= ax and math.hypot(x - ax, y - ay) <= k * ar


def under_hall_roof(x, y):
    """A ceiling for a heap inside the hall: a little over the intact roof
    where there is one (it shows only through a breach), none outside."""
    z = hall_roof(x, y)
    return z + 0.3 if z > 0.5 else 99.0


def half_disc(cx, cy, r, east=0.6, n=10):
    """A plan outline: the west half of a disc, run a little east past its centre."""
    return ([(cx + east, cy + r)] + [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a)))
                                     for a in np.linspace(90, 270, n)] + [(cx + east, cy - r)])


def hall_floors():
    """The hall's inside in plan: nave, porch and apse, for its rubble floor."""
    x0, x1, y0, y1 = HALL["nave"]
    px0, px1, py0, py1, ph = HALL["porch"]
    ax, ay, ar = HALL["apse"]
    return [[(x0 + 0.2, y0 + 0.2), (x1 - 0.2, y0 + 0.2), (x1 - 0.2, y1 - 0.2), (x0 + 0.2, y1 - 0.2)],
            [(px0 + 0.2, py0 + 0.2), (px1 - 0.2, py0 + 0.2), (px1 - 0.2, y0 + 0.25), (px0 + 0.2, y0 + 0.25)],
            list(reversed(half_disc(ax, ay, ar - 0.2, 0.0)))]


def in_hall(x, y):
    """Inside the hall's walls: nave and porch."""
    x0, x1, y0, y1 = HALL["nave"]
    px0, px1, py0, py1, ph = HALL["porch"]
    return (x0 < x < x1 and y0 + 0.2 < y < y1) or (px0 + 0.2 < x < px1 - 0.2 and py0 + 0.2 < y <= y0 + 0.2)


def in_circle(cx, cy, r):
    return lambda x, y: math.hypot(x - cx, y - cy) < r


def ring_cap(cx, cy, r, z0, slope=SLOPE):
    """A ceiling that holds a heap down to z0 at the foot of a round tower
    (cx, cy, r), rising away from it no steeper than slope."""
    return lambda x, y: z0 + slope * max(0.0, math.hypot(x - cx, y - cy) - r)


HALLS = {
    "a": dict(
        base=0.95, pieces=0.78,
        state=dict(
            walls={"front_w": [(0, 2.2), (0.5, 1.4), (1, 2.0)],
                   "front_e": [(0, 3.4), (0.14, 3.4), (0.2, 1.8), (0.35, 0.9), (0.5, 1.5), (0.6, 3.0), (0.66, 3.4),
                               (1, 3.4)]},
            apse_wall=[(90, 3.4), (130, 3.0), (160, 2.2), (190, 1.6), (220, 2.4), (250, 3.2), (270, 3.4)],
            # the front east tower a stump with a broken dome sat on it
            finial=False, towers=(3.3, None),
            # the drum stands on its west and front, broken down on its south-east
            drum=[(0, 7.0), (30, 8.0), (60, 10.6), (250, 10.6), (268, 9.6), (285, 7.2), (305, 5.8), (330, 5.6),
                  (350, 6.2)],
            # the dome's shell curls over the west, gone over the breach
            dome_break=[(0, -1.0), (40, -1.0), (65, 0.6), (90, 1.6), (120, 2.5), (160, 2.7), (210, 2.3), (240, 1.2),
                        (262, 0.3), (280, -1.0)],
        ),
        cuts=[
            # (the dark wedge behind the dome is its shadow on whole tiles, not a hole)
            ("roof", [[(242, 52), (264, 46), (290, 54), (294, 76), (276, 90), (250, 88)],
                      [(268, 96), (296, 90), (304, 120), (288, 142), (266, 132)],
                      [(6, 100), (28, 90), (52, 98), (64, 124), (58, 162), (30, 168), (8, 152)],
                      # the porch vault's west flank, under the heap
                      [(84, 122), (108, 116), (124, 128), (122, 160), (112, 192), (88, 196), (82, 160)]], 3.3),
        ],
        heaps=[
            # the drum's fill, a cell of lumps over its floor
            ("dome", ("circle", -3.4, 0.5, 2.5), 1.0, 0.0),
            # heaps lying on the roof over its breaches, each filling its hole and
            # standing a little proud of the vault round it
            ("breach", [(140, 96), (166, 88), (188, 100), (190, 124), (178, 140), (152, 140), (140, 120)], 1.2, None),
            ("porchw", [(84, 120), (110, 114), (126, 128), (124, 160), (110, 174), (86, 168)], 0.8, None),
            ("hole2", [(238, 48), (264, 42), (292, 50), (298, 78), (278, 94), (246, 92)], 0.9, None),
            ("holee", [(264, 92), (298, 86), (308, 120), (290, 146), (262, 136)], 0.8, None),
            # the apse filled where its half-dome fell, pouring over its broken wall
            ("apse", ("plan", half_disc(-7.6, 0.5, 2.55)), 1.0, 0.0),
            # and the rubble outside it, leaning on its wall
            ("west", [(2, 110), (15, 95), (35, 95), (55, 105), (65, 130), (60, 160), (45, 172), (20, 168),
                      (5, 150)], 1.4, 1.0),
            ("frontw", [(62, 140), (90, 134), (118, 140), (122, 170), (115, 200), (95, 210), (70, 205),
                        (62, 180)], 1.8, 1.0),
            # the vault's front fallen in under its breach: one heap through the
            # broken front wall, rising inside the nave under the hole
            ("frontm", [(182, 126), (206, 116), (224, 124), (244, 138), (250, 160), (245, 195), (215, 198),
                        (192, 190), (184, 165)], 4.0, 1.2),
            # held low round the front tower's stump so its lit face shows
            ("towerf", [(276, 98), (300, 92), (330, 100), (342, 125), (345, 160), (335, 180), (305, 186),
                        (285, 176), (272, 140)], 2.2, 1.0),
        ],
        fills={"dome": (-3.4, 0.5, 2.4, 7.6), "apse": (-7.6, 0.5, 2.55, 1.7)},
        fill_rims={"apse": [(90, 3.4), (130, 3.0), (160, 2.2), (190, 1.6), (220, 2.4), (250, 3.2), (270, 3.4)]},
        floors=hall_floors(),
        lean={"west": in_circle(-7.6, 0.5, 2.9), "towerf": in_circle(7.4, 2.45, 1.05)},
        lean_in={"frontm": in_hall},
        gappy=("west", "frontw", "frontm", "towerf", "breach"),
        bases={"breach": hall_roof, "porchw": hall_roof, "hole2": hall_roof, "holee": hall_roof},
        on={"breach": on_hall_roof, "porchw": on_hall_roof, "hole2": on_hall_roof, "holee": on_hall_roof},
        zmax={"towerf": ring_cap(7.4, -1.65, 1.05, 1.2), "frontm": under_hall_roof},
        cells={"breach": 0.5, "porchw": 0.5, "hole2": 0.5, "holee": 0.5},
        lumps={"dome": 0.12, "hole2": 0.3, "holee": 0.3, "breach": 0.3, "porchw": 0.3},
        beds={"dome": "#6b645a", "breach": "#57514a", "hole2": "#57514a", "holee": "#57514a", "porchw": "#57514a"},
        edge={"breach": 0.6, "hole2": 0.5, "holee": 0.5, "porchw": 0.5},
        more={"frontm": 1.2, "frontw": 1.2, "towerf": 1.1, "dome": 1.0, "breach": 1.3, "hole2": 1.4, "holee": 1.2,
              "porchw": 1.2},
        # small masonry over the heaps the picture draws densest
        stones={"towerf": 1.8, "frontm": 1.8, "west": 1.8, "hole2": 1.8},
        golden=("dome", "towerf"),
        scree={"west": 2, "frontw": 4, "frontm": 4, "towerf": 4},
        shards={"dome": (8, (0.8, 1.4)), "hole2": (3, (0.6, 1.0)),
                "frontw": (4, (0.5, 0.9)), "towerf": (5, (0.5, 1.0)), "frontm": (3, (0.5, 0.9)),
                "west": (4, (0.6, 1.1))},
        beams={"dome": 1, "breach": 2, "hole2": 1, "frontm": 1, "west": 1},
        bulbs=[("breach", 160, 100, 30, 80, 0.4), ("towerf", 313, 108, 200, 75, 0.42, "onion")],
        plates=[("breach", 168, 116, 1.6, 1.2, 30, 25, -15, 0), ("hole2", 262, 66, 1.4, 1.0, -20, 20, 10, 1)],
        # the front tower's dome broken open on its stump, slid west
        domes=[(3.0, 7.05, -1.65, 180, 25, 1.12, 2.2)],
        thin={"t_dark": 0.3, "t_lit": 0.6, "m_soot": 0.5},
        # the vault sooted where the dome's shadow falls on it behind the dome
        burns=[(-3.4, 0.5, 9.0, 3.5, 0.45), (1.6, 1.8, 6.0, 2.5, 0.5), (5.2, 2.2, 6.0, 2.0, 0.5),
               (2.0, -2.4, 3.0, 2.2, 0.5), (0.2, 2.0, 6.0, 2.2, 0.55)],
    ),
}
HALLS["b"] = dict(
    base=0.74, pieces=0.6, gut=0.8,
    # the drum's west half is the picture's palest standing mass, its bands lit marble
    clean=(hall_drum_out, 0.92, 0.2),
    state=dict(
        walls={"front_w": [(0, 1.6), (0.5, 1.0), (1, 1.5)],
               "front_e": [(0, 2.2), (0.2, 1.4), (0.4, 0.9), (0.55, 1.6), (0.75, 2.4), (0.9, 1.6), (1, 2.0)],
               "back": [(0, 2.0), (0.3, 1.2), (0.5, 2.2), (0.7, 1.4), (1, 1.8)],
               "east": [(0, 1.8), (0.5, 1.2), (1, 2.0)]},
        apse_wall=[(90, 2.4), (130, 1.8), (160, 1.2), (190, 1.0), (220, 1.6), (250, 2.2), (270, 2.4)],
        vault=False, tympanum=False, finial=False, cornice=False, dome=False, apse_dome=False,
        # the drum's west half stands in its bands, its front broken down into the heap
        drum=[(0, 6.4), (45, 7.0), (80, 8.6), (110, 10.6), (220, 10.6), (245, 8.4), (265, 5.0), (290, 4.6),
              (320, 5.8)],
        # and the dome's shell curls over it there
        dome_frag=(110, 240, 10.4, [(110, 0.6), (140, 1.9), (175, 2.4), (210, 1.8), (240, 0.5)]),
        towers=(2.8, 6.2), drum_z0=0.0,
    ),
    cuts=[
        ("roof", [[(0, 60), (30, 44), (56, 52), (72, 90), (76, 140), (0, 150)],
                  # the porch vault broken off where it met the drum's fallen front
                  [(120, 60), (150, 56), (176, 64), (178, 92), (168, 104), (140, 100), (120, 90)]], 3.2),
    ],
    heaps=[
        # the drum filled with the fallen dome nearly to its broken top, a cell of lumps over its floor
        ("dome", ("circle", -3.4, 0.5, 2.6), 1.0, 0.0),
        ("nave", [(150, 30), (200, 20), (250, 22), (290, 30), (292, 60), (290, 110), (280, 140), (250, 150),
                  (200, 152), (165, 145), (150, 110), (152, 60)], 1.8, 1.2),
        ("apse", [(10, 70), (40, 45), (80, 35), (100, 60), (100, 110), (85, 132), (55, 142), (28, 134),
                  (12, 110)], 1.8, 1.0),
        ("frontw", [(62, 125), (90, 118), (118, 125), (122, 150), (115, 176), (95, 184), (74, 176),
                    (68, 150)], 1.4, 1.0),
        # what fell out of the drum's broken front, leaning on the drum
        ("drumfront", ("plan", sector(-3.4, 0.5, 2.5, 5.0, 248, 335)), 3.0, 0.0),
        ("frontm", [(186, 135), (215, 130), (250, 134), (275, 140), (278, 165), (245, 172), (215, 172),
                    (190, 168)], 1.4, 1.0),
        # held low round the front tower's stump so its lit face shows
        ("towerf", [(276, 60), (300, 55), (335, 62), (348, 95), (350, 135), (338, 160), (305, 165),
                    (285, 158), (272, 110)], 1.8, 1.2),
    ],
    fills={"dome": (-3.4, 0.5, 2.4, 7.4)},
    floors=hall_floors(),
    lean={"drumfront": in_circle(-3.4, 0.5, 2.75), "towerf": in_circle(7.4, 2.45, 1.05)},
    zmax={"towerf": ring_cap(7.4, -1.65, 1.05, 1.0)},
    gappy=("apse", "frontw", "frontm", "towerf", "nave"),
    kinds={"slab": 1, "chunk": 5, "stone": 4, "beam": 1},
    edge={"nave": 1.5},
    lumps={"dome": 0.15},
    beds={"dome": "#7a7266"},
    more={"frontm": 1.4, "frontw": 1.4, "towerf": 1.4, "nave": 1.2, "dome": 1.8},
    # the field as stones over thin dark joints: small masonry, densest where the picture is
    stones={"nave": 2.2, "apse": 2.0, "frontw": 2.0, "frontm": 2.0, "towerf": 2.0, "drumfront": 1.6, "dome": 1.2},
    golden=("dome", "towerf"),
    scree={"apse": 3, "frontw": 4, "frontm": 4, "towerf": 4, "drumfront": 3},
    shards={"dome": (8, (0.8, 1.4)), "nave": (16, (0.7, 1.3)), "apse": (6, (0.7, 1.3)),
            "frontw": (4, (0.6, 1.0)), "towerf": (5, (0.6, 1.1)), "frontm": (5, (0.6, 1.0)),
            "drumfront": (5, (0.7, 1.3))},
    beams={"nave": 6, "dome": 3, "apse": 3, "frontm": 2, "towerf": 2, "drumfront": 2},
    bulbs=[("towerf", 315, 104, 200, 75, 0.42, "onion")],
    plates=[("nave", 236, 76, 1.8, 1.3, 20, 25, -10, 0), ("nave", 200, 60, 1.5, 1.1, -30, -15, 20, 1),
            ("dome", 130, 48, 1.6, 1.2, 25, 22, -12, 0),
            # the apse's half-dome down in its heap as a tilted piece of its tiles
            ("apse", 50, 92, 2.0, 1.4, 30, 25, -15, 0)],
    # the towers' domes broken open on their stumps, the front one slid west
    domes=[(5.9, 7.4, 2.45, 30, 12, 1.12, 2.2), (2.5, 7.05, -1.65, 180, 25, 1.12, 2.2)],
    thin={"t_dark": 0.32, "m_soot": 0.5},
    burns=[(-3.4, 0.5, 6.0, 3.5, 0.5), (1.6, 0.5, 2.0, 4.0, 0.5), (5.2, 0.5, 2.0, 3.0, 0.5),
           (-3.4, -2.2, 5.0, 2.5, 0.55)],
)


def hall_ruin(spr, spec):
    return stage_ruin(spr, HALLS[spr.name[-1]], hall_parts, stone_mats, GREEN, "hall", HALL_TONES, hall_inside)


# ---------------------------------------------------------------- the senate

BLUE = "#3c5c7a"  # the senate's tiles as the intact draws them
SENATE_TONES = {"lit": "#4f718d", "dark": "#2a3e52"}
SENATE = dict(
    centre=(0.0, 1.8), arms=(-5.4, 5.6, -0.9, 4.5), arm_wall=3.1, arm_rise=2.0,
    nave=(-3.5, 3.6, -3.6, 5.8), nave_wall=3.5, nave_rise=1.8, t=0.4,
    drum=(3.2, 3.4, 10.3), eave=3.45, dome_r=3.0, rise=3.4,
    steps=(0.05, -3.45, 2.75),
)


def ell_barrel(B, mat, u0, u1, c, z0, r, rise, t, seg=9, step=1.6, axis="x"):
    """A barrel vault whose arch is an ellipse r wide and rise high."""
    n = max(1, int(round(abs(u1 - u0) / step)))
    prof = [(math.cos(math.pi * k / seg), math.sin(math.pi * k / seg)) for k in range(seg + 1)]
    rows = [(r * a, rise * b) for a, b in prof] + [((r - t) * a, (rise - t) * b) for a, b in reversed(prof)]
    R = len(rows)
    V, F = [], []
    for i in range(n + 1):
        u = u0 + (u1 - u0) * i / n
        for a, b in rows:
            V.append((u, c + a, z0 + b) if axis == "x" else (c + a, u, z0 + b))
    for i in range(n):
        for k in range(R):
            k1 = (k + 1) % R
            F.append((i * R + k, i * R + k1, (i + 1) * R + k1, (i + 1) * R + k))
    for i in (0, n):
        for k in range(seg):
            q = [i * R + k, i * R + k + 1, i * R + R - 2 - k, i * R + R - 1 - k]
            F.append(tuple(q if i else reversed(q)))
    B.solid(mat, V, F)


def ell_tympanum(B, mat, u, c, z0, r, rise, t, seg=10, axis="x"):
    pts = [(c + r * math.cos(math.pi * k / seg), z0 + rise * math.sin(math.pi * k / seg)) for k in range(seg + 1)]
    V = []
    for off in (0.0, t):
        for a, b in pts:
            V.append((u + off, a, b) if axis == "x" else (a, u + off, b))
    m = len(pts)
    F = [tuple(range(m)), tuple(m + k for k in reversed(range(m)))]
    for k in range(m - 1):
        F.append((k, k + 1, m + k + 1, m + k))
    F.append((m - 1, 0, m, 2 * m - 1))
    B.solid(mat, V, F)


def senate_parts(G, M, st=None):
    """The senate, whole or broken as the stage dict st says: a cross of
    blue barrel vaults over marble walls, a drum and dome at the crossing
    with a lantern and gold finial, a portico and round steps in front."""
    st = st or {}
    P = SENATE
    mar, til, gold = M["marble"], M["tiles"], M["gold"]
    W, Rf, D = G["walls"], G["roof"], G["dome"]
    seed = st.get("seed", 0)
    walls = st.get("walls", {})
    t = P["t"]
    cx, cy = P["centre"]

    def wall(key, a, b, h):
        prof = walls.get(key, [(0, h), (1, h)])
        akit.broken_wall(W, mar, mar, a, b, t, prof, step=0.8, jitter=0.15 if key in walls else 0.0,
                         seed=seed + sum(map(ord, key)), notch=0.25 if key in walls else 0.0)

    ax0, ax1, ay0, ay1 = P["arms"]
    nx0, nx1, ny0, ny1 = P["nave"]
    ah, nh = P["arm_wall"], P["nave_wall"]
    # the cross arms' walls: their fronts and backs either side of the nave, their ends
    wall("arm_w_s", (ax0, ay0 + t / 2), (nx0, ay0 + t / 2), ah)
    wall("arm_e_s", (nx1, ay0 + t / 2), (ax1, ay0 + t / 2), ah)
    wall("arm_w_n", (nx0, ay1 - t / 2), (ax0, ay1 - t / 2), ah)
    wall("arm_e_n", (ax1, ay1 - t / 2), (nx1, ay1 - t / 2), ah)
    wall("arm_w_end", (ax0 + t / 2, ay1), (ax0 + t / 2, ay0), ah)
    wall("arm_e_end", (ax1 - t / 2, ay0), (ax1 - t / 2, ay1), ah)
    # the nave (front to back): the portico's front and sides, the back's end
    wall("front", (nx0, ny0 + t / 2), (nx1, ny0 + t / 2), nh)
    wall("nave_w", (nx0 + t / 2, ay0), (nx0 + t / 2, ny0), nh)
    wall("nave_e", (nx1 - t / 2, ny0), (nx1 - t / 2, ay0), nh)
    wall("nave_w_n", (nx0 + t / 2, ny1), (nx0 + t / 2, ay1), nh)
    wall("nave_e_n", (nx1 - t / 2, ay1), (nx1 - t / 2, ny1), nh)
    wall("back", (nx1, ny1 - t / 2), (nx0, ny1 - t / 2), nh)
    # the portico's pilasters and door
    for x in np.linspace(nx0 + 0.5, nx1 - 0.5, 6):
        top = min(nh, akit_profile(walls.get("front"), (x - nx0) / (nx1 - nx0), nh))
        if top > 1.2:
            put(W, mar, pal.prism(x - 0.2, x + 0.2, ny0 - 0.2, ny0 + 0.05, 0.0, top - 0.1))
    if akit_profile(walls.get("front"), 0.5, nh) > 2.4:
        put(W, M["window"], pal.prism(-1.0, 1.0, ny0 - 0.03, ny0 + 0.02, 0.6, 2.6))
    if st.get("cornice", True):
        put(W, mar, pal.prism(nx0 - 0.12, nx1 + 0.12, ny0 - 0.25, ny0 + 0.1, nh - 0.25, nh))
        for x0_, x1_ in ((ax0, nx0), (nx1, ax1)):
            put(W, mar, pal.prism(x0_, x1_, ay0 - 0.2, ay0 + 0.1, ah - 0.22, ah))
    # the vaults: arms east-west, nave north-south, and their end walls
    ar = (ay1 - ay0) / 2
    nr = (nx1 - nx0) / 2
    ncx = (nx0 + nx1) / 2
    vaults = st.get("vaults", {"arm_w": True, "arm_e": True, "nave_s": True, "nave_n": True})
    if vaults.get("arm_w"):
        ell_barrel(Rf, til, ax0, cx, (ay0 + ay1) / 2, ah, ar, P["arm_rise"], 0.28)
        ell_tympanum(W, mar, ax0 - 0.03, (ay0 + ay1) / 2, ah, ar, P["arm_rise"], 0.33)
    if vaults.get("arm_e"):
        ell_barrel(Rf, til, cx, ax1, (ay0 + ay1) / 2, ah, ar, P["arm_rise"], 0.28)
        ell_tympanum(W, mar, ax1 + 0.03, (ay0 + ay1) / 2, ah, ar, P["arm_rise"], -0.33)
    if vaults.get("nave_s"):
        ell_barrel(Rf, til, ny0, cy, ncx, nh, nr, P["nave_rise"], 0.28, axis="y")
        ell_tympanum(W, mar, ny0 - 0.03, ncx, nh, nr, P["nave_rise"], 0.33, axis="y")
    if vaults.get("nave_n"):
        ell_barrel(Rf, til, cy, ny1, ncx, nh, nr, P["nave_rise"], 0.28, axis="y")
        ell_tympanum(W, mar, ny1 + 0.03, ncx, nh, nr, P["nave_rise"], -0.33, axis="y")
    for key, u0, u1, zs in st.get("stubs", ()):
        # a length of a vault fallen onto the walls' broken tops, its end wall with it
        if key == "nave_s":
            ell_barrel(Rf, til, u0, u1, ncx, zs, nr, P["nave_rise"], 0.28, axis="y")
            ell_tympanum(W, mar, u0 - 0.03, ncx, zs, nr, P["nave_rise"], 0.33, axis="y")
    # the round steps in front of the portico, half rings stepping up
    sx, sy, sr = P["steps"]
    steps = st.get("steps", 4)
    for i in range(steps):
        rr = sr - i * 0.42
        shell(W, mar, sx, sy, [(rr, 0.0), (rr, 0.2 * (i + 1))], rr - 0.05, seg=14, a0=180, a1=360)
    # the drum, dome, lantern and finial at the crossing
    dr, dz0, dz1 = P["drum"]
    drum = st.get("drum")
    if drum is None:
        shell(D, mar, cx, cy, [(dr, dz0), (dr, dz1)], 0.35, seg=20)
        shell(D, mar, cx, cy, [(dr + 0.15, 6.6), (dr + 0.15, 6.9)], 0.5, seg=20)
        shell(D, M["window"], cx, cy, [(dr + 0.02, 7.6), (dr + 0.02, 9.0)], 0.1, seg=20)
        shell(D, mar, cx, cy, [(P["eave"], dz1 - 0.25), (P["eave"], dz1 + 0.05)], 0.6, seg=20)
    elif drum:
        broken_drum(D, mar, cx, cy, dr, st.get("drum_z0", dz0), dz1, drum,
                    [(dr + 0.15, 6.6, 6.9, 0.5, mar), (dr + 0.02, 7.6, 9.0, 0.1, M["window"]),
                     (P["eave"], dz1 - 0.25, dz1 + 0.05, 0.6, mar)], seed + 9, jag=st.get("drum_jag", 0.35),
                    t=st.get("drum_t", 0.35))
    if st.get("pilasters") and drum is not False:
        # pilasters between the windows, where the drum still stands over the band
        fn = heights(drum) if drum else (lambda a: dz1)
        for k in range(12):
            a = 15.0 + 30.0 * k
            if fn(a) < 9.3:
                continue
            ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
            D.box(mar, cx + (dr + 0.08) * ca, cy + (dr + 0.08) * sa, 7.5, 0.18, 0.42, 1.65, yaw=a)
    za = dz1 + P["rise"]
    dprof = dome_prof(P["dome_r"], P["rise"], 6, 0.8)
    if st.get("dome_break"):
        broken_shell(D, til, cx, cy, dprof, 0.3, heights(st["dome_break"]), seg=24, z0=dz1, jag=0.3, seed=seed + 4)
    elif st.get("dome", True):
        shell(D, til, cx, cy, dprof, 0.3, seg=20, z0=dz1)
    if st.get("lantern", True):
        put(D, mar, pal.lathe([(0.85, 0), (0.85, 0.9), (0.95, 0.9), (0.95, 1.1), (0.6, 1.1), (0.0, 1.3)], 12, cx, cy,
                              za - 0.2, "lantern"))
    if st.get("finial", True):
        put(D, gold, pal.finial(cx, cy, za + 1.0, 0.5))
        put(D, gold, pal.lathe([(0.05, 0.0), (0.05, 1.5), (0.0, 1.6)], 6, cx, cy, za + 3.2, "cross"))
        put(D, gold, pal.prism(cx - 0.3, cx + 0.3, cy - 0.04, cy + 0.04, za + 4.1, za + 4.2))
    if st.get("dome_frag"):
        a0, a1, z, top = st["dome_frag"]
        broken_shell(D, til, cx, cy, dprof, st.get("frag_t", 0.3), heights(top, wrap=False), seg=24, a0=a0, a1=a1,
                     z0=z, jag=0.3, seed=seed + 5)


def senate_vault(x, y):
    """The top of the senate's vaults in plan, the ground beside them."""
    P = SENATE
    ax0, ax1, ay0, ay1 = P["arms"]
    nx0, nx1, ny0, ny1 = P["nave"]
    z = 0.0
    ar, nr = (ay1 - ay0) / 2, (nx1 - nx0) / 2
    d = (y - (ay0 + ay1) / 2) / ar
    if ax0 <= x <= ax1 and abs(d) < 1:
        z = max(z, P["arm_wall"] + P["arm_rise"] * math.sqrt(1 - d * d))
    d = (x - (nx0 + nx1) / 2) / nr
    if ny0 <= y <= ny1 and abs(d) < 1:
        z = max(z, P["nave_wall"] + P["nave_rise"] * math.sqrt(1 - d * d))
    return z


def senate_env(x, y):
    """The intact senate's roof over (x, y) inside its walls (the cross of
    vaults, the drum and dome), None outside them."""
    P = SENATE
    ax0, ax1, ay0, ay1 = P["arms"]
    nx0, nx1, ny0, ny1 = P["nave"]
    t = P["t"] * 0.6
    zs = []
    if (ax0 + t < x < ax1 - t and ay0 + t < y < ay1 - t) or (nx0 + t < x < nx1 - t and ny0 + t < y < ny1 - t):
        zs.append(senate_vault(x, y))
    cx, cy = P["centre"]
    dr, dz0, dz1 = P["drum"]
    rho = math.hypot(x - cx, y - cy)
    if rho < dr - 0.2:
        zs.append(dz1 + max(0.0, dome_top(rho, P["dome_r"], P["rise"])))
    return max(zs) if zs else None


def senate_inside(co, n):
    return gutted(co, n, senate_env)


def senate_steps(x, y):
    """The top of the round steps in front of the portico, 0 off them."""
    sx, sy, sr = SENATE["steps"]
    rho = math.hypot(x - sx, y - sy)
    if y > sy or rho > sr:
        return 0.0
    return 0.2 * (min(3, int((sr - rho) / 0.42)) + 1)


def senate_front_b(x, y):
    """What a heap on the shell's portico lies on: the front arm's vault
    fallen onto its walls (springing at 2.6 from the portico back to y
    -1.6), and the steps in front."""
    nx0, nx1, ny0, ny1 = SENATE["nave"]
    nr, ncx = (nx1 - nx0) / 2, (nx0 + nx1) / 2
    z = senate_steps(x, y)
    d = (x - ncx) / nr
    if ny0 <= y <= -1.6 and abs(d) < 1:
        z = max(z, 2.6 + SENATE["nave_rise"] * math.sqrt(1 - d * d))
    return z


def in_senate(x, y):
    """Inside the senate's walls: the cross of arms and nave."""
    P = SENATE
    ax0, ax1, ay0, ay1 = P["arms"]
    nx0, nx1, ny0, ny1 = P["nave"]
    t = P["t"]
    return (ax0 + t < x < ax1 - t and ay0 + t < y < ay1 - t) or (nx0 + t < x < nx1 - t and ny0 + t < y < ny1 - t)


def senate_floors():
    """The senate's inside in plan, for its rubble floor."""
    ax0, ax1, ay0, ay1 = SENATE["arms"]
    nx0, nx1, ny0, ny1 = SENATE["nave"]
    return [[(ax0 + 0.2, ay0 + 0.2), (ax1 - 0.2, ay0 + 0.2), (ax1 - 0.2, ay1 - 0.2), (ax0 + 0.2, ay1 - 0.2)],
            [(nx0 + 0.2, ny0 + 0.2), (nx1 - 0.2, ny0 + 0.2), (nx1 - 0.2, ay0 + 0.25), (nx0 + 0.2, ay0 + 0.25)],
            [(nx0 + 0.2, ay1 - 0.25), (nx1 - 0.2, ay1 - 0.25), (nx1 - 0.2, ny1 - 0.2), (nx0 + 0.2, ny1 - 0.2)]]


def on_senate_vault(x, y):
    """On the senate's vaults, clear of the drum."""
    cx, cy = SENATE["centre"]
    return senate_vault(x, y) > 0.5 and math.hypot(x - cx, y - cy) > SENATE["drum"][0] - 0.05


def under_senate_roof(x, y):
    z = senate_vault(x, y)
    return z + 0.3 if z > 0.5 else 99.0


def senate_back_y(x):
    """How far north the senate's back walls stand at x (their inner face),
    or None beyond its ends."""
    ax0, ax1, ay0, ay1 = SENATE["arms"]
    nx0, nx1, ny0, ny1 = SENATE["nave"]
    t = SENATE["t"]
    if nx0 <= x <= nx1:
        return ny1 - t
    if ax0 <= x <= ax1:
        return ay1 - t
    return None


def back_wall_cap(top=1.3):
    """A ceiling holding a heap over the shell's back walls to about their
    broken tops, rising inward from them no steeper than the bed may stand."""
    def zmax(x, y):
        yb = senate_back_y(x)
        return 99.0 if yb is None else top + SLOPE * max(0.0, yb - y)
    return zmax


def clamp_back(P, ymax=6.3):
    """An outline kept within a little of the shell's back walls, however
    far back the picture's skyline reads on the ground."""
    return [(x, min(y, ymax)) for x, y in P]


SENATES = {
    "a": dict(
        base=0.95, pieces=0.9,
        state=dict(
            walls={"arm_w_s": [(0, 3.1), (0.3, 2.2), (0.6, 1.6), (1, 2.6)],
                   "arm_e_s": [(0, 2.6), (0.4, 1.4), (0.8, 1.2), (1, 2.4)],
                   "arm_e_end": [(0, 2.0), (0.5, 2.8), (1, 3.1)]},
            vaults={"arm_w": True, "arm_e": True, "nave_s": True, "nave_n": True},
            finial=False, lantern=False, steps=4,
            # the drum stands round, its front-left broken down low where the
            # dome's heap pours over it, the dome open to a ragged rim
            drum=[(0, 10.6), (185, 10.6), (200, 7.0), (215, 5.8), (230, 5.4), (248, 5.8), (262, 7.0), (275, 8.8),
                  (300, 9.2), (320, 9.6), (340, 10.6)],
            dome_break=[(0, 2.0), (35, 2.9), (65, 2.6), (90, 1.4), (115, 2.8), (150, 3.0), (178, 1.6), (192, -1.0),
                        (335, -1.0), (350, 0.8)],
            # the window band in separate windows between pilasters
            pilasters=True,
        ),
        cuts=[
            # breaches in the vaults where they caved under the heaps
            ("roof", [[(18, 60), (30, 56), (38, 64), (36, 80), (24, 84), (16, 76)],
                      [(8, 118), (40, 112), (66, 118), (66, 142), (40, 148), (8, 142)],
                      [(150, 108), (180, 104), (204, 110), (204, 142), (180, 150), (150, 142)],
                      [(84, 132), (110, 126), (134, 134), (132, 156), (108, 162), (86, 156)]], 3.0),
            # the portico's front wall, pilasters, cornice and gable broken under the heap
            ("walls", [[(86, 150), (130, 148), (138, 172), (136, 200), (86, 202), (80, 176)]], 0.9),
        ],
        heaps=[
            # the drum filled with the fallen dome nearly to its rim, a cell of lumps over its floor
            ("dome", ("circle", 0.0, 1.8, 2.9), 1.0, 0.0),
            # what poured over its broken front-left, lying on the vaults against the drum
            ("spill", ("plan", sector(0.0, 1.8, 3.0, 4.7, 200, 262)), 1.2, 0.0),
            # the arms' vaults fallen in: heaps through their broken walls,
            # rising inside under the holes
            ("west", [(2, 108), (20, 100), (50, 104), (68, 124), (70, 150), (50, 168), (25, 165), (5, 140)], 3.4,
             1.4),
            ("east", [(140, 100), (165, 94), (190, 102), (204, 125), (206, 155), (190, 180), (165, 182),
                      (145, 165), (140, 130)], 3.4, 1.6),
            # the front arm's vault fallen into the portico: a heap through its
            # broken front, rising inside under the hole in the vault
            ("nave", [(84, 132), (104, 126), (128, 130), (136, 144), (140, 166), (144, 186), (140, 204), (122, 212),
                      (100, 214), (80, 206), (72, 186), (76, 160)], 3.4, 2.0),
            ("steps", [(78, 196), (96, 190), (120, 190), (138, 198), (144, 218), (132, 234), (106, 238),
                       (84, 234), (72, 218)], 1.5, None),
        ],
        fills={"dome": (0.0, 1.8, 2.85, 8.9)},
        floors=senate_floors(),
        bases={"steps": senate_steps, "spill": senate_vault},
        on={"spill": on_senate_vault},
        lean={"spill": in_circle(0.0, 1.8, 3.25)},
        lean_in={"nave": in_senate, "west": in_senate, "east": in_senate},
        zmax={"spill": None, "nave": under_senate_roof, "west": under_senate_roof, "east": under_senate_roof},
        cells={"steps": 0.5, "spill": 0.45},
        gappy=("west", "east", "steps", "nave"),
        edge={"steps": 0.8, "spill": 0.5, "west": 1.2, "east": 1.2, "nave": 1.2},
        lumps={"dome": 0.12, "nave": 0.4, "steps": 0.3},
        beds={"dome": "#625b52", "west": "#5a544b", "east": "#5a544b", "nave": "#5a544b", "steps": "#5a544b",
              "spill": "#625b52"},
        more={"steps": 1.3, "east": 1.2, "west": 1.2, "dome": 1.3, "nave": 1.3, "spill": 2.4},
        stones={"spill": 2.0, "steps": 1.4, "west": 1.2, "east": 1.2},
        golden=("dome",),
        scree={"west": 3, "east": 3, "steps": 3, "nave": 3},
        shards={"dome": (8, (0.8, 1.4)), "east": (5, (0.6, 1.2)), "west": (5, (0.6, 1.1)),
                "nave": (5, (0.6, 1.2)), "steps": (2, (0.5, 0.9)), "spill": (3, (0.6, 1.1))},
        beams={"dome": 1, "west": 1, "east": 1, "nave": 1},
        # the finial lying in the dome's heap where the picture draws its gold
        bulbs=[("dome", 98, 47, 40, 62, 0.5)],
        plates=[("east", 178, 128, 1.8, 1.3, 20, 25, -10, 0), ("west", 30, 130, 1.5, 1.1, -20, 20, 15, 1)],
        thin={"t_dark": 0.5, "m_soot": 0.5},
        burns=[(0.0, 1.8, 8.0, 3.5, 0.45), (3.5, 1.8, 3.0, 2.5, 0.5), (0.0, -1.8, 3.5, 2.5, 0.5)],
    ),
}
SENATES["a"]["zmax"]["spill"] = rim_cap(0.0, 1.8, 3.2, SENATES["a"]["state"]["drum"], 0.05)
SENATES["b"] = dict(
    base=0.72, pieces=1.1, gut=0.8,
    state=dict(
        walls={"arm_w_s": [(0, 1.2), (0.5, 0.8), (1, 1.4)], "arm_e_s": [(0, 1.3), (0.5, 0.7), (1, 1.0)],
               "arm_w_n": [(0, 1.4), (1, 1.0)], "arm_e_n": [(0, 1.0), (1, 1.4)],
               "arm_w_end": [(0, 1.0), (1, 1.5)], "arm_e_end": [(0, 1.2), (1, 1.6)],
               "front": [(0, 1.4), (0.15, 2.6), (0.85, 2.6), (1, 1.2)],
               "nave_w": [(0, 1.8), (0.5, 2.0), (1, 2.6)], "nave_e": [(0, 2.6), (0.5, 2.0), (1, 1.4)],
               "nave_w_n": [(0, 1.2), (1, 1.6)], "nave_e_n": [(0, 1.6), (1, 1.2)], "back": [(0, 1.4), (1, 1.2)]},
        vaults={}, finial=False, lantern=False, dome=False, cornice=False, steps=4,
        # the front arm's vault fallen onto its walls, its arch with it
        stubs=[("nave_s", -3.6, -1.6, 2.6)],
        # the drum broken into fragments of a ragged ring, thick as its wall
        # and its bands, standing as high as the picture draws them, broken
        # low at the back-left and down to the heap at the front
        drum=[(0, 5.0), (20, 5.8), (38, 4.9), (55, 6.0), (72, 6.3), (88, 5.0), (100, 3.0), (118, 2.4), (136, 3.0),
              (150, 5.2), (164, 6.4), (182, 5.4), (200, 5.4), (225, 3.4), (236, 1.8), (242, 0.45), (298, 0.45),
              (305, 2.0), (320, 3.4), (345, 4.6)],
        drum_z0=0.0, drum_t=0.6, drum_jag=0.5, frag_t=0.5,
        # and a ragged piece of the dome's shell on it at the north-east
        dome_frag=(15, 85, 5.2, [(15, 0.6), (35, 1.8), (55, 2.4), (72, 1.6), (85, 0.5)]),
    ),
    cuts=[
        # the vault's broken end, along the heap
        ("roof", [[(30, 96), (180, 96), (180, 124), (150, 130), (122, 124), (96, 132), (66, 126), (30, 130)]], 1.5),
        # the portico's front broken under the heap
        ("walls", [[(84, 138), (128, 136), (136, 160), (134, 184), (84, 186), (78, 162)]], 0.9),
    ],
    heaps=[
        # the ring's fill, a cell of lumps over its floor, pouring out through its breaches
        ("dome", ("circle", 0.0, 1.8, 2.9), 1.0, 0.0),
        # the field's back read on the back walls' broken tops, held in front of them
        ("field", [(2, 60, 1.4), (30, 40, 1.4), (60, 10, 1.4), (100, 2, 1.4), (140, 10, 1.4), (180, 40, 1.4),
                   (210, 70, 1.4), (212, 120), (200, 150), (170, 160), (150, 156), (60, 156), (30, 150), (5, 120)],
         1.6, 1.2),
        # on the front arm's vault, fallen onto its walls
        ("portico", [(80, 134), (102, 128), (126, 132), (138, 146), (142, 168), (140, 190), (124, 204),
                     (100, 206), (82, 200), (72, 182), (74, 156)], 0.8, None),
        ("steps", [(66, 172), (90, 164), (120, 164), (144, 172), (150, 196), (136, 216), (106, 220), (76, 216),
                   (62, 196)], 1.4, None),
    ],
    fills={"dome": (0.0, 1.8, 2.85, 3.6)},
    # the fill stands only to the ring's broken top, and pours out at the breaches to meet the field
    fill_rims={"dome": [(0, 5.0), (20, 5.8), (38, 4.9), (55, 6.0), (72, 6.3), (88, 5.0), (100, 3.0), (118, 2.4),
                        (136, 3.0), (150, 5.2), (164, 6.4), (182, 5.4), (200, 5.4), (225, 3.4), (236, 1.8),
                        (242, 1.6), (298, 1.6), (305, 2.0), (320, 3.4), (345, 4.6)]},
    floors=senate_floors(),
    clamp={"field": clamp_back},
    bases={"portico": senate_front_b, "steps": senate_steps},
    on={"portico": lambda x, y: SENATE["nave"][0] < x < SENATE["nave"][1] and SENATE["nave"][2] <= y <= -1.6},
    zmax={"field": back_wall_cap()},
    cells={"portico": 0.5, "steps": 0.5, "field": 0.65},
    gappy=("field", "steps"),
    edge={"steps": 0.8, "portico": 0.6},
    lumps={"dome": 0.2, "portico": 0.4},
    more={"steps": 1.3, "field": 1.0, "dome": 1.5, "portico": 1.3},
    stones={"field": 1.6, "steps": 1.4, "dome": 1.2},
    golden=("dome",),
    scree={"field": 6, "steps": 3, "portico": 3},
    shards={"dome": (8, (0.8, 1.4)), "field": (16, (0.6, 1.2)), "steps": (2, (0.5, 0.9))},
    beams={"dome": 3, "field": 6},
    # the finial lying in the heap inside the ring, where the picture draws its gold
    bulbs=[("dome", 101, 33, 120, 88, 0.5)],
    # big pieces of the arms' vaults on the heaps
    plates=[("field", 172, 100, 2.4, 1.6, 15, 28, -12, "tiles"), ("field", 182, 124, 2.0, 1.5, -25, -20, 18, "tiles"),
            ("field", 40, 124, 1.8, 1.3, 30, 25, 10, "tiles"),
            # lengths of the arms' vaults propped against their back walls, where
            # the picture draws blue roof standing behind the ring
            ("field", 34, 62, 2.6, 3.2, 8, 66, 0, "tiles", True), ("field", 176, 70, 2.4, 3.0, -10, 64, 0, "tiles", True)],
    thin={"t_dark": 0.5, "m_soot": 0.6},
    burns=[(0.0, 1.8, 6.0, 4.0, 0.45), (3.5, 1.8, 2.0, 3.0, 0.6), (-3.5, 1.8, 2.0, 3.0, 0.6),
           (0.0, -1.4, 3.0, 2.5, 0.6)],
)


def senate_ruin(spr, spec):
    return stage_ruin(spr, SENATES[spr.name[-1]], senate_parts, stone_mats, BLUE, "senate", SENATE_TONES,
                      senate_inside)


# ---------------------------------------------------------------- running

BUILDERS = {"CreBuild01a": palace_ruin, "CreBuild01b": palace_ruin,
            "CreBuild02": intact(hall_parts, stone_mats, GREEN, "hall", HALL_TONES),
            "CreBuild02a": hall_ruin, "CreBuild02b": hall_ruin,
            "CreBuild03": intact(senate_parts, stone_mats, BLUE, "senate", SENATE_TONES),
            "CreBuild03a": senate_ruin, "CreBuild03b": senate_ruin}


def build(spr, spec):
    """One stage for hand/creon/build.py: (parts, info)."""
    return BUILDERS[spr.name](spr, spec)


def glb_box(path):
    """The extent (lo, hi) of what a glb file holds, in Blender's axes (x,
    y, z up), from its meshes' position bounds: what ships, without the loose
    vertices a Blender object can still carry."""
    import struct
    with open(path, "rb") as f:
        data = f.read()
    n = struct.unpack_from("<I", data, 12)[0]
    j = json.loads(data[20:20 + n])
    lo, hi = [1e9] * 3, [-1e9] * 3
    for node in j.get("nodes", []):
        if "mesh" not in node:
            continue
        t = node.get("translation", [0, 0, 0])
        for prim in j["meshes"][node["mesh"]]["primitives"]:
            a = j["accessors"][prim["attributes"]["POSITION"]]
            for k in range(3):
                lo[k] = min(lo[k], a["min"][k] + t[k])
                hi[k] = max(hi[k], a["max"][k] + t[k])
    # glTF is y up with -z forward of Blender's y
    return (lo[0], -hi[2], lo[1]), (hi[0], -lo[2], hi[1])


def make(name, out, render=True):
    t0 = time.time()
    hk.reset()
    spr = kit.Sprite(name)
    parts, info = build(spr, {})
    if os.environ.get("G1_TRIS"):
        for o in sorted(parts, key=lambda o: -len(o.data.polygons)):
            print("G1_PART", o.name, sum(len(p.vertices) - 2 for p in o.data.polygons), flush=True)
    glb = os.path.join(out, name + ".glb")
    ob = hk.finish(parts, glb, {"feature": name, "family": "creon"})
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    lo, hi = glb_box(glb)
    print("G1_BUILT", name, "tris", tris, "size %.2f x %.2f x %.2f" % tuple(b - a for a, b in zip(lo, hi)),
          "zmin %.2f zmax %.2f" % (lo[2], hi[2]), info, "%.1fs" % (time.time() - t0), flush=True)
    if os.environ.get("G1_CLOSE"):
        close(ob, os.path.join(out, "renders", name + "_close.png"), float(os.environ["G1_CLOSE"]))
    if os.environ.get("G1_FRAME"):
        # the model seen in another stage's frame, for tracing what fell
        fr = kit.Sprite(os.environ["G1_FRAME"])
        hk.renders(ob, os.path.join(out, "renders"), name + "_in_" + fr.name, fr.path, (fr.hx, fr.hy), scale=2)
    if render:
        rd = os.path.join(out, "renders")
        if os.environ.get("G1_PROJ"):
            proj(ob, os.path.join(rd, name + "_proj.png"), spr)
        hk.renders(ob, rd, name, spr.path, (spr.hx, spr.hy), scale=2)
        m = kit.fit(spr, os.path.join(rd, name + "_classic.png"))
        if m:
            print("G1_FIT", name, "iou %.3f cover %.3f spill %.3f" % (m["iou"], m["cover"], m["spill"]),
                  "render", np.round(m["render"], 3), "drawing", np.round(spr.mean(), 3), flush=True)
    return tris


def proj(ob, path, spr, scale=2):
    """The model drawn the way its picture is, a cell 16 pixels across and
    8 up (an oblique view, which the classic camera only approximates):
    sheared so a camera straight down sees it so, the anchor on the hotspot."""
    scene, cam = hk._stage(32)
    w, h = spr.w, spr.h
    S = Matrix(((1, 0, 0, 0), (0, 1, 0.5, 0), (0, -0.2, 0.4, 0), (0, 0, 0, 1)))
    ob.data.transform(S)  # an object's own transform cannot hold a shear
    P = 16.0 * scale
    W, Hh = w * scale, h * scale
    scene.render.resolution_x, scene.render.resolution_y = int(W), int(Hh)
    cam.data.ortho_scale = max(W, Hh) / P
    cx = (w / 2.0 - spr.hx) / 16.0
    cy = (spr.hy - h / 2.0) / 16.0
    cam.location = (cx, cy, 60.0)
    cam.rotation_euler = (0.0, 0.0, 0.0)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    ob.data.transform(S.inverted())


def close(ob, path, az_deg=-38.0, el_deg=28.0, res=1100):
    """A bigger low three-quarter view for judging the model in 3D."""
    scene, cam = hk._stage(48)
    az, el = math.radians(az_deg), math.radians(el_deg)
    d = Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)))
    right = Vector((0, 0, 1)).cross(d).normalized()
    up = d.cross(right).normalized()
    pts = [ob.matrix_world @ v.co for v in ob.data.vertices]
    us = [p.dot(right) for p in pts]
    vs = [p.dot(up) for p in pts]
    c = right * ((min(us) + max(us)) / 2) + up * ((min(vs) + max(vs)) / 2)
    aspect = 0.62
    scene.render.resolution_x, scene.render.resolution_y = res, int(res * aspect)
    cam.data.ortho_scale = max(max(us) - min(us), (max(vs) - min(vs)) / aspect) * 1.05
    cam.location = c + d * 80
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = os.environ.get("OK_STAGE_OUT", kit.ROOT + "/hand/creon_stages")
    if "--out" in argv:
        out = argv[argv.index("--out") + 1]
        argv = [a for a in argv if a not in ("--out", out)]
    render = "--norender" not in argv
    names = [a for a in argv if not a.startswith("--")] or list(NAMES)
    for n in names:
        try:
            make(n, out, render)
        except Exception as e:
            traceback.print_exc()
            print("G1_FAILED", n, repr(e), flush=True)


if __name__ == "__main__":
    main()
