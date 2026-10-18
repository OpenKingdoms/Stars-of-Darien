"""Kirenna, the Veruna monarch (VERMAGE), remastered for Stars of Darien.

    blender -b --factory-startup -P tools/units/kirenna.py -- [--out GLB] [--assets DIR] [--review DIR] [--rig RIG]

Builds the model on the original's rig (unitkit.Rig), so the original's
script walks her, casts, builds, and turns her into a mermaid in deep
water, and the pieces its Killed script throws come off as they should.
Every part is modelled at rest around the original's pivots, with a
sturdy build: polished steel plate with a cool sheen and scales etched on
it, a gold sash from her right shoulder to her left hip, and a barbed
trident (kirenna_armour.py); a realistic woman's face from the female body
in Blender Studio's CC0 Human Base Meshes, with fin ears, pale blonde hair
drawn back into a braid and a gilded fin crest (kirenna_head.py); and the
mermaid's tail on the pieces the script shows in the water. The player's
colour is where the original has it, on the cape and the pauldrons.

The materials are glTF PBR from three pictures baked here through one UV
layout, all made from noise and numbers and the model's own shape, as
elsin.py does. The hair strands are cut out of a picture drawn here.
Nothing comes from the original's art. Exports
unity/Assets/Overrides/Units/VERMAGE.glb, or --out.

--assets is where the CC0 head bundle is kept (docs/ASSET_CREDITS.md).
--review DIR renders the model beside the original from the player's
files, for checking only, and --rig names a pose dump from the engine
(Tests/Editor/UnitRigDump.cs with OKU_RIG_UNIT=VERMAGE).
"""
import argparse
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import kirenna_armour as ka  # noqa: E402
import kirenna_head as kh  # noqa: E402
import unitkit as uk  # noqa: E402

MODEL = "VERMAGE"
OUT = os.path.join(uk.REPO, "unity", "Assets", "Overrides", "Units", MODEL + ".glb")
ASSETS = os.environ.get("OKU_UNIT_ASSETS", os.path.join(os.path.dirname(uk.REPO), "unit-assets"))
V = Vector
FACE_BOX = (-0.14, kh.EYE_Z - 0.14, 0.28)  # x and z of the eye picture's corner, and its size


# ---------------------------------------------------------------- materials

def materials():
    m = {
        "plate": uk.material("plate", (0.62, 0.65, 0.7), metal=1.0, rough=0.25),
        "etched": uk.material("etched", (0.62, 0.65, 0.7), metal=1.0, rough=0.27),
        "gold": uk.material("gold", (0.8, 0.58, 0.26), metal=1.0, rough=0.3),
        "mail": uk.material("mail", (0.42, 0.44, 0.47), metal=1.0, rough=0.45),
        "leather": uk.material("leather", (0.07, 0.045, 0.03), rough=0.6),
        "sash": uk.material("sash", (0.78, 0.5, 0.08), rough=0.5),
        "steel": uk.material("steel", (0.3, 0.33, 0.37), metal=1.0, rough=0.3),
        "teamcolor": uk.material("teamcolor", (0.8, 0.8, 0.8), rough=0.85),
        "teamcolor_lining": uk.material("teamcolor_lining", (0.4, 0.4, 0.4), rough=0.9),
        "teamcolor_enamel": uk.material("teamcolor_enamel", (0.8, 0.8, 0.8), rough=0.3),
        "goldthread": uk.material("goldthread", (0.7, 0.5, 0.2), metal=0.6, rough=0.45),
        "skin": uk.material("skin", (0.72, 0.53, 0.45), rough=0.45),
        "eye": uk.material("eye", (0.8, 0.78, 0.76), rough=0.08),
        "hair": uk.material("hair", (0.48, 0.38, 0.22), rough=0.42),
        "braid": uk.material("braid", (0.48, 0.38, 0.22), rough=0.42),
        "finweb": uk.material("finweb", (0.8, 0.6, 0.3), metal=0.9, rough=0.32),
        "finskin": uk.material("finskin", (0.74, 0.5, 0.46), rough=0.4),
        "gem": uk.material("gem", (0.2, 0.62, 0.68), rough=0.08, emit=(0.1, 0.45, 0.55), strength=0.6),
        "tailscale": uk.material("tailscale", (0.1, 0.32, 0.3), metal=0.4, rough=0.3),
        "tailfin": uk.material("tailfin", (0.2, 0.42, 0.4), rough=0.4),
    }
    mat = uk.material("strands", (1, 1, 1), rough=0.6, double=True)
    b = mat.node_tree.nodes["Principled BSDF"]
    t = mat.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = kh.strand_picture()
    mat.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    mat.node_tree.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
    mat["uk_mask"] = 0.35
    mat[uk.okpaint.GENERATED] = True
    m["strands"] = mat
    return m


