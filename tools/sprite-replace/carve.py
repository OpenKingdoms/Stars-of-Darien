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
ALBEDO_GAIN = 1.3
STONE_TOP = 1.4  # the thickest a lying stone gets, in cells
STONE_SLOPE = 0.09  # cells of thickness per pixel in from the edge
LUMP = 0.15  # how far a canopy's bright leaf clusters push out, and its dark gaps in
ROUND_WORDS = ("tree", "bush", "plant", "shrub", "groundcover", "grass", "corn", "crops", "mushroom")


def load(catalog_dir, name):
    for r in json.load(open(os.path.join(catalog_dir, "catalog.json"))):
        if r["name"] == name:
            return r
    raise SystemExit("no feature " + name)


_CATALOGS = {}


def _catalog_row(sprite_path):
    """The catalog row of a sprite at <catalog dir>/sprites/<name>.png, or None."""
    root = os.path.dirname(os.path.dirname(os.path.abspath(sprite_path)))
    if root not in _CATALOGS:
        p = os.path.join(root, "catalog.json")
        _CATALOGS[root] = {r["name"]: r for r in json.load(open(p))} if os.path.exists(p) else {}
    return _CATALOGS[root].get(os.path.splitext(os.path.basename(sprite_path))[0])


class Sprite:
    def __init__(self, path):
        self.img = bpy.data.images.load(path)
        self.w, self.h = self.img.size
        # what the game paints this picture from at load (okpaint.py): a
        # feature's first frame, when the catalog has the sprite as drawn
        r = _catalog_row(path)
        self.paint = None
        if r and not r.get("texture") and r.get("sprite") and (r["sprite"]["w"], r["sprite"]["h"]) == (self.w, self.h):
            self.paint = {"kind": "feature", "name": r.get("seq") or r["name"], "world": r["world"],
                          "gain": ALBEDO_GAIN, "bleed": True, "alpha": "opaque", "size": [self.w, self.h]}
        elif r and r.get("texture") and r.get("texture_size"):
            # a card's 3DO texture at its own size: the UVs run 0 to 1 over
            # the picture whatever its size, so the game paints it unscaled
            self.paint = {"kind": "texture", "name": r["texture"], "world": r["world"],
                          "gain": ALBEDO_GAIN, "bleed": True, "alpha": "opaque", "size": list(r["texture_size"])}
        px = self.img.pixels[:]
        w, h = self.w, self.h
        # alpha as rows top-down, for lookups by sprite row
        self.alpha = [[px[((h - 1 - y) * w + x) * 4 + 3] > 0.5 for x in range(w)] for y in range(h)]
        import numpy as np
        # the drawn colours, rows top-down, for reading the drawing's parts
        self.rgb = np.array(px, dtype=np.float32).reshape(h, w, 4)[::-1, :, :3].copy()
        self.bleed(px)

    def copy(self, keep=None, mask=None):
        """This sprite on a texture of its own with its alpha, cleared where
        keep (rows top-down) is false. mask is how the game finds keep at
        load (okpaint.py), and without it a picture cut by keep is not one
        the game can paint."""
        import copy
        import numpy as np
        c = copy.copy(self)
        a = np.array(self.img.pixels[:], dtype=np.float32).reshape(self.h, self.w, 4)
        if keep is not None:
            a[..., 3] *= keep[::-1]
        c.img = bpy.data.images.new(self.img.name + "_copy", self.w, self.h, alpha=True)
        c.img.pixels[:] = a.ravel()
        c.alpha = (a[::-1, :, 3] > 0.5).tolist()
        c.paint = dict(self.paint) if self.paint and (keep is None or mask) else None
        if c.paint and keep is not None:
            c.paint["mask"] = dict(mask)
        return c

    def bleed(self, px):
        """Spread edge colours into every clear pixel, alpha untouched, so
        filtering and faces past the silhouette never pick up black."""
        import numpy as np
        a = np.array(px, dtype=np.float32).reshape(self.h, self.w, 4)
        rgb, known = a[..., :3].copy(), a[..., 3] > 0.5

        def shift(arr, dy, dx):
            # like np.roll, but nothing wraps round from the far edge
            out = np.zeros_like(arr)
            h, w = arr.shape[:2]
            out[max(dy, 0):h + min(dy, 0), max(dx, 0):w + min(dx, 0)] = \
                arr[max(-dy, 0):h + min(-dy, 0), max(-dx, 0):w + min(-dx, 0)]
            return out
        while known.any() and not known.all():
            acc = np.zeros_like(rgb)
            cnt = np.zeros(known.shape, np.float32)
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                k = shift(known, dy, dx)
                acc += shift(rgb, dy, dx) * k[..., None]
                cnt += k
            grow = ~known & (cnt > 0)
            rgb[grow] = acc[grow] / cnt[grow][:, None]
            known = known | grow
        # the sprite has its light painted in and the game lights the model
        # again: lift the colour so the lit model lands near the original
        a[..., :3] = np.clip(rgb * ALBEDO_GAIN, 0.0, 1.0)
        self.img.pixels[:] = a.ravel()

    def opaque(self):
        """Alpha 1 everywhere, for solid models: the game alpha-tests every
        texture, so a face painted from outside the silhouette would be a hole."""
        import numpy as np
        a = np.array(self.img.pixels[:], dtype=np.float32).reshape(-1, 4)
        a[:, 3] = 1.0
        self.img.pixels[:] = a.ravel()

    def clear_border(self):
        """Alpha 0 on the image's outer pixels, for cut-out models: a face
        past the image repeats its edge (glTF clamps), which would stretch a
        drawn edge pixel into a spike."""
        import numpy as np
        a = np.array(self.img.pixels[:], dtype=np.float32).reshape(self.h, self.w, 4)
        a[[0, -1], :, 3] = 0.0
        a[:, [0, -1], 3] = 0.0
        self.img.pixels[:] = a.ravel()
        if self.paint:
            self.paint["border"] = True

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


