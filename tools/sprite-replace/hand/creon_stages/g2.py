"""Creon destroyed stages, group 2: the Institute (CreBuild04), the
Coliseum (CreBuild05) and the Observatory (CreBuild06), run inside Blender.

    OK_REPLACE=<folder with sprites/ and catalog.json> \
    blender -b --factory-startup --python g2.py -- [Name ...] [--out DIR] [--norender]

None of the three intact buildings has a model, so each is built here from
its own picture as a parametric base (institute, coliseum, observatory)
and then broken. The ruin ('a') is the same building with its roof or dome
holed, rafters bared and rubble spilling south. The shell ('b') is the
ruin with more taken away: walls down to ragged courses, the roof or dome
mostly fallen in and the plan heaped with rubble. Asking for an intact
name (CreBuild04) builds the base alone, for checking it against its own
picture. Those are never shipped.

Frame as handkit, seen by the classic camera of handkit.renders: a point
(x, y, z) lands at column hx + 16 x and row hy - SY y - SZ z, where SY and
SZ (14.3 and 7.2 pixels a cell) are that camera's foreshortening of depth
and height. Outlines are traced on the stage pictures in their own pixels.

Every texture is made here or by the Aramon kit from noise and numbers,
and the colours are drawn colours read off the pictures, so the models
ship as geometry. Soot is the vertex colour each material multiplies:
darker toward breaches, heaps and broken tops.
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
import palace  # noqa: E402

OKR = os.environ.get("OK_REPLACE", "D:/OKReplace")
OUT = os.environ.get("G2_OUT", "D:/OKBuild/creon-stages/g2")


def _akit():
    """The Aramon ruin kit under its own name, pointed at the Creon pictures."""
    spec = importlib.util.spec_from_file_location("akit", os.path.join(TOOLS, "hand", "aramon_buildings", "kit.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    m.DATA = OKR
    m.OUT = OUT
    m.CAT.update({r["name"]: r for r in json.load(open(os.path.join(OKR, "catalog.json")))})
    return m


akit = _akit()

E = math.atan(1.0 / kit.TILT)
SY, SZ = 16.0 * math.sin(E), 16.0 * math.cos(E)
TAU = 2 * math.pi


def px(r, col, row, z=0.0):
    """The plan point at height z that the picture of r draws at (col, row)."""
    hx, hy = r["sprite"]["hotspot"]
    return ((col - hx) / 16.0, (hy - row - SZ * z) / SY)


def screen(r, x, y, z):
    hx, hy = r["sprite"]["hotspot"]
    return hx + 16.0 * x, hy - SY * y - SZ * z


# the Aramon kit reads pictures through these, so it sees the same camera
akit.px, akit.screen = px, screen


# ---------------------------------------------------------------- textures

def tex_rooftiles(seed, size=128, n=3):
    """Square copper tiles, n by n a repeat, dark joints, paler patina."""
    rng = np.random.default_rng(seed)
    u = (np.arange(size) + 0.5) / size * n
    iu = np.floor(u).astype(int)
    fu = u - iu
    tile = rng.uniform(0.82, 1.0, (n, n))
    g = tile[iu[:, None], iu[None, :]] * (0.9 + 0.1 * kit.fbm(rng, size, [(1.5, 1.0)]))
    g = g + 0.12 * np.clip((kit.fbm(rng, size, [(10.0, 1.0)]) - 0.55) * 3, 0, 1)
    d = np.minimum(fu, 1 - fu)
    g = np.where((d[:, None] < 0.07) | (d[None, :] < 0.07), 0.42, g)
    g = np.clip(g, 0.3, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def tex_dome(seed, size=256, ribs=8, rings=3):
    """Copper plates on a dome: ribs along v, seams round it, patina."""
    rng = np.random.default_rng(seed)
    t = (np.arange(size) + 0.5) / size
    fu, fv = (t * ribs) % 1, (t * rings) % 1
    g = 0.8 + 0.14 * kit.fbm(rng, size, [(2.0, 0.6), (8.0, 0.4)])
    g = g + 0.1 * np.clip((kit.fbm(rng, size, [(14.0, 1.0)]) - 0.5) * 3, 0, 1)
    rib = np.minimum(fu, 1 - fu)[None, :] < 0.035
    seam = np.minimum(fv, 1 - fv)[:, None] < 0.02
    g = np.where(rib, 0.52, np.where(seam, 0.62, g))
    g = np.clip(g, 0.3, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def tex_char(seed, size=128):
    """Charred timber: grain along u, checked cracks across it, grey ash
    on the ridges. The vertex colour gives the warm dark brown."""
    rng = np.random.default_rng(seed)
    v = (np.arange(size) + 0.5) / size
    streak = kit.norm01(kit._blur_x(rng.standard_normal((size, size)), 10.0))
    g = 0.62 + 0.25 * streak
    checks = np.clip(1 - np.abs((v * 9 + 0.3 * kit.fbm(rng, size, [(4.0, 1.0)])) % 1 - 0.5) * 12, 0, 1)
    g = g - 0.3 * checks.T
    ash = (rng.random((size, size)) > 0.985).astype(float)
    g = g + 0.35 * kit.norm01(kit.blur(ash, 0.8))
    g = np.clip(g, 0.2, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def tex_grain(seed, size=64, amp=0.12):
    """A plain stone grain for small pieces."""
    rng = np.random.default_rng(seed)
    g = np.clip(0.85 + amp * (kit.fbm(rng, size, [(1.0, 0.5), (4.0, 0.5)]) - 0.5) * 2, 0.4, 1.0)
    return np.dstack([g, g, g, np.ones_like(g)])


def tex_blocks(name, base, light, dark, size=128, n=44, seed=0, lo=12, hi=38):
    """Rubble seen from above: broken blocks from mid grey to pale, each
    lit along its top edge and shaded along its lower one, set in dark
    gaps that later blocks cut into earlier ones. Drawn colours, stored
    as the albedo each needs."""
    rng = np.random.default_rng(seed)
    b, l, d = [np.array(akit.rgb(c), float) for c in (base, light, dark)]
    col = np.zeros((size, size, 3)) + d
    for _ in range(n):
        w, h = int(rng.integers(lo, hi)), int(rng.integers(lo, int(hi * 0.8)))
        x, y = rng.integers(0, size, 2)
        c = b + (l - b) * rng.random() ** 0.45
        gx, gy = np.arange(x - 2, x + w + 2) % size, np.arange(y - 2, y + h + 2) % size
        col[np.ix_(gy, gx)] = d
        xs, ys = np.arange(x, x + w) % size, np.arange(y, y + h) % size
        col[np.ix_(ys, xs)] = c * (0.88 + 0.24 * rng.random())
        e = max(2, h // 6)
        col[np.ix_(ys[:e], xs)] = np.minimum(255, c * 1.2)
        col[np.ix_(ys[-e:], xs)] = c * 0.5
        col[np.ix_(ys, xs[:max(1, w // 10)])] = c * 0.72
        if rng.random() < 0.4:
            # a crack across the block
            k = int(rng.integers(e, max(e + 1, h - e)))
            col[np.ix_(ys[k:k + 1], xs)] = d
    soot = 0.78 + 0.22 * kit.norm01(kit.fbm(rng, size, [(2.0, 0.6), (5.0, 0.4)]))
    col *= (soot * (1 + 0.1 * (rng.random((size, size)) - 0.5)))[..., None]
    return akit._to_image(name, np.clip(col[::-1], 0, 255))


# ---------------------------------------------------------------- materials

class CMat:
    """A Creon material as the Aramon kit's Builder takes it: the vertex
    colour (the drawn colour as the albedo that renders to it, divided by
    the tile's own mean) times a grey generated tile."""

    def __init__(self, name, colour, arr=None, uv=1.0, rough=0.85, metal=0.0, ref=None, img=None):
        self.name, self.uv, self.uvfn = name, uv, None
        tex = kit.image(name, arr) if arr is not None else img
        self.textured = tex is not None
        self.m = kit.material(name, tex, rough=rough)
        self.m.node_tree.nodes["Principled BSDF"].inputs["Metallic"].default_value = metal
        self.m.use_backface_culling = True
        k = float(np.mean(kit.srgb_to_lin(arr[..., :3]))) if arr is not None else 1.0
        self.lin = np.array(akit.albedo(colour)) / k if colour is not None else np.ones(3)
        self.ref = akit.rgb(ref if ref is not None else (colour or "#808080"))


def materials(seed, tone=None):
    """The family's materials. tone overrides drawn colours by key, for a
    stage drawn darker than its intact."""
    c = dict(marble="#b2ab9e", floor="#a79a8c", roof="#4f6a52", dome="#437459", gold="#a88f3a", dark="#16130f",
             green="#4f6656", char="#3a2e27", chip="#b4ac9e", stone="#787167", sooty="#45413a", slab="#44604a",
             shard="#355a48")
    c.update(tone or {})
    s = seed % 1000
    marble = palace.tex_marble(s)
    M = {
        "marble": CMat("marble", c["marble"], marble, uv=1.6, rough=0.6),
        "trim": CMat("trim", c["marble"], tex_grain(s + 1, amp=0.08), uv=1.0, rough=0.6),
        "floor": CMat("floor", c["floor"], palace.tex_tiles(128, 2), uv=0.5, rough=0.5),
        "roof": CMat("roof", c["roof"], tex_rooftiles(s + 2), uv=1.0, rough=0.5),
        "dome": CMat("dome", c["dome"], tex_dome(s + 3), uv=1.0, rough=0.6, metal=0.12),
        "green": CMat("green", c["green"], palace.tex_tiles(128, 2), uv=1.0, rough=0.5),
        "gold": CMat("gold", c["gold"], None, rough=0.35, metal=0.8),
        "dark": CMat("dark", c["dark"], None, rough=0.95),
    }
    # broken pieces: marble chips pale to sooty, roof and dome slabs, char
    M["chip"] = CMat("chip", c["chip"], tex_grain(s + 4), uv=0.7, rough=0.8)
    M["stone"] = CMat("stone", c["stone"], tex_grain(s + 5), uv=0.7, rough=0.85)
    M["sooty"] = CMat("sooty", c["sooty"], tex_grain(s + 6, amp=0.2), uv=0.7, rough=0.9)
    M["slab"] = CMat("slab", c["slab"], tex_rooftiles(s + 7), uv=1.0, rough=0.6)
    M["shard"] = CMat("shard", c["shard"], tex_dome(s + 8), uv=1.0, rough=0.5, metal=0.2)
    M["char"] = CMat("char", c["char"], tex_char(s + 9), uv=1.2, rough=0.95)
    grit = akit.tex_grit("grit", c.get("grit", "#4e4a43"), light=c.get("grit_light", "#bab2a4"), dark="#141210",
                         uv=1.5, chips=0.24, darks=0.2, seed=s + 10)
    M["grit"] = CMat("grit", None, img=grit, uv=1.5, rough=1.0, ref=c.get("grit", "#4e4a43"))
    M["rubble"] = CMat("rubble", None, img=tex_blocks("rubble", c.get("rubble", "#5e5a52"),
                                                      c.get("rubble_light", "#c2b8aa"), "#0d0c0a", seed=s + 11),
                       uv=4.0, rough=0.95, ref=c.get("rubble", "#5e5a52"))
    return M, c


def debris_kinds(M, tiles="slab"):
    """The Aramon kit's cover() pieces in Creon's own materials."""
    stones = [M["chip"], M["chip"], M["stone"], M["sooty"]]
    return {"slab": stones + [M[tiles]], "tile": [M[tiles]], "stone": stones, "chunk": stones,
            "boulder": stones, "beam": [M["char"]], "board": [M["char"]]}


# ---------------------------------------------------------------- stage

class Stage:
    """One model being built: its picture, its materials and two builders,
    the structure (walls, roofs, domes, which cuts may touch) and the
    debris (heaps and pieces, which they never do)."""

    def __init__(self, name, tone=None):
        hk.reset()
        self.name = name
        self.r = akit.CAT[name]
        self.seed = zlib.crc32(name.encode())
        self.rng = random.Random(self.seed)
        self.pic = akit.Picture(self.r)
        self.M, self.colours = materials(self.seed, tone)
        self.S = akit.Builder(seed=self.seed)
        self.D = akit.Builder(seed=self.seed + 1)
        self.U = akit.Builder(seed=self.seed + 2)   # structure no cut reaches
        self.extra = []      # (object, material key) built outside the builders
        self.soot = []       # (x, y, z, radius, strength)
        self.halos = []      # (outline on the picture, radius in cells, darkest, lowest z)
        self.cuts = []       # (material keys, outlines, mode)
        self.tone = {}       # material key: brightness factor
        self.grounds = []
        self.grime = {"a": 0.3, "b": 0.4}.get(name[-1], 0.0)
        # soot sources multiply ('product') or the darkest one wins ('min'),
        # the blobs then going no darker than soot_floor
        self.soot_mode = "product"
        self.soot_floor = 0.12
        self.nosoot = ("dark",)
        self.cuts3d = []     # (material keys, cutter bmesh)
        self.settle = False  # drop loose pieces that touch nothing onto what is below
        self.dissolve = ()   # material keys whose cut faces are merged back where flat

    def add(self, bm, key, name=None):
        me = bpy.data.meshes.new(name or key)
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(me.name, me)
        bpy.context.collection.objects.link(ob)
        me.materials.append(self.M[key].m)
        ob["cmat"] = key
        self.extra.append(ob)
        return ob

    def px(self, col, row, z=0.0):
        return px(self.r, col, row, z)

    def plan(self, poly, z=0.0):
        return [px(self.r, c, rw, z) for c, rw in poly]


# ---------------------------------------------------------------- shapes

def dome_shell(cx, cy, z0, R, H, t, seg=32, rows=9, a0=0.0, a1=TAU, top=0.25, s0=0.0, uv=(4.0, 1.5)):
    """A dome's shell t thick as a closed solid: its outer surface an
    ellipse R across and H high from profile angle s0 up to where its
    radius is top, round from angle a0 to a1. UVs run round it and up it."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    full = abs((a1 - a0) - TAU) < 1e-6
    na = seg if full else seg + 1
    angs = [a0 + (a1 - a0) * k / seg for k in range(na)]
    s1 = math.acos(min(0.999, top / R))
    rings = []
    for (RR, HH) in ((R, H), (R - t, H - t)):
        ring = []
        for i in range(rows + 1):
            s = s0 + (s1 - s0) * i / rows
            r, z = RR * math.cos(s), HH * math.sin(s)
            ring.append([bm.verts.new((cx + r * math.cos(a), cy + r * math.sin(a), z0 + z)) for a in angs])
        rings.append(ring)
    out, inn = rings

    def quad(vs, uvs):
        try:
            f = bm.faces.new(vs)
        except ValueError:
            return
        for lp, q in zip(f.loops, uvs):
            lp[uvl].uv = q
    nk = seg
    for i in range(rows):
        for k in range(nk):
            k1 = (k + 1) % na
            u0, u1 = k / seg * uv[0], (k + 1) / seg * uv[0]
            v0, v1 = i / rows * uv[1], (i + 1) / rows * uv[1]
            quad((out[i][k], out[i][k1], out[i + 1][k1], out[i + 1][k]), ((u0, v0), (u1, v0), (u1, v1), (u0, v1)))
            quad((inn[i][k1], inn[i][k], inn[i + 1][k], inn[i + 1][k1]), ((u1, v0), (u0, v0), (u0, v1), (u1, v1)))
    for k in range(nk):
        k1 = (k + 1) % na
        quad((inn[0][k], inn[0][k1], out[0][k1], out[0][k]), ((0, 0), (0.1, 0), (0.1, 0.1), (0, 0.1)))
        quad((out[rows][k], out[rows][k1], inn[rows][k1], inn[rows][k]), ((0, 0), (0.1, 0), (0.1, 0.1), (0, 0.1)))
    if not full:
        for k in (0, na - 1):
            for i in range(rows):
                quad((out[i][k], inn[i][k], inn[i + 1][k], out[i + 1][k]), ((0, 0), (0.1, 0), (0.1, 0.1), (0, 0.1)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def ring_solid(B, mat, cx, cy, r0, r1, z0, z1, seg=40):
    """An annulus from radius r0 to r1 between heights z0 and z1."""
    B.lathe(mat, cx, cy, [(r0, z0), (r1, z0), (r1, z1), (r0, z1), (r0, z0)], seg=seg)


def arc_solid(B, mat, cx, cy, r0, r1, z0, z1, a0, a1, seg=20):
    """A stretch of an annulus from radius r0 to r1 and height z0 to z1,
    from angle a0 round to a1 (degrees), closed at its ends."""
    v, f = [], []
    for k in range(seg + 1):
        a = math.radians(a0 + (a1 - a0) * k / seg)
        c, s_ = math.cos(a), math.sin(a)
        v += [(cx + r0 * c, cy + r0 * s_, z0), (cx + r1 * c, cy + r1 * s_, z0),
              (cx + r1 * c, cy + r1 * s_, z1), (cx + r0 * c, cy + r0 * s_, z1)]
    for k in range(seg):
        a, b = 4 * k, 4 * (k + 1)
        for q in range(4):
            f.append((a + q, a + (q + 1) % 4, b + (q + 1) % 4, b + q))
    f += [(0, 1, 2, 3), (4 * seg + 3, 4 * seg + 2, 4 * seg + 1, 4 * seg)]
    B.solid(mat, v, f)


def ring_broken(B, mat, x, y, R, t, heights, seg=10, seed=0, step=0.5, a0=0.0, a1=360.0):
    """A round wall broken down, as the Aramon kit's ring_wall but with
    coarser stones, from angle a0 round to a1: heights is [(angle, h)]."""
    hs = sorted(heights)
    ang = [q[0] for q in hs] + [hs[0][0] + 360]
    val = [q[1] for q in hs] + [hs[0][1]]
    span, base = a1 - a0, a0
    for i in range(seg):
        a0, a1 = base + span * i / seg, base + span * (i + 1) / seg
        h0 = float(np.interp((a0 - ang[0]) % 360 + ang[0], ang, val))
        h1 = float(np.interp((a1 - ang[0]) % 360 + ang[0], ang, val))
        if max(h0, h1) < 0.05:
            continue
        p0 = (x + R * math.cos(math.radians(a0)), y + R * math.sin(math.radians(a0)))
        p1 = (x + R * math.cos(math.radians(a1)), y + R * math.sin(math.radians(a1)))
        akit.broken_wall(B, mat, mat, p0, p1, t, [(0, h0), (1, h1)], seed=seed + i, jitter=0.2, step=step)


def crenels(B, mat, cx, cy, R, z, w=0.22, h=0.28, t=0.25, step_deg=9.0, skip=None):
    """Merlons standing on a round parapet's top."""
    n = int(360 / step_deg)
    for i in range(n):
        a = i * step_deg
        if skip and skip(a):
            continue
        x, y = cx + R * math.cos(math.radians(a)), cy + R * math.sin(math.radians(a))
        B.box(mat, x, y, z, t, w, h, yaw=a)


def hip_roof(B, mat, g, t=0.22):
    """A hipped roof as one solid: eaves at g['z0'] round x0..x1 by y0..y1,
    a ridge at g['z1'] along y at x = g['xr'] from ya to yb."""
    x0, x1, y0, y1, z0, z1 = g["x0"], g["x1"], g["y0"], g["y1"], g["z0"], g["z1"]
    xr, ya, yb = g["xr"], g["ya"], g["yb"]
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (xr, ya, z1), (xr, yb, z1),
         (x0, y0, z0 - t), (x1, y0, z0 - t), (x1, y1, z0 - t), (x0, y1, z0 - t)]
    f = [(0, 1, 4), (1, 2, 5, 4), (2, 3, 5), (3, 0, 4, 5),
         (6, 7, 1, 0), (7, 8, 2, 1), (8, 9, 3, 2), (9, 6, 0, 3), (9, 8, 7, 6)]
    B.solid(mat, v, f)


def hip_z(g, x, y):
    """The hipped roof's height over a plan point, or -99 off it."""
    if not (g["x0"] <= x <= g["x1"] and g["y0"] <= y <= g["y1"]):
        return -99.0
    rise = g["z1"] - g["z0"]
    # each plane's height, the roof the lowest of them
    zs = [g["z0"] + rise * (x - g["x0"]) / (g["xr"] - g["x0"]),
          g["z0"] + rise * (g["x1"] - x) / (g["x1"] - g["xr"]),
          g["z0"] + rise * (y - g["y0"]) / (g["ya"] - g["y0"]),
          g["z0"] + rise * (g["y1"] - y) / (g["y1"] - g["yb"])]
    return min(min(zs), g["z1"])


def panel(B, mat, x, y, z, w, h, yaw):
    """A flat quad w wide and h high facing out along yaw (degrees)."""
    with B.at(x, y, z, yaw=yaw):
        B.mesh(mat, [(0, -w / 2, 0), (0, w / 2, 0), (0, w / 2, h), (0, -w / 2, h)], [(0, 1, 2, 3)])


def pilaster(B, mat, x, y, z, w, d, h, yaw):
    """A strip w wide standing d proud of a wall facing yaw: its front,
    sides and top only, the back being in the wall."""
    with B.at(x, y, z, yaw=yaw):
        v = [(0, -w / 2, 0), (0, w / 2, 0), (0, w / 2, h), (0, -w / 2, h),
             (d, -w / 2, 0), (d, w / 2, 0), (d, w / 2, h), (d, -w / 2, h)]
        B.mesh(mat, v, [(4, 5, 6, 7), (0, 4, 7, 3), (5, 1, 2, 6), (7, 6, 2, 3)])


def window_row(B, M, x0, x1, y, z0, z1, n, face=-1, pil=0.26, depth=0.16, frame=False, cut=True):
    """A run of n dark windows between pilasters on a wall facing -y
    (face -1), +y (face 1) or +x (face 0, then x0..x1 run along y at x = y).
    Pilasters no cut reaches are left open at the back."""
    w = (x1 - x0) / n
    s = -1 if face < 0 else 1
    for i in range(n + 1):
        u = x0 + i * w
        if not cut:
            if face:
                pilaster(B, M["trim"], u, y, z0 - 0.1, pil, depth, z1 - z0 + 0.2, 90 * s)
            else:
                pilaster(B, M["trim"], y, u, z0 - 0.1, pil, depth, z1 - z0 + 0.2, 0)
        elif face:
            B.box(M["trim"], u, y + s * depth / 2, z0 - 0.1, pil, depth, z1 - z0 + 0.2)
        else:
            B.box(M["trim"], y + depth / 2, u, z0 - 0.1, depth, pil, z1 - z0 + 0.2)
    for i in range(n):
        u = x0 + (i + 0.5) * w
        ww = w - pil - 0.12
        if face:
            panel(B, M["dark"], u, y + s * 0.02, z0, ww, z1 - z0, 90 * s)
            if frame:
                B.box(M["trim"], u, y + s * 0.06, z1 - 0.02, ww + 0.1, 0.12, 0.12)
        else:
            panel(B, M["dark"], y + 0.02, u, z0, ww, z1 - z0, 0)


def gear(B, mat, x, y, z, R, axis=(0, 0, 1), teeth=6, t=0.16, spokes=3):
    """A gold gear wheel: a toothed rim, hub and spokes, lying on axis."""
    ax = Vector(axis).normalized()
    q = ax.to_track_quat("Z", "Y" if abs(ax.z) < 0.99 else "X")
    with B.at(x, y, z):
        L = q.to_matrix().to_4x4()
        verts, faces = [], []

        def ring(r0, r1, h, seg):
            base = len(verts)
            for k in range(seg):
                a = TAU * k / seg
                for rr in (r0, r1):
                    for zz in (0.0, h):
                        verts.append((rr * math.cos(a), rr * math.sin(a), zz))
            for k in range(seg):
                a, b = base + 4 * k, base + 4 * ((k + 1) % seg)
                faces.extend([(a + 2, b + 2, b + 3, a + 3), (a, a + 1, b + 1, b), (a + 1, a + 3, b + 3, b + 1),
                              (a, b, b + 2, a + 2)])
        ring(R * 0.78, R, t, 8)
        ring(R * 0.12, R * 0.26, t * 1.3, 4)
        B.solid(mat, verts, faces, L)
        for k in range(teeth):
            a = TAU * k / teeth
            c = Vector((math.cos(a) * R * 1.06, math.sin(a) * R * 1.06, t / 2))
            M = L @ Matrix.Translation(c) @ Matrix.Rotation(a, 4, "Z")
            vs = [(sx * 0.1, sy * 0.07, sz * t / 2) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
            B.solid(mat, vs, [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)], M)
        for k in range(spokes):
            a = TAU * k / spokes + 0.3
            p0 = L @ Vector((math.cos(a) * R * 0.2, math.sin(a) * R * 0.2, t / 2))
            p1 = L @ Vector((math.cos(a) * R * 0.8, math.sin(a) * R * 0.8, t / 2))
            B.beam(mat, tuple(p0), tuple(p1), 0.12, t * 0.8)


