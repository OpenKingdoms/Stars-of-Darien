"""ZONMANA, the Zhon divine lodestone: a pedestal of twisted root strands
rising to one smooth rounded knob of root (its strands carried up over it as
relief only), that holds a polished deep navy glass orb with a star glint and a
faint blue glow inside, a quarter bigger than the picture's and half sunk in
a socket under a domed brow, over a mouth-like slit and a jaw ledge. Two-toned
vines curl round it (an arch over the orb with a leafy sprig, a spiral on the
left and a vine wound round the neck in a groove that hangs in a curl on the
right). Everything is modelled or painted from noise, no pixels of the picture.

    blender -b --factory-startup --python ZONMANA.py

The sturdy cut: built as the approved model, then the trunk is shortened and
thickened on a heavier foot and everything above it sits down on it.
"""
import math
import os
import random
import sys

import bmesh
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import zhonkit as zk  # noqa: E402
from zhonkit import hk, lin, rgb, mix, smoothstep, sstep  # noqa: E402

NAME = "ZONMANA"
SPRITE = r"D:\OKReplace\lodes\sprites\ZONMANA.png"
HOT = (24, 74)
OUT = r"D:\OKReplace\lodes\sturdy"
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


# deeper than the approved head, grown at the back so its face keeps its place
DEEP = 0.18
KNOT = Blob((0.32, -0.05 + DEEP, 5.45), (1.02, 0.62 + DEEP, 0.8), math.radians(24))
CHEEK = Blob((0.72, -0.05 + 0.8 * DEEP, 5.1), (0.38, 0.5 + 0.8 * DEEP, 0.4), -0.2)
BLOBS = [KNOT, CHEEK]

# ---- the orb and its socket --------------------------------------------------

ORB_R0 = 0.47             # the orb as the picture draws it
ORB_R = 0.58              # a quarter bigger, as on ZONLODE
K = ORB_R / ORB_R0
ORB_AT = (28.0, 33.5)     # the orb's centre in the picture
RS = ORB_R + 0.025        # socket radius
SINK = 0.1                # the bigger orb sits a little deeper along the classic view


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
        c = s + ax * 0.03 + VIEW * SINK
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


HOOD = hood_blob()
BLOBS.append(HOOD)
SLIT = RS + 0.2           # the dark slit under the orb


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
    return g, 0.12 * (g ** 0.6 - 0.62) + 0.025 * np.sin(11 * a - 1.4 * z + 1.0)


def ring_curve(D, S, deg):
    """Closed Catmull-Rom through keys (D degrees from 0, S values), so the outline has no kinks."""
    n = len(D)
    deg = np.asarray(deg, dtype=float) % 360
    i1 = np.searchsorted(D, deg, side="right") - 1
    i2 = (i1 + 1) % n
    i0, i3 = (i1 - 1) % n, (i1 + 2) % n
    d1 = D[i1]
    d2 = np.where(i2 == 0, 360.0, D[i2])
    t = (deg - d1) / (d2 - d1)
    p0, p1, p2, p3 = S[i0], S[i1], S[i2], S[i3]
    return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                  + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)


def foot_scale(deg):
    return ring_curve(FD, FS, deg)


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


def strand_relief(z):
    """How much the strands shape the surface: full on the neck, none over
    the knot, whose outline stays one smooth rounded form."""
    return 1 - smoothstep(4.7, 5.3, z)


def head_sdf(P, parts=False, jaw=True):
    k = [KNOT.sdf(P), CHEEK.sdf(P), HOOD.sdf(P), jaw_sdf(P) if jaw else np.full(P.shape[:-1], 9.0),
         neck_sdf(P), lip_sdf(P), JAW_FILL.sdf(P)]
    d = zk.smin(k[0], k[1], 0.55)
    d = zk.smin(d, k[2], 0.85)
    d = zk.smin(d, k[4], 0.3)
    d = zk.smin(d, k[6], 0.3)
    # a very gentle swell so it reads as grown root rather than blended balls
    d = d + 0.02 * (N3.fbm(P[..., 0] * 1.5, P[..., 1] * 1.5, P[..., 2] * 1.5, 3) - 0.5)
    g, sa, w = head_strands(P)
    fb = head_fibres(P, sa)
    d = d - (0.09 * (g ** 0.6 - 0.62) + 0.022 * (fb ** 0.7 - 0.6)) * w * strand_relief(P[..., 2])
    d = zk.smin(d, k[3], 0.06)
    d = zk.smin(d, k[5], 0.1)
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


