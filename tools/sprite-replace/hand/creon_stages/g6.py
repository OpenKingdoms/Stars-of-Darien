"""Creon's smaller pieces destroyed (stage group 6), run inside Blender:
the four ruined inventions, the fallen fences, the dead plants, the burnt
cart and shed, and the ruined well.

    OK_REPLACE=<folder with sprites/ and catalog.json> \
    blender -b --factory-startup --python g6.py -- [Name ...] [--norender]

Each model goes to $OK_STAGES/<Name>.glb (OK_STAGES is
D:/OKBuild/creon-stages/g6 unless set), its classic and turned renders to
$OK_STAGES/renders. Then

    python ../../handcompare.py <renders> <Name> <sprites>/<Name>.png <scale>

puts the stage's picture and the render side by side.

Same frame as handkit: one unit is one map cell, -Y toward the classic
camera, Z up, the origin at the stage's anchor. The classic view draws
(x, y, z) at column hx + 16 x and row hy - 16 y - 8 z, so a plan circle
stays a circle and a height lifts a point half its size up the picture.
Pieces are placed from points traced on the stage's own picture.

Geometry is gathered per material with the Aramon kit's Builder and its
ruin tools (heaps, beds, broken walls, debris), loaded under its own
name. Every material is a texture made here from noise and the drawn
colours, tinted by vertex colour: soot darkens a part where it burnt
without a material of its own, the pieces of one material (stones,
gravel, charred boards) each take one of its drawn shades, and ribs and
liquids take their colour from it. Ash, grit and gravel beds are cut by
a speckled alpha so the ground shows between the specks as drawn.
No picture is painted from the original: Creon's palette is not one the
engine paints with, so a Creon recipe would come out in the wrong colours.
"""
import importlib.util
import json
import math
import os
import random
import sys
import time
import traceback
import zlib
from contextlib import contextmanager

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
for p in (os.path.join(TOOLS, "hand", "creon"), TOOLS):
    if p not in sys.path:
        sys.path.insert(0, p)

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402

import handkit as hk  # noqa: E402
import kit as ck  # noqa: E402
import models as cmodels  # noqa: E402
import well as cwell  # noqa: E402

DATA = os.environ.get("OK_REPLACE", "D:/OKBuild/creon-stages/g6/okr")
OUT = os.environ.get("OK_STAGES", "D:/OKBuild/creon-stages/g6")


