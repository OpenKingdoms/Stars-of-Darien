"""Kirenna's head for kirenna.py: a realistic woman's face from the female
body in Blender Studio's CC0 Human Base Meshes (unitkit.human_head),
refined as a sea queen's, with fine features, high cheekbones, full lips
and a tapering jaw, and ears drawn back into fins.

Her pale blonde hair is drawn back sleek over the skull under strand
cards, gathered at the back in a gold clasp and plaited into a braid down
her back on the original's ponytail pieces (Pony1 to Pony4), so the script
swings it. A gilded fin crest rises along the top of her head from a slim
circlet with an aquamarine at the brow, where the original wears its gold
crest. Everything is placed by casting rays onto the face, so it follows
the face's real shape.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import elsin_head as eh
import kirenna_armour as ka
import unitkit as uk
from elsin_armour import dome, rim, shell, tube_rings

V = Vector
EYE_Z = 3.72          # the eyes' height, cells
NECK = (0.0, 0.02)    # the neck's axis, under the head's pivot
SCALE = 2.42          # cells to a metre: a head of about 0.53 cells
SOURCE = "GEO-body_female_realistic"
EYE_GROW = (0.013, 0.024, 1.07)  # the eyes and their lids made larger: inner and outer radius in metres, and by how much


def lerp(a, b, t):
    return a + (b - a) * t


def _g(a, s):
    return math.exp(-(a / s) ** 2)


def _step(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def regal(p, marks):
    """The bundle's woman as Kirenna: brows arching a little higher at their
    tails, eyes lifted at the outer corners, high full cheekbones over a
    soft hollow, a slimmer nose with a refined tip, fuller lips, and a jaw
    tapering to a delicate chin. The ears are drawn back and up into fins.
    p is in metres, centred between the eyes, -y forward."""
    x, y, z = p
    q = Vector(p)
    for e in marks["eyes"]:
        d = (Vector(p) - e).length
        if d < EYE_GROW[1] and y < e.y + 0.012:
            k = 1.0 - _step(EYE_GROW[0], EYE_GROW[1], d)
            q = e + (q - e) * (1.0 + (EYE_GROW[2] - 1.0) * k)                # larger eyes
    x, y, z = q
    sx = 1.0 if x >= 0 else -1.0
    ax = abs(x)
    front = 1.0 if y < -0.01 else max(0.0, 1.0 - (y + 0.01) / 0.04)
    q.z += 0.0011 * _g(z - 0.024, 0.009) * _g(ax - 0.047, 0.013) * front      # brows arching at their tails
    q.y += 0.0007 * _g(z - 0.02, 0.01) * _g(ax - 0.022, 0.014) * front        # a softer brow ridge
    q.z += 0.0009 * _g(z, 0.007) * _g(ax - 0.05, 0.008) * front               # the eyes' outer corners lifted
    lid = _g(ax - 0.033, 0.013) * front * (1.0 if y < -0.004 else 0.0)
    q.z -= 0.0017 * _g(z - 0.0085, 0.005) * lid                               # calm upper lids, a little lower
    cb = _g(z + 0.016, 0.013) * _g(ax - 0.05, 0.015) * front
    q.y -= 0.0026 * cb                                                        # high, full cheekbones
    q.x += sx * 0.0008 * cb
    q.z += 0.0008 * cb
    q.y += 0.0011 * _g(z + 0.054, 0.012) * _g(ax - 0.045, 0.012) * front      # a soft hollow under them
    nose = _g(z + 0.038, 0.017) * _g(ax, 0.022) * front
    q.x *= 1.0 - 0.22 * nose                                                  # a slimmer nose
    q.x *= 1.0 - 0.28 * _g(z + 0.043, 0.009) * _g(ax - 0.016, 0.012) * front  # neater wings to it
    q.y -= 0.0007 * _g(z + 0.012, 0.012) * _g(ax, 0.008) * front              # a straight, fine bridge
    q.z += 0.0011 * _g(z + 0.043, 0.007) * _g(ax, 0.011) * front              # its tip a little lifted
    q.y -= 0.0012 * _g(z + 0.064, 0.0045) * _g(ax, 0.017) * front             # a fuller upper lip
    q.y -= 0.0016 * _g(z + 0.078, 0.0055) * _g(ax, 0.016) * front             # and lower
    q.z += 0.0012 * _g(z + 0.071, 0.005) * _g(ax - 0.025, 0.006) * front      # the corners of the mouth lifted
    q.y -= 0.0016 * _g(z + 0.106, 0.012) * _g(ax, 0.016) * front              # a neat chin
    jaw = _step(-0.035, -0.095, z) * (1.0 - _step(0.03, 0.08, y)) * (1.0 - _step(-0.13, -0.16, z))
    q.x *= 1.0 - 0.085 * jaw                                                  # the jaw tapering to it
    below = _step(0.0, -0.02, z) * (1.0 - _step(0.03, 0.09, y))
    q.z += z * 0.04 * below                                                   # a longer, finer lower face
    q.x *= 1.0 - 0.06 * _step(-0.015, -0.045, z) * (1.0 - _step(0.03, 0.08, y))  # a slimmer lower face
    q.x *= 1.0 - 0.05 * _step(-0.11, -0.15, z)                                # a slender neck
    q.x *= 1.0 - 0.12 * _step(-0.125, -0.15, z) * (1.0 - _step(-0.15, -0.17, z))
    # the ears' tops drawn back and up to points, the fins along them added apart
    w = _g(y - 0.086, 0.018) * _step(-0.034, 0.004, z) * _step(0.07, 0.081, ax)
    w = w ** 1.3
    q.z += 0.026 * w
    q.y += 0.036 * w
    q.x += sx * 0.008 * w
    return q


class Face:
    """The fitted head and a way to find points on it."""

    def __init__(self, head, eyes):
        self.ob = head
        self.eyes = eyes
        bm = bmesh.new()
        bm.from_mesh(head.data)
        self.bvh = BVHTree.FromBMesh(bm)
        bm.free()
        vs = [v.co for v in head.data.vertices]
        self.top = max(v.z for v in vs)
        self.chin = EYE_Z - 0.11 * SCALE
        self.nose = min(v.y for v in vs)
        self.verts = [v.copy() for v in vs]

    def at(self, phi, z, inner=False):
        """The surface seen from the head's axis at height z, phi round from
        straight ahead (-Y), toward +X positive. None where nothing is hit.
        inner finds the skull from inside, under an ear."""
        o = V((NECK[0], NECK[1], z))
        d = V((math.sin(phi), -math.cos(phi), 0.0))
        hit = self.bvh.ray_cast(o, d, 0.7) if inner else self.bvh.ray_cast(o + d * 0.6, -d, 0.7)
        return (hit[0], hit[1]) if hit[0] is not None else (None, None)

    def out(self, phi, z, by, inner=False):
        """A point by cells out from the surface, along the ray."""
        p, n = self.at(phi, z, inner)
        if p is None:
            return None
        return p + V((math.sin(phi), -math.cos(phi), 0.0)) * by

    def dome(self, theta, phi, by=0.0):
        """The skull seen from a point inside the head, theta up from level
        and phi round as in at(), for the top of the head."""
        c = V((NECK[0], NECK[1] + 0.02, EYE_Z + 0.02))
        d = V((math.sin(phi) * math.cos(theta), -math.cos(phi) * math.cos(theta), math.sin(theta)))
        hit = self.bvh.ray_cast(c, d, 0.8)
        if hit[0] is None:
            return None, None
        return hit[0] + d * by, d


# ---------------------------------------------------------------- pictures

def strand_picture(size=512, seed=31):
    """Pale blonde strands for the cards, from numbers, each with its own
    shade and sway, tapering to its tip, as RGB and the cut-out alpha. Row
    0 is the tip, the root at the top."""
    rng = np.random.default_rng(seed)
    rgba = np.zeros((size, size, 4), np.float32)
    for _ in range(520):
        x0 = rng.uniform(0, size)
        root = rng.uniform(size * 0.85, size)
        tip = rng.uniform(0, size * 0.35)
        w = rng.uniform(0.9, 2.2)
        sway = rng.uniform(-5, 5)
        freq = rng.uniform(0.8, 2.0)
        col = np.array([0.5, 0.41, 0.25]) * rng.uniform(0.7, 1.2)
        if rng.uniform() < 0.2:
            col = np.array([0.66, 0.58, 0.42]) * rng.uniform(0.85, 1.1)
        if rng.uniform() < 0.12:
            col = np.array([0.3, 0.23, 0.13]) * rng.uniform(0.8, 1.1)
        for y in range(int(tip), int(root)):
            t = max(0.0, (y - tip) / max(1.0, root - tip))
            x = x0 + sway * math.sin(t * math.pi * freq)
            half = w * (0.35 + 0.65 * t ** 0.5)
            for xi in range(int(x - half - 1), int(x + half + 2)):
                xm = xi % size
                a = max(0.0, 1.0 - abs(xi - x) / (half + 0.5)) * min(1.0, t * 4.0)
                if a > rgba[y, xm, 3]:
                    rgba[y, xm, :3] = col * (0.85 + 0.3 * a)
                    rgba[y, xm, 3] = a
    return uk.image("kirenna_strands", rgba)


# ---------------------------------------------------------------- the head

def head(rig, M, assets):
    """The head piece's parts in model space at rest: the face, the eyes,
    the hair over the skull, the crest and circlet, the ears' fins, and
    the braid down to the first ponytail piece; the strand cards; and the
    braid's pieces. Returns (atlas parts, card parts, {piece: parts}, face)."""
    ob, eyes = uk.human_head(assets, EYE_Z, NECK, SCALE, cut_z=3.38, shape=regal, source=SOURCE, eye=".eye", multires=2)
    ob.data.materials.clear()
    ob.data.materials.append(M["skin"])
    dec = ob.modifiers.new("lighter", "DECIMATE")
    dec.ratio = 0.052
    dec.use_symmetry = True
    dec.symmetry_axis = "X"
    uk.apply(ob)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    # the decimation folds the skin over the lids here and there: smooth it out round the eyes
    near_eyes = [v for v in bm.verts if 1.15 < min((v.co - c).length / r for c, r in eyes) < 2.6]
    for _ in range(3):
        bmesh.ops.smooth_vert(bm, verts=near_eyes, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bmesh.ops.dissolve_degenerate(bm, dist=0.0008, edges=bm.edges[:])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(ob.data)
    bm.free()
    for name in ("sharp_edge", "sharp_face"):
        if name in ob.data.attributes:
            ob.data.attributes.remove(ob.data.attributes[name])
    uk.shade(ob, 180)
    face = Face(ob, eyes)
    parts = [ob]
    eyes = [(c, r * EYE_GROW[2]) for c, r in eyes]
    face.eyes = eyes
    for c, r in eyes:
        parts.append(eh.ellipsoid_ob(c + V((0, 0.003, 0)), (r * 0.98, r * 0.98, r * 0.98), 12, 8, M["eye"], "eye"))
    cap = hair_cap(face, M)
    parts.append(cap)
    parts += circlet(face, M) + crest(face, M) + ear_fins(face, M)
    clasp_at, braid_from = gather(face)
    parts.append(clasp(clasp_at, braid_from, M))
    cards = top_cards(face, M, cap, clasp_at) + hairline_cards(face, M, clasp_at)
    pieces = braid(rig, M, clasp_at, braid_from)
    parts += pieces.pop("Head")
    return parts, cards, pieces, face


def hairline(phi):
    """How far above the eyes the hair starts, round the head: high on a
    smooth brow, sweeping up over the temples and the ears, which stay
    bare, and down behind them to the nape."""
    a = abs(math.atan2(math.sin(phi), math.cos(phi)))
    if a < 0.8:
        return lerp(0.178, 0.155, (a / 0.8) ** 1.5)
    if a < 1.3:
        return lerp(0.155, 0.1, smooth01((a - 0.8) / 0.5))
    if a < 2.05:
        return 0.1 - 0.012 * math.sin(math.pi * (a - 1.3) / 0.75)
    return lerp(0.1, -0.2, min(1.0, (a - 2.05) / 0.8) ** 0.9)


def smooth01(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3.0 - 2.0 * t)


def hair_cap(face, M):
    """The hair drawn back sleek over the skull from the hairline to the
    crown, close over it, a little fuller toward the back where it
    gathers."""
    bm = bmesh.new()
    n, rows_n = 32, 10
    rows = []
    for j in range(rows_n):
        t = j / (rows_n - 1)
        row = []
        for i in range(n):
            phi = 2 * math.pi * i / n
            back = max(0.0, -math.cos(phi))
            z = lerp(face.top - 0.004, EYE_Z + hairline(phi), t ** 0.9)
            p = face.out(phi, min(z, face.top - 0.012), 0.013 + 0.02 * back * (1 - t) + 0.008 * (1 - t), inner=True)
            if p is None:
                p = V((NECK[0], NECK[1], z))
            row.append(V((p.x, p.y, max(z, p.z))))
        rows.append(row)
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    for r in range(rows_n - 1):
        for c in range(n):
            d = (c + 1) % n
            bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][d], vs[r][d]))
    top = bm.verts.new(V((NECK[0], NECK[1] + 0.02, face.top + 0.012)))
    for c in range(n):
        bm.faces.new((top, vs[0][c], vs[0][(c + 1) % n]))
    return uk.shade(uk.mesh("haircap", bm, [M["hair"]]), 70)


