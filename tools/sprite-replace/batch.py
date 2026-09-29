"""Carve many sprite features in one Blender session, most used first.

    blender -b --factory-startup -P tools/sprite-replace/batch.py -- <catalog dir> <out dir> [first] [count]

For each feature in catalog.json (sorted by how many maps use it) this
writes <out dir>/models/<feature>.glb and quick thumbnails,
<out dir>/thumbs/<feature>_classic.png and _turned.png for contact sheets,
both lit like the game, and _game.png, the classic view again. Solid
models carry COLOR_0, a gain the game multiplies in to undo lighting the
sprite's painted light a second time. Waves and
Noise are left out. Features already carved are skipped, so a batch can be
stopped and resumed.

A model ships as geometry: its painted materials name their sprite in
okPaint and the game paints them from the player's own files at load
(okpaint.py). OK_KEEP_PIXELS=1 keeps the pixels for review on this machine,
and such models go only to unity/Assets/Overrides/Generated, which git
ignores. A ruin's rubble skirt is cut by a mask the game cannot repeat, so
a ruin exports only with its pixels.
"""
import json
import math
import os
import sys
import time

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import arch  # noqa: E402
import frond  # noqa: E402
import carve  # noqa: E402
import okpaint  # noqa: E402


SHAPES = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "shapes.json")))
# the game draws its own sea foam, and Noise features are unseen sound emitters
SKIP_CATEGORIES = ("waves", "noise")
# the game's sun (Atmosphere.cs) and trilight ambient, for the game-lit thumbnail
GAME_SUN_PITCH, GAME_SUN_YAW = 48.0, 150.0
GAME_SUN_COLOUR = (1.0, 0.96, 0.88)
GAME_AMBIENT = ((0.28, 0.26, 0.22), (0.5, 0.5, 0.48), (0.62, 0.68, 0.78))  # ground, equator, sky
GAME_AMBIENT_INTENSITY = 0.85
GAME_POST_EXPOSURE = 0.1  # stops, before the Neutral tonemapper
# Light on a white face under those lights, measured on a sphere: ambient
# 0.412 + 0.122 n.z plus sun 0.951 n.l, so flat ground gets 1.241. A solid's
# COLOR_0 lifts the faces lit less than the ground, by at most GAIN_TOP: at
# 2.4 solids come out as bright against their sprites as flat decals do.
GAIN_AMBIENT, GAIN_SKY, GAIN_SUN = 0.412, 0.122, 0.951
GAIN_TOP = 2.4


def shape_of(name):
    """The hand classification in shapes.json, if the feature has one."""
    import fnmatch
    for shape, names in SHAPES.items():
        if shape.startswith("_"):
            continue
        for pat in names:
            if fnmatch.fnmatchcase(name, pat) or fnmatchcase_ci(name, pat):
                return shape
    return None


def fnmatchcase_ci(name, pat):
    import fnmatch
    return fnmatch.fnmatchcase(name.lower(), pat.lower())


def is_groundcover(r):
    """Living ground cover, laid as a decal with a little relief."""
    d = r["description"].lower()
    return any(w in d for w in ("plant", "groundcover", "ivy")) and not any(w in d for w in arch.BROKEN_WORDS + ("dead",))


def is_low_wreck(r):
    """Wreckage that lies on the ground: no height, or a ruin 20 or less."""
    words = (r["description"] + " " + r["category"]).lower()
    return r["height"] == 0 or (r["height"] <= 20 and any(w in words for w in arch.BROKEN_WORDS))


def sibling(r, rows):
    """A ruin's intact piece and the kind it is built as, or None when it
    has none to be cut from."""
    s = rows.get(arch.sibling_name(r["name"]) or "")
    if not s or not s.get("sprite") or is_low_wreck(r):
        return None
    shape = shape_of(s["name"]) or s.get("shape")
    kind = shape if shape in arch.HAND_KINDS else (None if shape else arch.kind_of(s))
    return (s, kind) if kind in arch.RUIN_KINDS else None


