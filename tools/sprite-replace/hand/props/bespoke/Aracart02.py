"""Aracart02, the handcart loaded with hay, built on its own:

    blender -b --factory-startup --python Aracart02.py [-- trial suffix]

The cart under the load is the table's (models.ARA_CART2, which passed);
the load is shaped here: a long loaf with rounded top edges and rounded
blunt ends, straw laid over every face of it, a ragged fringe hanging over
both beams and both ends and stalks poking out along the top edges.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402
from _common import kit, mk  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402

import models  # noqa: E402

NAME = "Aracart02"
# the picture's hay: base, fine lengthwise streaks, highlights, the shade in the folds
BASE, STREAK, HI, FOLD = (110, 102, 10), (60, 55, 6), (155, 144, 28), (48, 43, 6)
MID = (128, 118, 14)
PALE = (160, 148, 24)


def pose(q):
    """The cart's placing, as kit.cart() works it out: pitched about the axle
    till the shaft tips touch the ground, turned by yaw and moved to 'at'."""
    L, R, ax = q["bed"][0], q["wheel"], q.get("axle_x", 0.0)
    bw = q.get("beam", 0.11)
    zb = R + q.get("clear", 0.06)
    tip_r = q.get("shaft_t", bw * 0.8) / 2
    sz = zb + bw / 2
    dx = L / 2 + q["shafts"] - ax
    dzz = sz - R
    beta = math.acos(max(-1.0, min(1.0, (tip_r - R) / math.hypot(dx, dzz)))) - math.atan2(dx, dzz)
    M = Matrix.Translation((ax, 0, R)) @ Matrix.Rotation(beta, 4, "Y") @ Matrix.Translation((-ax, 0, -R))
    at = q.get("at", (0.0, 0.0))
    return Matrix.Translation((at[0], at[1], q.get("lift", 0.0))) @ Matrix.Rotation(math.radians(q["yaw"]), 4, "Z") @ M


def core(L, W, h, x0, y0, top, rnd, m, r_end=0.4, ring=13):
    """The loaf: a rounded-rectangle section (steep sides, top edges rounded
    about 0.12), full size down its length and rounded down to the bed over
    r_end at each end; open underneath, lumpy and ridged along its length."""
    Lh, a = L / 2, W / 2
    ends = [0.0, 0.05, 0.12, 0.2, 0.3, r_end]
    mid = int(round((L - 2 * r_end) / 0.26))
    ds = ends + [r_end + (L - 2 * r_end) * i / mid for i in range(1, mid)] + [L - d for d in reversed(ends)]
    off = Vector((rnd.uniform(0, 50), rnd.uniform(0, 50), rnd.uniform(0, 50)))
    verts, faces = [], []
    for d in ds:
        de = min(d, L - d)
        e = max(0.0, 1.0 - de / r_end)
        kz = math.sqrt(max(0.0, 1.0 - e * e))
        ky = 0.8 + 0.2 * kz
        x = x0 - Lh + d
        for j in range(ring):
            t = math.pi * (1.0 - j / (ring - 1))
            c, s = math.cos(t), math.sin(t)
            y = a * math.copysign(abs(c) ** 0.4, c)
            z = h * abs(s) ** 0.5 * (1.0 + 0.05 * (1.0 - (y / a) ** 2))
            p = Vector((x, y0 + y * ky, top + z * kz))
            zz = min(1.0, (p.z - top) / h)
            n1 = noise.noise_vector(p * 2.2 + off)
            n2 = noise.noise_vector(Vector((p.x * 1.2, p.y * 9.0, p.z * 9.0)) + off)
            # lumps, and ridges running along the load
            p += Vector((n1.x * 0.07 + n2.x * 0.03, (n1.y * 0.05 + n2.y * 0.035) * zz,
                         (n1.z * 0.07 + n2.z * 0.04) * zz))
            if de < 1e-6:
                p.z = top
            verts.append(tuple(p))
    for i in range(len(ds) - 1):
        for j in range(ring - 1):
            k = i * ring + j
            faces.append((k, k + ring, k + ring + 1, k + 1))
    ob = mk.mesh("hay", verts, faces, m)
    mk._fix_normals(ob)

    def fn(pp, co, nn, li):
        q = Vector((co.x * 1.1, co.y * 11.0, co.z * 11.0)) + off
        c = kit.mix(STREAK, BASE, 0.75 + 0.9 * noise.noise(q) + 0.3 * noise.noise(q * 2.7))
        if noise.noise(co * 6.0 + off) > 0.3:
            c = kit.mix(c, HI, 0.55)
        if co.z < top + 0.1:
            c = kit.mix(c, FOLD, 0.5)
        return c
    kit.paint(ob, fn)
    return ob


def surface(ob, rnd, n):
    """n points on the loaf's faces, by area, with their normals."""
    me = ob.data
    tris_ = []
    for f in me.polygons:
        vs = [me.vertices[i].co for i in f.vertices]
        for j in range(1, len(vs) - 1):
            ar = (vs[j] - vs[0]).cross(vs[j + 1] - vs[0]).length / 2
            tris_.append((vs[0], vs[j], vs[j + 1], f.normal.copy(), ar))
    total = sum(t[4] for t in tris_)
    acc, run_ = [], 0.0
    for t in tris_:
        run_ += t[4]
        acc.append(run_)
    out = []
    import bisect
    for k in range(n):
        a_, b_, c_, n_, _ = tris_[min(len(tris_) - 1, bisect.bisect(acc, rnd.uniform(0, total)))]
        r1, r2 = rnd.random(), rnd.random()
        if r1 + r2 > 1:
            r1, r2 = 1 - r1, 1 - r2
        out.append((a_ + (b_ - a_) * r1 + (c_ - a_) * r2, n_))
    return out


