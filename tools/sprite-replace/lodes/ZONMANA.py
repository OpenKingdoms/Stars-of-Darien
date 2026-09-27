"""ZONMANA, the Zhon divine lodestone: a pedestal of twisted root strands
rising to a knot of root that holds a glowing navy orb half sunk in a
socket, with root fingers gripping it and gilded vines curling round (an
arch over the orb with a leaf tuft, a spiral on the left and a vine wound
round the neck that hangs in a curl on the right).

    blender -b --factory-startup --python ZONMANA.py
"""
import math
import os
import random
import sys

import bmesh
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zhonkit as zk  # noqa: E402
from zhonkit import hk, lin, rgb, mix, smoothstep, sstep  # noqa: E402

NAME = "ZONMANA"
SPRITE = r"D:\OKReplace\lodes\sprites\ZONMANA.png"
HOT = (24, 74)
OUT = r"D:\OKReplace\lodes\hand"
VIEW = zk.VIEW


def at(px, row, y):
    return zk.at(px, row, y, HOT)


def screen(p):
    return zk.screen(p, HOT)


def on_plane(centre, dx, dup, lean=40.0):
    """A point on a plane leaning back by lean degrees through centre, placed
    by its screen offset in picture pixels (dup is upward)."""
    L = math.radians(lean)
    up = Vector((0, math.sin(L), math.cos(L)))
    k = zk.SY * math.sin(L) + zk.SZ * math.cos(L)
    return centre + Vector((dx / 16.0, 0, 0)) + up * (dup / k)


hk.reset()

# ---- the root knot: three lumpy ellipsoids ---------------------------------


class Blob:
    """A lumpy ellipsoid tilted about Y, with the UV rectangle it paints into."""

    def __init__(self, c, r, tilt, amp, seed, seg, rings, rect, hug=False):
        self.hug = hug
        rng = random.Random(seed)
        self.c, self.r, self.tilt, self.amp = np.array(c), np.array(r), tilt, amp
        self.ph = [rng.uniform(0, 6.28) for _ in range(3)]
        self.seg, self.rings, self.rect = seg, rings, rect

    def lump(self, a, t):
        return (np.sin(3 * a + 2 * t + self.ph[0]) + 0.7 * np.sin(5 * a - 3 * t + self.ph[1])
                + 0.5 * np.sin(2 * a + 5 * t + self.ph[2]))

    def point(self, a, t):
        a, t = np.broadcast_arrays(np.asarray(a, dtype=float), np.asarray(t, dtype=float))
        k = 1 + self.amp * self.lump(a, t)
        x = self.r[0] * np.sin(t) * np.cos(a) * k
        y = self.r[1] * np.sin(t) * np.sin(a) * k
        z = self.r[2] * np.cos(t) * k
        ct, st = math.cos(self.tilt), math.sin(self.tilt)
        return np.stack([x * ct + z * st + self.c[0], y + self.c[1], -x * st + z * ct + self.c[2]], axis=-1)

    def normal(self, a, t):
        a, t = np.broadcast_arrays(np.asarray(a, dtype=float), np.asarray(t, dtype=float))
        x = np.sin(t) * np.cos(a) / self.r[0]
        y = np.sin(t) * np.sin(a) / self.r[1]
        z = np.cos(t) / self.r[2]
        ct, st = math.cos(self.tilt), math.sin(self.tilt)
        n = np.stack([x * ct + z * st, y, -x * st + z * ct], axis=-1)
        return n / np.linalg.norm(n, axis=-1, keepdims=True)

    def inside(self, P):
        """Implicit value of the plain ellipsoid, below 1 inside."""
        d = P - self.c
        ct, st = math.cos(-self.tilt), math.sin(-self.tilt)
        x = d[..., 0] * ct + d[..., 2] * st
        z = -d[..., 0] * st + d[..., 2] * ct
        return np.sqrt((x / self.r[0]) ** 2 + (d[..., 1] / self.r[1]) ** 2 + (z / self.r[2]) ** 2)

    def uv(self, i, j):
        u0, v0, u1, v1 = self.rect
        return (u0 + (u1 - u0) * i / self.seg, v0 + (v1 - v0) * j / self.rings)

    def build(self, name, mat, deform):
        seg, rings = self.seg, self.rings
        A = np.array([2 * math.pi * i / seg for i in range(seg)])
        P = []
        for j in range(1, rings):
            t = math.pi * (1 - j / rings)
            P.append([Vector(p) for p in deform(self.point(A, t), self.hug)])
        top = Vector(deform(self.point(np.array([0.0]), 0.0), self.hug)[0])
        bot = Vector(deform(self.point(np.array([0.0]), math.pi), self.hug)[0])
        bm = bmesh.new()
        zk.grid(bm, P, uv=lambda j, i: self.uv(i, j + 1), top=top, bottom=bot)
        zk.fix_normals(bm)
        return zk.smooth(zk.make(name, bm, [mat]), 70)


