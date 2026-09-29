"""Builds the spires family from the table in models.py, run inside Blender:

    blender -b --factory-startup --python build.py [-- Name ...]

Each model goes to D:/OKReplace/hand/spires/models/<Name>.glb, with its
classic and turned renders in renders/. Then, outside Blender,
`python sheets.py` makes the compares and the contact sheets.
"""
import json
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import kit  # noqa: E402
import models  # noqa: E402
from kit import hk  # noqa: E402

CATALOG = r"D:\OKReplace\catalog.json"
BUILDERS = {"crag": kit.crag, "hoodoo": kit.hoodoo}


def make(p):
    """The parts of one table entry, after the sturdier pass."""
    p = kit.sturdy(dict(p))
    return BUILDERS[p["kind"]](p)


def build(name, rec, out=None, tag=""):
    out = out or kit.OUT
    hk.reset()
    parts = make(models.MODELS[name])
    kit.tidy(parts)
    kit.mk.whiten(parts)
    parts[0].name = name
    glb = os.path.join(out, "models", name + tag + ".glb")
    ob = hk.finish(parts, glb, {"feature": name, "family": "spires"})
    tris = kit.tris(ob)
    print("SPIRES_TRIS", name, tris)
    vs = bpy.context.scene.view_settings
    vs.view_transform = "Standard"
    vs.look = "None"
    hk.renders(ob, os.path.join(out, "renders"), name + tag, os.path.join(kit.SPRITES, name + ".png"),
               tuple(rec["sprite"]["hotspot"]), scale=2)
    return tris


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(models.MODELS)
    cat = {r["name"]: r for r in json.load(open(CATALOG))}
    ok, failed = [], []
    for n in names:
        try:
            ok.append((n, build(n, cat[n])))
        except Exception as e:  # keep going, report at the end
            import traceback
            traceback.print_exc()
            failed.append((n, repr(e)))
    for n, t in ok:
        print("SPIRES_OK", n, t)
    for n, e in failed:
        print("SPIRES_FAIL", n, e)


if __name__ == "__main__":
    main()
