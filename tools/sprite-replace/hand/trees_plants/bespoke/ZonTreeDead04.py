"""ZonTreeDead04, the Zhon forked snag, shaped against its own sprite.

Every limb follows its drawn pixels, each point pushed back along the
classic camera's ray to a chosen depth, so the classic view keeps the
drawing while the fork opens front to back: the right prong, drawn
tallest, leans well back, the left one only a little, which also keeps the
tree low. The trunk is 1.3 times the drawn width across and deeper than
wide, the prongs about 1.2 times their drawn width and tapering only in
their last third, and every end splits into tapered wedges. Five low
buttress roots of uneven reach settle onto the ground, the back ones short.
The bark is the drawing's own dark-to-light ramp, toned by _tone.fit.
"""
import math

import numpy as np

import _tone
import _wood as wd
import kit

CALIB = ()  # _tone.fit sets the bark's contrast and level instead
LIFT = 1.0


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
    roots = [(-165, 0.75), (-100, 0.55), (-38, 0.8), (52, 0.4), (138, 0.45)]
    ph = np.radians([a for a, _ in roots])
    Tp = np.array([px(27, 122, 0.0) - [0, 0, 0.15], px(27, 110, 0.05), px(27, 98, 0.1), px(26.5, 88, 0.18),
                   px(26, 80, 0.25)])
    Tp = np.array([Tp[0]] + [Tp[0] + (Tp[1] - Tp[0]) * f for f in (0.15, 0.35, 0.6)] + list(Tp[1:]))
    RXt = np.interp(Tp[:, 2], [0, 1.5, 4.0, 5.0], [0.62, 0.57, 0.59, 0.68])
    RYt = np.interp(Tp[:, 2], [0, 1.5, 4.0, 5.0], [0.86, 0.8, 0.8, 0.85])

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

    # left prong: up and a little back, splitting at the top into a branch
    # to the far left tip and one straight on
    L = limb([(22, 86, 0.2), (19.5, 70, 0.35), (16, 56, 0.45), (13, 44, 0.55), (11, 36, 0.62)],
             [0.4, 0.38, 0.36, 0.34, 0.33], [0.6, 0.58, 0.55, 0.52, 0.48], dict(n=(2, 2), length=(0.25, 0.4)), taper_to=0.85)
    limb([(11, 38, 0.6), (6, 31.5, 0.5), (2.5, 26.5, 0.4)], [0.27, 0.25, 0.23], [0.36, 0.34, 0.32],
         dict(length=(0.3, 0.5)))
    limb([(11.5, 38, 0.6), (12.5, 29, 0.85), (13, 21, 1.1)], [0.28, 0.26, 0.24], [0.38, 0.36, 0.34],
         dict(length=(0.3, 0.5)))
    # right prong: drawn tallest, so it leans well back
    R = limb([(29, 96, 0.12), (30.5, 84, 0.35), (32, 70, 0.95), (34, 56, 1.7), (36, 42, 2.4), (38, 28, 3.0), (40, 16, 3.5)],
             [0.5, 0.44, 0.38, 0.37, 0.35, 0.33, 0.3], [0.62, 0.56, 0.5, 0.48, 0.46, 0.43, 0.4],
             dict(n=(3, 3), length=(0.3, 0.55), first=1.05))
    # the thin middle spike, out of the right prong and back
    limb([(33, 52, 1.95), (30.5, 32, 2.45), (28.5, 12, 2.85)], [0.22, 0.2, 0.18], [0.28, 0.26, 0.23],
         dict(n=(2, 2), length=(0.3, 0.5)), sides=8)
    # stubs: right off the right prong twice, left off the left prong
    limb([(37, 47, 2.15), (40.5, 44.5, 2.05), (43.5, 42, 1.95)], [0.17, 0.15, 0.14], [0.2, 0.18, 0.16],
         dict(n=(2, 2), length=(0.15, 0.25)), sides=7)
    limb([(39.5, 25, 3.1), (41.5, 22.5, 3.05), (43.5, 20.5, 3.0)], [0.12, 0.11, 0.1], [0.14, 0.13, 0.12],
         dict(n=(2, 2), length=(0.15, 0.3)), sides=6)
    limb([(15, 53, 0.45), (12, 52.5, 0.35), (9.5, 52.5, 0.28)], [0.14, 0.13, 0.12], [0.17, 0.15, 0.14],
         dict(n=(2, 2), length=(0.15, 0.3)), sides=6)
    parts = [wood.to_object(spr.name + "_wood", kit.material("bark", tb.img, repeat=True))]
    for p in parts:
        p.visible_shadow = True
    tone = _tone.fit(parts, spr, tb)
    return parts, {"tone": tone, "wood_tris": wood.tris(), "left_top": np.round(L[-1], 2).tolist(), "right_top": np.round(R[-1], 2).tolist()}
