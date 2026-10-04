"""Creon destroyed stages, group 3: the Embassy (CreBuild07) and the two
slate-roofed buildings (CreBuild08, CreBuild09), each as a ruin (a,
"Damaged") and a shell (b, "Destroyed"). Run inside Blender:

    OK_REPLACE=D:/OKBuild/creon-stages/g3/okr \
    blender -b --factory-startup --python g3.py -- CreBuild07a CreBuild07b ...

None of the intact buildings has a model, so each is built here first as
a parametric block read off its own picture (walls by side, cornices,
facade, hip roof, dormers, chimney, porch), in the frame handkit uses:
one unit a cell, -Y toward the classic camera, Z up, the anchor at the
origin. A point (x, y, z) lands on a picture at column hx + 16 x and row
hy - 16 y - 8 z.

A ruin is that block broken where its own picture shows it broken: holes
cut through the roof along the classic camera's line of sight (the Aramon
kit's cut_view), charred floors and rafters in them, rubble heaped where
the picture draws it, pieces of the block's own materials strewn over
the heaps. A shell is the ruin taken further: the roof replaced by its
own tile cut into tilted slabs that the picture draws where they lie,
the walls down to ragged courses, the plan heaped with rubble.

Every texture is made here from noise and numbers (the Aramon kit's
textures and roof maps, marked generated). Soot is a vertex colour that
darkens each part's own material toward breaches and broken tops.

Each model goes to $G3_OUT/<Name>.glb (D:/OKBuild/creon-stages/g3 unless
set), its classic and turned views to $G3_OUT/renders. Naming an intact
(CreBuild07, 08 or 09) builds that block alone into $G3_OUT/check, to
compare the base against its own picture. It does not ship.
"""
import importlib.util
import json
import math
import os
import random
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
for p in (TOOLS,):
    if p not in sys.path:
        sys.path.insert(0, p)

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402
from mathutils.interpolate import poly_3d_calc  # noqa: E402
from mathutils.kdtree import KDTree  # noqa: E402

import handkit as hk  # noqa: E402

DATA = os.environ.get("OK_REPLACE", "D:/OKBuild/creon-stages/g3/okr")
OUT = os.environ.get("G3_OUT", "D:/OKBuild/creon-stages/g3")


def _load_akit():
    """The Aramon ruin kit under its own name, pointed at this work folder."""
    spec = importlib.util.spec_from_file_location("akit", os.path.join(TOOLS, "hand", "aramon_buildings", "kit.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    m.DATA = DATA
    m.OUT = OUT
    m.CAT.update({r["name"]: r for r in json.load(open(DATA + "/catalog.json"))})
    return m


A = _load_akit()
Mat = A.Mat


# ---------------------------------------------------------------- materials

def tex_checker(name, a, b, n=8, size=128, seed=0):
    """Marble paving in a checker of n by n squares a repeat, with thin joints."""
    rng = np.random.default_rng(seed)
    u = (np.arange(size) + 0.5) / size * n
    k = (np.floor(u)[None, :] + np.floor(u)[:, None]) % 2
    a, b = np.array(A.rgb(a), float), np.array(A.rgb(b), float)
    col = np.where(k[..., None] > 0, a[None, None], b[None, None])
    col = col * (1 + 0.06 * (rng.random((size, size, 1)) - 0.5))
    fu = u % 1
    joint = (np.minimum(fu, 1 - fu)[None, :] < 0.05) | (np.minimum(fu, 1 - fu)[:, None] < 0.05)
    col[joint] = col[joint] * 0.75
    return A._to_image(name, np.clip(col, 0, 255))


def tex_slate(name, base, gap, n=8, jv=0.16, ju=0.14, var=0.1, seed=0, size=128):
    """Square tiles or slates in straight columns, n a repeat each way, with
    broad dark joints both ways as these roofs are drawn."""
    rng = np.random.default_rng(seed)
    base, gap = np.array(A.rgb(base), float), np.array(A.rgb(gap), float)
    vv, uu = np.mgrid[0:size, 0:size].astype(float) / size * n
    r, c = np.floor(vv).astype(int), np.floor(uu).astype(int)
    fv, fu = vv - r, uu - c
    k = 1 + rng.normal(0, var, (n, n))[r % n, c % n]
    k = k * (1.06 - 0.14 * fv) * (1 + 0.05 * (rng.random((size, size)) - 0.5))
    col = base[None, None] * k[..., None]
    jt = np.clip(np.maximum((jv - fv) / jv, (ju - fu) / ju), 0, 1)[..., None]
    col = col * (1 - jt * 0.85) + gap * jt * 0.85
    return A._to_image(name, np.clip(col, 0, 255))


def tex_char(name, base="#2c221c", light="#4d3b30", seed=0, size=128):
    """Charred timber: dark planks with checked cracks across the grain and
    a few warm brown highlights where the char flaked off."""
    rng = np.random.default_rng(seed)
    base, light = np.array(A.rgb(base), float), np.array(A.rgb(light), float)
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    grain = 0.5 + 0.5 * np.sin(uu / size * 2 * np.pi * 9 + 3 * A._smooth(rng, size, 5))
    checks = np.abs(((vv / size) * 14 + 0.4 * A._smooth(rng, size, 7)) % 1 - 0.5) < 0.05
    t = np.clip(0.35 * grain + 0.4 * A._smooth(rng, size, 11), 0, 1)
    col = base[None, None] * (0.75 + 0.5 * t[..., None])
    hi = A._smooth(rng, size, 6) > 0.72
    col[hi] = light * (0.85 + 0.3 * rng.random((int(hi.sum()), 1)))
    col[checks] = base * 0.35
    return A._to_image(name, np.clip(col, 0, 255))


LOOKS = {
    # the Embassy: white marble storeys under blue glazed tile
    "CreBuild07": dict(wall="#cfc8b8", trim="#e6dfd0", dark="#1e1d1a", glass="#2a2c30", roof="#42627f", block="#aaa294",
                       tile=0.36, rafter="#4a2e22", joint="#0e151c"),
    # brown brick under green-grey slate, white stone dressings
    "CreBuild08": dict(wall="#6e5a40", trim="#d8d6cf", dark="#1c1915", glass="#4a5260", roof="#5e6350",
                       tile=0.48, rafter="#4a3022", joint="#161a12"),
    # khaki render under slate, marble balcony and arcade
    "CreBuild09": dict(wall="#7a7356", trim="#dcd6c6", dark="#1e1c17", glass="#505a68", roof="#5f6452",
                       tile=0.38, rafter="#4a3022", check=("#b8b8ac", "#8c8c82"), joint="#161a12"),
}


def materials(n, base, dim=1.0):
    """The block's own materials, their colours times dim. n names this
    model, base the intact."""
    L = {k: (A.shade(v, dim) if isinstance(v, str) and v.startswith("#") and k in ("wall", "trim", "roof") else v)
         for k, v in LOOKS[base].items()}
    M = {}
    if base == "CreBuild08":
        M["wall"] = Mat(n + "_wall", tex=A.tex_stone(n + "_wall", L["wall"], var=0.1, rows=10, seed=11,
                                                     mortar="#4a3c2c"), uv=1.6, ref=L["wall"])
    else:
        M["wall"] = Mat(n + "_wall", tex=A.tex_mottle(n + "_wall", L["wall"], var=0.07, seed=11), uv=1.6,
                        ref=L["wall"])
    M["trim"] = Mat(n + "_trim", tex=A.tex_mottle(n + "_trim", L["trim"], var=0.06, seed=12), uv=1.0, ref=L["trim"])
    M["wtop"] = Mat(n + "_wtop", tex=A.tex_rubble(n + "_wtop", A.shade(L["wall"], 0.55), light=L["trim"],
                                                   dark="#14120f", seed=18), uv=1.0, ref=L["wall"])
    M["dark"] = Mat(n + "_dark", L["dark"])
    M["glass"] = Mat(n + "_glass", L["glass"], rough=0.35)
    t = L["tile"]
    M["tile"] = Mat(n + "_tile", tex=tex_slate(n + "_tile", L["roof"], LOOKS[base].get("joint", "#141814"), n=8,
                                               seed=13), uv=8 * t, ref=L["roof"])
    M["rafter"] = Mat(n + "_rafter", tex=tex_char(n + "_rafter", L["rafter"], "#6a4630", seed=14), uv=1.0,
                      ref=L["rafter"])
    M["char"] = Mat(n + "_char", tex=A.tex_grit(n + "_char", "#262220", light="#6a645c", dark="#0a0908", uv=2.0,
                                                 chips=0.1, darks=0.3, seed=15), uv=2.0)
    M["ash"] = Mat(n + "_ash", tex=A.tex_grit(n + "_ash", "#3a3631", light="#8c857a", dark="#141210", uv=2.0,
                                               chips=0.12, darks=0.25, seed=16), uv=2.0)
    # the floors seen through the roof's holes: ash grey with pale grit, few darks
    M["hash"] = Mat(n + "_hash", tex=A.tex_grit(n + "_hash", "#4a453e", light="#9a9284", dark="#26231f", uv=2.0,
                                                 chips=0.16, darks=0.08, blotch=0.35, seed=19), uv=2.0)
    # the burnt floors seen down the darker breaches
    M["dash"] = Mat(n + "_dash", tex=A.tex_grit(n + "_dash", "#221f1b", light="#4a443c", dark="#0a0908", uv=2.0,
                                                 chips=0.08, darks=0.35, blotch=0.3, seed=21), uv=2.0)
    # big marble blocks off the storeys, undimmed so they read pale on the heaps
    blk = LOOKS[base].get("block", "#cdc6b8")
    M["block"] = Mat(n + "_block", tex=A.tex_mottle(n + "_block", blk, var=0.08, seed=20), uv=1.2, ref=blk)
    if "check" in L:
        M["check"] = Mat(n + "_check", tex=tex_checker(n + "_check", *L["check"], n=8, seed=17), uv=2.0)
    return M


def tile_roof(name, g, r, base, holes=(), halo=1.0, course=0.36, tile_w=0.36, var=0.1, gap="#141c24",
              jv=0.2, ju=0.18, soot=0.25, swirl=2.0, width=0.3, soot_col="#16120e", stagger=False, seed=1, ppc=40,
              rim=0.9, rexp=0.7, jk=0.85, jpow=1.0, planes=(1.0, 1.0, 1.0, 1.0), smudge=(), hpad=0.6):
    """A roof map for roof g as these roofs are drawn: square tiles in
    columns up each plane with broad dark joints both ways (jv and ju of
    a tile, jk their darkness, jpow under 1 for crisper ones), soot in
    swirls and haloed round the holes (outlines on the sprite of r), each
    plane (front, back, west, east) times its factor in planes. Registers
    its plan mapping like the Aramon kit's maps. smudge: [(outline, strength)]
    darkened where the picture is sooted though nothing is broken."""
    rng = np.random.default_rng(seed)
    S = min(float(ppc), 1024 / (2 * max(g.hx, g.hy)))
    nu, nv = max(16, int(2 * g.hx * S)), max(16, int(2 * g.hy * S))
    u = (-g.hx + (np.arange(nu) + 0.5) / nu * 2 * g.hx)[None, :].repeat(nv, 0)
    v = (-g.hy + (np.arange(nv) + 0.5) / nv * 2 * g.hy)[:, None].repeat(nu, 1)
    zs = g.ze + (g.hy - np.abs(v)) * g.k
    on_side = zs <= g.end_np(u)
    kend = np.where(u < 0, g.kes[0], g.kes[1])
    dist = np.where(on_side, (g.hy - np.abs(v)) * math.sqrt(1 + g.k ** 2), (g.hx - np.abs(u)) * np.sqrt(1 + kend ** 2))
    along = np.where(on_side, u * np.sign(v + 1e-9), v * np.sign(u + 1e-9))
    plane = np.where(on_side, np.where(v < 0, 0, 1), np.where(u < 0, 2, 3))
    ri = np.floor(dist / course)
    fv = dist / course - ri
    off = (ri % 2) * 0.5 * tile_w if stagger else 0.0
    cu = (along + off) / tile_w
    ci = np.floor(cu)
    fu = cu - ci
    basev, gapv = np.array(A.rgb(base), float), np.array(A.rgb(gap), float)
    ts = 1 + (A._hash2(ri, ci, plane) - 0.5) * 2 * var * 1.7
    kk = ts * (1.06 - 0.16 * np.clip((fv - jv) / (1 - jv), 0, 1)) * (1 + 0.05 * (rng.random((nv, nu)) - 0.5))
    kk = kk * np.array(planes, float)[plane]
    col = basev[None, None] * kk[..., None]
    jt = np.clip(np.maximum((jv - fv) / jv, np.maximum((ju - fu) / ju, (fu - 1 + ju * 0.4) / (ju * 0.4))), 0, 1) ** jpow
    col = col * (1 - jt[..., None] * jk) + gapv * jt[..., None] * jk
    Sq = max(nu, nv)
    tot = np.zeros((nv, nu))
    if soot > 0:
        n1 = (A._smooth(rng, Sq, 3) * 0.65 + A._smooth(rng, Sq, 7) * 0.35)[:nv, :nu]
        band = 0.5 + 0.5 * np.cos(2 * math.pi * n1 * swirl)
        streak = np.clip((band - (1 - width)) / (width * 0.7), 0, 1) ** 1.2
        cov = np.clip((A._smooth(rng, Sq, 2)[:nv, :nu] - 0.25) / 0.35, 0, 1)
        tot = soot * streak * (0.35 + 0.65 * cov)
    for q in holes:
        h = A.hole_centre(r, g, q)
        if h is None:
            continue
        lu, lv = g.local(h[0], h[1])
        rad = h[2] * halo + hpad
        d = np.sqrt((u - lu) ** 2 + (v - lv) ** 2)
        ragn = A._smooth(rng, Sq, 9)[:nv, :nu]
        tot = np.maximum(tot, rim * np.clip(1 - d / (rad * (0.75 + 0.5 * ragn)), 0, 1) ** rexp)
    for q, k in smudge:
        h = A.hole_centre(r, g, q)
        if h is None:
            continue
        lu, lv = g.local(h[0], h[1])
        d = np.sqrt((u - lu) ** 2 + (v - lv) ** 2)
        ragn = A._smooth(rng, Sq, 6)[:nv, :nu]
        tot = np.maximum(tot, k * np.clip(1.6 * (1 - d / (h[2] * (0.8 + 0.5 * ragn))), 0, 1) ** 0.8)
    t = np.clip(tot, 0, 1)[..., None]
    col = col * (1 - t) + np.array(A.rgb(soot_col), float) * t * (0.8 + 0.4 * rng.random((nv, nu, 1)))
    img = A._to_image(name, np.clip(col, 0, 255))
    hx, hy = g.hx, g.hy

    def fn(x, y, g=g, hx=hx, hy=hy):
        lu, lv = g.local(x, y)
        return ((lu + hx) / (2 * hx), (lv + hy) / (2 * hy))
    A.UVFN[name] = fn
    return Mat(name, tex=img, uvfn=name, ref=base)


# ---------------------------------------------------------------- the intact blocks

# walls x0..x1 by yf..yb (front to back) to the eaves at H, a hip roof rising R
# with a ridge rx either side of its middle along axis, t the wall thickness
P07 = dict(base="CreBuild07", x0=-6.0, x1=6.0, yf=-4.5, yb=3.4, H=9.6, R=2.1, rx=2.85, over=0.2, T=0.3, t=0.45,
           axis="x", cornice=0.75, dormer_depth=3.3)
P08 = dict(base="CreBuild08", x0=-6.5, x1=6.56, yf=-3.31, yb=3.81, H=6.5, R=1.8, rx=3.12, over=0.2, T=0.3, t=0.45,
           axis="x", cornice=0.45)
# CreBuild09's lower storey stands forward of its upper one, at y0, to zb
P09 = dict(base="CreBuild09", x0=-4.3, x1=4.375, yf=-3.81, yb=6.04, H=7.3, R=2.1, rx=1.81, over=0.2, T=0.3, t=0.45,
           axis="y", cornice=0.5, y0=-4.81, zb=4.3)
PLANS = {"CreBuild07": P07, "CreBuild08": P08, "CreBuild09": P09}


def roof_dims(P):
    L, W = P["x1"] - P["x0"], P["yb"] - P["yf"]
    cx, cy = (P["x0"] + P["x1"]) / 2, (P["yf"] + P["yb"]) / 2
    yaw = 0.0
    if P["axis"] == "y":
        L, W, yaw = W, L, 90.0
    return cx, cy, L, W, yaw, L / 2 - P["rx"]


def roof_geom(P):
    cx, cy, L, W, yaw, hip = roof_dims(P)
    return A.RoofGeom(cx, cy, L, W, P["H"], P["R"], P["over"], yaw=yaw, kind="hip", hip=hip, T=P["T"])


def roof(B, mat, P):
    cx, cy, L, W, yaw, hip = roof_dims(P)
    with B.at(cx, cy, yaw=yaw):
        A.roof(B, mat, L, W, P["H"], P["R"], kind="hip", over=P["over"], hip=hip, T=P["T"])


class Damage:
    """How a stage breaks the block: each wall run's height along it as
    [(f, h)] (f from 0 at its west or south end to 1), which facade details
    go (above the broken top, buried under the bank, or at random), and the
    seed for the ragged tops."""

    def __init__(self, P, prof=None, drop=0.0, buried=None, seed=0, thick=0.0, grid=(1.8, None)):
        self.P, self.prof, self.drop, self.seed, self.thick, self.grid = P, prof or {}, drop, seed, thick, grid
        self.porch = True
        self.bare = {}  # side: [(f0, f1)] where the cornice broke away though the wall stands
        self.buried = buried or (lambda x: -1.0)

    def h(self, side, f, full):
        q = self.prof.get(side)
        if not q:
            return full
        return min(full, float(np.interp(f, [a for a, _ in q], [b for _, b in q])))

    def keep(self, x, z, side="S"):
        P = self.P
        f = (x - P["x0"]) / (P["x1"] - P["x0"])
        full = P["zb"] if side == "S0" else P["H"]
        if z > self.h(side, f, full) - 0.3 or z < self.buried(x):
            return False
        if self.drop > 0:
            k = math.sin(x * 12.9898 + z * 78.233 + self.seed * 3.7) * 43758.5453
            if k - math.floor(k) < self.drop:
                return False
        return True


INWARD = {"S": (0, 1), "S0": (0, 1), "N": (0, -1), "W": (1, 0), "W0": (1, 0), "E": (-1, 0), "E0": (-1, 0)}


class WallPlan:
    """A wall from plan point a to b, t thick about the line (thick more on
    its inner side, inward the way in), from z0 to H where prof stands full.
    Where lower it breaks off in runs of masonry, each with a slanted,
    chipped top, so the line wanders rather than stepping in merlons. top
    holds each run's top as [(f, z)], f from 0 at a to 1 at b."""

    def __init__(self, a, b, t, prof, H, z0=0.0, seed=0, course=0.32, run=(0.6, 1.4), jitter=0.35, inward=None,
                 thick=0.0, slant=0.4):
        self.a, self.b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
        d = self.b - self.a
        self.L = d.length
        self.d = d.normalized()
        self.n = Vector((-self.d.y, self.d.x, 0))
        self.ta = self.tb = t / 2
        self.inner_a = None
        if inward is not None:
            self.inner_a = self.n.dot(Vector((inward[0], inward[1], 0))) > 0
            if thick:
                if self.inner_a:
                    self.ta += thick
                else:
                    self.tb += thick
        self.z0, self.H = z0, H
        rng = random.Random(seed)
        L = self.L
        if not prof or min(h for _, h in prof) >= H - 1e-3:
            self.top = [[(0.0, H), (1.0, H)]]
            return
        fs = [0.0]
        while fs[-1] < 1.0 - 1e-6:
            fs.append(min(1.0, fs[-1] + rng.uniform(*run) / L))
        if len(fs) > 2 and (fs[-1] - fs[-2]) * L < 0.3:
            del fs[-2]
        ip = ([q[0] for q in prof], [q[1] for q in prof])
        hs = []
        e = 0.0
        for i in range(len(fs) - 1):
            h = min(H, float(np.interp((fs[i] + fs[i + 1]) / 2, *ip)))
            if h < H - 0.05:
                # a wandering break line, so the runs step up and down in flights, not teeth
                e = max(-2 * jitter, min(1.5 * jitter, 0.65 * e + rng.gauss(0.0, jitter)))
                h += e
                if rng.random() < 0.1:
                    h -= rng.uniform(0.3, 0.7)
                h = z0 + course * round((h - z0) / course)
                h = min(H - 0.25, max(z0 + 0.15, h))
            else:
                h = H
            hs.append(h)
        segs = []
        for i, h in enumerate(hs):
            if segs and abs(segs[-1][2] - h) < 1e-6:
                segs[-1][1] = fs[i + 1]
            else:
                segs.append([fs[i], fs[i + 1], h])
        self.top = []
        cap = lambda z: min(H, max(z0 + 0.12, z))  # noqa: E731
        for f0, f1, h in segs:
            if h >= H - 1e-6:
                self.top.append([(f0, H), (f1, H)])
                continue
            w = (f1 - f0) * L
            s = slant * min(1.0, w / 0.9)
            # each run's top slants, with a chip or two knocked out of it
            pts = [(f0, cap(h + rng.uniform(-s, s)))]
            k = 1 if w < 0.8 else 2
            for j in range(k):
                u = f0 + (f1 - f0) * (j + rng.uniform(0.3, 0.7)) / k
                pts.append((u, cap(h + rng.uniform(-s, s) - rng.choice((0.0, 0.0, rng.uniform(0.15, 0.35))))))
            pts.append((f1, cap(h + rng.uniform(-s, s))))
            self.top.append(pts)

    def end_z(self, e):
        return self.top[0][0][1] if e == 0 else self.top[-1][-1][1]

    def hidden(self):
        return max(z for run in self.top for _, z in run) <= self.z0 + 0.1


def _spans(lo, hi, omit):
    """[lo, hi] less the intervals in omit."""
    out = [(lo, hi)]
    for a, b in omit:
        nxt = []
        for p, q in out:
            if b <= p or a >= q:
                nxt.append((p, q))
                continue
            if a > p:
                nxt.append((p, a))
            if b < q:
                nxt.append((b, q))
        out = nxt
    return [(p, q) for p, q in out if q - p > 0.01]


def wall_emit(B, mat, top, W, pull=(0.0, 0.0), omit=((), ()), grid=(None, None)):
    """Builds plan W: faces in mat, broken tops and ends in top. The inner
    face is drawn back pull at each end, for a mitred corner, and each end
    face leaves out the heights in omit, where the wall it meets covers it.
    grid (outer, inner) cuts each face into cells about that size, so the
    vertex soot can blotch and streak across it."""
    if W.hidden():
        return
    a, d, n, L = W.a, W.d, W.n, W.L
    pa, pb = pull

    def pos(f, z, side):
        s = f * L
        if W.inner_a is not None and side == (W.inner_a is True):
            s = min(max(s, pa), L - pb)
        p = a + d * s + (n * W.ta if side else -n * W.tb)
        return Vector((p.x, p.y, z))
    A_ = lambda f, z: pos(f, z, True)  # noqa: E731
    B_ = lambda f, z: pos(f, z, False)  # noqa: E731
    Wq, Tq = [], []
    z0 = W.z0
    for i, run in enumerate(W.top):
        for (f0, h0), (f1, h1) in zip(run, run[1:]):
            for side, P_ in ((True, A_), (False, B_)):
                cell = grid[1] if (W.inner_a is not None and side == (W.inner_a is True)) else grid[0]
                k = max(1, math.ceil((f1 - f0) * L / cell - 0.2)) if cell else 1
                m = max(1, math.ceil((max(h0, h1) - z0) / cell - 0.2)) if cell else 1
                for i_ in range(k):
                    fa, fb = f0 + (f1 - f0) * i_ / k, f0 + (f1 - f0) * (i_ + 1) / k
                    ta, tb = h0 + (h1 - h0) * i_ / k, h0 + (h1 - h0) * (i_ + 1) / k
                    for j in range(m):
                        za0, za1 = z0 + (ta - z0) * j / m, z0 + (ta - z0) * (j + 1) / m
                        zb0, zb1 = z0 + (tb - z0) * j / m, z0 + (tb - z0) * (j + 1) / m
                        if side:
                            Wq.append([P_(fa, za0), P_(fa, za1), P_(fb, zb1), P_(fb, zb0)])
                        else:
                            Wq.append([P_(fa, za0), P_(fb, zb0), P_(fb, zb1), P_(fa, za1)])
            Tq.append([A_(f0, h0), B_(f0, h0), B_(f1, h1), A_(f1, h1)])
        if i + 1 < len(W.top):
            f1, h = run[-1]
            h2 = W.top[i + 1][0][1]
            lo, hi = min(h, h2), max(h, h2)
            if hi - lo > 1e-4:
                if h > h2:
                    Tq.append([A_(f1, lo), A_(f1, hi), B_(f1, hi), B_(f1, lo)])
                else:
                    Tq.append([A_(f1, lo), B_(f1, lo), B_(f1, hi), A_(f1, hi)])
    za, zb = W.end_z(0), W.end_z(1)
    for lo, hi in _spans(z0, za, omit[0]):
        Wq.append([A_(0.0, lo), B_(0.0, lo), B_(0.0, hi), A_(0.0, hi)])
    for lo, hi in _spans(z0, zb, omit[1]):
        Wq.append([A_(1.0, lo), A_(1.0, hi), B_(1.0, hi), B_(1.0, lo)])
    for mat_, quads in ((mat, Wq), (top, Tq)):
        V, F = [], []
        for q in quads:
            u = []
            for p in q:
                if not u or (p - u[-1]).length > 1e-5:
                    u.append(p)
            if len(u) > 1 and (u[0] - u[-1]).length < 1e-5:
                u.pop()
            if len(u) < 3:
                continue
            nv = Vector((0.0, 0.0, 0.0))
            for k_ in range(len(u)):
                nv += u[k_].cross(u[(k_ + 1) % len(u)])
            if nv.length < 2e-3:
                continue
            F.append(tuple(range(len(V), len(V) + len(u))))
            V += [tuple(p) for p in u]
        B.mesh(mat_, V, F)


def wall_run(B, mat, top, a, b, t, prof, H, z0=0.0, seed=0, jitter=0.35, inward=None, thick=0.0, **kw):
    """A free standing wall run with square ends (cross walls)."""
    wall_emit(B, mat, top, WallPlan(a, b, t, prof, H, z0=z0, seed=seed, jitter=jitter, inward=inward, thick=thick,
                                    **kw))


def runs(P):
    """Each wall run (plan ends on its middle line, base and full height, by
    side) and the corners where two runs meet mitred: (run, end, run, end)."""
    x0, x1, yf, yb, t, H = P["x0"], P["x1"], P["yf"], P["yb"], P["t"], P["H"]
    if P["base"] == "CreBuild09":
        y0, zb = P["y0"], P["zb"]
        R = {"S0": ((x0, y0 + t / 2), (x1, y0 + t / 2), 0.0, zb),
             "S": ((x0, yf + t / 2), (x1, yf + t / 2), zb, H),
             "N": ((x0, yb - t / 2), (x1, yb - t / 2), 0.0, H),
             "W0": ((x0 + t / 2, y0), (x0 + t / 2, yf), 0.0, zb),
             "E0": ((x1 - t / 2, y0), (x1 - t / 2, yf), 0.0, zb),
             "W": ((x0 + t / 2, yf), (x0 + t / 2, yb), 0.0, H),
             "E": ((x1 - t / 2, yf), (x1 - t / 2, yb), 0.0, H)}
        C = [("S0", 0, "W0", 0), ("S0", 1, "E0", 0), ("S", 0, "W", 0), ("S", 1, "E", 0), ("N", 0, "W", 1),
             ("N", 1, "E", 1)]
        return R, C
    R = {"S": ((x0, yf + t / 2), (x1, yf + t / 2), 0.0, H),
         "N": ((x0, yb - t / 2), (x1, yb - t / 2), 0.0, H),
         "W": ((x0 + t / 2, yf), (x0 + t / 2, yb), 0.0, H),
         "E": ((x1 - t / 2, yf), (x1 - t / 2, yb), 0.0, H)}
    return R, [("S", 0, "W", 0), ("S", 1, "E", 0), ("N", 0, "W", 1), ("N", 1, "E", 1)]


def walls(B, M, P, D):
    """The wall runs as separate solids, each to its profile, mitred at the
    corners so no two faces share a plane: where both walls stand the
    corner is closed inside, above the lower one the higher shows its end."""
    R, C = runs(P)
    plans = {side: WallPlan(a, b, P["t"], D.prof.get(side), H, z0=z0, seed=D.seed * 31 + i, inward=INWARD[side],
                            thick=D.thick)
             for i, (side, (a, b, z0, H)) in enumerate(R.items())}
    pull, omit = {}, {}
    dp = P["t"] + D.thick
    for s1, e1, s2, e2 in C:
        p1, p2 = plans[s1], plans[s2]
        pull[s1, e1] = pull[s2, e2] = dp
        if not p2.hidden():
            omit[s1, e1] = [(p2.z0, p2.end_z(e2))]
        if not p1.hidden():
            omit[s2, e2] = [(p1.z0, p1.end_z(e1))]
    for side, W in plans.items():
        wall_emit(B, M["wall"], M["wtop"], W, pull=(pull.get((side, 0), 0.0), pull.get((side, 1), 0.0)),
                  omit=(omit.get((side, 0), ()), omit.get((side, 1), ())), grid=D.grid)
    return plans


OUT_N = {"S": (0, -1), "N": (0, 1), "W": (-1, 0), "E": (1, 0)}


def plan_low(W, f0, f1):
    """The lowest top of wall plan W between f0 and f1."""
    z = W.H
    for run in W.top:
        for (a, ha), (b, hb) in zip(run, run[1:]):
            if b < f0 or a > f1:
                continue
            for f in (max(a, f0), min(b, f1)):
                t = (f - a) / max(b - a, 1e-9)
                z = min(z, ha + (hb - ha) * t)
    return z


def cornice(B, M, P, D, c=0.3, seg=1.6, plans=None):
    """The moulded band c proud of the walls under the eaves, in runs of
    about seg cells, where the wall under it still stands to the eaves (as
    built, when plans gives the walls)."""
    x0, x1, yf, yb, t, h, H = P["x0"], P["x1"], P["yf"], P["yb"], P["t"], P["cornice"], P["H"]
    for side in "SNWE":
        sg = -1 if side in "SW" else 1
        if side in "SN":
            yl = yf + t / 2 if side == "S" else yb - t / 2
            lo, hi = x0 - c, x1 + c
            span = (x0, x1)
        else:
            xl = x0 + t / 2 if side == "W" else x1 - t / 2
            lo, hi = yf + t + 0.004, yb - t - 0.004
            span = (yf, yb)
        depth = t + c
        k = max(1, int(round((hi - lo) / seg)))
        kept = []
        for i in range(k):
            u0, u1 = lo + (hi - lo) * i / k, lo + (hi - lo) * (i + 1) / k
            fs = [min(1.0, max(0.0, (u - span[0]) / (span[1] - span[0]))) for u in (u0, (u0 + u1) / 2, u1)]
            if min(D.h(side, f, H) for f in fs) < H - 0.05:
                continue
            if any(a < fs[2] and fs[0] < b for a, b in D.bare.get(side, ())):
                continue
            if plans and side in plans and plan_low(plans[side], fs[0] - 0.02, fs[2] + 0.02) < H - 0.05:
                continue
            # neighbouring lengths kept join, up to about 2.5 cells; its top stands a hair over the wall's
            if kept and abs(kept[-1][1] - u0) < 1e-6 and u1 - kept[-1][0] < 2.6:
                kept[-1][1] = u1
            else:
                kept.append([u0, u1])
        for u0, u1 in kept:
            um = (u0 + u1) / 2
            if side in "SN":
                y = yl + sg * c / 2
                B.box(M["trim"], um, y, H - h, u1 - u0 - 0.004, depth, h + 0.03)
                if side == "S":
                    B.box(M["trim"], um, y + 0.08, H - h - 0.18, u1 - u0 - 0.004, depth - 0.16, 0.18)
            else:
                x = xl + sg * c / 2
                B.box(M["trim"], x, um, H - h, depth, u1 - u0 - 0.004, h + 0.03)


def window(B, M, x, y, z0, w, h, fr=0.09, arch=False, glass="glass", sill=True):
    """A window on a south face at y: dark glass in a pale frame, an arched
    head if asked."""
    d = 0.07
    B.box(M[glass], x, y - d / 2, z0, w, d, h)
    B.box(M["trim"], x - w / 2 - fr / 2, y - d / 2 - 0.01, z0, fr, d + 0.04, h)
    B.box(M["trim"], x + w / 2 + fr / 2, y - d / 2 - 0.01, z0, fr, d + 0.04, h)
    if sill:
        B.box(M["trim"], x, y - d / 2 - 0.04, z0 - fr, w + 2.6 * fr, d + 0.12, fr)
    if arch:
        r = w / 2
        arch_head(B, M["trim"], x, y + 0.02, z0 + h - 0.01, r + fr, 0.14)
        arch_head(B, M[glass], x, y, z0 + h - 0.01, r, 0.16)
    else:
        B.box(M["trim"], x, y - d / 2 - 0.01, z0 + h, w + 2 * fr, d + 0.04, fr)


def arch_head(B, mat, x, y, z, r, depth, seg=6):
    """A round window head on a south face at y: a half disc standing on z,
    depth thick toward -y."""
    pts = [(x + r * math.cos(math.pi * i / seg), z + r * math.sin(math.pi * i / seg)) for i in range(seg + 1)]
    n = len(pts)
    V = [(px_, y, pz) for px_, pz in pts] + [(px_, y - depth, pz) for px_, pz in pts]
    F = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))] + [(i, i + 1, n + i + 1, n + i) for i in range(n - 1)]
    F.append((n - 1, 0, n, 2 * n - 1))
    B.solid(mat, V, F)


