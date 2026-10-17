"""Review renders of Lokken beside the original, inside Blender, for
checking only (lokken.py --review DIR). The engine's pose dump does not
pose TARNECRO, so the poses come from tarnecro.cob in the player's files:
the idle pose, and the walk's and the first cast's keyframes, each turn
taken at its target. These pictures hold the original's art: they stay on
the reviewer's machine and never go into the repository.
"""
import json
import math
import os
import struct

import bpy
from mathutils import Euler, Vector

import review
import unitkit as uk

V = Vector
TARGET = V((0.0, -0.3, 2.0))


def keyframes(script, start=None, model="tarnecro"):
    """The script's turns read straight through, a pose at each sleep:
    [{piece: (x, y, z) degrees here}], the 3DO's y turn about z here."""
    d = uk.game_file("scripts/%s.cob" % model)
    _v, ns, npc, ncode, _st, _r, oso, osn, opn, ocode = struct.unpack_from("<10I", d, 0)

    def cstr(o):
        return d[o:d.index(b"\0", o)].decode("latin-1")

    offs = struct.unpack_from("<%dI" % ns, d, oso)
    names = [cstr(o) for o in struct.unpack_from("<%dI" % ns, d, osn)]
    pieces = [cstr(o) for o in struct.unpack_from("<%dI" % npc, d, opn)]
    code = struct.unpack_from("<%dI" % ncode, d, ocode)
    i = names.index(script)
    pc, end = offs[i], min([o for o in offs if o > offs[i]] + [ncode])
    width = {0x10001000: 2, 0x10002000: 2, 0x1000b000: 2, 0x1000c000: 2, 0x10021000: 1, 0x10023000: 1, 0x10011000: 2, 0x10012000: 2,
             0x10061000: 2, 0x10062000: 2, 0x10064000: 1, 0x10066000: 1, 0x10071000: 1, 0x10005000: 1, 0x10006000: 1, 0x1000f000: 1,
             0x10003000: 2, 0x10004000: 2, 0x10072000: 1, 0x10073000: 2, 0x10074000: 2, 0x10075000: 1, 0x10007000: 1, 0x10008000: 1,
             0x10009000: 1, 0x1000a000: 1, 0x1000d000: 1, 0x1000e000: 1, 0x10063000: 2}
    pose = {k: list(v) for k, v in (start or {}).items()}
    out, stack = [], []
    while pc < end:
        op = code[pc] & 0x100ff000
        n = width.get(op, 0)
        args = code[pc + 1:pc + 1 + n]
        if op == 0x10021000 and code[pc] & 0xf == 1:
            v = args[0]
            stack.append(v - (1 << 32) if v >= 1 << 31 else v)
        elif op in (0x10002000, 0x1000c000) and stack:
            name, axis = pieces[args[0]], args[1]
            deg = stack[-1] * 360.0 / 65536.0
            p = pose.setdefault(name, [0.0, 0.0, 0.0])
            p[(0, 2, 1)[axis]] = deg
            stack = []
        elif op == 0x10013000:
            out.append({k: tuple(v) for k, v in pose.items()})
            stack = []
        else:
            stack = [] if op != 0x10021000 else stack
        pc += 1 + n
    out.append({k: tuple(v) for k, v in pose.items()})
    return out


def pose(nodes, rig, turns, suffix=""):
    for p in rig.pieces:
        e = turns.get(p.name, (0.0, 0.0, 0.0))
        nodes[p.name + suffix if suffix else p.name].rotation_euler = Euler([math.radians(a) for a in e], "XYZ")
    bpy.context.view_layer.update()


VIEWS = {
    "front": dict(yaw=0, pitch=5, dist=13.0, lens=55),
    "low34": dict(yaw=38, pitch=2, dist=13.0, lens=55),
    "side": dict(yaw=90, pitch=3, dist=13.0, lens=55),
    "back34": dict(yaw=150, pitch=8, dist=13.0, lens=55),
    "classic": dict(yaw=0, pitch=uk.CLASSIC_PITCH, dist=40, ortho=6.5, target=V((0.0, -0.3, 1.8))),
    "face": dict(yaw=14, pitch=4, dist=2.4, lens=70, target=V((0.0, -0.95, 3.15))),
}


def run(rig, nodes, out, idle, quick=False, samples=48):
    """idle is the idle pose's turns. Writes old_ and new_ pictures and a
    manifest for sheets.py."""
    os.makedirs(out, exist_ok=True)
    onodes, coll = uk.original(rig)
    onodes = {k: v for k, v in onodes.items()}
    uk.stage()
    review.tint()
    manifest = {"views": [], "turn": [], "anims": {}}

    def show(remaster):
        for ob in coll.all_objects:
            ob.hide_render = remaster
        for n in nodes.values():
            for c in n.children:
                if c.type == "MESH":
                    c.hide_render = not remaster

    def both(tag, cam, w, h, s=samples):
        show(True)
        uk.render(os.path.join(out, "new_%s.png" % tag), cam, w, h, s)
        show(False)
        uk.render(os.path.join(out, "old_%s.png" % tag), cam, w, h, s)
        show(True)

    def put(turns):
        pose(nodes, rig, turns)
        for p in rig.pieces:
            e = turns.get(p.name, (0.0, 0.0, 0.0))
            onodes[p.name].rotation_euler = Euler([math.radians(a) for a in e], "XYZ")
        bpy.context.view_layer.update()

    put(idle)
    for k, v in VIEWS.items():
        cam = uk.camera("rv_" + k, v.get("target", TARGET), v["yaw"], v["pitch"], v["dist"], v.get("lens", 50), v.get("ortho"))
        both(k, cam, 900, 900)
        manifest["views"].append(k)
    if not quick:
        show(True)
        for i in range(8):
            cam = uk.camera("rv_turn", TARGET, i * 45, 8, 13.0, 55)
            uk.render(os.path.join(out, "turn_%d.png" % i), cam, 600, 600, samples)
            manifest["turn"].append(i * 45)
        cam = uk.camera("rv_anim", TARGET, 35, 10, 14.0, 50)
        for anim, take in (("walk", (0, 3, 6, 9, 12)), ("attack1", (0, 2, 4, 6, 8))):
            frames = keyframes(anim, idle)
            take = [t for t in take if t < len(frames)]
            manifest["anims"][anim] = take
            for t in take:
                put(frames[t])
                both("%s_%02d" % (anim, t), cam, 480, 480, 32)
        put(idle)
    json.dump(manifest, open(os.path.join(out, "manifest.json"), "w"), indent=1)
    print("REVIEW", out)
