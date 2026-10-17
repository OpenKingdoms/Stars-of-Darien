"""Lokken, the Necromancer of Taros (TARNECRO), remastered for Stars of Darien.

    blender -b --factory-startup -P tools/units/lokken.py -- [--out GLB] [--assets DIR] [--review DIR]

Builds the model on the original's rig (unitkit.Rig), so tarnecro.cob still
walks him, casts, builds, and throws the staff, the upper arms and legs, the
head and the torso when he dies. He is drawn as a lich: a gaunt, skull-like
face from Blender Studio's CC0 Human Base Meshes drawn tight over the
bundle's CC0 skull, with embers for eyes under a scowling brow, dark hair
swept back from two points over the temples and a white pointed beard
(lokken_head.py), skeletal hands, a long black robe trimmed in dark bronze
and frayed at the hem, armour of blackened, pitted iron (toothed and
spiked pauldrons, bracers, plates on the hands, greaves and sabatons), a
high flared collar edged in bone and a mantle with pointed shoulders, a
skull buckle, a long rotting cape torn at its foot, and a twisted staff
with a skull under a forked bronze head holding a burning crystal
(lokken_robes.py). The player's colour is where the original has it: the
collar and mantle, and the cape.

The script's idle pose (restore_x) leans him forward, raises his right
forearm with the staff upright and swings the cape's pieces back. Every
part is built where that pose holds it, then turned back to the rest pose
the game starts from, so he stands whole at rest in the game and bends at
the original's joints as he moves.

The materials are glTF PBR from three pictures baked here through one UV
layout: the colour, a normal map and metal with roughness, all made from
noise and numbers and the model's own shape. Nothing comes from the
original's art. Exports unity/Assets/Overrides/Units/TARNECRO.glb, or --out.

--assets is where the CC0 head bundle is kept (docs/ASSET_CREDITS.md).
--review DIR renders the model beside the original from the player's files,
for checking only, in the idle pose and a few frames of the walk and cast.
"""
import argparse
import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import lokken_head as lh  # noqa: E402
import lokken_robes as lr  # noqa: E402
import unitkit as uk  # noqa: E402

MODEL = "TARNECRO"
OUT = os.path.join(uk.REPO, "unity", "Assets", "Overrides", "Units", MODEL + ".glb")
ASSETS = os.environ.get("OKU_UNIT_ASSETS", os.path.join(os.path.dirname(uk.REPO), "unit-assets"))
V = Vector

# The script's idle pose: restore_x's turns in degrees about x, y and z here
# (the 3DO's x, z and y), read from tarnecro.cob.
IDLE = {"torso": (11.6, 0.0, 0.0), "luparm": (-9.49, 7.38, 0.0), "cape1": (-3.16, 0.0, 0.0), "ruparm": (-5.97, 0.0, 4.92),
        "rarm": (-63.63, 0.0, -15.11), "rhand": (-11.95, 0.0, 0.0), "cape2": (-13.71, 0.0, 0.0), "cape3": (3.87, 0.0, 0.0),
        "cape4": (9.14, 0.0, 0.0), "larm": (-15.11, 4.92, 0.0)}


def turned(rig, name, turns=IDLE):
    """Where a pose of turns moves a piece's rest geometry, as a matrix in
    model space: each piece turned about its pivot, then by its parents'."""
    chain = []
    p = rig[name]
    while True:
        chain.append(p)
        if p.parent < 0:
            break
        p = rig.pieces[p.parent]
    m = Matrix.Identity(4)
    for q in reversed(chain):
        e = turns.get(q.name, (0.0, 0.0, 0.0))
        r = Euler([math.radians(a) for a in e], "XYZ").to_matrix().to_4x4()
        m = m @ Matrix.Translation(q.pivot) @ r @ Matrix.Translation(-q.pivot)
    return m


class Idle:
    """The rig as the idle pose holds it, for building parts in place."""

    def __init__(self, rig):
        self.rig = rig
        self.m = {p.name: turned(rig, p.name) for p in rig.pieces}

    def pivot(self, name):
        return self.m[name] @ self.rig.pivot(name)

    def point(self, name, p):
        """A point given in the piece's rest frame, where the idle pose puts it."""
        return self.m[name] @ V(p)

    def axis(self, name, d):
        return (self.m[name].to_3x3() @ V(d)).normalized()


# ---------------------------------------------------------------- materials