def _aramon_kit():
    """The Aramon buildings kit, under its own name since both kits are
    kit.py, pointed at this family's pictures."""
    spec = importlib.util.spec_from_file_location("akit", os.path.join(TOOLS, "hand", "aramon_buildings", "kit.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    mod.DATA = DATA
    mod.OUT = OUT
    mod.CAT.update({r["name"]: r for r in json.load(open(DATA + "/catalog.json"))})
    return mod


A = _aramon_kit()


# ---------------------------------------------------------------- materials

class VMat:
    """A Builder material whose texture the vertex colour multiplies.
    tex carries the drawn colour (Aramon kit textures, made to come out
    near it), and an untextured one takes colour as its tint. soot is how far
    the model's soot reaches it, 0 for none."""

    def __init__(self, name, tex=None, uv=1.0, colour=None, rough=0.9, soot=1.0, ref=None, cut=False, vary=None,
                 made=None):
        self.name, self.uv, self.uvfn = name, uv, None
        self.ref = A.rgb(ref) if ref is not None else (A.rgb(colour) if colour is not None else None)
        self.base = np.array(A.albedo(colour)) if (colour is not None and tex is None) else np.ones(3)
        # vary: [(drawn colour, weight)], one picked for each separate piece
        # and carried by its vertex colour against the colour made (the
        # texture's own) or the untextured colour
        self.vary = None
        if vary:
            ref_c = made if made is not None else colour
            self.vary = [(tint_of(ref_c, c), w) for c, w in vary]
        self.m = ck.material(name, tex, rough=rough)
        self.m.use_backface_culling = not cut
        if cut:
            # the texture's alpha as a hard mask (glTF MASK), seen from both sides
            nt = self.m.node_tree
            t = [n for n in nt.nodes if n.type == "TEX_IMAGE"][0]
            gt = nt.nodes.new("ShaderNodeMath")
            gt.operation = "GREATER_THAN"
            gt.inputs[1].default_value = 0.5
            nt.links.new(t.outputs["Alpha"], gt.inputs[0])
            nt.links.new(gt.outputs[0], nt.nodes["Principled BSDF"].inputs["Alpha"])
        self.textured = tex is not None
        self.soot = soot


def tint_of(made, col):
    """The vertex colour that turns a surface made at drawn colour made
    into drawn colour col (both as drawn, the ratio of their albedos)."""
    a, b = np.array(A.albedo(made)), np.array(A.albedo(col))
    return tuple(float(v) for v in np.clip(b / np.maximum(a, 1e-4), 0.0, 1.0))


def tint_layer(bm):
    return bm.verts.layers.float_vector.get("tint") or bm.verts.layers.float_vector.new("tint")


@contextmanager
def tinted(B, mat, col):
    """Everything built inside in mat takes the vertex colour col (a
    (r, g, b) multiplier, or fn(co) giving one)."""
    bm = B._bm(mat)
    n0 = len(bm.verts)
    yield
    lay = tint_layer(bm)
    bm.verts.ensure_lookup_table()
    for v in list(bm.verts)[n0:]:
        v[lay] = Vector(col(v.co) if callable(col) else col)


def vary_islands(B, seed):
    """Each separate piece of a varied material takes one of its colours,
    by weight, a little jittered, unless it was tinted already."""
    for key, bm in B.bms.items():
        vm = B.mats[key]
        if not getattr(vm, "vary", None):
            continue
        rng = random.Random(seed + zlib.crc32(key.encode()))
        lay = tint_layer(bm)
        bm.verts.index_update()
        seen = set()
        tints, wts = zip(*vm.vary)
        for v in bm.verts:
            if v.index in seen:
                continue
            comp, stack = [], [v]
            seen.add(v.index)
            while stack:
                u = stack.pop()
                comp.append(u)
                for e in u.link_edges:
                    w = e.other_vert(u)
                    if w.index not in seen:
                        seen.add(w.index)
                        stack.append(w)
            t = rng.choices(tints, wts)[0]
            j = rng.uniform(0.92, 1.06)
            for u in comp:
                if u[lay].length == 0.0:
                    u[lay] = Vector(t) * j


def swatch(name, cols, kind="mottle", seed=0, uv=0.8, soot=1.0, sat=1.15, rough=0.9):
    """One VMat per drawn colour, with a little grain of its kind."""
    out = []
    for i, c0 in enumerate(cols):
        c = A.saturate(c0, sat)
        nm = "%s%d" % (name, i)
        if kind == "planks":
            t = A.tex_planks(nm, c, boards=3, var=0.12, seed=seed + i)
        elif kind == "char":
            t = tex_char(nm, seed + i, base=c)
        elif kind == "grit":
            t = A.tex_grit(nm, c, uv=uv, seed=seed + i)
        elif kind == "stone":
            t = A.tex_stone(nm, c, rows=3, seed=seed + i)
        else:
            t = A.tex_mottle(nm, c, var=0.16, seed=seed + i)
        out.append(VMat(nm, t, uv=uv, soot=soot, ref=c0, rough=rough))
    return out


def tex_char(name, seed, base="#2e2520", hi="#4d423b", crack="#120e0c", size=128, boards=3):
    """Charred planks: boards of burnt wood, grain along them, the scales
    of charcoal (alligator cracks, long along the grain and short across
    it) with their crowns a little lit and warm in places, so they read as
    burnt wood and not as black."""
    rng = np.random.default_rng(seed)
    base, hi, crack = (np.array(A.rgb(c), float) for c in (base, hi, crack))
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    bw = size / boards
    i = np.floor(vv / bw).astype(int)
    fa = vv / bw - i
    sh = 1 + rng.normal(0, 0.1, boards)
    grain = A._smooth(rng, size, 32)
    streak = np.sin(uu / size * 2 * np.pi * 9 + grain * 5 + fa * 9)
    col = base[None, None] * (sh[i] * (0.9 + 0.1 * streak))[..., None]
    # scales: rows along the grain, each row cut across at staggered places
    rows = 6 * boards
    wob = A._smooth(rng, size, 11)
    rv = vv / size * rows + 0.6 * grain + 0.4 * wob
    ri = np.floor(rv).astype(int)
    fv = rv - ri
    cw = 4.0 + 8.0 * (np.abs(np.sin(ri * 12.9898 + seed)) % 1.0)
    cuu = uu / size * 128 / cw + np.abs(np.sin(ri * 78.233 + seed * 0.1)) * 7 + 0.8 * wob
    ci = np.floor(cuu).astype(int)
    cu = cuu - ci
    crown = np.clip(1 - np.abs(fv - 0.5) * 2, 0, 1) * np.clip(1 - np.abs(cu - 0.5) * 2, 0, 1)
    cell = np.abs(np.sin(ci * 37.719 + ri * 11.13 + seed)) % 1.0
    warm = np.clip((A._smooth(rng, size, 6) - 0.4) * 2.5, 0, 1)
    t = (crown ** 0.8 * (0.15 + 0.6 * warm) * (0.4 + 0.6 * cell))[..., None]
    col = col * (0.8 + 0.35 * cell)[..., None]
    col = col * (1 - t) + hi[None, None] * t
    cracks = ((fv < 0.08) | (cu < 0.05)) & (A._smooth(rng, size, 13) > 0.25)
    col[cracks] = crack
    col[fa < 0.05] = crack
    return A._to_image(name, np.clip(col, 0, 255))


def tex_boards(name, base, boards=7, seed=0, size=128, gap=None, var=0.14, joints=1.0):
    """Weathered boards laid level: each its own shade, grain along it,
    a butt joint in the share joints of them, dark gaps between."""
    rng = np.random.default_rng(seed)
    base = np.array(A.rgb(base), float)
    gap = np.array(A.rgb(gap), float) if gap is not None else base * 0.35
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    bw = size / boards
    i = np.floor(vv / bw).astype(int)
    fa = vv / bw - i
    sh = 1 + rng.normal(0, var, boards)
    grain = A._smooth(rng, size, 20)
    streak = np.sin(uu / size * 2 * np.pi * rng.integers(2, 5, boards)[i] + grain * 5 + fa * 4)
    col = base[None, None] * (sh[i] * (0.9 + 0.1 * streak) * (1 + 0.08 * (rng.random((size, size)) - 0.5)))[..., None]
    # a little weathered paler toward each board's upper edge
    col *= (0.92 + 0.12 * fa)[..., None]
    joint = np.where(rng.random(boards) < joints, rng.integers(0, size, boards), -10)[i]
    col[(fa < 0.1) | (np.abs(uu - joint) < 1.2)] = gap
    return A._to_image(name, np.clip(col, 0, 255))


def tex_staves(name, base, staves=8, seed=0, size=128, gap=None, var=0.22, joints=0.15):
    """Upright staves: each its own shade, rounded darker to its edges,
    grain running up it, dark gaps between, and only now and then a
    joint across one, so a wall does not read as coursed blocks."""
    rng = np.random.default_rng(seed)
    base = np.array(A.rgb(base), float)
    gap = np.array(A.rgb(gap), float) if gap is not None else base * 0.35
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    bw = size / staves
    i = np.floor(uu / bw).astype(int)
    fa = uu / bw - i
    sh = 1 + rng.normal(0, var, staves)
    grain = A._smooth(rng, size, 20)
    streak = np.sin(vv / size * 2 * np.pi * rng.integers(1, 4, staves)[i] + grain * 5 + fa * 4)
    col = base[None, None] * (sh[i] * (0.9 + 0.1 * streak) * (1 + 0.08 * (rng.random((size, size)) - 0.5)))[..., None]
    col *= (0.84 + 0.16 * np.sin(np.pi * fa))[..., None]
    joint = np.where(rng.random(staves) < joints, rng.integers(0, size, staves), -10)[i]
    col[(fa < 0.08) | (np.abs(vv - joint) < 1.2)] = gap
    return A._to_image(name, np.clip(col, 0, 255))


def _albedo_srgb(c):
    """A drawn colour as the sRGB texel that renders near it (Aramon kit albedo)."""
    return np.array([A._srgb(v) for v in A.albedo(c)])


def tex_litter(name, cols, seed, size=128, n=70, twigs=18, gain=1.3):
    """Dead leaves and twig ends scattered with gaps between them, for
    cards cut by their alpha: curled grey blobs in the drawn shades, each
    darker at its rim, and thin dark twigs."""
    rng = np.random.default_rng(seed)
    rgba = np.zeros((size, size, 4))
    yy, xx = np.mgrid[0:size, 0:size].astype(float)
    shades = [_albedo_srgb(c) for c in cols]
    for _ in range(n):
        cx, cy = rng.uniform(0, size), rng.uniform(0, size)
        a, b = rng.uniform(3.5, 8.0), rng.uniform(2.0, 4.5)
        th = rng.uniform(0, math.pi)
        dx = (xx - cx + size / 2) % size - size / 2
        dy = (yy - cy + size / 2) % size - size / 2
        u = (dx * math.cos(th) + dy * math.sin(th)) / a
        v = (-dx * math.sin(th) + dy * math.cos(th)) / b
        d = u * u + v * v
        m = d < 1.0
        c = np.clip(shades[rng.integers(0, len(shades))] * rng.uniform(0.9, 1.12) * gain, 0, 1)
        rim = np.clip((d[m] - 0.55) / 0.45, 0, 1)[:, None]
        rgba[m, :3] = c * (1 - 0.3 * rim)
        rgba[m, 3] = 1.0
    dark = _albedo_srgb("#1c1c1a")
    for _ in range(twigs):
        x0, y0 = rng.uniform(0, size), rng.uniform(0, size)
        ang, L = rng.uniform(0, 2 * math.pi), rng.uniform(10, 26)
        for t in np.linspace(0, 1, int(L * 2)):
            x = int(x0 + math.cos(ang) * L * t) % size
            y = int(y0 + math.sin(ang) * L * t) % size
            rgba[y, x, :3] = dark
            rgba[y, x, 3] = 1.0
            rgba[y, (x + 1) % size, :3] = dark
            rgba[y, (x + 1) % size, 3] = 1.0
    return ck.image(name, rgba)


def tex_liquid(name, base, fleck=None, fleck_amt=0.0, streak=None, streak_amt=0.0, seed=0, size=128, var=0.18,
               fleck_px=1):
    """A pool's skin: soft swirls of its colour, with flecks of scum
    fleck_px texels across and optionally streaks of a second colour
    through it."""
    rng = np.random.default_rng(seed)
    base = np.array(A.rgb(base), float)
    n = A._smooth(rng, size, 5) * 0.5 + A._smooth(rng, size, 13) * 0.35 + A._smooth(rng, size, 31) * 0.15
    col = base[None, None] * (1 + (n - 0.5) * 2 * var)[..., None]
    if streak is not None:
        w = A._smooth(rng, size, 7)
        s = np.abs(((w * 6) % 1.0) - 0.5) < 0.05 * streak_amt
        s &= A._smooth(rng, size, 5) > 0.6
        col[s] = np.array(A.rgb(streak), float) * (0.8 + 0.4 * rng.random((int(s.sum()), 1)))
    if fleck is not None:
        k = max(1, int(fleck_px))
        f = rng.random((size // k, size // k)) < fleck_amt
        f = np.kron(f, np.ones((k, k), bool))
        f = np.pad(f, ((0, size - f.shape[0]), (0, size - f.shape[1])))
        col[f] = np.array(A.rgb(fleck), float) * (0.8 + 0.4 * rng.random((int(f.sum()), 1)))
    return A._to_image(name, np.clip(col, 0, 255))


def tex_speck(name, cols, cover=0.75, grain=0.075, uv=2.0, size=256, gap=0.1, seed=0):
    """Ash, grit or gravel as the pictures draw it, specks with ground
    between, for surfaces cut by their alpha: grains about grain cells
    across (Voronoi cells, each its own colour from cols, [(drawn colour,
    weight)], darker toward its rim), a hairline of ground between them,
    and whole grains dropped where a slow noise thins the bed, so about
    cover of it is solid. One tile spans uv cells and repeats seamlessly."""
    rng = np.random.default_rng(seed)
    g = max(4, int(round(uv / grain)))
    cs = size / g
    pts = rng.random((g, g, 2))
    yy, xx = np.mgrid[0:size, 0:size].astype(float) + 0.5
    gx, gy = xx / cs, yy / cs
    ix, iy = np.floor(gx).astype(int), np.floor(gy).astype(int)
    d1 = np.full((size, size), 9.0)
    d2 = np.full((size, size), 9.0)
    cid = np.zeros((size, size), int)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            jx, jy = (ix + dx) % g, (iy + dy) % g
            d = np.hypot(gx - (ix + dx + pts[jy, jx, 0]), gy - (iy + dy + pts[jy, jx, 1]))
            closer = d < d1
            d2 = np.where(closer, d1, np.minimum(d2, d))
            cid = np.where(closer, jy * g + jx, cid)
            d1 = np.where(closer, d, d1)
    slow = A._smooth(rng, g, max(2, g // 5)).ravel()
    slow = (slow - slow.min()) / max(np.ptp(slow), 1e-6)
    keep = rng.random(g * g) < np.clip(cover * (0.45 + 1.1 * slow) / (1 - gap * 0.8), 0, 1)
    shades = np.array([_albedo_srgb(c) for c, _ in cols])
    w = np.array([w for _, w in cols], float)
    pick = rng.choice(len(cols), g * g, p=w / w.sum())
    lit = rng.uniform(0.88, 1.12, g * g)
    rgba = np.zeros((size, size, 4))
    rgba[..., :3] = shades[pick[cid]] * (lit[cid] * (1.0 - 0.35 * np.clip(d1, 0, 1)))[..., None]
    rgba[..., 3] = (keep[cid] & (d2 - d1 > gap)).astype(float)
    rgba[..., :3] = np.clip(rgba[..., :3], 0, 1)
    return ck.image(name, rgba)


def tex_clumps(name, cols, cover=0.82, seed=0, size=256, blobs=(6, 15, 37)):
    """Moss or a growth in clumps for a surface cut by its alpha: its
    drawn shades [(colour, weight)] mottled in soft patches with a fine
    grain, and irregular holes where a noise runs low, so cover of it
    stands and the dark under it shows between."""
    rng = np.random.default_rng(seed)
    shades = np.array([_albedo_srgb(c) for c, _ in cols])
    w = np.cumsum([w for _, w in cols], dtype=float)
    w /= w[-1]
    n = A._smooth(rng, size, blobs[1]) * 0.6 + A._smooth(rng, size, blobs[2]) * 0.4
    n = (n - n.min()) / max(np.ptp(n), 1e-6)
    pick = np.searchsorted(w, np.clip(n, 0, 0.9999))
    grain = 1.0 + 0.16 * (rng.random((size, size)) - 0.5)
    m = A._smooth(rng, size, blobs[0]) * 0.5 + A._smooth(rng, size, blobs[1]) * 0.3 + A._smooth(rng, size, blobs[2]) * 0.2
    cut = np.quantile(m, 1.0 - cover)
    edge = np.clip((m - cut) / 0.04, 0, 1)
    rgba = np.zeros((size, size, 4))
    rgba[..., :3] = np.clip(shades[pick] * grain[..., None] * (0.7 + 0.3 * edge)[..., None], 0, 1)
    rgba[..., 3] = (m > cut).astype(float)
    return ck.image(name, rgba)


def speck_mat(name, cols, cover=0.75, grain=0.075, uv=2.0, seed=0, soot=0.0, rough=0.95):
    return VMat(name, tex_speck(name, cols, cover=cover, grain=grain, uv=uv, seed=seed), uv=uv, soot=soot, rough=rough,
                cut=True)


# ---------------------------------------------------------------- shapes

def smooth_path(pts, radii, k):
    """A polyline rounded through its points (Catmull-Rom), k steps a span."""
    P = [Vector(p) for p in pts]
    if k <= 1 or len(P) < 3:
        return P, radii
    out, rr = [], []
    for i in range(len(P) - 1):
        p0, p1, p2, p3 = P[max(i - 1, 0)], P[i], P[i + 1], P[min(i + 2, len(P) - 1)]
        for j in range(k):
            t = j / k
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
            rr.append(radii[i] + (radii[i + 1] - radii[i]) * t)
    out.append(P[-1])
    rr.append(radii[-1])
    return out, rr


def tube(B, mat, pts, radii, seg=8, caps=True, smooth=1, soft=False, ribs=None):
    """A round tube along a polyline, radii one per point or one for all.
    smooth rounds the path through its points in that many steps a span,
    soft shades it round, and ribs (lit, dark) shades alternate rings by
    vertex colour so it reads ribbed."""
    P = [Vector(p) for p in pts]
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * len(P)
    P, radii = smooth_path(P, list(radii), smooth)
    if soft or ribs:
        bm = B._bm(mat)
        n0 = len(bm.verts)
        fs = _tube(B, mat, P, radii, seg, caps)
        if soft:
            for f in (fs[:-2] if caps else fs):
                f.smooth = True
        if ribs:
            lay = tint_layer(bm)
            bm.verts.ensure_lookup_table()
            for i in range(len(P)):
                for k in range(seg):
                    bm.verts[n0 + i * seg + k][lay] = Vector(ribs[i % 2])
        return fs
    return _tube(B, mat, P, radii, seg, caps)


def collars(B, mat, pts, rad, every=0.4, w=0.1, seg=8, smooth=1, start=None):
    """Rings round a pipe along the same rounded path as tube, every
    cells apart, rad round and w long, so it reads banded."""
    P, _ = smooth_path([Vector(p) for p in pts], [0.0] * len(pts), smooth)
    run = every / 2 if start is None else start
    for i in range(len(P) - 1):
        a, b = P[i], P[i + 1]
        L = (b - a).length
        if L < 1e-6:
            continue
        t = (b - a) / L
        s = run
        while s < L:
            c = a + t * s
            _tube(B, mat, [c - t * (w / 2), c + t * (w / 2)], [rad, rad], seg, True)
            s += every
        run = s - L


def _tube(B, mat, P, radii, seg, caps):
    V, F = [], []
    n = None
    for i, p in enumerate(P):
        t = (P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized()
        if n is None:
            n = t.cross(Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))).normalized()
        else:
            n = (n - t * n.dot(t)).normalized()
        b = t.cross(n)
        for k in range(seg):
            a = 2 * math.pi * k / seg
            V.append(tuple(p + (n * math.cos(a) + b * math.sin(a)) * radii[i]))
    for i in range(len(P) - 1):
        for k in range(seg):
            k2 = (k + 1) % seg
            F.append((i * seg + k, i * seg + k2, (i + 1) * seg + k2, (i + 1) * seg + k))
    if caps:
        F.append(tuple(range(seg)))
        F.append(tuple(range((len(P) - 1) * seg, len(P) * seg)))
    return B.solid(mat, V, F)


def shard(B, mat, c, nrm, w, h, t, rng, k=5):
    """A broken flat piece (a plate, a stave panel, a dome shard) lying on
    normal nrm round point c, its outline ragged."""
    c, nrm = Vector(c), Vector(nrm).normalized()
    t1 = nrm.cross(Vector((0, 0, 1)) if abs(nrm.z) < 0.95 else Vector((1, 0, 0))).normalized()
    a0 = rng.uniform(0, 2 * math.pi)
    t1 = t1 * math.cos(a0) + nrm.cross(t1) * math.sin(a0)
    t2 = nrm.cross(t1)
    pts = []
    for i in range(k):
        an = 2 * math.pi * (i + rng.uniform(-0.25, 0.25)) / k
        rr = rng.uniform(0.75, 1.15) * 0.5
        pts.append(tuple(c + t1 * (math.cos(an) * w * rr) + t2 * (math.sin(an) * h * rr)))
    B.slab(mat, pts, t)


def noise_soot(seed, amt=0.5, scale=1.1, floor=0.0, zfade=None):
    """Soot as fbm blotches, a factor 1 - amt where it is thickest. With
    zfade (z0, z1, k) it also thickens toward z1 by k."""
    off = Vector((seed % 97 * 1.37, seed % 89 * 2.11, seed % 83 * 0.73))

    def f(co, n=None):
        v = noise.fractal(co * scale + off, 1.0, 2.0, 3)
        s = min(1.0, max(0.0, 0.5 + 0.9 * v))
        k = amt * s
        if zfade is not None:
            z0, z1, kz = zfade
            k += kz * min(1.0, max(0.0, (co.z - z0) / max(z1 - z0, 1e-3)))
        return max(floor, 1.0 - min(0.85, k))
    return f


# ---------------------------------------------------------------- output

def paint(obs, keys, B, soot):
    for ob, key in zip(obs, keys):
        vm = B.mats[key]
        me = ob.data
        ca = me.color_attributes.get("Col") or me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
        n = len(me.vertices)
        tint = np.ones((n, 3), np.float32)
        ta = me.attributes.get("tint")
        if ta is not None:
            tv = np.empty(n * 3, np.float32)
            ta.data.foreach_get("vector", tv)
            tv = tv.reshape(n, 3)
            on = np.abs(tv).sum(1) > 0
            tint[on] = tv[on]
            me.attributes.remove(ta)
        cols = np.empty((n, 4), np.float32)
        for i, v in enumerate(me.vertices):
            s = soot(v.co) if (soot is not None and vm.soot > 0) else 1.0
            k = 1.0 - vm.soot * (1.0 - s)
            c = vm.base * k * tint[i]
            cols[i] = (min(c[0], 1.0), min(c[1], 1.0), min(c[2], 1.0), 1.0)
        ca.data.foreach_set("color", cols.ravel())


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


SCALE = {}
BUILD = {}
TONE = {}
CAL = {}


def model(name, scale=2, tone=1.0, cal="opaque"):
    def deco(fn):
        BUILD[name] = fn
        SCALE[name] = scale
        TONE[name] = tone
        CAL[name] = cal
        return fn
    return deco


def brightness(name, r, tone, cal="opaque"):
    """Evens the model's brightness with the picture's where both draw:
    every texture and untinted colour scaled by one grey factor, so each
    material keeps its own hue (stone grey, wood warm) and only the light
    level moves. Returns the factor. It reads the render's fully opaque
    pixels (cal "opaque"). "alpha" is the older reading, partly covered
    pixels divided by their alpha, which CreShed01a was made with."""
    spr = ck.Sprite(name)
    path = os.path.join(OUT, "work", name + "_cal.png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    ck.classic(spr, path)
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    a = a.reshape(h, w, 4)[::-1][:spr.h, :spr.w]
    both = (a[..., 3] > (0.99 if cal == "opaque" else 0.5)) & spr.mask
    if both.sum() < 20:
        return 1.0
    if cal == "opaque":
        ren = a[..., :3][both] @ ck.LUMA
    else:
        ren = (a[..., :3][both] / a[..., 3][both][:, None]) @ ck.LUMA
    want = spr.lum[both] * tone
    f = float(np.clip(want.mean() / max(ren.mean(), 1e-3), 0.75, 1.5))
    seen = set()
    for ob in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        for m in ob.data.materials:
            texs = [n for n in m.node_tree.nodes if n.type == "TEX_IMAGE"] if m and m.use_nodes else []
            for t in texs:
                if t.image.name in seen:
                    continue
                seen.add(t.image.name)
                px = np.empty(len(t.image.pixels), np.float32)
                t.image.pixels.foreach_get(px)
                px = px.reshape(-1, 4)
                px[:, :3] = np.clip(px[:, :3] * f, 0, 1)
                t.image.pixels.foreach_set(px.ravel())
                t.image.pack()
            if not texs and m is not None:
                ca = ob.data.color_attributes.get("Col")
                c = np.empty(len(ca.data) * 4, np.float32)
                ca.data.foreach_get("color", c)
                c = c.reshape(-1, 4)
                c[:, :3] = np.clip(c[:, :3] * f ** 2.2, 0, 1)
                ca.data.foreach_set("color", c.ravel())
    return f


def opaque_mean(path):
    """The mean sRGB of a render's fully opaque pixels."""
    img = bpy.data.images.load(path, check_existing=False)
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    a = a.reshape(-1, 4)
    return a[a[:, 3] > 0.99, :3].mean(0)


def build(name, render=True):
    hk.reset()
    r = A.CAT[name]
    pic = A.Picture(r)
    seed = zlib.crc32(name.encode()) % 100000
    B = A.Builder(seed=seed)
    soot = BUILD[name](B, r, pic, seed)
    vary_islands(B, seed)
    keys = list(B.bms.keys())
    obs = B.objects()
    pairs = [(o, k) for o, k in zip(obs, keys) if len(o.data.polygons)]
    for o, _ in pairs:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(o.data)
        bm.free()
    paint([o for o, _ in pairs], [k for _, k in pairs], B, soot)
    obs = [o for o, _ in pairs]
    f = brightness(name, r, TONE[name], CAL[name]) if TONE[name] else 1.0
    print("G6_TONE", name, "%.3f" % f, flush=True)
    if os.environ.get("G6_PARTS"):
        for o in sorted(obs, key=tris, reverse=True)[:14]:
            print("G6_PART", name, o.name, tris(o), flush=True)
    glb = os.path.join(OUT, name + ".glb")
    ob = hk.finish(obs, glb, {"feature": name, "family": "creon"})
    ob.name = name
    n = tris(ob)
    lo = [min(v.co[i] for v in ob.data.vertices) for i in range(3)]
    hi = [max(v.co[i] for v in ob.data.vertices) for i in range(3)]
    print("G6_BUILT", name, "tris", n, "x %.2f..%.2f y %.2f..%.2f z %.2f..%.2f" % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]),
          flush=True)
    if render:
        rd = os.path.join(OUT, "renders")
        spr = os.path.join(DATA, "sprites", name + ".png")
        hk.renders(ob, rd, name, spr, tuple(r["sprite"]["hotspot"]), scale=SCALE[name])
        m = ck.fit(ck.Sprite(name), os.path.join(rd, name + "_classic.png"))
        if m:
            print("G6_FIT", name, "iou %.3f cover %.3f spill %.3f" % (m["iou"], m["cover"], m["spill"]),
                  "render #%02x%02x%02x" % tuple(int(v * 255) for v in opaque_mean(os.path.join(rd, name + "_classic.png"))),
                  "drawing #%02x%02x%02x" % tuple(int(v * 255) for v in ck.Sprite(name).mean()), flush=True)
    return n


def P(r, col, row, z=0.0):
    """The plan point the stage's picture draws at (col, row) at height z."""
    return A.px(r, col, row, z)


def poly_plan(r, poly, z=0.0):
    return [P(r, c, rw, z) for c, rw in poly]


def ash_bed(B, mat, r, pic, poly, top=0.05, z_at=0.05, cell=0.3, seed=0, amp=2.0, strict=False):
    """A low bed of ash and grit only where the picture draws something,
    its edges sloping into the ground within a cell (or torn off there)."""
    return A.heap(B, mat, r, A.jag(poly, amp, 4.0, seed), top, z_at=z_at, cell=cell, noise=0.03, seed=seed + 1, edge=0.6,
                  pic=pic, grow=0, strict=strict, base=-0.04)


def flat(z):
    return lambda x, y: z


def density(pic, rad=2):
    """How much of the drawing is solid round each pixel, over a square
    2 rad + 1 wide."""
    a = pic.alpha.astype(float)
    pad = np.pad(a, rad)
    n = 2 * rad + 1
    return sum(pad[i:i + a.shape[0], j:j + a.shape[1]] for i in range(n) for j in range(n)) / float(n * n)


def speck_bed(B, mat, r, pic, poly, z=0.02, step=0.42, size=(0.3, 0.5), thr=0.45, rad=2, avoid=(), seed=0, k=7,
              keep=None):
    """A bed lying flat at z where the picture draws speckle inside an
    outline traced on it: ragged round patches (mat is cut by its alpha,
    so ground shows between the specks), one wherever the drawing round a
    point is solid enough, smaller where it thins out. avoid is circles
    (x, y, R) in plan left bare, keep(x, y) an extra test."""
    rng = random.Random(seed)
    dens = density(pic, rad)
    P = poly_plan(r, poly, 0.0)
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    n = 0
    for gy in np.arange(min(ys), max(ys) + step, step):
        for gx in np.arange(min(xs), max(xs) + step, step):
            x, y = gx + rng.uniform(-0.4, 0.4) * step, gy + rng.uniform(-0.4, 0.4) * step
            if not A.inside_poly(P, x, y) or any(math.hypot(x - ax, y - ay) < aR for ax, ay, aR in avoid):
                continue
            if keep is not None and not keep(x, y):
                continue
            c, rw = A.screen(r, x, y, 0.0)
            ci, ri = int(c), int(rw)
            d = dens[ri, ci] if 0 <= ri < dens.shape[0] and 0 <= ci < dens.shape[1] else 0.0
            if d < thr:
                continue
            R = rng.uniform(*size) * (0.65 + 0.35 * min(1.0, (d - thr) / max(1e-3, 1.0 - thr)))
            a0 = rng.uniform(0, 2 * math.pi)
            pts = []
            for i in range(k):
                a = a0 + 2 * math.pi * (i + rng.uniform(-0.25, 0.25)) / k
                rr = R * rng.uniform(0.65, 1.15)
                pts.append((x + rr * math.cos(a), y + rr * math.sin(a), z + 0.003 * (n % 4)))
            B.mesh(mat, pts, [tuple(range(k))])
            n += 1
    return n


# ================================================================ fences

# The fences' stone as the pictures draw it: tan-grey rubble, about one
# stone in seven pale tan-cream, a few dark, sooted grey where it fell.
FENCE_STONE = [("#b6aa80", 0.17), ("#877c5e", 0.25), ("#675e46", 0.27), ("#463f2f", 0.22), ("#2a261c", 0.09)]


def fence_stone(name, seed, cols=FENCE_STONE):
    """One stone material made at the palest tan, each stone a drawn
    shade of it through its vertex colour."""
    made = "#b6aa80"
    return VMat(name + "_st", A.tex_mottle(name + "_st", A.saturate(made, 1.1), var=0.16, seed=seed), uv=0.6, soot=0.35,
                vary=cols, made=made)


def fence_grit(name, seed, cover=0.6):
    """Grit and stone dust lying flat round a heap, specks of the stones'
    colours with ground between."""
    return speck_mat(name + "_grit", [("#8a7d5c", 0.25), ("#5e5640", 0.3), ("#3a3428", 0.25), ("#16140e", 0.2)],
                     cover=cover, grain=0.07, uv=1.5, seed=seed)


def course(B, stone, x0, x1, y, depth, h, rng):
    """A pier's last course: a row of blocks with gaps, knocked askew."""
    n = max(2, int(round((x1 - x0) / 0.26)))
    for i in range(n):
        if rng.random() < 0.2:
            continue
        x = x0 + (i + 0.5) * (x1 - x0) / n
        for dy in (-depth / 4, depth / 4):
            if rng.random() < 0.3:
                continue
            B.box(stone, x + rng.uniform(-0.03, 0.03), y + dy + rng.uniform(-0.03, 0.03), 0.0,
                  (x1 - x0) / n * 0.9, depth / 2 * 0.9, h * rng.uniform(0.6, 1.1), yaw=rng.uniform(-12, 12),
                  pitch=rng.uniform(-6, 6))


def heap_surface(r, poly, top, z_at=0.1, edge=0.45):
    """A heap's height over plan points inside an outline traced on the
    picture, top in its middle falling to the ground at its edge. It is far
    below the ground outside, so pieces laid on it stay inside."""
    Pp = poly_plan(r, poly, z_at)

    def dist(x, y):
        best = 1e9
        for i in range(len(Pp)):
            a, b = Vector(Pp[i]), Vector(Pp[i - 1])
            p = Vector((x, y))
            d = b - a
            t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
            best = min(best, (a + d * t - p).length)
        return best

    def f(x, y):
        if not A.inside_poly(Pp, x, y):
            return -100.0
        return top * min(1.0, dist(x, y) / edge) ** 0.7
    return f, Pp


def rubble_heaps(B, r, pic, seed, heaps, stone, grit, n_stones, big=()):
    """Low rubble heaps traced on the picture, each (poly, top): a dark
    core of fallen stone with stones piled over it where the picture
    draws, big dressed blocks (x, y, sx, sy, sz, yaw, tilt) on top, and
    grit lying flat round it inside its outline."""
    rng = random.Random(seed + 5)
    fns = []
    for i, (poly, top) in enumerate(heaps):
        f, Pp = heap_surface(r, poly, top)
        fns.append(f)
        cx = sum(p[0] for p in Pp) / len(Pp)
        cy = sum(p[1] for p in Pp) / len(Pp)
        w = (max(p[0] for p in Pp) - min(p[0] for p in Pp)) * 0.55
        d = (max(p[1] for p in Pp) - min(p[1] for p in Pp)) * 0.55
        with tinted(B, stone, tint_of("#b6aa80", "#3a3428")):
            A.rock(B, stone, cx, cy, -0.03, w * 0.6, d * 0.6, top * 0.55 + 0.03, seed=seed + 30 + i, seg=6)
        speck_bed(B, grit, r, pic, poly, z=0.015, step=0.2, size=(0.12, 0.22), thr=0.3, rad=1, seed=seed + 40 + i)
    ground = A.ground_of(*fns)
    for i, (poly, top) in enumerate(heaps):
        A.cover(B, {"stone": [stone], "chunk": [stone]}, r, poly_plan(r, poly, 0.1), ground, n_stones[i],
                {"stone": 3, "chunk": 2}, pic=pic, seed=seed + 20 + i, stone=(0.1, 0.22), chunk=(0.07, 0.15), grow=1,
                lift=0.0, tilt=25, q=0.6)
    for x, y, sx, sy, sz, yaw, tilt in big:
        z = max(0.0, ground(x, y))
        B.box(stone, x, y, max(0.0, z - sz * 0.35), sx, sy, sz, yaw=yaw, pitch=tilt, roll=rng.uniform(-12, 12))
    return ground


@model("CreFence01a", scale=4, tone=0)
def fence01a(B, r, pic, seed):
    """The stone arch fallen into two low heaps at its piers' feet, the
    arch's dressed blocks lying across them and a scatter between."""
    stone = fence_stone("f1", seed)
    grit = fence_grit("f1", seed)
    rng = random.Random(seed + 9)
    for x0, x1 in ((-1.4, -0.65), (0.8, 1.55)):
        course(B, stone, x0, x1, 0.12, 0.5, 0.14, rng)
    heaps = [([(0, 3), (17, 1), (19, 12), (15, 20), (2, 19)], 0.3),
             ([(19, 6), (34, 6), (35, 16), (20, 17)], 0.16),
             ([(35, 0), (54, 2), (55, 16), (38, 18)], 0.3)]
    big = [(-1.2, -0.25, 0.26, 0.17, 0.14, 25, 14), (0.05, -0.2, 0.24, 0.16, 0.12, 75, 6),
           (1.1, -0.25, 0.26, 0.17, 0.14, -15, 12)]
    rubble_heaps(B, r, pic, seed, heaps, stone, grit, [40, 14, 40], big)
    return noise_soot(seed, amt=0.3, scale=2.0)


@model("CreFence02a", scale=4, tone=0)
def fence02a(B, r, pic, seed):
    """The stone pier fallen forward into one low heap."""
    stone = fence_stone("f2", seed)
    grit = fence_grit("f2", seed)
    course(B, stone, -0.28, 0.28, -0.05, 0.45, 0.13, random.Random(seed + 9))
    heaps = [([(2, 2), (14, 0), (26, 3), (26, 13), (14, 16), (3, 13)], 0.26)]
    big = [(-0.2, -0.4, 0.26, 0.17, 0.13, 30, 12)]
    rubble_heaps(B, r, pic, seed, heaps, stone, grit, [38], big)
    return noise_soot(seed, amt=0.3, scale=2.0)


def rail_fence(B, r, pic, seed, posts, pieces, line_poly, n_splinters, stone_cols=FENCE_STONE):
    """A rail fence down: each post base a low heap of its stones round a
    stump, the rails fallen charred where the picture draws them (pieces,
    each (col, row) to (col, row) on the ground, and a width), the rest
    burnt to grey ash lying flat along the line with a few splinters."""
    stone = fence_stone("fr", seed, stone_cols)
    grit = fence_grit("fr", seed, cover=0.5)
    ash = speck_mat("fr_ash", [("#5a5d5a", 0.45), ("#727572", 0.15), ("#3c3e3c", 0.2), ("#161614", 0.2)], cover=0.7,
                    grain=0.065, uv=1.5, seed=seed + 1)
    char = VMat("fr_char", tex_char("fr_char", seed, base="#3a332c"), uv=0.7, soot=0.3,
                vary=[("#3a332c", 0.5), ("#2a2622", 0.5)], made="#3a332c")
    rng = random.Random(seed)
    heaps = []
    for i, (poly, (x, y)) in enumerate(posts):
        course(B, stone, x - 0.28, x + 0.28, y, 0.45, 0.13, rng)
        heaps.append((poly, 0.22))
    rubble_heaps(B, r, pic, seed, heaps, stone, grit, [30] * len(heaps), [])
    for (c0, r0), (c1, r1), w in pieces:
        p0 = Vector(P(r, c0, r0, 0.05)).to_3d()
        p1 = Vector(P(r, c1, r1, 0.05)).to_3d()
        p0.z, p1.z = 0.02 + w * 0.4, 0.02 + w * 0.4
        B.beam(char, tuple(p0), tuple(p1), w, w * 0.85, twist=rng.uniform(-20, 20))
    speck_bed(B, ash, r, pic, line_poly, z=0.012, step=0.2, size=(0.16, 0.26), thr=0.3, rad=1, seed=seed + 3)
    # a few short charred splinters along the line
    Pl = poly_plan(r, line_poly, 0.0)
    xs, ys = [p[0] for p in Pl], [p[1] for p in Pl]
    laid = 0
    for _ in range(n_splinters * 20):
        if laid >= n_splinters:
            break
        x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
        if not A.inside_poly(Pl, x, y) or not pic.sees((x, y, 0.0), 0):
            continue
        a = rng.uniform(0, math.pi)
        L = rng.uniform(0.14, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0)) * (L / 2)
        w = rng.uniform(0.035, 0.055)
        B.beam(char, (x - d.x, y - d.y, w / 2 + 0.01), (x + d.x, y + d.y, w / 2 + 0.01), w, w * 0.8,
               twist=rng.uniform(-20, 20))
        laid += 1
    return noise_soot(seed, amt=0.3, scale=2.0)


@model("CreFence03a", scale=4, tone=0)
def fence03a(B, r, pic, seed):
    """The north-south rail fence down: post bases as rubble at both ends,
    the rails fallen as charred pieces along the line."""
    posts = [([(3, 0), (20, 1), (21, 17), (12, 21), (3, 18)], P(r, 11, 9, 0.2)),
             ([(1, 60), (21, 59), (22, 78), (10, 81), (0, 75)], P(r, 11, 70, 0.2))]
    pieces = [((10, 21), (11, 33), 0.09), ((8, 37), (9, 48), 0.1), ((10, 49), (12, 60), 0.12), ((13, 24), (14, 29), 0.06)]
    # its heaps are drawn paler than the arch's, more of their stones cream
    pale = [("#bcb086", 0.22), ("#8a7f60", 0.25), ("#675e46", 0.25), ("#463f2f", 0.19), ("#2a261c", 0.09)]
    return rail_fence(B, r, pic, seed, posts, pieces, [(5, 12), (17, 12), (17, 62), (5, 62)], 6, pale)


@model("CreFence04a", scale=4, tone=0)
def fence04a(B, r, pic, seed):
    """The east-west rail fence down, 03a turned."""
    posts = [([(0, 0), (13, 0), (14, 15), (6, 18), (0, 14)], P(r, 6, 8, 0.2)),
             ([(70, 0), (84, 0), (84, 14), (77, 17), (69, 14)], P(r, 77, 8, 0.2))]
    pieces = [((11, 8), (27, 6), 0.12), ((28, 6), (42, 3), 0.1), ((14, 10), (22, 10), 0.07), ((48, 5), (56, 6), 0.06),
              ((61, 4), (67, 7), 0.06)]
    # its heaps are drawn a shade darker than 03a's
    dark = [("#a49a74", 0.12), ("#7a7055", 0.25), ("#5c543e", 0.28), ("#40392b", 0.24), ("#262219", 0.11)]
    return rail_fence(B, r, pic, seed, posts, pieces, [(10, 1), (72, 1), (72, 12), (10, 12)], 6, dark)


# ================================================================ well

# the basin's basalt raised from the intact's dark vertex colour to the drawn grey
STONE_LIFT = 2.1
# the burnt roof's planks as drawn: warm orange-brown charred boards
WELL_PLANK = [("#6a4622", 0.35), ("#523618", 0.4), ("#3a2818", 0.25)]


@model("CreWell01a", scale=4, tone=0)
def well01a(B, r, pic, seed):
    """CreWell01's basin broken round a burnt-out pit. Its four walls are
    the intact model's boxes in its basalt, cut into blocks of uneven
    size, shifted and tipped on their beds: the south, east and west runs
    standing to ragged heights, the blocks at the south corners and the
    west run's south end tumbled in toward the pit so the corners round
    off, the north corners low and tipped in, and the north run thrown
    down where the roof came over it, its middle pushed out behind. The
    pit is open and fouled black, its black running out through the
    north run's gap. The burnt roof's planks lie charred round it, warm
    brown, over the corners and the north, a few of their ends dropped
    into the pit, and the pale log has slid in from the west. The posts
    burnt through, one lies behind."""
    rng = random.Random(seed)
    intact = ck.Sprite("CreWell01")
    parts, _ = cwell.build(intact, cmodels.MODELS["CreWell01"])
    basins = [p for p in parts if p.name.startswith("basin")]
    lo = [min(min((p.matrix_world @ v.co)[i] for v in p.data.vertices) for p in basins) for i in range(2)]
    hi = [max(max((p.matrix_world @ v.co)[i] for v in p.data.vertices) for p in basins) for i in range(2)]
    for p in parts:
        bpy.data.objects.remove(p, do_unlink=True)
    bx0, bx1, by0, by1 = lo[0], hi[0], lo[1], hi[1]
    cx, cy = (bx0 + bx1) / 2, (by0 + by1) / 2
    # the intact's basalt: its texture and its STONE vertex colour, raised
    # to the sooted grey the drawing has on the runs (lit tops near
    # #454543), soot kept to the pit and under the timbers
    stone = VMat("w_basalt", ck.image("w_basalt", ck.tex_stone("basalt", zlib.crc32(b"CreWell01") % 1000)), uv=1.0,
                 soot=0.5)
    stone.base = np.array(ck.srgb_to_lin(cwell.STONE)) * STONE_LIFT
    B.bevels[stone.name] = (0.03, 1)
    plank = VMat("w_plank", tex_char("w_plank", seed, base="#6a4622", hi="#9a6a38", crack="#1c120a"), uv=0.6,
                 soot=0.3, vary=WELL_PLANK, made="#6a4622")
    log = VMat("w_log", A.tex_planks("w_log", "#76643f", boards=2, seed=seed), uv=0.6, soot=0.6)
    # the pit near black, and with no sheen to grey it
    pit = VMat("w_pit", None, colour="#120f0c", rough=0.95, soot=0.0)
    pit.m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.1
    ash = speck_mat("w_ash", [("#37322a", 0.35), ("#4a4238", 0.2), ("#5a4a34", 0.15), ("#201c16", 0.3)], cover=0.65,
                    grain=0.07, uv=1.5, seed=seed + 1)
    wall, hz = 0.36, 0.5
    corner = tint_of("#808080", "#5c5c5a")
    # the pit's open middle and the north run's gap, kept clear of blocks and planks
    gx0, gx1 = -0.15, 0.6

    def tip():
        """A block's tilt on its bed, 5 to 20 degrees about a level axis."""
        t, ph = rng.uniform(5, 20), rng.uniform(0, 2 * math.pi)
        return t * math.cos(ph), t * math.sin(ph)

    def blocks(a, b):
        """Blocks 0.3 to 0.5 long laid end to end from plan point a to b:
        (centre x, y, length) for each."""
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        ls = []
        while sum(ls) < L - 0.15:
            ls.append(rng.uniform(0.3, 0.5))
        k = L / sum(ls)
        out, s = [], 0.0
        for l_ in ls:
            t = (s + l_ * k / 2) / L
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, l_ * k))
            s += l_ * k
        return out

    def stand(x, y, L, yaw, h, tint=None):
        p, q = tip()
        d = wall * rng.uniform(0.82, 1.05)
        x += rng.uniform(-0.03, 0.03)
        y += rng.uniform(-0.03, 0.03)
        yaw += rng.uniform(-10, 10)
        if tint is not None:
            with tinted(B, stone, tint):
                B.box(stone, x, y, -0.03, L * 0.94, d, h, yaw=yaw, pitch=p, roll=q)
        else:
            B.box(stone, x, y, -0.03, L * 0.94, d, h, yaw=yaw, pitch=p, roll=q)
        if h > hz * 0.75 and rng.random() < 0.35:
            # a top stone knocked askew on it
            B.box(stone, x + rng.uniform(-0.05, 0.05), y + rng.uniform(-0.05, 0.05), h - 0.06, L * 0.6,
                  d * 0.8, rng.uniform(0.1, 0.16), yaw=yaw + rng.uniform(-15, 15), pitch=p * 0.5, roll=q * 0.5)

    def drop(x, y, L, tint=None):
        """A block tumbled in toward the pit, lying tipped on the basin's floor."""
        args = (stone, x, y, -0.04, L * 0.85, wall * rng.uniform(0.75, 0.95), hz * rng.uniform(0.45, 0.6))
        kw = dict(yaw=rng.uniform(0, 180), pitch=rng.uniform(18, 32) * rng.choice((-1, 1)), roll=rng.uniform(-15, 15))
        if tint is not None:
            with tinted(B, stone, tint):
                B.box(*args, **kw)
        else:
            B.box(*args, **kw)
    # the south run, its corners and all: the end blocks tumbled in
    s_run = blocks((bx0, by0 + wall / 2), (bx1, by0 + wall / 2))
    for i, (x, y, L) in enumerate(s_run):
        if i == 0:
            drop(-0.5, -0.5, L, corner)
        elif i == len(s_run) - 1:
            drop(0.72, -0.48, L, corner)
        else:
            stand(x, y, L, 0.0, hz * rng.uniform(0.6, 1.0))
    # the west run up to its north corner: its south block tumbled in, the
    # corner low and tipped in
    w_run = blocks((bx0 + wall / 2, by0 + wall), (bx0 + wall / 2, by1))
    for i, (x, y, L) in enumerate(w_run):
        if i == 0:
            drop(-0.42, -0.2, L)
        elif i == len(w_run) - 1:
            stand(x + 0.08, y - 0.06, L * 0.9, 90.0, hz * rng.uniform(0.5, 0.65), corner)
        else:
            stand(x, y, L, 90.0, hz * rng.uniform(0.6, 1.0))
    # the east run up to its north corner, the corner low and tipped in
    e_run = blocks((bx1 - wall / 2, by0 + wall), (bx1 - wall / 2, by1))
    for i, (x, y, L) in enumerate(e_run):
        if i == len(e_run) - 1:
            stand(x - 0.08, y - 0.06, L * 0.9, 90.0, hz * rng.uniform(0.45, 0.6), corner)
        else:
            stand(x, y, L, 90.0, hz * rng.uniform(0.6, 1.0))
    # the north run thrown down low, its middle pushed out behind it
    for x, y, L in blocks((bx0 + wall, by1 - wall / 2), (bx1 - wall, by1 - wall / 2)):
        if gx0 - 0.1 < x < gx1 + 0.1:
            B.box(stone, x + rng.uniform(-0.1, 0.1), y + rng.uniform(0.75, 0.9), -0.04, L * 0.9, wall * 0.85,
                  hz * rng.uniform(0.4, 0.5), yaw=rng.uniform(-30, 30), pitch=rng.uniform(10, 20),
                  roll=rng.uniform(-12, 12))
        else:
            stand(x, y, L, 0.0, hz * rng.uniform(0.3, 0.5))
    # a block of the west run thrown out west, one of the south run out
    # in front
    for x, y in ((bx0 - 0.15, 0.32), (0.35, by0 - 0.1)):
        B.box(stone, x, y, -0.04, 0.38, 0.3, hz * 0.45, yaw=rng.uniform(0, 90), pitch=rng.uniform(-20, 20),
              roll=rng.uniform(-20, 20))
    # the pit, fouled black and open, its black running out through the
    # north run's gap and over the ground behind where the roof burnt
    B.box(pit, cx, cy, 0.0, bx1 - bx0 - wall * 2 + 0.04, by1 - by0 - wall * 2 + 0.04, 0.06)
    gap = [(gx0, by1 - wall), (gx1, by1 - wall), (gx1 + 0.08, by1 + 0.15), (gx1 - 0.05, by1 + 0.32),
           ((gx0 + gx1) / 2, by1 + 0.4), (gx0 + 0.05, by1 + 0.3), (gx0 - 0.08, by1 + 0.12)]
    B.prism(pit, gap, 0.0, 0.055)
    # the pale log, slid in from the west, its end down in the pit
    A.log(B, log, (-0.6, -0.15, 0.42), (-0.1, -0.3, 0.12), 0.1, seg=7)
    # the roof's planks, charred: lying over the corners and the north,
    # some from the ground up onto a run, a few ends dropped into the pit
    for a, b in (((-1.15, 0.62, 0.03), (-0.25, 0.2, 0.3)), ((-0.95, 0.2, 0.45), (-0.12, -0.05, 0.14)),
                 ((1.15, 0.75, 0.05), (0.6, 0.3, 0.32)), ((1.05, -0.05, 0.45), (0.48, -0.35, 0.1)),
                 ((-1.2, -0.45, 0.05), (-0.45, -0.52, 0.32)), ((0.0, 1.2, 0.03), (0.7, 0.85, 0.04)),
                 ((-0.75, 0.8, 0.03), (-0.05, 0.45, 0.12))):
        B.beam(plank, a, b, rng.uniform(0.18, 0.26), 0.05, twist=rng.uniform(-8, 8))
    post = tint_of("#6a4622", "#3a2818")
    with tinted(B, plank, post):
        # the posts burnt through at their feet, one lying behind
        B.beam(plank, (0.85, 0.75, 0.07), (0.15, 1.0, 0.07), 0.15)
    # warm ash under all, where the picture draws, ground between
    speck_bed(B, ash, r, pic, [(0, 2), (45, 2), (45, 43), (0, 43)], z=0.015, step=0.3, size=(0.22, 0.34), thr=0.45,
              rad=2, seed=seed)

    def soot(co):
        # soot climbs the stones from the pit, and lies on the inner faces
        # under the burnt planks, the outer faces and tops left grey
        d = math.hypot((co.x - cx) / (bx1 - bx0), (co.y - cy) / (by1 - by0))
        k = 0.35 * max(0.0, 1.0 - d * 1.8)
        return noise_soot(seed, amt=0.25, scale=2.2)(co) * (1.0 - k)
    return soot


# ================================================================ cart

CART_YAW = -38.0  # the shaft's way in plan, read off the intact drawing


def cart_bed(B, wood, board, x, y, z, yaw, pitch, roll, L, W, sides=0.28, broken=None, rng=None):
    """A cart bed: floor boards across, side boards along, as one rigid
    piece placed at (x, y, z) turned yaw (degrees) and tipped. broken is
    the side boards' ragged heights, a list per side."""
    with B.at(x, y, z, yaw=yaw, pitch=pitch, roll=roll):
        nb = max(3, int(L / 0.24))
        for i in range(nb):
            if broken and rng.random() < 0.12:
                continue
            u = -L / 2 + (i + 0.5) * L / nb
            B.box(board, u, 0, 0.0, L / nb * 0.9, W * rng.uniform(0.85, 1.0) if broken else W, 0.06)
        for s in (-1, 1):
            prof = broken[0 if s < 0 else 1] if broken else [(0, sides), (1, sides)]
            A.broken_wall(B, wood, board, (-L / 2, s * (W / 2 - 0.03)), (L / 2, s * (W / 2 - 0.03)), 0.06,
                          [(f, h + 0.06) for f, h in prof], z0=0.06, step=0.2, jitter=0.04 if broken else 0.0,
                          seed=rng.randint(0, 999), notch=0.2 if broken else 0.0)
        # the frame under it
        for s in (-1, 1):
            B.box(wood, 0, s * W * 0.32, -0.1, L, 0.1, 0.1)


# the cart's timber as drawn where it did not burn: warm brown boards
CART_WOOD = [("#8c7a66", 0.3), ("#6e5e4c", 0.4), ("#56483a", 0.3)]


@model("CreCart01a", scale=4, tone=0)
def cart01a(B, r, pic, seed):
    """The burnt cart: its bed snapped in two, the back half dropped onto
    its axle on one side where a wheel came off, the front half down flat
    with its boards splayed, a wheel lying on the ground, the shaft broken
    off. The fire took the middle, a heap of charred boards and the load
    over black ash, and charred the ends of what was left. The back rails,
    the shaft and the lost wheel are still warm brown timber."""
    rng = random.Random(seed)
    char = VMat("c_char", tex_char("c_char", seed, base="#3a3028", hi="#6a5c4c"), uv=0.6, soot=0.2,
                vary=[("#3a3028", 0.5), ("#2c2620", 0.5)], made="#3a3028")
    wood = VMat("c_wood", A.tex_planks("c_wood", "#8c7a66", boards=2, seed=seed, var=0.12), uv=0.6,
                soot=0.9, vary=CART_WOOD, made="#8c7a66")
    iron = VMat("c_iron", A.tex_mottle("c_iron", "#2c2c2c", var=0.2, seed=seed), uv=0.5, soot=0.2, rough=0.6)
    cargo = VMat("c_cargo", A.tex_mottle("c_cargo", "#655d50", var=0.16, seed=seed), uv=0.5, soot=0.6,
                 vary=[("#655d50", 0.5), ("#4a4945", 0.5)], made="#655d50")
    heap_m = VMat("c_heap", A.tex_grit("c_heap", "#26221d", light="#4a4540", dark="#100e0c", uv=1.2, chips=0.06,
                                       darks=0.25, seed=seed), uv=1.2, soot=0.2)
    ash = speck_mat("c_ash", [("#2a2420", 0.35), ("#1a1614", 0.35), ("#3c3530", 0.2), ("#4e4844", 0.1)], cover=0.6,
                    grain=0.07, uv=1.5, seed=seed + 1)
    B.bevels[wood.name] = (0.015, 1)
    yaw = CART_YAW
    # the back half, its near wheel gone: dropped onto that side
    bx, by = P(r, 20, 20, 0.25)
    cart_bed(B, wood, char, bx, by, 0.12, yaw + 12, -4, 11, 1.25, 1.0,
             broken=[[(0, 0.08), (0.4, 0.15), (1, 0.05)], [(0, 0.16), (0.5, 0.1), (1, 0.14)]], rng=rng)
    # its far wheel still on the axle, leaning out under the high side
    d = Vector((math.cos(math.radians(yaw + 12 + 90)), math.sin(math.radians(yaw + 12 + 90)), 0))
    wx, wy = bx + d.x * 0.62, by + d.y * 0.62
    A.wheel(B, wood, char, wx, wy, 0.24, 0.4, axis=tuple(d + Vector((0, 0, 1.8))), spokes=8, w=0.09)
    B.beam(iron, (bx - d.x * 0.3, by - d.y * 0.3, 0.1), (wx, wy, 0.24), 0.07)
    # the front half, down flat, its boards splayed out of the frame
    fx, fy = P(r, 50, 26, 0.1)
    with B.at(fx, fy, 0.03, yaw=yaw - 8):
        for i in range(6):
            u = -0.55 + i * 0.22 + rng.uniform(-0.05, 0.05)
            with B.at(u, rng.uniform(-0.1, 0.1), 0.0, yaw=rng.uniform(-14, 14), roll=rng.uniform(-6, 6)):
                B.box(char if i == 0 else wood, 0, 0, 0, 0.19, rng.uniform(0.75, 1.0), 0.06)
        A.broken_wall(B, wood, char, (-0.7, -0.5), (0.6, -0.55), 0.06, [(0, 0.2), (0.4, 0.26), (1, 0.08)],
                      step=0.2, jitter=0.04, seed=seed + 3)
        B.box(wood, 0.0, 0.45, 0.0, 1.2, 0.1, 0.12, yaw=6)
    # the shaft, snapped, one pole lying off the front
    a = Vector(P(r, 54, 30, 0.05)).to_3d()
    a.z = 0.06
    dd = Vector((math.cos(math.radians(yaw + 6)), math.sin(math.radians(yaw + 6)), 0))
    B.beam(wood, tuple(a), tuple(a + dd * 1.05 + Vector((0, 0, -0.01))), 0.09)
    B.beam(char, tuple(a + Vector((-0.25, 0.25, 0))), tuple(a + Vector((-0.25, 0.25, 0)) + dd * 0.55), 0.08)
    # the lost wheel, lying on the ground tipped on the debris
    lx, ly = P(r, 37, 42, 0.08)
    A.wheel(B, wood, wood, lx, ly, 0.11, 0.42, axis=(0.18, -0.12, 1.0), spokes=8, w=0.09)
    # the middle burnt through: charred boards and the load fallen in
    mid = [(18, 14), (44, 12), (46, 36), (24, 38)]
    g = A.heap(B, heap_m, r, A.jag(mid, 1.5, 3.0, seed), 0.2, z_at=0.15, cell=0.18, noise=0.05, seed=seed + 1,
               edge=0.5, pic=pic, strict=True)
    A.cover(B, {"beam": [char], "board": [char], "stone": [cargo]}, r, poly_plan(r, mid, 0.15), g, 14,
            {"beam": 2, "board": 3, "stone": 2}, pic=pic, seed=seed + 2, length=(0.4, 0.9), width=(0.07, 0.12),
            stone=(0.18, 0.3), lift=0.02, tilt=18, grow=1)
    # black ash round the burnt middle, specks with ground between, and
    # a few splinters
    under = [(0, 12), (30, 0), (60, 8), (62, 34), (44, 50), (10, 50)]
    speck_bed(B, ash, r, pic, under, z=0.015, step=0.3, size=(0.2, 0.34), thr=0.4, rad=2, seed=seed + 4)
    A.cover(B, {"beam": [char], "chunk": [char]}, r, poly_plan(r, under, 0.0), flat(0.0), 10,
            {"beam": 3, "chunk": 2}, pic=pic, seed=seed + 5, length=(0.15, 0.35), width=(0.04, 0.07),
            chunk=(0.05, 0.1), lift=0.02, tilt=6, grow=0)
    mx, my = P(r, 32, 25, 0.1)
    base = noise_soot(seed, amt=0.25, scale=2.0)

    def soot(co):
        # the fire was in the middle: timber near it charred, the ends warm
        k = max(0.0, 1.0 - math.hypot(co.x - mx, co.y - my) / 1.1)
        return base(co) * (1.0 - 0.6 * k)
    return soot


# ================================================================ shed

SHED = dict(x0=-1.5, x1=1.62, y0=-1.22, y1=1.65, wall=1.7, t=0.12)


@model("CreShed01a", scale=4, cal="alpha")
def shed01a(B, r, pic, seed):
    """The burnt shed: its plank walls where the intact's stand, burnt down
    to ragged tops (the front wall highest, with its window, the back and
    sides lower), the corner posts to stumps but one, the roof fallen in
    as a heap of charred boards and rafters inside, stones and barrels
    knocked out in front."""
    rng = random.Random(seed)
    s = SHED
    planks = VMat("s_planks", tex_boards("s_planks", "#5e5646", boards=10, seed=seed, gap="#1c1c13", var=0.18, joints=0.35), uv=1.6, soot=0.75)
    top = VMat("s_top", tex_char("s_top", seed, base="#2c2620", hi="#4d423b"), uv=0.6, soot=0.2)
    char = [VMat("s_char%d" % i, tex_char("s_char%d" % i, seed + i, base=c, hi="#625c50"), uv=0.5, soot=0.2)
            for i, c in enumerate(("#302a24", "#40382c"))]
    post = VMat("s_post", A.tex_planks("s_post", "#3a3026", boards=2, seed=seed + 3), uv=0.6, soot=0.7)
    stone = swatch("s_st", ["#3e3c36", "#48463a", "#5a5649"], "mottle", seed=seed, uv=0.5, soot=0.6)
    barrel_w = VMat("s_barrel", A.tex_planks("s_barrel", "#3e2c20", boards=6, vertical=True, seed=seed + 5), uv=0.6,
                    soot=0.5)
    barrel_r = VMat("s_barrel_r", A.tex_planks("s_barrel_r", "#3a2a24", boards=6, vertical=True, seed=seed + 6), uv=0.6,
                    soot=0.4)
    hoop = VMat("s_hoop", None, colour="#1c1a18", rough=0.6, soot=0.0)
    glow = VMat("s_window", None, colour="#5a3416", rough=0.8, soot=0.0)
    ash = VMat("s_ash", A.tex_grit("s_ash", "#3e3830", light="#5e5a50", dark="#201e18", uv=1.2, chips=0.1, darks=0.15,
                                    seed=seed),
               uv=1.2, soot=0.3)
    for m in [post]:
        B.bevels[m.name] = (0.02, 1)
    x0, x1, y0, y1, t = s["x0"], s["x1"], s["y0"], s["y1"], s["t"]
    # the walls, burnt to ragged tops, each a run of planks
    walls = {
        "S": ((x0, y0), (x1, y0), [(0, 1.45), (0.18, 1.5), (0.3, 0.7), (0.42, 0.55), (0.55, 1.2), (0.85, 1.35), (1, 1.1)]),
        "N": ((x1, y1), (x0, y1), [(0, 0.85), (0.35, 0.65), (0.6, 0.95), (0.85, 1.3), (1, 1.45)]),
        "W": ((x0, y1), (x0, y0), [(0, 1.2), (0.4, 0.8), (0.75, 0.55), (1, 0.9)]),
        "E": ((x1, y0), (x1, y1), [(0, 0.7), (0.4, 0.45), (0.7, 0.6), (1, 0.8)]),
    }
    for i, (side, (a, b, prof)) in enumerate(sorted(walls.items())):
        A.broken_wall(B, planks, top, a, b, t, prof, step=0.2, jitter=0.08, seed=seed + i, notch=0.2)
    # the front wall's window, a dark hole with the last of the shutter
    wx = x0 + (x1 - x0) * 0.72
    B.box(glow, wx, y0 - t / 2 - 0.005, 0.55, 0.42, 0.02, 0.32)
    B.box(char[0], wx, y0 - t / 2 - 0.02, 0.5, 0.52, 0.06, 0.06)
    # corner posts: one standing nearly whole, the rest burnt stumps
    for (x, y), h in (((x0, y1), 1.6), ((x1, y1), 1.0), ((x0, y0), 1.5), ((x1, y0), 1.2)):
        B.box(post, x, y, 0.0, 0.2, 0.2, h)
        B.box(top, x, y, h, 0.16, 0.16, 0.06, yaw=rng.uniform(0, 40), pitch=15)
    # the roof fallen in: a heap of charred boards and rafters on the
    # floor, rafters leaning from the back wall into it
    inner = [(14, 12), (60, 12), (60, 46), (14, 46)]
    g = A.heap(B, ash, r, inner, 0.55, z_at=0.4, cell=0.25, noise=0.08, seed=seed + 2, edge=0.9)
    A.cover(B, {"beam": char, "board": char + [planks]}, r, poly_plan(r, inner, 0.4), g, 34,
            {"beam": 3, "board": 3}, seed=seed + 3, length=(0.6, 1.4), width=(0.1, 0.16), lift=0.03, tilt=12,
            stick=0.05, jumble=0.06)
    for xa, za, xb in ((-1.0, 1.15, -0.6), (-0.2, 0.9, 0.1), (0.5, 1.2, 0.8), (1.1, 0.85, 1.3)):
        B.beam(rng.choice(char), (xa, y1 - 0.1, za), (xb, rng.uniform(-0.4, 0.2), g(xb, 0.0) + 0.05), 0.13)
    # the ridge beam, fallen across the heap
    B.beam(char[1], (x0 + 0.4, 0.9, g(x0 + 0.4, 0.9) + 0.1), (x1 - 0.3, -0.3, g(x1 - 0.3, -0.3) + 0.12), 0.17)
    # out in front: a stone heap and barrels knocked over, one burst
    fl = [(24, 58), (42, 58), (44, 72), (24, 72)]
    g2 = A.heap(B, ash, r, fl, 0.18, z_at=0.1, cell=0.18, noise=0.05, seed=seed + 4, edge=0.4, pic=pic, strict=True)
    A.cover(B, {"stone": stone, "chunk": stone}, r, poly_plan(r, fl, 0.1), g2, 14, {"stone": 3, "chunk": 1}, pic=pic,
            seed=seed + 5, stone=(0.15, 0.3), chunk=(0.08, 0.16), lift=0.0, grow=1)
    bx, by = P(r, 54, 66, 0.35)
    A.barrel(B, barrel_r, hoop, bx, by, 0.0, r=0.3, h=0.75)
    bx, by = P(r, 46, 64, 0.3)
    A.barrel(B, barrel_w, hoop, bx, by - 0.1, 0.0, r=0.28, h=0.7, lying=True, yaw=-25)
    bx, by = P(r, 15, 60, 0.2)
    with B.at(bx, by, 0.0, yaw=30):
        for k in range(5):
            a = math.radians(k * 70 + rng.uniform(-15, 15))
            B.box(barrel_w, 0.25 * math.cos(a), 0.25 * math.sin(a), 0.0, 0.12, 0.05, rng.uniform(0.3, 0.55),
                  yaw=math.degrees(a) + 90, pitch=rng.uniform(-30, 30))
        B.cyl(hoop, 0, 0, 0.0, 0.3, 0.04, seg=12)
    # ash and splinters round it all, where the picture draws
    ash_bed(B, ash, r, pic, [(0, 0), (71, 0), (71, 73), (0, 73)], top=0.04, z_at=0.05, cell=0.25, seed=seed + 6)
    A.cover(B, {"beam": char, "chunk": stone + char}, r, poly_plan(r, [(2, 44), (69, 44), (69, 72), (2, 72)], 0.0),
            flat(0.0), 20, {"beam": 2, "chunk": 2}, pic=pic, seed=seed + 7, length=(0.2, 0.5), width=(0.05, 0.08),
            chunk=(0.06, 0.12), lift=0.02, tilt=6, grow=0)

    def soot(co):
        # soot climbs the walls from their burnt tops and streaks down
        k = 0.45 * min(1.0, max(0.0, (co.z - 0.2) / 1.2))
        return noise_soot(seed, amt=0.35, scale=1.6)(co) * (1.0 - k)
    return soot


# ================================================================ dead plants

def dead_plant(B, r, pic, seed, crown=0.25):
    """A living bush's outline shrivelled to ash grey and collapsed: a low
    small core where it fell in, layers of dead leaf litter over it (cards
    cut by their alpha, so ground shows between the leaves as in the
    drawing), curled sprays standing up round the rim, and bare twigs
    splayed from the root out past the litter, bent down at their tips."""
    rng = random.Random(seed)
    cols = A.palette(pic, k=3, seed=seed, lit=0.0)
    dark, mid, light = ["#%02x%02x%02x" % tuple(int(v) for v in c) for c in cols]
    litter = VMat("p_litter", tex_litter("p_litter", [dark, mid, mid, mid, light], seed, gain=1.25), uv=1.0,
                  soot=0.0, cut=True)
    core = VMat("p_core", A.tex_grit("p_core", mid, light=light, dark=dark, uv=1.0, chips=0.15, darks=0.12, seed=seed),
                uv=1.0, soot=0.0)
    twig = VMat("p_twig", tex_char("p_twig", seed, base="#3a3936", hi="#4a4b4b", crack="#1c1c1a"), uv=0.4, soot=0.0)
    ys, xs = np.nonzero(pic.alpha)
    pts = np.array([P(r, c + 0.5, rw + 0.5, 0.0) for c, rw in zip(xs, ys)])
    cx, cy = pts.mean(0)
    R = float(np.percentile(np.hypot(pts[:, 0] - cx, pts[:, 1] - cy), 92))
    off = Vector((seed % 31 * 0.7, seed % 17 * 1.3, 0.0))

    def mound(x, y):
        d = math.hypot(x - cx, y - cy) / max(R, 1e-3)
        lump = 0.8 + 0.4 * noise.noise(Vector((x * 2.6, y * 2.6, 0.0)) + off)
        return crown * max(0.0, 1.0 - d * d) ** 0.7 * lump

    # the core: what the crown fell in to, low and small, inside the litter
    seg = 14
    ring = []
    for k in range(seg):
        a = 2 * math.pi * k / seg
        rr = R * 0.28 * (0.85 + 0.3 * rng.random())
        ring.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    V = [(x, y, 0.0) for x, y in ring] + [(cx + (x - cx) * 0.55, cy + (y - cy) * 0.55, mound(x, y) * 0.75)
                                          for x, y in ring] + [(cx, cy, crown * 0.45)]
    F = [tuple(range(seg))[::-1]]
    F += [(k, (k + 1) % seg, seg + (k + 1) % seg, seg + k) for k in range(seg)]
    F += [(seg + k, seg + (k + 1) % seg, 2 * seg) for k in range(seg)]
    B.solid(core, V, F)

    def card(x, y, z, size, nrm, mat):
        nrm = Vector(nrm).normalized()
        t1 = nrm.cross(Vector((0, 0, 1)) if abs(nrm.z) < 0.95 else Vector((1, 0, 0))).normalized()
        a = rng.uniform(0, 2 * math.pi)
        t1 = t1 * math.cos(a) + nrm.cross(t1) * math.sin(a)
        t2 = nrm.cross(t1)
        c = Vector((x, y, z))
        h = size / 2
        q = [c - t1 * h - t2 * h, c + t1 * h - t2 * h, c + t1 * h + t2 * h, c - t1 * h + t2 * h]
        low = min(v.z for v in q)
        if low < 0.01:
            q = [v + Vector((0, 0, 0.01 - low)) for v in q]
        high = max(v.z for v in q)
        if high > 0.48:
            # collapsed: nothing of it stands above half a cell
            q = [Vector((v.x, v.y, max(0.01, v.z - (high - 0.48)))) for v in q]
        B.mesh(mat, [tuple(v) for v in q], [(0, 1, 2, 3)])

    # litter: three layers of cards over the mound, each turned to its slope
    n = 0
    e = 0.1

    # how much of the drawing is solid round each pixel, over 5 by 5
    a = pic.alpha.astype(float)
    pad = np.pad(a, 2)
    dens = sum(pad[i:i + a.shape[0], j:j + a.shape[1]] for i in range(5) for j in range(5)) / 25.0

    def inside(x, y, z, k):
        # the card's middle in the drawing's thick and its reach toward each side at least in its fringe
        c, rw = A.screen(r, x, y, z)

        def d(cc, rr):
            ci, ri = int(cc), int(rr)
            return dens[ri, ci] if 0 <= ri < dens.shape[0] and 0 <= ci < dens.shape[1] else 0.0
        return d(c, rw) >= 0.45 and all(d(c + dc, rw + dr) >= 0.2 for dc, dr in ((k, 0), (-k, 0), (0, k), (0, -k)))

    for layer, (step, lift) in enumerate(((0.34, 0.0), (0.38, 0.05), (0.44, 0.1))):
        for gy in np.arange(cy - R * 1.15, cy + R * 1.15, step):
            for gx in np.arange(cx - R * 1.15, cx + R * 1.15, step):
                x, y = gx + rng.uniform(-0.45, 0.45) * step, gy + rng.uniform(-0.45, 0.45) * step
                z = mound(x, y) * (0.6 + 0.4 * layer / 2) + lift * rng.uniform(0.5, 1.0) + 0.02
                size = rng.uniform(0.42, 0.58)
                if not inside(x, y, z, 2.5):
                    continue
                # thinner toward the middle, where the drawing shows ground
                if math.hypot(x - cx, y - cy) < R * 0.6 and rng.random() < 0.45:
                    continue
                gx_ = (mound(x + e, y) - mound(x - e, y)) / (2 * e)
                gy_ = (mound(x, y + e) - mound(x, y - e)) / (2 * e)
                nrm = Vector((-gx_ + rng.uniform(-0.5, 0.5), -gy_ + rng.uniform(-0.5, 0.5), 1.0))
                card(x, y, z, size, nrm, litter)
                n += 1
    # curled sprays standing up round the rim, for some height seen low
    for k in range(int(10 * R)):
        a = rng.uniform(0, 2 * math.pi)
        rr = R * rng.uniform(0.45, 0.85)
        x, y = cx + rr * math.cos(a), cy + rr * math.sin(a)
        z = mound(x, y) * 0.6 + 0.08
        if not inside(x, y, z, 2.0):
            continue
        out = Vector((math.cos(a), math.sin(a), 0))
        card(x, y, z, rng.uniform(0.3, 0.4), out + Vector((0, 0, rng.uniform(0.6, 1.2))), litter)
        n += 1
    # twigs: from the root out past the litter, bent down to the ground
    n_tw = 7
    for k in range(n_tw):
        a = 2 * math.pi * (k + rng.uniform(-0.3, 0.3)) / n_tw
        d = np.array([math.cos(a), math.sin(a)])
        proj = (pts - [cx, cy]) @ d
        side = np.abs((pts - [cx, cy]) @ np.array([-d[1], d[0]]))
        sel = (side < 0.25) & (proj > 0)
        reach = float(np.percentile(proj[sel], 95)) if sel.sum() > 3 else R * 0.5
        reach = max(0.3, reach * rng.uniform(0.9, 1.05))
        bend = rng.uniform(-0.4, 0.4)
        chain = []
        for i in range(5):
            t = i / 4
            aa = a + bend * t
            x, y = cx + math.cos(aa) * reach * t, cy + math.sin(aa) * reach * t
            z = mound(x, y) * 0.9 + 0.03 if t < 0.9 else 0.03
            chain.append((x, y, max(0.03, z)))
        tube(B, twig, chain, [0.05, 0.042, 0.032, 0.024, 0.016], seg=4)
        p = Vector(chain[2])
        b = a + rng.choice((-1, 1)) * rng.uniform(0.5, 0.9)
        q = p + Vector((math.cos(b), math.sin(b), 0)) * reach * 0.45
        q.z = 0.03
        m = (p + q) / 2
        m.z = mound(m.x, m.y) * 0.9 + 0.04
        tube(B, twig, [tuple(p), tuple(m), tuple(q)], [0.026, 0.02, 0.014], seg=4)
    print("G6_PLANT", r["name"], "cards", n, "R %.2f" % R)
    return None


@model("CrePlant01a", scale=4)
def plant01a(B, r, pic, seed):
    return dead_plant(B, r, pic, seed)


@model("CrePlant02a", scale=4)
def plant02a(B, r, pic, seed):
    return dead_plant(B, r, pic, seed)


@model("CrePlant03a", scale=4)
def plant03a(B, r, pic, seed):
    return dead_plant(B, r, pic, seed, crown=0.26)


# ================================================================ inventions

FLOOR = (0.35, 0.33, 0.32)  # a tank floor: the grit darkened under its fouling


def invent_mats(n, seed):
    """The inventions' stuff as drawn: dark staves bound in copper, grey
    and purple-grey iron, green pipes, charred timber (the staves' burnt
    tops too), grit and gravel in one material whose pieces each take
    their own drawn shade, and the speckled bed they lie on."""
    M = {}
    M["stave"] = VMat(n + "_stave", tex_staves(n + "_stave", "#3a2c24", staves=8, seed=seed, var=0.22, gap="#120c0a"),
                      uv=1.0, soot=0.85)
    M["band"] = VMat(n + "_band", A.tex_mottle(n + "_band", "#8e7262", var=0.18, seed=seed, speck="#4a3a30",
                                               speck_amt=0.08), uv=0.5, soot=0.5, rough=0.55)
    M["iron"] = VMat(n + "_iron", A.tex_mottle(n + "_iron", "#2e2a2a", var=0.2, seed=seed + 1), uv=0.5, soot=0.3,
                     rough=0.6)
    M["grey"] = VMat(n + "_grey", A.tex_mottle(n + "_grey", "#5a5662", var=0.16, seed=seed + 2, speck="#2a2a30",
                                               speck_amt=0.06), uv=0.5, soot=0.5, rough=0.55)
    M["pipe"] = VMat(n + "_pipe", A.tex_mottle(n + "_pipe", "#5a5068", var=0.1, seed=seed + 3), uv=0.5, soot=0.3,
                     rough=0.65)
    M["green"] = VMat(n + "_green", A.tex_mottle(n + "_green", "#2a4a34", var=0.18, seed=seed + 4), uv=0.5, soot=0.4,
                      rough=0.5)
    char = VMat(n + "_char", tex_char(n + "_char", seed + 6, base="#3a2e26", hi="#5a4a3c"), uv=0.6, soot=0.1,
                vary=[("#3a2e26", 0.5), ("#2a221d", 0.5)], made="#3a2e26")
    M["char"] = [char]
    M["top"] = char
    grit = VMat(n + "_grit", A.tex_grit(n + "_grit", "#4a463e", light="#6e6a60", dark="#1e1c18", uv=1.5, chips=0.1,
                                        darks=0.18, blotch=0.4, seed=seed + 8), uv=1.5, soot=0.2,
                vary=[("#4a463e", 0.4), ("#5a5649", 0.25), ("#2e2c26", 0.35)], made="#4a463e")
    M["grit"] = grit
    M["gravel"] = [grit]
    M["bed"] = speck_mat(n + "_bed", [("#2e2825", 0.4), ("#504843", 0.2), ("#6a6258", 0.1), ("#121010", 0.3)],
                         cover=0.8, grain=0.07, uv=2.0, seed=seed + 10)
    return M


def liquid(n, base, seed, rough=0.4, **kw):
    return VMat(n, tex_liquid(n, base, seed=seed, **kw), uv=1.5, soot=0.0, rough=rough)


def h_at(heights):
    hs = sorted(heights)
    ang = [a for a, _ in hs] + [hs[0][0] + 360]
    val = [h for _, h in hs] + [hs[0][1]]

    def f(a):
        a = (a - ang[0]) % 360 + ang[0]
        return float(np.interp(a, ang, val))
    return f


def ring_bounds(heights, seg, lo=0.0, hi=360.0):
    """The angles a ring is cut at from lo to hi: every 360 / seg degrees
    and at each listed height's angle, so a height can step sharply (a
    section's broken end) instead of sloping across a whole segment."""
    step = 360.0 / seg
    b = [lo, hi]
    listed = [h[0] % 360.0 for h in heights]
    a = math.floor(lo / step + 1) * step
    while a < hi - 1e-6:
        # a grid cut near a listed one only adds facets
        if all(abs((a - q + 180.0) % 360.0 - 180.0) > step * 0.4 for q in listed):
            b.append(a)
        a += step
    for a, _ in heights:
        for a2 in (a % 360.0 - 360.0, a % 360.0, a % 360.0 + 360.0):
            if lo + 0.3 < a2 < hi - 0.3:
                b.append(a2)
    out = []
    for a in sorted(b):
        if not out or a - out[-1] > 0.3:
            out.append(a)
    out[-1] = hi
    return out


def stave_ring(B, mat, top, x, y, R, t, heights, seg=20, seed=0, jitter=0.12, notch=0.0, bounds=None, hf=None,
               step=0.3):
    """A round wall of staves broken down to the ragged heights [(angle,
    height)] round it, as the Aramon kit's ring_wall but with its notches
    under control, so a pool's level can be kept under every crack. A
    height of 0 is a gap, and a height listed a degree from another steps
    there, the end of a standing section."""
    hf = hf or h_at(heights)
    bs = bounds or ring_bounds(heights, seg)
    for i in range(len(bs) - 1):
        a0, a1 = bs[i], bs[i + 1]
        h0, h1 = hf(a0), hf(a1)
        if max(h0, h1) < 0.05 or (min(h0, h1) < 0.05 and a1 - a0 < 3.0):
            continue
        p0 = (x + R * math.cos(math.radians(a0)), y + R * math.sin(math.radians(a0)))
        p1 = (x + R * math.cos(math.radians(a1)), y + R * math.sin(math.radians(a1)))
        A.broken_wall(B, mat, top, p0, p1, t, [(0, h0), (1, h1)], seed=seed + i, jitter=jitter, step=step, notch=notch)
    return hf


def band_runs(B, mat, x, y, R, bounds, ok, zfn, w, t, faces=("outer",)):
    """Strips round a tank from radius R out to R + t, w tall, its foot at
    zfn(angle), on each run of the spans between bounds where ok(a0, a1):
    its outer face, and its top and inner faces where asked (a band over a
    wall's top)."""
    runs, cur = [], []
    for a0, a1 in zip(bounds, bounds[1:]):
        if ok(a0, a1):
            if not cur:
                cur = [a0]
            cur.append(a1)
        elif cur:
            runs.append(cur)
            cur = []
    if cur:
        whole = bounds[-1] - bounds[0] > 359.9
        if runs and whole and runs[0][0] == bounds[0] and cur[-1] == bounds[-1]:
            runs[0] = cur + runs[0][1:]
        else:
            runs.append(cur)
    for angles in runs:
        V, F = [], []
        for a in angles:
            c, s_ = math.cos(math.radians(a)), math.sin(math.radians(a))
            z0 = zfn(a)
            V += [(x + R * c, y + R * s_, z0), (x + (R + t) * c, y + (R + t) * s_, z0),
                  (x + (R + t) * c, y + (R + t) * s_, z0 + w), (x + R * c, y + R * s_, z0 + w)]
        for k in range(len(angles) - 1):
            b0, b1 = 4 * k, 4 * (k + 1)
            if "outer" in faces:
                F.append((b0 + 1, b1 + 1, b1 + 2, b0 + 2))
            if "top" in faces:
                F.append((b0 + 2, b1 + 2, b1 + 3, b0 + 3))
            if "inner" in faces:
                F.append((b0 + 3, b1 + 3, b1, b0))
        if "top" in faces:
            e = 4 * (len(angles) - 1)
            F += [(0, 1, 2, 3), (e + 3, e + 2, e + 1, e)]
        B.mesh(mat, V, F)


def in_arc(a, a0, a1):
    return (a - a0) % 360 <= (a1 - a0) % 360


def free_spans(arcs):
    """The spans of a circle (lo, hi), hi above lo, outside the arcs (a0, a1)."""
    if not arcs:
        return [(0.0, 360.0)]
    arcs = sorted((a0 % 360, a0 % 360 + (a1 - a0) % 360) for a0, a1, *_ in arcs)
    out = []
    for i, (a0, a1) in enumerate(arcs):
        n0 = arcs[(i + 1) % len(arcs)][0] + (360.0 if i + 1 == len(arcs) else 0.0)
        if n0 - a1 > 0.5:
            out.append((a1, n0))
    return out


def tank(B, M, x, y, R, heights, t=0.16, bands=(0.28, 0.62), band_h=0.12, rim=None, level=None, pool=None, seg=24,
         seed=0, floor=True, covered=(), jitter=0.12, notch=0.0, lean=(), step=0.3):
    """A stave tank broken open: its wall standing to the ragged heights
    [(angle, height)] round it, copper bands where the wall still stands
    above them, a rim band over what stands above rim, a floor and a pool.
    The pool's level is kept under every crack but those in covered, the
    spans (a0, a1) that a heap or a pour hides. Each lean arc (a0, a1,
    degrees) is a section split off and tipped outward about its foot.
    Returns (hf, level)."""
    hf = h_at(heights)
    full = max(h for _, h in heights)

    def section(cx, cy, off, lo, hi, sd):
        hl = [(a - off, h) for a, h in heights]

        def hfl(a):
            return hf(a + off)
        bs = ring_bounds(hl, seg, lo, hi)
        stave_ring(B, M["stave"], M["top"], cx, cy, R, t, hl, seed=sd, jitter=jitter, notch=notch, bounds=bs, hf=hfl,
                   step=step)

        def stands(z):
            return lambda a0, a1: min(hfl(a0), hfl(a1)) > z
        for f in bands:
            zb = f * full
            band_runs(B, M["band"], cx, cy, R + t / 2, bs, stands(zb + band_h + 0.08), lambda a, zb=zb: zb, band_h,
                      0.05)
        if rim is not None:
            band_runs(B, M["band"], cx, cy, R - t / 2 - 0.03, bs, stands(rim), lambda a: hfl(a) - 0.07, 0.1, t + 0.06,
                      faces=("outer", "top", "inner") if R > 1.5 else ("outer", "top"))
    for lo, hi in free_spans(lean):
        section(x, y, 0.0, lo, hi, seed)
    for k, (a0, a1, deg) in enumerate(lean):
        a1 = a0 + (a1 - a0) % 360
        am = (a0 + a1) / 2
        Ro = R + t / 2
        with B.at(x + Ro * math.cos(math.radians(am)), y + Ro * math.sin(math.radians(am)), -0.04, yaw=am, roll=deg):
            section(-Ro, 0.0, am, a0 - am, a1 - am, seed + 100 + 7 * k)
    if level is not None:
        def hidden(a):
            return any((a - a0) % 360 <= (a1 - a0) % 360 for a0, a1 in covered)
        sills = [hf(360.0 * i / seg) for i in range(seg) if not hidden(360.0 * i / seg)]
        if sills:
            level = min(level, min(sills) - jitter - 0.06)
    if floor and (pool is None or level is None or level > 0.9):
        # the floor shows only under a shallow pool or none
        with tinted(B, M["grit"], FLOOR):
            B.cyl(M["grit"], x, y, 0.0, R - t / 2 + 0.01, 0.06, seg=seg)
    if level is not None and pool is not None:
        B.cyl(pool, x, y, level - 0.06, R - t / 2 + 0.005, 0.06, seg=seg)
    return hf, level


def sill(hf, a0, a1, seg):
    """The lowest the wall stands between angles a0 and a1, at its joints."""
    return min(hf(360.0 * i / seg) for i in range(seg) if (360.0 * i / seg - a0) % 360 <= (a1 - a0) % 360)


def tongue(B, mat, x, y, R, t, level, low, a, w, reach, seed=0, k=4, slump=0.0):
    """Sludge pouring out through a crack at angle a: a narrow lumpy
    tongue from the pool over the crack's sill (low), w across there,
    down the wall's face and out onto the ground, thinning as it goes,
    shaded round. slump lays the fall down the face out that far, for a
    growth sagging over a rim rather than a pour."""
    rng = random.Random(seed)
    u = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0.0))
    v = Vector((-u.y, u.x, 0.0))
    o = R + t / 2 + 0.04
    # (out from the tank's middle, height, half width), pool to ground
    path = [(R - t / 2 - 0.45, level + 0.008, w * 0.5), (R - t / 2, level + 0.01, w * 0.5),
            (o, low + 0.05, w * 0.42), (o + 0.07 + slump * 0.5, low * 0.6, w * 0.32), (o + 0.12 + slump, 0.14, w * 0.27),
            (o + 0.3 + slump + reach * 0.35, 0.04, w * 0.33), (o + 0.3 + slump + reach * 0.75, 0.03, w * 0.26),
            (o + 0.3 + slump + reach, 0.025, w * 0.12)]
    top, nrm = [], []
    for i, (d, z, hw) in enumerate(path):
        p0 = path[max(i - 1, 0)]
        p1 = path[min(i + 1, len(path) - 1)]
        T = Vector((p1[0] - p0[0], p1[1] - p0[1])).normalized()
        N = Vector((-T.y, T.x))
        side = rng.uniform(-0.12, 0.12) * hw if i > 1 else 0.0
        for j in range(k + 1):
            s = -1 + 2 * j / k
            lump = rng.uniform(-0.02, 0.02) if (0 < j < k and i > 1) else 0.0
            hw2 = hw * (rng.uniform(0.8, 1.15) if i > 1 else 1.0)
            p = Vector((x, y, 0.0)) + u * (d + N.x * lump) + v * (s * hw2 + side)
            top.append(Vector((p.x, p.y, z + (1 - s * s) * 0.03 + N.y * lump)))
            nrm.append(u * N.x + Vector((0, 0, N.y)))
    n = len(path)
    V = [tuple(p) for p in top] + [tuple(p - q * 0.035) for p, q in zip(top, nrm)]
    m = n * (k + 1)
    F = []
    for i in range(n - 1):
        for j in range(k):
            q = (i * (k + 1) + j, i * (k + 1) + j + 1, (i + 1) * (k + 1) + j + 1, (i + 1) * (k + 1) + j)
            F.append(q)
            F.append(tuple(m + c for c in reversed(q)))
        for j in (0, k):
            F.append((i * (k + 1) + j, (i + 1) * (k + 1) + j, m + (i + 1) * (k + 1) + j, m + i * (k + 1) + j))
    F.append(tuple(range(k + 1)) + tuple(m + c for c in range(k, -1, -1)))
    e = (n - 1) * (k + 1)
    F.append(tuple(range(e, e + k + 1)) + tuple(m + c for c in range(e + k, e - 1, -1)))
    fs = B.solid(mat, V, F)
    for f in fs:
        f.smooth = True


