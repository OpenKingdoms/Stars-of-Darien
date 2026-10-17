"""Lokken's head for lokken.py: a realistic man's face from Blender Studio's
CC0 Human Base Meshes (unitkit.human_head) drawn tight over the bundle's
own CC0 skull, so the bone shows through as it would on a lich: hollow
temples and cheeks under sharp cheekbones, deep sockets with embers for
eyes, a hard brow and jaw, thin lips, and the bone bared on one cheek.
Dark hair swept back from two points over the temples round a high bald
crown, thin grey locks behind, and a white moustache and pointed beard,
wispy at their ends.

The head is built level where the torso leans by lokken.HEAD_FRAME, and
lokken.py turns it back to the rest pose. The hair and beard are solid volumes
under strand cards cut out of a strand picture drawn here, placed by
casting rays onto the face, so they follow its real shape.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import elsin_head as eh
import unitkit as uk

V = Vector
EYE_Z = 3.25          # the eyes' height in the idle pose, cells
NECK = (0.0, -0.47)   # the neck's axis under the head, where it is built
SCALE = 2.3           # cells to a metre
SKULL = "Skull - Realistic"
ORBIT_Z = 0.1105      # the skull's orbits above its lowest point, metres
SKULL_FIT = (1.06, 1.03, 1.06)   # the skull's scale into the head
SKULL_BACK = 0.135               # and how far back it sits, metres
EYE_BACK = 0.006      # metres the eyes sit deeper than the bundle's
WOUND = (0.057, 0.004, -0.036)   # where the skin has worn through to the bone on his left cheekbone, metres from between the eyes
WOUND_R = 0.011


def lerp(a, b, t):
    return a + (b - a) * t


def _g(a, s):
    return math.exp(-(a / s) ** 2)


def smooth01(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3.0 - 2.0 * t)


# ---------------------------------------------------------------- the face

def _pre(p, marks):
    """Before the skin is drawn onto the bone: a longer, narrower face, a
    thin nose with a high bridge and a slight hook, and a thin neck. p is in
    metres, centred between the eyes, -y forward."""
    _pre.marks = marks
    x, y, z = p
    q = Vector(p)
    front = 1.0 if y < -0.01 else max(0.0, 1.0 - (y + 0.01) / 0.04)
    ax = abs(x)
    if z < 0.0:
        q.z = z * 1.05
    q.x *= 1.0 - 0.06 * _g(z + 0.03, 0.05) * front                            # a narrow face
    q.y -= (0.004 * _g(z, 0.011) + 0.003 * _g(z + 0.018, 0.009)
            + 0.0015 * _g(z + 0.034, 0.008)) * _g(ax, 0.0105) * front         # a high bridge, slightly hooked
    q.z -= 0.0025 * _g(z + 0.042, 0.009) * _g(ax, 0.02) * front               # the tip drawn down
    q.x *= 1.0 - 0.2 * _g(z + 0.01, 0.016) * _g(ax, 0.016) * front            # a thin bridge
    q.x *= 1.0 - 0.14 * _g(z + 0.04, 0.01) * _g(ax - 0.015, 0.012) * front    # pinched nostrils
    if z < -0.11:
        n = marks["neck"]
        k = min(1.0, (-0.11 - z) / 0.05)
        q.x = n.x + (q.x - n.x) * (1.0 - 0.16 * k)                           # a thin old neck
        q.y = n.y + (q.y - n.y) * (1.0 - 0.1 * k)
    return q


# How tightly the skin lies on the bone round the face: (centre in metres
# from between the eyes, mirrored in x, radius, pull 0..1, skin left in metres).
PULLS = (
    ((0.064, 0.025, 0.03), 0.034, 1.0, 0.0005),     # the temples, hollow
    ((0.045, -0.012, -0.058), 0.028, 1.0, 0.0006),  # the cheeks, sunken under the bone
    ((0.05, -0.004, -0.028), 0.016, 0.85, 0.0018),  # the cheekbones, sharp
    ((0.048, 0.02, -0.095), 0.03, 0.8, 0.002),      # the jaw
    ((0.03, -0.022, 0.03), 0.026, 0.75, 0.002),     # the brow
    ((0.0, -0.02, 0.075), 0.04, 0.6, 0.0028),       # the forehead
    ((0.034, -0.012, -0.004), 0.018, 0.45, 0.0025), # round the eyes, sunken
    ((0.0, -0.03, -0.115), 0.025, 0.5, 0.004),      # the chin
)


def _weights(p):
    """The pull and the skin left at a point, the strongest pull winning,
    with the nose and the lips kept."""
    best_k, best_t = 0.0, 0.004
    x, y, z = p
    for (cx, cy, cz), r, k, t in PULLS:
        d = math.sqrt((abs(x) - cx) ** 2 + (y - cy) ** 2 * 0.5 + (z - cz) ** 2)
        w = k * _g(d, r)
        if w > best_k:
            best_k, best_t = w, t
    fore = 1.0 if y < -0.012 else 0.0
    keep = max(_g(abs(x), 0.013) * _g(z + 0.028, 0.026) * fore,           # the nose
               0.6 * _g(abs(x), 0.026) * _g(z + 0.068, 0.012) * fore)     # the lips
    best_k *= 1.0 - smooth01((y - 0.05) / 0.04)
    wx, wy, wz = WOUND
    w = _g(math.sqrt((x - wx) ** 2 + (y - wy) ** 2 + (z - wz) ** 2), WOUND_R)
    if w > 0.3:
        return max(best_k, w), lerp(best_t, 0.0003, w)
    return best_k * (1.0 - keep), best_t


def skull(assets):
    """The bundle's CC0 skull, fitted inside the head in the head's frame:
    metres, centred between the eyes, -y forward. Returns a BVH of it."""
    with bpy.data.libraries.load(uk.human_bundle(assets), link=False) as (src, dst):
        dst.objects = [SKULL]
    ob = dst.objects[0]
    bpy.context.scene.collection.objects.link(ob)
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    bpy.context.view_layer.update()
    ob.data.transform(ob.matrix_world)
    vs = [v.co for v in ob.data.vertices]
    lo = V([min(v[i] for v in vs) for i in range(3)])
    hi = V([max(v[i] for v in vs) for i in range(3)])
    o = V(((lo.x + hi.x) / 2, 0.0, lo.z + ORBIT_Z))
    sx, sy, sz = SKULL_FIT
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for v in bm.verts:
        p = v.co - o
        v.co = V((p.x * sx, p.y * sy + SKULL_BACK, p.z * sz))
    bm.normal_update()
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    bpy.data.objects.remove(ob)
    return bvh


def gaunt(me, centre, bone, scale, eyes, iterations=6):
    """Draws the skin of mesh me (model space, its eyes' midpoint at centre,
    scale cells to a metre) in toward the bone: each point's depth of flesh
    over the skull, smoothed over the face, is taken in by the region's
    pull down to the skin left, along the skin's own normal, so the hollows
    of the skull show and the face keeps its smooth skin."""
    n = len(me.vertices)
    me.update()
    co = np.array([v.co for v in me.vertices], np.float64)
    nrm = np.array([v.normal for v in me.vertices], np.float64)
    depth = np.zeros(n)
    k = np.zeros(n)
    tissue = np.zeros(n)
    cap = np.zeros(n)
    near_eye = [(c - centre) / scale for c, r in eyes]
    for i, v in enumerate(me.vertices):
        p = (v.co - centre) / scale
        k[i], tissue[i] = _weights(p)
        if k[i] < 0.01:
            continue
        loc, bn, _i, _d = bone.find_nearest(p)
        if loc is not None:
            depth[i] = max(0.0, (p - loc).dot(bn))
        e = min((p - q).length for q in near_eye)
        cap[i] = lerp(0.002, 0.024, smooth01((e - 0.014) / 0.024))
    edges = np.array([e.vertices for e in me.edges], np.int64)
    deg = np.bincount(edges.ravel(), minlength=n).astype(np.float64)
    deg[deg == 0] = 1.0

    def smooth(f, times):
        for _ in range(times):
            s_ = np.zeros_like(f)
            np.add.at(s_, edges[:, 0], f[edges[:, 1]])
            np.add.at(s_, edges[:, 1], f[edges[:, 0]])
            f = 0.5 * f + 0.5 * s_ / deg
        return f

    depth = smooth(depth, iterations)
    push = np.minimum(k * np.maximum(0.0, depth - tissue), cap)
    push = smooth(push, 2)
    for i, v in enumerate(me.vertices):
        p = (v.co - centre) / scale
        x, y, z = abs(p.x), p.y, p.z
        if y > 0.04:
            continue
        push[i] -= 0.0038 * _g(z - 0.024, 0.009) * _g(x - 0.03, 0.022)       # the brow ridge
        push[i] -= 0.0036 * _g(z + 0.026, 0.01) * _g(x - 0.052, 0.013)      # the cheekbones
        push[i] -= 0.0018 * _g(z + 0.1, 0.012) * _g(x - 0.05, 0.012) * _g(y - 0.03, 0.02)  # the jaw's angle
        push[i] += 0.0015 * _g(z - 0.012, 0.007) * _g(x - 0.036, 0.016) * (1.0 if y < -0.005 else 0.0)  # under the brow, the socket
    co -= nrm * (push * scale)[:, None]
    # how far each point sank or stood out, for the bake to shade the hollows and the bone
    shade = me.color_attributes.new("gaunt", "FLOAT_COLOR", "POINT")
    where = np.zeros(n)
    for i, v in enumerate(me.vertices):
        p = (V(co[i]) - centre) / scale
        x, y, z = abs(p.x), p.y, p.z
        where[i] = max(_g(math.sqrt((x - 0.064) ** 2 + (y - 0.025) ** 2 + (z - 0.03) ** 2), 0.03),
                       _g(math.sqrt((x - 0.045) ** 2 + (y + 0.012) ** 2 + (z + 0.06) ** 2), 0.028),
                       0.7 * _g(math.sqrt((x - 0.034) ** 2 + (z + 0.004) ** 2), 0.022) * (1.0 if y < 0.01 else 0.0))
    hollow = np.clip(push / 0.009, 0.0, 1.0) * where
    ridge = np.clip(-push / 0.0025, 0.0, 1.0)
    shade.data.foreach_set("color", np.stack([hollow, ridge, np.zeros(n), np.ones(n)], 1).astype(np.float32).ravel())
    # heavy upper lids, the eyes narrowed
    for i, v in enumerate(me.vertices):
        p = (v.co - centre) / scale
        for q in near_eye:
            d = p - q
            if d.z > 0.0 and d.y < 0.004 and abs(d.x) < 0.02:
                co[i][2] -= 0.0033 * scale * _g(d.x, 0.012) * _g(d.z - 0.006, 0.006)
    me.vertices.foreach_set("co", co.astype(np.float32).ravel())
    me.update()


class Face:
    """The fitted head and ways to find points on it."""

    def __init__(self, head, eyes):
        self.ob = head
        self.eyes = eyes
        bm = bmesh.new()
        bm.from_mesh(head.data)
        self.bvh = BVHTree.FromBMesh(bm)
        bm.free()
        vs = [v.co for v in head.data.vertices]
        self.top = max(v.z for v in vs)
        self.chin = min((v for v in vs if v.y < NECK[1] - 0.12), key=lambda v: v.z).z
        self.nose = min(v.y for v in vs)
        self.centre = V((NECK[0], (eyes[0][0].y + eyes[1][0].y) / 2 + 0.16, EYE_Z + 0.16))
        self.em = (eyes[0][0] + eyes[1][0]) / 2

    def round(self, phi, z, by):
        """The face seen from a vertical axis 5 cm behind the eyes, at z metres
        from the eyes, phi round from straight ahead, by cells out."""
        o = V((self.em.x, self.em.y + 0.05 * SCALE, self.em.z + z * SCALE))
        d = V((math.sin(phi), -math.cos(phi), 0.0))
        hit = self.bvh.ray_cast(o + d * 0.6, -d, 0.7)
        return hit[0] + d * by if hit[0] is not None else None

    def front(self, x, z, by):
        """The face seen from straight ahead at x and z metres from the eyes."""
        o = V((self.em.x + x * SCALE, self.em.y - 0.5, self.em.z + z * SCALE))
        hit = self.bvh.ray_cast(o, V((0, 1, 0)), 1.0)
        return hit[0] + V((0, -by, 0)) if hit[0] is not None else None

    def at(self, phi, z, inner=False):
        """The surface seen from the head's axis at height z, phi round from
        straight ahead (-Y), toward +X positive. None where nothing is hit."""
        o = V((NECK[0], NECK[1], z))
        d = V((math.sin(phi), -math.cos(phi), 0.0))
        hit = self.bvh.ray_cast(o, d, 0.7) if inner else self.bvh.ray_cast(o + d * 0.6, -d, 0.7)
        return (hit[0], hit[1]) if hit[0] is not None else (None, None)

    def out(self, phi, z, by, inner=False):
        p, n = self.at(phi, z, inner)
        if p is None:
            return None
        return p + V((math.sin(phi), -math.cos(phi), 0.0)) * by

    def dome(self, phi, theta, by):
        """The scalp seen from inside the skull, theta down from straight up
        and phi round from the front, by cells out along the ray."""
        d = V((math.sin(theta) * math.sin(phi), -math.sin(theta) * math.cos(phi), math.cos(theta)))
        hit = self.bvh.ray_cast(self.centre, d, 0.8)
        if hit[0] is None:
            return None
        return hit[0] + d * by


def head(rig, M, assets):
    """The head's parts, built level (lokken.HEAD_FRAME): the face, the eyes,
    the hair and beard volumes, and the strand cards. Returns (atlas parts,
    card parts, face)."""
    ob, eyes = uk.human_head(assets, EYE_Z, NECK, SCALE, cut_z=EYE_Z - 0.34, shape=_pre, multires=1)
    centre = (eyes[0][0] + eyes[1][0]) / 2
    gaunt(ob.data, centre, skull(assets), SCALE, eyes)
    ob.data.materials.clear()
    ob.data.materials.append(M["skin"])
    dec = ob.modifiers.new("lighter", "DECIMATE")
    dec.ratio = 0.17
    uk.apply(ob)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.dissolve_degenerate(bm, dist=0.002, edges=bm.edges[:])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(ob.data)
    bm.free()
    for f in ob.data.polygons:
        f.use_smooth = True
    eyes = [(c + V((0.0, EYE_BACK * SCALE, 0.0)), r) for c, r in eyes]
    face = Face(ob, eyes)
    parts = [ob]
    for c, r in eyes:
        parts.append(eh.ellipsoid_ob(c, (r * 0.86, r * 0.86, r * 0.86), 12, 8, M["eye"], "eye"))
        parts.append(eh.ellipsoid_ob(c + V((0.0, -r * 0.8, -r * 0.12)), (r * 0.33, r * 0.12, r * 0.33), 10, 6, M["ember"], "ember"))
    parts.append(side_hair(face, M))
    parts += beard_volume(face, M)
    parts += moustache(face, M)
    cards = band_cards(face, M) + back_cards(face, M) + beard_cards(face, M) + brow_cards(face, M)
    return parts, cards, face


# ---------------------------------------------------------------- pictures

def strand_picture(size=512, seed=19, dark=False):
    """Hair strands for the cards, from numbers: white and ash grey with a
    few darker, or near black with a little grey when dark, thin and
    straight, each tapering to its tip; RGB and the cut-out alpha. Row 0 is
    the tip, the root at the top."""
    rng = np.random.default_rng(seed)
    rgba = np.zeros((size, size, 4), np.float32)
    for _ in range(900):
        x0 = rng.uniform(0, size)
        root = rng.uniform(size * 0.85, size)
        tip = rng.uniform(0, size * 0.5)
        w = rng.uniform(0.45, 1.1)
        sway = rng.uniform(-4, 4)
        shade = rng.uniform(0.55, 1.0)
        col = np.array([0.74, 0.73, 0.7]) * shade
        if rng.uniform() < (0.85 if dark else 0.15):
            col = np.array([0.045, 0.042, 0.04]) * rng.uniform(0.7, 1.6)
        for y in range(int(tip), int(root)):
            t = max(0.0, (y - tip) / max(1.0, root - tip))
            x = x0 + sway * math.sin(t * math.pi)
            half = w * (0.3 + 0.7 * t ** 0.5)
            for xi in range(int(x - half - 1), int(x + half + 2)):
                xm = xi % size
                a = max(0.0, 1.0 - abs(xi - x) / (half + 0.5)) * min(1.0, t * 3.0)
                if a > rgba[y, xm, 3]:
                    rgba[y, xm, :3] = col * (0.85 + 0.3 * a)
                    rgba[y, xm, 3] = a
    # the clear texels take the strands' own colour, so they don't darken the strands as the game blends the picture down at a distance
    seen = rgba[..., 3] > 0.3
    rgba[~seen, :3] = rgba[seen, :3].mean(0)
    return uk.image("lokken_strands_dark" if dark else "lokken_strands", rgba)


# ---------------------------------------------------------------- hair

def _wrap(phi):
    return abs(math.atan2(math.sin(phi), math.cos(phi)))


def side_band(phi):
    """Where the dark hair lies, as angles down from straight up (top,
    bottom) round the head: two points high over the temples, sweeping
    back round the sides above the ears and down the nape, the crown and
    the brow bare. None in front of the points."""
    a = _wrap(phi)
    if a < 0.5:
        return None
    f = smooth01((a - 0.5) / 0.45)
    top = lerp(1.0, 0.78, f) - 0.3 * smooth01((a - 1.7) / 1.2)
    bottom = lerp(1.0, 1.75, f) + 0.7 * smooth01((a - 1.3) / 1.6)
    return top, bottom


def side_hair(face, M):
    """Dark hair close over the skull in the band, thinning to nothing at
    its edges, so the scalp's line shows."""
    bm = bmesh.new()
    n, rows_n = 33, 8
    rows = []
    for j in range(rows_n):
        t = j / (rows_n - 1)
        row = []
        for i in range(n):
            phi = lerp(0.5, 2 * math.pi - 0.5, i / (n - 1))
            top, bottom = side_band(phi)
            edge = min(1.0, min(i, n - 1 - i) / 4.0)
            p = face.dome(phi, lerp(top, bottom, t), 0.002 + 0.012 * edge * math.sin(math.pi * t))
            if p is None:
                p = row[-1].copy() if row else face.centre.copy()
            row.append(p)
        rows.append(row)
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    for r in range(rows_n - 1):
        for c in range(n - 1):
            bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][c + 1], vs[r][c + 1]))
    shell = uk.mesh("sidehair", bm, [M["hair"]], outward=False)
    m = shell.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.006, -1.0
    uk.apply(shell)
    return uk.shade(shell, 70)