def half_disc(B, mat, x, y, z, rx, ry, h, seg=10, ring=None):
    """A half round slab standing forward (-y) of a wall at y, or with ring a
    curved parapet that thick round its edge."""
    pts = [(x + rx * math.cos(math.pi + math.pi * i / seg), y + ry * math.sin(math.pi + math.pi * i / seg))
           for i in range(seg + 1)]
    if ring is None:
        B.prism(mat, pts, z, z + h)
        return
    inner = [(x + (rx - ring) * math.cos(math.pi + math.pi * i / seg),
              y + (ry - ring) * math.sin(math.pi + math.pi * i / seg)) for i in range(seg + 1)]
    B.prism(mat, pts + inner[::-1], z, z + h)


def dormer(B, M, g, x, yfront, w=1.15, wall=0.6, peak=0.45, depth=2.3):
    """A pointed dormer on the front slope: a gabled front with its window,
    its two roof planes drawn back to a point on the main roof, the east one
    in shade (M["shade"]) if the block has it."""
    zs = g.top(x, yfront)
    zq = g.top(x, yfront + depth)
    BL = (x - w / 2, yfront, zs - 0.12)
    BR = (x + w / 2, yfront, zs - 0.12)
    TL = (x - w / 2, yfront, zs + wall)
    TR = (x + w / 2, yfront, zs + wall)
    Pk = (x, yfront, zs + wall + peak)
    Q = (x, yfront + depth, zq - 0.05)
    if "shade" in M:
        B.solid(M["tile"], [BL, BR, TR, Pk, TL, Q], [(0, 1, 2, 3, 4), (0, 4, 5), (4, 3, 5), (2, 1, 5), (1, 0, 5)])
        B.mesh(M["shade"], [Pk, TR, Q], [(0, 1, 2)])
    else:
        B.solid(M["tile"], [BL, BR, TR, Pk, TL, Q], [(0, 1, 2, 3, 4), (0, 4, 5), (4, 3, 5), (3, 2, 5), (2, 1, 5),
                                                    (1, 0, 5)])
    window(B, M, x, yfront, zs + 0.05, w * 0.5, wall - 0.1, fr=0.08)


def chimney(B, M, x, y, z0, z1, sx=1.2, sy=0.7):
    B.box(M["wall"], x, y, z0, sx, sy, z1 - z0)
    B.box(M["trim"], x, y, z1, sx + 0.2, sy + 0.2, 0.18)
    for i in range(3):
        B.box(M["dark"], x - sx / 3 + i * sx / 3, y, z1 + 0.18, 0.22, 0.22, 0.35)


def band(B, mat, xa, xb, y, z, d, h, keep, seg=1.5):
    """A horizontal moulding along a south face in runs of about seg, each
    kept or not as keep says at its middle."""
    k = max(1, int(round((xb - xa) / seg)))
    kept = []
    for i in range(k):
        u0, u1 = xa + (xb - xa) * i / k, xa + (xb - xa) * (i + 1) / k
        if keep((u0 + u1) / 2, z + h / 2):
            if kept and abs(kept[-1][1] - u0) < 1e-6:
                kept[-1][1] = u1
            else:
                kept.append([u0, u1])
    for u0, u1 in kept:
        B.box(mat, (u0 + u1) / 2, y, z, u1 - u0 - 0.004, d, h)


def side_windows(B, M, P, D, sides, rows, step=1.6, w=0.55, edge=0.9):
    """Dark window bays along the given sides' outer faces, every step
    along, one per (z, h) in rows, where the wall still stands over them."""
    x0, x1, yf, yb = P["x0"], P["x1"], P["yf"], P["yb"]
    for side in sides:
        if side in "NS":
            lo, hi, fixed = x0, x1, (yb if side == "N" else yf)
        else:
            lo, hi, fixed = yf, yb, (x1 if side == "E" else x0)
        k = int((hi - lo - 2 * edge) / step)
        for i in range(k + 1):
            u = lo + edge + i * (hi - lo - 2 * edge) / max(k, 1)
            f = (u - lo) / (hi - lo) if side in "NS" else (u - yf) / (yb - yf)
            for z, h in rows:
                if z + h > D.h(side, f, P["H"]) - 0.4:
                    continue
                if side in "NS":
                    B.box(M["dark"], u, fixed, z, w, 0.12, h)
                else:
                    B.box(M["dark"], fixed, u, z, 0.12, w, h)


def facade07(B, M, P, keep=lambda x, z: True):
    """The Embassy's front: three storeys of marble, each under a bracketed
    cornice, dark window bays between."""
    x0, x1, y = P["x0"], P["x1"], P["yf"]
    cx, L = (x0 + x1) / 2, x1 - x0
    band(B, M["trim"], x0 - 0.05, x1 + 0.05, y - 0.12, 7.0, 0.34, 0.32, keep)
    band(B, M["trim"], x0 - 0.05, x1 + 0.05, y - 0.12, 3.3, 0.34, 0.32, keep)
    xs = [x0 + 0.6 + i * (L - 1.2) / 9 for i in range(10)]
    for z in (8.3, 6.65, 2.95):
        for x in xs:
            if keep(x, z):
                B.box(M["trim"], x, y - 0.1, z, 0.22, 0.28, 0.36 if z > 3 else 0.3)
    for x in xs[:-1]:
        xm = x + (xs[1] - xs[0]) / 2
        if keep(xm, 7.9):
            B.box(M["dark"], xm, y - 0.03, 7.5, 0.6, 0.06, 0.85)
        if keep(xm, 0.9):
            B.box(M["dark"], xm, y - 0.03, 0.45, 0.5, 0.06, 1.0)
    # the long loggia of the middle storey, and its one window at the west end
    band(B, M["dark"], -4.2, 5.4, y - 0.03, 4.0, 0.06, 1.3, keep, seg=1.2)
    if keep(-5.0, 4.6):
        B.box(M["dark"], -5.0, y - 0.03, 4.0, 0.45, 0.06, 1.3)
    band(B, M["trim"], x0 - 0.08, x1 + 0.08, y - 0.08, 0.0, 0.2, 0.25, keep, seg=2.0)


def facade08(B, M, P, keep=lambda x, z: True, porch=True):
    """Brick between stone quoins: a row of small windows, a row of arched
    ones in pale surrounds, small ones below, a door on a round porch."""
    x0, x1, y = P["x0"], P["x1"], P["yf"]
    cx = 0.0
    # quoins up both corners
    for xq in (x0 + 0.2, x1 - 0.2):
        for i in range(8):
            z = 0.1 + i * 0.78
            if keep(xq, z):
                B.box(M["trim"], xq, y - 0.04, z, 0.55 if i % 2 else 0.42, 0.12, 0.42)
    for c in (22, 46, 70, 94, 118, 142, 166, 190):
        x = (c - 106) / 16.0
        if keep(x, 5.4):
            window(B, M, x, y, 5.0, 0.42, 0.55, fr=0.07)
    for c in (27, 59, 91, 123, 155, 187):
        x = (c - 106) / 16.0
        if keep(x, 3.2):
            window(B, M, x, y, 2.2, 0.55, 1.45, fr=0.13, arch=True)
            B.box(M["trim"], x, y - 0.1, 1.85, 1.05, 0.22, 0.2)
    for c in (25, 50, 74, 139, 162, 186):
        x = (c - 106) / 16.0
        if keep(x, 1.1):
            window(B, M, x, y, 0.75, 0.42, 0.6, fr=0.07)
    if keep(cx, 1.2):
        window(B, M, cx, y, 0.3, 1.2, 2.0, fr=0.16, glass="glass")
        B.box(M["trim"], cx, y - 0.2, 2.4, 1.9, 0.45, 0.22)
    if porch and keep(cx, 0.2):
        half_disc(B, M["trim"], cx, y, 0.0, 1.2, 1.15, 0.16, seg=10)
        half_disc(B, M["trim"], cx, y, 0.16, 1.0, 0.8, 0.14, seg=10)


def facade09(B, M, P, keep=lambda x, z: True):
    """An arcade of three bays below, each with a pair of arched windows over
    a round balcony, a marble balcony along the upper storey with its five
    arched windows, and a round porch in the middle."""
    y0, yf, zb = P["y0"], P["yf"], P["zb"]
    x0, x1 = P["x0"], P["x1"]
    up = keep
    keep = lambda x, z: up(x, min(z, zb - 0.35), "S0")  # noqa: E731
    for bx in (-2.44, 0.06, 2.56):
        if keep(bx, 3.0):
            for dx in (-0.42, 0.42):
                window(B, M, bx + dx, y0, 2.85, 0.5, 0.95, fr=0.1, arch=True, sill=False)
            half_disc(B, M["trim"], bx, y0, 2.45, 1.2, 1.05, 0.22, seg=8)
            half_disc(B, M["trim"], bx, y0, 2.67, 1.2, 1.05, 0.38, seg=8, ring=0.14)
        if keep(bx, 1.0) and abs(bx) > 1:
            B.box(M["dark"], bx, y0 - 0.03, 0.3, 1.1, 0.06, 1.7)
    if keep(0.06, 0.6):
        B.box(M["dark"], 0.06, y0 - 0.03, 0.3, 1.0, 0.06, 1.9)
        half_disc(B, M["trim"], 0.06, y0, 0.0, 1.15, 1.1, 0.16, seg=10)
        half_disc(B, M["trim"], 0.06, y0, 0.16, 0.95, 0.8, 0.14, seg=10)
    # quoins on the lower storey's corners
    for xq in (x0 + 0.2, x1 - 0.2):
        for i in range(5):
            z = 0.1 + i * 0.8
            if keep(xq, z):
                B.box(M["trim"], xq, y0 - 0.04, z, 0.5 if i % 2 else 0.38, 0.12, 0.45)
    # the balcony: paving, a balustrade round its edge
    cx = (x0 + x1) / 2
    # the balcony floor runs in under the upper wall, its top a little over the storey's walls
    yq0, yq1 = y0 - 0.04, yf + P["t"]
    band(B, M["check"], x0 + 0.03, x1 - 0.03, (yq0 + yq1) / 2, zb - 0.12, yq1 - yq0, 0.18, keep, seg=1.5)
    t = 0.24
    for i in range(4):
        xa, xb = x0 + i * (x1 - x0) / 4, x0 + (i + 1) * (x1 - x0) / 4
        if keep((xa + xb) / 2, zb + 0.3):
            B.box(M["trim"], (xa + xb) / 2, y0 + t / 2, zb, xb - xa - 0.004, t, 0.6)
    for xs in (x0 + t / 2, x1 - t / 2):
        if keep(xs, zb + 0.3):
            B.box(M["trim"], xs, (y0 + t + yf) / 2, zb, t, yf - y0 - t - 0.004, 0.6)
    band(B, M["trim"], x0 - 0.1, x1 + 0.1, y0 + 0.1, zb - 0.32, 0.42, 0.18, keep, seg=1.5)
    # the upper storey: a checker dado and five arched windows
    for c in (22, 46, 70, 94, 118):
        x = (c - 71) / 16.0
        if up(x, 6.0):
            window(B, M, x, yf, 5.5, 0.5, 1.0, fr=0.11, arch=True)
    band(B, M["check"], x0 + 0.15, x1 - 0.15, yf - 0.02, zb, 0.04, 1.0, up, seg=1.5)