# ---------------------------------------------------------------- the Institute

INST = dict(mx0=-4.5, mx1=5.1, my0=-6.7, my1=6.2, mh=6.0, wt=0.45, par=0.35,
            px0=-5.6, px1=6.25, py1=6.4, ph=3.0,
            wx0=-9.75, wx1=-5.5, wy0=-2.45, wy1=1.5, wh=3.6,
            ax=5.1, ay=-0.85, ar=4.7, ah=4.4)
INST_ROOF = dict(x0=-4.12, x1=5.0, y0=-6.45, y1=5.9, z0=6.05, z1=7.3, xr=0.44, ya=-3.55, yb=3.0)


def _apse_pts(I, seg=20, shrink=0.0):
    R = I["ar"] - shrink
    pts = [(I["ax"] + R * math.cos(a), I["ay"] + R * math.sin(a))
           for a in np.linspace(-math.pi / 2, math.pi / 2, seg + 1)]
    return pts


def institute(st, mode="intact", walls=None, apse=None, wing=None, podium=None, roof=False):
    """CreBuild04, the Institute, drawn at hotspot 156, 135: a two-storey
    marble block 9.6 by 12.9 cells under a low hipped roof of green copper
    tiles (eaves 6, ridge 7.3), on a podium whose terraces run down its
    west and east sides, a flat-roofed wing to the west and a half-round
    apse to the east, both with checkered floors and crenellated parapets.

    mode 'intact' builds it whole. Otherwise walls, apse, wing and podium
    are broken-wall profiles ({side: [(f, h)]}, sides S E N W running
    anticlockwise) and parts left out are down to the ground. roof keeps
    the roof over broken walls, for cuts to take most of it away."""
    I, M, S = INST, st.M, st.S
    t = I["wt"]
    x0, x1, y0, y1, H = I["mx0"], I["mx1"], I["my0"], I["my1"], I["mh"]
    # the main block's walls, hollow inside
    if mode == "intact":
        S.box(M["marble"], (x0 + x1) / 2, y0 + t / 2, 0, x1 - x0, t, H)
        S.box(M["marble"], (x0 + x1) / 2, y1 - t / 2, 0, x1 - x0, t, H)
        S.box(M["marble"], x0 + t / 2, (y0 + y1) / 2, 0, t, y1 - y0 - 2 * t, H)
        S.box(M["marble"], x1 - t / 2, (y0 + y1) / 2, 0, t, y1 - y0 - 2 * t, H)
        # cornices and the parapet round the roof
        S.box(M["trim"], (x0 + x1) / 2, y0 - 0.1, H - 0.45, x1 - x0 + 0.3, 0.3, 0.45)
        for (a, b, c, d) in ((x0, x1, y0, y0 + 0.3), (x0, x1, y1 - 0.3, y1), (x0, x0 + 0.3, y0, y1),
                             (x1 - 0.3, x1, y0, y1)):
            S.box(M["trim"], (a + b) / 2, (c + d) / 2, H, b - a, d - c, I["par"])
        S.box(M["trim"], (I["px0"] + I["px1"]) / 2, y0 - 0.12, 2.75, I["px1"] - I["px0"] + 0.2, 0.32, 0.32)
        # the upper floor, hidden until the roof is holed
        S.box(M["sooty"], (x0 + x1) / 2, (y0 + y1) / 2, 2.9, x1 - x0 - 2 * t, y1 - y0 - 2 * t, 0.2)
        window_row(st.S, M, x0 + 0.5, x1 - 0.4, y0, 3.45, 5.3, 7)
        window_row(st.U, M, I["px0"] + 0.3, I["px1"] - 0.3, y0, 0.45, 2.45, 9, cut=False)
        # the back, as the 3D camera sees it turned
        st.U.box(M["trim"], (x0 + x1) / 2, y1 + 0.1, H - 0.45, x1 - x0 + 0.3, 0.3, 0.45)
        window_row(st.U, M, x0 + 0.5, x1 - 0.4, y1, 3.45, 5.3, 7, face=1, cut=False)
        window_row(st.U, M, x0 + 0.4, x1 - 0.4, y1, 0.45, 2.45, 7, face=1, cut=False)
        hip_roof(S, M["roof"], INST_ROOF)
        # the hips' ridge caps
        g = INST_ROOF
        for a, b in (((g["x0"], g["y0"], g["z0"]), (g["xr"], g["ya"], g["z1"])),
                     ((g["x1"], g["y0"], g["z0"]), (g["xr"], g["ya"], g["z1"])),
                     ((g["x0"], g["y1"], g["z0"]), (g["xr"], g["yb"], g["z1"])),
                     ((g["x1"], g["y1"], g["z0"]), (g["xr"], g["yb"], g["z1"])),
                     ((g["xr"], g["ya"], g["z1"]), (g["xr"], g["yb"], g["z1"]))):
            S.beam(M["roof"], (a[0], a[1], a[2] + 0.04), (b[0], b[1], b[2] + 0.04), 0.16, 0.12)
    else:
        prof = walls or {}
        runs = {"S": ((x0, y0 + t / 2), (x1, y0 + t / 2)), "E": ((x1 - t / 2, y0), (x1 - t / 2, y1)),
                "N": ((x1, y1 - t / 2), (x0, y1 - t / 2)), "W": ((x0 + t / 2, y1), (x0 + t / 2, y0))}
        for i, (side, pr) in enumerate(sorted(prof.items())):
            a, b = runs[side]
            akit.broken_wall(S, M["marble"], M["marble"], a, b, t, pr, seed=st.seed + i, step=0.5, jitter=0.22)
        # a pier at each corner where two broken runs meet, to the lower of their ends
        for (s0, s1), (cx_, cy_) in ((("S", "E"), (x1, y0)), (("E", "N"), (x1, y1)), (("N", "W"), (x0, y1)),
                                     (("W", "S"), (x0, y0))):
            if s0 in prof and s1 in prof:
                h = min(prof[s0][-1][1], prof[s1][0][1]) - 0.15
                S.box(M["marble"], cx_ - math.copysign(0.3, cx_), cy_ - math.copysign(0.3, cy_), 0.0, 0.62, 0.62, h)
        # windows only below each wall's broken top
        front = prof.get("S")
        if front:
            top = lambda f: float(np.interp(f, [q[0] for q in front], [q[1] for q in front]))
            n = 9
            w = (I["px1"] - 0.3 - (I["px0"] + 0.3)) / n
            for i in range(n):
                u = I["px0"] + 0.3 + (i + 0.5) * w
                f = (u - x0) / (x1 - x0)
                hz = top(f) if 0 <= f <= 1 else 2.6
                if hz > 2.6:
                    st.S.box(M["dark"], u, y0 - 0.02, 0.45, w - 0.4, 0.04, 2.0)
                if hz > 5.4 and 0.05 < f < 0.95:
                    st.S.box(M["dark"], u, y0 - 0.02, 3.45, w - 0.4, 0.04, 1.85)
            for i in range(n + 1):
                u = I["px0"] + 0.3 + i * w
                f = (u - x0) / (x1 - x0)
                hz = top(min(1, max(0, f))) if 0 <= f <= 1 else 2.75
                S.box(M["trim"], u, y0 - 0.08, 0.35, 0.26, 0.16, max(0.3, min(hz, 5.5) - 0.6))
        S.box(M["sooty"], (x0 + x1) / 2, (y0 + y1) / 2, 0.0, x1 - x0 - 2 * t, y1 - y0 - 2 * t, 0.25)
        if roof:
            hip_roof(S, M["roof"], INST_ROOF)
    # the podium's side terraces
    for (a, b) in ((I["px0"], x0), (x1, I["px1"])):
        prof = (podium or {}).get("W" if a < 0 else "E") if mode != "intact" else None
        if mode == "intact" or prof is None:
            S.box(M["marble"], (a + b) / 2, (y0 + I["py1"]) / 2, 0, b - a, I["py1"] - y0, I["ph"])
            S.box(M["floor"], (a + b) / 2, (y0 + I["py1"]) / 2, I["ph"], b - a - 0.1, I["py1"] - y0 - 0.1, 0.04)
            xe = a + 0.12 if a < 0 else b - 0.12
            S.box(M["trim"], xe, (y0 + I["py1"]) / 2, I["ph"], 0.24, I["py1"] - y0, 0.4)
            for yy in np.arange(y0 + 0.6, I["py1"] - 0.2, 1.6):
                st.U.box(M["trim"], xe, yy, I["ph"] + 0.4, 0.24, 0.3, 0.25)
        else:
            xm = (a + b) / 2
            akit.broken_wall(S, M["marble"], M["marble"], (xm, y0), (xm, I["py1"]), b - a, prof, seed=st.seed + 7,
                             step=0.4, jitter=0.2)
    # the west wing
    wx0, wx1, wy0, wy1, wh = I["wx0"], I["wx1"], I["wy0"], I["wy1"], I["wh"]
    if mode == "intact" or wing is None:
        S.box(M["marble"], (wx0 + wx1) / 2, (wy0 + wy1) / 2, 0, wx1 - wx0, wy1 - wy0, wh)
        S.box(M["floor"], (wx0 + wx1) / 2, (wy0 + wy1) / 2, wh, wx1 - wx0 - 0.1, wy1 - wy0 - 0.1, 0.04)
        for (a, b, c, d) in ((wx0, wx1, wy0, wy0 + 0.25), (wx0, wx1, wy1 - 0.25, wy1), (wx0, wx0 + 0.25, wy0, wy1)):
            S.box(M["trim"], (a + b) / 2, (c + d) / 2, wh, b - a, d - c, 0.45)
        for xx in np.arange(wx0 + 0.35, wx1, 1.0):
            S.box(M["trim"], xx, wy0 + 0.12, wh + 0.45, 0.3, 0.25, 0.25)
            S.box(M["trim"], xx, wy1 - 0.12, wh + 0.45, 0.3, 0.25, 0.25)
        window_row(st.U, M, wx0 + 0.4, wx1 - 0.2, wy0, 0.5, 2.9, 4, cut=False)
        window_row(st.U, M, wx0 + 0.4, wx1 - 0.2, wy1, 0.5, 2.9, 4, face=1, cut=False)
    else:
        runs = {"S": ((wx0, wy0 + 0.2), (wx1, wy0 + 0.2)), "N": ((wx1, wy1 - 0.2), (wx0, wy1 - 0.2)),
                "W": ((wx0 + 0.2, wy1), (wx0 + 0.2, wy0)), "E": ((wx1 - 0.2, wy0), (wx1 - 0.2, wy1))}
        for i, (side, pr) in enumerate(sorted(wing.items())):
            a, b = runs[side]
            akit.broken_wall(S, M["marble"], M["marble"], a, b, 0.4, pr, seed=st.seed + 20 + i, step=0.35, jitter=0.2)
        S.box(M["sooty"], (wx0 + wx1) / 2, (wy0 + wy1) / 2, 0, wx1 - wx0 - 0.4, wy1 - wy0 - 0.4, 0.2)
    # the apse
    ah = I["ah"]
    if mode == "intact" or apse is None:
        pts = _apse_pts(I)
        S.prism(M["marble"], pts, 0.0, ah)
        S.prism(M["floor"], _apse_pts(I, shrink=0.1), ah, ah + 0.04)
        arc = _apse_pts(I, 16, shrink=0.12)
        for p, q in zip(arc, arc[1:]):
            a = math.degrees(math.atan2(q[1] - p[1], q[0] - p[0]))
            L = math.hypot(q[0] - p[0], q[1] - p[1])
            S.box(M["trim"], (p[0] + q[0]) / 2, (p[1] + q[1]) / 2, ah, L + 0.02, 0.24, 0.45, yaw=a)
            if arc.index(p) % 2 == 0:
                S.box(M["trim"], (p[0] + q[0]) / 2, (p[1] + q[1]) / 2, ah + 0.45, 0.28, 0.24, 0.25, yaw=a)
        # windows round its curved wall
        for a in np.linspace(-80, 80, 9):
            ar = math.radians(a)
            x, y = I["ax"] + I["ar"] * math.cos(ar), I["ay"] + I["ar"] * math.sin(ar)
            panel(st.U, M["dark"], x + 0.03 * math.cos(ar), y + 0.03 * math.sin(ar), 0.6, 0.7, 2.6, a)
            with st.U.at(x, y, 0.0, yaw=a):
                pilaster(st.U, M["trim"], 0.0, 0.5, 0.4, 0.24, 0.16, 3.4, 0)
    else:
        akit.ring_wall(S, M["marble"], M["marble"], I["ax"], I["ay"], I["ar"] - 0.2, 0.4, apse, seg=18,
                       seed=st.seed + 40, jitter=0.25)
        S.prism(M["sooty"], _apse_pts(I, shrink=0.3), 0.0, 0.2)
    return INST_ROOF


# ---------------------------------------------------------------- the Coliseum

COL = dict(cx=0.19, cy=1.18, R=6.06, H=5.6, t=0.55, Ri=5.45, Hi=6.3, Rd=5.3, Hd=5.25)
BAYS = [(180.0, 1.6, 3.9, 5.9), (0.0, 1.6, 3.9, 5.9), (226.0, 1.75, 3.5, 6.0), (314.0, 1.75, 3.5, 6.0)]
VAULT = dict(x0=-1.9, x1=1.9, y0=-7.95, y1=-4.3, hw=2.0, hv=3.35)


def bay_at(b):
    a, _rb, _hb, d = b
    C = COL
    return C["cx"] + d * math.cos(math.radians(a)), C["cy"] + d * math.sin(math.radians(a))


def coliseum(st, mode="intact", drum=None, inner=None, bays=None, vault=None, dome=True, gone=(), gap=None, floor=True):
    """CreBuild05, the Coliseum, drawn at hotspot 114, 150: a round marble
    drum 6.06 cells in radius and 5.6 high, with a crenellated parapet and
    a walkway round an inner ring, under a great ribbed copper dome 5.3 in
    radius rising to 11.6. Four small domed bays stand against the drum
    (west, east and the two front quarters) and a barrel-vaulted entrance
    with green tiles runs out south to y -7.95.

    Broken, drum and inner are ring_wall heights [(angle, h)], bays the
    bays kept (index: drum height) and vault the vault's walls' profiles.
    gap (a0, a1, [(angle, h)]) breaks the drum down between two angles,
    leaving the rest whole. floor False leaves out the arena floor, for a
    drum filled with rubble."""
    C, M, S = COL, st.M, st.S
    cx, cy, R, H, t = C["cx"], C["cy"], C["R"], C["H"], C["t"]
    if mode == "intact" or drum is None:
        rings = ((M["marble"], R - t, R, 0.0, H), (M["trim"], R - 0.1, R + 0.22, H - 0.45, H - 0.1),
                 (M["floor"], C["Ri"], R - 0.05, H - 0.1, H), (M["trim"], R - 0.3, R + 0.02, H, H + 0.45),
                 (M["marble"], C["Ri"] - 0.35, C["Ri"], H - 0.1, C["Hi"]))
        for m, r0, r1, z0, z1 in rings:
            if gap:
                arc_solid(S, m, cx, cy, r0, r1, z0, z1, gap[1], gap[0] + 360.0, seg=20)
            else:
                ring_solid(S, m, cx, cy, r0, r1, z0, z1, seg=24)
        if gap:
            ring_broken(S, M["marble"], cx, cy, R - t / 2, t, gap[2], seg=8, seed=st.seed + 3, a0=gap[0], a1=gap[1])
        # merlons where the picture still draws the parapet, which no cut need touch
        Rm = R - 0.14
        crenels(st.U, M["trim"], cx, cy, Rm, H + 0.45, w=0.3, step_deg=15.0,
                skip=lambda a: any(akit.inside_poly(P, *screen(st.r, cx + Rm * math.cos(math.radians(a)),
                                                                 cy + Rm * math.sin(math.radians(a)), H + 0.6)[:2])
                                   for P in gone))
        # dark arched windows round the drum
        for a in range(0, 360, 24):
            if gap and gap[0] - 6 < (a + 10) % 360 < gap[1] + 6:
                continue
            ar = math.radians(a + 10)
            x, y = cx + R * math.cos(ar), cy + R * math.sin(ar)
            panel(S, M["dark"], x + 0.03 * math.cos(ar), y + 0.03 * math.sin(ar), 1.6, 0.8, 2.2, a + 10)
    else:
        akit.ring_wall(S, M["marble"], M["marble"], cx, cy, R - t / 2, t, drum, seg=28, seed=st.seed + 3, jitter=0.25)
    if mode != "intact" and inner is not None:
        akit.ring_wall(S, M["marble"], M["marble"], cx, cy, C["Ri"] - 0.17, 0.35, inner, seg=28,
                       seed=st.seed + 5, jitter=0.2)
    # the arena floor inside, dark, for the holes to look down on
    if floor:
        S.cyl(M["sooty"], cx, cy, 0.0, R - t, 0.15, seg=32)
    # the dome
    if dome:
        bm = dome_shell(cx, cy, C["Hi"], C["Rd"], C["Hd"], 0.32, seg=24, rows=6, top=0.55, uv=(4.0, 2.0))
        st.add(bm, "dome", "dome")
        if mode == "intact":
            S.lathe(M["gold"], cx, cy, [(0.0, C["Hi"] + C["Hd"] - 0.12), (0.62, C["Hi"] + C["Hd"] - 0.12),
                                        (0.62, C["Hi"] + C["Hd"] + 0.15), (0.3, C["Hi"] + C["Hd"] + 0.45),
                                        (0.0, C["Hi"] + C["Hd"] + 0.6)], seg=12)
    # the bays
    for i, b in enumerate(BAYS):
        a, rb, hb, _d = b
        bx, by = bay_at(b)
        keep = None if mode == "intact" else (bays or {}).get(i)
        if mode != "intact" and keep is None:
            continue
        h = hb if keep in (None, True) else keep
        if keep in (None, True):
            S.cyl(M["marble"], bx, by, 0.0, rb, h, seg=18)
        else:
            rng = random.Random(st.seed + i)
            ring_broken(S, M["marble"], bx, by, rb - 0.23, 0.46,
                        [(k * 45.0, h * rng.uniform(0.55, 1.1)) for k in range(8)], seg=10, seed=st.seed + 70 + i)
            S.cyl(M["sooty"], bx, by, 0.0, rb - 0.2, 0.2, seg=12)
        if keep in (None, True):
            S.cyl(M["trim"], bx, by, h - 0.3, rb + 0.15, 0.3, seg=18)
            S.lathe(M["dome"], bx, by, [(0.0, h), (rb + 0.1, h), (rb * 0.98, h + 0.45), (rb * 0.75, h + 0.85),
                                        (rb * 0.4, h + 1.1), (0.0, h + 1.18)], seg=18)
            for k in range(-2, 3):
                aa = math.radians(a + 34 * k)
                x, y = bx + rb * math.cos(aa), by + rb * math.sin(aa)
                panel(S, M["dark"], x + 0.03 * math.cos(aa), y + 0.03 * math.sin(aa), 0.9, 0.55, 1.7, a + 34 * k)
    # the entrance vault
    V = VAULT
    vx0, vx1, vy0, vy1, hw, hv = V["x0"], V["x1"], V["y0"], V["y1"], V["hw"], V["hv"]
    if mode == "intact" or vault is None:
        S.box(M["marble"], vx0 + 0.25, (vy0 + vy1) / 2, 0, 0.5, vy1 - vy0, hw)
        S.box(M["marble"], vx1 - 0.25, (vy0 + vy1) / 2, 0, 0.5, vy1 - vy0, hw)
        _vault(S, M["roof"], V, vy0 + 0.35, vy1)
        # its front: a marble face round a dark arch
        S.box(M["trim"], (vx0 + vx1) / 2, vy0 + 0.2, 0, vx1 - vx0 + 0.3, 0.4, 0.35)
        S.box(M["trim"], vx0 + 0.3, vy0 + 0.2, 0, 0.6, 0.4, hv - 0.3)
        S.box(M["trim"], vx1 - 0.3, vy0 + 0.2, 0, 0.6, 0.4, hv - 0.3)
        S.box(M["trim"], (vx0 + vx1) / 2, vy0 + 0.2, hw + 0.3, vx1 - vx0, 0.4, hv - hw - 0.25)
        S.box(M["dark"], (vx0 + vx1) / 2, vy0 + 0.25, 0.0, vx1 - vx0 - 1.2, 0.3, hw + 0.3)
    else:
        for i, (xx, pr) in enumerate(((vx0 + 0.25, vault.get("W")), (vx1 - 0.25, vault.get("E")))):
            if pr:
                akit.broken_wall(S, M["marble"], M["marble"], (xx, vy0), (xx, vy1), 0.5, pr, seed=st.seed + 60 + i)
        if vault.get("roof"):
            _vault(S, M["roof"], V, vault["roof"][0], vault["roof"][1])
            S.box(M["trim"], vx0 + 0.3, vy0 + 0.2, 0, 0.6, 0.4, hv - 0.6)
            S.box(M["dark"], (vx0 + vx1) / 2, vy0 + 0.25, 0.0, vx1 - vx0 - 1.2, 0.3, hw + 0.3)


def _vault(S, mat, V, ya, yb, seg=10, t=0.2):
    """The barrel vault's tiled roof from ya to yb, a half-ellipse over its walls."""
    cx = (V["x0"] + V["x1"]) / 2
    rx, rz = (V["x1"] - V["x0"]) / 2 + 0.1, V["hv"] - V["hw"]
    v, f = [], []
    for i in range(seg + 1):
        a = math.pi * i / seg
        for (rr, zz) in ((rx, rz), (rx - t, rz - t)):
            for y in (ya, yb):
                v.append((cx + rr * math.cos(a), y, V["hw"] + zz * math.sin(a)))
    for i in range(seg):
        a, b = 4 * i, 4 * (i + 1)
        f += [(a, b, b + 1, a + 1), (a + 2, a + 3, b + 3, b + 2), (a, a + 2, b + 2, b), (a + 1, b + 1, b + 3, a + 3)]
    f += [(0, 1, 3, 2), (4 * seg, 4 * seg + 2, 4 * seg + 3, 4 * seg + 1)]
    S.solid(mat, v, f)


# ---------------------------------------------------------------- the Observatory

# a little wider and lower than the picture's tower, which looks thin from low angles
OBS = dict(cx=0.0, cy=0.58, Rt=5.95, ht=0.3, sh=2.74, r0=4.2, dr=0.15, Rr=5.0, Rd=4.0, slot=math.radians(250))


def obs_top(storeys, last=None):
    return OBS["ht"] + (storeys - 1) * OBS["sh"] + (OBS["sh"] if last is None else last)


def obs_storeys(storeys, last=None):
    """(z0, height, radius) of each storey."""
    O = OBS
    return [(O["ht"] + k * O["sh"], O["sh"] if (k < storeys - 1 or last is None) else last, O["r0"] + k * O["dr"])
            for k in range(storeys)]


def standing(gaps):
    """The stretches (a0, a1) of a ring, in degrees, left between gaps (a0, a1)."""
    if not gaps:
        return [(0.0, 360.0)]
    g = sorted([a0 % 360.0, a0 % 360.0 + (a1 - a0)] for a0, a1 in gaps)
    m = [g[0]]
    for a0, a1 in g[1:]:
        if a0 <= m[-1][1]:
            m[-1][1] = max(m[-1][1], a1)
        else:
            m.append([a0, a1])
    if len(m) > 1 and m[-1][1] - 360.0 >= m[0][0]:
        m[0] = [m[-1][0], max(m[-1][1], m[0][1] + 360.0)]
        m.pop()
    if m[0][1] - m[0][0] >= 360.0:
        return []
    return [(m[i][1], m[(i + 1) % len(m)][0] + (360.0 if i == len(m) - 1 else 0.0)) for i in range(len(m))]