def band_cards(face, M, count=70, seed=11):
    """Dark strands lying sleek on the band, swept back from its points
    over the temples."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        side = 1 if i % 2 else -1
        a0 = rng.uniform(0.5, 1.9)
        v = rng.uniform(0.05, 0.85)
        path = []
        for j in range(7):
            a = a0 + 0.12 * j * (1.0 + 0.4 * v)
            top, bottom = side_band(a)
            k = v * min(1.0, max(0.0, (a0 - 0.5) / 0.3))
            th = lerp(top, bottom, k) + 0.02 * j
            p = face.dome(side * a, th, 0.012 + 0.003 * (i % 3))
            if p is not None:
                path.append(p)
        if len(path) >= 3:
            card_strip(bm, uv, path, rng.uniform(0.03, 0.045), path[len(path) // 2] - face.centre)
    ob = uk.mesh("bandcards", bm, [M["strands_dark"]], outward=False)
    return [uk.shade(ob, 80)]


def back_cards(face, M, count=48, seed=29):
    """Thin grey locks from over the ears and the back of the head, lank
    and straight, down to the collar."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        phi = lerp(1.5, 2 * math.pi - 1.5, rng.uniform())
        top, bottom = side_band(phi)
        theta0 = lerp(top, bottom, rng.uniform(0.45, 0.9))
        p0 = face.dome(phi, theta0, 0.02)
        if p0 is None:
            continue
        length = rng.uniform(0.28, 0.46)
        out = V((math.sin(phi), -math.cos(phi), 0.0))
        path = [p0]
        for k in range(1, 7):
            t = k / 6
            z = p0.z - length * t
            q = face.out(phi, max(z, face.chin + 0.06), 0.025 + 0.02 * (i % 2) + 0.05 * t * t, inner=True)
            if q is None:
                q = path[-1] + V((0, 0, -length / 6)) + out * 0.01
            path.append(V((q.x, q.y, z)))
        card_strip(bm, uv, path, rng.uniform(0.04, 0.07), out)
    ob = uk.mesh("backcards", bm, [M["strands"]], outward=False)
    return [uk.shade(ob, 80)]


