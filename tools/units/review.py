"""Review renders of a remastered unit beside its original, inside Blender.

    review.run(rig, nodes, out_dir, rig_json)

Renders, into out_dir, the remaster and the original from the player's
files under the same cameras: from the front, a low three-quarter and the
classic camera, standing as the game shows them at rest (the script's
restore_x, when a pose dump from the engine is given). Then a turntable of
the remaster and, with the dump, frames of each animation for both. Then
`python tools/units/sheets.py out_dir` lays them out as labelled sheets.
These pictures hold the original's art: they stay on the reviewer's
machine and never go into the repository.
"""
import json
import math
import os

import bpy
from mathutils import Vector

import unitkit as uk

TARGET = Vector((0.0, 0.0, 2.05))
VIEWS = {
    "front": dict(yaw=0, pitch=5, dist=8.6, lens=55),
    "low34": dict(yaw=38, pitch=2, dist=8.6, lens=55),
    "classic": dict(yaw=0, pitch=uk.CLASSIC_PITCH, dist=40, ortho=4.6),
    "face": dict(yaw=12, pitch=6, dist=2.6, lens=70, target=Vector((0.0, 0.0, 3.6))),
    "back34": dict(yaw=150, pitch=8, dist=8.6, lens=55),
}
# The player's colour the review shows: the engine's first, blue.
TINT = (40 / 255.0, 90 / 255.0, 220 / 255.0)


def tint(colour=TINT):
    """Multiplies every material named for the team by a player's colour,
    as the game does, for the review renders only."""
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in colour]
    for m in bpy.data.materials:
        if "team" not in m.name.lower() or not m.use_nodes:
            continue
        nt = m.node_tree
        b = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if b is None:
            continue
        inp = b.inputs["Base Color"]
        if inp.links:
            src = inp.links[0].from_socket
            mix = nt.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.inputs["Factor"].default_value = 1.0
            mix.inputs["B"].default_value = (*lin, 1.0)
            nt.links.new(src, mix.inputs["A"])
            nt.links.new(mix.outputs["Result"], inp)
        else:
            c = inp.default_value
            inp.default_value = (c[0] * lin[0], c[1] * lin[1], c[2] * lin[2], 1.0)


ANIMS = (("walk", 0, 32, 6), ("attack1", 0, 30, 6), ("attack2", 0, 36, 6), ("attack3", 0, 60, 6), ("startbuild", 0, 45, 6))


def _show(coll, nodes, show):
    for ob in coll.all_objects if coll is not None else []:
        ob.hide_render = not show
    if nodes is not None:
        for n in nodes.values():
            for c in n.children:
                if c.type == "MESH":
                    c.hide_render = not show


def _idle(rig, nodes, poses):
    if poses is not None and "restore_x" in poses.anims:
        uk.pose(nodes, rig, poses, "restore_x", poses.frames("restore_x") - 1)
    else:
        uk.rest(nodes, rig)


def run(rig, nodes, out, rig_json=None, colour=0, samples=48, quick=False):
    os.makedirs(out, exist_ok=True)
    poses = uk.Poses(rig_json) if rig_json else None
    onodes, coll = uk.original(rig, colour=colour)
    uk.stage()
    manifest = {"views": [], "turn": [], "anims": {}}
    cams = {k: uk.camera("rv_" + k, v.get("target", TARGET), v["yaw"], v["pitch"], v["dist"], v.get("lens", 50), v.get("ortho"))
            for k, v in VIEWS.items()}
    tint()

    def both(tag, cam, w, h, s=samples):
        _show(coll, None, False)
        _show(None, nodes, True)
        _hidden_by_pose(nodes)
        a = uk.render(os.path.join(out, "new_%s.png" % tag), cam, w, h, s)
        _show(None, nodes, False)
        _show(coll, None, True)
        _hidden_by_pose(onodes)
        b = uk.render(os.path.join(out, "old_%s.png" % tag), cam, w, h, s)
        _show(None, nodes, True)
        return a, b

    _idle(rig, nodes, poses)
    _idle(rig, onodes, poses)
    for k, cam in cams.items():
        both(k, cam, 900, 900)
        manifest["views"].append(k)
    if quick:
        json.dump(manifest, open(os.path.join(out, "manifest.json"), "w"), indent=1)
        return
    # the turntable: the remaster alone, eight turns, low and level
    _show(coll, None, False)
    for i in range(8):
        cam = uk.camera("rv_turn", TARGET, i * 45, 8, 8.6, 55)
        uk.render(os.path.join(out, "turn_%d.png" % i), cam, 600, 600, samples)
        manifest["turn"].append(i * 45)
    if poses is not None:
        cam = uk.camera("rv_anim", TARGET, 30, 10, 9.5, 50)
        for anim, f0, f1, count in ANIMS:
            if anim not in poses.anims:
                continue
            frames = [round(f0 + (f1 - f0) * j / (count - 1)) for j in range(count)]
            manifest["anims"][anim] = frames
            for f in frames:
                uk.pose(nodes, rig, poses, anim, f)
                uk.pose(onodes, rig, poses, anim, f)
                both("%s_%02d" % (anim, f), cam, 480, 480, 32)
        _idle(rig, nodes, poses)
    json.dump(manifest, open(os.path.join(out, "manifest.json"), "w"), indent=1)
    print("REVIEW", out)


def _hidden_by_pose(nodes):
    """Leaves pieces the script hides hidden (uk.pose marked them)."""
    return nodes
