"""ZonTmound06, the least mound: a compact ribbed drum of fused chimneys
under one continuous earthen dome, three stout fingers standing free under
the sprite's fingertips (x 6 to 30) with the dome low in the notch at
x 21, a lower finger on the right shoulder, and the broadest pale spoil
fan of the six across the front and lower left.

    blender -b --factory-startup --python ZonTmound06.py
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common as C  # noqa: E402

NAME = "ZonTmound06"
# the dome's skyline in the classic render (picture column, row), under the fingertips
SKY = [(1, 31), (2, 16), (4, 13), (5, 10), (19, 10), (21, 11), (23, 10), (33, 10), (34, 22), (35, 22), (36, 20), (39, 20), (40, 36)]
M = C.Mound(NAME, rx=0.97, ry=1.12, cy=0.32, sx=0.96, sr=0.2, dome=0.4, sky=SKY, taper=0.07, side=1.3, tilt=0.5)
# the sprite's fingertips (column, row) and each finger's radius
FINGERS = [(9, 2, 0.28), (17, 0, 0.28), (26, 0, 0.3), (35, 13, 0.27)]
OVAL = math.pi / 2  # ribs wider across than front to back
ZMAX = 4.6


def build(b):
    rng = random.Random(106)
    m = C.mats(b, body=(44, 37, 26), core=(41, 36, 24), neck=(48, 42, 30), cap=(144, 125, 90),
               fleck=(86, 73, 52), low=(45, 38, 29), sand=(84, 73, 51), sand2=(131, 114, 82),
               dark=(40, 35, 24), seed=6)
    angles = C.outer_ring(M, 18, rng)
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
        x, y, z0, top = C.place(M, zd, col, row, r, L=(1.0, 1.3), zmax=ZMAX)
        C.finger(b, m, x, y, z0, top, r, rng)
        tips.append((x, y))
        print("ZS_FINGER", NAME, "col %d row %d at %.2f %.2f dome %.2f tip %.2f (%.2f proud)" % (
            col, row, x, y, z0, top, top - z0))
    for x, y in C.ridges(M, angles, 8, (0.4, 0.86), rng, avoid=tips):
        C.knob(b, m, x, y, zd(x, y), rng.uniform(0.15, 0.4), rng.uniform(0.24, 0.28), rng)
    C.fan(b, M, m, math.radians(-200), math.radians(-35), 0.28, rng, emax=0.85)
    C.cull(b, m, M, zd)


if __name__ == "__main__":
    C.run(NAME, build)
