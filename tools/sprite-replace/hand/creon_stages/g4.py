"""The Creon houses destroyed, CreHouse01a to CreHouse07a, run inside Blender.

No intact Creon house has a model, so each is first modelled here as its
own picture (CreHouse01 to 07) shows it: dark stone walls under brown tile
roofs, gabled or hipped, with their wings, porches, chimneys, doors and
windows. A stage is that house broken in the same frame, so the walls that
still stand sit where the intact ones do. The roof is cut along the classic
camera's line of sight through outlines traced on the stage's picture
(Aramon kit cut_view), so what the picture lacks is what goes. Charred
rafters stick out of the broken edges and hang into the rubble, and the
walls stand to ragged tops all round. The heap fills the house inside
them, banked up to their tops and to the roof's cut edges, its crest no
higher than the picture's outline allows so the classic camera sees its
lit top, and it pours out over low stretches of wall in tongues no
steeper than about 37 degrees, with fallen stones at the foot of the
others. Broken stones, a few tile slabs and many charred timbers lie on
it, most where the classic camera sees least of them so far and about a
quarter where least lies in plan, so its backs and spills are covered
too. These stages are the end of the chain: indestructible and blocking.

Every texture is made here or by the Aramon kit from noise and a few
numbers (okpaint.generated). Soot is the vertex colour each material
multiplies, darkest at broken wall tops and round the holes.

    OK_REPLACE=D:/OKBuild/creon-stages/g4/okr \\
    blender -b --factory-startup --python g4.py -- [Name ...] [--out DIR] [--norender]

Each model goes to <out>/<Name>.glb (out defaults to the parent of
OK_REPLACE) and its classic and turned renders to <out>/renders.
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

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
for p in (os.path.join(TOOLS, "hand", "creon"), TOOLS):
    if p not in sys.path:
        sys.path.insert(0, p)
os.environ.setdefault("OK_REPLACE", "D:/OKBuild/creon-stages/g4/okr")
DATA = os.environ["OK_REPLACE"]

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from mathutils import noise as noise_mod  # noqa: E402

import handkit as hk  # noqa: E402
import kit as ckit  # noqa: E402  the creon kit: Sprite, fit

# the Aramon kit under its own name, both kits being kit.py
_spec = importlib.util.spec_from_file_location("akit", os.path.join(TOOLS, "hand", "aramon_buildings", "kit.py"))
akit = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(akit)
akit.DATA = DATA
akit.CAT.update({r["name"]: r for r in json.load(open(os.path.join(DATA, "catalog.json")))})
Mat = akit.Mat

# The game's camera looks down atan(2) from the ground, so it draws a cell's
# depth and height at TRUE_SQ of the picture's oblique rule (16 px a cell
# north, 8 px a cell up). The houses and their outlines are built under the
# oblique rule (SQ 1), and the stages in DEEPEN are then stretched north and
# south by up to 1 / TRUE_SQ, so the camera draws their depth as the picture
# does while their heights stay put. CreHouse03a's intact stood nearer its
# picture's depth and takes less. CreHouse01a and 05a were fitted unstretched.
TRUE_SQ = 2.0 / math.sqrt(5.0)
SQ = 1.0
DEEPEN = {"CreHouse02a": 1.0 / TRUE_SQ, "CreHouse03a": 1.04, "CreHouse04a": 1.0 / TRUE_SQ,
          "CreHouse06a": 1.0 / TRUE_SQ, "CreHouse07a": 1.0 / TRUE_SQ}


def screen(r, x, y, z):
    """Where the classic camera draws plan point x, y at height z."""
    hx, hy = r["sprite"]["hotspot"]
    return hx + 16.0 * x, hy - SQ * (16.0 * y + 8.0 * z)


def px(r, col, row, z=0.0):
    """The plan point at height z that the classic camera draws at (col, row)."""
    hx, hy = r["sprite"]["hotspot"]
    return (col - hx) / 16.0, ((hy - row) / SQ - 8.0 * z) / 16.0


def cut_view(obs, polys, r, mode="DIFFERENCE", zlo=-2.0, zhi=30.0):
    """Aramon kit cut_view through this camera: a prism along the line of
    sight through each outline on the picture."""
    hx, hy = r["sprite"]["hotspot"]
    bm = bmesh.new()
    for poly in polys:
        n = len(poly)
        lo = [bm.verts.new(((c - hx) / 16.0, (hy - rw) / (16.0 * SQ) - 0.5 * zlo, zlo)) for c, rw in poly]
        hi = [bm.verts.new(((c - hx) / 16.0, (hy - rw) / (16.0 * SQ) - 0.5 * zhi, zhi)) for c, rw in poly]
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
        akit.relay_uv(ob)
    bpy.data.objects.remove(cutter, do_unlink=True)
    return obs


def camera(name):
    """Points the Aramon kit at this file's camera rule and cut."""
    akit.screen, akit.px, akit.cut_view = screen, px, cut_view

# drawn colours, read off the pictures
ROOF = "#5a3c1e"        # the tiles' lit south slopes, #483118 to #563a1d
WALL = "#4a4630"        # dark field stone, #373323 to #564a39
STEP = "#8a8068"        # the pale steps and sills
GLASS = "#2c4436"       # the green window panes
CHAR = "#2a201b"        # charred timber, #1d1916 to #342824
CHAR_HI = "#4d3a2c"     # its warm lit faces
ASH = "#40362b"         # the heaps' bed
CHIP = "#a89a88"        # pale broken stone


# ---------------------------------------------------------------- materials

def tex_char(name, base=CHAR, hi=CHAR_HI, seed=0, size=128):
    """Charred planks: grain along u as the timbers lie, checked cracks
    across it, a few warm lit streaks, never pure black."""
    rng = np.random.default_rng(seed)
    b, h = np.array(akit.rgb(base), float), np.array(akit.rgb(hi), float)
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    rows = ckit.norm01(np.convolve(np.tile(rng.random(size), 3), np.ones(3) / 3, "same")[size:2 * size])
    grain = np.repeat(rows[:, None], size, 1)
    streak = np.clip((akit._smooth(rng, size, 5) - 0.55) * 3, 0, 1)
    checks = np.clip(1 - np.abs(((uu / size) * 14 + 0.4 * akit._smooth(rng, size, 6)) % 1 - 0.5) * 9, 0, 1)
    t = np.clip(0.35 * grain + 0.6 * streak, 0, 1)[..., None]
    col = b * (1 - t) + h * t
    col = col * (1 - 0.55 * checks[..., None]) * (0.9 + 0.2 * rng.random((size, size, 1)))
    return akit._to_image(name, np.clip(col, 0, 255))


def tex_wall(name, base=WALL, seed=0, size=128, rows=9, cols=6):
    """Rough field stone: rounded stones of mixed sizes and tints, wider
    than tall, in loose courses, thin dark joints between, each stone lit
    along its top and shaded under, a little moss and grime."""
    rng = np.random.default_rng(seed)
    b = np.array(akit.rgb(base), float)
    pts = []
    for j in range(rows):
        for i in range(cols):
            pts.append(((i + 0.5 + rng.uniform(-0.35, 0.35) + 0.5 * (j % 2)) / cols * size,
                        (j + 0.5 + rng.uniform(-0.25, 0.25)) / rows * size))
    P = np.array(pts) % size
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    d1 = np.full((size, size), 1e9)
    d2 = np.full((size, size), 1e9)
    own = np.zeros((size, size), int)
    dyv = np.zeros((size, size))
    for k, (px_, py_) in enumerate(P):
        dx = (uu - px_ + size / 2) % size - size / 2
        dy = (vv - py_ + size / 2) % size - size / 2
        d = np.hypot(dx * 0.62, dy)
        closer = d < d1
        d2 = np.where(closer, d1, np.minimum(d2, d))
        d1 = np.where(closer, d, d1)
        own = np.where(closer, k, own)
        dyv = np.where(closer, dy, dyv)
    k = 1 + rng.normal(0, 0.2, len(P))
    tint = np.stack([rng.normal(0, 0.05, len(P)), rng.normal(0, 0.04, len(P)), rng.normal(-0.02, 0.06, len(P))], -1)
    col = b[None, None] * (k[own] [..., None]) * (1 + tint[own])
    gap = d2 - d1
    shade = np.clip(0.72 + 0.09 * gap, 0.72, 1.06) * (1 - 0.012 * dyv)
    col = col * shade[..., None] * (1 + 0.16 * (rng.random((size, size)) - 0.5))[..., None]
    n = akit._smooth(rng, size, 5) * 0.6 + akit._smooth(rng, size, 13) * 0.4
    moss = np.clip((n - 0.66) / 0.15, 0, 1)[..., None]
    col = col * (1 - 0.4 * moss) + np.array(akit.rgb("#3a4226"), float) * 0.4 * moss
    joint = gap < 1.0
    col[joint] = np.array(akit.rgb("#211e17"), float)
    return akit._to_image(name, np.clip(col, 0, 255))


def mats_for(n, seed=1):
    m = {
        "wall": Mat(n + "_wall", tex=tex_wall(n + "_wall", seed=seed), uv=1.2),
        # the gable walls apart, so a stage can cut them alone
        "gable": Mat(n + "_gable", tex=tex_wall(n + "_gable", seed=seed), uv=1.2),
        "pale": Mat(n + "_pale", tex=akit.tex_mottle(n + "_pale", "#6e665a", var=0.2, seed=seed + 7,
                                                     speck="#5a5248", speck_amt=0.1), uv=1.0),
        "top": Mat(n + "_wtop", tex=akit.tex_mottle(n + "_wtop", "#4c463a", var=0.25, seed=seed + 1,
                                                    speck="#8a8070", speck_amt=0.08), uv=1.0),
        "dark": Mat(n + "_dark", "#0e0b08"),
        "glass": Mat(n + "_glass", GLASS, rough=0.3),
        "step": Mat(n + "_step", tex=akit.tex_stone(n + "_step", STEP, var=0.12, rows=3, seed=seed + 2), uv=1.0),
        "frame": Mat(n + "_frame", tex=akit.tex_planks(n + "_frame", "#3a2a1a", boards=2, seed=seed + 3), uv=1.0),
        "char": Mat(n + "_char", tex=tex_char(n + "_char", seed=seed + 4), uv=1.2),
        "ash": Mat(n + "_ash", tex=akit.tex_rubble(n + "_ash", ASH, light="#8a7e6e", dark="#100d0b", seed=seed + 5),
                   uv=2.5),
    }
    return m


def mats_more(n, m, seed=1):
    """The arched doors' wall stones and charred red-brown leaves, and the
    paler weathered wood of the fallen rafter runs."""
    m["rim"] = Mat(n + "_rim", tex=akit.tex_stone(n + "_rim", "#4d463a", var=0.14, rows=3, seed=seed + 8), uv=1.0)
    m["rim2"] = Mat(n + "_rim2", tex=akit.tex_stone(n + "_rim2", "#71675a", var=0.12, rows=3, seed=seed + 9), uv=1.0)
    m["leaf"] = Mat(n + "_leaf", tex=tex_char(n + "_leaf", base="#33241a", hi="#4d3820", seed=seed + 10), uv=1.2)
    m["lit"] = Mat(n + "_lit", tex=tex_char(n + "_lit", base="#4e4030", hi="#7a6446", seed=seed + 11), uv=1.2)
    return m


TINTED = {}  # material name: the flat colour it had, or None


def vertex_tinted(m):
    """Makes material m multiply its base colour by the vertex colour Col.
    Returns the flat colour a material without a picture had, which then
    goes into the vertex colours."""
    if m.name in TINTED:
        return TINTED[m.name]
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    inp = bsdf.inputs["Base Color"]
    flat = None
    if inp.is_linked:
        src = inp.links[0].from_socket
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs[0].default_value = 1.0
        nt.links.new(src, mix.inputs[6])
        nt.links.new(vc.outputs["Color"], mix.inputs[7])
        nt.links.new(mix.outputs[2], inp)
    else:
        flat = tuple(inp.default_value[:3])
        nt.links.new(vc.outputs["Color"], inp)
    TINTED[m.name] = flat
    return flat


def paint(obs, soot):
    """Vertex colours on every part: soot(ob, co) is how much of its colour
    a point keeps (0 to 1)."""
    for ob in obs:
        me = ob.data
        flat = vertex_tinted(me.materials[0]) if len(me.materials) else None
        rgb0 = np.array(flat if flat else (1.0, 1.0, 1.0))
        ca = me.color_attributes.get("Col") or me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
        for i, v in enumerate(me.vertices):
            k = soot(ob, v.co)
            c = np.clip(rgb0 * k, 0, 1)
            ca.data[i].color = (c[0], c[1], c[2], 1.0)


# ---------------------------------------------------------------- roofs

def clip(poly, f):
    """The part of a convex polygon where the linear f(p) <= 0."""
    out = []
    n = len(poly)
    for i in range(n):
        p, q = poly[i], poly[(i + 1) % n]
        fp, fq = f(p), f(q)
        if fp <= 1e-9:
            out.append(p)
        if (fp < -1e-9 and fq > 1e-9) or (fq < -1e-9 and fp > 1e-9):
            t = fp / (fp - fq)
            out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
    ded = []
    for p in out:
        if not ded or math.hypot(p[0] - ded[-1][0], p[1] - ded[-1][1]) > 1e-6:
            ded.append(p)
    if len(ded) > 1 and math.hypot(ded[0][0] - ded[-1][0], ded[0][1] - ded[-1][1]) < 1e-6:
        ded.pop()
    return ded


class Roof:
    """A roof over the eave rectangle x0..x1, y0..y1 (overhangs in), its top
    the lowest of its planes, each rising from one eave side at its own rise
    per cell: k = {"S": .., "N": .., "W": .., "E": ..}. A side left out
    stands as a gable end, and one plane alone is a lean-to. ze is the
    eaves' height, T the shell's thickness."""

    def __init__(self, x0, x1, y0, y1, ze, k, T=0.22):
        self.x0, self.x1, self.y0, self.y1, self.ze, self.k, self.T = x0, x1, y0, y1, ze, dict(k), T

    def lin(self, s):
        """(a, b, c): the distance in from eave side s is a x + b y + c."""
        return {"S": (0.0, 1.0, -self.y0), "N": (0.0, -1.0, self.y1), "W": (1.0, 0.0, -self.x0),
                "E": (-1.0, 0.0, self.x1)}[s]

    def zs(self, s, x, y):
        a, b, c = self.lin(s)
        return self.ze + self.k[s] * (a * x + b * y + c)

    def z(self, x, y):
        return min(self.zs(s, x, y) for s in self.k)

    def plane(self, x, y):
        return min(self.k, key=lambda s: self.zs(s, x, y))

    def inside(self, x, y, grow=0.0):
        return self.x0 - grow <= x <= self.x1 + grow and self.y0 - grow <= y <= self.y1 + grow

    def polys(self):
        rect = [(self.x0, self.y0), (self.x1, self.y0), (self.x1, self.y1), (self.x0, self.y1)]
        out = {}
        for s in self.k:
            P = rect
            for t in self.k:
                if t == s:
                    continue
                (a1, b1, c1), (a2, b2, c2) = self.lin(s), self.lin(t)
                k1, k2 = self.k[s], self.k[t]
                P = clip(P, lambda p, a1=a1, b1=b1, c1=c1, a2=a2, b2=b2, c2=c2, k1=k1, k2=k2:
                         k1 * (a1 * p[0] + b1 * p[1] + c1) - k2 * (a2 * p[0] + b2 * p[1] + c2))
                if len(P) < 3:
                    break
            if len(P) >= 3:
                out[s] = P
        return out

    def polys_all(self):
        return [p for P in self.polys().values() for p in P]

    def build(self, B, mat):
        """The shell as one closed solid: the planes' tops, the same T lower,
        and a fascia round the eave rectangle."""
        verts, faces, top, bot = [], [], {}, {}

        def key(p):
            return (round(p[0], 5), round(p[1], 5))

        def T_(p):
            k = key(p)
            if k not in top:
                top[k] = len(verts)
                verts.append((p[0], p[1], self.z(*p)))
            return top[k]

        def B_(p):
            k = key(p)
            if k not in bot:
                bot[k] = len(verts)
                verts.append((p[0], p[1], self.z(*p) - self.T))
            return bot[k]

        def edge_side(p, q):
            e = 1e-5
            for v, lo in ((0, self.x0), (0, self.x1), (1, self.y0), (1, self.y1)):
                if abs(p[v] - lo) < e and abs(q[v] - lo) < e:
                    return True
            return False

        polys = self.polys()
        # split edges at every corner another plane's polygon puts on them
        pts = {key(p): p for P in polys.values() for p in P}
        for s, P in polys.items():
            ring = []
            for i in range(len(P)):
                p, q = P[i], P[(i + 1) % len(P)]
                ring.append(p)
                d = (q[0] - p[0], q[1] - p[1])
                L2 = d[0] ** 2 + d[1] ** 2
                mids = []
                for kk, r in pts.items():
                    t = ((r[0] - p[0]) * d[0] + (r[1] - p[1]) * d[1]) / max(L2, 1e-12)
                    if 1e-4 < t < 1 - 1e-4:
                        off = abs((r[0] - p[0]) * d[1] - (r[1] - p[1]) * d[0]) / math.sqrt(max(L2, 1e-12))
                        if off < 1e-5:
                            mids.append((t, r))
                ring += [r for _, r in sorted(mids)]
            polys[s] = ring
        for s, P in polys.items():
            faces.append(tuple(T_(p) for p in P))
            faces.append(tuple(B_(p) for p in reversed(P)))
            for i in range(len(P)):
                p, q = P[i], P[(i + 1) % len(P)]
                if edge_side(p, q):
                    faces.append((T_(q), T_(p), B_(p), B_(q)))
        B.solid(mat, verts, faces)


