"""Elsin's head for elsin.py: a realistic face from Blender Studio's CC0
Human Base Meshes (unitkit.human_head), reshaped into a stern, weathered
king, with eyes, wavy dark-brown shoulder-length hair, a full beard joined
to the moustache, and a gilded crenellated crown set with red stones.

The hair and beard are a solid volume, so no scalp or chin shows through,
under strand cards cut out of a strand picture drawn here from numbers,
which give them a soft edge and strands up close. Everything is placed by
casting rays onto the face itself, so it follows the face's real shape.
"""
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import unitkit as uk

V = Vector
EYE_Z = 3.6           # the eyes' height, cells
NECK = (0.0, 0.035)   # the neck's axis, under the head's pivot
SCALE = 2.3           # cells to a metre: a head of about 0.53 cells
EYE_BACK = 0.002      # metres the eyes sit deeper than the bundle's


def lerp(a, b, t):
    return a + (b - a) * t


def _g(a, s):
    return math.exp(-(a / s) ** 2)


def stern(p, marks):
    """The bundle's neutral face as a weathered Western European king: a
    strong brow ridge over deep-set eyes with a crease in each upper lid, a
    long high-bridged nose with a slight hook, cheekbones over hollow
    cheeks, a longer face and a broad square jaw, and a thick neck. p is in
    metres, centred between the eyes, -y forward."""
    x, y, z = p
    q = Vector(p)
    front = 1.0 if y < -0.01 else max(0.0, 1.0 - (y + 0.01) / 0.04)
    ax = abs(x)
    ex = abs(marks["eyes"][0].x)
    if z < 0.0:
        q.z = z * 1.07                                                        # a longer lower face
    q.y -= 0.006 * _g(z - 0.018, 0.0105) * _g(ax - 0.015, 0.03) * front       # brow ridge
    q.z -= 0.0022 * _g(z - 0.02, 0.012) * _g(ax, 0.018) * front               # drawn down between the brows
    q.y += EYE_BACK * _g(ax - ex, 0.015) * _g(z, 0.012) * front               # deep-set eyes
    cz = 0.0105 - 0.005 * ((ax - ex) / 0.014) ** 2
    q.y += 0.0011 * _g(z - cz, 0.0016) * _g(ax - ex, 0.011) * front           # the crease in each upper lid
    q.y -= (0.0046 * _g(z, 0.011) + 0.0034 * _g(z + 0.018, 0.009)
            + 0.0018 * _g(z + 0.034, 0.008)) * _g(ax, 0.0105) * front        # a high bridge with a slight hook
    q.z -= 0.0022 * _g(z + 0.042, 0.009) * _g(ax, 0.02) * front               # the tip a little down
    q.x *= 1.0 - 0.16 * _g(z + 0.008, 0.014) * _g(ax, 0.016) * front          # a narrow bridge
    q.x *= 1.0 - 0.11 * _g(z + 0.04, 0.01) * _g(ax - 0.015, 0.012) * front    # narrow nostrils
    q.x *= 1.0 - 0.1 * _g(z + 0.038, 0.008) * _g(ax, 0.01) * front            # and a narrow tip
    q.y -= 0.0018 * _g(z + 0.012, 0.01) * _g(ax - 0.05, 0.012) * front        # cheekbones
    q.y += 0.0024 * _g(z + 0.052, 0.014) * _g(ax - 0.043, 0.013) * front      # hollow under them
    q.y -= 0.0013 * _g(z + 0.015, 0.005) * _g(ax - 0.033, 0.012) * front      # heavy under the eyes
    q.y += 0.002 * _g(z + 0.05, 0.02) * _g(ax - 0.03, 0.006) * front          # the folds from the nose
    q.x *= 1.0 - 0.05 * _g(z + 0.02, 0.03) * front                           # a narrower middle of the face
    if z < -0.06:
        k = min(1.0, (-0.06 - z) / 0.04)
        q.x *= 1.0 + 0.085 * k                                                # a broad square jaw
        q.y -= 0.0035 * k * _g(ax, 0.028) * front                             # a firm chin
    if z < -0.11:
        n = marks["neck"]
        k = min(1.0, (-0.11 - z) / 0.04)
        q.x = n.x + (q.x - n.x) * (1.0 + 0.12 * k)                             # a thick neck
        q.y = n.y + (q.y - n.y) * (1.0 + 0.1 * k)
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
        self.chin = min((v for v in vs if v.y < NECK[1] - 0.12), key=lambda v: v.z).z
        self.nose = min(v.y for v in vs)

    def at(self, phi, z, inside=None):
        """The surface seen from the head's axis at height z, phi round from
        straight ahead (-Y), toward +X positive. None where nothing is hit."""
        o = Vector((NECK[0], NECK[1], z)) if inside is None else inside
        d = Vector((math.sin(phi), -math.cos(phi), 0.0))
        hit = self.bvh.ray_cast(o + d * 0.6, -d, 0.7)
        return (hit[0], hit[1]) if hit[0] is not None else (None, None)

    def out(self, phi, z, by):
        """A point by cells out from the surface, along the ray."""
        p, n = self.at(phi, z)
        if p is None:
            return None
        d = Vector((math.sin(phi), -math.cos(phi), 0.0))
        return p + d * by


