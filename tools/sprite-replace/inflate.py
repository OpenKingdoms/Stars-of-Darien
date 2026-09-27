"""Turn one sprite-only feature into a 3D model that keeps the original's
painting: its silhouette is inflated into a rounded solid, and every
vertex takes the colour of exactly the sprite pixel the original game
would have drawn there.

    blender -b --factory-startup -P tools/sprite-replace/inflate.py -- <catalog dir> <feature> <out dir>

The original draws a feature as a sprite in an oblique view: a point at
cell (x, z) and height h lands on screen at x * 16 and z * 16 - h * 16 * tilt,
with tilt 0.5, measured from the sprite's hotspot. Here one Blender unit is
one map cell, with x to the east, -Y toward the viewer (south) and Z up.
Sprite rows above the hotspot become height, and rows below it become
ground reaching toward the viewer. Thickness grows with distance from the
silhouette's edge, so wide parts are deep and thin parts stay thin.

Writes <out dir>/<feature>.glb (origin at the footprint centre on the
ground, the texture embedded) and review renders: the original sprite
beside the model seen from the classic view and from a turned view.
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

TILT = 0.5
CELL = 16.0
STEP = 2  # sprite pixels per mesh vertex


def load(catalog_dir, name):
    for r in json.load(open(os.path.join(catalog_dir, "catalog.json"))):
        if r["name"] == name:
            return r
    raise SystemExit("no feature " + name)


def mask_and_depth(img):
    """Alpha mask on the STEP grid, and each cell's distance to the edge."""
    w, h = img.size
    px = img.pixels[:]
    gw, gh = w // STEP + 1, h // STEP + 1

    def inside(gx, gy):
        x, y = min(gx * STEP, w - 1), min(gy * STEP, h - 1)
        # Blender images store rows bottom up.
        return px[((h - 1 - y) * w + x) * 4 + 3] > 0.5
    m = [[inside(gx, gy) for gx in range(gw)] for gy in range(gh)]
    big = 10 ** 6
    d = [[0 if not m[y][x] else big for x in range(gw)] for y in range(gh)]
    for y in range(gh):
        for x in range(gw):
            if d[y][x]:
                up = d[y - 1][x] if y else 0
                left = d[y][x - 1] if x else 0
                d[y][x] = min(d[y][x], up + 1, left + 1)
    for y in range(gh - 1, -1, -1):
        for x in range(gw - 1, -1, -1):
            if d[y][x]:
                dn = d[y + 1][x] if y + 1 < gh else 0
                rt = d[y][x + 1] if x + 1 < gw else 0
                d[y][x] = min(d[y][x], dn + 1, rt + 1)
    return m, d, gw, gh


