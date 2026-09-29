"""Builds every bespoke mound in one Blender run:
    blender -b --factory-startup --python build_all.py [-- ZonTmound01 ...]"""
import importlib
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _common as C  # noqa: E402

NAMES = ["ZonTmound0%d" % i for i in range(1, 7)]
names = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else NAMES
for n in names:
    C.run(n, importlib.import_module(n).build)