# the classic camera looks along (0, 1, -2): toward it is (0, -1, 2)
CAM = (0.0, -1 / math.sqrt(5), 2 / math.sqrt(5))


def globe(spr, hx, hy, crown, zscale, x, y, z):
    """A crown is painted like a globe seen from the classic camera: each
    point takes the sprite pixel at its angle from the view axis, spaced
    evenly out to the rim, the far side mirrored onto the near, so round
    sides wrap the painting instead of smearing it. A rim sample past the
    silhouette walks in until it lands on the drawing."""
    cx, cy, cz, r, h = crown
    d = [(x - cx) / r, (y - cy) / r, (z - cz) / max(h, 1e-3)]
    n = math.sqrt(sum(c * c for c in d)) or 1.0
    d = [c / n for c in d]
    dot = sum(a * b for a, b in zip(d, CAM))
    if dot < 0:
        d = [a - 2 * dot * b for a, b in zip(d, CAM)]
        dot = -dot
    e = [a - dot * b for a, b in zip(d, CAM)]
    s = math.sqrt(sum(c * c for c in e))
    theta = math.acos(max(-1.0, min(1.0, dot)))
    k = (theta / (math.pi / 2)) / s if s > 1e-4 else 0.0
    ox = e[0] * k * r
    oy, oz = e[1] * k * r, e[2] * k * h * zscale
    sx0, sy0 = screen(hx, hy, cx, cy, cz * zscale)
    sx, sy = sx0 + ox * CELL, sy0 - oy * CELL - oz * CELL * TILT
    for _ in range(40):
        if spr.inside(sx, sy):
            break
        sx, sy = sx0 + (sx - sx0) * 0.95, sy0 + (sy - sy0) * 0.95
    return sx, sy


def leaf_light(spr):
    """Each pixel's luminance, blurred to the size of a leaf cluster and
    scaled to -1..1 over the drawing: bright clusters high, gaps low."""
    import numpy as np
    lum = spr.rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    a = np.array(spr.alpha, np.float32)
    k = np.exp(-0.5 * (np.arange(-6, 7) / 2.5) ** 2)
    k /= k.sum()

    def blur(m):
        m = np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 0, m)
        return np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 1, m)
    b = blur(lum * a) / np.maximum(blur(a), 1e-3)
    on = a > 0.5
    mu, sd = b[on].mean(), b[on].std() + 1e-3
    return np.clip((b - mu) / (2 * sd), -1.0, 1.0)