# ---------------------------------------------------------------- pictures

def strand_picture(size=512, seed=7):
    """Hair strands for the cards, from numbers: dark brown with a little
    grey, each with its own shade and sway, tapering to its tip; RGB and the
    cut-out alpha. Row 0 is the tip, the root at the top."""
    rng = np.random.default_rng(seed)
    rgba = np.zeros((size, size, 4), np.float32)
    yy = np.arange(size)
    for _ in range(520):
        x0 = rng.uniform(0, size)
        root = rng.uniform(size * 0.85, size)
        tip = rng.uniform(0, size * 0.45)
        w = rng.uniform(0.9, 2.4)
        sway = rng.uniform(-6, 6)
        freq = rng.uniform(1.0, 2.5)
        shade = rng.uniform(0.55, 1.35)
        col = np.array([0.1, 0.06, 0.034]) * shade
        if rng.uniform() < 0.07:
            col = np.array([0.36, 0.33, 0.3]) * rng.uniform(0.7, 1.1)
        for y in range(int(tip), int(root)):
            t = max(0.0, (y - tip) / max(1.0, root - tip))
            x = x0 + sway * math.sin(t * math.pi * freq)
            half = w * (0.35 + 0.65 * t ** 0.5)
            x1, x2 = int(x - half - 1), int(x + half + 2)
            for xi in range(x1, x2):
                xm = xi % size
                a = max(0.0, 1.0 - abs(xi - x) / (half + 0.5)) * min(1.0, t * 4.0)
                if a > rgba[y, xm, 3]:
                    rgba[y, xm, :3] = col * (0.85 + 0.3 * a)
                    rgba[y, xm, 3] = a
    return uk.image("elsin_strands", rgba)


# ---------------------------------------------------------------- the head

def head(rig, M, assets):
    """The head piece's parts, in model space at rest: the face (its own
    object, for the atlas), the eyes, the hair and beard volumes, the strand
    cards, and the crown. Returns (atlas parts, card parts, face)."""
    hp = rig.pivot("Head")
    ob, eyes = uk.human_head(assets, EYE_Z, NECK, SCALE, cut_z=3.2, shape=stern)
    ob.data.materials.clear()
    ob.data.materials.append(M["skin"])
    dec = ob.modifiers.new("lighter", "DECIMATE")
    dec.ratio = 0.55
    uk.apply(ob)
    eyes = [(c + V((0.0, EYE_BACK * SCALE, 0.0)), r) for c, r in eyes]
    face = Face(ob, eyes)
    parts = [ob]
    for i, (c, r) in enumerate(eyes):
        e = ellipsoid_ob(c + V((0, 0.004, 0)), (r * 0.98, r * 0.98, r * 0.98), 12, 8, M["eye"], "eye")
        parts.append(e)
    hair = hair_volume(face, M)
    parts += hair + beard_volume(face, M)
    parts += crown(face, M, hair)
    cards = hair_cards(face, M) + beard_cards(face, M) + moustache_cards(face, M) + brow_cards(face, M) + top_cards(face, M, hair[1])
    return parts, cards, face