def intact(B, M, P, g, D=None, dormers=(-3.0, 0.06, 2.9), chimney_at=True, roof_on=True):
    """The whole block, or as much of it as D (a Damage) leaves standing:
    wall runs, cornice, facade, dormers or chimney, roof."""
    D = D or Damage(P)
    base = P["base"]
    plans = walls(B, M, P, D)
    cornice(B, M, P, D, plans=plans)
    if base == "CreBuild09":
        facade09(B, M, P, D.keep)
        if chimney_at:
            chimney(B, M, 0.05, 5.1, g.top(0.05, 5.1) - 0.5, 9.9, sx=1.3, sy=0.8)
    elif base == "CreBuild07":
        facade07(B, M, P, D.keep)
        for x in dormers:
            dormer(B, M, g, x, -4.0, depth=P.get("dormer_depth", 2.3))
    else:
        facade08(B, M, P, D.keep, porch=D.porch)
        if chimney_at:
            chimney(B, M, -4.9, 0.9, g.top(-4.9, 0.9) - 0.5, 9.0, sx=1.3, sy=0.75)
    if roof_on:
        roof(B, M["roof"], P)


# ---------------------------------------------------------------- breaking

def rough_outline(poly, keep=(), amp=2.5, step=5.0, seed=0, broken=None):
    """An outline broken along its sides but those in keep (indices), which
    stay straight; with broken, only those sides break. Returns the new
    outline, the kept sides' new indices and each old corner's new index."""
    rng = random.Random(seed)
    out, kept, where = [], [], []
    for i in range(len(poly)):
        a, b = Vector(poly[i]), Vector(poly[(i + 1) % len(poly)])
        d = b - a
        where.append(len(out))
        if i in keep or d.length < step or (broken is not None and i not in broken):
            if i in keep:
                kept.append(len(out))
            out.append(tuple(a))
            continue
        k = max(1, int(d.length / step))
        nrm = Vector((-d.y, d.x)).normalized()
        for j in range(k):
            p = a + d * (j / k)
            if j:
                p = p + nrm * ((rng.random() - 0.5) * 2 * amp)
            out.append((p.x, p.y))
    return out, kept, where


def on_plane(r, c, rw, p0, n):
    """The point the classic camera sees at sprite pixel (c, rw) on the plane
    through p0 with normal n."""
    hx, hy = r["sprite"]["hotspot"]
    x, a = (c - hx) / 16.0, (hy - rw) / 16.0
    z = (n.z * p0.z - n.x * (x - p0.x) - n.y * (a - p0.y)) / (n.z - n.y / 2)
    return Vector((x, a - z / 2, z))


def _clip_y(pts, ymax):
    """A polygon cut back to y <= ymax."""
    out = []
    for i in range(len(pts)):
        a, b = pts[i - 1], pts[i]
        ia, ib = a.y <= ymax, b.y <= ymax
        if ia != ib:
            t = (ymax - a.y) / (b.y - a.y)
            out.append(a + (b - a) * t)
        if ib:
            out.append(b)
    return out


PLATES = []  # every slab laid: (its top corners, its normal), for laying pieces on


def plates_ground(x, y):
    """The top of the highest slab over plan point (x, y), or -100."""
    z = -100.0
    for pts, n in PLATES:
        if n.z > 0.2 and A.inside_poly([(p.x, p.y) for p in pts], x, y):
            p0 = pts[0]
            z = max(z, p0.z - (n.x * (x - p0.x) + n.y * (y - p0.y)) / n.z)
    return z


def _plate(B, mat, pts, T, edge, edge_mat, ew, n, ymax=None):
    area = sum(pts[i].x * pts[i - 1].y - pts[i - 1].x * pts[i].y for i in range(len(pts)))
    m = len(pts)
    if area < 0:
        pts = pts[::-1]
        edge = [(m - 2 - i) % m for i in (edge or ())]
    segs = [(pts[i], pts[(i + 1) % m]) for i in (edge or ())]
    if ymax is not None and max(p.y for p in pts) > ymax:
        pts = _clip_y(pts, ymax)
        cut = []
        for a, b in segs:
            if a.y > ymax and b.y > ymax:
                continue
            if a.y > ymax:
                a = a + (b - a) * ((ymax - a.y) / (b.y - a.y))
            elif b.y > ymax:
                b = b + (a - b) * ((ymax - b.y) / (a.y - b.y))
            cut.append((a, b))
        segs = cut
    if len(pts) < 3:
        return pts
    B.slab(mat, [tuple(p) for p in pts], T)
    PLATES.append(([p.copy() for p in pts], Vector(n).normalized()))
    if edge_mat is not None:
        for a, b in segs:
            if (b - a).length > 0.1:
                B.beam(edge_mat, tuple(a - n * (T * 0.5)), tuple(b - n * (T * 0.5)), ew, T * 1.6)
    return pts


def slab_px(B, mat, r, poly, zc, n=(0.0, 0.0, 1.0), T=0.2, edge=None, edge_mat=None, ew=0.3, rough=0.0, seed=0,
            broken=None, ymax=None):
    """A plate the classic camera sees as outline poly (sprite pixels of
    feature r), lying in the plane through the outline's middle at height
    zc with normal n. With edge, a band of edge_mat runs along those
    outline sides (indices), the eave or cornice it broke away with. With
    rough, its other sides (or only those in broken) are broken that many
    pixels."""
    if rough > 0:
        poly, edge, _ = rough_outline(poly, edge or (), amp=rough, seed=seed, broken=broken)
    n = Vector(n).normalized()
    cc = sum(c for c, _ in poly) / len(poly)
    rc = sum(w for _, w in poly) / len(poly)
    x0, y0 = A.px(r, cc, rc, zc)
    p0 = Vector((x0, y0, zc))
    pts = [on_plane(r, c, rw, p0, n) for c, rw in poly]
    return _plate(B, mat, pts, T, edge, edge_mat, ew, n, ymax)


def fold_px(B, mat, r, poly, crease, zc, n, angle, T=0.25, edge=(), edge_mat=None, ew=0.3, hip_mat=None,
            rough=0.0, broken=None, seed=0, ymax=None):
    """A roof section with a hip in it: outline poly (sprite pixels) folded
    along the line between its corners crease = (i, j). The part from i to
    j lies in the plane (zc, n) as slab_px lays it, the rest is turned
    about the crease by angle degrees. hip_mat draws the hip's cap."""
    m0 = len(poly)
    if rough > 0:
        poly, _, where = rough_outline(poly, (), amp=rough, seed=seed, broken=broken)
    else:
        where = list(range(m0))
    m = len(poly)
    ci, cj = where[crease[0]], where[crease[1]]
    n = Vector(n).normalized()
    cc = sum(c for c, _ in poly) / m
    rc = sum(w for _, w in poly) / m
    x0, y0 = A.px(r, cc, rc, zc)
    P1 = [on_plane(r, c, rw, Vector((x0, y0, zc)), n) for c, rw in poly]
    axis = (P1[cj] - P1[ci]).normalized()
    n2 = (Matrix.Rotation(math.radians(angle), 3, axis) @ n).normalized()
    if n2.z < 0:
        n2 = -n2
    idx1 = [(ci + k) % m for k in range((cj - ci) % m + 1)]
    idx2 = [(cj + k) % m for k in range((ci - cj) % m + 1)]
    P = list(P1)
    for k in idx2[1:-1]:
        P[k] = on_plane(r, poly[k][0], poly[k][1], P1[ci], n2)
    eset = set(where[e] for e in edge)
    e1 = [k for k, i in enumerate(idx1[:-1]) if i in eset]
    e2 = [k for k, i in enumerate(idx2[:-1]) if i in eset]
    _plate(B, mat, [P[i] for i in idx1], T, e1, edge_mat, ew, n, ymax)
    _plate(B, mat, [P[i] for i in idx2], T, e2, edge_mat, ew, n2, ymax)
    if hip_mat is not None:
        a, b = P[ci], P[cj]
        if ymax is not None and a.y > ymax:
            a = a + (b - a) * ((ymax - a.y) / (b.y - a.y))
        if ymax is not None and b.y > ymax:
            b = b + (a - b) * ((ymax - b.y) / (a.y - b.y))
        B.beam(hip_mat, tuple(a + (n + n2).normalized() * 0.02), tuple(b + (n + n2).normalized() * 0.02), 0.16, 0.12)
    return P


def rafter_px(B, mat, r, g, a, b, lift=0.9, w=0.16, drop=None):
    """A charred rafter the picture draws from pixel a to pixel b: its foot
    under the roof where a is, its broken end lift above the roof's plane
    at b (or drop below it)."""
    ha = g.hit(r, *a)
    if ha is None:
        return
    pa = Vector(ha) - Vector((0, 0, g.T * 0.7))
    hb = g.hit(r, *b)
    zb = (hb[2] if hb else ha[2]) + (lift if drop is None else -drop)
    xb, yb = A.px(r, b[0], b[1], zb)
    B.beam(mat, tuple(pa), (xb, yb, zb), w, w * 1.15)


def rest_zc(r, poly, n, ground, lift=0.06, z=4.0, floor=0.0):
    """The zc at which slab_px lays outline poly with normal n so its
    lowest corner rests on ground (each corner slides along its sight line
    as zc changes, so this settles it by a few steps)."""
    n = Vector(n).normalized()
    cc = sum(c for c, _ in poly) / len(poly)
    rc = sum(w for _, w in poly) / len(poly)
    for _ in range(12):
        x0, y0 = A.px(r, cc, rc, z)
        pts = [on_plane(r, c, rw, Vector((x0, y0, z)), n) for c, rw in poly]
        gap = min(p.z - max(floor, ground(p.x, p.y)) for p in pts)
        if abs(gap - lift) < 0.01:
            break
        z -= (gap - lift) * 0.8
    return z


def plate_points(r, poly, zc, n, crease=None, angle=0.0, p0=None):
    """Where slab_px (or fold_px, with a crease) puts the corners of
    outline poly, unroughened; p0, a point on the plane, overrides zc."""
    n = Vector(n).normalized()
    if p0 is None:
        cc = sum(c for c, _ in poly) / len(poly)
        rc = sum(w for _, w in poly) / len(poly)
        x0, y0 = A.px(r, cc, rc, zc)
        p0 = Vector((x0, y0, zc))
    P = [on_plane(r, c, rw, Vector(p0), n) for c, rw in poly]
    if crease:
        ci, cj = crease
        m = len(poly)
        axis = (P[cj] - P[ci]).normalized()
        n2 = (Matrix.Rotation(math.radians(angle), 3, axis) @ n).normalized()
        if n2.z < 0:
            n2 = -n2
        for k in [(cj + k) % m for k in range((ci - cj) % m + 1)][1:-1]:
            P[k] = on_plane(r, poly[k][0], poly[k][1], P[ci], n2)
    return P


def settle(r, poly, n, ground, crease=None, angle=0.0, ymax=None, lift=0.08, floor=0.0):
    """A height for a slab drawn as outline poly with normal n: its lowest
    corner resting on ground (corners behind ymax, which the builders clip
    away, are left out of the reckoning). Returns (zc, n)."""
    n = Vector(n).normalized()

    def gap(P):
        Q = [p for p in P if ymax is None or p.y <= ymax] or P
        return min(p.z - max(floor, ground(p.x, p.y)) for p in Q)
    z = 4.0
    for _ in range(16):
        P = plate_points(r, poly, z, n, crease, angle)
        g_ = gap(P)
        if abs(g_ - lift) < 0.01:
            break
        z -= (g_ - lift) * 0.8
    return z, tuple(n)


def rafter_on(B, mat, r, ground, a, b, la=0.1, lb=0.6, w=0.16, ymax=None):
    """A charred timber the picture draws from pixel a to pixel b, its foot
    la above the ground under a, its other end lb above the ground under b.
    An end that would land behind ymax is held at ymax, high as drawn."""
    hy = r["sprite"]["hotspot"][1]

    def at(c, rw, lift):
        z = 4.0
        for _ in range(12):
            x, y = A.px(r, c, rw, z)
            z = max(0.0, ground(x, y)) + lift
        x, y = A.px(r, c, rw, z)
        if ymax is not None and y > ymax:
            z = 2 * ((hy - rw) / 16.0 - ymax)
            x, y = A.px(r, c, rw, z)
        return (x, y, z)
    B.beam(mat, at(*a, la), at(*b, lb), w, w * 1.1)


def stick_px(B, mat, r, a, za, b, zb, w=0.16):
    """A timber from where the picture draws pixel a at height za to pixel b
    at height zb."""
    xa, ya = A.px(r, a[0], a[1], za)
    xb, yb = A.px(r, b[0], b[1], zb)
    B.beam(mat, (xa, ya, za), (xb, yb, zb), w, w * 1.1)


def lean(B, mat, x, yw, zb, zt, w, out=0.9, th=0.16, rng=None, side="S"):
    """A slab propped against a south face at yw: its foot out in front at
    height zb, its top against the wall at zt."""
    rng = rng or random.Random(0)
    j = lambda: (rng.random() - 0.5) * 0.25  # noqa: E731
    pts = [(x - w / 2 + j(), yw - out + j(), zb), (x + w / 2 + j(), yw - out + j(), zb + j()),
           (x + w / 2 * 0.8 + j(), yw - 0.08, zt + j()), (x - w / 2 * 0.9 + j(), yw - 0.08, zt + j())]
    B.slab(mat, pts, th)


def picks(M, dm, roof_share=1):
    """Debris kinds and their materials: marble and stone in the picture's
    own rubble colours, the block's trim, tile in the roof's colour, charred
    rafters."""
    return {"slab": dm["slab"] + [M["trim"]] * 2 + [M["tile"]] * roof_share,
            "stone": dm["stone"] + [M["trim"], M["wall"]],
            "chunk": dm["chunk"] + [M["trim"]],
            "boulder": dm["slab"] + [M["trim"], M["wall"]],
            "beam": [M["rafter"]] * 2 + dm["beam"][:1],
            "board": [M["rafter"]] + dm["board"][:1]}


def poly_plan(r, poly, z):
    return [A.px(r, c, rw, z) for c, rw in poly]


# ---------------------------------------------------------------- soot

class Soot:
    """Vertex colours for every part: 1 where clean, darker inside each
    burn (centre, radius or (rx, ry, rz), strength), low down if asked,
    and in noise blotches."""

    def __init__(self, burns, floor=0.55, blotch=0.25, seed=0, low=0.0):
        self.burns = []
        for c, rad, st in burns:
            rad = Vector(rad) if isinstance(rad, (tuple, list, Vector)) else Vector((rad, rad, rad))
            self.burns.append((Vector(c), rad, st))
        self.floor, self.blotch, self.low = floor, blotch, low
        self.off = Vector((seed * 3.1, seed * 1.7, seed * 0.9))

    def kb(self, p):
        """The burns alone at p."""
        k = 1.0
        for c, rad, st in self.burns:
            q = p - c
            d = math.sqrt((q.x / rad.x) ** 2 + (q.y / rad.y) ** 2 + (q.z / rad.z) ** 2)
            if d < 1:
                k *= 1 - st * (1 - d) ** 1.1
        return max(self.floor * 0.4, k)

    def k(self, p):
        k = 1.0
        for c, rad, st in self.burns:
            q = p - c
            d = math.sqrt((q.x / rad.x) ** 2 + (q.y / rad.y) ** 2 + (q.z / rad.z) ** 2)
            if d < 1:
                k *= 1 - st * (1 - d) ** 1.1
        n = noise.noise(p * 0.7 + self.off) * 0.5 + 0.5
        k *= 1 - self.blotch * max(0.0, n - 0.35) / 0.65
        k *= 1 - self.low * max(0.0, 1 - p.z / 1.5)
        return max(self.floor * 0.4, k)


def soot_patch(B, mat, c, side, w, h, st=0.75, seg=12, seed=0):
    """A sooted patch on a wall face (side S, N, W or E) in that wall's own
    material, a hair proud of it: vertices for the soot to darken, dark in
    the middle and clean at its rim, so it blends into the face. Returns
    its burn for Soot."""
    rng = random.Random(seed)
    o = Vector((*OUT_N[side], 0))
    u = Vector((0, 0, 1)).cross(o)
    rad = (abs(u.x) * w / 2 + abs(o.x) * 0.9, abs(u.y) * w / 2 + abs(o.y) * 0.9, h / 2)
    # a patch overlapping another on the same face adds only its burn, so no two lie in one plane
    for c2, o2, w2, h2 in SPANS:
        d = Vector(c) - Vector(c2)
        if Vector(o2).dot(o) > 0.99 and abs(d.dot(o)) < 0.3 and abs(d.dot(u)) < (w + w2) * 0.6                 and abs(d.z) < (h + h2) * 0.6:
            return (tuple(c), rad, st)
    SPANS.append((tuple(c), tuple(o), w, h))
    # close enough to stay behind the facade's details, a clear layer off the wall
    off = 0.02
    c = Vector(c) + o * off
    V = [tuple(c)]
    PATCHES.append((tuple(c), tuple(o), tuple(c), 0.0, off))
    for ring in (0.5, 1.0):
        for i in range(seg):
            a = 2 * math.pi * i / seg
            k = ring * (1 + (rng.random() - 0.5) * (0.35 if ring == 1.0 else 0.2))
            V.append(tuple(c + u * (math.cos(a) * w / 2 * k) + Vector((0, 0, math.sin(a) * h / 2 * k))))
            PATCHES.append((V[-1], tuple(o), tuple(c), ring, off))
    F = [(0, 1 + i, 1 + (i + 1) % seg) for i in range(seg)]
    F += [(1 + i, 1 + seg + i, 1 + seg + (i + 1) % seg, 1 + (i + 1) % seg) for i in range(seg)]
    fs = B.mesh(mat, V, F)
    for f in fs:
        f.normal_update()
        if f.normal.dot(o) < 0:
            f.normal_flip()
    return (tuple(c), rad, st)


PATCHES = []  # every soot patch vertex: (where, out, patch centre, 0 middle to 1 rim, offset from the wall)
SPANS = []  # every soot patch: (centre, out, width, height)


def fix_patches(obs, soot):
    """Fits the soot patches to the walls as they stand: each patch vertex
    goes onto the wall face behind it, drawn in toward its patch's middle
    where the wall has broken away under it (a patch whose middle is off
    the wall goes). Its colour is the wall's own there, darkened by the
    burns toward the middle, so the patch has no edge."""
    if not PATCHES:
        return
    kd = KDTree(len(PATCHES))
    for i, q in enumerate(PATCHES):
        kd.insert(q[0], i)
    kd.balance()
    info, wall_polys, verts, owners = {}, [], [], []
    for ob in obs:
        me = ob.data
        ca = me.color_attributes.get("Col")
        base = len(verts)
        for v in me.vertices:
            p = ob.matrix_world @ v.co
            verts.append(p)
            owners.append((ob, v.index, ca))
            _, j, d = kd.find(p)
            if j is not None and d < 1e-4:
                info[base + v.index] = PATCHES[j]
        for f in me.polygons:
            ids = [base + i for i in f.vertices]
            if not all(i in info for i in ids):
                wall_polys.append(ids)
    if not info or not wall_polys:
        return
    bvh = BVHTree.FromPolygons(verts, wall_polys)

    def colour(ids, at):
        w = poly_3d_calc([verts[i] for i in ids], at)
        col = Vector((0.0, 0.0, 0.0))
        for wi, i in zip(w, ids):
            ob, vi, ca = owners[i]
            col += Vector(ca.data[vi].color[:3]) * wi
        return col

    gone = {}
    for gi, (_, o, c, rim, off) in info.items():
        o, c, p = Vector(o), Vector(c), verts[gi]
        hit = None
        for t in (0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0):
            q = p + (c - p) * t
            loc, nrm, fi, dist = bvh.ray_cast(q + o * 0.3, -o, 0.6)
            if loc is not None and abs(nrm.dot(o)) > 0.7:
                hit = (loc, fi)
                break
        ob, vi, ca = owners[gi]
        if hit is None:
            gone.setdefault(ob, set()).add(vi)
            continue
        loc, fi = hit
        at = loc + o * off
        ob.data.vertices[vi].co = ob.matrix_world.inverted() @ at
        k = soot.kb(at) ** (1 - rim) if soot is not None else 1.0
        cc = colour(wall_polys[fi], loc) * k
        ca.data[vi].color = (cc.x, cc.y, cc.z, 1.0)
    for ob, ids in gone.items():
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in ids], context="VERTS")
        bm.to_mesh(ob.data)
        bm.free()


