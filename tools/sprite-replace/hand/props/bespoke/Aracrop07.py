"""Aracrop07, a patch of six rows of standing corn, built on its own:

    blender -b --factory-startup --python Aracrop07.py [-- trial suffix]

Six rows from its picture's six pale tassel bands, the widest patch of the
set with one leaf straying off its left edge; its earth all brown, with no
red-black furrows (the picture has none).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common  # noqa: E402
import _corn  # noqa: E402

NAME = "Aracrop07"
H = 1.54  # the tassel tops, a monarch's 0.4, lowered an eighth from the pixel fit
P = {"seed": 7, "h": H, "rows": _corn.rows_from_bands(37, (4, 15, 24, 34, 43, 54), H), "span": (-1.75, 1.8),
     "per_row": 8, "strays": ((-1.85, -0.3, 185, 0.6),), "stalk_r": (0.052, 0.026),
     "soil": ((44, 32, 21), (30, 22, 14)), "furrow": 0.4, "lime": 0.85, "lit_from": 0.45, "khaki": 0.5,
     "deep_to": 0.3, "g0": (0.45, 1.0), "sun": 1.22, "tassel": 0.34, "tassel_w": 0.15}


def build():
    return _corn.field(P)


if __name__ == "__main__":
    _common.main(NAME, build)