class Breach:
    """The tower's front torn out in 3D, storey by storey: each storey in
    courses of stone, each course losing its own stretch of the ring round
    the front, lopsided and different from the course below, so the walls
    end in broken steps rather than one smooth arch. knots are (z, west,
    east), the degrees lost either side of the front at height z. The
    cornices break at uneven lengths, some running on over the breach."""

    def __init__(self, st, rows, knots, z_lo=1.0, seed=0, front=270.0, jit=9.0):
        rng = random.Random(st.seed + seed)
        self.rows, self.front = rows, front
        zs, wl, wr = (list(q) for q in zip(*knots))
        top = zs[-1]
        self.courses = []
        for k, (z0, h, r) in enumerate(rows):
            nc = 3 if h > 2.0 else 2
            for j in range(nc):
                c0, c1 = z0 + h * j / nc, z0 + h * (j + 1) / nc
                if c1 <= z_lo or c0 >= top:
                    continue
                zm = (max(c0, z_lo) + min(c1, top)) / 2
                a = float(np.interp(zm, zs, wl)) + rng.uniform(-jit, jit)
                b = float(np.interp(zm, zs, wr)) + rng.uniform(-jit, jit)
                if rng.random() < 0.3:
                    a += rng.uniform(6, 14) * rng.choice((-1, 1))
                if rng.random() < 0.3:
                    b += rng.uniform(6, 14) * rng.choice((-1, 1))
                self.courses.append((max(c0, z_lo), min(c1, top), front - max(2.0, a), front + max(2.0, b), k))
        # each cornice's ends: degrees it runs on over the breach (+) or stops short (-)
        self.ends = [(rng.uniform(-7, 11), rng.uniform(-7, 11)) for _ in rows]

    def __call__(self, z, r=None):
        for c0, c1, a0, a1, _k in self.courses:
            if c0 <= z < c1:
                return (a0, a1)
        if self.courses and z < self.courses[0][0]:
            return self.courses[0][2:4]
        return None

    def storey(self, z0, z1):
        cs = [c for c in self.courses if c[0] < z1 and c[1] > z0]
        return (min(c[2] for c in cs), max(c[3] for c in cs)) if cs else None

    def cornice(self, k):
        z0, h, _r = self.rows[k]
        g = self(z0 + h - 0.2)
        if g is None:
            return None
        a0, a1 = g[0] + self.ends[k][0], g[1] - self.ends[k][1]
        return (a0, a1) if a1 > a0 + 2.0 else None

    def cutter(self, cx, cy, r0, r1):
        bm = bmesh.new()
        for c0, c1, a0, a1, _k in self.courses:
            sector(bm, cx, cy, r0, r1, c0 - 0.01, c1 + 0.01, a0, a1)
        return bm

    def outline_px(self, st, cx, cy):
        """The breach's outline on the picture, up its west edge and down its east."""
        R = {k: r for k, (_z, _h, r) in enumerate(self.rows)}
        west, east = [], []
        for c0, c1, a0, a1, k in self.courses:
            for z in (c0, c1):
                west.append(screen(st.r, cx + R[k] * math.cos(math.radians(a0)),
                                   cy + R[k] * math.sin(math.radians(a0)), z)[:2])
                east.append(screen(st.r, cx + R[k] * math.cos(math.radians(a1)),
                                   cy + R[k] * math.sin(math.radians(a1)), z)[:2])
        return west + east[::-1]

    def edges(self, cx, cy):
        """A point on each broken edge of each course, for soot rising from them."""
        R = {k: r for k, (_z, _h, r) in enumerate(self.rows)}
        out = []
        for c0, c1, a0, a1, k in self.courses:
            for a in (a0, a1):
                out.append((cx + R[k] * math.cos(math.radians(a)), cy + R[k] * math.sin(math.radians(a)),
                            (c0 + c1) / 2))
        return out


def observatory(st, storeys=5, terrace=None, dome="intact", rim=True, last=None, breach=None, shut=True,
                gone_px=(), cornice_gaps=None, skip_win=()):
    """CreBuild06, the Observatory, drawn at hotspot 91, 179: a round
    terrace 5.95 cells in radius with a green floor and a studded rim,
    and on it a tower of five marble storeys, 2.74 cells each, widening
    from 4.2 to 4.8 in radius as the picture's tower does, each under a
    cornice, to a rim 5 wide at 14. On the rim a ribbed green dome 4 in
    radius, a slot down its south face, a raised barrel shutter over
    its top and a gold telescope out of the slot. The tower is some 6
    percent wider and lower than a pixel fit, for girth.

    Broken, storeys sets how many stand and the dome sits on the last,
    which is last cells high when it has partly come down. shut is the
    shutter's lower end as an angle up the dome (True for 52 degrees,
    False for none). terrace gives
    the rim's heights round the terrace as [(angle, h)]. dome is 'intact',
    'open' (the telescope gone) or None, and cuts break it further.
    breach (a Breach) gives the angles (a0, a1) each course of the face
    has lost: windows and pilasters are left out over the storey's whole
    loss and its cornice breaks where the Breach says, so only the
    marble wall needs cutting. cornice_gaps {storey: [(a0, a1)]} breaks
    cornices elsewhere too, and skip_win {(storey, window)} leaves windows
    out where the wall is to be holed."""
    O, M, S, U = OBS, st.M, st.S, st.U
    cx, cy, ht = O["cx"], O["cy"], O["ht"]
    U.cyl(M["marble"], cx, cy, 0.0, O["Rt"], ht, seg=28)
    U.cyl(M["green"], cx, cy, ht, O["Rt"] - 0.2, 0.03, seg=28)
    if terrace is None:
        ring_solid(U, M["trim"], cx, cy, O["Rt"] - 0.22, O["Rt"], ht, ht + 0.3, seg=28)
    else:
        rng = random.Random(st.seed + 2)
        for k in range(16):
            a = 360.0 * (k + 0.5) / 16
            h = float(np.interp(a, [q[0] for q in terrace], [q[1] for q in terrace])) * rng.uniform(0.6, 1.0)
            if h > 0.05:
                ar = math.radians(a)
                U.box(M["trim"], cx + (O["Rt"] - 0.11) * math.cos(ar), cy + (O["Rt"] - 0.11) * math.sin(ar), ht,
                      0.22, 2 * math.pi * O["Rt"] / 16 * rng.uniform(0.75, 1.02), h, yaw=a)
    for k in range(10):
        a = TAU * (k + 0.5) / 10
        if terrace is not None and float(np.interp(math.degrees(a), [q[0] for q in terrace],
                                                   [q[1] for q in terrace])) < 0.2:
            continue
        U.cyl(M["gold"], cx + (O["Rt"] - 0.5) * math.cos(a), cy + (O["Rt"] - 0.5) * math.sin(a), ht, 0.14, 0.12, seg=4)
    top = obs_top(storeys, last)
    rows = obs_storeys(storeys, last)
    # the storeys as one stepped wall, each a little wider than the one below
    r_in = O["r0"] - 0.45
    prof = [(r_in, ht)]
    for z0, h, r in rows:
        prof += [(r, z0), (r, z0 + h)]
    prof += [(r_in, top), (r_in, ht)]
    S.lathe(M["marble"], cx, cy, prof, seg=20)

    def lost(a, gone):
        if not gone:
            return False
        return gone[0] <= a <= gone[1] or gone[0] <= a + 360 <= gone[1]
    for ks, (z0, h, r) in enumerate(rows):
        wh = min(1.55, h - 0.75)
        gone = breach.storey(z0, z0 + h) if breach else None
        # the top storeys stay with the structure the cuts break
        B = S if (breach and z0 + h > top - 3.2) else U
        for j in range(8):
            a = 360.0 * (j + 0.5) / 8
            if lost(a, gone) or lost(a - 8, gone) or lost(a + 8, gone) or (ks, j) in skip_win:
                continue
            ar = math.radians(a)
            c_, r_ = screen(st.r, cx + r * math.cos(ar), cy + r * math.sin(ar), z0 + h * 0.5)[:2]
            if any(akit.inside_poly(P, c_, r_) for P in gone_px):
                continue
            if wh > 0.4:
                panel(B, M["dark"], cx + (r + 0.03) * math.cos(ar), cy + (r + 0.03) * math.sin(ar), z0 + 0.5,
                      0.8, wh, a)
            with B.at(cx + r * math.cos(ar), cy + r * math.sin(ar), z0, yaw=a):
                if B is S:
                    B.box(M["trim"], 0.1, 0.62, 0.25, 0.2, 0.24, h - 0.75)
                else:
                    pilaster(B, M["trim"], 0.0, 0.62, 0.25, 0.24, 0.2, h - 0.75, 0)
        # the storey's floor, under the rubble once the face is gone, and its cornice
        if not breach:
            U.cyl(M["sooty"], cx, cy, z0, r - 0.4, 0.2, seg=16)
        if breach and rim and ks == len(rows) - 1:
            # the rim's own ring stands in for the top cornice
            continue
        cg = breach.cornice(ks) if breach else None
        gaps = ([cg] if cg else []) + list((cornice_gaps or {}).get(ks, []))
        if not gaps:
            ring_solid(B, M["trim"], cx, cy, r - 0.1, r + 0.28, z0 + h - 0.35, z0 + h, seg=16 if breach else 20)
        for a0, a1 in standing(gaps):
            arc_solid(B, M["trim"], cx, cy, r - 0.1, r + 0.28, z0 + h - 0.35, z0 + h, a0, a1,
                      seg=max(2, int((a1 - a0) / 22)))
        # a cornice block or two hanging from the broken ends
        if breach and gaps:
            rng = random.Random(st.seed + 31 * ks)
            for a0, a1 in gaps:
                for end, way in ((a0, 1.0), (a1, -1.0)):
                    if rng.random() < 0.45:
                        p = Vector((cx + (r + 0.09) * math.cos(math.radians(end - way * 2.0)),
                                    cy + (r + 0.09) * math.sin(math.radians(end - way * 2.0)), z0 + h - 0.18))
                        e = end + way * rng.uniform(5.0, 9.0)
                        q = Vector((cx + (r + 0.2) * math.cos(math.radians(e)),
                                    cy + (r + 0.2) * math.sin(math.radians(e)), z0 + h - rng.uniform(0.7, 1.1)))
                        st.D.beam(M["trim"], tuple(p), tuple(q), 0.34, 0.3)
    if rim:
        ring_solid(S, M["trim"], cx, cy, O["Rd"] - 0.1, O["Rr"], top - 0.45, top, seg=24)
        ring_solid(S, M["floor"], cx, cy, O["Rd"] - 0.05, O["Rr"] - 0.05, top, top + 0.03, seg=24)
        S.lathe(M["gold"], cx, cy, [(O["Rd"] - 0.1, top), (O["Rd"] + 0.08, top), (O["Rd"] + 0.08, top + 0.2),
                                     (O["Rd"] - 0.1, top + 0.2), (O["Rd"] - 0.1, top)], seg=18)
    if dome in ("intact", "open"):
        s0, s1 = O["slot"] - 0.3, O["slot"] + 0.3
        bm = dome_shell(cx, cy, top + 0.1, O["Rd"], O["Rd"], 0.28, seg=16, rows=6, a0=s1, a1=s0 + TAU, top=0.3,
                        uv=(4.0, 1.6))
        st.add(bm, "dome", "dome")
        d = Vector((math.cos(O["slot"]), math.sin(O["slot"]), 0))
        if shut:
            shutter(st, top, d, s0=52.0 if shut is True else shut)
        # the slot's floor, dark inside
        S.cyl(M["dark"], cx, cy, top, O["Rd"] - 0.3, 0.12, seg=24)
        if dome == "intact":
            base = Vector((cx, cy, top + 1.0))
            tip = base + Vector((d.x * 3.6, d.y * 3.6, 2.6))
            S.cyl(M["gold"], base.x, base.y, base.z, 0.42, (tip - base).length, seg=12, axis=tuple(tip - base))
            S.cyl(M["gold"], tip.x, tip.y, tip.z, 0.62, 0.3, seg=16, axis=tuple(tip - base))
            S.box(M["gold"], cx, cy, top, 0.5, 0.5, 1.0)
    return top


def shutter(st, top, d, s0=52.0, s1=128.0, w=0.75, h=0.55, n=10, m=7):
    """The slot's shutter: a ribbed half barrel standing h proud of the
    dome, riding over its top from the slot side to the far side, with
    the dark drive box on its far end rising above the dome's outline."""
    O, M = OBS, st.M
    c = Vector((O["cx"], O["cy"], top + 0.1))
    up = Vector((0, 0, 1))
    side = Vector((-d.y, d.x, 0))
    verts, faces = [], []
    for k in range(n + 1):
        s = math.radians(s0 + (s1 - s0) * k / n)
        rad = d * math.cos(s) + up * math.sin(s)
        base = c + rad * (O["Rd"] - 0.15)
        hh = h + 0.15 + (0.07 if k % 2 else 0.0)
        for j in range(m):
            phi = math.pi * j / (m - 1)
            verts.append(tuple(base + side * (w * math.cos(phi)) + rad * (hh * math.sin(phi))))
    for k in range(n):
        for j in range(m - 1):
            a, b = k * m + j, (k + 1) * m + j
            faces.append((a, a + 1, b + 1, b))
    faces.append(tuple(range(m)))
    faces.append(tuple(range(n * m + m - 1, n * m - 1, -1)))
    st.U.solid(M["roof"], verts, faces)
    # the drive box at its far end, two gold blocks on it
    s = math.radians(s1 - 8)
    rad = d * math.cos(s) + up * math.sin(s)
    p = c + rad * (O["Rd"] + h * 0.5)
    yaw = math.degrees(math.atan2(d.y, d.x))
    tilt = 90 - math.degrees(s)
    with st.U.at(p.x, p.y, p.z, yaw=yaw):
        st.U.box(M["dark"], 0, 0, -0.2, 1.0, 2.0, 1.1, roll=-tilt)
        for sy in (-0.55, 0.55):
            st.U.box(M["gold"], 0.1, sy, 0.75, 0.35, 0.35, 0.3, roll=-tilt)


def obs_dome_ground(top):
    O = OBS

    def g(x, y):
        r = math.hypot(x - O["cx"], y - O["cy"])
        return top + 0.1 + math.sqrt(O["Rd"] ** 2 - r * r) if r < O["Rd"] else -99.0
    return g


def whole_gear(st, ground, col, row, R, spokes=4, lean=(0.1, -0.45, 0.9)):
    """A gold gear lying whole on a heap where the picture draws it,
    tilted between the heap's slope and the camera, lifted clear of it."""
    x, y, z = on_ground(st, col, row, ground, 4.0)
    e = 0.15
    gx = (ground(x + e, y) - ground(x - e, y)) / (2 * e)
    gy = (ground(x, y + e) - ground(x, y - e)) / (2 * e)
    n = Vector((-max(-3, min(3, gx)), -max(-3, min(3, gy)), 1.0)).normalized()
    ax = (n + Vector(lean).normalized()).normalized()
    q = ax.to_track_quat("Z", "Y" if abs(ax.z) < 0.99 else "X")
    lift = 0.0
    for k in range(16):
        a = TAU * k / 16
        p = Vector((x, y, z)) + q @ Vector((math.cos(a) * R * 1.1, math.sin(a) * R * 1.1, 0))
        g = ground(p.x, p.y)
        if g > -50:
            lift = max(lift, g - p.z)
    gear(st.D, st.M["gold"], x, y, z + lift + 0.06, R, axis=tuple(ax), spokes=spokes)
    st.soot.append((x, y, z + lift, R * 1.6, 0.45))


def flank(st, gap, skin, ground, beams, seed, top_lo=4.0):
    """A breach in a dome's flank: a dark gap through the shell, rubble
    lying round it, charred rafters across it."""
    sk = mound(st, skin, 0.35, ground=ground, zrel=0.3, cell=0.5, edge=0.45, rough=0.4, seed=seed,
               holes=[gap], sink=0.12, zr=(top_lo, top_lo + 5.0), pic=True)
    strew(st, skin, sk, 12, z_at=top_lo + 3.0, kinds={"chunk": 5, "stone": 3, "tile": 3}, tiles="shard",
          size=(0.5, 1.0), chunk=(0.35, 0.75), seed=seed + 1, tilt=10)
    for ln in beams:
        lying_beam(st, off_holes(st, ground, [gap]), ln, w=0.18)
    st.halos.append((gap, 1.2, 0.35, top_lo))
    return sk


def rim_blocks(st, top, region, n, seed):
    """White marble blocks strewn along the rim's walkway."""
    O = OBS
    rg = lambda x, y: (top + 0.03 if O["Rd"] + 0.05 < math.hypot(x - O["cx"], y - O["cy"]) < O["Rr"] - 0.05
                       else -99.0)
    strew(st, region, rg, n, z_at=top, kinds={"chunk": 7, "stone": 3}, chunk=(0.4, 0.85), stone=(0.3, 0.6),
          seed=seed, tilt=12)


def slot_debris(st, top, region, seed):
    """What fell into the open slot: a white column stump, charred beams
    across it, gold bits of the telescope's mounting and blocks."""
    O, M = OBS, st.M
    d = Vector((math.cos(O["slot"]), math.sin(O["slot"]), 0))
    side = Vector((d.y, -d.x, 0))
    base = Vector((O["cx"], O["cy"], top + 0.12))
    p = base + d * 1.7
    st.D.cyl(M["chip"], p.x, p.y, p.z, 0.38, 1.5, seg=8, axis=(0.25 * d.x, 0.25 * d.y, 1.0))
    for k, (s, l) in enumerate(((0.8, 2.6), (2.0, 2.4), (2.8, 1.8))):
        a = base + d * s
        st.D.beam(M["char"], tuple(a - side * (l / 2) + Vector((0, 0, 0.3))),
                  tuple(a + side * (l / 2) + Vector((0, 0, 0.5 + 0.3 * k))), 0.2)
    for k, s in enumerate((1.1, 2.4, 3.0)):
        a = base + d * s + side * (0.5 if k % 2 else -0.6)
        st.D.box(M["gold"], a.x, a.y, a.z, 0.4, 0.3, 0.3, yaw=37 * k)
    fl = lambda x, y: top + 0.12 if math.hypot(x - O["cx"], y - O["cy"]) < O["Rd"] - 0.3 else -99.0
    strew(st, region, fl, 16, z_at=top + 1.0, kinds={"chunk": 5, "stone": 3, "beam": 1}, chunk=(0.35, 0.7),
          seed=seed, pic=False)
    st.soot.append((p.x, p.y, top + 1.0, 2.0, 0.4))


def obs_floor(z, ye, gone, seed, n=20):
    """A storey's floor inside the tower broken off along a ragged line
    near y = ye, and ragged too where the face is gone round it."""
    O = OBS
    R = O["r0"] - 0.48
    rng = random.Random(seed)
    pts = []
    for k in range(n):
        a = 360.0 * k / n
        ar = math.radians(a)
        r = R
        if gone and (gone[0] <= a <= gone[1] or gone[0] <= a + 360 <= gone[1]):
            r = R - 0.15 - 0.45 * rng.random()
        s = math.sin(ar)
        if s < -1e-3:
            r = min(r, (ye - O["cy"]) / s + rng.uniform(-0.3, 0.3))
        pts.append((O["cx"] + r * math.cos(ar), O["cy"] + r * math.sin(ar)))
    return pts


def obs_face(st, rows, breach, edges, HF, ys, hs, n_heap, n_floor, seed=10, buried=1, xc=-0.4, wide=2.6):
    """The tower's south face torn out. Before it the fallen face lies
    heaped on the terrace and up through the breach, burying the lowest
    buried floors and meeting the next floor's broken edge. Above, each
    floor is broken off along a ragged edge further back the higher it
    is, charred joists sticking out of it, and its rubble runs on over
    the edge and down onto what lies below, slabs tipping after it, so
    the rubble is one cascade from under the rim to the ground. ys, hs
    give the heap's height across y, falling away either side of x = xc
    beyond wide. Returns the highest ground at a plan point."""
    O, M = OBS, st.M
    rng = random.Random(st.seed + seed)
    cx, cy = O["cx"], O["cy"]
    rin = O["r0"] - 0.48
    floors = []
    for k, (z0, _h, r) in enumerate(rows[1:]):
        ye = edges[min(k, len(edges) - 1)]
        poly = obs_floor(z0, ye, breach(z0, r), st.seed + seed + k)
        if k >= buried:
            st.U.prism(M["sooty"], poly, z0 - 0.3, z0)
        floors.append((z0, ye, poly))
    tg = lambda x, y: O["ht"] if math.hypot(x - cx, y - cy) < O["Rt"] - 0.1 else 0.0
    cap = floors[buried] if buried < len(floors) else None

    def ramp(x, y):
        h = float(np.interp(y, ys, hs)) - 1.1 * max(0.0, abs(x - xc) - wide)
        if cap and akit.inside_poly(cap[2], x, y):
            h = min(h, cap[0] - 0.35 - tg(x, y))
        return max(0.0, h)
    inner = O["r0"] - 0.5
    keep = lambda x, y: math.hypot(x - cx, y - cy) < inner and y < cy - 0.6
    f = mound(st, HF, ramp, ground=tg, cell=0.7, edge=0.9, rough=0.3, seed=seed, pic=True, zr=(0.0, max(hs)),
              keep=keep)
    grounds = [f]
    strew(st, HF, f, n_heap, z_at=max(hs) * 0.5, seed=seed + 1, stick=0.1, tilt=20, chunk=(0.5, 1.1),
          kinds={"chunk": 8, "stone": 2, "slab": 1, "beam": 2})
    for k in range(buried, len(floors)):
        z0, ye, poly = floors[k]
        gl = list(grounds)
        lower = lambda x, y, gl=gl: max([g(x, y) for g in gl] + [tg(x, y)])

        def fg(x, y, poly=poly, z0=z0, lower=lower):
            if akit.inside_poly(poly, x, y):
                return z0
            if math.hypot(x - cx, y - cy) > rin + 0.35 or y > cy:
                return -99.0
            z = z0 - 2.3 * float(poly_dist(poly, [(x, y)])[0])
            return z if z > lower(x, y) - 0.3 else -99.0

        def topf(x, y, poly=poly, ye=ye):
            if akit.inside_poly(poly, x, y):
                d = y - ye
                return 0.6 + 0.9 * max(0.0, min(1.0, d / 0.6)) * max(0.25, 1.0 - max(0.0, d - 1.4) / 2.2)
            return max(0.2, 0.6 - 0.3 * float(poly_dist(poly, [(x, y)])[0]))
        sp = [screen(st.r, x, y, z0)[:2] for x, y in poly]
        fm = mound(st, sp, topf, ground=fg, zrel=0.0, cell=0.7, edge=0.5, rough=0.45, seed=seed + 20 + k,
                   sink=0.08, zr=(z0, z0 + 3.0), keep=lambda x, y: True)
        grounds.append(fm)
        strew(st, circle_px(st, cx, cy, rin + 0.3, z0, k=20), fm, n_floor if k < buried + 2 else n_floor // 2,
              z_at=z0, kinds={"chunk": 6, "stone": 3, "slab": 2, "beam": 2}, chunk=(0.45, 0.95), seed=seed + 30 + k,
              pic=False, stick=0.1, tilt=18)
        # slabs of the broken floor tipping off its edge onto what lies below
        for j in range(3):
            x = cx + (j - 1) * 1.4 + rng.uniform(-0.4, 0.4)
            ys_ = [p[1] for p in poly if abs(p[0] - x) < 0.6 and p[1] < cy]
            if not ys_:
                continue
            yb = min(ys_)
            L, W = rng.uniform(1.0, 1.6), rng.uniform(0.6, 1.0)
            bx, by = x + rng.uniform(-0.3, 0.3), yb - L * 0.55
            bz = lower(bx, by)
            if bz < -50 or bz > z0 - 0.3 or math.hypot(bx - cx, by - cy) > rin + 0.3:
                continue
            a, b = Vector((x, yb + 0.35, z0 + 0.12)), Vector((bx, by, bz + 0.05))
            side = Vector((W / 2, 0, 0))
            st.D.slab(M["chip" if j != 1 else "sooty"], [tuple(a + side), tuple(a - side), tuple(b - side * 0.8),
                                                          tuple(b + side * 0.8)], 0.16)
        # joists snapped off at the edge, some sagging
        x0, x1 = min(p[0] for p in poly), max(p[0] for p in poly)
        for j in range(3):
            x = x0 + (x1 - x0) * (j + 0.5 + rng.uniform(-0.25, 0.25)) / 3
            ys_ = [p[1] for p in poly if abs(p[0] - x) < 0.6 and p[1] < cy]
            if not ys_:
                continue
            yb = min(ys_)
            a = (x, yb + 0.9, z0 - 0.17)
            b = (x + rng.uniform(-0.25, 0.25), yb - rng.uniform(0.3, 0.9), z0 - 0.17 - rng.uniform(0.0, 0.6))
            st.D.beam(M["char"], a, b, 0.2, 0.24)
        st.halos.append(([screen(st.r, x, y, z0 + 0.3)[:2] for x, y in poly if y < ye + 1.0] or sp, 0.9, 0.5,
                         z0 - 0.5))

    def top(x, y):
        return max(g(x, y) for g in grounds)
    return top


