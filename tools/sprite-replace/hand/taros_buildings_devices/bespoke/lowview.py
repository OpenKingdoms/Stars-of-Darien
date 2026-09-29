"""Low close-up checks of a finished model, for pieces that float.

    blender -b --factory-startup --python bespoke/lowview.py -- <Name> <x> <y> <z> <size> <az> <el> [tag]

Loads models/<Name>.glb and renders the point (x, y, z) of the model's
frame (Z up) from azimuth az and elevation el (degrees; az 0 looks from
-Y, the classic side), size cells across, against a white sky so daylight
under a piece shows. Writes scratch/<Name>_low_<tag>.png.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

OUT = r"D:\OKReplace\hand\taros_buildings_devices"
a = sys.argv[sys.argv.index("--") + 1:]
name = a[0]
x, y, z, size, az, el = (float(v) for v in a[1:7])
tag = a[7] if len(a) > 7 else "%d_%d" % (az, el)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(OUT, "models", name + ".glb"))
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "MATERIAL"
sc.display.shading.show_shadows = True
w = bpy.data.worlds.new("w")
sc.world = w
w.color = (1, 1, 1)
sc.render.resolution_x = sc.render.resolution_y = 512
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
sc.collection.objects.link(cam)
cam.data.type = "ORTHO"
cam.data.ortho_scale = size
# glTF is Y up; the importer turns it back to Blender's Z up, so the frame holds
c = Vector((x, y, z))
A, E = math.radians(az), math.radians(el)
d = Vector((math.sin(A) * math.cos(E), -math.cos(A) * math.cos(E), math.sin(E)))
cam.location = c + d * 60
cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
cam.data.clip_end = 200
sc.camera = cam
sc.render.film_transparent = False
sc.render.filepath = os.path.join(OUT, "scratch", "%s_low_%s.png" % (name, tag))
bpy.ops.render.render(write_still=True)
print("LOWVIEW", sc.render.filepath)
