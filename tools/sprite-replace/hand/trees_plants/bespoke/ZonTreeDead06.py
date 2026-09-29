"""ZonTreeDead06, the Zhon snag with one upright prong and a heavy side
branch, shaped against its own sprite.

Every limb follows its drawn pixels, each point pushed back along the
classic camera's ray to a chosen depth: the upright prong, drawn tallest,
leans back straight from the crotch so the tree stays low, a sliver split
off its top runs up beside it, and the side branch leans back with an arm
out to the right that ends in a pale knobbly break inside the classic frame. The trunk is
1.3 times the drawn width across and deeper than wide, the prong and
branch about 1.2 times their drawn width and tapering only in their last
third, the branch eased into the trunk low down, and every end splits into
tapered wedges. Five low buttress roots of uneven reach settle onto the
ground, the back ones short so none draws up the trunk in classic.
The bark is the drawing's own dark-to-light ramp, toned by _tone.fit.
"""
import math

import numpy as np

import _tone
import _wood as wd
import kit

CALIB = ()  # _tone.fit sets the bark's contrast and level instead
LEAN = 0.7  # the prong's lean back from vertical, as a tangent
PALE = 1.4  # the broken knob's colour over the bark's


def build(spr, rng):
    hx, hy = spr.hx, spr.hy

    def px(col, row, y):
        """The point drawn at pixel (col, row) lying y cells back."""
        return np.array([(col + 0.5 - hx) / 16.0, y, (hy - row - 0.5 - 16.0 * y) / 8.0])

    tb = _tone.Bark(spr)
    bark = np.array([0.3, 0.3, 0.3])
    wood = kit.Geo()

    # trunk: the drawn centre from the foot to the fork, 1.3x the drawn width
    # across and deeper than wide, flaring at the foot toward its roots
    roots = [(-138, 0.5), (-88, 0.6), (-25, 0.75), (45, 0.4), (130, 0.45)]
    ph = np.radians([a for a, _ in roots])
    Tp = np.array([px(16, 122, 0.0) - [0, 0, 0.15], px(16, 108, 0.05), px(16.5, 94, 0.1), px(17, 84, 0.15), px(16.5, 76, 0.2)])
    Tp = np.array([Tp[0]] + [Tp[0] + (Tp[1] - Tp[0]) * f for f in (0.15, 0.35, 0.6)] + list(Tp[1:]))
    RXt = np.interp(Tp[:, 2], [0, 1.5, 4.0, 5.2], [0.62, 0.56, 0.58, 0.64])
    RYt = np.interp(Tp[:, 2], [0, 1.5, 4.0, 5.2], [0.9, 0.85, 0.85, 0.85])

    def foot(i, a, d):
        # lobes toward the roots, fading out over the first cell, no lip
        z = Tp[i, 2]
        az = math.atan2(d[1], d[0])
        lob = max(math.exp(-(((az - p + math.pi) % (2 * math.pi) - math.pi) / 0.45) ** 2) for p in ph)
        return 1 + 0.3 * math.exp(-max(z, 0) / 0.35) * lob + 0.08 * math.exp(-max(z, 0) / 0.6)

    wd.sweep(wood, Tp, RXt, RYt, bark, sides=16, ridges=9, amp=0.06, groove=0.75, shape=foot, cap_tip="cone", rng=rng,
             shade=lambda i: 0.8 + 0.2 * min(1.0, Tp[i, 2] / 3.0))
    base = np.array([Tp[0][0], Tp[0][1], 0.0])
    for a, reach in roots:
        wd.root(wood, base, math.radians(a), reach, 0.72, rng, bark * 0.85)

    def limb(pts, rx, ry, ends, taper_to=0.6, sides=10, **kw):
        """A limb through drawn points (col, row, y), radius rx/ry held to
        its last third and tapering to taper_to there, ending in wedges."""
        P = np.array([px(*p) for p in pts])
        s = np.concatenate([[0.0], np.cumsum(np.linalg.norm(P[1:] - P[:-1], axis=1))])
        u = s / s[-1]
        k = 1 - (1 - taper_to) * np.clip((u - 0.66) / 0.34, 0, 1) ** 1.2
        A, B = np.asarray(rx, float) * k, np.asarray(ry, float) * k
        T, U, W = wd.sweep(wood, P, A, B, bark, sides=sides, ridges=6, amp=0.07, groove=0.75, cap_tip=None, rng=rng)
        wd.wedges(wood, P[-1], T, U, W, A[-1], B[-1], rng, bark * 1.1, **ends)
        return P

    # the upright prong: straight, leaning back from the crotch at one steady
    # angle (LEAN, tan from vertical), so every point on it lies at yL(row)
    def yL(row):
        return 0.15 + LEAN / (8.0 * (1 + 2 * LEAN)) * (86 - row)

    L = limb([(c, r, yL(r)) for c, r in ((14.5, 86), (13.5, 74), (12.5, 62), (11, 50), (9, 38), (7, 26), (5, 15),
                                         (3.5, 6))],
             [0.36, 0.34, 0.33, 0.32, 0.31, 0.3, 0.28, 0.26], [0.66, 0.64, 0.62, 0.6, 0.57, 0.55, 0.52, 0.48],
             dict(length=(0.3, 0.55)))
    # the side branch: heavy at the trunk, up and back to its split top
    R = limb([(18, 100, 0.05), (21, 88, 0.2), (24, 76, 0.55), (26.5, 66, 0.95), (29, 56, 1.4), (31, 46, 1.8),
              (32.5, 36, 2.05), (34, 27, 2.25), (35.5, 20, 2.4)],
             [0.52, 0.48, 0.42, 0.38, 0.36, 0.34, 0.3, 0.26, 0.24], [0.7, 0.62, 0.55, 0.5, 0.47, 0.44, 0.4, 0.36, 0.33],
             dict(n=(2, 3), length=(0.3, 0.5)))
    # its arm out to the right, ending in a pale knobbly break inside the frame
    arm = limb([(30, 45, 1.85), (35, 43.5, 2.05), (39.5, 42, 2.2), (43.5, 41, 2.3)], [0.24, 0.22, 0.21, 0.2],
               [0.3, 0.28, 0.27, 0.26], dict(n=(2, 2), length=(0.12, 0.2), splay=(20, 35)), sides=8, taper_to=0.9)
    e = arm[-1]
    for dc, dr, dy, rad in ((0.2, -0.5, 0.0, (0.24, 0.3, 0.24)), (0.9, -2.8, 0.05, (0.16, 0.22, 0.2)),
                            (-0.8, -1.8, -0.1, (0.16, 0.2, 0.17)), (1.2, 0.4, 0.1, (0.13, 0.17, 0.14))):
        c = e + np.array([dc / 16.0, dy, (-dr - 16 * dy) / 8.0])
        kit.ellipsoid(wood, c, rad, seg=8, rings=5, col=bark * PALE * rng.uniform(0.9, 1.1), jitter=0.12, rng=rng,
                      rep=kit.BARK_REP)
    # the sliver split off the prong's top: its foot buried in the prong,
    # out and up beside it to a point well below the prong's tip
    limb([(8, 28, yL(28)), (9.2, 18, yL(18)), (10, 10, yL(10))], [0.13, 0.12, 0.1], [0.19, 0.17, 0.15],
         dict(n=(2, 2), length=(0.15, 0.25), splay=(3, 8)), sides=7, taper_to=0.7)
    # the stub left off the prong, and the twig up and right from it
    limb([(10.5, 67, yL(67)), (7, 66.5, yL(67) - 0.06), (4.5, 66, yL(67) - 0.1)], [0.14, 0.13, 0.12],
         [0.17, 0.15, 0.14], dict(n=(2, 2), length=(0.12, 0.22)), sides=7)
    limb([(15, 58, yL(58)), (18, 53, yL(53) + 0.05), (20, 48.5, yL(48.5) + 0.1)], [0.1, 0.09, 0.08],
         [0.12, 0.11, 0.1], dict(n=(2, 2), length=(0.1, 0.18)), sides=6)
    parts = [wood.to_object(spr.name + "_wood", kit.material("bark", tb.img, repeat=True))]
    for p in parts:
        p.visible_shadow = True
    tone = _tone.fit(parts, spr, tb)
    return parts, {"tone": tone, "wood_tris": wood.tris(), "left_top": np.round(L[-1], 2).tolist(), "right_top": np.round(R[-1], 2).tolist()}