# ---------------------------------------------------------------- pictures

def eye_picture(eyes, size=512):
    """Irises and pupils where the eyes look out, laid over them from the
    front through the face box: a sea blue iris, paler and greener round
    the pupil, with streaks and a dark rim, and the white a little warmer
    at its edge."""
    x0, z0, w = FACE_BOX
    k = np.linspace(0.0, 1.0, size)
    X, Z = np.meshgrid(x0 + w * k, z0 + w * k)
    rgba = np.zeros((size, size, 4), np.float32)
    rgba[..., :3] = (0.82, 0.8, 0.77)
    rgba[..., 3] = 1.0
    for c, r in eyes:
        d = np.hypot(X - c.x, Z - c.z)
        ri = r * 0.55
        a = np.arctan2(Z - c.z, X - c.x)
        streak = 0.8 + 0.2 * np.sin(a * 31.0) * np.sin(a * 11.0 + 1.0)
        iris = d < ri
        t = np.clip(d / ri, 0, 1)
        sea = np.stack([0.16 + 0.06 * t, 0.42 + 0.06 * t, 0.56 + 0.05 * t], -1)
        inner = np.clip((0.55 - t) / 0.2, 0, 1)[..., None]
        col = (sea * (1 - inner) + np.array([0.34, 0.52, 0.4]) * inner) * streak[..., None]
        rgba[iris, :3] = col[iris]
        rgba[iris & (d > ri * 0.86), :3] *= 0.38
        rgba[d < ri * 0.37, :3] = (0.01, 0.01, 0.012)
        edge = (d >= ri) & (d < r * 0.95)
        warm = np.clip((d - ri) / (r * 0.6), 0, 1)
        rgba[edge, 1] -= 0.06 * warm[edge]
        rgba[edge, 2] -= 0.07 * warm[edge]
    return uk.image("kirenna_eyes", rgba)


# ---------------------------------------------------------------- looks

