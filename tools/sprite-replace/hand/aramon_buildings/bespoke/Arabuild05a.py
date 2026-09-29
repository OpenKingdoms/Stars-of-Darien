"""Arabuild05a, the damaged long hall, shaped on its own against its sprite.

    blender -b --factory-startup --python bespoke/Arabuild05a.py [-- --norender]

The standing hall, wing and veranda are the family's intact long hall.
The fire's damage is placed here: the holes, heaps, the slumped front
section and its charcoal rim. Soot is drawn in sprite pixels (rings round
the holes, a dark patch east of the main hole, a mottled right veranda)
and projected onto every roof map, so it lands where the sprite has it.
"""
import math
import os
import random
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import kit  # noqa: E402
import models  # noqa: E402,F401  (loads the family modules in their order)
import m_halls  # noqa: E402
from kit import Mat, px  # noqa: E402
from m_longhall import (HALL, VER, WING5, grown, hall_geoms, hall_mats5, hay_tufts, local, long_hall, matte,  # noqa: E402
                        on_veranda, proud, resample, screen_strip, seat_islands)

NAME = "Arabuild05a"
R_ = kit.CAT[NAME]
HX, HY = R_["sprite"]["hotspot"]

# ---------------------------------------------------------------- outlines on the sprite (col, row)

MAIN = [(128, 72), (160, 64), (196, 70), (206, 100), (196, 124), (160, 130), (130, 122), (122, 96)]
BACK = [(172, 6), (214, 4), (232, 20), (228, 44), (196, 48), (176, 34)]
TIP = [(14, 30), (56, 26), (66, 40), (42, 50), (16, 46)]
VER_R = [(228, 128), (268, 126), (282, 150), (276, 176), (244, 180), (226, 160)]
WFRONT = [(30, 116), (52, 108), (80, 110), (100, 126), (102, 150), (96, 172), (70, 178), (42, 172), (28, 150)]
JN0 = [(58, 126), (100, 124), (104, 150), (96, 172), (70, 178), (58, 160)]
SLAB = [(134, 181), (140, 175), (150, 170), (165, 167), (180, 167), (193, 170), (199, 176), (200, 190),
        (197, 202), (192, 214), (189, 224), (188, 233), (182, 236), (176, 232), (169, 236), (161, 233),
        (154, 237), (147, 233), (140, 236), (134, 233), (133, 215), (133, 196)]
CUT = [(128, 256), (128, 196), (131, 180), (139, 170), (150, 164), (165, 161), (181, 161), (195, 164),
       (203, 172), (207, 186), (206, 197), (214, 199), (224, 202), (229, 214), (226, 224), (200, 246), (196, 256)]

# the back of the wing's broken front, filled under its roof so no
# interior shows black behind the heap
WBACK = [(26, 106), (52, 101), (82, 104), (102, 122), (100, 136), (60, 134), (28, 132)]
# the dark east of the main hole, cols 215-265 rows 55-105, and its lit
# patch above, cols 190-232 rows 48-76, left clean
EAST = [(236, 44), (258, 50), (272, 66), (272, 96), (262, 110), (236, 112), (212, 108), (200, 96), (204, 80),
        (226, 78), (234, 62)]