# ---------------------------------------------------------------- dressing

DENSITY = 0.45


def mound(st, poly, top, ground=0.0, zrel=None, cell=0.6, edge=1.0, rough=0.25, seed=0, holes=(), clip=None,
          sink=0.1, mat="rubble", pic=False, zr=None, keep=None, bottom=False, where=None):
    """A closed rubble mound lying on ground (a height, or a function of a
    plan point giving -99 off it) where the stage picture draws it inside
    poly, each point tested where it lands at zrel above the ground (by
    default 0.6 of its height). top is its height above the ground, a
    number or a function of the plan point, reached edge cells in from
    the outline. The rim sinks sink below the ground, and every edge left
    open over a hole or off the ground is skirted down to it, so no edge
    hangs in the air. Points keep(x, y) accepts are in whatever the
    outline says. With bottom the mound is closed underneath, sink below
    its ground, for a heap with nothing under it. where(col, row) limits
    it further to what the picture draws there. Returns the surface as a
    ground (-99 off it)."""
    rng = random.Random(st.seed + seed)
    gfun = ground if callable(ground) else (lambda x, y, g=float(ground): g)
    topf = top if callable(top) else (lambda x, y, t=float(top): t)
    if zr is None:
        g0 = 0.0 if callable(ground) else float(ground)
        zr = (g0, g0 + (float(top) if not callable(top) else 4.0))
    P = st.plan(poly, zr[0]) + st.plan(poly, zr[1])
    x0, x1 = min(q[0] for q in P) - cell, max(q[0] for q in P) + cell
    y0, y1 = min(q[1] for q in P) - cell, max(q[1] for q in P) + cell
    nx, ny = max(2, int((x1 - x0) / cell) + 1), max(2, int((y1 - y0) / cell) + 1)
    X = np.array([x0 + i * cell for i in range(nx + 1)])
    Y = np.array([y0 + j * cell for j in range(ny + 1)])
    state = np.zeros((nx + 1, ny + 1), int)      # 0 off, 1 out, 2 in
    G = np.zeros((nx + 1, ny + 1))
    T = np.zeros((nx + 1, ny + 1))
    for i, x in enumerate(X):
        for j, y in enumerate(Y):
            g = gfun(x, y)
            if g < -50:
                continue
            G[i, j] = g
            t = topf(x, y)
            T[i, j] = t
            if holes:
                c, r = screen(st.r, x, y, g)
                if any(akit.inside_poly(h, c, r) for h in holes):
                    continue
            c, r = screen(st.r, x, y, g + (zrel if zrel is not None else 0.6 * t))
            ok = t > 0.02 and akit.inside_poly(poly, c, r) and (clip is None or clip(x, y))
            if ok and where is not None:
                ok = where(c, r)
            if ok and pic:
                ok = st.pic.solid(c, r)
            if keep is not None and t > 0.02 and keep(x, y):
                ok = True
            state[i, j] = 2 if ok else 1
    jit = {(i, j): rng.uniform(-1, 1) for i in range(nx + 1) for j in range(ny + 1)}

    def heights():
        ins = np.argwhere(state == 2)
        outs = np.argwhere(state == 1)
        D = np.full(state.shape, 1e3)
        if len(ins) and len(outs):
            pi = np.stack([X[ins[:, 0]], Y[ins[:, 1]]], 1)
            po = np.stack([X[outs[:, 0]], Y[outs[:, 1]]], 1)
            for k in range(0, len(pi), 256):
                q = pi[k:k + 256]
                d = np.sqrt(((q[:, None, :] - po[None]) ** 2).sum(2)).min(1)
                D[ins[k:k + 256, 0], ins[k:k + 256, 1]] = np.maximum(0.0, d - cell * 0.5)
        Hh = np.zeros(state.shape)
        for i, j in ins:
            x, y = X[i], Y[j]
            h = T[i, j] * min(1.0, D[i, j] / edge) ** 0.7
            h *= 1.0 + rough * noise.noise(Vector((x * 0.8 + seed * 3.1, y * 0.8, 0.5)))
            h += jit[i, j] * rough * 0.3 * min(1.0, h)
            Hh[i, j] = max(0.05, h)
        return Hh
    Hh = heights()
    # each point tested again where its own surface lands, so the heap's
    # foot keeps inside the outline the picture draws
    for _ in range(2):
        moved = False
        for i, j in np.argwhere(state == 2):
            if keep is not None and keep(X[i], Y[j]):
                continue
            c, r = screen(st.r, X[i], Y[j], G[i, j] + Hh[i, j])
            ok = akit.inside_poly(poly, c, r) and (not pic or st.pic.solid(c, r))
            if not ok:
                state[i, j] = 1
                moved = True
        if not moved:
            break
        Hh = heights()
    Z = np.full(state.shape, np.nan)
    idx, verts = {}, []
    for i in range(nx + 1):
        for j in range(ny + 1):
            if state[i, j] == 0:
                continue
            x, y, g = X[i], Y[j], G[i, j]
            if state[i, j] == 2:
                z = g + Hh[i, j]
            else:
                z = g - sink
                # the rim pulled halfway in toward the heap, so it spills no further than the outline
                nb = [(i + a, j + b) for a in (-1, 0, 1) for b in (-1, 0, 1)
                      if (a or b) and 0 <= i + a <= nx and 0 <= j + b <= ny and state[i + a, j + b] == 2]
                if nb:
                    x += 0.5 * (sum(X[a] for a, _ in nb) / len(nb) - x)
                    y += 0.5 * (sum(Y[b] for _, b in nb) / len(nb) - y)
                    gg = gfun(x, y)
                    z = (gg if gg > -50 else g) - sink
            Z[i, j] = z
            idx[i, j] = len(verts)
            verts.append((x, y, z))
    faces = []
    for i in range(nx):
        for j in range(ny):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            have = [k for k in q if k in idx]
            if len(have) < 3 or not any(state[k] == 2 for k in have):
                continue
            if len(have) == 3:
                faces.append(tuple(idx[k] for k in have))
            elif rng.random() < 0.5:
                faces += [(idx[q[0]], idx[q[1]], idx[q[2]]), (idx[q[0]], idx[q[2]], idx[q[3]])]
            else:
                faces += [(idx[q[0]], idx[q[1]], idx[q[3]]), (idx[q[1]], idx[q[2]], idx[q[3]])]
    # skirt every open edge that stands above the ground
    count = {}
    for f in faces:
        for k in range(len(f)):
            e = (f[k], f[(k + 1) % len(f)])
            count[e] = count.get(e, 0) + 1
    rev = {v: k for k, v in idx.items()}
    low = {}

    def under(v):
        if v not in low:
            x, y, z = verts[v]
            i, j = rev[v]
            low[v] = len(verts)
            verts.append((x, y, min(z - 0.05, G[i, j] - sink)))
        return low[v]
    tops = list(faces)
    for (a, b), n in list(count.items()):
        if n != 1 or (b, a) in count:
            continue
        if state[rev[a]] != 2 and state[rev[b]] != 2 and not bottom:
            continue
        faces.append((a, under(a), under(b), b))
    if bottom:
        faces += [tuple(under(v) for v in reversed(f)) for f in tops]
    used = sorted({v for f in faces for v in f})
    remap = {v: k for k, v in enumerate(used)}
    st.D.mesh(st.M[mat], [verts[v] for v in used], [tuple(remap[v] for v in f) for f in faces])

    def surface(x, y):
        fi, fj = (x - x0) / cell, (y - y0) / cell
        i, j = int(math.floor(fi)), int(math.floor(fj))
        if not (0 <= i < nx and 0 <= j < ny):
            return -99.0
        q = [(i, j), (i + 1, j), (i, j + 1), (i + 1, j + 1)]
        if not all(state[k] == 2 for k in q):
            zs = [Z[k] for k in q if state[k] == 2]
            return float(np.mean(zs)) - 0.1 if len(zs) >= 2 else -99.0
        u, v = fi - i, fj - j
        return float(Z[i, j] * (1 - u) * (1 - v) + Z[i + 1, j] * u * (1 - v) + Z[i, j + 1] * (1 - u) * v
                     + Z[i + 1, j + 1] * u * v)
    st.grounds.append(surface)
    return surface


def field(st, x0, x1, y0, y1, fn, ground=0.0, cell=0.5, mat="rubble", seed=0, rough=0.3):
    """A rubble field filling a rectangle edge to edge, its height above
    ground fn(x, y), closed underneath and skirted down at its edges,
    which stand inside the walls round it. Returns the surface as a
    ground (-99 off it)."""
    rng = random.Random(st.seed + seed)
    nx, ny = max(1, int(math.ceil((x1 - x0) / cell))), max(1, int(math.ceil((y1 - y0) / cell)))
    X, Y = np.linspace(x0, x1, nx + 1), np.linspace(y0, y1, ny + 1)
    Z = np.zeros((nx + 1, ny + 1))
    for i, x in enumerate(X):
        for j, y in enumerate(Y):
            h = fn(x, y)
            h *= 1.0 + rough * noise.noise(Vector((x * 0.8 + seed * 3.1, y * 0.8, 0.5)))
            h += rng.uniform(-1, 1) * rough * 0.3 * min(1.0, h)
            Z[i, j] = ground + max(0.05, h)
    idx = lambda i, j: i * (ny + 1) + j
    verts = [(X[i], Y[j], Z[i, j]) for i in range(nx + 1) for j in range(ny + 1)]
    faces = []
    for i in range(nx):
        for j in range(ny):
            a, b, c, d = idx(i, j), idx(i + 1, j), idx(i + 1, j + 1), idx(i, j + 1)
            faces += [(a, b, c), (a, c, d)] if rng.random() < 0.5 else [(a, b, d), (b, c, d)]
    ring = ([idx(i, 0) for i in range(nx + 1)] + [idx(nx, j) for j in range(1, ny + 1)]
            + [idx(i, ny) for i in range(nx - 1, -1, -1)] + [idx(0, j) for j in range(ny - 1, 0, -1)])
    low = {}
    for v in ring:
        low[v] = len(verts)
        verts.append((verts[v][0], verts[v][1], ground - 0.1))
    for k in range(len(ring)):
        a, b = ring[k], ring[(k + 1) % len(ring)]
        faces.append((b, a, low[a], low[b]))
    faces.append(tuple(low[v] for v in ring))
    st.D.solid(st.M[mat], verts, faces)

    def surface(x, y):
        fi, fj = (x - x0) / (x1 - x0) * nx, (y - y0) / (y1 - y0) * ny
        if not (0 <= fi <= nx and 0 <= fj <= ny):
            return -99.0
        i, j = min(int(fi), nx - 1), min(int(fj), ny - 1)
        u, v = fi - i, fj - j
        return float(Z[i, j] * (1 - u) * (1 - v) + Z[i + 1, j] * u * (1 - v) + Z[i, j + 1] * (1 - u) * v
                     + Z[i + 1, j + 1] * u * v)
    st.grounds.append(surface)
    return surface


def rubble_where(st, lum=60.0, rad=2, thr=0.3):
    """Where the stage picture draws rubble rather than green copper:
    pixels neither green nor dark, spread a little so the chips join into
    patches. A test of a picture point (col, row)."""
    P = st.pic
    r, g, b = P.rgb[..., 0], P.rgb[..., 1], P.rgb[..., 2]
    green = (g / np.maximum(r, 1.0) > 1.15) & (g > 25)
    m = (P.alpha & ~green & (0.299 * r + 0.587 * g + 0.114 * b > lum)).astype(float)
    acc = np.zeros_like(m)
    for dy in range(-rad, rad + 1):
        for dx in range(-rad, rad + 1):
            acc += np.roll(np.roll(m, dy, 0), dx, 1)
    mask = acc / (2 * rad + 1) ** 2 > thr

    def fn(c, rw):
        ci, ri = int(c), int(rw)
        return 0 <= ri < P.h and 0 <= ci < P.w and bool(mask[ri, ci])
    return fn


def heap(st, poly, top, z_at=None, cell=0.65, edge=0.9, pic=True, mat="rubble", seed=0, z0=0.0, rough=0.25, **kw):
    """A mound on a flat floor at z0, its outline read at height z_at."""
    z_at = top * 0.5 + z0 if z_at is None else z_at
    return mound(st, poly, top, ground=z0, zrel=z_at - z0, cell=cell, edge=edge, rough=rough, seed=seed, mat=mat,
                 pic=pic, **kw)


def strew(st, poly, ground, n, z_at=0.5, kinds=None, size=(0.5, 1.1), length=(0.9, 2.2), tiles="slab", seed=0,
          stick=0.12, pic=True, stone=(0.35, 0.7), chunk=(0.45, 0.95), boulder=(0.7, 1.2), tilt=14, q=0.9):
    """Pieces lying on ground inside an outline on the stage picture:
    marble blocks and chips, tile slabs and charred rafters."""
    P = st.plan(poly, z_at)
    n = max(1, int(round(n * DENSITY)))
    return akit.cover(st.D, debris_kinds(st.M, tiles), st.r, P, ground, n,
                      kinds or {"slab": 3, "stone": 2, "chunk": 6, "beam": 2, "boulder": 1, "tile": 1},
                      pic=st.pic if pic else None, size=size, length=length, seed=st.seed + seed, lift=0.02,
                      tilt=tilt, stick=stick, grow=1, stone=stone, chunk=chunk, boulder=boulder, jumble=0.08, q=q)


def rafters(st, hole, g, n=5, seed=0, z_at=6.5, drop=1.6):
    """Charred rafters bared in a roof hole: running down the roof's slope
    from the hole's upper rim, snapped, their ends sagging into it."""
    rng = random.Random(st.seed + seed)
    P = st.plan(hole, z_at)
    xs, ys = [p[0] for p in P], [p[1] for p in P]
    cx, cy = sum(xs) / len(xs), sum(ys) / len(ys)
    for _ in range(n):
        x = rng.uniform(min(xs), max(xs))
        if not akit.inside_poly(P, x, cy):
            x = cx + rng.uniform(-0.6, 0.6)
        # a rafter runs across the hole, rests on its rim and sags inside
        a = (x, max(ys) + 0.3, 0)
        b = (x + rng.uniform(-0.5, 0.5), min(ys) - 0.2, 0)
        za, zb = g(a[0], a[1]), g(b[0], b[1])
        if za < -50 or zb < -50:
            continue
        mid = ((a[0] + b[0]) / 2 + rng.uniform(-0.2, 0.2), (a[1] + b[1]) / 2,
               (za + zb) / 2 - drop * rng.uniform(0.4, 1.0))
        w = rng.uniform(0.16, 0.24)
        st.D.beam(st.M["char"], (a[0], a[1], za - 0.05), mid, w, w * 1.2)
        if rng.random() < 0.6:
            end = (mid[0] + rng.uniform(-0.3, 0.3), mid[1] - rng.uniform(0.4, 1.0), mid[2] + rng.uniform(0.3, 1.0))
            st.D.beam(st.M["char"], mid, end, w * 0.9, w)


def flaps(st, hole, roofg, ground, n=3, seed=0, z_at=6.6, mat="slab", t=0.1):
    """Sections of a roof or dome still hinged on a hole's rim and
    sagging into it, onto the rubble below."""
    rng = random.Random(st.seed + seed)
    P = st.plan(hole, z_at)
    cx, cy = sum(q[0] for q in P) / len(P), sum(q[1] for q in P) / len(P)
    for _ in range(n * 4):
        if n <= 0:
            break
        rx, ry = P[rng.randrange(len(P))]
        d = Vector((rx - cx, ry - cy, 0))
        if d.length < 0.3:
            continue
        d.normalize()
        hx, hy = rx + d.x * 0.2, ry + d.y * 0.2
        hz = roofg(hx, hy)
        if hz < -50:
            continue
        L = rng.uniform(1.0, 1.9)
        tx, ty = hx - d.x * L, hy - d.y * L
        tz = ground(tx, ty)
        tz = hz - 1.4 if tz < -50 else tz
        tz = max(tz + 0.12, hz - L * 1.2)
        side = Vector((-d.y, d.x, 0)) * rng.uniform(0.45, 0.8)
        a, b = Vector((hx, hy, hz - 0.02)), Vector((tx, ty, tz))
        st.D.slab(st.M[mat], [tuple(a + side), tuple(a - side), tuple(b - side * 0.75), tuple(b + side * 0.75)], t)
        n -= 1


def soot_at(st, co):
    f = 1.0
    if st.soot_mode == "product":
        for (x, y, z, rad, k) in st.soot:
            dz = co.z - z
            d2 = (co.x - x) ** 2 + (co.y - y) ** 2 + (dz * (0.45 if dz > 0 else 1.2)) ** 2
            f *= 1.0 - k * math.exp(-d2 / (rad * rad))
    b = noise.noise(Vector((co.x * 0.7 + st.seed % 97, co.y * 0.7, co.z * 0.9)))
    f *= 1.0 + 0.16 * b
    if st.grime:
        # smoke stains, stretched up the walls
        n2 = noise.noise(Vector((co.x * 0.45 + 13.1, co.y * 0.45 + 7.7, co.z * 0.15)))
        f *= 1.0 - st.grime * max(0.0, 0.3 + n2)
    return max(0.12, min(1.15, f))


def poly_dist(P, Q):
    """Distance from each point of Q (n by 2) to polygon P, 0 inside it."""
    A = np.asarray(P, float)
    B = np.roll(A, -1, axis=0)
    Q = np.asarray(Q, float)
    d = B - A
    L2 = np.maximum((d ** 2).sum(1), 1e-9)
    t = np.clip(((Q[:, None, :] - A[None]) * d[None]).sum(2) / L2, 0, 1)
    proj = A[None] + t[..., None] * d[None]
    dist = np.sqrt(((Q[:, None, :] - proj) ** 2).sum(2)).min(1)
    ay, by = A[:, 1][None], B[:, 1][None]
    qx, qy = Q[:, 0][:, None], Q[:, 1][:, None]
    dy = np.where(np.abs(by - ay) < 1e-12, 1e-12, by - ay)
    xint = (B[:, 0] - A[:, 0])[None] * (qy - ay) / dy + A[:, 0][None]
    inside = (((ay > qy) != (by > qy)) & (qx < xint)).sum(1) % 2 == 1
    dist[inside] = 0.0
    return dist


def edge_dist(P, q):
    """Distance from point q to the nearest edge of polygon P."""
    A = np.asarray(P, float)
    B = np.roll(A, -1, axis=0)
    d = B - A
    t = np.clip(((np.asarray(q, float) - A) * d).sum(1) / np.maximum((d ** 2).sum(1), 1e-9), 0, 1)
    return float(np.sqrt(((A + t[:, None] * d - np.asarray(q, float)) ** 2).sum(1)).min())


def halo_factor(st, co):
    """Soot round each breach: points within a halo's radius of its rim
    on the picture, and above its zmin, darkened to its floor at the rim,
    the radius wandering with noise so the blotch is ragged."""
    f = np.ones(len(co))
    if not st.halos or not len(co):
        return f
    hx, hy = st.r["sprite"]["hotspot"]
    sc = np.stack([hx + 16.0 * co[:, 0], hy - SY * co[:, 1] - SZ * co[:, 2]], 1)
    nz = np.array([noise.noise(Vector((p[0] * 0.9 + 5.3, p[1] * 0.9, p[2] * 0.6))) for p in co])
    for h in st.halos:
        # darkest for a band round the rim, then fading out over rad; a
        # fifth entry is the highest z it reaches
        poly, rad, floor, zmin = h[:4]
        zmax = h[4] if len(h) > 4 else 1e9
        d = poly_dist(poly, sc) / 16.0
        core = 0.35 * rad * (1.0 + 0.6 * nz)
        t = np.clip((d - core) / (rad * (0.85 + 0.4 * nz)), 0, 1)
        k = floor + (1 - floor) * (t * t * (3 - 2 * t))
        k = np.where((co[:, 2] >= zmin) & (co[:, 2] <= zmax), k, 1.0)
        f = np.minimum(f, k) if st.soot_mode == "min" else f * k
    return f


def blob_factor(st, co):
    """The soot blobs as one factor per point, the darkest blob winning,
    no darker than the stage's soot floor."""
    f = np.ones(len(co))
    for (x, y, z, rad, k) in st.soot:
        dz = co[:, 2] - z
        d2 = (co[:, 0] - x) ** 2 + (co[:, 1] - y) ** 2 + (dz * np.where(dz > 0, 0.45, 1.2)) ** 2
        f = np.minimum(f, 1.0 - k * np.exp(-d2 / (rad * rad)))
    return np.maximum(f, st.soot_floor)


def near_halos(st, reach=2.2):
    """Whether a point lies within reach cells of a halo's rim on the picture."""
    def fn(co):
        c, r = screen(st.r, co.x, co.y, co.z)
        return any(poly_dist(h[0], [(c, r)])[0] / 16.0 < reach for h in st.halos)
    return fn