KNOT = Blob((0.3, 0.02, 5.5), (1.0, 0.74, 0.8), math.radians(24), 0.045, 5, 26, 16, (0.5, 0.5, 1.0, 1.0))
CHEEK = Blob((0.8, -0.05, 5.1), (0.38, 0.5, 0.4), -0.2, 0.05, 7, 14, 9, (0.75, 0.25, 1.0, 0.5))
BLOBS = [KNOT, CHEEK]

# ---- the orb and its socket --------------------------------------------------

ORB_R = 0.47
ORB_AT = (28.0, 33.5)     # the orb's centre in the picture
RS = ORB_R + 0.025        # socket radius
RIM = 0.34                # how far round the socket the knot is drawn in to its rim


def ray_hit(px, row, ped=False):
    """Where the camera ray through a picture pixel first meets the knot
    (or, with ped, the pedestal)."""
    p = Vector(at(px, row, -3.0))
    for _ in range(4000):
        if min(b.inside(np.array(p)) for b in BLOBS) <= 1.0:
            return p
        if ped and 0 < p.z < 5.2:
            x = p.x - float(pcx(p.z))
            if math.hypot(x, p.y) < float(ped_r(math.atan2(p.y, x), p.z)):
                return p
        p += VIEW * 0.002
    return p


def seat_orb():
    """The orb's centre sits on the knot's surface under the picture's orb,
    its socket opening between the surface normal and the classic camera,
    tipped toward the lower left where the picture shows the lit rim."""
    tx, ty = ORB_AT
    for _ in range(8):
        s = ray_hit(tx, ty)
        d = np.array(s) - KNOT.c
        ct, st = math.cos(-KNOT.tilt), math.sin(-KNOT.tilt)
        lx, lz = d[0] * ct + d[2] * st, -d[0] * st + d[2] * ct
        n = np.array([lx / KNOT.r[0] ** 2, d[1] / KNOT.r[1] ** 2, lz / KNOT.r[2] ** 2])
        n = Vector((n[0] * math.cos(KNOT.tilt) + n[2] * math.sin(KNOT.tilt), n[1],
                    -n[0] * math.sin(KNOT.tilt) + n[2] * math.cos(KNOT.tilt))).normalized()
        lowleft = (-Vector((1, 0, 0)) - zk.SCREEN_UP).normalized()
        ax = (n * 0.45 + (-VIEW) * 0.55 + lowleft * 0.3).normalized()
        c = s + ax * 0.03
        sx, sy = screen(c)
        tx += ORB_AT[0] - sx
        ty += ORB_AT[1] - sy
    return s, ax, c


SEAT, AX, ORB_C = seat_orb()
_AXn, _Cn = np.array(AX), np.array(ORB_C)


def hood_blob():
    """The lump over the orb's upper right: slid along the classic view (so
    the picture's outline keeps) until it overhangs the orb as a brow."""
    h0 = Vector((0.74, 0.3, 5.85))
    t = (ORB_C - h0).dot(-VIEW)
    c = h0 - VIEW * t * 0.95
    return Blob(tuple(c), (0.42, 0.42, 0.42), 0.3, 0.05, 6, 16, 10, (0.5, 0.0, 0.75, 0.5), hug=True)