def gather(face):
    """Where the hair is gathered behind (the clasp's centre) and the way
    the braid leaves it."""
    p = face.out(math.pi, EYE_Z + 0.02, 0.05, inner=True)
    return p, V((0.0, 0.75, -0.66)).normalized()


def clasp(at, way, M):
    """A gold clasp round the gathered hair, a band with a rolled lip at
    each end."""
    a, b = at - way * 0.04, at + way * 0.07
    rings = tube_rings([a, a.lerp(b, 0.15), a.lerp(b, 0.5), a.lerp(b, 0.85), b], [(0.048, 0.044), (0.058, 0.053), (0.055, 0.05), (0.058, 0.053), (0.046, 0.042)], 14)
    bm = bmesh.new()
    uk.loft(bm, rings, cap0=True, cap1=True)
    return uk.shade(uk.mesh("clasp", bm, [M["gold"]]), 40)


def top_cards(face, M, cap, gather_at, count=50, seed=11):
    """Strands lying on the hair, swept back from the hairline to where it
    gathers behind, so the sleek hair has a soft edge and a grain."""
    rng = np.random.default_rng(seed)
    bm0 = bmesh.new()
    bm0.from_mesh(cap.data)
    bvh = BVHTree.FromBMesh(bm0)
    bm0.free()
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    centre = V((NECK[0], NECK[1] + 0.02, EYE_Z + 0.04))
    for i in range(count):
        phi = rng.uniform(-2.3, 2.3)
        z0 = EYE_Z + hairline(phi) + rng.uniform(0.005, 0.03)
        start = face.out(phi, min(z0, face.top - 0.03), 0.0, inner=True)
        if start is None:
            continue
        path = []
        for j in range(7):
            t = j / 6
            q = start.lerp(gather_at, t ** 1.1)
            q.z += 0.09 * math.sin(math.pi * t) * (1.0 - abs(math.sin(phi)) * 0.6)
            d = (q - centre).normalized()
            hit = bvh.ray_cast(centre + d * 0.7, -d, 0.8)
            if hit[0] is None:
                break
            path.append(hit[0] + hit[1] * (0.004 + 0.003 * (i % 3)))
        if len(path) >= 4:
            hint = (path[len(path) // 2] - centre).normalized()
            eh.card(bm, uv, path, rng.uniform(0.06, 0.09), hint)
    return [uk.shade(uk.mesh("topcards", bm, [M["strands"]], outward=False), 80)]


def hairline_cards(face, M, gather_at, count=48, seed=37):
    """Short strands rooted on the skin just under the hairline, combed
    back onto the hair, so its edge is soft rather than a cap's."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        phi = rng.uniform(-2.2, 2.2)
        z0 = EYE_Z + hairline(phi) - rng.uniform(0.0, 0.015)
        path = []
        for k in range(4):
            t = k / 3
            ph = phi * (1.0 - 0.12 * t)
            z = z0 + 0.1 * t * (1.0 - 0.7 * abs(math.sin(phi)))
            p = face.out(ph, min(z, face.top - 0.02), 0.003 + 0.012 * t, inner=True)
            if p is None:
                break
            path.append(p + (gather_at - p).normalized() * 0.04 * t)
        if len(path) >= 3:
            eh.card(bm, uv, path, rng.uniform(0.025, 0.04), V((math.sin(phi), -math.cos(phi), 0.3)))
    return [uk.shade(uk.mesh("hairlinecards", bm, [M["strands"]], outward=False), 80)]


# ---------------------------------------------------------------- the crest and circlet

def circlet(face, M):
    """A slim gold circlet round the head at the hairline, dipping to a
    point at the brow where an aquamarine drop hangs."""
    pts = []
    for i in range(33):
        phi = lerp(-2.5, 2.5, i / 32)
        z = EYE_Z + hairline(phi) - 0.012 - 0.03 * math.exp(-(phi / 0.35) ** 2)
        p = face.out(phi, min(z, face.top - 0.03), 0.012, inner=True)
        if p is not None:
            pts.append(p)
    parts = [rim(pts, 0.011, M["gold"], "circlet", sides=4)]
    front = face.out(0.0, EYE_Z + hairline(0.0) - 0.045, 0.016, inner=True)
    bm = bmesh.new()
    rings = []
    for t, r in ((0.0, 0.002), (0.25, 0.017), (0.55, 0.022), (0.8, 0.018), (1.0, 0.002)):
        rings.append(uk.ring(front + V((0, -0.006, -0.075 * t)), V((1, 0, 0)), V((0, 1, 0)), r, r * 0.7, 10))
    uk.loft(bm, rings, cap0=True, cap1=True)
    parts.append(uk.shade(uk.mesh("browgem", bm, [M["gem"]]), 50))
    parts.append(dome(front + V((0, -0.004, 0.004)), (0.02, 0.012, 0.014), 10, 6, [M["gold"]], "gemsetting"))
    return parts


# The crest's spines along the top of the head: the angle up from level
# toward the front where each rises, its length in cells, and how far it
# rakes back, in degrees.
SPINES = ((32, 0.18, 40), (46, 0.36, 38), (60, 0.49, 36), (74, 0.55, 34), (88, 0.52, 33), (102, 0.42, 32), (116, 0.29, 31), (130, 0.16, 30))


def crest(face, M):
    """A gilded fin crest along the top of the head, as the original wears
    a gold crest: spines rising from a ridge on the hair, raked back, with
    a fine membrane between them scalloped down between their tips."""
    base, tips = [], []
    for ang, L, rake in SPINES:
        th = math.radians(ang)
        p, d = face.dome(th, 0.0, 0.02) if ang <= 90 else face.dome(math.pi - th, math.pi, 0.02)
        if p is None:
            continue
        up = V((0.0, d.y, d.z)).normalized()
        back = V((0.0, 1.0, 0.0))
        r = math.radians(rake)
        way = (up * math.cos(r) + back * math.sin(r)).normalized()
        side = 0.07 * L * (1 if len(base) % 2 else -1)
        base.append(p)
        tips.append(p + way * L + V((side, 0.0, 0.0)))
    parts = []
    for i, (b, t) in enumerate(zip(base, tips)):
        mid = b.lerp(t, 0.5) + V((0, 0.012, 0))
        bm = bmesh.new()
        uk.loft(bm, tube_rings([b - (t - b).normalized() * 0.02, b, mid, t], [(0.016, 0.012), (0.014, 0.011), (0.009, 0.008), (0.002, 0.002)], 6), cap0=True, cap1=True)
        parts.append(uk.shade(uk.mesh("spine", bm, [M["gold"]]), 40))
    # the membrane: rows from the base ridge up to the edge, the edge sagging between tips
    rows = 3
    cols = []
    for i in range(len(base) - 1):
        for s in (0.0, 0.25, 0.5, 0.75):
            b = base[i].lerp(base[i + 1], s)
            e = tips[i].lerp(tips[i + 1], s)
            sag = 0.14 * math.sin(math.pi * s) * (e - b).length
            e = e + (b - e).normalized() * sag
            cols.append([b.lerp(e, r / rows) for r in range(rows + 1)])
    cols.append([base[-1].lerp(tips[-1], r / rows) for r in range(rows + 1)])
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in col] for col in cols]
    for c in range(len(cols) - 1):
        for r in range(rows):
            bm.faces.new((vs[c][r], vs[c + 1][r], vs[c + 1][r + 1], vs[c][r + 1]))
    web = uk.mesh("crestweb", bm, [M["finweb"]], outward=False)
    m = web.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.007, 0.0
    uk.apply(web)
    parts.append(uk.shade(web, 50))
    # the gilded comb the crest stands on, along the top of the head
    taper = [min(1.0, 0.25 + 0.3 * k) * (1.0 - 0.4 * max(0, k - len(base) + 3) / 3) for k in range(len(base))]
    rows = []
    for w in (-0.075, -0.04, 0.0, 0.04, 0.075):
        rows.append([p + V((w * f, 0.0, -0.014 * (w / 0.075) ** 2 - 0.006)) for p, f in zip(base, taper)])
    parts.append(shell("crestcomb", rows, [M["gold"]], 0.012, 1.0))
    for w in (-0.078, 0.078):
        parts.append(rim([p + V((w * f, 0.0, -0.016)) for p, f in zip(base, taper)], 0.008, M["gold"], "combedge", sides=5))
    parts.append(rim(base, 0.016, M["gold"], "crestridge", sides=6))
    return parts


# ---------------------------------------------------------------- the ears' fins

def ear_fins(face, M):
    """A fin along the back of each ear: rays fanning back from the ear's
    edge with fine skin between them, scalloped between the rays' tips."""
    parts = []
    for sx in (-1.0, 1.0):
        ear = [v for v in face.verts if v.x * sx > 0.16 and EYE_Z - 0.09 < v.z < EYE_Z + 0.12 and v.y > NECK[1] + 0.02]
        if not ear:
            continue
        tip = max(ear, key=lambda v: v.y + 0.6 * v.z)
        low = [v for v in ear if v.z < EYE_Z - 0.04]
        lobe = max(low, key=lambda v: v.y) if low else tip - V((0, 0.08, 0.14))
        rays = []
        for k in range(5):
            t = k / 4
            b = lobe.lerp(tip, t ** 0.85) + V((-sx * 0.012, -0.012, 0.0))
            ang = math.radians(lerp(-55, 30, t))
            L = lerp(0.07, 0.12, math.sin(math.pi * min(1.0, t * 1.2)) ** 0.7)
            d = V((sx * 0.35, math.cos(ang), math.sin(ang))).normalized()
            rays.append((b, b + d * L))
        bm = bmesh.new()
        cols = []
        for k in range(len(rays) - 1):
            for s in (0.0, 0.5):
                b = rays[k][0].lerp(rays[k + 1][0], s)
                e = rays[k][1].lerp(rays[k + 1][1], s)
                e = e + (b - e) * (0.3 * math.sin(math.pi * s))
                cols.append([b.lerp(e, r / 3) for r in range(4)])
        cols.append([rays[-1][0].lerp(rays[-1][1], r / 3) for r in range(4)])
        vs = [[bm.verts.new(p) for p in col] for col in cols]
        for c in range(len(cols) - 1):
            for r in range(3):
                bm.faces.new((vs[c][r], vs[c + 1][r], vs[c + 1][r + 1], vs[c][r + 1]))
        web = uk.mesh("earfin", bm, [M["finskin"]], outward=False)
        m = web.modifiers.new("solid", "SOLIDIFY")
        m.thickness, m.offset = 0.006, 0.0
        uk.apply(web)
        parts.append(uk.shade(web, 50))
        for b, e in rays:
            bm = bmesh.new()
            uk.loft(bm, tube_rings([b, b.lerp(e, 0.5), e], [(0.007, 0.006), (0.005, 0.004), (0.0015, 0.0015)], 5), cap0=True, cap1=True)
            parts.append(uk.shade(uk.mesh("finray", bm, [M["finskin"]]), 50))
    return parts


# ---------------------------------------------------------------- the braid

BRAID = ("Head", "Pony1", "Pony2", "Pony3", "Pony4")


def braid_path(rig, at, way):
    """The braid's centre line, from the clasp back over the collar and
    down through the original's ponytail pivots to its tip."""
    piv = [rig.pivot(p) + V((0.0, 0.04, 0.0)) for p in BRAID[1:]]
    end = piv[-1] + (piv[-1] - piv[-2]).normalized() * 0.32
    ctrl = [at, at + way * 0.18, piv[0] + V((0, 0.0, 0.1)), piv[0]] + piv[1:] + [end]
    pts = []
    for a, b in zip(ctrl, ctrl[1:]):
        n = max(2, int((b - a).length / 0.035))
        for k in range(n):
            pts.append(a.lerp(b, k / n))
    pts.append(ctrl[-1])
    # smooth the corners
    for _ in range(6):
        pts = [pts[0]] + [(pts[i - 1] + pts[i] * 2 + pts[i + 1]) / 4 for i in range(1, len(pts) - 1)] + [pts[-1]]
    return pts


def braid(rig, M, at, way):
    """A three-strand plait from the clasp down the back, cut where the
    original's ponytail pieces turn, each piece's run tucked a little into
    the one above so no gap opens as they swing; bound in gold near the
    end, and a short tuft below. Returns {piece: [parts]}."""
    path = braid_path(rig, at + way * 0.06, way)
    cuts = [rig.pivot(p).z for p in BRAID[1:]]
    total = sum((b - a).length for a, b in zip(path, path[1:]))
    out = {p: [] for p in BRAID}

    def owner(z):
        for k, c in enumerate(cuts):
            if z > c:
                return BRAID[k]
        return BRAID[-1]

    segs = {p: [] for p in BRAID}
    s = 0.0
    for i, p in enumerate(path):
        if i:
            s += (p - path[i - 1]).length
        segs[owner(p.z)].append((s, p, i))
    for piece, pts in segs.items():
        if not pts:
            continue
        i0, i1 = pts[0][2], pts[-1][2]
        i0 = max(0, i0 - 2) if piece != "Head" else i0
        i1 = min(len(path) - 1, i1 + 1)
        sub = path[i0:i1 + 1]
        dist = [0.0]
        for a, b in zip(sub, sub[1:]):
            dist.append(dist[-1] + (b - a).length)
        s0 = sum((b - a).length for a, b in zip(path[:i0 + 1], path[1:i0 + 1]))
        rings = []
        ax_prev = None
        for k, p in enumerate(sub):
            a = sub[max(0, k - 1)]
            b = sub[min(len(sub) - 1, k + 1)]
            along = (b - a).normalized()
            ax = uk.frame(along)[0] if ax_prev is None else (ax_prev - along * ax_prev.dot(along)).normalized()
            ax_prev = ax
            ay = along.cross(ax)
            u = (s0 + dist[k]) / total
            R = lerp(0.075, 0.042, u ** 1.2) * (0.6 + 0.4 * min(1.0, (s0 + dist[k]) / 0.08))
            ring = []
            for j in range(13):
                t = 2 * math.pi * j / 13
                ph = (s0 + dist[k]) / 0.11 * 2 * math.pi
                lobe = 0.2 * math.cos(3 * t - ph) + 0.06 * math.cos(6 * t - 2 * ph)
                r = R * (1.0 + lobe)
                ring.append(p + ax * math.cos(t) * r + ay * math.sin(t) * r * 0.82)
            rings.append(ring)
        bm = bmesh.new()
        uk.loft(bm, rings, cap0=piece == "Head", cap1=piece == BRAID[-1])
        out[piece].append(uk.shade(uk.mesh("braid_" + piece, bm, [M["braid"]]), 60))
    for piece, obs in out.items():
        m = ka.idle_turn(rig, piece).inverted()
        for ob in obs:
            ob.data.transform(m)
    # gold bands where the plait ends, and the tuft below
    tail = path[-1]
    up = (path[-6] - path[-1]).normalized()
    for k, off in enumerate((0.28, 0.33)):
        c = tail + up * off
        bm = bmesh.new()
        uk.loft(bm, tube_rings([c - up * 0.015, c + up * 0.015], [(0.058, 0.052), (0.058, 0.052)], 12), cap0=True, cap1=True)
        out["Pony4"].append(uk.shade(uk.mesh("braidband", bm, [M["gold"]]), 40))
    bm = bmesh.new()
    rings = []
    for t, r in ((0.0, 0.05), (0.4, 0.065), (0.8, 0.05), (1.0, 0.012)):
        rings.append(uk.ring(tail + up * 0.22 - up * 0.36 * t, V((1, 0, 0)), up.cross(V((1, 0, 0))), r, r * 0.8, 10))
    uk.loft(bm, rings, cap0=True)
    out["Pony4"].append(uk.shade(uk.mesh("tuft", bm, [M["braid"]]), 60))
    m = ka.idle_turn(rig, "Pony4").inverted()
    for ob in out["Pony4"][-3:]:
        ob.data.transform(m)
    return out