def tone(rnd):
    return rnd.choices((STREAK, FOLD, BASE, MID, HI, PALE), (14, 6, 34, 22, 18, 6))[0]


def straws(pts, rnd, m, name):
    """Straw cards: each (base, direction, length, width, lift) a thin blade."""
    verts, faces, cols = [], [], []
    for b, d, ln, w, nrm in pts:
        s = d.cross(nrm)
        s = s.normalized() if s.length > 1e-4 else Vector((0, 1, 0))
        i = len(verts)
        verts += [tuple(b - s * w / 2), tuple(b + s * w / 2), tuple(b + d * ln + nrm * 0.02)]
        faces.append((i, i + 1, i + 2))
        cols.append(tone(rnd))
    ob = mk.mesh(name, verts, faces, m)
    kit.paint(ob, lambda pp, co, nn, li: cols[pp.index])
    return ob


def hay(L, W, h, x0, y0, top, rnd):
    m = kit.mat("hay", PALE, rough=1.0)
    body = core(L, W, h, x0, y0, top, rnd, m)
    parts = [body]
    Lh = L / 2
    # straw laid over every face: along the load on the top and sides,
    # down the slope on the rounded ends
    lay = []
    # the rounded ends and the steep lower flanks get a second layer
    x_lo, x_hi = x0 - Lh, x0 + Lh
    extra = [(co, nrm) for co, nrm in surface(body, rnd, 2500)
             if min(co.x - x_lo, x_hi - co.x) < 0.55 or co.z < top + 0.3][:200]
    for co, nrm in surface(body, rnd, 560) + extra:
        a = rnd.uniform(-0.5, 0.5) + (math.pi if rnd.random() < 0.5 else 0.0)
        T = Vector((math.cos(a), math.sin(a) * 0.35, rnd.uniform(-0.3, 0.3)))
        T = T - nrm * T.dot(nrm)
        if T.length < 1e-3:
            continue
        T.normalize()
        ln = rnd.uniform(0.18, 0.45)
        lay.append((co + nrm * 0.012 - T * ln / 2, T, ln, rnd.uniform(0.04, 0.06), nrm))
    parts.append(straws(lay, rnd, m, "straw_lying"))
    # a fringe hanging over both beams and both ends, 0.1 to 0.18 past the rim
    fr = []
    rim = [(co, nrm) for co, nrm in surface(body, rnd, 4000) if co.z < top + 0.2][:190]
    for co, nrm in rim:
        out = Vector((nrm.x, nrm.y, 0.0))
        if out.length < 0.2:
            continue
        out.normalize()
        d = (out + Vector((rnd.uniform(-0.5, 0.5) * out.y, rnd.uniform(-0.5, 0.5) * out.x,
                           rnd.uniform(-0.7, -0.15)))).normalized()
        ln = rnd.uniform(0.12, 0.26)
        fr.append((co - out * 0.04, d, ln, rnd.uniform(0.035, 0.05), Vector((0, 0, 1))))
    parts.append(straws(fr, rnd, m, "straw_fringe"))
    # stalks poking out of the upper edges and the crown, ragging the outline
    pk = []
    up = [(co, nrm) for co, nrm in surface(body, rnd, 3000) if co.z > top + h * 0.45 and (abs(nrm.y) > 0.35 or rnd.random() < 0.4)][:125]
    for co, nrm in up:
        d = (nrm + Vector((rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), rnd.uniform(-0.1, 0.4)))).normalized()
        pk.append((co - d * 0.03, d, rnd.uniform(0.12, 0.28), rnd.uniform(0.03, 0.045), nrm))
    parts.append(straws(pk, rnd, m, "straw_poke"))
    return parts


def build():
    q = kit.sturdy(models.ARA_CART2)
    hy = q.pop("hay")
    parts = kit.cart(q)
    L, W = q["bed"]
    bw = q.get("beam", 0.11)
    top = q["wheel"] + q.get("clear", 0.06) + bw + q.get("deck_t", 0.06) - 0.02
    load = hay(hy["L"] * hy["over"][0], W * hy["over"][1], hy["h"], hy["x"], hy["y"], top, random.Random(22))
    kit.place(load, pose(q))
    return parts + load


if __name__ == "__main__":
    _common.main(NAME, build)
