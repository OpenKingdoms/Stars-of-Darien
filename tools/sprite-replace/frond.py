"""Frond plants for the sprite-to-3D pipeline: palms and anything else whose
crown is spread leaves with gaps between them rather than a solid mass.

A frond plant is a thin trunk up to an umbrella that droops at the edge.
The umbrella is painted with the original sprite with its transparency
kept, so the gaps between the fronds stay gaps: from the classic camera
it is the original drawing, and from the side it reads as a canopy.

Same frame as carve.py: one Blender unit is one map cell, -Y toward the
classic camera, Z up, the origin at the anchor on the ground.
"""
import math

import bmesh
import bpy

import carve

BLOCK = 4  # sprite pixels per test block when measuring how gappy a crown is


DECAL_WORDS = ("smudge", "groundcover", "grass", "sand", "scorch", "puddle", "moss", "stain")


def is_decal(r):
    """Things that lie flat on the ground: stains, ground cover, sand. Not
    by category: 'grasses' holds the desert ferns."""
    text = (r["name"] + " " + r["description"]).lower()
    return any(w in text for w in DECAL_WORDS)


def build_decal(r, spr, relief=False):
    """A flat patch on the ground, a hair above it, painted with the
    sprite with its transparency kept. With relief, for ground cover, it
    rises up to 0.3 cell where the leaves are thickest."""
    hx, hy = r["sprite"]["hotspot"]
    x0, x1 = -hx / carve.CELL, (spr.w - hx) / carve.CELL
    # rows below the anchor reach toward the viewer, rows above away
    y_near = -(spr.h - hy) / carve.CELL
    y_far = hy / carve.CELL
    bm = bmesh.new()
    n, m = (max(16, spr.w // 3), max(16, spr.h // 3)) if relief else (16, 16)
    cover = _cover(spr) if relief else None
    vs = {}
    for i in range(n + 1):
        for j in range(m + 1):
            x, y, z = x0 + (x1 - x0) * i / n, y_near + (y_far - y_near) * j / m, 0.03
            if relief:
                px, py = carve.screen(hx, hy, x, y, 0.0)
                z += 0.3 * float(cover[min(spr.h - 1, max(0, int(py))), min(spr.w - 1, max(0, int(px)))])
            vs[(i, j)] = bm.verts.new((x, y, z))
    for i in range(n):
        for j in range(m):
            bm.faces.new([vs[(i, j)], vs[(i + 1, j)], vs[(i + 1, j + 1)], vs[(i, j + 1)]])
    me = bpy.data.meshes.new(r["name"])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(r["name"], me)
    bpy.context.collection.objects.link(ob)
    ob["box"] = (x0, x1, y_near, y_far)
    if relief:
        ob["relief"] = 1
    return ob


def _cover(spr):
    """How much of the neighbourhood of each pixel is drawn, 0 to 1."""
    import numpy as np
    k = np.exp(-0.5 * (np.arange(-9, 10) / 3.0) ** 2)
    k /= k.sum()
    a = np.array(spr.alpha, np.float32)
    a = np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 0, a)
    return np.apply_along_axis(lambda v: np.convolve(v, k, "same"), 1, a)


def is_frond(r, spr):
    """A living plant whose silhouette is more gap than leaf."""
    words = (r["description"] + " " + r["category"]).lower()
    if is_decal(r):
        return False
    if "dead" in words or not any(w in words for w in ("tree", "plant", "palm", "fern")):
        return False
    w, h = spr.w, spr.h
    hy = r["sprite"]["hotspot"][1]
    # only the crown: rows well above the ground point
    rows = range(0, max(1, int(hy * 0.7)), BLOCK)
    filled = total = 0
    xs = [x for y in range(h) for x in range(w) if spr.alpha[y][x]]
    if not xs:
        return False
    x0, x1 = min(xs), max(xs)
    for y in rows:
        for x in range(x0, x1 + 1, BLOCK):
            total += 1
            if any(spr.alpha[yy][xx] for yy in range(y, min(h, y + BLOCK)) for xx in range(x, min(w, x + BLOCK))):
                filled += 1
    return total > 0 and filled / total < 0.55


def build(r, spr):
    hx, hy = r["sprite"]["hotspot"]
    H = carve.drawn_height_for_frond(r, spr) if hasattr(carve, "drawn_height_for_frond") else None
    # the crown's centre on screen: the middle of the upper silhouette
    top = next(y for y in range(spr.h) if any(spr.alpha[y]))
    crown_rows = [y for y in range(top, int(top + (hy - top) * 0.6)) if any(spr.alpha[y])]
    xs = [x for y in crown_rows for x in range(spr.w) if spr.alpha[y][x]]
    cx_px = (min(xs) + max(xs)) / 2.0
    radius = (max(xs) - min(xs)) / 2.0 / carve.CELL
    # height of the crown's middle, standing at the anchor's depth
    crown_mid_row = (top + (crown_rows[-1] if crown_rows else top)) / 2.0
    crown_z = max(1.0, (hy - crown_mid_row) / (carve.CELL * carve.TILT))
    words = (r["description"] + " " + r["category"]).lower()
    palm = not (r.get("shape") == "fern" or ("tree" not in words and "palm" not in words and r.get("shape") != "palm"))
    if not palm:
        # a plant, not a tree: it sits low, with barely a stem
        crown_z = min(crown_z, 0.4 + 0.35 * radius)
    crown_x = (cx_px - hx) / carve.CELL
    bm = bmesh.new()
    # trunk: from the anchor up to the crown's middle, leaning if the crown sits off to one side
    seg = 10
    trunk_r, base_x = 0.12, 0.0
    if palm:
        # a palm's trunk as thick as drawn just above the anchor, in bark
        run = _run(spr, hx, hy - 2) or _run(spr, hx, hy - 6)
        if run:
            trunk_r = min(0.45, max(0.12, (run[1] - run[0] + 1) / 2 / carve.CELL))
            base_x = ((run[0] + run[1] + 1) / 2 - hx) / carve.CELL
    rings = []
    for k in range(seg + 1):
        t = k / seg
        x = base_x + (crown_x - base_x) * t * t
        z = crown_z * t
        ring = []
        for i in range(6):
            a = 2 * math.pi * i / 6
            ring.append(bm.verts.new((x + math.cos(a) * trunk_r * (1 - 0.3 * t), math.sin(a) * trunk_r * (1 - 0.3 * t), z)))
        rings.append(ring)
    for k in range(seg):
        for i in range(6):
            a, b = rings[k][i], rings[k][(i + 1) % 6]
            c, d = rings[k + 1][(i + 1) % 6], rings[k + 1][i]
            bm.faces.new((a, b, c, d)).material_index = 1 if palm else 0
    if palm:
        # closed at the top, where it shows through gaps in the fronds
        bm.faces.new(rings[-1]).material_index = 1
    # umbrella: a grid over the crown's disc, drooping toward the rim
    n = 24
    grid = {}
    for i in range(n + 1):
        for j in range(n + 1):
            u, v = (i / n) * 2 - 1, (j / n) * 2 - 1
            rr = math.hypot(u, v)
            if rr > 1.02:
                continue
            droop = 0.35 * radius * rr * rr
            grid[(i, j)] = bm.verts.new((crown_x + u * radius, v * radius, crown_z + 0.15 * radius - droop))
    for i in range(n):
        for j in range(n):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            if all(k in grid for k in q):
                bm.faces.new([grid[k] for k in q])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(r["name"])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(r["name"], me)
    bpy.context.collection.objects.link(ob)
    ob["box"] = (crown_x - radius, crown_x + radius, -radius, radius)
    ob["frond"] = 1
    if palm:
        ob["bark"] = 1
    return ob


def _run(spr, hx, y):
    """The unbroken run of drawn pixels on row y nearest column hx."""
    if not 0 <= y < spr.h or not any(spr.alpha[y]):
        return None
    x = min((x for x in range(spr.w) if spr.alpha[y][x]), key=lambda x: abs(x - hx))
    a, b = x, x
    while a > 0 and spr.alpha[y][a - 1]:
        a -= 1
    while b < spr.w - 1 and spr.alpha[y][b + 1]:
        b += 1
    return a, b


def cut_out(ob):
    """Keep the sprite's transparency on the crown: fronds with gaps. The
    glTF exporter writes alpha mode MASK for an alpha greater-than test."""
    for slot in ob.material_slots:
        mat = slot.material
        nt = mat.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        tex = next((nd for nd in nt.nodes if nd.type == "TEX_IMAGE"), None)
        if tex is None:
            # a palm's bark is solid
            continue
        gt = nt.nodes.new("ShaderNodeMath")
        gt.operation = "GREATER_THAN"
        gt.inputs[1].default_value = 0.5
        nt.links.new(tex.outputs["Alpha"], gt.inputs[0])
        nt.links.new(gt.outputs[0], bsdf.inputs["Alpha"])
        if "okPaint" in mat:
            mat["okPaint"]["alpha"] = "mask"
        mat.use_backface_culling = False
