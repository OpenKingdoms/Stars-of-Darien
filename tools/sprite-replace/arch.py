"""Built things for the sprite-to-3D pipeline: walls, fences, houses, huts,
towers and tents. Carving cannot invent a roof or a thin rail, so these
get real architecture shaped to the feature's footprint and height, and
carve.paint() then paints the original sprite onto it.

Same frame as carve.py: one Blender unit is one map cell, -Y toward the
classic camera, Z up, the origin at the feature's anchor on the ground.
"""
import math

import bmesh
import bpy

WALL_WORDS = ("wall", "palisade")
FENCE_WORDS = ("fence", "gate", "rail")
TOWER_WORDS = ("tower",)
TENT_WORDS = ("tent",)
HOUSE_WORDS = ("house", "hut", "hovel", "building", "barn", "shack", "cottage", "well", "temple", "church")
# kinds only shapes.json assigns, by looking at the sprite
HAND_KINDS = ("flat", "vault", "granary", "well", "site")
# the top of a lodestone site's plinth, in cells: lodestones stand on it
SITE_TOP = 0.3


def kind_of(r):
    words = (r["description"] + " " + r["category"]).lower()
    name = r["name"].lower()
    # Broken things stay irregular: carving suits them better.
    if any(w in words for w in ("ruin", "destroy", "damaged", "ransack", "rubble", "debris", "wreck", "burnt")):
        return None
    if any(w in words for w in TENT_WORDS) or name.startswith("zonhut"):
        return "tent"
    if any(w in words for w in TOWER_WORDS) or "tow" in name:
        return "tower"
    if any(w in words for w in FENCE_WORDS):
        return "fence"
    if any(w in words for w in WALL_WORDS):
        return "wall"
    if any(w in words for w in HOUSE_WORDS):
        return "house"
    return None


def _box(bm, cx, cy, z0, sx, sy, sz):
    m = bmesh.ops.create_cube(bm, size=1.0)
    for v in m["verts"]:
        v.co.x = cx + v.co.x * sx
        v.co.y = cy + v.co.y * sy
        v.co.z = z0 + (v.co.z + 0.5) * sz


def _cyl(bm, cx, cy, z0, r0, r1, h, seg=16):
    m = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r0, radius2=r1, depth=h)
    for v in m["verts"]:
        v.co.x += cx
        v.co.y += cy
        v.co.z += z0 + h / 2


def _hip_roof(bm, sx, sy, z0, h, over):
    hx, hy = sx / 2 + over, sy / 2 + over
    if sx >= sy:
        ridge = [(-(sx - sy) / 2, 0, z0 + h), ((sx - sy) / 2, 0, z0 + h)]
    else:
        ridge = [(0, -(sy - sx) / 2, z0 + h), (0, (sy - sx) / 2, z0 + h)]
    c = [(-hx, -hy, z0), (hx, -hy, z0), (hx, hy, z0), (-hx, hy, z0)]
    v = [bm.verts.new(p) for p in c + ridge]
    a, b = v[4], v[5]
    if sx >= sy:
        faces = [(v[0], v[1], b, a), (v[1], v[2], b), (v[2], v[3], a, b), (v[3], v[0], a), (v[3], v[2], v[1], v[0])]
    else:
        faces = [(v[0], v[1], a), (v[1], v[2], b, a), (v[2], v[3], b), (v[3], v[0], a, b), (v[3], v[2], v[1], v[0])]
    for f in faces:
        try:
            bm.faces.new(f)
        except ValueError:
            pass


def _top_row(spr):
    for y in range(spr.h):
        if any(spr.alpha[y]):
            return y
    return 0


def _ring(r, spr, kind):
    """Centre offset and radius of a round thing, read off the widest
    row of the sprite at or above the anchor."""
    hx, hy = r["sprite"]["hotspot"]
    spans = [spr.row_span(y) for y in range(max(0, hy - 60), min(spr.h, hy + 1))]
    spans = [s for s in spans if s]
    if not spans:
        return 0.0, 0.5
    lo, hi = max(spans, key=lambda s: s[1] - s[0])
    rad = (hi - lo + 1) / 2 / 16.0 * (0.8 if kind == "well" else 0.95)
    return ((lo + hi + 1) / 2 - hx) / 16.0, max(0.3, rad)


