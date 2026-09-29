"""Tarbuild02b, the spiked hall with its roof fallen in.

    blender -b --factory-startup --python bespoke/Tarbuild02b.py

The round-4 ruin, built here from the hall's parts, plus the piece the
sprite draws at the back-left: the back wall's left end still standing
as a tall broken block (sprite columns 63 to 150, top near row 30), with
the snapped finial and the one whole spire standing on it.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common as C  # noqa: E402
from common import K  # noqa: E402
import spiked as S  # noqa: E402
import ruin02 as R2  # noqa: E402
from spiked import HALL, SPIRES  # noqa: E402

NAME = "Tarbuild02b"


def back_block(x0, y1, zr):
    """The back wall's left end standing to a torn top about z 7.5, deep
    enough to seat the finials: short sections, each to its own height and
    depth, stepping down at the right end into the ragged wall."""
    rnd = random.Random(12)
    mt = R2.roof_dark()
    xa, xb = x0 - 0.12, SPIRES[1] + 1.35
    n = 11
    w = (xb - xa) / n
    parts = []
    for i in range(n):
        x = xa + w * (i + 0.5)
        under = min(abs(x - SPIRES[0]), abs(x - SPIRES[1])) < 0.75
        top = 7.62 + rnd.uniform(0.0, 0.14) if under else 7.25 + rnd.uniform(0.0, 0.35)
        if i == n - 1:
            top = 6.95
        yf = y1 - rnd.uniform(1.1, 1.5)
        b = K.block(w * 1.03, y1 + 0.08 - yf, top - 3.0, x, (yf + y1 + 0.08) / 2, 3.0, 0, mt, "back_block")
        # the torn top slants a little, front edge lower
        for v in b.data.vertices:
            if v.co.z > top - 1e-3 and v.co.y < y1 - 0.5:
                v.co.z -= rnd.uniform(0.08, 0.22)
        K.uv_box(b, 0.45, i * 0.31, 0.2)
        parts.append(b)
    # a few broken stones fallen off its face onto the heap in front
    for k in range(4):
        x = rnd.uniform(xa + 0.4, xb - 0.6)
        s = rnd.uniform(0.45, 0.7)
        st = K.rock((x, y1 - 1.55 - rnd.uniform(0.0, 0.3), zr - 0.2), (s, s * 0.8, s * 0.6), rnd.randrange(10 ** 6), mt,
                    "back_chunk", jitter=0.3)
        K.uv_box(st, 0.45)
        parts.append(st)
    return parts


def build():
    cx, cy, W, D, H = HALL["cx"], HALL["cy"], HALL["W"], HALL["D"], HALL["H"]
    x0, x1, y0, y1 = cx - W / 2, cx + W / 2, cy - D / 2, cy + D / 2
    zr = H - 0.3
    # the whole roof has fallen in; the back wall is torn along its top
    holes = [[(x0 + 0.3, y0 + 0.3), (x1 - 0.3, y0 + 0.3), (x1 - 0.3, y1 - 0.3), (x0 + 0.3, y1 - 0.3)]]
    spires = {0: -1.2, 2: "gone", 3: "gone", 4: "gone", 5: "gone", 6: "gone"}
    fins = {(1, i): 0 for i in range(4)}
    parts = S.hall(**HALL, holes=holes, spires=spires, horns={1: "gone", 2: "gone"}, fins=fins, seed=6, walls="fl",
                   door_prop=False, rings=False, ragged={"b": (0.72, 1.0)})
    parts += back_block(x0, y1, zr)
    # the back posts, snapped flat and reaching down into the torn wall
    for i, top in ((2, 2.3), (3, 2.1), (4, 2.4), (5, 2.2), (6, 2.3)):
        parts += R2.post_stump(SPIRES[i], y1 - 0.35, zr - 1.4, zr + 1.4 + top * K.RISE, seed=70 + i)

    # the left two-thirds are the fallen roof's dark slabs; sandy gravel
    # shows only in a strip behind the ring slab and over the right third
    def strip(x, y):
        return -0.9 < x < 3.1 and 0.2 < y < 1.35

    def dark(x, y):
        return x < 2.4 and not strip(x, y)

    def fill(x, y):
        f = zr - 0.55 + 0.3 * max(0.0, min(1.0, (y - (y1 - 2.4)) / 1.6))
        f += 0.4 * max(0.0, min(1.0, (-2.0 - x) / 4.5))
        return min(zr - 0.15, f + 0.15 * math.sin(1.7 * x + 0.6) * math.cos(1.3 * y + 0.2))

    fx0, fx1, fy0, fy1 = x0 + 0.3, x1 - 0.3, y0 + 0.3, y1 - 0.3
    body = K.heightfield(fx0, fx1, fy0, fy1, 21, 7, fill, R2.gravel(), "rubble_fill")
    body.data.materials.append(R2.roof_gaps())
    for pg in body.data.polygons:
        if dark(pg.center.x, pg.center.y):
            pg.material_index = 1
    K.uv_box(body, 0.45)
    parts.append(body)
    p, surf, ins = K.boulder_heaps([(1.2, 0.75, 1.9, 0.5, 0.3), (4.1, 0.0, 1.6, 2.6, 1.0, 10),
                                    (6.1, 0.3, 2.3, 2.6, 1.0), (5.1, -2.3, 2.1, 1.0, 0.6)], R2.gravel(),
                                   R2.gravel_stones(), fill, seed=41, per_area=0.75, size=(0.3, 0.6), power=1.4)
    parts += p

    def talus(x, y):
        d = max(0.0, x0 - x)
        e = max(0.0, abs(y - 0.3) - 2.9)
        return max(0.0, 3.7 * (1 - d / 4.3) - 1.6 * e)

    p, _, _ = K.boulder_heaps([(x0 - 1.5, 0.5, 3.0, 4.7, 0.45, 0, (0.3, 0.2))], R2.pebbles(), R2.tan_stones(), talus,
                              seed=51, per_area=1.0, size=(0.3, 0.55))
    parts += p
    p, _, _ = K.boulder_heaps([(9.9, -0.5, 1.7, 3.4, 3.2, 0, (0.5, 0.0))], R2.gravel(), R2.gravel_stones(), None,
                              seed=61, per_area=1.3, size=(0.3, 0.6), power=1.4)
    parts += p
    p, _, _ = K.boulder_heaps([(-2.9, y0 - 0.5, 5.0, 1.1, 0.55, 0, (0.0, -0.3)), (4.6, y0 - 0.4, 2.2, 0.8, 0.4)],
                              R2.pebbles(), R2.tan_stones(), None, seed=62, per_area=1.0, size=(0.25, 0.45))
    parts += p
    dk = R2.roof_dark()
    named = [((-5.3, 1.3, 4.2, 2.6, -6, 28, -14), None), ((-4.9, -1.35, 4.6, 2.5, 6, -14, 16), (-0.2, 0.25)),
             ((-7.5, 0.1, 1.3, 5.3, 2, 4, -38), None), ((-0.2, -0.85, 5.6, 2.3, -3, 7, -4), (0.9, 0.2))]
    for (x, y, w, d, yaw, tilt, roll), ring in named:
        z = zr - 0.05 if ring == (0.9, 0.2) else fill(x, y) + 0.3
        sl = R2.slab(x, y, z, w, d, yaw, tilt, roll, ring=ring, mt=dk)
        # ring_blade leaves the ring and blade 0.05 over the slab; bed them in it
        for p in sl[1:]:
            K.move(p, (0, 0, -0.08))
        parts += sl
    avoid = [(-6.0, -4.2, -1.8, -0.4), (-2.8, 2.6, -1.9, 0.2)]
    parts += R2.roof_jumble(dark, fill, (x0 + 0.5, 2.3, y0 + 0.5, y1 - 0.4), avoid, dk, seed=43)
    parts += R2.slabs([(1.2, -2.5, 0.25, 1.5, 1.2, -25, -24)], fill, mt=dk)
    parts += R2.slabs([(3.6, -1.0, 0.3, 2.1, 1.6, 12, 22), (5.8, 0.8, 0.3, 2.0, 1.5, -18, -28),
                       (7.3, -1.9, 0.25, 1.9, 1.5, 30, 32), (2.0, 1.4, 0.3, 1.8, 1.4, 40, 20),
                       (4.6, -2.6, 0.25, 1.8, 1.4, -8, -35), (7.6, 1.2, 0.3, 1.6, 1.3, 70, 30),
                       (8.7, -0.3, 0.25, 1.4, 1.6, 15, 28)], surf)
    return parts


if __name__ == "__main__":
    C.main(NAME, build)
