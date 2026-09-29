"""Build the Aramon buildings family, or the names given.

    blender -b --factory-startup --python build.py -- [name ...] [--norender]

Each model goes to D:/OKReplace/hand/aramon_buildings/models/<name>.glb,
its classic and turned renders and its projected silhouette to renders/.
"""
import importlib.util
import os
import sys
import time
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402
import models  # noqa: E402


def bespoke(name):
    """The build of a model shaped on its own in bespoke/<name>.py, if any,
    which takes the place of the family builder's."""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bespoke", name + ".py")
    if not os.path.exists(path):
        return None
    spec = importlib.util.spec_from_file_location("bespoke_" + name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.build


argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
render = "--norender" not in argv
names = [a for a in argv if not a.startswith("--")] or list(models.MODELS)
failed = []
for n in names:
    t = time.time()
    try:
        kit.build_one(n, bespoke(n) or models.MODELS[n], render=render)
    except Exception:
        traceback.print_exc()
        failed.append(n)
    print("AB_TIME", n, "%.1f s" % (time.time() - t), flush=True)
print("AB_FAILED", failed, flush=True)
