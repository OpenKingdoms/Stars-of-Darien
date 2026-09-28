"""ZONLODE, the Zhon lodestone: two weathered posts, one hemp rope lashed
round each post and knotted round the collar of a polished bronze orb that
hangs between them, with a gold strap down its front, a gold rib off the
collar down its upper left, a navy cabochon and a horned gold crest.

    blender -b --factory-startup --python ZONLODE.py
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

NAME = "ZONLODE"
SPRITE = r"D:\OKReplace\lodes\sprites\ZONLODE.png"
HOT = (27, 49)
OUT = r"D:\OKReplace\lodes\hand"

hk.reset()

# ---- layout (cells; -Y toward the camera) ----------------------------------

PY = -0.48            # posts, rope and orb share this plane
PX = 1.344            # post centres
PR = 0.285            # post radius
PH = 5.95             # post height at the rim
ORB_C0, ORB_R0 = Vector((0.0, PY, 2.85)), 0.71   # the orb as the picture draws it
ORB_R = 0.89                         # a quarter bigger than the picture's
ORB_C = Vector((0.0, PY, 2.72))      # lowered so the collar and crest keep their height
K = ORB_R / ORB_R0
TILT = math.radians(30)   # the orb's crown leans back toward the classic camera's up
POLE = Vector((0.0, math.sin(TILT), math.cos(TILT)))
BACK = POLE.cross(Vector((1, 0, 0)))  # completes the orb frame (x, BACK, POLE)
NECK_H, NECK_R = 0.15, 0.115        # the collar neck the rope is knotted round
CAP_TOP = 0.32                        # the crest seat above the orb surface
ROPE_R = 0.044
LASH_TOP = 5.32       # the top turn of each lashing, at the post's sides
TURNS = 2.5           # tight turns per lashing
PITCH = 2 * ROPE_R * 0.96
LASH_TILT = 0.07      # the turns ride a little higher on the front than the back
LASH_BAND = (LASH_TOP - TURNS * PITCH - 0.05, LASH_TOP + 0.05)
NS = 22               # post columns
TW = 0.10             # grain twist, radians per cell
VS = 0.9              # share of the post texture given to the sides


# ---- posts -----------------------------------------------------------------

class Post:
    def __init__(self, sign, seed, uoff, band):
        rng = random.Random(seed)
        self.s, self.uoff, self.band = sign, uoff, band
        self.ph = rng.uniform(0, 6.28)
        self.noise = zk.Noise(seed)
        self.tilt_a, self.tilt = rng.uniform(0, 2 * math.pi), 0.075
        front = int(round((0.75 - TW * 2.5 / (2 * math.pi)) * NS)) % NS
        cols = [(front + d) % NS for d in (-3, -1, 2, 4, 7, -7, 10)]
        self.cracks = []
        for c in cols:
            z0 = rng.uniform(0.1, PH - 2.2)
            z1 = min(PH - 0.15, z0 + rng.uniform(1.0, 3.4))
            self.cracks.append((c, z0, z1, rng.uniform(0.05, 0.08)))
        self.split = ((front + 5 * sign) % NS, PH - rng.uniform(1.1, 1.6))
        fa = 1.5 * math.pi
        self.knots = [(fa + sign * 0.45, rng.uniform(1.5, 2.6), 0.13),
                      (fa - sign * 1.4, rng.uniform(3.4, 4.2), 0.11)]

    def axis(self, z):
        t = np.asarray(z) / PH
        x = self.s * PX + self.s * 0.05 * np.sin(math.pi * t) + 0.02 * np.sin(7 * t + self.ph)
        y = PY + 0.04 * np.sin(4.5 * t + self.ph)
        return x, y

    def rbase(self, z):
        z = np.asarray(z, dtype=float)
        return PR * (1.03 - 0.06 * z / PH) * (1 + 0.17 * np.clip(1 - z / 0.45, 0, 1) ** 2)

    def ztop(self, a):
        return PH + self.tilt * np.cos(a - self.tilt_a) + 0.025 * np.sin(3 * a + self.ph)

    def zone(self, z):
        """0 under the lashing, 1 elsewhere: no cracks or knots under the rope."""
        z0, z1 = self.band
        return 1 - smoothstep(z0 - 0.2, z0 - 0.05, z) * (1 - smoothstep(z1 + 0.05, z1 + 0.2, z))

    def crack_depth(self, col, z, core=True):
        """Groove depth at float column col: full on the crack column, a
        quarter on its neighbours (the mesh) or a narrow line (the texture)."""
        col, z = np.asarray(col, dtype=float), np.asarray(z, dtype=float)
        d = np.zeros(np.broadcast(col, z).shape)
        for c, z0, z1, dep in self.cracks + [(self.split[0], self.split[1], PH + 1.0, 0.085)]:
            dc = np.abs((col - c + NS / 2) % NS - NS / 2)
            t = np.clip((z - z0) / (z1 - z0), 0, 1)
            prof = np.where((z > z0) & (z < z1), np.sin(math.pi * t) ** 0.5, 0.0)
            if z1 > PH:
                prof = smoothstep(z0, z0 + 0.5, z)
            w = np.clip(1 - dc / 0.3, 0, 1) if core else np.where(dc < 0.5, 1.0, np.where(dc < 1.5, 0.35, 0.0))
            d = np.maximum(d, dep * prof * w)
        return d

    def knot(self, a, z):
        """(radial offset, ring and core masks) of the knots at angle a."""
        a, z = np.asarray(a, dtype=float), np.asarray(z, dtype=float)
        off = np.zeros(np.broadcast(a, z).shape)
        ring = np.zeros_like(off)
        core = np.zeros_like(off)
        for ka, kz, ks in self.knots:
            da = ((a - ka + math.pi) % (2 * math.pi) - math.pi) * PR
            rho = np.sqrt(da ** 2 + ((z - kz) * 0.55) ** 2) / ks
            off += 0.075 * np.exp(-rho ** 2) - 0.05 * np.exp(-(rho / 0.35) ** 2)
            ring = np.maximum(ring, np.exp(-((rho - 0.95) / 0.16) ** 2) + 0.9 * np.exp(-(rho / 0.22) ** 2))
            core = np.maximum(core, np.exp(-((rho - 0.5) / 0.16) ** 2))
        return off, ring, core

    def radius(self, col, z, mesh=True):
        col, z = np.asarray(col, dtype=float), np.asarray(z, dtype=float)
        a = 2 * math.pi * col / NS + TW * z
        rb = self.rbase(z)
        out = 0.04 * np.sin(3 * a + self.ph) + 0.02 * np.sin(2 * a - 0.4 * z + 2 * self.ph)
        fib = self.noise.fbm(np.cos(a) * 4.0, np.sin(a) * 4.0, z * 1.3, 2) - 0.5
        zn = self.zone(z)
        k, _, _ = self.knot(a, z)
        r = rb * (1 + out) + (0.045 * fib - self.crack_depth(col, z, core=not mesh) + k) * zn
        z0, z1 = self.band
        r -= 0.01 * smoothstep(z0 - 0.08, z0, z) * (1 - smoothstep(z1, z1 + 0.08, z))
        return r

    def surface_r(self, a, z):
        """The post's radius at a world angle a (for laying rope on it)."""
        col = ((a - TW * z) / (2 * math.pi) * NS) % NS
        i0 = int(math.floor(col))
        t = col - i0
        return float(self.radius(i0, z) * (1 - t) + self.radius((i0 + 1) % NS, z) * t)

    def build(self, mat):
        zs = [0.0, 0.07, 0.18, 0.33]
        zs += list(np.linspace(0.55, PH, 12))
        for _, kz, _ in self.knots:
            zs += [kz - 0.14, kz - 0.06, kz + 0.06, kz + 0.14]
        zs += [self.band[0] - 0.12, self.band[1] + 0.12]
        zs = sorted(zs)
        fz = [zs[0]]
        for z in zs[1:]:
            if z - fz[-1] > 0.05:
                fz.append(z)
        fz[-1] = PH
        fr = [z / PH for z in fz]
        dome = [(0.95, 0.045), (0.82, 0.09), (0.6, 0.12), (0.33, 0.135)]
        sa = self.split[0]
        rng = random.Random(self.ph)
        P = []
        for f in fr:
            ring = []
            for i in range(NS):
                a0 = 2 * math.pi * i / NS
                z = f * float(self.ztop(a0 + TW * f * PH))
                a = a0 + TW * z
                r = float(self.radius(i, z))
                ax, ay = self.axis(z)
                ring.append(Vector((float(ax) + r * math.cos(a), float(ay) + r * math.sin(a), z)))
            P.append(ring)
        # a rounded, weathered top with the split running across it
        a_s = 2 * math.pi * sa / NS + TW * PH
        sdir = Vector((math.cos(a_s), math.sin(a_s)))
        ax, ay = (float(v) for v in self.axis(PH))
        for c, h in dome:
            ring = []
            for i in range(NS):
                a = 2 * math.pi * i / NS + TW * PH
                rt = float(self.rbase(PH)) * (1 + 0.04 * math.sin(3 * a + self.ph)) * c
                q = Vector((math.cos(a), math.sin(a))) * c
                along = q.dot(sdir)
                perp = abs(q.x * sdir.y - q.y * sdir.x)
                notch = 0.075 * math.exp(-(perp / 0.09) ** 2) * sstep(-0.35, 0.3, along)
                z = PH + (float(self.ztop(a)) - PH) * c + h + rng.uniform(-0.012, 0.012) - notch
                ring.append(Vector((ax + rt * math.cos(a), ay + rt * math.sin(a), z)))
            P.append(ring)
        apex = Vector((ax, ay, PH + 0.14))
        nside = len(fr)
        nd = len(dome)

        def uv(j, i):
            u = self.uoff + 0.5 * i / NS
            if j < nside:
                return (u, VS * fr[j])
            return (u, VS + (1 - VS) * (j - nside + 1) / (nd + 1))
        bm = bmesh.new()
        zk.grid(bm, P, uv=uv, top=apex)
        zk.fix_normals(bm)
        ob = zk.make("post%d" % self.s, bm, [mat])
        return zk.smooth(ob, 38)

    def paint(self, W=128, H=512):
        """Colour and height for this post's half of the texture."""
        u = (np.arange(W) + 0.5) / W
        v = (np.arange(H) + 0.5) / H
        U, Vv = np.meshgrid(u, v)
        col = U * NS
        f = np.clip(Vv / VS, 0, 1)
        a0 = 2 * math.pi * U
        z = f * self.ztop(a0 + TW * f * PH)
        a = a0 + TW * z
        n = self.noise
        px, py = np.cos(a) * 0.3, np.sin(a) * 0.3
        C_dark, C_light = rgb((30, 27, 12)), rgb((106, 94, 52))
        base = mix(C_dark, C_light, np.clip((n.fbm(px * 6, py * 6, z * 1.0 + 3, 4) - 0.22) * 1.9, 0, 1))
        ag = a.copy()  # the grain parts round the knots
        for ka, kz, ks in self.knots:
            da = (a - ka + math.pi) % (2 * math.pi) - math.pi
            ag = ag + 0.12 * np.tanh(da * PR / (0.4 * ks)) * np.exp(-((z - kz) / (2.2 * ks)) ** 2)
        grain = n.fbm(np.cos(ag) * 9, np.sin(ag) * 9, z * 0.5 + 11, 4)
        fine = n.value(np.cos(a) * 40, np.sin(a) * 40, z * 9)
        c = base * (0.5 + 1.0 * grain[..., None]) * (0.9 + 0.2 * fine[..., None])
        # weathered grey-olive on the exposed grain
        wth = smoothstep(0.5, 0.75, n.fbm(px * 8, py * 8, z * 1.2 + 41, 3)) * smoothstep(0.45, 0.6, grain)
        c = mix(c, rgb((104, 96, 64)), wth * 0.5)
        # fine yellow-green lichen speckle, denser in places
        sp = n.value(np.cos(a) * PR * 95, np.sin(a) * PR * 95, z * 80 + 7)
        dens = smoothstep(0.3, 0.7, n.fbm(px * 4, py * 4, z * 1.3 + 21, 3))
        m_l = smoothstep(0.7, 0.8, sp) * (0.2 + 0.8 * dens)
        c = mix(c, mix(rgb((112, 112, 48)), rgb((150, 142, 62)), fine), m_l * 0.9)
        # occasional small rust flecks
        rust = n.value(np.cos(a) * PR * 60, np.sin(a) * PR * 60, z * 50 + 33)
        m_r = smoothstep(0.84, 0.9, rust) * smoothstep(0.5, 0.64, n.fbm(px * 4, py * 4, z * 1.2 + 51, 2))
        c = mix(c, rgb((124, 82, 48)), m_r * 0.9)
        # cracks, knots, damp foot
        cr = self.crack_depth(col, z, core=True) / 0.05
        halo = np.clip(self.crack_depth(col + 0.45, z) + self.crack_depth(col - 0.45, z), 0, 0.05) / 0.05
        zn = self.zone(z)
        c = c * (1 - 0.35 * halo[..., None] * zn[..., None])
        c = mix(c, rgb((14, 12, 8)), np.clip(cr, 0, 1) * zn)
        _, ring, core = self.knot(a, z)
        c = mix(c, rgb((24, 19, 11)), np.clip(ring, 0, 1) * 0.85 * zn)
        c = mix(c, rgb((96, 84, 54)), core * 0.55 * zn)
        c = c * (0.72 + 0.28 * smoothstep(0.0, 0.5, z))[..., None]
        z0, z1 = self.band
        c = c * (1 - 0.3 * smoothstep(z0 - 0.1, z0, z) * (1 - smoothstep(z1, z1 + 0.1, z)))[..., None]
        # the end grain on top: weathered grey with growth rings and the split
        top = Vv >= VS
        rr = 1 - (Vv - VS) / (1 - VS)
        rings = 0.5 + 0.5 * np.sin(2 * math.pi * (rr * 6 + 0.4 * n.fbm(px * 8, py * 8, 3, 2)))
        tc = mix(rgb((26, 19, 9)), rgb((58, 45, 22)), n.fbm(px * 12, py * 12, 5, 3))
        tc = mix(tc, rgb((70, 58, 30)), smoothstep(0.6, 0.95, 1 - rr) * 0.4)  # the weathered edge
        tc = tc * (0.88 + 0.12 * rings[..., None])
        a_s = 2 * math.pi * self.split[0] / NS
        dsp = np.abs((a0 - a_s + math.pi) % (2 * math.pi) - math.pi)
        tc = mix(tc, rgb((9, 7, 4)), np.exp(-(dsp * rr / 0.05) ** 2) * (rr > 0.25))
        c = np.where(top[..., None], tc, c)
        h = 0.009 * (grain - 0.5) + 0.002 * (fine - 0.5) - 0.012 * np.clip(cr, 0, 1) * zn + 0.0015 * m_l
        h = np.where(top, 0.003 * rings, h)
        return c, h


