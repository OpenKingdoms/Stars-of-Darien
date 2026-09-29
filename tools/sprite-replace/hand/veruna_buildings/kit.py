"""Parametric builders for the Veruna town pieces, run inside Blender.

Houses, walled compounds, round halls, flat-roofed blocks, barrel vaults,
hovels, docks and their ruins. Same frame as handkit: one unit is one map
cell, -Y toward the classic camera, Z up, origin on the feature's anchor.

A Model gathers geometry into one mesh per material. Positions can be read
straight off the sprite: X(col) and Y(row, z) undo the classic projection
(column = hx + 16x, row = hy - 16y - 8z) for a point drawn at (col, row)
that stands z cells up.
"""
import json
import math
import os
import random
import sys
import zlib
from collections import deque

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(os.path.dirname(HERE))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)
import carve  # noqa: E402
import handkit as hk  # noqa: E402

SPRITES = "D:/OKReplace/sprites"
CATALOG = "D:/OKReplace/catalog.json"
OUT = "D:/OKReplace/hand/veruna_buildings"
# the sprite has its light painted in, the game lights the model again
GAIN = 1.15

_CAT = None


def record(name):
    global _CAT
    if _CAT is None:
        _CAT = {r["name"]: r for r in json.load(open(CATALOG))}
    return _CAT[name]


def lin(c):
    """sRGB 0-255 to a linear material colour, with the gain."""
    out = []
    for v in c[:3]:
        v = v / 255.0
        v = v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
        out.append(min(1.0, v * GAIN))
    return tuple(out)


def _outward(fn):
    """Recalculates the normals of the faces a builder made, as one island."""
    def wrap(self, key, *a, **k):
        bm = self.bm(key)
        n0 = len(bm.faces)
        out = fn(self, key, *a, **k)
        bm.faces.ensure_lookup_table()
        new = [bm.faces[i] for i in range(n0, len(bm.faces))]
        if new:
            bmesh.ops.recalc_face_normals(bm, faces=new)
        return out
    return wrap


def _mat4(loc, rot=(0.0, 0.0, 0.0), scale=(1.0, 1.0, 1.0)):
    yaw, pitch, roll = rot
    R = Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(pitch, 4, "Y") @ Matrix.Rotation(roll, 4, "X")
    return Matrix.Translation(Vector(loc)) @ R @ Matrix.Diagonal((*scale, 1.0))


