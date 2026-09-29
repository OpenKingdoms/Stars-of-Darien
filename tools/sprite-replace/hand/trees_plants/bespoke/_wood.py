"""Low-level pieces for the bespoke dead wood, run inside Blender: limbs
swept along a spine with elliptical rings, split ends of tapered wedges,
and low buttress roots lying on the ground.
"""
import math

import numpy as np

import kit
from kit import TAU, _unit

BARK_REP = kit.BARK_REP


def _frames(P, ref):
    """Tangents and ring axes along P: u is ref (a world vector, or 'side'
    for the horizontal side of a lying limb) squared to the tangent."""
    T = np.zeros_like(P)
    T[1:-1] = P[2:] - P[:-2]
    T[0], T[-1] = P[1] - P[0], P[-1] - P[-2]
    T = _unit(T)
    U, W = [], []
    for t in T:
        r = _unit(np.cross(t, [0.0, 0.0, 1.0])) if isinstance(ref, str) else np.asarray(ref, np.float64)
        u = _unit(r - t * float(np.dot(r, t)))
        U.append(u)
        W.append(np.cross(t, u))
    return T, np.array(U), np.array(W)


def sweep(geo, P, A, B, col, sides=10, ref=(1.0, 0.0, 0.0), ridges=0, amp=0.0, twist=0.35, groove=0.8, shape=None,
          cap_tip="dome", cap_base=None, rng=None, shade=None):
    """A limb along spine P (n, 3), its rings A across (along ref) and B the
    other way, in fibre ridges of amp times the radius (grooves darker by
    groove). shape(i, a) scales ring i at angle a; shade(i) scales its
    colour. Caps: 'dome', 'cone' (a tall point, for a trunk's top hidden
    in its prongs), 'flat' or None. Returns the last ring's frame."""
    P = np.asarray(P, np.float64)
    n = len(P)
    T, U, W = _frames(P, ref)
    s = np.concatenate([[0.0], np.cumsum(np.linalg.norm(P[1:] - P[:-1], axis=1))])
    ph = rng.uniform(0, TAU) if rng is not None else 0.0
    rk = rng.uniform(0.6, 1.3, max(ridges, 1)) if rng is not None else np.ones(max(ridges, 1))
    urep = max(1, round(TAU * float(np.mean(np.maximum(A, B))) * BARK_REP * 2))
    col = np.asarray(col, np.float64)
    V, UV, CC = [], [], []
    for i in range(n):
        for k in range(sides + 1):
            a = TAU * (k % sides) / sides
            f = 1.0
            g = 0.0
            if ridges:
                aa = a + twist * s[i] + ph
                g = math.cos(ridges * aa) * rk[int((aa % TAU) / TAU * ridges) % ridges]
                f += amp * g
            if shape is not None:
                f *= shape(i, a, U[i] * math.cos(a) + W[i] * math.sin(a))
            V.append(P[i] + (U[i] * A[i] * math.cos(a) + W[i] * B[i] * math.sin(a)) * f)
            UV.append((urep * k / sides, s[i] * BARK_REP))
            c = col * (groove + (1 - groove) * (0.5 + 0.5 * g)) if ridges else col
            CC.append(c * (shade(i) if shade else 1.0))
    m = sides + 1
    F = []
    for i in range(n - 1):
        for k in range(sides):
            a0, b0 = i * m + k, i * m + k + 1
            F.append((a0, b0, b0 + m, a0 + m))
    for end, cap in ((n - 1, cap_tip), (0, cap_base)):
        if not cap:
            continue
        # the cap closes in rings, textured on as the limb is, so the bark
        # runs over it instead of banding round its point
        sg = 1.0 if end else -1.0
        h = {"dome": 0.45, "cone": 1.6}.get(cap, 0.0) * min(A[end], B[end])
        prev = end * m
        for q, t in enumerate((0.35, 0.65, 0.88)):
            k_r = math.sqrt(1 - t * t) if cap == "dome" else 1 - t
            base_i = len(V)
            for k in range(sides + 1):
                V.append(P[end] + T[end] * sg * h * t + (V[end * m + k] - P[end]) * k_r)
                UV.append((UV[end * m + k][0], s[end] * BARK_REP + sg * h * t * BARK_REP))
                CC.append(CC[end * m + k])
            for k in range(sides):
                f = (prev + k, prev + k + 1, base_i + k + 1, base_i + k)
                F.append(f if end else tuple(reversed(f)))
            prev = base_i
        V.append(P[end] + T[end] * sg * h)
        UV.append((0.5, s[end] * BARK_REP + sg * h * BARK_REP))
        CC.append(CC[end * m])
        c = len(V) - 1
        for k in range(sides):
            f = (prev + k, prev + k + 1, c)
            F.append(f if end else tuple(reversed(f)))
    geo.add(np.array(V), F, np.array(UV), np.array(CC))
    return T[-1], U[-1], W[-1]