# ---- rope ------------------------------------------------------------------

def rope_texture(W=64, H=64):
    """A three-ply hemp lay: u runs along the rope (one lay per unit), v round it."""
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, Vv = np.meshgrid(u, v)
    n = zk.Noise(77)
    g = (3 * (Vv + U)) % 1.0
    ply = np.sin(math.pi * g) ** 0.6
    fib = n.value((Vv + U) * 3 * 16, (U - Vv) * 24, 0.5)
    c = mix(rgb((90, 84, 50)), rgb((192, 180, 112)), ply * (0.8 + 0.2 * fib))
    return c, 0.012 * ply + 0.002 * fib


def neck_frame():
    nc = ORB_C + POLE * (ORB_R + NECK_H)
    return nc, Vector((1, 0, 0)), BACK


def helix(post, a0, dirn, z0, z1, turns, per_turn=24, sink=None):
    """Turns laid on the post's surface from (a0, z0) to z1, riding LASH_TILT
    higher on the front than the back; sink(t) pulls the rope into the post."""
    pts = []
    n = max(2, int(round(turns * per_turn)))
    for k in range(n + 1):
        t = k / n
        a = a0 + dirn * 2 * math.pi * turns * t
        z = z0 + (z1 - z0) * t - LASH_TILT * math.sin(a)
        r = post.surface_r(a, z) + ROPE_R * 0.85 - (sink(t) if sink else 0.0)
        ax, ay = post.axis(z)
        pts.append(Vector((float(ax) + r * math.cos(a), float(ay) + r * math.sin(a), z)))
    return pts


