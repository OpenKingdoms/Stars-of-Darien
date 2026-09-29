"""AraTree08a, the taller Aramon dead fir, shaped against its own sprite.

The drawing runs on well below its anchor, so the trunk stands in front of
it, where the drawn foot is, and leans back to the drawn tip. Stiff spurs
in ragged whorls of four or five, about every 0.3 cells up to 0.3 cells
under the tip: drooping in the lower half, about half of them turned up
above, each as long as the drawn column reaches at its height. No limb
points at the camera: the skirt is two low whorls of three or four limbs
aimed within 30 degrees of straight left or right, sagging to the ground
with single spurs by turns, as wide as the drawn flared skirt.
"""
import math

import numpy as np

import _twig as tw
import kit
import sprite2d as s2

CALIB = ("twigs", "sprays")
LIFT = 1.0
ROUNDS = 3

BASE = (-0.15, -0.9)         # the trunk's foot, in front of the anchor where the drawn foot is
TOP = (-0.2, 0.05, 4.2)      # the leader's tip
WHORLS = (0.85, 0.3)         # the first whorl's height and the spacing up to the tip
UPPER_LEN = (0.5, 0.8)       # spur lengths in the upper half
LOWER_LEN = (0.8, 1.1)
# the skirt: (height on the trunk, [(azimuth degrees, length)])
SKIRT = [(0.55, [(-150, 1.2), (-30, 0.95), (172, 1.0), (12, 0.85)]),
         (0.3, [(-162, 1.15), (-15, 0.95), (156, 0.95), (-38, 0.9)])]


def build(spr, rng):
    hx, hy = spr.hx, spr.hy
    mask = s2.close(spr.mask, 2)
    tex, barkc = kit.bark_from(spr, None, "speckle", gain=1.0)
    wood, cards = kit.Geo(), kit.Geo()
    base = np.array([BASE[0], BASE[1], 0.0])
    top = np.array(TOP)

    def trunk_at(z):
        return base + (top - base) * (z / top[2])

    def reach(p):
        """How far the drawn column runs left and right of p in classic, cells."""
        col, row = hx + 16 * p[0], int(round(hy - 16 * p[1] - 8 * p[2]))
        if not (0 <= row < spr.h) or not mask[row].any():
            return 0.2, 0.2
        xs = np.nonzero(mask[row])[0]
        return max(col - xs.min(), 2.0) / 16.0, max(xs.max() + 1 - col, 2.0) / 16.0

    kit.flared_trunk(wood, base, top, 0.1, 0.025, 1.0, 4, rng, barkc, sides=6, rings=8, hf=0.35)
    n_spur = 0
    zs = np.arange(WHORLS[0], top[2] - 0.3 + 1e-6, WHORLS[1])
    for i, z in enumerate(zs):
        z = min(z + rng.uniform(-0.08, 0.08), top[2] - 0.3)
        p0 = trunk_at(z)
        le, ri = reach(p0)
        upper = z > 0.5 * top[2]
        near_tip = float(np.clip((top[2] - z) / 0.9, 0.5, 1.0))
        n = int(rng.integers(4, 6) if upper else rng.integers(5, 7))
        ph = i * 2.4 + rng.uniform(0, 0.6)
        for k in range(n):
            az = ph + 2 * math.pi * k / n + rng.uniform(-0.3, 0.3)
            c = math.cos(az)
            L = rng.uniform(*(UPPER_LEN if upper else LOWER_LEN)) * near_tip
            # sideways ones no further out than the drawn column
            L = min(L, 1.1 * (le if c < 0 else ri) / max(abs(c), 0.3))
            if upper and rng.random() < 0.5:
                pitch = rng.uniform(10, 30)
            else:
                pitch = -rng.uniform(15, 30) if not upper else -rng.uniform(5, 20)
            d = tw.droop_dir(az, pitch)
            q = p0 + np.array([0, 0, rng.uniform(-0.06, 0.06)])
            tw.spur(wood, cards, q, d, L, barkc * rng.uniform(0.8, 1.15), rng)
            n_spur += 1
            if not upper and L > 0.45:
                # a side twig half way out, by turns left and right
                sd = np.cross(d, [0.0, 0.0, 1.0]) * (1 if k % 2 else -1)
                tw.spur(wood, cards, q + d * L * rng.uniform(0.4, 0.6), d + sd * 1.2, L * rng.uniform(0.3, 0.45),
                        barkc * rng.uniform(0.8, 1.1), rng)
    # the leader over the last whorl
    for k in range(2):
        a = math.pi * k / 2 + rng.uniform(-0.3, 0.3)
        tw.spur(wood, cards, top - np.array([0, 0, 0.35]), tw.droop_dir(a, 75), 0.45, barkc, rng)
    for z0, limbs in SKIRT:
        for a, L in limbs:
            tw.skirt_limb(wood, cards, trunk_at(z0), math.radians(a + rng.uniform(-6, 6)), L, z0, barkc * 0.9, rng,
                          spurs=5, spur_len=(0.25, 0.45))
    twm = kit.material("twigs", kit.image("twigs", kit.tex_leaves("twigs", 11)), cut=True, cull=False)
    parts = [wood.to_object(spr.name + "_twigs", kit.material("bark", kit.image("bark", tex), repeat=True)),
             cards.to_object(spr.name + "_sprays", twm)]
    return parts, {"whorls": len(zs), "spurs": n_spur, "wood_tris": wood.tris(), "card_tris": cards.tris()}