def boulder(spr, hx, hy, top):
    """(x, y, rx, ry, h) of a dome lying on the ground, as wide as the rock
    is drawn. A dome ry deep and h tall shows 16 ry + sqrt((16 ry)^2 + (8 h)^2)
    rows, which barely depends on h, so h is 0.8 of the half width and the
    drawn height sets the depth. Its foot's front edge stands on the
    sprite's bottom row."""
    cols = [x for x in range(spr.w) if any(spr.alpha[y][x] for y in range(spr.h))]
    rows = [y for y in range(spr.h) if any(spr.alpha[y])]
    rx = (cols[-1] + 1 - cols[0]) / 2 / CELL
    h = min(top, 0.8 * rx)
    drawn = rows[-1] + 1 - rows[0]
    ry = max(0.35 * rx, (drawn ** 2 - (CELL * TILT * h) ** 2) / (2 * CELL * drawn))
    return ((cols[0] + cols[-1] + 1) / 2 - hx) / CELL, (hy - rows[-1] - 1) / CELL + ry, rx, ry, h


def carve(r, spr, hull=None):
    """With hull, a height in cells: every point the sprite covers up to that
    height, as deep as the footprint, for cutting a ruin to its outline."""
    hx, hy = r["sprite"]["hotspot"]
    fx, fz = r["footprint"]
    words = (r["description"] + " " + r["category"]).lower()
    rounded = any(wd in words for wd in ROUND_WORDS)
    # A living tree gets a trunk and a crown: the sprite sees it from above,
    # so carving alone would fill the whole height with canopy. A dead tree
    # keeps thin branches, carved like any other object.
    tree = "tree" in words and "dead" not in words
    shape = r.get("shape")
    if shape in ("crown", "poplar", "bush", "conifer"):
        tree, rounded = True, True
    if shape == "lode":
        rounded = True
    if "dead" in words or hull:
        rounded = False
    if hull:
        tree = False
    # The box: east-west from the sprite itself (things overhang their
    # footprint), north-south from the footprint, or as deep as wide for
    # plants, and up to the feature's height.
    x0, x1 = -hx / CELL, (spr.w - hx) / CELL
    if rounded:
        half = (x1 - x0) / 2
        y0, y1 = -half, half
    elif hull:
        # room for a sibling drawn wider than the ruin's footprint
        y0, y1 = -max(fx, fz) / 2 - 0.5, max(fx, fz) / 2 + 0.5
    else:
        y0, y1 = -fz / 2 - 0.25, fz / 2 + 0.25
    # no height: something lying low, never the hotspot's full reach
    H = r["height"] / CELL if r["height"] else min(0.5, hy / (CELL * TILT))
    if hull:
        H = hull
    # A rock is a boulder: its height comes from the drawing, not the TDF
    rock = None
    if not hull and r["category"].lower() == "rocks":
        rock = boulder(spr, hx, hy, H)
        H = rock[4]
        y0, y1 = rock[1] - rock[3] - 0.25, rock[1] + rock[3] + 0.25
    # Never taller than the sprite can show, even from the back edge.
    # Never taller than the sprite can show. A point at depth y and height
    # z lands on row hy - 16 y - 8 z, so the tallest reachable point is at
    # the front of the box, where -y is largest.
    H = min(H, (hy - y0 * CELL) / (CELL * TILT))
    # leafy canopies get lumps, with room for them toward and away from the camera
    lumpy = tree and shape in ("crown", "poplar", "conifer")
    if lumpy:
        y0, y1 = y0 * (1 + LUMP), y1 * (1 + LUMP)
    # Stones are mostly fallen slabs seen from above: every pixel of the
    # sprite is the top of a block lying on the ground, thicker away from
    # the silhouette's edge, so they lie down and have no holes.
    stone = not hull and ("standing stones" in words or shape == "stone")
    if stone:
        tree = rounded = False
        H = min(H, STONE_TOP)
        y0, y1 = (hy - spr.h - CELL * TILT * H) / CELL - 0.2, hy / CELL + 0.2
    nx = max(1, int(math.ceil((x1 - x0) * RES)))
    ny = max(1, int(math.ceil((y1 - y0) * RES)))
    nz = max(1, int(math.ceil(H * RES)))

    def centre(i, j, k):
        return x0 + (i + 0.5) / RES, y0 + (j + 0.5) / RES, (k + 0.5) / RES

    solid = bytearray(nx * ny * nz)
    ymid = (y0 + y1) / 2
    # a tree may stand lower than the sprite's height; its crown still
    # takes the sprite's full height of painting, stretched down
    zscale = 1.0
    if shape == "lode":
        # a lodestone stands as tall as its 3DO card; its painting, drawn
        # for a card leaning back, is stretched down onto it
        zscale = max(1.0, (hy / (CELL * TILT)) / H)
    if tree:
        wide = (x1 - x0) > 0.6 * H
        trunk_top = H * (0.32 if wide else 0.14)
        base = spr.row_span(hy - 2) or spr.row_span(hy - 6) or (hx - 2, hx + 2)
        trunk_x = ((base[0] + base[1]) / 2 - hx) / CELL
        trunk_r = min(0.45, max(0.14, (base[1] - base[0]) / 2 / CELL))
        crown_r = (x1 - x0) / 2
        crown_c = (trunk_top * 0.7 + H) / 2
        crown_h = (H - trunk_top * 0.7) / 2
        if shape in ("crown", "poplar", "conifer"):
            # thick enough to outlast the remesh below
            trunk_r = min(0.45, max(trunk_r, crown_r * 0.12, 1.6 / RES))
        if shape == "crown":
            # a round crown reaching down to a fifth of the tree, the tree
            # no taller than its crown is wide and a bit
            low = min(H, crown_r * 2.5)
            zscale = H / low
            trunk_top = 0.2 * low
            crown_c, crown_h = 0.6 * low, 0.4 * low
        elif shape == "conifer":
            # a cone narrowing to its tip, on a short trunk
            trunk_top = 0.15 * H
            crown_c, crown_h = (trunk_top + H) / 2, (H - trunk_top) / 2
        elif shape == "bush":
            # a low dome from the ground, no trunk
            crown_h = min(H * 0.5, crown_r * 0.7)
            crown_c = crown_h * 0.9
            trunk_top = 0.0
    if not rounded and not hull and not rock:
        spr.edge_distance()
    if lumpy:
        leaf = leaf_light(spr)
        crown = ((x0 + x1) / 2, (y0 + y1) / 2, crown_c, crown_r, crown_h)

        def lump(x, y, z):
            sx, sy = globe(spr, hx, hy, crown, zscale, x, y, z)
            xi = min(spr.w - 1, max(0, int(sx)))
            return 1.0 + LUMP * float(leaf[min(spr.h - 1, max(0, int(sy))), xi])

    def idx(i, j, k):
        return (k * ny + j) * nx + i

    if stone:
        for px in range(spr.w):
            i = int((((px + 0.5) - hx) / CELL - x0) * RES)
            prev = None
            for py in range(spr.h):
                if not spr.alpha[py][px] or not 0 <= i < nx:
                    prev = None
                    continue
                t = min(H, max(1.0 / RES, spr.dist[py][px] * STONE_SLOPE))
                j = int(((hy - (py + 0.5) - CELL * TILT * t) / CELL - y0) * RES)
                # a column's pixels are one run of ground, however the
                # thickening pulls them toward the camera
                lo, hi, tt = (j, j + 1, t) if prev is None else (min(j, prev[0]), max(j + 1, prev[0]), max(t, prev[1]))
                for jj in range(max(0, lo), min(ny, hi + 1)):
                    for k in range(min(nz, max(1, int(round(tt * RES))))):
                        solid[idx(i, jj, k)] = 1
                prev = (j, t)

    for k in range(0 if stone else nz):
        for j in range(ny):
            for i in range(nx):
                x, y, z = centre(i, j, k)
                sx, sy = screen(hx, hy, x, y, z * zscale)
                if tree and z < trunk_top:
                    # the trunk, hidden behind the crown in the sprite
                    if (x - trunk_x) ** 2 + (y - ymid) ** 2 <= trunk_r * trunk_r:
                        solid[idx(i, j, k)] = 1
                    continue
                if not spr.inside(sx, sy):
                    continue
                if tree:
                    # the crown: a rounded mass, as wide as the sprite
                    rr = (x - (x0 + x1) / 2) ** 2 + (y - ymid) ** 2
                    if shape == "conifer":
                        e = math.sqrt(rr) / max(1e-3, crown_r * (H - z) / (H - trunk_top))
                    else:
                        e = math.sqrt(rr / crown_r ** 2 + ((z - crown_c) / crown_h) ** 2)
                    if e > (lump(x, y, z) if lumpy and abs(e - 1) < LUMP else 1.0):
                        continue
                elif rock:
                    bx, by, rx, ry, _ = rock
                    if ((x - bx) / rx) ** 2 + ((y - by) / ry) ** 2 + (z / H) ** 2 > 1:
                        continue
                elif rounded:
                    # the width of the silhouette at this height, seen at the
                    # middle of the object, sets the radius of this slice
                    span = spr.row_span(screen(hx, hy, 0, (y0 + y1) / 2, z * zscale)[1])
                    if not span:
                        continue
                    cxs = ((span[0] + span[1]) / 2 - hx) / CELL
                    rad = (span[1] - span[0]) / 2 / CELL + 0.5 / RES
                    cy = (y0 + y1) / 2
                    if (x - cxs) ** 2 + (y - cy) ** 2 > rad * rad:
                        continue
                elif not hull:
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
    # foliage remeshes coarser than the carving grid, or its steps survive
    # into the surface; stone keeps the finer grid and its holes
    rem.voxel_size = (1.6 if tree else 1.0) / RES
    sm = ob.modifiers.new("smooth", "CORRECTIVE_SMOOTH")
    sm.iterations = 18 if tree else 6
    sm.use_only_smooth = True
    dec = ob.modifiers.new("decimate", "DECIMATE")
    dec.ratio = 0.35
    bpy.ops.object.convert(target="MESH")
    for p in ob.data.polygons:
        p.use_smooth = True
    lo = min(v.co.z for v in ob.data.vertices) if tree else 0.0
    if 0 < lo < 0.5:
        # smoothing lifts a trunk's foot off the ground: stretch it back down
        for v in ob.data.vertices:
            if v.co.z < 0.5:
                v.co.z -= lo * (0.5 - v.co.z) / (0.5 - lo)
    ob["box"] = (x0, x1, y0, y1)
    ob["zscale"] = zscale
    if tree and trunk_top > 0:
        ob["trunk"] = (trunk_x, ymid, trunk_r, trunk_top)
    if tree:
        ob["crown"] = ((x0 + x1) / 2, ymid, crown_c, crown_r, crown_h)
    if rock:
        ob["dome"] = (rock[0], rock[1], 0.0, rock[2], H)
    return ob, (nx * ny * nz, faces)


