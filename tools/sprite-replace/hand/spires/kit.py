"""Parametric builders for the Taros spires, run inside Blender: grey rock
crags (TarSpire01-08), each one massif built as a height field from faceted
pyramids and sharp ridge arms, and banded hoodoo columns in talus heaps of
angular slabs and gravel (TarSpire09-15). sturdy() applies the owner's
sturdier build to either.

Frame as handkit: one unit is one map cell, -Y toward the classic camera,
Z up, the origin on the feature's anchor. Colour lives in 'Col' vertex
colours that the game multiplies into each material (misckit.vmat), with
ambient occlusion baked in by casting rays against the whole model.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(os.path.dirname(HERE))
for _p in (TOOLS, os.path.join(TOOLS, "lodes")):
    if _p not in sys.path:
        sys.path.insert(0, _p)
import handkit as hk  # noqa: E402
import misckit as mk  # noqa: E402

OUT = r"D:\OKReplace\hand\spires"
SPRITES = r"D:\OKReplace\sprites"
TAU = 2 * math.pi
WHITE = (255, 255, 255)

# picture colours (0-255 sRGB), sampled from the sprites' lit and shaded faces
ROCK = {"dark": (26, 27, 31), "mid": (78, 74, 74), "light": (128, 120, 118), "pink": (136, 118, 114),
        "crevice": (12, 13, 16)}
BAND = {"dark": (38, 30, 22), "brown": (72, 57, 42), "mid": (100, 80, 59), "tan": (142, 113, 80),
        "hi": (184, 142, 92), "crack": (24, 19, 14)}
SLAB = {"dark": (70, 62, 44), "body": (112, 99, 70), "light": (152, 137, 96)}
GRIT = {"dark": (58, 50, 36), "mid": (96, 84, 60), "light": (124, 108, 78)}
SAND = {"dark": (80, 68, 50), "mid": (100, 86, 62), "light": (122, 105, 77)}
TWIG = (34, 26, 18)


# ---------------------------------------------------------------- basics

def mat(name, rough=0.9):
    """A white vertex-coloured material: the 'Col' colours are the albedo."""
    m = mk.vmat(name, WHITE, rough)
    m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.25
    return m


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def fbm(q, octaves=3):
    s, amp, tot = 0.0, 1.0, 0.0
    for _ in range(octaves):
        s += amp * noise.noise(q)
        tot += amp
        q = q * 2.13
        amp *= 0.5
    return s / tot


def hash01(*k):
    h = 2166136261
    for v in k:
        h = ((h ^ (int(v) & 0xFFFFFFFF)) * 16777619) & 0xFFFFFFFF
    return (h % 10007) / 10007.0


def new_mesh(name, verts, faces, m=None):
    ob = mk.mesh(name, verts, faces, m)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
    return ob


def set_cav(ob, values):
    """Keeps a per-vertex 'how far in' value for the painter (removed before export)."""
    at = ob.data.attributes.get("cav") or ob.data.attributes.new("cav", "FLOAT", "POINT")
    for i, v in enumerate(values):
        at.data[i].value = v
    return ob


def get_cav(ob):
    at = ob.data.attributes.get("cav")
    return [d.value for d in at.data] if at else [0.0] * len(ob.data.vertices)


def shade(ob, flat=False, angle=38):
    if flat:
        return mk.flat_shade(ob)
    return hk.smooth(ob, angle)


def join(parts, name):
    return mk.join(parts, name)


def tris(ob):
    return mk.tris(ob)


# ---------------------------------------------------------------- occlusion

def _dirs(n, seed=3):
    """Cosine-weighted hemisphere directions about +Z."""
    out = []
    ga = math.pi * (3 - math.sqrt(5))
    for i in range(n):
        u = (i + 0.5) / n
        r = math.sqrt(u)
        a = i * ga + seed
        out.append(Vector((r * math.cos(a), r * math.sin(a), math.sqrt(max(0.0, 1 - u)))))
    return out


def bake_ao(parts, dist=2.5, rays=24, ground=True):
    """Per-vertex openness (1 open, 0 shut) of every part against every part
    and the ground, returned as {part name: [value per vertex]}."""
    verts, polys = [], []
    for ob in parts:
        M = ob.matrix_world
        b = len(verts)
        verts += [M @ v.co for v in ob.data.vertices]
        polys += [[b + i for i in p.vertices] for p in ob.data.polygons]
    bvh = BVHTree.FromPolygons(verts, polys, epsilon=0.0)
    D = _dirs(rays)
    out = {}
    for ob in parts:
        M = ob.matrix_world
        N = M.to_3x3()
        vals = []
        for v in ob.data.vertices:
            p = M @ v.co
            n = (N @ v.normal)
            if n.length < 1e-6:
                n = Vector((0, 0, 1))
            n.normalize()
            if p.z < -0.08:
                vals.append(0.2)
                continue
            t = n.orthogonal().normalized()
            b = n.cross(t)
            o = p + n * 0.03
            occ = 0.0
            for d in D:
                w = t * d.x + b * d.y + n * d.z
                if ground and w.z < -1e-3 and (max(o.z, 0.0) / -w.z) < dist:
                    occ += 1.0
                    continue
                hit = bvh.ray_cast(o, w, dist)
                if hit[0] is not None:
                    occ += 0.35 + 0.65 * (1.0 - hit[3] / dist)
            vals.append(1.0 - occ / len(D))
        out[ob.name] = vals
    return out


def paint(ob, fn, ao=None, lo=0.28, gamma=1.3):
    """Vertex colours from fn(poly, co, normal, vertex index, loop index) ->
    sRGB, darkened by the baked openness (ao list per vertex)."""
    me = ob.data

    def f(p, co, n, li):
        vi = me.loops[li].vertex_index
        c = fn(p, co, n, vi, li)
        if ao is not None:
            k = lo + (1.0 - lo) * max(0.0, ao[vi]) ** gamma
            c = tuple(v * k for v in c)
        return c
    return mk.vpaint(ob, f)


# ---------------------------------------------------------------- crags

def chips(spots, seed=0, m=None, name="scree"):
    """Small jagged stones lying on the ground: spots is [(x, y, size)]."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    for (x, y, s) in spots:
        tmp = bmesh.new()
        bmesh.ops.create_icosphere(tmp, subdivisions=1 if s > 0.42 else 0, radius=1.0)
        rz = rnd.uniform(0, TAU)
        sx, sy, sz = s * rnd.uniform(0.8, 1.2), s * rnd.uniform(0.7, 1.0), s * rnd.uniform(0.45, 0.8)
        for v in tmp.verts:
            v.co += Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), rnd.uniform(-0.2, 0.2)))
            v.co = Matrix.Rotation(rz, 3, "Z") @ Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz))
            v.co += Vector((x, y, sz * 0.35))
        me = bpy.data.meshes.new("tmp")
        tmp.to_mesh(me)
        tmp.free()
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    ob = mk.from_bm(name, bm, m)
    return set_cav(ob, [0.0] * len(ob.data.vertices))


