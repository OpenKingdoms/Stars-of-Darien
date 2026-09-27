"""ZONLODE, the Zhon lodestone: two weathered posts, one hemp rope lashed
round each post and knotted round the collar of a brass orb that hangs
between them, with gold straps, a navy cabochon and a horned gold crest.

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
ORB_C = Vector((0.0, PY, 2.85))
ORB_R = 0.71
TILT = math.radians(45)   # the orb's crown leans back toward the classic camera's up
POLE = Vector((0.0, math.sin(TILT), math.cos(TILT)))
BACK = POLE.cross(Vector((1, 0, 0)))  # completes the orb frame (x, BACK, POLE)
NECK_H, NECK_R = 0.095, 0.115       # the collar neck the rope is knotted round
CAP_TOP = 0.17                        # the crest seat above the orb surface
ROPE_R = 0.05
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
        tc = mix(rgb((58, 52, 40)), rgb((92, 82, 64)), n.fbm(px * 12, py * 12, 5, 3))
        tc = tc * (0.88 + 0.12 * rings[..., None])
        a_s = 2 * math.pi * self.split[0] / NS
        dsp = np.abs((a0 - a_s + math.pi) % (2 * math.pi) - math.pi)
        tc = mix(tc, rgb((20, 17, 11)), np.exp(-(dsp * rr / 0.05) ** 2) * (rr > 0.25))
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
    c = mix(rgb((92, 88, 60)), rgb((190, 178, 126)), ply * (0.8 + 0.2 * fib))
    return c, 0.012 * ply + 0.002 * fib


def neck_frame():
    nc = ORB_C + POLE * (ORB_R + NECK_H)
    return nc, Vector((1, 0, 0)), BACK


def helix(post, a0, dirn, z0, z1, turns, lift=None, per_turn=12):
    pts = []
    n = max(2, int(round(turns * per_turn)))
    for k in range(n + 1):
        t = k / n
        a = a0 + dirn * 2 * math.pi * turns * t
        z = z0 + (z1 - z0) * t
        r = post.surface_r(a, z) + ROPE_R * 0.8 + (lift(t) if lift else 0.0)
        ax, ay = post.axis(z)
        pts.append(Vector((float(ax) + r * math.cos(a), float(ay) + r * math.sin(a), z)))
    return pts


def lashing(post):
    """From where the V rope arrives at the top: wraps down the post, one
    turn crossing back up over them, and a short tail hanging beside the
    V rope. Returns (points, count)."""
    s = post.s
    z0, z1 = post.band
    a0 = math.atan2(-0.9, -0.44) if s > 0 else math.atan2(-0.9, 0.44)
    wraps = helix(post, a0, s, z1, z0, 3.5, per_turn=9)
    a1 = a0 + s * 2 * math.pi * 3.5
    cross = helix(post, a1, s, z0, z1 + 0.06, 1.5, lift=lambda t: 1.7 * ROPE_R * math.sin(math.pi * t) ** 0.5,
                  per_turn=9)
    end = cross[-1]
    ax, ay = post.axis(end.z)
    outw = Vector((end.x - float(ax), end.y - float(ay), 0)).normalized()
    tail = [end + outw * 0.07 + Vector((0, 0, -0.08)), end + outw * 0.1 + Vector((0, 0, -0.24)),
            end + outw * 0.11 + Vector((-s * 0.03, 0, -0.42))]
    return wraps + cross[1:] + tail, len(wraps) + len(cross) - 1


def rope(posts, mat):
    """One rope: left tail and lashing, down to the orb's collar, round it
    one and a half times, and up to the right lashing and tail."""
    nc, ex, ey = neck_frame()
    rn = NECK_R + ROPE_R * 0.85
    loop = []
    n = 18
    for k in range(n + 1):
        t = k / n
        psi = math.pi + 3 * math.pi * t  # from the left side, round the front, one and a half turns
        h = -0.045 + 0.09 * t
        loop.append(nc + (ex * math.cos(psi) + ey * math.sin(psi)) * rn + POLE * h)
    pl, nl = lashing(posts[0])
    pr, nr = lashing(posts[1])
    pl.reverse()

    def v_rope(a, ta, b, tb):
        mid = (a + b) / 2 + Vector((0, 0, -0.04))
        ctrl = [a, a + ta * 0.18, mid, b - tb * 0.18, b]
        return zk.resample(zk.spline(ctrl, 6), 0.16)[1:-1]
    # tangents where the rope leaves the loop and joins each lashing
    tl = (loop[1] - loop[0]).normalized()
    tr = (loop[-1] - loop[-2]).normalized()
    tpl = (pl[-1] - pl[-2]).normalized()
    tpr = (pr[1] - pr[0]).normalized()
    left = v_rope(pl[-1], tpl, loop[0], tl)
    right = v_rope(loop[-1], tr, pr[0], tpr)
    pts = pl + left + loop + right + pr
    L = len(pts)

    def radius(s):
        # frayed tapering tails at both ends
        e = min(s, 1 - s) * L / 3.0
        return ROPE_R * (0.55 + 0.45 * min(1.0, e))
    bm = bmesh.new()
    zk.sweep(bm, pts, radius, seg=5, ulen=0.3)
    ob = zk.make("rope", bm, [mat])
    return zk.smooth(ob, 70)


# ---- orb, straps, gem, collar and crest --------------------------------------

def orb_point(psi, theta, r=ORB_R):
    """A point on the orb in its tilted frame: theta from the crown."""
    return ORB_C + (POLE * math.cos(theta) + (Vector((1, 0, 0)) * math.cos(psi) + BACK * math.sin(psi))
                    * math.sin(theta)) * r


def orb(mat):
    seg, rings = 24, 13
    P = [[orb_point(2 * math.pi * i / seg, math.pi * (rings - j) / rings) for i in range(seg)]
         for j in range(1, rings)]
    bm = bmesh.new()
    zk.grid(bm, P, top=orb_point(0, 0), bottom=orb_point(0, math.pi))
    zk.fix_normals(bm)
    return zk.smooth(zk.make("orb", bm, [mat]), 80)


def strap(psi, mat, width=0.07):
    """A flat gold strap round the orb on the great circle through the crown at psi."""
    pts = [orb_point(psi, 2 * math.pi * k / 24, ORB_R + 0.014) for k in range(25)]
    m = Vector((math.cos(psi), 0, 0)) + BACK * math.sin(psi)
    side = POLE.cross(m).normalized()
    bm = bmesh.new()
    zk.sweep(bm, pts, lambda s: width, seg=4, flat=0.28, up=tuple(side), a_off=math.pi / 4)
    return zk.smooth(zk.make("strap", bm, [mat]), 40)


def fit_strap_psi():
    """The second strap runs down the orb's upper left edge in the picture."""
    targets = [(19.0, 29.0), (17.0, 32.0), (21.5, 26.5)]
    best = None
    for d in range(0, 180, 2):
        psi = math.radians(d)
        err = 0.0
        for tx, ty in targets:
            e = 1e9
            for k in range(180):
                p = orb_point(psi, 2 * math.pi * k / 180, ORB_R)
                if (p - ORB_C).dot(-zk.VIEW) < 0:
                    continue
                sx, sy = zk.screen(p, HOT)
                e = min(e, (sx - tx) ** 2 + (sy - ty) ** 2)
            err += e
        if best is None or err < best[0]:
            best = (err, psi)
    return best[1]


