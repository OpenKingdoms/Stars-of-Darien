"""Builds the Zhon structures family: blender -b --factory-startup --python build.py -- [names]"""
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kit  # noqa: E402
import models  # noqa: E402

hk = kit.hk
OUT = "D:/OKReplace/hand/zhon_structures"
CAT = {r["name"]: r for r in json.load(open("D:/OKReplace/catalog.json"))}

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
# models with a script of their own in bespoke/ are built there (bespoke/build_all.py)
names = argv or [n for n in models.MODELS if not os.path.exists(os.path.join(HERE, "bespoke", n + ".py"))]
no_render = os.environ.get("ZS_NORENDER")
log = []
for name in names:
    t = time.time()
    r = CAT[name]
    p = kit.sturdy(dict(models.MODELS[name], name=name))
    hk.reset()
    b = kit.Build()
    getattr(kit, p["kind"])(b, p)
    objs = b.objects(p.get("smooth", 50))
    tris = kit.tri_count(objs)
    parts = " ".join("%s:%d" % (o.name, kit.tri_count([o])) for o in objs)
    paint = p.get("paint")
    ob = hk.finish(objs, os.path.join(OUT, "models", name + ".glb"),
                   {"replacesFeature": name, "handFamily": "zhon_structures"})
    hx, hy = r["sprite"]["hotspot"]
    rows = [hy - 16 * v.co.y - 8 * v.co.z for v in ob.data.vertices]
    cols = [hx + 16 * v.co.x for v in ob.data.vertices]
    print("ZS_CLASSIC", name, "rows %.0f..%.0f cols %.0f..%.0f of sprite %dx%d" % (
        min(rows), max(rows), min(cols), max(cols), r["sprite"]["w"], r["sprite"]["h"]))
    if not no_render:
        hk.renders(ob, os.path.join(OUT, "renders"), name, "D:/OKReplace/sprites/%s.png" % name,
                   tuple(r["sprite"]["hotspot"]), scale=2)
    print("ZS_PARTS", name, parts)
    log.append((name, tris, round(time.time() - t, 1)))
    print("ZS_BUILT", name, "tris", tris, "%.1fs" % (time.time() - t))
for row in log:
    print("ZS_SUMMARY", *row)
