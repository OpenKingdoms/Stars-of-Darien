"""The Great Sage, Creon's monarch (CRESAGE), remastered for Stars of Darien.

    blender -b --factory-startup -P tools/units/sage.py -- [--out GLB] [--review DIR] [--rig RIG]

Builds the model on the original's rig (unitkit.Rig), so the original's
script walks him, swings his arms, rolls his blade and opens it into jaws
to throw his blue flame, and throws his pieces when he dies. He is a steam
automaton, no flesh anywhere: a riveted copper body and a domed head with
a glowing T-slot visor and a frowning grille, a beard of brass rods fanned
over his chest, coil banks for shoulders, a boiler and a smokestack on his
back, a flywheel shield with Creon's key on its hub and a split blade
(sage_machine.py). Gears sit where the script turns his joints, so they
turn with them. The player's colour is where the original has it: enamel
on the chest, the shoulders, the top and the back, and down the front and
outer side of each leg.

The materials are glTF PBR from three pictures baked here through one UV
layout, made from noise and numbers and the model's own shape, as
elsin.py does: worn copper gone brown, rusty and green in its recesses,
brass polished where it is handled, cast iron, steel and chipped enamel.
The visor, the grille, the bells, the firebox and the smokestack glow from
inside. Nothing comes from the original's art. Exports
unity/Assets/Overrides/Units/CRESAGE.glb, or --out.

--review DIR renders the model beside the original from the player's
files, for checking only, and --rig names a pose dump from the engine
(Tests/Editor/UnitRigDump.cs with OKU_RIG_UNIT=CRESAGE).
"""
import argparse
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import sage_machine as sm  # noqa: E402
import unitkit as uk  # noqa: E402

MODEL = "CRESAGE"
OUT = os.path.join(uk.REPO, "unity", "Assets", "Overrides", "Units", MODEL + ".glb")
V = Vector
MIRROR = {"lupleg": "rupleg", "lfoot": "rfoot", "ltoe": "rtoe", "luparm": "ruparm", "larm": "rarm", "lhand": "rhand"}


# ---------------------------------------------------------------- materials

def materials():
    return {
        "copper": uk.material("copper", (0.5, 0.25, 0.14), metal=0.85, rough=0.4),
        "brass": uk.material("brass", (0.72, 0.55, 0.26), metal=1.0, rough=0.3),
        "iron": uk.material("iron", (0.09, 0.085, 0.08), metal=0.6, rough=0.6),
        "steel": uk.material("steel", (0.6, 0.6, 0.62), metal=1.0, rough=0.28),
        "coil": uk.material("coil", (0.62, 0.3, 0.16), metal=1.0, rough=0.32),
        "soot": uk.material("soot", (0.03, 0.028, 0.026), metal=0.2, rough=0.85),
        "ember": uk.material("ember", (0.9, 0.3, 0.05), rough=0.7, emit=(0.9, 0.3, 0.05), strength=1.6),
        "teamcolor_enamel": uk.material("teamcolor_enamel", (0.8, 0.8, 0.8), rough=0.35),
    }


# ---------------------------------------------------------------- looks

