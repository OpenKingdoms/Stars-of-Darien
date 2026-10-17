"""Elsin, the Aramon monarch (ARAKING), remastered for Stars of Darien.

    blender -b --factory-startup -P tools/units/elsin.py -- [--out GLB] [--assets DIR] [--review DIR] [--rig RIG]

Builds the model on the original's rig (unitkit.Rig), so the original's
script walks, fights, casts and builds with it, and the pieces its Killed
script throws come off as they should. Every part is modelled at rest
around the original's pivots and keeps to the original's silhouette, with
a sturdy build: gilded plate (elsin_armour.py), a realistic face from
Blender Studio's CC0 Human Base Meshes with hair and beard (elsin_head.py),
a crenellated crown with red stones, a longsword, and the player's colour
where the original has it, on the cape and the pauldrons, and on a tabard.

The materials are glTF PBR from three pictures baked here through one UV
layout: the colour, a normal map and metal with roughness, all made from
noise and numbers and the model's own shape. The strands of hair and the
embroidered emblem are cut out of pictures drawn here too. Nothing comes
from the original's art. Exports unity/Assets/Overrides/Units/ARAKING.glb,
or --out.

--assets is where the CC0 head bundle is kept, fetched on first use
(outside the repository; docs/ASSET_CREDITS.md). --review DIR renders the
model beside the original from the player's files, for checking only, and
--rig names a pose dump from the engine (Tests/Editor/UnitRigDump.cs) to
render the script's animations too.
"""
import argparse
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import elsin_armour as ea  # noqa: E402
import elsin_head as eh  # noqa: E402
import unitkit as uk  # noqa: E402

MODEL = "ARAKING"
OUT = os.path.join(uk.REPO, "unity", "Assets", "Overrides", "Units", MODEL + ".glb")
ASSETS = os.environ.get("OKU_UNIT_ASSETS", os.path.join(os.path.dirname(uk.REPO), "unit-assets"))
V = Vector
FACE_BOX = (-0.15, eh.EYE_Z - 0.15, 0.3)  # x and z of the eye picture's corner, and its size
EMBLEM_BOX = (-0.3, 1.95, 0.6)  # the emblem's lower corner on the cape's back, and its size


# ---------------------------------------------------------------- materials

def materials():
    m = {
        "gold": uk.material("gold", (0.75, 0.53, 0.22), metal=1.0, rough=0.35),
        "blade": uk.material("blade", (0.8, 0.8, 0.82), metal=1.0, rough=0.2),
        "mail": uk.material("mail", (0.3, 0.31, 0.33), metal=1.0, rough=0.45),
        "leather": uk.material("leather", (0.09, 0.05, 0.028), rough=0.65),
        "teamcolor": uk.material("teamcolor", (0.82, 0.82, 0.82), rough=0.88),
        "teamcolor_lining": uk.material("teamcolor_lining", (0.42, 0.42, 0.42), rough=0.9),
        "teamcolor_enamel": uk.material("teamcolor_enamel", (0.86, 0.86, 0.86), rough=0.32),
        "goldthread": uk.material("goldthread", (0.8, 0.6, 0.26), metal=0.8, rough=0.45),
        "skin": uk.material("skin", (0.47, 0.235, 0.155), rough=0.45),
        "eye": uk.material("eye", (0.8, 0.78, 0.74), rough=0.08),
        "hair": uk.material("hair", (0.05, 0.032, 0.02), rough=0.5),
        "gem": uk.material("gem", (0.5, 0.02, 0.03), rough=0.05, emit=(0.45, 0.02, 0.02), strength=0.6),
    }
    # the strands rough, so a card seen edge-on doesn't catch the sky; cut wide, so they read full
    for name, pic, metal, rough, cut in (("strands", eh.strand_picture(), 0.0, 0.8, 0.35), ("emblem", emblem_picture(), 0.8, 0.4, 0.5)):
        mat = uk.material(name, (1, 1, 1), metal=metal, rough=rough, double=True)
        b = mat.node_tree.nodes["Principled BSDF"]
        t = mat.node_tree.nodes.new("ShaderNodeTexImage")
        t.image = pic
        mat.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
        mat.node_tree.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
        mat["uk_mask"] = cut
        mat[uk.okpaint.GENERATED] = True
        m[name] = mat
    return m


