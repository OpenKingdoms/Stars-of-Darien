"""Parametric builders for walls, wall joints, towers and fences, run in Blender.

Same frame as handkit: one unit is one map cell, -Y toward the classic
camera, Z up, origin on the feature's anchor. A Model gathers geometry into
one mesh per material. X(col) and Y(row, z) undo the classic projection
(column = hx + 16x, row = hy - 16y - 8z) for a point drawn at (col, row)
standing z cells up, so positions can be read straight off the sprite.

Walls are swept along a plan polyline: a cross-section of (u, z) points,
u to the left of the direction of travel, mitred at every corner, so an L
or a diagonal run is one piece of geometry and never a filled box.
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(os.path.dirname(HERE))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)
import handkit as hk  # noqa: E402

SPRITES = "D:/OKReplace/sprites"
CATALOG = "D:/OKReplace/catalog.json"
OUT = "D:/OKReplace/hand/walls_joints"
# the sprite has its light painted in and the game lights the model again
GAIN = 1.12

_CAT = None
MODELS = {}
# name -> the data table its builder reads, and the keys the fitter may move
TABLES = {}
FITKEYS = {}
# groups that are cores behind blocks, cut a hair short of every clip
INSET_CUT = {"mortar"}
# groups of loose stones: an end cut keeps or drops each stone whole
WHOLE_CUT = ("rub", "sand")
# defaults for fitted keys a table row leaves out
FITDEF = {}


def model(*names):
    """Registers a builder: fn(m) fills a Model for each name."""
    def deco(fn):
        for n in names:
            MODELS[n] = fn
        return fn
    return deco


FITTED = os.path.join(HERE, "fitted.json")
_FIT = None


def params(name, table):
    """A model's parameters: its row in the table, with any values the
    silhouette fitter found (fitted.json) laid over it."""
    global _FIT
    if _FIT is None:
        _FIT = json.load(open(FITTED)) if os.path.exists(FITTED) else {}
    p = dict(table[name])
    p.update(_FIT.get(name, {}))
    p.update(OVERRIDE.get(name, {}))
    return p


OVERRIDE = {}


def record(name):
    global _CAT
    if _CAT is None:
        _CAT = {r["name"]: r for r in json.load(open(CATALOG))}
    return _CAT[name]


def lin(c, gain=GAIN):
    """sRGB 0-255 to a linear material colour, with the gain."""
    out = []
    for v in c[:3]:
        v = v / 255.0
        v = v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
        out.append(min(1.0, v * gain))
    return tuple(out)


def _mat4(loc, rot=(0.0, 0.0, 0.0), scale=(1.0, 1.0, 1.0)):
    yaw, pitch, roll = rot
    R = Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(pitch, 4, "Y") @ Matrix.Rotation(roll, 4, "X")
    return Matrix.Translation(Vector(loc)) @ R @ Matrix.Diagonal((*scale, 1.0))


def area2(pts):
    return 0.5 * sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1]
                     for i in range(len(pts)))


def mitres(pts, closed=False):
    """Per vertex, the vector m with P + u m the point u to the left of the
    path, mitred at corners (limited so a sharp corner cannot spike)."""
    P = [Vector((p[0], p[1])) for p in pts]
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
            out.append(ns[0])
            continue
        b = (ns[0] + ns[1]).normalized()
        out.append(b / max(0.35, b.dot(ns[0])))
    return P, out


class Model:
    def __init__(self, name):
        r = record(name)
        self.r, self.name = r, name
        self.hx, self.hy = r["sprite"]["hotspot"]
        self.w, self.h = r["sprite"]["w"], r["sprite"]["h"]
        self.png = os.path.join(SPRITES, name + ".png")
        self.pal = {}
        self.groups = {}
        self.rng = random.Random(name)
        self._pix = None
        self.tris = {}
        self.cutters = []
        # fast: only the big masses, for fitting the silhouette
        self.fast = False
        self.clips = []

    # ---- the frame --------------------------------------------------
    def X(self, col):
        return (col - self.hx) / 16.0

    def Y(self, row, z=0.0):
        return (self.hy - row - 8.0 * z) / 16.0

    def P(self, col, row, z=0.0):
        return (self.X(col), self.Y(row, z))

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
            bpy.data.images.remove(img)
        return self._pix

    def sample(self, x0, y0, x1, y1, pick=None):
        """Median sRGB (0-255) of the drawn pixels in a sprite rectangle;
        pick(r, g, b) keeps only some of them."""
        import numpy as np
        a = self.pix[y0:y1, x0:x1].reshape(-1, 4)
        a = a[a[:, 3] > 0.5][:, :3] * 255.0
        if pick is not None and len(a):
            k = np.array([bool(pick(*c)) for c in a])
            if k.any():
                a = a[k]
        if not len(a):
            return (128, 128, 128)
        return tuple(float(v) for v in np.median(a, 0))

    # ---- materials and groups ---------------------------------------
    def col(self, key, srgb, rough=0.9, metal=0.0, spec=None):
        """A plain material; spec below 0.5 dulls its sheen, so a deep
        colour is not washed out by the sky's reflection."""
        if isinstance(srgb, tuple) and len(srgb) == 4:
            srgb = self.sample(*srgb)
        self.pal[key] = dict(rgb=tuple(srgb), rough=rough, metal=metal)
        if spec is not None:
            self.pal[key]["spec"] = spec
        return key

    def shade(self, key, base, k, rough=0.9):
        """A darker or lighter tone of another material."""
        c = self.pal[base]["rgb"]
        return self.col(key, tuple(min(255.0, v * k) for v in c), rough, spec=self.pal[base].get("spec"))

    def bm(self, key):
        if key not in self.groups:
            self.groups[key] = bmesh.new()
        return self.groups[key]

    # ---- primitives -------------------------------------------------
    def box(self, key, x0, x1, y0, y1, z0, z1):
        self.obox(key, (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, abs(x1 - x0), abs(y1 - y0), abs(z1 - z0))

    def obox(self, key, cx, cy, cz, sx, sy, sz, yaw=0.0, pitch=0.0, roll=0.0):
        bmesh.ops.create_cube(self.bm(key), size=1.0, matrix=_mat4((cx, cy, cz), (yaw, pitch, roll), (sx, sy, sz)))

    def beam(self, key, p0, p1, w, h=None, roll=0.0):
        """A squared timber from p0 to p1 (3D points)."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        L = d.length
        if L < 1e-4:
            return
        yaw = math.atan2(d.y, d.x)
        pitch = -math.asin(max(-1.0, min(1.0, d.z / L)))
        c = (p0 + p1) / 2
        self.obox(key, c.x, c.y, c.z, L, w, h or w, yaw, pitch, roll)

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

    def rod(self, key, p0, p1, r, seg=6):
        """A round pole from p0 to p1."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        L = d.length
        if L < 1e-4:
            return
        q = d.normalized().to_track_quat("Z", "Y")
        M = Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4()
        res = bmesh.ops.create_cone(self.bm(key), cap_ends=True, segments=seg, radius1=r, radius2=r, depth=L,
                                    matrix=M)
        for f in {f for v in res["verts"] for f in v.link_faces}:
            if len(f.verts) == 4:
                f.smooth = True

    def prism(self, key, pts, z0, z1):
        """A plan polygon extruded from z0 to z1."""
        if area2(pts) < 0:
            pts = pts[::-1]
        bm = self.bm(key)
        vb = [bm.verts.new((x, y, z0)) for x, y in pts]
        vt = [bm.verts.new((x, y, z1)) for x, y in pts]
        bm.faces.new(vt)
        bm.faces.new(vb[::-1])
        n = len(pts)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((vb[i], vb[j], vt[j], vt[i]))

    def faces(self, key, polys):
        """Loose polygons (lists of 3D points), welded where they share points."""
        bm = self.bm(key)
        vmap = {}

        def V(p):
            k = tuple(round(c, 5) for c in p)
            if k not in vmap:
                vmap[k] = bm.verts.new(p)
            return vmap[k]
        for f in polys:
            vs = list(dict.fromkeys(V(p) for p in f))
            if len(vs) >= 3:
                try:
                    bm.faces.new(vs)
                except ValueError:
                    pass

    def solid(self, key, polys):
        """Closed polygons welded into one shell, normals made outward."""
        bm = self.bm(key)
        n0 = len(bm.faces)
        self.faces(key, polys)
        bm.faces.ensure_lookup_table()
        new = [bm.faces[i] for i in range(n0, len(bm.faces))]
        if new:
            bmesh.ops.recalc_face_normals(bm, faces=new)

    def rock(self, key, cx, cy, sx, sy, sz, yaw=0.0, z=0.0, jag=0.25, pitch=0.0, roll=0.0):
        """A rough block: a cube with its corners pulled about, resting at z."""
        bm = self.bm(key)
        res = bmesh.ops.create_cube(bm, size=1.0,
                                    matrix=_mat4((cx, cy, z + sz / 2), (yaw, pitch, roll), (sx, sy, sz)))
        for v in res["verts"]:
            v.co.x += self.rng.uniform(-jag, jag) * sx * 0.5
            v.co.y += self.rng.uniform(-jag, jag) * sy * 0.5
            v.co.z += self.rng.uniform(-jag, jag) * sz * 0.5
            v.co.z = max(v.co.z, 0.0)

    def plate(self, key, face, ext):
        """A planar polygon (3D points) extruded by the vector ext."""
        bm = self.bm(key)
        n0 = len(bm.faces)
        e = Vector(ext)
        a = [bm.verts.new(p) for p in face]
        b = [bm.verts.new(Vector(p) + e) for p in face]
        n = len(face)
        try:
            bm.faces.new(a)
            bm.faces.new(b[::-1])
        except ValueError:
            pass
        for i in range(n):
            j = (i + 1) % n
            try:
                bm.faces.new((a[i], a[j], b[j], b[i]))
            except ValueError:
                pass
        bm.faces.ensure_lookup_table()
        bmesh.ops.recalc_face_normals(bm, faces=[bm.faces[i] for i in range(n0, len(bm.faces))])

    def cut_prism(self, face, ext, skip=()):
        """A cutter: everything inside this extruded polygon is removed
        from every part (but the groups in skip) when the model is done."""
        bm = bmesh.new()
        e = Vector(ext)
        a = [bm.verts.new(p) for p in face]
        b = [bm.verts.new(Vector(p) + e) for p in face]
        n = len(face)
        bm.faces.new(a)
        bm.faces.new(b[::-1])
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        self.cutters.append((bm, set(skip)))

    def clip(self, x, y, nx, ny, batter=0.0, zb=1.0, keys=()):
        """Removes everything on the (nx, ny) side of the vertical plane
        through (x, y): a wall piece's end cut on a grid line. The groups
        in keys are cut by a sloped plane instead, batter beyond the cut at
        the ground and on it at height zb, so a plinth's end leans back.
        Applied when the model is done, solids closed over their cut."""
        n = Vector((nx, ny, 0.0)).normalized()
        c = Vector((x, y, 0.0))
        slope = None
        if batter > 0 and keys:
            slope = (c + n * batter, Vector((n.x, n.y, batter / zb)).normalized(), set(keys))
        self.clips.append((c, n, slope))

    def cut_mesh(self, bm, skip=()):
        """A closed bmesh used as a cutter."""
        self.cutters.append((bm, set(skip)))

    def boulder(self, key, cx, cy, sx, sy, sz, z=0.0, jag=0.18, yaw=0.0):
        """A rounded rock: an icosahedron squashed to size, its corners pulled about."""
        bm = self.bm(key)
        res = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.5,
                                         matrix=_mat4((cx, cy, z + sz / 2), (yaw, 0, 0), (sx, sy, sz)))
        for v in res["verts"]:
            f = 1.0 + self.rng.uniform(-jag, jag)
            c = Vector((cx, cy, z + sz / 2))
            v.co = c + (v.co - c) * f
            v.co.z = max(v.co.z, 0.0)
        for f in {f for v in res["verts"] for f in v.link_faces}:
            f.smooth = True

    # ---- sweeps -----------------------------------------------------
    def sweep(self, key, pts, section, closed=False, caps=True, keys=None, t0=None, t1=None):
        """The (u, z) section carried along the plan path, mitred at the
        corners. keys, one per section edge, sends that edge's faces to
        other groups; the caps go to key. t0/t1 trim the path by those
        distances along it."""
        path = pts
        if t0 is not None or t1 is not None:
            path = sub_path(pts, t0 or 0.0, path_len(pts) - (t1 or 0.0) if t1 is not None else None)
        P, M = mitres(path, closed)
        sec = list(section)
        k = len(sec)
        if area2(sec) < 0:
            sec = sec[::-1]
            if keys:
                keys = [keys[(k - 2 - i) % k] for i in range(k)]
        n = len(P)
        rings = []
        for i in range(n):
            rings.append([(P[i].x + M[i].x * u, P[i].y + M[i].y * u, z) for u, z in sec])
        polys = {}
        segs = n if closed else n - 1
        for i in range(segs):
            a, b = rings[i], rings[(i + 1) % n]
            for j in range(k):
                jj = (j + 1) % k
                # u to the left, going along the path: this winding faces out
                kk = keys[j] if keys else key
                polys.setdefault(kk, []).append([a[j], a[jj], b[jj], b[j]])
        if caps and not closed:
            polys.setdefault(key, []).append(rings[0][::-1])
            polys.setdefault(key, []).append(rings[-1])
        for kk, pl in polys.items():
            self.faces(kk, pl)

    # ---- the end ----------------------------------------------------
    def done(self):
        """One object per material, with the cutters taken out."""
        objs = []
        cut_obs = []
        for i, (cb, skip) in enumerate(self.cutters):
            me = bpy.data.meshes.new("cutter%d" % i)
            cb.to_mesh(me)
            cb.free()
            co = bpy.data.objects.new("cutter%d" % i, me)
            bpy.context.collection.objects.link(co)
            cut_obs.append((co, skip))
        for key, bm in self.groups.items():
            if not bm.verts:
                bm.free()
                continue
            for co, no, slope in self.clips:
                if slope and key in slope[2]:
                    co, no = slope[0], slope[1]
                if key in INSET_CUT:
                    # a core behind blocks is cut a hair short, or the two cut faces fight
                    co = co - no * 0.02
                _bisect(bm, co, no, whole=key.startswith(WHOLE_CUT))
            if not bm.faces:
                bm.free()
                continue
            me = bpy.data.meshes.new(self.name + "_" + key)
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(self.name + "_" + key, me)
            bpy.context.collection.objects.link(ob)
            mine = [co for co, skip in cut_obs if key not in skip]
            for co in mine:
                mod = ob.modifiers.new("cut", "BOOLEAN")
                mod.operation = "DIFFERENCE"
                mod.object = co
                mod.solver = "EXACT"
                mod.use_hole_tolerant = True
            if mine:
                bpy.ops.object.select_all(action="DESELECT")
                bpy.context.view_layer.objects.active = ob
                ob.select_set(True)
                for mod in list(ob.modifiers):
                    bpy.ops.object.modifier_apply(modifier=mod.name)
                me = ob.data
                if not me.polygons:
                    bpy.data.objects.remove(ob, do_unlink=True)
                    continue
                # the boolean leaves an empty slot in front
                me.materials.clear()
                for q in me.polygons:
                    q.material_index = 0
            p = self.pal.get(key, dict(rgb=(150, 150, 150), rough=0.9, metal=0.0))
            mat = hk.pbr(self.name + "_" + key, lin(p["rgb"]), p["rough"], p["metal"])
            if "spec" in p:
                mat.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = p["spec"]
            me.materials.append(mat)
            objs.append(ob)
            self.tris[key] = sum(len(q.vertices) - 2 for q in me.polygons)
        self.groups = {}
        for co, _ in cut_obs:
            bpy.data.objects.remove(co, do_unlink=True)
        return objs