def looks(eyes, hp):
    """Each material's albedo, height, roughness and metal for the bake:
    steel polished to a cool sheen, grimy in its recesses and scratched,
    some of it etched with scales; gold rubbed bright on its edges; mail,
    leather, silk and wool; fair skin with a little colour at the cheeks
    and lips, lined eyes and fine brows; and pale blonde hair. The face's
    marks are placed in the head piece's own frame (origin hp)."""
    eye_pic = eye_picture(eyes)
    eye_z = kh.EYE_Z - hp.z

    def obj(n):
        return n.coord("Object")

    def world(n):
        return n.geometry("Position")

    def sep(n, vec):
        s = n.new("ShaderNodeSeparateXYZ")
        n.link(vec, s.inputs[0])
        return s.outputs

    def scratches(n):
        return n.range(n.noise(n.scale(obj(n), (70.0, 70.0, 700.0)), 1.0, 1.0), 0.67, 0.72, 0.0, 1.0)

    def wear(n):
        edge = n.range(n.geometry("Pointiness"), 0.545, 0.6, 0.0, 1.0)
        edge = n.math("MULTIPLY", edge, n.range(n.noise(obj(n), 6.0, 3.0, 0.6), 0.4, 0.62, 0.3, 1.0))
        occ = n.new("ShaderNodeAmbientOcclusion")
        occ.only_local = True
        occ.samples = 16
        occ.inputs["Distance"].default_value = 0.06
        recess = n.range(occ.outputs["AO"], 0.9, 0.5, 0.0, 1.0)
        return edge, recess

    def scales(n, k=26.0):
        """Fish scales from the front, in rows each shifted half a scale:
        1 on a scale's lower rim, falling over it."""
        x, y, z = sep(n, world(n))
        row = n.math("FLOOR", n.math("MULTIPLY", z, k))
        u = n.math("ADD", n.math("MULTIPLY", n.math("ADD", x, n.math("MULTIPLY", y, 0.4)), k * 0.8), n.math("MULTIPLY", row, 0.5))
        arc = n.math("ABSOLUTE", n.math("SINE", n.math("MULTIPLY", u, math.pi)))
        f = n.math("FRACT", n.math("ADD", n.math("MULTIPLY", z, k), n.math("MULTIPLY", arc, 0.45)))
        return n.range(f, 0.0, 0.22, 1.0, 0.0)

    def steel_albedo(n, etched=False):
        edge, recess = wear(n)
        mottle = n.noise(obj(n), 4.0, 4.0, 0.6)
        col = n.mix(n.rgb((0.42, 0.47, 0.52)), (0.56, 0.6, 0.64), n.range(mottle, 0.35, 0.7, 0.0, 1.0))
        col = n.mix(col, (0.36, 0.45, 0.5), n.range(n.noise(obj(n), 1.5, 2.0), 0.4, 0.65, 0.0, 0.6))
        col = n.mix(col, (0.3, 0.33, 0.34), n.range(n.noise(obj(n), 9.0, 3.0, 0.65), 0.55, 0.75, 0.0, 0.5))
        col = n.mix(col, (0.12, 0.11, 0.1), n.math("MULTIPLY", recess, n.range(n.noise(obj(n), 14.0, 3.0), 0.4, 0.65, 0.45, 0.9)))
        col = n.mix(col, (0.74, 0.75, 0.77), n.math("MULTIPLY", edge, 0.45))
        col = n.mix(col, (0.36, 0.38, 0.42), n.math("MULTIPLY", scratches(n), 0.35))
        if etched:
            col = n.mix(col, (0.12, 0.12, 0.13), n.math("MULTIPLY", n.range(scales(n), 0.55, 0.95, 0.0, 1.0), 0.7))
        return n.mul(col, n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.1), 0.95))

    def steel_rough(n, base=0.2):
        edge, recess = wear(n)
        r = n.math("ADD", base, n.range(n.noise(obj(n), 4.0, 3.0, 0.6), 0.35, 0.68, 0.0, 0.2))
        r = n.math("ADD", r, n.range(n.noise(obj(n), 9.0, 3.0, 0.65), 0.55, 0.75, 0.0, 0.18))
        r = n.math("ADD", r, n.math("MULTIPLY", n.noise(obj(n), 40.0, 2.0), 0.06))
        r = n.math("ADD", r, n.math("MULTIPLY", recess, 0.4))
        return n.math("ADD", r, n.math("MULTIPLY", scratches(n), 0.08), clamp=True)

    def steel_metal(n):
        edge, recess = wear(n)
        return n.math("SUBTRACT", 1.0, n.math("MULTIPLY", recess, 0.4), clamp=True)

    def hammered(n, scale=20.0, k=1.0):
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "SMOOTH_F1"
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = scale
        dents = n.math("MULTIPLY", vor.outputs["Distance"], 0.3 * k)
        fine = n.math("MULTIPLY", n.noise(obj(n), 220.0, 2.0), 0.1 * k)
        return n.math("SUBTRACT", n.math("ADD", dents, fine), n.math("MULTIPLY", scratches(n), 0.3 * k))

    uk.look("plate", steel_albedo, hammered, lambda n: steel_rough(n, 0.24), steel_metal, bump=0.0016)
    uk.look("etched", lambda n: steel_albedo(n, True), lambda n: n.math("ADD", hammered(n, 20.0, 0.5), n.math("MULTIPLY", scales(n), 0.8)),
            lambda n: n.math("ADD", steel_rough(n, 0.22), n.math("MULTIPLY", scales(n), 0.1)), steel_metal, bump=0.003)

    def gold_albedo(n):
        edge, recess = wear(n)
        col = n.mix(n.rgb((0.7, 0.44, 0.12)), (0.86, 0.6, 0.22), n.range(n.noise(obj(n), 8.0, 3.0), 0.35, 0.7, 0.0, 1.0))
        col = n.mix(col, (0.22, 0.15, 0.07), n.math("MULTIPLY", recess, 0.7))
        return n.mix(col, (0.95, 0.8, 0.5), n.math("MULTIPLY", edge, 0.5))

    uk.look("gold", gold_albedo, lambda n: hammered(n, 40.0, 0.5),
            lambda n: n.math("ADD", 0.26, n.math("MULTIPLY", n.noise(obj(n), 12.0, 2.0), 0.16)), 1.0, bump=0.001)

    def rays(n):
        """Fine ribs running out along the crest's membrane."""
        x, y, z = sep(n, obj(n))
        a = n.math("ARCTAN2", n.math("SUBTRACT", z, eye_z + 0.02), y)
        return n.math("ABSOLUTE", n.math("SINE", n.math("MULTIPLY", a, 40.0)))

    uk.look("finweb", lambda n: n.mul(gold_albedo(n), n.math("ADD", 0.82, n.math("MULTIPLY", rays(n), 0.2))),
            rays, lambda n: n.math("ADD", 0.3, n.math("MULTIPLY", rays(n), 0.08)), 0.9, bump=0.0015)

    def rings(n):
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "F1"
        vor.inputs["Randomness"].default_value = 0.0
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = 80.0
        d = n.math("ABSOLUTE", n.math("SUBTRACT", vor.outputs["Distance"], 0.36))
        return n.range(d, 0.04, 0.11, 1.0, 0.0)

    uk.look("mail", lambda n: n.mul(n.rgb((0.42, 0.44, 0.47)), n.math("ADD", n.math("MULTIPLY", rings(n), 0.75), 0.25)),
            rings, lambda n: n.math("SUBTRACT", 0.6, n.math("MULTIPLY", rings(n), 0.25)), 0.85, bump=0.004, ao=1.4)

    def leather_albedo(n):
        edge = n.range(n.geometry("Pointiness"), 0.5, 0.58, 0.0, 1.0)
        col = n.mul(n.rgb((0.07, 0.045, 0.03)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 40.0, 3.0), 0.5), 0.75))
        return n.mix(col, (0.16, 0.11, 0.07), n.math("MULTIPLY", edge, 0.5))

    uk.look("leather", leather_albedo, lambda n: n.math("ADD", n.noise(obj(n), 90.0, 3.0), n.math("MULTIPLY", n.noise(obj(n), 12.0, 2.0), 0.6)),
            lambda n: n.math("ADD", 0.5, n.math("MULTIPLY", n.noise(obj(n), 20.0, 2.0), 0.25)), 0.0, bump=0.0015)

    def weave(n, k=70.0):
        return n.math("ADD", n.wave(world(n), k, "X"), n.wave(world(n), k, "Z"))

    def silk(n):
        """Gold silk with a soft sheen across its folds, smooth, as a woven
        grain is finer than the atlas holds."""
        sheen = n.range(n.noise(n.scale(world(n), (1.0, 1.0, 3.0)), 4.0, 2.0), 0.35, 0.7, 0.0, 1.0)
        col = n.mix(n.rgb((0.68, 0.42, 0.07)), (0.86, 0.6, 0.15), sheen)
        col = n.mix(col, (0.56, 0.34, 0.06), n.range(n.noise(world(n), 3.0, 1.0), 0.4, 0.7, 0.0, 0.18))
        return n.mul(col, 0.98)

    uk.look("sash", silk, None, lambda n: n.math("ADD", 0.42, n.math("MULTIPLY", n.noise(world(n), 8.0, 2.0), 0.12)), 0.0, ao=1.3)

    def steel_dark(n):
        edge, recess = wear(n)
        col = n.mix(n.rgb((0.26, 0.29, 0.34)), (0.36, 0.4, 0.46), n.range(n.noise(obj(n), 6.0, 3.0), 0.35, 0.7, 0.0, 1.0))
        col = n.mix(col, (0.08, 0.08, 0.09), n.math("MULTIPLY", recess, 0.6))
        return n.mix(col, (0.7, 0.72, 0.76), n.math("MULTIPLY", edge, 0.5))

    uk.look("steel", steel_dark, lambda n: hammered(n, 30.0, 0.6), lambda n: steel_rough(n, 0.24), 1.0, bump=0.0012)

    def cloth(base):
        def f(n):
            k = n.math("ADD", n.math("MULTIPLY", weave(n), 0.05), 0.9)
            k = n.math("ADD", k, n.math("MULTIPLY", n.noise(world(n), 5.0, 2.0), 0.06))
            return n.mul(n.rgb((base, base, base)), k)
        return f

    uk.look("teamcolor", cloth(0.8), lambda n: n.math("ADD", weave(n), n.math("MULTIPLY", n.noise(world(n), 3.0, 1.0), 0.8)), 0.85, 0.0, bump=0.0012, ao=1.6)
    uk.look("teamcolor_lining", cloth(0.4), weave, 0.9, 0.0, bump=0.001)
    uk.look("teamcolor_enamel", lambda n: n.mul(n.rgb((0.84, 0.84, 0.84)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 7.0, 2.0), 0.1), 0.88)),
            lambda n: hammered(n, 20.0, 0.5), lambda n: n.math("ADD", 0.26, n.math("MULTIPLY", n.noise(obj(n), 9.0, 2.0), 0.12)), 0.0, bump=0.0015)

    def thread(n):
        return n.math("ADD", n.wave(n.scale(world(n), (1.0, 1.0, 1.0)), 120.0, "Z"), n.math("MULTIPLY", n.wave(world(n), 40.0, "X", 2.0), 0.5))

    uk.look("goldthread", lambda n: n.mul(n.rgb((0.72, 0.52, 0.2)), n.math("ADD", n.math("MULTIPLY", thread(n), 0.18), 0.82)),
            thread, 0.45, 0.6, bump=0.0012)

    # ---- the face
    def near(n, centre, radius, squash=(1.0, 1.0, 1.0)):
        d = n.new("ShaderNodeVectorMath")
        d.operation = "DISTANCE"
        n.link(n.scale(obj(n), squash), d.inputs[0])
        d.inputs[1].default_value = tuple(c * s for c, s in zip(centre, squash))
        return n.range(d.outputs["Value"], radius, 0.0, 0.0, 1.0)

    sockets = [tuple(c - hp) for c, r in eyes]
    er = eyes[0][1]
    s = kh.SCALE
    ey = sockets[0][1]
    mouth = (0.0, ey - 0.033 * s, eye_z - 0.0712 * s)
    cheeks = [(sx * 0.052 * s, ey + 0.004 * s, eye_z - 0.03 * s) for sx in (-1, 1)]

    def lash(n, c):
        """Dark lashes along the lids' margins, where the skin meets the
        eye, full along the upper lid and swept out at its outer end, faint
        along the lower."""
        x, y, z = sep(n, obj(n))
        d = n.new("ShaderNodeVectorMath")
        d.operation = "DISTANCE"
        n.link(obj(n), d.inputs[0])
        d.inputs[1].default_value = c
        rim_ = n.range(d.outputs["Value"], er * 1.34, er * 1.12, 0.0, 1.0)
        dz = n.math("SUBTRACT", z, c[2])
        dx = n.math("MULTIPLY", n.math("SUBTRACT", x, c[0]), math.copysign(1.0, c[0]))
        upper = n.math("MAXIMUM", n.range(dz, -0.1 * er, 0.25 * er, 0.0, 1.0), n.range(dx, 0.7 * er, 1.0 * er, 0.0, 1.0))
        lid = n.math("MULTIPLY", rim_, n.math("ADD", 0.35, n.math("MULTIPLY", upper, 0.65)))
        # the wing: a short stroke rising out from the outer corner, tapering
        u = n.math("SUBTRACT", dx, 1.0 * er)
        along = n.math("MULTIPLY", n.range(u, -0.4 * er, -0.1 * er, 0.0, 1.0), n.range(u, 0.5 * er, 0.25 * er, 0.0, 1.0))
        line = n.math("ABSOLUTE", n.math("SUBTRACT", dz, n.math("ADD", 0.12 * er, n.math("MULTIPLY", u, 0.42))))
        width = n.math("MAXIMUM", n.math("SUBTRACT", 0.2 * er, n.math("MULTIPLY", u, 0.3)), 0.002 * er)
        wing = n.math("MULTIPLY", along, n.range(n.math("DIVIDE", line, width), 1.0, 0.0, 0.0, 1.0))
        front = n.range(y, c[1] + 0.6 * er, c[1], 0.0, 1.0)
        return n.math("MAXIMUM", lid, n.math("MULTIPLY", wing, front))

    def brows(n):
        """Fine brows, darker than her hair: from the inner end each rises
        in a soft arch to a peak two thirds out and tapers to its tail."""
        x, y, z = sep(n, obj(n))
        xm = n.math("DIVIDE", n.math("ABSOLUTE", x), s)
        zm = n.math("DIVIDE", n.math("SUBTRACT", z, eye_z), s)
        L = 0.044
        u = n.math("SUBTRACT", xm, 0.017)
        t = n.math("DIVIDE", u, L, clamp=True)
        inside = n.math("MULTIPLY", n.range(u, -0.002, 0.002, 0.0, 1.0), n.range(u, L + 0.001, L - 0.004, 0.0, 1.0))
        zc = n.math("ADD", 0.0195, n.math("MULTIPLY", n.math("SINE", n.math("MULTIPLY", t, math.pi * 0.78)), 0.0075))
        th = n.math("SUBTRACT", 0.0042, n.math("MULTIPLY", n.math("POWER", t, 1.3), 0.0018))
        d = n.math("DIVIDE", n.math("ABSOLUTE", n.math("SUBTRACT", zm, zc)), th)
        band = n.range(d, 1.0, 0.45, 0.0, 1.0)
        front = n.range(y, ey + 0.02 * s, ey, 0.0, 1.0)
        k = n.math("MULTIPLY", n.math("MULTIPLY", band, inside), front)
        return n.math("MULTIPLY", k, n.range(n.noise(n.scale(obj(n), (500.0, 500.0, 80.0)), 1.0, 2.0), 0.3, 0.7, 0.65, 1.0))

    def skin_albedo(n):
        """Fair skin warmed with colour where blood shows, at the cheeks, the
        nose and the ears, shaded under the cheekbones and lit along the
        bridge of the nose and the cheekbones; rose lips, a soft warm shadow
        on the lids, dark lashes and brows."""
        col = n.mul(n.rgb((0.58, 0.39, 0.3)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.06), 0.97))
        col = n.mix(col, (0.56, 0.34, 0.27), n.range(n.noise(obj(n), 7.0, 2.0), 0.4, 0.7, 0.0, 0.25))
        blush = n.math("MAXIMUM", near(n, cheeks[0], 0.11), near(n, cheeks[1], 0.11))
        col = n.mix(col, (0.62, 0.26, 0.24), n.math("MULTIPLY", blush, 0.42))
        col = n.mix(col, (0.6, 0.3, 0.27), n.math("MULTIPLY", near(n, (0.0, ey - 0.1 * s, eye_z - 0.042 * s), 0.05), 0.3))
        for sx in (-1, 1):
            hollow = near(n, (sx * 0.05 * s, ey + 0.01 * s, eye_z - 0.058 * s), 0.09, (1.0, 1.0, 1.8))
            col = n.mix(col, (0.4, 0.25, 0.2), n.math("MULTIPLY", hollow, 0.3))
            high = near(n, (sx * 0.048 * s, ey - 0.006 * s, eye_z - 0.022 * s), 0.07, (1.0, 1.0, 2.2))
            col = n.mix(col, (0.68, 0.5, 0.4), n.math("MULTIPLY", high, 0.3))
            ear = near(n, (sx * 0.075 * s, ey + 0.07 * s, eye_z - 0.0 * s), 0.12)
            col = n.mix(col, (0.6, 0.3, 0.26), n.math("MULTIPLY", ear, 0.3))
        col = n.mix(col, (0.68, 0.5, 0.4), n.math("MULTIPLY", near(n, (0.0, ey - 0.07 * s, eye_z - 0.01 * s), 0.05, (2.5, 1.0, 0.7)), 0.3))
        lid = n.math("MAXIMUM", near(n, (sockets[0][0], sockets[0][1], sockets[0][2] + er * 0.9), er * 1.7, (1.0, 1.0, 1.6)),
                     near(n, (sockets[1][0], sockets[1][1], sockets[1][2] + er * 0.9), er * 1.7, (1.0, 1.0, 1.6)))
        col = n.mix(col, (0.38, 0.23, 0.2), n.math("MULTIPLY", lid, 0.38))
        lips = n.range(near(n, mouth, 0.066, (1.0, 1.0, 2.1)), 0.0, 0.45, 0.0, 1.0)
        col = n.mix(col, (0.46, 0.11, 0.13), n.math("MULTIPLY", lips, 0.92))
        col = n.mix(col, (0.012, 0.009, 0.008), n.math("MAXIMUM", lash(n, sockets[0]), lash(n, sockets[1])))
        return n.mix(col, (0.07, 0.045, 0.03), n.math("MULTIPLY", brows(n), 0.92))

    def skin_height(n):
        vor = n.new("ShaderNodeTexVoronoi")
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = 800.0
        pores = n.range(vor.outputs["Distance"], 0.0, 0.35, -1.0, 0.0)
        return n.math("ADD", n.math("MULTIPLY", pores, 0.06), n.math("MULTIPLY", n.noise(obj(n), 60.0, 2.0), 0.12))

    def skin_rough(n):
        r = n.math("ADD", 0.46, n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.1))
        r = n.math("SUBTRACT", r, n.math("MULTIPLY", near(n, (0.0, ey - 0.07 * s, eye_z - 0.02 * s), 0.07), 0.08))
        return n.math("SUBTRACT", r, n.math("MULTIPLY", near(n, mouth, 0.05, (1.0, 1.0, 2.6)), 0.14))

    uk.look("skin", skin_albedo, skin_height, skin_rough, 0.0, bump=0.0008, ao=1.0)
    uk.look("eye", lambda n: n.image(eye_pic, "proj")[0], None, 0.06, 0.0, ao=0.3)

    def fin_albedo(n):
        """Fair skin turning coral toward the fin's edge, its rays darker."""
        x, y, z = sep(n, obj(n))
        out = n.range(y, ey + 0.32, ey + 0.46, 0.0, 1.0)
        col = n.mix(n.rgb((0.72, 0.52, 0.46)), (0.78, 0.4, 0.36), out)
        return n.mul(col, n.math("ADD", 0.9, n.math("MULTIPLY", n.noise(n.scale(obj(n), (40.0, 4.0, 40.0)), 3.0, 2.0), 0.15)))

    uk.look("finskin", fin_albedo, lambda n: n.noise(n.scale(obj(n), (60.0, 6.0, 60.0)), 3.0, 2.0), 0.38, 0.0, bump=0.0008, ao=0.6)
    uk.look("gem", lambda n: n.mix(n.rgb((0.12, 0.45, 0.52)), (0.4, 0.8, 0.82), n.range(n.geometry("Pointiness"), 0.5, 0.6, 0.0, 1.0)), None, 0.06, 0.0, ao=0.2)

    def hair_albedo(n, stretch):
        streak = n.noise(n.scale(obj(n), stretch), 1.0, 2.0)
        col = n.mix(n.rgb((0.48, 0.35, 0.16)), (0.8, 0.66, 0.4), n.range(streak, 0.3, 0.7, 0.0, 1.0))
        return n.mix(col, (0.3, 0.22, 0.11), n.range(n.noise(obj(n), 9.0, 2.0), 0.55, 0.75, 0.0, 0.35))

    def hair_height(n, stretch):
        return n.math("ADD", n.noise(n.scale(obj(n), stretch), 1.0, 2.0), n.math("MULTIPLY", n.noise(obj(n), 18.0, 2.0), 0.4))

    uk.look("hair", lambda n: hair_albedo(n, (220.0, 8.0, 220.0)), lambda n: hair_height(n, (220.0, 8.0, 220.0)), 0.4, 0.0, bump=0.0008)
    uk.look("braid", lambda n: hair_albedo(n, (200.0, 200.0, 10.0)), lambda n: hair_height(n, (200.0, 200.0, 10.0)), 0.42, 0.0, bump=0.0012, ao=1.4)

    # ---- the mermaid's tail
    def tail_albedo(n):
        x, y, z = sep(n, obj(n))
        belly = n.range(y, -0.05, -0.2, 0.0, 1.0)
        col = n.mix(n.rgb((0.025, 0.13, 0.15)), (0.08, 0.3, 0.28), n.range(n.noise(obj(n), 3.0, 2.0), 0.3, 0.7, 0.0, 1.0))
        col = n.mix(col, (0.32, 0.42, 0.36), n.math("MULTIPLY", belly, 0.5))
        return n.mix(col, (0.02, 0.08, 0.09), n.math("MULTIPLY", n.range(scales(n, 34.0), 0.5, 1.0, 0.0, 1.0), 0.6))

    uk.look("tailscale", tail_albedo, lambda n: scales(n, 34.0), lambda n: n.math("ADD", 0.26, n.math("MULTIPLY", scales(n, 34.0), 0.15)), 0.35,
            bump=0.004)

    def fin_rays(n):
        x, y, z = sep(n, obj(n))
        return n.math("ABSOLUTE", n.math("SINE", n.math("MULTIPLY", n.math("ARCTAN2", x, n.math("ADD", z, 0.3)), 30.0)))

    uk.look("tailfin", lambda n: n.mix(n.rgb((0.05, 0.22, 0.24)), (0.22, 0.42, 0.4), n.math("MULTIPLY", fin_rays(n), 0.5)), fin_rays, 0.4, 0.0, bump=0.0015)