def drop_bits(obs, size=0.2, thin=0.03):
    """Removes slivers and crumbs a cut leaves floating: loose parts under
    size across, or thinner than thin and under a cell."""
    for ob in obs:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        seen, gone = set(), []
        for v in bm.verts:
            if v.index in seen:
                continue
            stack, isl = [v], []
            seen.add(v.index)
            while stack:
                q = stack.pop()
                isl.append(q)
                for e in q.link_edges:
                    w = e.other_vert(q)
                    if w.index not in seen:
                        seen.add(w.index)
                        stack.append(w)
            lo = [min(p.co[i] for p in isl) for i in range(3)]
            hi = [max(p.co[i] for p in isl) for i in range(3)]
            ext = sorted(hi[i] - lo[i] for i in range(3))
            if ext[2] < size or (ext[0] < thin and ext[2] < 1.0):
                gone += isl
        if gone:
            bmesh.ops.delete(bm, geom=gone, context="VERTS")
            bm.to_mesh(ob.data)
        bm.free()


def _islands(bm):
    """The loose parts of a bmesh as lists of verts."""
    seen, out = set(), []
    for v in bm.verts:
        if v in seen:
            continue
        stack, isl = [v], []
        seen.add(v)
        while stack:
            q = stack.pop()
            isl.append(q)
            for e in q.link_edges:
                w = e.other_vert(q)
                if w not in seen:
                    seen.add(w)
                    stack.append(w)
        out.append(isl)
    return out


def floaters(obs, gap=0.06, above=0.1):
    """Every loose part standing clear of the ground (lowest point over
    above) and more than gap from every other part: [(object, its verts)],
    the bmeshes left open in the returned dict for moving them."""
    bms, parts, V, F, own = {}, [], [], [], []
    for ob in obs:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.transform(ob.matrix_world)
        bm.verts.ensure_lookup_table()
        bms[ob] = bm
        for isl in _islands(bm):
            k = len(parts)
            parts.append((ob, isl))
            fs = set(f for v in isl for f in v.link_faces)
            base = len(V)
            ids = {}
            for v in isl:
                ids[v] = len(V)
                V.append(v.co.copy())
            for f in fs:
                F.append([ids[v] for v in f.verts])
                own.append(k)
    bvh = BVHTree.FromPolygons(V, F)
    out = []
    for k, (ob, isl) in enumerate(parts):
        if min(v.co.z for v in isl) < above:
            continue
        pts = [v.co for v in isl] + [f.calc_center_median() for f in set(f for v in isl for f in v.link_faces)]
        if not any(own[i] != k for p in pts for _, _, i, _ in bvh.find_nearest_range(p, gap)):
            out.append(k)
    return out, bms, bvh, own, parts


def seat(obs, gap=0.06, embed=0.08, reach=1.2, quiet=False):
    """Seats the pieces that float clear of everything. A timber has the end
    nearer to something run on along its length until it is buried in it,
    or its lower end let down onto what lies under it. Anything else small
    drops onto what is under it, and crumbs a cut left are removed. Nothing
    moves further than reach. Prints what it moved and what it left."""
    obs = [o for o in obs if o.type == "MESH" and len(o.data.polygons)]
    fl, bms, bvh, own, parts = floaters(obs, gap)

    def cast(o, d, k, far):
        """The first thing other than part k along the ray, the ground included."""
        o0 = o = Vector(o)
        tg = -o0.z / d.z if d.z < -1e-6 else None
        while far > 0:
            loc, _, i, dist = bvh.ray_cast(o, d, far)
            if loc is None:
                break
            if own[i] != k:
                t = (loc - o0).length
                return (loc, t) if tg is None or t < tg else (o0 + d * tg, tg)
            o = loc + d * 1e-3
            far -= dist + 1e-3
        if tg is not None and tg <= (far if far > 0 else 0) + (o - o0).length:
            return o0 + d * tg, tg
        return None
    down = Vector((0, 0, -1))
    moved, gone = [], {}
    for k in fl:
        ob, isl = parts[k]
        P = np.array([tuple(v.co) for v in isl])
        box = (tuple(round(float(x), 2) for x in P.min(0)), tuple(round(float(x), 2) for x in P.max(0)))
        if np.ptp(P, 0).max() < 0.05:
            gone.setdefault(ob, []).extend(isl)
            continue
        c = P.mean(0)
        vt = np.linalg.svd(P - c)[2]
        t = (P - c) @ vt[0]
        span, wide = np.ptp(t), np.ptp((P - c) @ vt[1])
        lo = None
        if len(isl) == 8 and span > 2.5 * wide:
            mid = (t.min() + t.max()) / 2
            e0 = [v for v, s in zip(isl, t) if s < mid]
            e1 = [v for v in isl if v not in e0]
            c0 = sum((v.co for v in e0), Vector()) / len(e0)
            c1 = sum((v.co for v in e1), Vector()) / len(e1)
            if c1.z < c0.z:
                e0, e1, c0, c1 = e1, e0, c1, c0
            best = None
            for end, ce, ot in ((e0, c0, c1), (e1, c1, c0)):
                d = (ce - ot).normalized()
                h = cast(ce + d * 0.01, d, k, reach)
                if h and (best is None or h[1] < best[0]):
                    best = (h[1], end, d, "along")
            if best is None:
                h = cast(c0 + down * 0.01, down, k, reach)
                if h:
                    best = (h[1], e0, down, "down")
            if best is None:
                moved.append((ob.name, *box, "left"))
                continue
            dist, lo, d, how = best
            mv = d * (dist + 0.01 + embed)
            for v in lo:
                v.co += mv
            how = "%s %.2f" % (how, dist)
        elif np.ptp(P, 0).max() <= 3.0:
            best = None
            for v in isl:
                h = cast(v.co + down * 0.005, down, k, reach)
                if h:
                    best = h[1] if best is None else min(best, h[1])
            if best is None:
                moved.append((ob.name, *box, "left"))
                continue
            for v in isl:
                v.co.z -= best + 0.03
            how = "drop %.2f" % (best + 0.03)
        else:
            moved.append((ob.name, *box, "left"))
            continue
        moved.append((ob.name, *box, how))
        # a timber's moved faces have their texture laid again at the scale it had
        fs = set(f for v in isl for f in v.link_faces)
        if lo and ob.name.split(".")[0] not in A.UVFN:
            uvl = bms[ob].loops.layers.uv.active
            if uvl is not None:
                f0 = max(fs, key=lambda f: f.calc_area())
                l0, l1 = f0.loops[0], f0.loops[1]
                d3 = (l1.vert.co - l0.vert.co).length
                s = (Vector(l1[uvl].uv) - Vector(l0[uvl].uv)).length / max(d3, 1e-6)
                up = Vector((0, 0, 1))
                for f in fs:
                    f.normal_update()
                    n = f.normal
                    if abs(n.z) > 0.97:
                        tt, bb = Vector((1, 0, 0)), Vector((0, 1, 0))
                    else:
                        tt = up.cross(n).normalized()
                        bb = n.cross(tt).normalized()
                    for lp in f.loops:
                        lp[uvl].uv = (lp.vert.co.dot(tt) * s, lp.vert.co.dot(bb) * s)
    for ob, vs in gone.items():
        bmesh.ops.delete(bms[ob], geom=vs, context="VERTS")
    for ob, bm in bms.items():
        bm.transform(ob.matrix_world.inverted())
        bm.to_mesh(ob.data)
        bm.free()
    if not quiet:
        for m in moved:
            print("G3_SEAT", *m, flush=True)
    return moved


def slivers(obs, amin=0.002, short=0.08):
    """Clears the needle triangles a cut leaves (under amin square cells):
    a short edge is collapsed, a long thin one has its long edge turned,
    and what is left of either and too thin to see is removed."""
    for ob in obs:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        for _ in range(8):
            bad = [f for f in bm.faces if f.is_valid and f.calc_area() < amin]
            if not bad:
                break
            used, col, rot = set(), [], []
            for f in bad:
                e = min(f.edges, key=lambda e: e.calc_length())
                if e.calc_length() < short:
                    if not any(v in used for v in e.verts):
                        used.update(e.verts)
                        col.append(e)
                else:
                    el = max(f.edges, key=lambda e: e.calc_length())
                    if len(el.link_faces) == 2 and el not in rot:
                        rot.append(el)
            if col:
                bmesh.ops.collapse(bm, edges=col, uvs=True)
            rot = [e for e in rot if e.is_valid]
            if rot:
                bmesh.ops.rotate_edges(bm, edges=rot, use_ccw=False)
            if not col and not rot:
                break
        thin = [f for f in bm.faces if f.calc_area() < amin]
        if thin:
            bmesh.ops.delete(bm, geom=thin, context="FACES_ONLY")
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.to_mesh(ob.data)
        bm.free()


def vertex_soot(ob, soot, keep=1.0):
    """Paints Col on ob and makes its material multiply by it (a flat
    material's colour moves into Col)."""
    me = ob.data
    mat = me.materials[0] if len(me.materials) else None
    if mat is None:
        return
    nt = mat.node_tree
    b = nt.nodes["Principled BSDF"]
    if not mat.get("g3_vc"):
        vc = nt.nodes.new("ShaderNodeVertexColor")
        vc.layer_name = "Col"
        links = list(b.inputs["Base Color"].links)
        if links:
            mix = nt.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.inputs[0].default_value = 1.0
            nt.links.new(links[0].from_socket, mix.inputs[6])
            nt.links.new(vc.outputs["Color"], mix.inputs[7])
            nt.links.new(mix.outputs[2], b.inputs["Base Color"])
            mat["g3_flat"] = [1.0, 1.0, 1.0]
        else:
            mat["g3_flat"] = list(b.inputs["Base Color"].default_value[:3])
            nt.links.new(vc.outputs["Color"], b.inputs["Base Color"])
        mat["g3_vc"] = True
    flat = Vector(list(mat["g3_flat"]))
    ca = me.color_attributes.get("Col") or me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    M = ob.matrix_world
    for i, v in enumerate(me.vertices):
        k = soot.k(M @ v.co) if soot is not None else 1.0
        k = 1 - (1 - k) * keep
        ca.data[i].color = (flat.x * k, flat.y * k, flat.z * k, 1.0)
    me.color_attributes.active_color = ca


# ---------------------------------------------------------------- output

def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def export(name, obs, soot, keep=None, intact_of=None):
    """Soot every part, join, export as it ships and render the views."""
    keep = keep or {}
    obs = [o for o in obs if o.type == "MESH" and len(o.data.polygons)]
    seat(obs)
    for o in obs:
        k = 1.0
        for key, v in keep.items():
            if key in o.name:
                k = v
        vertex_soot(o, soot, k)
    fix_patches([o for o in obs if o.name.split(".")[0].endswith("_wall")], soot)
    slivers(obs)
    # the sliver pass can take away what a piece rested on
    seat(obs)
    for o in sorted(obs, key=tris, reverse=True)[:10]:
        print("G3_PART", name, o.name, tris(o), flush=True)
    sl = {}
    for o in obs:
        o.data.calc_loop_triangles()
        k = sum(1 for t in o.data.loop_triangles if t.area < 0.002)
        if k:
            sl[o.name] = k
    print("G3_SLIVER", name, sum(sl.values()), sl, flush=True)
    fl, bms, _, _, parts = floaters(obs, gap=0.08, above=0.12)
    for k in fl:
        P = np.array([tuple(v.co) for v in parts[k][1]])
        print("G3_FLOAT", name, parts[k][0].name, np.round(P.min(0), 2).tolist(), np.round(P.max(0), 2).tolist(),
              flush=True)
    for bm in bms.values():
        bm.free()
    glb = os.path.join(OUT, name + ".glb")
    ob = hk.finish(obs, glb, {"feature": name, "family": "creon"})
    n = tris(ob)
    r = A.CAT[name]
    rd = os.path.join(OUT, "renders")
    spr = os.path.join(DATA, "sprites", name + ".png")
    hk.renders(ob, rd, name, spr, tuple(r["sprite"]["hotspot"]), scale=2)
    lo = [min(v.co[i] for v in ob.data.vertices) for i in range(3)]
    hi = [max(v.co[i] for v in ob.data.vertices) for i in range(3)]
    print("G3_BUILT", name, "tris", n, "size %.2f x %.2f x %.2f" % tuple(ob.dimensions),
          "z %.2f..%.2f" % (lo[2], hi[2]), flush=True)
    return ob


# ---------------------------------------------------------------- the intact, for checking the fit

def build_intact(name):
    """The intact block alone, rendered against the intact picture (a check
    of the base the stages break, not a shipping model)."""
    hk.reset()
    P = PLANS[name]
    M = materials(name + "i", name)
    g = roof_geom(P)
    M["roof"] = A.roof_mat(name + "i_roof", g, LOOKS[name]["roof"], course=LOOKS[name]["tile"],
                           tile_w=LOOKS[name]["tile"], var=0.12, lip=0.2)
    B = A.Builder(seed=1)
    intact(B, M, P, g)
    obs = B.objects()
    return export_check(name, obs)


def export_check(name, obs):
    for o in obs:
        vertex_soot(o, None)
    ob = hk.finish(obs, os.path.join(OUT, "check", name + ".glb"), {"feature": name})
    r = A.CAT[name]
    rd = os.path.join(OUT, "check")
    hk.renders(ob, rd, name, os.path.join(DATA, "sprites", name + ".png"), tuple(r["sprite"]["hotspot"]), scale=2)
    print("G3_CHECK", name, "tris", tris(ob), "size %.2f x %.2f x %.2f" % tuple(ob.dimensions), flush=True)


STAGES = {}


def stage(name):
    def wrap(fn):
        STAGES[name] = fn
        return fn
    return wrap


# ---------------------------------------------------------------- shared by the stages

def tex_wreck(name, parts, gap="#2a2622", cells=18, size=192, grout=3.2, seed=0):
    """Broken masonry seen close: a few large angular chunks a repeat in the
    given (colour, weight) parts, each lit across its face from one side and
    darker toward its edges, with narrow grout between them."""
    rng = np.random.default_rng(seed)
    pts = rng.random((cells, 2)) * size
    pts = np.concatenate([pts + [dx, dy] for dx in (-size, 0, size) for dy in (-size, 0, size)])
    yy, xx = np.mgrid[0:size, 0:size].astype(float)
    d = np.sqrt((xx[..., None] - pts[:, 0]) ** 2 + (yy[..., None] - pts[:, 1]) ** 2)
    o = np.argsort(d, axis=-1)
    i0 = o[..., 0]
    d0 = np.take_along_axis(d, o[..., :1], -1)[..., 0]
    d1 = np.take_along_axis(d, o[..., 1:2], -1)[..., 0]
    cid = i0 % cells
    cols = np.array([A.rgb(c) for c, _ in parts], float)
    w = np.array([wt for _, wt in parts], float)
    pick = rng.choice(len(parts), size=cells, p=w / w.sum())
    shade = rng.uniform(0.85, 1.1, cells)
    ang = rng.uniform(0, 2 * np.pi, cells)
    cx, cy = pts[i0, 0], pts[i0, 1]
    lit = 1 + 0.16 * (((xx - cx) * np.cos(ang[cid]) + (yy - cy) * np.sin(ang[cid])) / (size / np.sqrt(cells)))
    col = cols[pick[cid]] * (shade[cid] * lit)[..., None]
    col *= (1 + 0.1 * (A._smooth(rng, size, 20) - 0.5) + 0.06 * (rng.random((size, size)) - 0.5))[..., None]
    e = d1 - d0
    rim = np.clip((e - grout) / (grout * 1.6), 0, 1)
    col *= (0.82 + 0.18 * rim)[..., None]
    col[e < grout] = np.array(A.rgb(gap), float) * (0.9 + 0.2 * rng.random((int((e < grout).sum()), 1)))
    return A._to_image(name, np.clip(col, 0, 255))


WRECK = {
    "CreBuild07": [("#a29b8e", 3), ("#6e6c66", 3), ("#38506a", 2), ("#34312e", 3), ("#4e4b46", 3)],
    "CreBuild08": [("#99958b", 1.5), ("#5c4b36", 3), ("#454a3b", 2), ("#352d25", 3), ("#262320", 3)],
    "CreBuild09": [("#b2ab9b", 3), ("#7a7356", 2), ("#565b4a", 2), ("#3e3c34", 2), ("#2e2c27", 2)],
}


def rubble_mat(n, base="CreBuild07", seed=21, uv=1.9, dark=1.0, pale=1.0, tag="_rubble", parts=None):
    """The heaps' wreck texture in the block's colours (or the given parts).
    dark weights the dark chunks, pale the marble."""
    parts = [(c, w * (dark if A.rgb(c)[0] < 64 else pale if A.rgb(c)[0] > 150 else 1.0))
             for c, w in (parts or WRECK[base])]
    return Mat(n + tag, tex=tex_wreck(n + tag, parts, seed=seed), uv=uv)


def clip(ground, *plans):
    """A ground for cover() that ends at the given plan outlines (cells), so
    no piece is laid outside the field."""
    return lambda x, y: ground(x, y) if any(A.inside_poly(p, x, y) for p in plans) else -100.0


def kinds_of(M, dm, roof_share=1, pale=2):
    """picks() with the big marble blocks among the slabs and chunks."""
    k = picks(M, dm, roof_share)
    k["slab"] = k["slab"] + [M["block"]] * pale
    k["chunk"] = k["chunk"] + [M["block"]] * pale
    return k


def hole_fill(B, r, pic, M, dm, g, holes, sag=0.35, n=8, seed=0, size=(0.35, 0.7), dark=False):
    """What shows down each hole: an ash grey floor sagging a little under
    the roof, pale chunks, tile bits and a charred timber or two on it.
    dark: a burnt floor sunk well under the roof, its pieces charred and
    sooted, as most of these pictures draw their breaches."""
    if dark:
        kinds = {"slab": [M["tile"]] * 2 + [M["rafter"]], "chunk": [M["char"], M["rafter"], M["tile"]],
                 "stone": [M["char"], M["dash"]]}
    else:
        kinds = {"slab": [M["tile"]] * 2 + [M["block"]], "chunk": [M["block"], M["trim"]] + dm["chunk"][-2:],
                 "stone": dm["stone"][1:]}
    for i, q in enumerate(grow(holes, 1.5)):
        fill, Pp = A.sag_fill(B, M["dash" if dark else "hash"], r, g, q, sag=sag, cell=0.55, noise=0.1, seed=seed + i,
                              edge=0.9)
        if len(Pp) >= 3 and n:
            A.cover(B, kinds, r, Pp, fill, n, {"slab": 3, "chunk": 4, "stone": 2}, pic=pic,
                    size=size, length=(0.8, 1.6), seed=seed + 10 + i, lift=0.04, tilt=25, grow=0,
                    stone=(0.2, 0.4), chunk=(0.2, 0.4), thick=(0.1, 0.16), jumble=0.1, tries=120)


def ragged(polys, amp=3.0, step=3.5, rad=0.22, seed=0):
    """Hole outlines made irregular: each corner pushed in or out from the
    middle by up to rad of its distance, then the sides jagged."""
    out = []
    for i, p in enumerate(polys):
        rng = random.Random(seed * 7 + i)
        cx, cy = sum(q[0] for q in p) / len(p), sum(q[1] for q in p) / len(p)
        q2 = []
        for x, y in p:
            k = 1 + rng.uniform(-rad, rad * 0.6)
            q2.append((cx + (x - cx) * k, cy + (y - cy) * k))
        out.append(A.jag(q2, amp, step, seed + i))
    return out


def heap_plan(B, mat, r, plan, top, z_at=0.5, **kw):
    """akit.heap over an outline given in plan cells rather than pixels."""
    poly = [A.screen(r, x, y, z_at) for x, y in plan]
    return A.heap(B, mat, r, poly, top, z_at=z_at, **kw)


def group(obs, *keys):
    return [o for o in obs if any(k in o.name for k in keys)]


def jagged(polys, amp=2.0, step=3.0, seed=0):
    return [A.jag(p, amp, step, seed + i) for i, p in enumerate(polys)]


def grow(polys, px):
    """Outlines pushed out px pixels from their middles."""
    out = []
    for p in polys:
        cx, cy = sum(q[0] for q in p) / len(p), sum(q[1] for q in p) / len(p)
        q2 = []
        for x, y in p:
            d = max(1e-6, math.hypot(x - cx, y - cy))
            q2.append((x + (x - cx) / d * px, y + (y - cy) / d * px))
        out.append(q2)
    return out


def hole_burns(r, g, holes, rad=2.2, s=0.6, k=1.6):
    """A burn round each hole, k times its radius but at least rad."""
    out = []
    for q in holes:
        h = A.hole_centre(r, g, q)
        if h is not None:
            out.append(((h[0], h[1], g.top(h[0], h[1]) or g.zr), max(rad, h[2] * k), s))
    return out


def setup(name):
    hk.reset()
    PATCHES.clear()
    SPANS.clear()
    PLATES.clear()
    r = A.CAT[name]
    pic = A.Picture(r)
    return r, pic


def nearest_on(plan, x, y):
    """The point of polygon plan's outline nearest (x, y)."""
    p = Vector((x, y))
    best, bp = 1e9, p
    for i in range(len(plan)):
        a, b = Vector(plan[i]), Vector(plan[i - 1])
        d = b - a
        t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
        q = a + d * t
        if (q - p).length < best:
            best, bp = (q - p).length, q
    return bp.x, bp.y