def mound_disc(B, mat, x, y, R, z_mid, z_edge, seg=16, rings=3, seed=0, amp=0.06):
    """A lumpy round fill inside a tank, z_edge at its rim rising to z_mid."""
    rng = random.Random(seed)
    V, F = [], []
    for k in range(rings):
        f = k / rings
        rr = R * (1 - f)
        for i in range(seg):
            an = 2 * math.pi * i / seg
            z = z_edge + (z_mid - z_edge) * math.sin(f * math.pi / 2) + (rng.uniform(-amp, amp) if k else 0.0)
            V.append((x + rr * math.cos(an), y + rr * math.sin(an), z))
    V.append((x, y, z_mid + rng.uniform(-amp, amp)))
    for k in range(rings - 1):
        for i in range(seg):
            j = (i + 1) % seg
            F.append((k * seg + i, k * seg + j, (k + 1) * seg + j, (k + 1) * seg + i))
    c = len(V) - 1
    for i in range(seg):
        F.append(((rings - 1) * seg + i, (rings - 1) * seg + (i + 1) % seg, c))
    B.mesh(mat, V, F)


def fill(B, mat, x, y, R, z0, hf=None, tilt=(0.0, 0.0), amp=0.06, seg=24, rings=3, seed=0, holes=0.0,
         hole_scale=1.3, pours=(), plugs=(), margin=0.12, slope=0.45, rag=0.0):
    """What is left in a tank: a lumpy surface z0 high, tilted by tilt
    (rise a cell east, north), out to radius R. Where the wall hf is
    broken low it sinks under the crack, sloping up inward by slope, but
    over a pour (a0, a1) it runs to the sill and in a plug (a heap fills
    the gap) it keeps its height. holes drops that share of its facets
    where a noise runs high, and rag frays its rim inward, for a growth
    and not a liquid."""
    rng = random.Random(seed)
    off = Vector((seed % 89 * 1.31, seed % 83 * 0.77, 0.0))

    def wall(a):
        if hf is None or any(in_arc(a, a0, a1) for a0, a1 in plugs):
            return None
        lo = min(hf(a + d) for d in (-9, -4.5, 0, 4.5, 9))
        return lo + (0.04 if any(in_arc(a, a0, a1) for a0, a1 in pours) else -margin)
    V, F = [], []
    for k in range(rings):
        f = k / rings
        for i in range(seg):
            a = 360.0 * i / seg
            rr = R * (1 - f) * (1 - (rng.uniform(0, rag) if k == 0 else 0.0))
            px_, py_ = x + rr * math.cos(math.radians(a)), y + rr * math.sin(math.radians(a))
            z = z0 + tilt[0] * (px_ - x) + tilt[1] * (py_ - y) + (rng.uniform(-amp, amp) if k else amp * 0.3)
            w = wall(a)
            if w is not None:
                z = min(z, w + slope * (R - rr))
            V.append((px_, py_, z))
    z = z0 + rng.uniform(-amp, amp)
    V.append((x, y, z))

    def keep(i, k):
        if holes <= 0.0:
            return True
        a = math.radians(360.0 * (i + 0.5) / seg)
        rr = R * (1 - (k + 0.5) / rings)
        v = noise.noise(Vector((x + rr * math.cos(a), y + rr * math.sin(a), 0.0)) * hole_scale + off)
        return v < 0.6 - 1.6 * holes
    for k in range(rings - 1):
        for i in range(seg):
            j = (i + 1) % seg
            if keep(i, k):
                F.append((k * seg + i, k * seg + j, (k + 1) * seg + j, (k + 1) * seg + i))
    c = len(V) - 1
    for i in range(seg):
        if keep(i, rings - 1):
            F.append(((rings - 1) * seg + i, (rings - 1) * seg + (i + 1) % seg, c))
    fs = B.mesh(mat, V, F)
    for f_ in fs:
        f_.smooth = True
    return fs