def card_strip(bm, uv, path, width, hint):
    """A strand card facing out along hint: the game lights a card's back
    face as if turned away, so each card's front must face the light."""
    h = hint.normalized() if hint.length > 1e-6 else V((0, 0, 1))
    eh.card(bm, uv, path, width, -h)


# ---------------------------------------------------------------- the beard

def _beard_edges(u):
    """At u across the face (-1 at his right ear to 1 at his left), in
    metres from between the eyes: the beard's top edge, the jaw's lower
    edge, and the beard's foot, a point under the chin."""
    a = abs(u)
    top = -0.084 + 0.006 * _g(a - 0.3, 0.12) - 0.012 * _g(a - 0.6, 0.15) + 0.045 * smooth01((a - 0.72) / 0.28) ** 1.5
    jaw = lerp(-0.128, -0.085, smooth01((a - 0.2) / 0.8))
    foot = jaw - 0.1 * max(0.0, 1.0 - a / 0.75) ** 1.6
    return top, jaw, foot


def beard_at(face, u, s, by):
    """A point on the beard at u across and s down (0 its top edge, 1 its
    foot), by cells out from the face."""
    top, jaw, foot = _beard_edges(u)
    z = lerp(top, foot, s)
    phi = u * math.radians(100)
    if z >= jaw:
        p = face.round(phi, z, by)
        if p is not None:
            return p
    j = face.round(phi, jaw, by) or face.round(phi * 0.9, jaw + 0.01, by)
    below = (jaw - z) / max(1e-6, jaw - foot) if jaw > foot else 0.0
    em = face.em
    x = em.x + (j.x - em.x) * (1.0 - 0.85 * below)
    y = j.y - 0.012 * SCALE * below
    return V((x, y, em.z + z * SCALE))