# its darkest part, below the lit patch and down to the eave course
EAST2 = [(204, 82), (230, 78), (262, 80), (264, 106), (214, 108), (202, 96)]
# the right veranda between the arch and the right hole and below it
RVER = [(206, 150), (226, 140), (286, 150), (292, 188), (262, 204), (214, 208), (206, 190)]
# the hall's front slope between the main hole and its eave course, dark
# in the sprite down to the arch
SOUTH = [(126, 118), (160, 128), (190, 122), (196, 130), (186, 140), (150, 152), (124, 152), (118, 134)]
# the wing roof's back by its broken tip, cols 62-100 rows 36-68
WING_B = [(58, 34), (82, 36), (100, 50), (98, 66), (74, 64), (60, 50)]
# the junction of wing and veranda above the lit left veranda
JUNC = [(92, 132), (124, 128), (128, 150), (120, 160), (96, 158)]
# the hall's east hip above the dark patch, mid-dark
EHIP = [(238, 28), (262, 34), (272, 50), (262, 58), (240, 56)]
# the slumped slab's right side, dark down its crease, and the crease
# down its left edge
SLAB_R = [(178, 166), (199, 174), (201, 190), (197, 204), (190, 222), (182, 222), (180, 196)]
SLAB_L = [(135, 232), (134, 214), (134, 196), (136, 182)]
# the hall's west hip, which the sprite draws in full light though it
# turns from the render's sun: its tiles a little paler
LIFT = [(96, 40), (118, 40), (121, 70), (117, 100), (104, 106), (94, 80)]
# the soot smudge at the left foot of the arch rim, cols 130-140
FOOT = [(129, 202), (129, 188), (132, 176), (138, 166)]


# ---------------------------------------------------------------- the soot, in sprite pixels

def seg_dist(C, R, poly, closed=True):
    pts = list(poly) + [poly[0]] if closed else list(poly)
    d = np.full(C.shape, 1e9)
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        dx, dy = x1 - x0, y1 - y0
        t = np.clip(((C - x0) * dx + (R - y0) * dy) / max(dx * dx + dy * dy, 1e-9), 0, 1)
        d = np.minimum(d, np.hypot(C - x0 - t * dx, R - y0 - t * dy))
    return d


def inside(C, R, poly):
    c = np.zeros(C.shape, bool)
    for i in range(len(poly)):
        (ax, ay), (bx, by) = poly[i], poly[i - 1]
        if ay == by:
            continue
        c ^= ((ay > R) != (by > R)) & (C < (bx - ax) * (R - ay) / (by - ay) + ax)
    return c


def hash2(a, b, p):
    h = np.sin(a * 12.9898 + b * 78.233 + p * 37.719) * 43758.5453
    return h - np.floor(h)


def vnoise(C, R, cell, seed):
    """Value noise in 0..1 on a lattice cell px apart, in sprite pixels."""
    x, y = C / cell, R / cell
    i, j = np.floor(x), np.floor(y)
    fx, fy = x - i, y - j
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    top = hash2(i, j, seed) * (1 - fx) + hash2(i + 1, j, seed) * fx
    bot = hash2(i, j + 1, seed) * (1 - fx) + hash2(i + 1, j + 1, seed) * fx
    return top * (1 - fy) + bot * fy


def ring(C, R, poly, core, fade):
    """Near black inside the outline and core px out from it, fading over
    fade px more."""
    d = seg_dist(C, R, poly)
    t = np.clip(1 - (d - core) / fade, 0, 1) ** 0.8
    return np.where(inside(C, R, poly), 1.0, t)


def zone(C, R, poly, s, fade):
    d = seg_dist(C, R, poly)
    return np.where(inside(C, R, poly), s, s * np.clip(1 - d / fade, 0, 1))


