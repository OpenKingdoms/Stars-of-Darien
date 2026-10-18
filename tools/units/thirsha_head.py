"""Thirsha's head for thirsha.py: a realistic woman's face from the female
body in Blender Studio's CC0 Human Base Meshes (unitkit.human_head),
reshaped into a striking, fierce huntress: a fine jaw and chin, high
cheekbones, full lips, large almond eyes lifted at their outer corners
under lids a little lowered, a slim straight nose and pointed ears. Long
lashes are cards on the upper lids. The skin is pale and the paint
(thirsha.looks) gives her dark brows, smoky eyes and a wine lip.

Her hair is sleek and black, parted in the middle and falling over the
tips of the ears past the shoulders, with four locks either side framing
the face and hanging over the collar. Smooth volumes stand the crown off
the skull and fill the fall behind, under strand cards in layers, each
card wearing a lock of tapered wisps drawn here, ending at its own
length. The locks the original's script swings (hair1 to hair8) are
clumps of cards on those pieces, so they stream as she flies. Everything
is placed by casting rays onto the face, so it follows the face's real
shape.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import elsin_head as eh
import unitkit as uk

V = Vector
EYE_Z = 3.5           # the eyes' height, cells
NECK = (0.0, 0.03)    # the neck's axis, under the head's pivot
SCALE = 2.2           # cells to a metre: a head of about 0.47 cells
SOURCE = "GEO-body_female_realistic"
EYE_R = 0.0124        # the bundle's eyeball radius, metres
EYE_GROW = 1.07       # the lids and eyeballs scaled about each eye's centre
FACE_TRIS = 9500      # the face after decimation, the back of the skull thinned most


def lerp(a, b, t):
    return a + (b - a) * t


def _g(a, s):
    return math.exp(-(a / s) ** 2)


def _step(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def _rot(a, b, ang):
    c, s = math.cos(ang), math.sin(ang)
    return a * c - b * s, a * s + b * c


def huntress(p, marks):
    """The bundle's woman as Thirsha. p is in metres, centred between the
    eyes, -y forward."""
    q = Vector(p)
    for e in marks["eyes"]:
        d = q - e
        rho = math.hypot(d.x, d.z)
        if rho > 0.034 or d.y > 0.022:
            continue
        sx = 1.0 if e.x > 0 else -1.0
        w = (1.0 - _step(0.017, 0.033, rho)) * (1.0 - _step(0.006, 0.022, d.y))
        d = d * (1.0 + (EYE_GROW - 1.0) * w)
        d.x *= 1.0 + 0.06 * w                                         # longer, almond
        d.x, d.z = _rot(d.x, d.z, sx * math.radians(8.0) * w)          # the outer corner lifted
        rho = math.hypot(d.x, d.z)
        lid = w * _step(-0.001, 0.005, d.z) * (1.0 - _step(EYE_R * 1.15, EYE_R * 2.3, rho))
        d.y, d.z = _rot(d.y, d.z, math.radians(10.0) * lid)            # the upper lid lowered over the iris
        low = w * _step(0.001, -0.005, d.z) * (1.0 - _step(EYE_R * 1.15, EYE_R * 2.2, rho))
        d.y, d.z = _rot(d.y, d.z, -math.radians(5.0) * low)            # the lower lid raised a little
        q = e + d
    x, y, z = q
    ax = abs(x)
    sx = 1.0 if x >= 0 else -1.0
    front = 1.0 if y < -0.01 else max(0.0, 1.0 - (y + 0.01) / 0.04)
    side = 1.0 - _step(0.015, 0.06, y)
    # a smooth brow, the outer brow a little raised for the arch
    q.y += 0.0012 * _g(z - 0.022, 0.01) * _g(ax - 0.026, 0.02) * front
    q.z += 0.0014 * _g(z - 0.02, 0.012) * _g(ax - 0.052, 0.012) * side
    # high cheekbones over a soft hollow
    cheek = _g(z + 0.02, 0.018) * _g(ax - 0.05, 0.02) * side
    q.x += sx * 0.0014 * cheek
    q.y -= 0.0018 * cheek
    q.z += 0.0012 * cheek
    hollow = _g(z + 0.058, 0.02) * _g(ax - 0.048, 0.02) * side
    q.x -= sx * 0.0024 * hollow
    q.y += 0.0014 * hollow
    q.x *= 1.0 - 0.04 * _g(z + 0.062, 0.03) * _step(0.015, 0.045, ax) * side
    # a slim straight nose, its tip a little lifted
    q.x *= 1.0 - 0.15 * _g(z + 0.036, 0.012) * _g(ax, 0.024) * front
    q.x *= 1.0 - 0.12 * _g(z + 0.006, 0.016) * _g(ax, 0.014) * front
    q.y += 0.0008 * _g(z + 0.032, 0.007) * _g(ax, 0.012) * front
    q.z += 0.0005 * _g(z + 0.04, 0.006) * _g(ax, 0.012) * front
    q.x *= 1.0 - 0.08 * _g(z + 0.042, 0.008) * _step(0.008, 0.016, ax) * _g(ax - 0.016, 0.01) * front   # a narrower alar base
    # full lips, a wider mouth, the lower lip a little fuller
    zm = -0.069
    lips = _g(z - zm, 0.011) * _g(ax, 0.021) * front
    q.z = zm + (q.z - zm) * (1.0 + 0.18 * lips)
    q.x *= 1.0 + 0.06 * _g(z - zm, 0.01) * _g(ax, 0.03) * front
    q.y -= 0.0006 * lips + 0.0004 * _g(z + 0.076, 0.005) * _g(ax, 0.014) * front
    q.y += 0.0022 * _g(z + 0.062, 0.016) * _g(ax, 0.03) * front          # the mouth set back a little
    # a fine jaw tapering to a neat chin
    low = _step(-0.03, -0.11, z) * (1.0 - _step(0.05, 0.1, y))
    q.x *= 1.0 - 0.14 * low
    q.z += 0.08 * max(-0.045, min(0.0, z + 0.075))                    # a longer chin
    gon = _g(z + 0.095, 0.022) * _step(0.03, 0.055, ax) * _g(y - 0.05, 0.035)
    q.x -= sx * 0.005 * gon
    chin = _g(z + 0.104, 0.014) * _g(ax, 0.02) * front
    q.x *= 1.0 - 0.06 * chin
    q.y -= 0.0045 * chin
    q.z -= 0.0015 * chin
    # the ears' tops drawn up and back to fine points
    w = _g(y - 0.086, 0.017) * _step(-0.032, 0.004, z) * _step(0.071, 0.081, ax)
    w = w ** 1.4
    q.z += 0.034 * w
    q.y += 0.024 * w
    q.x += sx * 0.011 * w
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

    def at(self, phi, z, inner=False):
        """The surface seen from the head's axis at height z, phi round from
        straight ahead (-Y), toward +X positive. None where nothing is hit.
        inner finds the skull from inside, under an ear."""
        o = Vector((NECK[0], NECK[1], z))
        d = Vector((math.sin(phi), -math.cos(phi), 0.0))
        hit = self.bvh.ray_cast(o, d, 0.7) if inner else self.bvh.ray_cast(o + d * 0.6, -d, 0.7)
        return (hit[0], hit[1]) if hit[0] is not None else (None, None)

    def out(self, phi, z, by, inner=False):
        """A point by cells out from the surface, along the ray."""
        p, n = self.at(phi, z, inner)
        if p is None:
            return None
        return p + Vector((math.sin(phi), -math.cos(phi), 0.0)) * by


# ---------------------------------------------------------------- pictures

LOCK_U = 0.74   # the strand picture's lock spans u 0 to this, its lashes the rest


def strand_picture(w=512, h=1024, seed=17):
    """Sleek black hair for the cards, from numbers. Across u 0 to LOCK_U a
    lock of wisps: each a solid tapered ribbon of hair a few texels wide,
    drawn with strands in its colour, so its cut-out holds up when the
    picture is seen small, the wisps fullest down the lock's middle and
    each tapering to a tip at its own length, a few flyaways straying from
    them, and a cool sheen across the strands where light catches them.
    Past LOCK_U a strip of short lashes. RGB and the cut-out alpha, the
    clear texels the hair's own colour. The colours are worked out linear
    and stored as sRGB, as the picture is read. Row 0 is the tip, the root
    at the top."""
    rng = np.random.default_rng(seed)
    rgba = np.zeros((h, w, 4), np.float32)
    rgba[..., :3] = (0.02, 0.02, 0.026)
    sheen_col = np.array([0.8, 0.88, 1.0])
    ys = np.arange(h)
    xs = np.arange(w)

    def wisp(cx, y0, y1, width, sway, sheen, band, seed_):
        """A tapered ribbon from its tip at row y0 to its root at row y1."""
        r = np.random.default_rng(seed_)
        lines = r.uniform(0.0, 1.0, 9)
        glints = r.uniform(-1.0, 1.0, 7)        # where across the wisp its strands catch the light
        lift = r.uniform(-0.12, 0.12, 7)        # and how far up or down the lock each catches it
        for y in range(max(0, int(y0)), min(h, int(y1))):
            t = max(0.0, (y - y0) / max(1.0, y1 - y0))
            x = cx + sway * math.sin(t * math.pi * 0.8)
            half = width * 0.5 * (0.08 + 0.92 * t ** 0.55)
            v = y / h
            shine = [sheen * math.exp(-((v - band - dl) / 0.07) ** 2) for dl in lift]
            lo, hi = int(x - half - 2), int(x + half + 3)
            for xi in range(max(0, lo), min(w, hi)):
                d = abs(xi - x)
                a = min(1.0, max(0.0, half + 0.8 - d) / 1.6) * min(1.0, t * 8.0, ((h - y) / (0.1 * h)) ** 1.5)
                if a <= rgba[y, xi, 3]:
                    continue
                q = (xi - x) / max(half, 0.5)
                strand = 0.75 + 0.5 * sum(math.exp(-((q - (2 * l - 1)) / 0.09) ** 2) for l in lines[:5]) / 2
                glint = sum(sh * math.exp(-((q - g) / 0.12) ** 2) for sh, g in zip(shine, glints))
                c = np.array([0.02, 0.02, 0.026]) * strand + sheen_col * min(glint, sheen)
                rgba[y, xi, :3] = c
                rgba[y, xi, 3] = a

    lw = w * LOCK_U
    n = 17
    for k in range(n):
        f = (k + rng.uniform(0.2, 0.8)) / n
        env = math.sin(math.pi * f) ** 0.5
        tip = h * (rng.uniform(0.0, 0.25) + 0.32 * (1.0 - env))
        wisp(6 + f * (lw - 12), tip, h, rng.uniform(26.0, 40.0) * (0.6 + 0.4 * env),
             rng.uniform(-4.0, 4.0), rng.uniform(0.096, 0.16), rng.uniform(0.5, 0.85), int(rng.integers(1 << 30)))
    for _ in range(8):
        x0 = rng.uniform(0.15, 0.85) * lw
        drift = rng.choice((-1.0, 1.0)) * rng.uniform(12.0, 30.0)
        y1 = h * rng.uniform(0.4, 0.85)
        y0 = y1 - h * rng.uniform(0.15, 0.3)
        for y in range(int(y0), int(y1)):
            t = max(0.0, (y - y0) / max(1.0, y1 - y0))
            x = x0 + drift * (1.0 - t) ** 2
            for xi in range(int(x - 1), int(x + 2)):
                if 0 <= xi < w:
                    a = max(0.0, 1.0 - abs(xi - x) / 1.4) * min(1.0, t * 5.0)
                    if a > rgba[y, xi, 3]:
                        rgba[y, xi, :3] = (0.03, 0.03, 0.036)
                        rgba[y, xi, 3] = a
    for _ in range(40):
        x0 = rng.uniform(lw + 8, w - 8)
        wisp(x0, h * rng.uniform(0.25, 0.55), h, rng.uniform(4.0, 7.0), rng.uniform(-3.0, 3.0), 0.0, 0.7, int(rng.integers(1 << 30)))
    lin = rgba[..., :3]
    rgba[..., :3] = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(np.maximum(lin, 1e-8), 1.0 / 2.4) - 0.055)
    return uk.image("thirsha_strands", rgba)


# ---------------------------------------------------------------- the head

def _thin_skull(ob):
    """Decimates the head to FACE_TRIS, keeping detail where she is looked
    at: the face and the ears keep most, the back of the skull under the
    hair and the neck under the collar the least."""
    g = ob.vertex_groups.new(name="thin")
    ey = NECK[1] - 0.1 * SCALE
    for v in ob.data.vertices:
        ry, rz = (v.co.y - ey) / SCALE, (v.co.z - EYE_Z) / SCALE
        face = 1.0 - _step(0.02, 0.06, ry)
        ear = _step(0.055, 0.07, abs(v.co.x) / SCALE) * _g(ry - 0.085, 0.03) * _step(-0.05, -0.02, rz)
        keep = max(face, ear) * _step(-0.17, -0.12, rz)
        g.add([v.index], lerp(1.0, 0.12, keep), "REPLACE")
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    dec = ob.modifiers.new("lighter", "DECIMATE")
    dec.ratio = min(1.0, FACE_TRIS / max(1, tris))
    dec.vertex_group = "thin"
    dec.vertex_group_factor = 1.0
    uk.apply(ob)
    ob.vertex_groups.clear()
    _unfold(ob)


def _unfold(ob, rounds=6):
    """Smooths away the few folds the decimation leaves on the face, where
    two neighbouring faces turn back on each other and shade as a dark
    speck. The eye sockets behind the eyeballs are left alone."""
    ey = NECK[1] - 0.1 * SCALE
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    for _ in range(rounds):
        bm.normal_update()
        vs = set()
        for e in bm.edges:
            if len(e.link_faces) == 2 and e.link_faces[0].normal.dot(e.link_faces[1].normal) < -0.2:
                c = (e.verts[0].co + e.verts[1].co) / 2
                if c.y < ey - 0.005:
                    for f in e.link_faces:
                        vs.update(f.verts)
        if not vs:
            break
        bmesh.ops.smooth_vert(bm, verts=list(vs), factor=0.6, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(ob.data)
    bm.free()


def head(rig, M, assets):
    """The head piece's parts in model space at rest: the face and eyes, the
    hair volumes apart from them so the face can take more of the atlas,
    the head's own cards, and the locks for the hair pieces. Returns (face
    parts, hair parts, card parts, {piece: card parts}, face)."""
    ob, eyes = uk.human_head(assets, EYE_Z, NECK, SCALE, cut_z=3.13, shape=huntress, source=SOURCE, eye=".eye", multires=2)
    ob.data.materials.clear()
    ob.data.materials.append(M["skin"])
    _thin_skull(ob)
    # the decimation's slivers go, and every face turns out
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.dissolve_degenerate(bm, dist=0.0015, edges=bm.edges[:])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(ob.data)
    bm.free()
    for f in ob.data.polygons:
        f.use_smooth = True
    eyes = [(c, r * EYE_GROW) for c, r in eyes]
    face = Face(ob, eyes)
    parts = [ob]
    for c, r in eyes:
        parts.append(eh.ellipsoid_ob(c + V((0, 0.003, 0)), (r * 0.98, r * 0.98, r * 0.98), 16, 10, M["eye"], "eye"))
    cap = hair_cap(face, M)
    shell = hair_shell(face, M)
    paths = framing_paths(face)
    hair = [cap, shell, framing(paths, M)]
    cards = crown_cards(face, M, cap) + hair_cards(face, M) + side_cards(face, M) + framing_cards(paths, M) + lashes(face, M)
    locks = hair_locks(rig, face, M, cap, shell)
    return parts, hair, cards, locks, face


# ---------------------------------------------------------------- lashes

def lid_margin(face, c, r, dx, up=True):
    """Where the lid's edge crosses the eyeball dx across from its centre
    c, found by looking straight in for the first height at which skin
    lies in front of the eye. None where the eye is shut there."""
    R = r * 0.98
    sgn = 1.0 if up else -1.0
    steps = 48
    for k in range(steps + 1):
        dz = sgn * R * k / steps
        if dx * dx + dz * dz >= R * R:
            return None
        o = V((c.x + dx, c.y - 0.4, c.z + dz))
        ball = c.y + 0.003 - math.sqrt(R * R - dx * dx - dz * dz)
        hit = face.bvh.ray_cast(o, V((0, 1, 0)), 0.6)
        if hit[0] is not None and hit[0].y < ball - 0.001:
            return V((c.x + dx, min(ball, hit[0].y) - 0.002, c.z + dz))
    return None


def lashes(face, M):
    """Long lashes along each upper lid, curling up and out, longest at the
    outer corner, as a strip of strand cards."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for c, r in face.eyes:
        sx = 1.0 if c.x > 0 else -1.0
        roots = []
        for k in range(15):
            t = k / 14
            dx = sx * lerp(-0.8, 0.95, t) * r
            p = lid_margin(face, c, r, dx)
            if p is not None:
                roots.append((t, p))
        if len(roots) < 4:
            continue
        rows = []
        for t, p in roots:
            ln = lerp(0.014, 0.03, t ** 1.3)
            out = V((sx * (0.15 + 0.5 * t), 0.0, 0.0))
            a = (V((0, -1.0, 0.55)) + out).normalized()
            b = (V((0, -0.45, 1.0)) + out * 1.3).normalized()
            rows.append([p, p + a * ln * 0.55, p + a * ln * 0.55 + b * ln * 0.5])
        vs = [[bm.verts.new(q) for q in row] for row in rows]
        for i in range(len(rows) - 1):
            for j in range(2):
                f = bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
                u0 = lerp(LOCK_U + 0.02, 0.99, i / (len(rows) - 1))
                u1 = lerp(LOCK_U + 0.02, 0.99, (i + 1) / (len(rows) - 1))
                v0, v1 = 1.0 - 0.45 * j, 1.0 - 0.45 * (j + 1)
                for loop, (u, v) in zip(f.loops, ((u0, v0), (u1, v0), (u1, v1), (u0, v1))):
                    loop[uv].uv = (u, v)
    return [uk.shade(uk.mesh("lashes", bm, [M["strands"]], outward=False), 80)]


