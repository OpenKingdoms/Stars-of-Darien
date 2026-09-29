"""ZonTreeDead05, the Zhon forked snag with the bent right prong, shaped
against its own sprite.

Every limb follows its drawn pixels, each point pushed back along the
classic camera's ray to a chosen depth, so the classic view keeps the
drawing while the fork opens front to back: the right prong leans well
back and bends right into its split end, the left prong stays near the
front and the thin middle spike rises between them. The trunk is 1.3 times
the drawn width across and deeper than wide, the prongs about 1.2 times
their drawn width and tapering only in their last third, and every end,
stubs included, splits into tapered wedges inside the classic frame. Five
low buttress roots of uneven reach settle onto the ground, the back ones short.
"""
import math

import numpy as np

import _wood as wd
import kit

CALIB = ("wood",)
LIFT = 1.0


def build(spr, rng):
    hx, hy = spr.hx, spr.hy

    def px(col, row, y):
        """The point drawn at pixel (col, row) lying y cells back."""
        return np.array([(col + 0.5 - hx) / 16.0, y, (hy - row - 0.5 - 16.0 * y) / 8.0])

    tex, bark = kit.bark_from(spr, None, "mottle", gain=1.25)
    wood = kit.Geo()

    # trunk: the drawn centre from the foot to the fork, 1.3x the drawn width
    # across and deeper than wide, flaring at the foot toward its roots
    roots = [(-135, 0.8), (-68, 0.5), (-12, 0.6), (58, 0.35), (148, 0.4)]
    ph = np.radians([a for a, _ in roots])
    Tp = np.array([px(26, 113, 0.0) - [0, 0, 0.15], px(26, 102, 0.05), px(26, 90, 0.1), px(25.5, 80, 0.16), px(25, 70, 0.22)])
    Tp = np.array([Tp[0]] + [Tp[0] + (Tp[1] - Tp[0]) * f for f in (0.15, 0.35, 0.6)] + list(Tp[1:]))
    RXt = np.interp(Tp[:, 2], [0, 1.5, 3.5, 4.8], [0.7, 0.64, 0.64, 0.74])
    RYt = np.interp(Tp[:, 2], [0, 1.5, 3.5, 4.8], [1.0, 0.95, 0.95, 1.0])

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
        wd.root(wood, base, math.radians(a), reach, 0.8, rng, bark * 0.85)

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

    # left prong: up and left near the front, its top leaning back
    L = limb([(18, 74, 0.2), (16, 60, 0.22), (13.5, 48, 0.25), (11.5, 38, 0.35), (9, 27, 0.55), (6, 19, 0.8),
              (4, 14, 1.0)],
             [0.4, 0.38, 0.36, 0.34, 0.32, 0.3, 0.28], [0.56, 0.54, 0.5, 0.48, 0.45, 0.42, 0.4], dict(length=(0.3, 0.55), splay=(3, 10)))
    # right prong: up and well back, bending right into its split end
    R = limb([(29, 86, 0.1), (30.5, 76, 0.3), (32, 64, 0.7), (33.5, 50, 1.3), (35, 38, 1.85), (38, 28, 2.3),
              (42.5, 19.5, 2.75), (46, 13, 3.1)],
             [0.5, 0.44, 0.4, 0.38, 0.37, 0.36, 0.34, 0.32], [0.64, 0.58, 0.54, 0.52, 0.5, 0.48, 0.46, 0.44],
             dict(n=(3, 3), length=(0.25, 0.45), splay=(5, 15)))
    # the thin middle spike, out of the right prong
    limb([(33.5, 31, 2.1), (30.5, 16, 2.2), (27.5, 5, 2.25)], [0.17, 0.15, 0.13], [0.22, 0.2, 0.18],
         dict(n=(2, 2), length=(0.25, 0.4)), sides=8)
    # stubs, tapered, with split ends
    limb([(36.5, 55, 1.15), (40, 54, 1.08), (43, 53, 1.0)], [0.14, 0.13, 0.12], [0.17, 0.15, 0.14],
         dict(n=(2, 2), length=(0.12, 0.22)), sides=7)
    limb([(37.5, 34.5, 2.0), (41, 34, 1.95), (44, 33.5, 1.9)], [0.13, 0.12, 0.11], [0.16, 0.14, 0.13],
         dict(n=(2, 2), length=(0.12, 0.22)), sides=7)
    limb([(12, 32, 0.37), (16.5, 30.5, 0.42), (20, 29, 0.46)], [0.11, 0.1, 0.09], [0.13, 0.12, 0.11],
         dict(n=(2, 2), length=(0.1, 0.2)), sides=6)
    parts = [wood.to_object(spr.name + "_wood", kit.material("bark", kit.image("bark", tex), repeat=True))]
    return parts, {"wood_tris": wood.tris(), "left_top": np.round(L[-1], 2).tolist(), "right_top": np.round(R[-1], 2).tolist()}