# ---------------------------------------------------------------- assembly

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

    left = {"FootL": ka.foot(rig, M), "LegLL": ka.shin(rig, M), "LegUL": ka.thigh(rig, M), "ShoulderL": ka.pauldron(rig, M),
            "ArmUL": ka.upper_arm(rig, M), "ArmLL": ka.forearm(rig, M)}
    for piece, ob in left.items():
        mirrored = uk.mirror_x(ob, ob.name[:-2] + "_R")
        put(piece, ob)
        put(piece[:-1] + "R", mirrored)
    put("HandL", ka.open_hand(rig, M))
    put("HandR", ka.grip_hand(rig, M))
    put("Trident", ka.trident(rig, M))
    put("Hip", ka.hip(rig, M))
    put("Torso", ka.torso(rig, M))
    for piece, ob in ka.cape(rig, M).items():
        put(piece, ob)
    for piece, ob in ka.tail(rig, M).items():
        put(piece, ob)
    head_parts, cards, braid, face = kh.head(rig, M, assets)
    looks(face.eyes, rig.pivot("Head"))
    head = uk.join(head_parts, "head")
    uk.project_uv(head, "proj", (FACE_BOX[0], 0.0, FACE_BOX[1]), (1, 0, 0), (0, 0, 1), (FACE_BOX[2], FACE_BOX[2]))
    put("Head", head)
    put("Head", uk.join(cards, "headcards"), own_uv=True)
    for piece, obs in braid.items():
        if obs:
            put(piece, uk.join(obs, "braid_" + piece))
    return rig, nodes, parts, own