def chin_blob():
    """A fold of root below the slit: the picture's lower lip."""
    q = ray_hit(22.5, 47.8, ped=True)
    return Blob(tuple(q - AX * 0.2 + Vector((0, 0, 0.06))), (0.5, 0.32, 0.22), math.radians(-8), 0.06, 8, 14, 8,
                (0.75, 0.0, 1.0, 0.25))


HOOD = hood_blob()
BLOBS.append(HOOD)
SLIT = RS + 0.24          # the dark slit under the orb


def socket_frame():
    e1 = (Vector((1, 0, 0)) - AX * AX.x).normalized()
    e2 = AX.cross(e1)  # screen-up round the socket
    return e1, e2


_E1, _E2 = (np.array(v) for v in socket_frame())


def deform(P, hug=False):
    """Hollows a socket for the orb out of the knot: surface in front of it
    is pushed back into a bowl behind it, and the ring round it is drawn
    in to the orb's equator, where the lip sits. With hug the surface only
    moves out of the orb, so the lump overhangs it."""
    P = np.array(P, dtype=float)
    d = P - _Cn
    if hug:
        dist = np.linalg.norm(d, axis=-1)
        inn = dist < RS + 0.01
        out = P.copy()
        out[inn] = (_Cn + d / np.maximum(dist, 1e-6)[..., None] * (RS + 0.01))[inn]
        return out
    h = d @ _AXn
    radial = d - h[..., None] * _AXn
    rho = np.linalg.norm(radial, axis=-1)
    bowl = -np.sqrt(np.maximum(RS ** 2 - rho ** 2, 0.0)) - 0.005
    inside = (rho < RS) & (h > bowl)
    out = P.copy()
    out[inside] = (_Cn + radial + bowl[..., None] * _AXn)[inside]
    w = (1 - smoothstep(RS, RS + RIM, rho)) * (~inside)
    ht = 0.015
    out += (w * (ht - h))[..., None] * _AXn
    # the slit under the orb, between its lip and the chin
    ang = np.arctan2(d @ _E2, d @ _E1)
    low = np.maximum(0.0, np.cos(ang - math.radians(265))) ** 1.5
    out -= (0.08 * np.exp(-((rho - SLIT) / 0.06) ** 2) * low * (~inside))[..., None] * _AXn
    return out


def lip(mat):
    """The raised rim of root round the orb's left and lower edge, lit, that
    thins away into the knot at both ends."""
    e1, e2 = socket_frame()
    a0, a1, n = 95.0, 345.0, 22
    pts = []
    for k in range(n + 1):
        t = math.radians(a0 + (a1 - a0) * k / n)
        pts.append(ORB_C + (e1 * math.cos(t) + e2 * math.sin(t)) * (RS + 0.04) + AX * 0.012)

    def radius(s):
        a = math.radians(a0 + (a1 - a0) * s)
        lowleft = max(0.0, math.cos(a - math.radians(225)))
        return (0.105 * (0.6 + 0.4 * lowleft)) * math.sin(math.pi * s) ** 0.6 + 0.004

    def bumps(s, a):
        return 0.08 * math.sin(11 * 2 * math.pi * s + 2 * a) + 0.05 * math.sin(23 * 2 * math.pi * s)
    bm = bmesh.new()
    zk.sweep(bm, pts, radius, seg=6, up=tuple(AX), bumps=bumps)
    return zk.smooth(zk.make("lip", bm, [mat]), 60)


def on_orb(ang, lat, lift=0.0):
    """A point on the orb's surface: ang round the socket (0 is screen
    right, 90 up), lat up from its equator toward the camera."""
    e1, e2 = socket_frame()
    t, ph = math.radians(ang), math.radians(lat)
    return ORB_C + ((e1 * math.cos(t) + e2 * math.sin(t)) * math.cos(ph) + AX * math.sin(ph)) * (ORB_R + lift)