JAW_R = 0.095
# the jaw ledge's centre line: (picture px, row, height), from the head's lower left
# corner, where it turns down and back, to under the orb where it sinks into the neck
JAW_KEYS = [(13.3, 48.6, 4.44), (14.3, 48.1, 4.54), (16.0, 47.9, 4.61), (18.5, 47.9, 4.66), (21.0, 47.8, 4.73),
            (23.2, 47.6, 4.8), (25.0, 47.2, 4.84)]
# the mass of root behind the ledge that fills the head's lower left corner
JAW_FILL = Blob((-0.27, -0.2, 4.82), (0.42, 0.3, 0.42), math.radians(-6))


def ped_sdf(P):
    """Roughly the distance to the pedestal (negative inside)."""
    z = np.clip(P[..., 2], 0.0, 5.2)
    x = P[..., 0] - pcx(z)
    t = np.hypot(x, P[..., 1]) - ped_r(np.arctan2(P[..., 1], x), z, grooved=False)
    return np.where((P[..., 2] > 0) & (P[..., 2] < 5.2), t, 9.0)


def first_hit(px, row):
    """Where the camera ray through a picture pixel first meets the head
    (without its jaw) or the pedestal, or None."""
    t = np.arange(0.0, 7.0, 0.003)
    P = np.array(at(px, row, -3.0)) + t[:, None] * np.array(VIEW)
    d = np.minimum(head_sdf(P, jaw=False), ped_sdf(P))
    i = int(np.argmax(d < 0))
    return Vector(P[i]) if d[i] < 0 else None


def jaw_path():
    """The ledge of root under the orb, the picture's lit lower lip: a rod
    placed through the picture pixels at the heights given."""
    pts = []
    for px, row, z in JAW_KEYS:
        u = HOT[1] - row
        pts.append(at(px, row, (u - zk.SZ * z) / zk.SY))
    return np.array([np.array(p) for p in zk.resample(zk.spline(pts, 6), 0.05)])


JAW_P = jaw_path()
_JL = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(JAW_P, axis=0), axis=1))])
JAW_S = _JL / _JL[-1]
# thickest in the middle, a little thinner where it rounds the corner and where it sinks in under the orb
JAW_RAD = JAW_R * (0.8 + 0.2 * np.sin(np.pi * JAW_S)) * (1 - 0.45 * JAW_S ** 3) * (0.55 + 0.45 * smoothstep(0.0, 0.12, JAW_S))