def ellipsoid_ob(c, r, nu, nv, mat, name):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=nu, v_segments=nv, radius=1.0)
    for v in bm.verts:
        v.co = Vector((c.x + v.co.x * r[0], c.y + v.co.y * r[1], c.z + v.co.z * r[2]))
    return uk.shade(uk.mesh(name, bm, [mat]), 80)


def hair_volume(face, M):
    """A solid shell of hair over the crown of the head and falling behind
    the ears to the shoulders, flaring a little as it falls."""
    bm = bmesh.new()
    rows = []
    zs = [lerp(face.top + 0.01, 3.27, k / 9) for k in range(10)]
    for j, z in enumerate(zs):
        row = []
        for i in range(17):
            phi = lerp(math.radians(-112), math.radians(112), i / 16) + math.pi
            # above the ears the hair hugs the skull, below them it falls
            p = face.out(phi, min(z, face.top - 0.01), 0.035 + 0.05 * max(0.0, (EYE_Z - z) / 0.33))
            if p is None:
                p = row[-1] if row else Vector((0, NECK[1] + 0.2, z))
            p = Vector((p.x * (1.0 + 0.12 * max(0.0, (EYE_Z - 0.05 - z) / 0.3)), p.y, z if z < face.top - 0.01 else p.z + (z - face.top + 0.01)))
            row.append(p)
        rows.append(row)
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    for r in range(len(rows) - 1):
        for c in range(len(rows[0]) - 1):
            bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][c + 1], vs[r][c + 1]))
    shell = uk.mesh("hairvolume", bm, [M["hair"]], outward=False)
    m = shell.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.03, 1.0
    uk.apply(shell)
    cap = ellipsoid_cap(face, M)
    return [uk.shade(shell, 60), cap]


def ellipsoid_cap(face, M):
    """The top of the head, covered: hair close over the skull from the
    hairline under the crown to the crown of the head."""
    bm = bmesh.new()
    rows = []
    n = 24
    for j in range(7):
        z = lerp(face.top - 0.004, EYE_Z + 0.1, (j / 6) ** 0.8)
        row = []
        for i in range(n):
            phi = 2 * math.pi * i / n
            p = face.out(phi, min(z, face.top - 0.012), 0.016 + 0.004 * j)
            if p is None:
                p = Vector((NECK[0], NECK[1], z))
            row.append(Vector((p.x, p.y, max(z, p.z))))
        rows.append(row)
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    for r in range(len(rows) - 1):
        for c in range(n):
            d = (c + 1) % n
            bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][d], vs[r][d]))
    top = bm.verts.new(Vector((NECK[0], NECK[1] + 0.02, face.top + 0.02)))
    for c in range(n):
        bm.faces.new((top, vs[0][c], vs[0][(c + 1) % n]))
    return uk.shade(uk.mesh("haircap", bm, [M["hair"]]), 70)


def beard_top(u):
    """The beard's top edge at u across the face (-1 to 1, 0 the middle):
    just under the nose, dipping at the corners of the mouth, then rising
    over the cheeks to the sideburns."""
    side = min(1.0, max(0.0, (abs(u) - 0.3) / 0.65))
    side = side * side * (3 - 2 * side)
    return lerp(EYE_Z - 0.128, EYE_Z - 0.035, side) - 0.022 * _g(abs(u) - 0.3, 0.12)


def beard_volume(face, M):
    """A full beard from the sideburns down the jaw to a broad, rounded chin,
    its top edge falling from the ears over the cheeks and running under
    the nose, so the moustache and the beard are one, standing off the
    face as hair does, full on the cheeks too."""
    parts = []
    bm = bmesh.new()
    rows = []
    nrow, ncol = 11, 21
    for r in range(nrow):
        s = r / (nrow - 1)
        row = []
        for c in range(ncol):
            u = lerp(-1.0, 1.0, c / (ncol - 1))
            phi = u * math.radians(98) * (1.0 - 0.45 * max(0.0, s - 0.45) / 0.55)
            side = min(1.0, max(0.0, (abs(u) - 0.3) / 0.65))
            side = side * side * (3 - 2 * side)
            top = beard_top(u) + 0.006 * math.sin(c * 2.3)
            bottom = lerp(face.chin - 0.1, face.chin + 0.02, abs(u) ** 1.7)
            z = lerp(top, bottom, s)
            thick = lerp(0.006, 0.045, min(1.0, s * 1.6)) * (1.0 - 0.1 * side)
            p = face.out(phi, max(z, face.chin + 0.01), thick)
            if p is None:
                p = row[-1] if row else Vector((0, -0.2, z))
            p = Vector((p.x, p.y, z))
            if z < face.chin:
                # below the chin it hangs in front of the throat
                p.y = min(p.y, face.nose * 0.55 + 0.015 * (face.chin - z) / 0.1)
            row.append(p)
        rows.append(row)
    vs = [[bm.verts.new(p) for p in row] for row in rows]
    for r in range(nrow - 1):
        for c in range(ncol - 1):
            bm.faces.new((vs[r][c], vs[r + 1][c], vs[r + 1][c + 1], vs[r][c + 1]))
    beard = uk.mesh("beardvolume", bm, [M["hair"]], outward=False)
    m = beard.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset = 0.03, -1.0
    uk.apply(beard)
    parts.append(uk.shade(beard, 60))
    return parts


