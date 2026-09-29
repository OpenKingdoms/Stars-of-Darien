"""Builds the Veruna town pieces: every model, or those named after --.

    blender -b --factory-startup --python build.py [-- VerBuild01 VerHut07 ...]

Each ships as D:/OKReplace/hand/veruna_buildings/models/<Name>.glb with no
picture in it: a painted part carries okPaint extras and the game paints it
from the player's own files. A model with painted parts also leaves a copy
with the pixels in review/, for thumbnails only. Renders go to renders/.
"""
import json
import os
import struct
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import kit  # noqa: E402
import handkit as hk  # noqa: E402
import carve  # noqa: E402
import bpy  # noqa: E402
import glob  # noqa: E402
import importlib  # noqa: E402
for _p in sorted(glob.glob(os.path.join(HERE, "models_*.py"))):
    importlib.import_module(os.path.basename(_p)[:-3])
# bespoke builds, one script per model, register last and replace the family's
import importlib.util  # noqa: E402
for _p in sorted(glob.glob(os.path.join(HERE, "bespoke", "Ver*.py"))):
    _spec = importlib.util.spec_from_file_location("bespoke_" + os.path.basename(_p)[:-3], _p)
    _spec.loader.exec_module(importlib.util.module_from_spec(_spec))


def glb_json(path):
    with open(path, "rb") as f:
        data = f.read()
    n = struct.unpack_from("<I", data, 12)[0]
    return json.loads(data[20:20 + n])


def ship(ob, path):
    """Strips the pictures from painted materials and exports the model;
    fails if the file still holds an image."""
    painted = 0
    for mat in ob.data.materials:
        if mat is None or "okPaint" not in mat:
            continue
        painted += 1
        nt = mat.node_tree
        for n in [n for n in nt.nodes if n.type == "TEX_IMAGE"]:
            nt.nodes.remove(n)
        rgb = list(mat.get("okFallback", (0.3, 0.3, 0.3)))
        nt.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1.0)
    if carve.PLAYERS_FILES in ob:
        del ob[carve.PLAYERS_FILES]
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True,
                              export_extras=True)
    j = glb_json(path)
    if j.get("images") or j.get("textures"):
        raise RuntimeError("shipped model still holds a picture: " + path)
    marked = sum(1 for m in j.get("materials", []) if "okPaint" in m.get("extras", {}))
    if marked != painted:
        raise RuntimeError("okPaint lost on export: %d of %d" % (marked, painted))
    return painted


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
        glb = os.path.join(kit.OUT, "models", name + ".glb")
        painted = any(o.data.uv_layers for o in objs)
        first = os.path.join(kit.OUT, "review", name + ".glb") if painted else glb
        ob = hk.finish(objs, first, {"feature": name}, keep_pixels=painted)
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        if "--norender" not in sys.argv:
            # AgX would dull and grey the colours: show the albedo as it is
            bpy.context.scene.view_settings.view_transform = "Standard"
            bpy.context.scene.view_settings.look = "None"
            hk.renders(ob, os.path.join(kit.OUT, "renders"), name, m.png, (m.hx, m.hy), scale=2)
        n_paint = ship(ob, glb) if painted else 0
        big = sorted(m.tris.items(), key=lambda kv: -kv[1])[:5]
        dims = tuple(round(v, 2) for v in ob.dimensions)
        print("VB_OK", name, "tris", tris, "painted", n_paint, "dims", dims, big, "%.1fs" % (time.time() - t0),
              flush=True)
    except Exception:
        traceback.print_exc()
        fails.append(name)
        print("VB_FAIL", name, flush=True)
print("VB_DONE", len(names) - len(fails), "built", "failed", fails, flush=True)