def fingers(mat):
    """Short root claws from the rim over the orb's edge, curling as they
    grip it (the picture's hot spot stays clear)."""
    parts = []
    rng = random.Random(9)
    for ang, sweep_, reach, r0 in ((4, -26, 44, 0.12), (52, -20, 50, 0.13), (140, 26, 36, 0.1)):
        ctrl = [on_orb(ang, -25, 0.14), on_orb(ang, -4, 0.07)]
        for k in range(1, 5):
            f = k / 4
            ctrl.append(on_orb(ang + sweep_ * f, reach * f, r0 * 0.4 * (1 - 0.6 * f)))
        pts = zk.resample(zk.spline(ctrl, 4), 0.04)
        nodes = [rng.uniform(0.15, 0.85) for _ in range(3)]

        def bumps(s, a, nodes=nodes):
            return sum(0.2 * math.exp(-((s - q) / 0.05) ** 2) for q in nodes) + 0.05 * math.sin(3 * a + 9 * s)

        def radius(s, r0=r0):
            end = math.sqrt(max(0.0, 1 - (max(0.0, s - 0.84) / 0.16) ** 2))  # a rounded fingertip
            return r0 * (1 - 0.45 * s) * end + 0.002
        bm = bmesh.new()
        zk.sweep(bm, pts, radius, seg=6, flat=0.75, up=tuple(AX), bumps=bumps)
        parts.append(zk.smooth(zk.make("finger", bm, [mat]), 60))
    return parts


def orb_parts():
    """A dark navy glass shell over a white-hot core, so it glows from any side."""
    shell = hk.pbr("zm_orb_glass", lin((14, 18, 34)), rough=0.05, emit=lin((16, 26, 56)), strength=0.35)
    b = shell.node_tree.nodes["Principled BSDF"]
    b.inputs["Alpha"].default_value = 0.84
    b.inputs["IOR"].default_value = 1.5
    shell.surface_render_method = "BLENDED"
    shell.use_backface_culling = True
    core = hk.pbr("zm_orb_core", lin((40, 42, 48)), rough=0.9, emit=lin((232, 238, 252)), strength=9.0)
    halo = hk.pbr("zm_orb_halo", lin((20, 26, 44)), rough=0.9, emit=lin((168, 188, 236)), strength=1.8)
    hb = halo.node_tree.nodes["Principled BSDF"]
    hb.inputs["Alpha"].default_value = 0.32
    halo.surface_render_method = "BLENDED"
    halo.use_backface_culling = True
    # the hot spot sits a little left of centre and toward the camera, as in the picture
    off = Vector((-1, 0, 0)) * 0.16 - zk.SCREEN_UP * 0.05 + (-VIEW) * 0.12

    def sphere(name, r, mat, u, v, c):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
        for q in bm.verts:
            q.co += c
        return zk.smooth(zk.make(name, bm, [mat]), 80)
    def wisp(name, r, mat, c, jit, seed, u=14, v=8):
        """A soft, lumpy ball of light, so the glow never reads as a pupil."""
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
        nz = zk.Noise(seed)
        for q in bm.verts:
            k = 1 + jit * 2 * (float(nz.value(q.co.x / r * 1.3 + 5, q.co.y / r * 1.3, q.co.z / r * 1.3)) - 0.5)
            q.co = q.co * k + c
        return zk.smooth(zk.make(name, bm, [mat]), 80)
    return [wisp("core", ORB_R * 0.29, core, ORB_C + off, 0.25, 4, 12, 7),
            wisp("halo", ORB_R * 0.5, halo, ORB_C + off * 0.45 + Vector((0.02, 0, 0.03)), 0.35, 5),
            sphere("orb", ORB_R, shell, 22, 11, ORB_C)]


# ---- the pedestal -------------------------------------------------------------

PROF = [(0.0, 0.74), (0.12, 0.76), (0.3, 0.74), (0.55, 0.7), (0.85, 0.64), (1.15, 0.58), (1.45, 0.54),
        (1.7, 0.525), (1.95, 0.515), (2.2, 0.51), (2.45, 0.51), (2.7, 0.51), (2.95, 0.515), (3.2, 0.53),
        (3.45, 0.545), (3.75, 0.56), (4.1, 0.55), (4.45, 0.58), (4.8, 0.55), (5.1, 0.42), (5.2, 0.0)]