def strands_material(name, pic, cut=0.35):
    mat = uk.material(name, (1, 1, 1), rough=0.8, double=True)
    b = mat.node_tree.nodes["Principled BSDF"]
    t = mat.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = pic
    mat.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    mat.node_tree.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
    mat["uk_mask"] = cut
    mat[uk.okpaint.GENERATED] = True
    return mat


def materials():
    m = {
        "robe": uk.material("robe", (0.02, 0.019, 0.022), rough=0.85),
        "robe_lining": uk.material("robe_lining", (0.026, 0.009, 0.009), rough=0.8),
        "bronze": uk.material("bronze", (0.2, 0.13, 0.065), metal=1.0, rough=0.5),
        "bronzethread": uk.material("bronzethread", (0.22, 0.16, 0.09), metal=0.6, rough=0.6),
        "iron": uk.material("iron", (0.022, 0.021, 0.023), metal=0.8, rough=0.6),
        "leather": uk.material("leather", (0.04, 0.026, 0.018), rough=0.62),
        "wood": uk.material("wood", (0.035, 0.024, 0.018), rough=0.6),
        "bone": uk.material("bone", (0.4, 0.35, 0.26), rough=0.55),
        "nail": uk.material("nail", (0.1, 0.085, 0.06), rough=0.35),
        "teamcolor": uk.material("teamcolor", (0.78, 0.78, 0.78), rough=0.82),
        "teamcolor_lining": uk.material("teamcolor_lining", (0.36, 0.36, 0.36), rough=0.88),
        "skin": uk.material("skin", (0.26, 0.28, 0.26), rough=0.5),
        "flesh": uk.material("flesh", (0.26, 0.28, 0.26), rough=0.5),
        "eye": uk.material("eye", (0.015, 0.012, 0.012), rough=0.15),
        "ember": uk.material("ember", (0.7, 0.12, 0.02), rough=0.3, emit=(1.0, 0.14, 0.015), strength=1.5),
        "crystal": uk.material("crystal", (0.5, 0.05, 0.01), rough=0.12, emit=(0.95, 0.16, 0.02), strength=1.3),
        "vein": uk.material("vein", (0.3, 0.04, 0.01), rough=0.4, emit=(0.8, 0.12, 0.02), strength=0.7),
        "gem": uk.material("gem", (0.16, 0.008, 0.008), rough=0.08, emit=(0.3, 0.015, 0.01), strength=0.35),
        "hair": uk.material("hair", (0.03, 0.028, 0.026), rough=0.55),
        "beard": uk.material("beard", (0.45, 0.44, 0.41), rough=0.7),
    }
    m["strands"] = strands_material("strands", lh.strand_picture())
    m["strands_dark"] = strands_material("strands_dark", lh.strand_picture(seed=23, dark=True))
    return m


# ---------------------------------------------------------------- looks