def build(r, sprite_path):
    img = bpy.data.images.load(sprite_path)
    w, h = img.size
    hx, hy = r["sprite"]["hotspot"]
    m, dist, gw, gh = mask_and_depth(img)
    # Depth follows the silhouette: each row's run of pixels becomes a
    # round cross-section as deep as it is wide, so a trunk is a trunk
    # and a canopy is a ball, not a slab.
    ROUND = 0.85
    thick = [[0.0] * gw for _ in range(gh)]
    for gy in range(gh):
        gx = 0
        while gx < gw:
            if not m[gy][gx]:
                gx += 1
                continue
            start = gx
            while gx < gw and m[gy][gx]:
                gx += 1
            end = gx - 1
            cx, rad = (start + end) / 2.0, (end - start) / 2.0 + 0.5
            for x in range(start, end + 1):
                thick[gy][x] = ROUND * math.sqrt(max(0.0, rad * rad - (x - cx) ** 2)) * STEP / CELL

    def thickness(gx, gy):
        return thick[gy][gx]

    def position(gx, gy, sign):
        """World point for grid (gx, gy) on the front (sign -1) or back (+1)."""
        sx, sy = gx * STEP, gy * STEP
        x = (sx - hx) / CELL
        t = thickness(gx, gy) * sign
        if sy <= hy:
            # above the hotspot: height, standing at the thickness offset
            y = t
            # screen row = hy + (-y) * CELL - z * CELL * TILT, solved for z,
            # so a point nearer the viewer sits higher to hit the same pixel
            z = (hy - sy + (-y) * CELL) / (CELL * TILT)
        else:
            # below the hotspot: ground reaching toward the viewer
            y = -(sy - hy) / CELL + t * 0.3
            z = 0.0
        return Vector((x, y, max(0.0, z)))

    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    verts = {}

    def v(gx, gy, sign):
        key = (gx, gy, sign)
        if key not in verts:
            verts[key] = bm.verts.new(position(gx, gy, sign))
        return verts[key]

    def uv(gx, gy):
        return (min(gx * STEP, w - 1) / w, 1.0 - min(gy * STEP, h - 1) / h)

    def quad(keys, sign):
        vs = [v(gx, gy, sign) for gx, gy in keys]
        if sign > 0:
            vs.reverse()
            keys = list(reversed(keys))
        try:
            f = bm.faces.new(vs)
        except ValueError:
            return
        for loop, (gx, gy) in zip(f.loops, keys):
            loop[uv_layer].uv = uv(gx, gy)

    for gy in range(gh - 1):
        for gx in range(gw - 1):
            c = [(gx, gy), (gx + 1, gy), (gx + 1, gy + 1), (gx, gy + 1)]
            if all(m[yy][xx] for xx, yy in c):
                quad(c, -1)
                quad(c, 1)
    # Close the rim: join front and back along the silhouette's edges.
    for gy in range(gh - 1):
        for gx in range(gw - 1):
            for (a, b), (nx, ny) in (
                    (((gx, gy), (gx + 1, gy)), (0, -1)),
                    (((gx + 1, gy + 1), (gx, gy + 1)), (0, 1)),
                    (((gx, gy + 1), (gx, gy)), (-1, 0)),
                    (((gx + 1, gy), (gx + 1, gy + 1)), (1, 0))):
                cell = [(gx, gy), (gx + 1, gy), (gx + 1, gy + 1), (gx, gy + 1)]
                if not all(m[yy][xx] for xx, yy in cell):
                    continue
                ox, oy = gx + nx, gy + ny
                ncell = [(ox, oy), (ox + 1, oy), (ox + 1, oy + 1), (ox, oy + 1)]
                outside = not (0 <= ox < gw - 1 and 0 <= oy < gh - 1) or \
                    not all(m[yy][xx] for xx, yy in ncell)
                if not outside:
                    continue
                try:
                    f = bm.faces.new([v(*a, -1), v(*b, -1), v(*b, 1), v(*a, 1)])
                except ValueError:
                    continue
                for loop, key in zip(f.loops, [a, b, b, a]):
                    loop[uv_layer].uv = uv(*key)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(r["name"])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(r["name"], me)
    bpy.context.collection.objects.link(ob)
    for p in me.polygons:
        p.use_smooth = True

    mat = bpy.data.materials.new(r["name"])
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 1.0
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    # The original's light is painted in: let the painting show through.
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 0.55
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    mat.blend_method = "CLIP" if hasattr(mat, "blend_method") else None
    me.materials.append(mat)
    img.pack()
    # Centre the footprint on the origin: the hotspot is the feature's
    # anchor, and the footprint spreads from it.
    return ob


def render(ob, out, name, sprite_path):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.film_transparent = True
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 2.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    scene.collection.objects.link(sun)
    w = bpy.data.worlds.new("w")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.2
    scene.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    lo = Vector([min(v.co[i] for v in ob.data.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for v in ob.data.vertices) for i in range(3)])
    centre = (lo + hi) / 2
    cam.data.ortho_scale = max(hi.x - lo.x, (hi.z - lo.z) * 0.5 + (hi.y - lo.y)) * 1.15
    # The classic view: height shows at half the scale of ground depth,
    # which an orthographic camera gets by looking down at atan(2).
    classic = math.atan(1.0 / TILT)
    for label, az, el in (("classic", -math.pi / 2, classic), ("turned", -math.pi / 2 + 0.9, math.radians(35))):
        d = 50.0
        cam.location = centre + Vector((math.cos(az) * math.cos(el) * d, math.sin(az) * math.cos(el) * d,
                                        math.sin(el) * d))
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, "%s_%s.png" % (name, label))
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    catalog, name, out = argv
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    r = load(catalog, name)
    sprite = os.path.join(catalog, "sprites", name + ".png")
    ob = build(r, sprite)
    # painted from the player's own sprite, so the studio keeps it out of git
    ob["okFromPlayersFiles"] = True
    render(ob, out, name, sprite)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(out, name + ".glb"), export_format="GLB",
                              use_selection=True, export_yup=True, export_extras=True)
    print("INFLATED", name, "verts", len(ob.data.vertices), "faces", len(ob.data.polygons),
          "size %.2f x %.2f x %.2f cells" % tuple(ob.dimensions))


main()
