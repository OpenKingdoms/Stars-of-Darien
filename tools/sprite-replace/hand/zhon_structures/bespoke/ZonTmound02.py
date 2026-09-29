"""ZonTmound02, the small mound: a ribbed drum of fused chimneys under one
continuous earthen dome, four stout fingers standing free at the back of it
under the sprite's fingertips (x 18 to 40), its side shoulders held up to
the picture's own, and pale spoil spilt at the front left.

    blender -b --factory-startup --python ZonTmound02.py
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common as C  # noqa: E402

NAME = "ZonTmound02"
# the dome's skyline in the classic render (picture column, row), under the fingertips
SKY = [(3, 30), (4, 13), (9, 12), (12, 10), (17, 10), (41, 10), (43, 9), (44, 15), (47, 18), (48, 30)]
M = C.Mound(NAME, rx=1.13, ry=1.31, cx=0.03, cy=0.3, sx=1.0, sr=0.22, dome=0.45, sky=SKY, taper=0.07, side=1.3, tilt=0.6)
# the sprite's fingertips (column, row) and each finger's radius
FINGERS = [(20, 2, 0.29), (24, 0, 0.3), (31, 0, 0.31), (38, 4, 0.29)]
OVAL = math.pi / 2  # ribs wider across than front to back
ZMAX = 5.2


def build(b):
    rng = random.Random(102)
    m = C.mats(b, body=(41, 36, 24), core=(39, 34, 23), neck=(46, 38, 29), cap=(147, 124, 90),
               fleck=(81, 70, 49), low=(43, 37, 26), sand=(86, 75, 50), sand2=(134, 109, 76),
               dark=(40, 35, 24), seed=2)
    angles = C.outer_ring(M, 20, rng)
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
        x, y, z0, top = C.place(M, zd, col, row, r, L=(1.1, 1.4), zmax=ZMAX)
        C.finger(b, m, x, y, z0, top, r, rng)
        tips.append((x, y))
        print("ZS_FINGER", NAME, "col %d row %d at %.2f %.2f dome %.2f tip %.2f (%.2f proud)" % (
            col, row, x, y, z0, top, top - z0))
    for x, y in C.ridges(M, angles, 10, (0.4, 0.86), rng, avoid=tips):
        C.knob(b, m, x, y, zd(x, y), rng.uniform(0.15, 0.4), rng.uniform(0.25, 0.3), rng)
    C.fan(b, M, m, math.radians(-175), math.radians(-50), 0.25, rng)
    C.cull(b, m, M, zd)


if __name__ == "__main__":
    C.run(NAME, build)