def tex_roof(name, roof, base=ROOF, r=None, holes=(), halo=7.0, dark=1.0, streaks=0.0, seed=1, ppc=44,
             strength=0.9, planes=None):
    """A whole roof's tiles drawn as one picture seen from above: rounded
    tiles in ribs running down each plane, courses lapping, the colour
    mottled. With holes (outlines on the stage picture r) soot haloes each,
    halo pixels wide, and broad streaks of soot cross the rest. planes
    ({side: k}) scales some planes' colour, k under 1 charred through, over
    1 for a slope turned from the light that the picture draws lit.
    Registers the plan mapping under name for Mat(..., uvfn=name)."""
    rng = np.random.default_rng(seed)
    W, D = roof.x1 - roof.x0, roof.y1 - roof.y0
    S = min(float(ppc), 1024.0 / max(W, D))
    nu, nv = max(16, int(W * S)), max(16, int(D * S))
    X = roof.x0 + (np.arange(nu) + 0.5) / nu * W
    Y = roof.y0 + (np.arange(nv) + 0.5) / nv * D
    X, Y = np.meshgrid(X, Y)
    Z = np.full(X.shape, 1e9)
    dist = np.zeros(X.shape)
    along = np.zeros(X.shape)
    plane = np.zeros(X.shape, int)
    for i, s in enumerate(sorted(roof.k)):
        a, b, c = roof.lin(s)
        d = a * X + b * Y + c
        z = roof.ze + roof.k[s] * d
        sel = z < Z
        Z = np.where(sel, z, Z)
        dist = np.where(sel, d * math.sqrt(1 + roof.k[s] ** 2), dist)
        along = np.where(sel, {"S": X, "N": -X, "W": -Y, "E": Y}[s], along)
        plane = np.where(sel, i, plane)
    rib, course = 0.22, 0.42
    ci = np.floor(along / rib)
    fu = along / rib - ci
    ri = np.floor(dist / course)
    fv = dist / course - ri
    h = akit._hash2(ci, ri, plane)
    col = np.array(akit.rgb(base), float)[None, None] * (0.85 + 0.3 * h)[..., None]
    # a rounded tile: lit along its crown, dark in the gutters between
    crown = np.sin(np.pi * fu)
    col = col * (0.45 + 0.65 * crown)[..., None]
    col = col * (1.04 - 0.14 * np.clip((fv - 0.15) / 0.85, 0, 1))[..., None]
    col = col * np.where(fv < 0.06, 0.6, 1.0)[..., None]
    Sq = max(nu, nv)
    blot = (akit._smooth(rng, Sq, 5) * 0.6 + akit._smooth(rng, Sq, 13) * 0.4)[:nv, :nu]
    warm = np.array(akit.rgb("#7a4a22"), float)
    cool = np.array(akit.rgb("#3a3022"), float)
    t = np.clip((blot - 0.5) * 2.2, -1, 1)[..., None]
    col = np.where(t > 0, col * (1 - 0.45 * t) + warm * 0.45 * t, col * (1 + 0.5 * t) - cool * 0.5 * t)
    col = col * (1 + 0.1 * (rng.random((nv, nu)) - 0.5))[..., None] * dark
    for i, s in enumerate(sorted(roof.k)):
        if planes and s in planes:
            col = np.where((plane == i)[..., None], col * planes[s], col)
    soot = np.zeros(X.shape)
    if streaks > 0:
        n1 = (akit._smooth(rng, Sq, 3) * 0.65 + akit._smooth(rng, Sq, 7) * 0.35)[:nv, :nu]
        band = 0.5 + 0.5 * np.cos(2 * math.pi * n1 * 2.5)
        soot = np.maximum(soot, streaks * np.clip((band - 0.55) / 0.3, 0, 1))
    if r is not None and holes:
        C, R = screen(r, X, Y, Z)
        for P in holes:
            d = poly_dist(P, C, R)
            soot = np.maximum(soot, strength * np.clip(1 - d / halo, 0, 1) ** 0.8)
    sc = np.array(akit.rgb("#120e0b"), float)
    s = np.clip(soot, 0, 0.95)[..., None]
    col = col * (1 - s) + sc * s * (0.8 + 0.4 * rng.random((nv, nu, 1)))
    img = akit._to_image(name, np.clip(col, 0, 255))

    def fn(x, y, roof=roof):
        return ((x - roof.x0) / (roof.x1 - roof.x0), (y - roof.y0) / (roof.y1 - roof.y0))
    akit.UVFN[name] = fn
    return Mat(name, tex=img, uvfn=name, ref=base, rough=0.8)


def poly_dist(P, C, R):
    """Distance in pixels from points (C, R) to polygon P, 0 inside."""
    inside = np.zeros(C.shape, bool)
    best = np.full(C.shape, 1e9)
    n = len(P)
    for i in range(n):
        (ax, ay), (bx, by) = P[i], P[i - 1]
        cond = (ay > R) != (by > R)
        xint = (bx - ax) * (R - ay) / np.where(by - ay == 0, 1e-9, by - ay) + ax
        inside ^= cond & (C < xint)
        dx, dy = bx - ax, by - ay
        L2 = max(dx * dx + dy * dy, 1e-9)
        t = np.clip(((C - ax) * dx + (R - ay) * dy) / L2, 0, 1)
        best = np.minimum(best, np.hypot(ax + dx * t - C, ay + dy * t - R))
    return np.where(inside, 0.0, best)


# ---------------------------------------------------------------- bodies

class Body:
    """A block of stone walls x0..x1 by y0..y1, H high, t thick."""

    def __init__(self, x0, x1, y0, y1, H, t=0.32):
        self.x0, self.x1, self.y0, self.y1, self.H, self.t = x0, x1, y0, y1, H, t

    def run(self, side):
        """The wall's centre line, anticlockwise round the block."""
        h, t = self.t / 2, self.t
        # the ends fit between the long walls, so no two faces share a plane
        return {"S": ((self.x0, self.y0 + h), (self.x1, self.y0 + h)),
                "E": ((self.x1 - h, self.y0 + t), (self.x1 - h, self.y1 - t)),
                "N": ((self.x1, self.y1 - h), (self.x0, self.y1 - h)),
                "W": ((self.x0 + h, self.y1 - t), (self.x0 + h, self.y0 + t))}[side]


def ragged(profile, H, L, seed, jitter=0.16, notch=0.3, step=0.28):
    """A wall's top profile [(f, h)] made dense and ragged, stone by stone,
    where it stands below H. The full-height runs stay level."""
    rng = random.Random(seed)
    pf = sorted(profile)
    k = max(2, int(L / step) + 1)
    out = []
    for i in range(k + 1):
        f = i / k
        h = float(np.interp(f, [q[0] for q in pf], [q[1] for q in pf]))
        if h < H - 0.02 and 0 < i < k:
            h += (rng.random() - 0.5) * 2 * jitter
            if rng.random() < notch:
                h -= rng.random() * jitter * 2.2
            h = min(h, H)
        out.append((f, max(0.06, h)))
    return out


def wall_run(B, mat, top_mat, a, b, t, prof):
    """A wall from plan point a to b, t thick, standing to the top profile
    [(f, h)] (f from 0 at a to 1 at b). Level stretches are one face."""
    keep = [prof[0]]
    for i in range(1, len(prof) - 1):
        if not (abs(prof[i][1] - prof[i - 1][1]) < 1e-6 and abs(prof[i][1] - prof[i + 1][1]) < 1e-6):
            keep.append(prof[i])
    keep.append(prof[-1])
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    d = b - a
    L = d.length
    d.normalize()
    nrm = Vector((-d.y, d.x, 0)) * (t / 2)
    k = len(keep)
    V = [tuple(a + d * (f * L) + nrm + Vector((0, 0, h))) for f, h in keep]
    V += [tuple(a + d * (f * L) - nrm + Vector((0, 0, h))) for f, h in keep]
    V += [tuple(a + nrm), tuple(b + nrm), tuple(a - nrm), tuple(b - nrm)]
    a0, a1, b0, b1 = 2 * k, 2 * k + 1, 2 * k + 2, 2 * k + 3
    side_a = [a0] + list(range(k)) + [a1]
    side_b = [b1] + list(range(2 * k - 1, k - 1, -1)) + [b0]
    faces = [tuple(side_a), tuple(side_b), (a0, b0, k, 0), (b1, a1, k - 1, 2 * k - 1), (a0, a1, b1, b0)]
    B.mesh(mat, V, faces)
    B.mesh(top_mat, V, [(i, k + i, k + i + 1, i + 1) for i in range(k - 1)])


def walls(B, m, body, tops=None, seed=0):
    """The four walls, each to its profile in tops ({side: [(f, h)]}, f
    along its run anticlockwise), full height where left out. A side given
    as None is down to the ground."""
    tops = tops or {}
    for i, side in enumerate("SENW"):
        prof = tops.get(side, [(0, body.H), (1, body.H)])
        if prof is None:
            continue
        a, b = body.run(side)
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        wall_run(B, m["wall"], m["top"], a, b, body.t, ragged(prof, body.H, L, seed + i))


def wall_h(body, tops, side, f):
    """How high side's wall stands at fraction f along its run."""
    prof = (tops or {}).get(side, [(0, body.H), (1, body.H)])
    if prof is None:
        return 0.0
    pf = sorted(prof)
    return float(np.interp(f, [q[0] for q in pf], [q[1] for q in pf]))


def gable(B, mat, body, roof, side):
    """The wall under a gable end, from the walls' tops to the roof's
    underside, on side W, E, S or N of the body."""
    t = body.t
    if side in "WE":
        x = body.x0 + t / 2 if side == "W" else body.x1 - t / 2
        ys = sorted({body.y0, body.y1} | {round(p[1], 5) for p in roof.polys_all()
                                          if body.y0 < p[1] < body.y1})
        top = [(y, max(body.H - 0.04, roof.z(x, y) - roof.T - 0.02)) for y in ys]
        pts = [(body.y0, body.H - 0.05), (body.y1, body.H - 0.05)] + list(reversed(top))
        v = [(x - t / 2, a, z) for a, z in pts] + [(x + t / 2, a, z) for a, z in pts]
    else:
        y = body.y0 + t / 2 if side == "S" else body.y1 - t / 2
        xs = sorted({body.x0, body.x1} | {round(p[0], 5) for p in roof.polys_all()
                                          if body.x0 < p[0] < body.x1})
        top = [(x, max(body.H - 0.04, roof.z(x, y) - roof.T - 0.02)) for x in xs]
        pts = [(body.x0, body.H - 0.05), (body.x1, body.H - 0.05)] + list(reversed(top))
        v = [(a, y - t / 2, z) for a, z in pts] + [(a, y + t / 2, z) for a, z in pts]
    if max(z for _, z in top) < body.H + 0.05:
        return
    n = len(pts)
    f = [tuple(range(n)), tuple(range(n, 2 * n))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    B.solid(mat, v, f)


def opening(B, m, body, side, a, b, z0, z1, kind="dark", tops=None):
    """A door or window on a face, a to b along the face's x (S, N) or y
    (W, E): a dark recess (panes if kind is glass) and a timber lintel.
    Where the wall stands lower than its top, it is a gap up to the break."""
    L = (body.x1 - body.x0) if side in "SN" else (body.y1 - body.y0)
    lo = body.x0 if side in "SN" else body.y0
    f0, f1 = (a - lo) / L, (b - lo) / L
    if side in "NW":
        f0, f1 = 1 - f1, 1 - f0
    hmin = min(wall_h(body, tops, side, f) for f in np.linspace(f0, f1, 5))
    lintel = hmin >= z1 + 0.15
    if not lintel:
        if kind == "glass" or hmin - 0.12 - z0 < 0.3:
            return
        z1 = hmin - 0.12
    mat = m["glass" if kind == "glass" else "dark"]
    d = 0.05
    if side in "SN":
        y = body.y0 - d / 2 if side == "S" else body.y1 + d / 2
        yo = y - 0.02 if side == "S" else y + 0.02
        B.box(mat, (a + b) / 2, y, z0, b - a, d, z1 - z0)
        if lintel:
            B.box(m["frame"], (a + b) / 2, yo, z1, b - a + 0.16, d + 0.04, 0.1)
        if kind == "glass":
            B.box(m["frame"], (a + b) / 2, yo, (z0 + z1) / 2 - 0.03, b - a, d + 0.03, 0.06)
            B.box(m["frame"], (a + b) / 2, yo, z0 - 0.06, b - a + 0.1, d + 0.06, 0.06)
    else:
        x = body.x0 - d / 2 if side == "W" else body.x1 + d / 2
        B.box(mat, x, (a + b) / 2, z0, d, b - a, z1 - z0)
        if lintel:
            B.box(m["frame"], x, (a + b) / 2, z1, d + 0.04, b - a + 0.16, 0.1)


def steps(B, mat, x0, x1, y_wall, depth, h, n=3):
    """Stone steps up to a door in a south wall at y_wall."""
    for i in range(n):
        d = depth * (n - i) / n
        B.box(mat, (x0 + x1) / 2, y_wall - d / 2, 0, x1 - x0 - 0.06 * i, d, h * (i + 1) / n)


def chimney(B, mat, x, y, z0, z1, s=0.5, cap=True):
    """A square stone stack, capped unless it is a broken stump."""
    B.box(mat, x, y, z0, s, s, z1 - z0)
    if cap:
        B.box(mat, x, y, z1, s * 1.2, s * 1.2, 0.12)


# ---------------------------------------------------------------- ruin

def jag(P, amp=2.0, step=4.0, seed=0):
    """An outline on the picture made ragged (Aramon kit jag)."""
    return akit.jag(P, amp, step, seed)


def base(ob):
    """An object's name without Blender's .001 suffix."""
    return ob.name.split(".")[0]


def cull_islands(ob, min_top=0.0, min_area=0.0):
    """Deletes the loose bits a cut leaves in ob: islands whose upward faces
    cover less than min_top square cells, or whose whole surface is less
    than min_area."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    seen, gone = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        st, comp = [f], []
        seen.add(f.index)
        while st:
            u = st.pop()
            comp.append(u)
            for e in u.edges:
                for w in e.link_faces:
                    if w.index not in seen:
                        seen.add(w.index)
                        st.append(w)
        area = sum(q.calc_area() for q in comp)
        up = sum(q.calc_area() for q in comp if q.normal.z > 0.3)
        if area < min_area or up < min_top:
            gone += comp
    if gone:
        bmesh.ops.delete(bm, geom=gone, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(ob.data)
    bm.free()
    return len(gone)


def cut(obs, keys, polys, r, cull=None):
    """Cuts the parts whose names end in one of keys along the classic
    camera's sight through outlines on the stage picture. With cull
    (min_top, min_area) the loose bits the cut leaves go too."""
    sel = [o for o in obs if any(base(o).endswith(k) for k in keys)]
    if sel and polys:
        akit.cut_view(sel, polys, r)
        # the cutter leaves an empty slot behind that no face uses
        for o in sel:
            me = o.data
            used = {p.material_index for p in me.polygons}
            while len(me.materials) > 1 and me.materials[-1] is None and len(me.materials) - 1 not in used:
                me.materials.pop(index=len(me.materials) - 1)
            if cull:
                cull_islands(o, *cull)
    return obs


def rafters(B, mat, roof, r, holes, ground, spacing=0.7, w=0.13, seed=0, stub=(0.2, 0.9), hang=0.5, span=1.3,
            skip=0.55, fall=0.2, foot=None):
    """The roof's rafters where the tiles are gone: under each plane, from
    the eaves up its slope. Where a rafter crosses a hole narrower than span
    it stays whole. Otherwise it is snapped a little way in from each edge
    and sags, and some snapped ends hang on down to the rubble (ground). A few
    lie fallen whole from the wall top into the heap, their feet on what
    foot(x, y) says stands there if given."""
    rng = random.Random(seed)
    T = roof.T + w / 2
    hp = [list(h) for h in holes]

    def hole_at(p):
        c, rw = screen(r, p.x, p.y, p.z)
        return any(akit.inside_poly(P, c, rw) for P in hp)

    def droop(a, d, L):
        """A snapped rafter from a along d for L, sagging and twisted aside
        as it goes."""
        k = rng.uniform(0.1, 0.8)
        t = math.radians(rng.uniform(-14, 14))
        dd = Vector((d.x * math.cos(t) - d.y * math.sin(t), d.x * math.sin(t) + d.y * math.cos(t), d.z))
        e = a + dd * L
        e.z -= k * L
        return e
    for s in roof.k:
        a, b, c = roof.lin(s)
        up = Vector((a, b, roof.k[s])).normalized()
        lo, hi = (roof.x0, roof.x1) if s in "SN" else (roof.y0, roof.y1)
        n = max(1, int((hi - lo) / spacing))
        for i in range(n + 1):
            u = lo + 0.12 + (hi - lo - 0.24) * i / n + rng.uniform(-0.06, 0.06)
            eave = {"S": Vector((u, roof.y0, 0)), "N": Vector((u, roof.y1, 0)), "W": Vector((roof.x0, u, 0)),
                    "E": Vector((roof.x1, u, 0))}[s]
            eave.z = roof.zs(s, eave.x, eave.y) - T
            pts = []
            p = eave.copy()
            while roof.inside(p.x, p.y, 1e-6) and roof.plane(p.x, p.y) == s:
                pts.append(p.copy())
                p += up * 0.08
            if len(pts) < 3:
                continue
            ins = [hole_at(q + Vector((0, 0, T))) for q in pts]
            j = 0
            while j < len(pts):
                if not ins[j]:
                    j += 1
                    continue
                k = j
                while k + 1 < len(pts) and ins[k + 1]:
                    k += 1
                A, Bp = pts[j], pts[k]
                length = (Bp - A).length
                j0, k0 = j, k
                j = k + 1
                if length < 0.1:
                    continue
                if j0 == 0 and k0 == len(pts) - 1 and not (ground is not None and rng.random() < fall):
                    continue
                if length <= span and j0 > 0 and k0 < len(pts) - 1:
                    if rng.random() > skip * 0.5:
                        B.beam(mat, tuple(A - up * 0.1), tuple(Bp + up * 0.1), w, w * 1.2)
                    continue
                if ground is not None and j0 == 0 and rng.random() < fall:
                    # fallen whole: its foot still on the eave, its head down in the heap
                    head = A + up * min(length, rng.uniform(2.0, 3.5))
                    gz = ground(head.x, head.y)
                    if 0.05 < gz < head.z - 0.5:
                        head.z = gz + 0.1
                        A = A.copy()
                        if foot is not None:
                            A.z = min(A.z, max(foot(A.x, A.y), ground(A.x, A.y)) + w / 2)
                        if head.z <= A.z + 0.2:
                            B.beam(mat, tuple(A), tuple(head), w, w * 1.2, twist=rng.uniform(-15, 15))
                    continue
                for end, d, edge_in in ((A, up, j0 > 0), (Bp, -up, k0 < len(pts) - 1)):
                    # the back slopes' stubs mostly hidden or gone
                    if not edge_in or rng.random() < (skip if s != "N" else 0.5 + skip / 2):
                        continue
                    L = rng.uniform(*stub)
                    e = droop(end, d, L)
                    # tucked well back under the tiles that still stand
                    B.beam(mat, tuple(end - d * 0.4), tuple(e), w, w * 1.2, twist=rng.uniform(-8, 8))
                    if ground is not None and rng.random() < hang:
                        dd = Vector((d.x, d.y, 0))
                        if dd.length < 1e-6:
                            continue
                        dd.normalize()
                        far = e + dd * rng.uniform(0.8, 1.8)
                        gz = ground(far.x, far.y)
                        if 0.05 < gz < e.z - 0.3:
                            far.z = gz + 0.08
                            B.beam(mat, tuple(e), tuple(far), w, w * 1.2, twist=rng.uniform(-20, 20))


def wall_top_at(h, tops):
    """How high the walls of house h stand at a plan point, 0 off them."""
    def f(x, y):
        best = 0.0
        p = Vector((x, y))
        for key, body in h.bodies.items():
            for side in "SENW":
                a, b = (Vector(q) for q in body.run(side))
                d = b - a
                t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
                if (a + d * t - p).length < body.t / 2 + 0.12:
                    best = max(best, wall_h(body, tops.get(key), side, t))
        return best
    return f


def ridge_fall(B, mat, roof, r, holes, ground, w=0.2, seed=0):
    """The ridge beam where it breaks off into a hole: from the last of it
    under standing tiles it hangs on along the ridge, down to the rubble."""
    rng = random.Random(seed)
    hp = [list(h) for h in holes]
    if len(roof.k) < 2:
        return
    polys = roof.polys()
    # the ridge: the line where two planes meet, sampled along the roof's long way
    along_x = ("S" in roof.k and "N" in roof.k) or not ("W" in roof.k and "E" in roof.k)
    n = 60
    line = []
    for i in range(n + 1):
        t = i / n
        if along_x:
            x = roof.x0 + (roof.x1 - roof.x0) * t
            ys = np.linspace(roof.y0, roof.y1, 81)
            y = float(ys[int(np.argmax([roof.z(x, yy) for yy in ys]))])
        else:
            y = roof.y0 + (roof.y1 - roof.y0) * t
            xs = np.linspace(roof.x0, roof.x1, 81)
            x = float(xs[int(np.argmax([roof.z(xx, y) for xx in xs]))])
        z = roof.z(x, y) - roof.T - w / 2
        line.append(Vector((x, y, z)))
    ins = [any(akit.inside_poly(P, *screen(r, p.x, p.y, p.z + roof.T + w / 2)) for P in hp) for p in line]
    for i in range(1, len(line)):
        for a, b, d in ((i - 1, i, 1), (i, i - 1, -1)):
            if not ins[a] and ins[b]:
                start = line[a]
                k = b
                while 0 <= k + d < len(line) and ins[k + d] and (line[k] - start).length < 3.0:
                    k += d
                end = line[k].copy()
                L = (end - start).length
                if L < 0.4:
                    continue
                L = min(L, rng.uniform(1.2, 2.2))
                end = start + (line[k] - start).normalized() * L
                gz = ground(end.x, end.y) if ground is not None else 0.0
                end.z = max(0.0, gz) + 0.12
                B.beam(mat, tuple(start), tuple(end), w, w * 1.1, twist=rng.uniform(-10, 10))


def lid(h, holes, r):
    """The height a heap may reach at a plan point: under the standing
    tiles of house h's roofs (those the holes, stage outlines per roof, did
    not take), a little below their underside."""
    hp = {k: [list(P) for P in v] for k, v in (holes or {}).items()}

    def f(x, y):
        top = 1e9
        for key, (roof, _, _) in h.roofs.items():
            if not roof.inside(x, y):
                continue
            z = roof.z(x, y)
            if any(akit.inside_poly(P, *screen(r, x, y, z)) for P in hp.get(key, [])):
                continue
            top = min(top, z - roof.T - 0.06)
        return top
    return f


def on_outline(P, q, tol=1e-4):
    """Whether plan point q lies on polygon P's outline."""
    for i in range(len(P)):
        a, b = Vector(P[i]), Vector(P[i - 1])
        d = b - a
        t = max(0.0, min(1.0, (q - a).dot(d) / max(d.length_squared, 1e-12)))
        if (a + d * t - q).length < tol:
            return True
    return False