def jaw_sdf(P, near=False):
    """Distance to the jaw ledge; with near, also the offset from its
    closest point on the centre line (for painting)."""
    d = np.full(P.shape[:-1], 9.0)
    off = np.zeros(P.shape)
    for i in range(len(JAW_P) - 1):
        a, b = JAW_P[i], JAW_P[i + 1]
        ab = b - a
        t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1)
        q = a + t[..., None] * ab
        r = JAW_RAD[i] * (1 - t) + JAW_RAD[i + 1] * t
        di = np.linalg.norm(P - q, axis=-1) - r
        if near:
            off = np.where((di < d)[..., None], P - q, off)
        d = np.minimum(d, di)
    return (d, off) if near else d


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
RUSTC = rgb((146, 88, 60))
RUSTF = rgb((142, 80, 54))   # the brighter orange-red flecks
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
    # the weathered strands of the front left, where the picture's light falls
    fl = smoothstep(-0.6, 0.4, np.cos(a + 2.4)) * smoothstep(0.3, 1.0, z) * (1 - smoothstep(4.0, 4.6, z))
    # grooves between the strands a little darker (less so on the worn front left), the ridge tops catch the light
    c = c * (0.72 + 0.26 * fl + (0.34 - 0.26 * fl) * smoothstep(0.0, 0.3, g))[..., None]
    c = mix(c, C_LIGHT, smoothstep(0.78, 1.0, g) * 0.25 * smoothstep(0.4, 0.6, f) * (1 - 0.7 * fl))
    # the trunk's bark leans olive, as in the picture
    c = c * np.array((0.96, 1.02, 1.06))
    # and the worn front left is a paler olive
    lum = (c @ np.array((0.3, 0.59, 0.11)))[..., None]
    c = mix(c, lum * np.array((1.02, 1.0, 0.78)), 0.35 * fl)
    c = c * (1 + 1.0 * fl)[..., None]
    # rust in blotches over the trunk's front left, thickest under the neck
    front = smoothstep(-0.2, 0.5, np.cos(a + 2.3))
    zone = smoothstep(0.5, 1.0, z) * (1 - smoothstep(3.7, 4.0, z))
    st = N2.fbm(np.cos(along) * 3, np.sin(along) * 3, z * 5.0 + 17, 3)
    thr = 0.58 - 0.06 * smoothstep(2.4, 3.0, z)
    rust = smoothstep(thr, thr + 0.08, st) * front * zone
    c = mix(c, RUSTC * (0.85 + 0.3 * f)[..., None] * (1 + 0.5 * fl)[..., None], rust * 0.85)
    # a little on the back and the right too
    c = mix(c, RUSTC * 0.8, smoothstep(0.64, 0.72, st) * (1 - front) * zone * 0.4)
    # and small orange-red flecks, drawn out along the grain, over the worn front left
    fk = N4.fbm(np.cos(along) * 18 + 7, np.sin(along) * 18, z * 11.0, 3)
    fz = smoothstep(0.6, 1.2, z) * (1 - smoothstep(3.7, 4.2, z))
    fleck = smoothstep(0.575, 0.625, fk) * smoothstep(-0.3, 0.5, np.cos(a + 2.1)) * fz
    c = mix(c, RUSTF * (0.9 + 0.2 * f)[..., None] * (1 + 0.4 * fl)[..., None], fleck * 0.88)
    # damp, dark foot, and the damp side away from the picture's light
    c = c * (0.62 + 0.38 * smoothstep(0.0, 0.9, z))[..., None]
    damp = smoothstep(-0.05, 0.3, np.cos(a + 0.18)) * (1 - smoothstep(4.3, 5.0, z))
    c = c * (1 - 0.65 * damp)[..., None]
    # the vine's groove shaded
    c = c * (1 - 3.0 * groove(a, z))[..., None]
    # the soft shade of the hollow behind the hanging curl, on the trunk's front right
    da = (a + 1.05 + math.pi) % (2 * math.pi) - math.pi
    c = c * (1 - 0.65 * np.exp(-(da / 0.4) ** 2 - ((z - 2.8) / 0.6) ** 2))[..., None]
    # up into the head's older, darker root, gradually
    top = smoothstep(3.9, 4.7, z)
    c = c * (1 - 0.68 * smoothstep(3.95, 4.35, z))[..., None]
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
    crease = np.clip((np.min(np.stack(k[:5] + k[6:]), axis=0) - d) / 0.08, 0, 1)
    c = c * (1 - 0.3 * crease)[..., None]
    # ambient shade from the head's own folds, the orb and the trunk
    occ = np.zeros(len(P))
    for i, dl in enumerate((0.04, 0.09, 0.18, 0.32)):
        occ += np.maximum(0.0, dl - occluders(P + Nn * dl)) / dl * 0.5 ** i
    c = c * np.clip(1 - 0.4 * occ, 0.45, 1.0)[..., None]
    c = c * (0.86 + 0.2 * np.clip(Nn[..., 2], 0, 1))[..., None]
    # the picture's light from the left and front: its lit side paler, the crown, right and back in shade
    lp = np.array((-0.75, -0.55, 0.18)) / np.linalg.norm((-0.75, -0.55, 0.18))
    c = c * (0.42 + 0.95 * smoothstep(-0.3, 0.7, Nn @ lp))[..., None]
    # the crown's upper right, turned furthest from that light, in deep shade
    c = c * (1 - 0.3 * smoothstep(0.0, 0.7, Nn @ np.array((0.72, 0.25, 0.65))))[..., None]
    h, rho, ang = socket_coords(P)
    dist = np.linalg.norm(P - _Cn, axis=-1)
    # the bowl behind the orb goes dark
    bowl = smoothstep(RS + 0.06, RS + 0.01, dist) * smoothstep(0.1, -0.05, h)
    c = c * (1 - 0.5 * bowl)[..., None]
    # the lit rim round the orb's lower left, a worn olive grey running down toward the slit
    lowleft = np.maximum(0.0, np.cos(np.radians(ang - 240.0))) ** 2.5
    rim = smoothstep(RS - 0.01, RS + 0.05, rho) * smoothstep(SLIT - 0.01, RS + 0.16, rho)
    rim = rim * lowleft * smoothstep(-0.3, -0.05, h)
    c = mix(c, rgb((180, 174, 130)) * (0.85 + 0.3 * f)[..., None] * (1 + 0.5 * smoothstep(RS + 0.08, RS + 0.18, rho))[..., None], rim * 0.9)
    # the rest of the raised rim, up the orb's left side, stays dark old root
    upleft = smoothstep(200.0, 150.0, ang) * smoothstep(80.0, 110.0, ang)
    c = c * (1 - 0.35 * upleft * np.exp(-((rho - RS - 0.06) / 0.08) ** 2))[..., None]
    slit = np.exp(-((rho - SLIT) / 0.05) ** 2) * np.maximum(0.0, np.cos(np.radians(ang - 262.0))) ** 2
    c = c * (1 - 0.8 * slit * smoothstep(-0.35, -0.1, h))[..., None]
    # the underside of the brow where it overhangs the orb
    brow = brow_weight(ang) * smoothstep(RS + 0.35, RS + 0.08, dist) * smoothstep(0.35, -0.25, Nn[..., 2])
    c = c * (1 - 0.55 * brow)[..., None]
    # the jaw ledge: its lit top a worn rusty brown, a dark slit in the crease above it
    jd, joff = jaw_sdf(P, near=True)
    jup = joff @ np.array(zk.SCREEN_UP) / np.maximum(np.linalg.norm(joff, axis=-1), 1e-6)
    on = smoothstep(0.03, 0.0, jd)
    ledge = on * smoothstep(-0.25, 0.35, jup)
    toright = smoothstep(-0.45, 0.0, P[..., 0])  # the half under the head's shade, a little paler
    lipc = rgb((162, 124, 94)) * (0.85 + 0.3 * f)[..., None] * (1 + 0.7 * toright)[..., None]
    c = mix(c, lipc, ledge * 0.8)
    c = c * (1 - 0.55 * on * smoothstep(-0.2, -0.6, jup))[..., None]
    crease_up = np.exp(-((jd - 0.05) / 0.045) ** 2) * smoothstep(0.3, 0.8, jup)
    c = c * (1 - 0.6 * crease_up)[..., None]
    # the mouth: the hollow above the ledge, in the shade of the rim
    mouth = smoothstep(0.012, 0.03, jd) * smoothstep(0.17, 0.07, jd) * smoothstep(0.1, 0.4, jup)
    c = c * (1 - 0.5 * mouth)[..., None]
    # the grain is carried by the relief where the knot's outline was smoothed
    ks = 1 + 1.2 * (1 - strand_relief(P[..., 2]))
    hh = 0.004 * f + 0.004 * m - 0.004 * cr + (0.014 * g ** 0.6 + 0.006 * fb) * w * ks
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
    m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.1
    return m