def soot_at(C, R):
    """How far each sprite pixel is sooted, 0 clean to 1 near black."""
    mot = vnoise(C, R, 5.0, 3) * 0.6 + vnoise(C, R, 11.0, 4) * 0.4
    t = np.zeros(C.shape)
    t = np.maximum(t, ring(C, R, MAIN, 2.0, 5.0))
    t = np.maximum(t, ring(C, R, BACK, 3.0, 5.0))
    t = np.maximum(t, ring(C, R, TIP, 2.5, 5.0))
    t = np.maximum(t, ring(C, R, WFRONT, 3.0, 6.0))
    t = np.maximum(t, ring(C, R, VER_R, 2.0, 4.0))
    t = np.maximum(t, zone(C, R, EAST, 0.74, 8.0) * (0.8 + 0.4 * mot))
    t = np.maximum(t, zone(C, R, EAST2, 0.86, 5.0) * (0.85 + 0.3 * mot))
    t = np.maximum(t, zone(C, R, SOUTH, 0.7, 5.0) * (0.8 + 0.4 * mot))
    t = np.maximum(t, zone(C, R, WING_B, 0.6, 6.0) * (0.75 + 0.5 * mot))
    t = np.maximum(t, zone(C, R, JUNC, 0.65, 5.0) * (0.8 + 0.4 * mot))
    t = np.maximum(t, zone(C, R, EHIP, 0.45, 6.0) * (0.7 + 0.6 * mot))
    t = np.maximum(t, zone(C, R, SLAB_R, 0.55, 6.0) * (0.7 + 0.6 * mot))
    t = np.maximum(t, np.clip(1 - (seg_dist(C, R, SLAB_L, closed=False) - 2.0) / 5.0, 0, 1) ** 0.8)
    # mottled mid-dark, brightening toward the veranda's right end
    fall = np.clip((284 - C) / 18.0, 0.3, 1.0)
    t = np.maximum(t, zone(C, R, RVER, 0.62, 6.0) * fall * (0.7 + 0.6 * mot))
    t = np.maximum(t, 0.45 * np.clip(1 - (seg_dist(C, R, FOOT, closed=False) - 1.0) / 2.5, 0, 1))
    return np.clip(t, 0, 1)


# ---------------------------------------------------------------- roof maps painted with it

def _enc(v):
    """Drawn colour to the stored albedo, as kit._to_image stores it."""
    vs = np.array([p[1] for p in kit._HK], float)
    al = np.array([p[0] for p in kit._HK], float)
    a_hk = np.where(v < 194, np.interp(v, vs, al), 1.0)
    x = v / 255.0
    lin = np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)
    a = np.clip(np.sqrt(np.maximum(a_hk, 1e-5) * np.maximum(lin / kit.GAME_GAIN, 1e-5)), 0, 1)
    return np.where(a <= 0.0031308, a * 12.92, 1.055 * a ** (1 / 2.4) - 0.055)


LUT_V = np.linspace(0, 255, 2041)
LUT_S = _enc(LUT_V)
SOOT = np.array(kit.rgb("#17130f"), float)


def lean_to(x, y):
    """Whether a plan point is under the east lean-to's roof (it shares the
    veranda's map, its east end wrapping round), and that roof's height."""
    ox, oy, _ = local(HALL, HALL["L"] / 2 + 1.0, -0.6)
    c, s = math.cos(math.radians(HALL["yaw"])), math.sin(math.radians(HALL["yaw"]))
    lx, ly = (x - ox) * c + (y - oy) * s, -(x - ox) * s + (y - oy) * c
    ins = (lx >= -0.95) & (lx <= 1.15) & (np.abs(ly) <= 2.35)
    return ins, 3.3 - (lx + 0.9) * 0.45


def texel_screen(g, w, h, lean=False):
    """Where the classic camera draws each texel of roof g's map."""
    u = (-g.hx + (np.arange(w) + 0.5) / w * 2 * g.hx)[None, :].repeat(h, 0)
    v = (-g.hy + (np.arange(h) + 0.5) / h * 2 * g.hy)[:, None].repeat(w, 1)
    z = np.minimum(g.ze + (g.hy - np.abs(v)) * g.k, g.end_np(u))
    c, s = math.cos(math.radians(g.yaw)), math.sin(math.radians(g.yaw))
    x, y = g.cx + u * c - v * s, g.cy + u * s + v * c
    if lean:
        for du in (0.0, 2 * g.hx):
            xx, yy = x + du * c, y + du * s
            ins, zl = lean_to(xx, yy)
            x, y, z = np.where(ins, xx, x), np.where(ins, yy, y), np.where(ins, zl, z)
    return HX + 16 * x, HY - 16 * y - 8 * z, u, v