# ---------------------------------------------------------------- the hair

def hairline(phi):
    """How far above the eyes the hair starts, round the head: a rounded
    brow line, down over the temples to the ears, over the backs of the
    ears and down the nape behind."""
    a = abs(math.atan2(math.sin(phi), math.cos(phi)))
    if a < 1.0:
        return lerp(0.158, 0.112, (a / 1.0) ** 1.6)
    if a < 1.45:
        return lerp(0.112, -0.02, smooth01((a - 1.0) / 0.45))
    if a < 1.75:
        return lerp(-0.02, -0.08, smooth01((a - 1.45) / 0.3))
    return lerp(-0.08, -0.14, min(1.0, (a - 1.75) / 0.8))


def smooth01(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3.0 - 2.0 * t)


def hair_cap(face, M):
    """The top of the head under the cards: a smooth volume standing off the
    skull most at the crown, from the hairline up, a shallow part down the
    middle."""
    bm = bmesh.new()
    n, rows_n = 32, 15
    rows = []
    for j in range(rows_n):
        t = j / (rows_n - 1)
        row = []
        for i in range(n):
            phi = 2 * math.pi * i / n
            edge = hairline(phi)
            z = lerp(face.top - 0.002, EYE_Z + edge, t ** 0.8)
            part = 0.004 * _g(math.sin(phi) * (1.0 - t), 0.05) * (1.0 if math.cos(phi) > 0 else 0.0) * (1.0 - t)
            zc = min(z, face.top - 0.012)
            p = face.out(phi, zc, 0.008 + 0.05 * (1 - t) ** 1.3 - part, inner=True)
            if p is None:
                p = Vector((NECK[0], NECK[1], z))
            else:
                ear, _ = face.at(phi, zc)
                axis = Vector((NECK[0], NECK[1], zc))
                if ear is not None and (ear - axis).length + 0.035 > (p - axis).length:
                    p = axis + (p - axis).normalized() * ((ear - axis).length + 0.035)
            row.append(Vector((p.x, p.y, max(z, p.z) - part * 0.5)))
        rows.append(row)
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    for r in range(rows_n - 1):
        for c in range(n):
            d = (c + 1) % n
            bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][d], vs[r][d]))
    top = bm.verts.new(Vector((NECK[0], NECK[1] + 0.03, face.top + 0.055)))
    for c in range(n):
        bm.faces.new((top, vs[0][c], vs[0][(c + 1) % n]))
    return uk.shade(uk.mesh("haircap", bm, [M["hair"]]), 180)


