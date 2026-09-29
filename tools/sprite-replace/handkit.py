"""Helpers for hand-built replacement models, run inside Blender.

Same frame as carve.py: one Blender unit is one map cell (16 px, 16 3DO
units), -Y points toward the classic camera, Z is up, and the origin is
the object's anchor on the ground. A monarch stands 4 cells tall.

    import handkit as hk
    hk.reset()
    base = hk.cylinder(1.6, 0.5, seg=6, mat=hk.pbr("stone", (0.6, 0.58, 0.52)))
    ...
    ob = hk.finish([base, ...], "out/ARALODE.glb", {"replacesTexture": "araplainlode"})
    hk.renders(ob, "out", "ARALODE", "sprites/ARALODE.png", (27, 39))

finish() exports the model as it ships, geometry only (okpaint.py): a part
painted from the original picture names it in okPaint and loses the pixels,
and any other texture must come from generated(), made from noise and
numbers, or the export fails. keep_pixels=True, or OK_KEEP_PIXELS=1, keeps
the pixels for review renders on this machine only.

renders() writes <name>_classic.png, the model seen by the classic camera
at the picture's own scale with the anchors aligned, and <name>_turned.png,
a low three-quarter view beside a 4-cell monarch marker. Then, outside
Blender, `python handcompare.py <out dir> <name> <picture> [scale]` puts the
picture, the classic render and the two overlaid side by side in
<name>_compare.png, so size and silhouette can be checked by eye.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import carve  # noqa: E402
import okpaint  # noqa: E402
from okpaint import generated  # noqa: E402,F401

CELL = carve.CELL
TILT = carve.TILT
MONARCH = 4.0  # cells, from araking.3do


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def pbr(name, rgb, rough=0.85, metal=0.0, emit=None, strength=0.0):
    """A plain lit material; emit is an (r, g, b) glow for crystals and fire."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit is not None:
        b.inputs["Emission Color"].default_value = (*emit, 1.0)
        b.inputs["Emission Strength"].default_value = strength
    return m


def _object(name, bm, mat=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    if mat is not None:
        me.materials.append(mat)
    return ob


def cylinder(r_bottom, height, r_top=None, seg=16, z=0.0, x=0.0, y=0.0, mat=None, name="cyl"):
    """A prism or cone standing on z: seg=6 gives a hexagon, r_top=0 a spike."""
    bm = bmesh.new()
    r_top = r_bottom if r_top is None else r_top
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r_bottom, radius2=max(r_top, 1e-4),
                          depth=height)
    for v in bm.verts:
        v.co.z += z + height / 2
        v.co.x += x
        v.co.y += y
    return _object(name, bm, mat)