def painted_roof(n, key, g, seed, clean=None, lean=False, **kw):
    """Clean tiles for roof g, sooted where soot_at says; clean(u, v) is
    a 0..1 mask of texels kept clear (the lit eave course)."""
    d = dict(course=0.26, tile_w=0.7, var=0.12, lip=0.2, moss="#2a221c", moss_amt=0.1, dark=0.88)
    d.update(kw)
    base = n + key + "_tiles"
    img = kit.tex_roofmap(base, g, "#7a665a", seed=seed, **d)
    w, h = img.size
    a = np.array(img.pixels[:], np.float32).reshape(h, w, 4)[..., :3]
    disp = np.interp(a, LUT_S, LUT_V)
    C, R, u, v = texel_screen(g, w, h, lean)
    t = soot_at(C, R)
    if clean is not None:
        t = t * (1 - clean(u, v))
    lift = zone(C, R, LIFT, 0.3, 6.0)
    disp = np.clip(disp * (1 + lift[..., None]), 0, 255)
    grain = 0.8 + 0.4 * np.random.default_rng(seed).random((h, w, 1))
    disp = disp * (1 - t[..., None]) + SOOT * grain * t[..., None]
    bpy.data.images.remove(img)
    out = kit._to_image(n + key, np.clip(disp, 0, 255))
    return matte(Mat(n + key, tex=out, uvfn=base, ref="#7a665a"))


def eave_clean(g, width=0.75):
    """The front slope's lowest courses of roof g."""
    def fn(u, v):
        dist = (g.hy - np.abs(v)) * math.sqrt(1 + g.k ** 2)
        m = np.clip((width - dist) / 0.25, 0, 1)
        return np.where(v < 0, m, 0.0)
    return fn


# ---------------------------------------------------------------- charcoal and broken timber

def debris(n):
    """Charcoal pieces with about a third light plank ends and splinters,
    grey and reddish brown; ref None so none is chosen by the sprite's
    dark where it lies."""
    def planks(k, c, s):
        return Mat("%s_bw%d" % (n, k), tex=kit.tex_planks("%s_bw%d" % (n, k), c, boards=2, var=0.1, seed=s), uv=0.8)

    def mottle(k, c, s):
        return Mat("%s_bs%d" % (n, k), tex=kit.tex_mottle("%s_bs%d" % (n, k), c, var=0.14, seed=s), uv=0.8)
    wd = [planks(0, "#3c322b", 71), planks(1, "#4a3e35", 72), planks(2, "#332a24", 73), planks(3, "#43372f", 74)]
    wl = [planks(4, "#77716a", 75), planks(5, "#6c4a38", 76)]
    sd = [mottle(0, "#403831", 81), mottle(1, "#4c433b", 82), mottle(2, "#362f29", 83)]
    sl = [mottle(3, "#76706a", 84)]
    wood = wd + wd[:3] + wl + wl[:1]          # 7 dark, 3 light
    slab = sd + sd + sd[:1] + sl + sl + sl    # 7 dark, 3 light
    return {"slab": slab, "tile": slab, "stone": slab, "chunk": slab, "beam": wood, "board": wood,
            "boulder": slab}, wl