def looks():
    """Each material's albedo, height, roughness and metal for the bake."""

    def obj(n):
        return n.coord("Object")

    def world(n):
        return n.geometry("Position")

    def wear(n, lo=0.53, hi=0.6):
        """The edges rubbed bright and the recesses where grime sits."""
        edge = n.range(n.geometry("Pointiness"), lo, hi, 0.0, 1.0)
        edge = n.math("MULTIPLY", edge, n.range(n.noise(obj(n), 7.0, 3.0, 0.6), 0.38, 0.6, 0.25, 1.0))
        occ = n.new("ShaderNodeAmbientOcclusion")
        occ.only_local = True
        occ.samples = 16
        occ.inputs["Distance"].default_value = 0.08
        recess = n.range(occ.outputs["AO"], 0.92, 0.5, 0.0, 1.0)
        return edge, recess

    def grime(n, col):
        """Dirt splashed up from the ground and soot from the stacks."""
        sep = n.new("ShaderNodeSeparateXYZ")
        n.link(world(n), sep.inputs[0])
        low = n.math("MULTIPLY", n.range(sep.outputs["Z"], 1.1, 0.05, 0.0, 0.55), n.range(n.noise(world(n), 6.0, 4.0, 0.6), 0.35, 0.6, 0.4, 1.0))
        col = n.mix(col, (0.11, 0.085, 0.06), low)
        high = n.math("MULTIPLY", n.range(sep.outputs["Z"], 4.25, 4.85, 0.0, 0.6), n.range(n.noise(world(n), 8.0, 3.0), 0.3, 0.6, 0.5, 1.0))
        return n.mix(col, (0.025, 0.022, 0.02), high)

    def scratches(n):
        return n.range(n.noise(n.scale(obj(n), (70.0, 70.0, 500.0)), 1.0, 1.0), 0.67, 0.72, 0.0, 1.0)

    def pits(n, scale=160.0):
        vor = n.new("ShaderNodeTexVoronoi")
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = scale
        return n.range(vor.outputs["Distance"], 0.0, 0.3, -1.0, 0.0)

    # copper: brown with age, rust patches, verdigris in the recesses and
    # running down from them, rubbed bright on the edges
    def copper_masks(n):
        edge, recess = wear(n)
        oxide = n.range(n.noise(obj(n), 2.2, 4.0, 0.62), 0.36, 0.6, 0.2, 0.85)
        rust = n.range(n.noise(obj(n), 5.0, 5.0, 0.7), 0.57, 0.68, 0.0, 0.9)
        drip = n.range(n.noise(n.scale(world(n), (16.0, 16.0, 2.2)), 1.0, 4.0, 0.6), 0.55, 0.7, 0.0, 1.0)
        verd = n.math("MAXIMUM", n.math("MULTIPLY", recess, 1.1), n.math("MULTIPLY", drip, 0.55))
        verd = n.math("MULTIPLY", verd, n.range(n.noise(obj(n), 4.0, 3.0), 0.3, 0.6, 0.35, 1.0), clamp=True)
        return edge, recess, oxide, rust, verd

    def copper_albedo(n):
        edge, recess, oxide, rust, verd = copper_masks(n)
        mid = n.noise(obj(n), 11.0, 3.0, 0.6)
        col = n.mix(n.rgb((0.44, 0.2, 0.1)), (0.62, 0.31, 0.17), n.range(mid, 0.38, 0.66, 0.0, 1.0))
        col = n.mix(col, (0.16, 0.075, 0.045), oxide)
        col = n.mix(col, (0.26, 0.095, 0.035), rust)
        col = n.mix(col, (0.19, 0.36, 0.29), verd)
        col = n.mix(col, (0.08, 0.06, 0.05), n.math("MULTIPLY", recess, 0.35))
        col = n.mix(col, (0.8, 0.44, 0.27), n.math("MULTIPLY", edge, 0.8))
        col = n.mix(col, (0.55, 0.3, 0.2), n.math("MULTIPLY", scratches(n), 0.25))
        return grime(n, n.mul(col, n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 40.0, 2.0), 0.18), 0.91)))

    def copper_rough(n):
        edge, recess, oxide, rust, verd = copper_masks(n)
        r = n.math("ADD", 0.32, n.math("MULTIPLY", oxide, 0.3))
        r = n.math("ADD", r, n.math("MULTIPLY", n.math("MAXIMUM", rust, verd), 0.45))
        r = n.math("ADD", r, n.math("MULTIPLY", n.noise(obj(n), 30.0, 2.0), 0.08))
        return n.math("SUBTRACT", r, n.math("MULTIPLY", edge, 0.15), clamp=True)

    def copper_metal(n):
        edge, recess, oxide, rust, verd = copper_masks(n)
        m_ = n.math("SUBTRACT", 0.95, n.math("MULTIPLY", oxide, 0.45))
        m_ = n.math("SUBTRACT", m_, n.math("MULTIPLY", n.math("MAXIMUM", rust, verd), 0.85))
        return n.math("ADD", m_, n.math("MULTIPLY", edge, 0.6), clamp=True)

    def hammered(n, scale=22.0, k=1.0):
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "SMOOTH_F1"
        n.link(obj(n), vor.inputs["Vector"])
        vor.inputs["Scale"].default_value = scale
        dents = n.math("MULTIPLY", vor.outputs["Distance"], 0.5 * k)
        fine = n.math("MULTIPLY", n.noise(obj(n), 200.0, 2.0), 0.15 * k)
        return n.math("SUBTRACT", n.math("ADD", dents, fine), n.math("MULTIPLY", scratches(n), 0.25 * k))

    def copper_height(n):
        _e, _r, _o, rust, verd = copper_masks(n)
        h = hammered(n, 18.0, 0.8)
        h = n.math("ADD", h, n.math("MULTIPLY", pits(n, 120.0), n.math("MULTIPLY", rust, 0.5)))
        return n.math("ADD", h, n.math("MULTIPLY", n.noise(obj(n), 90.0, 3.0), n.math("MULTIPLY", verd, 0.4)))

    uk.look("copper", copper_albedo, copper_height, copper_rough, copper_metal, bump=0.002)

    # brass: polished where it is handled and on its edges, tarnished brown
    # and a little green in its recesses
    def brass_masks(n):
        edge, recess = wear(n, 0.52, 0.58)
        tarnish = n.math("MAXIMUM", recess, n.range(n.noise(obj(n), 4.0, 4.0, 0.6), 0.45, 0.7, 0.0, 0.7))
        return edge, recess, tarnish

    def brass_albedo(n):
        edge, recess, tarnish = brass_masks(n)
        col = n.mix(n.rgb((0.62, 0.46, 0.2)), (0.76, 0.6, 0.3), n.range(n.noise(obj(n), 14.0, 3.0), 0.35, 0.65, 0.0, 1.0))
        col = n.mix(col, (0.25, 0.18, 0.08), n.math("MULTIPLY", tarnish, 0.75))
        col = n.mix(col, (0.18, 0.28, 0.2), n.math("MULTIPLY", recess, 0.4))
        return grime(n, n.mix(col, (0.92, 0.78, 0.46), n.math("MULTIPLY", edge, 0.85)))

    def brass_rough(n):
        edge, recess, tarnish = brass_masks(n)
        r = n.math("ADD", 0.22, n.math("MULTIPLY", tarnish, 0.35))
        return n.math("SUBTRACT", r, n.math("MULTIPLY", edge, 0.1), clamp=True)

    def brass_metal(n):
        edge, recess, tarnish = brass_masks(n)
        return n.math("SUBTRACT", 1.0, n.math("MULTIPLY", recess, 0.5), clamp=True)

    uk.look("brass", brass_albedo, lambda n: hammered(n, 30.0, 0.4), brass_rough, brass_metal, bump=0.0015)

    # cast iron: nearly black, pitted, rusting in its recesses and in
    # streaks, worn to bare metal on its edges
    def iron_masks(n):
        edge, recess = wear(n)
        streak = n.range(n.noise(n.scale(world(n), (20.0, 20.0, 2.5)), 1.0, 4.0, 0.6), 0.56, 0.7, 0.0, 0.7)
        rust = n.math("MAXIMUM", n.math("MULTIPLY", recess, 0.9), streak)
        rust = n.math("MAXIMUM", rust, n.range(n.noise(obj(n), 6.0, 5.0, 0.7), 0.6, 0.7, 0.0, 0.8))
        return edge, recess, rust

    def iron_albedo(n):
        edge, recess, rust = iron_masks(n)
        col = n.mix(n.rgb((0.06, 0.056, 0.052)), (0.1, 0.095, 0.088), n.range(n.noise(obj(n), 6.0, 4.0, 0.6), 0.4, 0.7, 0.0, 1.0))
        col = n.mix(col, (0.2, 0.08, 0.03), rust)
        col = n.mix(col, (0.34, 0.14, 0.05), n.math("MULTIPLY", rust, n.range(n.noise(obj(n), 30.0, 3.0), 0.55, 0.7, 0.0, 0.7)))
        col = n.mix(col, (0.32, 0.31, 0.3), n.math("MULTIPLY", edge, 0.7))
        return grime(n, n.mul(col, n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 50.0, 2.0), 0.2), 0.9)))

    def iron_rough(n):
        edge, recess, rust = iron_masks(n)
        r = n.math("ADD", 0.55, n.math("MULTIPLY", rust, 0.35))
        return n.math("SUBTRACT", r, n.math("MULTIPLY", edge, 0.25), clamp=True)

    def iron_metal(n):
        edge, recess, rust = iron_masks(n)
        m_ = n.math("SUBTRACT", 0.6, n.math("MULTIPLY", rust, 0.6))
        return n.math("ADD", m_, n.math("MULTIPLY", edge, 0.4), clamp=True)

    uk.look("iron", iron_albedo, lambda n: n.math("ADD", pits(n, 140.0), hammered(n, 26.0, 0.6)), iron_rough, iron_metal, bump=0.0025)

    def steel_albedo(n):
        edge, recess = wear(n, 0.52, 0.57)
        brushed = n.math("ADD", n.math("MULTIPLY", n.noise(n.scale(obj(n), (40.0, 0.8, 40.0)), 4.0, 2.0), 0.14), 0.86)
        col = n.mul(n.rgb((0.56, 0.56, 0.58)), brushed)
        col = n.mix(col, (0.2, 0.12, 0.08), n.math("MULTIPLY", recess, 0.5))
        col = n.mix(col, (0.3, 0.15, 0.08), n.range(n.noise(obj(n), 9.0, 4.0, 0.7), 0.64, 0.72, 0.0, 0.6))
        return n.mix(col, (0.86, 0.86, 0.88), n.math("MULTIPLY", edge, 0.8))

    uk.look("steel", steel_albedo, lambda n: n.math("ADD", n.math("MULTIPLY", scratches(n), 0.6), n.noise(n.scale(obj(n), (60.0, 1.0, 60.0)), 6.0, 2.0)),
            lambda n: n.math("ADD", 0.22, n.math("MULTIPLY", n.noise(n.scale(obj(n), (30.0, 1.0, 30.0)), 6.0, 2.0), 0.14)), 1.0, bump=0.002)

    # coil: copper wire wound round the shoulders, darker between the turns
    def wire(n):
        return n.wave(obj(n), 260.0, "X", 0.0, "SIN")

    def coil_albedo(n):
        edge, recess = wear(n)
        col = n.mix(n.rgb((0.26, 0.11, 0.05)), (0.66, 0.33, 0.17), wire(n))
        col = n.mix(col, (0.2, 0.09, 0.05), n.range(n.noise(obj(n), 3.0, 4.0, 0.6), 0.45, 0.7, 0.0, 0.7))
        col = n.mix(col, (0.2, 0.34, 0.27), n.math("MULTIPLY", recess, 0.7))
        return n.mix(col, (0.85, 0.5, 0.3), n.math("MULTIPLY", edge, 0.5))

    uk.look("coil", coil_albedo, wire, lambda n: n.math("ADD", 0.28, n.math("MULTIPLY", n.noise(obj(n), 6.0, 2.0), 0.25)), 0.9, bump=0.0015, ao=1.3)

    uk.look("soot", lambda n: n.mix(n.rgb((0.022, 0.02, 0.018)), (0.09, 0.05, 0.03), n.range(n.noise(obj(n), 20.0, 3.0), 0.45, 0.7, 0.0, 1.0)),
            lambda n: n.noise(obj(n), 80.0, 3.0), 0.85, 0.15, bump=0.001, ao=0.6)

    uk.look("ember", lambda n: n.mix(n.rgb((0.75, 0.16, 0.02)), (1.0, 0.5, 0.12), n.range(n.noise(obj(n), 25.0, 4.0, 0.6), 0.4, 0.7, 0.0, 1.0)),
            None, 0.7, 0.0, ao=0.0)

    # enamel: the player's colour, glossy, chipped to the iron on its edges,
    # grimy in its corners
    def enamel_masks(n):
        edge, recess = wear(n, 0.52, 0.6)
        chip = n.range(n.noise(obj(n), 18.0, 4.0, 0.7), 0.6, 0.64, 0.0, 1.0)
        chips = n.math("MAXIMUM", n.math("MULTIPLY", edge, chip), n.range(n.noise(obj(n), 40.0, 3.0, 0.7), 0.7, 0.73, 0.0, 1.0))
        return edge, recess, chips

    def enamel_albedo(n):
        edge, recess, chips = enamel_masks(n)
        col = n.mul(n.rgb((0.8, 0.8, 0.8)), n.math("ADD", n.math("MULTIPLY", n.noise(obj(n), 5.0, 4.0, 0.6), 0.3), 0.78))
        col = n.mix(col, (0.36, 0.34, 0.32), n.math("MULTIPLY", recess, 0.7))
        col = n.mix(col, (0.46, 0.42, 0.38), n.math("MULTIPLY", n.range(n.noise(n.scale(world(n), (14.0, 14.0, 3.0)), 1.0, 3.0), 0.5, 0.7, 0.0, 1.0), 0.6))
        col = n.mix(col, (0.55, 0.4, 0.3), n.math("MULTIPLY", n.range(n.noise(obj(n), 9.0, 4.0, 0.7), 0.6, 0.7, 0.0, 1.0), 0.5))
        return grime(n, n.mix(col, (0.09, 0.08, 0.075), chips))

    def enamel_height(n):
        edge, recess, chips = enamel_masks(n)
        return n.math("SUBTRACT", n.math("MULTIPLY", n.noise(obj(n), 120.0, 2.0), 0.15), chips)

    uk.look("teamcolor_enamel", enamel_albedo, enamel_height, lambda n: n.math("ADD", n.math("ADD", 0.38, n.math("MULTIPLY", n.noise(obj(n), 8.0, 3.0), 0.15)), n.math("MULTIPLY", enamel_masks(n)[2], 0.4)),
            lambda n: n.math("MULTIPLY", enamel_masks(n)[2], 0.5), bump=0.0015)


