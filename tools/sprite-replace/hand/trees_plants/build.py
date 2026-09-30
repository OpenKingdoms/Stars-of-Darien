"""Builds the trees_plants family in one Blender session.

    blender -b --factory-startup --python build.py [-- Name Name ...]

A name with a script in bespoke/ is built by that script (bespoke/common.py)
instead of the kit.

Each model goes to D:/OKReplace/hand/trees_plants/models/<Name>.glb, with
its classic and turned renders in .../renders. Each model is built under
its sturdiness factors (kit.SHAPE, from the spec's "sturdy" or kit.STURDY
for its kind). Before the export the model's vertex colours are scaled so
its classic render matches the drawing's mean colour where the two overlap.
"""
import math
import os
import sys
import time
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.normpath(os.path.join(HERE, "..", ".."))
for p in (HERE, KIT):
    if p not in sys.path:
        sys.path.insert(0, p)

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import handkit as hk  # noqa: E402
import kit  # noqa: E402
import models  # noqa: E402
import sprite2d as s2  # noqa: E402

OUT = os.environ.get("OK_REPLACE", "D:/OKReplace") + "/hand/trees_plants"
WORK = os.path.join(OUT, "work", "calib")


def deep_alpha():
    """Leaf cards stack many alpha-cut layers; Cycles stops a ray after 8
    transparent hits and paints black, which the engine's alpha test never
    does, so the renders allow as many as a crown can have."""
    bpy.context.scene.cycles.transparent_max_bounces = 128
    bpy.context.scene.cycles.max_bounces = max(bpy.context.scene.cycles.max_bounces, 12)


def classic(ob, spr, path, scale=1, samples=16):
    """The classic view only, as handkit.renders frames it."""
    scene, cam = hk._stage(samples)
    deep_alpha()
    P = hk.CELL * scale
    e = math.atan(1.0 / hk.TILT)
    d = Vector((0.0, math.cos(e), -math.sin(e)))
    up = Vector((0.0, math.sin(e), math.cos(e)))
    W, H = spr.w * scale, spr.h * scale
    dx = spr.hx * scale - W / 2.0
    dy_up = H / 2.0 - spr.hy * scale
    C = -Vector((1.0, 0.0, 0.0)) * (dx / P) - up * (dy_up / P)
    scene.render.resolution_x, scene.render.resolution_y = int(W), int(H)
    cam.data.ortho_scale = max(W, H) / P
    cam.location = C - d * 60
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    scene.cycles.samples = 32


def overlap_means(path, spr, own=False):
    """Mean sRGB of the drawing and of the render where both are opaque,
    or with own each over its own opaque pixels."""
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    a = a.reshape(h, w, 4)[::-1]
    k = w // spr.w
    a = a[:spr.h * k, :spr.w * k].reshape(spr.h, k, spr.w, k, 4).mean((1, 3))
    both = (a[..., 3] > 0.5) & spr.mask
    if both.sum() < 10:
        return None
    rsel = (a[..., 3] > 0.5) if own else both
    ren = a[..., :3][rsel] / a[..., 3][rsel][:, None]
    spill = float(((a[..., 3] > 0.5) & ~spr.mask).sum()) / spr.mask.sum()
    return spr.rgb[spr.mask if own else both].mean(0), ren.mean(0), float(both.sum()) / spr.mask.sum(), spill


