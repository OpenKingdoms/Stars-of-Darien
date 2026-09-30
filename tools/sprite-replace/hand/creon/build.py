"""Builds the creon family in one Blender session.

    blender -b --factory-startup --python build.py [-- Name Name ...]

Each model goes to $OK_REPLACE/hand/creon/models/<Name>.glb (OK_REPLACE
is D:/OKReplace unless set), its classic and turned renders to
.../renders. Before the export the vertex colours are scaled so the
classic render's mean colour meets the model's target: the drawing's mean
times the spec's tone. Then

    python ../../handcompare.py <renders> <Name> <sprites>/<Name>.png <scale>

puts the drawing and the render side by side.
"""
import os
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.normpath(os.path.join(HERE, "..", ".."))
for p in (HERE, KIT):
    if p not in sys.path:
        sys.path.insert(0, p)

import numpy as np  # noqa: E402

import handkit as hk  # noqa: E402
import kit  # noqa: E402
import models  # noqa: E402
import stones  # noqa: E402

BUILDERS = dict(stones.BUILDERS)


def bespoke(kind):
    """A builder in its own module, loaded when first asked for."""
    import importlib
    return importlib.import_module(kind).build


def build(name, render=True):
    hk.reset()
    spec = models.MODELS[name]
    spr = kit.Sprite(name)
    make = BUILDERS.get(spec["kind"]) or bespoke(spec["kind"])
    parts, info = make(spr, spec)
    target = np.asarray(spec.get("target", spr.mean() * spec.get("tone", 0.8)))
    before = kit.calibrate(parts[0], spr, target, os.path.join(kit.OUT, "work")) if spec.get("calib", True) else None
    glb = os.path.join(kit.OUT, "models", name + ".glb")
    extras = {"feature": name, "family": "creon"}
    ob = hk.finish(parts, glb, extras)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print("CREON_BUILT", name, "tris", tris, info, "target", np.round(target, 3),
          "before", None if before is None else np.round(before["render"], 3))
    if render:
        scale = spec.get("scale", 2)
        hk.renders(ob, os.path.join(kit.OUT, "renders"), name, spr.path, (spr.hx, spr.hy), scale=scale)
        m = kit.fit(spr, os.path.join(kit.OUT, "renders", name + "_classic.png"))
        if m:
            print("CREON_FIT", name, "iou %.3f cover %.3f spill %.3f" % (m["iou"], m["cover"], m["spill"]),
                  "render", np.round(m["render"], 3), "drawing", np.round(spr.mean(), 3))
    return tris


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "--norender" not in argv
    names = [a for a in argv if not a.startswith("--")] or list(models.ORDER)
    for n in names:
        t0 = time.time()
        try:
            build(n, render)
        except Exception as e:
            traceback.print_exc()
            print("CREON_FAILED", n, repr(e))
        print("CREON_TIME", n, "%.1fs" % (time.time() - t0))


main()