def scatter(region, n, size=(0.15, 0.4), seed=0, inner=0.0, keep=None):
    """Spots for chips in an ellipse (cx, cy, rx, ry), from `inner` of the
    radius out to its rim; keep(x, y) can veto a spot."""
    cx, cy, rx, ry = region
    rnd = random.Random(seed)
    spots = []
    tries = 0
    while len(spots) < n and tries < n * 30:
        tries += 1
        a = rnd.uniform(0, TAU)
        d = math.sqrt(rnd.uniform(inner * inner, 1.0))
        x, y = cx + rx * d * math.cos(a), cy + ry * d * math.sin(a)
        if keep is not None and not keep(x, y):
            continue
        spots.append((x, y, rnd.uniform(*size)))
    return spots


def rock_colour(seed=0, pal=ROCK, grit=0.25, strata=0.08, cavity=1.5, dust=0.0, light=0.5):
    """Matte grey rock: a two-tone mottle of pinkish-grey lit stone and
    near-black pits (light sets how much is pale), faint strata, a strong
    per-face grain and dark crevices."""
    rnd = random.Random(seed)
    off = Vector((rnd.uniform(0, 90), rnd.uniform(0, 90), rnd.uniform(0, 90)))

    def fn(poly, co, n, vi, li, cav):
        c0 = co.lerp(poly.center, 0.35)
        v = 0.6 * fbm(c0 * 1.2 + off) + 0.55 * noise.noise(c0 * 3.7 + off) + (light - 0.5)
        k = smoothstep(-0.3, 0.3, v)
        c = mk.mixc(pal["dark"], pal["light"], k)
        if k > 0.6:
            c = mk.mixc(c, pal["pink"], (k - 0.6) * 1.5)
        s = strata * math.sin(c0.z * 4.2 + 2.0 * noise.noise(c0 * 0.6 + off))
        g = grit * (0.6 * (hash01(poly.index, seed) * 2 - 1) + 0.6 * (hash01(li, seed, 3) * 2 - 1))
        c = tuple(v_ * (1.0 + s + g) for v_ in c)
        cv = cav[vi]
        if cv < 0:
            c = mk.mixc(c, pal["crevice"], min(0.9, -cv * cavity * 2.2))
        if n.z < -0.3:
            c = mk.mixc(c, pal["crevice"], 0.5)
        if dust and n.z > 0.55:
            c = mk.mixc(c, pal["light"], dust)
        return c
    return fn


def paint_rock(ob, ao, seed=0, lo=0.22, **kw):
    cav = get_cav(ob)
    fn = rock_colour(seed, **kw)
    return paint(ob, lambda p, co, n, vi, li: fn(p, co, n, vi, li, cav), ao, lo=lo)


def polygon(sides, corner, rnd, spin=None, corners=None):
    """An uneven convex polygon about the origin, unit size: its side lines
    as (normal x, normal y, distance) and its corners, which can be given as
    [(degrees, radius)] so they point along a crag's arms."""
    rot = math.radians(rnd.uniform(0, 360) if spin is None else spin)
    cs = []
    if corners:
        for deg, k in sorted(corners):
            cs.append(Vector((k * math.cos(math.radians(deg)), k * math.sin(math.radians(deg)))))
    for j in range(0 if corners else sides):
        a = rot + TAU * j / sides + rnd.uniform(-0.2, 0.2) * TAU / sides
        k = rnd.uniform(1.0 - corner, 1.0 + 0.3 * corner)
        cs.append(Vector((k * math.cos(a), k * math.sin(a))))
    sides = len(cs)
    lines = []
    for j in range(sides):
        e = cs[(j + 1) % sides] - cs[j]
        n = Vector((e.y, -e.x)).normalized()
        if n.dot(cs[j]) < 0:
            n = -n
        lines.append((n.x, n.y, n.dot(cs[j])))
    return lines, cs


def pyramid_fn(x0, y0, h, r, ry=None, sides=5, p=1.1, corner=0.2, spin=None, corners=None, crown=0.0, cut=None,
               seed=0):
    """Height over (x, y) of a faceted pyramid: an uneven `sides`-gon r (ry
    across) at the ground, or one with the given corners, flanks falling as
    (1 - d)^p, so p over 1 bows them in, and the top `crown` of its radius
    broken off level; below zero outside, for the zero line to be found.
    cut (degrees, slope, level) breaks the top on a slanted plane through
    level x h on the axis, falling toward that bearing."""
    rnd = random.Random(seed)
    ry = r if ry is None else ry
    lines, _ = polygon(sides, corner, rnd, spin, corners)
    if cut:
        ca, sa = math.cos(math.radians(cut[0])), math.sin(math.radians(cut[0]))

    def f(x, y):
        u, v = (x - x0) / r, (y - y0) / ry
        d = max((u * nx + v * ny) / o for nx, ny, o in lines)
        if d >= 1.0:
            return -(d - 1.0) * h
        z = h * min(1.0, (1.0 - d) / (1.0 - crown)) ** p
        if cut:
            z = max(0.02, min(z, h * cut[2] - cut[1] * ((x - x0) * ca + (y - y0) * sa)))
        return z
    return f


def wedge_fn(a, b, width, k=1.0, taper=0.5, end=0.2, flank=1.2, nose=2.5):
    """Height over (x, y) of a sharp-crested arm from a = (x, y, z) down to
    b = (x, y): the crest falls as (1 - s)^k to `end`, the flanks fall to
    the ground `width` either side of it (narrowing with the crest's height
    to the power taper) as (1 - l / w)^flank, and it noses down past b."""
    A, B = Vector((a[0], a[1])), Vector((b[0], b[1]))
    za = a[2]
    D = B - A
    L = D.length
    d = D / L

    def f(x, y):
        vx, vy = x - A.x, y - A.y
        s = (vx * d.x + vy * d.y) / L
        l = abs(vx * d.y - vy * d.x)
        sc = min(1.0, max(0.0, s))
        c = end + (za - end) * (1.0 - sc) ** k
        w = width * (c / za) ** taper + 0.1
        hh = c * (1.0 - l / w) ** flank if l < w else -(l - w)
        if s > 1.0:
            hh -= (s - 1.0) * L * nose
        elif s < 0.0:
            hh -= -s * L * nose
        return hh
    return f