# ---------------------------------------------------------------- assembly

def build():
    """The rig and every part on it, joined into one object per piece (the
    head apart, for its texels). Returns (rig, nodes, parts)."""
    rig = uk.Rig(MODEL)
    M = materials()
    looks()
    nodes = rig.nodes()
    groups = {"torso": sm.torso(rig, M), "hip": sm.hip(rig, M), "lupleg": sm.leg(rig, M), "lfoot": sm.foot(rig, M), "ltoe": sm.toe(rig, M),
              "luparm": sm.shoulder(rig, M), "larm": sm.forearm(rig, M), "lhand": sm.fist(rig, M), "spindle": sm.spindle_parts(rig, M)}
    head = sm.head(M)
    for piece, right in MIRROR.items():
        groups[right] = [uk.mirror_x(o, o.name.replace("_L", "") + "_R") for o in groups[piece]]
    groups["lhand"] += sm.shield(rig, M)
    groups["rhand"] += sm.hilt(rig, M)
    groups.update(sm.jaws(rig, M))
    parts = {}
    for piece, obs in groups.items():
        ob = uk.join(obs, piece + "_geo")
        uk.attach(ob, nodes[piece])
        parts[piece] = [ob]
    ob = uk.join(head, "head")
    uk.attach(ob, nodes["torso"])
    parts["torso"].append(ob)
    return rig, nodes, parts