def card(bm, uv, path, width, normal_hint):
    """A strand card down a path of points, width across, facing out along
    normal_hint; u across the strand picture, v from the root (1) to the tip."""
    n = len(path)
    vs = []
    for k, p in enumerate(path):
        a = path[max(0, k - 1)]
        b = path[min(n - 1, k + 1)]
        along = (b - a).normalized()
        across = along.cross(normal_hint).normalized()
        w = width * (1.0 - 0.45 * k / (n - 1))
        vs.append((bm.verts.new(p - across * w * 0.5), bm.verts.new(p + across * w * 0.5)))
    rng = np.random.default_rng(len(bm.verts))
    u0 = float(rng.uniform(0.0, 0.5))
    for k in range(n - 1):
        f = bm.faces.new((vs[k][0], vs[k + 1][0], vs[k + 1][1], vs[k][1]))
        for loop, (u, v) in zip(f.loops, ((u0, 1 - k / (n - 1)), (u0, 1 - (k + 1) / (n - 1)), (u0 + 0.5, 1 - (k + 1) / (n - 1)), (u0 + 0.5, 1 - k / (n - 1)))):
            loop[uv].uv = (u, v)


def hair_cards(face, M, count=56, seed=3):
    """Strand cards over the hair volume, from under the crown down to the
    shoulders, in two layers, each waving in its own way."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        layer = i % 2
        phi = math.pi + math.radians(rng.uniform(-108, 108))
        z0 = rng.uniform(EYE_Z + 0.06, face.top - 0.04)
        length = rng.uniform(0.32, 0.5)
        path = []
        for k in range(8):
            t = k / 7
            z = z0 - length * t
            wave = 0.014 * math.sin(t * 9.0 + i * 1.7) * t ** 0.5
            p = face.out(phi, max(min(z, face.top - 0.02), 3.3), 0.06 + 0.02 * layer + 0.06 * t * t + wave)
            if p is None:
                break
            sway = 0.03 * math.sin(t * 7.0 + i)
            p = Vector((p.x * (1.0 + 0.12 * t) + sway * math.cos(phi), p.y + sway * math.sin(phi), z))
            path.append(p)
        if len(path) < 3:
            continue
        hint = Vector((math.sin(phi), -math.cos(phi), 0))
        card(bm, uv, path, rng.uniform(0.07, 0.11), hint)
    ob = uk.mesh("haircards", bm, [M["strands"]], outward=False)
    return [uk.shade(ob, 80)]


def beard_cards(face, M, count=200, seed=5):
    """Strand cards over the beard, longer at the chin, for its soft edge,
    more of them near its top, lying close to the cheek there."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        u = rng.uniform(-1.0, 1.0)
        phi = u * math.radians(95)
        top = beard_top(u) - 0.01
        z0 = top - (top - face.chin - 0.06) * rng.uniform() ** 1.4
        lie = lerp(0.018, 0.05, min(1.0, 1.5 * (top - z0) / (top - face.chin)))
        length = rng.uniform(0.08, 0.13) * (1.4 if abs(u) < 0.45 else 1.15)
        path = []
        for k in range(4):
            t = k / 3
            z = z0 - length * t
            p = face.out(phi * (1.0 - 0.3 * t * (z < face.chin)), max(z, face.chin + 0.01), lie + 0.04 * t)
            if p is None:
                break
            p = Vector((p.x * (1.0 - 0.15 * t * (z < face.chin)), min(p.y, face.nose * 0.55) if z < face.chin else p.y, z))
            path.append(p)
        if len(path) < 3:
            continue
        hint = Vector((math.sin(phi), -math.cos(phi), 0))
        card(bm, uv, path, rng.uniform(0.04, 0.07), hint)
    ob = uk.mesh("beardcards", bm, [M["strands"]], outward=False)
    return [uk.shade(ob, 80)]


