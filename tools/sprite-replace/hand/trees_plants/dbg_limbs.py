"""Prints each lifted limb of a limbs model: parent, side, mirrored, ends
and segment elevations (scaled frame). blender -b --python dbg_limbs.py -- Name"""
import math
import os
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.normpath(os.path.join(HERE, "..", "..")))
import numpy as np  # noqa: E402
import kit  # noqa: E402
import models  # noqa: E402
import sprite2d as s2  # noqa: E402

name = sys.argv[sys.argv.index("--") + 1]
spec = models.MODELS[name]
spr = s2.Sprite(name)
rng = np.random.default_rng(zlib.crc32(name.encode()) + spec.get("seed", 0))
g, h, r = spec.get("sturdy", kit.STURDY.get(spec["kind"], (1.0, 1.0, 1.0)))
kit.SHAPE.update(girth=g, height=h, radius=r)
mask = spr.mask
br = s2.skeleton_tree(mask, spec.get("root", (spr.hx, spr.hy)), prune=spec.get("prune", (3.0, 1.2)), bridge=10.0)
ls = dict(spec.get("lift", {}))
ls.setdefault("rscale", 1.25)
limbs = kit.lift_smooth(spr, br, ls, rng) if ls.get("mode") in ("swing", "balanced") else kit.lift(spr, br, ls, rng)
S = np.array([g, g, h])
for i, L in enumerate(limbs):
    Q = L["P"] * S
    d = np.diff(Q, axis=0)
    el = [round(math.degrees(math.atan2(v[2], math.hypot(v[0], v[1])))) for v in d]
    print(i, "par", L["parent"], "side", L.get("side"), "mir", L.get("mir"), "start", np.round(Q[0], 2), "end", np.round(Q[-1], 2),
          "R0 %.2f" % (L["R"][0] * r), "elev", el)