def lashing(post):
    """From where the V rope arrives on the inner side: tight turns up the
    post, the end tucked in under the top turn at the back."""
    s = post.s
    a0 = math.radians(-20.0) if s < 0 else math.radians(-160.0)
    z0 = LASH_TOP - TURNS * PITCH
    wraps = helix(post, a0, s, z0, LASH_TOP, TURNS)
    a1 = a0 + s * 2 * math.pi * TURNS
    tuck = helix(post, a1, s, LASH_TOP, LASH_TOP - 0.05, 0.12, sink=lambda t: ROPE_R * 1.9 * t)
    return wraps + tuck[1:]


def tail(post, mat, seed):
    """The rope's end, pulled out from under the bottom turn on the front
    and hanging, round to the frayed tip."""
    s = post.s
    a = math.radians(-128.0) if s < 0 else math.radians(-52.0)
    z0 = LASH_TOP - TURNS * PITCH - LASH_TILT * math.sin(a)
    ax, ay = post.axis(z0)
    out = Vector((math.cos(a), math.sin(a), 0))
    side = Vector((0, 0, 1)).cross(out) * s

    def p(r, dz, sd=0.0):
        return Vector((float(ax), float(ay), z0 + dz)) + out * r + side * sd
    rs = post.surface_r(a, z0)
    ctrl = [p(rs - 0.04, 0.04), p(rs + ROPE_R * 0.4, -0.02), p(rs + ROPE_R * 0.95, -0.1, 0.01),
            p(rs + ROPE_R * 1.05, -0.2, 0.025), p(rs + ROPE_R * 1.2, -0.3, 0.03)]
    pts = zk.resample(zk.spline(ctrl, 8), 0.024)
    bm = bmesh.new()
    zk.sweep(bm, pts, lambda q: ROPE_R * (0.95 + 0.12 * sstep(0.8, 1.0, q)), seg=8, ulen=0.3)
    ob = zk.smooth(zk.make("tail", bm, [mat]), 70)
    d = (pts[-1] - pts[-2]).normalized()
    return [ob, frayed(pts[-1], d, out, mat, seed)]


