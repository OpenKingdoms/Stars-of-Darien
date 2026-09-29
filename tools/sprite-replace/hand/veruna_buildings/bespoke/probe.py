"""Lists the parts of a built model whose vertices project inside a sprite
box: blender -b --factory-startup --python probe.py -- Name c0 r0 c1 r1"""
import os
import sys
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.argv = [a for a in sys.argv]
args = sys.argv[sys.argv.index("--") + 1:]
sys.argv = sys.argv[:sys.argv.index("--")] + ["--", args[0], "--norender"]
sys.path.insert(0, HERE)
import kit  # noqa: E402
import handkit as hk  # noqa: E402
import glob  # noqa: E402
import importlib  # noqa: E402
import importlib.util  # noqa: E402
for _p in sorted(glob.glob(os.path.join(HERE, "models_*.py"))):
    importlib.import_module(os.path.basename(_p)[:-3])
for _p in sorted(glob.glob(os.path.join(HERE, "bespoke", "Ver*.py"))):
    _s = importlib.util.spec_from_file_location("bespoke_" + os.path.basename(_p)[:-3], _p)
    _s.loader.exec_module(importlib.util.module_from_spec(_s))
name = args[0]
c0, r0, c1, r1 = (float(v) for v in args[1:5])
hk.reset()
m = kit.Model(name)
kit.MODELS[name](m)
objs = m.done()
for ob in objs:
    hits = []
    for v in ob.data.vertices:
        c, r = m.scr(v.co.x, v.co.y, v.co.z)
        if c0 <= c <= c1 and r0 <= r <= r1:
            hits.append(v.co.z)
    if hits:
        print("PROBE", ob.name, len(hits), "z %.2f..%.2f" % (min(hits), max(hits)))