def massif(fns, box, res=0.16, seed=0, jag=0.22, crag=0.18, fine=0.06, flutes=0.0, flute_n=9, centre=(0.0, 0.0),
           m=None, name="massif"):
    """One rock mass over the ground: the highest of the height functions
    fns, broken by coarse lumps (jag), ridged crags (crag), fine grain and
    gullies running down from centre (flutes), meshed on a grid of res
    cells over box (x0, y0, x1, y1). Its outline is snapped to the zero
    line and dips just under the ground."""
    rnd = random.Random(seed)
    o1 = Vector((rnd.uniform(0, 60), rnd.uniform(0, 60), rnd.uniform(0, 60)))
    o2 = Vector((rnd.uniform(0, 60), rnd.uniform(0, 60), rnd.uniform(0, 60)))
    o3 = Vector((rnd.uniform(0, 60), rnd.uniform(0, 60), rnd.uniform(0, 60)))
    x0, y0, x1, y1 = box
    nx, ny = int(math.ceil((x1 - x0) / res)) + 1, int(math.ceil((y1 - y0) / res)) + 1
    H = [[0.0] * nx for _ in range(ny)]
    C = [[0.0] * nx for _ in range(ny)]
    for j in range(ny):
        y = y0 + j * res
        for i in range(nx):
            x = x0 + i * res
            h = max(f(x, y) for f in fns)
            if h > -0.6:
                q = Vector((x, y, 0.0))
                hp = max(h, 0.0) ** 0.8
                n1 = fbm(q * 0.85 + o1, 3)
                n2 = 1.0 - 2.0 * abs(noise.noise(q * 1.9 + o2))
                n3 = noise.noise(q * 4.6 + o3)
                g = 0.0
                if flutes:
                    ang = math.atan2(y - centre[1], x - centre[0])
                    rr = math.hypot(x - centre[0], y - centre[1])
                    g = -abs(noise.noise(Vector((math.cos(ang) * flute_n * 0.35, math.sin(ang) * flute_n * 0.35,
                                                 rr * 0.25)) + o1))
                    g = flutes * (2.0 * g + 0.5)
                dn = jag * hp * n1 + crag * hp * n2 + fine * min(1.0, max(h, 0.0)) * n3 + g * hp
                h += dn
                C[j][i] = dn - crag * hp * 0.4
            H[j][i] = h
    # ground shut in by rock would show as a hole, so it gets a low floor
    out = [[False] * nx for _ in range(ny)]
    todo = [(i, j) for i in range(nx) for j in (0, ny - 1)] + [(i, j) for j in range(ny) for i in (0, nx - 1)]
    while todo:
        i, j = todo.pop()
        if 0 <= i < nx and 0 <= j < ny and not out[j][i] and H[j][i] <= 0.0:
            out[j][i] = True
            todo += [(i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1)]
    for j in range(ny):
        for i in range(nx):
            if H[j][i] <= 0.0 and not out[j][i]:
                H[j][i] = 0.06
    idx = {}
    verts, cav = [], []

    def vid(i, j):
        if (i, j) in idx:
            return idx[(i, j)]
        x, y, h = x0 + i * res, y0 + j * res, H[j][i]
        if h <= 0.0:
            # an outside point moves to where its edges to inside points cross zero
            pts = []
            for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                ii, jj = i + di, j + dj
                if 0 <= ii < nx and 0 <= jj < ny and H[jj][ii] > 0.0:
                    t = h / (h - H[jj][ii])
                    pts.append((x + di * res * t, y + dj * res * t))
            if pts:
                x = sum(q[0] for q in pts) / len(pts)
                y = sum(q[1] for q in pts) / len(pts)
            h = -0.05
        idx[(i, j)] = len(verts)
        verts.append((x, y, h))
        cav.append(C[j][i])
        return idx[(i, j)]
    faces = []
    for j in range(ny - 1):
        for i in range(nx - 1):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            hs = [H[b_][a_] for a_, b_ in q]
            if max(hs) <= 0.0:
                continue
            v = [vid(a_, b_) for a_, b_ in q]
            # split along the diagonal that keeps crests and gullies whole
            if hs[0] + hs[2] >= hs[1] + hs[3]:
                faces += [(v[0], v[1], v[2]), (v[0], v[2], v[3])]
            else:
                faces += [(v[0], v[1], v[3]), (v[1], v[2], v[3])]
    ob = mk.mesh(name, verts, faces, m)
    # every face of a height field looks at the sky
    for poly in ob.data.polygons:
        if poly.normal.z < 0:
            poly.flip()
    ob.data.update()
    return set_cav(ob, cav)


def knob(c, s, seed=0, cuts=3, m=None, name="knob"):
    """A detached, rounded but broken rock: half sizes s, sitting on c."""
    ob = boulder((c[0], c[1], c[2] + s[2] * 0.55), s, seed=seed, sub=1, jag=0.22, cuts=cuts, m=m, name=name)
    return set_cav(ob, [0.0] * len(ob.data.vertices))


def crag(p, m=None):
    """A crag from a table entry: one massif from 'peaks' [(x, y, h, r,
    extras)] and 'ridges' [((x, y, z), (x, y), extras)] with 'noise'
    settings, detached 'knobs' [((x, y, z), half sizes)] and 'scree'
    [(ellipse, count, extras)] of small stones in front and to the sides."""
    m = m or mat("spire_rock", 0.92)
    seed = p.get("seed", 0)
    fns, xs, ys = [], [], []
    for i, e in enumerate(p.get("peaks", [])):
        x, y, h, r = e[:4]
        kw = dict(e[4]) if len(e) > 4 else {}
        kw.setdefault("seed", 17 * i + seed)
        fns.append(pyramid_fn(x, y, h, r, **kw))
        rr = max(r, kw.get("ry", r)) * 1.35
        xs += [x - rr, x + rr]
        ys += [y - rr, y + rr]
    for a, b, *rest in p.get("ridges", []):
        kw = dict(rest[0]) if rest else {}
        fns.append(wedge_fn(a, b, **kw))
        w = kw["width"] + 0.5
        xs += [a[0] - w, a[0] + w, b[0] - w, b[0] + w]
        ys += [a[1] - w, a[1] + w, b[1] - w, b[1] + w]
    parts = []
    if fns:
        box = (min(xs), min(ys), max(xs), max(ys))
        nz = dict(p.get("noise", {}))
        pk = p["peaks"][0] if p.get("peaks") else (0.0, 0.0)
        nz.setdefault("centre", (pk[0], pk[1]))
        parts.append(shade(massif(fns, box, seed=seed, m=m, **nz), flat=True))
    for i, (c, s) in enumerate(p.get("knobs", [])):
        parts.append(shade(knob(c, s, seed=seed + 41 * i, m=m, name="knob%d" % i), flat=True))
    spots = []
    for i, sc in enumerate(p.get("scree", [])):
        region, n = sc[:2]
        kw = dict(sc[2]) if len(sc) > 2 else {}
        # only in front and to the sides: behind, a chip shows above the rock as a speck in the air
        kw.setdefault("keep", lambda x, y, r=region: y < r[1] + 0.15 * r[3])
        spots += scatter(region, n, seed=seed + 31 * i, **kw)
    if spots:
        sc_ob = shade(chips(spots, seed=seed + 3, m=m), flat=True)
        sc_ob["scree"] = True
        parts.append(sc_ob)
    ao = bake_ao(parts, dist=p.get("ao_dist", 2.2), rays=p.get("rays", 20))
    for i, q in enumerate(parts):
        paint_rock(q, ao[q.name], seed=seed + i, lo=0.45 if q.get("scree") else 0.22, **p.get("colour", {}))
    return parts