def field_heap(B, mat, plan, hfn, cell=0.55, edge=1.4, noise=0.3, seed=0, base=-0.06, z0=0.0, edges=None, clamp=False,
               mask=None, ymax=None):
    """A rubble field over plan polygon (cells): its top hfn(x, y) inside,
    tapering to the ground over edge cells at the outline, roughened by
    noise, all lifted z0. With edges (side indices), only those sides
    taper. With clamp, the grid stops at the outline instead of skirting
    down past it (for a heap held inside walls). mask(x, y, z) false sinks
    a point most of the way down, so gaps open where the picture shows
    ground. ymax holds the grid in front of that line (inside a wall).
    Returns its surface (0 off it) for laying pieces on."""
    rng = random.Random(seed)
    xs, ys = [p[0] for p in plan], [p[1] for p in plan]
    x0, x1, y0, y1 = min(xs) - cell, max(xs) + cell, min(ys) - cell, max(ys) + cell
    nx, ny = int((x1 - x0) / cell) + 1, int((y1 - y0) / cell) + 1

    def dist(x, y):
        best = 1e9
        p = Vector((x, y))
        for i in range(len(plan)):
            if edges is not None and (i - 1) % len(plan) not in edges:
                continue
            a, b = Vector(plan[i]), Vector(plan[i - 1])
            d = b - a
            t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
            best = min(best, (a + d * t - p).length)
        return best

    def surface(x, y):
        if not A.inside_poly(plan, x, y):
            return 0.0
        return z0 + hfn(x, y) * min(1.0, dist(x, y) / edge) ** 0.7

    V, F, grid, inn = [], [], {}, {}
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = x0 + i * cell + (rng.random() - 0.5) * cell * 0.5
            y = y0 + j * cell + (rng.random() - 0.5) * cell * 0.5
            if ymax is not None:
                y = min(y, ymax)
            ok = A.inside_poly(plan, x, y)
            if not ok and clamp:
                x, y = nearest_on(plan, x, y)
                z = z0 + hfn(x, y) * min(1.0, dist(x, y) / edge) ** 0.7
                V_ok = True
            else:
                V_ok = False
            if not V_ok:
                z = surface(x, y)
            if ok:
                z = max(z0 + 0.03, z + (rng.random() - 0.5) * 2 * noise * min(1.0, (z - z0) / 0.6 + 0.2))
                if mask is not None and not mask(x, y, z):
                    z = z0 + max(base, (z - z0) * 0.2)
            elif not V_ok:
                z = base + z0
            grid[i, j], inn[i, j] = len(V), ok
            V.append((x, y, z))
    for j in range(ny):
        for i in range(nx):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            if not any(inn[k] for k in q):
                continue
            ids = [grid[k] for k in q]
            if ymax is not None and sum(V[k][1] >= ymax - 1e-9 for k in ids) > 2:
                continue
            F.append((ids[0], ids[1], ids[2]))
            F.append((ids[0], ids[2], ids[3]))
    B.mesh(mat, V, F)
    return surface

def ledge(B, mat, xa, xb, yw, z, depth=0.75, h=0.8, step=0.3, seed=0):
    """Rubble fallen onto a cornice ledge along a south face at yw: banked
    against the wall to about h above z, running out depth over the ledge,
    its ends crumbling down. Returns its surface for laying pieces on."""
    rng = random.Random(seed)
    k = max(2, int((xb - xa) / step))
    V, F = [], []
    for i in range(k + 1):
        x = xa + (xb - xa) * i / k
        e = max(0.0, min(1.0, min(x - xa, xb - x) / 0.4)) ** 0.6
        hh = h * e * rng.uniform(0.75, 1.15)
        V += [(x, yw - depth, z - 0.02), (x, yw - depth * rng.uniform(0.35, 0.6), z + hh * rng.uniform(0.45, 0.7)),
              (x, yw + 0.04, z + hh)]
    for i in range(k):
        for j in range(2):
            a_, b_ = 3 * i + j, 3 * (i + 1) + j
            F.append((a_, b_, b_ + 1, a_ + 1))
    B.mesh(mat, V, F)

    def surface(x, y):
        if not (xa <= x <= xb and yw - depth <= y <= yw + 0.04):
            return 0.0
        e = max(0.0, min(1.0, min(x - xa, xb - x) / 0.4)) ** 0.6
        return z + h * e * min(1.0, (y - yw + depth) / depth) ** 0.8
    return surface


def cornice_runs(B, mat, ground, ys, xa, xb, seed=0, w=0.42, h=0.34, run=(1.0, 2.6), gap=(0.25, 0.9)):
    """Broken lengths of cornice lying along x on a heap, one line of them
    at each plan y in ys, with gaps between."""
    rng = random.Random(seed)
    for y in ys:
        x = xa + rng.uniform(0, 0.6)
        while x < xb - 0.5:
            x1 = min(xb, x + rng.uniform(*run))
            y0 = y + rng.uniform(-0.12, 0.12)
            y1 = y0 + rng.uniform(-0.3, 0.3)
            z0, z1 = ground(x, y0), ground(x1, y1)
            if z0 > 0.05 and z1 > 0.05:
                B.beam(mat, (x, y0, z0 + h / 2 - 0.08), (x1, y1, z1 + h / 2 - 0.08), w, h, twist=rng.uniform(-8, 8))
            x = x1 + rng.uniform(*gap)



def z_on(r, c, rw, ground, lift=0.1, floor=0.0):
    """The height at which the classic camera's ray through sprite pixel
    (c, rw) first meets ground(x, y) lifted by lift, marching down it from
    above."""
    z = 12.0
    while z > floor + lift:
        x, y = A.px(r, c, rw, z)
        if z <= max(floor, ground(x, y)) + lift:
            return z
        z -= 0.04
    return floor + lift


def plate_z(B, mat, r, poly, anchors, T=0.28, edge=(), edge_mat=None, ew=0.32, rough=0.0, broken=None, seed=0):
    """A flat roof section drawn as outline poly (sprite pixels) in the
    plane through three of its corners held at given heights, anchors =
    [(index, z)] * 3. Sections sharing two anchored corners meet along that
    line, which is how a hip corner stands folded. edge sides get the white
    eave band. Returns its corners in 3D."""
    Q = [Vector((*A.px(r, poly[i][0], poly[i][1], z), z)) for i, z in anchors]
    n = (Q[1] - Q[0]).cross(Q[2] - Q[0]).normalized()
    if n.z < 0:
        n = -n
    if rough > 0:
        poly2, _, where = rough_outline(poly, (), amp=rough, seed=seed, broken=broken)
    else:
        poly2, where = list(poly), list(range(len(poly)))
    pts = [on_plane(r, c, rw, Q[0], n) for c, rw in poly2]
    _plate(B, mat, pts, T, [where[e] for e in edge], edge_mat, ew, n)
    return [on_plane(r, c, rw, Q[0], n) for c, rw in poly]


def plate_n(pts):
    """The upward normal of a flat section's corners."""
    n = (pts[1] - pts[0]).cross(pts[2] - pts[0]).normalized()
    return -n if n.z < 0 else n


# ---------------------------------------------------------------- CreBuild07a

ROOF07A = "#60a2de"

@stage("CreBuild07a")
def s07a(name):
    """The Embassy damaged: four ragged breaches through the blue roof with
    rubble, tile and rafters in them, black with soot round them, the west
    dormer gone, the front-west corner fallen in a heap that slopes up into
    it with the roof's corner slid down it, the front storeys' ornament
    smashed onto their own ledges and onto a bank of pale blocks and
    cornice lengths along the front."""
    r, pic = setup(name)
    P, base = P07, "CreBuild07"
    M = materials(name, base, dim=0.82)
    g = roof_geom(P)
    rng = random.Random(7)
    holes = {
        "left": [(48, 46), (58, 41), (70, 46), (73, 57), (64, 66), (52, 65), (45, 56)],
        "mid": [(120, 72), (131, 68), (141, 74), (142, 85), (133, 91), (121, 88), (117, 80)],
        "right": [(154, 30), (168, 24), (186, 26), (198, 34), (214, 40), (214, 70), (194, 78), (178, 84), (162, 76),
                  (152, 60), (150, 42)],
        "back": [(82, 6), (96, 2), (112, 7), (116, 19), (106, 27), (92, 27), (82, 18)],
    }
    corner = [(-20, 92), (16, 92), (40, 80), (54, 73), (70, 94), (78, 122), (72, 150), (-20, 150)]
    # the eave broken through over the front's middle, where its top storey fell out
    front = [(116, 108), (126, 104), (136, 106), (142, 112), (142, 132), (116, 132)]
    # the glazed blue as drawn, black only close round each breach, a little soot down the slopes
    M["roof"] = tile_roof(name + "_roof", g, r, ROOF07A, holes=list(holes.values())
                          + [[(30, 84), (56, 74), (72, 96), (60, 112), (36, 104)]], halo=1.0, hpad=1.2,
                          gap="#05080c", jv=0.12, ju=0.32, jk=0.95, jpow=0.6, var=0.18, soot=0.3, swirl=1.6,
                          width=0.35, rim=0.97, rexp=0.35, soot_col="#0a1018", planes=(0.92, 1.4, 0.72, 1.0),
                          smudge=[([(190, 60), (214, 60), (214, 126), (196, 126)], 0.95),
                                  ([(70, 0), (138, 0), (134, 14), (110, 26), (76, 24)], 0.85),
                                  ([(30, 22), (56, 16), (74, 34), (60, 44), (34, 42)], 0.8)])
    M["hip"] = Mat(name + "_hip", "#1a2430")
    # the dormers' east planes in their own shade, as drawn
    M["shade"] = Mat(name + "_shade", tex=tex_slate(name + "_shade", "#1d2d3d", "#06090c", n=8, seed=23), uv=8 * 0.36,
                     ref="#1d2d3d")
    D = Damage(P, prof={"S": [(0, 1.4), (0.16, 2.2), (0.22, 3.4), (0.27, 5.4), (0.31, 7.6), (0.35, 9.6), (0.5, 9.6),
                              (0.53, 7.6), (0.58, 6.7), (0.66, 7.0), (0.7, 7.8), (0.73, 9.6), (1, 9.6)],
                        "W": [(0, 2.0), (0.15, 3.6), (0.22, 2.6), (0.34, 5.8), (0.4, 4.9), (0.47, 8.2), (0.52, 9.6),
                              (1, 9.6)]},
               drop=0.2, buried=lambda x: 0.0 if x > -1.0 else 3.0, seed=7, thick=0.2)
    # the back eave's cornice broken off over the back breach, where rubble lies on the wall's top
    D.bare = {"N": [(0.06, 0.81)]}
    B = A.Builder(seed=7)
    intact(B, M, P, g, D, dormers=(0.06, 2.9))
    A.hip_caps(B, M["hip"], g, w=0.16, lift=0.02, ridge=False)
    # the top storey's ceiling, left under the roof where the corner broke
    B.box(M["trim"], -3.3, -0.8, 8.85, 5.6, 7.0, 0.3)
    # the dark of the gutted storeys behind the front
    B.box(M["dark"], 2.2, P["yf"] + P["t"] + 0.25, 0.0, 7.4, 0.4, 9.0)
    # the east and back faces' windows, as the front's storeys have them
    side_windows(B, M, P, D, "EN", ((0.5, 1.0), (4.1, 1.2), (7.5, 0.85)), step=2.0)
    obs = B.objects()
    J = ragged(list(holes.values()), amp=2.5, step=3.5, seed=3)
    big = jagged([corner], amp=3.0, step=5.0, seed=11) + jagged([front], amp=2.5, step=4.0, seed=12)
    A.cut_view(group(obs, "_roof"), J + big, r)
    # the hips, the trim and the dormers cut a little wider, so their cut faces never lie in the roof's
    A.cut_view(group(obs, "_hip"), grow(J, 0.6) + grow(big, 0.6), r)
    A.cut_view(group(obs, "_trim", "_tile"), grow(J, 1.0) + grow(big, 1.0), r)
    # the corner's cut runs down through the ceiling too
    A.cut_view(group(obs, "_trim"), [[(-20, 92), (16, 92), (40, 80), (54, 73), (70, 94), (78, 122), (72, 170),
                                      (-20, 170)]], r)
    drop_bits(obs)
    slivers(group(obs, "_roof", "_tile", "_trim", "_hip"))

    dm = A.debris_mats(name, pic, seed=5, box=(0, 140, 215, 227), k=6, gain=1.0, sat=1.1)
    dm = {k: (v[2:] if k in ("slab", "stone", "chunk", "boulder", "tile") else v) for k, v in dm.items()}
    kinds = kinds_of(M, dm, roof_share=3, pale=2)
    rub = rubble_mat(name, base, pale=1.0, dark=1.0,
                     parts=[("#cbc3b2", 3.5), ("#5e5c57", 1.0), ("#38506a", 1.2), ("#171513", 5), ("#34312d", 2)])
    burns = []
    # rubble, tile and rafters down in the holes
    hole_fill(B, r, pic, M, dm, g, J, sag=1.0, n=5, seed=20, dark=True)
    slab_px(B, M["tile"], r, [(170, 44), (184, 42), (188, 56), (174, 60)], g.hit(r, 178, 50)[2] - 0.9,
            n=(0.3, -0.35, 0.9), T=0.22, rough=0.5, seed=4)
    for a, b, lift in (((80, 22), (70, 4), 0.6), ((100, 22), (110, 6), 0.5), ((168, 44), (206, 34), 0.4),
                       ((176, 64), (194, 70), 0.3), ((126, 80), (136, 73), 0.3)):
        rafter_px(B, M["rafter"], r, g, a, b, lift=lift, w=0.15)
    # the top storey's charred floor under the broken middle, and its wreck down through the front onto the ledge
    B.box(M["dash"], 1.4, -2.225, 6.3, 4.4, 4.45, 0.25)
    bplan = [(0.0, -5.0), (3.0, -5.0), (3.2, -3.4), (2.6, -2.2), (0.6, -2.2), (-0.2, -3.4)]
    field_heap(B, rub, bplan, lambda x, y: 0.5 + 1.0 * min(1.0, max(0.0, (y + 4.8) / 1.6)), cell=0.5, edge=0.5,
               noise=0.3, seed=30, z0=6.6, clamp=True)
    for a, b, dr in (((118, 112), (124, 126), 1.2), ((140, 110), (136, 128), 1.4)):
        rafter_px(B, M["rafter"], r, g, a, b, w=0.15, drop=dr)
    # the fallen corner: a heap sloping up into it from the ground out front
    # and over the broken west wall, about forty degrees, under the slid roof
    cplan = [(-6.5, -5.45), (-1.6, -5.5), (-0.9, -3.6), (-0.9, 1.6), (-5.72, 1.6), (-5.72, -1.6), (-6.5, -2.4)]

    def cfn(x, y):
        return max(0.25, min(6.0 + 0.35 * math.sin(x * 1.3 + y), 0.9 * (y + 5.45), 1.0 * (x + 6.5) + 2.6))
    # the storeys' own white marble, paler than the wreck along the front
    rubc = rubble_mat(name, base, seed=24, tag="_rubblec",
                      parts=[("#c4bcab", 4), ("#8a857c", 2), ("#38506a", 1.2), ("#2a2724", 2.5), ("#5a5650", 2)])
    hc = field_heap(B, rubc, cplan, cfn, cell=0.5, edge=0.4, noise=0.3, seed=31, edges=(0, 1, 5, 6))
    # the roof's corner slid down it, its eave still on it
    cpoly = [(10, 106), (30, 99), (50, 103), (60, 118), (58, 136), (44, 146), (12, 146)]
    zc, cn = settle(r, cpoly, (-0.1, -0.66, 0.74), hc, (4, 0), 22, lift=0.1)
    fold_px(B, M["tile"], r, cpoly, (4, 0), zc, cn, 22, T=0.3, edge=[5, 6], edge_mat=M["trim"], ew=0.4,
            hip_mat=M["hip"], rough=1.5, broken=[0, 1, 2, 3], seed=5)
    spoly = [(40, 147), (58, 144), (63, 158), (50, 168), (38, 160)]
    zc, sn = settle(r, spoly, (0.3, -0.5, 0.85), hc, lift=0.08)
    slab_px(B, M["tile"], r, spoly, zc, n=sn, T=0.25, rough=0.5)
    # rafters hanging from the roof's broken edge down into it, their feet under the roof that stands
    for a, b, dr in (((38, 70), (50, 104), 1.9), ((52, 62), (64, 100), 1.8), ((20, 80), (26, 106), 1.4),
                     ((62, 80), (70, 120), 2.1)):
        rafter_px(B, M["rafter"], r, g, a, b, w=0.15, drop=dr)
    # the front: rubble on each cornice ledge, a low bank of blocks along it
    lg = []
    for z, hh, seed in ((7.32, (0.9, 1.3), 34), (3.62, (1.6, 2.2), 35)):
        x = -1.2
        while x < P["x1"] - 0.3:
            x1 = min(P["x1"], x + rng.uniform(3.0, 5.0))
            lg.append(ledge(B, rub, x, x1, P["yf"], z, depth=0.32, h=rng.uniform(*hh), seed=seed + int(x * 7)))
            x = x1 + rng.uniform(-0.3, 0.3)
    cornice_runs(B, M["block"], A.ground_of(*lg), (P["yf"] - 0.12,), -1.0, P["x1"], seed=38, w=0.3, h=0.26,
                 run=(1.2, 2.6), gap=(0.4, 1.2))
    fplan = [(-6.0, -6.05), (-1.0, -6.1), (3.0, -6.0), (6.2, -5.9), (6.6, -4.6), (6.6, -4.3), (-1.0, -4.3)]
    ffn = lambda x, y: 3.5 * min(1.0, max(0.0, (y + 6.1) / 1.6)) ** 0.8  # noqa: E731
    hf = field_heap(B, rub, fplan, ffn, cell=0.55, edge=0.6, noise=0.35, seed=32)
    east_plan = [(5.8, -3.0), (6.7, -2.6), (6.8, -0.4), (6.4, 0.8), (5.8, 0.2)]
    he = heap_plan(B, rub, r, east_plan, 1.4, z_at=0.8, cell=0.45, noise=0.2, seed=33, edge=0.6)
    ground = clip(A.ground_of(hc, hf, he), cplan, fplan, [(5.6, -3.2), (7.0, -3.2), (7.0, 1.0), (5.6, 1.0)])
    cornice_runs(B, M["block"], ground, (-5.0, -5.45, -5.85), -1.0, 6.2, seed=37, run=(1.2, 2.6))
    # cornice lengths and blocks lying across the corner's slope
    cornice_runs(B, M["block"], clip(hc, cplan), (-4.7, -3.7, -2.7, -1.6), -6.3, -1.2, seed=39, w=0.4, h=0.32,
                 run=(0.9, 2.0), gap=(0.3, 0.8))
    pl = [A.px(r, c, rw, 1.0) for c, rw in [(78, 140), (212, 140), (214, 228), (78, 228)]]
    A.cover(B, kinds, r, pl, ground, 50, {"slab": 3, "chunk": 5, "stone": 1.5, "boulder": 1, "beam": 1}, pic=pic,
            size=(0.5, 1.0), length=(0.9, 1.8), seed=41, tilt=30, grow=1, stone=(0.3, 0.55), chunk=(0.32, 0.6),
            boulder=(0.6, 1.0), thick=(0.14, 0.26), jumble=0.1, tries=80)
    pc = [A.px(r, c, rw, 2.0) for c, rw in [(0, 84), (80, 84), (80, 228), (0, 228)]]
    A.cover(B, kinds, r, pc, clip(hc, cplan), 40, {"slab": 2, "chunk": 6, "stone": 1, "boulder": 1, "beam": 0.6},
            pic=pic, size=(0.5, 1.0), length=(0.9, 1.8), seed=43, tilt=25, grow=1, stone=(0.3, 0.5),
            chunk=(0.35, 0.8), boulder=(0.6, 1.0), thick=(0.14, 0.24), jumble=0.1, tries=80)
    pe = [A.px(r, c, rw, 1.0) for c, rw in [(196, 60), (214, 60), (214, 110), (196, 110)]]
    A.cover(B, kinds, r, pe, ground, 10, {"chunk": 3, "stone": 3, "beam": 1}, pic=pic, seed=44, tilt=25, grow=1,
            stone=(0.25, 0.45), chunk=(0.25, 0.45), tries=80)
    # soot up the front from the bank and the breaches, round the broken
    # corner, and down the east and back walls from the NE breach
    yf, yb = P["yf"], P["yb"]
    for c, side, w, h in (((-1.0, yf, 6.2), "S", 2.2, 5.0), ((1.0, yf, 6.3), "S", 2.6, 1.6),
                          ((3.6, yf, 6.2), "S", 2.8, 1.6), ((5.4, yf, 6.4), "S", 1.4, 1.8),
                          ((2.0, yf, 2.8), "S", 3.2, 1.2), ((4.6, yf, 2.7), "S", 2.6, 1.2),
                          ((1.6, yf, 8.6), "S", 3.4, 1.6), ((4.4, yf, 8.6), "S", 2.6, 1.6),
                          ((P["x0"], -1.0, 6.8), "W", 2.6, 5.4), ((P["x0"], 2.2, 8.2), "W", 2.6, 2.4),
                          ((P["x1"], -2.6, 5.4), "E", 2.4, 4.4), ((P["x1"], 1.4, 8.2), "E", 3.4, 3.0),
                          ((P["x1"], 0.6, 4.6), "E", 2.4, 3.6),
                          ((3.4, yb, 8.0), "N", 3.6, 3.0), ((-0.6, yb, 8.4), "N", 2.6, 2.0),
                          ((4.4, yb, 4.6), "N", 2.4, 3.2)):
        burns.append(soot_patch(B, M["wall"], c, side, w, h, st=0.7, seed=len(burns)))
    obs += B.objects()
    nb = g.hit(r, 180, 52)
    burns += hole_burns(r, g, list(holes.values()), rad=1.2, s=0.6, k=0.9) + [
        ((-4.5, -3.0, 6.0), 3.5, 0.5), ((1.0, -5.4, 1.2), (6.5, 1.2, 1.6), 0.45),
        ((nb[0] + 0.6, nb[1], nb[2] - 1.2), (2.0, 2.4, 2.4), 0.6), ((2.0, -3.6, 8.4), (3.5, 1.6, 1.4), 0.45),
        ((1.6, -4.7, 5.2), (7.5, 1.2, 4.8), 0.4), ((1.6, -4.6, 8.8), (7.5, 1.4, 1.6), 0.55)]
    export(name, obs, Soot(burns, blotch=0.3, seed=7, low=0.15),
           keep={"_roof": 0.25, "_block": 0.2, "_rubblec": 0.3, "_rubble": 0.6, "_tile": 0.6, "_hash": 0.5})