def route(r, spr, rows=None):
    """The row with its hand shape, the arch kind, whether it is a decal,
    and whether it keeps the sprite's alpha (fronds and decals). rows, the
    catalog by name, lets a ruin find its intact sibling."""
    shape = shape_of(r["name"]) or r.get("shape")
    if shape:
        r = dict(r, shape=shape)
    kind = shape if shape in arch.HAND_KINDS else (None if shape else arch.kind_of(r))
    if kind == "ruin":
        sib = sibling(r, rows or {})
        kind = "ruin" if sib else None
        if sib:
            r = dict(r, sibling=sib[0], sibling_kind=sib[1])
    decal = shape == "decal" or (not shape and not kind and (frond.is_decal(r) or is_low_wreck(r)))
    if shape in ("palm", "fern"):
        fr = True
    elif shape in ("crown", "poplar", "bush", "conifer", "spire", "stone"):
        fr = False
    else:
        fr = not kind and (decal or frond.is_frond(r, spr))
    return r, kind, decal, fr


def luma(path):
    """Mean luminance of an image's opaque pixels, 0 to 1."""
    import numpy as np
    img = bpy.data.images.load(path, check_existing=False)
    a = np.array(img.pixels[:], dtype=np.float32).reshape(-1, 4)
    bpy.data.images.remove(img)
    a = a[a[:, 3] > 0.5]
    return float((a[:, :3] @ np.array([0.299, 0.587, 0.114], np.float32)).mean()) if len(a) else 0.0


def sun_forward():
    """The way the game's sunlight travels, in Blender axes (Unity's x, y, z
    are Blender's x, z, y)."""
    p, y = math.radians(GAME_SUN_PITCH), math.radians(GAME_SUN_YAW)
    return Vector((math.sin(y) * math.cos(p), math.cos(y) * math.cos(p), -math.sin(p)))


def light_gain(ob):
    """COLOR_0 for a solid: the sprite has its light painted in and the game
    lights the model again, so each corner the game lights less than flat
    ground gets the gain back to the ground's light, at most GAIN_TOP.
    Faces lit at least as well as the ground keep 1. The material multiplies
    it in, as glTF does, and cut-out materials are left plain."""
    import numpy as np
    me = ob.data
    n = np.empty(len(me.loops) * 3, np.float32)
    me.corner_normals.foreach_get("vector", n)
    n = n.reshape(-1, 3)
    to_sun = -np.array(sun_forward(), np.float32)
    lit = GAIN_AMBIENT + GAIN_SKY * n[:, 2] + GAIN_SUN * np.maximum(0.0, n @ to_sun)
    ground = GAIN_AMBIENT + GAIN_SKY + GAIN_SUN * float(to_sun[2])
    g = np.clip(ground / lit, 1.0, GAIN_TOP)
    # cut-out faces keep 1: the gain would reach them through COLOR_0 too
    cut = [i for i, sl in enumerate(ob.material_slots)
           if sl.material and sl.material.node_tree.nodes["Principled BSDF"].inputs["Alpha"].is_linked]
    if cut:
        mi = np.empty(len(me.loops), np.int32)
        for p in me.polygons:
            mi[p.loop_start:p.loop_start + p.loop_total] = p.material_index
        g[np.isin(mi, cut)] = 1.0
    at = me.color_attributes.new("gain", "FLOAT_COLOR", "CORNER")
    at.data.foreach_set("color", np.column_stack([g, g, g, np.ones_like(g)]).ravel())
    for slot in ob.material_slots:
        nt = slot.material.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        base = bsdf.inputs["Base Color"]
        if bsdf.inputs["Alpha"].is_linked:
            continue
        col = nt.nodes.new("ShaderNodeVertexColor")
        col.layer_name = "gain"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type, mix.blend_type = "RGBA", "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        if base.is_linked:
            nt.links.new(base.links[0].from_socket, mix.inputs[6])
        else:
            mix.inputs[6].default_value = base.default_value
        nt.links.new(col.outputs["Color"], mix.inputs[7])
        nt.links.new(mix.outputs[2], base)
    return float(g.mean())


