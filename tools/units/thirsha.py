"""Thirsha, the Zhon monarch (ZONHUNT), remastered for Stars of Darien.

    blender -b --factory-startup -P tools/units/thirsha.py -- [--out GLB] [--assets DIR] [--review DIR] [--rig RIG]

Builds the model on the original's rig (unitkit.Rig), so the original's
script stands her, flies her, fights, casts and builds with her, and swings
her wings and hair. Every part is modelled at rest around the original's
pivots, with a sturdy build: blackened plate, black leather and an
oxblood belt (thirsha_armour.py), a striking woman's face from the female
body in Blender Studio's CC0 Human Base Meshes, pale as a vampire's, with
pointed ears, smoky eyes, a wine lip and sleek black hair
(thirsha_head.py), a bat's wings with fingers that fan as the script folds
them, a barbed spear and a round shield. The player's colour is where the
original has it: the wings, the shoulders, a ring round the shield, and a
panel at the waist.

The materials are glTF PBR from three pictures baked here through one UV
layout, all made from noise and numbers and the model's own shape, as
elsin.py does. The hair strands are cut out of a picture drawn here.
Nothing comes from the original's art. Exports
unity/Assets/Overrides/Units/ZONHUNT.glb, or --out.

--assets is where the CC0 head bundle is kept (docs/ASSET_CREDITS.md).
--review DIR renders the model beside the original from the player's
files, for checking only, and --rig names a pose dump from the engine
(Tests/Editor/UnitRigDump.cs with OKU_RIG_UNIT=ZONHUNT).
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
import thirsha_armour as ta  # noqa: E402
import thirsha_head as th  # noqa: E402
import unitkit as uk  # noqa: E402

MODEL = "ZONHUNT"
OUT = os.path.join(uk.REPO, "unity", "Assets", "Overrides", "Units", MODEL + ".glb")
ASSETS = os.environ.get("OKU_UNIT_ASSETS", os.path.join(os.path.dirname(uk.REPO), "unit-assets"))
V = Vector
FACE_BOX = (-0.13, th.EYE_Z - 0.13, 0.26)  # x and z of the eye picture's corner, and its size


# ---------------------------------------------------------------- materials

def materials():
    m = {
        "plate": uk.material("plate", (0.022, 0.022, 0.026), metal=0.15, rough=0.3),
        "trim": uk.material("trim", (0.12, 0.12, 0.13), metal=1.0, rough=0.38),
        "steel": uk.material("steel", (0.5, 0.5, 0.52), metal=1.0, rough=0.26),
        "mail": uk.material("mail", (0.07, 0.07, 0.08), metal=1.0, rough=0.6),
        "leather": uk.material("leather", (0.024, 0.021, 0.022), rough=0.55),
        "oxblood": uk.material("oxblood", (0.07, 0.012, 0.015), rough=0.5),
        "wood": uk.material("wood", (0.035, 0.025, 0.022), rough=0.5),
        "wingskin": uk.material("wingskin", (0.03, 0.027, 0.03), rough=0.5),
        "claw": uk.material("claw", (0.03, 0.026, 0.024), rough=0.25),
        "teamcolor": uk.material("teamcolor", (0.5, 0.5, 0.5), rough=0.88),
        "teamcolor_lining": uk.material("teamcolor_lining", (0.2, 0.2, 0.2), rough=0.9),
        "teamcolor_enamel": uk.material("teamcolor_enamel", (0.55, 0.55, 0.55), rough=0.3),
        "teamcolor_membrane": uk.material("teamcolor_membrane", (0.12, 0.12, 0.12), rough=0.55),
        "skin": uk.material("skin", (0.84, 0.74, 0.72), rough=0.45),
        "eye": uk.material("eye", (0.8, 0.78, 0.76), rough=0.08),
        "hair": uk.material("hair", (0.026, 0.026, 0.032), rough=0.32),
    }
    mat = uk.material("strands", (1, 1, 1), rough=0.5, double=True)
    b = mat.node_tree.nodes["Principled BSDF"]
    t = mat.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = th.strand_picture()
    mat.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    mat.node_tree.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
    mat["uk_mask"] = 0.35
    mat[uk.okpaint.GENERATED] = True
    m["strands"] = mat
    return m


# ---------------------------------------------------------------- pictures

def eye_picture(eyes, size=512):
    """Irises and pupils where the eyes look out, laid over them from the
    front through the face box: a pale silver-grey iris with fine streaks
    and a dark rim, a dark pupil, the white soft and cool, a little rosy at
    its corners."""
    x0, z0, w = FACE_BOX
    k = np.linspace(0.0, 1.0, size)
    X, Z = np.meshgrid(x0 + w * k, z0 + w * k)
    rgba = np.zeros((size, size, 4), np.float32)
    rgba[..., :3] = (0.68, 0.68, 0.7)
    rgba[..., 3] = 1.0
    for c, r in eyes:
        d = np.hypot(X - c.x, Z - c.z)
        ri = r * 0.47
        a = np.arctan2(Z - c.z, X - c.x)
        streak = 0.8 + 0.2 * np.sin(a * 37.0) * np.sin(a * 11.0 + 1.0)
        iris = d < ri
        t = np.clip(d / ri, 0, 1)
        col = np.stack([0.3 + 0.16 * t, 0.33 + 0.16 * t, 0.37 + 0.15 * t], -1) * streak[..., None]
        col[..., 0] += 0.12 * np.exp(-((t - 0.45) / 0.12) ** 2)      # a warm ring round the pupil
        col[..., 1] += 0.06 * np.exp(-((t - 0.45) / 0.12) ** 2)
        rgba[iris, :3] = col[iris]
        rgba[iris & (d > ri * 0.82), :3] *= 0.3
        rgba[d < ri * 0.36, :3] = (0.01, 0.009, 0.01)
        edge = (d >= ri) & (d < r * 0.98)
        corner = np.clip((np.abs(X - c.x) - ri) / (r * 0.6), 0, 1)
        rgba[edge, 1] -= 0.03 * corner[edge]
        rgba[edge, 2] -= 0.02 * corner[edge]
    return uk.image("thirsha_eyes", rgba)


# ---------------------------------------------------------------- looks

def looks(eyes, hp):
    """Each material's albedo, height, roughness and metal for the bake:
    blackened plate worn to gunmetal on its edges, grimy in its recesses,
    hammered and scratched. Dark mail rings, black and oxblood leather with
    a grain, dark wood, the wings' skin wrinkled and veined, and black hair
    in strands. The skin is pale and cool, with a rose flush over high
    cheekbones and shade under them, smoky eyes lined and winged, arched
    brows and a deep wine lip.
    The face's marks are placed in the head piece's own frame (origin hp)."""
    eye_pic = eye_picture(eyes)

    def obj(n):
        return n.coord("Object")

    def world(n):
        return n.geometry("Position")

    def scratches(n):
        return n.range(n.noise(n.scale(obj(n), (60.0, 60.0, 600.0)), 1.0, 1.0), 0.66, 0.72, 0.0, 1.0)

    def wear(n):
        """Masks for old blackening: the edges rubbed through to the steel,
        and the recesses where grime and rust sit."""
        edge = n.range(n.geometry("Pointiness"), 0.545, 0.6, 0.0, 1.0)
        edge = n.math("MULTIPLY", edge, n.range(n.noise(obj(n), 6.0, 3.0, 0.6), 0.4, 0.62, 0.2, 1.0))
        occ = n.new("ShaderNodeAmbientOcclusion")
        occ.only_local = True
        occ.samples = 16
        occ.inputs["Distance"].default_value = 0.06
        recess = n.range(occ.outputs["AO"], 0.9, 0.5, 0.0, 1.0)
        return edge, recess

    def plate_albedo(n):
        edge, recess = wear(n)
        mottle = n.noise(obj(n), 5.0, 4.0, 0.6)
        col = n.mix(n.rgb((0.02, 0.02, 0.024)), (0.038, 0.037, 0.042), n.range(mottle, 0.4, 0.7, 0.0, 0.7))
        col = n.mix(col, (0.045, 0.026, 0.018), n.math("MULTIPLY", recess, n.range(n.noise(obj(n), 14.0, 3.0), 0.45, 0.65, 0.1, 0.5)))
        col = n.mix(col, (0.2, 0.2, 0.215), n.math("MULTIPLY", edge, 0.45))
        col = n.mix(col, (0.12, 0.12, 0.13), n.math("MULTIPLY", scratches(n), 0.15))
        return n.mul(col, n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.16), 0.92))

    def plate_rough(n):
        edge, recess = wear(n)
        r = n.math("ADD", 0.26, n.range(n.noise(obj(n), 4.0, 3.0, 0.6), 0.35, 0.68, 0.0, 0.18))
        r = n.math("ADD", r, n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.08))
        r = n.math("ADD", r, n.math("MULTIPLY", recess, 0.35))
        return n.math("SUBTRACT", r, n.math("MULTIPLY", edge, 0.05), clamp=True)

    def plate_metal(n):
        edge, recess = wear(n)
        m_ = n.math("ADD", 0.12, n.math("MULTIPLY", edge, 0.8))
        m_ = n.math("ADD", m_, n.math("MULTIPLY", scratches(n), 0.25))
        return n.math("SUBTRACT", m_, n.math("MULTIPLY", recess, 0.1), clamp=True)

    def hammered(n, scale=24.0, k=1.0):
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "SMOOTH_F1"
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = scale
        dents = n.math("MULTIPLY", vor.outputs["Distance"], 0.5 * k)
        fine = n.math("MULTIPLY", n.noise(obj(n), 220.0, 2.0), 0.15 * k)
        return n.math("SUBTRACT", n.math("ADD", dents, fine), n.math("MULTIPLY", scratches(n), 0.3 * k))

    uk.look("plate", plate_albedo, hammered, plate_rough, plate_metal, bump=0.0022)
    uk.look("trim", lambda n: n.mul(n.rgb((0.12, 0.12, 0.13)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 20.0, 2.0), 0.3), 0.75)),
            lambda n: hammered(n, 40.0, 0.5), lambda n: n.math("ADD", 0.3, n.math("MULTIPLY", n.noise(obj(n), 12.0, 2.0), 0.2)), 1.0, bump=0.0015)

    def rings(n):
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "F1"
        vor.inputs["Randomness"].default_value = 0.0
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = 75.0
        d = n.math("ABSOLUTE", n.math("SUBTRACT", vor.outputs["Distance"], 0.36))
        return n.range(d, 0.04, 0.11, 1.0, 0.0)

    uk.look("mail", lambda n: n.mul(n.rgb((0.075, 0.075, 0.085)), n.math("ADD", n.math("MULTIPLY", rings(n), 0.8), 0.2)),
            rings, lambda n: n.math("SUBTRACT", 0.72, n.math("MULTIPLY", rings(n), 0.2)), 0.65, bump=0.004, ao=1.4)

    def brushed(n, k):
        return n.math("ADD", n.math("MULTIPLY", n.noise(n.scale(obj(n), (25.0, 0.6, 25.0)), 4.0, 2.0), k), 1.0 - k)

    def steel_height(n):
        sep = n.new("ShaderNodeSeparateXYZ")
        n.link(obj(n), sep.inputs[0])
        rib = n.range(n.math("ABSOLUTE", sep.outputs["X"]), 0.0, 0.012, 0.6, 0.0)
        return n.math("ADD", rib, n.math("MULTIPLY", n.noise(n.scale(obj(n), (40.0, 1.0, 40.0)), 8.0, 2.0), 0.08))

    uk.look("steel", lambda n: n.mul(n.rgb((0.5, 0.5, 0.52)), brushed(n, 0.14)), steel_height,
            lambda n: n.math("ADD", 0.16, n.math("MULTIPLY", n.noise(n.scale(obj(n), (30.0, 1.0, 30.0)), 6.0, 2.0), 0.12)), 1.0, bump=0.005)

    def leather_albedo(base, worn):
        def f(n):
            edge = n.range(n.geometry("Pointiness"), 0.5, 0.58, 0.0, 1.0)
            col = n.mul(n.rgb(base), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 40.0, 3.0), 0.5), 0.75))
            return n.mix(col, worn, n.math("MULTIPLY", edge, 0.5))
        return f

    def leather_height(n):
        return n.math("ADD", n.noise(obj(n), 90.0, 3.0), n.math("MULTIPLY", n.noise(obj(n), 12.0, 2.0), 0.6))

    def leather_rough(n):
        return n.math("ADD", 0.45, n.math("MULTIPLY", n.noise(obj(n), 20.0, 2.0), 0.25))

    uk.look("leather", leather_albedo((0.024, 0.021, 0.022), (0.07, 0.062, 0.06)), leather_height, leather_rough, 0.0, bump=0.0015)
    uk.look("oxblood", leather_albedo((0.075, 0.013, 0.016), (0.16, 0.05, 0.05)), leather_height, leather_rough, 0.0, bump=0.0015)

    def grain(n):
        return n.wave(n.scale(obj(n), (30.0, 1.0, 30.0)), 6.0, "Y", 6.0)

    uk.look("wood", lambda n: n.mul(n.rgb((0.035, 0.025, 0.022)), n.math("ADD", n.math("MULTIPLY", grain(n), 0.35), 0.75)),
            grain, lambda n: n.math("ADD", 0.5, n.math("MULTIPLY", grain(n), 0.15)), 0.0, bump=0.0012)

    def wrinkles(n):
        return n.math("ADD", n.noise(n.scale(obj(n), (8.0, 30.0, 8.0)), 20.0, 4.0, 0.7), n.math("MULTIPLY", n.noise(obj(n), 120.0, 2.0), 0.4))

    uk.look("wingskin", lambda n: n.mul(n.rgb((0.03, 0.027, 0.03)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 15.0, 3.0), 0.5), 0.75)),
            wrinkles, lambda n: n.math("ADD", 0.42, n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.2)), 0.0, bump=0.002, ao=1.2)
    uk.look("claw", lambda n: n.mix(n.rgb((0.035, 0.03, 0.026)), (0.2, 0.18, 0.15), n.range(n.noise(obj(n), 30.0, 2.0), 0.5, 0.8, 0.0, 0.6)),
            None, 0.25, 0.0)

    def veins(n):
        """Veins branching through the wing's skin, and fine creases."""
        big = n.math("ABSOLUTE", n.math("SUBTRACT", n.noise(world(n), 2.2, 3.0, 0.55), 0.5))
        fine = n.math("ABSOLUTE", n.math("SUBTRACT", n.noise(world(n), 7.0, 2.0, 0.5), 0.5))
        v = n.math("MAXIMUM", n.range(big, 0.025, 0.0, 0.0, 1.0), n.math("MULTIPLY", n.range(fine, 0.02, 0.0, 0.0, 1.0), 0.5))
        return n.math("MAXIMUM", v, n.math("MULTIPLY", n.range(n.noise(world(n), 40.0, 3.0), 0.5, 0.7, 0.0, 1.0), 0.25))

    def membrane_albedo(n):
        mottle = n.noise(world(n), 3.0, 3.0, 0.6)
        col = n.mul(n.rgb((0.12, 0.12, 0.12)), n.math("ADD", n.math("MULTIPLY", mottle, 0.4), 0.78))
        return n.mix(col, (0.03, 0.027, 0.028), n.math("MULTIPLY", veins(n), 0.6))

    uk.look("teamcolor_membrane", membrane_albedo, veins, lambda n: n.math("ADD", 0.5, n.math("MULTIPLY", veins(n), 0.15)), 0.0, bump=0.0015, ao=0.8)

    def weave(n):
        return n.math("ADD", n.wave(world(n), 70.0, "X"), n.wave(world(n), 70.0, "Z"))

    def cloth(base):
        def f(n):
            k = n.math("ADD", n.math("MULTIPLY", weave(n), 0.05), 0.9)
            k = n.math("ADD", k, n.math("MULTIPLY", n.noise(world(n), 5.0, 2.0), 0.06))
            return n.mul(n.rgb((base, base, base)), k)
        return f

    uk.look("teamcolor", cloth(0.5), lambda n: n.math("ADD", weave(n), n.math("MULTIPLY", n.noise(world(n), 3.0, 1.0), 0.8)), 0.88, 0.0, bump=0.0012, ao=1.6)
    uk.look("teamcolor_lining", cloth(0.2), weave, 0.9, 0.0, bump=0.001)
    uk.look("teamcolor_enamel", lambda n: n.mul(n.rgb((0.56, 0.56, 0.56)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 7.0, 2.0), 0.1), 0.88)),
            lambda n: hammered(n, 20.0, 0.6), lambda n: n.math("ADD", 0.28, n.math("MULTIPLY", n.noise(obj(n), 9.0, 2.0), 0.14)), 0.0, bump=0.002)

    s = th.SCALE
    socks = [V(c - hp) for c, r in eyes]
    ex = abs(socks[0].x) / s          # the eyes' half spacing, metres
    ey, ez = socks[0].y, socks[0].z   # their depth and height in the head's frame

    def xyz(n):
        sep = n.new("ShaderNodeSeparateXYZ")
        n.link(obj(n), sep.inputs[0])
        return sep.outputs["X"], sep.outputs["Y"], sep.outputs["Z"]

    def ell(n, P, cx, cz, a, b, ang=0.0, b_low=None):
        """How far a point lies out of an ellipse on the face seen from the
        front, 1 on its edge. Its centre, half axes and turn are in metres
        from between the eyes, and b_low gives it a different depth below."""
        X, _, Z = P
        dx = n.math("SUBTRACT", X, cx * s)
        dz = n.math("SUBTRACT", Z, ez + cz * s)
        ca, sa = math.cos(ang), math.sin(ang)
        u = n.math("ADD", n.math("MULTIPLY", dx, ca / (a * s)), n.math("MULTIPLY", dz, sa / (a * s)))
        v = n.math("SUBTRACT", n.math("MULTIPLY", dz, ca), n.math("MULTIPLY", dx, sa))
        if b_low is None:
            v = n.math("MULTIPLY", v, 1.0 / (b * s))
        else:
            below = n.math("LESS_THAN", v, 0.0)
            v = n.math("MULTIPLY", v, n.math("ADD", 1.0 / (b * s), n.math("MULTIPLY", below, 1.0 / (b_low * s) - 1.0 / (b * s))))
        return n.math("SQRT", n.math("ADD", n.math("MULTIPLY", u, u), n.math("MULTIPLY", v, v)))

    def inside(n, e, soft=0.35):
        return n.range(e, 1.0, 1.0 - soft, 0.0, 1.0)

    def front(n, P):
        return n.range(P[1], ey + 0.05 * s, ey + 0.02 * s, 0.0, 1.0)

    def both(n, P, f):
        """f(n, P, side) for each side, the stronger of the two."""
        return n.math("MAXIMUM", f(n, P, 1.0), f(n, P, -1.0))

    def smoky(n, P, sx):
        e = ell(n, P, sx * (ex + 0.003), 0.005, 0.023, 0.016, sx * math.radians(12.0))
        below = n.range(P[2], ez - 0.01 * s, ez - 0.002 * s, 0.2, 1.0)
        return n.math("MULTIPLY", n.range(e, 1.3, 0.55, 0.0, 1.0), below)

    def liner(n, P, sx):
        e = ell(n, P, sx * ex, 0.0006, 0.0185, 0.0088, sx * math.radians(8.0), b_low=0.0098)
        lash = n.range(e, 1.32, 0.98, 0.0, 1.0)
        lash = n.math("MULTIPLY", lash, n.range(P[2], ez - 0.006 * s, ez + 0.001 * s, 0.55, 1.0))
        w = ell(n, P, sx * (ex + 0.0185), 0.0042, 0.0085, 0.0017, sx * math.radians(28.0))
        return n.math("MAXIMUM", lash, inside(n, w, 0.45))

    BROW = ((0.0105, 0.0088, 0.0036), (0.02, 0.013, 0.003), (0.032, 0.0173, 0.0025), (0.043, 0.0188, 0.002),
            (0.052, 0.0168, 0.0014), (0.059, 0.013, 0.0006))

    def seg(n, P, a, b, ra, rb):
        """1 inside a stroke from a to b, (x, z) in metres from between the
        eyes, ra wide at a narrowing to rb, falling to 0 at its edge."""
        X, _, Z = P
        dx = n.math("SUBTRACT", X, a[0] * s)
        dz = n.math("SUBTRACT", Z, ez + a[1] * s)
        bx, bz = (b[0] - a[0]) * s, (b[1] - a[1]) * s
        t = n.math("MULTIPLY", n.math("ADD", n.math("MULTIPLY", dx, bx), n.math("MULTIPLY", dz, bz)), 1.0 / (bx * bx + bz * bz), clamp=True)
        ex_ = n.math("SUBTRACT", dx, n.math("MULTIPLY", t, bx))
        ez_ = n.math("SUBTRACT", dz, n.math("MULTIPLY", t, bz))
        d = n.math("SQRT", n.math("ADD", n.math("MULTIPLY", ex_, ex_), n.math("MULTIPLY", ez_, ez_)))
        r = n.math("ADD", ra * s, n.math("MULTIPLY", t, (rb - ra) * s))
        return n.range(n.math("DIVIDE", d, r), 1.0, 0.55, 0.0, 1.0)

    def brow(n, P, sx):
        k = None
        for (ax_, az, ra), (bx_, bz, rb) in zip(BROW, BROW[1:]):
            m = seg(n, P, (sx * ax_, az), (sx * bx_, bz), ra, rb)
            k = m if k is None else n.math("MAXIMUM", k, m)
        return k

    def brows(n, P):
        """Dark brows, full at their inner ends and lowered a little there,
        arching high two thirds out and tapering to fine tails, drawn in
        fine hairs."""
        hairs = n.range(n.noise(n.scale(obj(n), (90.0, 400.0, 400.0)), 1.0, 2.0), 0.35, 0.65, 0.7, 1.0)
        return n.math("MULTIPLY", n.math("MULTIPLY", both(n, P, brow), hairs), front(n, P))

    def lips(n, P):
        e = ell(n, P, 0.0, -0.0692, 0.026, 0.0098, b_low=0.0112)
        X = n.math("MULTIPLY", P[0], 1.0 / (0.026 * s))
        taper = n.math("SUBTRACT", 1.0, n.math("MULTIPLY", n.math("MULTIPLY", X, X), 0.4))
        e = n.math("DIVIDE", e, n.math("MAXIMUM", taper, 0.3))
        return n.math("MULTIPLY", n.range(e, 1.06, 0.82, 0.0, 1.0), front(n, P))

    def skin_albedo(n):
        P = xyz(n)
        col = n.mul(n.rgb((0.84, 0.74, 0.72)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.05), 0.975))
        fr = front(n, P)
        cheek = both(n, P, lambda n, P, sx: inside(n, ell(n, P, sx * 0.05, -0.024, 0.024, 0.014, sx * math.radians(18.0)), 1.0))
        col = n.mix(col, (0.74, 0.5, 0.56), n.math("MULTIPLY", n.math("MULTIPLY", cheek, fr), 0.3))
        hollow = both(n, P, lambda n, P, sx: inside(n, ell(n, P, sx * 0.049, -0.057, 0.022, 0.0085, -sx * math.radians(22.0)), 1.0))
        col = n.mix(col, (0.56, 0.45, 0.5), n.math("MULTIPLY", n.math("MULTIPLY", hollow, fr), 0.2))
        col = n.mix(col, (0.12, 0.075, 0.11), n.math("MULTIPLY", n.math("MULTIPLY", both(n, P, smoky), fr), 0.78))
        col = n.mix(col, (0.018, 0.013, 0.018), n.math("MULTIPLY", n.math("MULTIPLY", both(n, P, liner), fr), 0.95))
        col = n.mix(col, (0.016, 0.012, 0.014), n.math("MULTIPLY", brows(n, P), 0.95))
        lp = lips(n, P)
        col = n.mix(col, (0.16, 0.012, 0.04), n.math("MULTIPLY", lp, 0.94))
        line = inside(n, ell(n, P, 0.0, -0.0695, 0.025, 0.0008), 0.6)
        return n.mix(col, (0.08, 0.008, 0.022), n.math("MULTIPLY", n.math("MULTIPLY", line, fr), 0.45))

    def skin_rough(n):
        r = n.math("ADD", 0.44, n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.1))
        return n.math("SUBTRACT", r, n.math("MULTIPLY", lips(n, xyz(n)), 0.16))

    uk.look("skin", skin_albedo, None, skin_rough, 0.0, ao=0.15)
    uk.look("eye", lambda n: n.image(eye_pic, "proj")[0], None, 0.06, 0.0, ao=0.3)

    def strands(n):
        return n.noise(n.scale(obj(n), (150.0, 150.0, 5.0)), 1.0, 3.0)

    def hair_height(n):
        return n.math("ADD", n.noise(obj(n), 120.0, 2.0), n.math("MULTIPLY", n.noise(obj(n), 18.0, 2.0), 0.5))

    uk.look("hair", lambda n: n.mul(n.rgb((0.026, 0.026, 0.032)), n.range(strands(n), 0.3, 0.7, 0.6, 1.6)),
            lambda n: n.math("ADD", strands(n), n.math("MULTIPLY", hair_height(n), 0.3)),
            lambda n: n.range(strands(n), 0.35, 0.65, 0.38, 0.68), 0.0, bump=0.0016)


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

    spear = rig.pivot("spear")
    w_r = rig.pivot("hand_R")
    left = {"foot_L": ta.foot(rig, M), "leglow_L": ta.shin(rig, M), "legup_L": ta.thigh(rig, M), "gardbrace_L": ta.pauldron(rig, M),
            "armup_L": ta.upper_arm(rig, M), "armlow_L": ta.forearm(rig, M)}
    left.update(ta.wing(rig, M))
    for piece, ob in left.items():
        mirrored = uk.mirror_x(ob, ob.name + "_R")
        put(piece, ob)
        put(piece[:-1] + "R", mirrored)
    put("hand_L", ta.hand(rig, M, (spear.x, spear.z)))
    put("hand_R", uk.mirror_x(ta.hand(rig, M, (-(w_r.x - 0.04), 1.75), turn=-90.0), "fist_R"))
    put("hand_R", ta.shield(rig, M))
    put("spear", ta.spear(rig, M))
    put("hips", ta.hip(rig, M))
    put("torso", ta.torso(rig, M))
    head_parts, hair_parts, cards, locks, face = th.head(rig, M, assets)
    looks(face.eyes, rig.pivot("head"))
    head = uk.join(head_parts, "head")
    uk.project_uv(head, "proj", (FACE_BOX[0], 0.0, FACE_BOX[1]), (1, 0, 0), (0, 0, 1), (FACE_BOX[2], FACE_BOX[2]))
    put("head", head)
    put("head", uk.join(hair_parts, "hair"))
    put("head", uk.join(cards, "headcards"), own_uv=True)
    for piece, ob in locks.items():
        put(piece, ob, own_uv=True)
    return rig, nodes, parts, own


