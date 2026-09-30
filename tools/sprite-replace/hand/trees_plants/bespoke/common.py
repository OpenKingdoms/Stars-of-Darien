"""Driver for the round-5 bespoke trees_plants models, run inside Blender.

Each bespoke/<Name>.py defines build(spr, rng) -> (parts, info) and CALIB,
the part roles whose vertex colours are calibrated. This module builds it,
matches its colours to the drawing, exports the .glb and renders it:

    blender -b --factory-startup --python bespoke/run.py -- Name [Name ...]

The kit's sturdiness pass is off here (SHAPE all 1): every bespoke model
is shaped at its true size in its own script.
"""
import importlib
import math
import os
import sys
import time
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
FAM = os.path.dirname(HERE)
KIT = os.path.normpath(os.path.join(FAM, "..", ".."))
for p in (KIT, FAM, HERE):
    if p not in sys.path:
        sys.path.insert(0, p)

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import handkit as hk  # noqa: E402
import kit  # noqa: E402
import sprite2d as s2  # noqa: E402

OUT = os.environ.get("OK_REPLACE", "D:/OKReplace") + "/hand/trees_plants"
WORK = os.path.join(OUT, "work", "r5")
LUMA = np.array([0.299, 0.587, 0.114])


def deep_alpha():
    """Stacked alpha-cut cards need more transparent bounces than Cycles'
    default, or deep crowns render black where the engine never would."""
    bpy.context.scene.cycles.transparent_max_bounces = 128
    bpy.context.scene.cycles.max_bounces = max(bpy.context.scene.cycles.max_bounces, 12)


def classic(spr, path, scale=1, samples=16):
    """The classic view only, framed as handkit.renders frames it."""
    scene, cam = hk._stage(samples)
    deep_alpha()
    P = hk.CELL * scale
    e = math.atan(1.0 / hk.TILT)
    d = Vector((0.0, math.cos(e), -math.sin(e)))
    up = Vector((0.0, math.sin(e), math.cos(e)))
    W, H = spr.w * scale, spr.h * scale
    C = -Vector((1.0, 0.0, 0.0)) * ((spr.hx * scale - W / 2.0) / P) - up * ((H / 2.0 - spr.hy * scale) / P)
    scene.render.resolution_x, scene.render.resolution_y = int(W), int(H)
    cam.data.ortho_scale = max(W, H) / P
    cam.location = C - d * 60
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    scene.cycles.samples = 32


def stats(path, spr):
    """Mean sRGB and median luminance of the drawing and of the render,
    each over its own opaque pixels, and the render's share of them."""
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    a = a.reshape(h, w, 4)[::-1]
    k = w // spr.w
    a = a[:spr.h * k, :spr.w * k].reshape(spr.h, k, spr.w, k, 4).mean((1, 3))
    sel = a[..., 3] > 0.5
    if sel.sum() < 10:
        return None
    ren = a[..., :3][sel] / a[..., 3][sel][:, None]
    srgb = spr.rgb[spr.mask]
    return srgb.mean(0), ren.mean(0), float(np.median(srgb @ LUMA)), float(np.median(ren @ LUMA)), sel.sum() / spr.mask.sum()


def scale_colours(parts, which, f):
    for ob in parts:
        if not any(ob.name.endswith("_" + w) for w in which):
            continue
        ca = ob.data.color_attributes.get("Col")
        if ca is None:
            continue
        c = np.empty(len(ca.data) * 4, np.float32)
        ca.data.foreach_get("color", c)
        c = c.reshape(-1, 4)
        c[:, :3] = np.clip(c[:, :3] * f, 0, 1)
        ca.data.foreach_set("color", c.ravel())


def calibrate(parts, spr, which, lift=1.0, rounds=2, hue=1.0):
    """Scales the listed parts' vertex colours until the classic render's
    median luminance is lift times the drawing's and its mean hue the
    drawing's (hue weakens the hue step)."""
    os.makedirs(os.path.join(WORK, "calib"), exist_ok=True)
    path = os.path.join(WORK, "calib", spr.name + ".png")
    log = []
    for _ in range(rounds):
        classic(spr, path, scale=2)
        m = stats(path, spr)
        if m is None:
            return None
        ms, mr, Ls, Lr, cover = m
        tint = np.clip(((ms / ms.mean()) / np.maximum(mr / max(mr.mean(), 1e-4), 1e-3)) ** hue, 0.75, 1.35)
        ratio = np.clip(tint * lift * Ls / max(Lr, 1e-4), 0.4, 3.0)
        scale_colours(parts, which, ratio ** 2.2)
        log.append((round(Ls, 3), round(Lr, 3), [round(float(v), 2) for v in ratio]))
    return log


def views(ob, name, res=256):
    """Front and back from 20 degrees up, the side and the top, into work/r5/views."""
    scene, cam = hk._stage(16)
    deep_alpha()
    lo = Vector([min((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
    hi = Vector([max((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
    centre = (lo + hi) / 2
    scene.render.resolution_x = scene.render.resolution_y = res
    cam.data.ortho_scale = max(hi - lo) * 1.1
    out = os.path.join(WORK, "views")
    os.makedirs(out, exist_ok=True)
    s20, c20 = math.sin(math.radians(20)), math.cos(math.radians(20))
    for tag, d in (("front", Vector((0, -c20, s20))), ("back", Vector((0, c20, s20))),
                   ("side", Vector((c20, 0, s20))), ("top", Vector((0, -0.02, 1)))):
        d.normalize()
        cam.location = centre + d * 60
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, "%s_%s.png" % (name, tag))
        bpy.ops.render.render(write_still=True)
    scene.cycles.samples = 32


def build(name, render=True):
    hk.reset()
    kit.SHAPE.update(girth=1.0, height=1.0, radius=1.0)
    mod = importlib.import_module(name)
    importlib.reload(mod)
    spr = s2.Sprite(name)
    rng = np.random.default_rng(zlib.crc32(name.encode()) + getattr(mod, "SEED", 0))
    parts, info = mod.build(spr, rng)
    for p in parts:
        p.visible_shadow = True
    cal = (calibrate(parts, spr, mod.CALIB, getattr(mod, "LIFT", 1.0), getattr(mod, "ROUNDS", 2), getattr(mod, "HUE", 1.0))
           if mod.CALIB else None)
    glb = os.path.join(OUT, "models", name + ".glb")
    ob = hk.finish(parts, glb, {"feature": name, "family": "trees_plants"})
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print("BESPOKE_BUILT", name, "tris", tris, info, "calib", cal)
    if render:
        hk._stage()
        deep_alpha()
        hk.renders(ob, os.path.join(OUT, "renders"), name, os.path.join(s2.SPRITES, name + ".png"),
                   (spr.hx, spr.hy), scale=2)
        m = stats(os.path.join(OUT, "renders", name + "_classic.png"), spr)
        if m is not None:
            print("BESPOKE_MATCH", name, "sprite", np.round(m[0], 3), "render", np.round(m[1], 3),
                  "median lum %.3f / %.3f" % (m[2], m[3]), "cover %.2f" % m[4])
        views(ob, name)
    return tris


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "--norender" not in argv
    for n in [a for a in argv if not a.startswith("--")]:
        t0 = time.time()
        try:
            build(n, render)
        except Exception as e:
            import traceback
            traceback.print_exc()
            print("BESPOKE_FAILED", n, repr(e))
        print("BESPOKE_TIME", n, "%.1fs" % (time.time() - t0))