def scatter(B, dm, r, pic, poly, n, seed=0, kinds=None):
    """Loose stones and bits on the ground where the picture's spray is."""
    flat = lambda x, y: 0.0  # noqa: E731
    akit.cover(B, dm, r, akit.plan_of(r, poly, 0.0), flat, n, kinds or {"stone": 4, "chunk": 3, "beam": 1},
               pic=pic, size=(0.25, 0.45), length=(0.4, 0.9), stone=(0.16, 0.3), chunk=(0.12, 0.22),
               seed=seed, tilt=20, grow=1, tries=200)


# ---------------------------------------------------------------- dense rubble

GREYS = ("#48443a", "#57524a", "#655f52", "#736c5c")  # the drawn stones, lit a little above their mean
PALE = ("#8a8272", "#9a917f")             # the paler stones some heaps hold


def tex_jumble(name, seed=0, size=256, cells=4.0, stones=GREYS, pale=0.0, gap="#17130f", n_per=8, chip=0.015,
               bright=1.0):
    """The heaps' bed drawn as the pictures draw rubble: a dense jumble of
    small broken stones, each lit on top and dark round its foot, charred
    sticks across them with warm lit edges, pale chips, near-black gaps
    between. size px span cells map cells."""
    rng = np.random.default_rng(seed)
    ppc = size / cells
    col = np.zeros((size, size, 3)) + np.array(akit.rgb(gap), float)
    vv, uu = np.mgrid[0:size, 0:size].astype(float)
    tints = [np.array(akit.rgb(c), float) for c in stones + (("#4a4034", "#3e352b") if not pale else PALE)]
    n = int(cells * cells * n_per)
    for _ in range(n):
        cx, cy = rng.uniform(0, size), rng.uniform(0, size)
        rx = rng.uniform(0.1, 0.22) * ppc
        ry = rx * rng.uniform(0.55, 0.95)
        a = rng.uniform(0, math.pi)
        dx = (uu - cx + size / 2) % size - size / 2
        dy = (vv - cy + size / 2) % size - size / 2
        u = (dx * math.cos(a) + dy * math.sin(a)) / rx
        v = (-dx * math.sin(a) + dy * math.cos(a)) / ry
        # a broken outline: the larger of two norms, so the stones have corners
        d = np.maximum(np.abs(u) * 0.8 + np.abs(v) * 0.5, np.hypot(u, v) * 0.9)
        inside = d < 1
        if not inside.any():
            continue
        t = tints[rng.integers(len(tints))] * rng.uniform(0.6, 1.25) * bright
        lit = np.clip(1.0 + 0.5 * (dy / ry) - 0.4 * d, 0.4, 1.5)
        col[inside] = (t[None] * lit[inside][:, None])
    for _ in range(int(cells * cells * 4.5)):
        cx, cy = rng.uniform(0, size), rng.uniform(0, size)
        L = rng.uniform(0.6, 1.4) * ppc
        w = rng.uniform(0.07, 0.13) * ppc
        a = rng.uniform(0, math.pi)
        dx = (uu - cx + size / 2) % size - size / 2
        dy = (vv - cy + size / 2) % size - size / 2
        along = dx * math.cos(a) + dy * math.sin(a)
        across = -dx * math.sin(a) + dy * math.cos(a)
        on = (np.abs(along) < L / 2) & (np.abs(across) < w / 2)
        b = np.array(akit.rgb(("#221a15", "#2c211a", "#1a1512")[rng.integers(3)]), float)
        hi = np.array(akit.rgb("#4d3a2c"), float)
        edge = np.clip(1 - np.abs(across / (w / 2) - 0.55) * 3, 0, 1)
        c = b[None] * (1 - edge[on][:, None] * 0.0) + (hi - b)[None] * edge[on][:, None] * 0.8
        col[on] = c
    chips = rng.random((size, size)) > 1.0 - chip
    col[chips] = np.array(akit.rgb(CHIP), float) * rng.uniform(0.6, 1.0, (int(chips.sum()), 1))
    col *= (0.92 + 0.16 * rng.random((size, size, 1)))
    return akit._to_image(name, np.clip(col, 0, 255))


def bed_mat(n, seed=0, pale=0.0, chip=0.015, cells=4.0, bright=1.0):
    """The heaps' bed, a dense jumble of small stones and charred sticks
    that the larger pieces lie on."""
    return Mat(n + "_bed", tex=tex_jumble(n + "_bed", seed=seed, pale=pale, chip=chip, cells=cells, bright=bright),
               uv=cells)


def spray_mat(n, seed=0):
    """Where the heaps spill out over the walls and spray onto the ground:
    grey ash thick with small stones, paler than the heaps' body, as the
    pictures dither it."""
    return Mat(n + "_spray", tex=tex_jumble(n + "_spray", seed=seed + 1, gap="#3c372f", n_per=10), uv=4.0)


def debris_dense(n, pic, box, seed=0, pale=False):
    """Pieces for the dense heaps: broken stones in the drawn greys and the
    rubble's own colours (and paler ones), tile slabs in the roof's
    colours, charred timbers mostly near black, small chips."""
    cols = akit.palette(pic, k=6, seed=seed, box=box, lit=0.3)
    small = akit.swatches(n + "_ds", cols[1:], "mottle", seed=seed, sat=1.3, gain=1.15)
    grey = [Mat(n + "_db%d" % i, tex=akit.tex_mottle(n + "_db%d" % i, c, var=0.18, seed=seed + 20 + i, speck="#242019",
                                                     speck_amt=0.06), uv=0.9, ref=c) for i, c in enumerate(GREYS)]
    light = [Mat(n + "_dp%d" % i, tex=akit.tex_mottle(n + "_dp%d" % i, c, var=0.16, seed=seed + 30 + i, speck="#4a4438",
                                                      speck_amt=0.08), uv=0.9, ref=c)
             for i, c in enumerate(PALE)] if pale else []
    tiles = [Mat(n + "_dt%d" % i, tex=akit.tex_tiles(n + "_dt%d" % i, c, rows=4, cols=3, var=0.12, seed=seed + 40 + i),
                 uv=0.8, ref=c) for i, c in enumerate(("#4a3018", "#3a2814", "#5a3a1c"))]
    wood = [Mat(n + "_dw%d" % i, tex=tex_char(n + "_dw%d" % i, base=b, hi=h, seed=seed + 60 + i), uv=1.0, ref=b)
            for i, (b, h) in enumerate(((CHAR, CHAR_HI), ("#1e1814", "#3a2c24"), ("#2c2018", "#4a3626")))]
    return {"stone": grey, "pale": light or grey, "slab": tiles, "tile": tiles, "beam": wood,
            "board": wood, "chip": small, "chunk": small}


BOULDERS = ("#57534a", "#635e52", "#6f695a", "#7b7464")  # big grey and pale stones, as lit they draw
TAN = ("#5c5648", "#665e4e", "#6e6554")  # big grey-tan stones


def big_mats(n, cols, seed=0):
    """Mottled stone in each drawn colour, for big boulders."""
    return [Mat(n + "_bg%d" % i, tex=akit.tex_mottle(n + "_bg%d" % i, c, var=0.16, seed=seed + 50 + i, speck="#3a352c",
                                                     speck_amt=0.07), uv=0.9, ref=c) for i, c in enumerate(cols)]


def breast(B, mat, dark, fill, x, y, z, W=2.0, D=1.6, Hb=1.0, flue=0.36, seed=0, gap=(3.4, 4.3), tilt=(14, -12, 7)):
    """A chimney breast fallen whole and broken: a ring of pale stone round
    a ragged round flue, its top broken off at two or three heights, the
    outer edges lower than the inner as if chipped round, and the ring
    broken down low between angles gap (radians), so it lies as a C. The
    flue holds a dark bed with stones (fill) fallen in. Returns the
    centre and height for its soot."""
    rng = random.Random(seed)
    n = 9
    a0 = rng.uniform(0, 2 * math.pi / n)
    angs = [a0 + 2 * math.pi * (i + rng.uniform(-0.15, 0.15)) / n for i in range(n)]

    def outer(a):
        c, s_ = abs(math.cos(a)), abs(math.sin(a))
        return 1.0 / ((c / (W / 2)) ** 3.5 + (s_ / (D / 2)) ** 3.5) ** (1 / 3.5)
    levels = (Hb, Hb * 0.8, Hb * 0.62)
    ro = [outer(a) * rng.uniform(0.84, 1.0) for a in angs]
    ri = [flue * rng.uniform(0.8, 1.2) for _ in angs]
    hi = []
    for a in angs:
        aa = a % (2 * math.pi)
        low = gap[0] <= aa <= gap[1]
        hi.append(Hb * rng.uniform(0.22, 0.32) if low else rng.choice(levels) * rng.uniform(0.92, 1.05))
    ho = [h_ * rng.uniform(0.7, 0.86) for h_ in hi]
    v = []
    for i, a in enumerate(angs):
        c, s_ = math.cos(a), math.sin(a)
        v += [(c * ri[i], s_ * ri[i], 0.0), (c * ro[i], s_ * ro[i], 0.0), (c * ri[i], s_ * ri[i], hi[i]),
              (c * ro[i], s_ * ro[i], ho[i])]
    f = []
    for i in range(n):
        j = (i + 1) % n
        a, b = 4 * i, 4 * j
        f += [(a, b, b + 1, a + 1), (a + 2, a + 3, b + 3, b + 2), (a + 1, b + 1, b + 3, a + 3), (a, a + 2, b + 2, b)]
    with B.at(x, y, z, yaw=tilt[0], pitch=tilt[1], roll=tilt[2]):
        B.solid(mat, v, f)
        B.prism(dark, [(math.cos(a) * flue * 1.05, math.sin(a) * flue * 1.05) for a in angs], 0.0, Hb * 0.35)
        for k in range(3):
            a = rng.uniform(0, 2 * math.pi)
            akit.lump(B, rng.choice(fill), math.cos(a) * flue * 0.4, math.sin(a) * flue * 0.4, Hb * 0.3,
                      flue * 0.9, flue * 0.75, flue * 0.6, seed=rng.randint(0, 99999))
    return Vector((x, y, z)), Hb


def shard(B, mat, x, y, z, sx, sy, sz, seed=0, tilt=22.0):
    """A broken stone: the hull of eight points thrown round a squat
    ellipsoid, so it has a few broad flat faces and sharp edges rather
    than a box's, twelve triangles, standing on z and tipped up to tilt
    degrees."""
    rng = random.Random(seed)
    pts = []
    for zf, n, a0 in ((0.08, 4, 0.0), (0.55, 3, 0.8), (1.0, 1, 0.3)):
        for i in range(n):
            a = 2 * math.pi * (i + a0 + rng.uniform(-0.25, 0.25)) / n
            rr = (0.85 if zf < 0.5 else 1.0 if zf < 0.9 else 0.35) * rng.uniform(0.7, 1.1)
            pts.append(Vector((math.cos(a) * rr * sx / 2, math.sin(a) * rr * sy / 2,
                               zf * sz * rng.uniform(0.8, 1.1))))
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=list(bm.verts))
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.verts.index_update()
    v = [tuple(q.co) for q in bm.verts]
    f = [tuple(q.index for q in face.verts) for face in bm.faces]
    bm.free()
    with B.at(x, y, z, yaw=rng.uniform(0, 360), pitch=rng.uniform(-tilt, tilt), roll=rng.uniform(-tilt, tilt)):
        B.solid(mat, v, f)


def boulder(B, mat, x, y, z, sx, sy, sz, seed=0):
    """A big rounded stone: the smooth shaded hull of points on a squat
    ellipsoid, about twenty triangles, its foot on z."""
    rng = random.Random(seed)
    pts = []
    for zf, n, rr, a0 in ((0.05, 5, 0.8, 0.0), (0.45, 5, 1.0, 0.5), (0.82, 3, 0.62, 0.2), (1.0, 1, 0.0, 0.0)):
        for i in range(n):
            a = 2 * math.pi * (i + a0 + rng.uniform(-0.2, 0.2)) / n
            k = rr * rng.uniform(0.85, 1.08)
            pts.append(Vector((math.cos(a) * k * sx / 2, math.sin(a) * k * sy / 2, zf * sz * rng.uniform(0.9, 1.05))))
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=list(bm.verts))
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.verts.index_update()
    v = [tuple(q.co) for q in bm.verts]
    f = [tuple(q.index for q in face.verts) for face in bm.faces]
    bm.free()
    with B.at(x, y, z, yaw=rng.uniform(0, 360), pitch=rng.uniform(-10, 10), roll=rng.uniform(-10, 10)):
        for face in B.solid(mat, v, f):
            face.smooth = True


def seen_point(r, Ps, ground, rng, zmax=6.0):
    """A point on surface ground(x, y) inside the plan polygons Ps, drawn
    evenly over what the classic camera sees of them rather than over
    their plan, so the steep faces toward the camera get their share.
    None on a miss."""
    cols = [screen(r, x, y, 0.0)[0] for P in Ps for x, y in P]
    rows = [screen(r, x, y, z)[1] for P in Ps for x, y in P for z in (0.0, zmax)]
    c, rw = rng.uniform(min(cols), max(cols)), rng.uniform(min(rows), max(rows))
    z = zmax
    while z > 0.0:
        x, y = px(r, c, rw, z)
        if any(akit.inside_poly(P, x, y) for P in Ps) and ground(x, y) >= z:
            return x, y
        z -= 0.04
    return None


def hidden(g, x, y, z, zmax=7.0):
    """Whether surface g hides plan point (x, y, z) from the classic camera."""
    zz = z + 0.12
    while zz < zmax:
        if g(x, y - 0.5 * (zz - z)) > zz:
            return True
        zz += 0.12
    return False