def crown_cards(face, M, cap, per=21, seed=11):
    """Strands over the crown in three layers, rooted along the part and
    sweeping down either side and back over the cap, each layer lifting a
    little off the one under it and arching as it goes, each ending at its
    own length among the hair below."""
    rng = np.random.default_rng(seed)
    bm0 = bmesh.new()
    bm0.from_mesh(cap.data)
    bvh = BVHTree.FromBMesh(bm0)
    bm0.free()
    C = Vector((NECK[0], NECK[1] + 0.02, EYE_Z + 0.06))
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for side in (-1.0, 1.0):
        for i in range(per):
            layer = i % 3
            s_ = (i + rng.uniform(0.0, 1.0)) / per
            beta = math.radians(lerp(16.0, 128.0, s_ ** 0.8))
            root = Vector((side * 0.035, -math.cos(beta), math.sin(beta))).normalized()
            az = side * math.radians(lerp(62.0, 165.0, s_) + rng.uniform(-8.0, 8.0))
            el = math.radians(lerp(-4.0, -18.0, s_) + rng.uniform(0.0, 18.0))
            end = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
            path = []
            for k in range(6):
                t = k / 5
                d = (root * (1.0 - t) + end * t).normalized()
                hit = bvh.ray_cast(C + d * 0.8, -d, 0.9)
                if hit[0] is None:
                    break
                path.append(hit[0] + hit[1] * (0.005 + 0.007 * layer + 0.006 * math.sin(math.pi * t) * (layer + 1) / 3))
            if len(path) >= 4:
                card(bm, uv, path, rng.uniform(0.08, 0.12), (path[len(path) // 2] - C).normalized(), rng.uniform() < 0.5, root=1.0)
    return [uk.shade(uk.mesh("crowncards", bm, [M["strands"]], outward=False), 80)]


def fall(face, phi, z, by=0.0):
    """A point on the fall of hair behind the ears at angle phi and height
    z, by cells out from it: close over the skull, then hanging straight
    and spreading toward the shoulders."""
    drop = max(0.0, (EYE_Z - z) / 0.5)
    zc = max(z, face.chin + 0.02)
    p = face.out(phi, zc, 0.025 + 0.035 * drop + by, inner=True)
    if p is None:
        return None
    # over a pointed ear the hair stands off the ear instead, so its tip stays under the hair
    ear, _ = face.at(phi, zc)
    if ear is not None:
        axis = Vector((NECK[0], NECK[1], zc))
        lift = (ear - axis).length + 0.022 + by - (p - axis).length
        if lift > 0.0:
            p = p + (p - axis).normalized() * lift
    k = 1.0 + 0.4 * drop
    return Vector((NECK[0] + (p.x - NECK[0]) * k, NECK[1] + (p.y - NECK[1]) * (1.0 + 0.06 * drop) + 0.07 * drop, z))


SHELL = 0.03   # the fall's thickness outward from fall(), the cards lying beyond it
SPAN = 1.56    # how far round from the back the fall reaches either side, over the ears' tips


def hem(u):
    """The volume's end across the fall, u from -1 to 1: lowest down the
    middle of the back, a little shorter toward the sides. The cards over it
    run on past it, each to its own length."""
    return 2.97 + 0.06 * u * u


def hair_shell(face, M):
    """A smooth fall of hair down the back of the head to the shoulders,
    behind the ears, sleek and spreading toward its ends."""
    bm = bmesh.new()
    rows = []
    for k in range(11):
        row = []
        for i in range(17):
            u = lerp(-1.0, 1.0, i / 16)
            phi = math.pi + SPAN * u
            z = lerp(EYE_Z + 0.06, hem(u), k / 10)
            p = fall(face, phi, z, -0.02 * smooth01((abs(u) - 0.75) / 0.25) * smooth01((k - 3) / 3) - 0.016 * max(0.0, 1.0 - k / 3))
            if p is None:
                p = row[-1] if row else Vector((0, NECK[1] + 0.22, z))
            row.append(p)
        rows.append(row)
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    for r in range(len(rows) - 1):
        for c in range(len(rows[0]) - 1):
            bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][c + 1], vs[r][c + 1]))
    shell = uk.mesh("hairshell", bm, [M["hair"]], outward=False)
    m = shell.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = SHELL, 1.0
    uk.apply(shell)
    return uk.shade(shell, 60)


