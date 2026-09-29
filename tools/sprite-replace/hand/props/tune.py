"""Tries parameter variants of one prop, run inside Blender:

    blender -b --factory-startup --python tune.py -- <Name> <tag> '<json overrides>' [<tag> '<json>' ...]

Each variant is the models.py entry with the overrides merged in; it is
built and rendered into D:/OKReplace/hand/props/work as <Name>_<tag>, and
nothing in models/ or renders/ is touched.
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

WORK = os.path.join(kit.OUT, "work")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    name, rest = argv[0], argv[1:]
    cat = {r["name"]: r for r in json.load(open(r"D:\OKReplace\catalog.json"))}
    for tag, js in zip(rest[::2], rest[1::2]):
        p = dict(models.MODELS[name], _sprite=os.path.join(kit.SPRITES, name + ".png"),
                 _hotspot=tuple(cat[name]["sprite"]["hotspot"]), _footprint=tuple(cat[name]["footprint"]))
        p.update(json.loads(js))
        p = dict(kit.sturdy(p), _sprite=p["_sprite"], _hotspot=p["_hotspot"], _footprint=p["_footprint"])
        hk.reset()
        parts = getattr(kit, p["kind"])(p)
        mk.tidy(parts)
        mk.whiten(parts)
        ob = hk.finish(parts, os.path.join(WORK, "glb", "%s_%s.glb" % (name, tag)))
        print("TUNE_TRIS", name, tag, mk.tris(ob))
        kit.for_render(ob)
        bpy.context.scene.view_settings.view_transform = "Standard"
        hk.renders(ob, WORK, "%s_%s" % (name, tag), os.path.join(kit.SPRITES, name + ".png"),
                   tuple(cat[name]["sprite"]["hotspot"]), scale=2)


main()