def rubble(B, dm, r, pic, Ps, ground, seed=0, stones=24, slabs=6, big=2, timbers=20, chips=10,
           stone=(0.3, 0.65), slab=(0.4, 0.7), big_slab=(1.0, 1.5), length=(0.7, 1.4), width=(0.09, 0.15),
           chip=(0.16, 0.3), flank=0.28, fgrow=3, sunk=0.25, stick=0.1, grow=1, tilt=30, kind="stone",
           pale_in=None, open_at=None, rounded=False, wood="beam"):
    """Dense rubble on a heap's surface ground(x, y) over the plan polygons
    Ps, as the pictures draw it: broken stones a little sunk, two or three
    big tile slabs lying tilted across them and smaller ones, many
    charred timbers, chips between. Most pieces go where the classic
    camera sees least rubble so far. The flank share go where least lies
    yet in plan, so the backs and the spills the classic camera does not
    see are covered too. A piece the camera sees stays inside the drawing
    (grow px, fgrow for the flank share). Stones are of dm[kind], or pale
    where pale_in(x, y) says so. With open_at(x, y) no piece goes where it
    says False (under a roof that stands). With rounded the
    stones are big rounded boulders. Timbers are of dm[wood]. Returns the
    surface with the stones on it."""
    rng = random.Random(seed)
    Ps = [list(P) for P in Ps]
    occ = np.zeros((pic.h, pic.w), bool) if pic is not None else None
    yy, xx = (np.mgrid[0:pic.h, 0:pic.w] if pic is not None else (None, None))
    xs = [p[0] for P in Ps for p in P]
    ys = [p[1] for P in Ps for p in P]
    X0, Y0, pc = min(xs) - 0.5, min(ys) - 0.5, 0.15
    pnx, pny = int((max(xs) - X0 + 0.5) / pc) + 2, int((max(ys) - Y0 + 0.5) / pc) + 2
    pocc = np.zeros((pny, pnx), bool)
    pj, pi = np.mgrid[0:pny, 0:pnx]
    rocks = []

    def g2(x, y):
        z = ground(x, y)
        for a, b, s, zb, sz in rocks:
            if abs(x - a) < s and abs(y - b) < s:
                d = math.hypot(x - a, y - b) / (s * 0.42)
                if d < 1:
                    z = max(z, zb + sz * math.sqrt(1 - d * d))
        return z

    def pfrac(x, y, rad):
        i0, i1 = max(0, int((x - X0 - rad) / pc)), min(pnx, int((x - X0 + rad) / pc) + 1)
        j0, j1 = max(0, int((y - Y0 - rad) / pc)), min(pny, int((y - Y0 + rad) / pc) + 1)
        return pocc[j0:j1, i0:i1].mean() if i0 < i1 and j0 < j1 else 1.0

    def mark(x, y, z, rad, x2=None, y2=None, z2=None):
        """Notes the rubble laid round (x, y, z), or along to (x2, y2, z2):
        in plan, and where the classic camera draws it."""
        x2, y2, z2 = (x, y, z) if x2 is None else (x2, y2, z2)
        for grid, a, b, d, k in ((pocc, ((x - X0) / pc, (y - Y0) / pc), ((x2 - X0) / pc, (y2 - Y0) / pc), (pi, pj),
                                  rad / pc),
                                 (occ, screen(r, x, y, z), screen(r, x2, y2, z2), (xx, yy), rad * 9)):
            if grid is None:
                continue
            a, b = np.array(a, float), np.array(b, float)
            e = b - a
            L2 = max(float(e @ e), 1e-9)
            t = np.clip(((d[0] - a[0]) * e[0] + (d[1] - a[1]) * e[1]) / L2, 0, 1)
            grid[np.hypot(a[0] + e[0] * t - d[0], a[1] + e[1] * t - d[1]) <= k] = True

    def spot(rad, fl, k=10):
        """Of k points on the heap, the one with least rubble round it: seen
        by the classic camera, or with fl anywhere in plan."""
        best = None
        for _ in range(k):
            if fl:
                x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
                if not any(akit.inside_poly(P, x, y) for P in Ps) or g2(x, y) < 0.04:
                    continue
                if open_at is not None and not open_at(x, y):
                    continue
                f = pfrac(x, y, rad)
            else:
                pt = seen_point(r, Ps, g2, rng)
                if pt is None:
                    continue
                x, y = pt
                if open_at is not None and not open_at(x, y):
                    continue
                c, rw = screen(r, x, y, g2(x, y))
                rp = rad * 9
                x0, x1 = max(0, int(c - rp)), min(pic.w, int(c + rp) + 1)
                y0, y1 = max(0, int(rw - rp)), min(pic.h, int(rw + rp) + 1)
                f = occ[y0:y1, x0:x1].mean() if x0 < x1 and y0 < y1 else 1.0
            if best is None or f < best[0]:
                best = (f, (x, y))
            if f == 0:
                break
        return best[1] if best else None

    def fits(pts, fl):
        if pic is None:
            return True
        gr = fgrow if fl else grow
        return all(pic.sees(p, gr) or (fl and hidden(g2, p[0], p[1], p[2])) for p in pts)

    def colour(x, y, z):
        return pic.colour(*screen(r, x, y, z), q=0.7) if pic is not None else None

    def slope_frame(x, y, tilt_):
        e = 0.12
        z0 = g2(x, y)
        gx = (g2(x + e, y) - g2(x - e, y)) / (2 * e)
        gy = (g2(x, y + e) - g2(x, y - e)) / (2 * e)
        nrm = Vector((-max(-1.5, min(1.5, gx)), -max(-1.5, min(1.5, gy)), 1.0)).normalized()
        tt = math.tan(math.radians(tilt_))
        nrm = (nrm + Vector((rng.uniform(-1, 1) * tt, rng.uniform(-1, 1) * tt, 0))).normalized()
        a = rng.uniform(0, 2 * math.pi)
        d = Vector((math.cos(a), math.sin(a), 0))
        t1 = (d - nrm * d.dot(nrm)).normalized()
        return z0, nrm, t1, nrm.cross(t1)

    got = dict(stones=0, slabs=0, timbers=0, chips=0)
    # broken stones first, the rest lies on them
    tries = 0
    while got["stones"] < stones and tries < stones * 30:
        tries += 1
        fl = rng.random() < flank
        s = rng.uniform(*stone)
        pt = spot(s * 0.7, fl)
        if pt is None:
            continue
        x, y = pt
        z0 = g2(x, y)
        sx, sy, sz = s, s * rng.uniform(0.6, 1.0), s * rng.uniform(0.35, 0.6)
        rim = [(x + a, y + b, z0 + sz * 0.4) for a, b in ((sx / 2, 0), (-sx / 2, 0), (0, sy / 2), (0, -sy / 2))]
        if not fits(rim, fl):
            continue
        if any(math.hypot(x - a, y - b) < (s + c) * 0.3 for a, b, c, _, _ in rocks):
            continue
        use = "pale" if pale_in is not None and pale_in(x, y) else kind
        mat = rng.choice(dm[use])
        zb = z0 - sz * sunk
        if rounded:
            sz = s * rng.uniform(0.5, 0.7)
            zb = z0 - sz * sunk
            boulder(B, mat, x, y, zb, sx, sy, sz, seed=rng.randint(0, 99999))
        elif s < 0.38:
            akit.lump(B, mat, x, y, zb, sx, sy, sz * 1.2, seed=rng.randint(0, 99999))
        else:
            shard(B, mat, x, y, zb, sx, sy, sz, seed=rng.randint(0, 99999))
        rocks.append((x, y, s, zb, sz))
        mark(x, y, zb + sz * 0.6, s * 0.6)
        got["stones"] += 1

    def lay_slab(n, size, key):
        tries = 0
        while got["slabs"] < n and tries < 400:
            tries += 1
            fl = rng.random() < flank
            w, h = rng.uniform(*size), rng.uniform(*size) * 0.8
            pt = spot(w * 0.6, fl)
            if pt is None:
                continue
            x, y = pt
            z0, nrm, t1, t2 = slope_frame(x, y, tilt)
            c = Vector((x, y, z0)) + nrm * rng.uniform(0.0, 0.08)
            pts = []
            for i in range(4):
                an = 2 * math.pi * (i + rng.uniform(-0.2, 0.2)) / 4
                rr = rng.uniform(0.8, 1.15) * 0.7
                pts.append(c + t1 * (math.cos(an) * w * rr) + t2 * (math.sin(an) * h * rr))
            if min(q.z for q in pts) < -0.12 or not fits(pts, fl):
                continue
            _, mat = akit._pick(rng, dm, {key: 1}, colour(x, y, z0))
            B.slab(mat, [tuple(q) for q in pts], rng.uniform(0.07, 0.11))
            mark(x, y, z0, w * 0.55)
            got["slabs"] += 1
    if big:
        lay_slab(big, big_slab, "slab")
    if slabs:
        lay_slab(big + slabs, slab, "tile")
    tries = 0
    while got["timbers"] < timbers and tries < timbers * 40:
        tries += 1
        fl = rng.random() < flank
        L, w = rng.uniform(*length), rng.uniform(*width)
        pt = spot(0.4, fl)
        if pt is None:
            continue
        x, y = pt
        a = rng.uniform(0, math.pi)
        d = Vector((math.cos(a), math.sin(a), 0)) * (L / 2)
        p0, p1 = Vector((x, y, 0)) - d, Vector((x, y, 0)) + d
        z0, z1, zm = g2(p0.x, p0.y), g2(p1.x, p1.y), g2(x, y)
        if min(z0, z1) < -50 or abs(z1 - z0) > L * 0.8:
            continue
        p0.z, p1.z = z0 + w * 0.25, z1 + w * 0.25
        lift = zm + w * 0.25 - (p0.z + p1.z) / 2
        if lift > w * 0.6:
            continue
        if lift > 0:
            p0.z += lift
            p1.z += lift
        if rng.random() < stick:
            p1.z += rng.uniform(0.25, 0.6)
        if not fits([p0, p1], fl):
            continue
        _, mat = akit._pick(rng, dm, {wood: 1}, colour(x, y, zm))
        B.beam(mat, tuple(p0), tuple(p1), w, w * rng.uniform(0.8, 1.2), twist=rng.uniform(-15, 15))
        mark(p0.x, p0.y, p0.z, w * 1.2, p1.x, p1.y, p1.z)
        got["timbers"] += 1
    tries = 0
    while got["chips"] < chips and tries < chips * 30:
        tries += 1
        fl = rng.random() < flank
        s = rng.uniform(*chip)
        pt = spot(s, fl, k=6)
        if pt is None:
            continue
        x, y = pt
        z0 = g2(x, y)
        if not fits([(x, y, z0 + s * 0.3)], fl):
            continue
        _, mat = akit._pick(rng, dm, {"chip": 1}, colour(x, y, z0))
        akit.lump(B, mat, x, y, z0 - s * 0.2, s, s * rng.uniform(0.7, 1.0), s * rng.uniform(0.45, 0.7),
                  seed=rng.randint(0, 99999))
        mark(x, y, z0, s * 0.6)
        got["chips"] += 1
    if os.environ.get("G4_TRIS"):
        print("STAGE_RUBBLE seed %d" % seed, got)
    return g2


def wall_runs(h, tops):
    """Every standing wall run of house h, with the ragged top walls()
    gives it: (a, b, t, profile) per run, plan points a to b."""
    out = []
    for key, body in h.bodies.items():
        tp = tops.get(key) or {}
        for i, side in enumerate("SENW"):
            prof = tp.get(side, [(0, body.H), (1, body.H)])
            if prof is None:
                continue
            a, b = body.run(side)
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            out.append((Vector(a), Vector(b), body.t, ragged(prof, body.H, L, len(key) * 7 + i), key + side))
    return out


def near_wall(runs, p):
    """The wall run nearest plan point p: (distance to its centre line,
    fraction along it, the run)."""
    best = None
    for run in runs:
        a, b = run[0], run[1]
        d = b - a
        t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
        k = (a + d * t - p).length
        if best is None or k < best[0]:
            best = (k, t, run)
    return best


def stadium(a, b, R, n=6):
    """A plan outline round segment a to b at distance R."""
    d = (b - a)
    d = d.normalized() if d.length > 1e-9 else Vector((1.0, 0.0))
    nrm = Vector((-d.y, d.x))
    out = []
    for c, sgn in ((b, 1), (a, -1)):
        for i in range(n + 1):
            t = -math.pi / 2 + math.pi * i / n
            v = d * (math.cos(t) * sgn) + nrm * (math.sin(t) * sgn)
            out.append(tuple(c + v * R))
    return out


def banked(h, tops, crest, tongues=(), lap=-0.04, slope=0.8, lumps=0.3, seed=0, rise=0.75, free=(), slopes=None,
           ceiling=None):
    """The top of a heap inside house h's walls (standing to tops): crest(x,
    y) made lumpy and brought down toward each standing wall at slope per
    cell to its top, so it banks against the walls without rising over
    them, but where it pours out over them in tongues [(x, y, half)]:
    points on a wall's centre line and half the width poured over. There
    it laps over the wall top and falls away on every side at rise per
    cell to the ground, a lumpy half cone. Returns (top(x, y), the
    tongues' plan outlines). The walls named in free (body key and side,
    as "mW") do not hold the heap down: a gable or roof stands over them.
    slopes gives some walls a steeper rise than slope, as where the heap
    stands up behind a breach. A tongue (x, y, half, over) laps over the
    wall top by over (0.22 if left out). With a fifth item True, over is
    its height outright: a low apron of fallen stones at the wall's foot.
    A sixth item is the tongue's own fall per cell in place of rise.
    ceiling(x, y), if given, caps the lumpy crest, as skyline() does to
    keep the top lit and drawn. A point banks only against the walls of
    the bodies it stands in."""
    slopes = slopes or {}
    runs = wall_runs(h, tops)
    boxes = [(b.x0, b.x1, b.y0, b.y1) for b in h.bodies.values()]
    own = {k: (b.x0 - 0.05, b.x1 + 0.05, b.y0 - 0.05, b.y1 + 0.05) for k, b in h.bodies.items()}
    tg = []
    for tgu in tongues:
        x, y, half = tgu[:3]
        over = tgu[3] if len(tgu) > 3 else 0.22
        flat = len(tgu) > 4 and tgu[4]
        fall = tgu[5] if len(tgu) > 5 else rise
        p = Vector((x, y))
        _, t, (a, b, wt, prof, _) = near_wall(runs, p)
        hw = float(np.interp(t, [q[0] for q in prof], [q[1] for q in prof]))
        d = (b - a).normalized()
        c = a + (b - a) * t
        tg.append((c - d * half, c + d * half, over if flat else hw + over, Vector((d.y, -d.x)), c, fall))

    def lump(x, y, k=0.6):
        return noise_mod.noise(Vector((x * k + seed * 3.1, y * k + seed * 1.7, 0.3)))

    def top(x, y):
        p = Vector((x, y))
        z = 0.0
        if any(x0 <= x <= x1 and y0 <= y <= y1 for x0, x1, y0, y1 in boxes):
            z = crest(x, y) * (1 + lumps * 0.5 * lump(x, y))
            if ceiling is not None:
                z = min(z, ceiling(x, y))
            for a, b, wt, prof, nm in runs:
                bx = own[nm[:-1]]
                if nm in free or not (bx[0] <= x <= bx[1] and bx[2] <= y <= bx[3]):
                    continue
                d = b - a
                t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
                k = (a + d * t - p).length
                if k < 4.0:
                    hw = float(np.interp(t, [q[0] for q in prof], [q[1] for q in prof]))
                    z = min(z, hw + lap + slopes.get(nm, slope) * max(0.0, k - 0.05))
        for a, b, h0, out, c, fall in tg:
            if (p - c).dot(out) < -0.3 and z <= 0.0:
                continue
            d = b - a
            t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
            k = (a + d * t - p).length * (1 + 0.25 * lump(x, y, 1.6))
            zt = (h0 - fall * k) * (1 + lumps * 0.5 * lump(x, y, 1.1))
            z = max(z, zt)
        return z

    def outline(a, b, h0, out, c, fall):
        """The half stadium on the wall's outer side, closed 0.3 inside it."""
        P = []
        for q in stadium(a, b, h0 / fall * 1.3 + 0.15):
            q = Vector(q)
            k = (q - c).dot(out)
            P.append(tuple(q - out * (k + 0.3)) if k < -0.3 else tuple(q))
        return P
    return top, [outline(*t) for t in tg]


def skyline(r, pic, yc, grow=1, margin=0.1, lo=0.0):
    """The height the picture's top outline allows a heap's crest at plan
    line y = yc, as a function (x, y): at yc what the outline allows
    there, in front of it more by the classic camera's ray. A heap whose
    crest runs along yc no higher and drops away steeply behind shows
    the classic camera its lit top right up to the drawn outline, not a
    slope turned from the light."""
    hx, hy = r["sprite"]["hotspot"]
    a = pic.alpha
    if grow:
        g = a.copy()
        for d in range(1, grow + 1):
            g[d:] |= a[:-d]
            g[:, d:] |= a[:, :-d]
            g[:, :-d] |= a[:, d:]
        a = g
    tops = [int(np.where(a[:, c])[0].min()) if a[:, c].any() else pic.h for c in range(pic.w)]

    def f(x, y):
        c = int(round(hx + 16 * x))
        if not 0 <= c < pic.w:
            return lo
        return max(lo, ((hy - tops[c]) / SQ - 16 * yc) / 8.0 - margin) + 2.0 * max(0.0, yc - y)
    return f


def mound(B, mat, polys, height, cell=0.4, seed=0, noise=0.08, r=None, pic=None, under=None, lines=((), ()),
          outer=None, inside=None, sink=False):
    """A heap over the union of plan outlines polys, each given as (outline,
    grow): its top height(x, y), noisy, kept under under(x, y) and, with
    pic, down to where the classic camera sees it inside the drawing
    within grow px (anywhere if grow is None). Its foot is drawn in onto
    the union's outline, so a wall standing there hides the cut. lines
    (xs, ys) adds grid lines, so a steep lip along a wall is drawn in
    full rather than smeared over a cell. Returns its surface, -1e9 off
    it. With outer, the faces whose middle inside(x, y) says is out of the
    house are made of outer instead. With sink, out of the house it ends
    where its top meets the ground, sloping just under it, rather than in
    a film on the ground.
    """
    rng = random.Random(seed)
    Ps = [list(P) for P, _ in polys]
    xs, ys = [p[0] for P in Ps for p in P], [p[1] for P in Ps for p in P]
    x0, x1, y0, y1 = min(xs) - cell, max(xs) + cell, min(ys) - cell, max(ys) + cell
    nx, ny = max(2, int((x1 - x0) / cell) + 1), max(2, int((y1 - y0) / cell) + 1)
    gx, gy = np.linspace(x0, x1, nx + 1), np.linspace(y0, y1, ny + 1)

    def merge(g, extra):
        g = list(g)
        for v in extra:
            if g[0] < v < g[-1] and min(abs(v - q) for q in g) > cell * 0.2:
                g.append(v)
        return np.array(sorted(g))
    gx, gy = merge(gx, lines[0]), merge(gy, lines[1])
    nx, ny = len(gx) - 1, len(gy) - 1
    H = np.full((ny + 1, nx + 1), -0.06)
    sunk = np.zeros((ny + 1, nx + 1), bool)
    for j, y in enumerate(gy):
        for i, x in enumerate(gx):
            grows = [g for (P, g) in polys if akit.inside_poly(P, x, y)]
            if not grows:
                continue
            h = height(x, y)
            h += (rng.random() - 0.5) * 2 * noise * min(1.0, max(0.0, h) / 1.5 + 0.2)
            if under is not None:
                h = min(h, under(x, y))
            gr = None if None in grows else max(grows)
            if pic is not None and gr is not None:
                while h > 0.1 and not pic.solid(*screen(r, x, y, h), grow=gr):
                    h -= 0.1
                if h <= 0.1 and not pic.solid(*screen(r, x, y, 0.0), grow=gr):
                    continue
            if sink and h <= 0.02 and inside is not None and not inside(x, y):
                H[j, i], sunk[j, i] = -0.03, True
                continue
            H[j, i] = max(0.03, h)

    def edge_point(x, y):
        """The nearest point on the union's outline."""
        p, best, bp = Vector((x, y)), 1e9, None
        for k, P in enumerate(Ps):
            for i in range(len(P)):
                a, b = Vector(P[i]), Vector(P[i - 1])
                d = b - a
                t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-9)))
                q = a + d * t
                e = (q - p).length
                if e < best and not any(akit.inside_poly(Q, q.x, q.y) and not on_outline(Q, q)
                                        for m_, Q in enumerate(Ps) if m_ != k):
                    best, bp = e, q
        return bp if bp is not None and best < cell * 1.45 else None
    verts, faces, idx, far = [], [], {}, set()
    for j in range(ny + 1):
        for i in range(nx + 1):
            idx[i, j] = len(verts)
            x, y = gx[i], gy[j]
            if H[j, i] < 0 and not sunk[j, i]:
                near = [1 for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1))
                        if 0 <= i + a <= nx and 0 <= j + b <= ny and H[j + b, i + a] > 0]
                q = edge_point(x, y) if near else None
                if q is not None:
                    x, y = q.x, q.y
                else:
                    far.add(len(verts))
            verts.append((x, y, H[j, i]))
    for j in range(ny):
        for i in range(nx):
            q = [idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]]
            if all(verts[k][2] <= 0 for k in q):
                continue
            for t in ((q[0], q[1], q[2]), (q[0], q[2], q[3])):
                if not far.intersection(t):
                    faces.append(t)
    if outer is not None and inside is not None:
        def mid(t):
            return (sum(verts[k][0] for k in t) / 3, sum(verts[k][1] for k in t) / 3)
        out_f = [t for t in faces if not inside(*mid(t))]
        faces = [t for t in faces if inside(*mid(t))]
        for f in B.mesh(outer, verts, out_f):
            f.smooth = True
    for f in B.mesh(mat, verts, faces):
        f.smooth = True

    def surface(x, y):
        if not (x0 <= x <= x1 and y0 <= y <= y1):
            return -1e9
        i = min(nx - 1, max(0, int(np.searchsorted(gx, x)) - 1))
        j = min(ny - 1, max(0, int(np.searchsorted(gy, y)) - 1))
        u, v = (x - gx[i]) / (gx[i + 1] - gx[i]), (y - gy[j]) / (gy[j + 1] - gy[j])
        z = (H[j, i] * (1 - u) * (1 - v) + H[j, i + 1] * u * (1 - v) + H[j + 1, i] * (1 - u) * v
             + H[j + 1, i + 1] * u * v)
        return z if z > 0.02 else -1e9
    return surface