def plank_heap(B, r, g, mats, poly, seed):
    """Broken planks and rafter ends heaped in the back hole: most lying
    across it a little above the roof, criss-crossed, some raised at one
    end, three standing up past the ridge line by 0.3 to 0.5 cells."""
    rng = random.Random(seed)
    pts = [g.hit(r, c, rw) for c, rw in poly]
    pts = [p for p in pts if p is not None]
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    rad = max(math.hypot(p[0] - cx, p[1] - cy) for p in pts)
    zr = g.zr
    for i, m in enumerate(mats):
        a = 2 * math.pi * i / len(mats) + rng.uniform(-0.3, 0.3)
        d = rad * rng.uniform(0.1, 0.55)
        x, y = cx + math.cos(a) * d, cy + math.sin(a) * d
        t = g.top(x, y)
        t = zr - 0.4 if t is None else t
        ang = rng.uniform(0, math.pi)
        if i in (1, 4, 8):
            lean = rng.uniform(0.45, 0.65)
            p0 = (x - math.cos(ang) * lean, y - math.sin(ang) * lean, t - 0.35)
            p1 = (x + math.cos(ang) * lean, y + math.sin(ang) * lean, zr + rng.uniform(0.3, 0.5))
            w = rng.uniform(0.14, 0.2)
        else:
            L = rng.uniform(1.3, 2.1) if i % 4 else rng.uniform(0.6, 0.9)
            p0 = (x - math.cos(ang) * L / 2, y - math.sin(ang) * L / 2, t + rng.uniform(-0.15, 0.1))
            p1 = (x + math.cos(ang) * L / 2, y + math.sin(ang) * L / 2, t + rng.uniform(0.05, 0.4))
            w = rng.uniform(0.2, 0.3)
        B.beam(m, p0, p1, w, rng.uniform(0.07, 0.12), twist=rng.uniform(-25, 25))


# ---------------------------------------------------------------- the model