def densify(ob, step, near=None, up=False):
    """Splits long edges so vertex colour can grade across a face: only
    edges whose middle near(point) accepts, and with up only those of
    faces looking upward."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for _ in range(4):
        long = []
        for e in bm.edges:
            if e.calc_length() <= step:
                continue
            if up and not any(f.normal.z > 0.3 for f in e.link_faces):
                continue
            if near is not None and not near((e.verts[0].co + e.verts[1].co) / 2):
                continue
            long.append(e)
        if not long:
            break
        bmesh.ops.subdivide_edges(bm, edges=long, cuts=1, use_grid_fill=True)
        bm.normal_update()
    bm.to_mesh(ob.data)
    bm.free()


def cut(st, obs, polys, mode="DIFFERENCE", zlo=-3.0, zhi=40.0):
    """Cuts objects along the classic camera's line of sight through
    outlines on the stage picture: DIFFERENCE takes away what the picture
    shows gone, INTERSECT keeps only what it shows standing."""
    hx, hy = st.r["sprite"]["hotspot"]
    bm = bmesh.new()
    for poly in polys:
        n = len(poly)
        lo = [bm.verts.new(((c - hx) / 16.0, (hy - rw) / SY - 0.5 * zlo, zlo)) for c, rw in poly]
        hi = [bm.verts.new(((c - hx) / 16.0, (hy - rw) / SY - 0.5 * zhi, zhi)) for c, rw in poly]
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
        mod.use_hole_tolerant = True
        lo = [min(v.co[i] for v in ob.data.vertices) - 0.02 for i in range(3)]
        hi = [max(v.co[i] for v in ob.data.vertices) + 0.02 for i in range(3)]
        n0 = akit.tris(ob)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        if os.environ.get("G2_TRIS"):
            print("G2_CUT", st.name, ob.name, n0, akit.tris(ob), flush=True)
        # a cut only takes away, so anything outside the old bounds is the solver's mistake
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bad = [v for v in bm.verts if any(v.co[i] < lo[i] or v.co[i] > hi[i] for i in range(3))]
        if bad:
            print("G2_CUT_STRAY", st.name, ob.name, len(bad), flush=True)
            bmesh.ops.delete(bm, geom=bad, context="VERTS")
            bm.to_mesh(ob.data)
        bm.free()
    bpy.data.objects.remove(cutter, do_unlink=True)


def cut_solid(st, obs, cbm, mode="DIFFERENCE"):
    """Cuts objects with a cutter built in 3D (a bmesh of closed solids)."""
    me = bpy.data.meshes.new("cutter3d")
    cbm.to_mesh(me)
    cutter = bpy.data.objects.new("cutter3d", me)
    bpy.context.collection.objects.link(cutter)
    for ob in obs:
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = ob
        ob.select_set(True)
        mod = ob.modifiers.new("cut3d", "BOOLEAN")
        mod.operation, mod.solver, mod.object = mode, "EXACT", cutter
        mod.use_self = True
        mod.use_hole_tolerant = True
        lo = [min(v.co[i] for v in ob.data.vertices) - 0.02 for i in range(3)]
        hi = [max(v.co[i] for v in ob.data.vertices) + 0.02 for i in range(3)]
        bpy.ops.object.modifier_apply(modifier=mod.name)
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bad = [v for v in bm.verts if any(v.co[i] < lo[i] or v.co[i] > hi[i] for i in range(3))]
        if bad:
            print("G2_CUT_STRAY", st.name, ob.name, len(bad), flush=True)
            bmesh.ops.delete(bm, geom=bad, context="VERTS")
            bm.to_mesh(ob.data)
        bm.free()
    bpy.data.objects.remove(cutter, do_unlink=True)


def sector(bm, cx, cy, r0, r1, z0, z1, a0, a1, step=6.0):
    """A closed stretch of an annulus into bm, angles in degrees."""
    n = max(1, int(math.ceil((a1 - a0) / step)))
    ring = []
    for k in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * k / n)
        c, s_ = math.cos(a), math.sin(a)
        ring.append([bm.verts.new((cx + rr * c, cy + rr * s_, zz)) for rr, zz in ((r0, z0), (r1, z0), (r1, z1), (r0, z1))])
    fs = []
    for k in range(n):
        a, b = ring[k], ring[k + 1]
        for q in range(4):
            fs.append(bm.faces.new((a[q], a[(q + 1) % 4], b[(q + 1) % 4], b[q])))
    fs.append(bm.faces.new(ring[0]))
    fs.append(bm.faces.new(ring[-1][::-1]))
    bmesh.ops.recalc_face_normals(bm, faces=fs)


def slit_box(bm, p, u, v, w, L, d):
    """A thin closed box into bm centred on p: L along u, w along v, d deep along u x v."""
    p, u, v = Vector(p), Vector(u).normalized(), Vector(v).normalized()
    n = u.cross(v).normalized()
    vs = [bm.verts.new(p + u * (a * L / 2) + v * (b * w / 2) + n * (c * d / 2))
          for a in (-1, 1) for b in (-1, 1) for c in (-1, 1)]
    fs = [bm.faces.new([vs[i] for i in f]) for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6),
                                                     (0, 2, 6, 4), (1, 5, 7, 3))]
    bmesh.ops.recalc_face_normals(bm, faces=fs)


def settle(st, obs, tol=0.1, maxdrop=1.6, crumb=0.6):
    """Loose pieces that touch nothing are dropped straight down onto
    whatever lies under them, or removed if that is further than maxdrop;
    structure scraps a cut left floating are removed if smaller than crumb."""
    from mathutils.bvhtree import BVHTree
    for _pass in range(2):
        bms = []
        verts, polys, owner = [], [], []
        islands = []          # (object index, [face indices], kind)
        for oi, ob in enumerate(obs):
            bm = bmesh.new()
            bm.from_mesh(ob.data)
            bm.verts.ensure_lookup_table()
            bm.faces.ensure_lookup_table()
            bms.append(bm)
            seen = [False] * len(bm.faces)
            base = len(verts)
            verts += [v.co.copy() for v in bm.verts]
            fid = {}
            for f in bm.faces:
                if seen[f.index]:
                    continue
                stack, group = [f], []
                seen[f.index] = True
                while stack:
                    g = stack.pop()
                    group.append(g.index)
                    for v in g.verts:
                        for h in v.link_faces:
                            if not seen[h.index]:
                                seen[h.index] = True
                                stack.append(h)
                for gi in group:
                    fid[gi] = len(islands)
                islands.append((oi, group, ob["kind"]))
            for f in bm.faces:
                polys.append([base + v.index for v in f.verts])
                owner.append(fid[f.index])
        tree = BVHTree.FromPolygons(verts, polys)
        moved = 0
        moves = {}
        drops = set()
        for ii, (oi, group, kind) in enumerate(islands):
            bm = bms[oi]
            vs = {v for gi in group for v in bm.faces[gi].verts}
            if min(v.co.z for v in vs) < tol:
                continue
            pts = [v.co for v in vs] + [bm.faces[gi].calc_center_median() for gi in group]
            if any(owner[idx] != ii for p in pts for (_l, _n, idx, _d) in tree.find_nearest_range(p, tol)):
                continue
            if kind != "D":
                area = sum(bm.faces[gi].calc_area() for gi in group)
                if area < crumb:
                    drops.add(ii)
                continue
            # how far it can fall before something stops it
            best = min(v.co.z for v in vs)
            for v in vs:
                p = v.co.copy()
                for _ in range(8):
                    hit = tree.ray_cast(p, Vector((0, 0, -1)), 40.0)
                    if hit[0] is None:
                        break
                    if owner[hit[2]] == ii:
                        p = hit[0] + Vector((0, 0, -1e-3))
                        continue
                    best = min(best, v.co.z - hit[0].z)
                    break
            if best > maxdrop:
                drops.add(ii)
            else:
                moves[ii] = max(0.0, best - 0.03)
        todo = [(ii, oi, list({v for gi in group for v in bms[oi].faces[gi].verts}))
                for ii, (oi, group, kind) in enumerate(islands) if ii in moves or ii in drops]
        for ii, oi, vs in todo:
            bm = bms[oi]
            group = islands[ii][1]
            if ii in drops:
                bmesh.ops.delete(bm, geom=vs, context="VERTS")
                print("G2_SETTLE_DROP", st.name, obs[oi].name, len(group), flush=True)
            else:
                for v in vs:
                    v.co.z -= moves[ii]
                moved += 1
                print("G2_SETTLE_MOVE", st.name, obs[oi].name, "%.2f" % moves[ii], flush=True)
        for oi, ob in enumerate(obs):
            bms[oi].to_mesh(ob.data)
            bms[oi].free()
        if not moves and not drops:
            break


def finish(st, out, render=True, scale=2, steps=None):
    """Objects from both builders, structure densified so soot can grade
    across it, the planned cuts made, every vertex coloured, one model."""
    steps = steps or {}
    obs = []
    for B, kind in ((st.S, "S"), (st.D, "D"), (st.U, "U")):
        for ob in B.objects():
            ob["cmat"] = ob.name.split(".")[0]
            ob["kind"] = kind
            obs.append(ob)
    for ob in st.extra:
        ob["kind"] = "S"
        obs.append(ob)
    for ob in obs:
        uvs = ob.data.uv_layers
        if not len(uvs):
            uvs.new(name="UVMap")
        uvs[0].name = "UVMap"
        while len(uvs) > 1:
            uvs.remove(uvs[1])
        if ob["kind"] == "S" and ob["cmat"] in steps:
            sp = steps[ob["cmat"]]
            if isinstance(sp, tuple):
                densify(ob, 2.0, up=sp[2])
                densify(ob, sp[0], near=near_halos(st, sp[1]) if sp[1] else None, up=sp[2])
            else:
                densify(ob, sp)
    if os.environ.get("G2_TRIS"):
        for o in sorted(obs, key=akit.tris, reverse=True)[:6]:
            print("G2_PRECUT", st.name, o["kind"], o.name, akit.tris(o), flush=True)
    for keys, polys, mode, zlo in st.cuts:
        targets = [o for o in obs if o["kind"] == "S" and o["cmat"] in keys]
        cut(st, targets, polys, mode, zlo=zlo)
    for keys, cbm in st.cuts3d:
        targets = [o for o in obs if o["kind"] == "S" and o["cmat"] in keys]
        cut_solid(st, targets, cbm)
    obs = [o for o in obs if len(o.data.polygons)]
    if st.settle:
        settle(st, obs)
        obs = [o for o in obs if len(o.data.polygons)]
    for ob in obs:
        if ob["cmat"] in st.dissolve and ob["kind"] == "S":
            bm = bmesh.new()
            bm.from_mesh(ob.data)
            n0 = len(bm.faces)
            bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(0.5), verts=bm.verts[:], edges=bm.edges[:],
                                     delimit={"UV"})
            print("G2_DISSOLVE", st.name, ob.name, n0, len(bm.faces), flush=True)
            bm.to_mesh(ob.data)
            bm.free()
    if os.environ.get("G2_TRIS"):
        for o in sorted(obs, key=akit.tris, reverse=True)[:14]:
            print("G2_PART", st.name, o["kind"], o.name, akit.tris(o), flush=True)
    for ob in obs:
        cm = st.M[ob["cmat"]]
        k = st.tone.get(ob["cmat"], 1.0)
        base = cm.lin * k
        sooty = ob["cmat"] not in st.nosoot
        grain = 0.22 if ob["kind"] == "D" else 0.06
        kit.paint_points(ob, lambda co, n, base=base, sooty=sooty, grain=grain: base * (
            (soot_at(st, co) if sooty else 1.0) * (1.0 + grain * noise.noise(co * 2.7))))
        halos = sooty and st.halos and ob["kind"] != "D"
        if (halos or (sooty and st.soot_mode == "min")) and len(ob.data.vertices):
            co = np.empty(len(ob.data.vertices) * 3)
            ob.data.vertices.foreach_get("co", co)
            co = co.reshape(-1, 3)
            hf = halo_factor(st, co) if halos else np.ones(len(co))
            if st.soot_mode == "min":
                hf = np.minimum(hf, blob_factor(st, co))
            if os.environ.get("G2_TRIS"):
                print("G2_HALO", ob.name, len(hf), "min %.2f mean %.2f" % (hf.min(), hf.mean()), flush=True)
            ca = ob.data.color_attributes.get("Col")
            c = np.empty(len(ca.data) * 4, np.float32)
            ca.data.foreach_get("color", c)
            c = c.reshape(-1, 4)
            c[:, :3] *= hf[:, None].astype(np.float32)
            ca.data.foreach_set("color", c.ravel())
        kit.shade(ob, 35)
    glb = os.path.join(out, st.name + ".glb")
    ob = hk.finish(obs, glb, {"feature": st.name, "family": "creon"})
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    dims = tuple(ob.dimensions)
    lo_z = min(v.co.z for v in ob.data.vertices)
    print("G2_BUILT", st.name, "tris", tris, "size %.2f x %.2f x %.2f" % dims, "minz %.2f" % lo_z, flush=True)
    if render:
        spr = kit.Sprite(st.name)
        rd = os.path.join(out, "renders")
        hk.renders(ob, rd, st.name, spr.path, (spr.hx, spr.hy), scale=scale)
        m = kit.fit(spr, os.path.join(rd, st.name + "_classic.png"))
        if m:
            print("G2_FIT", st.name, "iou %.3f cover %.3f spill %.3f" % (m["iou"], m["cover"], m["spill"]),
                  "render", np.round(m["render"], 3), "drawing", np.round(spr.mean(), 3), flush=True)
    return tris


# ---------------------------------------------------------------- the intacts, for checking

def build_intact(name, out, render=True):
    st = Stage(name)
    if name == "CreBuild04":
        institute(st)
    elif name == "CreBuild05":
        coliseum(st)
    else:
        observatory(st)
    return finish(st, out, render)


STAGES = {}


def stage(name):
    def deco(fn):
        STAGES[name] = fn
        return fn
    return deco


# ---------------------------------------------------------------- stage helpers

def clip_px(st, poly, z, rect=None, circle=None):
    """An outline on the picture with its plan at height z kept inside a
    rectangle (x0, x1, y0, y1) or a circle (cx, cy, R)."""
    out = []
    for c, rw in poly:
        x, y = px(st.r, c, rw, z)
        if rect:
            x, y = min(max(x, rect[0]), rect[1]), min(max(y, rect[2]), rect[3])
        if circle:
            cx, cy, R = circle
            d = math.hypot(x - cx, y - cy)
            if d > R:
                x, y = cx + (x - cx) * R / d, cy + (y - cy) * R / d
        out.append(screen(st.r, x, y, z)[:2])
    return out


def rect_px(st, x0, x1, y0, y1, z):
    return [screen(st.r, x, y, z)[:2] for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]


def circle_px(st, cx, cy, R, z, k=28, a0=0.0, a1=360.0):
    return [screen(st.r, cx + R * math.cos(math.radians(a)), cy + R * math.sin(math.radians(a)), z)[:2]
            for a in np.linspace(a0, a1, k, endpoint=abs(a1 - a0) < 359)]


def middle(st, poly, z):
    P = st.plan(poly, z)
    return sum(p[0] for p in P) / len(P), sum(p[1] for p in P) / len(P)


def on_ground(st, col, row, ground, z=1.0):
    """The plan point and height where a piece drawn at (col, row) lies on ground."""
    x = y = 0.0
    for _ in range(8):
        x, y = px(st.r, col, row, z)
        g = ground(x, y)
        z = 0.0 if g < -50 else g
    return x, y, z


def off_holes(st, ground, holes, z=None):
    """ground, except where its surface lands inside a hole on the picture."""
    def g(x, y):
        h = ground(x, y)
        if h < -50:
            return h
        c, r = screen(st.r, x, y, h)
        return -99.0 if any(akit.inside_poly(P, c, r) for P in holes) else h
    return g


def smooth01(u):
    u = max(0.0, min(1.0, u))
    return u * u * (3 - 2 * u)


def jagged(st, polys, amp=2.5, step=4.0):
    return [akit.jag(p, amp, step, st.seed + i) for i, p in enumerate(polys)]


def leaners(st, sticks, w=0.2):
    """Charred beams from one world point to another."""
    for a, b in sticks:
        st.D.beam(st.M["char"], a, b, w, w * 1.15)


# ---------------------------------------------------------------- the Institute's stages

T04A = dict(marble="#948d80", floor="#8e8375", roof="#567054", chip="#b2aa9c", stone="#767067", sooty="#3e3a34",
            slab="#587458", char="#553628")
T04B = dict(marble="#979083", floor="#8a8072", roof="#44593f", chip="#aaa294", stone="#706a61", sooty="#38342f",
            slab="#4f6c51", grit="#35322d", char="#553628")


@stage("CreBuild04a")
def cre04a(out, render=True):
    """The Damaged Institute: the intact block with four breaches in the
    roof (a big one by the back of the ridge, a small one west of the
    ridge and the two front corners), rafters bared in them and rubble on
    the floor below, soot round each, the front corners' walls broken down
    to the upper storey, the front-left one's rubble heaped against the
    facade from the ground up to the broken storey, a heap on the west
    wing's terrace and another on the apse, their parapets broken."""
    st = Stage("CreBuild04a", tone=T04A)
    st.grime = 0.12     # the drawing is as bright as its intact but for the breaches
    # the darkest soot source wins, so halos and blobs never multiply to black
    st.soot_mode, st.soot_floor, st.nosoot, st.settle = "min", 0.5, ("dark", "char"), True
    g = institute(st)
    I = INST
    H1 = [(128, 6), (150, 3), (186, 3), (214, 7), (222, 26), (212, 52), (203, 76), (194, 94), (176, 95), (160, 80),
          (146, 58), (132, 34)]
    H2 = [(120, 110), (134, 105), (150, 109), (153, 126), (138, 134), (122, 129)]
    H3 = [(84, 168), (104, 160), (130, 163), (150, 175), (152, 197), (120, 201), (96, 198), (84, 186)]
    H4 = [(170, 160), (186, 150), (206, 152), (226, 164), (236, 184), (226, 198), (196, 201), (174, 193)]
    # a fifth, small breach on the west eave, over the wall top
    H5 = [(86, 44), (97, 38), (110, 42), (116, 54), (111, 67), (99, 72), (88, 66)]
    holes = jagged(st, (H1, H2, H3, H4, H5), 2.5)
    st.cuts.append((("roof", "trim"), holes, "DIFFERENCE", 5.0))
    st.cuts.append((("marble",), jagged(st, ([(86, 46), (98, 42), (104, 52), (100, 66), (90, 64)],), 2.0),
                    "DIFFERENCE", 5.3))
    H3w = [(76, 172), (154, 172), (154, 196), (140, 203), (126, 198), (112, 207), (98, 201), (86, 206), (76, 200)]
    H4w = [(168, 180), (240, 180), (240, 196), (228, 200), (214, 194), (200, 201), (186, 197), (168, 200)]
    walls = jagged(st, (H3w, H4w), 1.5)
    st.cuts.append((("marble", "trim", "dark"), walls, "DIFFERENCE", 3.25))
    W1 = [(6, 96), (18, 80), (44, 76), (62, 88), (64, 112), (44, 126), (14, 122)]
    A1 = [(268, 98), (286, 82), (306, 88), (318, 112), (318, 140), (306, 156), (284, 162), (268, 146)]
    st.cuts.append((("trim",), jagged(st, (W1,), 2.0), "DIFFERENCE", 3.3))
    st.cuts.append((("trim",), jagged(st, (A1,), 2.0), "DIFFERENCE", 4.1))
    # soot round every breach: wide round the big back one, a narrow black
    # rim round the front two so the tile between them stays green, and the
    # wall breaches' soot kept off the roof
    for h, (rad, fl) in zip(holes, ((1.8, 0.2), (1.1, 0.25), (0.6, 0.3), (0.6, 0.3), (0.8, 0.3))):
        st.halos.append((h, rad, fl, 5.4))
    for h in walls:
        st.halos.append((h, 1.0, 0.5, 2.6, 5.95))
    for h, z in ((W1, I["wh"] - 0.3), (A1, I["ah"] - 0.3)):
        st.halos.append((akit.jag(h, 2.0, 4.0, st.seed), 1.4, 0.5, z))
    # rubble on the upper floor under each breach, seen through it
    inner = (I["mx0"] + 0.5, I["mx1"] - 0.5, I["my0"] + 0.5, I["my1"] - 0.5)
    floor = 3.1
    roofg = lambda x, y: hip_z(g, x, y)
    clip = lambda x, y: inner[0] < x < inner[1] and inner[2] < y < inner[3]
    # the front two heaped right up to the broken front wall
    front = (inner[0], inner[1], I["my0"] + I["wt"] + 0.05, inner[3])
    fclip = lambda x, y: front[0] < x < front[1] and front[2] < y < front[3]
    for i, (h, top, n) in enumerate(((H1, 3.0, 24), (H2, 3.1, 8), (H3, 2.6, 12), (H4, 2.6, 12), (H5, 3.0, 8))):
        za = floor + top * 0.8
        fr = i in (2, 3)
        hp = clip_px(st, holes[i], za, rect=front if fr else inner)
        f = heap(st, hp, top, z_at=za, z0=floor, cell=0.6, edge=0.6 if i >= 2 else 1.1, seed=10 + i, pic=False,
                 clip=fclip if fr else clip)
        strew(st, hp, f, n, z_at=za, kinds={"slab": 2, "chunk": 4, "beam": 3, "tile": 3}, seed=20 + i, pic=False)
        rafters(st, h, roofg, n=(7, 2, 4, 4, 3)[i], seed=30 + i, z_at=6.6, drop=1.4 if i not in (1, 4) else 0.9)
        flaps(st, h, roofg, f, n=(4, 1, 2, 2, 1)[i], seed=35 + i)
        x, y = middle(st, h, 6.6)
        st.soot.append((x, y, 6.0, 1.6 if i in (2, 3) else 2.0, 0.3))
    # pieces lying on the roof round the breaches
    ground = off_holes(st, roofg, holes, 6.6)
    for i, (R, n) in enumerate((([(118, 0), (230, 0), (236, 40), (222, 104), (150, 104), (116, 60)], 22),
                                ([(80, 150), (160, 150), (160, 192), (80, 192)], 10),
                                ([(165, 140), (242, 140), (242, 192), (165, 192)], 10),
                                ([(80, 34), (120, 34), (124, 76), (80, 76)], 8))):
        strew(st, R, ground, n, z_at=6.6, kinds={"stone": 3, "chunk": 4, "slab": 2, "tile": 3},
              size=(0.5, 1.0), seed=40 + i)
    # the front-left corner's rubble heaped against the facade, from the
    # ground up to the broken storey
    y0 = I["my0"]
    F1 = [(62, 200), (74, 184), (96, 184), (120, 190), (146, 196), (150, 222), (144, 242), (122, 251), (88, 251),
          (68, 240), (58, 222)]

    def ramp(x, y):
        h = float(np.interp(y, [y0 - 2.3, y0 - 1.2, y0 - 0.2, y0 + 1.5], [0.7, 2.0, 3.4, 3.6]))
        return h * max(0.35, min(1.0, 1.0 - (abs(x + 3.1) - 1.6) / 1.8))
    f1 = mound(st, F1, ramp, ground=0.0, zrel=None, cell=0.55, edge=0.9, rough=0.3, seed=50, pic=True,
               zr=(0.0, 3.6))
    F2 = [(176, 224), (232, 222), (236, 234), (212, 242), (184, 240)]
    f2 = heap(st, F2, 0.6, z_at=0.3, edge=0.6, seed=51)
    strew(st, F1, f1, 50, z_at=1.8, seed=52, stick=0.2, kinds={"slab": 3, "chunk": 6, "beam": 2, "boulder": 2,
                                                                  "tile": 2})
    strew(st, F2, f2, 10, z_at=0.3, seed=53, size=(0.4, 0.8))
    for (x, y, z) in ((-3.0, -6.9, 4.4), (2.9, -6.9, 4.8)):
        st.soot.append((x, y, z, 1.8, 0.35))
    st.soot.append((-2.6, -8.0, 1.0, 2.2, 0.3))
    # the west wing's terrace and the apse's floor heaped
    w1 = heap(st, W1, 1.1, z_at=I["wh"] + 0.6, z0=I["wh"] + 0.04, edge=1.0, seed=60)
    a1 = heap(st, A1, 1.3, z_at=I["ah"] + 0.7, z0=I["ah"] + 0.04, edge=1.1, seed=61)
    strew(st, W1, w1, 20, z_at=I["wh"] + 0.6, seed=62)
    strew(st, A1, a1, 24, z_at=I["ah"] + 0.7, seed=63)
    for poly, z in ((W1, I["wh"]), (A1, I["ah"])):
        x, y = middle(st, poly, z + 0.5)
        st.soot.append((x, y, z + 0.5, 2.0, 0.45))
    return finish(st, out, render, steps={"roof": (1.2, 1.5, True), "marble": 3.0, "floor": 2.5})


