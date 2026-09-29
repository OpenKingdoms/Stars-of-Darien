"""ZonTmound05, the tall mound: a ribbed drum of fused chimneys under one
continuous earthen dome, a tall cluster of six stout fingers in the middle
(x 15 to 40), a separate shorter pillar on the far left wall behind a
notch, a flat right shoulder, and pale spoil spilt across the front.

    blender -b --factory-startup --python ZonTmound05.py
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common as C  # noqa: E402

NAME = "ZonTmound05"
# the dome's skyline in the classic render (picture column, row), under the fingertips
SKY = [(2, 60), (3, 31), (8, 31), (9, 36), (10, 36), (11, 26), (12, 20), (14, 14), (20, 13), (21, 12), (38, 11), (39, 13), (44, 14), (45, 15), (57, 15), (58, 25), (63, 29), (64, 60)]
M = C.Mound(NAME, rx=1.3, ry=1.95, cx=0.21, cy=0.5, sx=1.0, sr=0.24, dome=0.6, sky=SKY, taper=0.07, side=1.15, tilt=0.85)
# the sprite's fingertips (column, row) and each finger's radius
FINGERS = [(16, 5, 0.32), (23, 2, 0.33), (28, 0, 0.35), (33, 0, 0.35), (37, 1, 0.33), (42, 7, 0.3)]
OVAL = math.pi / 2  # ribs wider across than front to back
ZMAX = 7.4
PILLAR = (8.2, 25, 0.42, 0.45)  # column, row, radius, depth


def build(b):
    rng = random.Random(105)
    m = C.mats(b, body=(35, 31, 21), core=(33, 30, 20), neck=(40, 33, 24), cap=(137, 116, 83),
               fleck=(71, 60, 43), low=(37, 32, 22), sand=(77, 68, 44), sand2=(120, 105, 75),
               dark=(40, 35, 24), seed=5)
    angles = C.outer_ring(M, 24, rng)
    zd = C.core(b, M, m, angles, drop=0.15)
    for a in angles:
        x, y = M.at(a)
        wall = zd(x, y)
        if math.sin(a) > -0.3:
            tip = wall + rng.uniform(0.0, 0.25)
        elif rng.random() < 0.3:
            tip = wall * rng.uniform(0.55, 0.9)  # a short chimney: a pale knob down the face
        else:
            tip = wall + rng.uniform(0.12, 0.38)
        C.rib(b, m, M, a, wall, tip, M.sr * rng.uniform(1.0, 1.12), rng, face=OVAL, flat=0.7)
    tips = []
    for col, row, r in FINGERS:
        x, y, z0, top = C.place(M, zd, col, row, r, L=(1.2, 1.5), zmax=ZMAX)
        C.finger(b, m, x, y, z0, top, r, rng)
        tips.append((x, y))
        print("ZS_FINGER", NAME, "col %d row %d at %.2f %.2f dome %.2f tip %.2f (%.2f proud)" % (
            col, row, x, y, z0, top, top - z0))
    for x, y in C.ridges(M, angles, 15, (0.4, 0.86), rng, avoid=tips):
        C.knob(b, m, x, y, zd(x, y), rng.uniform(0.15, 0.4), rng.uniform(0.28, 0.34), rng)
    # the separate pillar on the far left: a stout chimney from the ground beside
    # the wall, its cap near the sprite's own at column 6, row 25, a cell under the
    # cluster (held to ZMAX - 1), joined to the wall behind by a lower fused
    # chimney so its top stands apart over the notch but it is no lone stalk
    x, y, r = M.x_of(PILLAR[0]), PILLAR[3], PILLAR[2]
    top = min(ZMAX - 1.0, ((M.hy - PILLAR[1]) / C.K - 9 * r - 16 * y) / 8)
    C.column(b, m, x, y, 0.0, top, r, rng, seg=8, step=0.5, flare=1.1)
    xs, ys = x + 0.36, y + 0.3
    C.column(b, m, xs, ys, 0.0, top * 0.75, r * 0.95, rng, seg=8, step=0.5, flare=1.3)
    print("ZS_PILLAR", NAME, "at %.2f %.2f tip %.2f, saddle tip %.2f" % (x, y, top, top * 0.75))
    C.fan(b, M, m, math.radians(-165), math.radians(-15), 0.28, rng)
    C.cull(b, m, M, zd)


if __name__ == "__main__":
    C.run(NAME, build)