# Glb extras that mark a model as made from the player's own files, which
# the studio then keeps out of the shared folders.
PLAYERS_FILES = "okFromPlayersFiles"


def paint(ob, r, spr):
    """Every corner takes the sprite pixel the original drew at that spot."""
    ob[PLAYERS_FILES] = True
    hx, hy = r["sprite"]["hotspot"]
    me = ob.data
    uv = me.uv_layers.new(name="UVMap")
    x0, x1, y0, y1 = ob["box"]
    xc, yc = (x0 + x1) / 2, (y0 + y1) / 2
    stretch = (x1 - x0) / max(1e-3, (y1 - y0))
    zscale = ob.get("zscale", 1.0)
    trunk = tuple(ob["trunk"]) if "trunk" in ob else None
    crown = tuple(ob["crown"]) if "crown" in ob else None
    # a boulder's hidden faces are painted as a globe's, so they land on the rock
    dome = tuple(ob["dome"]) if "dome" in ob else None
    site_top = ob.get("site_top")

    def in_trunk(x, y, z):
        return trunk is not None and z < trunk[3] - 0.05 and             (x - trunk[0]) ** 2 + (y - trunk[1]) ** 2 < (trunk[2] * 1.8) ** 2

    for poly in me.polygons:
        n = poly.normal
        for li in poly.loop_indices:
            x, y, z = me.vertices[me.loops[li].vertex_index].co
            seen = n.z >= -0.3 and (n.y <= -0.3 or n.z > 0.6 or ob.get("relief"))
            if n.z < -0.3:
                # undersides: the painting turned under, as for the sides
                px, py, pz = xc + (y - yc) * stretch, yc, z
            elif n.y <= -0.3 or n.z > 0.6 or ob.get("relief"):
                # faces the classic camera saw: the sprite exactly
                px, py, pz = x, y, z
            elif abs(n.y) >= abs(n.x):
                # the back: the front painting, mirrored front to back
                px, py, pz = x, 2 * yc - y, z
            else:
                # east and west sides: the painting turned onto the side
                side = 1.0 if n.x > 0 else -1.0
                px, py, pz = xc + side * (y - yc) * stretch, yc, z
            if site_top is not None:
                # the site art lies on the plinth's top as it lay on the
                # ground, scaled out to the rim; the rest is plain stone
                k = ob["site_art"]
                sx, sy = screen(hx, hy, x * k, y * k, 0.0)
            elif crown and not in_trunk(x, y, z):
                sx, sy = globe(spr, hx, hy, crown, zscale, x, y, z)
            elif dome and not seen:
                sx, sy = globe(spr, hx, hy, dome, 1.0, x, y, z)
            else:
                sx, sy = screen(hx, hy, px, py, pz * zscale)
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
    if spr.paint:
        # alpha stays unused until frond.cut_out links it
        mat["okPaint"] = dict(spr.paint, alpha="opaque")
    me.materials.append(mat)
    if trunk or ob.get("bark"):
        # a frond's trunk faces come marked for the bark already
        bark = bpy.data.materials.new(r["name"] + "_bark")
        bark.use_nodes = True
        bb = bark.node_tree.nodes["Principled BSDF"]
        bb.inputs["Base Color"].default_value = (*bark_colour(spr, hx, hy, bool(ob.get("bark"))), 1.0)
        bb.inputs["Roughness"].default_value = 1.0
        me.materials.append(bark)
        for poly in me.polygons if trunk else ():
            if all(in_trunk(*me.vertices[vi].co) for vi in poly.vertices):
                poly.material_index = 1
    if site_top is not None:
        rim = bpy.data.materials.new(r["name"] + "_rim")
        rim.use_nodes = True
        rb = rim.node_tree.nodes["Principled BSDF"]
        rb.inputs["Base Color"].default_value = (*rim_colour(spr, hx, hy), 1.0)
        rb.inputs["Roughness"].default_value = 1.0
        me.materials.append(rim)
        for poly in me.polygons:
            top = poly.normal.z > 0.6 and min(me.vertices[vi].co.z for vi in poly.vertices) > site_top - 1e-3
            poly.material_index = 0 if top else 1
    spr.img.pack()