def wedges(geo, C, T, U, W, A, B, rng, col, n=(2, 3), length=(0.35, 0.9), splay=(5, 20), arc=5, first=None):
    """A split end: the ring (C, axes U*A and W*B, facing T) cut into 2-3
    sectors, each a tapered wedge of arc+2 sides from the full ring to its
    own point, splayed a little outward. first gives the first wedge's
    length, for one long splinter."""
    k = int(rng.integers(n[0], n[1] + 1))
    cuts = np.sort((rng.uniform(0, TAU) + TAU * np.arange(k) / k + rng.uniform(-0.35, 0.35, k)) % TAU)
    cuts = np.append(cuts, cuts[0] + TAU)
    for j in range(k):
        a = np.linspace(cuts[j], cuts[j + 1], arc + 1)
        rim = [C + (U * A * math.cos(t) + W * B * math.sin(t)) for t in a]
        poly = np.array([C] + rim) - T * 0.06
        cen = poly.mean(0)
        out = _unit(cen - C + T * 1e-6)
        L = first if (first and j == 0) else rng.uniform(*length)
        sp = math.radians(rng.uniform(*splay))
        d = _unit(T * math.cos(sp) + out * math.sin(sp) + rng.normal(0, 0.05, 3))
        mid = cen + d * L * 0.45 + (poly - cen) * 0.0
        ring1 = mid + (poly - cen) * rng.uniform(0.45, 0.6)
        tip = cen + d * L + (poly[1 + arc // 2] - cen) * 0.3
        V = np.concatenate([poly, ring1, [tip]])
        q = len(poly)
        F = []
        for i in range(q):
            i2 = (i + 1) % q
            F.append((i, i2, q + i2, q + i))
            F.append((q + i, q + i2, 2 * q))
        cc = [col * rng.uniform(0.95, 1.1)] * q + [col * 1.1] * q + [col * 1.2]
        uv = [(i / q, 0.0) for i in range(q)] + [(i / q, L * 0.45 * BARK_REP) for i in range(q)] + [(0.5, L * BARK_REP)]
        geo.add(V, F, np.array(uv), np.array(cc))


def root(geo, base, az, reach, r_foot, rng, col, h0=0.38, w0=0.3, sides=8):
    """A buttress root: out of the foot at half its radius, a low wide
    ridge no higher than h0 at the bark, settling onto the ground and
    running reach cells beyond the bark to a thin tip."""
    d = np.array([math.cos(az), math.sin(az), 0.0])
    side = np.array([-d[1], d[0], 0.0])
    L = r_foot + reach
    t = np.linspace(0, 1, 9)
    dist = 0.5 * r_foot + (L - 0.5 * r_foot) * t
    wig = rng.normal(0, 0.05, len(t)) * t
    hh = h0 * np.clip(1 - (dist - r_foot) / max(reach, 1e-3), 0, 1) ** 1.6 * (dist >= r_foot) + h0 * (dist < r_foot)
    hh = np.maximum(hh, 0.06 * (1 - t) + 0.02)
    ww = w0 * (1 - t) ** 0.9 + 0.035
    P = np.array([base + d * q + side * w + np.array([0, 0, 0.5 * h - 0.02]) for q, w, h in zip(dist, wig, hh)])
    sweep(geo, P, ww, 0.55 * hh + 0.02, col * rng.uniform(0.85, 1.0), sides=sides, ref="side", cap_tip="dome")
