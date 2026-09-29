"""Builds the walls and joints family: every model, or those named after --.

    blender -b --factory-startup --python build.py [-- VerWall01 AraWall05 ...] [--norender]

Each goes to D:/OKReplace/hand/walls_joints/models/<Name>.glb, with the
classic and turned renders in renders/ and the model's triangles in the
classic projection (renders/<Name>_proj.json) for fit.py.
"""
import glob
import importlib
import json
import os
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import kit  # noqa: E402
import handkit as hk  # noqa: E402
import support  # noqa: E402
import bpy  # noqa: E402

for _p in sorted(glob.glob(os.path.join(HERE, "models_*.py"))):
    importlib.import_module(os.path.basename(_p)[:-3])


def projected(ob, m):
    """The model's triangles as sprite pixels (col, row), for the fit check."""
    me = ob.data
    me.calc_loop_triangles()
    V = [ob.matrix_world @ v.co for v in me.vertices]
    out = []
    for t in me.loop_triangles:
        out.append([list(m.scr(V[i].x, V[i].y, V[i].z)) for i in t.vertices])
    return out


argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
names = [a for a in argv if not a.startswith("--")] or sorted(kit.MODELS)
fails = []
for name in names:
    t0 = time.time()
    try:
        hk.reset()
        m = kit.Model(name)
        kit.MODELS[name](m)
        objs = m.done()
        # nothing may hang in the air: report and remove loose islands
        gone = support.drop(objs)
        if gone:
            print("WJ_LOOSE", name, len(gone), gone[:10], flush=True)
        glb = os.path.join(kit.OUT, "models", name + ".glb")
        ob = hk.finish(objs, glb, {"feature": name})
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        ren = os.path.join(kit.OUT, "renders")
        os.makedirs(ren, exist_ok=True)
        with open(os.path.join(ren, name + "_proj.json"), "w") as f:
            json.dump({"w": m.w, "h": m.h, "tris": projected(ob, m)}, f)
        if "--norender" not in sys.argv:
            # AgX would dull and grey the colours: show the albedo as it is
            bpy.context.scene.view_settings.view_transform = "Standard"
            bpy.context.scene.view_settings.look = "None"
            hk.renders(ob, ren, name, m.png, (m.hx, m.hy), scale=2)
        big = sorted(m.tris.items(), key=lambda kv: -kv[1])[:4]
        print("WJ_OK", name, "tris", tris, big, "%.1fs" % (time.time() - t0), flush=True)
    except Exception:
        traceback.print_exc()
        fails.append(name)
        print("WJ_FAIL", name, flush=True)
print("WJ_DONE", len(names) - len(fails), "built", "failed", fails, flush=True)