@stage("CreBuild04b")
def cre04b(out, render=True):
    """The Destroyed Institute: the ruin with the roof fallen in but for
    the west plane, cracked across with rubble on its south half, a strip
    along the east eave and the north-east hip, which has dropped and
    tilted; the walls standing to ragged tops (highest on the west,
    lowest at the front-left corner), the block heaped with rubble to half
    its height and strewn with the fallen roof's slabs, the wing and the
    apse down to stumps under heaps of their own, and the front-left
    corner's heap leaning on the facade. Outside the north wall's low
    middle the rubble lies heaped to half the wall's height, a slope of
    blocks and slabs falling to a toe two cells out, under the picture's
    top edge, with slabs leaning on the wall above it."""
    st = Stage("CreBuild04b", tone=T04B)
    st.grime = 0.25
    st.nosoot, st.settle = ("dark", "char"), True
    I = INST
    walls = {"S": [(0, 3.2), (0.25, 3.9), (0.45, 4.8), (0.7, 5.3), (1, 5.7)],
             "E": [(0, 5.5), (0.2, 5.9), (0.55, 6.0), (0.75, 5.1), (1, 6.0)],
             "N": [(0, 6.0), (0.25, 4.6), (0.55, 3.4), (0.8, 4.4), (1, 5.8)],
             "W": [(0, 6.0), (0.55, 6.0), (0.8, 5.2), (1, 3.9)]}
    # the wing keeps its paved terrace, heaped only at its west end
    g = institute(st, "broken", walls=walls, roof=True,
                  apse=[(0, 0.9), (30, 1.8), (60, 2.4), (90, 3.2), (92, 0.0), (268, 0.0), (270, 1.6), (300, 2.2),
                        (330, 1.2)],
                  podium={"W": [(0, 2.2), (0.35, 3.0), (0.7, 2.4), (1, 2.8)], "E": [(0, 2.6), (0.5, 3.0), (1, 2.2)]})

    def top_of(side, f):
        pr = walls[side]
        return float(np.interp(min(1.0, max(0.0, f)), [q[0] for q in pr], [q[1] for q in pr]))
    x0_, x1_, y0_, y1_ = I["mx0"], I["mx1"], I["my0"], I["my1"]
    topN = lambda x: top_of("N", (x1_ - x) / (x1_ - x0_))
    topS = lambda x: top_of("S", (x - x0_) / (x1_ - x0_))
    topE = lambda y: top_of("E", (y - y0_) / (y1_ - y0_))
    topW = lambda y: top_of("W", (y1_ - y) / (y1_ - y0_))
    # the roof kept: the west plane with a ragged east edge, the east eave
    K1 = [(96, 18), (118, 20), (140, 30), (150, 46), (146, 70), (152, 96), (136, 108), (122, 120), (124, 150),
          (138, 166), (128, 186), (98, 192)]
    K3 = [(222, 88), (246, 82), (250, 192), (232, 198), (226, 150)]
    st.cuts.append((("roof",), [akit.jag(K1, 4.0, 4.0, st.seed), akit.jag(K3, 2.0, 4.0, st.seed + 1)], "INTERSECT",
                    -3.0))
    # a crack running down across the west plane
    crack = [(150, 66), (138, 84), (124, 98), (110, 118), (99, 142), (96, 146), (103, 120), (118, 96), (132, 80),
             (147, 62)]
    st.cuts.append((("roof",), [crack], "DIFFERENCE", 4.0))
    # the north-east hip dropped onto the wall tops, its inner corner sagging
    A, B, C, Dp = (5.05, 6.0, 6.0), (2.6, 5.6, 5.35), (5.05, 2.6, 5.95), (3.4, 3.3, 4.7)
    st.D.slab(st.M["roof"], [A, B, Dp, C], 0.2)
    st.D.beam(st.M["char"], (4.6, 5.4, 5.7), (3.0, 3.0, 3.6), 0.2)
    st.halos.append((akit.jag(crack, 1.0, 4.0, st.seed), 0.9, 0.35, 5.0))
    # the block heaped inside from wall to wall, higher under the fallen
    # east half and up to just under the north wall's broken top, so no
    # pit opens along any wall
    t = I["wt"]
    za = 1.8
    inside = rect_px(st, I["mx0"] + t, I["mx1"] - t, I["my0"] + t, I["my1"] - t, za)
    sm = lambda u: (lambda c: c * c * (3 - 2 * c))(max(0.0, min(1.0, u)))

    def fill(x, y):
        e = max(0.0, min(1.0, (x + 0.6) / 3.0)) * max(0.0, min(1.0, (y - y0_ - 1.2) / 2.0))
        h = 2.8 + 1.2 * e
        wn = sm(1.0 - (y1_ - t - y) / 2.0)
        h += (topN(x) - 0.6 - h) * wn
        for near, cap in (((y - y0_ - t) < 0.6, topS(x)), ((x - x0_ - t) < 0.6, topW(y)), ((x1_ - t - x) < 0.6, topE(y))):
            if near:
                h = min(h, cap - 0.5)
        return h
    hin = field(st, x0_ + t - 0.15, x1_ - t + 0.15, y0_ + t - 0.15, y1_ - t + 0.15, fill, ground=0.2, cell=0.8,
                seed=10, rough=0.3)
    strew(st, inside, hin, 56, z_at=za, kinds={"stone": 3, "chunk": 6, "beam": 3, "boulder": 2}, length=(1.4, 3.4),
          seed=12, stick=0.25, pic=False)
    # slabs and blocks strewn into the fill along the north wall
    strew(st, rect_px(st, x0_ + t, x1_ - t, y1_ - 2.2, y1_ - t, 3.8), hin, 18, z_at=3.8,
          kinds={"tile": 2, "chunk": 4, "stone": 2}, size=(0.7, 1.3), seed=17, tilt=24, pic=False)
    # the fallen roof's slabs lying tilted across the central and east heap
    east = rect_px(st, -0.6, I["mx1"] - t, I["my0"] + t, I["my1"] - t, 3.2)
    strew(st, east, hin, 56, z_at=3.2, kinds={"tile": 1}, size=(1.0, 1.9), seed=13, tilt=28, pic=False)
    st.soot += [(0.6, 0.0, 3.0, 5.0, 0.45), (3.0, 2.0, 5.5, 3.0, 0.35), (-3.0, -4.0, 4.0, 2.5, 0.35)]
    # beams fallen from the walls into the heap
    leaners(st, [((I["mx0"] + 0.3, 2.0, 5.9), (-1.4, 0.6, hin(-1.4, 0.6))),
                 ((I["mx1"] - 0.3, -2.0, 5.9), (2.2, -1.2, hin(2.2, -1.2))),
                 ((1.5, I["my1"] - 0.3, 4.3), (0.8, 3.1, hin(0.8, 3.1))),
                 ((I["mx0"] + 0.3, -3.0, 5.4), (-2.0, -2.2, hin(-2.0, -2.2)))])
    # what is left of the west plane: rubble on its south half
    roofg = lambda x, y: hip_z(g, x, y)
    K1s = [(98, 120), (124, 120), (124, 150), (138, 166), (128, 186), (100, 190)]
    rs = mound(st, K1s, 0.45, ground=roofg, zrel=0.3, cell=0.6, edge=0.5, rough=0.4, seed=14, zr=(5.5, 7.0))
    strew(st, K1s, rs, 22, z_at=6.4, kinds={"stone": 3, "chunk": 5, "tile": 2}, size=(0.5, 1.0), seed=45)
    strew(st, K1, off_holes(st, roofg, [crack]), 8, z_at=6.6, kinds={"chunk": 3, "stone": 2}, seed=46)
    # the apse heaped over its stumps and spilling east
    apse = [screen(st.r, x, y, 1.2)[:2] for x, y in _apse_pts(I, 16, shrink=-1.5)]
    ah = lambda x, y: 2.4 if math.hypot(x - I["ax"], y - I["ay"]) < I["ar"] - 0.3 else 0.9
    ag = mound(st, apse, ah, ground=0.0, zrel=1.0, cell=0.8, edge=1.2, rough=0.35, seed=20, zr=(0.0, 2.6), pic=True)
    strew(st, apse, ag, 34, z_at=0.8, seed=22)
    st.soot.append((I["ax"] + 2.0, I["ay"], 2.0, 3.0, 0.4))
    # the wing's terrace: its west end heaped, its parapet broken there,
    # stones scattered over the paving
    wz = I["wh"] + 0.04
    WH = [(2, 92), (14, 80), (38, 78), (54, 88), (58, 112), (46, 128), (10, 128)]
    st.cuts.append((("trim",), jagged(st, (WH,), 2.0), "DIFFERENCE", I["wh"] - 0.3))
    wg = heap(st, WH, 1.1, z_at=wz + 0.5, z0=wz, edge=1.0, seed=30, cell=0.6,
              clip=lambda x, y: I["wx0"] + 0.1 < x < I["wx1"] and I["wy0"] + 0.1 < y < I["wy1"] - 0.1)
    strew(st, WH, wg, 22, z_at=wz + 0.5, seed=32)
    pave = lambda x, y: wz if (I["wx0"] + 0.3 < x < I["wx1"] and I["wy0"] + 0.3 < y < I["wy1"] - 0.3) else -99.0
    strew(st, rect_px(st, I["wx0"] + 0.4, I["wx1"] - 0.2, I["wy0"] + 0.4, I["wy1"] - 0.4, wz), pave, 14, z_at=wz,
          kinds={"stone": 3, "chunk": 3}, chunk=(0.3, 0.6), seed=33)
    x, y = middle(st, WH, wz + 0.5)
    st.soot.append((x, y, wz + 0.5, 2.0, 0.4))
    st.halos.append((akit.jag(WH, 2.0, 4.0, st.seed), 1.2, 0.5, I["wh"] - 0.4))
    # rubble heaped against the low middle of the north wall's outside, from
    # half the wall's height down at about 45 degrees to a toe two cells out,
    # kept under the picture's top edge, its face built of blocks and slabs
    # with more leaning on the wall above it
    xa, xb = -3.4, 4.4
    frame = lambda y, z: SY * y + SZ * z <= st.r["sprite"]["hotspot"][1] - 2.0

    def nr(x, y):
        d = max(0.0, y - y1_)
        side = sm(min(x - xa, xb - x) / 1.4)
        h0 = min(2.3, max(1.9, 0.5 * topN(x)))
        return max(0.0, h0 * side - 1.1 * d)
    ns = field(st, xa, xb, y1_ - 0.25, y1_ + 2.15, nr, ground=0.0, cell=0.52, seed=15, rough=0.35)
    spill = rect_px(st, xa + 0.4, xb - 0.4, y1_ + 0.15, y1_ + 1.7, 1.0)
    strew(st, spill, ns, 60, z_at=1.0, seed=16, pic=False, stick=0.1, tilt=26, size=(0.6, 1.1),
          chunk=(0.5, 0.95), kinds={"chunk": 8, "slab": 3, "stone": 3, "tile": 2})
    rng = random.Random(st.seed + 70)
    for k in range(7):
        x = xa + 0.9 + (xb - xa - 1.8) * (k + rng.uniform(0.2, 0.8)) / 7
        yb = y1_ + rng.uniform(0.5, 0.8)
        zb = ns(x, yb)
        zt = min(zb + rng.uniform(0.9, 1.4), topN(x) - 0.25)
        if zb < -50 or zt < zb + 0.5:
            continue
        w = rng.uniform(0.35, 0.55)
        mat = st.M[rng.choice(("chip", "stone", "slab", "chip"))]
        if k % 3 == 2:
            # a block resting on the heap against the wall
            s = rng.uniform(0.55, 0.8)
            st.D.box(mat, x, y1_ + s * 0.5 + 0.03, ns(x, y1_ + s * 0.5) - 0.12, s * 1.3, s, s * 0.8,
                     yaw=rng.uniform(-12, 12), roll=rng.uniform(-8, 8))
            continue
        # a slab leaning on the wall, its foot on the heap
        j = [rng.uniform(-0.15, 0.15) for _ in range(4)]
        st.D.slab(mat, [(x - w, yb, zb + 0.02), (x + w + j[0], yb + rng.uniform(-0.1, 0.1), zb + 0.02),
                        (x + w * 0.9 + j[1], (yb + y1_) / 2, (zb + zt) / 2 + j[2]),
                        (x + w * 0.6, y1_ + 0.03, zt + j[3]), (x - w * 0.8, y1_ + 0.03, zt - j[3])],
                  rng.uniform(0.18, 0.26))
    assert frame(y1_ + 2.15, 0.2) and frame(y1_ + 0.2, 4.5)
    # the podium's terraces heaped where they broke
    for k, xm in enumerate(((I["px0"] + I["mx0"]) / 2, (I["mx1"] + I["px1"]) / 2)):
        strip = rect_px(st, xm - 0.9, xm + 0.9, I["my0"], I["py1"], 2.6)
        hs = heap(st, strip, 0.6, z_at=2.6, z0=2.2, edge=0.5, seed=35 + k, cell=0.6)
        strew(st, strip, hs, 12, z_at=2.6, seed=37 + k)
    # the front-left corner's rubble heaped against the facade
    y0 = I["my0"]
    F1 = [(58, 198), (76, 186), (100, 192), (146, 200), (156, 222), (146, 248), (114, 258), (80, 254), (60, 238),
          (52, 218)]

    def ramp(x, y):
        h = float(np.interp(y, [y0 - 2.4, y0 - 1.2, y0 - 0.2, y0 + 1.0], [0.8, 2.2, 3.5, 3.6]))
        return h * max(0.35, min(1.0, 1.0 - (abs(x + 3.3) - 1.8) / 1.8))
    f1 = mound(st, F1, ramp, ground=0.0, cell=0.6, edge=0.9, rough=0.3, seed=40, pic=True, zr=(0.0, 3.6))
    F2 = [(170, 232), (236, 230), (240, 242), (210, 248), (176, 246)]
    f2 = heap(st, F2, 0.7, z_at=0.3, edge=0.6, seed=41)
    strew(st, F1, f1, 60, z_at=1.8, seed=42, stick=0.2, kinds={"slab": 3, "chunk": 6, "beam": 2, "boulder": 2,
                                                                  "tile": 2})
    strew(st, F2, f2, 10, z_at=0.3, seed=43)
    st.soot += [(-2.6, -8.0, 1.5, 2.8, 0.35), (-3.5, -6.9, 3.5, 2.5, 0.45)]
    return finish(st, out, render, steps={"roof": 1.6, "marble": 3.0})


# ---------------------------------------------------------------- the Coliseum's stages

T05A = dict(marble="#8a857a", dome="#335a47", roof="#305443", chip="#a39b8e", rubble="#4f4b44",
            rubble_light="#a8a092", stone="#6f6b63", sooty="#3b3833",
            shard="#2f5242")
T05B = dict(marble="#827d73", dome="#305443", roof="#2d4e3c", chip="#a0988b", rubble="#4c4842",
            rubble_light="#a39b8e", stone="#6a665e", sooty="#36332e",
            shard="#2d4d3e", grit="#34322e")


def dome_ground(x, y):
    C = COL
    r = math.hypot(x - C["cx"], y - C["cy"])
    if r >= C["Rd"]:
        return -99.0
    return C["Hi"] + C["Hd"] * math.sqrt(1 - (r / C["Rd"]) ** 2)


def walk_ground(x, y):
    C = COL
    r = math.hypot(x - C["cx"], y - C["cy"])
    return C["H"] if C["Ri"] + 0.05 < r < C["R"] - 0.35 else -99.0


