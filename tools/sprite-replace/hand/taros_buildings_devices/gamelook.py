"""The classic view lit like the game (its sun, sky ambient and tonemapper,
from batch.py), beside the sprite, for judging colour. Scratch output only.

    blender -b --factory-startup --python gamelook.py -- names...
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit as K  # noqa: E402
from build import MODELS  # noqa: E402
import batch  # noqa: E402

SCR = os.path.join(K.OUT, "scratch")


def look(n):
    K.reset()
    parts = MODELS[n]()
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.render.film_transparent = True
    batch.game_light(scene)
    K.standard_view()
    r = K.CATALOG[n]
    hx, hy = r["sprite"]["hotspot"]
    scale = 2
    img = bpy.data.images.load(os.path.join(K.SPRITES, n + ".png"))
    W, H = img.size[0] * scale, img.size[1] * scale
    P = 16 * scale
    e = math.atan(1.0 / K.hk.TILT)
    d = Vector((0.0, math.cos(e), -math.sin(e)))
    up = Vector((0.0, math.sin(e), math.cos(e)))
    C = -Vector((1, 0, 0)) * ((hx * scale - W / 2.0) / P) - up * ((H / 2.0 - hy * scale) / P)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = max(W, H) / P
    cam.location = C - d * 60
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = int(W), int(H)
    st = scene.render.image_settings
    st.file_format, st.color_depth = "OPEN_EXR", "32"
    exr = os.path.join(SCR, n + "_game.exr")
    scene.render.filepath = exr
    bpy.ops.render.render(write_still=True)
    batch.game_png(scene, exr, os.path.join(SCR, n + "_game.png"))
    print("GAMELOOK", n)


for n in sys.argv[sys.argv.index("--") + 1:]:
    look(n)