PZ, PRr = np.array([p[0] for p in PROF]), np.array([p[1] for p in PROF])
# the foot's outline read off the picture's bottom edge: (degrees, scale)
FOOT = [(0, 1.05), (45, 0.95), (90, 0.95), (135, 1.0), (180, 1.08), (230, 0.8), (262, 0.7), (306, 1.36),
        (340, 1.2), (360, 1.05)]
FD, FS = np.array([f[0] for f in FOOT], dtype=float), np.array([f[1] for f in FOOT])
# root flares round the back and sides: (degrees, extra scale, height)
FLARES = [(72, 0.55, 1.2), (144, 0.34, 0.72), (216, 0.2, 0.6), (0, 0.12, 0.6)]
PSEG = 24
TWIST = 0.9


def pcx(z):
    return 0.3 * smoothstep(0.2, 1.8, z) - 0.05 * smoothstep(3.4, 4.8, z)


def strands(a, z):
    """Twisted root strands: rounded ridges with narrow grooves between."""
    g = 0.5 + 0.5 * np.cos(5 * a + TWIST * z)
    return g, 0.19 * (g ** 0.6 - 0.62) + 0.03 * np.sin(11 * a - 1.4 * z + 1.0)


def pmod(a, z):
    a, z = np.broadcast_arrays(np.asarray(a, dtype=float), np.asarray(z, dtype=float))
    deg = np.degrees(a) % 360
    t = np.interp(deg, FD, np.arange(len(FD)))
    i0 = np.clip(np.floor(t).astype(int), 0, len(FD) - 2)
    f = t - i0
    foot = FS[i0] + (FS[i0 + 1] - FS[i0]) * (1 - np.cos(math.pi * f)) / 2
    foot_w = np.clip(1 - z / 1.1, 0, 1) ** 1.6
    m = 1 + (foot - 1) * foot_w
    for d0, amp, hgt in FLARES:
        # each flare is the foot of a strand, so it follows the strand's twist
        da = (a - math.radians(d0) + TWIST * z / 5 + math.pi) % (2 * math.pi) - math.pi
        m = m + amp * np.exp(-(da / 0.3) ** 2) * np.clip(1 - z / hgt, 0, 1) ** 1.6
    bulge = 0.45 * np.maximum(0.0, np.cos(a - 0.35)) ** 3 * smoothstep(1.5, 2.3, z) * (1 - smoothstep(3.3, 4.0, z))
    _, s = strands(a, z)
    return m + bulge + s * (1 - 0.6 * smoothstep(4.5, 5.1, z)) + 0.03 * np.sin(3 * a + 4 * z)


def ped_r(a, z):
    return np.interp(z, PZ, PRr) * pmod(a, z)


def ped_point(a, z):
    r = ped_r(a, z)
    return np.stack(np.broadcast_arrays(pcx(z) + r * np.cos(a), r * np.sin(a), z), axis=-1)


def pedestal(mat):
    zs = list(np.concatenate([[0.0, 0.08, 0.2, 0.36, 0.55, 0.78], np.linspace(1.0, 5.1, 18)]))
    A = np.array([2 * math.pi * i / PSEG for i in range(PSEG)])
    P = [[Vector(p) for p in deform(ped_point(A, z))] for z in zs]
    bm = bmesh.new()
    zk.grid(bm, P, uv=lambda j, i: (0.5 * i / PSEG, zs[min(j, len(zs) - 1)] / 5.2),
            top=Vector((float(pcx(5.2)), 0.0, 5.2)))
    zk.fix_normals(bm)
    return zk.smooth(zk.make("pedestal", bm, [mat]), 55)


CHIN = chin_blob()
BLOBS.append(CHIN)

# ---- the root texture ---------------------------------------------------------

N1, N2 = zk.Noise(21), zk.Noise(22)
C_DARK, C_MID, C_LIGHT = rgb((27, 23, 11)), rgb((68, 57, 30)), rgb((116, 100, 56))
RUSTC = rgb((138, 88, 60))