def build(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    g, gw, gv = hall_geoms()
    hx_, hy_ = r["sprite"]["hotspot"]
    m = hall_mats5(n)
    m["roof"] = painted_roof(n, "_roof", g, 1, clean=eave_clean(g))
    m["vroof"] = painted_roof(n, "_vroof", gv, 2, lean=True)
    # lighter charcoal: a base near L 28 on the heaps, with pale chips
    m["char"] = matte(Mat(n + "_char", tex=kit.tex_grit(n + "_char", "#463d36", light="#8e877e", dark="#1e1a16",
                                                         uv=2.0, chips=0.1, darks=0.22, seed=4), uv=2.0))
    floor = matte(Mat(n + "_floor", tex=kit.tex_grit(n + "_floor", "#2c2621", light="#857e75", dark="#12100d",
                                                      uv=2.0, chips=0.1, darks=0.28, seed=9), uv=2.0))
    m["wall"] = Mat(n + "_hwall", tex=kit.tex_planks(n + "_hwall", "#2a2018", boards=8, seed=5), uv=2.0)
    mw = dict(m, roof=painted_roof(n, "_wroof", gw, 3),
              wall=Mat(n + "_wwall", tex=kit.tex_planks(n + "_wwall", "#2a2018", boards=8, seed=5), uv=2.0),
              timber=Mat(n + "_wtimber", tex=kit.tex_planks(n + "_wtimber", "#1e1812", boards=2, seed=133), uv=1.0),
              cap=Mat(n + "_wcap", tex=kit.tex_planks(n + "_wcap", "#4a3a30", boards=2, seed=134), uv=1.0))
    # the posts and front beam under the fallen section went with it
    long_hall(B, r, m, mw, gap=(-4.2, 1.6))
    obs = B.objects()
    for ob in obs:
        if ob.name.endswith("_timber"):
            proud(ob, HALL)
        elif ob.name.endswith("_wtimber"):
            proud(ob, WING5)
    J = {k: kit.jag(q, 2.5, 4.0, i) for i, (k, q) in enumerate((("main", MAIN), ("back", BACK), ("tip", TIP),
                                                                  ("wfront", WFRONT), ("ver_r", VER_R)))}
    jn = kit.jag(JN0, 2.5, 4.0, 9)
    kit.cut_view([o for o in obs if o.name.endswith("_roof")], [J["main"], J["back"], jn], r)
    kit.cut_view([o for o in obs if o.name.endswith("_rcap")], [grown(q) for q in (J["main"], J["back"], jn)], r)
    kit.cut_view([o for o in obs if o.name.endswith("_wroof")], [J["tip"], J["wfront"]], r)
    kit.cut_view([o for o in obs if o.name.endswith("_wwall")], [WFRONT], r)
    kit.cut_view([o for o in obs if o.name.endswith(("_wtimber", "_wcap"))], [grown(WFRONT)], r)
    kit.cut_view([o for o in obs if o.name.endswith("_vroof")], [J["wfront"], J["ver_r"], kit.jag(CUT, 1.5, 4.0, 5)],
                 r)
    dm, light = debris(n)
    rafter = Mat(n + "_wrafter", tex=kit.tex_planks(n + "_wrafter", "#3a2e24", boards=2, seed=7), uv=1.0)
    m_halls.roof_frame(B, rafter, r, g, [MAIN], spacing=1.3, lath=20.0, w=0.18)
    # charred floors sagging under the holes with debris lying in them
    kit.hole_insides(B, r, pic, floor, dm, g, [J["main"]], sag=1.0, n=14, seed=10, size=(0.4, 0.8), cell=0.5)
    kit.hole_insides(B, r, pic, floor, dm, gw, [J["tip"]], sag=0.6, n=6, seed=20, cell=0.5)
    kit.hole_insides(B, r, pic, floor, dm, g, [jn], sag=1.3, n=14, seed=24, size=(0.4, 0.8), cell=0.5)
    # the back hole: a shallow charred floor and broken planks pushed up
    # through the ridge, not a plate
    kit.sag_fill(B, m["char"], r, g, J["back"], sag=0.35, cell=0.6, noise=0.08, seed=12, edge=0.8)
    pale = [Mat(n + "_bp%d" % i, tex=kit.tex_planks(n + "_bp%d" % i, c, boards=2, var=0.1, seed=90 + i), uv=0.8)
            for i, c in enumerate(("#8c857b", "#6f675e", "#6a4a3a"))]
    plank_heap(B, r, g, [pale[0], dm["beam"][0], pale[1], pale[2], pale[0], dm["beam"][1], pale[1],
                         dm["beam"][3], pale[0], pale[2]], BACK, 13)
    flat = lambda x, y: 0.0  # noqa: E731
    # the right hole's heap read near the roof's height and grown under its
    # rim, so no dark gap to the ground shows round it
    h = kit.heap(B, m["char"], r, grown(kit.jag(VER_R, 2.0, 4.0, 31), 3.0), 1.5, z_at=1.3, cell=0.5, noise=0.06, seed=32,
                 edge=1.0, pic=pic)
    kit.cover(B, dm, r, kit.plan_of(r, VER_R, 1.3), kit.ground_of(h, flat), 16, {"slab": 3, "beam": 3, "board": 2},
              pic=pic, size=(0.4, 0.8), length=(1.0, 2.2), seed=41, tilt=30, grow=1, jumble=0.2, stick=0.25)
    kit.sag_fill(B, m["char"], r, gw, WBACK, sag=0.7, cell=0.5, noise=0.08, seed=34, edge=0.6)
    hw = kit.heap(B, m["char"], r, J["wfront"], 2.3, z_at=0.2, cell=0.55, noise=0.12, seed=33, edge=1.4, pic=pic)
    west = kit.ground_of(hw, flat)
    pre = B.objects()
    kit.cover(B, dm, r, kit.plan_of(r, WFRONT, 0.2), west, 30,
              {"slab": 3, "beam": 3, "board": 2, "stone": 1}, pic=pic, size=(0.4, 0.9), length=(1.0, 2.4), seed=44,
              tilt=35, grow=1, jumble=0.3, stick=0.3)
    kit.cover(B, dm, r, kit.plan_of(r, [(36, 150), (110, 160), (116, 206), (60, 200)], 0.0), flat, 8,
              {"slab": 3, "beam": 2, "stone": 2}, pic=pic, size=(0.3, 0.6), length=(0.6, 1.4), seed=50, tilt=20,
              grow=1, tries=300)
    wp = B.objects()
    Pw = kit.plan_of(r, [(10, 96), (116, 96), (124, 214), (10, 214)], 0.5)
    for ob in wp:
        seat_islands(ob, west, Pw)
    for top, low in (((50, 112), (54, 150)), ((76, 114), (70, 156)), ((94, 128), (84, 160))):
        a = gw.hit(r, *top)
        if a is None:
            continue
        x, y = px(r, low[0], low[1], 1.2)
        B.beam(rafter, (a[0], a[1], a[2] - 0.25), (x, y, max(0.2, hw(x, y)) + 0.05), 0.18, 0.15, twist=8)
    vtop = lambda x, y: (gv.top(x, y) + 0.02) if gv.top(x, y) is not None else -1e9  # noqa: E731
    kit.cover(B, dm, r, on_veranda(r, gv, [(98, 150), (130, 148), (134, 172), (100, 180)]), vtop, 6,
              {"slab": 3, "beam": 2, "stone": 2}, pic=pic, size=(0.3, 0.6), length=(0.6, 1.4), seed=46, tilt=15,
              grow=1, tries=300)
    # the slumped slab: a plane through the sprite's slab, dropped to 1.6 at
    # the arch and 0.7 at its ragged front, tipped toward the camera
    ZB, ZF, RB, RF = 1.6, 0.7, 167, 235
    yb = (hy_ - RB - 8 * ZB) / 16.0
    yf = (hy_ - RF - 8 * ZF) / 16.0
    sl = (ZB - ZF) / (yb - yf)

    def s_at(c, rw, dz=0.0):
        y = (hy_ - rw - 8 * (ZF + dz) + 8 * sl * yf) / (16 + 8 * sl)
        return ((c - hx_) / 16.0, y, ZF + (y - yf) * sl + dz)
    Ps = [s_at(c, rw) for c, rw in SLAB]
    xs = [p[0] for p in Ps]
    D = yb - yf + 0.4
    gs = kit.RoofGeom((min(xs) + max(xs)) / 2, yb + 0.1, max(xs) - min(xs) + 0.6, 2 * D, ZF - 0.3 * sl, sl * D, 0.0,
                      yaw=0.0, kind="gable", T=0.16)
    ms = painted_roof(n, "_sroof", gs, 11, course=0.37, tile_w=1.2, dark=0.72)
    torn = matte(Mat(n + "_torn", tex=kit.tex_tiles(n + "_torn", "#4e4238", rows=6, cols=3, var=0.16, seed=77),
                     uv=1.2))
    area = sum(Ps[i][0] * Ps[(i + 1) % len(Ps)][1] - Ps[(i + 1) % len(Ps)][0] * Ps[i][1] for i in range(len(Ps)))
    B.slab(ms, Ps if area > 0 else list(reversed(Ps)), 0.16)
    char2 = matte(Mat(n + "_char2", tex=kit.tex_grit(n + "_char2", "#2c2722", light="#6a6258", dark="#100e0c", uv=1.2,
                                                chips=0.05, darks=0.3, seed=48), uv=1.2))
    lo = resample([(128, 236), (128, 214), (128, 196), (131, 186)], 4)
    li = resample([(134, 236), (133, 214), (133, 196), (136, 186)], 4)
    screen_strip(B, char2, r, lo, li, lambda i, f, c, rw: s_at(c, rw)[2] - 0.1, 0.08, across=1)
    outer = resample([(129, 190), (132, 174), (140, 163), (152, 155), (167, 151), (184, 151), (198, 156),
                      (207, 165), (211, 178), (212, 197)], 16)
    inner = resample([(140, 193), (141, 181), (148, 174), (159, 171), (172, 170), (185, 170), (193, 173),
                      (197, 180), (198, 188), (196, 198)], 16)
    rng = random.Random(49)
    zo = []
    for c, rw in outer:
        hh = gv.hit(r, c, rw)
        zo.append(hh[2] + 0.03 if hh is not None else 2.0)
    bump = [0.3 + rng.uniform(-0.08, 0.08) for _ in outer]

    def rim_z(i, f, c, rw):
        zi = s_at(c, rw)[2] + 0.03
        return zo[i] * (1 - f) + zi * f + bump[i] * math.sin(math.pi * min(1.0, f * 1.15)) ** 0.7
    screen_strip(B, char2, r, outer, inner, rim_z, 0.3, across=3)
    for k, i in enumerate((2, 5, 8, 10, 13)):
        c = outer[i][0] + (inner[i][0] - outer[i][0]) * 0.45
        rw = outer[i][1] + (inner[i][1] - outer[i][1]) * 0.45
        z = rim_z(i, 0.45, c, rw)
        x, y = px(r, c, rw, z)
        kit.rock(B, char2, x, y, z - 0.1, rng.uniform(0.3, 0.45), rng.uniform(0.25, 0.35), rng.uniform(0.18, 0.26),
                 seed=60 + k, seg=5)
    for u, hgt, tilt in ((-3.1, 0.45, 14), (-1.2, None, 5), (0.7, 0.5, -16)):
        x, y, _ = local(HALL, u, -HALL["W"] / 2 - VER["depth"] + 0.3)
        top = hgt if hgt is not None else ZF + (y - yf) * sl - 0.17
        B.box(m["post"], x, y, 0, 0.28, 0.28, top, yaw=HALL["yaw"], pitch=tilt)
    kit.cover(B, dm, r, kit.plan_of(r, [(136, 236), (192, 234), (196, 250), (134, 252)], 0.0), flat, 6,
              {"slab": 3, "beam": 2, "stone": 2}, pic=None, size=(0.3, 0.6), length=(0.6, 1.4), seed=53, tilt=20)
    for i, q in enumerate(([(236, 176), (286, 170), (286, 196), (240, 204)],
                           [(100, 196), (132, 196), (132, 214), (100, 214)])):
        kit.cover(B, dm, r, kit.plan_of(r, q, 0.0), flat, 8, {"slab": 3, "beam": 2, "stone": 2}, pic=pic,
                  size=(0.3, 0.6), length=(0.6, 1.4), seed=51 + i, tilt=20, grow=1, tries=300)
    x, y = px(r, 220, 222, 0.3)
    with B.at(x, y, 0.25, yaw=30, pitch=12, roll=-8):
        B.slab(torn, [(-1.0, -0.6, 0), (1.0, -0.7, 0), (0.8, 0.6, 0), (-0.9, 0.5, 0)], 0.15)
    hay_tufts(B, m, r, [(165, 242, 6)], seed=5)
    return obs + pre + wp


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if os.environ.get("AB_SOOT"):
        # the soot as drawn on the sprite's canvas, for checking the maps
        C, R = np.meshgrid(np.arange(R_["sprite"]["w"]) + 0.5, np.arange(R_["sprite"]["h"]) + 0.5)
        np.save(kit.OUT + "/tmp/soot_%s.npy" % NAME, soot_at(C, R))
    ob = kit.build_one(NAME, build, render="--norender" not in argv)
    if os.environ.get("AB_CLOSE") and "--norender" not in argv:
        # a close low three-quarter view, as the judges look at it
        from mathutils import Vector
        sc = bpy.context.scene
        cam = sc.camera
        sc.render.resolution_x, sc.render.resolution_y = 1400, 1000
        for k, az in enumerate((-math.pi / 2 + 0.9, -math.pi / 2 - 0.7)):
            el = math.radians(30)
            dt = Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)))
            centre = Vector((0.0, 1.0, 2.0))
            cam.data.ortho_scale = 19.0
            cam.location = centre + dt * 60
            cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
            sc.render.filepath = kit.OUT + "/tmp/%s_close%d.png" % (NAME, k)
            bpy.ops.render.render(write_still=True)