def stand(nodes):
    """The cape's and the braid's pieces turned as the script's idle pose
    turns them, so their patterns are baked running on across the hinges
    as she stands."""
    for k, deg in ka.IDLE.items():
        nodes[k].rotation_euler = [math.radians(a) for a in deg]
    bpy.context.view_layer.update()


VIEWS = {
    "front": dict(yaw=0, pitch=5, dist=11.0, lens=55, target=V((0.0, 0.0, 2.3))),
    "low34": dict(yaw=38, pitch=2, dist=11.0, lens=55, target=V((0.0, 0.0, 2.3))),
    "classic": dict(yaw=0, pitch=uk.CLASSIC_PITCH, dist=40, ortho=6.0, target=V((0.0, 0.0, 2.0))),
    "face": dict(yaw=14, pitch=4, dist=2.2, lens=70, target=V((0.0, -0.05, kh.EYE_Z - 0.02))),
    "back34": dict(yaw=150, pitch=8, dist=11.0, lens=55, target=V((0.0, 0.0, 2.3))),
}
ANIMS = (("walk", 0, 40, 6), ("toswim", 0, 75, 6), ("dive", 0, 75, 6), ("startbuild", 0, 45, 6))


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
    print("KIRENNA_TRIANGLES", uk.triangles(obs))
    for piece, ps in sorted(parts.items()):
        print("  %-12s %6d" % (piece, uk.triangles(ps)))
    if not a.no_bake:
        atlas = [o for o in obs if o not in own]
        uk.unwrap(atlas, scale={"head": 3.0})
        print("KIRENNA_SLIVERS", uk.tuck_slivers(atlas, a.size))
        stand(nodes)
        uk.bake_maps(atlas, a.size, name=MODEL)
        uk.rest(nodes, rig)
        uk.export(nodes[rig.pieces[0].name], a.out)
    if a.review:
        import review
        review.run(rig, nodes, a.review, a.rig, quick=a.quick, views=VIEWS, anims=ANIMS, target=V((0.0, 0.0, 2.3)), dist=11.0)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
