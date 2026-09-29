"""Low-level pieces for the bespoke dead firs, run inside Blender: a stiff
twig spur (a thin tube with two twig cards crossed along it) and a drooping
limb carrying alternating short spurs.
"""
import math

import numpy as np

import kit
from kit import _unit


def spur(wood, cards, p0, d, L, col, rng, card_w=0.55, thick=1.0):
    """A stiff spur from p0 along d, L long, with crossed twig cards."""
    d = _unit(np.asarray(d, np.float64))
    p1 = p0 + d * L
    rb = float(np.clip(0.03 + 0.025 * L, 0.035, 0.06)) * thick
    kit.tube(wood, np.array([p0, p1]), np.array([rb, rb * 0.35]), sides=3, col=col)
    side = _unit(np.cross(d, [0.0, 0.0, 1.0])) if abs(d[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    upn = np.cross(side, d)
    w = L * card_w
    for sgn in (-1.0, 1.0):
        nrm = _unit(upn * math.cos(0.8) + side * math.sin(0.8) * sgn)
        kit.card(cards, p0 + d * L * 0.05, nrm, d, w, L * 1.05, col=col * rng.uniform(0.9, 1.15),
                 N=_unit(nrm * 0.3 + np.array([0, 0, 1.0])), anchor=0.0)


def droop_dir(az, deg):
    """A unit direction at azimuth az (radians) pitched deg degrees (down if negative)."""
    e = math.radians(deg)
    return np.array([math.cos(az) * math.cos(e), math.sin(az) * math.cos(e), math.sin(e)])


def skirt_limb(wood, cards, base, az, L, z0, col, rng, r0=0.06, spurs=3, spur_len=(0.25, 0.45)):
    """A low limb from base (on the trunk, z0 up) out along azimuth az,
    sagging to rest near the ground L cells out, with single spurs by turns
    left and right along it. Returns its tip."""
    h = np.array([math.cos(az), math.sin(az), 0.0])
    ts = np.linspace(0, 1, 6)
    P = np.array([base + h * L * t + np.array([0, 0, z0 * (1 - t) ** 1.6 + 0.05 * t]) for t in ts])
    kit.tube(wood, P, np.linspace(r0, 0.04, len(P)), sides=5, col=col)
    perp = np.array([-h[1], h[0], 0.0])
    sg = rng.choice((-1.0, 1.0))
    for q, t in enumerate(np.sort(rng.uniform(0.3, 0.97, spurs))):
        p = np.array([np.interp(t, ts, P[:, i]) for i in range(3)])
        s = sg * (1 if q % 2 == 0 else -1)
        a = math.radians(rng.uniform(35, 60))
        d = h * math.cos(a) + perp * s * math.sin(a)
        d[2] = math.tan(math.radians(rng.uniform(-20, 15)))
        spur(wood, cards, p, d, rng.uniform(*spur_len), col * rng.uniform(0.85, 1.1), rng)
    return P[-1]