# ---------------------------------------------------------------- pictures

def eye_picture(eyes, size=512):
    """Irises and pupils where the eyes look out, laid over them from the
    front through the face box, from numbers: a grey-blue iris with a
    hazel ring round the pupil, streaks and a dark rim, and the white, a
    little warmer at its edge."""
    x0, z0, w = FACE_BOX
    k = np.linspace(0.0, 1.0, size)
    X, Z = np.meshgrid(x0 + w * k, z0 + w * k)
    rgba = np.zeros((size, size, 4), np.float32)
    rgba[..., :3] = (0.8, 0.77, 0.72)
    rgba[..., 3] = 1.0
    for c, r in eyes:
        d = np.hypot(X - c.x, Z - c.z)
        ri = r * 0.47
        a = np.arctan2(Z - c.z, X - c.x)
        streak = 0.85 + 0.15 * np.sin(a * 23.0) * np.sin(a * 7.0 + 1.0)
        iris = d < ri
        t = np.clip(d / ri, 0, 1)
        blue = np.stack([0.2 + 0.08 * t, 0.25 + 0.08 * t, 0.28 + 0.07 * t], -1)
        hazel = np.clip((0.62 - t) / 0.2, 0, 1)[..., None]
        col = (blue * (1 - hazel) + np.array([0.3, 0.23, 0.12]) * hazel) * streak[..., None]
        rgba[iris, :3] = col[iris]
        rgba[iris & (d > ri * 0.85), :3] *= 0.45
        rgba[d < ri * 0.36, :3] = (0.015, 0.012, 0.01)
        edge = (d >= ri) & (d < r * 0.95)
        warm = np.clip((d - ri) / (r * 0.6), 0, 1)
        rgba[edge, 1] -= 0.08 * warm[edge]
        rgba[edge, 2] -= 0.1 * warm[edge]
    return uk.image("elsin_eyes", rgba)


def emblem_picture(size=256):
    """Aramon's tower embroidered in gold thread for the back of the cape:
    crenellated, with a gate and a window, its stitches running across,
    and clear around it."""
    k = np.linspace(0.0, 1.0, size)
    X, Y = np.meshgrid(k, k)
    body = (np.abs(X - 0.5) < 0.2) & (Y > 0.12) & (Y < 0.7)
    top = (np.abs(X - 0.5) < 0.26) & (Y >= 0.7) & (Y < 0.78)
    merlons = (np.abs(X - 0.5) < 0.26) & (Y >= 0.78) & (Y < 0.88) & (((X - 0.24) / 0.104).astype(int) % 2 == 0)
    gate = (np.abs(X - 0.5) < 0.07) & (Y > 0.12) & ((Y < 0.27) | (np.hypot(X - 0.5, Y - 0.27) < 0.07))
    window = (np.abs(X - 0.5) < 0.03) & (Y > 0.46) & (Y < 0.58)
    shape = (body | top | merlons) & ~gate & ~window
    stitch = 0.82 + 0.18 * np.sin((X * 0.7 + Y) * size * 0.9)
    rgba = np.zeros((size, size, 4), np.float32)
    rgba[..., 0] = 0.85 * stitch
    rgba[..., 1] = 0.64 * stitch
    rgba[..., 2] = 0.27 * stitch
    rgba[..., 3] = shape.astype(np.float32)
    return uk.image("elsin_emblem", rgba)


# ---------------------------------------------------------------- looks