def moustache_cards(face, M):
    """The moustache: strands from under the nose sweeping down and out
    over the lip into the beard at the corners of the mouth, in layers."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rng = np.random.default_rng(13)
    for side in (-1, 1):
        for k in range(10):
            path = []
            start = 0.01 + 0.016 * k + rng.uniform(-0.01, 0.01)
            drop = rng.uniform(0.07, 0.095)
            for j in range(5):
                t = j / 4
                phi = side * lerp(start, 0.28 + 0.025 * k, t)
                z = EYE_Z - 0.13 - 0.003 * k - drop * t ** 1.3
                p = face.out(phi, z, 0.01 + 0.004 * (k % 3) + 0.012 * t)
                if p is not None:
                    path.append(p)
            if len(path) >= 3:
                card(bm, uv, path, rng.uniform(0.03, 0.042), Vector((0, -1, -0.2)).normalized())
    ob = uk.mesh("moustachecards", bm, [M["strands"]], outward=False)
    return [uk.shade(ob, 80)]


def top_cards(face, M, cap, count=56, seed=11):
    """Strands lying on the hair over the top of the head, combed back from
    the front of the crown."""
    rng = np.random.default_rng(seed)
    bm0 = bmesh.new()
    bm0.from_mesh(cap.data)
    bvh = BVHTree.FromBMesh(bm0)
    bm0.free()
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        x = rng.uniform(-0.22, 0.22)
        y0 = rng.uniform(-0.3, -0.02)
        path = []
        for j in range(6):
            t = j / 5
            ray_o = Vector((x * (1.0 - 0.25 * t), NECK[1] + y0 + 0.3 * t, face.top + 0.3))
            hit = bvh.ray_cast(ray_o, Vector((0, 0, -1)), 0.8)
            if hit[0] is None or hit[0].z < face.top - 0.14:
                if path:
                    break
                continue
            path.append(hit[0] + hit[1] * (0.006 + 0.003 * (i % 2)))
        if len(path) >= 3:
            card(bm, uv, path, rng.uniform(0.06, 0.09), Vector((0, 0, 1)))
    ob = uk.mesh("topcards", bm, [M["strands"]], outward=False)
    return [uk.shade(ob, 80)]


def brow_cards(face, M):
    """Two full brows on the brow ridge, a soft arch drawn a little down
    at the middle, a stern look."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for side in (-1, 1):
        for layer in range(2):
            path = []
            for k in range(6):
                t = k / 5
                phi = side * lerp(0.085, 0.5, t)
                z = EYE_Z + 0.028 + 0.014 * math.sin(math.pi * min(1.0, t * 1.25)) - 0.004 * t + 0.005 * (layer - 0.5)
                p = face.out(phi, z, 0.005 + 0.002 * layer)
                if p is not None:
                    path.append(p)
            if len(path) >= 3:
                card(bm, uv, path, 0.018, Vector((0, -1, 0.3)).normalized())
    ob = uk.mesh("browcards", bm, [M["strands"]], outward=False)
    return [uk.shade(ob, 80)]


# ---------------------------------------------------------------- the crown