# ---- the orb -------------------------------------------------------------------

# the star glint sits a little left of centre, and a smaller spot toward the lower left rim
# (screen offsets from the orb's centre, in cells, screen up positive)
GLINT = (-0.16 * K, -0.03 * K)
GLINT2 = (-0.28 * K, -0.26 * K)
GLOW_STRENGTH = 3.3
RIM_GLOW = 0.05
HALO = (0.28, 16.0)   # the glow round the glint: strength and width in degrees
SHEEN = 0.06
INNER = 0.13          # the light gathered inside the glass, low on the right
GLASS_ROUGH, GLASS_SPEC = 0.04, 0.5


def uvsphere(name, r, c, mat, u=32, v=16, uvs=False):
    bm = bmesh.new()
    if uvs:
        bm.loops.layers.uv.new("UVMap")
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r, calc_uvs=uvs)
    for q in bm.verts:
        q.co += c
    return zk.smooth(zk.make(name, bm, [mat]), 89)


def sphere_dir(dx, dup):
    """The direction from the orb's centre to the point the classic camera
    sees at a screen offset (dx, dup) in cells from it."""
    toward = np.array(-VIEW)
    side = np.array(Vector((1, 0, 0)) * dx + zk.SCREEN_UP * dup)
    g = side / ORB_R + toward * math.sqrt(max(0.0, 1 - (side @ side) / ORB_R ** 2))
    return g / np.linalg.norm(g)


