"""Aracrop10, a patch of four rows of standing corn, built on its own:

    blender -b --factory-startup --python Aracrop10.py [-- trial suffix]

Four rows from its picture's tassel bands, one leaf straying off the
right; the furrows a shade darker, for the picture's heavier dark share.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402
import _corn  # noqa: E402

NAME = "Aracrop10"
H = 1.452  # the tassel tops, lowered an eighth from the pixel fit
P = {"seed": 10, "h": H, "rows": _corn.rows_from_bands(24, (4, 14, 23, 32.5), H), "span": (-1.35, 1.2),
     "per_row": 7, "stalk_r": (0.052, 0.026), "soil": ((44, 32, 21), (22, 11, 10)), "furrow": 0.85,
     "lime": 0.85, "lit_from": 0.45, "khaki": 0.5, "deep_to": 0.42, "g0": (0.45, 1.0), "sun": 1.16,
     "tassel": 0.34, "tassel_w": 0.15,
     "strays": ((1.15, -0.5, 5, 0.65),)}


def build():
    return _corn.field(P)


if __name__ == "__main__":
    _common.main(NAME, build)
