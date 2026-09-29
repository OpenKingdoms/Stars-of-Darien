"""Aracrop09, a patch of four rows of standing corn, built on its own:

    blender -b --factory-startup --python Aracrop09.py [-- trial suffix]

Four rows from its picture's tassel bands; the left edge steps in at the
second and third rows as the picture's does, their end plants spreading no
extra leaves there, and one leaf strays off the front left.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402
import _corn  # noqa: E402

NAME = "Aracrop09"
H = 1.452  # the tassel tops, lowered an eighth from the pixel fit
P = {"seed": 9, "h": H, "rows": _corn.rows_from_bands(23, (2, 13, 22, 31.5), H), "spans": [(-1.25, 1.5), (-1.05, 1.4), (-1.05, 1.3), (-1.17, 1.3)],
     "per_row": 7, "stalk_r": (0.052, 0.026), "soil": ((44, 32, 21), (22, 11, 10)), "furrow": 0.85,
     "lime": 0.85, "lit_from": 0.45, "khaki": 0.5, "deep_to": 0.3, "g0": (0.45, 1.0), "sun": 1.16,
     "tassel": 0.34, "tassel_w": 0.15,
     "strays": ((-1.2, -1.25, 190, 0.8),), "no_edge": ((1, -1), (2, -1))}


def build():
    return _corn.field(P)


if __name__ == "__main__":
    _common.main(NAME, build)