# ---------------------------------------------------------------- hoodoos

def catmull(pts, sub=3):
    """Catmull-Rom through (z, r) points, sub steps per span."""
    out = []
    P = [pts[0]] + list(pts) + [pts[-1]]
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for s in range(sub):
            t = s / sub
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1[k]) + (-p0[k] + p2[k]) * t + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t2
                                    + (-p0[k] + 3 * p1[k] - 3 * p2[k] + p3[k]) * t3) for k in range(2)))
    out.append(tuple(pts[-1]))
    return out


def spline(profile, sub=3):
    """Catmull-Rom through a profile of (z, r) points, restarted at any point
    marked as a corner (z, r, 1), so a rim stays crisp."""
    runs, cur = [], []
    for q in profile:
        cur.append((q[0], q[1]))
        if len(q) > 2 and q[2]:
            runs.append(cur)
            cur = [(q[0], q[1])]
    if len(cur) > 1 or not runs:
        runs.append(cur)
    out = []
    for r in runs:
        pts = catmull(r, sub) if len(r) > 1 else r
        out += pts if not out else pts[1:]
    return out


def column(profile, x=0.0, y=0.0, seg=16, sub=2, lean=(0.0, 0.0), wob=0.07, swirl=0.05, seed=0, ry=1.0,
           period=0.9, ledge=0.07, wobble=0.35, tilt=(0.06, 0.03), knob=0.0, m=None, name="column"):
    """A lathed stone column: profile is (z, r) bottom to top, ending on the
    axis, (z, r, 1) marking a crisp corner; lean bends the axis (tip offset in
    cells, growing as t^2); wob lumps the surface; ledge swells each stratum
    (period cells, wobbling by `wobble`) between dark grooves; knob raises
    small knobbly ledges. ry squashes it front to back."""
    rnd = random.Random(seed)
    off = Vector((rnd.uniform(0, 60), rnd.uniform(0, 60), rnd.uniform(0, 60)))
    boff = Vector((rnd.uniform(0, 60), rnd.uniform(0, 60), rnd.uniform(0, 60)))
    pts = spline(profile, sub)
    H = max(z for z, _ in pts)
    rmean = sum(r for _, r in pts) / len(pts)
    verts, cav, zbs = [], [], []
    rings = []
    for (z, r) in pts[:-1]:
        t = max(0.0, z / H)
        cx, cy = x + lean[0] * t * t, y + lean[1] * t * t
        ring = []
        for j in range(seg):
            a = TAU * j / seg
            ca, sa = math.cos(a), math.sin(a)
            q = Vector((ca * 1.1, sa * 1.1, z * 0.55)) + off
            d = wob * fbm(q, 2) + swirl * noise.noise(Vector((ca * 2.5, sa * 2.5, z * 1.6)) + off * 1.3)
            px, py = cx + r * ca, cy + r * ry * sa
            zb = z + wobble * fbm(Vector((px, py, z)) * 0.7 + boff, 2) + tilt[0] * px + tilt[1] * py
            f = (zb / period) % 1.0
            bump = math.sin(math.pi * f ** 0.75) - 0.55
            d += ledge * bump
            if knob:
                kn = noise.noise(Vector((ca * 3.2, sa * 3.2, z * 2.4)) + boff)
                d += knob * (max(0.0, kn) * 1.6 - 0.25)
            rr = max(0.02, r * (1.0 + d))
            ring.append(len(verts))
            verts.append((cx + rr * ca, cy + rr * ry * sa, z))
            cav.append(d + 0.6 * (r - rmean) / max(rmean, 0.1))
            zbs.append(zb)
        rings.append(ring)
    zt, _ = pts[-1]
    apex = len(verts)
    verts.append((x + lean[0], y + lean[1], zt))
    cav.append(sum(cav[i] for i in rings[-1]) / seg)
    zbs.append(sum(zbs[i] for i in rings[-1]) / seg)
    faces = []
    for k in range(len(rings) - 1):
        for j in range(seg):
            j2 = (j + 1) % seg
            faces.append((rings[k][j], rings[k][j2], rings[k + 1][j2], rings[k + 1][j]))
    for j in range(seg):
        faces.append((rings[-1][j], rings[-1][(j + 1) % seg], apex))
    faces.append(tuple(reversed(rings[0])))
    ob = new_mesh(name, verts, faces, m)
    at = ob.data.attributes.new("zb", "FLOAT", "POINT")
    for i, v in enumerate(zbs):
        at.data[i].value = v
    return set_cav(ob, cav)


def band_colour(seed=0, pal=BAND, period=0.9, streak=0.7, top_light=0.35, seq=None, cavity=1.2, grit=0.3,
                cracks=0.08, tone=1.0, ramp=None, under=0.0):
    """Banded sandstone: strata by the column's own band height (vertex 'zb'),
    each band a colour from seq, marbled pale streaks, a paler crown, dark
    grooves and a per-face grain. ramp [(z, k)] scales the colour by height,
    under darkens faces that look down."""
    rnd = random.Random(seed)

    def ramp_k(z):
        if z <= ramp[0][0]:
            return ramp[0][1]
        for (z0, k0), (z1, k1) in zip(ramp, ramp[1:]):
            if z <= z1:
                return k0 + (k1 - k0) * smoothstep(z0, z1, z)
        return ramp[-1][1]
    off = Vector((rnd.uniform(0, 90), rnd.uniform(0, 90), rnd.uniform(0, 90)))
    seq = seq or ["dark", "tan", "brown", "mid", "dark", "hi", "brown", "tan", "mid", "dark", "tan", "brown"]
    pal = {k: tuple(min(255.0, v * tone) for v in c) for k, c in pal.items()}
    cols = [pal[s] for s in seq]

    def fn(poly, co, n, vi, li, cav, zb):
        u = zb[vi] / period
        i = math.floor(u)
        f = u - i
        a, b = cols[i % len(cols)], cols[(i + 1) % len(cols)]
        c = mk.mixc(a, b, smoothstep(0.65, 1.0, f))
        c0 = co
        s = fbm(Vector((c0.x * 2.6, c0.y * 2.6, c0.z * 6.0)) + off * 1.9, 3)
        if s > 0.15:
            c = mk.mixc(c, pal["tan"] if s < 0.45 else pal["hi"], min(1.0, (s - 0.15) * 3.0) * streak)
        elif s < -0.3:
            c = mk.mixc(c, pal["dark"], min(1.0, (-0.3 - s) * 3.0) * streak)
        g = grit * (0.6 * (hash01(vi, seed) * 2 - 1) + 0.5 * (hash01(li, seed, 5) * 2 - 1))
        c = tuple(v * (1.0 + g) for v in c)
        if n.z > 0.5:
            c = mk.mixc(c, pal["hi"], top_light * (n.z - 0.5) * 2)
        w = noise.noise(Vector((c0.x * 1.8, c0.y * 1.8, c0.z * 3.2)) + off * 0.7)
        if abs(w) < cracks:
            c = mk.mixc(c, pal["crack"], 0.85)
        cv = cav[vi]
        if cv < 0:
            c = mk.mixc(c, pal["dark"], min(0.85, -cv * cavity * 2.5))
        if under and n.z < -0.1:
            c = mk.mixc(c, pal["dark"], min(1.0, under * (-n.z - 0.1) * 2.0))
        if ramp:
            k = ramp_k(co.z)
            c = tuple(v * k for v in c)
        return c
    return fn