def pour(B, mat, x, y, R, t, level, low, a, w, reach, spread=1.6, seed=0, k=6, dz=0.0, rag=0.0):
    """Liquor bursting out through a wide breach at angle a: a sheet from
    the pool over the breach's sill (low), w across there, down the wall's
    face and fanning out over the ground to spread times as wide, its
    front edge ragged. rag frays its two sides too, each on its own, from
    the wall's foot out, so the sheet spreads unevenly."""
    rng = random.Random(seed)
    u = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0.0))
    v = Vector((-u.y, u.x, 0.0))
    o = R + t / 2 + 0.04
    path = [(R - t / 2 - 0.5, level + 0.008, 0.5), (R - t / 2, level + 0.01, 0.5), (o, low + 0.05, 0.5),
            (o + 0.08, low * 0.55, 0.52), (o + 0.16, 0.1, 0.56), (o + 0.25 + reach * 0.3, 0.035, 0.5 * (1 + spread) / 2),
            (o + 0.25 + reach * 0.7, 0.03, 0.5 * spread), (o + 0.25 + reach, 0.025, 0.42 * spread)]
    top = []
    if rag > 0.0:
        ph = [rng.uniform(0.0, 2 * math.pi) for _ in range(3)]
    for i, (d, z, hw) in enumerate(path):
        if rag > 0.0:
            # each side its own way, in soft lobes: widening down the wall's
            # face, pinched or bulging further out
            f = 0.0 if i <= 2 else (0.5 if i <= 4 else 1.0)
            grow = (1.0, 1.0, 1.0, 1.12, 1.3)[i] if i < 5 else 1.0
            nl = math.sin(i * 1.9 + ph[0]) * 0.6 + rng.uniform(-0.3, 0.3)
            nr = math.sin(i * 1.4 + ph[1]) * 0.6 + rng.uniform(-0.3, 0.3)
            sides = (hw * w * grow * (1 + rag * f * nl), hw * w * grow * (1 + rag * f * nr))
        for j in range(k + 1):
            s = -1 + 2 * j / k
            e = d
            if i >= 5 and rag > 0.0:
                # the front of the fan in lobes, longer in its middle
                e = d + (0.2 * reach * math.sin(j * 1.3 + ph[2]) + 0.15 * reach * (1 - s * s) +
                         rng.uniform(-0.05, 0.05) * reach) * (i - 4) / 3
            elif i >= 5:
                # the front of the fan: ragged, longer in its middle
                e = d + (rng.uniform(-0.25, 0.2) * reach + 0.15 * reach * (1 - s * s)) * (i - 4) / 3
            if rag > 0.0:
                side = -sides[0] + (sides[0] + sides[1]) * j / k
                if 0 < j < k and i > 2:
                    side += rng.uniform(-0.06, 0.06)
                p = Vector((x, y, 0.0)) + u * e + v * side
            else:
                hw2 = hw * w * (rng.uniform(0.85, 1.12) if i > 2 else 1.0)
                p = Vector((x, y, 0.0)) + u * e + v * (s * hw2)
            zz = z + (rng.uniform(-0.008, 0.012) if i > 3 else 0.0) + (dz * (1 - s * s) if i <= 2 else 0.0)
            top.append((p.x, p.y, zz))
    F = []
    n = len(path)
    for i in range(n - 1):
        for j in range(k):
            F.append((i * (k + 1) + j, (i + 1) * (k + 1) + j, (i + 1) * (k + 1) + j + 1, i * (k + 1) + j + 1))
    fs = B.mesh(mat, top, F)
    for f_ in fs:
        f_.smooth = True
    return fs


