"""ZONMANA, the Zhon divine lodestone: a pedestal of twisted root strands
rising to one rounded head of root, its strands carried up over it, that
holds a glowing blue glass orb half sunk in a socket under a domed brow, with vines curling round (an arch over
the orb with a leafy sprig, a spiral on the left and a vine wound round the
neck in a groove that hangs in a curl on the right).

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

# ---- the head's parts: ellipsoids that are blended into one form -------------


class Blob:
    """An ellipsoid tilted about Y."""

    def __init__(self, c, r, tilt):
        self.c, self.r, self.tilt = np.array(c, dtype=float), np.array(r, dtype=float), tilt

    def inside(self, P):
        """Implicit value, below 1 inside."""
        d = P - self.c
        ct, st = math.cos(-self.tilt), math.sin(-self.tilt)
        x = d[..., 0] * ct + d[..., 2] * st
        z = -d[..., 0] * st + d[..., 2] * ct
        return np.sqrt((x / self.r[0]) ** 2 + (d[..., 1] / self.r[1]) ** 2 + (z / self.r[2]) ** 2)

    def sdf(self, P):
        """Roughly the distance to the surface, in cells."""
        return (self.inside(P) - 1.0) * float(np.prod(self.r)) ** (1 / 3)


KNOT = Blob((0.3, 0.02, 5.5), (1.0, 0.74, 0.8), math.radians(24))
CHEEK = Blob((0.8, -0.05, 5.1), (0.38, 0.5, 0.4), -0.2)
BLOBS = [KNOT, CHEEK]

# ---- the orb and its socket --------------------------------------------------

ORB_R = 0.47
ORB_AT = (28.0, 33.5)     # the orb's centre in the picture
RS = ORB_R + 0.025        # socket radius


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
    """The brow over the orb's upper right: slid along the classic view (so
    the picture's outline keeps) until it overhangs the orb."""
    h0 = Vector((0.74, 0.3, 5.85))
    t = (ORB_C - h0).dot(-VIEW)
    c = h0 - VIEW * t * 0.95
    return Blob(tuple(c), (0.44, 0.42, 0.42), 0.3)


def chin_blob():
    """A fold of root below the slit: the picture's lower lip."""
    q = ray_hit(22.5, 47.8, ped=True)
    return Blob(tuple(q - AX * 0.24 + Vector((0, 0, 0.06))), (0.5, 0.3, 0.28), math.radians(-8))


HOOD = hood_blob()
BLOBS.append(HOOD)
SLIT = RS + 0.24          # the dark slit under the orb


def socket_frame():
    e1 = (Vector((1, 0, 0)) - AX * AX.x).normalized()
    e2 = AX.cross(e1)  # screen-up round the socket
    return e1, e2


_E1, _E2 = (np.array(v) for v in socket_frame())


def socket_coords(P):
    """(height along the socket axis, distance from it, angle round it in
    degrees with 0 screen right and 90 up) of points P."""
    d = P - _Cn
    h = d @ _AXn
    rho = np.linalg.norm(d - h[..., None] * _AXn, axis=-1)
    ang = np.degrees(np.arctan2(d @ _E2, d @ _E1)) % 360
    return h, rho, ang


# ---- the pedestal -------------------------------------------------------------

PROF = [(0.0, 0.74), (0.12, 0.76), (0.3, 0.74), (0.55, 0.7), (0.85, 0.64), (1.15, 0.58), (1.45, 0.54),
        (1.7, 0.525), (1.95, 0.515), (2.2, 0.51), (2.45, 0.51), (2.7, 0.51), (2.95, 0.515), (3.2, 0.53),
        (3.45, 0.545), (3.75, 0.56), (4.1, 0.55), (4.45, 0.58), (4.8, 0.55), (5.1, 0.42), (5.2, 0.0)]
PZ, PRr = np.array([p[0] for p in PROF]), np.array([p[1] for p in PROF])
# the foot's outline read off the picture's bottom edge: (degrees, scale), eased round as a closed curve
FOOT = [(0, 0.97), (45, 0.95), (90, 0.95), (135, 0.98), (180, 1.1), (225, 0.8), (262, 0.62), (285, 1.0),
        (307, 1.45), (330, 1.18)]
FD, FS = np.array([f[0] for f in FOOT], dtype=float), np.array([f[1] for f in FOOT])
# root flares round the back and sides: (degrees, extra scale, height)
FLARES = [(72, 0.55, 1.2), (130, 0.2, 0.6), (216, 0.08, 0.5), (0, 0.12, 0.6)]
PSEG = 44
TWIST = 0.9
SPH = 1.47   # strand phase: a broad strand faces the front left where the picture's trunk is lit
# the neck vine's path round the trunk: from WRAP_A0 degrees, WRAP_SPAN round, rising
WRAP_A0, WRAP_SPAN = 120.0, 225.0
VINE_NECK_R = 0.075


def wrap_z(t):
    return 3.62 + 0.42 * np.clip(t, 0.0, 1.0) ** 1.2