def looks(eyes, hp):
    """Each material's albedo, height, roughness and metal for the bake:
    gilding aged in its creases and polished on its edges, hammered and
    scratched; leather with a grain; wool with a weave; mail rings; a
    weathered face with pores and lines; eyes; hair in strands. The face's
    marks, the lids' creases among them, are placed in the head piece's
    own frame, whose origin is hp."""
    eye_pic = eye_picture(eyes)
    eye_z = eh.EYE_Z - hp.z

    def obj(n):
        return n.coord("Object")

    def world(n):
        # model space at rest, the same on every piece, for cloth that runs across the cape's hinges
        return n.geometry("Position")

    def wear(n):
        """Masks for old gilding: tarnish in broad patches, the edges rubbed
        bright, and the recesses, where the gilt has worn off the steel."""
        tarnish = n.range(n.noise(obj(n), 3.0, 4.0, 0.6), 0.42, 0.68, 0.0, 1.0)
        edge = n.range(n.geometry("Pointiness"), 0.5, 0.58, 0.0, 1.0)
        occ = n.new("ShaderNodeAmbientOcclusion")
        occ.only_local = True
        occ.samples = 16
        occ.inputs["Distance"].default_value = 0.06
        recess = n.range(occ.outputs["AO"], 0.9, 0.5, 0.0, 1.0)
        return tarnish, edge, recess

    def scratches(n):
        return n.range(n.noise(n.scale(obj(n), (60.0, 60.0, 600.0)), 1.0, 1.0), 0.66, 0.72, 0.0, 1.0)

    def gold_albedo(n):
        """A muted gold browned by tarnish in patches, the dark steel showing
        in the recesses, paler where hands and blades have rubbed it thin."""
        tarnish, edge, recess = wear(n)
        col = n.mix(n.rgb((0.6, 0.42, 0.17)), (0.36, 0.27, 0.15), n.math("MULTIPLY", tarnish, 0.55))
        col = n.mix(col, (0.14, 0.13, 0.12), n.math("MULTIPLY", recess, 0.9))
        col = n.mix(col, (0.82, 0.64, 0.36), n.math("MULTIPLY", edge, 0.45))
        col = n.mix(col, (0.7, 0.66, 0.58), n.math("MULTIPLY", scratches(n), 0.35))
        return n.mul(col, n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.16), 0.92))

    def gold_rough(n):
        tarnish, edge, recess = wear(n)
        r = n.math("ADD", 0.2, n.range(n.noise(obj(n), 4.0, 3.0, 0.6), 0.35, 0.68, 0.0, 0.3))
        r = n.math("ADD", r, n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.1))
        r = n.math("ADD", r, n.math("MULTIPLY", tarnish, 0.18))
        r = n.math("ADD", r, n.math("MULTIPLY", recess, 0.3))
        r = n.math("SUBTRACT", r, n.math("MULTIPLY", edge, 0.12))
        return n.math("SUBTRACT", r, n.math("MULTIPLY", scratches(n), 0.06), clamp=True)

    def hammered(n, scale=26.0, k=1.0):
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "SMOOTH_F1"
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = scale
        dents = n.math("MULTIPLY", vor.outputs["Distance"], 0.6 * k)
        fine = n.math("MULTIPLY", n.noise(obj(n), 220.0, 2.0), 0.15 * k)
        return n.math("SUBTRACT", n.math("ADD", dents, fine), n.math("MULTIPLY", scratches(n), 0.25 * k))

    uk.look("gold", gold_albedo, hammered, gold_rough, 1.0, bump=0.0025)

    def rings(n):
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "F1"
        vor.inputs["Randomness"].default_value = 0.0
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = 70.0
        d = n.math("ABSOLUTE", n.math("SUBTRACT", vor.outputs["Distance"], 0.36))
        return n.range(d, 0.04, 0.11, 1.0, 0.0)

    uk.look("mail", lambda n: n.mul(n.rgb((0.36, 0.37, 0.39)), n.math("ADD", n.math("MULTIPLY", rings(n), 0.8), 0.2)),
            rings, lambda n: n.math("SUBTRACT", 0.6, n.math("MULTIPLY", rings(n), 0.25)), 1.0, bump=0.004, ao=1.4)

    def blade_height(n):
        z = n.new("ShaderNodeSeparateXYZ")
        n.link(obj(n), z.inputs[0])
        groove = n.range(n.math("ABSOLUTE", z.outputs["Z"]), 0.012, 0.024, -1.0, 0.0)
        return n.math("ADD", groove, n.math("MULTIPLY", n.noise(n.scale(obj(n), (40.0, 1.0, 40.0)), 8.0, 2.0), 0.08))

    def brushed(n, k):
        return n.math("ADD", n.math("MULTIPLY", n.noise(n.scale(obj(n), (25.0, 0.6, 25.0)), 4.0, 2.0), k), 1.0 - k)

    uk.look("blade", lambda n: n.mul(n.rgb((0.8, 0.8, 0.82)), brushed(n, 0.12)), blade_height,
            lambda n: n.math("ADD", 0.14, n.math("MULTIPLY", n.noise(n.scale(obj(n), (30.0, 1.0, 30.0)), 6.0, 2.0), 0.1)), 1.0, bump=0.006)

    def leather_albedo(n):
        edge = n.range(n.geometry("Pointiness"), 0.5, 0.58, 0.0, 1.0)
        col = n.mul(n.rgb((0.09, 0.05, 0.028)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 40.0, 3.0), 0.4), 0.78))
        return n.mix(col, (0.2, 0.12, 0.07), n.math("MULTIPLY", edge, 0.5))

    uk.look("leather", leather_albedo, lambda n: n.math("ADD", n.noise(obj(n), 90.0, 3.0), n.math("MULTIPLY", n.noise(obj(n), 12.0, 2.0), 0.6)),
            lambda n: n.math("ADD", 0.55, n.math("MULTIPLY", n.noise(obj(n), 20.0, 2.0), 0.25)), 0.0, bump=0.0015)

    def weave(n):
        # coarse enough for the atlas to hold, a heavy wool's twill up close
        return n.math("ADD", n.wave(world(n), 70.0, "X"), n.wave(world(n), 70.0, "Z"))

    def cloth(base):
        def f(n):
            k = n.math("ADD", n.math("MULTIPLY", weave(n), 0.05), 0.9)
            k = n.math("ADD", k, n.math("MULTIPLY", n.noise(world(n), 5.0, 2.0), 0.05))
            return n.mul(n.rgb((base, base, base)), k)
        return f

    uk.look("teamcolor", cloth(0.86), lambda n: n.math("ADD", weave(n), n.math("MULTIPLY", n.noise(world(n), 3.0, 1.0), 0.8)), 0.88, 0.0, bump=0.0012, ao=1.6)
    uk.look("teamcolor_lining", cloth(0.46), weave, 0.9, 0.0, bump=0.001)
    uk.look("teamcolor_enamel", lambda n: n.mul(n.rgb((0.9, 0.9, 0.9)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 7.0, 2.0), 0.08), 0.92)),
            lambda n: hammered(n, 20.0, 0.6), lambda n: n.math("ADD", 0.26, n.math("MULTIPLY", n.noise(obj(n), 9.0, 2.0), 0.14)), 0.0, bump=0.002)
    uk.look("goldthread", lambda n: n.mul(n.rgb((0.66, 0.53, 0.3)), n.math("ADD", n.math("MULTIPLY", n.wave(world(n), 50.0, "X", 0.0, "SAW"), 0.25), 0.8)),
            lambda n: n.wave(world(n), 50.0, "X", 0.0, "SAW"), 0.5, 0.75, bump=0.002)

    def near(n, centre, radius):
        d = n.new("ShaderNodeVectorMath")
        d.operation = "DISTANCE"
        n.link(obj(n), d.inputs[0])
        d.inputs[1].default_value = centre
        return n.range(d.outputs["Value"], radius, 0.0, 0.0, 1.0)

    nose = (0.0, -0.33, eye_z - 0.1)
    cheeks = [(s * 0.105, -0.27, eye_z - 0.075) for s in (-1, 1)]
    sockets = [tuple(c - hp) for c, r in eyes]

    def creases(n):
        """1 along the fold of each upper lid, an arc over the eye under the brow."""
        sep = n.new("ShaderNodeSeparateXYZ")
        n.link(obj(n), sep.inputs[0])
        out = None
        for sx, sy, sz in sockets:
            dx = n.math("SUBTRACT", sep.outputs["X"], sx)
            arc = n.math("ADD", sz + 0.024, n.math("MULTIPLY", n.math("MULTIPLY", dx, dx), -11.0))
            line = n.range(n.math("ABSOLUTE", n.math("SUBTRACT", sep.outputs["Z"], arc)), 0.0, 0.0045, 1.0, 0.0)
            line = n.math("MULTIPLY", line, n.range(n.math("ABSOLUTE", dx), 0.024, 0.036, 1.0, 0.0))
            line = n.math("MULTIPLY", line, n.range(sep.outputs["Y"], sy - 0.005, sy - 0.02, 0.0, 1.0))
            out = line if out is None else n.math("MAXIMUM", out, line)
        return out

    def skin_albedo(n):
        col = n.mul(n.rgb((0.47, 0.235, 0.155)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.1), 0.95))
        col = n.mix(col, (0.42, 0.2, 0.13), n.math("MULTIPLY", n.range(n.noise(obj(n), 9.0, 3.0), 0.45, 0.7, 0.0, 1.0), 0.25))
        red = n.math("MAXIMUM", near(n, nose, 0.1), n.math("MAXIMUM", near(n, cheeks[0], 0.11), near(n, cheeks[1], 0.11)))
        col = n.mix(col, (0.55, 0.18, 0.13), n.math("MULTIPLY", red, 0.5))
        shade = n.math("MAXIMUM", near(n, sockets[0], 0.075), near(n, sockets[1], 0.075))
        col = n.mul(col, n.math("SUBTRACT", 1.0, n.math("MULTIPLY", shade, 0.36)))
        return n.mul(col, n.math("SUBTRACT", 1.0, n.math("MULTIPLY", creases(n), 0.3)))

    def skin_height(n):
        vor = n.new("ShaderNodeTexVoronoi")
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = 700.0
        pores = n.range(vor.outputs["Distance"], 0.0, 0.35, -1.0, 0.0)
        sep = n.new("ShaderNodeSeparateXYZ")
        n.link(obj(n), sep.inputs[0])
        forehead = n.math("MULTIPLY", n.range(sep.outputs["Z"], eye_z + 0.06, eye_z + 0.09, 0.0, 1.0), n.range(sep.outputs["Y"], -0.12, -0.2, 0.0, 1.0))
        lines = n.math("MULTIPLY", n.math("SINE", n.math("MULTIPLY", sep.outputs["Z"], 260.0)), forehead)
        # crow's feet at the outer corners of the eyes, and lines under them
        corners = n.math("MAXIMUM", *[near(n, (x + math.copysign(0.05, x), y + 0.01, z), 0.045) for x, y, z in sockets])
        crows = n.math("MULTIPLY", n.math("SINE", n.math("MULTIPLY", sep.outputs["Z"], 420.0)), corners)
        under = n.math("MAXIMUM", *[near(n, (x, y - 0.02, z - 0.035), 0.022) for x, y, z in sockets])
        crows = n.math("ADD", crows, n.math("MULTIPLY", n.math("SINE", n.math("MULTIPLY", sep.outputs["Z"], 330.0)), n.math("MULTIPLY", under, 0.35)))
        # two frown lines between the brows
        mid = (sockets[0][0] + sockets[1][0]) * 0.5
        frown = n.math("MULTIPLY", n.range(sep.outputs["Z"], sockets[0][2] + 0.012, sockets[0][2] + 0.028, 0.0, 1.0),
                       n.range(sep.outputs["Z"], sockets[0][2] + 0.06, sockets[0][2] + 0.042, 0.0, 1.0))
        frown = n.math("MULTIPLY", frown, n.range(n.math("ABSOLUTE", n.math("SUBTRACT", n.math("ABSOLUTE", n.math("SUBTRACT", sep.outputs["X"], mid)), 0.013)), 0.0, 0.004, 1.0, 0.0))
        skin = n.math("ADD", n.math("ADD", n.math("MULTIPLY", pores, 0.12), n.math("MULTIPLY", lines, 0.9)), n.math("MULTIPLY", crows, 0.6))
        skin = n.math("SUBTRACT", skin, n.math("MULTIPLY", frown, 1.2))
        return n.math("SUBTRACT", skin, n.math("MULTIPLY", creases(n), 1.6))

    uk.look("skin", skin_albedo, skin_height, lambda n: n.math("ADD", 0.42, n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.15)), 0.0, bump=0.002, ao=0.8)
    uk.look("eye", lambda n: n.image(eye_pic, "proj")[0], None, 0.06, 0.0, ao=0.3)

    def strands(n):
        return n.noise(n.scale(obj(n), (160.0, 160.0, 10.0)), 1.0, 2.0)

    uk.look("hair", lambda n: n.mul(n.rgb((0.045, 0.028, 0.017)), n.range(strands(n), 0.3, 0.75, 0.7, 1.5)), strands, 0.62, 0.0, bump=0.0015)
    uk.look("gem", lambda n: n.rgb((0.5, 0.02, 0.03)), None, 0.05, 0.0, ao=0.2)