def overflow(B, mats, x, y, R, t, hf, a0, a1, z_in, reach, bulge=0.35, step=6.0, seed=0, lift=0.05):
    """A growth that has come up over a broken rim and down the outside:
    a mat from z_in inside the tank over the wall's stubs (hf), bulging
    out past them and slumping to the ground reach further out, its edge
    torn, between angles a0 and a1. reach may be fn(angle). mats is the
    dark mat under it and a growth cut by its alpha laid lift over it,
    so the mat shows through its gaps. The arc's ends draw in."""
    rng = random.Random(seed)
    a1 = a0 + (a1 - a0) % 360
    n = max(2, int(math.ceil((a1 - a0) / step)))
    rows = []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        end = math.sin(math.pi * i / n) ** 0.5
        s = max(hf(a + d) for d in (-5, 0, 5)) + 0.14
        rc = (reach(a) if callable(reach) else reach) * rng.uniform(0.75, 1.15) * (0.3 + 0.7 * end)
        b = bulge * (0.4 + 0.6 * end)
        o = R + t / 2
        prof = [(R - t / 2 - 0.6, z_in), (R - t / 2 - 0.1, max(z_in, s) + 0.04), (o + 0.06, s + 0.02),
                (o + b, s * 0.8 + 0.05), (o + b + rc * 0.35, s * 0.42 + 0.04), (o + b + rc * 0.7, 0.1),
                (o + b + rc, 0.02)]
        c, sn = math.cos(math.radians(a)), math.sin(math.radians(a))
        row = []
        for j, (rr, z) in enumerate(prof):
            if 2 < j < len(prof) - 1:
                rr += rng.uniform(-0.08, 0.08)
                z += rng.uniform(-0.07, 0.07)
            elif j == len(prof) - 1:
                rr += rng.uniform(-0.2, 0.15) * rc
            row.append((x + rr * c, y + rr * sn, max(0.02, z)))
        rows.append(row)
    k = len(rows[0])
    F = [(i * k + j, i * k + j + 1, (i + 1) * k + j + 1, (i + 1) * k + j) for i in range(n) for j in range(k - 1)]
    V = [p for row in rows for p in row]
    for m, dz in zip(mats, (0.0, lift)):
        fs = B.mesh(m, [(p[0], p[1], p[2] + dz * (1.0 if p[2] > 0.03 else 0.3)) for p in V], F)
        for f_ in fs:
            f_.smooth = True
    return rows


def rim_moss(B, mat, x, y, R, t, hf, angles, rng, size=(0.4, 0.6)):
    """Clumps of a growth lying over a wall's broken top at each angle,
    hanging a little over its outer face."""
    for a in angles:
        h = hf(a)
        c, sn = math.cos(math.radians(a)), math.sin(math.radians(a))
        s_ = rng.uniform(*size)
        rr = R + t / 2 + rng.uniform(-0.05, 0.1)
        A.lump(B, mat, x + rr * c, y + rr * sn, h - s_ * 0.2, s_ * rng.uniform(1.0, 1.4), s_ * rng.uniform(0.9, 1.2),
               s_ * rng.uniform(0.45, 0.6), seed=rng.randint(0, 9999))


def dome_patch(B, mat, Rd, Hd, a0, a1, t0, t1, na=2, nt=2, th=0.07, rag=0.0, seed=0, centre=False):
    """A curved patch of a dome's shell, Rd across and Hd high, between
    azimuths a0 and a1 (degrees) and rises t0 to t1 (fractions of the way
    up), th thick, its top edge ragged by rag. It is placed about the
    dome's own middle or, with centre, about the patch's. Returns the
    points down its a0 edge, for a rib."""
    rng = random.Random(seed)

    def S(a, t, off):
        e = max(0.0, min(1.0, t)) * math.pi / 2
        rr = (Rd - off) * math.cos(e)
        return Vector((rr * math.cos(math.radians(a)), rr * math.sin(math.radians(a)), (Hd - off) * math.sin(e)))
    jag = [[(rng.uniform(-rag, rag) if j == nt else 0.0) for j in range(nt + 1)] for i in range(na + 1)]
    outer = [[S(a0 + (a1 - a0) * i / na, t0 + (t1 - t0) * j / nt + jag[i][j], 0.0) for j in range(nt + 1)]
             for i in range(na + 1)]
    inner = [[S(a0 + (a1 - a0) * i / na, t0 + (t1 - t0) * j / nt + jag[i][j], th) for j in range(nt + 1)]
             for i in range(na + 1)]
    c = Vector((0, 0, 0))
    if centre:
        c = sum((p for row in outer for p in row), Vector((0, 0, 0))) / ((na + 1) * (nt + 1))
    N = (na + 1) * (nt + 1)

    def ix(i, j, s):
        return s * N + i * (nt + 1) + j
    V = [tuple(p - c) for row in outer for p in row] + [tuple(p - c) for row in inner for p in row]
    F = []
    for i in range(na):
        for j in range(nt):
            F.append((ix(i, j, 0), ix(i + 1, j, 0), ix(i + 1, j + 1, 0), ix(i, j + 1, 0)))
            F.append((ix(i, j + 1, 1), ix(i + 1, j + 1, 1), ix(i + 1, j, 1), ix(i, j, 1)))
    for i in range(na):
        for j in (0, nt):
            F.append((ix(i, j, 0), ix(i + 1, j, 0), ix(i + 1, j, 1), ix(i, j, 1)))
    for j in range(nt):
        for i in (0, na):
            F.append((ix(i, j, 0), ix(i, j + 1, 0), ix(i, j + 1, 1), ix(i, j, 1)))
    B.solid(mat, V, F)
    return [tuple(outer[0][j] - c) for j in range(nt + 1)]


def dome_shard(B, M, pos, Rd, Hd, span, t0, t1, yaw=0.0, roll=-45.0, pitch=0.0, seed=0):
    """A piece of a fallen dome: a patch of its shell (span degrees round,
    rises t0 to t1), boards in the stave material, a copper rib down one
    side, set with its middle at pos facing out along yaw (degrees), laid
    flat at roll -45, standing at 45, its outer end lifted below -45."""
    with B.at(pos[0], pos[1], pos[2], yaw=yaw, pitch=pitch, roll=roll):
        edge = dome_patch(B, M["stave"], Rd, Hd, -span / 2, span / 2, t0, t1, th=0.07, rag=0.06, seed=seed,
                          centre=True)
        tube(B, M["band"], [Vector(p) + Vector((0, 0, 0.03)) for p in edge], 0.045, seg=4)


def breach_heap(B, M, x, y, R, level, a0, a1, reach=1.0, seed=0, seg=7, inner=0.5, beams=9):
    """Where a tank's wall burst: a heap of its staves and grit from inside
    (inner of the way out) over the broken wall out onto the ground,
    standing above the pool all across the breach so the pool's edge stays
    under it."""
    rng = random.Random(seed)
    prof = [(R * inner, level + 0.1), (R - 0.2, level + 0.16), (R + 0.1, max(level * 0.8, 0.3)), (R + 0.5 * reach, 0.28),
            (R + reach, 0.0)]
    V, F = [], []
    k = len(prof)
    for i in range(seg + 1):
        a = math.radians(a0 + (a1 - a0) * i / seg)
        w = math.sin(math.pi * i / seg) ** 0.6
        for j, (rr, z) in enumerate(prof):
            if j < 2:
                # inside the wall it holds the pool's edge the whole way across
                rr2, zz = rr, z + rng.uniform(-0.03, 0.05)
            else:
                rr2 = R + (rr - R) * (0.4 + 0.6 * w)
                zz = z * (0.3 + 0.7 * w) + (rng.uniform(-0.06, 0.06) if j < k - 1 else 0.0)
            V.append((x + rr2 * math.cos(a), y + rr2 * math.sin(a), max(0.0, zz)))
    for i in range(seg):
        for j in range(k - 1):
            F.append((i * k + j, (i + 1) * k + j, (i + 1) * k + j + 1, i * k + j + 1))
    B.mesh(M["grit"], V, F)
    for _ in range(beams):
        i, j = rng.randint(1, seg - 1), rng.randint(0, k - 2)
        p = Vector(V[i * k + j]) + Vector((0, 0, 0.04))
        an = rng.uniform(0, math.pi)
        d = Vector((math.cos(an), math.sin(an), rng.uniform(-0.2, 0.2))) * rng.uniform(0.25, 0.5)
        B.beam(rng.choice(M["char"] + [M["stave"]]), tuple(p - d), tuple(p + d), rng.uniform(0.1, 0.16))


def pool_on_ground(B, mat, r, pic, poly, seed, top=0.05):
    """A spill lying on the ground where the picture draws it."""
    return A.heap(B, mat, r, A.jag(poly, 1.5, 4.0, seed), top, z_at=0.05, cell=0.3, noise=0.015, seed=seed, edge=0.5,
                  pic=pic, grow=0, strict=True)