def looks(eyes, hp, pivot):
    """Each material's albedo, height, roughness and metal for the bake.
    The face's marks are placed in the head piece's own frame at rest: the
    head's idle matrix hp maps a point at rest to where it is built, and
    pivot is the head's pivot."""
    inv = hp.inverted()

    def obj(n):
        return n.coord("Object")

    def world(n):
        return n.geometry("Position")

    def near(n, centre, radius, vec=None):
        d = n.new("ShaderNodeVectorMath")
        d.operation = "DISTANCE"
        n.link(vec if vec is not None else obj(n), d.inputs[0])
        d.inputs[1].default_value = centre
        return n.range(d.outputs["Value"], radius, 0.0, 0.0, 1.0)

    def scratches(n):
        return n.range(n.noise(n.scale(obj(n), (60.0, 60.0, 600.0)), 1.0, 1.0), 0.66, 0.72, 0.0, 1.0)

    def recess(n, dist=0.06):
        occ = n.new("ShaderNodeAmbientOcclusion")
        occ.only_local = True
        occ.samples = 16
        occ.inputs["Distance"].default_value = dist
        return n.range(occ.outputs["AO"], 0.9, 0.5, 0.0, 1.0)

    def hammered(n, scale=26.0, k=1.0):
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "SMOOTH_F1"
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = scale
        dents = n.math("MULTIPLY", vor.outputs["Distance"], 0.6 * k)
        fine = n.math("MULTIPLY", n.noise(obj(n), 220.0, 2.0), 0.15 * k)
        return n.math("SUBTRACT", n.math("ADD", dents, fine), n.math("MULTIPLY", scratches(n), 0.25 * k))

    # bronze: old and dark, green with verdigris in its recesses and patches, rubbed a little brighter on its edges
    def verdigris(n):
        patches = n.range(n.noise(obj(n), 5.0, 4.0, 0.65), 0.55, 0.75, 0.0, 1.0)
        return n.math("MAXIMUM", n.math("MULTIPLY", recess(n, 0.05), 0.85), n.math("MULTIPLY", patches, 0.55))

    def bronze_albedo(n):
        tarnish = n.range(n.noise(obj(n), 3.0, 4.0, 0.6), 0.4, 0.7, 0.0, 1.0)
        edge = n.range(n.geometry("Pointiness"), 0.52, 0.6, 0.0, 1.0)
        col = n.mix(n.rgb((0.19, 0.12, 0.06)), (0.06, 0.045, 0.03), n.math("MULTIPLY", tarnish, 0.7))
        col = n.mix(col, (0.09, 0.17, 0.13), verdigris(n))
        col = n.mix(col, (0.035, 0.03, 0.025), n.math("MULTIPLY", recess(n, 0.03), 0.6))
        col = n.mix(col, (0.36, 0.25, 0.13), n.math("MULTIPLY", edge, 0.35))
        return n.mul(col, n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.2), 0.9))

    uk.look("bronze", bronze_albedo, lambda n: n.math("ADD", hammered(n), n.math("MULTIPLY", verdigris(n), 0.4)),
            lambda n: n.math("ADD", 0.42, n.math("MULTIPLY", verdigris(n), 0.45)),
            lambda n: n.math("SUBTRACT", 1.0, n.math("MULTIPLY", verdigris(n), 0.95), clamp=True), bump=0.002)
    uk.look("bronzethread", lambda n: n.mix(n.mul(n.rgb((0.2, 0.14, 0.08)), n.math("ADD", n.math("MULTIPLY", n.wave(world(n), 60.0, "Z", 0.0, "SAW"), 0.3), 0.7)),
                                            (0.07, 0.11, 0.08), n.math("MULTIPLY", n.range(n.noise(world(n), 7.0, 3.0), 0.5, 0.75, 0.0, 1.0), 0.6)),
            lambda n: n.wave(world(n), 60.0, "Z", 0.0, "SAW"), 0.65, 0.5, bump=0.0018)

    def pits(n):
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "F1"
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = 140.0
        small = n.range(vor.outputs["Distance"], 0.0, 0.18, 1.0, 0.0)
        return n.math("MULTIPLY", small, n.range(n.noise(obj(n), 12.0, 3.0), 0.45, 0.65, 0.0, 1.0))

    def rust(n):
        streak = n.range(n.noise(n.scale(obj(n), (30.0, 30.0, 4.0)), 3.0, 3.0), 0.55, 0.75, 0.0, 1.0)
        return n.math("MAXIMUM", n.math("MULTIPLY", recess(n, 0.05), 0.9), n.math("MULTIPLY", streak, 0.45))

    def iron_albedo(n):
        col = n.mul(n.rgb((0.022, 0.021, 0.024)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 6.0, 3.0), 0.5), 0.75))
        col = n.mix(col, (0.11, 0.045, 0.018), rust(n))
        col = n.mix(col, (0.012, 0.01, 0.01), n.math("MULTIPLY", pits(n), 0.8))
        edge = n.range(n.geometry("Pointiness"), 0.53, 0.6, 0.0, 1.0)
        return n.mix(col, (0.12, 0.12, 0.13), n.math("MULTIPLY", edge, 0.5))

    uk.look("iron", iron_albedo, lambda n: n.math("SUBTRACT", hammered(n, 30.0, 0.8), n.math("MULTIPLY", pits(n), 0.8)),
            lambda n: n.math("ADD", 0.5, n.math("MULTIPLY", rust(n), 0.4)),
            lambda n: n.math("SUBTRACT", 0.8, n.math("MULTIPLY", rust(n), 0.7)), bump=0.0025)

    def leather_albedo(n):
        edge = n.range(n.geometry("Pointiness"), 0.5, 0.58, 0.0, 1.0)
        col = n.mul(n.rgb((0.04, 0.026, 0.018)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 40.0, 3.0), 0.5), 0.75))
        return n.mix(col, (0.12, 0.085, 0.06), n.math("MULTIPLY", edge, 0.5))

    uk.look("leather", leather_albedo, lambda n: n.math("ADD", n.noise(obj(n), 90.0, 3.0), n.math("MULTIPLY", n.noise(obj(n), 12.0, 2.0), 0.6)),
            lambda n: n.math("ADD", 0.5, n.math("MULTIPLY", n.noise(obj(n), 20.0, 2.0), 0.25)), 0.0, bump=0.0015)

    def grain(n):
        return n.wave(n.scale(obj(n), (40.0, 1.0, 40.0)), 5.0, "Y", 8.0)

    uk.look("wood", lambda n: n.mul(n.rgb((0.035, 0.024, 0.018)), n.math("ADD", n.math("MULTIPLY", grain(n), 0.5), 0.7)),
            grain, lambda n: n.math("ADD", 0.5, n.math("MULTIPLY", grain(n), 0.15)), 0.0, bump=0.002)

    def bone_albedo(n):
        col = n.mul(n.rgb((0.4, 0.35, 0.26)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 30.0, 3.0), 0.25), 0.85))
        col = n.mix(col, (0.2, 0.16, 0.1), n.math("MULTIPLY", recess(n, 0.04), 0.85))
        return n.mix(col, (0.32, 0.25, 0.15), n.math("MULTIPLY", n.range(n.noise(obj(n), 6.0, 3.0), 0.5, 0.7, 0.0, 1.0), 0.5))

    uk.look("bone", bone_albedo, lambda n: n.math("ADD", n.noise(obj(n), 120.0, 2.0), n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.5)),
            lambda n: n.math("ADD", 0.5, n.math("MULTIPLY", recess(n, 0.04), 0.3)), 0.0, bump=0.0012, ao=1.3)
    uk.look("nail", lambda n: n.mix(n.rgb((0.1, 0.085, 0.06)), (0.24, 0.2, 0.14), n.range(n.noise(obj(n), 40.0, 2.0), 0.5, 0.8, 0.0, 0.6)),
            None, 0.35, 0.0)

    # the robe: black wool, worn grey on its folds' edges, dusty toward the hem
    def weave(n):
        return n.math("ADD", n.wave(world(n), 80.0, "X"), n.wave(world(n), 80.0, "Z"))

    def robe_albedo(n):
        sep = n.new("ShaderNodeSeparateXYZ")
        n.link(world(n), sep.inputs[0])
        col = n.mul(n.rgb((0.022, 0.021, 0.024)), n.math("ADD", n.math("MULTIPLY", weave(n), 0.06), 0.9))
        col = n.mul(col, n.math("ADD", n.math("MULTIPLY", n.noise(world(n), 4.0, 3.0), 0.35), 0.85))
        dust = n.range(sep.outputs["Z"], 0.45, 0.04, 0.0, 1.0)
        col = n.mix(col, (0.05, 0.045, 0.04), n.math("MULTIPLY", dust, n.range(n.noise(world(n), 9.0, 3.0), 0.35, 0.7, 0.2, 0.7)))
        edge = n.range(n.geometry("Pointiness"), 0.53, 0.6, 0.0, 1.0)
        return n.mix(col, (0.06, 0.056, 0.06), n.math("MULTIPLY", edge, 0.5))

    uk.look("robe", robe_albedo, lambda n: n.math("ADD", weave(n), n.math("MULTIPLY", n.noise(world(n), 3.0, 1.0), 0.8)),
            lambda n: n.math("ADD", 0.82, n.math("MULTIPLY", n.noise(world(n), 6.0, 2.0), 0.12)), 0.0, bump=0.0012, ao=1.4)
    uk.look("robe_lining", lambda n: n.mul(n.rgb((0.026, 0.009, 0.009)), n.math("ADD", n.math("MULTIPLY", weave(n), 0.05), 0.9)),
            weave, 0.75, 0.0, bump=0.001)

    def cloth(base):
        """Wool in the player's colour, faded, stained with rot and grime,
        darker toward the ground."""
        def f(n):
            sep = n.new("ShaderNodeSeparateXYZ")
            n.link(world(n), sep.inputs[0])
            k = n.math("ADD", n.math("MULTIPLY", weave(n), 0.05), 0.9)
            k = n.math("ADD", k, n.math("MULTIPLY", n.noise(world(n), 5.0, 2.0), 0.1))
            col = n.mul(n.rgb((base, base, base)), k)
            rot = n.range(n.noise(world(n), 3.5, 4.0, 0.65), 0.5, 0.72, 0.0, 1.0)
            col = n.mix(col, (base * 0.32, base * 0.3, base * 0.27), n.math("MULTIPLY", rot, 0.7))
            low = n.range(sep.outputs["Z"], 1.2, 0.05, 0.0, 1.0)
            col = n.mix(col, (base * 0.25, base * 0.23, base * 0.2), n.math("MULTIPLY", low, n.range(n.noise(world(n), 8.0, 3.0), 0.3, 0.7, 0.4, 0.85)))
            return n.mix(col, (base * 0.2, base * 0.19, base * 0.18), n.math("MULTIPLY", recess(n, 0.1), 0.5))
        return f

    uk.look("teamcolor", cloth(0.72), lambda n: n.math("ADD", weave(n), n.math("MULTIPLY", n.noise(world(n), 3.0, 1.0), 0.8)), 0.8, 0.0, bump=0.0012, ao=1.6)
    uk.look("teamcolor_lining", cloth(0.38), weave, 0.88, 0.0, bump=0.001)

    # the face, in the head piece's rest frame
    sockets = [tuple(inv @ c - pivot) for c, r in eyes]
    ey = sockets[0][1]
    ez = (sockets[0][2] + sockets[1][2]) / 2
    s = lh.SCALE
    mouth = (0.0, ey - 0.03 * s, ez - 0.075 * s)
    temple = [(sx * 0.068 * s, ey + 0.03 * s, ez + 0.03 * s) for sx in (-1, 1)]
    em = (eyes[0][0] + eyes[1][0]) / 2 - V((0.0, lh.EYE_BACK * s, 0.0))
    wound = tuple(inv @ (em + V(lh.WOUND) * s) - pivot)

    def worn(n):
        """The ragged patch where the bone shows through, and its torn rim."""
        d = n.new("ShaderNodeVectorMath")
        d.operation = "DISTANCE"
        n.link(obj(n), d.inputs[0])
        d.inputs[1].default_value = wound
        r = lh.WOUND_R * s * 1.2
        ragged = n.math("ADD", d.outputs["Value"], n.math("MULTIPLY", n.math("SUBTRACT", n.noise(obj(n), 90.0, 3.0), 0.5), r * 0.9))
        bare = n.range(ragged, r * 0.75, r * 0.6, 0.0, 1.0)
        rim = n.math("SUBTRACT", n.range(ragged, r * 1.05, r * 0.8, 0.0, 1.0), bare)
        return bare, rim

    def lips_mask(n):
        d = n.new("ShaderNodeVectorMath")
        d.operation = "DISTANCE"
        n.link(n.scale(obj(n), (1.0, 1.0, 2.6)), d.inputs[0])
        d.inputs[1].default_value = (mouth[0], mouth[1], mouth[2] * 2.6)
        return n.range(d.outputs["Value"], 0.075, 0.035, 0.0, 1.0)

    def cracks(n):
        """Fine cracks running up and down the dry lips."""
        lines = n.math("ABSOLUTE", n.math("SUBTRACT", n.noise(n.scale(obj(n), (90.0, 6.0, 9.0)), 2.0, 2.0), 0.5))
        return n.math("MULTIPLY", n.range(lines, 0.03, 0.0, 0.0, 1.0), lips_mask(n))

    def veins(n, wide=0.25):
        """Faint dark veins under the thin skin, plainest at the temples."""
        temples = n.math("MAXIMUM", near(n, temple[0], 0.13), near(n, temple[1], 0.13))
        lines = n.math("ABSOLUTE", n.math("SUBTRACT", n.noise(obj(n), 16.0, 2.5, 0.5), 0.5))
        fine = n.math("ABSOLUTE", n.math("SUBTRACT", n.noise(obj(n), 40.0, 2.0, 0.5), 0.5))
        v = n.math("MAXIMUM", n.range(lines, 0.008, 0.0, 0.0, 1.0), n.math("MULTIPLY", n.range(fine, 0.006, 0.0, 0.0, 1.0), 0.5))
        return n.math("MULTIPLY", v, n.math("ADD", wide, n.math("MULTIPLY", temples, 0.35)))

    def skin_albedo(n):
        """Cold grey-green skin drawn thin over the bone: mottled and
        bruised, faint dark veins, pale where the bone pushes through,
        shadowed in the hollows and round the sunken eyes, the lips grey,
        dry and cracked."""
        col = n.mul(n.rgb((0.26, 0.28, 0.26)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 25.0, 2.0), 0.14), 0.93))
        bruise = n.range(n.noise(obj(n), 7.0, 3.0, 0.6), 0.5, 0.72, 0.0, 1.0)
        col = n.mix(col, (0.19, 0.18, 0.21), n.math("MULTIPLY", bruise, 0.45))
        col = n.mix(col, (0.3, 0.31, 0.24), n.math("MULTIPLY", n.range(n.noise(obj(n), 11.0, 3.0), 0.55, 0.75, 0.0, 1.0), 0.35))
        bone = n.range(n.geometry("Pointiness"), 0.52, 0.6, 0.0, 1.0)
        col = n.mix(col, (0.46, 0.44, 0.37), n.math("MULTIPLY", bone, 0.5))
        gaunt = n.new("ShaderNodeAttribute")
        gaunt.attribute_name = "gaunt"
        sep = n.new("ShaderNodeSeparateColor")
        n.link(gaunt.outputs["Color"], sep.inputs[0])
        hollow = n.math("MAXIMUM", recess(n, 0.12), sep.outputs["Red"])
        col = n.mix(col, (0.085, 0.085, 0.11), n.math("MULTIPLY", hollow, 0.8))
        col = n.mix(col, (0.48, 0.47, 0.4), n.math("MULTIPLY", sep.outputs["Green"], 0.4))
        col = n.mix(col, (0.15, 0.16, 0.2), veins(n, 0.12))
        shade = n.math("MAXIMUM", near(n, sockets[0], 0.14), near(n, sockets[1], 0.14))
        col = n.mix(col, (0.02, 0.018, 0.024), n.math("MINIMUM", n.math("MULTIPLY", shade, 1.6), 0.94))
        col = n.mix(col, (0.12, 0.1, 0.12), n.math("MULTIPLY", lips_mask(n), 0.85))
        col = n.mix(col, (0.04, 0.03, 0.035), n.math("MULTIPLY", cracks(n), 0.8))
        col = n.mul(col, n.math("SUBTRACT", 1.0, n.math("MULTIPLY", recess(n, 0.08), 0.5)))
        bare, rim = worn(n)
        col = n.mix(col, (0.07, 0.035, 0.03), n.math("MULTIPLY", rim, 0.85))
        bone_col = n.mul(n.rgb((0.5, 0.46, 0.36)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 60.0, 3.0), 0.3), 0.8))
        return n.mix(col, bone_col, bare)

    def lines(n, freq, mask):
        sep = n.new("ShaderNodeSeparateXYZ")
        n.link(obj(n), sep.inputs[0])
        wob = n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.4)
        return n.math("MULTIPLY", n.math("SINE", n.math("ADD", n.math("MULTIPLY", sep.outputs["Z"], freq), wob)), mask)

    def skin_height(n):
        """Pores, the creases of great age (the brow, the corners of the
        eyes, under them, between the brows and down the neck), veins
        standing a little proud, and the cracked lips."""
        sep = n.new("ShaderNodeSeparateXYZ")
        n.link(obj(n), sep.inputs[0])
        vor = n.new("ShaderNodeTexVoronoi")
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = 600.0
        pores = n.range(vor.outputs["Distance"], 0.0, 0.35, -1.0, 0.0)
        crepe = n.math("MULTIPLY", n.noise(n.scale(obj(n), (6.0, 6.0, 20.0)), 10.0, 3.0), 0.3)
        brow = n.math("MULTIPLY", n.range(sep.outputs["Z"], ez + 0.06, ez + 0.1, 0.0, 1.0), n.range(sep.outputs["Y"], ey + 0.02, ey - 0.06, 0.0, 1.0))
        forehead = lines(n, 230.0, brow)
        corners = n.math("MAXIMUM", *[near(n, (x + math.copysign(0.055, x), y + 0.02, z), 0.05) for x, y, z in sockets])
        crows = lines(n, 380.0, corners)
        under = n.math("MAXIMUM", *[near(n, (x, y - 0.02, z - 0.04), 0.03) for x, y, z in sockets])
        bags = lines(n, 300.0, n.math("MULTIPLY", under, 0.6))
        mid = (sockets[0][0] + sockets[1][0]) * 0.5
        frown = n.math("MULTIPLY", n.range(sep.outputs["Z"], ez + 0.012, ez + 0.03, 0.0, 1.0), n.range(sep.outputs["Z"], ez + 0.08, ez + 0.05, 0.0, 1.0))
        frown = n.math("MULTIPLY", frown, n.range(n.math("ABSOLUTE", n.math("SUBTRACT", n.math("ABSOLUTE", n.math("SUBTRACT", sep.outputs["X"], mid)), 0.014)), 0.0, 0.005, 1.0, 0.0))
        throat = n.range(sep.outputs["Z"], ez - 0.3, ez - 0.4, 0.0, 1.0)
        neck = lines(n, 160.0, n.math("MULTIPLY", throat, 0.7))
        vein = n.math("MULTIPLY", veins(n, 0.15), 0.6)
        bare, rim = worn(n)
        skin = n.math("ADD", n.math("MULTIPLY", pores, 0.12), crepe)
        skin = n.math("ADD", skin, n.math("ADD", n.math("MULTIPLY", forehead, 0.9), n.math("MULTIPLY", crows, 0.6)))
        skin = n.math("ADD", skin, n.math("ADD", bags, n.math("MULTIPLY", neck, 0.8)))
        skin = n.math("SUBTRACT", n.math("ADD", skin, vein), n.math("ADD", n.math("MULTIPLY", frown, 1.4), n.math("MULTIPLY", cracks(n), 1.2)))
        return n.math("ADD", n.math("MULTIPLY", skin, n.math("SUBTRACT", 1.0, bare)), n.math("MULTIPLY", rim, 0.8))

    def flesh_albedo(n):
        """The hands and the neck: the same cold skin over bone and tendon."""
        col = n.mul(n.rgb((0.25, 0.27, 0.25)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.16), 0.92))
        col = n.mix(col, (0.45, 0.43, 0.36), n.math("MULTIPLY", n.range(n.geometry("Pointiness"), 0.52, 0.6, 0.0, 1.0), 0.55))
        v = n.math("ABSOLUTE", n.math("SUBTRACT", n.noise(obj(n), 22.0, 2.0, 0.5), 0.5))
        col = n.mix(col, (0.11, 0.12, 0.16), n.math("MULTIPLY", n.range(v, 0.015, 0.0, 0.0, 1.0), 0.5))
        return n.mix(col, (0.06, 0.06, 0.075), n.math("MULTIPLY", recess(n, 0.04), 0.7))

    uk.look("flesh", flesh_albedo, lambda n: n.math("ADD", n.math("MULTIPLY", n.noise(n.scale(obj(n), (4.0, 4.0, 40.0)), 12.0, 3.0), 0.5),
                                                    n.math("MULTIPLY", n.noise(obj(n), 200.0, 2.0), 0.2)),
            lambda n: n.math("ADD", 0.5, n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.14)), 0.0, bump=0.0012)

    uk.look("skin", skin_albedo, skin_height, lambda n: n.math("ADD", 0.48, n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.14)), 0.0, bump=0.0016, ao=1.0)
    uk.look("eye", lambda n: n.rgb((0.015, 0.012, 0.012)), None, 0.15, 0.0, ao=0.2)
    uk.look("ember", lambda n: n.mix(n.rgb((1.0, 0.55, 0.12)), (0.6, 0.12, 0.02), n.range(n.noise(obj(n), 200.0, 2.0), 0.3, 0.7, 0.0, 1.0)), None, 0.3, 0.0, ao=0.0)
    uk.look("crystal", lambda n: n.mix(n.rgb((0.5, 0.05, 0.01)), (0.85, 0.3, 0.05), n.range(n.noise(obj(n), 40.0, 2.0), 0.45, 0.75, 0.0, 1.0)), None, 0.12, 0.0, ao=0.2)
    uk.look("gem", lambda n: n.rgb((0.22, 0.01, 0.01)), None, 0.08, 0.0, ao=0.2)
    uk.look("vein", lambda n: n.mix(n.rgb((0.3, 0.04, 0.01)), (0.6, 0.15, 0.03), n.range(n.noise(obj(n), 30.0, 2.0), 0.4, 0.7, 0.0, 1.0)), None, 0.4, 0.0, ao=0.6)

    def strands(n):
        return n.noise(n.scale(obj(n), (160.0, 160.0, 10.0)), 1.0, 2.0)

    uk.look("hair", lambda n: n.mul(n.rgb((0.03, 0.028, 0.026)), n.range(strands(n), 0.3, 0.75, 0.7, 1.5)), strands, 0.6, 0.0, bump=0.0012)
    def combed(n):
        return n.noise(n.scale(obj(n), (180.0, 180.0, 14.0)), 1.0, 2.0)

    uk.look("beard", lambda n: n.mul(n.rgb((0.5, 0.49, 0.46)), n.range(combed(n), 0.3, 0.75, 0.7, 1.25)), combed, 0.7, 0.0, bump=0.0015)