def gem(gem_mat, gold):
    # where the picture has it, on the face the classic camera sees
    sx, sy = (29.5 - 27) / 16.0 / ORB_R, (35.5 - 31) / 16.0 / ORB_R
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
    prof = [(0.25, -0.06), (0.245, -0.025), (0.21, 0.015), (0.165, 0.045), (0.135, 0.06), (NECK_R, 0.075),
            (NECK_R, 0.125), (0.155, 0.14), (0.16, 0.155), (0.14, CAP_TOP), (0.08, CAP_TOP + 0.01)]
    seg = 12
    ex, ey = Vector((1, 0, 0)), BACK
    P = [[top + (ex * math.cos(2 * math.pi * i / seg) + ey * math.sin(2 * math.pi * i / seg)) * r + POLE * h
          for i in range(seg)] for r, h in prof]
    bm = bmesh.new()
    zk.grid(bm, P, top=top + POLE * (CAP_TOP + 0.012))
    zk.fix_normals(bm)
    return zk.smooth(zk.make("collar", bm, [mat]), 45)


LEAN = math.radians(28)   # the crest's own lean, a little more upright than the crown
CR = 0.46                 # crest arc radius
TIPS = math.radians(18)   # horn tips this far above the arc's centre line


def crest(gold, dark):
    """A thick horned crescent rising from the flange; its inner face is dark."""
    up = Vector((0, math.sin(LEAN), math.cos(LEAN)))
    ex = Vector((1, 0, 0))
    nf = ex.cross(up)  # faces the classic camera
    wr_b = 0.1
    base = ORB_C + POLE * (ORB_R + CAP_TOP - 0.01)
    ctr = base + up * (CR + wr_b * 0.55)
    oct_ = [(1, 0.55), (0.55, 1), (-0.55, 1), (-1, 0.55), (-1, -0.55), (-0.55, -1), (0.55, -1), (1, -0.55)]
    inner = {3, 4}  # faces from vertex k to k+1 that look into the crescent
    n = 26
    rings = []
    bm = bmesh.new()
    for k in range(n + 1):
        s = k / n
        phi = math.pi - TIPS + (math.pi + 2 * TIPS) * s
        rdir = ex * math.cos(phi) + up * math.sin(phi)
        c = ctr + rdir * CR
        w = math.sin(math.pi * s)
        wr = wr_b * w ** 0.6
        wn = 0.19 * w ** 0.85
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

posts = [Post(-1, 3, 0.0, (5.14, 5.5)), Post(1, 7, 0.5, (5.26, 5.62))]
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
GOLD = hk.pbr("zl_gold", lin((240, 202, 115)), rough=0.34, metal=0.65)
DARK = hk.pbr("zl_crest_inner", lin((80, 80, 76)), rough=0.48, metal=0.5)
BRONZE = hk.pbr("zl_bronze", lin((190, 172, 128)), rough=0.3, metal=1.0)
GEM = hk.pbr("zl_gem", lin((14, 22, 50)), rough=0.06, emit=lin((20, 42, 110)), strength=0.25)

# ---- build -----------------------------------------------------------------

psi2 = fit_strap_psi()
print("STRAP_PSI", round(math.degrees(psi2), 1))
parts = [posts[0].build(BARK), posts[1].build(BARK), rope(posts, ROPE)]
parts += [orb(BRONZE), strap(math.pi / 2, GOLD), strap(psi2, GOLD)]
parts += gem(GEM, GOLD)
parts += [collar(GOLD), crest(GOLD, DARK)]
for p in parts:
    print("PART", p.name, zk.tris(p))

ob = hk.finish(parts, os.path.join(OUT, "models", NAME + ".glb"),
               {"replacesTexture": "zhonlode", "replacesPiece": "zonlode"})
print("TRIS", zk.tris(ob))
hk.renders(ob, os.path.join(OUT, "renders"), NAME, SPRITE, HOT, scale=4)
