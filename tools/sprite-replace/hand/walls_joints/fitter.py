"""Fits a few placement parameters of models to their sprites' silhouettes.

    blender -b --factory-startup --python fitter.py -- Name[:key,key...] ...

Builds each model in fast mode (the big masses only), projects it with
the classic projection and scores the overlap with the sprite's drawn
pixels, the cast shadow ignored (intersection over union). A coordinate
descent with shrinking steps moves the named keys (or the model's FIT
list) and the best values are written to fitted.json.
"""
import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import glob  # noqa: E402
import importlib  # noqa: E402

import bpy  # noqa: E402

import kit  # noqa: E402
import handkit as hk  # noqa: E402

for _p in sorted(glob.glob(os.path.join(HERE, "models_*.py"))):
    importlib.import_module(os.path.basename(_p)[:-3])

# per model: the keys the fitter may move and their first step
FIT = {}


def target(m):
    px = m.pix  # rows top-down, 0-1
    a = px[..., 3] > 0.5
    lum = (px[..., :3] * 255) @ np.array([0.3, 0.59, 0.11])
    shadow = a & (lum < 24)
    return a & ~shadow, shadow


def raster(tris, w, h):
    img = np.zeros((h, w), bool)
    ys, xs = np.mgrid[0:h, 0:w]
    xs = xs + 0.5
    ys = ys + 0.5
    for t in tris:
        (x0, y0), (x1, y1), (x2, y2) = t
        lo_x, hi_x = int(max(0, min(x0, x1, x2))), int(min(w - 1, max(x0, x1, x2)) + 1)
        lo_y, hi_y = int(max(0, min(y0, y1, y2))), int(min(h - 1, max(y0, y1, y2)) + 1)
        if lo_x >= hi_x or lo_y >= hi_y:
            continue
        X = xs[lo_y:hi_y, lo_x:hi_x]
        Y = ys[lo_y:hi_y, lo_x:hi_x]
        d = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(d) < 1e-9:
            continue
        l0 = ((y1 - y2) * (X - x2) + (x2 - x1) * (Y - y2)) / d
        l1 = ((y2 - y0) * (X - x2) + (x0 - x2) * (Y - y2)) / d
        l2 = 1 - l0 - l1
        img[lo_y:hi_y, lo_x:hi_x] |= (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
    return img


def score(name, over):
    """Mean of two overlaps: the whole silhouette, and the roof (the model's
    tile parts against the drawing's tile-red pixels)."""
    kit.OVERRIDE[name] = over
    hk.reset()
    m = kit.Model(name)
    m.fast = True
    kit.MODELS[name](m)
    objs = m.done()
    tris, roof = [], []
    for ob in objs:
        me = ob.data
        me.calc_loop_triangles()
        V = [ob.matrix_world @ v.co for v in me.vertices]
        is_roof = any(k in ob.name for k in ROOF_KEYS)
        for t in me.loop_triangles:
            q = [m.scr(V[i].x, V[i].y, V[i].z) for i in t.vertices]
            tris.append(q)
            if is_roof:
                roof.append(q)
    body, shadow = target(m)
    # a margin round the picture, so a model reaching past its edge is a miss
    P = 32
    body = np.pad(body, P)
    shadow = np.pad(shadow, P)
    tris = [[(x + P, y + P) for x, y in t] for t in tris]
    roof = [[(x + P, y + P) for x, y in t] for t in roof]
    W, H = m.w + 2 * P, m.h + 2 * P
    full = raster(tris, W, H)
    mdl = full & ~shadow
    # covering the cast shadow is half a miss: the model may not grow into it freely
    s1 = (mdl & body).sum() / max(1, (mdl | body).sum() + 0.5 * (full & shadow).sum())
    px = np.pad(m.pix[..., :3] * 255, ((P, P), (P, P), (0, 0)))
    red = body & (px[..., 0] > 1.6 * px[..., 1]) & (px[..., 0] > 90)
    if not roof or red.sum() < 20:
        return s1
    mr = raster(roof, W, H)
    s2 = (mr & red).sum() / max(1, (mr | red).sum())
    return 0.5 * (s1 + s2)


ROOF_KEYS = ("_tile",)


def fit(name, keys):
    base = kit.params(name, kit.TABLES[name])
    cur = {k: base[k] if k in base else kit.FITDEF[k] for k, _ in keys}
    steps = {k: s for k, s in keys}
    best = score(name, cur)
    print("FIT", name, "start %.4f" % best, cur, flush=True)
    for rnd in range(4):
        improved = True
        while improved:
            improved = False
            for k in cur:
                for sgn in (1, -1):
                    trial = dict(cur)
                    trial[k] = round(cur[k] + sgn * steps[k], 4)
                    s = score(name, trial)
                    if s > best + 1e-4:
                        best, cur, improved = s, trial, True
                        break
        steps = {k: v / 2 for k, v in steps.items()}
    print("FIT", name, "best %.4f" % best, cur, flush=True)
    return cur, best


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:]
    store = json.load(open(kit.FITTED)) if os.path.exists(kit.FITTED) else {}
    for arg in argv:
        name, _, ks = arg.partition(":")
        spec = kit.FITKEYS[name]
        steps = dict(spec)
        keys = [(k, steps.get(k, 0.2)) for k in ks.split(",")] if ks else list(spec)
        cur, best = fit(name, keys)
        # another fitter may have written meanwhile: merge into the file as it is now
        store = json.load(open(kit.FITTED)) if os.path.exists(kit.FITTED) else {}
        store.setdefault(name, {}).update(cur)
        store[name]["_iou"] = round(best, 4)
        json.dump(store, open(kit.FITTED, "w"), indent=1, sort_keys=True)