def preview(mats):
    """The looks wired straight into the materials, for quick renders
    without a bake."""
    for m in mats:
        L = uk.LOOKS.get(m.name)
        if not L or m.name.startswith("strands"):
            continue
        nt = m.node_tree
        n = uk.Nodes(nt)
        b = next(x for x in nt.nodes if x.type == "BSDF_PRINCIPLED")
        if L.get("albedo"):
            n.link(L["albedo"](n), b.inputs["Base Color"])
        for key, sock in (("rough", "Roughness"), ("metal", "Metallic")):
            v = L.get(key)
            if callable(v):
                n.link(v(n), b.inputs[sock])
            elif v is not None:
                b.inputs[sock].default_value = v
        if L.get("height"):
            bump = n.new("ShaderNodeBump")
            bump.inputs["Distance"].default_value = L.get("bump", 1.0)
            n.link(L["height"](n), bump.inputs["Height"])
            n.link(bump.outputs["Normal"], b.inputs["Normal"])


# ---------------------------------------------------------------- assembly

# The head is built level where the torso leans this far, between its
# lean in the idle pose and its sway in the walk.
HEAD_FRAME = {"torso": (6.0, 0.0, 0.0)}


def build(assets=ASSETS):
    """The rig and every part on it. Returns (rig, nodes, parts, own), own
    the parts with pictures of their own, kept out of the atlas."""
    rig = uk.Rig(MODEL)
    idle = Idle(rig)
    M = materials()
    nodes = rig.nodes()
    parts, own = {}, []

    def put(piece, ob, built=None, own_uv=False):
        """Hangs a part on its piece; built is the matrix of the pose it was
        built in, turned back to rest here."""
        if built is not None:
            ob.data.transform(built.inverted())
        uk.attach(ob, nodes[piece])
        parts.setdefault(piece, []).append(ob)
        if own_uv:
            own.append(ob)

    head_m = turned(rig, "head", HEAD_FRAME)
    head_parts, cards, face = lh.head(rig, M, assets)
    looks(face.eyes, head_m, rig.pivot("head"))
    put("head", uk.join(head_parts, "face"), head_m)
    put("head", uk.join(cards, "headcards"), head_m, own_uv=True)
    for piece, items in lr.body(rig, idle, M, assets).items():
        for ob, frame in items:
            put(piece, ob, idle.m[piece] if frame == "idle" else None)
    return rig, nodes, parts, own