def glass_textures(W=512, H=256):
    """Body colour and glow for the polished glass, painted on the sphere's
    own UVs: a deep navy gem, a little lighter and greener on its right, with
    a hard-edged star glint, a second small spot toward the lower left rim and
    a faint blue light gathered inside it low on the right. The sky's rim comes
    from the glass itself, not from the glow, so it moves with the view."""
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    lon, lat = 2 * math.pi * (U - 0.5), math.pi * (V - 0.5)  # Blender's sphere UVs
    n = np.stack([np.cos(lat) * np.cos(lon), np.cos(lat) * np.sin(lon), np.sin(lat)], axis=-1)
    toward = np.array(-VIEW)
    facing = np.clip(n @ toward, 0, 1)
    sx, sy = n[..., 0], n @ np.array(zk.SCREEN_UP)   # screen right and up, in orb radii
    right = n @ np.array((Vector((1, 0, 0)) - VIEW * 0.3).normalized())
    body = mix(rgb((8, 13, 34)), rgb((13, 32, 50)), smoothstep(-0.2, 0.8, right))
    body = body * (0.75 + 0.25 * smoothstep(0.0, 0.8, facing))[..., None]
    # the star glint: a hard white core, four fine rays and a small grey halo
    g1 = sphere_dir(*GLINT)
    th = np.degrees(np.arccos(np.clip(n @ g1, -1, 1)))
    # the core drawn out a little up and down the screen
    tha = np.degrees(np.arccos(np.clip(n @ sphere_dir(GLINT[0], GLINT[1] + 0.03 * K), -1, 1)))
    thb = np.degrees(np.arccos(np.clip(n @ sphere_dir(GLINT[0], GLINT[1] - 0.03 * K), -1, 1)))
    e1 = np.array(Vector((1, 0, 0)))
    e1 = e1 - g1 * (e1 @ g1)
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(g1, e1)
    a = np.arctan2(n @ e2, n @ e1)
    ray = np.abs(np.cos(2 * a)) ** 48 * np.exp(-(th / 15.0) ** 2)
    core = 1 - smoothstep(8.0, 10.5, np.minimum(tha, thb))
    halo = HALO[0] * np.exp(-(th / HALO[1]) ** 2) + 0.04 * np.exp(-(th / 26.0) ** 2)
    # the second spot, smaller
    g2 = sphere_dir(*GLINT2)
    th2 = np.degrees(np.arccos(np.clip(n @ g2, -1, 1)))
    core2 = (1 - smoothstep(5.0, 7.5, th2)) + 0.05 * np.exp(-(th2 / 13.0) ** 2)
    # a faint trail of light between the two
    tr = np.full(th.shape, 180.0)
    for t in np.linspace(0.15, 0.85, 8):
        q = g1 * (1 - t) + g2 * t
        tr = np.minimum(tr, np.degrees(np.arccos(np.clip(n @ (q / np.linalg.norm(q)), -1, 1))))
    trail = 0.18 * np.exp(-(tr / 6.0) ** 2)
    # a dim grey sheen of sky over the glass's upper half
    sheen = SHEEN * smoothstep(-0.1, 0.5, sy) * smoothstep(0.2, 0.7, facing)
    glow = (np.clip(core + 0.35 * ray + halo + core2 + trail + sheen, 0, 1))[..., None] \
        * rgb((250, 248, 244))
    # a faint blue rim, only where the side out of the socket turns away from the camera
    out = smoothstep(-0.1, 0.5, n @ np.array(AX))
    rim = smoothstep(0.55, 0.15, facing) * out
    glow = glow + rgb((40, 90, 170)) * (RIM_GLOW * rim)[..., None]
    # light from the upper left gathered inside the glass, a soft crescent low on the right
    lr = (0.6 * sx - 0.8 * sy) / np.maximum(np.hypot(sx, sy), 1e-6)
    crescent = smoothstep(0.2, 0.9, lr) * smoothstep(0.2, 0.45, facing) * (1 - smoothstep(0.7, 0.92, facing))
    inner = INNER * (crescent + 0.1 * smoothstep(0.3, 1.0, facing) ** 2)
    glow = glow + rgb((40, 120, 230)) * inner[..., None]
    return body, glow