def card(bm, uv, path, width, hint, flip=False, taper=0.3, root=0.9):
    """A strand card down a path, root first, width across and facing out
    along hint, wearing the whole lock in the strand picture: u across it,
    mirrored when flip, v from root to the tip (0). A root of 1 takes in the
    picture's faded top, for a card whose root shows."""
    n = len(path)
    vs = []
    for k, p in enumerate(path):
        along = (path[min(n - 1, k + 1)] - path[max(0, k - 1)]).normalized()
        across = along.cross(hint)
        if across.length < 1e-6:
            across = along.orthogonal()
        across.normalize()
        w = width * (1.0 - taper * k / (n - 1))
        vs.append((bm.verts.new(p - across * w * 0.5), bm.verts.new(p + across * w * 0.5)))
    u0, u1 = (LOCK_U, 0.0) if flip else (0.0, LOCK_U)
    for k in range(n - 1):
        f = bm.faces.new((vs[k][0], vs[k + 1][0], vs[k + 1][1], vs[k][1]))
        va, vb = root * (1.0 - k / (n - 1)), root * (1.0 - (k + 1) / (n - 1))
        for loop, (u, v) in zip(f.loops, ((u0, va), (u0, vb), (u1, vb), (u1, va))):
            loop[uv].uv = (u, v)