def box(sx, sy, sz, x=0.0, y=0.0, z=0.0, mat=None, name="box"):
    """A box sitting on z, centred on x, y."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x + v.co.x * sx
        v.co.y = y + v.co.y * sy
        v.co.z = z + (v.co.z + 0.5) * sz
    return _object(name, bm, mat)


def bevel(ob, width=0.05, segments=2):
    """Softens hard edges so stone and metal catch the light."""
    mod = ob.modifiers.new("bevel", "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    _apply(ob)
    return ob


def smooth(ob, angle=40):
    for p in ob.data.polygons:
        p.use_smooth = True
    try:
        ob.data.set_sharp_from_angle(angle=math.radians(angle))
    except AttributeError:
        pass
    return ob


def _apply(ob):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.convert(target="MESH")


def project_paint(ob, sprite_png, hotspot, footprint=(2, 2)):
    """Paints the original picture onto the part the way the classic camera
    saw it (carve.paint), for decorated faces; give the part its own object."""
    spr = carve.Sprite(sprite_png)
    lo = [min(v.co[i] for v in ob.data.vertices) for i in range(2)]
    hi = [max(v.co[i] for v in ob.data.vertices) for i in range(2)]
    ob["box"] = (lo[0], hi[0], lo[1], hi[1])
    r = {"name": ob.name, "sprite": {"w": spr.w, "h": spr.h, "hotspot": list(hotspot)},
         "footprint": list(footprint), "height": 0}
    ob.data.materials.clear()
    carve.paint(ob, r, spr)
    return ob


def finish(parts, glb_path, extras=None, keep_pixels=None):
    """Joins the parts into one object anchored at the origin and exports it
    as it ships (okpaint.export), or with its pictures for review."""
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    # any part painted from the player's sprites marks the whole model
    stamped = any(p.get(carve.PLAYERS_FILES) for p in parts)
    if len(parts) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    if stamped:
        ob[carve.PLAYERS_FILES] = True
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for k, v in (extras or {}).items():
        ob[k] = v
    okpaint.export(ob, glb_path, keep_pixels)
    print("HANDKIT_EXPORT", glb_path, "size %.2f x %.2f x %.2f cells" % tuple(ob.dimensions))
    return ob


def _stage(samples=32):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = samples
    scene.render.film_transparent = True
    if "hk_sun" not in bpy.data.objects:
        sun = bpy.data.objects.new("hk_sun", bpy.data.lights.new("hk_sun", "SUN"))
        sun.data.energy = 3.5
        sun.rotation_euler = (math.radians(40), 0, math.radians(30))
        scene.collection.objects.link(sun)
        w = bpy.data.worlds.new("hk_world")
        w.use_nodes = True
        w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.6
        scene.world = w
    cam = bpy.data.objects.get("hk_cam")
    if cam is None:
        cam = bpy.data.objects.new("hk_cam", bpy.data.cameras.new("hk_cam"))
        scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    scene.camera = cam
    return scene, cam


def renders(ob, out_dir, name, sprite_png=None, hotspot=None, scale=2):
    """The compare sheet (classic view at the picture's own scale, anchors
    aligned) and the turned view beside a monarch-height marker."""
    os.makedirs(out_dir, exist_ok=True)
    scene, cam = _stage()
    P = CELL * scale  # pixels per cell in the render
    e = math.atan(1.0 / TILT)
    d = Vector((0.0, math.cos(e), -math.sin(e)))
    up = Vector((0.0, math.sin(e), math.cos(e)))
    right = Vector((1.0, 0.0, 0.0))
    if sprite_png:
        img = bpy.data.images.load(sprite_png)
        W, H = img.size[0] * scale, img.size[1] * scale
        hx, hy = hotspot
        # the anchor must land where the picture's hotspot is
        dx = hx * scale - W / 2.0
        dy_up = H / 2.0 - hy * scale
        C = -right * (dx / P) - up * (dy_up / P)
    else:
        W = H = 512
        C = Vector((0, 0, 1.0))
    scene.render.resolution_x, scene.render.resolution_y = int(W), int(H)
    cam.data.ortho_scale = max(W, H) / P
    cam.location = C - d * 60
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    classic = os.path.join(out_dir, name + "_classic.png")
    scene.render.filepath = classic
    bpy.ops.render.render(write_still=True)

    # turned: a low three-quarter view with a monarch marker for scale
    lo = Vector([min((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
    hi = Vector([max((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
    marker = box(0.5, 0.5, MONARCH, x=hi.x + 1.0, y=0.0, mat=pbr("hk_marker", (0.25, 0.3, 0.8)), name="hk_marker")
    az, el = -math.pi / 2 + 0.9, math.radians(30)
    centre = Vector(((lo.x + hi.x + 1.5) / 2, (lo.y + hi.y) / 2, max(hi.z, MONARCH) / 2))
    dt = Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)))
    scene.render.resolution_x = scene.render.resolution_y = 512
    cam.data.ortho_scale = max(hi.x - lo.x + 3.0, hi.y - lo.y + 2.0, max(hi.z, MONARCH) + 1.0) * 1.15
    cam.location = centre + dt * 60
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    turned = os.path.join(out_dir, name + "_turned.png")
    scene.render.filepath = turned
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(marker, do_unlink=True)

    print("HANDKIT_RENDERS", classic, turned)
