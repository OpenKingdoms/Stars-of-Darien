"""Aracrop08, a patch of six rows of standing corn, built on its own:

    blender -b --factory-startup --python Aracrop08.py [-- trial suffix]

Six rows from its picture's six tassel bands, each row's ends where the
picture's are; the red-black furrows of its picture between the rows.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402
import _corn  # noqa: E402

NAME = "Aracrop08"
H = 1.496  # the tassel tops, lowered an eighth from the pixel fit
P = {"seed": 8, "h": H, "rows": _corn.rows_from_bands(32, (3, 15, 24, 33, 43, 52), H), "spans": [(-1.95, 1.8), (-1.85, 1.9), (-1.9, 1.75), (-1.85, 1.85), (-1.95, 2.1),
              (-1.8, 1.95)],
     "per_row": 8, "stalk_r": (0.052, 0.026), "soil": ((44, 32, 21), (22, 11, 10)), "furrow": 0.6,
     "lime": 0.85, "lit_from": 0.45, "khaki": 0.5, "deep_to": 0.3, "g0": (0.45, 1.0), "sun": 1.22,
     "tassel": 0.34, "tassel_w": 0.17, "tassel_n": 4, "ears": 0.15}


def build():
    return _corn.field(P)


if __name__ == "__main__":
    _common.main(NAME, build)
