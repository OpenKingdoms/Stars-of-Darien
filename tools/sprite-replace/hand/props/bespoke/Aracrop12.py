"""Aracrop12, a patch of four rows of standing corn, built on its own:

    blender -b --factory-startup --python Aracrop12.py [-- trial suffix]

Four rows from its picture's tassel bands, wide apart; the earth behind
the back row brown, not black, and pulled in closer.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402
import _corn  # noqa: E402

NAME = "Aracrop12"
H = 1.364  # the tassel tops, lowered an eighth from the pixel fit
P = {"seed": 12, "h": H, "rows": _corn.rows_from_bands(27, (2, 15, 27, 37), H), "span": (-1.15, 1.25), "spacing": 0.75,
     "per_row": 6, "stalk_r": (0.052, 0.026), "soil": ((44, 32, 21), (22, 11, 10)), "furrow": 0.6,
     "lime": 0.85, "lit_from": 0.45, "khaki": 0.5, "deep_to": 0.42, "g0": (0.45, 1.0), "sun": 1.3,
     "tassel": 0.38, "tassel_w": 0.15,
     "soil_back": 0.45}


def build():
    return _corn.field(P)


if __name__ == "__main__":
    _common.main(NAME, build)