def pcx(z):
    return 0.3 * smoothstep(0.1, 1.3, z) - 0.05 * smoothstep(3.4, 4.8, z)


def strands(a, z):
    """Twisted root strands: rounded ridges with shallow grooves between."""
    g = 0.5 + 0.5 * np.cos(5 * a + TWIST * z + SPH)
    return g, 0.17 * (g ** 0.6 - 0.62) + 0.025 * np.sin(11 * a - 1.4 * z + 1.0)


def foot_scale(deg):
    """Closed Catmull-Rom through the FOOT keys, so the outline has no kinks."""
    n = len(FD)
    deg = np.asarray(deg, dtype=float) % 360
    i1 = np.searchsorted(FD, deg, side="right") - 1
    i2 = (i1 + 1) % n
    i0, i3 = (i1 - 1) % n, (i1 + 2) % n
    d1 = FD[i1]
    d2 = np.where(i2 == 0, 360.0, FD[i2])
    t = (deg - d1) / (d2 - d1)
    p0, p1, p2, p3 = FS[i0], FS[i1], FS[i2], FS[i3]
    return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                  + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)


def groove(a, z):
    """The shallow channel the neck vine lies in."""
    deg = np.degrees(np.asarray(a, dtype=float)) % 360
    t = ((deg - WRAP_A0) % 360) / WRAP_SPAN
    span = (t <= 1.0) * smoothstep(0.0, 0.1, t) * (1 - smoothstep(0.9, 1.0, t))
    return 0.045 * np.exp(-((np.asarray(z, dtype=float) - wrap_z(t)) / 0.075) ** 2) * span


def pmod(a, z):
    a, z = np.broadcast_arrays(np.asarray(a, dtype=float), np.asarray(z, dtype=float))
    foot = foot_scale(np.degrees(a))
    foot_w = np.clip(1 - z / 1.1, 0, 1) ** 1.6
    m = 1 + (foot - 1) * foot_w
    for d0, amp, hgt in FLARES:
        # each flare is the foot of a strand, so it follows the strand's twist
        da = (a - math.radians(d0) + TWIST * z / 5 + math.pi) % (2 * math.pi) - math.pi
        m = m + amp * np.exp(-(da / 0.32) ** 2) * np.clip(1 - z / hgt, 0, 1) ** 1.6
    bulge = 0.45 * np.maximum(0.0, np.cos(a - 0.35)) ** 3 * smoothstep(1.5, 2.3, z) * (1 - smoothstep(3.3, 4.0, z))
    # the broad convex front left that catches the light under the head
    fl = np.maximum(0.0, np.cos(a + 2.0)) ** 2
    bulge = bulge + 0.12 * fl * smoothstep(1.8, 2.6, z) * (1 - smoothstep(3.5, 4.1, z))
    _, s = strands(a, z)
    return m + bulge + s * (1 - 0.6 * smoothstep(4.5, 5.1, z)) + 0.03 * np.sin(3 * a + 4 * z)


def ped_r(a, z, grooved=True):
    r = np.interp(z, PZ, PRr) * pmod(a, z)
    return r - groove(a, z) if grooved else r


def ped_point(a, z):
    r = ped_r(a, z)
    return np.stack(np.broadcast_arrays(pcx(z) + r * np.cos(a), r * np.sin(a), z), axis=-1)


PED_Z = sorted(set(np.round(np.concatenate([
    [0.0, 0.03, 0.08, 0.14, 0.21, 0.29, 0.38, 0.48, 0.6, 0.73, 0.87, 1.02, 1.18, 1.35],
    np.linspace(1.55, 3.35, 8), np.arange(3.42, 4.22, 0.05), np.linspace(4.32, 5.1, 5)]), 4)))


def pedestal(mat):
    A = np.array([2 * math.pi * i / PSEG for i in range(PSEG)])
    P = [[Vector(p) for p in ped_point(A, z)] for z in PED_Z]
    bm = bmesh.new()
    zk.grid(bm, P, uv=lambda j, i: (0.5 * i / PSEG, PED_Z[min(j, len(PED_Z) - 1)] / 5.2),
            top=Vector((float(pcx(5.2)), 0.0, 5.2)))
    zk.fix_normals(bm)
    return zk.smooth(zk.make("pedestal", bm, [mat]), 80)


CHIN = chin_blob()
BLOBS.append(CHIN)

# ---- the head: one blended form ------------------------------------------------

N3 = zk.Noise(23)
LIP_A = (95.0, 305.0)


def lip_path(n=24):
    e1, e2 = socket_frame()
    pts, rad = [], []
    for k in range(n + 1):
        s = k / n
        t = math.radians(LIP_A[0] + (LIP_A[1] - LIP_A[0]) * s)
        pts.append(np.array(ORB_C + (e1 * math.cos(t) + e2 * math.sin(t)) * (RS + 0.05) + AX * 0.0))
        lowleft = max(0.0, math.cos(t - math.radians(225)))
        rad.append((0.1 * (0.6 + 0.4 * lowleft)) * math.sin(math.pi * s) ** 0.6 + 0.004)
    return np.array(pts), np.array(rad)