def drawn_height(r, spr, kind):
    """How tall the sprite shows the thing, in cells. The unit files'
    height is not the drawn height (walls say 12 cells and are drawn 1),
    so read it off the silhouette: the top row is the highest point, seen
    at the depth where that point stands."""
    hx, hy = r["sprite"]["hotspot"]
    fx, fz = r["footprint"]
    top = _top_row(spr)
    if kind in ("house", "tent", "fence") or (kind == "wall" and max(fx, fz) > 2 * min(fx, fz)):
        back = 0.0
    elif kind == "flat" or (kind == "vault" and fz > fx):
        # the far edge of the roof is the highest thing drawn
        back = fz / 2 * 0.85
    elif kind == "vault":
        back = 0.0
    elif kind in ("well", "granary"):
        # the far rim is the highest thing drawn
        back = _ring(r, spr, kind)[1]
    elif kind == "tower":
        back = max(0.4, min(fx, fz) / 2 * 0.9, spr.w / 32.0 * 0.85)
    else:
        back = fz / 2 * 0.95
    return max(0.4, (hy - back * 16.0 - top) / 8.0)


def build(r, kind, spr=None):
    fx, fz = r["footprint"]
    # the drawn width, which often overhangs the footprint
    drawn = r["sprite"]["w"] / 16.0
    H = max(0.5, (r["height"] or 32) / 16.0)
    if spr is not None and kind != "site":
        H = min(H, drawn_height(r, spr, kind)) if r["height"] else drawn_height(r, spr, kind)
    bm = bmesh.new()
    if kind in ("wall", "fence"):
        along_x = fx >= fz
        length = max(fx, fz)
        if kind == "wall" and max(fx, fz) <= 2 * min(fx, fz):
            # walls are laid a piece at a time: a squarish piece is a block
            sx = min(fx * 0.95, drawn * 0.95)
            sy = fz * 0.9
            _box(bm, 0, 0, 0, sx, sy, H)
            _box(bm, 0, 0, H, sx * 1.06, sy * 1.06, 0.12)
        elif kind == "wall":
            thick = min(0.8 * min(fx, fz), 0.35 + 0.15 * min(fx, fz))
            if along_x:
                _box(bm, 0, 0, 0, length, thick, H)
            else:
                _box(bm, 0, 0, 0, thick, length, H)
            # a coping along the top
            if along_x:
                _box(bm, 0, 0, H, length, thick * 1.2, 0.12)
            else:
                _box(bm, 0, 0, H, thick * 1.2, length, 0.12)
        else:
            posts = max(2, int(round(length)) + 1)
            for i in range(posts):
                t = -length / 2 + i * length / (posts - 1)
                px, py = (t, 0) if along_x else (0, t)
                _box(bm, px, py, 0, 0.12, 0.12, H)
            for zr in (H * 0.45, H * 0.85):
                if along_x:
                    _box(bm, 0, 0, zr - 0.05, length, 0.07, 0.1)
                else:
                    _box(bm, 0, 0, zr - 0.05, 0.07, length, 0.1)
    elif kind == "tower":
        rad = max(0.4, min(fx, fz) / 2 * 0.9, drawn / 2 * 0.85)
        shaft = H * 0.85
        _cyl(bm, 0, 0, 0, rad * 1.05, rad * 0.95, shaft)
        _cyl(bm, 0, 0, shaft, rad * 1.1, rad * 1.1, H * 0.05)
        for i in range(10):
            a = 2 * math.pi * i / 10
            _box(bm, math.cos(a) * rad, math.sin(a) * rad, shaft + H * 0.05, rad * 0.35, rad * 0.35, H * 0.1)
    elif kind == "tent":
        rad = max(0.5, min(fx, fz) / 2 * 0.95)
        _cyl(bm, 0, 0, 0, rad, 0.05, H, seg=8)
    elif kind == "flat":
        sx, sy = fx * 0.85, fz * 0.85
        _box(bm, 0, 0, 0, sx, sy, H)
        t, ph = 0.12, 0.18
        _box(bm, 0, -sy / 2 + t / 2, H, sx, t, ph)
        _box(bm, 0, sy / 2 - t / 2, H, sx, t, ph)
        _box(bm, -sx / 2 + t / 2, 0, H, t, sy, ph)
        _box(bm, sx / 2 - t / 2, 0, H, t, sy, ph)
    elif kind == "vault":
        sx, sy = fx * 0.85, fz * 0.85
        along_y = sy > sx
        span = sx if along_y else sy
        length = sy if along_y else sx
        rise = min(span / 2, H * 0.45)
        wall_h = H - rise
        _box(bm, 0, 0, 0, sx, sy, wall_h)
        seg = 12
        ends = []
        for e in (-1, 1):
            ring = []
            for i in range(seg + 1):
                a = math.pi * i / seg
                u, z = math.cos(a) * span / 2, wall_h + math.sin(a) * rise
                ring.append(bm.verts.new((u, e * length / 2, z) if along_y else (e * length / 2, u, z)))
            ends.append(ring)
        for i in range(seg):
            bm.faces.new((ends[0][i], ends[0][i + 1], ends[1][i + 1], ends[1][i]))
        for ring in ends:
            bm.faces.new(ring)
    elif kind == "granary":
        cx, rad = _ring(r, spr, kind)
        roof = min(H * 0.25, rad * 0.6)
        shaft = H - roof
        _cyl(bm, cx, 0, 0, rad, rad * 0.97, shaft, seg=20)
        _cyl(bm, cx, 0, shaft, rad * 1.1, 0.05, roof, seg=20)
    elif kind == "site":
        # a lodestone site: a two-step round plinth, its top the site art
        rad = max(min(fx, fz) / 2 * 0.95, spr.w / 32.0)
        _cyl(bm, 0, 0, 0, rad * 1.12, rad * 1.08, SITE_TOP * 0.45, seg=32)
        _cyl(bm, 0, 0, SITE_TOP * 0.45, rad, rad, SITE_TOP * 0.55, seg=32)
    elif kind == "well":
        cx, rad = _ring(r, spr, kind)
        ring_h = min(H, 0.5)
        _cyl(bm, cx, 0, 0, rad, rad, ring_h, seg=16)
        if H > 1.2:
            # the frame that holds the bucket
            for e in (-1, 1):
                _box(bm, cx + e * rad * 0.9, 0, 0, 0.1, 0.1, H * 0.9)
            _box(bm, cx, 0, H * 0.9 - 0.08, rad * 2, 0.08, 0.08)
    else:  # house
        sx, sy = fx * 0.85, fz * 0.85
        wall_h = H * 0.42
        _box(bm, 0, 0, 0, sx, sy, wall_h)
        _hip_roof(bm, sx, sy, wall_h, H - wall_h, over=0.15 * min(sx, sy) / 2 + 0.1)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(r["name"])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(r["name"], me)
    bpy.context.collection.objects.link(ob)
    # enough faces for the painting to follow, then smooth shading off:
    # architecture keeps its hard edges
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    sub = ob.modifiers.new("sub", "SUBSURF")
    sub.subdivision_type = "SIMPLE"
    sub.levels = 3
    bpy.ops.object.convert(target="MESH")
    lo = [min(v.co[i] for v in me.vertices) for i in range(2)]
    hi = [max(v.co[i] for v in me.vertices) for i in range(2)]
    ob["box"] = (lo[0], hi[0], lo[1], hi[1])
    if kind == "site":
        ob["site_top"] = SITE_TOP
        ob["standTop"] = SITE_TOP
    return ob
