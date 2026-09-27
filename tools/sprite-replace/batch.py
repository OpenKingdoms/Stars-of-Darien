"""Carve many sprite features in one Blender session, most used first.

    blender -b --factory-startup -P tools/sprite-replace/batch.py -- <catalog dir> <out dir> [first] [count]

For each feature in catalog.json (sorted by how many maps use it) this
writes <out dir>/models/<feature>.glb and two quick thumbnails,
<out dir>/thumbs/<feature>_classic.png and _turned.png, rendered with the
fast workbench engine for contact sheets. Features already carved are
skipped, so a batch can be stopped and resumed.

The models are painted with the player's own sprites, so they stay on
the player's machine: copy them into unity/Assets/Overrides/Generated,
which git ignores.
"""
import json
import math
import os
import sys
import time

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import arch  # noqa: E402
import frond  # noqa: E402
import carve  # noqa: E402


SHAPES = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "shapes.json")))


def shape_of(name):
    """The hand classification in shapes.json, if the feature has one."""
    import fnmatch
    for shape, names in SHAPES.items():
        if shape.startswith("_"):
            continue
        for pat in names:
            if fnmatch.fnmatchcase(name, pat) or fnmatchcase_ci(name, pat):
                return shape
    return None


def fnmatchcase_ci(name, pat):
    import fnmatch
    return fnmatch.fnmatchcase(name.lower(), pat.lower())


def thumbs(ob, out, name):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 256
    scene.render.resolution_y = 256
    scene.render.film_transparent = True
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    lo = Vector([min(v.co[i] for v in ob.data.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for v in ob.data.vertices) for i in range(3)])
    centre = (lo + hi) / 2
    span = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z)
    for label, az, el in (("classic", -math.pi / 2, math.atan(1.0 / carve.TILT)),
                          ("turned", -math.pi / 2 + 0.9, math.radians(30))):
        cam.location = centre + Vector((math.cos(az) * math.cos(el) * 50, math.sin(az) * math.cos(el) * 50,
                                        math.sin(el) * 50))
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.ortho_scale = span * 1.2
        scene.render.filepath = os.path.join(out, "thumbs", "%s_%s.png" % (name, label))
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    catalog, out = argv[0], argv[1]
    first = int(argv[2]) if len(argv) > 2 else 0
    count = int(argv[3]) if len(argv) > 3 else 10 ** 6
    os.makedirs(os.path.join(out, "models"), exist_ok=True)
    os.makedirs(os.path.join(out, "thumbs"), exist_ok=True)
    rows = [r for r in json.load(open(os.path.join(catalog, "catalog.json"))) if r.get("sprite")]
    names = os.environ.get("ONLY_NAMES")
    if names:
        keep = set(names.split(","))
        rows = [r for r in rows if r["name"] in keep]
    if os.environ.get("ONLY_SHAPED"):
        rows = [r for r in rows if shape_of(r["name"])]
    if os.environ.get("ONLY_FROND"):
        rows = [r for r in rows if not arch.kind_of(r) and (frond.is_decal(r) or frond.is_frond(
            r, carve.Sprite(os.path.join(catalog, "sprites", r["name"] + ".png"))))]
    only = os.environ.get("ONLY_ARCH")
    if only:
        rows = [r for r in rows if arch.kind_of(r)]
    done = failed = 0
    t0 = time.time()
    for r in rows[first:first + count]:
        name = r["name"]
        glb = os.path.join(out, "models", name + ".glb")
        if os.path.exists(glb):
            continue
        try:
            bpy.ops.wm.read_factory_settings(use_empty=True)
            spr = carve.Sprite(os.path.join(catalog, "sprites", name + ".png"))
            shape = shape_of(name) or r.get("shape")
            if shape:
                r = dict(r, shape=shape)
            kind = shape if shape in arch.HAND_KINDS else (None if shape else arch.kind_of(r))
            decal = shape == "decal" or (not shape and not kind and frond.is_decal(r))
            if shape in ("palm", "fern"):
                fr = True
            elif shape in ("crown", "poplar", "bush"):
                fr = False
            else:
                fr = not kind and (decal or frond.is_frond(r, spr))
            if decal:
                ob = frond.build_decal(r, spr)
            elif kind:
                ob = arch.build(r, kind, spr)
            elif fr:
                ob = frond.build(r, spr)
            else:
                ob = carve.carve(r, spr)[0]
            carve.paint(ob, r, spr)
            if fr:
                frond.cut_out(ob)
            thumbs(ob, out, name)
            bpy.ops.object.select_all(action="DESELECT")
            ob.select_set(True)
            bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True, export_yup=True, export_extras=True)
            done += 1
            print("BATCH_OK", name, "tris", sum(len(p.vertices) - 2 for p in ob.data.polygons), flush=True)
        except Exception as e:  # keep going, report at the end
            failed += 1
            print("BATCH_FAIL", name, repr(e), flush=True)
    print("BATCH_DONE carved %d failed %d in %.0f s" % (done, failed, time.time() - t0), flush=True)


main()