def shards(B, M, rng, x, y, R, n, z=0.1, spread=0.8, kinds=("stave", "band")):
    """Pieces of a burst tank lying round it: stave panels and band strips."""
    for _ in range(n):
        a = rng.uniform(0, 2 * math.pi)
        rr = R + rng.uniform(0.1, spread)
        px_, py_ = x + rr * math.cos(a), y + rr * math.sin(a)
        k = rng.choice(kinds)
        if k == "band":
            L = rng.uniform(0.5, 1.1)
            with B.at(px_, py_, z * rng.uniform(0.3, 1.0), yaw=math.degrees(a) + 90 + rng.uniform(-40, 40),
                      pitch=rng.uniform(-10, 10)):
                B.box(M["band"], 0, 0, 0, L, 0.06, 0.1)
        else:
            nrm = (rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), 1.0)
            shard(B, M["stave"], (px_, py_, z * rng.uniform(0.3, 1.0) + 0.06), nrm, rng.uniform(0.4, 0.8),
                  rng.uniform(0.3, 0.6), 0.07, rng)


def pipe_px(B, mat, r, pts, rad, seg=8):
    """A pipe through points traced on the picture, each (col, row, z) or
    (col, row, z, lift): the plan point drawn there at height z, lifted."""
    P3 = [P(r, p[0], p[1], p[2]) + (p[2] + (p[3] if len(p) > 3 else 0.0),) for p in pts]
    tube(B, mat, P3, rad, seg=seg, smooth=4, soft=True)
    return P3


def invent_bed(B, M, r, pic, seed, polys, n, avoid=(), keep=None, thr=0.5, kinds=None, step=0.5):
    """The speckled bed of grit and ash lying flat where the picture draws
    it inside the outlines, ground showing between, kept off the tanks
    (avoid, circles (x, y, R)), and pieces strewn over it on the ground."""
    for i, poly in enumerate(polys):
        speck_bed(B, M["bed"], r, pic, poly, z=0.02, step=step, size=(0.34, 0.52), thr=thr, rad=2, avoid=avoid,
                  seed=seed + i, keep=keep)

    def ground(x, y):
        if any(math.hypot(x - ax, y - ay) < aR + 0.1 for ax, ay, aR in avoid):
            return -100.0
        if keep is not None and not keep(x, y):
            return -100.0
        return 0.0
    mats = {"beam": M["char"], "board": M["char"], "stone": M["gravel"], "chunk": M["gravel"] + [M["grey"], M["iron"]],
            "slab": [M["stave"], M["band"]]}
    for i, poly in enumerate(polys):
        A.cover(B, mats, r, poly_plan(r, poly, 0.05), ground, n // len(polys),
                kinds or {"beam": 2, "stone": 3, "chunk": 3, "slab": 2}, pic=pic, seed=seed + 20 + i,
                length=(0.4, 1.0), width=(0.08, 0.14), stone=(0.18, 0.4), chunk=(0.12, 0.28), size=(0.3, 0.6),
                lift=0.02, tilt=12, grow=1)


def invent_soot(seed):
    base = noise_soot(seed, amt=0.55, scale=0.9)

    def f(co):
        return base(co) * (1.0 - 0.25 * min(1.0, max(0.0, co.z / 3.0)))
    return f


@model("CreInvent01a", scale=2)
def invent01a(B, r, pic, seed):
    """The ruined distillery. The still's round wall of staves and copper
    bands has burst on its east third: from the north-east round to the
    south-east only stubs stand, the staves splayed outward off them and
    lying broken on the ground, and a heap of charred staves and grit in
    the breach holds in the teal liquor. Its front-left is split and
    heaped over too, and the condenser inside is snapped to a charred
    stub. Banded coils hang snapped and sagging round its west flank, and
    the great grey pipe that arched over to its back is broken at the top,
    its end hanging short of the still. The little basin in front is a
    broken C open to the north-east, a banded coil drum lies on its side
    to the east, and staves and band strips lie on speckled grit."""
    rng = random.Random(seed)
    M = invent_mats("i1", seed)
    teal = liquid("i1_teal", "#1e3e36", seed, fleck="#2e5a48", fleck_amt=0.05, var=0.3)
    # the still, its rim read off the drawing: centre (55, 37) at 2.0 up, 27 px round
    H = 2.0
    cx, cy = P(r, 55, 37, H)
    R = 1.7
    heights = [(52, 2.25), (60, 2.3), (76, 2.5), (90, 2.3), (104, 2.55), (118, 2.5), (134, 2.45), (150, 2.0),
               (185, 1.5), (215, 1.0), (240, 0.45), (262, 0.6), (285, 1.25), (299, 1.7),
               (300, 0.55), (312, 0.32), (326, 0.72), (338, 0.4), (352, 0.78), (6, 0.45), (18, 0.3), (30, 0.62),
               (42, 0.36), (51, 0.5)]
    _, level = tank(B, M, cx, cy, R, heights, t=0.17, rim=1.65, level=0.8, pool=teal, seed=seed,
                    covered=[(205, 292), (300, 51)])
    breach_heap(B, M, cx, cy, R, level, 205, 292, reach=1.1, seed=seed + 9)
    with tinted(B, M["grit"], (0.42, 0.37, 0.34)):
        breach_heap(B, M, cx, cy, R, level, 300, 411, reach=0.6, seed=seed + 13, seg=10, inner=0.74)
    # the burst third's staves, splayed outward off their stubs and lying
    # broken on the ground, some with a strip of band still on
    for k in range(9):
        a = 302 + 108 * (k + rng.uniform(0.15, 0.85)) / 9
        ar = math.radians(a)
        L = rng.uniform(0.8, 1.6)
        lying = k % 3 != 1
        rr = R + 0.1 + (rng.uniform(0.15, 0.6) if lying else 0.0)
        with B.at(cx + rr * math.cos(ar), cy + rr * math.sin(ar), 0.04 if lying else 0.0, yaw=a + rng.uniform(-28, 28),
                  roll=rng.uniform(80, 88) if lying else rng.uniform(48, 64)):
            wd = rng.uniform(0.18, 0.28)
            B.box(M["stave"], 0, 0, 0, 0.07, wd, L)
            if rng.random() < 0.45:
                B.box(M["band"], -0.05, 0, L * rng.uniform(0.3, 0.6), 0.03, wd + 0.04, 0.1)
    for k in range(4):
        shard(B, M["stave"], (cx + (R * 0.3) * math.cos(k * 1.6), cy + (R * 0.3) * math.sin(k * 1.6), level + 0.02),
              (rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 1.0), rng.uniform(0.4, 0.7), rng.uniform(0.25, 0.4),
              0.05, rng)
    # the condenser inside, snapped off to a charred stub with a jagged top
    with B.at(cx + 0.35, cy - 0.25, 0.0, pitch=-6, roll=5):
        B.cyl(M["iron"], 0, 0, 0.0, 0.34, 1.0, seg=9)
        for k in range(5):
            a = 2 * math.pi * k / 5 + rng.uniform(-0.3, 0.3)
            h = rng.uniform(0.1, 0.28)
            w0 = rng.uniform(0.16, 0.24)
            with B.at(0.28 * math.cos(a), 0.28 * math.sin(a), 0.97, yaw=math.degrees(a) + 90,
                      pitch=rng.uniform(-10, 10)):
                B.solid(M["iron"], [(-w0 / 2, -0.035, 0), (w0 / 2, -0.035, 0), (w0 / 2, 0.035, 0), (-w0 / 2, 0.035, 0),
                                    (rng.uniform(-0.03, 0.03), 0.0, h)],
                        [(0, 3, 2, 1), (0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)])
    # the great grey pipe rose off the ground west of the still and arched
    # over to its back. It is snapped at the top, its broken end hanging
    # short of the still, and a stub of it juts from the still's back wall
    gp = pipe_px(B, M["grey"], r, [(13, 40, 0.2), (10, 30, 0.8), (6, 18, 1.6, 0.35), (8, 7, 2.3, 0.75),
                                   (14, 3, 2.4, 0.75), (19, 5, 2.2, 0.4)], [0.19, 0.2, 0.21, 0.21, 0.21, 0.2])
    collars(B, M["iron"], gp, 0.26, every=0.45, w=0.12, seg=6, smooth=4)
    a = math.radians(142)
    out = Vector((math.cos(a), math.sin(a), 0.0))
    s0 = Vector((cx, cy, 1.75)) + out * (R + 0.05)
    stub = [tuple(s0 - out * 0.15), tuple(s0 + out * 0.35 + Vector((0, 0, 0.04))),
            tuple(s0 + out * 0.6 + Vector((0, 0, -0.3)))]
    tube(B, M["grey"], stub, 0.19, seg=8, smooth=2, soft=True)
    collars(B, M["iron"], stub, 0.25, every=0.45, w=0.12, seg=6, smooth=2)
    # the coiled pipes round the still's west flank, banded with collars
    # like stacked rings: a coil sprung loose lying along the flank, and two
    # loops still round it, sagging, one snapped end hanging to the grit
    body = (0.5, 0.48, 0.56)
    a = math.radians(188)
    rad, tng = Vector((math.cos(a), math.sin(a), 0.0)), Vector((-math.sin(a), math.cos(a), 0.0))
    c = Vector((cx, cy, 0.46)) + rad * (R + 0.62)
    axis = (tng + Vector((0, 0, -0.2))).normalized()
    side = axis.cross(Vector((0, 0, 1))).normalized()
    up = side.cross(axis)
    turns, n_t = 2.5, 5
    pts = []
    for i in range(int(turns * n_t) + 1):
        f = i / (turns * n_t)
        an = 2 * math.pi * turns * f
        pts.append(tuple(c + axis * (1.6 * (f - 0.5)) + (side * math.cos(an) + up * math.sin(an)) * 0.33))
    with tinted(B, M["pipe"], body):
        tube(B, M["pipe"], pts, 0.15, seg=6, smooth=2, soft=True)
    collars(B, M["pipe"], pts, 0.195, every=0.4, w=0.13, seg=6, smooth=2)
    for k, (rr, a0, a1, z0, z1, sag, hang) in enumerate(((R + 0.3, 148, 262, 1.4, 1.25, 0.12, True),
                                                         (R + 0.32, 150, 204, 1.07, 1.05, 0.02, False))):
        n = max(3, int((a1 - a0) / 14))
        pts = []
        for i in range(n + 1):
            f = i / n
            a = math.radians(a0 + (a1 - a0) * f)
            pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a),
                        z0 + (z1 - z0) * f - sag * math.sin(math.pi * f) + 0.04 * math.sin(7 * f + k)))
        if hang:
            # the snapped end swings out and down to the ground
            ex, ey, ez = pts[-1]
            a = math.radians(a1)
            out = Vector((math.cos(a), math.sin(a), 0.0))
            tng = Vector((-math.sin(a), math.cos(a), 0.0))
            p1 = Vector((ex, ey, ez)) + tng * 0.3 + out * 0.15 + Vector((0, 0, -ez * 0.45))
            p2 = Vector((ex, ey, 0.0)) + tng * 0.42 + out * 0.4 + Vector((0, 0, 0.14))
            pts += [tuple(p1), tuple(p2)]
        with tinted(B, M["pipe"], body):
            tube(B, M["pipe"], pts, 0.15, seg=6, smooth=2, soft=True)
        collars(B, M["pipe"], pts, 0.195, every=0.4, w=0.13, seg=6, smooth=2)
    # the green pipe off the east side, down to the drum
    pipe_px(B, M["green"], r, [(82, 58, 1.2), (92, 62, 1.1), (101, 70, 0.9), (104, 80, 0.7)], 0.14)
    # a coil drum lying on its side to the east, banded, its west end open
    # on the coil wound inside
    sx, sy = P(r, 112, 90, 0.55)
    with B.at(sx, sy, 0.55, yaw=40):
        with B.at(0, 0, 0, roll=90):
            B.cyl(M["stave"], 0, 0, -0.55, 0.55, 1.1, seg=12)
            for zb in (-0.42, 0.0, 0.36):
                B.cyl(M["band"], 0, 0, zb - 0.05, 0.585, 0.1, seg=12, cap=False)
            B.cyl(M["iron"], 0, 0, 0.55, 0.5, 0.02, seg=12)
        for rc in (0.4, 0.24):
            ring = [(-0.58, rc * math.cos(2 * math.pi * i / 8), rc * math.sin(2 * math.pi * i / 8)) for i in range(9)]
            tube(B, M["pipe"], ring, 0.06, seg=4, caps=False)
    # the basin in front, broken to a C open to the north-east with grit in
    # it, the staves of its open side dropped outside
    bx, by = P(r, 76, 110, 0.3)
    tank(B, M, bx, by, 0.9, [(0, 0.45), (12, 0.55), (13, 0.0), (100, 0.0), (101, 0.6), (110, 0.5), (170, 0.2),
                             (230, 0.42), (300, 0.15)], t=0.14, bands=(0.45,), band_h=0.08, seg=16, seed=seed + 2)
    # grey grit speckled over its floor and spilling out of the open side
    bgrit = speck_mat("i1_bgrit", [("#6e6a60", 0.3), ("#4e4a42", 0.4), ("#2a2622", 0.3)], cover=0.7, grain=0.07, uv=1.5,
                      seed=seed + 11)

    def in_basin(x, y):
        d = math.hypot(x - bx, y - by)
        return d < 0.8 or (d < 1.35 and in_arc(math.degrees(math.atan2(y - by, x - bx)), 15, 98))
    speck_bed(B, bgrit, r, pic, [(56, 92), (100, 92), (100, 126), (56, 126)], z=0.08, step=0.28, size=(0.18, 0.3),
              thr=0.35, rad=1, seed=seed + 12, keep=in_basin)
    for k in range(3):
        a = math.radians(rng.uniform(30, 85))
        A.lump(B, M["grit"], bx + 0.95 * math.cos(a), by + 0.95 * math.sin(a), 0.0, rng.uniform(0.25, 0.4),
               rng.uniform(0.2, 0.3), rng.uniform(0.1, 0.16), seed=seed + 60 + k)
    for k in range(4):
        a = math.radians(25 + 70 * (k + rng.uniform(0.2, 0.8)) / 4)
        rr = 0.95 + rng.uniform(0.15, 0.5)
        with B.at(bx + rr * math.cos(a), by + rr * math.sin(a), 0.035, yaw=math.degrees(a) + rng.uniform(-40, 40),
                  roll=rng.uniform(80, 88)):
            B.box(M["stave"], 0, 0, 0, 0.06, rng.uniform(0.16, 0.24), rng.uniform(0.35, 0.6))
    # a fallen stave beam off to the north-east and a broken box
    B.beam(M["char"][0], P(r, 82, 17, 0.1) + (0.12,), P(r, 106, 34, 0.1) + (0.12,), 0.24)
    with B.at(*P(r, 108, 50, 0.25), 0.0, yaw=-25, pitch=10):
        B.box(M["iron"], 0, 0, 0, 0.75, 0.45, 0.4)
    shards(B, M, rng, cx, cy, R, 6, spread=1.2)
    # speckled grit where the picture draws it, in front and to the west,
    # none behind the still
    invent_bed(B, M, r, pic, seed + 4, [[(0, 0), (122, 0), (122, 126), (0, 126)]], 28,
               avoid=[(cx, cy, R), (sx, sy, 0.65), (bx, by, 0.85)],
               keep=lambda x, y: y < cy - 0.3 or x < cx - R)
    return invent_soot(seed)


def plug(B, M, x, y, R, a0, a1, top, seed=0, reach=0.7):
    """Rubble slumped into a crack in a tank's wall: a dark heap of its
    charred staves and grit across the gap, top high at the wall line,
    holding back what is left inside."""
    with tinted(B, M["grit"], (0.4, 0.36, 0.33)):
        breach_heap(B, M, x, y, R, top - 0.1, a0 - 5, a1 + 5, reach=reach, seed=seed, seg=3, inner=1 - 0.5 / R, beams=3)


def sunk(B, M, rng, x, y, R, zf, n, kinds=("beam", "beam", "stave")):
    """Charred timbers and stave panels half sunk in a tank's sludge,
    lying every way, zf(x, y) the surface under each."""
    for _ in range(n):
        a = rng.uniform(0, 2 * math.pi)
        rr = R * math.sqrt(rng.uniform(0.02, 0.6))
        px_, py_ = x + rr * math.cos(a), y + rr * math.sin(a)
        z = zf(px_, py_)
        k = rng.choice(kinds)
        if k == "beam":
            L = rng.uniform(0.6, 1.3)
            yaw = rng.uniform(0, 2 * math.pi)
            d = Vector((math.cos(yaw), math.sin(yaw), math.tan(math.radians(rng.uniform(-22, 22))))) * (L / 2)
            c = Vector((px_, py_, z - 0.03))
            B.beam(rng.choice(M["char"]), tuple(c - d), tuple(c + d), rng.uniform(0.12, 0.18))
        else:
            shard(B, M["stave"], (px_, py_, z + 0.02), (rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), 1.0),
                  rng.uniform(0.35, 0.6), rng.uniform(0.25, 0.4), 0.05, rng)


