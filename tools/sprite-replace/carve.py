"""Turn one sprite-only feature into a 3D model with the right proportions
from every side, painted with the original sprite.

    blender -b --factory-startup -P tools/sprite-replace/carve.py -- <catalog dir> <feature> <out dir>

The original draws a feature in an oblique view: a point x cells east,
y cells north and z cells up from the feature's anchor lands on the
sprite at column hx + 16 x and row hy + 16 (-y) - 8 z (tilt 0.5). The
sprite alone cannot say how deep or tall an object is, but the feature's
definition can: its footprint and its height. So the model is carved from
the box those give, keeping every part that the original would have drawn
inside the silhouette. Plants take a round cross-section at each height,
as wide as the sprite is there. The surface is smoothed, and every point
takes the colour of the sprite pixel the original drew at that spot.

One Blender unit is one map cell. -Y is toward the viewer (south), Z up,
and the origin is the feature's anchor on the ground.
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
RES = 8  # voxels per cell
ROUND_WORDS = ("tree", "bush", "plant", "shrub", "groundcover", "grass", "corn", "crops", "mushroom")


def load(catalog_dir, name):
    for r in json.load(open(os.path.join(catalog_dir, "catalog.json"))):
        if r["name"] == name:
            return r
    raise SystemExit("no feature " + name)


class Sprite:
    def __init__(self, path):
        self.img = bpy.data.images.load(path)
        self.w, self.h = self.img.size
        px = self.img.pixels[:]
        w, h = self.w, self.h
        # alpha as rows top-down, for lookups by sprite row
        self.alpha = [[px[((h - 1 - y) * w + x) * 4 + 3] > 0.5 for x in range(w)] for y in range(h)]
        self.bleed(px)

    def bleed(self, px, passes=12):
        """Spread edge colours into the clear pixels, alpha untouched, so
        filtering and faces just past the silhouette never pick up black."""
        import numpy as np
        a = np.array(px, dtype=np.float32).reshape(self.h, self.w, 4)
        rgb, known = a[..., :3].copy(), a[..., 3] > 0.5
        for _ in range(passes):
            if known.all():
                break
            acc = np.zeros_like(rgb)
            cnt = np.zeros(known.shape, np.float32)
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                k = np.roll(known, (dy, dx), (0, 1))
                acc += np.roll(rgb, (dy, dx), (0, 1)) * k[..., None]
                cnt += k
            grow = ~known & (cnt > 0)
            rgb[grow] = acc[grow] / cnt[grow][:, None]
            known = known | grow
        a[..., :3] = rgb
        self.img.pixels[:] = a.ravel()

    def edge_distance(self):
        """Pixels from each opaque pixel to the silhouette's edge (a two
        pass chamfer), so wide parts can be deep and thin parts thin."""
        w, h, a = self.w, self.h, self.alpha
        big = 10 ** 6
        d = [[big if a[y][x] else 0 for x in range(w)] for y in range(h)]
        for y in range(h):
            for x in range(w):
                if d[y][x]:
                    d[y][x] = min(d[y][x], (d[y - 1][x] + 1) if y else 1, (d[y][x - 1] + 1) if x else 1)
        for y in range(h - 1, -1, -1):
            for x in range(w - 1, -1, -1):
                if d[y][x]:
                    d[y][x] = min(d[y][x], (d[y + 1][x] + 1) if y + 1 < h else 1,
                                  (d[y][x + 1] + 1) if x + 1 < w else 1)
        self.dist = d

    def depth_at(self, sx, sy):
        xi, yi = int(math.floor(sx)), int(math.floor(sy))
        if 0 <= xi < self.w and 0 <= yi < self.h:
            return self.dist[yi][xi]
        return 0

    def inside(self, sx, sy):
        xi, yi = int(math.floor(sx)), int(math.floor(sy))
        return 0 <= xi < self.w and 0 <= yi < self.h and self.alpha[yi][xi]

    def row_span(self, sy):
        yi = int(math.floor(sy))
        if not 0 <= yi < self.h:
            return None
        xs = [x for x in range(self.w) if self.alpha[yi][x]]
        return (min(xs), max(xs)) if xs else None


def screen(hx, hy, x, y, z):
    return hx + x * CELL, hy + (-y) * CELL - z * CELL * TILT


def carve(r, spr):
    hx, hy = r["sprite"]["hotspot"]
    fx, fz = r["footprint"]
    words = (r["description"] + " " + r["category"]).lower()
    rounded = any(wd in words for wd in ROUND_WORDS)
    # A living tree gets a trunk and a crown: the sprite sees it from above,
    # so carving alone would fill the whole height with canopy. A dead tree
    # keeps thin branches, carved like any other object.
    tree = "tree" in words and "dead" not in words
    shape = r.get("shape")
    if shape in ("crown", "poplar", "bush"):
        tree, rounded = True, True
    if "dead" in words:
        rounded = False
    # The box: east-west from the sprite itself (things overhang their
    # footprint), north-south from the footprint, or as deep as wide for
    # plants, and up to the feature's height.
    x0, x1 = -hx / CELL, (spr.w - hx) / CELL
    if rounded:
        half = (x1 - x0) / 2
        y0, y1 = -half, half
    else:
        y0, y1 = -fz / 2 - 0.25, fz / 2 + 0.25
    H = r["height"] / CELL if r["height"] else (hy / (CELL * TILT))
    # Never taller than the sprite can show, even from the back edge.
    # Never taller than the sprite can show. A point at depth y and height
    # z lands on row hy - 16 y - 8 z, so the tallest reachable point is at
    # the front of the box, where -y is largest.
    H = min(H, (hy - y0 * CELL) / (CELL * TILT))
    nx = max(1, int(math.ceil((x1 - x0) * RES)))
    ny = max(1, int(math.ceil((y1 - y0) * RES)))
    nz = max(1, int(math.ceil(H * RES)))

    def centre(i, j, k):
        return x0 + (i + 0.5) / RES, y0 + (j + 0.5) / RES, (k + 0.5) / RES

    solid = bytearray(nx * ny * nz)
    ymid = (y0 + y1) / 2
    if tree:
        wide = (x1 - x0) > 0.6 * H
        trunk_top = H * (0.32 if wide else 0.14)
        base = spr.row_span(hy - 2) or spr.row_span(hy - 6) or (hx - 2, hx + 2)
        trunk_x = ((base[0] + base[1]) / 2 - hx) / CELL
        trunk_r = min(0.45, max(0.14, (base[1] - base[0]) / 2 / CELL))
        crown_r = (x1 - x0) / 2
        crown_c = (trunk_top * 0.7 + H) / 2
        crown_h = (H - trunk_top * 0.7) / 2
        if shape == "crown":
            # a dome about as tall as it is wide, sitting on the trunk
            crown_h = min(crown_h, crown_r * 0.9)
            crown_c = H - crown_h
            trunk_top = max(0.3, crown_c - crown_h * 0.5)
            # a trunk that can carry the crown, not a stick
            trunk_r = min(0.6, max(trunk_r, crown_r * 0.13))
        elif shape == "bush":
            # a low dome from the ground, no trunk
            crown_h = min(H * 0.5, crown_r * 0.7)
            crown_c = crown_h * 0.9
            trunk_top = 0.0
    if not rounded:
        spr.edge_distance()

    def idx(i, j, k):
        return (k * ny + j) * nx + i

    for k in range(nz):
        for j in range(ny):
            for i in range(nx):
                x, y, z = centre(i, j, k)
                sx, sy = screen(hx, hy, x, y, z)
                if tree and z < trunk_top:
                    # the trunk, hidden behind the crown in the sprite
                    if (x - trunk_x) ** 2 + (y - ymid) ** 2 <= trunk_r * trunk_r:
                        solid[idx(i, j, k)] = 1
                    continue
                if not spr.inside(sx, sy):
                    continue
                if tree:
                    # the crown: a rounded mass, as wide as the sprite
                    f = 1.0 - ((z - crown_c) / crown_h) ** 2
                    if f <= 0 or (x - (x0 + x1) / 2) ** 2 + (y - ymid) ** 2 > crown_r * crown_r * f:
                        continue
                elif rounded:
                    # the width of the silhouette at this height, seen at the
                    # middle of the object, sets the radius of this slice
                    span = spr.row_span(screen(hx, hy, 0, (y0 + y1) / 2, z)[1])
                    if not span:
                        continue
                    cxs = ((span[0] + span[1]) / 2 - hx) / CELL
                    rad = (span[1] - span[0]) / 2 / CELL + 0.5 / RES
                    cy = (y0 + y1) / 2
                    if (x - cxs) ** 2 + (y - cy) ** 2 > rad * rad:
                        continue
                else:
                    # as deep as the painting is wide here, within the footprint
                    half = min((y1 - y0) / 2, max(0.25, spr.depth_at(sx, sy) * 1.2 / CELL))
                    if abs(y - ymid) > half:
                        continue
                solid[idx(i, j, k)] = 1

    # boundary faces between solid and empty voxels
    bm = bmesh.new()
    verts = {}

    def v(i, j, k):
        key = (i, j, k)
        if key not in verts:
            verts[key] = bm.verts.new((x0 + i / RES, y0 + j / RES, k / RES))
        return verts[key]

    def filled(i, j, k):
        return 0 <= i < nx and 0 <= j < ny and 0 <= k < nz and solid[idx(i, j, k)]

    faces = 0
    for k in range(nz):
        for j in range(ny):
            for i in range(nx):
                if not solid[idx(i, j, k)]:
                    continue
                for (di, dj, dk), quad in (
                        ((1, 0, 0), [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)]),
                        ((-1, 0, 0), [(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)]),
                        ((0, 1, 0), [(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)]),
                        ((0, -1, 0), [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)]),
                        ((0, 0, 1), [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)]),
                        ((0, 0, -1), [(0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)])):
                    if filled(i + di, j + dj, k + dk):
                        continue
                    try:
                        bm.faces.new([v(i + a, j + b, k + c) for a, b, c in quad])
                        faces += 1
                    except ValueError:
                        pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(r["name"])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(r["name"], me)
    bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    # smooth the blocks into a surface, then bring the count down
    rem = ob.modifiers.new("remesh", "REMESH")
    rem.mode = "VOXEL"
    rem.voxel_size = 1.0 / RES
    sm = ob.modifiers.new("smooth", "CORRECTIVE_SMOOTH")
    sm.iterations = 6
    sm.use_only_smooth = True
    dec = ob.modifiers.new("decimate", "DECIMATE")
    dec.ratio = 0.35
    bpy.ops.object.convert(target="MESH")
    for p in ob.data.polygons:
        p.use_smooth = True
    ob["box"] = (x0, x1, y0, y1)
    return ob, (nx * ny * nz, faces)


def paint(ob, r, spr):
    """Every corner takes the sprite pixel the original drew at that spot."""
    hx, hy = r["sprite"]["hotspot"]
    me = ob.data
    uv = me.uv_layers.new(name="UVMap")
    x0, x1, y0, y1 = ob["box"]
    xc, yc = (x0 + x1) / 2, (y0 + y1) / 2
    stretch = (x1 - x0) / max(1e-3, (y1 - y0))
    for poly in me.polygons:
        n = poly.normal
        for li in poly.loop_indices:
            x, y, z = me.vertices[me.loops[li].vertex_index].co
            if n.z < -0.3:
                # undersides: the painting turned under, as for the sides
                px, py, pz = xc + (y - yc) * stretch, yc, z
            elif n.y <= -0.3 or n.z > 0.6:
                # faces the classic camera saw: the sprite exactly
                px, py, pz = x, y, z
            elif abs(n.y) >= abs(n.x):
                # the back: the front painting, mirrored front to back
                px, py, pz = x, 2 * yc - y, z
            else:
                # east and west sides: the painting turned onto the side
                side = 1.0 if n.x > 0 else -1.0
                px, py, pz = xc + side * (y - yc) * stretch, yc, z
            sx, sy = screen(hx, hy, px, py, pz)
            uv.data[li].uv = (sx / spr.w, 1.0 - sy / spr.h)
    mat = bpy.data.materials.new(r["name"])
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 1.0
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = spr.img
    tex.interpolation = "Linear"
    tex.extension = "EXTEND"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    # Plain lit colour: in the game the sun and shadows light it like
    # everything else.
    me.materials.append(mat)
    spr.img.pack()


def render(ob, out, name):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.film_transparent = True
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    scene.collection.objects.link(sun)
    w = bpy.data.worlds.new("w")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.8
    scene.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    lo = Vector([min(v.co[i] for v in ob.data.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for v in ob.data.vertices) for i in range(3)])
    centre = (lo + hi) / 2
    span = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z)
    for label, az, el in (("classic", -math.pi / 2, math.atan(1.0 / TILT)),
                          ("turned", -math.pi / 2 + 0.9, math.radians(30)),
                          ("side", 0.0, math.radians(15))):
        d = 50.0
        cam.location = centre + Vector((math.cos(az) * math.cos(el) * d, math.sin(az) * math.cos(el) * d,
                                        math.sin(el) * d))
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.ortho_scale = span * 1.25
        scene.render.filepath = os.path.join(out, "%s_%s.png" % (name, label))
        bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    catalog, name, out = argv
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    r = load(catalog, name)
    spr = Sprite(os.path.join(catalog, "sprites", name + ".png"))
    ob, (cells, faces) = carve(r, spr)
    paint(ob, r, spr)
    render(ob, out, name)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(out, name + ".glb"), export_format="GLB",
                              use_selection=True, export_yup=True)
    print("CARVED", name, "voxels", cells, "tris", sum(len(p.vertices) - 2 for p in ob.data.polygons),
          "size %.2f x %.2f x %.2f cells" % tuple(ob.dimensions))


if __name__ == "__main__":
    main()