VIEWS = {
    "front": dict(yaw=0, pitch=5, dist=14.0, lens=55, target=V((0.0, 0.0, 2.5))),
    "low34": dict(yaw=38, pitch=2, dist=14.0, lens=55, target=V((0.0, 0.0, 2.5))),
    "classic": dict(yaw=0, pitch=uk.CLASSIC_PITCH, dist=40, ortho=7.5, target=V((0.0, 0.0, 2.2))),
    "face": dict(yaw=14, pitch=4, dist=2.4, lens=70, target=V((0.0, -0.05, th.EYE_Z - 0.02))),
    "back34": dict(yaw=150, pitch=8, dist=14.0, lens=55, target=V((0.0, 0.0, 2.5))),
}
ANIMS = (("fly", 0, 30, 6), ("soar", 0, 30, 3), ("attack1", 0, 60, 6), ("land", 0, 45, 6), ("startbuild", 0, 45, 6))


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
    print("THIRSHA_TRIANGLES", uk.triangles(obs))
    for piece, ps in sorted(parts.items()):
        print("  %-12s %6d" % (piece, uk.triangles(ps)))
    if not a.no_bake:
        atlas = [o for o in obs if o not in own]
        # the face by its object, which the join names after the head's node ("head.001")
        face = {o.name: 5.0 for o in parts["head"] if o not in own and "skin" in [m.name for m in o.data.materials]}
        face.update({o.name: 2.0 for o in parts["head"] if o not in own and o.name.startswith("hair")})
        uk.unwrap(atlas, scale=face)
        print("THIRSHA_SLIVERS", uk.tuck_slivers(atlas, a.size, texels=1.5, anchor=3.0))
        uk.bake_maps(atlas, a.size, name=MODEL)
        uk.rest(nodes, rig)
        uk.export(nodes[rig.pieces[0].name], a.out)
    if a.review:
        import review
        review.run(rig, nodes, a.review, a.rig, quick=a.quick, views=VIEWS, anims=ANIMS, target=V((0.0, 0.0, 2.6)), dist=14.0)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