@model("CreInvent02a", scale=2)
def invent02a(B, r, pic, seed):
    """The destroyed extractor. Its two big tanks have lost their domed
    roofs and their walls stand cracked into sections. The west tank's
    east wall is split off from the rest, a piece at the north-west stands
    on its own, its west arc has split away and leans outward, and its
    front is broken down to a stub, the yellow-green sludge pouring through
    a V-crack at the front-left onto the ground. The east tank is broken
    open on its north and east, its green sludge spilling out east. The
    sludge has settled low and tilted, with pieces of the domes and
    charred staves sunk in it, and rubble chokes the cracks. A gravel tank
    and a silo stand behind. In front the condenser tower has lost its
    cone, which lies on the ground beside it, the next ring is broken open
    with its rubble spilling out, and the blue-water tank is broken open
    at the front under the half dome still over its back. Grey machinery
    and pipe pieces lie knocked about between the tanks, charred staves
    along the front, all on speckled grit."""
    rng = random.Random(seed)
    M = invent_mats("i2", seed)
    yellow = liquid("i2_yel", "#4e4e14", seed, rough=0.8, fleck="#6a2a14", fleck_amt=0.07, fleck_px=4,
                    streak="#2a2a16", streak_amt=1.0, var=0.3)
    green = liquid("i2_grn", "#34461a", seed + 1, rough=0.8, fleck="#4e6024", fleck_amt=0.06, streak="#1a2212",
                   streak_amt=1.0, var=0.35)
    blue = liquid("i2_blu", "#20404e", seed + 2, rough=0.6, fleck="#3a6a74", fleck_amt=0.04, var=0.25)
    # the west tank: rim centre (49, 61) at 2.9 up. Gaps at the north-east,
    # the north-west and the south-east, the west arc leaning out, the
    # front a stub and the V-crack at the front-left
    ax, ay, RA = -2.75, -0.44, 2.56
    hA = [(0, 2.75), (18, 2.35), (36, 2.85), (51, 2.55), (52, 0.0), (66, 0.0), (67, 2.65), (84, 2.15), (100, 2.85),
          (114, 2.5), (127, 2.8), (128, 0.0), (140, 0.0), (141, 2.45), (158, 2.75), (176, 2.35), (194, 2.7),
          (209, 2.1), (216, 1.3), (226, 0.5), (236, 0.85), (244, 1.05), (258, 0.7), (272, 1.15), (286, 0.8),
          (299, 1.1), (300, 0.0), (310, 0.0), (311, 2.4), (330, 2.8)]
    hfA, _ = tank(B, M, ax, ay, RA, hA, t=0.2, rim=2.15, seed=seed, seg=24, floor=False, lean=[(141, 209, 11)],
                  step=0.38)
    tA = (0.05, 0.06)

    def zA(x, y):
        return 0.85 + tA[0] * (x - ax) + tA[1] * (y - ay)
    fill(B, yellow, ax, ay, RA - 0.11, 0.85, hf=hfA, tilt=tA, amp=0.07, seed=seed + 3, pours=[(214, 238)],
         plugs=[(46, 72), (122, 146), (141, 209), (294, 316)])
    for k, (a0, a1) in enumerate(((52, 66), (128, 140), (300, 310))):
        plug(B, M, ax, ay, RA, a0, a1, 1.05, seed=seed + 70 + k)
    tongue(B, yellow, ax, ay, RA, 0.2, 0.6, sill(hfA, 216, 236, 24), 226, 0.8, 1.5, seed=seed + 1)
    pool_on_ground(B, yellow, r, pic, [(10, 98), (60, 104), (78, 128), (60, 146), (20, 138), (8, 115)], seed + 1)
    # the east tank: rim centre (141, 72) at 2.4 up, broken open on its
    # north and east, the green sludge spilling out east
    bx, by, RB = 3.03, -0.9, 2.1
    hB = [(0, 0.45), (12, 0.6), (21, 0.5), (22, 1.9), (40, 2.25), (60, 2.4), (79, 2.0), (80, 0.0), (96, 0.0),
          (97, 2.1), (118, 1.75), (132, 2.3), (175, 2.2), (210, 1.9), (250, 2.3), (283, 2.05), (290, 1.15),
          (306, 1.35), (322, 0.95), (338, 1.05), (348, 0.55)]
    hfB, _ = tank(B, M, bx, by, RB, hB, t=0.2, rim=1.8, seed=seed + 1, seg=24, floor=False, step=0.38)
    tB = (-0.05, 0.05)

    def zB(x, y):
        return 1.0 + tB[0] * (x - bx) + tB[1] * (y - by)
    fill(B, green, bx, by, RB - 0.11, 1.0, hf=hfB, tilt=tB, amp=0.07, seed=seed + 4, pours=[(345, 22)],
         plugs=[(74, 102)])
    plug(B, M, bx, by, RB, 80, 96, 1.15, seed=seed + 75)
    tongue(B, green, bx, by, RB, 0.2, 0.62, sill(hfB, 350, 15, 24), 2, 0.95, 1.1, seed=seed + 2)
    pool_on_ground(B, green, r, pic, [(172, 62), (194, 70), (196, 104), (182, 114), (170, 100)], seed + 2)
    # behind: the small tank, gravel in it, and the tall silo, torn at its top
    tank(B, M, 2.1, 2.3, 1.15, [(0, 1.6), (90, 1.5), (180, 1.2), (270, 1.6)], t=0.14, bands=(0.45,), seg=14,
         seed=seed + 2)
    A.heap(B, M["grit"], r, [(110, 14), (142, 14), (142, 40), (110, 40)], 0.9, z_at=1.3, cell=0.45, noise=0.12,
           seed=seed + 3, edge=0.8)
    tank(B, M, 4.6, 1.5, 0.72, [(0, 3.0), (90, 3.3), (150, 2.6), (220, 3.1), (300, 2.8)], t=0.12, bands=(0.55,),
         seg=14, seed=seed + 4)
    # in front, left: the condenser tower on its ring, cracked and leaning a
    # little, open at the top where its cone was knocked off
    rx, ry = 0.3, -4.0
    tank(B, M, rx, ry, 0.9, [(0, 0.55), (40, 0.62), (100, 0.5), (160, 0.6), (200, 0.45), (215, 0.12), (232, 0.45),
                             (300, 0.55)], t=0.12, bands=(0.5,), band_h=0.08, seg=14, seed=seed + 5)
    with B.at(rx + 0.05, ry + 0.12, 0.0, pitch=4, roll=-5):
        hT = [(0, 2.6), (60, 2.3), (120, 2.55), (150, 2.6), (196, 2.45), (214, 1.5), (232, 2.45), (280, 2.6),
              (320, 2.2)]
        bT = ring_bounds(hT, 12)
        hfT = stave_ring(B, M["stave"], M["top"], 0, 0, 0.6, 0.1, hT, seed=seed + 11, jitter=0.05, bounds=bT)
        for zb in (0.75, 1.75):
            band_runs(B, M["band"], 0, 0, 0.65, bT, lambda a0, a1, zb=zb: min(hfT(a0), hfT(a1)) > zb + 0.2,
                      lambda a, zb=zb: zb, 0.1, 0.04)
        with tinted(B, M["grit"], FLOOR):
            B.cyl(M["grit"], 0, 0, 0.0, 0.56, 0.08, seg=12)
    # its cone, knocked off and lying on the ground beside it
    with B.at(rx - 1.2, ry - 0.15, 0.6, yaw=205, roll=68):
        B.cyl(M["grey"], 0, 0, 0.0, 0.68, 0.72, r_top=0.14, seg=12)
        B.cyl(M["iron"], 0, 0, 0.72, 0.15, 0.12, seg=8)
    # in front, middle: the next ring broken open, its rubble spilling out of the gap
    gx, gy = 1.9, -4.85
    tank(B, M, gx, gy, 0.75, [(0, 0.75), (54, 0.62), (55, 0.0), (68, 0.0), (69, 0.66), (120, 0.8), (174, 0.5),
                              (175, 0.0), (245, 0.0), (246, 0.45), (290, 0.7)], t=0.12, bands=(0.45,), band_h=0.08,
         rim=0.6, seg=14, seed=seed + 6, floor=False)
    mound_disc(B, M["grit"], gx, gy, 0.69, 0.5, 0.28, seg=12, rings=3, seed=seed + 12)
    for k in range(8):
        a = math.radians(rng.uniform(178, 242)) if k < 5 else rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0.7, 1.2) if k < 5 else rng.uniform(0.0, 0.45)
        z = 0.0 if k < 5 else 0.42
        A.lump(B, M["grit"], gx + rr * math.cos(a), gy + rr * math.sin(a), z, rng.uniform(0.2, 0.34),
               rng.uniform(0.18, 0.3), rng.uniform(0.12, 0.22), seed=seed + 30 + k)
    # in front, right: the blue-water tank broken open at the front, the
    # water run down low and out over the stubs, half its dome still over
    # its back
    wx, wy = 3.4, -3.9
    hW = [(0, 1.4), (70, 1.32), (150, 1.4), (205, 1.28), (214, 0.42), (232, 0.32), (252, 0.46), (272, 0.33),
          (292, 0.4), (304, 0.52), (312, 1.3), (330, 1.35)]
    tank(B, M, wx, wy, 0.9, hW, t=0.12, bands=(0.4,), rim=1.15, level=0.36, pool=blue, seg=14, seed=seed + 7,
         jitter=0.05, covered=[(212, 314)])
    pour(B, blue, wx, wy, 0.9, 0.12, 0.36, 0.3, 262, 0.9, 0.45, spread=1.3, seed=seed + 8)
    with B.at(wx, wy, 1.3):
        dome_patch(B, M["stave"], 0.96, 0.62, 12, 168, 0.0, 0.78, na=5, nt=2, th=0.07, rag=0.12, seed=seed + 4)
        band_runs(B, M["band"], 0, 0, 0.9, ring_bounds((), 14), lambda a0, a1: 20 < a0 and a1 < 181,
                  lambda a: -0.02, 0.1, 0.07, faces=("outer", "top"))
    # the big domes' pieces, tilted half sunk in the sludge and one leaning
    # on each inner wall, and charred staves sunk with them
    for (tx, ty, TR, zf, n_lie, k0) in ((ax, ay, RA, zA, 2, 0), (bx, by, RB, zB, 2, 7)):
        Hd = TR * 0.45
        for k in range(n_lie + 1):
            a = (k0 * 40 + k * 120.0 + rng.uniform(-18, 18)) % 360
            if 205 < a < 245 or a > 340 or a < 15:
                a = (a + 60) % 360
            if k < n_lie:
                rr = TR * rng.uniform(0.25, 0.5)
                px_, py_ = tx + rr * math.cos(math.radians(a)), ty + rr * math.sin(math.radians(a))
                dome_shard(B, M, (px_, py_, zf(px_, py_) + 0.04), TR, Hd, rng.uniform(26, 36), 0.1, 0.5,
                           yaw=a + rng.uniform(-30, 30), roll=rng.uniform(-35, -15), pitch=rng.uniform(-12, 12),
                           seed=seed + 40 + k + k0)
            else:
                rr = TR * 0.62
                px_, py_ = tx + rr * math.cos(math.radians(a)), ty + rr * math.sin(math.radians(a))
                dome_shard(B, M, (px_, py_, zf(px_, py_) + 0.45), TR, Hd, rng.uniform(22, 28), 0.1, 0.6, yaw=a,
                           roll=rng.uniform(-80, -70), pitch=rng.uniform(-6, 6), seed=seed + 50 + k + k0)
        sunk(B, M, rng, tx, ty, TR, zf, 5)
    # the machinery between the big tanks, and the pipes that fed them
    B.box(M["grey"], 0.0, 1.75, 0.0, 0.9, 0.7, 1.2, yaw=8)
    B.box(M["iron"], 0.0, 1.75, 1.2, 0.6, 0.5, 0.35, yaw=8)
    B.cyl(M["grey"], 0.45, 0.4, 0.0, 0.32, 1.4, seg=10)
    B.cyl(M["band"], 0.45, 0.4, 1.4, 0.36, 0.1, seg=10)
    tube(B, M["green"], [(ax + RA * 0.98, ay - 0.6, 1.1), (0.0, -0.8, 1.2), (0.6, -1.4, 1.0), (bx - RB * 0.97, by - 0.5, 0.9)],
         0.17, seg=6, smooth=2, soft=True)
    tube(B, M["green"], [(ax + RA * 0.9, ay + 1.0, 0.6), (-0.3, 1.0, 0.9), (0.0, 1.5, 1.0)], 0.15, seg=6, soft=True)
    tube(B, M["grey"], [(0.45, 0.4, 1.2), (1.0, 0.0, 1.5), (bx - RB * 0.95, by + 0.6, 1.6)], 0.14, seg=6, soft=True)
    tube(B, M["green"], [(rx + 0.6, ry + 0.3, 1.0), (1.0, -3.0, 0.8), (bx - RB * 0.6, by - RB * 0.75, 0.6)], 0.14, seg=6,
         soft=True)
    # a snapped pipe hanging off the east tank's front
    tube(B, M["green"], [(bx + 0.2, by - RB - 0.1, 1.4), (bx + 0.3, by - RB - 0.5, 0.7), (bx + 0.6, by - RB - 0.8, 0.12)],
         0.15, seg=6, smooth=2, soft=True)
    # grey and iron machinery knocked about between the tanks, and pieces
    # of pipe, where the picture draws them
    for k, (c_, rw_, kind) in enumerate(((84, 44, "grey"), (99, 58, "grey"), (104, 70, "pipe"), (92, 86, "iron"),
                                         (101, 98, "grey"), (110, 108, "pipe"), (95, 114, "iron"), (106, 124, "grey"),
                                         (88, 104, "iron"))):
        x_, y_ = P(r, c_, rw_, 0.25)
        if kind == "pipe":
            yaw = rng.uniform(0, math.pi)
            L = rng.uniform(0.6, 0.9)
            d = Vector((math.cos(yaw), math.sin(yaw), 0.0)) * (L / 2)
            c = Vector((x_, y_, 0.13))
            tube(B, M["grey"], [tuple(c - d), tuple(c + d + Vector((0, 0, 0.1)))], 0.13, seg=6)
        else:
            s_ = rng.uniform(0.3, 0.8) if kind == "grey" else rng.uniform(0.3, 0.5)
            B.box(M[kind], x_, y_, -0.03, s_, s_ * rng.uniform(0.6, 0.9), s_ * rng.uniform(0.6, 1.0),
                  yaw=rng.uniform(0, 90), pitch=rng.uniform(-14, 14), roll=rng.uniform(-14, 14))
    # charred staves strewn along the south front
    small = [(rx, ry, 1.0), (gx, gy, 0.85), (wx, wy, 1.0), (ax, ay, RA + 0.2), (bx, by, RB + 0.2)]
    n = 0
    while n < 8:
        x_, y_ = rng.uniform(-5.4, 5.6), rng.uniform(-5.6, -3.3)
        if any(math.hypot(x_ - a_, y_ - b_) < c_ for a_, b_, c_ in small):
            continue
        yaw = rng.uniform(0, math.pi)
        L = rng.uniform(0.5, 1.1)
        d = Vector((math.cos(yaw), math.sin(yaw), 0.0)) * (L / 2)
        c = Vector((x_, y_, 0.06))
        B.beam(rng.choice(M["char"] + [M["stave"]]), tuple(c - d), tuple(c + d + Vector((0, 0, rng.uniform(0, 0.12)))),
               rng.uniform(0.12, 0.2))
        n += 1
    invent_bed(B, M, r, pic, seed + 8, [[(0, 20), (199, 20), (199, 166), (0, 166)]], 16,
               avoid=[(ax, ay, RA), (bx, by, RB), (2.1, 2.3, 1.15), (4.6, 1.5, 0.72), (rx, ry, 0.85), (gx, gy, 0.7),
                      (wx, wy, 0.85)], step=0.75)
    return invent_soot(seed)


def strew(B, M, r, pic, rng, box, n, avoid=(), kinds=("grey", "grey", "pipe", "iron"), size=(0.3, 1.0), seed=0):
    """Debris lying where the picture draws it inside box (pixels x0, y0,
    x1, y1): grey machinery blocks, lengths of pipe and iron pieces, size
    cells across, kept off the circles in avoid."""
    dens = density(pic, 2)
    placed = tries = 0
    while placed < n and tries < n * 80:
        tries += 1
        c, rw = rng.uniform(box[0], box[2]), rng.uniform(box[1], box[3])
        ci, ri = int(c), int(rw)
        if not (0 <= ri < dens.shape[0] and 0 <= ci < dens.shape[1]) or dens[ri, ci] < 0.55:
            continue
        x, y = P(r, c, rw, 0.15)
        if any(math.hypot(x - ax, y - ay) < aR for ax, ay, aR in avoid):
            continue
        k = kinds[placed % len(kinds)]
        s_ = rng.uniform(*size)
        if k == "pipe":
            yaw = rng.uniform(0, math.pi)
            d = Vector((math.cos(yaw), math.sin(yaw), 0.0)) * (s_ / 2)
            q = Vector((x, y, 0.12))
            tube(B, M["grey"], [tuple(q - d), tuple(q + d + Vector((0, 0, rng.uniform(0, 0.12))))], 0.12, seg=6)
        elif k == "iron":
            s_ *= 0.6
            B.box(M["iron"], x, y, -0.02, s_, s_ * rng.uniform(0.4, 0.8), s_ * rng.uniform(0.3, 0.6),
                  yaw=rng.uniform(0, 90), pitch=rng.uniform(-15, 15), roll=rng.uniform(-15, 15))
        else:
            B.box(M["grey"], x, y, -0.03, s_, s_ * rng.uniform(0.55, 0.85), s_ * rng.uniform(0.4, 0.75),
                  yaw=rng.uniform(0, 90), pitch=rng.uniform(-12, 12), roll=rng.uniform(-12, 12))
        placed += 1
    return placed


@model("CreInvent03a", scale=2)
def invent03a(B, r, pic, seed):
    """The destroyed refinery. The front tank's south wall has burst wide
    open, its blue liquor streaked red pouring over the stubs and fanning
    out on the ground, curved pieces of its fallen dome standing up out of
    it. The back tank's rim is broken into sections, a deep notch at the
    north and the east side down to stubs, its tall back staves standing
    broken. It is grown over with green moss, lumpy and holed, that spills
    out through the east breach and hangs over its south-west rim down
    between the tanks to the ground. The little fountain tank is broken
    down to stubs on its west and front, its teal water run out west over
    the ground with debris in it, its fountain snapped off and lying in
    the spill. The small tank off to the east is a C, its west half gone.
    Grey machinery and pipes lie between, and a field of grey blocks,
    lengths of pipe and iron pieces lies to the west and south-west, on
    speckled grit."""
    rng = random.Random(seed)
    M = invent_mats("i3", seed)
    blue = liquid("i3_blu", "#1c3442", seed, streak="#7a2410", streak_amt=2.6, fleck="#2a4e50", fleck_amt=0.06,
                  var=0.3)
    moss = liquid("i3_moss", "#3a5a22", seed + 1, rough=0.9, fleck="#5a7a2c", fleck_amt=0.14, streak="#1a2010",
                  streak_amt=1.2, var=0.4)
    teal = liquid("i3_teal", "#1a3e38", seed + 2, fleck="#2a5650", fleck_amt=0.05, var=0.25)
    # the front tank, its south wall burst wide, the liquor pouring out
    ax, ay, RA = 1.6, -2.82, 2.85
    hA = [(0, 2.9), (25, 1.75), (40, 3.0), (80, 2.7), (120, 3.0), (160, 2.6), (200, 2.9), (232, 2.2), (242, 1.6),
          (243, 0.48), (256, 0.32), (271, 0.4), (286, 0.3), (298, 0.5), (299, 1.5), (310, 1.8), (325, 2.6)]
    hfA, lvA = tank(B, M, ax, ay, RA, hA, t=0.2, rim=2.6, level=0.8, pool=blue, seed=seed, seg=24,
                    covered=[(242, 300)])
    pour(B, blue, ax, ay, RA, 0.2, lvA, 0.38, 270, 2.0, 0.6, spread=2.4, seed=seed + 1, k=10, rag=0.7)
    # its dome's pieces, brown planks rising steeply out of the liquor
    for k, a in enumerate((35, 105, 150, 200, 330)):
        a += rng.uniform(-12, 12)
        rr = RA * rng.uniform(0.3, 0.5)
        pos = (ax + rr * math.cos(math.radians(a)), ay + rr * math.sin(math.radians(a)), lvA + 0.32)
        dome_shard(B, M, pos, RA, RA * 0.45, rng.uniform(26, 36), 0.12, 0.62, yaw=a + rng.uniform(-20, 20),
                   roll=rng.uniform(-5, 15), pitch=rng.uniform(-12, 12), seed=seed + 40 + k)
    # the back tank, its rim broken into sections: its east side from the
    # south-east round to the north-east down to stubs, a deep notch at
    # the north between the tall broken back staves, and the south-west
    # rim low where the moss came over it
    bx, by, RB = -0.3, 3.0, 2.2
    hB = [(0, 1.15), (10, 1.35), (21, 1.0), (33, 1.3), (45, 1.05), (57, 1.4), (68, 1.2), (75, 1.3), (76, 3.3), (84, 3.7),
          (93, 3.3), (94, 1.15), (106, 1.1), (107, 3.5), (122, 3.85), (138, 3.95), (152, 3.2), (165, 2.5), (180, 2.6),
          (210, 2.35), (222, 1.7), (238, 1.35), (256, 1.42), (266, 2.0), (285, 2.1), (305, 2.4), (316, 2.0),
          (326, 1.7), (338, 1.5), (348, 1.35)]
    hfB, _ = tank(B, M, bx, by, RB, hB, t=0.2, rim=2.5, seed=seed + 1, seg=24, floor=False)
    # the moss: lumpy clumps with dark gaps between them over a dark mat,
    # frayed at its edge, low where it ran out of the breaks
    mat_ = VMat("i3_mat", None, colour="#121410", rough=0.95, soot=0.0)
    B.cyl(mat_, bx, by, 1.05, RB - 0.12, 0.04, seg=16)
    clumps = VMat("i3_clump", tex_clumps("i3_clump", [("#2a4418", 0.3), ("#3a5a22", 0.4), ("#4a6a2a", 0.2),
                                                      ("#5a7a2c", 0.1)], cover=0.84, seed=seed + 20), uv=3.0,
                  soot=0.0, rough=0.9, cut=True)
    mz = 1.75
    fill(B, clumps, bx, by, RB - 0.1, mz, hf=hfB, tilt=(0.0, -0.05), amp=0.14, seg=24, rings=4, seed=seed + 5,
         pours=[(340, 70), (94, 106), (222, 262)], margin=0.08, slope=0.5, rag=0.14)
    # over the east stubs and down the outside to the ground, furthest out
    # at the north-east, short at the east and down between the tanks
    # toward the north-east tank

    def reach(a):
        return float(np.interp((a + 45) % 360, [0, 20, 35, 55, 70, 85, 100, 117],
                               [0.35, 0.45, 0.3, 0.25, 0.55, 0.95, 1.05, 0.8]))
    sparse = VMat("i3_clump2", tex_clumps("i3_clump2", [("#2a4418", 0.35), ("#3a5a22", 0.4), ("#4a6a2a", 0.25)],
                                          cover=0.6, seed=seed + 21), uv=2.5, soot=0.0, rough=0.9, cut=True)
    overflow(B, (mat_, sparse), bx, by, RB, 0.2, hfB, 322, 70, 1.45, reach, bulge=0.4, seed=seed + 11)
    # clumps over the tall north staves, hanging a little over their tops
    rim_moss(B, moss, bx, by, RB, 0.2, hfB, [80, 86, 112, 119, 127, 134, 142, 149], rng, size=(0.45, 0.65))
    # over the south-west rim down between the tanks to the ground, and a
    # little out of the north notch
    tongue(B, moss, bx, by, RB, 0.2, 1.62, 1.35, 238, 1.6, 0.35, seed=seed + 6, slump=0.75)
    tongue(B, moss, bx, by, RB, 0.2, 1.55, 1.4, 256, 0.9, 0.25, seed=seed + 7, slump=0.55)
    tongue(B, moss, bx, by, RB, 0.2, 1.3, 1.1, 100, 0.45, 0.35, seed=seed + 9)
    # staves fallen off its west side, leaning back against it
    for a, L_, w_ in ((150, 2.1, 0.42), (163, 2.3, 0.38), (176, 2.0, 0.45), (190, 1.7, 0.4)):
        c, sn = math.cos(math.radians(a)), math.sin(math.radians(a))
        # its foot out on the ground, its top resting on the wall
        lean = math.degrees(math.asin(min(0.95, 0.85 / L_)))
        with B.at(bx + (RB + 0.95) * c, by + (RB + 0.95) * sn, 0.0, yaw=a + 90 + rng.uniform(-8, 8),
                  pitch=-lean, roll=rng.uniform(-6, 6)):
            B.box(M["stave"], 0, 0, 0, w_, 0.08, L_)
    # the fountain tank, broken down to stubs on its west and front, its
    # teal water run out west over the ground
    fx, fy = -3.44, -0.3
    hF = [(0, 1.0), (80, 0.9), (138, 1.05), (146, 0.95), (147, 0.2), (165, 0.12), (190, 0.08), (215, 0.14), (240, 0.1),
          (265, 0.25), (290, 0.3), (296, 0.32), (297, 0.85), (320, 0.95)]
    tank(B, M, fx, fy, 1.25, hF, t=0.14, bands=(0.5,), rim=0.85, level=0.18, pool=teal, seg=18, seed=seed + 2,
         jitter=0.04, covered=[(146, 298)])
    pour(B, teal, fx, fy, 1.25, 0.14, 0.18, 0.12, 196, 1.7, 1.1, spread=1.4, seed=seed + 3)
    # the fountain snapped off to a jagged stump, its top lying in the spill
    B.cyl(M["grey"], fx + 0.1, fy + 0.15, 0.0, 0.3, 0.5, r_top=0.28, seg=8)
    for k in range(4):
        a = 2 * math.pi * k / 4 + rng.uniform(-0.3, 0.3)
        w0 = rng.uniform(0.16, 0.22)
        with B.at(fx + 0.1 + 0.22 * math.cos(a), fy + 0.15 + 0.22 * math.sin(a), 0.48, yaw=math.degrees(a) + 90):
            B.solid(M["grey"], [(-w0 / 2, -0.04, 0), (w0 / 2, -0.04, 0), (w0 / 2, 0.04, 0), (-w0 / 2, 0.04, 0),
                                (0.0, 0.0, rng.uniform(0.1, 0.25))],
                    [(0, 3, 2, 1), (0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)])
    with B.at(fx - 1.75, fy - 0.55, 0.24, yaw=70, roll=88):
        B.cyl(M["grey"], 0, 0, -0.45, 0.26, 0.9, r_top=0.21, seg=8)
        B.cyl(M["band"], 0, 0, 0.0, 0.3, 0.1, seg=8)
    # the small tank off to the east, its west and south standing banded,
    # broken open at the north-east, its staves fallen out that way
    tx3, ty3 = 3.69, 1.6
    tank(B, M, tx3, ty3, 0.78, [(0, 0.85), (12, 0.6), (13, 0.0), (80, 0.0), (81, 1.05), (100, 1.35), (118, 1.7),
                                (135, 1.45), (180, 1.5), (230, 1.4), (270, 1.25), (310, 1.15), (345, 1.0)],
         t=0.12, rim=1.2, seg=14, seed=seed + 3)
    for k in range(3):
        a = math.radians(rng.uniform(20, 75))
        rr = 0.78 + rng.uniform(0.15, 0.4)
        with B.at(tx3 + rr * math.cos(a), ty3 + rr * math.sin(a), 0.04, yaw=math.degrees(a) + 90 + rng.uniform(-25, 25),
                  roll=rng.uniform(80, 88)):
            B.box(M["stave"], 0, 0, 0, 0.07, rng.uniform(0.18, 0.26), rng.uniform(0.7, 1.0))
    # grey machinery and the pipes from the fountain to the front tank
    B.box(M["grey"], -1.7, -1.6, 0.0, 0.9, 0.65, 0.75, yaw=20)
    B.box(M["iron"], -1.7, -1.6, 0.75, 0.55, 0.4, 0.3, yaw=20)
    tube(B, M["grey"], [(fx + 1.2, fy - 0.5, 0.5), (-2.0, -1.4, 0.5), (-1.25, -1.9, 0.55), (ax - RA * 0.95, ay + 0.8, 0.6)],
         0.15, seg=8, smooth=2, soft=True)
    tube(B, M["green"], [(bx - 0.6, by - RB * 0.95, 1.2), (-1.4, -0.2, 1.0), (-1.7, -1.3, 0.9)], 0.14, seg=8, soft=True)
    tube(B, M["iron"], [(ax + RA * 0.97, ay + 0.45, 1.0), (4.7, -2.45, 0.95), (5.0, -3.0, 0.5), (5.05, -3.3, 0.12)],
         0.17, seg=8, smooth=2, soft=True)
    shards(B, M, rng, ax, ay, RA, 4, spread=1.0)
    shards(B, M, rng, bx, by, RB, 3, spread=1.0)
    # the debris field to the west and south-west: grey blocks, lengths of
    # pipe and iron pieces, some lying in the teal spill
    strew(B, M, r, pic, rng, (0, 95, 70, 160), 13, avoid=[(fx, fy, 1.3), (ax, ay, RA + 0.1), (-1.7, -1.6, 0.6)])
    # speckled grit and debris to the west and in front, as drawn
    invent_bed(B, M, r, pic, seed + 5, [[(0, 60), (72, 60), (72, 130), (62, 194), (0, 194)],
                                       [(62, 160), (174, 160), (174, 194), (62, 194)]], 24,
               avoid=[(ax, ay, RA), (bx, by, RB), (fx, fy, 1.2), (tx3, ty3, 0.8)])
    return invent_soot(seed)


