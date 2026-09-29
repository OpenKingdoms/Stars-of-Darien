"""Tile colour calibration (system Python): python tilecal.py names...

For each model, the mean colour over the roof in the drawing and in the
classic render resampled onto it; the ratio is folded into tilecal.json,
which verwall.palette applies. Rebuild after.
"""
import json
import os
import sys

import numpy as np

import calib

HERE = os.path.dirname(os.path.abspath(__file__))
PATH = os.path.join(HERE, "tilecal.json")


def ratio(name):
    """Mean colour over the roof: where the render shows tile and the
    drawing is reddish there (its dark mottling included), drawing / render."""
    a, r = calib.aligned(name)
    ren = (r[..., 3] > 128) & (r[..., 0] > 1.6 * r[..., 1]) & (r[..., 0] > 90)
    spr = (a[..., 3] > 128) & (a[..., 0] > 1.25 * a[..., 1]) & (a[..., 0] > 40)
    k = ren & spr
    n = int(k.sum())
    if n < 20:
        return None, 0
    return a[k][:, :3].mean(0) / np.maximum(r[k][:, :3].mean(0), 1), n


if __name__ == "__main__":
    cal = json.load(open(PATH)) if os.path.exists(PATH) else {}
    for n in sys.argv[1:]:
        k, cnt = ratio(n)
        if k is None:
            print("%-12s too few tile pixels" % n)
            continue
        old = np.array(cal.get(n, (1.0, 1.0, 1.0)))
        new = np.clip(old * k, 0.45, 1.3)
        cal[n] = [round(float(v), 3) for v in new]
        print("%-12s n=%4d ratio %s -> gain %s" % (n, cnt, k.round(3), cal[n]))
    json.dump(cal, open(PATH, "w"), indent=1, sort_keys=True)