# ---------------------------------------------------------------- assembly

def emblem_decal(capes, M):
    """The embroidered tower on the back of the cape's second piece: a
    patch lying on the cloth, cut out of the emblem picture."""
    x0, z0, w = EMBLEM_BOX
    bm0 = bmesh.new()
    bm0.from_mesh(capes["Cape2"].data)
    bvh = BVHTree.FromBMesh(bm0)
    bm0.free()
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    n = 14
    grid = []
    for r in range(n + 1):
        row = []
        for c in range(n + 1):
            x, z = x0 + w * c / n, z0 + w * r / n
            hit = bvh.ray_cast(V((x, 2.0, z)), V((0, -1, 0)), 3.0)
            p = hit[0] + hit[1] * 0.006 if hit[0] is not None else V((x, 0.55, z))
            row.append((bm.verts.new(p), (1.0 - c / n, r / n)))
        grid.append(row)
    for r in range(n):
        for c in range(n):
            quad = (grid[r][c], grid[r + 1][c], grid[r + 1][c + 1], grid[r][c + 1])
            f = bm.faces.new([q[0] for q in quad])
            for loop, q in zip(f.loops, quad):
                loop[uv].uv = q[1]
    return uk.shade(uk.mesh("emblem", bm, [M["emblem"]], outward=False), 60)


