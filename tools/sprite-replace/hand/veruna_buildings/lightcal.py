"""Measures how the classic render lights a plain colour on each face
direction: blender -b --factory-startup --python lightcal.py"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bpy  # noqa: E402
import kit  # noqa: E402
import handkit as hk  # noqa: E402

hk.reset()
m = kit.Model("VerHut03")
m.col("g", (128, 128, 128))
# a flat top, a south face, an east face, a west face, a 30 degree roof
m.box("g", -2, 2, -2, 2, 0, 0.05)
ob_parts = []
m.done  # noqa
objs = m.done()
bpy.context.scene.view_settings.view_transform = "Standard"
bpy.context.scene.view_settings.look = "None"
ob = hk.finish(objs, os.path.join(kit.OUT, "work", "cal", "cal.glb"), {})
hk.renders(ob, os.path.join(kit.OUT, "work", "cal"), "top", None, None, scale=2)