def frayed(tip, d, out, mat, seed):
    """Loose fibres splaying a little from the rope's cut end."""
    rng = random.Random(seed)
    side = d.cross(out).normalized()
    bm = bmesh.new()
    for k in range(7):
        a = 2 * math.pi * k / 7 + rng.uniform(-0.3, 0.3)
        rad = out * math.cos(a) + side * math.sin(a)
        p0 = tip - d * 0.01 + rad * ROPE_R * 0.5
        ln = rng.uniform(0.04, 0.07)
        p2 = p0 + d * ln + rad * ln * rng.uniform(0.15, 0.35)
        pts = zk.resample(zk.spline([p0, (p0 + p2) / 2 + rad * 0.004, p2], 3), 0.014)
        zk.sweep(bm, pts, lambda q: 0.011 * (1 - 0.85 * q) + 0.001, seg=4, ulen=0.3)
    zk.fix_normals(bm)
    return zk.smooth(zk.make("fibres", bm, [mat]), 70)


def rope(posts, mat):
    """One rope: tucked into the left lashing, down to the orb's collar,
    round it one and a half times, and up into the right lashing."""
    nc, ex, ey = neck_frame()
    rn = NECK_R + ROPE_R * 0.85
    loop = []
    n = 36
    for k in range(n + 1):
        t = k / n
        psi = math.pi + 3 * math.pi * t  # from the left side, round the front, one and a half turns
        h = -0.045 + 0.09 * t
        loop.append(nc + (ex * math.cos(psi) + ey * math.sin(psi)) * rn + POLE * h)
    pl = lashing(posts[0])
    pr = lashing(posts[1])
    pl.reverse()

    def v_rope(a, ta, b, tb):
        mid = (a + b) / 2 + Vector((0, 0, -0.04))
        ctrl = [a, a + ta * 0.16, mid, b - tb * 0.16, b]
        return zk.resample(zk.spline(ctrl, 8), 0.08)[1:-1]
    # tangents where the rope leaves the loop and joins each lashing
    tl = (loop[1] - loop[0]).normalized()
    tr = (loop[-1] - loop[-2]).normalized()
    tpl = (pl[-1] - pl[-2]).normalized()
    tpr = (pr[1] - pr[0]).normalized()
    left = v_rope(pl[-1], tpl, loop[0], tl)
    right = v_rope(loop[-1], tr, pr[0], tpr)
    pts = pl + left + loop + right + pr
    bm = bmesh.new()
    zk.sweep(bm, pts, lambda s: ROPE_R, seg=8, ulen=0.3)
    ob = zk.smooth(zk.make("rope", bm, [mat]), 70)
    return [ob] + tail(posts[0], mat, 11) + tail(posts[1], mat, 12)


# ---- orb, straps, gem, collar and crest --------------------------------------

def orb_point(psi, theta, r=ORB_R):
    """A point on the orb in its tilted frame: theta from the crown."""
    return ORB_C + (POLE * math.cos(theta) + (Vector((1, 0, 0)) * math.cos(psi) + BACK * math.sin(psi))
                    * math.sin(theta)) * r


ORB_SEG, ORB_RINGS = 64, 32
OCX, OCY = zk.screen(ORB_C, HOT)   # the orb's centre in the picture
OCX0, OCY0 = zk.screen(ORB_C0, HOT)
RPX = ORB_R * 16


def pic(x, row):
    """A mark placed on the picture's orb, carried onto the bigger one."""
    return OCX + (x - OCX0) * K, OCY + (row - OCY0) * K


GLINT = (*pic(24.6, 30.0), 1.3 * K, 2.1 * K)   # the hot spot: picture x, row and its half-sizes in px


def glint(P, N, cap_only=False, parts=False):
    """The hot spot on the upper left front: a hard white core in a soft
    bloom, with a thin streak running down under it (or with cap_only the
    gleam along the collar's lower left), for points P with normals N
    (arrays). With parts, the core, bloom and streak come back apart."""
    sx = HOT[0] + 16 * P[..., 0]
    sy = HOT[1] - (zk.SY * P[..., 1] + zk.SZ * P[..., 2])
    gx, gy, rx, ry = GLINT
    front = smoothstep(0.1, 0.35, N @ np.array(-zk.VIEW))
    d = np.sqrt(((sx - gx) / rx) ** 2 + ((sy - gy) / ry) ** 2)
    core = (1 - smoothstep(0.5, 0.95, d)) * front
    bloom = np.exp(-(d / 2.4) ** 2) * front
    tx, ty = pic(23.9, 35.2)
    dt = np.sqrt(((sx - tx) / (0.75 * K)) ** 2 + ((sy - ty) / (3.4 * K)) ** 2)
    tail = 0.6 * (1 - smoothstep(0.35, 1.0, dt)) * front
    if parts:
        return core, bloom, tail
    cx, cy = pic(21.2, 26.8)
    cap = 0.85 * np.exp(-((sx - cx) / (2.2 * K)) ** 2 - ((sy - cy) / (1.3 * K)) ** 2)
    if cap_only:
        return cap * front
    return np.clip(np.maximum(core + 0.4 * bloom, tail), 0, 1)