def _islands(bm):
    bm.verts.ensure_lookup_table()
    seen, out = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        seen.add(v.index)
        stack, comp = [v], []
        while stack:
            a = stack.pop()
            comp.append(a)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen:
                    seen.add(b.index)
                    stack.append(b)
        out.append(comp)
    return out


def _bisect(bm, co, no, whole=False):
    """Cuts bm by the plane, drops the side no points to, and closes every
    solid over the cut. whole: small pieces (stones) are kept or dropped
    whole by their middle, never sliced open."""
    if whole:
        bm.verts.index_update()
        big = []
        for comp in _islands(bm):
            lo = [min(v.co[i] for v in comp) for i in range(3)]
            hi = [max(v.co[i] for v in comp) for i in range(3)]
            if max(h - l for h, l in zip(hi, lo)) > 0.9:
                big.extend(comp)
                continue
            mid = sum((v.co for v in comp), Vector()) / len(comp)
            if (mid - co).dot(no) > 0:
                bmesh.ops.delete(bm, geom=comp, context="VERTS")
        if not big:
            return
        faces = {f for v in big for f in v.link_faces}
        edges = {e for v in big for e in v.link_edges}
        geom = list(big) + list(edges) + list(faces)
    else:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=co, plane_no=no, clear_outer=True)
    cut = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge) and e.is_valid and e.is_boundary]
    if not cut:
        return
    new = bmesh.ops.holes_fill(bm, edges=cut, sides=0)["faces"]
    for f in new:
        f.normal_update()
        if f.normal.dot(no) < 0:
            f.normal_flip()


def path_len(pts):
    return sum((Vector(pts[i + 1]) - Vector(pts[i])).length for i in range(len(pts) - 1))


def point_at(pts, t):
    """The point and direction t along a polyline."""
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1])
        L = (b - a).length
        if t <= L or i == len(pts) - 2:
            d = (b - a).normalized()
            return a + d * max(0.0, min(t, L)), d, i
        t -= L
    raise ValueError


def sub_path(pts, t0, t1=None):
    """The part of a polyline from t0 to t1 along it, keeping the corners."""
    L = path_len(pts)
    t1 = L if t1 is None else t1
    out = [tuple(point_at(pts, t0)[0])]
    acc = 0.0
    for i in range(1, len(pts) - 1):
        acc += (Vector(pts[i]) - Vector(pts[i - 1])).length
        if t0 < acc < t1:
            out.append(tuple(pts[i]))
    out.append(tuple(point_at(pts, t1)[0]))
    return out