class Model:
    def __init__(self, name):
        r = record(name)
        self.r, self.name = r, name
        self.hx, self.hy = r["sprite"]["hotspot"]
        self.w, self.h = r["sprite"]["w"], r["sprite"]["h"]
        self.png = os.path.join(SPRITES, name + ".png")
        self.pal = {}
        self.groups = {}
        self.paint_keys = set()
        self.rng = random.Random(name)
        self._pix = None
        self.beam_share = 0.45
        self.roof_log = []
        self.tris = {}
        # cap key -> (sooted key, share): see _wall_run
        self.soot = {}
        self._soot_nz = self.noise("soot", 1.5)
        # the owner's sturdier rule: heights times zk, thin timbers times girth
        self.zk = 1.0
        self.girth = 1.2
        # the colour lift of painted parts, written to okPaint for the game
        self.paint_gain = carve.ALBEDO_GAIN

    def sturdy(self, k):
        """Builds this model k times as tall as the drawing's exact fit, and
        deeper so its top back edge still lands where the drawing has it."""
        self.zk = k

    # ---- turning a part -------------------------------------------
    def rot(self, cx, cy, yaw):
        """with m.rot(x, y, yaw): everything built inside turns about (x, y)."""
        model = self

        class _Turn:
            def __enter__(self):
                self.n = {k: len(bm.verts) for k, bm in model.groups.items()}
                return self

            def __exit__(self, *exc):
                c, s_ = math.cos(yaw), math.sin(yaw)
                for k, bm in model.groups.items():
                    bm.verts.ensure_lookup_table()
                    for i in range(self.n.get(k, 0), len(bm.verts)):
                        v = bm.verts[i]
                        x, y = v.co.x - cx, v.co.y - cy
                        v.co.x, v.co.y = cx + c * x - s_ * y, cy + s_ * x + c * y
                return False
        return _Turn()

    # ---- the frame --------------------------------------------------
    def X(self, col):
        return (col - self.hx) / 16.0

    def Y(self, row, z=0.0):
        return (self.hy - row - 8.0 * z) / 16.0

    def P(self, col, row, z=0.0):
        return (self.X(col), self.Y(row, z))

    def frame(self, dx=0, dy=0):
        """X and Y for coordinates read off a sibling's sprite: a ruin's
        drawing sits dx, dy pixels off its intact one."""
        m = self

        class F:
            @staticmethod
            def X(c):
                return m.X(c + dx)

            @staticmethod
            def Y(r, z=0.0):
                return m.Y(r + dy, z)

            @staticmethod
            def P(c, r, z=0.0):
                return m.X(c + dx), m.Y(r + dy, z)
        return F

    def circle(self, cx, cy, r, n=24, ry=None):
        ry = r if ry is None else ry
        return [(cx + r * math.cos(2 * math.pi * i / n), cy + ry * math.sin(2 * math.pi * i / n)) for i in range(n)]

    def scr(self, x, y, z=0.0):
        return self.hx + 16.0 * x, self.hy - 16.0 * y - 8.0 * z

    # ---- the picture ------------------------------------------------
    @property
    def pix(self):
        if self._pix is None:
            import numpy as np
            img = bpy.data.images.load(self.png)
            a = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
            self._pix = a[::-1].copy()
            # a second datablock of the same file greys out the painted parts in Cycles
            bpy.data.images.remove(img)
        return self._pix

    def at(self, col, row):
        c, r = int(math.floor(col)), int(math.floor(row))
        if 0 <= c < self.w and 0 <= r < self.h and self.pix[r, c, 3] > 0.5:
            return self.pix[r, c, :3]
        return None

    def mean_at(self, col, row, rad=2):
        import numpy as np
        c, r = int(col), int(row)
        a = self.pix[max(0, r - rad):r + rad + 1, max(0, c - rad):c + rad + 1].reshape(-1, 4)
        a = a[a[:, 3] > 0.5]
        return a[:, :3].mean(0) if len(a) else None

    def sample(self, x0, y0, x1, y1):
        """Median sRGB (0-255) of the drawn pixels in a sprite rectangle."""
        import numpy as np
        a = self.pix[y0:y1, x0:x1].reshape(-1, 4)
        a = a[a[:, 3] > 0.5][:, :3]
        if not len(a):
            return (128, 128, 128)
        return tuple(float(v) * 255 for v in np.median(a, 0))

    # ---- materials and groups ---------------------------------------
    def col(self, key, srgb, rough=0.9, metal=0.0, paint=False):
        if isinstance(srgb, tuple) and len(srgb) == 4:
            srgb = self.sample(*srgb)
        self.pal[key] = dict(rgb=srgb, rough=rough, metal=metal)
        if paint:
            self.paint_keys.add(key)
        return key

    def bm(self, key):
        if key not in self.groups:
            self.groups[key] = bmesh.new()
        return self.groups[key]

    # ---- primitives -------------------------------------------------
    def box(self, key, x0, x1, y0, y1, z0, z1):
        return self.obox(key, (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, abs(x1 - x0), abs(y1 - y0), abs(z1 - z0))

    def obox(self, key, cx, cy, cz, sx, sy, sz, yaw=0.0, pitch=0.0, roll=0.0):
        bm = self.bm(key)
        bmesh.ops.create_cube(bm, size=1.0, matrix=_mat4((cx, cy, cz), (yaw, pitch, roll), (sx, sy, sz)))

    def beam(self, key, p0, p1, w, h=None, roll=0.0):
        """A squared timber from p0 to p1 (3D points)."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        L = d.length
        if L < 1e-4:
            return
        if max(w, h or w) <= 0.3:
            w, h = w * self.girth, (h or w) * self.girth
        yaw = math.atan2(d.y, d.x)
        pitch = -math.asin(max(-1.0, min(1.0, d.z / L)))
        c = (p0 + p1) / 2
        self.obox(key, c.x, c.y, c.z, L, w, h or w, yaw, pitch, roll)

    def post(self, key, x, y, z0, z1, w, yaw=0.0, lean=0.0):
        """An upright post or pile of side w (times girth) from z0 to z1."""
        w *= self.girth
        self.obox(key, x, y, (z0 + z1) / 2, w, w, z1 - z0, yaw, 0.0, lean)

    def _fix(self, bm, faces):
        bmesh.ops.recalc_face_normals(bm, faces=[f for f in faces if f.is_valid])

    @_outward
    def prism(self, key, pts, z0, z1):
        """A polygon (x, y points, either winding) extruded from z0 to z1."""
        bm = self.bm(key)
        if _area(pts) < 0:
            pts = pts[::-1]
        vb = [bm.verts.new((x, y, z0)) for x, y in pts]
        vt = [bm.verts.new((x, y, z1)) for x, y in pts]
        bm.faces.new(vt)
        bm.faces.new(vb[::-1])
        n = len(pts)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((vb[i], vb[j], vt[j], vt[i]))

    @_outward
    def slab(self, key, pts3, t, down=True):
        """A planar polygon (3D points, counter-clockwise seen from above)
        given thickness t straight down."""
        bm = self.bm(key)
        top = [bm.verts.new(p) for p in pts3]
        bot = [bm.verts.new((p[0], p[1], p[2] - t)) for p in pts3]
        bm.faces.new(top)
        bm.faces.new(bot[::-1])
        n = len(pts3)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((bot[i], bot[j], top[j], top[i]))

    def cyl(self, key, cx, cy, z0, r0, h, r1=None, seg=12, smooth=True, rot=None):
        bm = self.bm(key)
        r1 = r0 if r1 is None else r1
        if rot is None:
            M = Matrix.Translation((cx, cy, z0 + h / 2))
        else:
            M = _mat4((cx, cy, z0), rot) @ Matrix.Translation((0, 0, h / 2))
        res = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r0, radius2=max(r1, 1e-4),
                                    depth=h, matrix=M)
        if smooth:
            for f in {f for v in res["verts"] for f in v.link_faces}:
                if len(f.verts) == 4:
                    f.smooth = True

    @_outward
    def lathe(self, key, cx, cy, prof, seg=16, smooth=True, cap_top=True):
        """A turned solid from (radius, z) pairs, bottom to top."""
        bm = self.bm(key)
        rings = []
        for r, z in prof:
            ring = []
            for i in range(seg):
                a = 2 * math.pi * i / seg
                ring.append(bm.verts.new((cx + r * math.cos(a), cy + r * math.sin(a), z)))
            rings.append(ring)
        for a, b in zip(rings, rings[1:]):
            for i in range(seg):
                j = (i + 1) % seg
                f = bm.faces.new((a[i], a[j], b[j], b[i]))
                f.smooth = smooth
        bm.faces.new(rings[0][::-1])
        if cap_top:
            bm.faces.new(rings[-1])

    def stone(self, key, cx, cy, sx, sy, sz, yaw=0.0, z=None):
        bm = self.bm(key)
        z = sz * 0.35 if z is None else z
        # a rough chunk: a cube with its corners pulled about, top narrower
        res = bmesh.ops.create_cube(bm, size=1.0, matrix=_mat4((cx, cy, z), (yaw, 0, 0), (sx, sy, sz)))
        for v in res["verts"]:
            top = v.co.z > z
            k = 0.3 if top else 0.12
            v.co.x += self.rng.uniform(-k, k) * sx * (0.8 if top else 1)
            v.co.y += self.rng.uniform(-k, k) * sy * (0.8 if top else 1)
            v.co.z += self.rng.uniform(-0.15, 0.15) * sz
            v.co.z = max(v.co.z, 0.0)

    # ---- roofs ------------------------------------------------------
    def roof(self, key, x0, x1, y0, y1, z, rise, axis="x", ends=("hip", "hip"), over=0.2, t=0.12,
             core=None, keep=None, cell=0.4, rafters=None):
        """A pitched roof over the rectangle, ridge along axis. An end is
        'hip' (sloped) or 'gable' (the core's wall shows under it). core is
        the wall material filling the roof space. keep(x, y, z) -> bool
        punches holes: each roof cell whose centre fails is left open."""
        self.roof_log.append((x0, x1, y0, y1, z, rise, axis, ends))
        if axis == "y":
            # build along x in a swapped frame, then swap back
            sw = lambda p: (p[1], p[0], p[2])  # noqa: E731
            faces, cf = _roof_faces(y0, y1, x0, x1, z, rise, ends, over, t)
            faces = [[sw(p) for p in f][::-1] for f in faces]
            cf = [[sw(p) for p in f][::-1] for f in cf]
        else:
            faces, cf = _roof_faces(x0, x1, y0, y1, z, rise, ends, over, t)
        for f in faces:
            if keep is None:
                self.slab(key, f, t)
            else:
                self.grid_slab(key, f, t, keep, cell)
                if rafters:
                    self.rafters(rafters, f, keep)
        if core:
            self.solid(core, cf)

    def roof_z(self, x, y):
        """The height of the roofs built so far over (x, y), or None."""
        best = None
        for e in self.roof_log:
            if e[0] == "cone":
                _, cx, cy, r, z, rise = e
                d = math.hypot(x - cx, y - cy)
                if d <= r:
                    zz = z + rise * (1 - d / r)
                    best = zz if best is None else max(best, zz)
                continue
            x0, x1, y0, y1, z, rise, axis, ends = e
            if not (x0 <= x <= x1 and y0 <= y <= y1):
                continue
            if axis == "y":
                x, y, x0, x1, y0, y1 = y, x, y0, y1, x0, x1
            hw = (y1 - y0) / 2
            d = min(y - y0, y1 - y)
            if ends[0] == "hip":
                d = min(d, x - x0)
            if ends[1] == "hip":
                d = min(d, x1 - x)
            zz = z + rise * min(1.0, d / hw)
            if axis == "y":
                x, y = y, x
            best = zz if best is None else max(best, zz)
        return best

    def fallen(self, keys, pts, dz=0.7, cell=0.5, seed=5, n=0, zmin=0.0):
        """The charred timbers and fallen roof just under a broken roof,
        coloured as the drawing shows them through its holes; n loose
        beams and tiles lie on top."""
        def zfn(x, y):
            z = self.roof_z(x, y)
            return zmin if z is None else max(zmin, z - dz)
        self.mound(keys, pts, zfn, cell=cell, seed=seed, noise=0.2)
        if n:
            self.heap(keys, pts, 1.0, n=n, size=(0.25, 0.5), zfn=zfn, seed=seed)

    def holes(self, *polys):
        """keep(x, y, z) that refuses the points drawn inside any of the
        given outlines (sprite pixels of this model's own picture)."""
        def keep(x, y, z):
            c, r = self.scr(x, y, z)
            return not any(_inside(p, c, r) for p in polys)
        return keep

    @_outward
    def solid(self, key, faces):
        bm = self.bm(key)
        vmap = {}

        def V(p):
            k = tuple(round(c, 5) for c in p)
            if k not in vmap:
                vmap[k] = bm.verts.new(p)
            return vmap[k]
        for f in faces:
            vs = [V(p) for p in f]
            if len(set(vs)) >= 3:
                try:
                    bm.faces.new(list(dict.fromkeys(vs)))
                except ValueError:
                    pass

    @_outward
    def grid_slab(self, key, poly, t, keep, cell=0.3, jitter=0.25):
        """A roof face cut into cells, keeping those keep() accepts, each
        kept run given thickness t. poly is a planar 3D polygon (3 or 4
        points, counter-clockwise from above)."""
        P = [Vector(p) for p in poly]
        if len(P) == 3:
            P = [P[0], P[1], P[2], P[2]]
        a, b, c, d = P
        nu = max(1, int(round(max((b - a).length, (c - d).length) / cell)))
        nv = max(1, int(round(max((d - a).length, (c - b).length) / cell)))
        bm = self.bm(key)
        grid = {}

        def pt(i, j):
            u, v = i / nu, j / nv
            p = a.lerp(b, u).lerp(d.lerp(c, u), v)
            if 0 < i < nu and 0 < j < nv:
                # a ragged break, not a ruled line
                h = zlib.crc32(("%d,%d,%s" % (i, j, self.name)).encode()) % 1000 / 1000.0 - 0.5
                g = zlib.crc32(("%d,%d,%s" % (j, i, key)).encode()) % 1000 / 1000.0 - 0.5
                p = p + (b - a) / nu * h * jitter + (d - a) / nv * g * jitter
            return p
        for i in range(nu + 1):
            for j in range(nv + 1):
                grid[i, j] = pt(i, j)
        top, bot, edges = {}, {}, {}
        for i in range(nu):
            for j in range(nv):
                q = [grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]]
                cen = sum(q, Vector()) / 4
                if not keep(cen.x, cen.y, cen.z):
                    continue
                ks = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
                for k in ks:
                    if k not in top:
                        top[k] = bm.verts.new(grid[k])
                        bot[k] = bm.verts.new(grid[k] - Vector((0, 0, t)))
                vt = list(dict.fromkeys(top[k] for k in ks))
                if len(vt) < 3:
                    continue
                try:
                    bm.faces.new(vt)
                    bm.faces.new(list(dict.fromkeys(bot[k] for k in ks))[::-1])
                except ValueError:
                    continue
                for e in zip(ks, ks[1:] + ks[:1]):
                    edges[frozenset(e)] = edges.get(frozenset(e), 0) + 1
                    edges.setdefault(("dir", frozenset(e)), e)
        for e, n in list(edges.items()):
            if isinstance(e, tuple) or n != 1:
                continue
            k1, k2 = edges[("dir", e)]
            if top[k1] is top[k2]:
                continue
            try:
                bm.faces.new((top[k2], top[k1], bot[k1], bot[k2]))
            except ValueError:
                pass

    def cone(self, key, cx, cy, r, z, rise, hole=0.0, t=0.12, seg=32, keep=None, cell=0.35, core=None,
             ry=None, apex=None, rings=None):
        """A conical roof over an eave circle (or ellipse, ry), open at the
        top when hole > 0; apex=(x, y) leans it. With keep, each cell of
        rings x sectors that keep() refuses is left open."""
        ry = r if ry is None else ry
        ax, ay = apex if apex else (cx, cy)
        nr = rings or (max(2, int((r - hole) / cell)) if keep else 1)

        def P(i, k):
            a = 2 * math.pi * i / seg
            e = Vector((cx + r * math.cos(a), cy + ry * math.sin(a), z))
            h = Vector((ax + hole * math.cos(a), ay + hole * math.sin(a), z + rise * (1 - hole / r)))
            return e.lerp(h, k / nr)
        for i in range(seg):
            for k in range(nr):
                q = [P(i, k), P(i + 1, k), P(i + 1, k + 1), P(i, k + 1)]
                if keep is not None:
                    c = sum(q, Vector()) / 4
                    if not keep(c.x, c.y, c.z):
                        continue
                q = [tuple(p) for p in q]
                if hole <= 0 and k == nr - 1:
                    q = q[:3]
                self.slab(key, q, t)
        if core:
            self.cyl(core, (cx + ax) / 2, (cy + ay) / 2, z - 0.01, min(r, ry) * 0.9, rise * 0.8,
                     r1=hole + 0.05, seg=seg, smooth=False)

    @_outward
    def vault(self, key, x0, x1, y0, y1, z, rise, axis="x", t=0.12, seg=10, over=0.1, end=None):
        """A barrel roof along axis: an arch over the short side."""
        if axis == "x":
            span0, span1, a0, a1 = y0, y1, x0 - over, x1 + over
        else:
            span0, span1, a0, a1 = x0, x1, y0 - over, y1 + over
        c = (span0 + span1) / 2
        hw = (span1 - span0) / 2 + over
        arc = []
        for i in range(seg + 1):
            th = math.pi * i / seg
            arc.append((c - hw * math.cos(th), z + rise * math.sin(th)))
        bm = self.bm(key)

        def V(s, a, zz):
            return bm.verts.new((a, s, zz) if axis == "x" else (s, a, zz))
        outer0 = [V(s, a0, zz) for s, zz in arc]
        outer1 = [V(s, a1, zz) for s, zz in arc]
        inner0 = [V(c + (s - c) * (hw - t) / hw, a0, z + (zz - z) * (rise - t) / rise) for s, zz in arc]
        inner1 = [V(c + (s - c) * (hw - t) / hw, a1, z + (zz - z) * (rise - t) / rise) for s, zz in arc]
        for i in range(seg):
            for A, B in ((outer0, outer1), (inner1, inner0)):
                f = bm.faces.new((A[i], A[i + 1], B[i + 1], B[i]))
                f.smooth = True
            bm.faces.new((outer0[i + 1], outer0[i], inner0[i], inner0[i + 1]))
            bm.faces.new((outer1[i], outer1[i + 1], inner1[i + 1], inner1[i]))
        bm.faces.new((outer0[0], inner0[0], inner1[0], outer1[0]))
        bm.faces.new((inner0[-1], outer0[-1], outer1[-1], inner1[-1]))
        if end:
            # the end walls fill the arch
            hw0 = (span1 - span0) / 2
            for aa in ((a0 + over), (a1 - over)):
                pts = [(c - hw0 * math.cos(math.pi * i / seg), z + (rise - t) * math.sin(math.pi * i / seg))
                       for i in range(seg + 1)]
                th = 0.15
                if axis == "x":
                    self._arch_plate(end, pts, aa, th, "x")
                else:
                    self._arch_plate(end, pts, aa, th, "y")

    @_outward
    def gables(self, key, x0, x1, y0, y1, z, rise, axis="x", t=0.2, ends=(True, True)):
        """The triangular end walls of a gable roof over the rectangle, its
        ridge along axis, standing on the walls at z."""
        bm = self.bm(key)
        if axis == "x":
            spots = [(x0 + t / 2, ends[0]), (x1 - t / 2, ends[1])]
            ym = (y0 + y1) / 2
            tri = lambda a: [(a, y0, z), (a, y1, z), (a, ym, z + rise)]  # noqa: E731
            off = Vector((t / 2, 0, 0))
        else:
            spots = [(y0 + t / 2, ends[0]), (y1 - t / 2, ends[1])]
            xm = (x0 + x1) / 2
            tri = lambda a: [(x0, a, z), (x1, a, z), (xm, a, z + rise)]  # noqa: E731
            off = Vector((0, t / 2, 0))
        for a, on in spots:
            if not on:
                continue
            P = [Vector(p) for p in tri(a)]
            f = [bm.verts.new(p - off) for p in P]
            b = [bm.verts.new(p + off) for p in P]
            bm.faces.new(f)
            bm.faces.new(b[::-1])
            for i in range(3):
                j = (i + 1) % 3
                bm.faces.new((f[i], b[i], b[j], f[j]))

    def vault_cells(self, key, x0, x1, y0, y1, z, rise, axis="x", keep=None, cell=0.4, t=0.14, over=0.1, seg=10):
        """A barrel roof cut into cells round the arch and along the axis,
        each a solid block; keep(x, y, z) drops the cells of a broken one."""
        if axis == "x":
            span0, span1, a0, a1 = y0, y1, x0 - over, x1 + over
        else:
            span0, span1, a0, a1 = x0, x1, y0 - over, y1 + over
        c = (span0 + span1) / 2
        hw = (span1 - span0) / 2 + over
        n = max(1, int((a1 - a0) / cell))

        def P(i, s_, k):
            th = math.pi * i / seg
            r = 1.0 if k == 0 else 1.0 - t / max(hw, 1e-3)
            s = c - hw * r * math.cos(th)
            zz = z + (rise * r if k == 0 else rise - t) * math.sin(th)
            a = a0 + (a1 - a0) * s_ / n
            return (a, s, zz) if axis == "x" else (s, a, zz)
        for i in range(seg):
            for j in range(n):
                mid = P(i + 0.5, j + 0.5, 0)
                if keep is not None and not keep(*mid):
                    continue
                bm = self.bm(key)
                o = [bm.verts.new(P(ii, jj, 0)) for ii, jj in ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))]
                u = [bm.verts.new(P(ii, jj, 1)) for ii, jj in ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))]
                fs = [bm.faces.new(o), bm.faces.new(u[::-1])]
                for q in range(4):
                    r_ = (q + 1) % 4
                    fs.append(bm.faces.new((o[q], u[q], u[r_], o[r_])))
                bmesh.ops.recalc_face_normals(bm, faces=fs)

    @_outward
    def _arch_plate(self, key, pts, a, th, axis):
        bm = self.bm(key)

        def V(s, zz, off):
            return bm.verts.new((a + off, s, zz) if axis == "x" else (s, a + off, zz))
        f0 = [V(s, zz, -th / 2) for s, zz in pts]
        f1 = [V(s, zz, th / 2) for s, zz in pts]
        bm.faces.new(f0)
        bm.faces.new(f1[::-1])
        n = len(pts)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((f0[i], f1[i], f1[j], f0[j]))

    # ---- walls ------------------------------------------------------
    def wall(self, key, pts, t, h, z0=0.0, closed=False, cap=None, cap_h=0.1, cap_over=0.05, ext=None):
        """A wall of thickness t along a polyline with mitred corners; open
        ends run on by ext (t/2) so they meet a wall they butt against."""
        pts = [tuple(p) for p in pts]
        ext = t / 2 if ext is None else ext
        self._mitred(key, pts, t, z0, z0 + h, closed, ext)
        if cap:
            self._mitred(cap, pts, t + 2 * cap_over, z0 + h, z0 + h + cap_h, closed, ext + cap_over)

    def _mitred(self, key, pts, t, z0, z1, closed, ext):
        n = len(pts)
        if n < 2:
            return
        P = [Vector(p) for p in pts]
        if not closed:
            e0 = (P[1] - P[0]).normalized()
            e1 = (P[-1] - P[-2]).normalized()
            P[0] = P[0] - e0 * ext
            P[-1] = P[-1] + e1 * ext
        L, R = _offsets(P, t / 2, closed), _offsets(P, -t / 2, closed)
        for i in range(n if closed else n - 1):
            j = (i + 1) % n
            if (P[j] - P[i]).length < 1e-4:
                continue
            self.prism(key, [tuple(L[i]), tuple(L[j]), tuple(R[j]), tuple(R[i])], z0, z1)

    def bwall(self, key, pts, t, prof, closed=False, jag=0.25, step=0.3, z0=0.0, cap=None, frac=False):
        """A broken wall along a polyline. prof lists (s, h) keys along the
        path (s in cells from its start); h <= 0 is a breach. The top is
        ragged by jag, and each standing run is one extruded profile."""
        n = len(pts)
        segs = [(pts[i], pts[(i + 1) % n]) for i in range(n if closed else n - 1)]
        s_base = 0.0
        if frac:
            total = sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in segs)
            prof = [(s * total, h) for s, h in prof]
        prof = sorted(prof)

        def H(s):
            if s <= prof[0][0]:
                return prof[0][1]
            for (s0, h0), (s1, h1) in zip(prof, prof[1:]):
                if s0 <= s <= s1:
                    if h0 <= 0 or h1 <= 0:
                        # a breach has hard edges
                        return h0 if (s - s0) < (s1 - s) else h1
                    return h0 + (h1 - h0) * (s - s0) / max(1e-6, s1 - s0)
            return prof[-1][1]
        corner_h = []
        for (ax, ay), (bx, by) in segs:
            dx, dy = bx - ax, by - ay
            L = math.hypot(dx, dy)
            if L < 1e-4:
                continue
            ux, uy = dx / L, dy / L
            k = max(2, int(L / step))
            ss = [L * i / k for i in range(k + 1)]
            hs = []
            for s in ss:
                hh = H(s_base + s)
                if hh > 0:
                    hh = max(0.08, hh + self.rng.uniform(-jag, jag * 0.5))
                hs.append(hh)
            run = []
            for s, hh in zip(ss, hs):
                if hh > 0.0:
                    run.append((s, hh))
                elif run:
                    self._wall_run(key, ax, ay, ux, uy, t, run, z0, cap)
                    run = []
            if run:
                self._wall_run(key, ax, ay, ux, uy, t, run, z0, cap)
            corner_h.append(((bx, by), hs[-1]))
            s_base += L
        for (cx, cy), hh in corner_h[:-1] if not closed else corner_h:
            if hh > 0:
                self.obox(key, cx, cy, z0 + hh / 2 - 0.02, t * 1.02, t * 1.02, hh - 0.04)
                if cap:
                    self.obox(cap, cx, cy, z0 + hh - 0.03, t * 1.03, t * 1.03, 0.06)

    def auto_wall(self, key, pts, t, h, cap=None, closed=False, step=0.3, levels=(1.0, 0.75, 0.5, 0.3),
                  hidden=None, jag=0.12, want=("stone", "plaster"), rad=1):
        """A free-standing wall whose standing height at each point is read
        off the drawing: the highest level at which its top shows. hidden
        (x, y) -> True keeps it whole where something drawn in front hides it."""
        n = len(pts)
        segs = [(pts[i], pts[(i + 1) % n]) for i in range(n if closed else n - 1)]
        for (ax, ay), (bx, by) in segs:
            L = math.hypot(bx - ax, by - ay)
            k = max(2, int(L / step))
            prof = []
            for i in range(k + 1):
                u = i / k
                x, y = ax + (bx - ax) * u, ay + (by - ay) * u
                hh = 0.0
                if hidden and hidden(x, y):
                    hh = h
                else:
                    for lv in levels:
                        col, row = self.scr(x, y, h * lv)
                        hit = 0
                        for dc in range(-rad, rad + 1):
                            for dr in range(-rad, rad + 1):
                                c = self.at(col + dc, row + dr)
                                if c is not None and self.classify(c) in want:
                                    hit += 1
                        if hit >= (2 * rad + 1) ** 2 * 0.4:
                            hh = h * lv
                            break
                prof.append((L * u, hh))
            self.bwall(key, [(ax, ay), (bx, by)], t, prof, jag=jag, cap=cap)

    def _wall_run(self, key, ax, ay, ux, uy, t, run, z0, cap=None):
        """One standing stretch of a broken wall; its ragged top goes to cap."""
        if len(run) < 2:
            s, hh = run[0]
            run = [(s - 0.1, hh), (s + 0.1, hh)]
        bm = self.bm(key)
        nx, ny = -uy, ux
        outline = [(run[0][0], 0.0), (run[-1][0], 0.0)] + [(s, hh) for s, hh in reversed(run)]
        front = [bm.verts.new((ax + ux * s + nx * t / 2, ay + uy * s + ny * t / 2, z0 + z)) for s, z in outline]
        back = [bm.verts.new((ax + ux * s - nx * t / 2, ay + uy * s - ny * t / 2, z0 + z)) for s, z in outline]
        new = [bm.faces.new(front), bm.faces.new(back[::-1])]
        m = len(outline)
        tops = []
        for i in range(m):
            j = (i + 1) % m
            f = bm.faces.new((front[j], front[i], back[i], back[j]))
            new.append(f)
            if 2 <= i <= m - 2:
                tops.append(f)
        bmesh.ops.recalc_face_normals(bm, faces=new)
        if cap:
            for f in tops:
                k = cap
                if cap in self.soot:
                    # sooted patches along a cap, by a noise over the plan
                    alt, frac = self.soot[cap]
                    c = f.calc_center_median()
                    if self._soot_nz(c.x, c.y) < frac * 2 - 1:
                        k = alt
                cb = self.bm(k)
                cb.faces.new([cb.verts.new(v.co) for v in f.verts])
            bmesh.ops.delete(bm, geom=tops, context="FACES_ONLY")
        bmesh.ops.triangulate(bm, faces=new[:2])

    def window(self, key, x, y, z0, w, h, face="S", depth=0.06, sill=None):
        """A dark opening on a wall face (S, N, E, W) centred at x or y."""
        if face in "SN":
            sgn = -1 if face == "S" else 1
            self.obox(key, x, y + sgn * depth / 2, z0 + h / 2, w, depth * 2, h)
            if sill:
                self.obox(sill, x, y + sgn * depth, z0 - 0.04, w + 0.1, depth * 2, 0.08)
        else:
            sgn = -1 if face == "W" else 1
            self.obox(key, x + sgn * depth / 2, y, z0 + h / 2, depth * 2, w, h)

    # ---- a flat-roofed block ---------------------------------------
    def flat(self, wall, top, pts, h, par_h=0.35, par_t=0.22, z0=0.0, cap=None, keep=None, cell=0.3,
             shell=0.3):
        """A block with a flat roof and a parapet round its rim. With keep,
        the block is a shell of walls and its roof (pts must then be a
        rectangle) keeps only the cells keep() accepts."""
        if _area(pts) < 0:
            pts = pts[::-1]
        if keep is None:
            self.prism(wall, pts, z0, z0 + h)
            self.prism(top, [_inset(pts, i, par_t * 0.5) for i in range(len(pts))], z0 + h - 0.01, z0 + h + 0.015)
        else:
            self.wall(wall, [_inset(pts, i, shell / 2) for i in range(len(pts))], shell, h, z0=z0, closed=True)
            self.grid_slab(top, [(x, y, z0 + h + 0.015) for x, y in pts], 0.18, keep, cell)
        if par_h > 0:
            self.wall(cap or wall, [_inset(pts, i, par_t / 2) for i in range(len(pts))], par_t, par_h,
                      z0=z0 + h, closed=True)

    # ---- props ------------------------------------------------------
    def barrel(self, key, x, y, r=0.3, h=0.7, band=None, lid=None, z0=0.0, rot=None):
        prof = [(r * 0.85, 0.0), (r, h * 0.3), (r, h * 0.7), (r * 0.85, h)]
        if rot is None:
            self.lathe(key, x, y, [(rr, z0 + zz) for rr, zz in prof], seg=10)
            if band:
                for zz in (h * 0.2, h * 0.8):
                    self.cyl(band, x, y, z0 + zz - 0.03, r * 0.98, 0.06, seg=10)
            if lid:
                self.cyl(lid, x, y, z0 + h - 0.01, r * 0.8, 0.03, seg=10, smooth=False)
        else:
            self.cyl(key, x, y, z0, r * 0.95, h, seg=10, rot=rot)

    def ladder(self, key, p0, p1, w=0.5, step=0.3, r=0.05):
        """Two rails from p0 to p1 (3D, the foot and the top) with rungs."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        side = Vector((-d.y, d.x, 0.0))
        side = side.normalized() * (w / 2) if side.length > 1e-6 else Vector((w / 2, 0, 0))
        for sg in (-1, 1):
            self.beam(key, p0 + side * sg, p1 + side * sg, r * 2)
        n = max(1, int(d.length / step))
        for i in range(1, n):
            c = p0.lerp(p1, i / n)
            self.beam(key, c - side, c + side, r * 1.4)

    def stair(self, key, x0, x1, y0, y1, z0, z1, axis="y", n=None, up=1):
        """A solid flight of steps over the rectangle, rising along axis
        (up=1 toward +, -1 toward -) from z0 to z1."""
        span = (y1 - y0) if axis == "y" else (x1 - x0)
        n = n or max(2, int(abs(z1 - z0) / 0.22))
        for i in range(n):
            # one column of masonry per step, side by side
            a, b = (i / n, (i + 1) / n) if up > 0 else (1 - (i + 1) / n, 1 - i / n)
            zt = z0 + (z1 - z0) * (i + 1) / n
            if axis == "y":
                self.box(key, x0, x1, y0 + span * a, y0 + span * b, z0, zt)
            else:
                self.box(key, x0 + span * a, x0 + span * b, y0, y1, z0, zt)

    def leanto(self, key, path, w, z_out, z_in, post=None, wall=None, t=0.15, over=0.15, step=1.6, keep=None,
               piece=0.5, sag=0.0, droop=None, wall_h=None):
        """A shed roof along path, its outer edge on the path at z_out and
        its inner edge w to the right at z_in, on posts (post) with a wall
        (wall) under the outer edge. keep(x, y, z) drops pieces of a broken
        one, sag lets the kept pieces slump at random, and droop(u) sinks
        the inner edge by that much u of the way along the path."""
        P = [Vector(p) for p in path]
        outer = _offsets(P, over, False)
        inner = _offsets(P, -w, False)
        rng = random.Random(self.name + key)
        lens = [(P[i + 1] - P[i]).length for i in range(len(P) - 1)]
        total = sum(lens) or 1.0
        done = 0.0

        def dz(u):
            return droop(u) if droop else 0.0
        for i in range(len(P) - 1):
            a0, a1, b0, b1 = outer[i], outer[i + 1], inner[i], inner[i + 1]
            L = max((a1 - a0).length, (b1 - b0).length)
            n = max(1, int(L / piece)) if (keep or droop or sag) else 1
            for k in range(n):
                u0, u1 = k / n, (k + 1) / n
                q = [a0.lerp(a1, u0), a0.lerp(a1, u1), b0.lerp(b1, u1), b0.lerp(b1, u0)]
                g0, g1 = (done + lens[i] * u0) / total, (done + lens[i] * u1) / total
                z = [z_out - 0.3 * dz(g0), z_out - 0.3 * dz(g1), z_in - dz(g1), z_in - dz(g0)]
                c = sum(q, Vector((0, 0))) / 4
                if keep and not keep(c.x, c.y, (z_out + z_in) / 2):
                    continue
                if sag:
                    d = rng.uniform(0, sag)
                    z = [z[0] - d * 0.3, z[1] - d * 0.3, z[2] - d, z[3] - d]
                pts = [(p.x, p.y, zz) for p, zz in zip(q, z)]
                if _area([(p[0], p[1]) for p in pts]) < 0:
                    pts = pts[::-1]
                self.slab(key, pts, t)
            done += lens[i]
        if wall:
            self.wall(wall, [tuple(p) for p in P], 0.25, wall_h or (z_out - 0.05))
        if post:
            n = max(2, int(total / step) + 1)
            for k in range(n):
                s_ = total * k / (n - 1)
                for i in range(len(P) - 1):
                    seg = lens[i]
                    if s_ <= seg + 1e-6:
                        u = s_ / seg
                        break
                    s_ -= seg
                pp = inner[i].lerp(inner[i + 1], u)
                c = P[i].lerp(P[i + 1], u)
                pp = c + (pp - c) * 0.9
                if keep and not keep(pp.x, pp.y, z_in):
                    continue
                top = z_in - t - dz(k / (n - 1))
                self.post(post, pp.x, pp.y, 0.0, top, 0.14, lean=0.15 * dz(k / (n - 1)))

    @_outward
    def batter(self, key, p0, p1, h, d):
        """A sloped foot to a wall: a wedge along p0-p1, d deep at the
        ground on the right-hand side of the direction, none at height h."""
        bm = self.bm(key)
        a, b = Vector(p0), Vector(p1)
        e = (b - a).normalized()
        n = Vector((e.y, -e.x))
        tri = [(Vector((0, 0)), 0.0), (n * d, 0.0), (Vector((0, 0)), h)]
        va = [bm.verts.new((a.x + o.x, a.y + o.y, z)) for o, z in tri]
        vb = [bm.verts.new((b.x + o.x, b.y + o.y, z)) for o, z in tri]
        bm.faces.new(va)
        bm.faces.new(vb[::-1])
        for i in range(3):
            j = (i + 1) % 3
            bm.faces.new((va[i], vb[i], vb[j], va[j]))

    def pyramid(self, key, x0, x1, y0, y1, z, rise, over=0.25, t=0.1, hole=0.0, keep=None, cell=0.35):
        """A four-sided roof to one apex over the rectangle, the eaves
        carried out by over; hole (0 to 1) leaves a smoke hole at the top."""
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        hx, hy = (x1 - x0) / 2, (y1 - y0) / 2
        k = over / min(hx, hy)
        E = [Vector((cx + sx * hx * (1 + k), cy + sy * hy * (1 + k), z - rise * k)) for sx, sy in
             ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        A = Vector((cx, cy, z + rise))
        for i in range(4):
            a, b = E[i], E[(i + 1) % 4]
            if hole > 0:
                q = [a, b, b.lerp(A, 1 - hole), a.lerp(A, 1 - hole)]
            else:
                q = [a, b, A]
            q = [tuple(p) for p in q]
            if keep is None:
                self.slab(key, q, t)
            else:
                self.grid_slab(key, q, t, keep, cell)

    def planks(self, key, eave0, eave1, top1, top0, w=0.3, gap=0.035, t=0.06, ragged=0.12, keep=None, lift=0.0,
               keys=None):
        """A roof face of boards laid from the eave (eave0-eave1) up to the
        top edge (top0-top1), each its own thin slab, their ends ragged.
        keys cycles the boards through several tones."""
        E0, E1, T0, T1 = (Vector(p) for p in (eave0, eave1, top0, top1))
        n = max(1, int(round((E1 - E0).length / w)))
        rng = random.Random("%s:%s:%d" % (self.name, key, n))
        for i in range(n):
            u0, u1 = (i + gap / w * 0.5) / n, (i + 1 - gap / w * 0.5) / n
            a0, a1 = E0.lerp(E1, u0), E0.lerp(E1, u1)
            b0, b1 = T0.lerp(T1, u0), T0.lerp(T1, u1)
            d = rng.uniform(-ragged, ragged)
            a0, a1 = a0.lerp(b0, -d / max(1e-3, (b0 - a0).length)), a1.lerp(b1, -d / max(1e-3, (b1 - a1).length))
            c = (a0 + a1 + b0 + b1) / 4
            if keep is not None and not keep(c.x, c.y, c.z):
                continue
            k = keys[rng.randrange(len(keys))] if keys else key
            dz = lift + rng.uniform(0, 0.02)
            q = [tuple(p + Vector((0, 0, dz))) for p in (a0, a1, b1, b0)]
            if _area([(p[0], p[1]) for p in q]) < 0:
                q = q[::-1]
            self.slab(k, q, t)

    def crate(self, key, x, y, s=0.6, h=None, yaw=0.0, z0=0.0, edge=None):
        h = h or s
        self.obox(key, x, y, z0 + h / 2, s, s, h, yaw)
        if edge:
            for dz in (0.03, h - 0.03):
                self.obox(edge, x, y, z0 + dz, s + 0.03, s + 0.03, 0.05, yaw)

    def awning(self, key_a, key_b, p0, p1, depth, drop, z, stripes=6, t=0.04, sag=0.0):
        """A striped cloth awning from wall line p0-p1 (at height z) out by
        depth, falling drop over that depth."""
        (ax, ay), (bx, by) = p0, p1
        L = math.hypot(bx - ax, by - ay)
        ux, uy = (bx - ax) / L, (by - ay) / L
        nx, ny = uy, -ux  # out toward the right-hand side of p0->p1
        for i in range(stripes):
            s0, s1 = L * i / stripes, L * (i + 1) / stripes
            q = [(ax + ux * s0, ay + uy * s0, z), (ax + ux * s1, ay + uy * s1, z),
                 (ax + ux * s1 + nx * depth, ay + uy * s1 + ny * depth, z - drop),
                 (ax + ux * s0 + nx * depth, ay + uy * s0 + ny * depth, z - drop)]
            if _area([(p[0], p[1]) for p in q]) < 0:
                q = q[::-1]
            self.slab(key_a if i % 2 == 0 else key_b, q, t)

    # ---- debris -----------------------------------------------------
    def classify(self, rgb):
        r, g, b = rgb
        v = max(r, g, b)
        if v < 0.28:
            return "char"
        if r > 0.28 and r > 1.45 * g and r > 1.7 * b:
            return "tile"
        if r > g * 1.12 and g > b * 1.08 and v < 0.55:
            return "wood"
        if v > 0.62 and (v - min(r, g, b)) < 0.25:
            return "plaster"
        return "stone"

    def class_colours(self, rect=None):
        """The mean drawn colour (sRGB 0-255) of each debris class."""
        import numpy as np
        a = self.pix
        if rect:
            x0, y0, x1, y1 = rect
            a = a[y0:y1, x0:x1]
        a = a.reshape(-1, 4)
        a = a[a[:, 3] > 0.5][:, :3]
        out = {}
        buckets = {}
        for c in a[:: max(1, len(a) // 6000)]:
            buckets.setdefault(self.classify(c), []).append(c)
        for k, v in buckets.items():
            out[k] = tuple(float(x) * 255 for x in np.median(np.array(v), 0))
        return out

    def ruin_palette(self, rect=None, k=9, prefix="deb"):
        """Debris materials from the drawing's own colours: k tones found by
        clustering its pixels. Returns a Palette for scatter(), heap() and
        mound()."""
        import numpy as np
        a = self.pix
        if rect:
            x0, y0, x1, y1 = rect
            a = a[y0:y1, x0:x1]
        a = a.reshape(-1, 4)
        a = a[a[:, 3] > 0.5][:, :3].astype(np.float64)
        a = a[:: max(1, len(a) // 8000)]
        rng = np.random.default_rng(len(self.name))
        # seed the tones across the brightness range, then refine
        order = np.argsort(a.sum(1))
        cen = a[order[np.linspace(0, len(a) - 1, k).astype(int)]].copy()
        for _ in range(20):
            d = ((a[:, None, :] - cen[None]) ** 2).sum(2)
            lab = d.argmin(1)
            for i in range(k):
                if (lab == i).any():
                    cen[i] = a[lab == i].mean(0)
                else:
                    cen[i] = a[rng.integers(len(a))]
        keys = []
        for i, c in enumerate(cen):
            key = "%s_%d" % (prefix, i)
            self.col(key, tuple(float(v) * 255 for v in c))
            keys.append(key)
        return Palette(self, cen, keys)

    @staticmethod
    def roofish(rgb):
        r, g, b = rgb
        return r > 0.15 and r > 1.3 * g and r > 1.4 * b

    def keep_class(self, classes=("tile",), frac=0.45, rad=2, pred=None):
        """keep(x, y, z) for broken roofs: true where most of the drawing
        round that point's picture spot is of the given classes."""
        memo = {}

        def keep(x, y, z):
            col, row = self.scr(x, y, z)
            k = (int(col), int(row))
            if k not in memo:
                hit = tot = 0
                for dc in range(-rad, rad + 1):
                    for dr in range(-rad, rad + 1):
                        c = self.at(col + dc, row + dr)
                        tot += 1
                        if c is not None and (pred(c) if pred else self.classify(c) in classes):
                            hit += 1
                memo[k] = hit >= frac * tot
            return memo[k]
        return keep

    def rafters(self, key, poly, keep, spacing=1.1, w=0.12, seg=0.35, want=("char", "wood")):
        """Bare rafters where a broken roof face is open and the drawing
        shows timber: poly is the face, its first two points the eave."""
        P = [Vector(p) for p in poly]
        n = len(P)
        i = min(range(n), key=lambda i: P[i].z + P[(i + 1) % n].z)
        P = P[i:] + P[:i]
        e0, e1 = P[0], P[1]
        r0, r1 = (P[3], P[2]) if n == 4 else (P[2], P[2])
        n = max(1, int((e1 - e0).length / spacing))
        for i in range(n + 1):
            u = (i + 0.5) / (n + 1)
            a, b = e0.lerp(e1, u), r0.lerp(r1, u)
            k = max(1, int((b - a).length / seg))
            run = None
            for j in range(k):
                p = a.lerp(b, (j + 0.5) / k)
                c = self.at(*self.scr(p.x, p.y, p.z))
                ok = not keep(p.x, p.y, p.z) and c is not None and self.classify(c) in want
                if ok and run is None:
                    run = j
                if (not ok or j == k - 1) and run is not None:
                    jj = j + 1 if ok else j
                    if jj - run >= 2:
                        self.beam(key, a.lerp(b, run / k) - Vector((0, 0, w)), a.lerp(b, jj / k) - Vector((0, 0, w)), w)
                    run = None

    def components(self, rect=None, max_area=400, min_area=3):
        """Loose bits of the drawing: connected opaque pixel groups."""
        import numpy as np
        a = self.pix[..., 3] > 0.5
        h, w = a.shape
        x0, y0, x1, y1 = rect or (0, 0, w, h)
        seen = np.zeros_like(a)
        out = []
        for sy in range(h):
            for sx in range(w):
                if not a[sy, sx] or seen[sy, sx]:
                    continue
                q = deque([(sy, sx)])
                seen[sy, sx] = True
                px = []
                while q:
                    y, x = q.popleft()
                    px.append((y, x))
                    for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                        if 0 <= yy < h and 0 <= xx < w and a[yy, xx] and not seen[yy, xx]:
                            seen[yy, xx] = True
                            q.append((yy, xx))
                if not (min_area <= len(px) <= max_area):
                    continue
                arr = np.array(px, dtype=np.float32)
                cy, cx = arr.mean(0)
                if not (x0 <= cx < x1 and y0 <= cy < y1):
                    continue
                out.append(arr)
        return out

    def scatter(self, keys, rect=None, max_area=300, min_area=3, skip=None, lift=0.0):
        """Every loose bit of the drawing becomes a lying plank, beam, tile
        or stone of its colour, where the drawing shows it."""
        import numpy as np
        n = 0
        for arr in self.components(rect, max_area, min_area):
            cy, cx = arr.mean(0)
            if skip and any(x0 <= cx < x1 and y0 <= cy < y1 for x0, y0, x1, y1 in skip):
                continue
            cols = np.array([self.pix[int(y), int(x), :3] for y, x in arr])
            rgb = cols.mean(0)
            if len(arr) > 70:
                # a clump of bits: a piece for every few pixels of it
                k = int(len(arr) / 35)
                for _ in range(k):
                    y, x = arr[self.rng.randrange(len(arr))]
                    c = self.pix[int(y), int(x), :3]
                    s = self.rng.uniform(0.25, 0.45)
                    yaw = self.rng.uniform(0, math.pi)
                    if self.classify(c) in ("stone", "plaster"):
                        self.stone(keys.key(c), self.X(x), self.Y(y, 0.1), s, s * 0.8, s * 0.5, yaw)
                    else:
                        self.obox(keys.key(c), self.X(x), self.Y(y, 0.05), 0.05 + lift, s * 1.4, s * 0.6, 0.07,
                                  yaw, self.rng.uniform(-0.1, 0.1))
                    n += 1
                continue
            kind = self.classify(rgb)
            key = keys.key(rgb)
            d = arr - arr.mean(0)
            cov = d.T @ d / len(arr) + np.eye(2) * 0.25
            ev, evec = np.linalg.eigh(cov)
            L = math.sqrt(12 * ev[1]) / 16.0
            W = max(math.sqrt(12 * ev[0]) / 16.0, 0.06)
            vy, vx = evec[:, 1]
            yaw = math.atan2(-vy, vx)
            elong = L / W
            if kind in ("stone", "plaster") and elong < 2.2:
                th = min(0.45, 0.55 * W)
                x, y = self.X(cx), self.Y(cy, th * 0.5)
                self.stone(key, x, y, L, W, th, yaw)
            else:
                th = 0.05 if kind == "tile" else min(0.18, max(0.06, 0.6 * W))
                x, y = self.X(cx), self.Y(cy, th * 0.5 + lift)
                tilt = self.rng.uniform(-0.12, 0.12)
                self.obox(key, x, y, th / 2 + lift + abs(tilt) * L * 0.5, L, W, th, yaw, tilt)
            n += 1
        return n

    def heap(self, keys, pts, z_top, n=60, size=(0.25, 0.7), base=None, zfn=None, seed=0):
        """Rubble inside a polygon: a low mound of the base material with
        stones, beams and tiles on it, each coloured as the drawing is where
        it lands. zfn(x, y) gives the mound height there."""
        rng = random.Random("%s:%s" % (self.name, seed))
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        if base:
            self.prism(base, pts, 0.0, z_top * 0.5)
        placed = 0
        tries = 0
        while placed < n and tries < n * 20:
            tries += 1
            x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
            if not _inside(pts, x, y):
                continue
            zb = zfn(x, y) if zfn else z_top * rng.uniform(0.3, 1.0)
            col, row = self.scr(x, y, zb)
            c = self.at(col, row)
            if c is None:
                continue
            kind = self.classify(c)
            key = keys.key(c)
            s = rng.uniform(*size)
            yaw = rng.uniform(0, math.pi)
            zc = zb if zfn else zb * 0.6
            if kind in ("char", "wood") and rng.random() < self.beam_share:
                L = s * rng.uniform(2.0, 3.6)
                w = rng.uniform(0.12, 0.2)
                pitch = rng.uniform(-0.5, 0.5)
                self.obox(key, x, y, zc + abs(math.sin(pitch)) * L * 0.3, L, w, w, yaw, pitch)
            elif kind == "tile":
                self.obox(key, x, y, zc + 0.03, s, s * 0.7, 0.06, yaw, rng.uniform(-0.4, 0.4))
            else:
                self.stone(key, x, y, s, s * rng.uniform(0.6, 1.0), s * rng.uniform(0.4, 0.8), yaw, z=zc)
            placed += 1
        return placed

    def mound(self, keys, pts, zfn, cell=0.5, taper=0.0, seed=0, noise=0.15):
        """A rubble heap as a height field over a polygon, each cell in the
        material of the debris the drawing shows where it lands."""
        rng = random.Random("%s:m%s" % (self.name, seed))
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        x0, y0 = min(xs), min(ys)
        nx = max(1, int((max(xs) - x0) / cell) + 1)
        ny = max(1, int((max(ys) - y0) / cell) + 1)
        Z = {}

        def z_at(i, j):
            if (i, j) not in Z:
                x, y = x0 + i * cell, y0 + j * cell
                z = zfn(x, y)
                if taper > 0:
                    z *= min(1.0, _edge_dist(pts, x, y) / taper)
                Z[i, j] = max(0.0, z + rng.uniform(-noise, noise) * (1 if z > 0.05 else 0))
            return Z[i, j]
        for i in range(nx):
            for j in range(ny):
                cx, cy = x0 + (i + 0.5) * cell, y0 + (j + 0.5) * cell
                if not _inside(pts, cx, cy):
                    continue
                q = [(x0 + a * cell, y0 + b * cell, z_at(a, b)) for a, b in ((i, j), (i + 1, j), (i + 1, j + 1),
                                                                             (i, j + 1))]
                zc = sum(p[2] for p in q) / 4
                c = self.mean_at(*self.scr(cx, cy, zc), rad=int(cell * 8))
                bm = self.bm(keys.key(c) if c is not None else keys["stone"])
                bm.faces.new([bm.verts.new(p) for p in q])

    def rubble(self, keys, pts, ztop, n=60, size=(0.25, 0.6), taper=0.8, seed=0, cell=0.5, mound=True):
        """A heap: the mound and loose stones, beams and tiles on it."""
        rng = random.Random("%s:r%s" % (self.name, seed))
        bumps = [(rng.uniform(min(p[0] for p in pts), max(p[0] for p in pts)),
                  rng.uniform(min(p[1] for p in pts), max(p[1] for p in pts)), rng.uniform(0.6, 1.0))
                 for _ in range(6)]

        def zfn(x, y):
            b = max(k * math.exp(-((x - bx) ** 2 + (y - by) ** 2) / 6.0) for bx, by, k in bumps)
            return ztop * (0.55 + 0.45 * b)
        if mound:
            self.mound(keys, pts, zfn, cell=cell, taper=taper, seed=seed)
        return self.heap(keys, pts, ztop, n=n, size=size, seed=seed,
                         zfn=lambda x, y: zfn(x, y) * min(1.0, _edge_dist(pts, x, y) / max(taper, 1e-3)))

    # ---- smooth breaks ----------------------------------------------
    def noise(self, seed=0, scale=1.0, n=4):
        """noise(x, y): smooth ripples of about scale cells, -1 to 1."""
        rng = random.Random("%s:n%s" % (self.name, seed))
        waves = []
        for i in range(n):
            a = rng.uniform(0, 2 * math.pi)
            k = rng.uniform(0.8, 1.3) * (1.7 ** i) * 2 * math.pi / (scale * 4)
            waves.append((math.cos(a) * k, math.sin(a) * k, rng.uniform(0, 2 * math.pi), 0.6 ** i))
        tot = sum(w[3] for w in waves)
        return lambda x, y: sum(w * math.sin(kx * x + ky * y + p) for kx, ky, p, w in waves) / tot

    def blobs(self, spots, rag=0.3, seed=0):
        """A field over the plan that is negative inside ragged round holes;
        spots are (x, y, r) or (x, y, rx, ry) in cells."""
        rng = random.Random("%s:b%s" % (self.name, seed))
        B = []
        for s in spots:
            ry = s[3] if len(s) > 3 else s[2]
            B.append((s[0], s[1], s[2], ry, [rng.uniform(0, 2 * math.pi) for _ in range(3)]))

        def f(x, y, z=0.0):
            best = 1e9
            for bx, by, rx, ry, ph in B:
                dx, dy = (x - bx) / rx, (y - by) / ry
                th = math.atan2(dy, dx)
                k = 1 + rag * (0.55 * math.sin(3 * th + ph[0]) + 0.3 * math.sin(5 * th + ph[1])
                               + 0.15 * math.sin(8 * th + ph[2]))
                best = min(best, (math.hypot(dx, dy) / k - 1.0) * min(rx, ry))
            return best
        return f

    def blobs_px(self, spots, rag=0.3, seed=0):
        """blobs() for spots read off the drawing: (col, row, z, r) or
        (col, row, z, rx, ry), r in cells."""
        return self.blobs([(self.X(s[0]), self.Y(s[1], s[2])) + tuple(s[3:]) for s in spots], rag, seed)

    def region(self, pts, rag=0.15, seed=0, scale=0.8):
        """A field that is positive inside the outline, its edge rippled."""
        nz = self.noise("r%s" % seed, scale)

        def f(x, y, z=0.0):
            d = _edge_dist(pts, x, y)
            return (d if _inside(pts, x, y) else -d) + rag * nz(x, y)
        return f

    def pic_field(self, pred, sigma=2.0, bias=0.5, clear=0.0):
        """A field read off the drawing: the blurred share of pixels near a
        point's picture spot that pred() accepts, less bias. Its zero line
        is a smooth ragged outline of what the drawing shows; clear pixels
        count as clear (0 or 1)."""
        import numpy as np
        a = self.pix
        h, w = a.shape[:2]
        M = np.zeros((h, w), np.float32)
        for r in range(h):
            for c in range(w):
                M[r, c] = (1.0 if pred(a[r, c, :3]) else 0.0) if a[r, c, 3] > 0.5 else clear
        k = max(1, int(sigma * 3))
        g = np.exp(-0.5 * (np.arange(-k, k + 1) / max(sigma, 1e-3)) ** 2)
        g /= g.sum()
        P = np.pad(M, k, mode="edge")
        P = np.apply_along_axis(lambda v: np.convolve(v, g, "valid"), 1, P)
        P = np.apply_along_axis(lambda v: np.convolve(v, g, "valid"), 0, P)

        def f(x, y, z=0.0):
            c, r = self.scr(x, y, z)
            c, r = min(max(c - 0.5, 0.0), w - 1.001), min(max(r - 0.5, 0.0), h - 1.001)
            c0, r0 = int(c), int(r)
            u, v = c - c0, r - r0
            top = P[r0, c0] * (1 - u) + P[r0, c0 + 1] * u
            bot = P[r0 + 1, c0] * (1 - u) + P[r0 + 1, c0 + 1] * u
            return float(top * (1 - v) + bot * v) - bias
        return f

    def band(self, key, poly, t, field, width=0.12, lift=0.012, cell=0.15):
        """A thin slab on a plane polygon where 0 < field < width: the
        scorched rim round a burnt hole."""
        P = [(p[0], p[1], p[2] + lift) for p in poly]
        self.cut_slab(key, P, t, lambda x, y, z=0.0: min(field(x, y, z), width - field(x, y, z)), cell)

    @staticmethod
    def fmin(*fs):
        """The field kept where all the given fields keep."""
        return lambda x, y, z=0.0: min(f(x, y, z) for f in fs)

    @staticmethod
    def fmax(*fs):
        """The field kept where any of the given fields keeps."""
        return lambda x, y, z=0.0: max(f(x, y, z) for f in fs)

    @_outward
    def cut_grid(self, key, G, nu, nv, field, off, wrap=False, bottom=True):
        """A surface patch through grid points G[i, j], kept where field(x,
        y, z) > 0 with its break traced smoothly between the grid points
        (marching squares), thickened by off(p). wrap joins column nu to 0."""
        bm = self.bm(key)

        def K(k):
            return (k[0] % nu, k[1]) if wrap else k
        val = {}
        for k, p in G.items():
            if K(k) not in val:
                val[K(k)] = field(p.x, p.y, p.z)
        top, bot = {}, {}

        def vert(k, p):
            if k not in top:
                top[k] = bm.verts.new(p)
                bot[k] = bm.verts.new(p + off(p))
            return k
        edges = {}
        for i in range(nu):
            for j in range(nv):
                ks = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
                poly = []
                for a in range(4):
                    ka, kb = ks[a], ks[(a + 1) % 4]
                    va, vb = val[K(ka)], val[K(kb)]
                    if va > 0:
                        poly.append(vert(K(ka), G[ka]))
                    if (va > 0) != (vb > 0):
                        k0, k1 = sorted((K(ka), K(kb)))
                        g0, g1 = (G[ka], G[kb]) if K(ka) == k0 else (G[kb], G[ka])
                        u = max(0.03, min(0.97, val[k0] / (val[k0] - val[k1])))
                        poly.append(vert(("e", k0, k1), g0.lerp(g1, u)))
                poly = list(dict.fromkeys(poly))
                if len(poly) < 3:
                    continue
                try:
                    bm.faces.new([top[k] for k in poly])
                    if bottom:
                        # an underside no camera above can see is left off when asked
                        bm.faces.new([bot[k] for k in poly][::-1])
                except ValueError:
                    continue
                for a in range(len(poly)):
                    e = (poly[a], poly[(a + 1) % len(poly)])
                    fe = frozenset(e)
                    edges[fe] = edges.get(fe, 0) + 1
                    edges.setdefault(("dir", fe), e)
        for e, n in list(edges.items()):
            if isinstance(e, tuple) or n != 1:
                continue
            k1, k2 = edges[("dir", e)]
            try:
                bm.faces.new((top[k2], top[k1], bot[k1], bot[k2]))
            except ValueError:
                pass

    def cut_slab(self, key, poly, t, field, cell=0.15, bottom=True):
        """A planar polygon (3 or 4 3D points) given thickness t straight
        down, kept where field > 0 with a smooth ragged break."""
        P = [Vector(p) for p in poly]
        if len(P) == 3:
            P = [P[0], P[1], P[2], P[2]]
        a, b, c, d = P
        nu = max(1, int(round(max((b - a).length, (c - d).length) / cell)))
        nv = max(1, int(round(max((d - a).length, (c - b).length) / cell)))
        G = {}
        for i in range(nu + 1):
            for j in range(nv + 1):
                u, v = i / nu, j / nv
                G[i, j] = a.lerp(b, u).lerp(d.lerp(c, u), v)
        down = Vector((0, 0, -t))
        self.cut_grid(key, G, nu, nv, field, lambda p: down, bottom=bottom)

    def cut_roof(self, key, x0, x1, y0, y1, z, rise, axis="x", ends=("hip", "hip"), over=0.2, t=0.12,
                 field=None, cell=0.15, rafters=None, rafter_w=0.12, spacing=0.9):
        """roof() with its holes cut smoothly by field; rafters (a material)
        run up the open parts of each face."""
        self.roof_log.append((x0, x1, y0, y1, z, rise, axis, ends))
        if axis == "y":
            sw = lambda p: (p[1], p[0], p[2])  # noqa: E731
            faces, _ = _roof_faces(y0, y1, x0, x1, z, rise, ends, over, t)
            faces = [[sw(p) for p in f][::-1] for f in faces]
        else:
            faces, _ = _roof_faces(x0, x1, y0, y1, z, rise, ends, over, t)
        for f in faces:
            if field is None:
                self.slab(key, f, t)
            else:
                self.cut_slab(key, f, t, field, cell)
                if rafters:
                    self.open_rafters(rafters, f, field, spacing=spacing, w=rafter_w)

    def gabled_ruin(self, wall, roof, x0, x1, y0, y1, ze, rise, axis, field, keys=None, rafters="char",
                    t=0.3, spacing=0.7, over=0.18, dz=0.6, ends=("gable", "gable")):
        """A gabled block whose walls stand and whose roof is holed where
        field <= 0: bare rafters over the holes and, under them, the fallen
        roof in the drawing's own tones (keys, from ruin_palette)."""
        box = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
        self.wall(wall, [_inset(box, i, t / 2) for i in range(4)], t, ze, closed=True)
        self.gables(wall, x0, x1, y0, y1, ze, rise, axis, t=t * 0.8,
                    ends=(ends[0] == "gable", ends[1] == "gable"))
        self.cut_roof(roof, x0, x1, y0, y1, ze, rise, axis, ends, over=over, field=field, rafters=rafters,
                      spacing=spacing, rafter_w=0.12)
        if keys is not None:
            self.fallen(keys, [_inset(box, i, t) for i in range(4)], dz=dz, cell=0.4)

    def open_rafters(self, key, poly, field, spacing=0.9, w=0.12, seg=0.2):
        """Bare rafters from eave to ridge wherever a roof face is open."""
        P = [Vector(p) for p in poly]
        n = len(P)
        i = min(range(n), key=lambda i: P[i].z + P[(i + 1) % n].z)
        P = P[i:] + P[:i]
        e0, e1 = P[0], P[1]
        r0, r1 = (P[3], P[2]) if n == 4 else (P[2], P[2])
        m = max(1, int((e1 - e0).length / spacing))
        for i in range(m + 1):
            u = (i + 0.5) / (m + 1)
            a, b = e0.lerp(e1, u), r0.lerp(r1, u)
            k = max(2, int((b - a).length / seg))
            opened = [field(*a.lerp(b, (j + 0.5) / k)) <= 0 for j in range(k)]
            for j0, j1 in _runs(opened):
                # each bare length runs a little into the kept roof at its ends
                u0, u1 = max(0, j0 - 1) / k, min(k, j1 + 1) / k
                self.beam(key, a.lerp(b, u0) - Vector((0, 0, w * 0.8)), a.lerp(b, u1) - Vector((0, 0, w * 0.8)), w)

    def cut_cone(self, key, cx, cy, r, z, rise, hole=0.0, t=0.12, seg=40, rings=10, field=None, ry=None,
                 apex=None):
        """cone() cut smoothly by field."""
        ry = r if ry is None else ry
        ax, ay = apex if apex else (cx, cy)
        hole = max(hole, 0.02)
        G = {}
        for i in range(seg + 1):
            a = 2 * math.pi * i / seg
            e = Vector((cx + r * math.cos(a), cy + ry * math.sin(a), z))
            h = Vector((ax + hole * math.cos(a), ay + hole * math.sin(a), z + rise * (1 - hole / r)))
            for k in range(rings + 1):
                G[i, k] = e.lerp(h, k / rings)
        down = Vector((0, 0, -t))
        self.cut_grid(key, G, seg, rings, field or (lambda x, y, zz=0.0: 1.0), lambda p: down, wrap=True)
        self.roof_log.append(("cone", cx, cy, r, z, rise))

    def cone_rafters(self, key, cx, cy, r, z, rise, field, n=18, w=0.12, hole=0.0, apex=None, ry=None, seg=0.2):
        """Bare charred rafters along a cone's slope wherever it is open."""
        ry = r if ry is None else ry
        ax, ay = apex if apex else (cx, cy)
        top = Vector((ax, ay, z + rise * (1 - max(hole, 0.02) / r)))
        for i in range(n):
            a = 2 * math.pi * (i + 0.5) / n
            e = Vector((cx + r * 0.97 * math.cos(a), cy + ry * 0.97 * math.sin(a), z))
            k = max(2, int((top - e).length / seg))
            opened = [field(*e.lerp(top, (j + 0.5) / k)) <= 0 for j in range(k)]
            for j0, j1 in _runs(opened):
                u0, u1 = max(0, j0 - 1) / k, min(k, j1 + 1) / k
                d = Vector((0, 0, -w * 0.9))
                self.beam(key, e.lerp(top, u0) + d, e.lerp(top, u1) + d, w)

    def cut_vault(self, key, x0, x1, y0, y1, z, rise, axis="x", field=None, t=0.14, over=0.1, seg=14, cell=0.2):
        """A barrel roof whose break is cut smoothly by field."""
        if axis == "x":
            span0, span1, a0, a1 = y0, y1, x0 - over, x1 + over
        else:
            span0, span1, a0, a1 = x0, x1, y0 - over, y1 + over
        c = (span0 + span1) / 2
        hw = (span1 - span0) / 2 + over
        n = max(1, int((a1 - a0) / cell))
        G = {}
        for i in range(seg + 1):
            th = math.pi * i / seg
            s, zz = c - hw * math.cos(th), z + rise * math.sin(th)
            for j in range(n + 1):
                a = a0 + (a1 - a0) * j / n
                G[i, j] = Vector((a, s, zz) if axis == "x" else (s, a, zz))

        def off(p):
            s, zz = (p.y, p.z) if axis == "x" else (p.x, p.z)
            d = Vector((c - s, z - zz))
            d = d.normalized() * t if d.length > 1e-6 else Vector((0, -t))
            d = Vector((0, d.x, d.y)) if axis == "x" else Vector((d.x, 0, d.y))
            # the inner face of an arch: toward its axis, along the rise
            return Vector((d.x, d.y, -abs(d.z)))
        self.cut_grid(key, G, seg, n, field or (lambda x, y, zz=0.0: 1.0), off)

    # ---- burnt rubble ------------------------------------------------
    def mix(self, rect=None, pts=None, z=0.5):
        """The share of charred timber, red tile, timber and stone in the
        drawing over a sprite rectangle, or over a plan outline."""
        import numpy as np
        if rect is None and pts is not None:
            cs = [self.scr(x, y, z) for x, y in pts]
            rect = (int(min(c for c, _ in cs)), int(min(r for _, r in cs)), int(max(c for c, _ in cs)) + 1,
                    int(max(r for _, r in cs)) + 1)
        a = self.pix
        if rect:
            x0, y0, x1, y1 = rect
            a = a[max(0, y0):max(0, y1), max(0, x0):max(0, x1)]
        a = a.reshape(-1, 4)
        a = a[a[:, 3] > 0.5][:, :3]
        out = {"char": 0.0, "tile": 0.0, "wood": 0.0, "stone": 0.0}
        for c in a[:: max(1, len(a) // 4000)]:
            k = self.classify(c)
            out["stone" if k == "plaster" else k] += 1
        tot = sum(out.values()) or 1.0
        return {k: v / tot for k, v in out.items()}

    def rubble_tones(self, char=(40, 32, 26), tile=(120, 50, 35), wood=(92, 62, 42), stone=None, ash=None,
                     rect=None):
        """The rubble materials: charred beams, tile shards, timber, stones
        and the ash of the heap; stone and ash come from the drawing's own
        mid tones when not given."""
        import numpy as np
        if stone is None or ash is None:
            a = self.pix
            if rect:
                x0, y0, x1, y1 = rect
                a = a[y0:y1, x0:x1]
            a = a.reshape(-1, 4)
            a = a[a[:, 3] > 0.5][:, :3] * 255
            L = a @ np.array([0.299, 0.587, 0.114])
            if ash is None:
                sel = a[(L > np.percentile(L, 30)) & (L < np.percentile(L, 60))]
                ash = tuple(sel.mean(0)) if len(sel) else (60, 52, 44)
            if stone is None:
                sel = a[(L > np.percentile(L, 75)) & (L < np.percentile(L, 95))]
                stone = tuple(sel.mean(0)) if len(sel) else (120, 110, 94)
        self.col("r_char", char)
        self.col("r_char2", tuple(v * 1.25 for v in char))
        self.col("r_tile", tile)
        self.col("r_tile2", tuple(v * 0.8 for v in tile))
        self.col("r_wood", wood)
        self.col("r_stone", stone)
        self.col("r_stone2", tuple(v * 0.78 for v in stone))
        self.col("r_stone3", tuple(min(255, v * 1.18) for v in stone))
        self.col("r_ash", ash)
        self.col("r_ash2", tuple(v * 0.8 for v in ash))
        self.col("r_ash3", tuple(min(255, v * 1.2) for v in ash))
        return dict(char=("r_char", "r_char2"), tile=("r_tile", "r_tile2"), wood=("r_wood",),
                    stone=("r_stone", "r_stone2", "r_stone3"), ash=("r_ash", "r_ash2", "r_ash3"))

    def debris(self, T, pts, H, lo=0.3, hi=0.6, bank=0.8, cell=0.45, seed=0, density=1.8, mix=None,
               size=(0.3, 0.75), zfn=None, taper=0.0, lumps=0.35, beam_len=(0.8, 2.2), beam_w=(0.14, 0.26),
               maxn=260, where=None, bed=True, bed_where=None):
        """Burnt rubble filling an outline: a lumpy mound banked up against
        its edges to hi of the wall height H and lo of it in the middle
        (or zfn(x, y)), strewn with charred beams, red tile shards, timbers
        and stones in the proportions of mix (see mix()). T is from
        rubble_tones()."""
        rng = random.Random("%s:d%s" % (self.name, seed))
        nz = self.noise("d%s" % seed, 0.8)
        mix = mix or dict(PIECES)
        if zfn is None:
            def zfn(x, y):
                d = _edge_dist(pts, x, y)
                z = H * (lo + (hi - lo) * math.exp(-d / bank))
                return z * (1 + lumps * nz(x, y))
        if taper > 0:
            base = zfn
            zfn = lambda x, y: base(x, y) * min(1.0, _edge_dist(pts, x, y) / taper)  # noqa: E731
        ash = T["ash"]
        pick = self.noise("a%s" % seed, 0.7)
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        x0, y0 = min(xs), min(ys)
        nx, ny = int((max(xs) - x0) / cell) + 1, int((max(ys) - y0) / cell) + 1
        Z = {}

        def z_at(i, j):
            if (i, j) not in Z:
                x, y = x0 + i * cell, y0 + j * cell
                Z[i, j] = max(0.0, zfn(x, y) + rng.uniform(-0.06, 0.06))
            return Z[i, j]
        bw = bed_where or where
        for i in range(nx if bed else 0):
            for j in range(ny):
                cx, cy = x0 + (i + 0.5) * cell, y0 + (j + 0.5) * cell
                if not _inside(pts, cx, cy) or (bw and not bw(cx, cy)):
                    continue
                q = [(x0 + a * cell, y0 + b * cell, z_at(a, b)) for a, b in ((i, j), (i + 1, j), (i + 1, j + 1),
                                                                             (i, j + 1))]
                v = pick(cx, cy)
                k = ash[0] if -0.25 < v < 0.3 else ash[1] if v <= -0.25 else ash[2]
                bm = self.bm(k)
                bm.faces.new([bm.verts.new(p) for p in q])
        area = abs(_area(pts))
        n = min(maxn, int(area * density))
        kinds = list(mix.items())
        tot = sum(w for _, w in kinds) or 1.0
        placed = tries = 0
        while placed < n and tries < n * 30:
            tries += 1
            x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
            if not _inside(pts, x, y) or (where and not where(x, y)):
                continue
            u = rng.uniform(0, tot)
            for kind, w in kinds:
                u -= w
                if u <= 0:
                    break
            zb = zfn(x, y)
            yaw = rng.uniform(0, math.pi)
            if kind in ("char", "wood"):
                L = rng.uniform(*beam_len)
                w = rng.uniform(*beam_w)
                pitch = rng.uniform(-0.45, 0.45)
                key = rng.choice(T[kind])
                self.obox(key, x, y, zb + abs(math.sin(pitch)) * L * 0.25 + w * 0.3, L, w, w, yaw, pitch,
                          rng.uniform(-0.3, 0.3))
            elif kind == "tile":
                s = rng.uniform(size[0], size[1] * 0.9)
                self.obox(rng.choice(T["tile"]), x, y, zb + 0.04, s, s * rng.uniform(0.5, 0.9), 0.05, yaw,
                          rng.uniform(-0.5, 0.5), rng.uniform(-0.4, 0.4))
            else:
                s = rng.uniform(*size)
                self.stone(rng.choice(T["stone"]), x, y, s, s * rng.uniform(0.6, 1.0), s * rng.uniform(0.4, 0.8),
                           yaw, z=zb + s * 0.1)
            placed += 1
        return placed

    def shard(self, key, cx, cy, cz, sx, sy, yaw=0.0, pitch=0.0, roll=0.0, t=0.08, rag=0.25, n=8, seed=0):
        """A ragged flat piece (a fallen slab of roof, a board panel, a
        torn cloth): an irregular outline sx by sy, tilted and turned."""
        rng = random.Random("%s:s%s:%s" % (self.name, key, seed))
        M = _mat4((cx, cy, cz), (yaw, pitch, roll))
        pts = []
        for i in range(n):
            a = 2 * math.pi * (i + rng.uniform(-0.3, 0.3)) / n
            # a squarish outline with bites out of it
            c, s_ = math.cos(a), math.sin(a)
            k = 1.0 / max(abs(c), abs(s_)) ** 0.6
            rr = k * (1 - rng.uniform(0, rag))
            pts.append(M @ Vector((c * rr * sx / 2, s_ * rr * sy / 2, 0.0)))
        if _area([(p.x, p.y) for p in pts]) < 0:
            pts = pts[::-1]
        self.slab(key, [tuple(p) for p in pts], t)

    def tile_slab(self, keys, cx, cy, cz, sx, sy, yaw=0.0, pitch=0.0, roll=0.0, course=0.3, t=0.07, rag=0.3,
                  seed=0):
        """A fallen piece of tiled roof: courses of tiles across its length
        sy, each a little ragged at the ends and lapped over the one below,
        in the tones of keys by turns."""
        rng = random.Random("%s:t%s" % (self.name, seed))
        M = _mat4((cx, cy, cz), (yaw, pitch, roll))
        n = max(2, int(round(sy / course)))
        d = sy / n
        for i in range(n):
            w = sx * (1.0 - rng.uniform(0.0, rag))
            u = rng.uniform(-0.5, 0.5) * (sx - w)
            v = -sy / 2 + (i + 0.5) * d
            C = M @ _mat4((u, v, i * 0.012), (0.0, 0.0, -0.08), (w, d * 1.15, t))
            bmesh.ops.create_cube(self.bm(keys[i % len(keys)]), size=1.0, matrix=C)

    def drape(self, keys, p0, u, v, nu, nv, zfn, field=None, t=0.03, stripes_along="u"):
        """A cloth over the grid p0 + u*i/nu + v*j/nv, its height z0 +
        zfn(i/nu, j/nv), striped across keys, torn where field <= 0."""
        p0, u, v = Vector(p0), Vector(u), Vector(v)
        grid = {}
        for i in range(nu + 1):
            for j in range(nv + 1):
                a, b = i / nu, j / nv
                p = p0 + u * a + v * b
                grid[i, j] = Vector((p.x, p.y, p0.z + zfn(a, b)))
        keep = field or (lambda x, y, z=0.0: 1.0)
        down = Vector((0, 0, -t))
        n = nu if stripes_along == "u" else nv
        for c in range(n):
            # one stripe of cloth per column (or row) of the grid
            if stripes_along == "u":
                Gc = {(0, j): grid[c, j] for j in range(nv + 1)}
                Gc.update({(1, j): grid[c + 1, j] for j in range(nv + 1)})
                self.cut_grid(keys[c % len(keys)], Gc, 1, nv, keep, lambda p: down)
            else:
                Gc = {(i, 0): grid[i, c] for i in range(nu + 1)}
                Gc.update({(i, 1): grid[i, c + 1] for i in range(nu + 1)})
                self.cut_grid(keys[c % len(keys)], Gc, nu, 1, keep, lambda p: down)

    # ---- finishing --------------------------------------------------
    def done(self):
        """One object per material; painted groups take the picture."""
        objs = []
        spr = None
        for key, bm in self.groups.items():
            if not bm.verts:
                bm.free()
                continue
            # a broken roof's kept cells merge back into big faces
            bmesh.ops.dissolve_limit(bm, angle_limit=0.01, verts=bm.verts[:], edges=bm.edges[:])
            me = bpy.data.meshes.new(self.name + "_" + key)
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(self.name + "_" + key, me)
            bpy.context.collection.objects.link(ob)
            if key in self.paint_keys:
                if spr is None:
                    spr = carve.Sprite(self.png)
                    spr.opaque()
                    if abs(self.paint_gain - carve.ALBEDO_GAIN) > 1e-6:
                        # the review copy shows the lift the game will use
                        import numpy as np
                        a = np.array(spr.img.pixels[:], dtype=np.float32).reshape(-1, 4)
                        a[:, :3] = np.clip(a[:, :3] * (self.paint_gain / carve.ALBEDO_GAIN), 0.0, 1.0)
                        spr.img.pixels[:] = a.ravel()
                lo = [min(v.co[i] for v in me.vertices) for i in range(2)]
                hi = [max(v.co[i] for v in me.vertices) for i in range(2)]
                ob["box"] = (lo[0], hi[0], lo[1], hi[1])
                rr = {"name": self.name + "_" + key, "sprite": {"w": self.w, "h": self.h,
                                                                "hotspot": [self.hx, self.hy]},
                      "footprint": self.r["footprint"], "height": 0}
                carve.paint(ob, rr, spr)
                del ob["box"]
                # the game paints this part from the player's own files
                mat = me.materials[0]
                mat["okPaint"] = dict(kind="feature", name=self.r.get("seq", self.name), world=self.r["world"],
                                      gain=round(self.paint_gain, 3), bleed=True, alpha="opaque", size=[self.w, self.h])
                p = self.pal.get(key, dict(rgb=(150, 150, 150)))
                mat["okFallback"] = list(lin(p["rgb"]))
            else:
                p = self.pal.get(key, dict(rgb=(150, 150, 150), rough=0.9, metal=0.0))
                me.materials.append(hk.pbr(self.name + "_" + key, lin(p["rgb"]), p["rough"], p["metal"]))
            objs.append(ob)
            self.tris[key] = sum(len(p.vertices) - 2 for p in me.polygons)
        self.groups = {}
        if self.zk != 1.0 and objs:
            self._squat(objs)
        # a painted part goes first: the joined mesh keeps the first part's UV layer active
        objs.sort(key=lambda o: 0 if o.data.uv_layers else 1)
        return objs

    def _squat(self, objs):
        """Heights times zk, depth stretched back from the front so the top
        back edge keeps its place in the classic view. Runs after painting,
        so painted parts keep the picture they were fitted to."""
        vs = [v.co for ob in objs for v in ob.data.vertices]
        y0, y1 = min(c.y for c in vs), max(c.y for c in vs)
        H = max(c.z for c in vs)
        k = self.zk
        s = 1.0 + H * (1.0 - k) / (2.0 * max(1.0, y1 - y0))
        for c in vs:
            if c.z > 0:
                c.z *= k
            c.y = y0 + (c.y - y0) * s
        for ob in objs:
            ob.data.update()


class Palette:
    """Debris tones: key(rgb) is the material nearest a drawn colour, and
    kind(rgb) says whether a piece there is timber, tile or stone."""

    def __init__(self, m, cen, keys):
        self.m, self.cen, self.keys = m, cen, keys

    def key(self, rgb):
        import numpy as np
        return self.keys[int(((self.cen - np.asarray(rgb)[None]) ** 2).sum(1).argmin())]

    def kind(self, rgb):
        return self.m.classify(rgb)

    def get(self, kind, default=None):
        # the plain tone for a kind, for callers that ask by name
        best = {"char": 0, "stone": len(self.keys) // 2, "plaster": len(self.keys) - 1}
        return self.keys[best.get(kind, len(self.keys) // 2)]

    def __getitem__(self, kind):
        return self.get(kind)


def _runs(flags):
    """(start, end) of each run of true flags."""
    out, j0 = [], None
    for j, f in enumerate(list(flags) + [False]):
        if f and j0 is None:
            j0 = j
        elif not f and j0 is not None:
            out.append((j0, j))
            j0 = None
    return out


def _area(pts):
    return 0.5 * sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1]
                     for i in range(len(pts)))


def _inside(pts, x, y):
    c = False
    n = len(pts)
    for i in range(n):
        (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
        if (y0 > y) != (y1 > y) and x < (x1 - x0) * (y - y0) / (y1 - y0 + 1e-12) + x0:
            c = not c
    return c


def _edge_dist(pts, x, y):
    best = 1e9
    n = len(pts)
    for i in range(n):
        (ax, ay), (bx, by) = pts[i], pts[(i + 1) % n]
        dx, dy = bx - ax, by - ay
        L2 = dx * dx + dy * dy or 1e-9
        u = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / L2))
        best = min(best, math.hypot(ax + u * dx - x, ay + u * dy - y))
    return best


def _offsets(P, d, closed):
    """Each point of a polyline moved d to its left, corners mitred."""
    n = len(P)
    out = []
    for i in range(n):
        ns = []
        if closed or i > 0:
            e = (P[i] - P[i - 1]).normalized()
            ns.append(Vector((-e.y, e.x)))
        if closed or i < n - 1:
            e = (P[(i + 1) % n] - P[i]).normalized()
            ns.append(Vector((-e.y, e.x)))
        if len(ns) == 1 or (ns[0] + ns[1]).length < 1e-6:
            out.append(P[i] + ns[0] * d)
            continue
        b = (ns[0] + ns[1]).normalized()
        out.append(P[i] + b * (d / max(0.3, b.dot(ns[0]))))
    return out


def _inset(pts, i, d):
    """Vertex i of a counter-clockwise polygon moved d inward."""
    n = len(pts)
    p0, p1, p2 = Vector(pts[i - 1]), Vector(pts[i]), Vector(pts[(i + 1) % n])
    e0 = (p1 - p0).normalized()
    e1 = (p2 - p1).normalized()
    n0 = Vector((-e0.y, e0.x))
    n1 = Vector((-e1.y, e1.x))
    bis = n0 + n1
    if bis.length < 1e-6:
        return tuple(p1 + n0 * d)
    bis.normalize()
    k = d / max(0.3, bis.dot(n0))
    return tuple(p1 + bis * k)


def _roof_faces(x0, x1, y0, y1, z, rise, ends, over, t):
    """Top faces (with overhang) and the core solid's faces of a roof with
    its ridge along x."""
    ex0, ex1, ey0, ey1 = x0 - over, x1 + over, y0 - over, y1 + over
    yc = (y0 + y1) / 2
    hw = (y1 - y0) / 2
    zr = z + rise
    ze = z - rise * over / max(hw, 1e-3)  # the slope carries on past the wall
    rx0 = x0 + hw if ends[0] == "hip" else ex0
    rx1 = x1 - hw if ends[1] == "hip" else ex1
    if rx0 > rx1:
        rx0 = rx1 = (x0 + x1) / 2
    faces = []
    S = [(ex0 if ends[0] == "hip" else ex0, ey0, ze), (ex1, ey0, ze), (rx1, yc, zr), (rx0, yc, zr)]
    N = [(ex1, ey1, ze), (ex0, ey1, ze), (rx0, yc, zr), (rx1, yc, zr)]
    faces += [S, N]
    if ends[0] == "hip":
        faces.append([(ex0, ey1, ze), (ex0, ey0, ze), (rx0, yc, zr)])
    if ends[1] == "hip":
        faces.append([(ex1, ey0, ze), (ex1, ey1, ze), (rx1, yc, zr)])
    # remove degenerate quads (pyramid) by dropping repeated points
    clean = []
    for f in faces:
        g = []
        for p in f:
            if not g or (Vector(p) - Vector(g[-1])).length > 1e-5:
                g.append(p)
        if len(g) > 1 and (Vector(g[0]) - Vector(g[-1])).length < 1e-5:
            g.pop()
        if len(g) >= 3:
            clean.append(g)
    # the core under the roof, a little lower so the slabs cover it
    zc = zr - t * 1.2
    cx0 = x0 + hw if ends[0] == "hip" else x0
    cx1 = x1 - hw if ends[1] == "hip" else x1
    if cx0 > cx1:
        cx0 = cx1 = (x0 + x1) / 2
    b = [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
    core = [b[::-1],
            [(x0, y0, z), (x1, y0, z), (cx1, yc, zc), (cx0, yc, zc)],
            [(x1, y1, z), (x0, y1, z), (cx0, yc, zc), (cx1, yc, zc)],
            [(x0, y1, z), (x0, y0, z), (cx0, yc, zc)],
            [(x1, y0, z), (x1, y1, z), (cx1, yc, zc)]]
    return clean, core


# the pieces strewn on a burnt heap: the drawing's shadows read as char to
# classify(), so its own counts overweight the beams
PIECES = {"char": 0.35, "stone": 0.35, "wood": 0.18, "tile": 0.12}

MODELS = {}


def model(*names):
    """Registers a builder: fn(m) fills a Model for each name."""
    def deco(fn):
        for n in names:
            MODELS[n] = fn
        return fn
    return deco