def boulder(c, s, seed=0, sub=1, jag=0.26, cuts=2, m=None, name="boulder"):
    """A rounded lump of stone centred at c with half sizes s, with `cuts`
    flat broken faces sliced off it."""
    rnd = random.Random(seed)
    off = Vector((rnd.uniform(0, 60), rnd.uniform(0, 60), rnd.uniform(0, 60)))
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
    planes = []
    for _ in range(cuts):
        nrm = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.3, 1))).normalized()
        planes.append((nrm, rnd.uniform(0.55, 0.8)))
    rz = rnd.uniform(0, TAU)
    for v in bm.verts:
        d = jag * noise.noise(v.co * 1.3 + off)
        v.co = v.co * (1.0 + d)
        for nrm, dist in planes:
            h = v.co.dot(nrm)
            if h > dist:
                v.co -= nrm * (h - dist)
        v.co = Vector((v.co.x * s[0], v.co.y * s[1], v.co.z * s[2]))
        v.co = Matrix.Rotation(rz, 3, "Z") @ v.co + Vector(c)
    ob = mk.from_bm(name, bm, m)
    return ob


class Heap:
    """A talus heap round a column's foot: `top` high inside r_in (radii
    stretched by rx, ry), falling as (1 - u)^fall to the ground at r_out, so
    fall over 1 gives the concave slope of loose rubble; front lowers it
    toward the camera, lump roughens it; ryb, when given, stretches its back
    half instead of ry."""

    def __init__(self, cx, cy, r_in, r_out, top, fall=1.6, rx=1.0, ry=1.0, front=1.0, lump=0.1, ryb=None, seed=0):
        self.cx, self.cy, self.r_in, self.r_out, self.top = cx, cy, r_in, r_out, top
        self.fall, self.rx, self.ry, self.front, self.lump = fall, rx, ry, front, lump
        self.ryb = ry if ryb is None else ryb
        rnd = random.Random(seed)
        self.off = Vector((rnd.uniform(0, 60), rnd.uniform(0, 60), rnd.uniform(0, 60)))

    def sy(self, s):
        """The y stretch toward bearing sine s: ry in front, ryb behind."""
        return self.ryb if s > 0 else self.ry

    def polar(self, x, y):
        dx, dy = (x - self.cx) / self.rx, (y - self.cy) / self.sy(y - self.cy)
        return math.hypot(dx, dy), math.atan2(dy, dx)

    def u(self, x, y):
        d, _ = self.polar(x, y)
        return max(0.0, (d - self.r_in) / max(self.r_out - self.r_in, 0.1))

    def z(self, x, y, lumps=True):
        d, a = self.polar(x, y)
        u = max(0.0, (d - self.r_in) / max(self.r_out - self.r_in, 0.1))
        if u >= 1.0:
            return 0.0
        t = self.top * (1.0 - (1.0 - self.front) * max(0.0, -math.sin(a)))
        z = t * (1.0 - u) ** self.fall
        if lumps:
            z += self.lump * noise.noise(Vector((x * 1.3, y * 1.3, 0.5)) + self.off) * (1.0 - u)
        return max(0.0, z)

    def normal(self, x, y):
        e = 0.06
        gx = (self.z(x + e, y, False) - self.z(x - e, y, False)) / (2 * e)
        gy = (self.z(x, y + e, False) - self.z(x, y - e, False)) / (2 * e)
        return Vector((-gx, -gy, 1.0)).normalized()


def mound(hp, seg=32, rings=8, rough=0.1, jitter=0.0, m=None, name="mound"):
    """The heap's own surface, under the slabs: rubble and grit, its rim
    wandering and dipping just under the ground; jitter shifts the inner
    rings' points so no radial fan shows."""
    rnd = random.Random(int(hp.off.x * 1000))
    verts = [(hp.cx, hp.cy, hp.top)]
    for k in range(1, rings + 1):
        d0 = hp.r_out * (k / rings) ** 0.85
        for j in range(seg):
            a = TAU * (j + 0.5 * (k % 2)) / seg
            d = d0
            if jitter and k < rings:
                a += rnd.uniform(-0.5, 0.5) * jitter * TAU / seg
                d *= 1.0 + rnd.uniform(-0.5, 0.5) * jitter / k ** 0.5
            rim = 1.0 + (rough * noise.noise(Vector((math.cos(a) * 2.2, math.sin(a) * 2.2, 0.7)) + hp.off)
                         if k == rings else 0.0)
            x = hp.cx + hp.rx * d * rim * math.cos(a)
            y = hp.cy + hp.sy(math.sin(a)) * d * rim * math.sin(a)
            verts.append((x, y, hp.z(x, y) if k < rings else -0.03))
    faces = [(0, 1 + j, 1 + (j + 1) % seg) for j in range(seg)]
    for k in range(1, rings):
        b0, b1 = 1 + (k - 1) * seg, 1 + k * seg
        for j in range(seg):
            j2 = (j + 1) % seg
            faces.append((b0 + j, b1 + j, b1 + j2))
            faces.append((b0 + j, b1 + j2, b0 + j2))
    ob = new_mesh(name, verts, faces, m)
    return set_cav(ob, [0.0] * len(verts))


def hull_rock(pts, c, up, yaw, m=None, name="slab"):
    """The convex hull of local points, turned by yaw, tilted so its Z runs
    along up, and moved to c."""
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    res = bmesh.ops.convex_hull(bm, input=vs)
    junk = list({v for v in res["geom_interior"] + res["geom_unused"] if isinstance(v, bmesh.types.BMVert)})
    if junk:
        bmesh.ops.delete(bm, geom=junk, context="VERTS")
    M = (Matrix.Translation(c) @ Vector((0, 0, 1)).rotation_difference(up).to_matrix().to_4x4()
         @ Matrix.Rotation(yaw, 4, "Z"))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mk.from_bm(name, bm, m)


def slab_points(s, rnd):
    """A broken slab's corners: a jittered box of half sizes s with some
    corners knocked in, and a few points on its sides for more facets."""
    pts = []
    for ix in (-1, 1):
        for iy in (-1, 1):
            for iz in (-1, 1):
                k = rnd.uniform(0.5, 0.85) if rnd.random() < 0.45 else 1.0
                pts.append(Vector((ix * s[0] * rnd.uniform(0.75, 1.0) * k, iy * s[1] * rnd.uniform(0.7, 1.0) * k,
                                   iz * s[2] * rnd.uniform(0.7, 1.0) * (k if iz > 0 else 1.0))))
    for _ in range(3):
        a = rnd.uniform(0, TAU)
        pts.append(Vector((math.cos(a) * s[0] * 0.97, math.sin(a) * s[1] * 0.97, rnd.uniform(-0.4, 0.6) * s[2])))
    return pts