T = V((0.0, 0.0, 2.45))
VIEWS = {
    "front": dict(yaw=0, pitch=5, dist=13.0, lens=55, target=T),
    "low34": dict(yaw=38, pitch=2, dist=13.0, lens=55, target=T),
    "classic": dict(yaw=0, pitch=uk.CLASSIC_PITCH, dist=40, ortho=7.0, target=V((0.0, 0.0, 2.2))),
    "face": dict(yaw=16, pitch=6, dist=3.4, lens=60, target=V((0.0, -0.2, 3.6))),
    "back34": dict(yaw=150, pitch=8, dist=13.0, lens=55, target=T),
}
ANIMS = (("walk", 0, 30, 6), ("attack1", 0, 36, 6), ("attack3", 0, 60, 6), ("startbuild", 0, 40, 6))


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--review", default=None)
    ap.add_argument("--rig", default=None)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--quick", action="store_true", help="review the stills only")
    ap.add_argument("--size", type=int, default=2048, help="the atlas's side")
    a = ap.parse_args(argv)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rig, nodes, parts = build()
    obs = [o for ps in parts.values() for o in ps]
    print("SAGE_TRIANGLES", uk.triangles(obs))
    for piece, ps in sorted(parts.items()):
        print("  %-12s %6d" % (piece, uk.triangles(ps)))
    if not a.no_bake:
        uk.unwrap(obs, scale={"head": 1.6})
        print("SAGE_SLIVERS", uk.tuck_slivers(obs, a.size))
        uk.bake_maps(obs, a.size, name=MODEL)
        uk.rest(nodes, rig)
        uk.export(nodes[rig.pieces[0].name], a.out)
    if a.review:
        import review
        review.run(rig, nodes, a.review, a.rig, quick=a.quick, views=VIEWS, anims=ANIMS, target=V((0.0, 0.0, 2.5)), dist=13.0)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