def hair_cards(face, M, count=56, seed=3):
    """Strand cards down the fall of hair behind, in three layers lying
    close, straight and sleek, running on past the end of the volume each
    to its own length, and a few flyaways standing off them."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        layer = i % 3
        fly = i % 11 == 10
        u = rng.uniform(-0.95, 0.95)
        phi = math.pi + SPAN * u
        drift = rng.uniform(-0.06, 0.06)
        z0 = rng.uniform(EYE_Z + 0.02, EYE_Z + 0.1)
        z1 = hem(u) - rng.uniform(0.0, 0.14)
        path = []
        for k in range(6):
            t = k / 5
            by = (SHELL + 0.004 + 0.009 * layer) * min(1.0, 0.3 + t * 2.5) + (0.03 * t * t if fly else 0.0)
            p = fall(face, phi + drift * t, lerp(z0, z1, t), by)
            if p is None:
                break
            path.append(p)
        if len(path) < 3:
            continue
        w = rng.uniform(0.02, 0.035) if fly else rng.uniform(0.09, 0.14)
        card(bm, uv, path, w, Vector((math.sin(phi), -math.cos(phi), 0)), rng.uniform() < 0.5, root=1.0)
    return [uk.shade(uk.mesh("haircards", bm, [M["strands"]], outward=False), 80)]


def side_cards(face, M, count=24, seed=29):
    """Hair falling behind the pointed ears to the shoulders on both sides,
    over the edges of the fall, in two layers of their own lengths."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        side = 1 if i % 2 else -1
        u = side * rng.uniform(0.62, 0.98)
        phi = math.pi + SPAN * u
        z0 = rng.uniform(EYE_Z - 0.02, EYE_Z + 0.08)
        z1 = hem(u) - rng.uniform(0.0, 0.12)
        path = []
        for k in range(7):
            p = fall(face, phi, lerp(z0, z1, k / 6), (SHELL + 0.004 + 0.009 * ((i // 2) % 2)) * min(1.0, 0.3 + k / 6 * 2.5))
            if p is None:
                break
            path.append(p)
        if len(path) >= 3:
            card(bm, uv, path, rng.uniform(0.08, 0.12), Vector((math.sin(phi), -math.cos(phi), 0)), rng.uniform() < 0.5, root=1.0)
    return [uk.shade(uk.mesh("sidecards", bm, [M["strands"]], outward=False), 80)]


# The locks framing the face, four either side: where each leaves the part
# (phi), where it passes the temple (phi, height above the eyes), how far it
# hangs, how far it swings out and forward over the collar.
FRAMING = ((0.06, 0.9, 0.07, 1.0, -0.01, 0.0), (0.18, 1.0, 0.05, 0.92, 0.015, 0.015),
           (0.32, 1.1, 0.03, 0.8, 0.04, 0.025), (0.48, 1.2, 0.0, 0.66, 0.06, 0.0))


def framing_paths(face, seed=41):
    """The way each lock framing the face runs, from under the crown's
    strands over the temple, then hanging clear of the cheek and jaw with a
    gentle S, out over the collar and down over the breast: (side, points,
    outward)."""
    rng = np.random.default_rng(seed)
    out = []
    for side in (-1.0, 1.0):
        for ph0, ph_t, zt, length, swing, fwd in FRAMING:
            pts = []
            for ph, z, by in ((max(ph0, ph_t - 0.45), EYE_Z + 0.15, 0.008), ((ph0 + 2 * ph_t) / 3, EYE_Z + 0.11, 0.016), (ph_t, EYE_Z + zt, 0.02)):
                p = face.out(side * ph, min(z, face.top - 0.02), by, inner=True)
                if p is not None:
                    pts.append(p)
            if len(pts) < 3:
                continue
            last = pts[-1]
            o = Vector((last.x - NECK[0], last.y - NECK[1], 0.0)).normalized()
            for t, (out_, f_, dz) in enumerate(((0.012, 0.0, -0.1), (0.06, -0.005, -0.2), (0.09, -0.015, -0.29), (0.11, -0.025, -0.38), (0.12, -0.035, -0.46))):
                if dz < -0.46 * length - 0.01:
                    break
                s_ = 0.012 * math.sin((t + 1) * 1.3) * side
                pts.append(last + o * (out_ + swing * (t + 1) / 5) + Vector((s_, f_ - fwd * (t + 1) / 5, dz)) + Vector((0, 0, rng.uniform(-0.01, 0.01))))
            outs = []
            for p in pts:
                r = Vector((p.x - NECK[0], p.y - NECK[1], 0.0))
                up = max(0.0, min(1.0, (p.z - (EYE_Z + 0.1)) / 0.12))
                outs.append((r.normalized() * (1.0 - up) + Vector((0, 0, 1)) * up).normalized())
            out.append((side, pts, outs))
    return out


def framing(paths, M):
    """Under the longest locks either side a slim smooth core of hair, so no
    light shows through the cards over it."""
    bm = bmesh.new()
    for side, pts, outs in paths[0:2] + paths[4:6]:
        pts, outs = pts[1:], outs[1:]
        rings = []
        n = len(pts)
        for k, (p, o) in enumerate(zip(pts, outs)):
            t = (pts[min(n - 1, k + 1)] - pts[max(0, k - 1)]).normalized()
            w = t.cross(o).normalized()
            o = w.cross(t).normalized() * (1.0 if w.cross(t).dot(o) >= 0 else -1.0)
            f = k / (n - 1)
            hw = lerp(0.03, 0.016, f ** 0.8)
            th_ = lerp(0.012, 0.008, f)
            c = p + o * th_ * 0.6
            rings.append([c + w * math.cos(2 * math.pi * i / 8) * hw + o * math.sin(2 * math.pi * i / 8) * th_ for i in range(8)])
        tip = pts[-1] + (pts[-1] - pts[-2]).normalized() * 0.02
        rings.append([tip.copy() for _ in rings[-1]])
        uk.loft(bm, rings, cap0=True)
    ob = uk.mesh("framing", bm, [M["hair"]])
    return uk.shade(ob, 180)


def framing_cards(paths, M, seed=43):
    """Each framing lock as two or three strand cards lying over one
    another, the outer ones narrower and a little shorter, mirrored at
    random, a flyaway on some."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for j, (side, pts, outs) in enumerate(paths):
        n = len(pts)
        for layer in range(3 if j % 4 < 2 else 2):
            path = []
            for k, (p, o) in enumerate(zip(pts, outs)):
                if k == 0 and layer:
                    continue
                path.append(p + o * (0.012 + 0.01 * layer))
            path.append(path[-1] + (path[-1] - path[-2]).normalized() * rng.uniform(0.03, 0.1))
            if layer:
                path = path[:max(3, len(path) - int(rng.integers(0, 2)))]
            card(bm, uv, path, rng.uniform(0.11, 0.15) * (1.0 - 0.15 * layer), outs[n // 2], rng.uniform() < 0.5, 0.35)
        if rng.uniform() < 0.5:
            path = [p + o * 0.04 + Vector((side * 0.01 * k, 0, 0)) for k, (p, o) in enumerate(zip(pts[2:], outs[2:]))]
            if len(path) >= 3:
                card(bm, uv, path, 0.025, outs[n // 2], rng.uniform() < 0.5, 0.2)
    return [uk.shade(uk.mesh("framecards", bm, [M["strands"]], outward=False), 80)]


# ---------------------------------------------------------------- the locks

# Each lock the script swings: where its strands start round the head (phi,
# height above the eyes), how many, and how far they fall, in cells. The
# two on top run back from the crown either side of the part and down, and
# the rest lie on the fall of hair, as the original's run down from the same
# places.
LOCKS = {
    "hair1": dict(phi=(-0.3, 0.3), up=(0.17, 0.2), n=6, drop=0.3),
    "hair2": dict(phi=(-0.5, 0.5), up=(0.18, 0.21), n=6, drop=0.36),
    "hair3": dict(phi=(2.6, 3.7), up=(0.08, 0.14), n=8, drop=0.48),
    "hair4_L": dict(phi=(2.5, 3.0), up=(-0.18, -0.1), n=5, drop=0.3),
    "hair4_R": dict(phi=(3.3, 3.8), up=(-0.18, -0.1), n=5, drop=0.3),
    "hair5": dict(phi=(-2.2, -1.8), up=(0.08, 0.14), n=5, drop=0.42),
    "hair6": dict(phi=(-2.7, -2.2), up=(0.08, 0.15), n=5, drop=0.42),
    "hair7": dict(phi=(1.8, 2.2), up=(0.08, 0.14), n=5, drop=0.42),
    "hair8": dict(phi=(2.2, 2.7), up=(0.08, 0.15), n=5, drop=0.42),
}


def hair_locks(rig, face, M, cap, shell, seed=23):
    """A clump of strand cards for each lock piece, lying on the hair near
    where the original's lock starts, so as the script turns a lock its
    strands lift out of the hair and stream."""
    rng = np.random.default_rng(seed)
    bm0 = bmesh.new()
    bm0.from_mesh(cap.data)
    over = BVHTree.FromBMesh(bm0)
    bm0.from_mesh(shell.data)
    outer = BVHTree.FromBMesh(bm0)
    bm0.free()
    out = {}
    for piece, L in LOCKS.items():
        if piece not in rig.by_name and piece.lower() not in rig.by_name:
            continue
        bm = bmesh.new()
        uv = bm.loops.layers.uv.new("UVMap")
        for k in range(2 * L["n"]):
            phi = lerp(L["phi"][0], L["phi"][1], (k + rng.uniform(0.1, 0.9)) / (2 * L["n"]))
            z0 = EYE_Z + rng.uniform(*L["up"])
            by = SHELL + 0.001 + 0.004 * (k % 2)
            path = []
            hint = Vector((math.sin(phi), -math.cos(phi), 0.0))
            if piece in ("hair1", "hair2"):
                x0 = math.copysign(0.025 + 0.2 * abs(math.sin(phi)), phi)
                for j in range(6):
                    t = j / 5
                    o = Vector((x0 * (1.0 + 0.4 * t), NECK[1] + lerp(0.0, 0.14, t), face.top + 0.3))
                    hit = over.ray_cast(o, Vector((0, 0, -1)), 0.9)
                    if hit[0] is not None:
                        path.append(hit[0] + hit[1] * (0.005 + 0.004 * (k % 2)))
                        if j == 1:
                            hint = (hit[1] + Vector((0, 0.7, 0.7))).normalized()
                if not path:
                    continue
                last = path[-1]
                for j in range(1, 4):
                    o = Vector((last.x * (1.0 + 0.1 * j), NECK[1] + 0.8, last.z - L["drop"] * j / 3))
                    hit = outer.ray_cast(o, Vector((0, -1, 0)), 1.0)
                    if hit[0] is not None:
                        path.append(hit[0] + hit[1] * (0.008 + 0.004 * (k % 2)))
            else:
                for j in range(5):
                    q = fall(face, phi, z0 - L["drop"] * j / 4, by * min(1.0, 0.2 + j * 0.5))
                    if q is not None:
                        path.append(q)
            if len(path) < 3:
                continue
            wide = (0.06, 0.08) if piece in ("hair1", "hair2") else (0.05, 0.08)
            card(bm, uv, path, rng.uniform(*wide), hint, rng.uniform() < 0.5, root=1.0)
        out[piece] = uk.shade(uk.mesh("lock_" + piece, bm, [M["strands"]], outward=False), 80)
    return out