def root_colour(P, nz, extra_dark=0.0, zc=1.0):
    """Mottled olive-brown root: fbm patches, fine grain and a little
    lightening on faces that look up."""
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    m = N1.fbm(x * 3.2, y * 3.2, z * 3.2, 4)
    f = N2.fbm(x * 16, y * 16, z * 16, 3)
    c = mix(C_DARK, C_MID, smoothstep(0.25, 0.55, m))
    c = mix(c, C_LIGHT, smoothstep(0.55, 0.8, m) * 0.8)
    c = c * (0.8 + 0.4 * f)[..., None]
    c = c * (0.85 + 0.3 * np.clip(nz, 0, 1))[..., None]
    # a fine network of creases in the bark
    cr = 1 - smoothstep(0.0, 0.045, np.abs(N1.fbm(x * 6.5 + 3, y * 6.5, z * 6.5 * zc, 3) - 0.5))
    c = c * (1 - 0.55 * cr)[..., None]
    return c * (1 - np.asarray(extra_dark, dtype=float))[..., None], m, f, cr


def paint_pedestal(W=256, H=512):
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    a, z = 2 * math.pi * U, V * 5.2
    P = ped_point(a, z)
    g, _ = strands(a, z)
    c, m, f, cr = root_colour(P, np.zeros_like(z) + 0.4, zc=0.3)
    # grooves between the strands go dark, the ridge tops catch the light
    c = c * (0.5 + 0.6 * smoothstep(0.0, 0.25, g))[..., None]
    c = mix(c, C_LIGHT, smoothstep(0.75, 1.0, g) * 0.4 * smoothstep(0.4, 0.6, f))
    # fine rust mottling on the worn ridge tops of the lower trunk, most on its front
    low = 1 - smoothstep(1.5, 3.8, z)
    front = 0.35 + 0.65 * np.maximum(0.0, -np.sin(a + 0.4))
    speck = N2.fbm(P[..., 0] * 22, P[..., 1] * 22, P[..., 2] * 22 + 9, 3)
    rust = smoothstep(0.55, 0.9, g) * low * front * smoothstep(0.44, 0.6, speck)
    c = mix(c, RUSTC * (0.8 + 0.4 * f)[..., None], rust * 0.9)
    c = c * (0.7 + 0.3 * smoothstep(0.0, 0.35, z))[..., None]
    h = 0.02 * g ** 0.6 + 0.006 * f + 0.004 * m - 0.008 * cr
    return c, h


def blob_masks(b, P):
    """Crevices where one lump grows out of another go dark."""
    crev = np.zeros(P.shape[:-1])
    for o in BLOBS:
        if o is b:
            continue
        s = o.inside(P)
        crev = np.maximum(crev, np.exp(-((s - 1.0) / 0.12) ** 2) * 0.55)
    return crev


def paint_blob(b, W, H):
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    a, t = 2 * math.pi * U, math.pi * (1 - V)
    P0 = b.point(a, t)
    P = deform(P0, b.hug)
    nz = b.normal(a, t)[..., 2]
    crev = blob_masks(b, P0)
    c, m, f, cr = root_colour(P, nz, crev)
    # the socket: dark bowl, a lit bronze-olive rim, the slit under the orb
    d = P - _Cn
    hh = d @ _AXn
    rho = np.linalg.norm(d - hh[..., None] * _AXn, axis=-1)
    bowl = rho < RS + 0.01
    c = np.where(bowl[..., None], c * 0.25, c)
    e1, e2 = (np.array(v) for v in socket_frame())
    ang = np.arctan2(d @ e2, d @ e1)
    lowleft = np.maximum(0.0, np.cos(ang - math.radians(215))) ** 1.2
    rimz = np.exp(-((rho - RS - 0.12) / 0.09) ** 2) * lowleft * (~bowl)
    c = mix(c, rgb((150, 124, 86)) * (0.85 + 0.3 * f)[..., None], rimz * 0.7)
    slit = np.exp(-((rho - SLIT) / 0.06) ** 2) * np.maximum(0.0, np.cos(ang - math.radians(262))) ** 2
    c = c * (1 - 0.85 * slit)[..., None]
    h = 0.006 * f + 0.008 * m - 0.02 * slit - 0.01 * cr
    return c, h