def gear(B, mat, x, y, z, R, w=0.12, teeth=10, axis=(0, 0, 1)):
    """An iron gear wheel: a thick disc with square teeth round its rim,
    its axle along axis."""
    ax = Vector(axis).normalized()
    Mw = Matrix.Translation((x, y, z)) @ ax.to_track_quat("Z", "Y" if abs(ax.z) < 0.99 else "X").to_matrix().to_4x4()
    with B.at():
        B.M = B.M @ Mw
        B.cyl(mat, 0, 0, -w / 2, R, w, seg=teeth * 2)
        for i in range(teeth):
            a = 2 * math.pi * (i + 0.25) / teeth
            B.box(mat, (R + 0.05) * math.cos(a), (R + 0.05) * math.sin(a), -w / 2, 0.12, R * 0.3, w,
                  yaw=math.degrees(a))
        B.cyl(mat, 0, 0, -w * 0.9, R * 0.25, w * 1.8, seg=8)


def rag(B, mat, top, d, L, w, rng, segs=3):
    """A strip of torn cloth hanging from point top, blown along d (a
    plan direction): two or three bent pieces getting narrower, its end
    torn ragged."""
    p = Vector(top)
    d = Vector((d[0], d[1], 0.0)).normalized()
    side = Vector((-d.y, d.x, 0.0))
    V, F = [], []
    for i in range(segs + 1):
        f = i / segs
        hw = w / 2 * (1 - 0.45 * f)
        c = p + d * (L * 0.45 * f ** 1.4) + Vector((0, 0, -L * f))
        tw = side * rng.uniform(-0.04, 0.04)
        if i == segs:
            # the torn end: three ragged points
            V += [tuple(c - side * hw + tw + Vector((0, 0, rng.uniform(0, 0.12)))),
                  tuple(c + tw + Vector((0, 0, -rng.uniform(0, 0.1)))),
                  tuple(c + side * hw + tw + Vector((0, 0, rng.uniform(0, 0.12))))]
        else:
            V += [tuple(c - side * hw + tw), tuple(c + tw + d * 0.04), tuple(c + side * hw + tw)]
    for i in range(segs):
        for j in range(2):
            F.append((3 * i + j, 3 * i + j + 1, 3 * (i + 1) + j + 1, 3 * (i + 1) + j))
    B.mesh(mat, V, F)


@model("CreInvent04a", scale=2)
def invent04a(B, r, pic, seed):
    """The destroyed Argus generator. Its mast is stripped of the banner
    sails, its head a splintered crown of broken staves and spar stubs.
    The spars left on it are thin and broken, bunched in its upper half at
    odd angles and lengths: most rise to the east, one long one reaches
    west near the top, some are snapped short, and torn strips of sail
    hang from most of them. At its foot the sails' wreckage lies heaped,
    dark red-brown cloth draped over the broken yards that fell against
    the mast, north and east of the drum. The mast stands on the wheeled
    frame, broken and slumped: boards gone or charred at their ends, its
    north end sagged onto the ground and a gear fallen against its east
    end. The domed drum in front is cracked open between its ribs, and
    the gearing off its west side lies broken on the ground, a toothed
    wheel and a crank."""
    rng = random.Random(seed)
    M = invent_mats("i4", seed)
    wood = VMat("i4_wood", A.tex_planks("i4_wood", "#4a3628", boards=3, seed=seed, var=0.15), uv=0.8, soot=0.6,
                vary=[("#4a3628", 0.5), ("#3a2a22", 0.5)], made="#4a3628")
    # the sails' cloth as drawn at the mast's foot: dark red-brown, scorched black in patches
    cloth = VMat("i4_cloth", A.tex_mottle("i4_cloth", "#33201b", var=0.35, seed=seed + 3, speck="#0e0807",
                                          speck_amt=0.24), uv=0.8, soot=0.7, cut=True)
    B.bevels[wood.name] = (0.02, 1)
    char = M["char"][0]
    # the drum: a low banded wall under a dome of ribs, cracked open
    dx, dy, RD = -0.69, -1.06, 2.25
    tank(B, M, dx, dy, RD, [(0, 1.0), (60, 0.95), (120, 1.0), (180, 0.9), (240, 0.6), (300, 1.0)], t=0.16, bands=(0.4,),
         rim=0.85, seg=24, seed=seed, floor=False)
    nrib = 12
    hub = 2.25
    gone = {3, 4, 8}
    for k in range(nrib):
        a = 2 * math.pi * k / nrib
        pts = []
        for i in range(6):
            t = i / 5
            rr = RD * (1 - t) * 0.98 + 0.28 * t
            z = 0.95 + (hub - 0.95) * math.sin(t * math.pi / 2)
            pts.append((dx + rr * math.cos(a), dy + rr * math.sin(a), z))
        if k in gone:
            pts = pts[:3]
        tube(B, M["iron"], pts, 0.09, seg=5)
        # the panels between the ribs, some cracked away
        if k not in gone and (k + 1) % nrib not in gone and rng.random() > 0.15:
            a2 = 2 * math.pi * (k + 1) / nrib
            for i in range(4):
                t0, t1 = i / 4, (i + 1) / 4
                if i >= 2 and rng.random() < 0.35:
                    continue
                q = []
                for t, aa in ((t0, a), (t0, a2), (t1, a2), (t1, a)):
                    rr = RD * (1 - t) * 0.97 + 0.28 * t
                    z = 0.95 + (hub - 0.95) * math.sin(t * math.pi / 2) - 0.03
                    q.append((dx + rr * math.cos(aa), dy + rr * math.sin(aa), z))
                B.slab(M["stave"], q, 0.05)
    B.cyl(M["grey"], dx, dy, hub - 0.15, 0.3, 0.3, seg=10)
    B.cyl(M["iron"], dx, dy, hub + 0.15, 0.18, 0.15, seg=8)
    # fallen panels inside and out
    for k in range(6):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0.3, RD + 0.6)
        shard(B, M["stave"], (dx + rr * math.cos(a), dy + rr * math.sin(a), 0.15 if rr > RD else 0.25),
              (rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), 1.0), rng.uniform(0.5, 0.8), rng.uniform(0.4, 0.6), 0.05, rng)
    with tinted(B, M["grit"], FLOOR):
        B.cyl(M["grit"], dx, dy, 0.0, RD - 0.1, 0.08, seg=20)
    # the frame: a timber deck on beams, its north end sagged onto the
    # ground, boards gone, others tipped up or charred at their ends, its
    # beams broken
    fx0, fx1, fy0, fy1, fz = 0.6, 4.1, -1.6, 2.6, 0.95
    L, W = fx1 - fx0, fy1 - fy0
    sag = 0.7  # how far the north end has dropped
    tilt = math.degrees(math.atan2(sag, W))
    cxf, cyf = (fx0 + fx1) / 2, (fy0 + fy1) / 2

    def deck(x, y):
        """The deck's top under (x, y), or the ground off it."""
        if fx0 <= x <= fx1 and fy0 <= y <= fy1:
            return fz - sag / 2 - (y - cyf) * math.tan(math.radians(tilt)) + 0.1
        return 0.04
    with B.at(cxf, cyf, -sag / 2, roll=-3, pitch=-tilt):
        for i in range(10):
            u = -W / 2 + (i + 0.5) * W / 10
            if i in (1, 4, 5, 8):
                continue
            Lb = L * rng.uniform(0.7, 1.0)
            off = rng.uniform(-0.2, 0.2)
            up = rng.uniform(10, 16) if i in (3, 7) else rng.uniform(-3, 3)
            lift = Lb * 0.4 * math.sin(math.radians(abs(up)))
            with B.at(off, u, fz + lift, yaw=rng.uniform(-7, 7), roll=up * rng.choice((-1, 1))):
                B.box(wood, 0, 0, 0, Lb * 0.8, W / 10 * 0.9, 0.1)
                # charred ends, one or both
                for s_ in ((-1, 1) if rng.random() < 0.5 else (rng.choice((-1, 1)),)):
                    B.box(char, s_ * Lb * 0.45, 0, 0, Lb * 0.12, W / 10 * 0.85, 0.09, roll=s_ * rng.uniform(4, 12))
        # the south beam whole, the north one down on the ground askew,
        # the side beams broken, their ends dropped
        B.box(char, 0, -(W / 2 - 0.15), fz - 0.25, L, 0.22, 0.25)
        B.box(char, 0.2, W / 2 - 0.1, fz - 0.22, L * 0.8, 0.22, 0.25, yaw=6, roll=4)
        for s_ in (-1, 1):
            x_ = s_ * (L / 2 - 0.15)
            B.box(char, x_, -W * 0.2, fz - 0.25, 0.22, W * 0.6, 0.25)
            B.beam(char, (x_ + s_ * 0.05, W * 0.12, fz - 0.15), (x_ + s_ * 0.3, W / 2 - 0.1, fz - 0.2), 0.22)
        for sx_ in (-1, 1):
            # posts under the south end only: the north end lies on the ground
            B.box(char, sx_ * (L / 2 - 0.2), -(W / 2 - 0.2), 0.0, 0.24, 0.24, fz - 0.25)
    # broken boards round the mast's foot
    mx, my = 1.6, 1.0
    zf = deck(mx, my)
    for k in range(5):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0.6, 1.0)
        L_ = rng.uniform(0.6, 1.1)
        with B.at(mx + rr * math.cos(a), my + rr * math.sin(a), zf + rng.uniform(0.0, 0.25), yaw=math.degrees(a) +
                  rng.uniform(-50, 50), pitch=rng.uniform(-25, 25), roll=rng.uniform(-30, 30)):
            B.box(rng.choice((wood, char)), 0, 0, 0, L_, rng.uniform(0.18, 0.3), 0.08)
    # a gear fallen against the deck's east end, where the drawing has it
    gear(B, M["iron"], 3.1, -0.85, 1.35, 0.5, axis=(0.2, -0.75, 0.62), teeth=7)
    B.beam(M["iron"], (3.1, -0.6, 1.1), (3.6, 0.4, 0.95), 0.1)
    # the mast, stripped and burnt to charcoal, thick at its step and
    # tapering to its snapped top, darkest up where the sails burnt on it,
    # ash grey on the crowns of its scales
    top = 10.6
    rb, rt = 0.47, 0.37
    mast = VMat("i4_mast", tex_liquid("i4_mast", "#13110f", fleck="#6e6a64", fleck_amt=0.15, fleck_px=4,
                                      streak="#050404", streak_amt=1.5, seed=seed + 9, var=0.35), uv=1.5, soot=0.25)
    # charcoal hardly shines: a low specular, or the sheen lifts it to grey
    mast.m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.1

    def mr(z):
        return rb + (rt - rb) * max(0.0, min(1.0, (z - zf) / (top - zf)))

    def burnt(co):
        k = 1.0 - 0.55 * max(0.0, min(1.0, (co.z - 2.5) / (top - 4.0)))
        return (k, k, k)
    B.box(char, mx, my, zf - 0.1, 1.2, 1.2, 0.4)
    zs = [zf + (top - zf) * f for f in (0.0, 0.33, 0.66, 1.0)]
    with tinted(B, mast, burnt):
        tube(B, mast, [(mx, my, zs[0]), (mx, my, zs[1]), (mx + 0.01, my, zs[2]), (mx + 0.02, my, zs[3])],
             [mr(z) for z in zs], seg=12)
        # the masthead: a splintered crown of its staves, and spar stubs
        for k in range(9):
            a = 2 * math.pi * k / 9 + rng.uniform(-0.15, 0.15)
            h = rng.uniform(0.25, 0.75)
            lean = rng.uniform(4, 16)
            with B.at(mx + 0.02 + (rt - 0.07) * math.cos(a), my + (rt - 0.07) * math.sin(a), top - 0.1,
                      yaw=math.degrees(a) + 90, pitch=lean):
                # a splinter tapering to a point
                w0 = rng.uniform(0.1, 0.15)
                B.solid(mast, [(-w0 / 2, -0.04, 0), (w0 / 2, -0.04, 0), (w0 / 2, 0.04, 0), (-w0 / 2, 0.04, 0),
                               (rng.uniform(-0.02, 0.02), 0.0, h)],
                        [(0, 3, 2, 1), (0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)])
        for yaw_, up, L_ in ((20, 55, 0.9), (150, 48, 0.7), (265, 62, 0.6)):
            d = Vector((math.cos(math.radians(yaw_)), math.sin(math.radians(yaw_)),
                        math.tan(math.radians(up)))).normalized()
            a = Vector((mx + 0.02, my, top - 0.25))
            b = a + d * (L_ + 0.1)
            tube(B, mast, [tuple(a), tuple(b)], [0.11, 0.07], seg=6)
            # its broken end, a splinter on
            B.beam(mast, tuple(b), tuple(b + d * 0.2 + Vector((rng.uniform(-0.05, 0.05), 0, 0))), 0.05)
    # the spars left on it, traced on the drawing: (height, way in plan,
    # length, rise in degrees, torn strips hanging), thin, broken off at
    # their ends, bunched up the mast, most rising to the east
    spars = [(9.3, 196, 1.5, -6, 1), (9.0, -28, 1.35, 6, 1), (8.2, 160, 0.45, 12, 0), (7.6, 25, 0.6, 22, 1),
             (6.7, 6, 2.1, 20, 2), (6.1, -12, 1.6, 24, 1), (5.2, 120, 0.8, 30, 0), (4.6, 2, 1.2, 16, 1)]
    for z, yaw, L, up, n_rag in spars:
        d = Vector((math.cos(math.radians(yaw)), math.sin(math.radians(yaw)), math.tan(math.radians(up))))
        d.normalize()
        # rooted in the mast's face, its length out from there
        a = Vector((mx + (mr(z) - 0.06) * math.cos(math.radians(yaw)), my + (mr(z) - 0.06) * math.sin(math.radians(yaw)),
                    z))
        b = a + d * L
        with tinted(B, mast, burnt):
            tube(B, mast, [tuple(a), tuple((a + b) / 2), tuple(b)], [0.12, 0.105, 0.085], seg=6)
            # the snapped end, a splinter of it on
            B.beam(mast, tuple(b - d * 0.05),
                   tuple(b + d * rng.uniform(0.12, 0.25) + Vector((0, 0, rng.uniform(-0.06, 0.06)))), 0.05)
        for k in range(n_rag):
            t = rng.uniform(0.35, 0.85)
            p = a + (b - a) * t + Vector((0, 0, -0.06))
            # the strips scorched darker than the heap's cloth
            with tinted(B, cloth, (0.4, 0.36, 0.36)):
                rag(B, cloth, tuple(p), (rng.uniform(0.6, 1.0), rng.uniform(-0.5, 0.1)), rng.uniform(0.6, 1.5),
                    rng.uniform(0.28, 0.45), rng, segs=3)
    # the sails' wreckage heaped at the mast's foot, north and east of the
    # drum: the yards that came down, leaning against the mast, and the
    # cloth draped over them from about 3 cells up to the deck
    for yaw, L_, z_top in ((8, 2.5, 3.0), (52, 2.3, 2.7), (96, 2.2, 3.1), (-32, 2.1, 2.5), (128, 1.9, 2.4)):
        u = Vector((math.cos(math.radians(yaw)), math.sin(math.radians(yaw)), 0.0))
        foot = Vector((mx, my, 0.0)) + u * L_
        foot.z = deck(foot.x, foot.y)
        head = Vector((mx, my, z_top)) + u * (rb + 0.08) + Vector((0, 0, rng.uniform(-0.2, 0.2)))
        B.beam(char, tuple(foot - (head - foot).normalized() * 0.1), tuple(head + (head - foot).normalized() * 0.25),
               rng.uniform(0.18, 0.22))
    a0, a1, n_a = -45.0, 128.0, 16
    rings = [(rb + 0.06, 3.0), (0.9, 2.45), (1.35, 1.75), (1.85, 0.9), (2.35, 0.0)]
    V, F = [], []
    for j in range(n_a + 1):
        an = math.radians(a0 + (a1 - a0) * j / n_a)
        u = Vector((math.cos(an), math.sin(an), 0.0))
        fold = 0.3 * math.sin(j * 2.3 + 0.7)
        for i, (rr, z) in enumerate(rings):
            rr2 = rr * (rng.uniform(0.7, 1.15) if i == len(rings) - 1 else rng.uniform(0.92, 1.08))
            p = Vector((mx, my, 0.0)) + u * rr2
            zz = deck(p.x, p.y) + 0.04 if i == len(rings) - 1 else z + fold * (i > 0) + rng.uniform(-0.25, 0.25)
            # the cloth hangs under the yards' heads, never inside the deck
            zz = max(zz, deck(p.x, p.y) + 0.05)
            V.append((p.x, p.y, zz))
    nr = len(rings)
    for j in range(n_a):
        for i in range(nr - 1):
            if i > 0 and rng.random() < 0.18:
                continue
            F.append((j * nr + i, j * nr + i + 1, (j + 1) * nr + i + 1, (j + 1) * nr + i))
    for f_ in B.mesh(cloth, V, F):
        f_.smooth = True
    # the gearing off the west side, broken off: a toothed wheel and a crank lying on the ground
    gx, gy = P(r, 22, 90, 0.4)
    gear(B, M["iron"], gx + 0.75, gy + 0.15, 0.08, 0.5, axis=(0.15, 0.2, 1.0), teeth=7)
    with B.at(gx - 0.35, gy + 0.2, 0.06, yaw=35):
        B.box(M["iron"], 0, 0, 0, 1.2, 0.13, 0.12)
        B.box(M["iron"], 0.6, 0.25, 0, 0.13, 0.55, 0.12)
        B.box(M["iron"], 0.6, 0.5, 0, 0.32, 0.13, 0.12)
        B.cyl(M["grey"], -0.6, 0, 0.0, 0.14, 0.22, seg=8)
    tube(B, M["grey"], [(gx + 0.3, gy, 0.35), (gx + 1.4, gy - 0.4, 0.55), (dx - RD * 0.95, dy + 0.4, 0.6)], 0.14, seg=8,
         soft=True)
    # a sheet of the cloth fallen off the frame onto the ground in front
    for k in range(2):
        x, y = rng.uniform(fx0 + 0.6, fx1 + 0.3), rng.uniform(fy0 - 0.6, fy0 + 0.2)
        shard(B, cloth, (x, y, 0.05), (rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), 1.0), rng.uniform(0.8, 1.3),
              rng.uniform(0.6, 0.9), 0.03, rng)
    invent_bed(B, M, r, pic, seed + 6, [[(0, 75), (140, 75), (140, 157), (0, 157)]], 36, avoid=[(dx, dy, RD - 0.1)])
    return invent_soot(seed)


# ================================================================ main

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "--norender" not in argv
    names = [a for a in argv if not a.startswith("--")] or list(BUILD)
    for n in names:
        t0 = time.time()
        try:
            build(n, render)
        except Exception as e:
            traceback.print_exc()
            print("G6_FAILED", n, repr(e), flush=True)
        print("G6_TIME", n, "%.1fs" % (time.time() - t0), flush=True)


if __name__ == "__main__":
    main()