# ---------------------------------------------------------------- CreBuild07b

@stage("CreBuild07b")
def s07b(name):
    """The Embassy destroyed: the walls down to ragged courses, the back one
    highest, the plan heaped with the storeys' wreck. The roof's two back
    hip corners still stand on the back wall's top, each folded along its
    hip with its white eaves, falling forward steeply onto the heap, bare
    rafters sticking up over them; more of the blue roof lies in tilted
    sections over the middle, broken marble and cornice over the front."""
    r, pic = setup(name)
    P, base = P07, "CreBuild07"
    M = materials(name, base, dim=0.74)
    rng = random.Random(17)
    M["roof"] = M["tile"]
    # the storeys' marble as pale as on the ruin's heap: the shell is darker for its soot and holes, not greyer stone
    M["block"] = Mat(name + "_block", tex=A.tex_mottle(name + "_block", "#c8bfb0", var=0.1, seed=20), uv=1.2,
                     ref="#c8bfb0")
    M["hip"] = Mat(name + "_hip", "#1a2430")
    # the back wall stands highest under the two hip corners, just below where they lean on it
    # the side walls broken down into the heap, so the wreck spills over their tops
    D = Damage(P, prof={"N": [(0, 5.4), (0.1, 5.9), (0.2, 6.0), (0.3, 5.5), (0.42, 4.8), (0.5, 4.5), (0.58, 4.4),
                              (0.7, 5.1), (0.85, 5.9), (0.92, 5.6), (1, 5.0)],
                        "W": [(0, 0.9), (0.2, 1.5), (0.34, 1.0), (0.5, 2.2), (0.62, 1.6), (0.76, 3.2), (0.88, 4.6),
                              (1, 6.0)],
                        "E": [(0, 1.1), (0.18, 1.8), (0.36, 1.2), (0.52, 2.6), (0.66, 2.0), (0.8, 3.8), (0.9, 5.0),
                              (1, 6.2)],
                        "S": [(0, 1.6), (0.3, 2.4), (0.5, 1.8), (0.75, 2.8), (1, 2.0)]},
               drop=0.5, buried=lambda x: 2.4, seed=17, thick=0.2, grid=(1.5, 1.5))
    B = A.Builder(seed=17)
    g = roof_geom(P)
    intact(B, M, P, g, D, dormers=(), roof_on=False)
    obs = B.objects()

    dm = A.debris_mats(name, pic, seed=6, box=(0, 0, 220, 223), k=6, gain=0.95, sat=1.1)
    dm = {k: (v[1:] if k in ("slab", "stone", "chunk", "boulder", "tile") else v) for k, v in dm.items()}
    kinds = picks(M, dm, roof_share=2)
    kinds = {k: [M["block"] if m is M["trim"] else m for m in v] for k, v in kinds.items()}
    # the marble comes broken: irregular lumps and rocks, not bricks
    kinds["stone"] = kinds["stone"] + [M["block"]] * 4
    kinds["boulder"] = kinds["boulder"] + [M["block"]] * 4
    kinds["chunk"] = kinds["chunk"] + [M["block"]] * 2
    # the ruin's heap colours: pale marble and blue tile among dark gaps
    rub = rubble_mat(name, base, seed=23, uv=3.2,
                     parts=[("#cbc3b2", 3.5), ("#8a857c", 1.8), ("#38506a", 1.0), ("#121110", 5.5), ("#4e4a44", 1.5)])
    # the field of wreck: highest against the back wall, out over the front
    # and over the broken side walls as the picture draws it, with ground showing in its gaps
    plan = [(-6.45, -5.85), (-1.0, -5.95), (3.0, -5.9), (6.6, -5.6), (7.1, -2.0), (6.9, 3.1), (-6.45, 3.1),
            (-6.6, -1.0)]
    hfn = lambda x, y: 2.5 + 1.9 * min(1.0, max(0.0, (y + 4.5) / 7.9))  # noqa: E731
    inside = lambda x, y: P["x0"] + 0.3 < x < P["x1"] - 0.3 and P["yf"] + 0.3 < y < P["yb"] - 0.3  # noqa: E731
    field = field_heap(B, rub, plan, hfn, cell=0.6, edge=0.9, noise=0.45, seed=24, edges=(0, 1, 2, 3, 4, 6, 7),
                       mask=lambda x, y, z: inside(x, y) or pic.sees((x, y, z), 0), ymax=3.3)
    ground = clip(field, plan)
    # the two back hip corners, standing on the back wall's top, folded along their hips
    hp = lambda c, rw, lift=0.12: z_on(r, c, rw, field, lift)  # noqa: E731
    # (the classic render stands 0.89 of the picture's height over the anchor,
    # so the tops are traced a few pixels over the drawn ones, as the intact's are)
    nw_w = [(48, 3), (86, 64), (66, 92), (42, 100), (20, 94)]
    nw_b = [(48, 3), (66, 8), (96, 17), (104, 46), (86, 64)]
    zc_nw = hp(86, 64)
    plate_z(B, M["tile"], r, nw_w, [(0, 8.55), (1, zc_nw), (4, hp(20, 94))], edge=[4], edge_mat=M["trim"],
            rough=1.5, broken=[1, 2, 3], seed=1)
    nwb = plate_z(B, M["tile"], r, nw_b, [(0, 8.55), (1, 7.9), (4, zc_nw)], edge=[0], edge_mat=M["trim"],
                  rough=1.5, broken=[2, 3], seed=2)
    stick_px(B, M["hip"], r, (48, 3), 8.57, (86, 64), zc_nw + 0.02, w=0.14)
    ne_b = [(118, 17), (150, 13), (172, 3), (150, 62), (122, 66)]
    ne_e = [(172, 3), (182, 27), (178, 66), (150, 62)]
    zc_ne = hp(150, 62)
    neb = plate_z(B, M["tile"], r, ne_b, [(2, 8.55), (3, zc_ne), (0, 7.0)], edge=[0, 1], edge_mat=M["trim"],
                  rough=1.5, broken=[2, 3, 4], seed=3)
    nee = plate_z(B, M["tile"], r, ne_e, [(0, 8.55), (3, zc_ne), (1, 7.5)], edge=[0], edge_mat=M["trim"], rough=1.5,
                  broken=[1, 2], seed=4)
    stick_px(B, M["hip"], r, (172, 3), 8.57, (150, 62), zc_ne + 0.02, w=0.14)
    # bare rafters on the sections, sticking up past their eaves, leaning out
    for pts, a, b, up in ((nwb, (78, 28), (76, -6), 0.3), (nwb, (60, 22), (54, -4), 0.22),
                          (nwb, (90, 32), (100, -2), 0.28), (neb, (160, 24), (164, -5), 0.25),
                          (neb, (132, 30), (126, -1), 0.32), (nee, (174, 34), (212, 30), 0.1)):
        fa = on_plane(r, a[0], a[1], pts[0], plate_n(pts)) + Vector((0, 0, 0.12))
        stick_px(B, M["rafter"], r, a, fa.z, b, fa.z + (a[1] - b[1]) / 8.0 * up, w=0.16)
    # more of the roof in sections lying tilted on the heap over the middle and front
    slabs = [
        ([(112, 92), (140, 86), (146, 112), (118, 118)], (0.25, -0.3, 0.9), None, [0, 3]),
        ([(140, 96), (176, 90), (184, 118), (150, 128)], (-0.2, -0.35, 0.9), [1], [2]),
        ([(118, 120), (150, 128), (152, 142), (122, 140)], (0.2, -0.25, 0.94), None, [2]),
        ([(12, 118), (42, 112), (56, 126), (52, 148), (14, 150)], (0.3, -0.4, 0.87), [3, 4], [0, 1]),
        ([(62, 120), (96, 118), (98, 152), (66, 156)], (0.25, -0.25, 0.93), None, [0, 2]),
    ]
    for i, (poly, n, edge, broken) in enumerate(slabs):
        zc, n = settle(r, poly, n, field, ymax=3.3, lift=0.25 if i == 3 else 0.1)
        slab_px(B, M["tile"], r, poly, zc, n=n, T=0.3, edge=edge, edge_mat=M["trim"], ew=0.38, rough=1.5,
                broken=broken, seed=i + 5, ymax=3.3)
    # rafters lying across the heap and the slabs, a few ends up in the air
    for a, b, la, lb in (((60, 96), (92, 98), 0.04, 0.4), ((62, 158), (70, 176), 0.2, 0.04),
                         ((100, 150), (124, 160), 0.3, 0.04), ((150, 104), (176, 112), 0.5, 0.04),
                         ((30, 100), (50, 106), 0.04, 0.4), ((176, 150), (196, 168), 0.2, 0.04),
                         ((108, 70), (130, 78), 0.04, 0.6)):
        rafter_on(B, M["rafter"], r, field, a, b, la, lb, w=0.17, ymax=2.6)
    # cornice lengths and broken marble heaped over the front half, the
    # size of the drawn chips, and lumps of wreck out over the skirt
    pf = [(-6.4, -6.0), (6.6, -6.0), (6.6, -1.5), (-6.4, -1.5)]
    A.cover(B, {"beam": [M["block"]]}, r, pf, ground, 14, {"beam": 1}, pic=pic, length=(0.8, 1.6),
            width=(0.3, 0.5), seed=52, tilt=25, grow=1, stick=0.1)
    cornice_runs(B, M["block"], clip(field, pf), (-5.0, -3.6), -5.6, 6.0, seed=54, w=0.36, h=0.3,
                 run=(0.9, 2.0), gap=(0.6, 1.6))
    pl = [A.px(r, c, rw, 2.5) for c, rw in [(0, 60), (220, 60), (220, 223), (0, 223)]]
    A.cover(B, kinds, r, pl, ground, 105, {"slab": 2, "chunk": 3, "stone": 5, "boulder": 2.5, "beam": 0.5}, pic=pic,
            size=(0.4, 1.0), length=(0.9, 1.8), seed=51, tilt=30, grow=1, stone=(0.3, 0.7), chunk=(0.3, 0.6),
            boulder=(0.5, 1.0), thick=(0.14, 0.26), jumble=0.1, tries=80)
    # the skirt outside the walls: lumps of wreck lying out over it, not gravel
    skirt = lambda x, y: -100.0 if inside(x, y) else ground(x, y)  # noqa: E731
    A.cover(B, kinds, r, plan, skirt, 40, {"stone": 4, "boulder": 3, "chunk": 1, "beam": 0.4}, pic=pic,
            length=(0.8, 1.5), seed=53, tilt=30, grow=1, stone=(0.35, 0.8), chunk=(0.3, 0.6), boulder=(0.5, 1.0),
            tries=80)
    # soot over the standing walls, outside and in
    burns = []
    yb, yi = P["yb"], P["yb"] - P["t"] - 0.2
    for c, side, w, h in (((-3.0, yb, 5.0), "N", 3.0, 4.0), ((2.6, yb, 5.4), "N", 3.4, 4.2),
                          ((P["x0"], 1.4, 4.0), "W", 3.0, 4.0), ((P["x1"], 1.2, 4.2), "E", 3.0, 4.4),
                          ((P["x1"], -2.6, 2.2), "E", 2.4, 2.6), ((-0.4, yi, 5.0), "S", 3.0, 3.6)):
        burns.append(soot_patch(B, M["wall"], c, side, w, h, st=0.8, seed=len(burns)))
    obs += B.objects()
    burns += [((0.0, 1.0, 4.5), (7.0, 3.5, 4.0), 0.55), ((0.0, 3.0, 6.5), (7.5, 1.2, 3.0), 0.6),
              ((-6.0, 0.0, 4.0), (1.2, 4.0, 3.0), 0.5), ((6.0, 0.0, 4.0), (1.2, 4.0, 3.0), 0.5)]
    export(name, obs, Soot(burns, blotch=0.3, seed=17, low=0.1),
           keep={"_block": 0.2, "_tile": 0.75, "_rubble": 0.5, "_trim": 0.6})


# ---------------------------------------------------------------- CreBuild08a

@stage("CreBuild08a")
def s08a(name):
    """The brick building damaged: a wide breach through the back-west of
    its slate roof inside the eaves, filled with wreck and bare rafters
    under the chimney stump, holes in the middle of both slopes, the
    front-east corner broken open with the roof spilling over the broken
    front wall, a low bank of rubble along the west of the front under the
    arched windows, the round porch still standing. Soot black round every
    breach."""
    r, pic = setup(name)
    P, base = P08, "CreBuild08"
    M = materials(name, base, dim=0.75)
    g = roof_geom(P)
    rng = random.Random(8)
    holes = {
        "backwest": [(18, 30), (46, 22), (76, 24), (98, 34), (102, 56), (86, 72), (48, 76), (20, 66)],
        "back": [(127, 23), (143, 21), (149, 31), (140, 39), (128, 36)],
        "front": [(99, 84), (117, 80), (126, 92), (123, 110), (105, 114), (97, 100)],
        "east": [(132, 82), (170, 76), (200, 84), (216, 96), (216, 126), (180, 126), (150, 124), (130, 100)],
    }
    # the slate's own green-grey, black close round the breaches
    M["roof"] = tile_roof(name + "_roof", g, r, "#8c9878", holes=list(holes.values()), halo=1.0, hpad=0.7, course=0.48,
                          tile_w=0.5, gap="#0e100b", jv=0.16, ju=0.14, jk=0.9, jpow=0.8, soot=0.35, swirl=1.8,
                          width=0.4, rim=0.97, rexp=0.4, soot_col="#100f0b", planes=(1.0, 1.0, 1.1, 0.6),
                          smudge=[([(0, 4), (70, 0), (84, 20), (60, 44), (20, 56), (0, 50)], 0.8),
                                  ([(100, 6), (124, 4), (122, 18), (104, 20)], 0.75),
                                  ([(156, 44), (174, 40), (180, 58), (162, 62)], 0.7)])
    M["hip"] = Mat(name + "_hip", "#1e211a")
    # the back wall broken under the cut back-west strip, the east and back walls
    # down steeply into the open north-east corner
    D = Damage(P, prof={"S": [(0, 6.5), (0.56, 6.5), (0.61, 4.6), (0.66, 2.6), (0.8, 2.2), (0.9, 2.8), (1.0, 3.6)],
                        "E": [(0, 3.6), (0.3, 4.2), (0.55, 6.0), (0.62, 6.5), (0.68, 6.5), (0.74, 5.2), (0.8, 4.0),
                              (0.86, 2.6), (0.92, 1.4), (1, 0.9)],
                        "N": [(0, 5.9), (0.06, 6.2), (0.12, 5.8), (0.2, 6.3), (0.27, 6.0), (0.34, 6.5), (0.86, 6.5),
                              (0.9, 4.6), (0.93, 2.4), (0.96, 1.0), (1, 0.8)],
                        "W": [(0, 6.5), (0.84, 6.5), (0.9, 5.5), (1, 4.6)]},
               drop=0.08, buried=lambda x: 1.2 if x < -1.2 else -1.0, seed=8, thick=0.15, grid=(0.9, None))
    # no white eave left along the back, as drawn
    D.bare = {"N": [(0.0, 0.9)]}
    B = A.Builder(seed=8)
    intact(B, M, P, g, D, chimney_at=True)
    A.hip_caps(B, M["hip"], g, w=0.14, lift=0.02, ridge=True)
    obs = B.objects()
    J = ragged([holes["back"], holes["front"]], amp=2.0, step=3.5, seed=5)
    big = jagged([holes["backwest"], holes["east"]], amp=2.5, step=5.0, seed=5)
    # the back-west strip and the north-east eave corner broken away, traced
    # over the drawn edges by the classic render's 0.894 of the picture's height
    big += jagged([[(-20, -40), (80, -40), (80, -4), (76, 2), (70, 6), (50, 7), (25, 6), (14, 9), (8, 14), (3, 18),
                    (-20, 20)],
                   [(152, -40), (172, -40), (171, 5), (163, 7), (156, 2)],
                   [(186, -40), (240, -40), (240, 47), (213, 46), (204, 43), (198, 30), (192, 13), (187, -2)]],
                  amp=2.0, step=4.0, seed=9)
    A.cut_view(group(obs, "_roof"), J + big, r)
    # the hips and trim cut a little wider, so their cut faces never lie in the roof's
    A.cut_view(group(obs, "_hip"), grow(J + big, 0.6), r)
    A.cut_view(group(obs, "_trim"), grow(J + big, 1.0), r)
    drop_bits(obs)
    slivers(group(obs, "_roof", "_trim", "_hip"))

    dm = A.debris_mats(name, pic, seed=6, box=(0, 0, 213, 182), k=6, gain=1.0, sat=1.1)
    dm = {k: (v[2:] if k in ("slab", "stone", "chunk", "boulder", "tile") else v) for k, v in dm.items()}
    kinds = kinds_of(M, dm, roof_share=3, pale=1)
    rub = rubble_mat(name, base, seed=25, dark=1.6)
    # the breaches: burnt floors sunk under the slate, charred pieces and rafters in them
    hole_fill(B, r, pic, M, dm, g, J + [jagged([holes["backwest"]], amp=2.5, step=5.0, seed=5)[0]], sag=0.9, n=6,
              seed=26, dark=True)
    for a, b, lift in (((30, 64), (24, 40), 0.3), ((50, 70), (58, 44), 0.4), ((70, 66), (80, 40), 0.3),
                       ((88, 60), (90, 40), 0.2), ((40, 30), (64, 34), -0.2), ((130, 34), (138, 22), 0.4),
                       ((108, 104), (100, 92), 0.3), ((116, 108), (124, 98), 0.3)):
        if lift < 0:
            rafter_px(B, M["rafter"], r, g, a, b, w=0.15, drop=-lift)
        else:
            rafter_px(B, M["rafter"], r, g, a, b, lift=lift, w=0.15)
    # the front slope's hole with a slab tipped into it
    hz = g.hit(r, 112, 96)
    slab_px(B, M["tile"], r, [(102, 86), (120, 84), (124, 104), (106, 108)], hz[2] - 0.8, n=(0.35, 0.3, 0.9), T=0.25,
            rough=0.5, seed=7)
    # the front-east corner: roof and floor heaped inside to under the eaves,
    # over the broken front wall's top, and a little spilt in front of it
    ef = [(0.6, -3.0), (5.95, -3.0), (5.95, 3.2), (2.2, 3.2), (0.6, -0.4)]
    he = field_heap(B, rub, ef, lambda x, y: min(2.1 + 2.5 * min(1.0, max(0.0, (y + 3.0) / 3.6)) ** 0.7,
                                                 2.2 + 1.5 * max(0.0, 5.95 - x) + 1.5 * max(0.0, 3.2 - y)),
                    cell=0.5, edge=1.0, noise=0.3, seed=28, edges=(3, 4), clamp=True)
    es = [(1.6, -4.0), (6.5, -3.9), (6.5, -3.32), (1.6, -3.32)]
    hs = field_heap(B, rub, es, lambda x, y: 0.55 * min(1.0, max(0.0, (y + 4.0) / 0.6)), cell=0.4, edge=0.4,
                    noise=0.1, seed=30, edges=(0, 1, 3))
    for i, (poly, n) in enumerate((([(150, 84), (178, 80), (184, 100), (160, 108)], (-0.3, -0.4, 0.86)),
                                   ([(176, 96), (204, 92), (206, 116), (184, 118)], (0.3, -0.3, 0.9)),
                                   ([(134, 100), (154, 98), (160, 118), (140, 122)], (0.2, -0.5, 0.85)))):
        zc, n = settle(r, poly, n, he, lift=0.1)
        slab_px(B, M["tile"], r, poly, zc, n=n, T=0.28, rough=1.0, broken=[0, 2], seed=10 + i)
    # a low bank of rubble along the west of the front, under the arched windows
    wl = [(-6.7, -4.75), (-1.2, -4.7), (-1.2, -3.32), (-6.7, -3.32)]

    def wfn(x, y):
        bump = max(math.exp(-((x - c) / 0.6) ** 2) for c in (-5.9, -3.94, -1.94))
        return (1.4 + 2.0 * bump) * min(1.0, max(0.0, (y + 4.75) / 1.4)) ** 0.9
    hl = field_heap(B, rub, wl, wfn, cell=0.4, edge=0.5, noise=0.25, seed=29, edges=(0, 1, 3))
    # the porch's ring filled with wreck up over the door
    yf_ = P["yf"]
    pring = [(1.1 * math.cos(math.pi + math.pi * i / 10), yf_ + 1.05 * math.sin(math.pi + math.pi * i / 10))
             for i in range(11)]
    hp_ = field_heap(B, rub, pring, lambda x, y: 0.35 + 2.0 * min(1.0, max(0.0, (y - yf_ + 1.05) / 1.05)) ** 1.2,
                     cell=0.3, edge=0.25, noise=0.15, seed=31, edges=tuple(range(10)))
    ground = clip(A.ground_of(he, hl, hs, hp_), ef, wl, es, pring)
    for a, b, la, lb in (((150, 100), (140, 84), 0.1, 0.5), ((186, 100), (196, 84), 0.1, 0.5),
                         ((170, 112), (164, 96), 0.1, 0.4)):
        rafter_on(B, M["rafter"], r, he, a, b, la, lb, w=0.15)
    pl = [A.px(r, c, rw, 1.0) for c, rw in [(0, 60), (213, 60), (213, 182), (0, 182)]]
    A.cover(B, kinds, r, pl, ground, 80, {"slab": 3, "chunk": 4, "stone": 3, "boulder": 1, "beam": 1}, pic=pic,
            size=(0.45, 1.0), length=(0.9, 1.8), seed=61, tilt=20, grow=1, stone=(0.3, 0.6), chunk=(0.3, 0.55),
            boulder=(0.5, 0.9), thick=(0.12, 0.22), jumble=0.05, tries=80)
    burns = []
    yf, yb = P["yf"], P["yb"]
    # soot streaking up the west front from its heaps, and round the porch
    for c, side, w, h in (((-3.94, yf, 2.8), "S", 1.3, 3.6), ((2.6, yf, 4.6), "S", 2.0, 3.2),
                          ((-1.94, yf, 2.8), "S", 1.3, 3.6), ((-5.9, yf, 2.8), "S", 1.1, 3.6),
                          ((0.0, yf, 3.4), "S", 1.4, 2.0),
                          ((P["x0"], 2.4, 4.4), "W", 3.0, 3.4), ((P["x1"], -0.6, 4.6), "E", 2.6, 3.6),
                          ((-3.4, yb, 5.0), "N", 3.6, 2.6), ((2.6, yb, 5.2), "N", 3.0, 2.4)):
        burns.append(soot_patch(B, M["wall"], c, side, w, h, st=0.7, seed=len(burns)))
    obs += B.objects()
    nw = g.hit(r, 58, 48)
    burns += hole_burns(r, g, J, rad=1.2, s=0.6, k=1.0) + [
        ((4.0, -3.0, 3.5), 3.5, 0.55), ((nw[0], nw[1], nw[2]), (3.4, 2.6, 2.5), 0.6),
        ((3.5, -3.4, 2.6), (3.2, 1.0, 2.6), 0.5), ((0.0, -3.5, 6.2), (7.0, 1.0, 1.2), 0.4),
        ((-3.9, -3.4, 1.6), (3.4, 0.9, 3.8), 0.8), ((-5.9, -3.4, 1.8), (1.0, 0.9, 4.4), 0.75),
        ((-1.9, -3.4, 1.8), (1.0, 0.9, 4.4), 0.75), ((-3.9, -3.4, 1.8), (1.0, 0.9, 4.4), 0.75), ((0.0, -3.5, 1.2), (1.4, 0.9, 2.4), 0.5),
        ((-3.5, 2.0, 7.0), (3.6, 2.4, 2.4), 0.5)]
    export(name, obs, Soot(burns, blotch=0.35, seed=8, low=0.15),
           keep={"_roof": 0.3, "_rubble": 0.8, "_tile": 0.6, "_dash": 0.5})


