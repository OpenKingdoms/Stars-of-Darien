"""Builds every bespoke prop (or the ones named) in one Blender run:

    blender -b --factory-startup --python run.py [-- Name ...]

Each goes where its own script sends it: models/<Name>.glb and renders/.
"""
import importlib
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import _common  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
names = argv or sorted(f[:-3] for f in os.listdir(HERE) if f[:3] in ("Ara", "Ver", "Zon") and f.endswith(".py"))
for n in names:
    mod = importlib.import_module(n)
    print("PROPS_OK", n, _common.run(mod.NAME, mod.build))
