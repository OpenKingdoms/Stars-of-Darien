"""ZonTmound04, the leaning mound: a ribbed drum of fused chimneys under one
continuous earthen dome, three clusters of fused fingers under the
sprite's three peaks (x 12, 27 and 39) with the dome low in the notches
between, the right side held up, the whole mass leaning a little left,
and a broad pale spoil fan across the front and lower left.

    blender -b --factory-startup --python ZonTmound04.py
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common as C  # noqa: E402

NAME = "ZonTmound04"
# the dome's skyline in the classic render (picture column, row), under the fingertips
SKY = [(0, 22), (3, 12), (8, 11), (10, 12), (14, 12), (15, 11), (20, 11), (21, 10), (31, 10), (32, 12), (34, 14), (35, 12), (40, 12), (41, 14), (43, 20), (45, 27), (48, 29), (49, 40)]
M = C.Mound(NAME, rx=1.22, ry=1.44, cx=0.03, cy=0.42, sx=0.93, sr=0.22, dome=0.55, sky=SKY, taper=0.07, side=1.2, tilt=0.65)
# the sprite's fingertips (column, row) and each finger's radius
FINGERS = [(11, 2, 0.3), (13, 4, 0.28), (24, 1, 0.3), (27, 0, 0.32), (29, 2, 0.28), (37, 2, 0.3), (40, 3, 0.28)]
OVAL = math.pi / 2  # ribs wider across than front to back
ZMAX = 6.0
LEAN = -0.015  # the picture's top overhangs its foot on the left


def build(b):
    rng = random.Random(104)
    m = C.mats(b, body=(33, 30, 20), core=(32, 28, 20), neck=(37, 33, 22), cap=(131, 111, 80),
               fleck=(67, 58, 40), low=(35, 30, 21), sand=(77, 68, 43), sand2=(111, 98, 69),
               dark=(40, 35, 24), seed=4)
    angles = C.outer_ring(M, 22, rng)
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
    C.fan(b, M, m, math.radians(-200), math.radians(-30), 0.28, rng, emax=0.85)
    C.cull(b, m, M, zd)
    for bm in b.parts.values():  # the whole mass sheared over to the left
        for v in bm.verts:
            v.co.x += LEAN * v.co.z


if __name__ == "__main__":
    C.run(NAME, build)
