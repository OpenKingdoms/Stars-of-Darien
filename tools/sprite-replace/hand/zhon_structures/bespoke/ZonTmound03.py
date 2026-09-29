"""ZonTmound03, the mound with the left-hand peak: a ribbed drum of fused
chimneys under one continuous earthen dome, two stout fingers at the
left-of-centre peak (x 18 to 33) and two lower on the right shoulder
(x 36 to 45), and pale spoil spilt across the front.

    blender -b --factory-startup --python ZonTmound03.py
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common as C  # noqa: E402

NAME = "ZonTmound03"
# the dome's skyline in the classic render (picture column, row), under the fingertips
SKY = [(3, 40), (4, 23), (7, 20), (9, 13), (15, 12), (17, 11), (33, 11), (35, 14), (36, 17), (48, 17), (52, 20), (53, 36)]
M = C.Mound(NAME, rx=1.35, ry=1.56, cx=-0.05, cy=0.4, sx=1.0, sr=0.23, dome=0.55, sky=SKY, taper=0.07, side=1.25, tilt=0.7)
# the sprite's fingertips (column, row) and each finger's radius
FINGERS = [(21, 2, 0.3), (28, 0, 0.3), (38, 8, 0.3), (44, 9, 0.3)]
OVAL = math.pi / 2  # ribs wider across than front to back
ZMAX = 6.2


def build(b):
    rng = random.Random(103)
    m = C.mats(b, body=(43, 37, 26), core=(40, 36, 24), neck=(47, 40, 30), cap=(148, 126, 91),
               fleck=(85, 72, 51), low=(45, 38, 28), sand=(78, 69, 46), sand2=(117, 101, 73),
               dark=(40, 35, 24), seed=3)
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
    for x, y in C.ridges(M, angles, 12, (0.4, 0.86), rng, avoid=tips):
        C.knob(b, m, x, y, zd(x, y), rng.uniform(0.15, 0.4), rng.uniform(0.26, 0.3), rng)
    C.fan(b, M, m, math.radians(-160), math.radians(-20), 0.25, rng)
    C.cull(b, m, M, zd)


if __name__ == "__main__":
    C.run(NAME, build)