LIP_P, LIP_RAD = lip_path()


def lip_sdf(P):
    d = np.full(P.shape[:-1], 9.0)
    for i in range(len(LIP_P) - 1):
        a, b = LIP_P[i], LIP_P[i + 1]
        ab = b - a
        t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1)
        q = a + t[..., None] * ab
        r = LIP_RAD[i] * (1 - t) + LIP_RAD[i + 1] * t
        d = np.minimum(d, np.linalg.norm(P - q, axis=-1) - r)
    return d


NECK_A, NECK_B = np.array([float(pcx(4.35)), 0.0, 4.35]), np.array([0.3, 0.0, 5.3])


def neck_sdf(P):
    ab = NECK_B - NECK_A
    t = np.clip(((P - NECK_A) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(P - (NECK_A + t[..., None] * ab), axis=-1) - (0.5 + 0.06 * t)


def brow_weight(ang):
    """1 over the orb's upper right, where the brow may overhang it."""
    return smoothstep(0.0, 25.0, ang) * (1 - smoothstep(95.0, 125.0, ang))


def head_strands(P):
    """The trunk's twisted strands continued up over the head: (phase 0..1
    with 1 on a ridge, the angle round the trunk's axis, and how strongly
    they show, full on the neck and fainter over the crown)."""
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    ang = np.arctan2(y, x - pcx(np.minimum(z, 5.2)))
    g = 0.5 + 0.5 * np.cos(5 * ang + TWIST * z + SPH)
    dist = np.linalg.norm(P - _Cn, axis=-1)
    w = (1 - 0.45 * smoothstep(4.9, 6.3, z)) * smoothstep(RS + 0.03, RS + 0.2, dist)
    return g, ang, w


def head_fibres(P, ang):
    """Finer root strands between the big ones, twisting the same way (1 on a ridge)."""
    return 0.5 + 0.5 * np.sin(14 * ang + 2.4 * P[..., 2] + 1.3 * N3.fbm(P[..., 0] * 2, P[..., 1] * 2, P[..., 2], 2))


def head_sdf(P, parts=False):
    k = [KNOT.sdf(P), CHEEK.sdf(P), HOOD.sdf(P), CHIN.sdf(P), neck_sdf(P), lip_sdf(P)]
    d = zk.smin(k[0], k[1], 0.34)
    d = zk.smin(d, k[2], 0.42)
    d = zk.smin(d, k[3], 0.36)
    d = zk.smin(d, k[4], 0.3)
    d = zk.smin(d, k[5], 0.1)
    # broad, gentle swells so it reads as grown root rather than blended balls
    d = d + 0.035 * (N3.fbm(P[..., 0] * 1.8, P[..., 1] * 1.8, P[..., 2] * 1.8, 3) - 0.5)
    g, sa, w = head_strands(P)
    fb = head_fibres(P, sa)
    d = d - (0.09 * (g ** 0.6 - 0.62) + 0.022 * (fb ** 0.7 - 0.6)) * w
    h, rho, ang = socket_coords(P)
    # the slit under the orb
    low = np.maximum(0.0, np.cos(np.radians(ang - 265.0))) ** 1.5
    d = d + 0.07 * np.exp(-((rho - SLIT) / 0.055) ** 2) * low * smoothstep(-0.35, -0.1, h)
    # the socket: the orb's sphere, and the view of it kept clear except under the brow
    sphere = np.linalg.norm(P - _Cn, axis=-1) - RS
    front = np.maximum(rho - RS * 0.96, -h) + 0.6 * brow_weight(ang)
    carve = zk.smin(sphere, front, 0.04)
    d = zk.smax(d, -carve, 0.04)
    if parts:
        return d, k
    return d


def head(mats):
    lo, hi = (-1.05, -1.15, 3.8), (1.75, 1.15, 7.1)
    V, Q = zk.surface_nets(head_sdf, lo, hi, 0.032)
    ob = zk.mesh_from("head", V, Q, mats)
    zk.decimate(ob, 6000)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


# ---- the root texture ---------------------------------------------------------

N1, N2, N4 = zk.Noise(21), zk.Noise(22), zk.Noise(24)
C_DARK, C_MID, C_LIGHT = rgb((48, 41, 21)), rgb((100, 85, 50)), rgb((138, 120, 76))
RUSTC = rgb((126, 74, 46))
TEX_W, TEX_H = 1024, 512


def bark(P, along=None, k=1.0):
    """Smooth olive-brown bark: broad mottling stretched along the grain, a
    faint lengthwise fibre and a few soft cracks. along is a coordinate that
    stays constant along the grain (the trunk's twisted angle)."""
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    if along is None:
        u1, u2, u3 = x * 2.6, y * 2.6, z * 2.6
        f1, f2, f3 = x * 11, y * 11, z * 11
    else:
        u1, u2, u3 = np.cos(along) * 2.2, np.sin(along) * 2.2, z * 0.55
        f1, f2, f3 = np.cos(along) * 14, np.sin(along) * 14, z * 1.6
    m = N1.fbm(u1, u2, u3, 4)
    f = N2.fbm(f1, f2, f3, 3)
    c = mix(C_DARK, C_MID, smoothstep(0.3, 0.6, m))
    c = mix(c, C_LIGHT, smoothstep(0.58, 0.8, m) * 0.6)
    c = c * (0.86 + 0.28 * f)[..., None]
    cr = 1 - smoothstep(0.0, 0.03, np.abs(N4.fbm(u1 * 1.6 + 3, u2 * 1.6, u3 * 1.6, 2) - 0.5))
    c = c * (1 - 0.22 * cr * k)[..., None]
    return c, m, f, cr


CURL_C0, CURL_TIP = (41.0, 58.0), (38.3, 64.7)   # the hanging curl's centre and tip, in picture px


def paint_pedestal(W, H):
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    a, z = 2 * math.pi * U, V * 5.2
    P = ped_point(a, z)
    g, _ = strands(a, z)
    along = a + TWIST * z / 5  # constant along a strand
    c, m, f, cr = bark(P, along)
    # grooves between the strands a little darker, the ridge tops catch the light
    c = c * (0.72 + 0.34 * smoothstep(0.0, 0.3, g))[..., None]
    c = mix(c, C_LIGHT, smoothstep(0.78, 1.0, g) * 0.25 * smoothstep(0.4, 0.6, f))
    # long rust streaks down the worn strands of the trunk's front
    front = smoothstep(-0.2, 0.6, -np.sin(a + 0.25))
    zone = smoothstep(1.0, 1.6, z) * (1 - smoothstep(3.5, 4.0, z))
    st = N2.fbm(np.cos(along) * 7, np.sin(along) * 7, z * 0.45 + 17, 3)
    patch = smoothstep(0.35, 0.6, N4.fbm(np.cos(along) * 5, np.sin(along) * 5, z * 1.4 + 3, 3))
    rust = smoothstep(0.47, 0.62, st) * front * zone * (0.45 + 0.55 * smoothstep(0.3, 0.8, g)) * patch
    rust = np.maximum(rust, smoothstep(0.5, 0.64, st) * smoothstep(0.3, 0.85, np.cos(a + 1.95)) * zone * 0.8)
    c = mix(c, RUSTC * (0.8 + 0.4 * f)[..., None], rust * 0.85)
    # a little on the back and the upper trunk too
    c = mix(c, RUSTC * 0.8, smoothstep(0.62, 0.72, st) * (1 - front) * zone * 0.4)
    # and rusty blotches scattered over the worn front left
    blot = N4.fbm(P[..., 0] * 3.2 + 7, P[..., 1] * 3.2, P[..., 2] * 2.4, 3)
    fz = smoothstep(0.6, 1.2, z) * (1 - smoothstep(3.7, 4.2, z))
    c = mix(c, rgb((142, 82, 56)) * (0.85 + 0.3 * f)[..., None],
            smoothstep(0.56, 0.66, blot) * smoothstep(-0.1, 0.7, np.cos(a + 1.85)) * fz * 0.85)
    # the trunk's bark leans olive, as in the picture
    c = c * np.array((0.96, 1.02, 1.06))
    # damp, dark foot, and the damp side away from the picture's light
    c = c * (0.62 + 0.38 * smoothstep(0.0, 0.9, z))[..., None]
    damp = smoothstep(-0.05, 0.3, np.cos(a + 0.35)) * (1 - smoothstep(4.3, 5.0, z))
    c = c * (1 - 0.6 * damp)[..., None]
    # the weathered, paler strand on the front left, where the picture's light falls
    fl = smoothstep(0.1, 0.8, np.cos(a + 1.85)) * smoothstep(0.3, 1.0, z) * (1 - smoothstep(4.0, 4.6, z))
    c = c * (1 + 0.6 * fl)[..., None]
    # the vine's groove shaded
    c = c * (1 - 3.0 * groove(a, z))[..., None]
    # the soft shade of the hollow behind the hanging curl, on the trunk's front right
    da = (a + 1.05 + math.pi) % (2 * math.pi) - math.pi
    c = c * (1 - 0.65 * np.exp(-(da / 0.4) ** 2 - ((z - 2.8) / 0.6) ** 2))[..., None]
    # up into the head's older, darker root, gradually
    top = smoothstep(3.9, 4.7, z)
    c = c * (1 + top[..., None] * (rgb((205, 196, 180)) * 0.85 - 1))
    h = 0.012 * g ** 0.6 + 0.004 * f + 0.003 * m - 0.004 * cr
    return c, h


def occluders(Q):
    """Distance to the head, the orb and the trunk, for the ambient shade."""
    d = np.minimum(head_sdf(Q), np.linalg.norm(Q - _Cn, axis=-1) - ORB_R)
    z = np.clip(Q[..., 2], 0.0, 5.2)
    x = Q[..., 0] - pcx(z)
    t = np.hypot(x, Q[..., 1]) - ped_r(np.arctan2(Q[..., 1], x), z, grooved=False)
    return np.minimum(d, np.where(Q[..., 2] < 5.2, t, 9.0))


def paint_head(ob, W, H):
    """Colour and height for the head, baked through its UVs from 3D noise:
    the trunk's bark and strands carried up over it, darker in the creases,
    under the brow and in the bowl behind the orb, with a soft hint of the
    picture's light from the upper left."""
    R, C, P, Nn = zk.raster(ob, W, H)
    g, sa, w = head_strands(P)
    along = sa + TWIST * P[..., 2] / 5
    c1, m1, f1, cr1 = bark(P, along)
    c0, m0, f0, cr0 = bark(P)
    c = mix(c0, c1, w) * rgb((205, 196, 180))  # the head is older root, a little darker and warmer
    m, f, cr = m0 + (m1 - m0) * w, f0 + (f1 - f0) * w, cr0 + (cr1 - cr0) * w
    # grooves between the strands darker and the ridges catching light, as on the trunk
    fb = head_fibres(P, sa)
    sh = (0.68 + 0.38 * smoothstep(0.0, 0.3, g)) * (0.8 + 0.22 * smoothstep(0.1, 0.6, fb))
    c = c * (1 + w * (sh - 1))[..., None]
    c = mix(c, C_LIGHT, smoothstep(0.75, 1.0, g) * 0.3 * smoothstep(0.35, 0.6, f) * w)
    d, k = head_sdf(P, parts=True)
    # dark where the parts grow out of one another
    crease = np.clip((np.min(np.stack(k[:5]), axis=0) - d) / 0.08, 0, 1)
    c = c * (1 - 0.3 * crease)[..., None]
    # ambient shade from the head's own folds, the orb and the trunk
    occ = np.zeros(len(P))
    for i, dl in enumerate((0.04, 0.09, 0.18, 0.32)):
        occ += np.maximum(0.0, dl - occluders(P + Nn * dl)) / dl * 0.5 ** i
    c = c * np.clip(1 - 0.4 * occ, 0.45, 1.0)[..., None]
    c = c * (0.86 + 0.2 * np.clip(Nn[..., 2], 0, 1))[..., None]
    # a soft hint of the picture's light from the upper left, never below 70 per cent
    lp = np.array((-0.6, -0.3, 0.74)) / np.linalg.norm((-0.6, -0.3, 0.74))
    c = c * (0.7 + 0.42 * smoothstep(-0.35, 0.45, Nn @ lp))[..., None]
    h, rho, ang = socket_coords(P)
    dist = np.linalg.norm(P - _Cn, axis=-1)
    # the bowl behind the orb goes dark
    bowl = smoothstep(RS + 0.06, RS + 0.01, dist) * smoothstep(0.1, -0.05, h)
    c = c * (1 - 0.8 * bowl)[..., None]
    # the lit rim round the orb's lower left
    lowleft = np.maximum(0.0, np.cos(np.radians(ang - 215.0))) ** 1.2
    rim = np.exp(-((rho - RS - 0.07) / 0.075) ** 2) * lowleft * smoothstep(-0.2, 0.0, h)
    c = mix(c, rgb((178, 148, 98)) * (0.85 + 0.3 * f)[..., None], rim * 0.85)
    slit = np.exp(-((rho - SLIT) / 0.05) ** 2) * np.maximum(0.0, np.cos(np.radians(ang - 262.0))) ** 2
    c = c * (1 - 0.8 * slit * smoothstep(-0.35, -0.1, h))[..., None]
    # the underside of the brow where it overhangs the orb
    brow = brow_weight(ang) * smoothstep(RS + 0.35, RS + 0.08, dist) * smoothstep(0.35, -0.25, Nn[..., 2])
    c = c * (1 - 0.55 * brow)[..., None]
    hh = 0.004 * f + 0.004 * m - 0.004 * cr + (0.014 * g ** 0.6 + 0.006 * fb) * w
    col = np.zeros((H, W, 3))
    hgt = np.zeros((H, W))
    mask = np.zeros((H, W), dtype=bool)
    col[R, C], hgt[R, C], mask[R, C] = c, hh, True
    col, _ = zk.dilate(col, mask, 8)
    hgt, _ = zk.dilate(hgt[..., None], mask, 8)
    return col, hgt[..., 0]


def root_material(head_ob):
    """One atlas: the pedestal on the left half, the head on the right."""
    Wp = TEX_W // 2
    cells = zk.unwrap(head_ob)
    uvl = head_ob.data.uv_layers["UVMap"].data
    for d in uvl:
        d.uv = (0.5 + 0.5 * d.uv[0], d.uv[1])
    col = np.zeros((TEX_H, TEX_W, 3))
    nrm = np.zeros((TEX_H, TEX_W, 3))
    c, h = paint_pedestal(Wp, TEX_H)
    col[:, :Wp] = c
    nrm[:, :Wp] = zk.normals_from_height(h, 2 * math.pi * 0.55 / Wp, 5.2 / TEX_H)
    # the head's UVs now span the right half: paint them there
    for d in uvl:
        d.uv = ((d.uv[0] - 0.5) * 2, d.uv[1])
    c, h = paint_head(head_ob, Wp, TEX_H)
    for d in uvl:
        d.uv = (0.5 + 0.5 * d.uv[0], d.uv[1])
    col[:, Wp:] = c
    nrm[:, Wp:] = zk.normals_from_height(h, cells / Wp, cells / TEX_H)
    img = zk.image("zm_root_col", col)
    nimg = zk.image("zm_root_nrm", nrm, noncolor=True)
    m = zk.tex_mat("zm_root", img, rough=0.9, nimg=nimg, nstrength=1.0)
    # dry root barely shines: a low specular keeps its dark sides warm rather than sky grey
    m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.15
    return m


# ---- the orb -------------------------------------------------------------------

# the hot spot sits a little left of centre and toward the camera, as in the picture
GLOW_OFF = Vector((-1, 0, 0)) * 0.2 - zk.SCREEN_UP * 0.03 + (-VIEW) * 0.1


GLOW_STRENGTH = 3.3
BODY_GLOW = 0.25


def uvsphere(name, r, c, mat, u=32, v=16, uvs=False):
    bm = bmesh.new()
    if uvs:
        bm.loops.layers.uv.new("UVMap")
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r, calc_uvs=uvs)
    for q in bm.verts:
        q.co += c
    return zk.smooth(zk.make(name, bm, [mat]), 89)


def glass_textures(W=256, H=128):
    """Body colour and glow for the glass, painted on the sphere's own UVs:
    lighter blue on the lit upper left, navy toward the rim, a soft glow
    round the point where the hot core shows through and a faint blue glow
    over the side out of the socket."""
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    lon, lat = 2 * math.pi * (U - 0.5), math.pi * (V - 0.5)  # Blender's sphere UVs
    n = np.stack([np.cos(lat) * np.cos(lon), np.cos(lat) * np.sin(lon), np.sin(lat)], axis=-1)
    toward = np.array(-VIEW)
    lit = n @ np.array((Vector((-0.6, 0, 0)) + zk.SCREEN_UP * 0.5 - VIEW * 0.6).normalized())
    facing = np.clip(n @ toward, 0, 1)
    off = np.array(GLOW_OFF)
    side = off - toward * (off @ toward)
    g = side / ORB_R + toward * math.sqrt(max(0.0, 1 - (side @ side) / ORB_R ** 2))
    g /= np.linalg.norm(g)
    th = np.degrees(np.arccos(np.clip(n @ g, -1, 1)))
    body = mix(rgb((9, 14, 36)), rgb((28, 44, 88)), smoothstep(-0.3, 0.9, lit))
    body = body * (0.8 + 0.2 * smoothstep(0.0, 0.7, facing))[..., None]
    keys = [(0, (252, 252, 253)), (10, (242, 243, 246)), (20, (124, 126, 134)), (31, (58, 62, 76)),
            (44, (16, 22, 42)), (60, (5, 8, 20)), (85, (0, 0, 0))]
    glow = np.zeros(th.shape + (3,))
    for (t0, c0), (t1, c1) in zip(keys[:-1], keys[1:]):
        w = np.clip((th - t0) / (t1 - t0), 0, 1)
        seg_ = (th >= t0) & (th < t1)
        glow[seg_] = mix(rgb(c0), rgb(c1), smoothstep(0, 1, w))[seg_]
    glow[th >= keys[-1][0]] = 0.0
    # the body's own faint blue light, the same from every side
    out = smoothstep(-0.1, 0.5, n @ np.array(AX))  # only the side out of the socket
    glow = glow + body * (BODY_GLOW / GLOW_STRENGTH) * out[..., None]
    return body, glow


def orb_parts():
    body, gl = glass_textures()
    glass = zk.tex_mat("zm_orb_glass", zk.image("zm_orb_body", body), rough=0.28)
    nt = glass.node_tree
    b = nt.nodes["Principled BSDF"]
    uvn = [q for q in nt.nodes if q.type == "UVMAP"][0]
    te = nt.nodes.new("ShaderNodeTexImage")
    te.image = zk.image("zm_orb_glow", gl)
    nt.links.new(uvn.outputs["UV"], te.inputs["Vector"])
    nt.links.new(te.outputs["Color"], b.inputs["Emission Color"])
    b.inputs["Emission Strength"].default_value = GLOW_STRENGTH
    b.inputs["Alpha"].default_value = 0.8
    b.inputs["IOR"].default_value = 1.5
    glass.surface_render_method = "BLENDED"
    glass.use_backface_culling = True
    return [uvsphere("orb", ORB_R, ORB_C, glass, 40, 20, uvs=True)]


# ---- vines --------------------------------------------------------------------

LIT_SIDE = (Vector((0, 0, 1)) - VIEW * 0.8).normalized()  # the side of a vine that faces the light


def vine_material():
    """Dry vine: a light top ridge, a darker underside and faint fibres
    running along it (u along, v round from the top)."""
    W, H = 64, 64
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    n = zk.Noise(41)
    au, av = 2 * math.pi * U, 2 * math.pi * V
    fib = n.fbm(np.cos(av) * 7 + np.cos(au) * 0.9, np.sin(av) * 7 + np.sin(au) * 0.9, 0.3, 3)
    ridge = np.cos(av)
    c = mix(rgb((62, 46, 26)), rgb((156, 124, 76)), smoothstep(-0.8, 0.55, ridge))
    c = mix(c, rgb((246, 220, 142)), smoothstep(0.7, 0.99, ridge))
    c = c * (0.88 + 0.24 * fib)[..., None]
    img = zk.image("zm_vine_col", c)
    nimg = zk.image("zm_vine_nrm", zk.normals_from_height(0.0025 * fib, 0.5 / W, 2 * math.pi * 0.06 / H),
                    noncolor=True)
    m = zk.tex_mat("zm_vine", img, rough=0.4, metal=0.0, nimg=nimg, nstrength=0.8)
    m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.45
    return m


VINE = vine_material()
LEAF = hk.pbr("zm_leaf", lin((66, 68, 36)), rough=0.62)


def gentle(seed, count=4):
    rng = random.Random(seed)
    nodes = [rng.uniform(0.1, 0.9) for _ in range(count)]

    def f(s, a):
        return sum(0.07 * math.exp(-((s - q) / 0.03) ** 2) for q in nodes)
    return f


def vine(ctrl, radius, name="vine", seed=1, step=0.04, seg=8, n=10):
    pts = zk.resample(zk.spline(ctrl, n), step)
    bm = bmesh.new()
    zk.sweep(bm, pts, radius, seg=seg, bumps=gentle(seed), ulen=0.5, frames_=zk.frames_facing(pts, LIT_SIDE))
    return zk.smooth(zk.make(name, bm, [VINE]), 70)


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
    """The handle over the orb: out of the head low on the left, up its
    left side, over the top and down into the head behind."""
    ctrl = [at(18.5, 49.0, 0.05), at(15.6, 47.6, -0.12), at(14, 45.5, -0.25), at(12.3, 40, -0.25),
            at(12.4, 34, -0.15), at(13.4, 29, -0.1), at(15.5, 24.6, 0.0), at(18.5, 21.2, 0.15),
            at(22.5, 18.6, 0.3), at(27, 17.3, 0.45), at(31, 17.5, 0.6), at(33.4, 19.9, 0.72), at(33.5, 23, 0.7)]
    return vine(ctrl, taper(0.09, 0.065, 0.02, root=0.06), name="arch", seed=3)


def leaf(bm, base, goal, lift, width, cup=0.3, bend=0.25, seed=0):
    """A cupped, bending leaf from base toward goal; lift tips its face
    toward the camera (+) or away (-)."""
    rng = random.Random(seed)
    d = goal - base
    ln = d.length
    d.normalize()
    face = (-VIEW + Vector((0, 0, 0.3)) + d * 0.0).normalized()
    side = d.cross(face).normalized()
    nrm = side.cross(d).normalized()
    # tip the blade round its own axis a little
    tw = lift + rng.uniform(-0.2, 0.2)
    side, nrm = (side * math.cos(tw) + nrm * math.sin(tw)), (nrm * math.cos(tw) - side * math.sin(tw))
    rows, cols = 7, 5
    grid = []
    for i in range(rows + 1):
        t = i / rows
        w = width * math.sin(math.pi * min(1.0, t ** 0.62)) ** 0.6
        ctr = base + d * ln * t + nrm * (-bend * ln * t * t) + nrm * 0.02 * math.sin(math.pi * t)
        if i == rows:
            grid.append([bm.verts.new(ctr)])
            continue
        row = []
        for j in range(cols):
            x = -1 + 2 * j / (cols - 1)
            row.append(bm.verts.new(ctr + side * x * w / 2 + nrm * cup * x * x * w * 0.5))
        grid.append(row)
    for i in range(rows):
        a, b = grid[i], grid[i + 1]
        for j in range(cols - 1):
            if len(b) == 1:
                bm.faces.new((a[j], a[j + 1], b[0]))
            else:
                bm.faces.new((a[j], a[j + 1], b[j + 1], b[j]))


def sprig():
    """A twig off the arch with a spray of olive leaves, top left."""
    base = at(19.5, 21.5, 0.14)
    tip = at(13.8, 18.4, 0.24)
    mid = (base + tip) / 2 + Vector((0.0, 0, 0.06))
    parts = [vine([base, mid, tip], taper(0.045, 0.022), name="twig", seed=4, step=0.02, seg=6)]
    bm = bmesh.new()
    # (attach along the twig 0..1, goal in picture px, lift, width, bend)
    for k, (s, px, row, lift, w, bend) in enumerate((
            (1.0, 9.0, 16.6, 0.5, 0.21, 0.3), (1.0, 9.8, 20.0, -0.5, 0.2, 0.3),
            (1.0, 11.6, 15.0, 0.1, 0.2, 0.25), (0.75, 14.2, 15.2, -0.5, 0.2, 0.3),
            (0.7, 12.8, 21.0, 0.6, 0.17, 0.25), (0.5, 17.2, 15.2, 0.2, 0.19, 0.3),
            (0.3, 20.4, 16.0, -0.4, 0.16, 0.2))):
        a = base.lerp(tip, s) if s < 1 else tip
        if 0.0 < s < 1.0:
            a = zk.spline([base, mid, tip], 8)[int(round(s * 16))]
        sx, sy = screen(a)
        goal = on_plane(a, px - sx, sy - row, lean=50.0) + Vector((0, -0.16 * lift, 0.06 * lift))
        leaf(bm, a, goal, lift, w, cup=0.35, bend=bend, seed=k)
    zk.fix_normals(bm)
    parts.append(zk.smooth(zk.make("leaves", bm, [LEAF]), 70))
    return parts


def spiral():
    """The curl off the head's left side, on a plane leaning back to face
    the classic camera (points are picture pixels)."""
    c0 = (4.6, 38.4)
    c = at(c0[0], c0[1], -0.3)
    path = [(8.2, 34.4), (4.9, 33.3), (2.2, 34.2), (1.2, 37.0), (1.4, 40.0), (2.9, 42.5), (5.3, 43.4),
            (7.1, 42.0), (7.3, 40.0), (6.2, 39.0), (5.2, 39.9)]
    ctrl = [at(14.5, 38.5, 0.05), at(12.5, 38, -0.15), at(10.5, 35.8, -0.25)]
    ctrl += [on_plane(c, px - c0[0], c0[1] - row) for px, row in path]
    return vine(ctrl, taper(0.085, 0.045, 0.03, root=0.04), name="spiral", seed=5, step=0.028)


def neck_ring_and_curl():
    """A vine wound once round the neck, lying in its groove, that leaves on
    the right and hangs in a curl (curl points are picture pixels)."""
    ctrl = []
    n = 24
    for k in range(n + 1):
        t = k / n
        a = math.radians(WRAP_A0 + WRAP_SPAN * t)
        z = float(wrap_z(t))
        r = float(ped_r(a, z)) + VINE_NECK_R * 0.6
        ctrl.append(Vector((float(pcx(z)) + r * math.cos(a), r * math.sin(a), z)))
    c0 = CURL_C0
    c = at(c0[0], c0[1], -0.35)
    path = [(39.2, 49.8), (43.2, 49.9), (45.5, 52.6), (45.9, 57.0), (45.4, 61.0), (43.1, 63.6), (40.5, 65.0),
            (39.1, 65.4), CURL_TIP]
    ctrl += [on_plane(c, px - c0[0], c0[1] - row) for px, row in path]
    L = [0.0]
    for i in range(1, len(ctrl)):
        L.append(L[-1] + (ctrl[i] - ctrl[i - 1]).length)
    s_leave = L[n] / L[-1]

    def radius(s):
        # even round the neck, thicker where it leaves, fine at both tips
        r = VINE_NECK_R + 0.045 * sstep(s_leave - 0.05, s_leave + 0.1, s) * (1 - sstep(s_leave + 0.15, 1.0, s))
        r -= 0.03 * sstep(0.75, 1.0, s)
        return r * min(1.0, (1 - s) / 0.03) ** 0.5 * min(1.0, s / 0.04) ** 0.6 + 0.001
    return vine(ctrl, radius, name="curl", seed=6, step=0.035, n=6)


# ---- build --------------------------------------------------------------------

HEAD = head([])
ROOT = root_material(HEAD)
HEAD.data.materials.append(ROOT)

parts = [pedestal(ROOT), HEAD]
parts += orb_parts()
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
# the same views under a mid-grey sky, as a Unity skybox would light them
zk.grey_renders(ob, os.path.join(OUT, "renders", "grey"), NAME, SPRITE, HOT, scale=4)
CHECK = os.environ.get("ZK_CHECK")
if CHECK:
    zk.closeups(CHECK, NAME + "_head", tuple(ORB_C + Vector((0.1, 0.2, 0.1))), 2.6,
                [("classic", -90, 63.4), ("front34", -45, 20), ("side", 0, 10), ("back34", 135, 25),
                 ("frontlow", -100, 5)])
    zk.closeups(CHECK, NAME + "_body", (0.3, 0.0, 3.6), 7.8, [("front", -90, 15), ("left", -150, 15),
                                                           ("right", -20, 15)], res=448)
    zk.closeups(CHECK, NAME + "_neck", (0.25, 0.0, 3.85), 1.8, [("front", -90, 10), ("left", -160, 15)])
    zk.closeups(CHECK, NAME + "_foot", (0.3, -0.2, 0.6), 2.8, [("frontleft", -120, 12), ("classic", -90, 63.4)])
    zk.closeups(CHECK, NAME + "_leaves", tuple(at(15.0, 17.5, 0.3)), 1.1, [("classic", -90, 63.4),
                                                                        ("front34", -45, 20), ("side", 0, 10)])