def pose(nodes, rig, turns):
    """Puts the piece nodes in a pose of turns, as the script would."""
    for p in rig.pieces:
        e = turns.get(p.name, (0.0, 0.0, 0.0))
        nodes[p.name].rotation_euler = Euler([math.radians(a) for a in e], "XYZ")
    bpy.context.view_layer.update()


VIEWS = {
    "front": dict(yaw=0, pitch=5, dist=14.0, lens=55, target=V((0.0, -0.3, 2.1))),
    "low34": dict(yaw=38, pitch=2, dist=14.0, lens=55, target=V((0.0, -0.3, 2.1))),
    "side": dict(yaw=90, pitch=3, dist=14.0, lens=55, target=V((0.0, -0.3, 2.1))),
    "classic": dict(yaw=0, pitch=uk.CLASSIC_PITCH, dist=40, ortho=7.0, target=V((0.0, -0.3, 1.9))),
    "face": dict(yaw=14, pitch=4, dist=2.4, lens=70, target=V((0.0, -0.9, lh.EYE_Z - 0.06))),
    "back34": dict(yaw=150, pitch=8, dist=14.0, lens=55, target=V((0.0, -0.3, 2.1))),
}


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--assets", default=ASSETS)
    ap.add_argument("--review", default=None)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--quick", action="store_true", help="review the stills only")
    ap.add_argument("--size", type=int, default=2048, help="the atlas's side")
    a = ap.parse_args(argv)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rig, nodes, parts, own = build(a.assets)
    obs = [o for ps in parts.values() for o in ps]
    print("LOKKEN_TRIANGLES", uk.triangles(obs))
    for piece, ps in sorted(parts.items()):
        print("  %-12s %6d" % (piece, uk.triangles(ps)))
    if not a.no_bake:
        atlas = [o for o in obs if o not in own]
        uk.unwrap(atlas, scale={"face": 3.0})
        print("LOKKEN_SLIVERS", uk.tuck_slivers(atlas, a.size))
        pose(nodes, rig, IDLE)
        uk.bake_maps(atlas, a.size, name=MODEL)
        uk.rest(nodes, rig)
        uk.export(nodes[rig.pieces[0].name], a.out)
    else:
        preview(bpy.data.materials)
    if a.review:
        import lokken_review
        lokken_review.run(rig, nodes, a.review, IDLE, quick=a.quick)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