# ---------------------------------------------------------------- CreBuild08b

def broken_ring(B, mat, cx, y, rx, ry, w, h, keep, seg=12, z=0.0, seed=0):
    """The round porch's step broken: its half ring (rx, ry out from the
    wall at y, w wide, h high) in the arcs keep names (indices of seg),
    each a little sunk or tipped."""
    rng = random.Random(seed)
    for i in keep:
        a0, a1 = math.pi + math.pi * i / seg, math.pi + math.pi * (i + 1) / seg
        k = 1
        outer = [(cx + rx * math.cos(a0 + (a1 - a0) * j / k), y + ry * math.sin(a0 + (a1 - a0) * j / k))
                 for j in range(k + 1)]
        inner = [(cx + (rx - w) * math.cos(a0 + (a1 - a0) * j / k), y + (ry - w) * math.sin(a0 + (a1 - a0) * j / k))
                 for j in range(k, -1, -1)]
        dz = rng.uniform(-0.08, 0.02)
        B.prism(mat, outer + inner, z + dz, z + dz + h * rng.uniform(0.7, 1.0))


@stage("CreBuild08b")
def s08b(name):
    """The brick building destroyed: the front wall stands full height
    with its windows at the west under its piece of sooted roof, and to the
    east as broken courses with the lower windows; between, the fallen roof
    and storeys pour out over its broken middle down to the ground and
    across the porch's broken ring. Behind it all the roof has come down
    onto a heap of brick, slate and timber filling the plan, the east hip
    lying tipped across it with its eaves, wreck strewn out to the east and
    banked along the front as in the ruin."""
    r, pic = setup(name)
    P, base = P08, "CreBuild08"
    M = materials(name, base, dim=0.66)
    g = roof_geom(P)
    rng = random.Random(18)
    M["roof"] = tile_roof(name + "_roof", g, r, "#575d4a", course=0.48, tile_w=0.5, gap="#181b14", jv=0.14, ju=0.12,
                          soot=0.65, swirl=1.8, width=0.5,
                          holes=[[(60, 60), (80, 58), (92, 72), (86, 100), (66, 98)], [(4, 56), (30, 54), (28, 76)]],
                          halo=1.6, rim=0.95, rexp=0.5)
    M["hip"] = Mat(name + "_hip", "#1e211a")
    # the front low only under the cascade; east of it the brick stands to about the arched windows' heads
    D = Damage(P, prof={"S": [(0, 6.5), (0.33, 6.5), (0.37, 4.4), (0.41, 1.8), (0.46, 1.1), (0.5, 1.3), (0.53, 2.4),
                              (0.55, 4.2), (0.66, 4.3), (0.8, 4.3), (0.9, 4.6), (1, 5.0)],
                        "W": [(0, 6.5), (0.5, 6.5), (0.65, 4.5), (1, 3.5)],
                        "N": [(0, 3.5), (0.3, 4.6), (0.6, 3.8), (0.85, 5.0), (1, 4.5)],
                        "E": [(0, 5.0), (0.4, 3.6), (0.7, 3.6), (1, 3.4)]},
               drop=0.1, buried=lambda x: 0.4 if x < -1.6 else -1.0, seed=18, thick=0.15, grid=(1.2, None))
    D.porch = False
    B = A.Builder(seed=18)
    intact(B, M, P, g, D, chimney_at=False)
    A.hip_caps(B, M["hip"], g, w=0.14, lift=0.02, ridge=True)
    obs = B.objects()
    # all the roof but its front-west corner is gone, its broken edge drawn back off the broken front
    keep_roof = [(0, 50), (40, 46), (70, 54), (86, 60), (84, 90), (72, 112), (0, 112)]
    A.cut_view(group(obs, "_roof"), jagged([keep_roof], amp=3.0, step=5.0, seed=2), r, mode="INTERSECT")
    A.cut_view(group(obs, "_hip"), grow(jagged([keep_roof], amp=3.0, step=5.0, seed=2), -0.6), r, mode="INTERSECT")
    drop_bits(obs)
    slivers(group(obs, "_roof", "_hip"))

    dm = A.debris_mats(name, pic, seed=7, box=(0, 0, 233, 173), k=6, gain=1.0, sat=1.1)
    dm = {k: (v[2:] if k in ("slab", "stone", "chunk", "boulder", "tile") else v) for k, v in dm.items()}
    kinds = kinds_of(M, dm, roof_share=3, pale=1)
    rub = rubble_mat(name, base, seed=35, dark=1.8)
    # the field inside the walls, high in the middle behind the broken front, and strewn out east
    inner = [(-6.05, -3.0), (6.1, -3.0), (6.1, 3.35), (-6.05, 3.35)]

    def hfn(x, y):
        # up from the broken walls' tops at the edges to a third of the walls' height and more in the middle
        d = min(x + 5.9, 5.96 - x, 3.2 - y)
        return min(3.7 + 0.9 * min(1.0, max(0.0, (y + 3.0) / 3.0)) + 0.5 * math.sin(x * 0.9 + 1.0),
                   2.6 + 1.3 * max(0.0, d), 1.4 + 2.0 * max(0.0, y + 3.0))
    hi = field_heap(B, rub, inner, hfn, cell=0.6, edge=1.0, noise=0.35, seed=36, clamp=True, edges=())
    out_e = [(6.4, -3.6), (7.5, -2.6), (7.7, 1.0), (7.2, 3.6), (6.4, 3.6)]
    ho = field_heap(B, rub, out_e, lambda x, y: 1.4, cell=0.55, edge=1.1, noise=0.25, seed=37, edges=(0, 1, 2, 3))
    # the cascade out over the broken middle of the front, down to the ground across the porch
    cas = [(-2.6, -4.5), (-1.7, -4.45), (-1.0, -3.45), (0.75, -3.45), (0.75, -2.6), (-2.6, -2.6)]

    def cfn(x, y):
        k = min(1.0, max(0.0, (x + 2.6) / 1.2), max(0.0, (0.75 - x) / 0.6))
        return k * (0.4 + 4.2 * min(1.0, max(0.0, (y + 4.5) / 1.9)) ** 1.1)
    hc = field_heap(B, rub, cas, cfn, cell=0.5, edge=0.5, noise=0.35, seed=38, edges=(0, 1, 2, 3, 5))
    # the banks along the front the ruin had, west and east
    wl = [(-6.6, -4.5), (-2.4, -4.45), (-2.4, -3.32), (-6.6, -3.32)]
    hl = field_heap(B, rub, wl, lambda x, y: 1.8 * min(1.0, max(0.0, (y + 4.5) / 1.2)) ** 0.9, cell=0.5,
                    edge=0.5, noise=0.25, seed=39, edges=(0, 1, 3))
    # heaps against the standing east front, between its lower windows
    es = [(1.7, -4.0), (6.7, -3.95), (6.7, -3.32), (1.7, -3.32)]

    def efn(x, y):
        bump = max(math.exp(-((x - c) / 0.4) ** 2) for c in (2.8, 4.25, 5.8))
        return (0.25 + 1.7 * bump) * min(1.0, max(0.0, (y + 4.0) / 0.65)) ** 0.9
    hs = field_heap(B, rub, es, efn, cell=0.4, edge=0.4, noise=0.15, seed=40, edges=(0, 1, 3))
    field = A.ground_of(hi, ho, hc, hl, hs)
    ground = clip(field, inner, out_e, cas, wl, es)
    # the porch's step, broken into pieces under the wreck
    # its white stone, out in front of the soot
    M["white"] = Mat(name + "_white", tex=A.tex_mottle(name + "_white", "#d4d1c8", var=0.06, seed=22), uv=1.0,
                     ref="#d4d1c8")
    broken_ring(B, M["white"], 0.0, P["yf"], 1.2, 1.15, 0.3, 0.2, keep=(0, 1, 3, 4, 5, 7, 8, 10, 11), seed=3)
    pring = [(1.05 * math.cos(math.pi + math.pi * i / 10), P["yf"] + 1.0 * math.sin(math.pi + math.pi * i / 10))
             for i in range(11)]
    field_heap(B, rub, pring, lambda x, y: 0.22, cell=0.3, edge=0.3, noise=0.08, seed=41, edges=tuple(range(10)))
    broken_ring(B, M["white"], 0.0, P["yf"], 1.0, 0.8, 0.26, 0.16, keep=(0, 2, 3, 8, 9, 11), z=0.2, seed=4)
    pp = [(-1.1, -4.4), (1.1, -4.4), (1.1, -3.5), (-1.1, -3.5)]
    A.cover(B, {"chunk": [M["trim"], M["wall"]], "stone": [M["wall"]]}, r, pp,
            lambda x, y: 0.0 if A.inside_poly(pp, x, y) else -100.0, 6, {"chunk": 2, "stone": 1}, seed=74,
            chunk=(0.2, 0.4), stone=(0.2, 0.35), tries=60)
    # the east hip tipped across the heap: a straight-edged triangle off the roof, its hip folded, its eaves on
    zc, n = settle(r, [(146, 24), (180, 12), (208, 42), (204, 66), (166, 68)], (0.3, -0.2, 0.93), field, (1, 4), -18,
                   ymax=3.4, lift=0.12)
    fold_px(B, M["tile"], r, [(146, 24), (180, 12), (208, 42), (204, 66), (166, 68)], (1, 4), zc, n, -18, T=0.28,
            edge=[1, 2], edge_mat=M["trim"], ew=0.32, hip_mat=M["hip"], rough=1.0, broken=[3, 4], seed=9, ymax=3.4)
    slabs = [
        ([(100, 30), (130, 26), (138, 50), (108, 56)], (-0.2, -0.3, 0.93), None),
        ([(100, 66), (130, 62), (134, 88), (104, 92)], (0.1, -0.4, 0.9), None),
        ([(150, 80), (176, 76), (182, 98), (156, 102)], (0.3, -0.3, 0.9), None),
        ([(118, 40), (144, 36), (148, 58), (122, 62)], (0.2, -0.35, 0.9), None),
        ([(96, 104), (124, 100), (128, 124), (100, 128)], (0.2, -0.6, 0.78), None),
        ([(130, 110), (156, 106), (160, 128), (134, 132)], (-0.2, -0.55, 0.8), None),
    ]
    for i, (poly, n, edge) in enumerate(slabs):
        zc, n = settle(r, poly, n, field, ymax=3.4, lift=0.1)
        slab_px(B, M["tile"], r, poly, zc, n=n, T=0.28, edge=edge, edge_mat=M["trim"], ew=0.3, rough=1.0,
                broken=[1, 2], seed=i, ymax=3.4)
    for a, b, la, lb in (((150, 30), (154, 50), 0.6, 0.04), ((104, 22), (118, 30), 0.4, 0.04),
                         ((180, 40), (214, 46), 0.3, 0.04), ((126, 110), (146, 118), 0.2, 0.04),
                         ((200, 100), (226, 108), 0.2, 0.04), ((120, 76), (140, 70), 0.04, 0.4),
                         ((90, 120), (104, 150), 0.3, 0.04), ((140, 118), (150, 142), 0.2, 0.04)):
        rafter_on(B, M["rafter"], r, field, a, b, la, lb, w=0.16, ymax=2.95)
    pl = [A.px(r, c, rw, 2.0) for c, rw in [(70, 0), (233, 0), (233, 173), (70, 173)]]
    A.cover(B, kinds, r, pl, ground, 115, {"slab": 3, "chunk": 6, "stone": 3, "boulder": 1, "beam": 1.5}, pic=pic,
            size=(0.45, 1.0), length=(0.9, 1.8), seed=71, tilt=25, grow=1, stone=(0.3, 0.6), chunk=(0.28, 0.55),
            boulder=(0.5, 0.9), thick=(0.12, 0.22), jumble=0.08, tries=80)
    pw = [A.px(r, c, rw, 1.0) for c, rw in [(0, 120), (70, 120), (70, 173), (0, 173)]]
    A.cover(B, kinds, r, pw, ground, 18, {"chunk": 4, "stone": 3, "boulder": 1, "beam": 1}, pic=pic,
            length=(0.8, 1.5), seed=72, tilt=25, grow=1, stone=(0.3, 0.55), chunk=(0.28, 0.5), boulder=(0.5, 0.8),
            tries=80)
    # chunks and timbers fallen on the roof still standing
    kp = [g.hit(r, c, rw) for c, rw in [(6, 56), (38, 50), (66, 58), (80, 64), (78, 92), (66, 108), (6, 108)]]
    kp = [(h[0], h[1]) for h in kp if h is not None]
    A.cover(B, kinds, r, kp, lambda x, y: (g.top(x, y) or -100.0) if A.inside_poly(kp, x, y) else -100.0, 16,
            {"chunk": 4, "stone": 3, "beam": 1.5}, pic=pic, length=(0.8, 1.4), seed=73, tilt=10, grow=1,
            stone=(0.22, 0.45), chunk=(0.22, 0.45), tries=80)
    burns = []
    yf = P["yf"]
    for c, side, w, h in (((-4.4, yf, 4.0), "S", 3.4, 4.6), ((-2.6, yf, 4.2), "S", 1.6, 3.6),
                          ((4.4, yf, 2.6), "S", 2.6, 2.8),
                          ((P["x0"], -0.6, 4.6), "W", 3.4, 4.0), ((P["x0"], 2.4, 2.6), "W", 3.0, 2.6)):
        burns.append(soot_patch(B, M["wall"], c, side, w, h, st=0.75, seed=len(burns)))
    obs += B.objects()
    burns += [((1.5, 0.3, 4.0), (6.5, 3.6, 2.6), 0.6), ((-3.6, -1.5, 6.5), 3.2, 0.6), ((1.2, -3.6, 2.4), 3.4, 0.5),
              ((-4.0, -0.8, 7.4), (3.0, 2.6, 1.6), 0.5), ((-4.4, -3.4, 2.0), (2.6, 0.9, 3.6), 0.65),
              ((-5.9, -3.4, 2.2), (1.0, 0.9, 4.4), 0.6), ((-3.0, -3.4, 2.2), (1.0, 0.9, 4.4), 0.6),
              ((3.6, -3.4, 2.0), (3.4, 0.9, 2.4), 0.5)]
    export(name, obs, Soot(burns, blotch=0.45, seed=18, low=0.15),
           keep={"_rubble": 0.6, "_roof": 0.6, "_tile": 1.0, "_ds": 0.5, "_white": 0.3})


# ---------------------------------------------------------------- CreBuild09a and 09b

def fallen_bow(B, mat, x, y, z, yaw, pitch, roll, r=1.15):
    """One of the arcade's round balcony fronts lying broken in the rubble."""
    with B.at(x, y, z, yaw=yaw, pitch=pitch, roll=roll):
        half_disc(B, mat, 0.0, 0.0, 0.0, r, r * 0.9, 0.36, seg=8, ring=0.15)


P09_FRONT = {"S0": [(0, 4.3), (0.5, 4.3), (0.56, 2.8), (0.66, 1.6), (0.85, 1.2), (1, 1.8)],
             "S": [(0, 7.3), (0.53, 7.3), (0.58, 5.4), (0.64, 4.5), (1, 4.4)],
             "E0": [(0, 1.4), (1, 2.4)]}