def pebble_points(s, rnd):
    pts = []
    for i in range(6):
        a = TAU * i / 6 + rnd.uniform(-0.4, 0.4)
        pts.append(Vector((math.cos(a) * s * rnd.uniform(0.7, 1.1), math.sin(a) * s * rnd.uniform(0.6, 1.0),
                           rnd.uniform(-0.3, 0.3) * s)))
    pts.append(Vector((rnd.uniform(-0.3, 0.3) * s, rnd.uniform(-0.3, 0.3) * s, s * rnd.uniform(0.45, 0.7))))
    pts.append(Vector((0.0, 0.0, -0.5 * s)))
    return pts


def spots_on(hp, n, size, seed=0, gap=0.45, inner=0.85, outer=0.92, avoid=(), grow=0.3, arc=None):
    """Poisson spots over a heap from `inner` x r_in to `outer` x r_out, sized
    from `size`, shrinking by grow toward the rim; avoid is [(x, y, r)]."""
    rnd = random.Random(seed)
    out = []
    tries = 0
    lo, hi = hp.r_in * inner, hp.r_out * outer
    while len(out) < n and tries < n * 80:
        tries += 1
        a = math.radians(rnd.uniform(*arc)) if arc else rnd.uniform(0, TAU)
        d = lo + (hi - lo) * math.sqrt(rnd.uniform(0.0, 1.0))
        x, y = hp.cx + hp.rx * d * math.cos(a), hp.cy + hp.sy(math.sin(a)) * d * math.sin(a)
        if any(math.hypot(x - ax, y - ay) < ar for ax, ay, ar in avoid):
            continue
        u = hp.u(x, y)
        s = rnd.uniform(*size) * (1.0 - grow * min(1.0, u))
        if all(math.hypot(x - x2, y - y2) >= gap * (s + s2) for x2, y2, s2 in out):
            out.append((x, y, s))
    return out


def packed_spots(hp, n, size, seed=0, gap=0.8, inner=0.85, outer=0.92, avoid=(), grow=0.3, tries=80):
    """Spots for a packed pile: n sizes from `size` placed biggest first, kept
    gap x (s + s2) apart measured over the heap's surface, so stones on a
    steep slope touch as they do on the flat."""
    rnd = random.Random(seed)
    sizes = sorted((rnd.uniform(*size) for _ in range(n)), reverse=True)
    lo, hi = hp.r_in * inner, hp.r_out * outer
    out = []
    for s0 in sizes:
        for _ in range(tries):
            a = rnd.uniform(0, TAU)
            d = lo + (hi - lo) * math.sqrt(rnd.uniform(0.0, 1.0))
            x, y = hp.cx + hp.rx * d * math.cos(a), hp.cy + hp.sy(math.sin(a)) * d * math.sin(a)
            if any(math.hypot(x - ax, y - ay) < ar for ax, ay, ar in avoid):
                continue
            s = s0 * (1.0 - grow * min(1.0, hp.u(x, y)))
            z = hp.z(x, y, False)
            if all(math.sqrt((x - x2) ** 2 + (y - y2) ** 2 + (z - z2) ** 2) >= gap * (s + s2) for x2, y2, z2, s2 in out):
                out.append((x, y, z, s))
                break
    return [(x, y, s) for x, y, _, s in out]


def boulder_points(s, rnd, n=16):
    """A rounded but broken stone: points on an ellipsoid of half sizes s,
    jittered, with a flatter underside and one or two faces sheared off."""
    pts = []
    ga = math.pi * (3 - math.sqrt(5))
    cuts = [(Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0.0, 1.0))).normalized(),
             rnd.uniform(0.6, 0.8)) for _ in range(rnd.choice((1, 2)))]
    for i in range(n):
        zz = 1 - 2 * (i + 0.5) / n
        rr = math.sqrt(max(0.0, 1 - zz * zz))
        a = i * ga + rnd.uniform(-0.3, 0.3)
        v = Vector((rr * math.cos(a), rr * math.sin(a), max(zz, -0.55))) * rnd.uniform(0.85, 1.08)
        for nrm, dist in cuts:
            h = v.dot(nrm)
            if h > dist:
                v -= nrm * (h - dist)
        pts.append(Vector((v.x * s[0], v.y * s[1], v.z * s[2])))
    return pts


def rubble(hp, spots, seed=0, flat=0.62, tilt=0.35, shape="slab", m=None, name="slabs"):
    """Angular slabs (or rounded-angular boulders, shape 'boulder') lying
    on the heap, following its slope, two thirds of each standing clear."""
    rnd = random.Random(seed)
    obs = []
    for i, (x, y, s) in enumerate(spots):
        if shape == "boulder":
            hs = (s * rnd.uniform(0.95, 1.15), s * rnd.uniform(0.8, 1.0), s * flat * rnd.uniform(0.85, 1.15))
        else:
            hs = (s * rnd.uniform(0.95, 1.35), s * rnd.uniform(0.65, 0.95), s * flat * rnd.uniform(0.8, 1.25))
        up = (hp.normal(x, y) + Vector((rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt), 0.0))).normalized()
        pts = boulder_points(hs, rnd) if shape == "boulder" else slab_points(hs, rnd)
        z = hp.z(x, y) + hs[2] * 0.35
        obs.append(hull_rock(pts, Vector((x, y, z)), up, rnd.uniform(0, TAU), m=m, name="%s%d" % (name, i)))
    return obs


def gravel(spots, hps, seed=0, m=None, name="gravel"):
    """Small broken stones lying on the heaps or the ground."""
    rnd = random.Random(seed)
    obs = []
    for i, (x, y, s) in enumerate(spots):
        z = max([h.z(x, y) for h in hps] + [0.0])
        up = (Vector((0, 0, 1)) + Vector((rnd.uniform(-0.4, 0.4), rnd.uniform(-0.4, 0.4), 0))).normalized()
        obs.append(hull_rock(pebble_points(s, rnd), Vector((x, y, z + 0.15 * s)), up, rnd.uniform(0, TAU), m=m,
                             name="%s%d" % (name, i)))
    return obs


def ring_spots(cx, cy, rx, ry, n, size, d0=0.7, d1=1.12, seed=0, keep=None, ryb=None):
    """Spots for gravel in an elliptic band from d0 to d1 of (rx, ry), ryb
    behind when given."""
    rnd = random.Random(seed)
    out = []
    tries = 0
    ryb = ry if ryb is None else ryb
    while len(out) < n and tries < n * 40:
        tries += 1
        a = rnd.uniform(0, TAU)
        d = rnd.uniform(d0, d1)
        x, y = cx + rx * d * math.cos(a), cy + (ryb if math.sin(a) > 0 else ry) * d * math.sin(a)
        if keep is not None and not keep(x, y):
            continue
        out.append((x, y, rnd.uniform(*size)))
    return out