def heap_in(B, m, dm, r, pic, h, tops, floors, crest, tongues=(), under=None, cell=0.4, seed=0, lumps=0.3,
            tgrow=None, extra=(), slope=0.8, free=(), slopes=None, ceiling=None, lines=((), ()), sink=False,
            **kw):
    """A heap inside house h's walls over the plan outlines floors (drawn to
    the walls' centre lines), its top banked() from crest with tongues
    poured over the walls, dense rubble() on it, a share on the flanks.
    extra: more (outline, grow) parts of its ground. The tongues are kept
    to the drawing within tgrow px (not at all if None). lines (xs, ys)
    adds grid lines, as along a roof's cut edge the heap stands up to.
    With sink its tongues end in the ground without a film round them.
    Returns the surface."""
    top, tpolys = banked(h, tops, crest, tongues, lumps=lumps, seed=seed, slope=slope, free=free, slopes=slopes,
                         ceiling=ceiling)
    polys = [(P, 1) for P in floors] + [(P, tgrow) for P in tpolys] + list(extra)
    # grid lines along each wall's inner face, so the heap's cut edge stays inside the wall, and a little in
    # from those the heap stands up steeply behind
    lx, ly = list(lines[0]), list(lines[1])
    for a, b, wt, prof, nm in wall_runs(h, tops):
        inward = Vector((-(b - a).y, (b - a).x)).normalized()
        ks = (wt / 2, wt / 2 + 0.22) if (slopes or {}).get(nm, 0) >= 2.0 else (wt / 2,)
        if abs(a.y - b.y) < 1e-6:
            ly += [a.y + inward.y * k for k in ks]
        elif abs(a.x - b.x) < 1e-6:
            lx += [a.x + inward.x * k for k in ks]
    boxes = [(b.x0, b.x1, b.y0, b.y1) for b in h.bodies.values()]
    g = mound(B, m["ash"], polys, top, cell=cell, seed=seed, r=r, pic=pic, under=under, lines=(lx, ly),
              outer=m.get("spray"), inside=lambda x, y: any(a <= x <= b and c <= y <= d for a, b, c, d in boxes),
              sink=sink)
    if under is not None:
        kw.setdefault("open_at", lambda x, y: under(x, y) > 1e8)
    return rubble(B, dm, r, pic, [P for P, _ in polys], g, seed=seed + 1, **kw)


def heap_dense(B, m, dm, r, pic, poly, top, z_at=None, cell=0.38, edge=1.0, seed=0, amp=4.0, step=6.0, noise=0.12,
               grow=1, spray=False, **kw):
    """A heap where the picture draws one (outline in its pixels, read at
    z_at), dense rubble on it. With spray its bed is the paler spray's ash."""
    z_at = top * 0.5 if z_at is None else z_at
    P = jag(poly, amp, step, seed)
    h = akit.heap(B, m["spray" if spray and "spray" in m else "ash"], r, P, top, z_at=z_at, cell=cell, noise=noise,
                  seed=seed, edge=edge, pic=pic, grow=grow)
    kw.setdefault("flank", 0.15)
    return rubble(B, dm, r, pic, [akit.plan_of(r, P, z_at)], h, seed=seed + 1, grow=grow, **kw)


def edge_hang(B, dm, mat, roof, r, hole, n, seed=0, drop=(25, 55)):
    """Broken tile slabs and battens hanging from a roof's cut edge into
    the hole (its outline on the stage picture): n of each, spread along
    the edge where the roof still stands."""
    rng = random.Random(seed)
    P = list(hole)

    def on_roof(c, rw):
        z = roof.ze + 1.0
        for _ in range(6):
            x, y = px(r, c, rw, z)
            z = roof.z(x, y)
        x, y = px(r, c, rw, z)
        return Vector((x, y, roof.z(x, y))) if roof.inside(x, y) else None

    def in_hole(p):
        return akit.inside_poly(P, *screen(r, p.x, p.y, p.z))
    cands = []
    for i in range(len(P)):
        a, b = Vector(P[i]), Vector(P[i - 1])
        for k in range(int((b - a).length / 3) + 1):
            q = a + (b - a) * (k / max(1, int((b - a).length / 3)))
            e = on_roof(q.x, q.y)
            if e is None:
                continue
            dirs = []
            for j in range(8):
                an = 2 * math.pi * j / 8
                d = Vector((math.cos(an), math.sin(an), 0.0))
                s = e + d * 0.45
                s.z = roof.z(s.x, s.y)
                o = e - d * 0.45
                o.z = roof.z(o.x, o.y)
                if in_hole(s) and roof.inside(o.x, o.y) and not in_hole(o):
                    dirs.append(d)
            if dirs:
                cands.append((e, dirs))
    rng.shuffle(cands)
    used = []
    k = 0
    for e, dirs in cands:
        if k >= n:
            break
        if any((e - u).length < 0.8 for u in used):
            continue
        used.append(e)
        d = rng.choice(dirs)
        side = Vector((-d.y, d.x, 0.0))
        top = e - Vector((0, 0, roof.T * 0.3))
        L = rng.uniform(0.5, 0.9)
        dn = math.radians(rng.uniform(*drop))
        out = (d * math.cos(dn) - Vector((0, 0, math.sin(dn)))) * L
        w = rng.uniform(0.35, 0.55)
        a0, a1 = top - d * 0.15 - side * w / 2, top - d * 0.15 + side * w / 2
        pts = [a0, a1, a1 + out + side * rng.uniform(-0.1, 0.1), a0 + out * rng.uniform(0.7, 1.0)]
        _, sm = akit._pick(rng, dm, {"tile": 1}, None)
        B.slab(sm, [tuple(q) for q in pts], 0.06)
        # a batten across the break, sticking out into the hole
        bs = top - Vector((0, 0, roof.T * 0.8)) + side * rng.uniform(-0.4, 0.4)
        B.beam(mat, tuple(bs - d * 0.3), tuple(bs + d * rng.uniform(0.5, 0.9) - Vector((0, 0, rng.uniform(0.05, 0.3)))),
               0.07, 0.05, twist=rng.uniform(-10, 10))
        k += 1
    return k


def soot_fn(bodies, hi=0.42, lo=0.85, by_mat=None):
    """How much colour a point keeps: walls darken up toward their broken
    tops (lo at the ground, hi at the top), timbers and rubble stay as
    made, roofs carry their soot in their picture."""
    by_mat = by_mat or {}

    def f(ob, co):
        nm = base(ob)
        for key, k in by_mat.items():
            if nm.endswith(key):
                return k(co) if callable(k) else k
        if nm.endswith(("_wall", "_wtop", "_gable", "_rim", "_rim2")):
            Hm = max(b.H for b in bodies)
            t = min(1.0, max(0.0, co.z / Hm))
            k = lo + (hi - lo) * t ** 1.3
            # blotched along the broken tops, so the soot runs down in streaks
            if co.z > 0.3:
                k *= 0.85 + 0.3 * (0.5 + 0.5 * noise_mod.noise(Vector((co.x * 1.4, co.y * 1.4, co.z * 0.3))))
            return min(1.0, k)
        return 1.0
    return f


# ---------------------------------------------------------------- output