def beard_volume(face, M):
    """A white beard from under the lip along the jaw to the ears, the
    hollow cheeks above it bare, gathering below the chin to a point."""
    bm = bmesh.new()
    nrow, ncol = 12, 21
    rows = []
    for r in range(nrow):
        s_ = r / (nrow - 1)
        row = []
        for c in range(ncol):
            u = lerp(-1.0, 1.0, c / (ncol - 1))
            edge = min(1.0, (1.0 - abs(u)) / 0.15)
            thick = lerp(-0.006, 0.03, smooth01(s_ * 1.8)) * edge * (1.0 - 0.7 * abs(u))
            row.append(beard_at(face, u, s_, thick))
        rows.append(row)
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    for r in range(nrow - 1):
        for c in range(ncol - 1):
            bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][c + 1], vs[r][c + 1]))
    beard = uk.mesh("beardvolume", bm, [M["beard"]], outward=False)
    m = beard.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.02, -1.0
    uk.apply(beard)
    return [uk.shade(beard, 60)]


def beard_cards(face, M, count=150, seed=5):
    """Strand cards over the beard, running down and gathering to its point,
    longer and looser at the foot, for a soft and wispy edge."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        u = rng.uniform(-0.95, 0.95)
        s0 = rng.uniform(0.06, 0.75)
        length = rng.uniform(0.25, 0.45)
        path = []
        for k in range(5):
            t = k / 4
            s_ = s0 + length * t
            p = beard_at(face, u * (1.0 - 0.25 * t), min(s_, 1.08), (0.012 + 0.01 * (i % 3)) * min(1.0, s_ * 4.0) + 0.012 + 0.015 * t)
            path.append(p)
        hint = V((math.sin(u * 1.7), -math.cos(u * 1.7), 0.0))
        card_strip(bm, uv, path, rng.uniform(0.035, 0.055), hint)
    ob = uk.mesh("beardcards", bm, [M["strands"]], outward=False)
    return [uk.shade(ob, 80)]


def moustache(face, M):
    """The moustache as two solid locks from under the nose, drooping over
    the lip past the corners of the mouth into the beard; the game shades
    thin cards lying this close to the face too dark, so these are solid,
    their strands in the bake."""
    out = []
    for side in (-1, 1):
        rows = []
        for i in range(9):
            t = i / 8
            x = -0.002 + 0.04 * t ** 0.85
            zc = -0.058 - 0.048 * t ** 1.7
            w = 0.013 * (1.0 - 0.55 * t) + 0.003
            row = []
            for j in range(5):
                v = j / 4 * 2.0 - 1.0
                by = (0.002 + 0.012 * (1.0 - v * v)) * (1.0 - 0.5 * t)
                p = face.front(side * x, zc + v * w * 0.5, by)
                row.append(p if p is not None else (row[-1] if row else face.em.copy()))
            rows.append(row)
        bm = bmesh.new()
        vs = [[bm.verts.new(p) for p in row] for row in rows]
        for r in range(len(rows) - 1):
            for c in range(4):
                bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][c + 1], vs[r][c + 1]))
        ob = uk.mesh("moustache", bm, [M["beard"]], outward=False)
        m = ob.modifiers.new("solid", "SOLIDIFY")
        m.thickness, m.offset = 0.006, 0.0
        uk.apply(ob)
        out.append(uk.shade(ob, 60))
    return out


def brow_cards(face, M):
    """Heavy grey brows on the hard brow ridge, drawn down hard at the
    middle in a scowl."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for side in (-1, 1):
        for layer in range(2):
            path = []
            for k in range(6):
                t = k / 5
                phi = side * lerp(0.08, 0.5, t)
                z = EYE_Z + 0.012 + 0.04 * math.sin(0.5 * math.pi * min(1.0, t * 1.25)) - 0.008 * max(0.0, t - 0.8) / 0.2 + 0.006 * layer
                p = face.out(phi, z, 0.005 + 0.002 * layer)
                if p is not None:
                    path.append(p)
            if len(path) >= 3:
                card_strip(bm, uv, path, 0.022 - 0.004 * layer, V((0, -1, 0.3)))
    ob = uk.mesh("browcards", bm, [M["strands"]], outward=False)
    return [uk.shade(ob, 80)]
