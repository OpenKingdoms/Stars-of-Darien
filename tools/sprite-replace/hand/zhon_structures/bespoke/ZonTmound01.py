"""ZonTmound01, the broad mound: a ribbed drum of fused chimneys under one
continuous earthen dome, five stout fingers standing free at the back of it
under the sprite's own fingertips (left and centre), the right shoulder
lower and flat, and pale spoil spilt at the front and front left.

    blender -b --factory-startup --python ZonTmound01.py
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common as C  # noqa: E402

NAME = "ZonTmound01"
# the dome's skyline in the classic render (picture column, row): 9 to 11 px
# under the fingertips across the middle, the right shoulder at rows 12 to 16
SKY = [(3, 40), (4, 28), (5, 17), (7, 14), (10, 12), (13, 11), (44, 11), (46, 13), (48, 14), (52, 12),
       (56, 13), (57, 16), (58, 24), (59, 33), (60, 40)]
M = C.Mound(NAME, rx=1.62, ry=1.78, cy=0.4, sx=1.12, sr=0.23, dome=0.6, sky=SKY, taper=0.07, side=1.15, tilt=0.8)
# the sprite's fingertips (column, row) and each finger's radius
FINGERS = [(13, 6, 0.3), (19, 2, 0.32), (26, 1, 0.33), (32, 0, 0.35), (40, 4, 0.32)]
OVAL = math.pi / 2  # ribs wider across than front to back


def build(b):
    rng = random.Random(101)
    m = C.mats(b, body=(37, 33, 22), core=(35, 31, 21), neck=(42, 35, 26), cap=(146, 124, 90),
               fleck=(73, 63, 45), low=(38, 33, 24), sand=(89, 80, 53), sand2=(130, 112, 83),
               dark=(40, 35, 24), seed=1)
    angles = C.outer_ring(M, 28, rng)
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
        x, y, z0, top = C.place(M, zd, col, row, r, zmax=7.4)
        C.finger(b, m, x, y, z0, top, r, rng)
        tips.append((x, y))
        print("ZS_FINGER", NAME, "col %d row %d at %.2f %.2f dome %.2f tip %.2f (%.2f proud)" % (
            col, row, x, y, z0, top, top - z0))
    for x, y in C.ridges(M, angles, 14, (0.4, 0.86), rng, avoid=tips):
        C.knob(b, m, x, y, zd(x, y), rng.uniform(0.15, 0.4), rng.uniform(0.26, 0.32), rng)
    C.fan(b, M, m, math.radians(-178), math.radians(-40), 0.28, rng)
    C.cull(b, m, M, zd)


if __name__ == "__main__":
    C.run(NAME, build)