def orb(mat):
    seg, rings = ORB_SEG, ORB_RINGS
    P = [[orb_point(2 * math.pi * i / seg, math.pi * (rings - j) / rings) for i in range(seg)]
         for j in range(1, rings)]
    bm = bmesh.new()
    zk.grid(bm, P, uv=lambda j, i: (i / seg, (j + 1) / rings), top=orb_point(0, 0),
            bottom=orb_point(0, math.pi))
    zk.fix_normals(bm)
    return zk.smooth(zk.make("orb", bm, [mat]), 80)


def orb_paint(W=512, H=256):
    """Polished bronze: a dark ground reflected round the lower rim under a
    soft horizon, a bright band of sky just above it fading to a warm mid
    tone over the crown, a hard hot spot on the upper left, a warm rim light
    low on the right and a faint glow from within. Returns colour,
    metal-rough, emission and height."""
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    psi, th = 2 * math.pi * U, math.pi * (1 - V)
    ex, eb, ep = (np.array(q) for q in (Vector((1, 0, 0)), BACK, POLE))
    n = (ep * np.cos(th)[..., None] + (ex * np.cos(psi)[..., None] + eb * np.sin(psi)[..., None])
         * np.sin(th)[..., None])
    P = np.array(ORB_C) + n * ORB_R
    sx, sy = n[..., 0], n @ np.array(zk.SCREEN_UP)     # screen right and up, in orb radii
    facing = n @ np.array(-zk.VIEW)
    nz = zk.Noise(31)
    Q = n * 3.0
    blot = nz.fbm(Q[..., 0], Q[..., 1], Q[..., 2], 4)
    fine = nz.fbm(Q[..., 0] * 7, Q[..., 1] * 7, Q[..., 2] * 7 + 5, 3)
    # the world as a camera at the classic elevation facing each point would see it reflected
    # there, so the horizon runs round the orb as a level ring
    ce, se = math.cos(zk.E), math.sin(zk.E)
    nh = np.sqrt(np.clip(1 - n[..., 2] ** 2, 0, 1))
    rz = -se + 2 * (ce * nh + se * n[..., 2]) * n[..., 2]
    lit = smoothstep(-0.6, 0.8, -0.75 * sx + 0.65 * sy + 0.25 * (blot - 0.5))   # the picture's light, upper left
    ground = mix(rgb((34, 20, 8)), rgb((84, 54, 22)), smoothstep(-0.9, -0.1, rz))
    sky = mix(rgb((238, 196, 114)), rgb((196, 142, 66)), smoothstep(0.05, 0.3, rz))
    sky = mix(sky, rgb((150, 102, 44)), smoothstep(0.35, 0.9, rz))
    sky = sky * (0.7 + 0.45 * lit)[..., None]
    c = mix(ground, sky, smoothstep(-0.12, 0.1, rz + 0.06 * (fine - 0.5)))
    # a broad warm sheen round the hot spot
    sxp, syp = HOT[0] + 16 * P[..., 0], HOT[1] - (zk.SY * P[..., 1] + zk.SZ * P[..., 2])
    hx, hy = pic(23.0, 31.5)
    halo = np.exp(-((sxp - hx) / (4.0 * K)) ** 2 - ((syp - hy) / (6.0 * K)) ** 2) * smoothstep(0.1, 0.4, facing)
    c = mix(c, rgb((222, 184, 110)), halo * 0.45)
    # darker right of the strap, as the picture's dark reflection there
    shade = np.exp(-((sx - 0.34) / 0.15) ** 2 - ((sy + 0.02) / 0.45) ** 2) * smoothstep(0.0, 0.3, facing)
    c = c * (1 - 0.45 * shade)[..., None]
    # a faint tarnish and the red-violet specks on the right, kept from the old bronze
    ox = smoothstep(0.1, 0.8, 0.9 * sx - 0.5 * sy + 0.5 * (blot - 0.5))
    c = mix(c, rgb((60, 38, 16)), ox * 0.25)
    spk = nz.value(Q[..., 0] * 11 + 40, Q[..., 1] * 11, Q[..., 2] * 11)
    m_s = smoothstep(0.78, 0.86, spk) * smoothstep(0.0, 0.3, sx) * smoothstep(0.35, 0.6, blot)
    c = mix(c, rgb((110, 58, 70)), m_s * 0.45)
    # warm light thrown back up onto the lower right rim
    toward = (0.6 * sx - 0.8 * sy) / np.maximum(np.hypot(sx, sy), 1e-6)
    rim = smoothstep(0.03, 0.12, facing) * (1 - smoothstep(0.24, 0.4, facing)) * smoothstep(0.5, 0.9, toward)
    c = mix(c, rgb((240, 158, 74)), rim * 0.85)
    # the hot spot: white in its hard core, golden where it blooms and down its streak
    core, bloom, streak = glint(P, n, parts=True)
    g = np.clip(np.maximum(core + 0.4 * bloom, streak), 0, 1)
    c = mix(c, rgb((246, 212, 136)), np.clip(0.9 * bloom + streak, 0, 1))
    c = mix(c, rgb((255, 248, 222)), core)
    rough = 0.12 + 0.05 * ox + 0.03 * shade - 0.05 * g
    metal = 0.7 - 0.1 * ox
    mr = np.stack([np.zeros_like(rough), rough, metal], axis=-1)
    # the hot spot burns white; a faint amber glow from within fills the face
    glow = smoothstep(0.3, 1.0, facing) ** 2 * (1 - 0.8 * core)
    emit = (rgb((255, 244, 214)) * core[..., None] + rgb((255, 200, 110)) * (0.22 * bloom + 0.35 * streak)[..., None]
            + rgb((255, 140, 50)) * (0.2 * glow)[..., None]
            + rgb((255, 150, 70)) * (0.06 * rim)[..., None])
    h = 0.0003 * fine
    return c, mr, emit, h