def build(assets=ASSETS):
    """The rig and every part on it. Returns (rig, nodes, parts, own), own
    the parts with pictures of their own, kept out of the atlas."""
    rig = uk.Rig(MODEL)
    M = materials()
    nodes = rig.nodes()
    parts, own = {}, []

    def put(piece, ob, own_uv=False):
        uk.attach(ob, nodes[piece])
        parts.setdefault(piece, []).append(ob)
        if own_uv:
            own.append(ob)

    left = {"FootL": ea.foot(rig, M), "LegLL": ea.shin(rig, M), "LegUL": ea.thigh(rig, M), "ShoulderL": ea.pauldron(rig, M),
            "ArmUL": ea.upper_arm(rig, M), "ArmLL": ea.forearm(rig, M), "HandL": ea.hand(rig, M)}
    for piece, ob in left.items():
        mirrored = uk.mirror_x(ob, ob.name[:-2] + "_R")
        put(piece, ob)
        put(piece[:-1] + "R", mirrored)
    put("Hip", ea.hip(rig, M))
    put("Torso", ea.torso(rig, M))
    head_parts, cards, face = eh.head(rig, M, assets)
    looks(face.eyes, rig.pivot("Head"))
    head = uk.join(head_parts, "head")
    uk.project_uv(head, "proj", (FACE_BOX[0], 0.0, FACE_BOX[1]), (1, 0, 0), (0, 0, 1), (FACE_BOX[2], FACE_BOX[2]))
    put("Head", head)
    put("Head", uk.join(cards, "headcards"), own_uv=True)
    put("Sword", ea.sword(rig, M))
    capes = ea.cape(rig, M)
    decal = emblem_decal(capes, M)
    for piece, ob in capes.items():
        put(piece, ob)
    put("Cape2", decal, own_uv=True)
    return rig, nodes, parts, own