def rim_colour(spr, hx, hy):
    """The median colour of a site's outer ring of art, made linear as a
    material colour is (the image holds sRGB values)."""
    import numpy as np
    a = np.array(spr.img.pixels[:], dtype=np.float32).reshape(spr.h, spr.w, 4)[::-1]
    yy, xx = np.mgrid[0:spr.h, 0:spr.w]
    d = np.hypot(xx + 0.5 - hx, yy + 0.5 - hy) / (spr.w / 2.0)
    ring = (d > 0.75) & (d < 1.0) & np.array(spr.alpha)
    if ring.sum() < 4:
        return (0.07, 0.06, 0.055)
    c = np.median(a[ring][:, :3], 0)
    return tuple(float(v) for v in np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92))


def bark_colour(spr, hx, hy, drawn_ok=False):
    """The brownest pixels near where the tree meets the ground, made
    linear as a material colour is, or a plain bark brown when the sprite
    shows none. With drawn_ok, for a palm whose trunk is drawn bare, the
    median colour there stands in when none is brown (grey bark)."""
    px = spr.img.pixels[:]
    w, h = spr.w, spr.h
    picks, drawn = [], []
    for y in range(max(0, hy - 10), min(h, hy + 4)):
        for x in range(max(0, hx - 12), min(w, hx + 12)):
            i = ((h - 1 - y) * w + x) * 4
            rr, gg, bb = px[i:i + 3]
            # the mask, not the texture's alpha, which opaque() may have set
            if spr.alpha[y][x]:
                drawn.append((rr, gg, bb))
                if rr > gg * 1.05 and rr > bb * 1.2 and 0.08 < rr < 0.8:
                    picks.append((rr, gg, bb))
    if len(picks) < 4:
        if len(drawn) < 4 or not drawn_ok:
            return (0.20, 0.13, 0.07)
        drawn.sort(key=sum)
        picks = [drawn[len(drawn) // 2]]
    c = [sum(c[k] for c in picks) / len(picks) for k in range(3)]
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)


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
                              use_selection=True, export_yup=True, export_extras=True)
    print("CARVED", name, "voxels", cells, "tris", sum(len(p.vertices) - 2 for p in ob.data.polygons),
          "size %.2f x %.2f x %.2f cells" % tuple(ob.dimensions))


if __name__ == "__main__":
    main()