def sand(cx, cy, rx, ry, seed=0, seg=40, rings=4, h=0.06, rough=0.22, ryb=None, m=None, name="sand"):
    """A thin sandy skirt over an ellipse (ryb deep behind when given), its
    rim broken and wandering, in small faces so the grit can speckle."""
    rnd = random.Random(seed)
    ryb = ry if ryb is None else ryb
    off = Vector((rnd.uniform(0, 50), rnd.uniform(0, 50), 0))
    verts = [(cx, cy, h)]
    for k in range(1, rings + 1):
        d = k / rings
        for j in range(seg):
            a = TAU * (j + 0.5 * (k % 2)) / seg
            q = Vector((math.cos(a), math.sin(a), 0.3))
            rim = 1.0 + (rough * (0.65 * noise.noise(q * 2.0 + off) + 0.35 * noise.noise(q * 5.5 + off))) * d
            # the rim dips under the ground so it never fights the terrain for the same pixels
            verts.append((cx + rx * d * rim * math.cos(a), cy + (ryb if math.sin(a) > 0 else ry) * d * rim * math.sin(a),
                          h * (1.0 - d * d) + (-0.03 if k == rings else rnd.uniform(0, 0.03))))
    faces = [(0, 1 + j, 1 + (j + 1) % seg) for j in range(seg)]
    for k in range(1, rings):
        b0, b1 = 1 + (k - 1) * seg, 1 + k * seg
        for j in range(seg):
            j2 = (j + 1) % seg
            faces.append((b0 + j, b1 + j, b1 + j2))
            faces.append((b0 + j, b1 + j2, b0 + j2))
    ob = new_mesh(name, verts, faces, m)
    return set_cav(ob, [0.0] * len(verts))


def twig(base, direction, length, seed=0, m=None, name="twig"):
    """A dead thorny sprig: a bent stem with two or three side spurs."""
    rnd = random.Random(seed)
    parts = []
    d = Vector(direction).normalized()
    p = Vector(base)
    pts = [p.copy()]
    for i in range(3):
        d = (d + Vector((rnd.uniform(-0.35, 0.35), rnd.uniform(-0.35, 0.35), rnd.uniform(-0.1, 0.25)))).normalized()
        p = p + d * length / 3
        pts.append(p.copy())
    for i in range(3):
        parts.append(mk.rod(pts[i], pts[i + 1], 0.07 * (1 - i / 3.5), 0.07 * (1 - (i + 1) / 3.5), seg=4, mat=m,
                            name=name))
    for i in (1, 2, 2):
        sd = (Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0.2, 1)))).normalized()
        parts.append(mk.rod(pts[i], pts[i] + sd * length * 0.4, 0.04, 0.006, seg=3, mat=m, name=name))
    ob = join(parts, name)
    return set_cav(ob, [0.0] * len(ob.data.vertices))


def slab_colour(seed=0, pal=SLAB):
    """Broken sandstone: body colour mottled toward pale and dark, a tint per
    slab, a per-face grain."""
    rnd = random.Random(seed)
    off = Vector((rnd.uniform(0, 90), rnd.uniform(0, 90), rnd.uniform(0, 90)))

    def fn(poly, co, n, vi, li, tint):
        v = fbm(co * 2.2 + off, 3)
        c = mk.mixc(pal["body"], pal["light"], smoothstep(0.0, 0.5, v)) if v > 0 else \
            mk.mixc(pal["body"], pal["dark"], smoothstep(0.0, 0.5, -v))
        if noise.noise(co * 5.3 + off * 0.3) > 0.45:
            c = mk.mixc(c, pal["dark"], 0.6)
        if n.z < -0.2:
            c = mk.mixc(c, pal["dark"], 0.7)
        g = 0.1 * (hash01(poly.index, seed) * 2 - 1) + 0.08 * (hash01(li, seed, 9) * 2 - 1)
        return tuple(v_ * tint * (1.0 + g) for v_ in c)
    return fn


def grit_colour(seed=0, pal=SAND, dark=0.3, light=0.25):
    """Speckled grit: each corner dark, mid or light."""
    def fn(poly, co, n, vi, li):
        k = hash01(li, seed, 7)
        return pal["dark"] if k < dark else pal["light"] if k > 1.0 - light else pal["mid"]
    return fn