def root_material():
    W, H = 512, 512
    col = np.zeros((H, W, 3))
    hgt = np.zeros((H, W))
    c, h = paint_pedestal(256, 512)
    col[:, :256], hgt[:, :256] = c, h
    for b in BLOBS:
        u0, v0, u1, v1 = b.rect
        x0, x1, y0, y1 = int(u0 * W), int(u1 * W), int(v0 * H), int(v1 * H)
        c, h = paint_blob(b, x1 - x0, y1 - y0)
        col[y0:y1, x0:x1], hgt[y0:y1, x0:x1] = c, h
    img = zk.image("zm_root_col", col)
    nrm = zk.normal_image("zm_root_nrm", hgt, 2 * math.pi * 0.55 / 256, 5.2 / 512, 1.0, wrap_u=False)
    return zk.tex_mat("zm_root", img, rough=0.85, nimg=nrm, nstrength=1.2)


# ---- vines --------------------------------------------------------------------

VINE = hk.pbr("zm_vine", lin((216, 182, 118)), rough=0.33, metal=0.5)
LEAF = hk.pbr("zm_leaf", lin((84, 92, 50)), rough=0.7)


def knobbly(seed, count=6, amp=0.16, width=0.018):
    rng = random.Random(seed)
    nodes = [rng.uniform(0.05, 0.95) for _ in range(count)]

    def f(s, a):
        return (sum(amp * math.exp(-((s - q) / width) ** 2) for q in nodes)
                + 0.06 * math.sin(2 * a + 40 * s) + 0.04 * math.sin(5 * a - 23 * s))
    return f


def vine(ctrl, radius, n=5, seg=5, name="vine", seed=1, step=0.09):
    pts = zk.spline(ctrl, n)
    if step:
        pts = zk.resample(pts, step)
    bm = bmesh.new()
    zk.sweep(bm, pts, radius, seg=seg, bumps=knobbly(seed))
    return zk.smooth(zk.make(name, bm, [VINE]), 60)


def taper(r0, r1, tip=0.04, root=0.0):
    """Radius from r0 to r1 along the vine, closing to a point over the last
    tip (and the first root, when it grows out of a point too)."""
    def f(s):
        k = min(1.0, (1 - s) / tip) ** 0.5
        if root:
            k *= min(1.0, s / root) ** 0.5
        return (r0 + (r1 - r0) * s ** 1.2) * k + 0.001
    return f


def arch():
    """The handle over the orb: out of the knot low on the left, up its
    left side, over the top and down into the knot behind."""
    ctrl = [at(18.5, 49.0, 0.05), at(15.6, 47.6, -0.12), at(14, 45.5, -0.25), at(12.3, 40, -0.25),
            at(12.4, 34, -0.15), at(13.4, 29, -0.1), at(15.5, 24.5, 0.0), at(18.5, 21, 0.15),
            at(22.5, 18.3, 0.3), at(27, 16.9, 0.45), at(31, 17.2, 0.6), at(33.4, 19.6, 0.72), at(33.5, 23, 0.7)]
    return vine(ctrl, taper(0.12, 0.075, 0.02, root=0.06), n=5, name="arch", seed=3)


def sprig():
    """A twig off the arch with a tuft of leaves, top left."""
    base = at(19.5, 21.5, 0.15)
    tip = at(14.3, 18.8, 0.5)
    parts = [vine([base, (base + tip) / 2 + Vector((0.0, 0, 0.06)), tip], taper(0.045, 0.02), n=4, seg=5,
                  name="twig", seed=4)]
    bm = bmesh.new()
    for (px, row, ln, w) in ((9.6, 15.8, 0.3, 0.14), (10.2, 19.6, 0.27, 0.13), (13.4, 15.2, 0.26, 0.12),
                             (16.6, 16.4, 0.22, 0.11), (12.6, 21.2, 0.2, 0.1)):
        goal = on_plane(tip, px - 14.3, 18.8 - row, lean=55.0)
        a = tip + (goal - tip) * 0.15
        d = (goal - a).normalized()
        side = d.cross(-VIEW).normalized()
        nrm = side.cross(d).normalized()
        n = 5
        left, right = [], []
        for k in range(n + 1):
            t = k / n
            w_ = w * math.sin(math.pi * t) ** 0.8
            c = a + d * ln * t + nrm * 0.05 * math.sin(math.pi * t)
            left.append(bm.verts.new(c + side * w_ / 2))
            right.append(bm.verts.new(c - side * w_ / 2) if 0 < k < n else left[-1])
        for k in range(n):
            vs = [left[k], left[k + 1], right[k + 1], right[k]]
            uniq = []
            for v in vs:
                if v not in uniq:
                    uniq.append(v)
            if len(uniq) >= 3:
                bm.faces.new(uniq)
    parts.append(hk.smooth(zk.make("leaves", bm, [LEAF]), 70))
    return parts


