"""Low-level pieces for the bespoke poplars, run inside Blender.

A crown is a closed surface given as a radius round an upright axis: each
direction phi has its own meridian from under(phi) up to the tip, so the
front of the crown can lift off the trunk while the back hangs low. Leaf
cards are strewn evenly over it, over a dark core of the same shape.
"""
import math

import numpy as np

import kit
from kit import TAU, TO_CAM, Geo, _unit, card


def smooth(a, b, x):
    t = np.clip((np.asarray(x, np.float64) - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def bumps(rng, n, u_range, amp, su=0.07, sphi=0.55):
    """A lumpy multiplier f(u, phi) of n gaussian lumps and dents."""
    U = rng.uniform(*u_range, n)
    PH = rng.uniform(0, TAU, n)
    A = rng.uniform(-0.5, 1.0, n) * amp

    def f(u, phi):
        d = (np.asarray(phi)[..., None] - PH + math.pi) % TAU - math.pi
        g = np.exp(-((np.asarray(u)[..., None] - U) / su) ** 2 - (d / sphi) ** 2)
        return 1.0 + (A * g).sum(-1)
    return f


class Crown:
    """axis(z) -> (x, y); radius(z, phi) -> cells; under(phi) -> the bottom
    z of that meridian; top the tip's z. rb rounds each meridian's foot."""

    def __init__(self, axis, radius, under, top, rb=0.9, nz=64, nphi=48):
        self.axis, self.radius, self.under, self.top, self.rb = axis, radius, under, top, rb
        self.V = self.grid(nz, nphi, 1.0)

    def grid(self, nz, nphi, k):
        V = np.zeros((nz + 1, nphi + 1, 3))
        for j in range(nphi + 1):
            phi = TAU * (j % nphi) / nphi
            z0 = self.under(phi)
            z = z0 + (self.top - z0) * np.linspace(0, 1, nz + 1) ** 0.85
            r = np.array([self.radius(zz, phi) for zz in z])
            t = np.clip((z - z0) / self.rb, 0, 1)
            r = r * np.sqrt(1 - (1 - t) ** 2) * (np.array([k(zz) for zz in z]) if callable(k) else k)
            r[0] = r[-1] = 0.0
            ax = np.array([self.axis(zz) for zz in z])
            V[:, j, 0] = ax[:, 0] + r * math.cos(phi)
            V[:, j, 1] = ax[:, 1] + r * math.sin(phi)
            V[:, j, 2] = z
        return V

    def sample(self, spacing, rng):
        """Points spread evenly over the surface with their outward normals:
        candidates by area, thinned to about spacing apart."""
        V = self.V
        a, b, c, d = V[:-1, :-1], V[:-1, 1:], V[1:, 1:], V[1:, :-1]
        N = np.cross(c - a, d - b)
        area = 0.5 * np.linalg.norm(N, axis=-1)
        cnt = rng.poisson(area.ravel() * 4.0 / spacing ** 2)
        P, Nn = [], []
        A4 = [x.reshape(-1, 3) for x in (a, b, c, d)]
        Nf = N.reshape(-1, 3)
        for q in np.nonzero(cnt)[0]:
            for _ in range(cnt[q]):
                s, t = rng.random(), rng.random()
                p = (A4[0][q] * (1 - s) + A4[1][q] * s) * (1 - t) + (A4[3][q] * (1 - s) + A4[2][q] * s) * t
                P.append(p)
                Nn.append(Nf[q])
        P, Nn = np.array(P), _unit(np.array(Nn))
        ax = np.array([self.axis(z) for z in P[:, 2]])
        out = np.einsum("ij,ij->i", Nn[:, :2], P[:, :2] - ax)
        Nn[out < 0] *= -1
        order = rng.permutation(len(P))
        keep, cell = [], {}
        h = spacing
        for i in order:
            key = tuple((P[i] // h).astype(int))
            ok = True
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    for dz in (-1, 0, 1):
                        for k in cell.get((key[0] + dx, key[1] + dy, key[2] + dz), ()):
                            if np.sum((P[k] - P[i]) ** 2) < (0.8 * spacing) ** 2:
                                ok = False
                                break
                        if not ok:
                            break
                    if not ok:
                        break
                if not ok:
                    break
            if ok:
                keep.append(i)
                cell.setdefault(key, []).append(i)
        return P[keep], Nn[keep]

    def core(self, geo, k, col, nz=22, nphi=14):
        """The same shape shrunk to k (or k(z)) of its radius, closed, one colour."""
        V = self.grid(nz, nphi, k)[:, :-1]
        m = nphi
        flat = V.reshape(-1, 3)
        ax = np.array([self.axis(z) for z in flat[:, 2]])
        Nn = np.concatenate([flat[:, :2] - ax, np.full((len(flat), 1), 0.3)], 1)
        F = []
        for i in range(nz):
            for j in range(m):
                a0, b0 = i * m + j, i * m + (j + 1) % m
                F.append((a0, b0, b0 + m, a0 + m))
        geo.add(flat, F, None, col, Nn)


def sprig(geo, p, n, size, col, rng, uv, fc=0.0, up_bias=0.5, tilt=0.4, flat=0.25):
    """An upright poplar sprig standing out of the surface at p."""
    Nsh = _unit(n + np.array([0, 0, flat]))
    fn = _unit(n + TO_CAM * fc + np.array([0, 0, up_bias]) + rng.normal(0, tilt, 3))
    upv = _unit(np.array([0, 0, 1.0]) + np.array([n[0], n[1], 0]) * 0.6 + rng.normal(0, 0.2, 3))
    card(geo, p - upv * size * 0.35, fn, upv, size * 0.6, size * 1.2, uvrect=uv, col=col, N=Nsh, anchor=0.0)


def spray(geo, p, n, size, col, rng, uv):
    """A spray jutting from the rim, turned toward the classic camera."""
    out = _unit(n + rng.normal(0, 0.25, 3) + np.array([0, 0, 0.15]))
    fn = _unit(TO_CAM - out * float(np.dot(TO_CAM, out)) + rng.normal(0, 0.3, 3))
    card(geo, p - out * size * 0.25, fn, out, size * 0.6, size * 1.2, uvrect=uv, col=col,
         N=_unit(n + np.array([0, 0, 0.4])), anchor=0.0)


def fleck(geo, p, n, size, col, rng, uv):
    q = p + n * 0.05 + rng.normal(0, 0.1, 3)
    card(geo, q, _unit(n + TO_CAM + rng.normal(0, 0.3, 3)), _unit(rng.normal(0, 1, 3)), size, size, uvrect=uv,
         col=col, N=_unit(n + np.array([0, 0, 1.0])))


def leader(geo, tip, size, col, uv, rng, n=3):
    """Upright sprigs crossed on the tip, closing the crown to a point."""
    for k in range(n):
        a = math.pi * k / n + rng.uniform(-0.2, 0.2)
        card(geo, tip - np.array([0, 0, size * 0.9]), np.array([math.cos(a), math.sin(a), 0.0]), np.array([0, 0, 1.0]),
             size * 0.45, size * 1.6, uvrect=uv, col=col, N=_unit(np.array([0, -0.3, 1.0])), anchor=0.0)


def leaf_colour(pal, rng, up, ao, accents=()):
    return kit.leaf_colour(pal, rng, up, ao, accents)