def slit(pts, w=1.6):
    """A crack: a polyline on the picture widened to a thin outline."""
    P = [Vector((c, r)) for c, r in pts]
    left, right = [], []
    for i, p in enumerate(P):
        d = (P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized()
        n = Vector((-d.y, d.x)) * (w * (0.5 if i in (0, len(P) - 1) else 1.0))
        left.append(tuple(p + n))
        right.append(tuple(p - n))
    return left + right[::-1]


def lying_beam(st, ground, pts, w=0.2, lift=0.06):
    """A charred rafter lying along a surface through points drawn on the
    picture, bending where the surface does."""
    P = [on_ground(st, c, r, ground, 9.0) for c, r in pts]
    on = [ground(p[0], p[1]) > -50 for p in P]
    for (a, b), oa, ob in zip(zip(P, P[1:]), on, on[1:]):
        if oa and ob:
            st.D.beam(st.M["char"], (a[0], a[1], a[2] + lift), (b[0], b[1], b[2] + lift), w, w * 1.1)


def sag_slabs(st, rim, surf, n, seed=0, mat="shard", t=0.22, drop=0.55, size=(0.8, 1.4), z_at=10.5):
    """Pieces of a shell still lying on a breach's rim, along the shell and
    tipping down into the breach, thick enough to read as broken shell."""
    rng = random.Random(st.seed + seed)
    P = [px(st.r, c, r_, z_at) for c, r_ in rim]
    cx, cy = sum(q[0] for q in P) / len(P), sum(q[1] for q in P) / len(P)
    for k in range(n):
        x, y = P[int(k * len(P) / n)]
        d = Vector((cx - x, cy - y, 0))
        if d.length < 0.3:
            continue
        d.normalize()
        h0 = surf(x - d.x * 0.3, y - d.y * 0.3)
        if h0 < -50:
            continue
        L, W = rng.uniform(*size), rng.uniform(*size) * 0.7
        a = Vector((x - d.x * 0.3, y - d.y * 0.3, h0 + 0.05))
        b = a + d * L + Vector((0, 0, -L * drop * rng.uniform(0.6, 1.2)))
        side = Vector((-d.y, d.x, 0)) * W / 2
        st.D.slab(st.M[mat], [tuple(a + side), tuple(a - side), tuple(b - side * 0.7), tuple(b + side * 0.7)], t)


def rim_slabs(st, hole, surf, holes, n, seed=0, mat="shard", t=0.22, size=(0.8, 1.3), z_at=10.0, rest=None):
    """Pieces of a shell hinged on a hole's rim: each lies on the surface
    just outside the rim and tips over the edge into the hole, its middle
    still over the surface. With rest (a ground) the tipped end lies on
    it, and a piece that would have to rise to reach it is left out."""
    rng = random.Random(st.seed + seed)
    cc = sum(p[0] for p in hole) / len(hole), sum(p[1] for p in hole) / len(hole)
    over = lambda p: any(akit.inside_poly(h, *screen(st.r, p.x, p.y, p.z)[:2]) for h in holes)
    for k in range(n):
        c, r_ = hole[int(k * len(hole) / n)]
        dc = Vector((c - cc[0], r_ - cc[1]))
        if dc.length < 1:
            continue
        dc.normalize()
        x, y, z = on_ground(st, c + dc.x * 7, r_ + dc.y * 7, surf, z_at)
        if surf(x, y) < -50:
            continue
        cx, cy = px(st.r, cc[0], cc[1], z)
        d = Vector((cx - x, cy - y, 0))
        if d.length < 0.3:
            continue
        d.normalize()
        L, W = rng.uniform(*size), rng.uniform(*size) * 0.7
        a = Vector((x, y, 0)) - d * (L * 0.55)
        if surf(a.x, a.y) < -50:
            continue
        a.z = surf(a.x, a.y) + 0.04
        b = Vector((x, y, z)) + d * (L * 0.45) + Vector((0, 0, -0.25 - 0.35 * rng.random()))
        if rest is not None:
            zr = rest(b.x, b.y)
            if zr < -50 or zr + 0.06 > a.z + 0.2:
                continue
            b.z = zr + 0.06
        mid = (a + b) / 2
        if over(Vector((mid.x, mid.y, surf(mid.x, mid.y) if surf(mid.x, mid.y) > -50 else mid.z))):
            continue
        side = Vector((-d.y, d.x, 0)) * W / 2
        st.D.slab(st.M[mat], [tuple(a + side), tuple(a - side), tuple(b - side * 0.8), tuple(b + side * 0.8)], t)


def bowl_slabs(st, region, surf, holes, n, seed=0, mat="shard", t=0.22, size=(0.9, 1.5), z_at=10.0, tip=0.3):
    """Large shell slabs lying on a sagging bowl inside an outline on the
    picture, each corner on the surface and one end a little raised, none
    over a hole."""
    rng = random.Random(st.seed + seed)
    cs, rs = [p[0] for p in region], [p[1] for p in region]
    placed = 0
    for _ in range(n * 30):
        if placed >= n:
            break
        c, r_ = rng.uniform(min(cs), max(cs)), rng.uniform(min(rs), max(rs))
        if not akit.inside_poly(region, c, r_):
            continue
        x, y, z = on_ground(st, c, r_, surf, z_at)
        if surf(x, y) < -50:
            continue
        L, W = rng.uniform(*size), rng.uniform(*size) * 0.65
        a = rng.uniform(0, TAU)
        u, v = Vector((math.cos(a), math.sin(a), 0)), Vector((-math.sin(a), math.cos(a), 0))
        corners = [Vector((x, y, 0)) + u * (su * L / 2) + v * (sv * W / 2) for su, sv in ((1, 1), (1, -1), (-1, -1), (-1, 1))]
        zs = [surf(q.x, q.y) for q in corners]
        if min(zs) < -50:
            continue
        if any(akit.inside_poly(h, *screen(st.r, q.x, q.y, zq)[:2]) for q, zq in zip(corners, zs) for h in holes):
            continue
        lift = rng.uniform(0.0, tip)
        for i, (q, zq) in enumerate(zip(corners, zs)):
            q.z = zq + 0.04 + (lift if i < 2 else 0.0)
        st.D.slab(st.M[mat], [tuple(q) for q in corners], t)
        placed += 1


def dome_term(a):
    """The profile angle at azimuth a (radians) where the Coliseum dome
    turns from facing the classic camera to facing away from it, -1 on
    the south half, which faces it from foot to crown."""
    C = COL
    sa = math.sin(a)
    return math.atan(0.5 * C["Hd"] / C["Rd"] * sa) if sa > 0 else -1.0


def dome_pt(a, s, dr=0.0, dh=None):
    C = COL
    R, H = C["Rd"] + dr, C["Hd"] + (dr if dh is None else dh)
    return (C["cx"] + R * math.cos(s) * math.cos(a), C["cy"] + R * math.cos(s) * math.sin(a), C["Hi"] + H * math.sin(s))


def view_cut_at(st, p, polys, zlo):
    """Whether cut() through these outlines takes the point p away."""
    return p[2] >= zlo and any(akit.inside_poly(P, *screen(st.r, *p)[:2]) for P in polys)


def back_band(st, polys, zlo, cols=12, seed=0, drop=(0.04, 0.12), t=0.32, uv=(4.0, 2.0)):
    """The back of the Coliseum dome, which the classic camera never sees,
    rebuilt whole so the picture's holes, cut along the camera's line of
    sight, take only the front shell and leave no slivers behind it: a
    band from the dome's foot to where it turns away from the camera, in
    columns, each column's top stepping down a little where the front
    above it is gone. The old back is cut away under it. Returns the
    columns as (a0, a1, top)."""
    C = COL
    rng = random.Random(st.seed + seed)
    band, cutter = bmesh.new(), bmesh.new()
    uvl = band.loops.layers.uv.new("UVMap")
    s1 = math.acos(0.55 / C["Rd"])
    cols_out = []

    def column(bm, a0, a1, s_lo, s_hi, d_out, d_in, rows, uvs=None):
        ring = []
        for i in range(rows + 1):
            s = s_lo + (s_hi - s_lo) * i / rows
            ring.append([bm.verts.new(dome_pt(a, s, dr)) for a in (a0, a1) for dr in (d_out, -t - d_in)])
        fs = []
        # 0 outer a0, 1 inner a0, 2 outer a1, 3 inner a1
        for i in range(rows):
            lo, hi = ring[i], ring[i + 1]
            for q, uvq in (((lo[0], lo[2], hi[2], hi[0]), True), ((lo[3], lo[1], hi[1], hi[3]), True),
                           ((lo[1], lo[0], hi[0], hi[1]), False), ((lo[2], lo[3], hi[3], hi[2]), False)):
                f = bm.faces.new(q)
                fs.append(f)
                if uvs is not None:
                    for lp in f.loops:
                        p = lp.vert.co
                        a = math.atan2(p.y - C["cy"], p.x - C["cx"]) % TAU
                        s = math.asin(max(-1.0, min(1.0, (p.z - C["Hi"]) / C["Hd"])))
                        lp[uvs].uv = (a / TAU * uv[0], s / s1 * uv[1]) if uvq else (0.05 * p.z, 0.05 * p.x)
        fs.append(bm.faces.new((ring[0][0], ring[0][1], ring[0][3], ring[0][2])))
        fs.append(bm.faces.new((ring[-1][0], ring[-1][2], ring[-1][3], ring[-1][1])))
        if uvs is not None:
            for f in fs[-2:]:
                for lp in f.loops:
                    lp[uvs].uv = (0.05 * lp.vert.co.x, 0.05 * lp.vert.co.y)
        bmesh.ops.recalc_face_normals(bm, faces=fs)

    for k in range(cols):
        a0, a1 = math.pi * k / cols, math.pi * (k + 1) / cols
        terms = [dome_term(a0 + (a1 - a0) * u) for u in (0.0, 0.25, 0.5, 0.75, 1.0)]
        s_hi, s_lo = max(terms), min(terms)
        if s_hi < 0.02:
            continue
        gone = any(view_cut_at(st, dome_pt(a0 + (a1 - a0) * u, s_hi + ds), polys, zlo)
                   for u in (0.1, 0.5, 0.9) for ds in (0.03, 0.12))
        top = s_hi if not gone else max(0.0, s_lo - rng.uniform(*drop))
        column(cutter, a0 - 0.003, a1 + 0.003, -0.03, s_hi + 0.004, 0.1, 0.1, 1)
        if top > 0.02:
            column(band, a0, a1, 0.0, top, 0.0, 0.0, max(1, int(math.ceil(top / 0.24))), uvl)
        cols_out.append((a0, a1, top, s_hi))
    st.cuts3d.append((("dome",), cutter))
    st.M.setdefault("domeback", st.M["dome"])
    st.add(band, "domeback", "domeback")
    return cols_out


def dome_left(st, polys, zlo, cols, a, s, t=0.0, whole=True):
    """Whether the Coliseum dome's shell is left at azimuth a, profile
    angle s, once the holes and the back band are made: on its outer
    surface, or with t through its whole thickness (whole) or any of it."""
    for a0, a1, top, s_hi in cols:
        if a0 <= a <= a1 and s < s_hi:
            return s < top
    left = [not view_cut_at(st, dome_pt(a, s, -d), polys, zlo) for d in ((0.0, t / 2, t) if t else (0.0,))]
    return all(left) if whole else any(left)


def shell_strips(st, polys, zlo, cols, rad=0.45, da=4.0, ds=4.0, t=0.32, min_cells=3):
    """Cuts away what the holes leave of the dome's front shell narrower
    than twice rad (cells): the shell left is mapped round and up the dome,
    opened (eroded then grown back by rad, measured on the shell) and what
    the opening loses on the front is cut out in small stepped sectors,
    with the slivers the slanting cuts leave clear of any wide shell."""
    C = COL
    s1 = math.acos(0.55 / C["Rd"])
    A = np.radians(np.arange(0.0, 360.0, da) + da / 2)
    S = np.radians(np.arange(0.0, math.degrees(s1), ds) + ds / 2)
    M = np.array([[dome_left(st, polys, zlo, cols, a, s, t) for a in A] for s in S])
    some = np.array([[dome_left(st, polys, zlo, cols, a, s, t, whole=False) for a in A] for s in S])
    dar, dsr = math.radians(da), math.radians(ds)
    hs = math.hypot(C["Rd"], C["Hd"]) / math.sqrt(2) * dsr

    def morph(X, grow, rad=rad):
        out = X.copy()
        for j in range(len(S)):
            wa = C["Rd"] * math.cos(S[j]) * dar
            ni, nj = int(rad / max(wa, 1e-3)) + 1, int(rad / hs) + 1
            acc = np.ones(len(A), bool) if not grow else np.zeros(len(A), bool)
            for dj in range(-nj, nj + 1):
                jj = j + dj
                for di in range(-ni, ni + 1):
                    if (di * wa) ** 2 + (dj * hs) ** 2 > rad * rad:
                        continue
                    if jj < 0:
                        row = np.ones(len(A), bool)       # below the foot counts as shell
                    elif jj >= len(S):
                        row = np.zeros(len(A), bool)
                    else:
                        row = np.roll(X[jj], -di)
                    acc = (acc | row) if grow else (acc & row)
            out[j] = acc
        return out
    # narrow strips of whole shell, and slivers the slanting cuts leave that
    # stand clear of any wide shell
    opened = morph(morph(M, False), True)
    near = morph(opened, True, 0.3)
    lost = (M & ~opened) | (some & ~M & ~near)
    # single chips off an edge are left: only strips and slivers go
    seen = np.zeros_like(lost)
    for j0, i0 in zip(*np.nonzero(lost)):
        if seen[j0, i0]:
            continue
        comp, stack = [], [(j0, i0)]
        seen[j0, i0] = True
        while stack:
            j, i = stack.pop()
            comp.append((j, i))
            for dj, di in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                jj, ii = j + dj, (i + di) % len(A)
                if 0 <= jj < len(S) and lost[jj, ii] and not seen[jj, ii]:
                    seen[jj, ii] = True
                    stack.append((jj, ii))
        if len(comp) < min_cells:
            for j, i in comp:
                lost[j, i] = False
    # only the front: the back band is built whole and no cut reaches it
    front = np.array([[not any(a0 <= a <= a1 and s < s_hi for a0, a1, _t, s_hi in cols) for a in A] for s in S])
    cutm = lost & front
    runs = {}
    for j in range(len(S)):
        i = 0
        while i < len(A):
            if not cutm[j, i]:
                i += 1
                continue
            k = i
            while k + 1 < len(A) and cutm[j, k + 1]:
                k += 1
            runs[(j, i, k)] = j
            i = k + 1
    # a run carried on unchanged by the row above grows into it
    for (j, i, k) in sorted(runs):
        if (j - 1, i, k) in runs:
            runs[(j, i, k)] = runs.pop((j - 1, i, k))
    bm = bmesh.new()
    for (j, i, k), j0 in sorted(runs.items()):
        a0, a1 = math.degrees(A[i]) - da / 2 - 0.2, math.degrees(A[k]) + da / 2 + 0.2
        s_lo, s_hi = S[j0] - dsr / 2 - 0.004, S[j] + dsr / 2 + 0.004
        ring = []
        for a in np.radians(np.linspace(a0, a1, max(2, int((a1 - a0) / 12.0) + 2))):
            ring.append([bm.verts.new(dome_pt(a, s_, dr)) for s_, dr in ((s_lo, 0.1), (s_hi, 0.1), (s_hi, -t - 0.1),
                                                                         (s_lo, -t - 0.1))])
        fs = []
        for p, q in zip(ring, ring[1:]):
            for e in range(4):
                fs.append(bm.faces.new((p[e], p[(e + 1) % 4], q[(e + 1) % 4], q[e])))
        fs += [bm.faces.new(ring[0]), bm.faces.new(ring[-1][::-1])]
        bmesh.ops.recalc_face_normals(bm, faces=fs)
    print("G2_STRIPS", st.name, int(cutm.sum()), "cells", len(runs), "runs", flush=True)
    if runs:
        st.cuts3d.append((("dome",), bm))

    def gone(a, s):
        i, j = int((math.degrees(a) % 360.0) / da) % len(A), int(math.degrees(s) / ds)
        return 0 <= j < len(S) and bool(lost[j, i])
    return gone


def dome_fill(st, polys, zlo, cols, skin, sk_ground, gone=None, nr=6, na=20, rf=4.93, base=5.8, slope=1.0, seed=0):
    """The drum filled with the fallen crown under what is left of the
    dome: a rubble heightfield wall to wall, just under the shell or the
    rubble skin wherever either still covers it, and falling at slope from
    their edges where nothing does, so the skin and the shell rest on it
    and no low view sees daylight through the dome. Returns the surface
    and the same surface only where it lies open (-99 elsewhere)."""
    C = COL
    cx, cy = C["cx"], C["cy"]
    rng = random.Random(st.seed + seed)
    Ri, Hi_ = C["Rd"] - 0.32, C["Hd"] - 0.32

    def inner(r):
        return C["Hi"] + Hi_ * math.sqrt(max(0.0, 1 - (r / Ri) ** 2)) if r < Ri else C["Hi"]

    def shell(x, y):
        r = math.hypot(x - cx, y - cy)
        if r >= C["Rd"]:
            return False
        a = math.atan2(y - cy, x - cx) % TAU
        s = math.acos(min(1.0, r / C["Rd"]))
        return dome_left(st, polys, zlo, cols, a, s) and not (gone and gone(a, s))

    def cap(x, y):
        caps = []
        r = math.hypot(x - cx, y - cy)
        if shell(x, y):
            caps.append(inner(r) - 0.05)
        if skin(x, y) > -50:
            caps.append(sk_ground(x, y) - 0.12 - 0.06)
        return min(caps) if caps else None

    pts = [(cx, cy)] + [(cx + rf * i / nr * math.cos(TAU * k / na), cy + rf * i / nr * math.sin(TAU * k / na))
                        for i in range(1, nr + 1) for k in range(na)]
    caps = [cap(x, y) for x, y in pts]
    covered = [(p, c) for p, c in zip(pts, caps) if c is not None]
    Z = []
    for (x, y), c in zip(pts, caps):
        r = math.hypot(x - cx, y - cy)
        if c is not None:
            z = c - rng.uniform(0.0, 0.15)
        else:
            z = max([base] + [cq - slope * math.hypot(x - q[0], y - q[1]) for q, cq in covered])
            z += 0.25 * noise.noise(Vector((x * 0.9 + seed, y * 0.9, 1.7))) + rng.uniform(-0.15, 0.1)
            z = min(z, inner(r) - 0.1)
        Z.append(max(min(base, inner(r) - 0.1), z))
    floor = 0.1
    verts = [(x, y, z) for (x, y), z in zip(pts, Z)] + [(x, y, floor) for x, y in pts[-na:]]
    ring = lambda i, k: 1 + (i - 1) * na + k % na
    faces = [(0, ring(1, k), ring(1, k + 1)) for k in range(na)]
    for i in range(1, nr):
        faces += [(ring(i, k), ring(i + 1, k), ring(i + 1, k + 1), ring(i, k + 1)) for k in range(na)]
    lo = len(pts)
    faces += [(ring(nr, k), lo + k, lo + (k + 1) % na, ring(nr, k + 1)) for k in range(na)]
    st.D.mesh(st.M["rubble"], verts, faces)
    Zg = np.array(Z[1:]).reshape(nr, na)

    def surface(x, y):
        r = math.hypot(x - cx, y - cy)
        if r > rf:
            return -99.0
        fi = r / rf * nr
        fk = (math.atan2(y - cy, x - cx) % TAU) / TAU * na
        k0 = int(fk) % na
        u = fk - int(fk)
        i0 = int(fi)
        if i0 >= nr:
            i0, fi = nr - 1, float(nr)
        ring_z = lambda i: Z[0] if i == 0 else Zg[i - 1, k0] * (1 - u) + Zg[i - 1, (k0 + 1) % na] * u
        v = fi - i0
        return float(ring_z(i0) * (1 - v) + ring_z(min(nr, i0 + 1)) * v)

    def open_at(x, y):
        z = surface(x, y)
        return -99.0 if z < -50 or cap(x, y) is not None else z
    st.grounds.append(surface)
    return surface, open_at


def _coliseum_broken(st, gaps, crescent, slits, skin, thick, bays, inner_top, W, FL, VR, n_skin=60, n_slabs=0,
                     rafter_lines=(), sag=0, vault=None, vcut=None, drum_gap=None, skin_cell=0.55, crater=None,
                     patchy=False, bowl=False, where_kw=None, skin_edge=0.9, flank=None, fill=False, jag_step=5.0,
                     heap_kinds=None):
    """The Coliseum's ruin and shell share this: the dome holed where the
    picture shows it open (gaps), its east flank broken away from the drum
    (crescent) and cracked (slits), the arena heaped under it, a closed
    skin of rubble lying proud on the dome where the picture draws rubble,
    with marble blocks, green shell fragments and charred rafters on it,
    the bays and parapet broken under solid heaps spilling west and
    south-west, rubble on the walkway and before the entrance. crater
    (outline, depth) caves the crown in: the shell cut away inside the
    outline and the rubble lying in a bowl sagging depth below where the
    dome was, its ribs still arching over it. patchy lays the skin only
    in the patches where the picture draws rubble, green showing between
    them, but always over the crater. With bowl the slabs and charred ribs
    lie on the sagging bowl itself, hinged on the hole's rim or lying on
    the rubble, and none hangs over the hole."""
    C = COL
    coliseum(st, "ruin", bays=bays, vault=vault, gone=jagged(st, (W, FL), 2.0), gap=drum_gap, floor=not fill)
    holes = jagged(st, gaps, 2.5, jag_step) + jagged(st, [crescent], 2.0, jag_step + 2.0)
    st.cuts.append((("dome",), holes + [slit(q, 1.4) for q in slits], "DIFFERENCE", C["Hi"] + 0.3))
    st.cuts.append((("trim",), jagged(st, (W, FL), 2.0), "DIFFERENCE", 4.0))
    if vcut:
        st.cuts.append((("roof", "trim", "dark"), jagged(st, vcut, 4.0, 5.0), "DIFFERENCE", 0.6))
    for h in holes:
        st.halos.append((h, 1.3, 0.35, C["Hi"] + 0.5))
    # the arena heaped with the fallen crown, seen through the holes (with
    # fill, the drum filled under the dome instead, further down)
    hin = None
    if not fill:
        arena = circle_px(st, C["cx"], C["cy"], C["Ri"] - 0.4, 4.5, k=24)
        hin = heap(st, arena, inner_top, z_at=4.5, z0=0.15, edge=3.4, seed=10, pic=False, cell=1.2)
        for i, h in enumerate(holes[:len(gaps)]):
            hp = clip_px(st, h, inner_top - 0.5, circle=(C["cx"], C["cy"], C["Ri"] - 0.6))
            strew(st, hp, hin, 12, z_at=inner_top - 0.5, kinds={"chunk": 3, "beam": 2, "tile": 3}, tiles="shard",
                  seed=12 + i, pic=False, length=(1.2, 2.8), stick=0.2)
    # the skin of rubble over the dome, proud of it, ending in the holes,
    # and sagging into the caved crown
    cxy = (C["cx"], C["cy"])
    tf = lambda x, y: thick[0] + (thick[1] - thick[0]) * max(0.0, 1 - math.hypot(x - cxy[0], y - cxy[1]) / 4.0)
    ground = dome_ground
    if crater:
        cj = akit.jag(crater[0], 3.0, jag_step, st.seed + 7)
        st.cuts.append((("dome",), [cj], "DIFFERENCE", C["Hi"] + 0.3))
        st.halos.append((cj, 1.0, 0.4, C["Hi"] + 0.5))

        def ground(x, y):
            g = dome_ground(x, y)
            if g < -50:
                return g
            c, r = screen(st.r, x, y, g)[:2]
            if not akit.inside_poly(cj, c, r):
                return g
            t = min(1.0, edge_dist(cj, (c, r)) / 16.0 / 1.6)
            return g - crater[1] * t * t * (3 - 2 * t)
        tf0 = tf
        tf = lambda x, y: min(tf0(x, y), 0.55) if ground(x, y) < dome_ground(x, y) - 0.05 else tf0(x, y)

        def in_crater(x, y):
            g = dome_ground(x, y)
            return g > -50 and akit.inside_poly(cj, *screen(st.r, x, y, g)[:2])
    sk = mound(st, skin, tf, ground=ground, zrel=0.4, cell=skin_cell, edge=skin_edge, rough=0.35, seed=5,
               holes=holes, sink=0.12, zr=(C["Hi"] + 1.0, C["Hi"] + C["Hd"] + 1.0), bottom=bool(crater),
               where=rubble_where(st, **(where_kw or {})) if patchy else None,
               keep=in_crater if (crater and patchy) else None)
    x, y = middle(st, skin, 10.0)
    st.soot.append((x, y, 10.5, 3.5, 0.35))
    rest = None
    if fill:
        # the dome's back kept whole and the drum filled under the crust
        polys = holes + [slit(q, 1.4) for q in slits] + ([cj] if crater else [])
        cols = back_band(st, polys, C["Hi"] + 0.3, seed=80)
        gone = shell_strips(st, polys, C["Hi"] + 0.3, cols)
        rest, open_at = dome_fill(st, polys, C["Hi"] + 0.3, cols, sk, ground, gone=gone, seed=81)
        for i, h in enumerate(holes):
            hp = clip_px(st, h, 8.0, circle=(C["cx"], C["cy"], 4.6))
            strew(st, hp, open_at, 9, z_at=8.0, kinds={"chunk": 4, "beam": 1, "tile": 3, "stone": 2}, tiles="shard",
                  seed=12 + i, pic=False, length=(1.2, 2.4), stick=0.1, size=(0.6, 1.1))
    strew(st, skin, sk, n_skin, z_at=10.0, kinds={"chunk": 6, "stone": 3, "tile": 5}, tiles="shard",
          size=(0.6, 1.2), chunk=(0.4, 0.85), seed=20, tilt=8)
    if n_slabs:
        strew(st, skin, sk, n_slabs, z_at=10.0, kinds={"tile": 1}, tiles="shard", size=(1.0, 1.9), seed=23, tilt=10,
              pic=False)
    # charred ribs: over the crater they still follow the dome's curve
    ribs = (lambda x, y: max(sk(x, y), dome_ground(x, y) + 0.05)) if (crater and not bowl) else sk
    for i, ln in enumerate(rafter_lines):
        lying_beam(st, ribs, ln, w=0.18 + 0.03 * (i % 2))
    if bowl:
        if sag:
            rim_slabs(st, holes[0], sk, holes, sag, seed=60, rest=rest)
        if crater:
            bowl_slabs(st, cj, sk, holes, crater[2], seed=61)
    else:
        if sag:
            sag_slabs(st, gaps[0], dome_ground, sag, seed=60)
        if crater:
            sag_slabs(st, cj, dome_ground, crater[2], seed=61, size=(0.9, 1.6))
    if flank:
        # broken shell and blocks lying over the camera-facing flanks
        fg = off_holes(st, lambda x, y: sk(x, y) if sk(x, y) > -50 else dome_ground(x, y), holes)
        strew(st, flank[0], fg, flank[1], z_at=8.0, kinds={"chunk": 5, "stone": 3, "tile": 4}, tiles="shard",
              size=(0.6, 1.1), chunk=(0.4, 0.8), seed=24, tilt=10)
    strew(st, [(0, 30), (100, 0), (100, 200), (0, 200)], walk_ground, 20, z_at=C["H"],
          kinds={"stone": 3, "chunk": 4, "slab": 2}, size=(0.4, 0.8), seed=21)
    # solid heaps over the west bay and the south-west quarter, and before the entrance
    w = heap(st, W, 2.4, z_at=1.6, edge=1.2, seed=30, cell=0.7)
    fl = heap(st, FL, 3.8, z_at=1.8, edge=2.0, seed=31, cell=0.7)
    v = heap(st, VR, 0.9, z_at=0.5, edge=0.8, seed=32, cell=0.6)
    for i, (poly, f, n, z) in enumerate(((W, w, 20, 1.6), (FL, fl, 44, 1.8), (VR, v, 14, 0.5))):
        strew(st, poly, f, n, z_at=z, seed=40 + i, tiles="shard", stick=0.15,
              kinds=heap_kinds or {"slab": 2, "chunk": 6, "beam": 2, "boulder": 2, "tile": 3})
    st.soot += [(-6.0, 1.2, 2.5, 2.6, 0.4), (-3.6, -3.4, 3.0, 3.2, 0.4), (0.8, -6.5, 1.0, 2.0, 0.3)]
    return hin


@stage("CreBuild05a")
def cre05a(out, render=True):
    """The Damaged Coliseum: the dome still whole in shape but heaped over
    its crown with rubble, crossed by cracks and charred rafters, with
    only small dark gaps through it, its east flank broken away from the
    drum in a dark crescent, rubble lying on the walkway, the west and
    south-west bays broken under heaps that spill round the drum, the
    entrance vault standing under rubble."""
    st = Stage("CreBuild05a", tone=T05A)
    gaps = [[(104, 37), (114, 32), (125, 36), (123, 46), (110, 48)],
            [(138, 65), (150, 60), (159, 68), (153, 78), (141, 77)],
            [(92, 83), (102, 79), (111, 86), (105, 94), (94, 92)]]
    crescent = [(156, 2), (194, 10), (216, 30), (228, 66), (230, 108), (220, 130), (208, 126), (202, 100), (194, 72),
                (184, 46), (170, 22)]
    slits = [[(64, 66), (78, 61), (92, 57)], [(112, 104), (126, 112), (141, 121)], [(88, 102), (79, 113), (70, 124)],
             [(160, 92), (168, 106), (176, 118)], [(150, 30), (160, 44), (166, 58)]]
    skin = [(44, 74), (60, 36), (94, 8), (136, 0), (184, 10), (214, 46), (224, 92), (214, 126), (180, 138), (140, 142),
            (100, 140), (66, 126), (48, 104)]
    rafters_ = [[(84, 64), (104, 56), (126, 46)], [(118, 84), (138, 74), (160, 56)], [(98, 112), (116, 100), (132, 92)],
                [(132, 18), (128, 36), (122, 58)]]
    W = [(0, 86), (24, 78), (44, 90), (48, 124), (36, 148), (8, 146), (0, 124)]
    FL = [(28, 150), (60, 146), (96, 160), (112, 196), (104, 226), (64, 230), (30, 216), (18, 184)]
    VR = [(118, 226), (162, 222), (166, 262), (152, 282), (118, 280)]
    crater = [(84, 34), (104, 18), (132, 12), (156, 22), (168, 44), (168, 72), (158, 96), (138, 112), (116, 112),
              (100, 96), (88, 76), (80, 54)]
    _coliseum_broken(st, gaps, crescent, slits, skin, (0.5, 1.0), {0: 2.6, 1: True, 2: 2.2, 3: True}, 5.0, W, FL, VR,
                     n_skin=70, rafter_lines=rafters_, crater=(crater, 1.5, 6), patchy=True)
    return finish(st, out, render)


@stage("CreBuild05b")
def cre05b(out, render=True):
    """The Destroyed Coliseum, the ruin taken further: the crown gone in a
    breach a third of the dome across, its rim's shell pieces tipping
    into it, the rest of the dome under a thicker heap strewn with large
    green shell slabs, the east flank broken away wider, the arena heaped
    higher, three bays broken, the drum down in its south-west quarter
    and the vault's east half fallen. The back of the dome, which the
    picture never shows, stands whole to where it turns from the camera,
    stepping down where the front above it is gone, the shell's narrow
    strips are cut away in steps, and the drum is filled with rubble up
    under the crust and the shell, so no low view sees daylight through."""
    st = Stage("CreBuild05b", tone=T05B)
    st.settle = True
    st.dissolve = ("dome", "domeback")
    gaps = [[(112, 14), (128, 8), (146, 7), (164, 14), (174, 34), (168, 54), (146, 62), (124, 58), (110, 40)],
            [(70, 96), (82, 90), (92, 98), (86, 108), (74, 106)]]
    crescent = [(170, 12), (204, 26), (224, 58), (234, 100), (232, 142), (216, 168), (206, 152), (202, 124),
                (196, 94), (188, 62), (178, 36)]
    slits = [[(58, 70), (74, 64), (92, 60)], [(104, 112), (124, 122), (146, 134)], [(150, 92), (164, 110), (178, 126)],
             [(60, 120), (72, 132), (84, 142)]]
    # the skin runs down the camera-facing flanks to the dome's foot
    skin = [(40, 60), (70, 22), (110, 2), (160, 0), (200, 20), (222, 60), (230, 110), (216, 148), (180, 164),
            (140, 168), (100, 166), (62, 152), (40, 110)]
    rafters_ = [[(80, 50), (100, 62), (118, 72)], [(150, 74), (172, 86), (190, 98)], [(110, 92), (130, 100), (150, 112)],
                [(64, 82), (80, 74), (98, 70)]]
    W = [(0, 84), (26, 74), (48, 88), (52, 126), (38, 152), (6, 150), (0, 126)]
    FL = [(24, 146), (62, 140), (100, 156), (116, 196), (108, 228), (64, 232), (26, 218), (14, 184)]
    VR = [(112, 214), (166, 210), (170, 262), (156, 284), (112, 282)]
    vcut = [(132, 206), (150, 200), (168, 210), (168, 286), (126, 286), (124, 248), (134, 230)]
    crater = [(70, 48), (94, 26), (126, 14), (166, 18), (188, 42), (192, 80), (180, 112), (160, 126), (128, 110),
              (98, 102), (74, 98), (64, 72)]
    _coliseum_broken(st, gaps, crescent, slits, skin, (0.7, 1.3), {0: 1.8, 1: True, 2: 1.6, 3: 2.3}, 5.6, W, FL, VR,
                     n_skin=38, n_slabs=20, rafter_lines=rafters_, sag=6, vcut=[vcut],
                     vault={"W": [(0, 2.0), (1, 2.0)], "E": [(0, 1.0), (0.5, 1.5), (1, 0.7)],
                            "roof": (VAULT["y0"] + 0.35, VAULT["y1"])},
                     drum_gap=(204.0, 252.0, [(204, 4.4), (214, 3.1), (226, 2.6), (240, 3.0), (252, 4.2)]),
                     crater=(crater, 2.2, 8), patchy=True, bowl=True, where_kw=dict(lum=25.0, rad=3, thr=0.3),
                     skin_edge=0.5, skin_cell=0.56, fill=True, jag_step=8.0,
                     heap_kinds={"slab": 2, "chunk": 8, "beam": 2, "tile": 3},
                     flank=([(36, 96), (60, 120), (100, 130), (140, 132), (180, 128), (214, 118), (222, 140),
                             (186, 162), (140, 170), (100, 168), (60, 154), (38, 124)], 42))
    return finish(st, out, render)


# ---------------------------------------------------------------- the Observatory's stages

T06A = dict(marble="#8e897e", dome="#3c5648", roof="#4d6b5e", chip="#b6ad9f", rubble="#5c5850",
            rubble_light="#c4baab", stone="#6c665e", sooty="#38342f",
            shard="#2c4234", green="#40564a", gold="#7f7336")
T06B = dict(marble="#8a857a", dome="#3a5747", roof="#43604f", chip="#b2a99b", rubble="#57534b",
            rubble_light="#c0b6a7", stone="#6c665e", sooty="#34302b",
            shard="#2f4a3a", green="#3a4e43", grit="#33302c", gold="#7a6f34")


@stage("CreBuild06a")
def cre06a(out, render=True):
    """The Hurt Observatory: the top storey half come down, so the dome
    sits 1.6 cells lower, its telescope gone and its slot open with the
    mounting's wreck fallen into it, the shutter still riding over the
    top, the dome holed on both flanks, white blocks strewn on the rim,
    and the tower's south face torn out storey by storey in broken
    courses from the ground to just under the rim, its rubble one pale
    cascade down the broken floors into a heap that rises to the second
    floor, a sooted gold gear wheel lying whole on the heap."""
    st = Stage("CreBuild06a", tone=T06A)
    st.settle = True
    O = OBS
    cx, cy = O["cx"], O["cy"]
    NE = jagged(st, ([(120, 0), (190, 0), (190, 82), (172, 74), (158, 54), (148, 36), (138, 18)],), 2.5, 7.0)
    rows = obs_storeys(5, 1.7)
    breach = Breach(st, rows, [(1.0, 100, 74), (3.0, 92, 68), (5.8, 80, 54), (8.5, 64, 40), (11.0, 46, 26),
                               (obs_top(5, 1.7) - 0.5, 18, 6)], seed=40)
    top = observatory(st, storeys=5, last=1.7, dome="open", breach=breach, gone_px=NE, shut=65.0,
                      terrace=[(0, 0.3), (175, 0.3), (195, 0.12), (215, 0.0), (325, 0.0), (345, 0.15), (359, 0.3)])
    st.cuts3d.append((("marble",), breach.cutter(cx, cy, O["r0"] - 0.8, O["r0"] + 5 * O["dr"] + 0.6)))
    st.cuts.append((("marble", "trim", "floor", "gold", "dome"), NE, "DIFFERENCE", top - 3.0))
    # the dome holed on its north-west flank, broken off at the north-east, rubble and rafters on both
    dg = obs_dome_ground(top)
    NWg = jagged(st, ([(36, 52), (45, 47), (52, 56), (48, 67), (38, 65)],), 1.5)
    st.cuts.append((("dome",), NWg, "DIFFERENCE", top + 0.5))
    flank(st, NWg[0], [(30, 40), (46, 34), (62, 44), (60, 72), (48, 84), (32, 80), (28, 60)], dg,
          [[(32, 58), (46, 60), (58, 62)], [(42, 44), (44, 60), (46, 76)]], 70, top)
    flank(st, NE[0], [(116, 22), (136, 12), (150, 30), (162, 50), (174, 76), (160, 88), (136, 86), (120, 64)], dg,
          [[(124, 46), (140, 50), (152, 50)], [(130, 66), (148, 70), (162, 72)]], 75, top)
    # the open slot with what fell into it
    slot_debris(st, top, [(60, 66), (96, 64), (100, 124), (58, 126)], 80)
    # white blocks on the rim, west and south
    rim_blocks(st, top, [(6, 48), (50, 48), (50, 106), (146, 106), (150, 146), (28, 152), (6, 152)], 50, 85)
    # the fallen face heaped before the breach and up it to the second floor,
    # the floors above broken back with their rubble cascading down
    HF = [(10, 140), (40, 132), (96, 130), (132, 134), (148, 150), (150, 190), (140, 222), (112, 240), (64, 243),
          (28, 230), (10, 196)]
    f = obs_face(st, rows, breach, [-2.9, -2.0, -1.2, -0.5], HF,
                 [-6.4, -5.7, -5.0, -4.3, -3.6, -2.9, -2.3, -1.7, -1.0], [0.15, 1.2, 2.3, 3.3, 4.2, 4.9, 5.4, 5.2, 4.0],
                 32, 14, buried=1, xc=-0.4, wide=2.6)
    st.halos.append((breach.outline_px(st, cx, cy), 1.2, 0.4, 0.5))
    st.soot += [(x, y, z, 0.9, 0.3) for x, y, z in breach.edges(cx, cy)]
    whole_gear(st, f, 50, 184, 0.8)
    st.soot += [(cx, cy - 4.0, 7.0, 4.0, 0.3), (cx, cy - 5.0, 1.5, 3.0, 0.3)]
    return finish(st, out, render, steps={"marble": 4.0})


def back_damage(st, rows, holes, cracks):
    """The back of a broken tower: ragged holes knocked through the wall
    where windows were and cracks running up it, soot rising from both."""
    O = OBS
    cx, cy = O["cx"], O["cy"]
    bm = bmesh.new()
    for k, j in holes:
        a = 360.0 * (j + 0.5) / 8
        z0, _h, r = rows[k]
        for da0, da1, dz0, dz1 in ((-7, 6, 0.45, 1.95), (-3, 9, 1.0, 2.3), (-10, 1, 0.25, 1.1)):
            sector(bm, cx, cy, O["r0"] - 0.8, r + 0.5, z0 + dz0, z0 + dz1, a + da0, a + da1, step=4.0)
        st.soot.append((cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a)), z0 + 1.6, 1.1, 0.45))
    rm = O["r0"] - 0.1
    for cr in cracks:
        P = [Vector((cx + rm * math.cos(math.radians(a)), cy + rm * math.sin(math.radians(a)), z)) for a, z in cr]
        for p0, p1 in zip(P, P[1:]):
            u = p1 - p0
            er = Vector((p0.x + p1.x - 2 * cx, p0.y + p1.y - 2 * cy, 0)).normalized()
            slit_box(bm, (p0 + p1) / 2, u, er.cross(u), 0.1, u.length + 0.12, 1.6)
        st.soot += [(p.x, p.y, p.z, 0.7, 0.35) for p in P]
    st.cuts3d.append((("marble",), bm))