def crown(face, M, hair):
    """A gilded crown on the brow: a tall band rolled at its foot and along
    its top, eight square merlons standing above it like a tower's, and a
    red stone in a gold bezel set in the band under each merlon, the one in
    front bigger. It sits on the hair, a smooth ring round the head."""
    n, merlons = 48, 8
    per = n // merlons
    z0 = face.top - 0.155
    z1, zt = z0 + 0.075, z0 + 0.14
    trees = []
    for ob in hair:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        trees.append(BVHTree.FromBMesh(bm))
        bm.free()
    raw = []
    for i in range(n):
        phi = 2 * math.pi * (i + 0.5) / n
        d = Vector((math.sin(phi), -math.cos(phi), 0.0))
        p = face.out(phi, z0 + 0.03, 0.045)
        r = (p.xy - Vector(NECK)).length if p is not None else 0.27
        for t in trees:
            for z in (z0, z0 + 0.03, z1):
                o = Vector((NECK[0], NECK[1], z))
                hit = t.ray_cast(o + d * 0.6, -d, 0.7)[0]
                if hit is not None:
                    r = max(r, (hit - o).length + 0.026)
        raw.append(r)
    # the ring is smoothed, then pushed out until it clears every point it was given
    f = np.fft.rfft(raw)
    f[5:] = 0
    ring_r = np.fft.irfft(f, n)
    ring_r += max(0.0, float(np.max(np.array(raw) - ring_r)))
    rad = [Vector((NECK[0] + math.sin(phi) * r, NECK[1] - math.cos(phi) * r, z0))
           for phi, r in ((2 * math.pi * (i + 0.5) / n, float(ring_r[i])) for i in range(n))]

    def at(i, z, grow=0.0):
        i0 = math.floor(i)
        p = rad[i0 % n].lerp(rad[(i0 + 1) % n], i - i0)
        d = Vector((p.x - NECK[0], p.y - NECK[1], 0)).normalized()
        return Vector((p.x, p.y, z)) + d * (grow + 0.1 * (z - z0))

    def merlon(i):
        return (i + 2) % per < 4

    bm = bmesh.new()
    foot = [bm.verts.new(at(i, z0 - 0.01, 0.012)) for i in range(n)]
    low = [bm.verts.new(at(i, z0)) for i in range(n)]
    mid = [bm.verts.new(at(i, z1)) for i in range(n)]
    hi = {i: bm.verts.new(at(i, zt)) for i in range(n) if merlon(i)}
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((foot[i], foot[j], low[j], low[i]))
        bm.faces.new((low[i], low[j], mid[j], mid[i]))
        if i in hi and j in hi:
            bm.faces.new((mid[i], mid[j], hi[j], hi[i]))
    band = uk.mesh("crownband", bm, [M["gold"]], outward=False)
    m = band.modifiers.new("solid", "SOLIDIFY")
    m.thickness, m.offset, m.use_even_offset = 0.022, -1.0, True
    uk.apply(band)
    parts = [uk.shade(band, 35)]

    def tube(pts, centre, across, r, sides, name):
        """A closed roll through pts, its section square to the line from centre and across."""
        bm = bmesh.new()
        rings = [uk.ring(p, (p - centre - across * (p - centre).dot(across)).normalized(), across, r, r, sides, start=math.pi / sides)
                 for p in pts + [pts[0]]]
        uk.loft(bm, rings)
        return uk.shade(uk.mesh(name, bm, [M["gold"]]), 50)

    up = Vector((0.0, 0.0, 1.0))
    for z, g, r, name in ((z0 + 0.002, 0.006, 0.009, "crownfoot"), (z1 - 0.006, 0.004, 0.008, "crownroll")):
        parts.append(tube([at(i, z, g) for i in range(0, n, 2)], Vector((NECK[0], NECK[1], z)), up, r, 6, name))
    for k in range(merlons):
        c = at(per * k - 0.5, (z0 + z1) * 0.5 + 0.002)
        w, h = (0.022, 0.026) if k == 0 else (0.015, 0.02)
        out = Vector((c.x - NECK[0], c.y - NECK[1], 0)).normalized()
        side = Vector((-out.y, out.x, 0.0))
        up = Vector((0.0, 0.0, 1.0))
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=1.0)
        for v in bm.verts:
            x, y, z = v.co
            v.co = c + out * 0.004 + side * (x * w) - out * (y * 0.6 * w) + up * (z * h)
        parts.append(uk.shade(uk.mesh("stone", bm, [M["gem"]]), 80))
        ring_ = [c + out * 0.004 + side * (math.cos(t) * (w + 0.003)) + up * (math.sin(t) * (h + 0.003))
                 for t in (2 * math.pi * q / 8 for q in range(8))]
        parts.append(tube(ring_, c + out * 0.004, out, 0.0045, 3, "bezel"))
    return parts