def game_light(scene):
    """The game's sun and a sky-to-ground ambient."""
    fwd = sun_forward()
    sun = bpy.data.objects.new("game_sun", bpy.data.lights.new("game_sun", "SUN"))
    # strength pi lights a white face to 1, like Unity's intensity 1
    sun.data.energy = math.pi
    sun.data.color = GAME_SUN_COLOUR
    sun.rotation_euler = fwd.to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(sun)
    w = bpy.data.worlds.new("game_sky")
    w.use_nodes = True
    nt = w.node_tree
    tc, sep = nt.nodes.new("ShaderNodeTexCoord"), nt.nodes.new("ShaderNodeSeparateXYZ")
    mr, ramp = nt.nodes.new("ShaderNodeMapRange"), nt.nodes.new("ShaderNodeValToRGB")
    mr.inputs["From Min"].default_value = -1.0
    els = ramp.color_ramp.elements
    els[0].color, els[1].color = (*GAME_AMBIENT[0], 1.0), (*GAME_AMBIENT[2], 1.0)
    els.new(0.5).color = (*GAME_AMBIENT[1], 1.0)
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], ramp.inputs["Fac"])
    bg = nt.nodes["Background"]
    nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = GAME_AMBIENT_INTENSITY
    scene.world = w


def neutral(x):
    """Unity URP's Neutral tonemapper (NeutralTonemap in Color.hlsl)."""
    a, b, c, d, e, f = 0.2, 0.29, 0.24, 0.272, 0.02, 0.3

    def curve(v):
        return (v * (a * v + c * b) + d * e) / (v * (a * v + b) + d * f) - e / f
    scale = 1.0 / curve(5.3)
    return curve(x * scale) * scale


def game_png(scene, exr, png):
    """The linear game render through the game's post exposure and
    tonemapper, saved as an 8-bit sRGB PNG."""
    import numpy as np
    src = bpy.data.images.load(exr, check_existing=False)
    w, h = src.size
    px = np.array(src.pixels[:], dtype=np.float32).reshape(-1, 4)
    bpy.data.images.remove(src)
    os.remove(exr)
    # float buffers hold premultiplied colour: tonemap the straight colour
    al = px[:, 3:4]
    rgb = np.where(al > 0, px[:, :3] / np.maximum(al, 1e-6), 0.0)
    px[:, :3] = neutral(np.maximum(rgb, 0.0) * 2 ** GAME_POST_EXPOSURE) * al
    img = bpy.data.images.new("game_png", w, h, alpha=True, float_buffer=True)
    img.pixels[:] = px.ravel()
    st = scene.render.image_settings
    st.file_format, st.color_mode, st.color_depth = "PNG", "RGBA", "8"
    img.save_render(png, scene=scene)
    bpy.data.images.remove(img)