def ne_courses(st, poly, z_lo, z_hi, r=4.5, course=0.8, jit=8.0, seed=0):
    """A cutter taking the tower's wall and rim away where the picture
    shows them gone, course by course as the Breach does: each course of
    about course cells loses the stretch of the ring whose face the
    outline covers at its height, widened by a jitter and now and then
    by a step."""
    O = OBS
    cx, cy = O["cx"], O["cy"]
    rng = random.Random(st.seed + seed)
    bm = bmesh.new()
    z = z_lo
    while z < z_hi - 0.05:
        z1 = min(z_hi, z + course * rng.uniform(0.8, 1.2))
        zm = (z + z1) / 2
        inside = [a for a in range(-90, 180, 2)
                  if any(akit.inside_poly(poly, *screen(st.r, cx + rr * math.cos(math.radians(a)),
                                                        cy + rr * math.sin(math.radians(a)), zz)[:2])
                         for rr in (r, O["Rr"]) for zz in (z, zm, z1))]
        if inside:
            # only ever wider than the outline, so nothing it covers is left
            a0 = min(inside) - rng.uniform(0, jit) - (rng.uniform(6, 14) if rng.random() < 0.3 else 0)
            a1 = max(inside) + rng.uniform(0, jit) + (rng.uniform(6, 14) if rng.random() < 0.3 else 0)
            if a1 > a0 + 4.0:
                sector(bm, cx, cy, O["r0"] - 0.8, O["Rr"] + 0.4, z - 0.01, z1 + 0.01, a0, a1)
        z = z1
    return bm


def shell_cells(present, A, Sg, cx, cy, z0, R, H, t, uv=(4.0, 1.6)):
    """A dome's shell from a grid of cells round it (column edges A) and
    up it (profile angles Sg): outer and inner faces for each cell that
    stands, and a wall along every edge between a standing cell and one
    gone, so its breaks come out in steps. present[j][i] is cell (i, j)."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    ni, nj = len(A) - 1, len(Sg) - 1
    s1 = Sg[-1]
    V = {}

    def v(i, j, inner):
        k = (i, j, inner)
        if k not in V:
            RR, HH = (R - t, H - t) if inner else (R, H)
            a, s_ = A[i], Sg[j]
            V[k] = bm.verts.new((cx + RR * math.cos(s_) * math.cos(a), cy + RR * math.cos(s_) * math.sin(a),
                                 z0 + HH * math.sin(s_)))
        return V[k]

    def face(vs, uvs):
        try:
            f = bm.faces.new(vs)
        except ValueError:
            return
        for lp, q in zip(f.loops, uvs):
            lp[uvl].uv = q
    on = lambda i, j: 0 <= i < ni and 0 <= j < nj and present[j][i]
    for j in range(nj):
        for i in range(ni):
            if not present[j][i]:
                continue
            u0, u1 = A[i] / TAU * uv[0], A[i + 1] / TAU * uv[0]
            w0, w1 = Sg[j] / s1 * uv[1], Sg[j + 1] / s1 * uv[1]
            face((v(i, j, 0), v(i + 1, j, 0), v(i + 1, j + 1, 0), v(i, j + 1, 0)), ((u0, w0), (u1, w0), (u1, w1), (u0, w1)))
            face((v(i + 1, j, 1), v(i, j, 1), v(i, j + 1, 1), v(i + 1, j + 1, 1)), ((u1, w0), (u0, w0), (u0, w1), (u1, w1)))
            small = ((0, 0), (0.1, 0), (0.1, 0.1), (0, 0.1))
            if not on(i, j - 1):
                face((v(i, j, 1), v(i + 1, j, 1), v(i + 1, j, 0), v(i, j, 0)), small)
            if not on(i, j + 1):
                face((v(i, j + 1, 0), v(i + 1, j + 1, 0), v(i + 1, j + 1, 1), v(i, j + 1, 1)), small)
            if not on(i - 1, j):
                face((v(i, j, 0), v(i, j + 1, 0), v(i, j + 1, 1), v(i, j, 1)), small)
            if not on(i + 1, j):
                face((v(i + 1, j, 1), v(i + 1, j + 1, 1), v(i + 1, j + 1, 0), v(i + 1, j, 0)), small)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def dome_halves(st, top, gone_px, x_split=-1.1, hinge=(-2.4, 0.25), tilt=(15.0, 12.0), cut=(1.0, 1.5), da=12.0,
                ds=7.0, t=0.28, seed=0, south=0.45):
    """The Observatory's dome split down a trough into two halves, each
    tipped outward on the chord of its inner foot (the west one west, the
    east one east, their outer feet sinking into the rim), its crown
    broken away 1 to 1.5 cells down in steps on its north slope (south
    times that on its south one), and anything the picture
    shows gone (gone_px, tested where each piece ends up) taken away in
    the same stepped cells. Returns a ray-cast surface of both halves:
    ceiling(x, y) the lowest underside over a plan point and roof(x, y)
    the highest top (-99 where none), with the top edges of the breaks
    as (outer point, inner point, half)."""
    from mathutils.bvhtree import BVHTree
    O = OBS
    cx, cy, R = O["cx"], O["cy"], O["Rd"]
    z0 = top + 0.1
    rng = random.Random(st.seed + seed)
    s1 = math.acos(0.3 / R)
    A = list(np.radians(np.arange(0.0, 360.0 + 1e-6, da)))
    nj = int(math.ceil(math.degrees(s1) / ds))
    Sg = list(np.linspace(0.0, s1, nj + 1))
    ni = len(A) - 1
    mats = {}
    for h, xh, ang in (("W", hinge[0], -tilt[0]), ("E", hinge[1], tilt[1])):
        mats[h] = (Matrix.Translation((xh, cy, z0)) @ Matrix.Rotation(math.radians(ang), 4, "Y")
                   @ Matrix.Translation((-xh, -cy, -z0)))
    pt = lambda a, s_, RR=R: Vector((cx + RR * math.cos(s_) * math.cos(a), cy + RR * math.cos(s_) * math.sin(a),
                                     z0 + RR * math.sin(s_)))
    masks, edges = {}, []
    bms = []
    for h in ("W", "E"):
        M = mats[h]
        P = [[False] * ni for _ in range(nj)]
        for j in range(nj):
            for i in range(ni):
                ac, sc = (A[i] + A[i + 1]) / 2, (Sg[j] + Sg[j + 1]) / 2
                q = pt(ac, sc)
                if (q.x < x_split) != (h == "W"):
                    continue
                aj = ac + rng.uniform(-0.3, 0.3) * (A[i + 1] - A[i])
                sj = sc + rng.uniform(-0.3, 0.3) * (Sg[j + 1] - Sg[j])
                c, r_ = screen(st.r, *(M @ pt(aj, sj)))[:2]
                P[j][i] = not any(akit.inside_poly(G, c, r_) for G in gone_px)
        # the crown broken away in steps a column at a time, deepest on the
        # north slope, which the picture draws heaped with rubble
        zt = max([R * math.sin(Sg[j + 1]) for j in range(nj) for i in range(ni) if P[j][i]] or [0.0])
        for i in range(ni):
            north = max(0.0, math.sin((A[i] + A[i + 1]) / 2))
            lim = zt - rng.uniform(*cut) * (south + (1.0 - south) * north) - (0.4 if rng.random() < 0.25 else 0.0)
            for j in range(nj):
                if R * math.sin(Sg[j + 1]) > lim:
                    P[j][i] = False
        # cells left standing alone go
        for _ in range(2):
            for j in range(nj):
                for i in range(ni):
                    if P[j][i] and sum(1 for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1))
                                       if 0 <= i + di < ni and 0 <= j + dj < nj and P[j + dj][i + di]) < 2 and j > 0:
                        P[j][i] = False
        masks[h] = P
        bm = shell_cells(P, A, Sg, cx, cy, z0, R, R, t)
        bm.transform(M)
        for j in range(nj):
            for i in range(ni):
                if P[j][i] and (j + 1 >= nj or not P[j + 1][i]):
                    a = (A[i] + A[i + 1]) / 2
                    edges.append((M @ pt(a, Sg[j + 1]), M @ pt(a, Sg[j + 1], R - t), h))
        bms.append(bm)
    both = bmesh.new()
    for bm in bms:
        me = bpy.data.meshes.new("tmp")
        bm.to_mesh(me)
        both.from_mesh(me)
        bpy.data.meshes.remove(me)
    tree = BVHTree.FromBMesh(both)
    both.free()
    for bm, h in zip(bms, ("W", "E")):
        st.add(bm, "dome", "dome" + h)

    def ceiling(x, y):
        hit = tree.ray_cast(Vector((x, y, top - 1.0)), Vector((0, 0, 1)), 20.0)
        return hit[0].z if hit[0] is not None else -99.0

    def roof(x, y):
        hit = tree.ray_cast(Vector((x, y, top + 8.0)), Vector((0, 0, -1)), 12.0)
        return hit[0].z if hit[0] is not None else -99.0
    return ceiling, roof, edges


@stage("CreBuild06b")
def cre06b(out, render=True):
    """The Ruined Observatory: the ruin taken further, two storeys down so
    the dome sits 6 cells lower than the intact's, split down a trough
    into two green halves, each tipped outward on its inner foot with its
    crown broken away in steps, deepest on the north slope, the fallen
    crown heaped inside them and spilling from the trough's ends, charred
    beams fallen across it from half to half, blocks and green slabs on
    the broken tops, the rim heaped all round with white blocks, the
    north east of the rim and the wall under it broken off course by
    course, the tower's south face torn out
    in broken courses with its rubble cascading into a pale heap, two
    sooted gears lying on it, and the back broken too: cornices snapped,
    windows knocked out, cracks with soot rising from them and a skirt
    of rubble round the north foot."""
    st = Stage("CreBuild06b", tone=T06B)
    st.settle = True
    O = OBS
    cx, cy = O["cx"], O["cy"]
    NE = jagged(st, ([(108, 0), (190, 0), (190, 96), (160, 84), (148, 66), (140, 44), (132, 22), (120, 8)],), 2.5, 7.0)
    rows = obs_storeys(3, 2.4)
    breach = Breach(st, rows, [(1.0, 100, 74), (3.0, 92, 66), (5.0, 80, 52), (6.6, 62, 38), (obs_top(3, 2.4) - 0.5, 30, 12)],
                    seed=40)
    holes = {(0, 1), (1, 2), (2, 1)}
    top = observatory(st, storeys=3, last=2.4, dome=None, breach=breach, shut=False, gone_px=NE,
                      terrace=[(0, 0.3), (160, 0.3), (185, 0.0), (340, 0.0), (355, 0.3)],
                      cornice_gaps={0: [(52, 78)], 1: [(16, 40), (118, 150)], 2: [(88, 108)]}, skip_win=holes)
    st.cuts3d.append((("marble",), breach.cutter(cx, cy, O["r0"] - 0.8, O["r0"] + 3 * O["dr"] + 0.6)))
    back_damage(st, rows, sorted(holes), [[(40, 0.5), (44, 1.3), (38, 2.2), (43, 3.0), (39, 3.9)],
                                          [(140, 3.3), (136, 4.1), (142, 4.9), (137, 5.8), (141, 6.6), (138, 7.4)],
                                          [(98, 0.5), (101, 1.4), (96, 2.3), (99, 2.9)]])
    Dm = [(64, 0), (100, 0), (98, 30), (94, 60), (100, 90), (98, 116), (80, 126), (62, 118), (64, 88), (58, 50),
          (58, 20)]
    gap = jagged(st, (Dm,), 4.0)
    st.cuts.append((("gold",), gap, "DIFFERENCE", top + 0.3))
    # (the outline carried on up past the picture's top, where the rim's far edge lies)
    NEx = [(c, -40.0 if r_ < 3.0 else r_) for c, r_ in NE[0]]
    st.cuts3d.append((("marble", "trim", "floor", "gold"), ne_courses(st, NEx, top - 2.5, top + 1.2, seed=45)))
    # the dome split down the trough, both halves tipped outward and their
    # crowns broken away in steps, the north east gone in the same steps
    ceiling, roof, brk = dome_halves(st, top, [gap[0], NE[0]], seed=50)
    st.S.cyl(st.M["dark"], cx, cy, top, O["Rd"] - 0.05, 0.12, seg=16)
    # the fallen crown heaped inside, up to just under the halves and above
    # their broken feet in the trough, spilling over the rim at its ends
    under = lambda x, y: ceiling(x, y) > -50

    def fill(x, y):
        r = math.hypot(x - cx, y - cy)
        h = 1.9 - 0.2 * max(0.0, r - 1.2)
        if under(x, y):
            h = min(h, ceiling(x, y) - top - 0.3)
        return h
    ne_gone = lambda x, y: akit.inside_poly(NE[0], *screen(st.r, x, y, top + 1.0)[:2])
    keep = lambda x, y: math.hypot(x - cx, y - cy) < (4.6 if not under(x, y) else 3.9) and not ne_gone(x, y)
    bowl = mound(st, circle_px(st, cx, cy, 4.8, top + 1.0, k=24), fill, ground=top + 0.03, cell=0.45, edge=0.45,
                 rough=0.3, seed=50, keep=keep, clip=lambda x, y: False, zr=(top, top + 2.5))
    hp = clip_px(st, [(56, 0), (102, 0), (100, 60), (104, 120), (80, 130), (58, 122)], top + 1.8,
                 circle=(cx, cy, O["Rd"] - 0.3))
    strew(st, hp, bowl, 26, z_at=top + 1.6, tiles="shard", kinds={"tile": 4, "chunk": 4, "beam": 1},
          size=(0.7, 1.4), length=(1.2, 2.6), chunk=(0.4, 0.85), seed=51, pic=False, stick=0.1)
    strew(st, circle_px(st, cx, cy, O["Rd"] - 0.3, top + 1.6, k=20), bowl, 10, z_at=top + 1.6,
          tiles="shard", kinds={"tile": 4, "chunk": 2}, size=(0.8, 1.4), chunk=(0.4, 0.8), seed=56, pic=False)
    st.soot += [(cx - 1.0, cy, top + 1.5, 2.6, 0.45), (cx + 1.0, cy + 1.5, top + 1.5, 2.2, 0.4)]
    # charred beams fallen across the trough, resting on both halves and sagging into it
    for (c0, r0_), (c1, r1_) in (((52, 28), (106, 50)), ((50, 72), (106, 58)), ((54, 86), (108, 104)),
                                 ((56, 112), (106, 116)), ((56, 12), (104, 14))):
        A = on_ground(st, c0, r0_, roof, top + 3.0)
        Bq = on_ground(st, c1, r1_, roof, top + 3.0)
        if roof(A[0], A[1]) < -50 or roof(Bq[0], Bq[1]) < -50:
            continue
        mx, my = (A[0] + Bq[0]) / 2, (A[1] + Bq[1]) / 2
        mz = bowl(mx, my)
        mz = max(mz + 0.12, min(A[2], Bq[2]) - 1.8) if mz > -50 else min(A[2], Bq[2]) - 1.0
        st.D.beam(st.M["char"], (A[0], A[1], A[2] + 0.06), (mx, my, mz), 0.2)
        st.D.beam(st.M["char"], (mx, my, mz), (Bq[0], Bq[1], Bq[2] + 0.06), 0.2)
    # blocks and green slabs lying on the halves' broken tops, slabs tipping
    # from them onto the heap inside
    rng = random.Random(st.seed + 57)
    for po, pi, h in brk:
        u = rng.random()
        if u < 0.16:
            m = (po + pi) / 2
            sz = rng.uniform(0.35, 0.55)
            st.D.box(st.M[rng.choice(("chip", "stone", "chip"))], m.x, m.y, m.z - sz * 0.35, sz * 1.3, sz, sz * 0.6,
                     yaw=rng.uniform(0, 360), pitch=rng.uniform(-28, 28), roll=rng.uniform(-20, 20))
        elif u < 0.4:
            d = Vector((pi.x - po.x, pi.y - po.y, 0))
            if d.length < 1e-3:
                continue
            d.normalize()
            far = pi + d * rng.uniform(0.9, 1.4)
            zf = bowl(far.x, far.y)
            if zf < -50 or zf > pi.z:
                continue
            side = Vector((-d.y, d.x, 0)) * rng.uniform(0.3, 0.45)
            a0 = pi + Vector((0, 0, 0.03))
            b0 = Vector((far.x, far.y, zf + 0.05))
            st.D.slab(st.M["shard"], [tuple(a0 + side), tuple(a0 - side), tuple(b0 - side * 0.8), tuple(b0 + side * 0.8)],
                      0.18)
    st.halos.append((gap[0], 1.0, 0.45, top + 0.5))
    st.soot += [(po.x, po.y, po.z, 0.9, 0.35) for po, _pi, _h in brk[::3]]
    # white blocks heaped all round the rim
    rim_blocks(st, top, [(0, 0), (188, 0), (188, 225), (0, 225)], 84, 55)
    # the fallen face heaped up the breach to the upper floor, whose rubble cascades onto it
    HF = [(2, 126), (40, 124), (96, 128), (132, 126), (148, 150), (152, 190), (130, 212), (96, 220), (56, 218),
          (18, 202), (2, 168)]
    f = obs_face(st, rows, breach, [-2.8, -2.2], HF,
                 [-6.4, -5.6, -4.8, -4.0, -3.3, -2.6, -2.0, -1.2], [0.15, 1.3, 2.5, 3.5, 4.4, 5.0, 5.2, 4.5],
                 36, 20, buried=1, xc=-0.4, wide=2.6)
    st.halos.append((breach.outline_px(st, cx, cy), 1.2, 0.4, 0.5))
    st.soot += [(x, y, z, 0.9, 0.3) for x, y, z in breach.edges(cx, cy)]
    whole_gear(st, f, 48, 152, 0.75)
    whole_gear(st, f, 108, 186, 0.5, spokes=3)
    # a skirt of rubble round the north foot, under the broken back
    tg = lambda x, y: O["ht"] if math.hypot(x - cx, y - cy) < O["Rt"] - 0.1 else 0.0

    def skirt(x, y):
        d = math.hypot(x - cx, y - cy)
        a = math.degrees(math.atan2(y - cy, x - cx)) % 360.0
        if not 8.0 < a < 172.0 or d < O["r0"] - 0.2:
            return 0.0
        return max(0.0, 1.1 * smooth01(min(a - 8.0, 172.0 - a) / 30.0) * (1.0 - (d - O["r0"]) / 1.5))
    sg = mound(st, circle_px(st, cx, cy, O["r0"] + 1.7, O["ht"], k=24), skirt, ground=tg, cell=0.55, edge=0.4,
               rough=0.4, seed=60, zr=(O["ht"], O["ht"]), keep=lambda x, y: True)
    strew(st, circle_px(st, cx, cy, O["r0"] + 1.6, 0.7, k=24), sg, 30, z_at=0.7, kinds={"chunk": 6, "stone": 3, "slab": 1},
          seed=61, pic=False, tilt=16)
    st.soot += [(cx, cy - 4.0, 4.0, 3.5, 0.3)]
    return finish(st, out, render, steps={"marble": 4.0})


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = OUT
    if "--out" in argv:
        out = argv[argv.index("--out") + 1]
    render = "--norender" not in argv
    names = [a for a in argv if not a.startswith("--") and a != out] or list(STAGES)
    for n in names:
        t0 = time.time()
        try:
            if n in STAGES:
                STAGES[n](out, render)
            else:
                build_intact(n, out, render)
        except Exception as e:
            traceback.print_exc()
            print("G2_FAILED", n, repr(e), flush=True)
        print("G2_TIME", n, "%.1fs" % (time.time() - t0), flush=True)


if __name__ == "__main__":
    main()