def bronze_material(name, col, mr, emit, emit_strength, nimg=None, nstrength=1.0, spec=0.2):
    """Colour, metal-rough (green rough, blue metal, as glTF packs them) and
    emission textures on one set of UVs."""
    m = zk.tex_mat(name, col, nimg=nimg, nstrength=nstrength)
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    uvn = [q for q in nt.nodes if q.type == "UVMAP"][0]
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = mr
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(uvn.outputs["UV"], t.inputs["Vector"])
    nt.links.new(t.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], b.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], b.inputs["Metallic"])
    if emit is not None:
        te = nt.nodes.new("ShaderNodeTexImage")
        te.image = emit
        nt.links.new(uvn.outputs["UV"], te.inputs["Vector"])
        nt.links.new(te.outputs["Color"], b.inputs["Emission Color"])
        b.inputs["Emission Strength"].default_value = emit_strength
    b.inputs["Specular IOR Level"].default_value = spec  # low keeps a dull patina from greying
    return m


def strap_path(psi, t0, t1, r):
    """Points on the great circle through the crown at psi, theta t0 to t1."""
    n = max(4, int(round(abs(t1 - t0) / (2 * math.pi) * 64)))
    return [orb_point(psi, t0 + (t1 - t0) * k / n, r) for k in range(n + 1)]


def strap(psi, mat, t0=0.0, t1=2 * math.pi, width=0.065, tip=0.0):
    """A flat strap on the orb along the great circle through the crown at
    psi; a partial one (tip > 0) narrows over its last tip of length."""
    pts = strap_path(psi, t0, t1, ORB_R + 0.014)
    m = Vector((math.cos(psi), 0, 0)) + BACK * math.sin(psi)
    side = POLE.cross(m).normalized()
    L = sum((pts[i + 1] - pts[i]).length for i in range(len(pts) - 1))

    def wid(s):
        if tip <= 0:
            return width
        return width * (0.25 + 0.75 * min(1.0, (1 - s) / tip) ** 0.7)
    bm = bmesh.new()
    zk.sweep(bm, pts, wid, seg=4, flat=0.28, up=tuple(side), a_off=math.pi / 4, ulen=L)
    return zk.smooth(zk.make("strap", bm, [mat]), 40)


def strap_paint(psi, t0, t1, fade, W=256):
    """Along one strap: pale burnished gold near the collar, darkening to
    oxidised bronze further down (fade gives the picture rows over which it
    turns), with the glint where it crosses the burnished spot."""
    th = t0 + (t1 - t0) * (np.arange(W) + 0.5) / W
    n = np.array([np.array(orb_point(psi, t, 1.0) - ORB_C) for t in th])
    P = np.array(ORB_C) + n * (ORB_R + 0.03)
    # the front run's picture rows set the fade; the back mirrors it by the angle from the crown
    a = np.minimum(np.abs(th), np.abs(2 * math.pi - th))
    row = np.array([zk.screen(ORB_C + Vector(q) * ORB_R, HOT)[1] for q in n])
    front = n @ np.array(-zk.VIEW) > 0
    o = np.argsort(row[front])
    a_rows = np.interp(fade, row[front][o], a[front][o])
    k = smoothstep(a_rows[0], a_rows[1], a)
    gold, mid, pat = rgb((240, 210, 120)), rgb((175, 135, 65)), rgb((70, 46, 22))
    c = np.where((k < 0.5)[..., None], mix(gold, mid, k * 2), mix(mid, pat, k * 2 - 1))
    g = glint(P, n)
    c = mix(c, rgb((246, 244, 222)), np.clip(g * 1.3, 0, 1))
    rough = 0.22 + 0.33 * k
    metal = 0.5 - 0.2 * k
    mr = np.stack([np.zeros_like(rough), rough, metal], axis=-1)
    emit = rgb((255, 250, 232)) * smoothstep(0.35, 1.0, g)[..., None]

    def rep(x):
        return np.repeat(x[None], 4, axis=0)
    return rep(c), rep(mr), rep(emit)


# the rib's course along the orb's upper left edge, in picture pixels
RIB = [pic(x, row) for x, row in ((24.8, 25.2), (23.4, 25.7), (21.3, 27.1), (19.3, 28.7), (17.9, 30.3),
                                  (16.9, 32.0), (16.4, 33.7), (16.2, 35.4))]


def lift(x, row, fmin=0.16):
    """The orb's outward normal where the classic camera sees picture pixel
    (x, row), kept at least fmin toward the camera."""
    sx, sy = (x - OCX) / RPX, (OCY - row) / RPX
    q, lim = math.hypot(sx, sy), math.sqrt(1 - fmin * fmin)
    if q > lim:
        sx, sy = sx * lim / q, sy * lim / q
    d = math.sqrt(max(0.0, 1 - sx * sx - sy * sy))
    return (Vector((1, 0, 0)) * sx + zk.SCREEN_UP * sy - zk.VIEW * d).normalized()


