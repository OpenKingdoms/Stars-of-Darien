"""Builds the props family from the table in models.py, run inside Blender:

    blender -b --factory-startup --python build.py [-- Name ...]

Each model goes to D:/OKReplace/hand/props/models/<Name>.glb, geometry
only, with its classic and turned renders in renders/. A model with parts
painted from the picture ships them as okPaint materials; its copy with the
pixels, for review only, goes to review/. Then, outside Blender,
`python sheets.py` makes the compares and the contact sheets, and
`python glbcheck.py` checks no shipped model holds an image.
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
from kit import hk, mk  # noqa: E402

CATALOG = r"D:\OKReplace\catalog.json"
_box = hk.box


def _marker_box(*a, **k):
    # the monarch marker casts no shadow, which would black out a prop's
    # far-right piece in the turned view
    ob = _box(*a, **k)
    if k.get("name") == "hk_marker":
        ob.visible_shadow = False
    return ob


hk.box = _marker_box


def build(name, rec):
    p = dict(kit.sturdy(models.MODELS[name]), _sprite=os.path.join(kit.SPRITES, name + ".png"),
             _hotspot=tuple(rec["sprite"]["hotspot"]), _footprint=tuple(rec["footprint"]))
    hk.reset()
    parts = getattr(kit, p["kind"])(p)
    painted = [q.name for q in parts if q.get("painted")]
    stamped = any(q.get(hk.carve.PLAYERS_FILES) for q in parts)
    mk.tidy(parts)
    if stamped:
        parts[0][hk.carve.PLAYERS_FILES] = True
    mk.whiten(parts)
    parts[0].name = name
    extras = {"feature": name, "family": "props"}
    if painted:
        extras["projectPainted"] = painted
    shipped = os.path.join(kit.OUT, "models", name + ".glb")
    ob = hk.finish(parts, os.path.join(kit.OUT, "review", name + ".glb") if painted else shipped, extras,
                   keep_pixels=bool(painted))
    if painted:
        kit.ship(ob, shipped, rec)
    tris = mk.tris(ob)
    print("PROPS_TRIS", name, tris)
    kit.for_render(ob)
    bpy.context.scene.view_settings.view_transform = "Standard"
    hk.renders(ob, os.path.join(kit.OUT, "renders"), name, os.path.join(kit.SPRITES, name + ".png"),
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
        print("PROPS_OK", n, t)
    for n, e in failed:
        print("PROPS_FAIL", n, e)


main()
