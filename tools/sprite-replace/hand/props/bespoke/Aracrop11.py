"""Aracrop11, a patch of four rows of standing corn, built on its own:

    blender -b --factory-startup --python Aracrop11.py [-- trial suffix]

Four rows from its picture's tassel bands, the front row running further
left into the picture's lower-left corner.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402
import _corn  # noqa: E402

NAME = "Aracrop11"
H = 1.32  # the tassel tops, lowered an eighth from the pixel fit
P = {"seed": 11, "h": H, "rows": _corn.rows_from_bands(27, (4, 15, 26, 37.5), H), "spans": [(-1.1, 1.05), (-1.1, 1.05), (-1.1, 1.05), (-1.3, 1.05)], "spacing": 0.7,
     "per_row": 6, "stalk_r": (0.052, 0.026), "soil": ((44, 32, 21), (22, 11, 10)), "furrow": 0.85,
     "lime": 0.85, "lit_from": 0.45, "khaki": 0.5, "deep_to": 0.42, "g0": (0.45, 1.0), "sun": 1.16,
     "tassel": 0.34, "tassel_w": 0.15}


def build():
    return _corn.field(P)


if __name__ == "__main__":
    _common.main(NAME, build)