def drop_floaters(ob, reach=0.12, ground=0.05, small=2.5):
    """Deletes the loose pieces of ob, timbers, stones and slabs under small
    cells across, that touch neither the ground nor anything else.
    Returns how many went."""
    from mathutils.bvhtree import BVHTree
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    comp, groups = {}, []
    for f in bm.faces:
        if f.index in comp:
            continue
        st, g = [f], []
        comp[f.index] = len(groups)
        while st:
            u = st.pop()
            g.append(u)
            for v in u.verts:
                for w in v.link_faces:
                    if w.index not in comp:
                        comp[w.index] = len(groups)
                        st.append(w)
        groups.append(g)
    tree = BVHTree.FromBMesh(bm)
    gone = []
    for c, g in enumerate(groups):
        vs = {v for f in g for v in f.verts}
        lo = [min(v.co[i] for v in vs) for i in range(3)]
        hi = [max(v.co[i] for v in vs) for i in range(3)]
        if lo[2] < ground or max(h - l for h, l in zip(hi, lo)) > small or len(g) > 40:
            continue
        if not any(comp[idx] != c for v in vs for (_, _, idx, _) in tree.find_nearest_range(v.co, reach)):
            gone += g
    if gone:
        bmesh.ops.delete(bm, geom=gone, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(ob.data)
    bm.free()
    return len(gone)


def finish(name, obs, out, render=True, scale=2):
    obs = [o for o in obs if o.type == "MESH" and len(o.data.polygons)]
    if os.environ.get("G4_TRIS"):
        for o in sorted(obs, key=lambda o: -sum(len(p.vertices) - 2 for p in o.data.polygons))[:14]:
            print("STAGE_PART", name, base(o), sum(len(p.vertices) - 2 for p in o.data.polygons))
    for o in obs:
        for k in ("uv", "uvfn"):
            if k in o:
                del o[k]
    glb = os.path.join(out, name + ".glb")
    bpy.ops.object.select_all(action="DESELECT")
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    if name in DEEPEN:
        ob.data.transform(Matrix.Scale(DEEPEN[name], 4, (0.0, 1.0, 0.0)))
        ob.data.update()
    k = drop_floaters(ob)
    if k:
        print("STAGE_FLOATERS", name, "faces dropped", k)
    ob = hk.finish([ob], glb, {"feature": name, "family": "creon"})
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print("STAGE_BUILT", name, "tris", tris, "size %.2f x %.2f x %.2f" % tuple(ob.dimensions),
          "zmin %.2f" % min(v.co.z for v in ob.data.vertices), flush=True)
    if render:
        spr = ckit.Sprite(name)
        rd = os.path.join(out, "renders")
        hk.renders(ob, rd, name, spr.path, (spr.hx, spr.hy), scale=scale)
        mt = ckit.fit(spr, os.path.join(rd, name + "_classic.png"))
        if mt:
            print("STAGE_FIT", name, "iou %.3f cover %.3f spill %.3f" % (mt["iou"], mt["cover"], mt["spill"]),
                  "render", np.round(mt["render"] * 255).astype(int), "drawing",
                  np.round(spr.mean() * 255).astype(int), flush=True)
    return tris


# ---------------------------------------------------------------- a house and its ruin

class House:
    """An intact house in the anchor's frame: bodies of stone walls, roofs
    (each with its tiles' drawn colour), gable walls, doors and windows,
    steps, chimneys and the posts of porches."""

    def __init__(self):
        self.bodies, self.roofs = {}, {}
        self.gables, self.openings, self.steps, self.chimneys, self.posts = [], [], [], [], []

    def body(self, key, *a, **k):
        self.bodies[key] = Body(*a, **k)
        return self.bodies[key]

    def roof(self, key, *a, base=ROOF, kind="tiles", **k):
        self.roofs[key] = (Roof(*a, **k), base, kind)
        return self.roofs[key][0]


def build(B, m, n, h, r=None, holes=None, tops=None, gables=None, chimneys=None, roof_dark=0.82, streaks=0.3,
          halo=7.0, wall_holes=None, gable_holes=None, posts=None, cull=False, plane_dark=None, sooty=None):
    """House h as it stands in stage r: each roof cut where holes ({roof:
    [outline]}, stage pixels) say, the walls to their tops ({body: {side:
    profile}}), only the gables listed (all if None) and the chimneys
    given as [(index, top)] (all whole if None). With cull the slivers
    the cuts leave go. plane_dark ({roof: {side: k}}) chars whole planes.
    sooty ({roof: [outline]}) names the holes a roof is sooted round where
    they are not all of its cuts. Returns the parts."""
    holes, tops = holes or {}, tops or {}
    for key, (roof, base_col, kind) in h.roofs.items():
        nm = "%s_r%s" % (n, key)
        if kind == "planks":
            m[nm] = Mat(nm, tex=akit.tex_planks(nm, base_col, boards=6, seed=len(key), vertical=True), uv=1.4)
        else:
            # each roof sooted round its own holes only, as another roof's hole may overlap it on the picture
            soot_at = (sooty or {}).get(key, holes.get(key, []))
            m[nm] = tex_roof(nm, roof, akit.saturate(base_col, 1.35), r=r, holes=soot_at if r else (),
                             halo=halo, dark=roof_dark if r else 1.0, streaks=streaks if r else 0.0, seed=len(key) + 3,
                             planes=(plane_dark or {}).get(key))
        roof.build(B, m[nm])
    for key, body in h.bodies.items():
        walls(B, m, body, tops.get(key), seed=len(key) * 7)
    for bk, rk, side in h.gables:
        if gables is None or (bk, rk, side) in gables:
            gable(B, m["gable"], h.bodies[bk], h.roofs[rk][0], side)
    for bk, side, a, b, z0, z1, kind in h.openings:
        opening(B, m, h.bodies[bk], side, a, b, z0, z1, kind=kind, tops=tops.get(bk))
    for x0, x1, yw, d, hh in h.steps:
        steps(B, m["step"], x0, x1, yw, d, hh)
    for i, (x, y, z0, z1, sz) in enumerate(h.chimneys):
        top = z1 if chimneys is None else dict(chimneys).get(i)
        if top is not None:
            chimney(B, m["wall"], x, y, z0 if top >= z1 else 0.0, top, sz, cap=top >= z1)
    for i, (x, y, hh, sz) in enumerate(h.posts):
        if posts is None or i in posts:
            B.box(m["frame"], x, y, 0, sz, sz, hh)
    obs = B.objects()
    for key, P in holes.items():
        cut(obs, ("_r" + key,), P, r, cull=(0.3, 0.15) if cull else None)
    if wall_holes:
        cut(obs, ("_wall", "_wtop", "_dark", "_glass", "_frame"), wall_holes, r, cull=(0.0, 0.1) if cull else None)
    if gable_holes:
        cut(obs, ("_gable",), gable_holes, r, cull=(0.0, 0.2) if cull else None)
    return obs


def arch_door(B, m, body, a, b, top, spring, leaf=0.45, swing=55.0, leaves=1, pale=(2, 5), floor=True):
    """A round-headed doorway on body's south face from x a to b: a dark
    opening whose head is half an octagon reaching top, a rim of the wall's
    own dark stones round it with the few in pale paler, the charred floor
    running in low across it (unless floor is False), and charred
    red-brown leaves (none, one hinged on the east jamb, or two), leaf of
    the width each, swung a little open toward the camera. Needs
    mats_more."""
    y = body.y0
    cx, R = (a + b) / 2, (b - a) / 2
    k = (top - spring) / R

    def outline(grow):
        pts = [(a - grow, 0.0), (b + grow, 0.0)]
        for i in range(5):
            t = math.pi * i / 4
            pts.append((cx + (R + grow) * math.cos(t), spring + (R + grow) * k * math.sin(t)))
        return pts
    inner, outer = outline(0.0), outline(0.16)

    def plate(mat, pts, y0, y1):
        n = len(pts)
        v = [(x, y0, z) for x, z in pts] + [(x, y1, z) for x, z in pts]
        f = [tuple(range(n)), tuple(range(n, 2 * n))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
        B.solid(mat, v, f)
    plate(m["dark"], inner, y - 0.05, y + 0.01)
    if floor:
        B.box(m["leaf"], cx, y - 0.07, 0.0, b - a, 0.04, 0.22)
    # the rim, stone by stone from one jamb over the head to the other
    ring_in, ring_out = inner[1:] + [inner[0]], outer[1:] + [outer[0]]
    for i in range(len(ring_in) - 1):
        plate(m["rim2" if i in pale else "rim"], [ring_in[i], ring_out[i], ring_out[i + 1], ring_in[i + 1]],
              y - 0.04, y + 0.01)
    W = (b - a) * leaf
    hinges = [(b - 0.04, swing, -1), (a + 0.04, -swing, 1)][:leaves]
    for hx, yaw, sgn in hinges:
        with B.at(hx, y - 0.08, 0.0, yaw=yaw):
            B.box(m["leaf"], sgn * W / 2, -0.04, 0.0, W, 0.08, spring + R * k * (0.55 if leaves == 1 else 0.3))


def ladder(B, mat, r, g, a, b, width=0.8, rail=0.17, rungs=5, seed=0):
    """A fallen run of rafters still nailed to their battens, lying on
    surface g from picture point a to b (each read at the height g has
    there): two rails and the rungs across them."""
    rng = random.Random(seed)

    def at(c, rw):
        z = 1.0
        for _ in range(4):
            x, y = px(r, c, rw, z)
            z = max(0.0, g(x, y))
        return Vector((x, y, z + rail * 0.3))

    def on(p):
        return Vector((p.x, p.y, max(0.0, g(p.x, p.y)) + rail * 0.3))
    p0, p1 = at(*a), at(*b)
    d = p1 - p0
    side = Vector((-d.y, d.x, 0)).normalized() * (width / 2)
    rails = [(on(p0 + side * sgn), on(p1 + side * sgn)) for sgn in (1, -1)]
    for e0, e1 in rails:
        B.beam(mat, tuple(e0), tuple(e1), rail, rail * 1.15, twist=rng.uniform(-6, 6))
    for i in range(rungs):
        t = (i + 0.5) / rungs + rng.uniform(-0.04, 0.04)
        ca, cb = (e0 + (e1 - e0) * t + Vector((0, 0, rail * 0.55)) for e0, e1 in rails)
        B.beam(mat, tuple(ca + (ca - cb) * 0.1), tuple(cb + (cb - ca) * 0.1), rail * 0.6, rail * 0.5,
               twist=rng.uniform(-10, 10))


# ---------------------------------------------------------------- the houses

def house01():
    """CreHouse01: a small house, a gable roof with its ridge east and west,
    a chimney on the back slope at the west end, the door and its steps in
    the middle of the front between two windows."""
    h = House()
    h.body("m", -2.72, 2.78, -1.5, 1.75, 3.0)
    h.roof("m", -2.87, 2.94, -1.68, 1.98, 3.1, {"S": 0.585, "N": 0.585})
    h.gables = [("m", "m", "W"), ("m", "m", "E")]
    h.openings = [("m", "S", -0.44, 0.25, 0.0, 1.25, "door"), ("m", "S", -1.75, -1.0, 1.9, 2.5, "glass"),
                  ("m", "S", 0.875, 1.625, 1.9, 2.5, "glass"), ("m", "S", -2.0, -1.6, 0.6, 1.1, "dark")]
    h.steps = [(-0.5, 0.44, -1.5, 0.6, 0.4)]
    h.chimneys = [(-2.16, 1.2, 2.6, 4.3, 0.5)]
    return h


def house02():
    """CreHouse02, the long house: a two-storey block on the east under a
    gable roof whose ridge runs east and west, a chimney on its front
    slope, and a lower wing on the west, hipped at its far end, with a wide
    cart door in its front."""
    h = House()
    h.body("m", -1.0, 4.62, -1.875, 1.39, 3.05)
    h.roof("m", -1.12, 4.75, -2.05, 1.57, 3.23, {"S": 0.66, "N": 0.66})
    h.body("w", -4.62, -1.0, -1.875, 1.39, 2.0)
    h.roof("w", -4.75, -1.0, -2.05, 1.57, 2.1, {"S": 0.66, "N": 0.66, "W": 0.66})
    h.gables = [("m", "m", "W"), ("m", "m", "E")]
    h.openings = [("m", "S", 0.125, 0.875, 2.0, 2.6, "glass"), ("m", "S", 2.875, 3.625, 2.0, 2.6, "glass"),
                  ("m", "S", 2.875, 3.375, 0.6, 1.1, "dark"), ("m", "S", 0.6, 1.3, 0.0, 1.3, "door"),
                  ("w", "S", -3.8, -1.6, 0.0, 1.55, "dark")]
    h.chimneys = [(4.1, -1.3, 2.8, 4.3, 0.55)]
    return h


def house03():
    """CreHouse03, the Stable: a long low block whose very flat roof has its
    ridge running back from the front, off centre to the east, so the
    front wall is a broad gable with two arched doors, and a chimney on
    the long west slope."""
    h = House()
    h.body("m", -4.94, 4.88, -2.5, 2.78, 2.3)
    h.roof("m", -5.06, 4.94, -2.61, 2.89, 2.35, {"W": 0.216, "E": 0.49})
    h.gables = [("m", "m", "S"), ("m", "m", "N")]
    h.openings = [("m", "S", -3.56, -2.81, 0.0, 1.2, "door"), ("m", "S", 2.06, 4.1, 0.0, 1.45, "door"),
                  ("m", "S", -2.6, -2.2, 1.4, 1.8, "glass"), ("m", "S", -0.12, 0.31, 1.4, 1.8, "glass"),
                  ("m", "S", 1.12, 1.56, 1.6, 2.0, "glass")]
    h.chimneys = [(-1.2, 1.8, 2.0, 3.9, 0.55)]
    return h


def house04():
    """CreHouse04, the Manor: a tall block under a hipped roof whose ridge
    runs back from the front, a chimney at its back east corner, and a low
    wing across its front under a nearly flat lean-to roof."""
    h = House()
    h.body("m", -4.5, 4.7, -1.74, 6.4, 3.3)
    h.roof("m", -4.83, 4.94, -2.04, 6.71, 3.2, {"S": 1.02, "N": 0.83, "W": 0.39, "E": 0.53})
    h.body("w", -3.56, 3.69, -6.0, -1.74, 1.0)
    h.roof("w", -3.7, 3.82, -6.15, -1.74, 1.0, {"S": 0.146})
    h.body("a", -4.75, -3.56, -3.2, -1.74, 1.4)
    h.roof("a", -4.85, -3.56, -3.3, -1.74, 1.4, {"W": 0.3})
    h.gables = [("w", "w", "W"), ("w", "w", "E"), ("a", "a", "S"), ("a", "a", "N")]
    h.openings = [("m", "S", -0.44, 0.06, 2.2, 2.8, "glass"), ("m", "S", 0.94, 1.44, 2.2, 2.8, "glass"),
                  ("w", "S", -1.06, -0.69, 0.2, 0.7, "dark"), ("w", "S", 1.0, 1.31, 0.2, 0.7, "dark"),
                  ("a", "W", -2.9, -2.1, 0.3, 1.0, "dark")]
    h.chimneys = [(4.1, 5.5, 3.0, 4.9, 0.6)]
    return h


def house05():
    """CreHouse05, the small house: narrow and deep, its ridge running back
    from a gabled front, a window over the front wall and a chimney on
    the east slope near the back."""
    h = House()
    h.body("m", -1.94, 1.88, -2.63, 2.6, 3.1)
    h.roof("m", -2.0, 1.94, -2.78, 2.62, 3.1, {"W": 0.515, "E": 0.5})
    h.gables = [("m", "m", "S"), ("m", "m", "N")]
    h.openings = [("m", "S", -0.25, 0.375, 2.0, 2.6, "glass"), ("m", "S", -1.4, -0.7, 0.0, 1.3, "door")]
    h.chimneys = [(1.375, 2.1, 2.5, 4.0, 0.55)]
    return h


def house06():
    """CreHouse06, the Villa: a great block under a tall hipped roof, a
    galleried front under a lean-to with an arcade of six arches, a small
    porch before it, a chimney at the back west corner, and a darker east
    wing with its ridge running back."""
    h = House()
    h.body("m", -6.3, 2.6, -2.2, 5.3, 4.0)
    h.roof("m", -6.5, 2.75, -2.44, 5.56, 4.0, {"S": 1.0, "N": 0.75, "W": 0.75, "E": 0.75})
    h.body("g", -6.3, 2.6, -3.69, -2.2, 2.3)
    h.roof("g", -6.45, 2.75, -3.85, -2.2, 2.25, {"S": 0.86})
    h.roof("p", -3.5, -0.4, -5.3, -3.69, 1.1, {"S": 0.35})
    h.body("e", 2.75, 6.2, -1.2, 2.5, 2.6)
    h.roof("e", 2.65, 6.3, -1.4, 2.7, 2.8, {"W": 0.55, "E": 0.95}, base="#3a2614")
    h.gables = [("g", "g", "W"), ("g", "g", "E"), ("e", "e", "S"), ("e", "e", "N")]
    for c in (12, 35, 58, 81, 104, 127):
        h.openings.append(("g", "S", (c - 106) / 16.0, (c + 15 - 106) / 16.0, 0.9, 1.9, "dark"))
    h.openings.append(("e", "S", 3.3, 5.4, 0.0, 1.7, "dark"))
    h.posts = [(-3.3, -5.2, 1.1, 0.2), (-0.6, -5.2, 1.1, 0.2)]
    h.chimneys = [(-5.6, 4.9, 3.5, 5.2, 0.7)]
    return h


def house07():
    """CreHouse07: a house whose ridge runs back from a gabled front, a
    chimney on the west slope, the door and its steps at the west end of
    the front, and a plank lean-to on posts before its east half."""
    h = House()
    h.body("m", -3.06, 2.75, -2.125, 3.25, 3.4)
    h.roof("m", -3.19, 2.88, -2.28, 3.5, 3.56, {"W": 0.32, "E": 0.34})
    h.roof("p", 0.125, 2.88, -3.35, -2.125, 1.1, {"S": 0.82}, base="#5a3a20", kind="planks")
    h.gables = [("m", "m", "S"), ("m", "m", "N")]
    h.openings = [("m", "S", -2.06, -1.31, 0.0, 1.4, "door"), ("m", "S", -2.25, -1.8, 2.4, 2.9, "glass"),
                  ("m", "S", -0.56, -0.12, 2.4, 2.9, "glass"), ("m", "S", 0.5, 0.9, 2.4, 2.9, "glass")]
    h.steps = [(-2.12, -1.25, -2.125, 0.6, 0.45)]
    h.posts = [(0.3, -3.22, 1.0, 0.18), (2.7, -3.22, 1.0, 0.18)]
    h.chimneys = [(-2.5, -1.0, 3.0, 4.55, 0.45)]
    return h


def stage01a(n, r, B, m, pic):
    """CreHouse01a, the Ruined House: the roof's west half fallen into a
    dense heap of stones and charred timbers, banked inside the broken back
    and west walls and up to the roof's ragged edge, pouring out west over
    the low west wall into the spray. The east end stands on its walls,
    the front wall is a stub west of the door and broken in round the
    east window."""
    h = house01()
    hole = jag([(-6, -60), (49, -60), (49, -6), (51, 6), (58, 11), (68, 15), (78, 17), (85, 24), (86, 34), (80, 43),
                (71, 48), (63, 54), (59, 64), (-6, 66)], 5.5, 7.0, 1)
    tops = {"m": {"S": [(0, 1.2), (0.12, 1.55), (0.25, 1.75), (0.36, 1.45), (0.44, 1.7), (0.48, 2.2), (0.5, 3.0),
                        (1, 3.0)],
                  "N": [(0, 3.0), (0.62, 3.0), (0.67, 2.3), (0.74, 1.95), (0.84, 1.55), (0.9, 1.85), (1, 1.45)],
                  "W": [(0, 1.5), (0.2, 1.75), (0.3, 1.3), (0.36, 0.7), (0.47, 0.75), (0.55, 1.2),
                        (0.8, 1.5), (1, 1.25)]}}
    # the east window broken out to a dark gap, its timbers hanging out
    h.openings = [o if o[2] != 0.875 else ("m", "S", 0.8, 1.7, 1.5, 2.6, "dark") for o in h.openings]
    obs = build(B, m, n, h, r, holes={"m": [hole]}, tops=tops, gables=[("m", "m", "E")], chimneys=[(0, 1.9)],
                halo=5.0, cull=True)
    body = h.bodies["m"]
    roof = h.roofs["m"][0]
    y = body.y0
    B.beam(m["char"], (0.95, y + 0.3, 2.45), (1.25, y - 0.5, 1.85), 0.12, 0.14, twist=8)
    B.beam(m["char"], (1.5, y + 0.25, 1.62), (1.75, y - 0.75, 0.3), 0.13, 0.15, twist=-12)
    B.box(m["char"], 1.25, y - 0.02, 1.48, 1.0, 0.12, 0.08)
    m["ash"] = bed_mat(n, 7)
    m["spray"] = spray_mat(n, 7)
    dm = debris_dense(n, pic, (8, 4, 90, 84), seed=7)
    lids = lid(h, {"m": [hole]}, r)
    t2 = body.t / 2
    floor = [(body.x0 + t2, body.y0 + t2), (1.9, body.y0 + t2), (1.9, body.y1 - t2), (body.x0 + t2, body.y1 - t2)]
    sky = skyline(r, pic, body.y1 - 0.55)

    # highest toward the roof's broken edge, so the rubble reaches the tiles, lower at the west end
    def crest(x, y):
        e = min(1.0, max(0.0, (x + 2.4) / 2.8))
        t = min(1.0, max(0.0, (y - body.y0) / (body.y1 - body.y0)))
        return 2.3 + 1.5 * e + 0.4 * math.sin(math.pi * t)
    g1 = heap_in(B, m, dm, r, pic, h, tops, [floor], crest,
                 tongues=[(body.x0 + t2, 0.3, 0.3), (-1.6, body.y1 - t2, 1.0, 0.4, True)], under=lids, cell=0.4,
                 seed=21, tgrow=3, slopes={"mN": 2.6}, ceiling=sky, stones=54, big=1, slabs=5, timbers=24, chips=12,
                 stone=(0.22, 0.55))
    # the spray west of the house, low, grey stones on the ground
    g2 = heap_dense(B, m, dm, r, pic, [(0, 24), (20, 18), (22, 40), (21, 70), (8, 72), (0, 56)], 0.3, z_at=0.15,
                    edge=0.5, seed=23, spray=True, stones=6, big=0, slabs=0, timbers=3, chips=8, stone=(0.25, 0.45))
    # what fell out through the broken window
    g3 = heap_dense(B, m, dm, r, pic, [(76, 70), (96, 70), (98, 80), (90, 86), (78, 84)], 0.5, z_at=0.25, edge=0.4,
                    seed=25, stones=3, big=0, slabs=1, timbers=3, chips=4, stone=(0.25, 0.45), slab=(0.3, 0.5))
    g = akit.ground_of(g1, g2, g3)
    rafters(B, m["char"], roof, r, [hole], g, seed=31, skip=0.35, foot=wall_top_at(h, tops))
    ridge_fall(B, m["char"], roof, r, [hole], g, seed=32)
    edge_hang(B, dm, m["char"], roof, r, hole, 5, seed=33)
    scatter(B, dm, r, pic, [(0, 12), (14, 12), (14, 66), (0, 66)], 10, seed=41)
    scatter(B, dm, r, pic, [(22, 80), (52, 80), (52, 90), (22, 90)], 5, seed=42)
    return obs, h


def stage02a(n, r, B, m, pic):
    """CreHouse02a, the Sacked House: two dense heaps of charred timber and
    stone, one in each half, banked inside the broken back walls and
    lapping over them, fallen stones at their feet, the main block's
    pouring out over its broken front. The roof's ragged edges are left
    at both ends and along the east front, and the wing keeps its whole
    front eave. The wing's heap banks up against the main block's west
    gable, lit stones on it. The wing's arched cart door stands in the
    wall's own dark stones, its charred red-brown leaf a little open and
    fallen timbers before it."""
    h = house02()
    # the wing's cart door is built as an arch below
    h.openings = [o for o in h.openings if o[0] != "w"]
    mats_more(n, m, 41)
    hole_w = jag([(19, 2), (64, 2), (64, 53), (50, 55), (34, 52), (20, 54), (16, 42), (20, 30)], 4.0, 6.0, 2)
    hole_m = jag([(70, 14), (90, 12), (92, -4), (92, -60), (156, -60), (156, 36), (134, 38), (116, 36), (104, 38),
                  (98, 48), (96, 64), (70, 64)], 5.0, 7.0, 3)
    tops = {"m": {"S": [(0, 3.05), (0.08, 3.05), (0.12, 1.8), (0.25, 1.2), (0.38, 1.0), (0.46, 1.6), (0.5, 3.05),
                        (1, 3.05)],
                  "N": [(0, 2.9), (0.2, 2.5), (0.36, 2.6), (0.45, 2.1), (0.55, 2.2), (0.6, 2.4), (0.75, 2.8),
                        (0.88, 3.05), (1, 3.05)]},
            "w": {"S": [(0, 2.0), (0.3, 2.0), (0.42, 1.6), (0.6, 1.75), (0.72, 2.0), (1, 2.0)],
                  "N": [(0, 2.0), (0.22, 1.8), (0.32, 1.6), (0.42, 1.7), (0.5, 1.85), (0.7, 1.9), (1, 2.0)],
                  "E": None}}
    obs = build(B, m, n, h, r, holes={"m": [hole_m], "w": [hole_w]}, tops=tops, gables=[("m", "m", "W")],
                chimneys=[], halo=5.0, cull=True)
    bm, bw = h.bodies["m"], h.bodies["w"]
    arch_door(B, m, bw, -3.8, -1.6, 1.5, 0.95, leaf=0.55, swing=15)
    m["ash"] = bed_mat(n, 8)
    m["spray"] = spray_mat(n, 8)
    dm = debris_dense(n, pic, (10, 2, 152, 92), seed=8)
    lids = lid(h, {"m": [hole_m], "w": [hole_w]}, r)
    t2 = bw.t / 2
    floor_w = [(bw.x0 + t2, bw.y0 + t2), (bm.x0 + t2, bw.y0 + t2), (bm.x0 + t2, bw.y1 - t2), (bw.x0 + t2, bw.y1 - t2)]
    floor_m = [(bm.x0 + t2, bm.y0 + t2), (bm.x1 - t2, bm.y0 + t2), (bm.x1 - t2, bm.y1 - t2), (bm.x0 + t2, bm.y1 - t2)]

    sky_w, sky_m = skyline(r, pic, bw.y1 - 0.55), skyline(r, pic, bm.y1 - 0.55)

    # banked up against the main block's west gable
    def crest_w(x, y):
        t = min(1.0, max(0.0, (y - bw.y0) / (bw.y1 - bw.y0)))
        return 2.8 + 0.3 * t + 1.7 * max(0.0, 1.0 - (bm.x0 - x) / 0.8)

    def crest_m(x, y):
        t = min(1.0, max(0.0, (y - bm.y0) / (bm.y1 - bm.y0)))
        u = min(1.0, max(0.0, (x - bm.x0) / (bm.x1 - bm.x0)))
        return 3.4 + 0.3 * t + 0.2 * math.sin(math.pi * u)
    g1 = heap_in(B, m, dm, r, pic, h, tops, [floor_w], crest_w, tongues=[(-2.6, bw.y1 - t2, 1.0, 0.45, True)],
                 under=lids, cell=0.5, seed=22, tgrow=3, free=("mW",), slopes={"wN": 2.6, "wS": 2.2}, ceiling=sky_w,
                 sink=True,
                 stones=18,
                 big=2, slabs=4, timbers=18, chips=6, stone=(0.22, 0.55))
    # lit stones against the gable, where its shadow fell on a bare slope
    g1 = rubble(B, dm, r, pic, [[(bm.x0 - 0.6, bw.y0 + 0.6), (bm.x0 + t2, bw.y0 + 0.6), (bm.x0 + t2, bw.y1 - 0.4),
                                 (bm.x0 - 0.6, bw.y1 - 0.4)]], g1, seed=73, stones=5, big=0, slabs=0, timbers=0,
                chips=0, stone=(0.4, 0.6), flank=0.0, open_at=lambda x, y: lids(x, y) > 1e8)
    g2 = heap_in(B, m, dm, r, pic, h, tops, [floor_m], crest_m,
                 tongues=[(2.0, bm.y1 - t2, 1.6, 0.45, True), (0.65, bm.y0 + t2, 0.4)], under=lids, cell=0.5, seed=24,
                 tgrow=3, free=("mW",), slopes={"mS": 1.5, "mN": 2.6}, ceiling=sky_m, stones=24, big=3, slabs=5,
                 timbers=26, chips=8, stone=(0.22, 0.55), sink=True)
    # what fell out through the cart door
    g4 = heap_dense(B, m, dm, r, pic, [(26, 72), (56, 72), (58, 82), (30, 84)], 0.45, z_at=0.2, edge=0.5, seed=27,
                    stones=4, big=0, slabs=1, timbers=4, chips=4, stone=(0.22, 0.45), slab=(0.3, 0.5))
    g = akit.ground_of(g1, g2, g4)
    for key, hole in (("m", hole_m), ("w", hole_w)):
        rafters(B, m["char"], h.roofs[key][0], r, [hole], g, seed=32 + len(key), skip=0.3, foot=wall_top_at(h, tops))
        ridge_fall(B, m["char"], h.roofs[key][0], r, [hole], g, seed=34 + len(key))
    # timbers fallen across the cart door
    y = bw.y0
    B.beam(m["char"], (-3.5, y - 0.05, 1.05), (-2.3, y - 0.55, 0.12), 0.14, 0.16, twist=10)
    B.beam(m["char"], (-2.0, y - 0.1, 0.9), (-3.1, y - 0.75, 0.1), 0.12, 0.14, twist=-14)
    scatter(B, dm, r, pic, [(78, 80), (112, 80), (112, 92), (78, 92)], 8, seed=43)
    scatter(B, dm, r, pic, [(0, 56), (22, 56), (22, 82), (0, 82)], 5, seed=44)
    return obs, h


def stage03a(n, r, B, m, pic):
    """CreHouse03a, the Ruined Stable: one long heap of stones and timbers
    down the middle, broken into lumps, big rounded grey and pale
    boulders lying on it and fallen runs of rafters across it, banked
    inside the broken back and west walls and up to the roof's cut edges.
    It pours out west through a breach and spills down over the front
    wall west of the middle, where the eave and the door are gone, and
    lies in a low spray at the north-east corner. A strip of roof along
    the back of the west half, the front-left strip, the front part of
    the east slope and the front wall with its arched east door stand."""
    h = house03()
    # the east arch is built below, the west door is lost under the spill
    h.openings = [o for o in h.openings if not (o[1] == "S" and o[2] in (2.06, -3.56))]
    mats_more(n, m, 43)
    hole = jag([(88, 6), (88, -60), (176, -60), (176, 40), (150, 38), (130, 40), (121, 48), (119, 70), (124, 86),
                (110, 94), (90, 92), (82, 86), (80, 72), (66, 66), (52, 64), (52, 80), (50, 96), (24, 96), (22, 80),
                (24, 64), (8, 62), (-20, 62), (-20, 31), (6, 31), (20, 34), (30, 28), (38, 18), (48, 13), (60, 14),
                (72, 11), (82, 12)], 5.0, 7.0, 4)
    tops = {"m": {"S": [(0, 2.3), (0.095, 2.3), (0.12, 1.6), (0.16, 1.05), (0.2, 1.2), (0.24, 1.0), (0.265, 1.7),
                        (0.28, 2.3), (1, 2.3)],
                  "N": [(0, 2.3), (0.3, 2.3), (0.38, 2.0), (0.45, 1.75), (0.58, 1.9), (0.7, 1.7), (0.85, 2.3),
                        (1, 2.3)],
                  "W": [(0, 2.3), (0.22, 1.9), (0.4, 1.6), (0.52, 0.75), (0.62, 0.8), (0.72, 1.6), (0.86, 1.8),
                        (1, 2.3)]}}
    obs = build(B, m, n, h, r, holes={"m": [hole]}, tops=tops, chimneys=[(0, 1.6)], halo=6.0, cull=True,
                gable_holes=[jag([(82, 60), (180, 60), (180, 96), (82, 96)], 1.5, 4.0, 6),
                             jag([(84, -40), (180, -40), (180, 14), (84, 14)], 1.5, 4.0, 7),
                             jag([(22, 70), (54, 70), (54, 100), (22, 100)], 1.5, 4.0, 18)])
    body = h.bodies["m"]
    arch_door(B, m, body, 2.06, 4.1, 1.45, 0.75, leaf=0.48, swing=20, leaves=2)
    m["ash"] = bed_mat(n, 9, chip=0.004, cells=3.0, bright=1.3)
    m["spray"] = spray_mat(n, 9)
    dm = debris_dense(n, pic, (20, 0, 170, 110), seed=9, pale=True)
    dm["big"] = big_mats(n, BOULDERS, 9)
    lids = lid(h, {"m": [hole]}, r)
    t2 = body.t / 2
    floor = [(body.x0 + t2, body.y0 + t2), (body.x1 - t2, body.y0 + t2), (body.x1 - t2, body.y1 - t2),
             (body.x0 + t2, body.y1 - t2)]
    sky = skyline(r, pic, body.y1 - 0.55)

    # mounded in the middle, lumpy, up under the roof's cut edges
    def crest(x, y):
        t = min(1.0, max(0.0, (y - body.y0) / (body.y1 - body.y0)))
        return 2.7 + 0.6 * max(0.0, 1 - (x / 4.6) ** 2) + 0.3 * t
    g1 = heap_in(B, m, dm, r, pic, h, tops, [floor], crest,
                 tongues=[(body.x0 + t2, -0.4, 0.35), (3.6, body.y1 - t2, 1.0, 0.45, True),
                          (body.x1 - t2, 2.0, 0.5, 0.4, True), (-2.0, body.y1 - t2, 1.6, 0.4, True),
                          (-3.1, body.y0 + t2, 0.75, 0.25, False, 2.4)],
                 under=lids, cell=0.55, seed=27, tgrow=3, slopes={"mN": 2.6}, ceiling=sky, sink=True,
                 lumps=0.6, stones=16, big=3, slabs=5, timbers=20, chips=4, stone=(0.22, 0.55))
    # big rounded boulders on the heap's visible top, timbers across them
    g1 = rubble(B, dm, r, pic, [floor], g1, seed=83, stones=13, big=0, slabs=0, timbers=5, chips=0,
                stone=(0.55, 1.0), kind="big", flank=0.0, rounded=True, open_at=lambda x, y: lids(x, y) > 1e8)
    # low spray west of the west wall and at the north-east corner
    g3 = heap_dense(B, m, dm, r, pic, [(0, 30), (12, 30), (14, 56), (12, 84), (-1, 86), (-2, 56)], 0.3, z_at=0.15,
                    edge=0.5, seed=29, spray=True, stones=4, big=0, slabs=0, timbers=3, chips=6, stone=(0.22, 0.45))
    g4 = heap_dense(B, m, dm, r, pic, [(156, 6), (172, 8), (173, 30), (160, 32)], 0.3, z_at=0.15, edge=0.5, seed=30,
                    spray=True,
                    stones=3, big=0, slabs=0, timbers=2, chips=4, stone=(0.22, 0.45))
    g = akit.ground_of(g1, g3, g4)
    # a stone where the west spray thins out between the tongue and the spray
    xs, ys = px(r, 4, 79, 0.1)
    akit.lump(B, dm["stone"][1], xs, ys, -0.06, 0.45, 0.38, 0.32, seed=91)
    # charred timbers down the front wall where the heap spills over it
    y = body.y0
    for i, (xa, xb) in enumerate(((-3.75, -3.55), (-3.3, -3.5), (-2.85, -2.6), (-2.5, -2.75))):
        za, zb = g(xa, y + 0.35), g(xb, y - 0.55)
        B.beam(m["char"], (xa, y + 0.35, max(0.1, za) + 0.06), (xb, y - 0.55, max(0.05, zb) + 0.06), 0.13, 0.15,
               twist=9 * (i - 1.5))
    ladder(B, m["lit"], r, g, (44, 74), (62, 42), width=0.75, rungs=5, seed=1)
    ladder(B, m["lit"], r, g, (88, 64), (96, 22), width=0.85, rungs=6, seed=2)
    rafters(B, m["char"], h.roofs["m"][0], r, [hole], g, seed=35, foot=wall_top_at(h, tops))
    ridge_fall(B, m["char"], h.roofs["m"][0], r, [hole], g, seed=36)
    edge_hang(B, dm, m["char"], h.roofs["m"][0], r, hole, 4, seed=37)
    scatter(B, dm, r, pic, [(0, 36), (14, 36), (14, 72), (0, 72)], 5, seed=45)
    scatter(B, dm, r, pic, [(158, 6), (173, 6), (173, 34), (158, 34)], 5, seed=46)
    scatter(B, dm, r, pic, [(22, 104), (54, 104), (54, 116), (22, 116)], 4, seed=47)
    return obs, h


def stage04a(n, r, B, m, pic):
    """CreHouse04a, the Ruined Manor: the block's hipped roof caved into a
    great heap of stones and dark timbers with big rounded boulders on
    it, banked inside the broken back and side walls right up to them and
    to the roof's ragged cut edges. It pours out east through a breach
    and west over the broken west wall into a low pale spray, and a low
    skirt of fallen stones lies at the foot of the back wall, which stands
    to a ragged top. The roof stands at the back west corner, in the west
    hip's triangle and the front slope's west half, and in a strip of the
    east hip. The wing's roof stands with two heaps broken up through it,
    each filling its hole to the cut edges and spilling over the wing's
    side into spray."""
    h = house04()
    hole = jag([(12, 26), (30, 24), (52, 22), (72, 26), (74, -4), (74, -60), (190, -60), (190, 96), (158, 94),
                (142, 96), (138, 148), (114, 148), (104, 124), (98, 110), (84, 108), (70, 102), (56, 97), (40, 95),
                (24, 96), (12, 98)], 5.0, 7.0, 8)
    wl = jag([(28, 168), (42, 160), (58, 157), (74, 162), (82, 176), (79, 194), (84, 210), (76, 226), (52, 230),
              (36, 224), (26, 206), (31, 186)], 3.0, 6.0, 9)
    wr = jag([(95, 197), (108, 187), (128, 184), (146, 188), (158, 196), (162, 212), (156, 230), (130, 234),
              (104, 232), (94, 216)], 3.0, 6.0, 10)
    tops = {"m": {"S": [(0, 3.3), (0.6, 3.3), (0.65, 2.6), (0.7, 2.3), (0.75, 2.6), (0.78, 3.3), (1, 3.3)],
                  "E": [(0, 3.3), (0.2, 3.3), (0.3, 2.5), (0.42, 1.2), (0.52, 1.15), (0.62, 2.0), (0.8, 1.9),
                        (1, 2.2)],
                  "N": [(0, 2.45), (0.06, 2.75), (0.12, 2.3), (0.2, 2.65), (0.28, 2.2), (0.36, 2.55), (0.44, 2.25),
                        (0.52, 2.6), (0.58, 2.35), (0.61, 2.9), (0.64, 3.3), (1, 3.3)],
                  "W": [(0, 3.3), (0.07, 3.3), (0.1, 1.6), (0.13, 0.7), (0.18, 0.45), (0.26, 0.4), (0.34, 0.5),
                        (0.42, 0.45), (0.47, 1.2), (0.56, 2.2), (0.64, 2.5), (0.7, 3.3), (1, 3.3)]},
            "w": {"N": None}, "a": {"E": None}}
    obs = build(B, m, n, h, r, holes={"m": [hole], "w": [wl, wr]}, tops=tops, chimneys=[], halo=6.0, cull=True,
                gable_holes=[jag([(24, 182), (44, 182), (44, 224), (24, 224)], 1.5, 4.0, 19),
                             jag([(144, 188), (166, 188), (166, 230), (144, 230)], 1.5, 4.0, 20)])
    body, bw = h.bodies["m"], h.bodies["w"]
    roof = h.roofs["m"][0]
    m["ash"] = bed_mat(n, 10, chip=0.004, cells=3.0, bright=1.3)
    m["spray"] = spray_mat(n, 10)
    dm = debris_dense(n, pic, (20, 10, 180, 140), seed=10, pale=True)
    dm["big"] = big_mats(n, BOULDERS, 10)
    lids = lid(h, {"m": [hole], "w": [wl, wr]}, r)
    t2 = body.t / 2
    floor = [(body.x0 + t2, body.y0 + t2), (body.x1 - t2, body.y0 + t2), (body.x1 - t2, body.y1 - t2),
             (body.x0 + t2, body.y1 - t2)]
    sky = skyline(r, pic, body.y1 - 0.55)

    # grid lines along the east hip's cut edge, so the heap stands up to it
    ex = (140 - 92) / 16.0

    # highest in the middle, up to the roof's cut edges, the east hip's too
    def crest(x, y):
        t = min(1.0, max(0.0, (y - body.y0) / (body.y1 - body.y0)))
        u = min(1.0, max(0.0, (x - body.x0) / (body.x1 - body.x0)))
        edge = 1.0 * max(0.0, 1.0 - abs(x - ex) / 1.1) * min(1.0, max(0.0, (1.2 - y) / 0.8))
        return 2.9 + 0.8 * math.sin(math.pi * min(1.0, t * 1.1)) + 0.4 * math.sin(math.pi * u) + edge
    g1 = heap_in(B, m, dm, r, pic, h, tops, [floor], crest,
                 tongues=[(body.x1 - t2, 2.1, 0.35, 0.22, False, 1.0), (1.5, body.y1 - t2, 2.4, 0.35, True)],
                 under=lids, cell=0.95, seed=30, slopes={"mN": 2.6}, ceiling=sky, sink=True,
                 lumps=0.35, stones=13, big=3, slabs=4, timbers=20, chips=3, stone=(0.22, 0.55),
                 lines=((ex - 0.08, ex + 0.1), ()))
    g1 = rubble(B, dm, r, pic, [floor], g1, seed=84, stones=12, big=0, slabs=0, timbers=4, chips=0,
                stone=(0.5, 0.95), kind="big", flank=0.0, rounded=True, open_at=lambda x, y: lids(x, y) > 1e8)

    # the wing's heaps fill their holes up under the cut edges and spill over its sides
    tw = bw.t / 2
    box = (bw.x0 + tw, bw.x1 - tw, bw.y0 + tw, bw.y1)

    def in_wing(P):
        for f in (lambda p: box[0] - p[0], lambda p: p[0] - box[1], lambda p: box[2] - p[1],
                  lambda p: p[1] - box[3]):
            P = clip(P, f)
        return P

    def grown(P, d):
        cx, cy = sum(p[0] for p in P) / len(P), sum(p[1] for p in P) / len(P)
        out = []
        for c, rw in P:
            L = math.hypot(c - cx, rw - cy)
            out.append((c + (c - cx) / L * d, rw + (rw - cy) / L * d) if L > 1e-6 else (c, rw))
        return out
    g2 = heap_in(B, m, dm, r, pic, h, tops, [in_wing(akit.plan_of(r, grown(wl, 11), 1.3))], lambda x, y: 1.95,
                 tongues=[(bw.x0 + tw, -4.5, 0.9, 0.2, False, 1.3)],
                 under=lids, cell=0.55, seed=31, lumps=0.5, sink=True, stones=4, big=1, slabs=1, timbers=4,
                 chips=1,
                 stone=(0.22, 0.5))
    g3 = heap_in(B, m, dm, r, pic, h, tops, [in_wing(akit.plan_of(r, grown(wr, 11), 1.3))], lambda x, y: 1.95,
                 tongues=[(bw.x1 - tw, -4.8, 0.8, 0.2, False, 1.3)],
                 under=lids, cell=0.55, seed=32, lumps=0.5, sink=True, stones=4, big=1, slabs=1, timbers=4,
                 chips=1,
                 stone=(0.22, 0.5))
    # the low pale spray west of the broken west wall, north of where the hip roof's shadow falls
    g4 = heap_dense(B, m, dm, r, pic, [(0, 28), (21, 25), (23, 48), (22, 72), (2, 74), (-2, 50)], 0.3, z_at=0.15,
                    edge=0.5, cell=0.5, seed=33, spray=True, stones=3, big=0, slabs=0, timbers=1, chips=3, stone=(0.22, 0.45),
                    kind="pale")
    g = akit.ground_of(g1, g2, g3, g4)
    rafters(B, m["char"], roof, r, [hole], g, seed=36, spacing=1.25, skip=0.6, foot=wall_top_at(h, tops))
    ridge_fall(B, m["char"], roof, r, [hole], g, seed=37)
    rafters(B, m["char"], h.roofs["w"][0], r, [wl, wr], g, seed=38, spacing=1.0, skip=0.65)
    edge_hang(B, dm, m["char"], roof, r, hole, 3, seed=39)
    # pale stones in the west spray
    scatter(B, dict(dm, stone=dm["pale"]), r, pic, [(0, 34), (16, 34), (16, 72), (0, 72)], 4, seed=47,
            kinds={"stone": 4, "chunk": 2})
    scatter(B, dm, r, pic, [(172, 30), (186, 30), (186, 120), (172, 120)], 4, seed=48)
    scatter(B, dm, r, pic, [(40, 222), (160, 222), (160, 235), (40, 235)], 5, seed=49)
    return obs, h


def stage05a(n, r, B, m, pic):
    """CreHouse05a, the small house ruined: the roof gone but for a strip
    of the west eaves on its wall, a dense heap of timbers and broken
    stones banked inside the broken back wall and the ragged stub of the
    east wall, its top as ragged as the drawing's, fallen stones at the
    back wall's foot, and loose stones and timbers strewn east of the
    house. The front gable wall stands to its eaves."""
    h = house05()
    hole = jag([(20, -24), (96, -24), (96, 96), (20, 96), (18, 60), (14, 30), (0, 26), (-12, 26), (-12, -24)], 4.0,
               6.0, 11)
    tops = {"m": {"S": [(0, 3.1), (0.7, 3.1), (0.85, 2.8), (1, 2.6)],
                  "E": [(0, 2.4), (0.15, 1.6), (0.3, 1.3), (0.5, 1.05), (0.7, 1.4), (0.85, 1.2), (1, 1.5)],
                  "N": [(0, 1.5), (0.3, 1.9), (0.6, 2.1), (1, 2.2)],
                  "W": [(0, 1.7), (0.3, 2.1), (0.42, 3.1), (1, 3.1)]}}
    obs = build(B, m, n, h, r, holes={"m": [hole]}, tops=tops, gables=[], chimneys=[], halo=5.0, cull=True)
    body = h.bodies["m"]
    m["ash"] = bed_mat(n, 11)
    m["spray"] = spray_mat(n, 11)
    dm = debris_dense(n, pic, (6, 0, 64, 82), seed=11)
    lids = lid(h, {"m": [hole]}, r)
    t2 = body.t / 2
    floor = [(body.x0 + t2, body.y0 + t2), (body.x1 - t2, body.y0 + t2), (body.x1 - t2, body.y1 - t2),
             (body.x0 + t2, body.y1 - t2)]
    sky = skyline(r, pic, body.y1 - 0.55, margin=0.0)

    def crest(x, y):
        t = min(1.0, max(0.0, (y - body.y0) / (body.y1 - body.y0)))
        return 2.5 + 0.5 * t
    g1 = heap_in(B, m, dm, r, pic, h, tops, [floor], crest,
                 tongues=[(0.0, body.y1 - t2, 1.3, 0.4, True)], under=lids, cell=0.4, seed=33, tgrow=3,
                 slopes={"mN": 2.6}, ceiling=sky, lumps=0.35,
                 stones=30, big=2, slabs=4, timbers=20, chips=10, stone=(0.22, 0.55))
    g = g1
    rafters(B, m["char"], h.roofs["m"][0], r, [hole], g, seed=38, spacing=0.65, skip=0.35,
            foot=wall_top_at(h, tops))
    ridge_fall(B, m["char"], h.roofs["m"][0], r, [hole], g, seed=39)
    # loose stones and timbers strewn on the ground east of the house
    scatter(B, dm, r, pic, [(54, 14), (78, 14), (78, 80), (54, 80)], 22, seed=50,
            kinds={"stone": 5, "chunk": 3, "beam": 2})
    return obs, h


def stage06a(n, r, B, m, pic):
    """CreHouse06a, the Destroyed Villa: the big hipped roof fallen in at
    its middle to a great heap banked inside the broken back wall, whose
    middle stands to a ragged top with a window still in it. The chimney
    breast lies fallen in the heap, a broken pale ring round its sooted
    flue with rubble down in it and broken pale pieces beside, and the
    heap spills down through the gallery and over the porch. The west
    slope stands, stripped to its boards in a broad band where the tiles
    slid off, a strip of tiles left before the heap, and a strip of the
    east slope stands along its eaves. Corners of the front slope, the
    gallery's ends and the dark east wing stand, the wing's west slope
    charred black, its front half fallen in under rubble and its east
    slope whole."""
    h = house06()
    m_ = h.roofs["m"][0]
    # the boards under the west slope's tiles, where they slid off
    h.roofs["b"] = (Roof(m_.x0, m_.x1, m_.y0, m_.y1, m_.ze - 0.07, m_.k, T=0.1), "#9a7448", "planks")
    hole_m = jag([(50, -60), (130, -60), (130, -2), (134, 12), (141, 18), (142, 40), (141, 62), (143, 86), (150, 108),
                  (112, 112), (104, 130), (100, 140), (60, 132), (58, 104), (46, 96), (30, 92), (28, 76), (32, 60),
                  (40, 48), (50, 38)], 4.0, 6.0, 12)
    strip = [(-10, -60), (14, -60), (14, 8), (23, 20), (12, 33), (25, 46), (13, 60), (26, 73), (14, 87), (22, 100),
             (-10, 100)]
    rest = [(14, -60), (400, -60), (400, 400), (-10, 400), (-10, 100), (22, 100), (14, 87), (26, 73), (13, 60),
            (25, 46), (12, 33), (23, 20), (14, 8)]
    hole_g = jag([(60, 124), (110, 124), (112, 160), (108, 192), (60, 192), (56, 160)], 3.0, 5.0, 13)
    hole_p = jag([(68, 168), (114, 168), (114, 220), (68, 220)], 2.0, 4.0, 18)
    hole_e = jag([(136, 84), (160, 79), (186, 81), (190, 150), (136, 150)], 3.0, 5.0, 14)
    tops = {"m": {"S": [(0, 4.0), (0.34, 4.0), (0.42, 2.8), (0.6, 2.2), (0.85, 2.6), (1, 3.0)],
                  "N": [(0, 4.0), (0.1, 4.0), (0.16, 3.4), (0.22, 3.3), (0.3, 3.5), (0.38, 3.1), (0.48, 3.4),
                        (0.56, 3.0), (0.66, 3.3), (0.74, 3.5), (0.8, 4.0), (1, 4.0)],
                  "E": [(0, 3.0), (0.12, 2.6), (0.22, 3.1), (0.28, 4.0), (1, 4.0)],
                  "W": [(0, 4.0), (0.3, 4.0), (0.4, 3.2), (0.6, 3.4), (0.7, 4.0), (1, 4.0)]},
            "g": {"S": [(0, 2.3), (0.38, 2.3), (0.45, 1.4), (0.6, 1.0), (0.68, 1.6), (0.72, 2.3), (1, 2.3)],
                  "N": None},
            "e": {"S": [(0, 1.3), (0.4, 1.0), (0.7, 1.5), (0.82, 2.6), (1, 2.6)], "W": None}}
    holes = {"m": [hole_m, strip], "b": [hole_m, rest], "g": [hole_g], "p": [hole_p], "e": [hole_e]}
    obs = build(B, m, n, h, r, holes=holes, tops=tops, gables=[("g", "g", "W"), ("g", "g", "E"), ("e", "e", "N")],
                halo=6.0, posts=[0], cull=True, plane_dark={"e": {"W": 0.22}, "m": {"W": 1.4}}, sooty={"m": [hole_m]})
    bm_, be = h.bodies["m"], h.bodies["e"]
    # the window still in the back wall's standing middle, seen from inside
    wx, yw = 0.37, bm_.y1 - bm_.t - 0.02
    B.box(m["glass"], wx, yw, 2.1, 0.7, 0.05, 0.75)
    B.box(m["frame"], wx, yw - 0.02, 2.85, 0.86, 0.08, 0.1)
    B.box(m["frame"], wx, yw - 0.02, 2.04, 0.8, 0.09, 0.06)
    m["ash"] = bed_mat(n, 12)
    m["spray"] = spray_mat(n, 12)
    dm = debris_dense(n, pic, (40, 0, 210, 215), seed=12, pale=True)
    lids = lid(h, holes, r)
    t2 = bm_.t / 2
    floor = [(bm_.x0 + t2, bm_.y0 + t2), (bm_.x1 - t2, bm_.y0 + t2), (bm_.x1 - t2, bm_.y1 - t2),
             (bm_.x0 + t2, bm_.y1 - t2)]
    sky = skyline(r, pic, bm_.y1 - 0.55)

    ex = (141 - 107) / 16.0

    # highest in the middle, up to the roof's cut edges, down below the window
    def crest(x, y):
        t = min(1.0, max(0.0, (y - bm_.y0) / (bm_.y1 - bm_.y0)))
        u = min(1.0, max(0.0, (x - bm_.x0) / (bm_.x1 - bm_.x0)))
        dip = 3.0 * max(0.0, 1.0 - abs(x - wx) / 0.95) * max(0.0, 1.0 - (yw - y) / 1.5)
        # up to the east strip's cut edge
        edge = 0.9 * max(0.0, 1.0 - (ex - x) / 1.0) if x < ex + 0.3 else 0.0
        return 3.4 + 0.7 * math.sin(math.pi * min(1.0, t * 1.1)) + 0.5 * math.sin(math.pi * u) - dip + edge
    g1 = heap_in(B, m, dm, r, pic, h, tops, [floor], crest,
                 tongues=[(-2.0, bm_.y1 - t2, 2.4, 0.45, True), (bm_.x0 + t2, 3.6, 1.2, 0.4, True)],
                 under=lids, cell=0.65, seed=34, tgrow=3, slopes={"mN": 2.6}, ceiling=sky, lumps=0.35, sink=True,
                 lines=((ex - 0.08, ex + 0.1), ()), stones=58, big=3, slabs=10, timbers=46, chips=14,
                 stone=(0.24, 0.62))
    # down through the gallery and over the porch
    g2 = heap_dense(B, m, dm, r, pic, [(60, 122), (110, 122), (114, 150), (110, 180), (104, 206), (84, 214),
                                       (70, 204), (62, 170), (58, 146)], 2.6, z_at=1.3, edge=1.3, seed=35,
                    stones=12, big=1, slabs=3, timbers=12, chips=5, stone=(0.22, 0.55))
    # the wing filled to its broken front, rising to the cut edge behind
    floor_e = [(bm_.x1, be.y0 + t2), (be.x1 - t2, be.y0 + t2), (be.x1 - t2, be.y1 - t2), (bm_.x1, be.y1 - t2)]
    ridge = (h.roofs["e"][0].x0 * 0.55 + h.roofs["e"][0].x1 * 0.95) / 1.5

    def crest_e(x, y):
        t = min(1.0, max(0.0, (y - be.y0) / (be.y1 - be.y0)))
        return 2.2 + 2.6 * t + 0.8 * max(0.0, 1.0 - abs(x - ridge) / 1.0)
    g3 = heap_in(B, m, dm, r, pic, h, tops, [floor_e], crest_e, slopes={"eS": 1.6, "eE": 2.0},
                 under=lids, cell=0.5, seed=36, tgrow=3, stones=10, big=1, slabs=2, timbers=10, chips=4,
                 stone=(0.2, 0.48), lines=((), (0.78, 0.95)), sink=True)
    g = akit.ground_of(g1, g2, g3)
    # the chimney breast fallen whole and broken, pale pieces of it beside
    x, y = px(r, 114, 64, 3.8)
    c0, Hb = breast(B, m["pale"], m["dark"], dm["stone"], x, y, max(0.0, g(x, y)) - 0.15, W=2.2, D=1.75, Hb=1.1,
                    seed=61)

    def pale_soot(co, c=c0, Hb=Hb):
        up = min(1.0, max(0.0, (co.z - c.z) / (Hb * 1.1)))
        near = max(0.0, 1.0 - math.hypot(co.x - c.x, co.y - c.y) / 0.7)
        return max(0.55, 1.0 - 0.22 * up - 0.3 * near)
    SOOT.setdefault(n, {})["_pale"] = pale_soot
    for (c, rw, zz), sz, sd in (((86, 77, 3.3), (0.9, 0.7, 0.55), 62), ((98, 79, 3.3), (0.6, 0.5, 0.4), 63),
                                ((130, 78, 3.5), (0.55, 0.45, 0.35), 64)):
        x2, y2 = px(r, c, rw, zz)
        akit.rock(B, m["pale"], x2, y2, max(0.0, g(x2, y2)) - sz[2] * 0.35, *sz, seed=sd, seg=5)
    # a run of rafters fallen still nailed to their battens
    ladder(B, m["char"], r, g, (70, 70), (92, 108), width=0.8, rungs=5, seed=3)
    ladder(B, m["char"], r, g, (128, 86), (146, 52), width=0.75, rungs=4, seed=4)
    # the stripped band's charred battens
    roof_b = h.roofs["b"][0]
    for k, c0_ in enumerate((4, 9, 15)):
        pts = []
        for rw in range(-20, 100, 4):
            col = c0_ + (k % 2)
            zz = roof_b.ze + 0.5
            for _ in range(5):
                xx, yy = px(r, col, rw, zz)
                zz = roof_b.z(xx, yy)
            if roof_b.inside(xx, yy) and akit.inside_poly(strip, col, rw) and not akit.inside_poly(hole_m, col, rw):
                pts.append(Vector((xx, yy, zz + 0.03)))
        if len(pts) >= 2:
            B.beam(m["char"], tuple(pts[0]), tuple(pts[-1]), 0.08, 0.06, twist=5)
    for key, P in (("m", [hole_m]), ("g", [hole_g]), ("e", [hole_e])):
        rafters(B, m["char"], h.roofs[key][0], r, P, g, seed=40 + ord(key), spacing=0.95, skip=0.5,
                foot=wall_top_at(h, tops))
    ridge_fall(B, m["char"], h.roofs["m"][0], r, [hole_m], g, seed=44)
    edge_hang(B, dm, m["char"], h.roofs["m"][0], r, hole_m, 5, seed=45)
    scatter(B, dm, r, pic, [(200, 80), (219, 80), (219, 140), (200, 140)], 6, seed=51)
    scatter(B, dm, r, pic, [(64, 200), (110, 200), (110, 216), (64, 216)], 6, seed=52)
    scatter(B, dm, r, pic, [(150, 136), (196, 136), (196, 146), (150, 146)], 5, seed=53)
    return obs, h


def stage07a(n, r, B, m, pic):
    """CreHouse07a, the Destroyed House: the roof fallen in all but its
    west eaves, its back west corner, its lower front edge and a strip of
    the east slope, a dense heap of stones and timber banked inside the
    broken back wall, whose gable is gone above the breach. The heap
    rises under the west eaves, where big rounded grey-tan stones lie,
    and up to the east strip's cut edge, and pours out over the broken
    east half of the front wall where the plank lean-to stood, one post
    of it left. The arched doorway at the west end of the front stands
    open and dark above its steps. Grey spray lies west of the west
    wall."""
    h = house07()
    # the doorway is built as an arch below
    h.openings = [o for o in h.openings if o[6] != "door"]
    mats_more(n, m, 47)
    hole = jag([(12, 36), (20, 36), (46, 36), (48, 4), (48, -60), (112, -60), (112, 38), (94, 40), (92, 92), (80, 96),
                (56, 90), (50, 86), (14, 84), (12, 60)], 4.0, 6.0, 15)
    hole_p = [(50, 96), (112, 96), (112, 146), (50, 146)]
    tops = {"m": {"S": [(0, 3.4), (0.5, 3.4), (0.55, 2.4), (0.7, 1.7), (0.85, 1.5), (0.95, 2.2), (1, 2.8)],
                  "N": [(0, 2.5), (0.15, 2.3), (0.26, 2.6), (0.31, 3.4), (1, 3.4)]}}
    obs = build(B, m, n, h, r, holes={"m": [hole], "p": [hole_p]}, tops=tops, chimneys=[(0, 1.8)], posts=[0],
                halo=6.0, cull=True, gable_holes=[jag([(50, 66), (112, 66), (112, 94), (50, 94)], 1.5, 4.0, 16),
                                                  jag([(71, -40), (112, -40), (112, 24), (71, 24)], 1.5, 4.0, 17)])
    body = h.bodies["m"]
    arch_door(B, m, body, -2.06, -1.31, 1.5, 1.1, leaves=0, floor=False, pale=(2,))
    m["ash"] = bed_mat(n, 13)
    m["spray"] = spray_mat(n, 13)
    dm = debris_dense(n, pic, (10, 4, 100, 140), seed=13, pale=True)
    dm["big"] = big_mats(n, TAN, 13)
    lids = lid(h, {"m": [hole], "p": [hole_p]}, r)
    t2 = body.t / 2
    floor = [(body.x0 + t2, body.y0 + t2), (body.x1 - t2, body.y0 + t2), (body.x1 - t2, body.y1 - t2),
             (body.x0 + t2, body.y1 - t2)]
    sky = skyline(r, pic, body.y1 - 0.55)

    # up under the west eaves and to the east strip's cut edge, rising toward the back
    def crest(x, y):
        t = min(1.0, max(0.0, (y - body.y0) / (body.y1 - body.y0)))
        u = min(1.0, max(0.0, (x - body.x0) / (body.x1 - body.x0)))
        return 2.9 + 0.6 * u + 0.3 * t
    g1 = heap_in(B, m, dm, r, pic, h, tops, [floor], crest,
                 tongues=[(1.9, body.y0 + t2, 0.4, 0.22, False, 1.4), (0.0, body.y1 - t2, 1.2, 0.35, True),
                          (body.x0 + t2, 1.2, 0.8, 0.35, True)],
                 under=lids, cell=0.5, seed=37, tgrow=3, slopes={"mN": 2.6, "mS": 1.4}, ceiling=sky, lumps=0.35,
                 sink=True,
                 stones=40, big=3, slabs=6, timbers=30, chips=10, stone=(0.22, 0.56))
    # the big rounded grey-tan stones the drawing heaps up under the west eaves
    west = [(body.x0 + t2, body.y0 + t2), (-0.4, body.y0 + t2), (-0.4, body.y1 - t2), (body.x0 + t2, body.y1 - t2)]
    g1 = rubble(B, dm, r, pic, [west], g1, seed=71, stones=9, big=0, slabs=0, timbers=0, chips=0, stone=(0.6, 0.95),
                kind="big", flank=0.0, rounded=True, open_at=lambda x, y: lids(x, y) > 1e8)
    # grey spray west of the house, low
    g2 = heap_dense(B, m, dm, r, pic, [(0, 30), (12, 30), (14, 50), (12, 64), (0, 64)], 0.3, z_at=0.15, edge=0.5,
                    seed=38, spray=True, stones=4, big=0, slabs=0, timbers=2, chips=5, stone=(0.22, 0.45))
    g = akit.ground_of(g1, g2)
    rafters(B, m["char"], h.roofs["m"][0], r, [hole], g, seed=39, spacing=0.7, skip=0.4, foot=wall_top_at(h, tops))
    ridge_fall(B, m["char"], h.roofs["m"][0], r, [hole], g, seed=40)
    edge_hang(B, dm, m["char"], h.roofs["m"][0], r, hole, 4, seed=41)
    # the lean-to's fallen planks across the spill
    for i, (a_, b_) in enumerate((((0.6, -2.4), (2.1, -3.3)), ((1.4, -2.3), (2.6, -3.0)), ((0.9, -2.9), (2.4, -3.5)))):
        za, zb = max(0.05, g(*a_)), max(0.05, g(*b_))
        B.beam(m["char"], (a_[0], a_[1], za + 0.04), (b_[0], b_[1], zb + 0.04), 0.32, 0.06, twist=8 * (i - 1))
    scatter(B, dm, r, pic, [(66, 120), (105, 120), (105, 143), (66, 143)], 10, seed=53)
    scatter(B, dm, r, pic, [(0, 30), (14, 30), (14, 62), (0, 62)], 6, seed=54)
    return obs, h


# soot kept by material, where a stage wants more than soot_fn gives
SOOT = {"CreHouse02a": {"_wtop": 0.45}, "CreHouse06a": {"_rb": 0.78}}

STAGES = {"CreHouse01a": stage01a, "CreHouse02a": stage02a, "CreHouse03a": stage03a, "CreHouse04a": stage04a,
          "CreHouse05a": stage05a, "CreHouse06a": stage06a, "CreHouse07a": stage07a}
HOUSES = {"CreHouse01": house01, "CreHouse02": house02, "CreHouse03": house03, "CreHouse04": house04,
          "CreHouse05": house05, "CreHouse06": house06, "CreHouse07": house07}


def make(n):
    """Builds stage n, or intact house n to check its fit, soot painted."""
    camera(n)
    r = akit.CAT[n]
    B = akit.Builder(seed=zlib.crc32(n.encode()))
    pic = akit.Picture(r)
    m = mats_for(n, zlib.crc32(n.encode()) % 1000)
    if n in STAGES:
        obs, h = STAGES[n](n, r, B, m, pic)
        obs += B.objects()
        paint(obs, soot_fn(list(h.bodies.values()), by_mat=SOOT.get(n)))
    else:
        obs = build(B, m, n, HOUSES[n]())
        paint(obs, lambda ob, co: 1.0)
    return obs


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "--norender" not in argv
    out = os.path.dirname(DATA.rstrip("/\\"))
    if "--out" in argv:
        out = argv[argv.index("--out") + 1]
        argv = [a for a in argv if a != out]
    names = [a for a in argv if not a.startswith("--")] or list(STAGES)
    names = [a for a in names if a in STAGES or a in HOUSES]
    for n in names:
        t0 = time.time()
        try:
            hk.reset()
            obs = make(n)
            finish(n, obs, out if n in STAGES else os.path.join(out, "work", "intact"), render)
        except Exception as e:
            traceback.print_exc()
            print("STAGE_FAILED", n, repr(e), flush=True)
        print("STAGE_TIME", n, "%.1fs" % (time.time() - t0), flush=True)


main()