def orb_parts():
    body, gl = glass_textures()
    glass = zk.tex_mat("zm_orb_glass", zk.image("zm_orb_body", body), rough=GLASS_ROUGH)
    nt = glass.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Specular IOR Level"].default_value = GLASS_SPEC
    uvn = [q for q in nt.nodes if q.type == "UVMAP"][0]
    te = nt.nodes.new("ShaderNodeTexImage")
    te.image = zk.image("zm_orb_glow", gl)
    nt.links.new(uvn.outputs["UV"], te.inputs["Vector"])
    nt.links.new(te.outputs["Color"], b.inputs["Emission Color"])
    b.inputs["Emission Strength"].default_value = GLOW_STRENGTH
    b.inputs["IOR"].default_value = 1.5
    return [uvsphere("orb", ORB_R, ORB_C, glass, 48, 24, uvs=True)]


# ---- vines --------------------------------------------------------------------

# the vines' lit ridge sits RIDGE_TILT round from the side facing the classic
# camera toward the screen's upper left, where the picture's light comes from
UPLEFT = (Vector((-0.6, 0, 0)) + zk.SCREEN_UP * 0.8).normalized()
RIDGE_TILT = math.radians(18)
VINE_ULEN = 1.5  # cells of vine per repeat of its texture


def frames_lit(pts):
    """Sweep frames whose first normal (the texture's lit ridge) leans from
    the camera-facing side of the tube toward the screen's upper left."""
    T = []
    for i in range(len(pts)):
        T.append((pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized())
    N, prev_e, prev = [], None, None
    for t in T:
        c = -VIEW - t * (-VIEW).dot(t)
        if c.length < 0.1 and prev is not None:  # the tube runs toward the camera
            c = prev - t * prev.dot(t)
        c = c.normalized()
        e = t.cross(c).normalized()
        k = e.dot(UPLEFT)
        if abs(k) < 0.15 and prev_e is not None:
            k = e.dot(prev_e)
        e = e if k >= 0 else -e
        n = (c * math.cos(RIDGE_TILT) + e * math.sin(RIDGE_TILT)).normalized()
        if prev is not None and n.dot(prev) < 0.2:
            n = (prev - t * prev.dot(t)).normalized().lerp(n, 0.3).normalized()
        N.append(n)
        prev, prev_e = n, e
    B = [T[i].cross(N[i]) for i in range(len(pts))]
    return T, N, B


def vine_material():
    """Dry vine, two-toned: a salmon, pinkish tan body with dark flanks, a
    narrow yellow-cream highlight only along the lit ridge, faint fibres and
    worn patches along it (u along, v round from the ridge)."""
    W, H = 192, 64
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    n, n2 = zk.Noise(41), zk.Noise(42)
    au, av = 2 * math.pi * U, 2 * math.pi * V
    fib = n.fbm(np.cos(av) * 7 + np.cos(au) * 2.7, np.sin(av) * 7 + np.sin(au) * 2.7, 0.3, 3)
    blot = n2.fbm(np.cos(au) * 3.0 + np.cos(av) * 0.8, np.sin(au) * 3.0 + np.sin(av) * 0.8, 2.1, 3)
    ridge = np.cos(av)
    c = mix(rgb((30, 19, 13)), rgb((106, 68, 51)), smoothstep(-0.05, 0.55, ridge))
    # the body salmon, going khaki in places along the vine
    body = mix(rgb((144, 98, 77)), rgb((134, 114, 72)), smoothstep(0.58, 0.72, blot))
    c = mix(c, body, smoothstep(0.42, 0.78, ridge))
    # the highlight wanders a little along the vine, yellow cream to pinkish cream
    hl = smoothstep(0.9 + 0.04 * (blot - 0.5), 0.98, ridge)
    c = mix(c, mix(rgb((255, 230, 108)), rgb((255, 214, 128)), smoothstep(0.45, 0.7, blot)), hl)
    # worn, rustier patches along the body
    c = mix(c, c * np.array((0.92, 0.72, 0.62)), smoothstep(0.55, 0.7, blot) * (1 - hl) * 0.8)
    c = c * (0.88 + 0.24 * fib)[..., None]
    img = zk.image("zm_vine_col", c)
    nimg = zk.image("zm_vine_nrm", zk.normals_from_height(0.0025 * fib, VINE_ULEN / W, 2 * math.pi * 0.06 / H),
                    noncolor=True)
    m = zk.tex_mat("zm_vine", img, rough=0.55, metal=0.0, nimg=nimg, nstrength=0.8)
    m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.12
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
    zk.sweep(bm, pts, radius, seg=seg, bumps=gentle(seed), ulen=VINE_ULEN, frames_=frames_lit(pts))
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
    hit = first_hit(14.0, 42.6) or Vector(at(14.0, 42.6, float(JAW_P[0][1])))
    e = 0.01
    grad = Vector([float(head_sdf(np.array([hit + Vector(d) * e]))[0] - head_sdf(np.array([hit - Vector(d) * e]))[0])
                   for d in ((1, 0, 0), (0, 1, 0), (0, 0, 1))]).normalized()
    root = hit - grad * 0.3  # the vine grows out from inside the head
    print("ARCH ROOT", tuple(round(v, 3) for v in root))
    ctrl = [root, hit + grad * 0.03 + Vector((0, 0, 0.1)), at(12.8, 39.0, -0.25),
            at(13.1, 34, -0.15), at(14.2, 29, -0.1), at(16.2, 25.0, 0.0), at(19.1, 21.8, 0.15),
            at(23.0, 19.3, 0.3), at(27, 18.1, 0.45), at(31, 18.1, 0.56), at(33.4, 20.4, 0.5), at(33.5, 23, 0.38), at(33.6, 25.5, 0.3)]
    return vine(ctrl, taper(0.09, 0.065, 0.02), name="arch", seed=3)


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
    base = at(19.9, 22.1, 0.14)
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
    # it branches off the arch's left side, growing from inside it
    ctrl = [at(12.7, 37.4, -0.2), at(11.5, 36.4, -0.24), at(10.4, 35.6, -0.27)]
    ctrl += [on_plane(c, px - c0[0], c0[1] - row) for px, row in path]
    return vine(ctrl, taper(0.085, 0.045, 0.03, root=0.04), name="spiral", seed=5, step=0.028)


# ---- the sturdy cut --------------------------------------------------------------

# the head and all that hangs on it drop by HEAD_D; the trunk below is squashed
# to meet it, leans back into it over its upper part and gains girth
HEAD_D = Vector((0.0, 0.32, -0.85))
Z1 = 4.6                  # from here up (approved heights) the trunk moves with the head
ZA = 3.6                  # below this the trunk is squashed evenly
LEAN0 = 1.5               # the lean back starts here
GIRTH, GIRTH_Z = 0.18, 3.9   # the trunk's extra girth, fading out from GIRTH_Z to Z1
TAPER = 0.08                 # and a little more toward the ground all the way up
# the foot's girth at the ground by direction (degrees, scale), blended in below FOOT_H:
# the flare goes round the back, which the classic camera sees behind the trunk
GROUND = [(0, 1.1), (40, 1.34), (72, 1.5), (110, 1.5), (150, 1.14), (180, 1.14), (215, 1.12), (260, 1.12),
          (307, 1.1), (335, 1.07)]
FOOT_H = 1.4
GD, GS = np.array([k[0] for k in GROUND], dtype=float), np.array([k[1] for k in GROUND])
CURL_OUT = Vector((0.03, -0.13, 0.22))   # mostly toward the classic camera, so it keeps its place there
# the loop drawn in from the frame's right edge, eased in from where it leaves the ring
CURL_IN, CURL_EASE = -0.2, (0.0, 0.4, 0.85)

_ZG = np.linspace(0.0, 8.0, 1601)
_DR = 1 - smoothstep(ZA, Z1, _ZG)
_RG = np.concatenate([[0.0], np.cumsum((_DR[1:] + _DR[:-1]) / 2 * np.diff(_ZG))])
_RG /= np.interp(Z1, _ZG, _RG)


def warp(P):
    """Points of the approved trunk (..., 3) to the sturdy one."""
    P = np.asarray(P, dtype=float)
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    r = np.interp(z, _ZG, _RG)
    q = smoothstep(LEAN0, Z1, z)
    cx = pcx(np.clip(z, 0.0, 5.2))
    g = 1 + GIRTH * (1 - smoothstep(GIRTH_Z, Z1, z)) + TAPER * np.clip(1 - z / Z1, 0, 1)
    a = np.degrees(np.arctan2(y, x - cx))
    g = g + (ring_curve(GD, GS, a) - (1 + GIRTH + TAPER)) * np.clip(1 - z / FOOT_H, 0, 1) ** 2
    return np.stack([cx + (x - cx) * g, y * g + HEAD_D.y * q, z + HEAD_D.z * r], axis=-1)


def warp_object(ob):
    me = ob.data
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    me.vertices.foreach_set("co", warp(co.reshape(-1, 3)).ravel())
    me.update()
    return ob


def move_object(ob, d=HEAD_D):
    ob.data.transform(Matrix.Translation(d))
    ob.data.update()
    return ob


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
    # the ring follows the trunk, the curl hangs from where it leaves
    moved = [Vector(warp(np.array(p))) for p in ctrl]
    delta = moved[-1] - ctrl[-1] + CURL_OUT
    ctrl = moved
    c0 = CURL_C0
    c = at(c0[0], c0[1], -0.35)
    path = [(39.2, 49.8), (43.2, 49.9), (45.5, 52.6), (45.9, 57.0), (45.4, 61.0), (43.1, 63.6), (40.5, 65.0),
            (39.1, 65.4), CURL_TIP]
    ease = list(CURL_EASE) + [1.0] * (len(path) - len(CURL_EASE))
    ctrl += [on_plane(c, px - c0[0], c0[1] - row) + delta + Vector((CURL_IN * e, 0, 0))
             for (px, row), e in zip(path, ease)]
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

parts = [warp_object(pedestal(ROOT)), move_object(HEAD)]
parts += [move_object(o) for o in orb_parts() + [arch(), spiral()] + sprig()]
parts += [neck_ring_and_curl()]
for p in parts:
    print("PART", p.name, zk.tris(p))

ob = hk.finish(parts, os.path.join(OUT, "models", NAME + ".glb"),
               {"replacesTexture": "Zonmanalodestone", "replacesPiece": "zonmana"})
print("TRIS", zk.tris(ob))
print("ORB", tuple(round(v, 3) for v in ORB_C + HEAD_D), "AXIS", tuple(round(v, 3) for v in AX),
      "SEAT", tuple(round(v, 3) for v in SEAT + HEAD_D))
hk.renders(ob, os.path.join(OUT, "renders"), NAME, SPRITE, HOT, scale=4)
# the same views under a mid-grey sky, as a Unity skybox would light them
zk.grey_renders(ob, os.path.join(OUT, "renders", "grey"), NAME, SPRITE, HOT, scale=4)
CHECK = os.environ.get("ZK_CHECK")
if CHECK:
    zk.closeups(CHECK, NAME + "_head", tuple(ORB_C + HEAD_D + Vector((0.1, 0.2, 0.1))), 2.6,
                [("classic", -90, 63.4), ("front34", -45, 20), ("side", 0, 10), ("back34", 135, 25),
                 ("frontlow", -100, 5)])
    zk.closeups(CHECK, NAME + "_body", (0.3, 0.0, 3.1), 7.2, [("front", -90, 15), ("left", -150, 15),
                                                           ("right", -20, 15), ("side", 0, 8)], res=448)
    jc = Vector(JAW_P[len(JAW_P) // 2]) + HEAD_D
    zk.closeups(CHECK, NAME + "_jaw", (jc.x, jc.y + 0.2, jc.z + 0.15), 1.6,
                [("classic", -90, 63.4), ("front", -90, 10), ("left", -150, 15), ("low", -110, -10)])
    zk.closeups(CHECK, NAME + "_neck", (0.25, 0.25, 3.2), 1.8, [("front", -90, 10), ("left", -160, 15)])
    zk.closeups(CHECK, NAME + "_foot", (0.3, -0.2, 0.6), 2.8, [("frontleft", -120, 12), ("classic", -90, 63.4)])
    zk.closeups(CHECK, NAME + "_leaves", tuple(at(15.0, 17.5, 0.3) + HEAD_D), 1.1, [("classic", -90, 63.4),
                                                                                 ("front34", -45, 20), ("side", 0, 10)])