def rib_path():
    ctrl = [ORB_C + lift(x, row) * ORB_R for x, row in RIB]
    pts = zk.resample(zk.spline(ctrl, 8), 0.02)
    return [ORB_C + (p - ORB_C).normalized() * (ORB_R + 0.006) for p in pts]


def rim_rib(mat):
    """A gold band from under the collar down the orb's upper left edge:
    broad and flat where it leaves the collar, narrowing into a round rib
    that ends a little below the equator."""
    pts = rib_path()
    T = [(pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized() for i in range(len(pts))]
    N = [(p - ORB_C).normalized().cross(t).normalized() for p, t in zip(pts, T)]
    B = [t.cross(n) for t, n in zip(T, N)]
    L = sum((pts[i + 1] - pts[i]).length for i in range(len(pts) - 1))
    bm = bmesh.new()
    zk.sweep(bm, pts, lambda s: 0.042 + 0.082 * (1 - s) - 0.016 * sstep(0.85, 1.0, s), seg=12, flat=0.2,
             bumps=lambda s, a: 3.4 * sstep(0.2, 0.6, s) * math.sin(a) ** 2, frames_=(T, N, B), ulen=L)
    return zk.smooth(zk.make("rib", bm, [mat]), 80)


def rib_paint(W=128):
    """Bright gold along the rib, burnished where it leaves the collar and
    browning toward its end."""
    pts = rib_path()
    L = [0.0]
    for i in range(1, len(pts)):
        L.append(L[-1] + (pts[i] - pts[i - 1]).length)
    t = (np.arange(W) + 0.5) / W
    P = np.array([np.interp(t * L[-1], L, [p[k] for p in pts]) for k in range(3)]).T
    n = (P - np.array(ORB_C)) / np.linalg.norm(P - np.array(ORB_C), axis=-1, keepdims=True)
    k = smoothstep(0.72, 1.0, t)
    c = mix(mix(rgb((244, 206, 96)), rgb((236, 182, 74)), smoothstep(0.2, 0.6, t)), rgb((118, 72, 28)), k)
    g = glint(P, n, cap_only=True)
    c = mix(c, rgb((250, 226, 120)), np.clip(g * 1.2, 0, 1))
    mr = np.stack([np.zeros_like(t), 0.22 + 0.3 * k, 0.3 + 0.0 * k], axis=-1)
    emit = rgb((255, 214, 96)) * (0.5 * smoothstep(0.3, 1.0, g))[..., None]

    def rep(x):
        return np.repeat(x[None], 4, axis=0)
    return rep(c), rep(mr), rep(emit)


def gem(gem_mat, gold):
    # where the picture has it, on the face the classic camera sees
    gx, gy = pic(29.5, 31)
    sx, sy = (gx - OCX) / RPX, (OCY - gy) / RPX
    toward = -zk.VIEW
    n = (Vector((1, 0, 0)) * sx + zk.SCREEN_UP * sy + toward * math.sqrt(1 - sx * sx - sy * sy)).normalized()
    base = ORB_C + n * ORB_R
    e1 = (Vector((1, 0, 0)) - n * n.x).normalized()
    e2 = n.cross(e1)
    prof = [(0.118, -0.03), (0.118, 0.004), (0.105, 0.03), (0.078, 0.048), (0.042, 0.058)]
    seg = 16
    P = [[base + (e1 * math.cos(2 * math.pi * i / seg) + e2 * math.sin(2 * math.pi * i / seg)) * r + n * h
          for i in range(seg)] for r, h in prof]
    bm = bmesh.new()
    zk.grid(bm, P, top=base + n * 0.061)
    zk.fix_normals(bm)
    g = zk.smooth(zk.make("gem", bm, [gem_mat]), 70)
    ring = [base + (e1 * math.cos(2 * math.pi * k / seg) + e2 * math.sin(2 * math.pi * k / seg)) * 0.124
            + n * 0.004 for k in range(seg + 1)]
    bm = bmesh.new()
    zk.sweep(bm, ring, lambda s: 0.015, seg=4, up=tuple(n))
    b = zk.smooth(zk.make("bezel", bm, [gold]), 60)
    return [g, b]


def collar(mat):
    """A gold cap on the crown, a neck for the rope, and a flange the crest sits on."""
    top = ORB_C + POLE * ORB_R
    n0, n1 = NECK_H - 0.035, NECK_H + 0.035
    prof = [(0.25, -0.06), (0.245, -0.025), (0.21, 0.015), (0.165, 0.045), (0.135, 0.06), (NECK_R, n0 - 0.01),
            (NECK_R, n1 + 0.01), (0.155, CAP_TOP - 0.03), (0.16, CAP_TOP - 0.015), (0.14, CAP_TOP),
            (0.08, CAP_TOP + 0.01)]
    seg = 16
    ex, ey = Vector((1, 0, 0)), BACK
    P = [[top + (ex * math.cos(2 * math.pi * i / seg) + ey * math.sin(2 * math.pi * i / seg)) * r + POLE * h
          for i in range(seg)] for r, h in prof]
    bm = bmesh.new()
    zk.grid(bm, P, top=top + POLE * (CAP_TOP + 0.012))
    zk.fix_normals(bm)
    return zk.smooth(zk.make("collar", bm, [mat]), 45)


LEAN = math.radians(28)   # the crest's own lean, close to the crown's
CR = 0.46                 # crest arc radius
TIPS = math.radians(25)   # horn tips this far above the arc's centre line


def crest(gold, dark):
    """A thick horned crescent rising from the flange; its inner face is dark."""
    up = Vector((0, math.sin(LEAN), math.cos(LEAN)))
    ex = Vector((1, 0, 0))
    nf = ex.cross(up)  # faces the classic camera
    wr_b = 0.125
    base = ORB_C + POLE * (ORB_R + CAP_TOP - 0.01)
    ctr = base + up * (CR + wr_b * 0.55)
    oct_ = [(1, 0.55), (0.55, 1), (-0.55, 1), (-1, 0.4), (-1, -0.4), (-0.55, -1), (0.55, -1), (1, -0.55)]
    inner = {3}  # the face from vertex 3 to 4 looks into the crescent
    n = 26
    rings = []
    bm = bmesh.new()
    for k in range(n + 1):
        s = k / n
        phi = math.pi - TIPS + (math.pi + 2 * TIPS) * s
        rdir = ex * math.cos(phi) + up * math.sin(phi)
        c = ctr + rdir * CR
        w = math.sin(math.pi * s)
        wr = wr_b * w ** 0.5
        wn = 0.15 * w ** 0.8
        if w < 1e-3:
            rings.append([bm.verts.new(c + rdir * 0.01)])
            continue
        rings.append([bm.verts.new(c + rdir * (wr * a) + nf * (wn * b)) for a, b in oct_])
    m = len(oct_)
    for k in range(n):
        A, B = rings[k], rings[k + 1]
        for q in range(m):
            q2 = (q + 1) % m
            if len(A) == 1:
                f = bm.faces.new((A[0], B[q2], B[q]))
            elif len(B) == 1:
                f = bm.faces.new((A[q], A[q2], B[0]))
            else:
                f = bm.faces.new((A[q], A[q2], B[q2], B[q]))
            f.material_index = 1 if q in inner else 0
    zk.fix_normals(bm)
    return zk.smooth(zk.make("crest", bm, [gold, dark]), 40)


# ---- materials -------------------------------------------------------------

posts = [Post(-1, 3, 0.0, LASH_BAND), Post(1, 7, 0.5, LASH_BAND)]
cl, hl = posts[0].paint()
cr_, hr = posts[1].paint()
POST_TEX = zk.image("zl_post_col", np.concatenate([cl, cr_], axis=1))
POST_NRM = zk.normal_image("zl_post_nrm", np.concatenate([hl, hr], axis=1),
                           2 * math.pi * PR / 128, PH / (512 * VS), 1.0, wrap_u=False)
BARK = zk.tex_mat("zl_bark", POST_TEX, rough=0.9, nimg=POST_NRM, nstrength=1.0)
rc, rh = rope_texture()
ROPE_TEX = zk.image("zl_rope_col", rc)
ROPE_NRM = zk.normal_image("zl_rope_nrm", rh, 0.3 / 64, 2 * math.pi * ROPE_R / 64, 1.0, wrap_u=True, wrap_v=True)
ROPE = zk.tex_mat("zl_rope", ROPE_TEX, rough=0.95, nimg=ROPE_NRM)
GOLD = hk.pbr("zl_gold", lin((228, 188, 96)), rough=0.32, metal=0.5)
DARK = hk.pbr("zl_crest_inner", lin((128, 120, 102)), rough=0.42, metal=0.45)
EMIT = 3.2   # emission strength of the burnished glint
oc, omr, oem, oh = orb_paint()
BRONZE = bronze_material("zl_bronze", zk.image("zl_orb_col", oc), zk.image("zl_orb_mr", omr, noncolor=True),
                         zk.image("zl_orb_emit", oem), EMIT,
                         nimg=zk.normal_image("zl_orb_nrm", oh, 2 * math.pi * ORB_R / oh.shape[1],
                                              math.pi * ORB_R / oh.shape[0], 1.0), nstrength=0.6, spec=0.5)
GEM = hk.pbr("zl_gem", lin((14, 22, 50)), rough=0.06, emit=lin((20, 42, 110)), strength=0.25)

PSI1 = math.pi / 2 - math.radians(5.5)   # the strap runs just left of the crown's meridian
S1 = strap_paint(PSI1, 0.0, 2 * math.pi, (pic(0, 32.5)[1], pic(0, 39.5)[1]))
S2 = rib_paint()
STRAP1 = bronze_material("zl_strap", zk.image("zl_strap_col", S1[0]), zk.image("zl_strap_mr", S1[1], noncolor=True),
                         zk.image("zl_strap_emit", S1[2]), EMIT)
RIBM = bronze_material("zl_rib", zk.image("zl_rib_col", S2[0]), zk.image("zl_rib_mr", S2[1], noncolor=True),
                      zk.image("zl_rib_emit", S2[2]), EMIT)

# ---- build -----------------------------------------------------------------

parts = [posts[0].build(BARK), posts[1].build(BARK)] + rope(posts, ROPE)
parts += [orb(BRONZE), strap(PSI1, STRAP1), rim_rib(RIBM)]
parts += gem(GEM, GOLD)
parts += [collar(GOLD), crest(GOLD, DARK)]
for p in parts:
    print("PART", p.name, zk.tris(p))

ob = hk.finish(parts, os.path.join(OUT, "models", NAME + ".glb"),
               {"replacesTexture": "zhonlode", "replacesPiece": "zonlode"})
print("TRIS", zk.tris(ob))
hk.renders(ob, os.path.join(OUT, "renders"), NAME, SPRITE, HOT, scale=4)
# the same views under a mid-grey sky, to check the metals don't go pale
zk.grey_renders(ob, os.path.join(OUT, "renders", "grey"), NAME, SPRITE, HOT, scale=4)
CHECK = os.environ.get("ZK_CHECK")
if CHECK:
    zk.closeups(CHECK, NAME + "_lash", (-PX, PY, LASH_TOP - 0.1), 1.3,
                [("front", -90, 20), ("front34", -45, 25), ("side", 180, 10), ("back34", 135, 25)])
    zk.closeups(CHECK, NAME + "_orb", tuple(ORB_C + Vector((0, 0, 0.3))), 2.2, [("classic", -90, 63.4),
                                                                              ("low", -60, 15), ("q34", -130, 25),
                                                                              ("side", 180, 10), ("back", 90, 25)])