def hoodoo(p):
    """Columns (profile list) standing in talus heaps of angular slabs and
    gravel, on a sandy skirt, from a table entry."""
    mc = mat("spire_band", 0.88)
    mb = mat("spire_boulder", 0.9)
    ms = mat("spire_sand", 0.95)
    mt = mat("spire_twig", 0.9)
    seed = p.get("seed", 0)
    cols = []
    for i, c in enumerate(p["columns"]):
        kw = dict(c)
        prof = kw.pop("profile")
        kw.setdefault("seed", seed + 11 * i)
        cols.append(shade(column(prof, m=mc, name="col%d" % i, **kw), angle=42))
    avoid = [(c.get("x", 0.0), c.get("y", 0.0), 0.8 * c["profile"][0][1]) for c in p["columns"][1:]]
    hps, slabs, pebbles, grounds = [], [], [], []
    for i, e in enumerate(p.get("heaps", [])):
        hp = Heap(e["cx"], e["cy"], e["r_in"], e["r_out"], e["top"], fall=e.get("fall", 1.6), rx=e.get("rx", 1.0),
                  ry=e.get("ry", 1.0), front=e.get("front", 1.0), lump=e.get("lump", 0.1), ryb=e.get("ry_back"),
                  seed=seed + 7 * i)
        hps.append(hp)
        grounds.append(shade(mound(hp, seg=e.get("seg", 32), rings=e.get("rings", 8), jitter=e.get("jitter", 0.0),
                                   m=ms, name="mound%d" % i), angle=50))
        if e.get("packed"):
            sp = packed_spots(hp, e["n"], e["size"], seed=seed + 7 * i + 1, gap=e.get("gap", 0.8),
                              inner=e.get("inner", 0.85), outer=e.get("outer", 0.92), avoid=avoid,
                              grow=e.get("grow", 0.3))
        else:
            sp = spots_on(hp, e["n"], e["size"], seed=seed + 7 * i + 1, gap=e.get("gap", 0.45),
                          inner=e.get("inner", 0.85), outer=e.get("outer", 0.92), avoid=avoid, arc=e.get("arc"),
                          grow=e.get("grow", 0.3))
        if e.get("collar"):
            # a few slabs laid over the top of the cone, round the column's foot
            cn, csz, cout, *cin = e["collar"]
            sp += spots_on(hp, cn, csz, seed=seed + 7 * i + 5, gap=0.6, inner=cin[0] if cin else 0.7,
                           outer=(hp.r_in + cout * (hp.r_out - hp.r_in)) / hp.r_out, avoid=avoid, grow=0.0)
        slabs += rubble(hp, sp, seed=seed + 7 * i + 2, flat=e.get("flat", 0.62), tilt=e.get("tilt", 0.35),
                        shape=e.get("shape", "slab"), m=mb, name="slab%d_" % i)
        if e.get("chips"):
            sp2 = spots_on(hp, e["chips"], e.get("chip_size", (0.08, 0.16)), seed=seed + 7 * i + 3, gap=0.9,
                           inner=1.0, outer=1.0, avoid=avoid, grow=0.3)
            pebbles += gravel(sp2, hps, seed=seed + 7 * i + 4, m=mb, name="peb%d_" % i)
    if p.get("sand"):
        cx, cy, rx, ry, *ryb = p["sand"]
        ryb = ryb[0] if ryb else None
        grounds.append(sand(cx, cy, rx, ry, seed=seed + 4, rough=p.get("sand_rough", 0.3), ryb=ryb, m=ms))
        # loose grit over the skirt's edge breaks its outline
        n, size = p.get("grit", (44, (0.05, 0.13)))
        pebbles += gravel(ring_spots(cx, cy, rx, ry, n, size, d0=0.8, d1=1.15, seed=seed + 8, ryb=ryb), hps,
                          seed=seed + 9, m=mb, name="grit")
    rocks = []
    tints = []
    rnd = random.Random(seed + 99)
    for q in slabs:
        rocks.append(shade(q, flat=True))
        tints.append(rnd.uniform(0.82, 1.12))
    for q in pebbles:
        rocks.append(shade(q, flat=True))
        tints.append(rnd.uniform(0.8, 1.2))
    rock = join(rocks, "rubble") if rocks else None
    rest = []
    for i, tw in enumerate(p.get("twigs", [])):
        x, y, z = tw[0]
        z = min(z, max([h.z(x, y) for h in hps] + [0.3]) - 0.1)
        rest.append(twig((x, y, z), tw[1], tw[2], seed=seed + 5 * i, m=mt, name="twig%d" % i))
    parts = cols + ([rock] if rock is not None else []) + grounds + rest
    ao = bake_ao(parts, dist=p.get("ao_dist", 1.6), rays=p.get("rays", 24))
    for i, q in enumerate(cols):
        cav = get_cav(q)
        zb = [d.value for d in q.data.attributes["zb"].data]
        bk = dict(p.get("bands", {}))
        bk.setdefault("period", p["columns"][i].get("period", 0.9))
        fn = band_colour(seed + i, **bk)
        paint(q, lambda pp, co, n, vi, li, fn=fn, cav=cav, zb=zb: fn(pp, co, n, vi, li, cav, zb), ao[q.name], lo=0.3)
    if rock is not None:
        fnb = slab_colour(seed, pal=p.get("slab_pal", SLAB))
        per_face = _piece_index(rock)
        paint(rock, lambda pp, co, n, vi, li: fnb(pp, co, n, vi, li, tints[per_face[pp.index] % len(tints)]),
              ao[rock.name], lo=0.12, gamma=1.5)
    for q in grounds:
        if q.name.startswith("mound"):
            fn = grit_colour(seed, pal=p.get("mound_pal", GRIT), dark=0.35, light=0.2)
            paint(q, lambda pp, co, n, vi, li, fn=fn: fn(pp, co, n, vi, li), ao[q.name], lo=0.1, gamma=1.8)
        else:
            fn = grit_colour(seed + 1, pal=p.get("sand_pal", SAND))
            paint(q, lambda pp, co, n, vi, li, fn=fn: fn(pp, co, n, vi, li), ao[q.name], lo=0.55)
    for q in rest:
        paint(q, lambda pp, co, n, vi, li: TWIG, None)
    return parts


def _piece_index(ob):
    """Which connected piece each face of a joined mesh belongs to."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    seen = [-1] * len(bm.faces)
    k = 0
    for f in bm.faces:
        if seen[f.index] >= 0:
            continue
        stack_ = [f]
        seen[f.index] = k
        while stack_:
            g = stack_.pop()
            for e in g.edges:
                for h in e.link_faces:
                    if seen[h.index] < 0:
                        seen[h.index] = k
                        stack_.append(h)
        k += 1
    bm.free()
    return seen


# ---------------------------------------------------------------- sturdier

def sturdy(p):
    """The owner's sturdier build (2026-09-27): a pixel-exact fit to the
    classic camera makes tall parts spindly in 3D, so parts over `tall`
    cells get `girth` times the radius and `height` times the height, and
    step back by up to `back` cells to win back `hold` of the height the
    classic view loses (it draws 2 y + z)."""
    st = p.get("sturdy")
    if not st:
        return p
    g, s = st.get("girth", 1.18), st.get("height", 0.86)
    back, hold, tall = st.get("back", 0.0), st.get("hold", 1.0), st.get("tall", 1.5)
    q = dict(p)

    def step(h):
        return min(back, 0.5 * (1.0 - s) * h * hold)
    if p["kind"] == "crag":
        peaks, dy0 = [], 0.0
        for i, e in enumerate(p.get("peaks", [])):
            x, y, h, r = e[:4]
            kw = dict(e[4]) if len(e) > 4 else {}
            if h > tall:
                dy = step(h)
                dy0 = dy if i == 0 else dy0
                y, h, r = y + dy, h * s, r * g
                if "ry" in kw:
                    kw["ry"] *= g
            peaks.append((x, y, h, r, kw))
        q["peaks"] = peaks
        ridges = []
        for a, b, *rest in p.get("ridges", []):
            kw = dict(rest[0]) if rest else {}
            a = (a[0], a[1] + (dy0 if kw.pop("follow", True) else 0.0), a[2] * s)
            kw["width"] *= g
            ridges.append((a, b, kw))
        q["ridges"] = ridges
    else:
        cols, dy0 = [], None
        for c in p["columns"]:
            c = dict(c)
            H = max(z for z, *_ in c["profile"])
            if H > tall:
                dy = step(H)
                dy0 = dy if dy0 is None else dy0
                c["y"] = c.get("y", 0.0) + dy
                c["profile"] = [(z * s if z > 0 else z, r * g, *rest) for z, r, *rest in c["profile"]]
                c["period"] = c.get("period", 0.9) * s
            cols.append(c)
        q["columns"] = cols
        heaps = []
        for e in p.get("heaps", []):
            e = dict(e)
            # the heap follows the main column half way, so its back stays buried
            e["cy"] = e["cy"] + 0.5 * (dy0 or 0.0)
            e["r_in"] = e["r_in"] * g
            e["r_out"] = max(e["r_out"], e["r_in"] + 0.8)
            heaps.append(e)
        q["heaps"] = heaps
    return q


def tidy(parts):
    """Drops working attributes and custom properties before the join."""
    for q in parts:
        at = q.data.attributes.get("cav")
        if at is not None:
            q.data.attributes.remove(at)
        at = q.data.attributes.get("zb")
        if at is not None:
            q.data.attributes.remove(at)
        for k in list(q.keys()):
            del q[k]
    return parts