def spiral():
    """The curl off the knot's left side, on a plane leaning back to face
    the classic camera (points are picture pixels)."""
    c0 = (4.6, 38.4)
    c = at(c0[0], c0[1], -0.3)
    path = [(8.2, 34.4), (4.9, 33.3), (2.2, 34.2), (1.2, 37.0), (1.4, 40.0), (2.9, 42.5), (5.3, 43.4),
            (7.1, 42.0), (7.3, 40.0), (6.2, 39.0), (5.2, 39.9)]
    ctrl = [at(14.5, 38.5, 0.05), at(12.5, 38, -0.15), at(10.5, 35.8, -0.25)]
    ctrl += [on_plane(c, px - c0[0], c0[1] - row) for px, row in path]
    return vine(ctrl, taper(0.1, 0.05, 0.03, root=0.04), n=4, name="spiral", seed=5)


def neck_ring_and_curl():
    """A vine wound once round the neck, hugging its strands, that leaves on
    the right and hangs in a curl (curl points are picture pixels)."""
    ctrl = []
    angs = list(range(120, 346, 15))
    for k, d in enumerate(angs):
        t = k / (len(angs) - 1)
        a = math.radians(d)
        z = 3.62 + 0.42 * t ** 1.2
        r = float(ped_r(a, z)) + 0.07
        ctrl.append(Vector((float(pcx(z)) + r * math.cos(a), r * math.sin(a), z)))
    c0 = (41.0, 58.0)
    c = at(c0[0], c0[1], -0.35)
    path = [(39.2, 49.8), (43.2, 49.9), (45.5, 52.6), (45.9, 57.0), (45.4, 61.0), (43.1, 63.6), (40.5, 65.0),
            (38.0, 65.4), (36.7, 64.3)]
    ctrl += [on_plane(c, px - c0[0], c0[1] - row) for px, row in path]

    def radius(s):
        # wound thin round the neck, thick where it leaves, fine at both tips
        r = 0.08 + 0.05 * sstep(0.3, 0.5, s) * (1 - sstep(0.55, 1.0, s)) - 0.03 * sstep(0.7, 1.0, s)
        return r * min(1.0, (1 - s) / 0.03) ** 0.5 * min(1.0, s / 0.08) ** 0.6 + 0.001
    return vine(ctrl, radius, n=4, name="curl", seed=6)


# ---- build --------------------------------------------------------------------

ROOT = root_material()
FINGER = hk.pbr("zm_finger", lin((72, 60, 39)), rough=0.75)
RIMM = hk.pbr("zm_rim", lin((122, 102, 66)), rough=0.55, metal=0.2)

parts = [pedestal(ROOT), KNOT.build("knot", ROOT, deform), HOOD.build("hood", ROOT, deform),
         CHEEK.build("cheek", ROOT, deform), CHIN.build("chin", ROOT, deform)]
parts += orb_parts()
parts += [lip(RIMM)] + fingers(FINGER)
parts += [arch(), spiral(), neck_ring_and_curl()]
parts += sprig()
for p in parts:
    print("PART", p.name, zk.tris(p))

ob = hk.finish(parts, os.path.join(OUT, "models", NAME + ".glb"),
               {"replacesTexture": "Zonmanalodestone", "replacesPiece": "zonmana"})
print("TRIS", zk.tris(ob))
print("ORB", tuple(round(v, 3) for v in ORB_C), "AXIS", tuple(round(v, 3) for v in AX),
      "SEAT", tuple(round(v, 3) for v in SEAT))
hk.renders(ob, os.path.join(OUT, "renders"), NAME, SPRITE, HOT, scale=4)
