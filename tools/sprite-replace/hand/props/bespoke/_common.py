"""Shared finishing for the bespoke props, run inside Blender. Each bespoke
script builds its parts and calls run(); the export, the shipped copy and
the renders are as build.py makes them, so a bespoke model lands where the
table-built one did.
"""
import json
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
PROPS = os.path.dirname(HERE)
for _p in (HERE, PROPS):
    if _p not in sys.path:
        sys.path.insert(0, _p)
import kit  # noqa: E402
from kit import hk, mk  # noqa: E402

CATALOG = r"D:\OKReplace\catalog.json"
_box = hk.box


def _marker_box(*a, **k):
    # the monarch marker casts no shadow, as in build.py
    ob = _box(*a, **k)
    if k.get("name") == "hk_marker":
        ob.visible_shadow = False
    return ob


hk.box = _marker_box


def record(name):
    return next(r for r in json.load(open(CATALOG)) if r["name"] == name)


def run(name, build, render_dir=None, suffix=""):
    """Builds, exports and renders one model; build() returns its parts."""
    rec = record(name)
    hk.reset()
    parts = build()
    mk.tidy(parts)
    mk.whiten(parts)
    parts[0].name = name
    ob = hk.finish(parts, os.path.join(kit.OUT, "models", name + ".glb") if not suffix else
                   os.path.join(kit.OUT, "work", "glb", name + suffix + ".glb"),
                   {"feature": name, "family": "props", "bespoke": True})
    tris = mk.tris(ob)
    print("PROPS_TRIS", name + suffix, tris)
    kit.for_render(ob)
    bpy.context.scene.view_settings.view_transform = "Standard"
    hk.renders(ob, render_dir or os.path.join(kit.OUT, "renders"), name + suffix,
               os.path.join(kit.SPRITES, name + ".png"), tuple(rec["sprite"]["hotspot"]), scale=2)
    return tris


def args():
    """Arguments after '--': an optional render suffix for trial builds,
    which then go to work/ and leave the shipped model alone."""
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return a[0] if a else ""


def main(name, build):
    suf = args()
    run(name, build, os.path.join(kit.OUT, "work") if suf else None, suf)