def stand(nodes):
    """The cape's pieces turned as the script's idle pose turns them, so the
    cloth's pattern is baked running on across the hinges as he stands."""
    for k, deg in ea.IDLE_TURN.items():
        nodes[k].rotation_euler.x = math.radians(deg)
    bpy.context.view_layer.update()


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--assets", default=ASSETS)
    ap.add_argument("--review", default=None)
    ap.add_argument("--rig", default=None)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--quick", action="store_true", help="review the stills only")
    ap.add_argument("--size", type=int, default=2048, help="the atlas's side")
    a = ap.parse_args(argv)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rig, nodes, parts, own = build(a.assets)
    obs = [o for ps in parts.values() for o in ps]
    print("ELSIN_TRIANGLES", uk.triangles(obs))
    for piece, ps in sorted(parts.items()):
        print("  %-10s %6d" % (piece, uk.triangles(ps)))
    if not a.no_bake:
        atlas = [o for o in obs if o not in own]
        uk.unwrap(atlas, scale={"head": 3.0})
        stand(nodes)
        uk.bake_maps(atlas, a.size, name="ARAKING")
        uk.rest(nodes, rig)
        uk.export(nodes["AraGP"], a.out)
    if a.review:
        import review
        review.run(rig, nodes, a.review, a.rig, quick=a.quick)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