@stage("CreBuild09a")
def s09a(name):
    """The arcaded building damaged: the east half of its front fallen into
    a heap of marble with the arcade's round fronts in it, heaped up to the
    balcony inside the broken storey behind, under a breach in the roof's
    front. The roof keeps its height and its tiles elsewhere, with a few
    black breaches full of wreck and bare rafters, the rafters sticking up
    over the back eave where the chimney came down. The west half of the
    front still stands with its balcony and windows."""
    r, pic = setup(name)
    P, base = P09, "CreBuild09"
    M = materials(name, base, dim=0.78)
    g = roof_geom(P)
    rng = random.Random(9)
    holes = {
        "top": [(62, 8), (100, 6), (112, 14), (108, 40), (96, 58), (74, 60), (62, 40)],
        "mid": [(95, 62), (112, 58), (130, 66), (128, 86), (110, 90), (96, 82)],
        "west": [(22, 34), (44, 28), (60, 40), (58, 64), (40, 76), (24, 66)],
        "edge": [(-20, 30), (16, 28), (22, 42), (14, 54), (-20, 54)],
    }
    front = [(60, 128), (86, 122), (112, 126), (150, 128), (162, 150), (162, 178), (100, 176), (62, 166), (56, 146)]
    M["roof"] = tile_roof(name + "_roof", g, r, "#59604b", holes=list(holes.values()) + [front], halo=1.7,
                          course=0.38, tile_w=0.38, gap="#14170f", jv=0.18, ju=0.16, soot=0.6, swirl=1.8, width=0.5,
                          rim=0.97, rexp=0.45)
    M["hip"] = Mat(name + "_hip", "#1e211a")
    # the back wall broken under the back eave's fallen middle
    prof = dict(P09_FRONT, E=[(0, 4.6), (0.1, 6.0), (0.18, 7.3), (1, 7.3)],
                N=[(0, 7.3), (0.35, 7.3), (0.4, 6.6), (0.5, 6.2), (0.62, 6.5), (0.72, 6.1), (0.8, 6.6), (0.86, 7.3),
                   (1, 7.3)])
    D = Damage(P, prof=prof, drop=0.0, buried=lambda x: 1.6 if x > 0.3 else -1.0, seed=9, thick=0.15)
    B = A.Builder(seed=9)
    intact(B, M, P, g, D, chimney_at=False)
    A.hip_caps(B, M["hip"], g, w=0.14, lift=0.02, ridge=True)
    # the chimney broken off over the roof
    chimney(B, M, 0.05, 5.1, g.top(0.05, 5.1) - 0.5, 8.7, sx=1.3, sy=0.8)
    obs = B.objects()
    J = ragged(list(holes.values()), amp=2.0, step=3.5, seed=6)
    big = jagged([front], amp=2.5, step=5.0, seed=6)
    # the back eave broken away over the top breach (traced over the drawn edge by the render's 0.894)
    big += jagged([[(54, -40), (122, -40), (120, 2), (110, 6), (90, 8), (70, 6), (57, 3)]], amp=2.0, step=4.0, seed=7)
    A.cut_view(group(obs, "_roof"), J + big, r)
    A.cut_view(group(obs, "_hip"), grow(J + big, 0.6), r)
    A.cut_view(group(obs, "_trim"), grow(J + big, 1.0), r)
    drop_bits(obs, size=0.35, thin=0.05)

    dm = A.debris_mats(name, pic, seed=8, box=(60, 150, 157, 260), k=6, gain=1.0, sat=1.05)
    dm = {k: (v[2:] if k in ("slab", "stone", "chunk", "boulder", "tile") else v) for k, v in dm.items()}
    kinds = kinds_of(M, dm, roof_share=1, pale=2)
    # the heap as drawn: white marble blocks and arch pieces with dark gaps between, few mid tones
    rub = rubble_mat(name, base, seed=45, parts=[("#d0c8b7", 2.0), ("#a69e8c", 1.0), ("#565b4a", 1.0), ("#100f0d", 4.6),
                                                 ("#2a2824", 1.4)])
    M["white"] = Mat(name + "_white", tex=A.tex_mottle(name + "_white", "#ddd6c6", var=0.06, seed=22), uv=1.0,
                     ref="#ddd6c6")
    # the breaches: black, sunk under the slate, charred wreck and rafters in them
    hole_fill(B, r, pic, M, dm, g, J, sag=0.9, n=6, seed=46, dark=True)
    # the front's east half heaped out over the ground, and inside the broken
    # storey behind it to about the balcony, under the breach in the roof's front
    fr = [(-1.2, -4.9), (-0.4, -5.95), (2.0, -6.1), (4.9, -5.85), (5.1, -3.0), (4.6, -1.2), (4.15, -0.6), (4.15, 2.4),
          (-1.2, 2.4)]
    ffn = lambda x, y: 2.4 + 2.5 * min(1.0, max(0.0, (y + 5.5) / 3.3)) ** 0.8  # noqa: E731
    hf = field_heap(B, rub, fr, ffn, cell=0.5, edge=1.0, noise=0.4, seed=47, edges=(1, 2, 3, 4), clamp=True)
    for x, y, z, yaw, pitch, roll in ((1.6, -4.9, 2.6, 20, 35, 10), (3.4, -4.4, 3.0, -30, -25, 15),
                                      (0.6, -5.2, 1.8, 70, 20, -10)):
        fallen_bow(B, M["white"], x, y, max(0.2, hf(x, y) - 0.25), yaw, pitch, roll)
    for x, y, sx, yaw, pitch in ((2.6, -3.4, 2.2, 15, 20), (1.2, -4.0, 1.8, -25, -15), (3.8, -3.2, 1.6, 60, 30),
                                 (3.0, -5.0, 1.4, -50, 15), (2.0, -5.4, 1.0, 80, 10)):
        B.box(M["white"], x, y, max(0.1, hf(x, y) - 0.15), sx, 0.45, 0.4, yaw=yaw, pitch=pitch)
    for a, b, lift in (((40, 60), (32, 40), 0.4), ((50, 52), (56, 36), 0.3), ((12, 46), (2, 40), 0.3),
                       ((104, 82), (120, 66), 0.3)):
        rafter_px(B, M["rafter"], r, g, a, b, lift=lift, w=0.15)
    # rafters standing up out of the top breach over the back eave, their feet down in its sunk fill
    for a, b, up in (((76, 34), (62, -4), 1.4), ((90, 40), (102, -6), 1.6), ((104, 30), (118, 0), 1.2),
                     ((70, 28), (54, 6), 1.0)):
        zr = (g.hit(r, *a) or (0, 0, 8.5))[2]
        stick_px(B, M["rafter"], r, a, zr - 1.1, b, zr - 0.2 + up, w=0.17)
    # wreck along the broken back wall's top, under the fallen eave
    field_heap(B, rub, [(-1.3, 5.0), (3.2, 5.0), (3.2, 6.25), (-1.3, 6.25)],
               lambda x, y: 0.9 + 0.3 * math.sin(x * 2.1), cell=0.4, edge=0.5, noise=0.25, seed=49, z0=5.9)
    for a, b, la, lb in (((86, 130), (100, 148), 0.4, 0.1), ((120, 140), (132, 160), 0.3, 0.1),
                         ((70, 150), (92, 142), 0.2, 0.5)):
        rafter_on(B, M["rafter"], r, hf, a, b, la, lb, w=0.16)
    ground = clip(hf, fr)
    pl = [A.px(r, c, rw, 2.0) for c, rw in [(56, 120), (157, 120), (157, 260), (56, 260)]]
    A.cover(B, kinds, r, pl, ground, 75, {"slab": 3, "chunk": 6, "stone": 2, "beam": 0.6}, pic=pic,
            size=(0.5, 1.0), length=(0.9, 1.6), seed=48, tilt=25, grow=1, stone=(0.3, 0.55), chunk=(0.35, 0.65),
            thick=(0.14, 0.26), jumble=0.08, tries=80)
    burns = []
    for c, side, w, h in (((-0.6, P["yf"], 6.0), "S", 2.0, 3.4), ((-2.0, P["y0"], 3.4), "S", 2.4, 2.4),
                          ((P["x1"], -0.6, 5.0), "E", 3.0, 4.4), ((P["x0"], 2.0, 5.4), "W", 3.0, 3.4),
                          ((-1.0, P["yb"], 5.6), "N", 3.4, 3.0), ((P["x1"], 3.4, 5.6), "E", 2.6, 2.8)):
        burns.append(soot_patch(B, M["wall"], c, side, w, h, st=0.7, seed=len(burns)))
    obs += B.objects()
    fb = g.hit(r, 104, 150) or (1.6, -3.0, 7.0)
    burns += hole_burns(r, g, J, rad=1.8, s=0.65) + [((2.4, -3.8, 3.0), 3.6, 0.45),
                                                     ((fb[0], fb[1], fb[2]), (3.6, 2.6, 3.0), 0.55)]
    export(name, obs, Soot(burns, blotch=0.35, seed=9, low=0.12),
           keep={"_roof": 0.5, "_rubble": 0.5, "_block": 0.3, "_white": 0.15, "_hash": 0.4, "_dash": 0.5})


def hinge_px(B, mat, r, poly, hinge, zh, ground, T=0.28, edge=(), edge_mat=None, ew=0.3, rough=0.0, broken=None,
             seed=0, lift=0.08):
    """A roof section drawn as outline poly (sprite pixels) still hanging
    from its eave: its corners hinge = (i, j) held at height zh on the wall
    top, the rest let down about that line until its lowest corner rests on
    ground. Returns its normal."""
    def at(k, z):
        x, y = A.px(r, poly[k][0], poly[k][1], z)
        return Vector((x, y, z))
    a, b = at(hinge[0], zh), at(hinge[1], zh)
    others = [k for k in range(len(poly)) if k not in hinge]
    pa, pb = Vector(poly[hinge[0]]), Vector(poly[hinge[1]])
    d = (pb - pa).normalized()

    def far(k):
        q = Vector(poly[k]) - pa
        return (q - d * q.dot(d)).length
    f = max(others, key=far)
    zf, n = zh - 1.0, Vector((0.0, 0.0, 1.0))
    for _ in range(40):
        n = (b - a).cross(at(f, zf) - a).normalized()
        if n.z < 0:
            n = -n
        P = [on_plane(r, poly[k][0], poly[k][1], a, n) for k in others]
        gap = min(p.z - max(0.0, ground(p.x, p.y)) for p in P)
        if abs(gap - lift) < 0.01:
            break
        zf -= (gap - lift) * 0.7
    if rough > 0:
        poly2, _, where = rough_outline(poly, (), amp=rough, seed=seed, broken=broken)
    else:
        poly2, where = list(poly), list(range(len(poly)))
    pts = [on_plane(r, c, rw, a, n) for c, rw in poly2]
    _plate(B, mat, pts, T, [where[e] for e in edge], edge_mat, ew, n)
    return n


def attic_wall(B, M, g, a, b, t, seed=0, gap=0.55):
    """A cross wall from plan point a to b filling up under the roof g to
    gap below its top, broken along its top."""
    k = 8
    prof = []
    for i in range(k + 1):
        f = i / k
        x, y = a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f
        prof.append((f, (g.top(x, y) or g.ze) - gap))
    wall_run(B, M["wall"], M["wtop"], a, b, t, prof, g.zr + 1.0, seed=seed, jitter=0.25)


@stage("CreBuild09b")
def s09b(name):
    """The arcaded building destroyed: the west half of its front still
    stands with the piece of roof over it, closed behind by the broken
    cross walls under that roof. The rest of the roof has fallen in: slabs
    of the back slope still hang from the back wall's top down onto the
    wreck, the west slope's eave lies on it, more slabs, timbers and marble
    across the heap that fills the plan. The back corner has come down,
    and the east half of the front lies in a heap of marble out over the
    ground with the arcade's round fronts in it."""
    r, pic = setup(name)
    P, base = P09, "CreBuild09"
    M = materials(name, base, dim=0.7)
    g = roof_geom(P)
    M["roof"] = tile_roof(name + "_roof", g, r, "#53584a", course=0.38, tile_w=0.38, gap="#14170f", jv=0.18, ju=0.16,
                          soot=0.75, swirl=1.8, width=0.55)
    # the storeys' marble in the drawn chips' range, pale on the heap
    M["block"] = Mat(name + "_block", tex=A.tex_mottle(name + "_block", "#c4b8a8", var=0.08, seed=20), uv=1.2,
                     ref="#c4b8a8")
    M["hip"] = Mat(name + "_hip", "#1e211a")
    # the fallen slates darker, with broad black joints, as the shell draws them
    M["tile"] = Mat(name + "_tile", tex=tex_slate(name + "_tile", "#4e5444", "#080907", n=8, jv=0.3, ju=0.28,
                                                  seed=13), uv=8 * 0.38, ref="#4e5444")
    # the east wall ragged down into the heap but where the north-east slab rests on it
    prof = dict(P09_FRONT,
                E=[(0, 3.4), (0.15, 4.2), (0.3, 3.6), (0.45, 4.6), (0.6, 4.0), (0.72, 5.2), (0.82, 4.6), (0.9, 6.4),
                   (1, 7.0)],
                W=[(0, 7.3), (0.45, 7.3), (0.55, 6.0), (0.7, 4.6), (0.85, 3.2), (0.95, 2.0), (1, 1.6)],
                N=[(0, 1.6), (0.05, 1.9), (0.09, 4.0), (0.12, 6.9), (0.45, 6.9), (0.5, 6.3), (0.62, 6.1), (0.68, 6.9),
                   (1, 7.3)])
    D = Damage(P, prof=prof, drop=0.0, buried=lambda x: 1.6 if x > 0.3 else -1.0, seed=19, thick=0.15)
    B = A.Builder(seed=19)
    intact(B, M, P, g, D, chimney_at=False)
    A.hip_caps(B, M["hip"], g, w=0.14, lift=0.02, ridge=True)
    obs = B.objects()
    keep_roof = [(0, 92), (30, 98), (60, 106), (92, 110), (96, 128), (80, 150), (78, 168), (0, 168)]
    A.cut_view(group(obs, "_roof", "_hip"), jagged([keep_roof], amp=3.0, step=5.0, seed=3), r, mode="INTERSECT")
    drop_bits(obs)
    # the cross walls the kept roof stands on, filling up under it
    yc, xc = -1.3, 0.75
    attic_wall(B, M, g, (P["x0"] + P["t"] + 0.15, yc), (xc + 0.18, yc), 0.35, seed=91)
    attic_wall(B, M, g, (xc, P["yf"] + P["t"]), (xc, yc - 0.18), 0.35, seed=92)

    dm = A.debris_mats(name, pic, seed=9, box=(0, 0, 154, 256), k=6, gain=1.05, sat=1.05)
    dm = {k: (v[2:] if k in ("slab", "stone", "chunk", "boulder", "tile") else v) for k, v in dm.items()}
    kinds = kinds_of(M, dm, roof_share=2, pale=4)
    kinds["stone"] = kinds["stone"] + [M["block"]] * 3
    rub_in = rubble_mat(name, base, seed=55, parts=[("#b2ab9b", 1.0), ("#565b4a", 1.0), ("#0d0c0a", 7), ("#22201c", 2)])
    rub_out = rubble_mat(name, base, seed=57, tag="_rubblef",
                         parts=[("#cfc3b1", 4), ("#a89e8e", 1.0), ("#565b4a", 0.7), ("#0f0e0c", 4.6), ("#2e2c27", 1.2)])
    # the balcony's and the eaves' white stone where it fell clear of the soot
    M["white"] = Mat(name + "_white", tex=A.tex_mottle(name + "_white", "#ddd6c6", var=0.06, seed=22), uv=1.0,
                     ref="#ddd6c6")
    # the wreck inside, highest along the back and toward the fallen front, low in the broken back corner
    cl = lambda v: min(1.0, max(0.0, v))  # noqa: E731
    inner = [(-3.7, -1.1), (-0.3, -1.1), (-0.3, -3.3), (3.78, -3.3), (3.78, 5.44), (-3.7, 5.44)]

    def hin(x, y):
        # with a hump at the back's middle, where the drawn wreck stands up between the slabs
        return min(3.3 + 2.4 * cl((y - 1.5) / 3.0) ** 1.2 + 0.9 * cl((-0.5 - y) / 2.0) * cl((x - 0.9) / 1.0)
                   - 3.9 * cl((y - 3.4) / 1.6) * cl((-2.4 - x) / 1.2)
                   + 1.3 * cl((y - 3.6) / 1.2) * math.exp(-((x - 0.4) / 1.5) ** 2), 1.5 + 6.0 * cl((y + 3.3) / 1.0))
    hi_ = field_heap(B, rub_in, inner, hin, cell=0.55, edge=1.0, noise=0.35, seed=56, clamp=True, edges=())
    # the front's east half heaped out over the ground and the lower storey
    fr = [(0.0, -5.6), (2.0, -5.75), (4.9, -5.5), (5.1, -3.0), (4.6, -2.4), (4.375, -2.4), (4.375, -2.5),
          (0.3, -2.5), (0.04, -3.75)]
    hf = field_heap(B, rub_out, fr, lambda x, y: 4.2 * cl((y + 5.75) / 2.4) ** 0.8, cell=0.5, edge=1.0, noise=0.4,
                    seed=57, edges=(0, 1, 2, 3))
    field = A.ground_of(hi_, hf)
    ground = clip(field, inner, fr)
    # the back slope's sections in a jumble, traced over the drawn edges by the classic render's 0.894:
    # the north-west one broken across, its upper part still propped on the back wall, its lower
    # part slid off onto the heap at its own tilt; the north-east one leaning the other way;
    # the middle one fallen flat onto the heap
    nw = plate_z(B, M["tile"], r, [(14, -14), (60, -3), (56, 16), (9, 17)], [(0, 9.0), (1, 8.0), (2, 7.0)],
                 edge=[0], edge_mat=M["trim"], rough=1.5, broken=[1, 3], seed=0)
    zl = z_on(r, 32, 46, field, 0.1)
    plate_z(B, M["tile"], r, [(11, 10), (56, 14), (52, 38), (32, 46), (7, 47)],
            [(0, nw[3].z - 0.04), (1, nw[2].z - 0.2), (3, zl)], edge=[4], edge_mat=M["trim"], rough=1.5,
            broken=[1, 2, 3], seed=1)
    zl = z_on(r, 110, 35, field, 0.12)
    plate_z(B, M["tile"], r, [(100, -8), (126, -12), (134, 26), (110, 35), (98, 15)], [(0, 8.2), (1, 8.9), (3, zl)],
            edge=[0, 1], edge_mat=M["trim"], rough=1.5, broken=[2, 3, 4], seed=2)
    zc, n = settle(r, [(64, 18), (90, 14), (96, 40), (68, 46)], (-0.35, -0.45, 0.82), field, lift=0.06)
    slab_px(B, M["tile"], r, [(64, 18), (90, 14), (96, 40), (68, 46)], zc, n=n, T=0.28, edge=[0], edge_mat=M["trim"],
            ew=0.32, rough=1.5, broken=[1, 2, 3], seed=3)
    # between them charred rafters standing up out of the wreck
    for a, b, up in (((70, 20), (64, -2), 1.0), ((86, 26), (96, 0), 1.2), ((58, 30), (48, 14), 0.7),
                     ((120, 40), (140, 30), 0.5)):
        z0 = z_on(r, a[0], a[1], field, -0.15)
        stick_px(B, M["rafter"], r, a, z0, b, z0 + up, w=0.17)
    for i, (poly, n, edge) in enumerate((
            ([(10, 52), (40, 50), (54, 66), (50, 92), (6, 98)], (0.35, -0.3, 0.88), [4]),
            ([(76, 52), (106, 48), (114, 76), (82, 82)], (0.25, -0.3, 0.9), [0]),
            ([(94, 90), (122, 86), (128, 112), (98, 118)], (-0.2, -0.35, 0.9), [1]),
            ([(36, 64), (62, 60), (66, 86), (40, 92)], (-0.25, -0.35, 0.9), [0]),
            ([(56, 88), (84, 84), (88, 104), (60, 108)], (0.3, -0.3, 0.9), []))):
        zc, n = settle(r, poly, n, field, lift=0.1)
        slab_px(B, M["tile"], r, poly, zc, n=n, T=0.28, edge=edge, edge_mat=M["trim"], ew=0.32, rough=1.5,
                broken=[k for k in range(len(poly)) if k not in edge], seed=10 + i)
    # lengths of the eaves' cornice lying on the heap, charred timbers across it
    for a, b, la, lb in (((112, 120), (128, 104), 0.18, 0.18), ((114, 152), (128, 142), 0.18, 0.18),
                         ((104, 168), (126, 158), 0.18, 0.18), ((40, 74), (60, 70), 0.18, 0.18),
                         ((92, 196), (112, 188), 0.18, 0.18), ((70, 100), (92, 96), 0.18, 0.18)):
        rafter_on(B, M["white"], r, field, a, b, la, lb, w=0.36, ymax=5.3)
    for a, b, la, lb in (((60, 80), (72, 100), 0.2, 0.4), ((86, 130), (96, 150), 0.1, 0.4),
                         ((110, 60), (128, 66), 0.3, 0.1), ((24, 60), (36, 80), 0.4, 0.1)):
        rafter_on(B, M["rafter"], r, field, a, b, la, lb, w=0.16, ymax=5.3)
    # the arcade's round fronts in the heap
    for x, y, yaw, pitch, roll in ((1.6, -4.9, 20, 35, 10), (3.5, -4.5, -30, -25, 15), (2.6, -4.85, 70, 20, -10)):
        fallen_bow(B, M["white"], x, y, max(0.2, hf(x, y) - 0.25), yaw, pitch, roll)
    pl = [A.px(r, c, rw, 2.5) for c, rw in [(0, 0), (154, 0), (154, 256), (0, 256)]]
    A.cover(B, kinds, r, pl, ground, 80, {"slab": 2, "chunk": 5, "stone": 3, "boulder": 1.5, "beam": 1}, pic=pic,
            size=(0.45, 1.0), length=(0.9, 1.6), seed=58, tilt=30, grow=1, stone=(0.3, 0.7), chunk=(0.3, 0.6),
            boulder=(0.5, 1.0), thick=(0.14, 0.24), jumble=0.1, tries=80)
    # chunks, stones and timbers on the fallen slabs and on the roof still standing
    sg = clip(plates_ground, inner)
    A.cover(B, kinds, r, inner, sg, 26, {"chunk": 5, "stone": 2, "beam": 1.5}, pic=pic, length=(0.8, 1.5), seed=59,
            tilt=25, grow=1, stone=(0.25, 0.5), chunk=(0.25, 0.5), jumble=0.06, tries=80)
    # pale lumps among the slabs at the back
    pb = [(-3.7, 2.4), (3.78, 2.4), (3.78, 5.44), (-3.7, 5.44)]
    A.cover(B, {"chunk": [M["block"], M["white"]], "stone": [M["block"]]}, r, pb, clip(field, inner), 15,
            {"chunk": 3, "stone": 2}, pic=pic, seed=61, tilt=25, grow=1, chunk=(0.3, 0.55), stone=(0.3, 0.5), tries=80)
    kp = [g.hit(r, c, rw) for c, rw in [(4, 100), (30, 104), (58, 110), (86, 114), (88, 128), (74, 150), (4, 150)]]
    kp = [(h[0], h[1]) for h in kp if h is not None]
    A.cover(B, kinds, r, kp, lambda x, y: (g.top(x, y) or -100.0) if A.inside_poly(kp, x, y) else -100.0, 14,
            {"chunk": 4, "stone": 3, "beam": 1}, pic=pic, length=(0.8, 1.4), seed=60, tilt=10, grow=1,
            stone=(0.2, 0.4), chunk=(0.2, 0.42), tries=80)
    burns = []
    yi, xi = P["yb"] - P["t"] - 0.15, P["x0"] + P["t"] + 0.15
    for c, side, w, h in (((-0.6, P["yf"], 6.0), "S", 2.0, 3.4), ((-2.0, P["y0"], 3.4), "S", 2.4, 2.4),
                          ((P["x0"], 0.4, 5.4), "W", 3.0, 3.6), ((P["x0"], -2.4, 6.2), "W", 2.4, 2.2),
                          ((-2.0, yi, 5.2), "S", 3.0, 3.0), ((2.4, yi, 5.4), "S", 3.4, 3.2),
                          ((P["x1"], 3.0, 6.0), "E", 3.0, 3.2), ((-1.5, P["yb"], 5.0), "N", 3.4, 3.4)):
        burns.append(soot_patch(B, M["wall"], c, side, w, h, st=0.7, seed=len(burns)))
    obs += B.objects()
    burns += [((-2.6, 5.4, 7.6), (2.4, 1.8, 2.6), 0.6), ((2.8, 5.4, 7.6), (1.8, 1.8, 2.6), 0.55),
              ((0.5, 2.5, 4.5), (5.0, 4.5, 3.5), 0.75), ((-1.6, -1.6, 8.4), 2.6, 0.6), ((2.4, -4.4, 2.0), 3.0, 0.3),
              ((0.0, 3.2, 6.5), (5.0, 3.6, 2.6), 0.7), ((-2.0, -4.4, 4.0), (2.8, 1.2, 3.6), 0.45),
              ((-1.8, -0.6, 7.6), (2.8, 3.2, 2.0), 0.6), ((1.6, 0.6, 5.0), (2.6, 2.6, 2.4), 0.6)]
    export(name, obs, Soot(burns, blotch=0.4, seed=19, low=0.12),
           keep={"_roof": 0.85, "_rubblef": 0.15, "_rubble": 0.7, "_tile": 1.0, "_block": 0.15, "_white": 0.1,
                 "_trim": 0.5})


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = [a for a in argv if not a.startswith("--")] or list(STAGES)
    for n in names:
        t0 = time.time()
        try:
            if n in PLANS:
                build_intact(n)
            else:
                STAGES[n](n)
        except Exception as e:
            traceback.print_exc()
            print("G3_FAILED", n, repr(e), flush=True)
        print("G3_TIME", n, "%.1fs" % (time.time() - t0), flush=True)


if __name__ == "__main__":
    main()