def thumbs(ob, out, name, sprite_png=None):
    """The classic view and a turned one, both lit like the game (its sun,
    sky ambient, tonemapper and the model's COLOR_0 gain), so a review sees
    what the game shows; _game repeats the classic view."""
    import shutil
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 16
    scene.cycles.max_bounces = 0
    scene.cycles.use_denoising = False
    scene.view_settings.view_transform = "Standard"
    scene.render.resolution_x = 256
    scene.render.resolution_y = 256
    scene.render.film_transparent = True
    game_light(scene)
    # roughness 1 and no gloss in the game, only for these renders
    bsdfs = [s.material.node_tree.nodes["Principled BSDF"] for s in ob.material_slots
             if s.material and s.material.use_nodes and "Principled BSDF" in s.material.node_tree.nodes]
    spec = [(b, b.inputs["Specular IOR Level"].default_value) for b in bsdfs]
    for b, _ in spec:
        b.inputs["Specular IOR Level"].default_value = 0.0
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    lo = Vector([min(v.co[i] for v in ob.data.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for v in ob.data.vertices) for i in range(3)])
    centre = (lo + hi) / 2
    span = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z)
    paths = []
    for label, az, el in (("classic", -math.pi / 2, math.atan(1.0 / carve.TILT)),
                          ("turned", -math.pi / 2 + 0.9, math.radians(30))):
        cam.location = centre + Vector((math.cos(az) * math.cos(el) * 50, math.sin(az) * math.cos(el) * 50,
                                        math.sin(el) * 50))
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.ortho_scale = span * 1.2
        path = os.path.join(out, "thumbs", "%s_%s.png" % (name, label))
        scene.render.image_settings.file_format = "OPEN_EXR"
        scene.render.filepath = path[:-4] + ".exr"
        bpy.ops.render.render(write_still=True)
        game_png(scene, scene.render.filepath, path)
        paths.append(path)
    shutil.copyfile(paths[0], os.path.join(out, "thumbs", "%s_game.png" % name))
    for b, v in spec:
        b.inputs["Specular IOR Level"].default_value = v
    bpy.data.objects.remove(bpy.data.objects["game_sun"], do_unlink=True)
    if sprite_png:
        s, g = luma(sprite_png), luma(paths[0])
        print("BATCH_LUMA", name, "sprite %.3f game %.3f ratio %.2f" % (s, g, g / s if s else 0.0), flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    catalog, out = argv[0], argv[1]
    first = int(argv[2]) if len(argv) > 2 else 0
    count = int(argv[3]) if len(argv) > 3 else 10 ** 6
    os.makedirs(os.path.join(out, "models"), exist_ok=True)
    os.makedirs(os.path.join(out, "thumbs"), exist_ok=True)
    every = json.load(open(os.path.join(catalog, "catalog.json")))
    by_name = {r["name"]: r for r in every}
    rows = [r for r in every if r.get("sprite") and r["category"].lower() not in SKIP_CATEGORIES]
    names = os.environ.get("ONLY_NAMES")
    if names:
        keep = set(names.split(","))
        rows = [r for r in rows if r["name"] in keep]
    if os.environ.get("ONLY_SHAPED"):
        rows = [r for r in rows if shape_of(r["name"])]
    if os.environ.get("ONLY_FROND"):
        rows = [r for r in rows if not arch.kind_of(r) and (frond.is_decal(r) or frond.is_frond(
            r, carve.Sprite(os.path.join(catalog, "sprites", r["name"] + ".png"))))]
    only = os.environ.get("ONLY_ARCH")
    if only:
        rows = [r for r in rows if arch.kind_of(r)]
    done = failed = 0
    t0 = time.time()
    for r in rows[first:first + count]:
        name = r["name"]
        glb = os.path.join(out, "models", name + ".glb")
        if os.path.exists(glb):
            continue
        try:
            bpy.ops.wm.read_factory_settings(use_empty=True)
            sprite_png = os.path.join(catalog, "sprites", name + ".png")
            spr = carve.Sprite(sprite_png)
            r, kind, decal, fr = route(r, spr, by_name)
            skirt = None
            if decal:
                ob = frond.build_decal(r, spr, relief=is_groundcover(r))
            elif kind == "ruin":
                sib_spr = carve.Sprite(os.path.join(catalog, "sprites", r["sibling"]["name"] + ".png"))
                ob, skirt, keep = arch.build_ruin(r, spr, sib_spr)
                # the rubble the solid does not stand on keeps its alpha
                skirt_spr = spr.copy(keep)
            elif kind:
                ob = arch.build(r, kind, spr)
            elif fr:
                ob = frond.build(r, spr)
            else:
                ob = carve.carve(r, spr)[0]
            if not fr:
                spr.opaque()
            else:
                spr.clear_border()
            carve.paint(ob, r, spr)
            if skirt:
                carve.paint(skirt, r, skirt_spr)
                frond.cut_out(skirt)
                bpy.ops.object.select_all(action="DESELECT")
                skirt.select_set(True)
                ob.select_set(True)
                bpy.context.view_layer.objects.active = ob
                bpy.ops.object.join()
            ob[carve.PLAYERS_FILES] = True
            if r.get("texture"):
                # a model standing in for a painted card of a unit's 3DO
                ob["replacesTexture"] = r["texture"]
                ob["replacesPiece"] = r["piece"]
            if fr:
                frond.cut_out(ob)
            if not fr or ob.get("bark"):
                # of a frond, only a palm's bark takes the gain
                print("BATCH_GAIN", name, "mean %.2f" % light_gain(ob), flush=True)
            thumbs(ob, out, name, sprite_png)
            okpaint.export(ob, glb)
            done += 1
            print("BATCH_OK", name, "tris", sum(len(p.vertices) - 2 for p in ob.data.polygons), flush=True)
        except Exception as e:  # keep going, report at the end
            failed += 1
            print("BATCH_FAIL", name, repr(e), flush=True)
    print("BATCH_DONE carved %d failed %d in %.0f s" % (done, failed, time.time() - t0), flush=True)


if __name__ == "__main__":
    main()