def calibrate(parts, spr, spec, default_parts=None):
    """Scales the vertex colours so the classic render's mean colour meets
    the drawing's. Only the parts whose names end in one of spec
    'calib_parts' (or the builder's default) are scaled; spec 'calib'
    weakens or turns it off."""
    k = spec.get("calib", 1.0)
    if not k:
        return None
    os.makedirs(WORK, exist_ok=True)
    path = os.path.join(WORK, spr.name + ".png")
    classic(None, spr, path)
    m = overlap_means(path, spr, spec.get("calib_own", False))
    if m is None:
        return None
    ms, mr, cover, spill = m
    ratio = np.clip(ms / np.maximum(mr, 1e-3), 0.4, 3.0) ** k
    f = ratio ** 2.2
    which = spec.get("calib_parts", default_parts)
    for ob in parts:
        if which and not any(ob.name.endswith("_" + w) for w in which):
            continue
        ca = ob.data.color_attributes.get("Col")
        if ca is None:
            continue
        c = np.empty(len(ca.data) * 4, np.float32)
        ca.data.foreach_get("color", c)
        c = c.reshape(-1, 4)
        c[:, :3] = np.clip(c[:, :3] * f, 0, 1)
        ca.data.foreach_set("color", c.ravel())
    return {"sprite": [round(float(v), 3) for v in ms], "render": [round(float(v), 3) for v in mr],
            "cover": round(cover, 3), "factor": [round(float(v), 2) for v in f]}


def views(ob, name, res=320):
    """Side (+x), back (+y) and top views for checking depth, into work/views."""
    scene, cam = hk._stage(16)
    deep_alpha()
    lo = Vector([min((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
    hi = Vector([max((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
    centre = (lo + hi) / 2
    size = max(hi - lo) * 1.1
    scene.render.resolution_x = scene.render.resolution_y = res
    cam.data.ortho_scale = size
    out = os.path.join(OUT, "work", "views")
    os.makedirs(out, exist_ok=True)
    for tag, d in (("side", Vector((1, 0, 0.12))), ("back", Vector((0, 1, 0.12))), ("top", Vector((0, -0.02, 1)))):
        d.normalize()
        cam.location = centre + d * 60
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, "%s_%s.png" % (name, tag))
        bpy.ops.render.render(write_still=True)
    scene.cycles.samples = 32


def build(name, render=True, with_views=False):
    hk.reset()
    spec = models.MODELS[name]
    spr = s2.Sprite(name)
    rng = np.random.default_rng(zlib.crc32(name.encode()) + spec.get("seed", 0))
    g, h, r = spec.get("sturdy", kit.STURDY.get(spec["kind"], (1.0, 1.0, 1.0)))
    kit.SHAPE.update(girth=g, height=h, radius=r)
    parts, info = kit.BUILDERS[spec["kind"]](spr, spec, rng)
    cast = spec.get("cast", True)
    for p in parts:
        # grass casts no shadow in the engine, where detail meshes never do
        p.visible_shadow = cast
    cal = calibrate(parts, spr, spec, kit.CALIB_PARTS.get(spec["kind"]))
    glb = os.path.join(OUT, "models", name + ".glb")
    extras = {"feature": name, "family": "trees_plants"}
    if not cast:
        extras["castShadows"] = False
    ob = hk.finish(parts, glb, extras)
    ob.visible_shadow = cast
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print("TREES_BUILT", name, "tris", tris, info, "calib", cal)
    if render:
        hk._stage()
        deep_alpha()
        hk.renders(ob, os.path.join(OUT, "renders"), name, os.path.join(s2.SPRITES, name + ".png"),
                   (spr.hx, spr.hy), scale=2)
        m = overlap_means(os.path.join(OUT, "renders", name + "_classic.png"), spr)
        if m is not None:
            print("TREES_MATCH", name, "sprite", np.round(m[0], 3), "render", np.round(m[1], 3), "cover", round(m[2], 3),
                  "spill", round(m[3], 3))
        if with_views:
            views(ob, name)
    return tris


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "--norender" not in argv
    with_views = "--views" in argv
    names = [a for a in argv if not a.startswith("--")] or list(models.MODELS)
    for n in names:
        t0 = time.time()
        try:
            if os.path.exists(os.path.join(HERE, "bespoke", n + ".py")):
                # round 5: a model with its own script is built by it, not the kit
                sys.path.insert(0, os.path.join(HERE, "bespoke"))
                import common
                common.build(n, render)
            else:
                build(n, render, with_views)
        except Exception as e:
            import traceback
            traceback.print_exc()
            print("TREES_FAILED", n, repr(e))
        print("TREES_TIME", n, "%.1fs" % (time.time() - t0))


main()
