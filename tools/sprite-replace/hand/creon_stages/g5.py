"""Creon dead trees and the ash smudges they leave (destroyed stages, group g5).

    OK_REPLACE=<staging okr> blender -b --factory-startup --python g5.py -- [Name ...] [--norender]

The dead trees (CreTree01a, 03a to 06a) are the trees_plants kit's limb
skeletons lifted from the stage pictures, as CreTree02a was: bare charcoal
snags on the intact tree's anchor, with a foot shaped like the intact
trunk's, since FeatureFalls keeps the trunk and bursts the crown at the
swap. The foot is a sleeve that ends on the trunk's own first ring, so
the two meet with no rim. Each one is built, then rebuilt with its height
factor corrected so it stands at the intact model's height times the
stage's def height over the intact's. The poplars then lean back above
their lowest cells, as the intact poplars do, until the classic view
fills the drawing.

The smudges (CreTreesmudge01 to 07) are what a dead tree leaves when it
falls: a low ash bed following the picture's outline, charcoal lumps and
a few charred log ends lying on it, and the snapped foot of the tree that
stood there, sized from that tree's model.

Every texture is generated from noise. Models go to $OK_G5_OUT (default
D:/OKBuild/creon-stages/g5) as <Name>.glb, renders to its renders folder.
"""
import math
import os
import sys
import time
import zlib

os.environ.setdefault("OK_REPLACE", "D:/OKBuild/creon-stages/g5/okr")
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, "..", ".."))
TREES = os.path.join(TOOLS, "hand", "trees_plants")
for p in (TOOLS, TREES):
    if p not in sys.path:
        sys.path.insert(0, p)

import bpy  # noqa: E402
import numpy as np  # noqa: E402

import handkit as hk  # noqa: E402
import kit  # noqa: E402  (trees_plants/kit.py)
import models as tm  # noqa: E402  (trees_plants/models.py)
import sprite2d as s2  # noqa: E402

OUT = os.environ.get("OK_G5_OUT", "D:/OKBuild/creon-stages/g5")
RENDERS = os.path.join(OUT, "renders")
VIEWS = False


def _trees_build():
    """The trees_plants build script's calibration and render helpers,
    loaded without running its main()."""
    path = os.path.join(TREES, "build.py")
    src = open(path, encoding="utf-8").read()
    src = src[:src.rindex("\nmain()")]
    ns = {"__file__": path, "__name__": "trees_build"}
    exec(compile(src, path, "exec"), ns)
    ns["WORK"] = os.path.join(OUT, "work", "calib")
    return ns


TB = _trees_build()


# ---------------------------------------------------------------- dead trees

# Every dead tree stands on a convex foot (convex_foot below), its sizes in
# cells: 'base' across the ground, narrowing at 'decay' into the trunk's
# own ring at 'h', where the trunk is at least 'trunk' and at most 'cap'
# thick, held for 'ease' cells above and then easing into the drawn trunk.
# 'flare' and 'n' are its root lobes, 'sides' the trunk's, and 'taper'
# keeps the trunk from swelling again above the foot. The kit's own foot
# is held off (r, up).
FOOT_OFF = {"r": 1e-4, "up": 1e-3, "convex": True}
# the poplars follow the intact poplars' trunks with a little more girth.
# CreTree01's is 0.30, 0.26, 0.23 and 0.21 cells at 0.05, 0.3, 0.6 and
# 1.0 up, and CreTree03's 0.24, 0.21, 0.19 and 0.17
POPLAR_FOOT = dict(FOOT_OFF, base=0.33, trunk=0.25, cap=0.25, decay=0.35, flare=0.18, h=1.0, n=4, sides=8, taper=True)
# the poplar snags: CreTree02a's settings, the trunk on the intact's anchor
# for its lowest 1.5 cells and leaning back above them as the intact's
# crown does, so it fills the drawing at the intact's height. Its stubs
# are straight, climbing so they read upturned in the classic view as
# drawn, and reach no wider than the drawing
POPLAR_SNAG = dict(tm.SNAG4, foot=POPLAR_FOOT, lean={"from": 1.5, "p": 1.4, "fill": 0.95, "max": 2.2},
                   straight={"rise": 0.5, "emax": 60.0}, width=1.05)


def snag(base, **kw):
    d = dict(base)
    lift = dict(base["lift"])
    lift.update(kw.pop("lift", {}))
    d["lift"] = lift
    d.update(kw)
    return d


# the broadleaves: 04a and 06a are AraTree04a and 06a's pictures in Creon's
# palette, so they start from those settings, forked lower: the trunk
# upright to its drawn fork, the leader and limbs climbing steeply from it
# so the crown spreads wide round a thick short trunk. 05a, the leaning
# one, forks lowest into its two drawn main limbs, climbing steeply
# enough to reach its height with a few more fitting passes.
CRE05A = {"kind": "limbs", "bark": "speckle", "sides_min": 5, "sturdy": (1.08, 0.92, 1.0),
          "lift": {"mode": "swing", "trunk": [0], "trunk_top": 2.6, "trunk_elev": (60, 72), "elev": (46, 62), "rmax": 0.8,
                   "root_slope": 1.0, "reach": 1.2},
          "extra": {"front": 2, "back": 2, "z": (0.45, 0.7), "len": (1.3, 1.9), "elev": (25, 40), "r": 0.2}}

# 06a's rising limbs behind the trunk (AraTree06a's) kept within the
# drawing: swung further round to the sides, shorter and flatter, and no
# limb reaching wider than the drawing
CRE06A_EXTRA = [{"az": [(152, 162), (154, 164)], "zabs": (3.4, 4.4), "len": (1.1, 1.3), "elev": (14, 20), "elev_post": True,
                 "kink": (18, 30), "fork_len": 0.5, "r": 0.25},
                {"az": [(30, 35)], "zabs": (3.0, 3.2), "len": (1.6, 1.7), "elev": (14, 16), "elev_post": True, "kink": (18, 30),
                 "fork_len": 0.5, "r": 0.25},
                {"az": [(38, 44)], "zabs": (3.3, 3.6), "len": (1.5, 1.6), "elev": (16, 18), "elev_post": True, "kink": (18, 30),
                 "fork_len": 0.5, "r": 0.24},
                {"az": [(40, 48)], "zabs": (4.2, 4.5), "len": (1.2, 1.4), "elev": (20, 24), "elev_post": True, "kink": (18, 30),
                 "fork_len": 0.5, "r": 0.2}]

# 04a's limbs reach past the drawing once the kit stretches them, so each
# one is kept within it on its own and its forks stay where they are
# drawn. Its limbs toward and away from the camera, where the drawing
# shows none, stay within about 25 degrees of straight on, so the classic
# view sees them over the trunk and not beside it
CRE04A_EXTRA = [{"az": [(-115, -100), (-80, -65)], "zabs": (3.9, 4.6), "len": (1.4, 1.8), "elev": (22, 32), "elev_post": True,
                 "kink": (18, 30), "fork_len": 0.5, "r": 0.22},
                {"az": [(65, 80), (100, 115)], "zabs": (4.2, 5.0), "len": (1.4, 1.9), "elev": (25, 35), "elev_post": True,
                 "kink": (18, 30), "fork_len": 0.5, "r": 0.22},
                {"az": [(85, 95)], "zabs": (5.0, 5.4), "len": (1.2, 1.5), "elev": (30, 38), "elev_post": True, "kink": (18, 30),
                 "fork_len": 0.45, "r": 0.2}]

# target heights: the intact model's height times the stage's def height
# over the intact's def height (CreTree01.glb 9.78 for 226, 03 11.59 for
# 254, 04 8.19 for 140, 05 8.68 for 140, 06 7.65 for 130). The broadleaves'
# feet follow the intact trunks' (about 1.0, 0.85, 0.72 and 0.6 cells at
# 0.05, 0.3, 0.6 and 1.0 up), which FeatureFalls holds through the swap.
# 04a's foot narrows more slowly, with smaller lobes, to keep the intact's
# girth at 0.3 and 0.6 cells without spreading wider at the ground.
BROAD_FOOT = dict(FOOT_OFF, base=0.86, trunk=0.62, decay=0.45, flare=0.3, h=1.3, n=5, sides=12)
TREE_SPECS = {
    "CreTree01a": dict(POPLAR_SNAG, target=9.78 * 224 / 226, foot=dict(POPLAR_FOOT, base=0.35, trunk=0.25, cap=0.25)),
    "CreTree03a": dict(POPLAR_SNAG, target=11.59, foot=dict(POPLAR_FOOT, base=0.27, trunk=0.21, cap=0.21, decay=0.4)),
    "CreTree04a": snag(tm.MODELS["AraTree04a"], target=8.19, foot=dict(BROAD_FOOT, base=0.97, trunk=0.64, decay=0.7, flare=0.25),
                       width=1.0, width_nested=True, extra=CRE04A_EXTRA,
                       lift={"root_slope": 1.0, "trunk_top": 3.0, "trunk_elev": (70, 80), "elev": (38, 56), "reach": 1.2}),
    "CreTree05a": dict(CRE05A, target=8.68 * 136 / 140, foot=dict(BROAD_FOOT, trunk=0.6), hmax=1.25, passes=7),
    "CreTree06a": snag(tm.MODELS["AraTree06a"], target=7.65, foot=dict(BROAD_FOOT, base=0.9, flare=0.34),
                       extra=CRE06A_EXTRA, width=1.0,
                       lift={"root_slope": 1.0, "trunk_top": 4.5, "trunk_elev": (70, 80), "elev": (30, 50), "reach": 1.2}),
}
ASH_LIGHT = (0.5, 0.5, 0.48)      # the pale ash on the lit upper sides of the limbs


def ash_light(ob, rng, amount=0.35):
    """Pale ash settled in patches on the upward faces of the limbs, mixed
    into the vertex colours before the calibration evens the whole out."""
    me = ob.data
    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    nr = np.empty(n * 3)
    me.vertices.foreach_get("normal", nr)
    nr = nr.reshape(-1, 3)
    ca = me.color_attributes.get("Col")
    c = np.empty(n * 4, np.float32)
    ca.data.foreach_get("color", c)
    c = c.reshape(-1, 4)
    k = rng.normal(size=(7, 3)) * 2.2
    ph = rng.uniform(0, 2 * math.pi, 7)
    noise = np.sin(co @ k.T + ph).sum(1) / math.sqrt(7 / 2)
    w = np.clip(np.clip(nr[:, 2], 0, 1) ** 1.5 * (0.35 + 0.5 * noise), 0, 1) * amount
    grey = _lin(ASH_LIGHT) * 0.5
    c[:, :3] = c[:, :3] * (1 - w[:, None]) + grey[None, :] * w[:, None]
    ca.data.foreach_set("color", c.ravel())


def turntable(ob, name, n=4, el=25, res=360):
    """Low views from n sides round the model, for judging it in 3D."""
    from mathutils import Vector
    scene, cam = hk._stage(24)
    TB["deep_alpha"]()
    lo = Vector([min((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
    hi = Vector([max((ob.matrix_world @ v.co)[i] for v in ob.data.vertices) for i in range(3)])
    centre = (lo + hi) / 2
    size = max(hi - lo) * 1.15
    scene.render.resolution_x = scene.render.resolution_y = res
    cam.data.ortho_scale = size
    out = os.path.join(OUT, "work", "views")
    os.makedirs(out, exist_ok=True)
    e = math.radians(el)
    for k in range(n):
        az = -math.pi / 2 + 2 * math.pi * k / n + 0.4
        d = Vector((math.cos(az) * math.cos(e), math.sin(az) * math.cos(e), math.sin(e)))
        cam.location = centre + d * 60
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, "%s_az%d.png" % (name, k))
        bpy.ops.render.render(write_still=True)


_ELEV_DEPTH = kit.elev_depth


def level_drawn_down(sx, sy, deg):
    """The kit's limb depth, with limbs drawn running down the picture held
    near level: climbing, they would have to reach far toward the camera to
    stay on their pixels' rays, and stand out as spears in 3D."""
    return _ELEV_DEPTH(sx, sy, min(deg, DOWN_ELEV) if sy < 0 else deg)


DOWN_ELEV = 8.0

_FLARED = kit.flared_trunk
_LIMBS_GEO = kit.limbs_geo
FOOT = {}    # the tree's foot spec
LIMB = {}    # its limb options: the sprite, the width share, straight stubs
TRUNK = {}   # the trunk tube's first ring, which the foot shares


def subtrees(limbs):
    """A function giving limb i and every limb forking off it, at any depth."""
    kids = {i: [] for i in range(len(limbs))}
    for i, L in enumerate(limbs):
        if L["parent"] is not None:
            kids[L["parent"]].append(i)

    def walk(i):
        out = [i]
        for c in kids[i]:
            out += walk(c)
        return out
    return walk


def keep_width(limbs, spr, share, nested=False):
    """Each limb off the trunk shrunk toward its start, its forks with it,
    until it reaches no further across than the drawing does, times share.
    Nested, each limb is shrunk on its own and its forks keep their drawn
    places, only their starts moving with the points they leave from, and
    then each fork is kept within the drawing the same way. The kit lays a
    fork across from its own drawn pixels, not from its stretched parent,
    so this keeps the forks where they are drawn."""
    g = kit.SHAPE["girth"]
    lo = (spr.left - spr.hx) / s2.CELL * share / g
    hi = (spr.right + 1 - spr.hx) / s2.CELL * share / g
    walk = subtrees(limbs)

    def shrink(i):
        S = limbs[i]["P"][0].copy()
        idx = walk(i)
        X = np.concatenate([limbs[j]["P"][:, 0] for j in idx]) - S[0]
        f = 1.0
        if X.max() > 1e-6 and S[0] + X.max() > hi:
            f = min(f, max(hi - S[0], 0.0) / X.max())
        if X.min() < -1e-6 and S[0] + X.min() < lo:
            f = min(f, max(S[0] - lo, 0.0) / -X.min())
        if f < 1.0:
            f = max(f, 0.3)
            for j in idx:
                limbs[j]["P"] = S + (limbs[j]["P"] - S) * f

    def fit(i):
        L = limbs[i]
        P = L["P"]
        S = P[0].copy()
        X = P[:, 0] - S[0]
        f = 1.0
        if X.max() > 1e-6 and S[0] + X.max() > hi:
            f = min(f, max(hi - S[0], 0.0) / X.max())
        if X.min() < -1e-6 and S[0] + X.min() < lo:
            f = min(f, max(S[0] - lo, 0.0) / -X.min())
        if f < 1.0:
            L["P"] = S + (P - S) * max(f, 0.3)
        for c, C in enumerate(limbs):
            if C["parent"] == i:
                if f < 1.0:
                    C["P"] = C["P"].copy()
                    C["P"][0] = L["P"][int(np.argmin(np.linalg.norm(P - C["P"][0], axis=1)))]
                fit(c)

    for i, L in enumerate(limbs):
        if L["parent"] == 0:
            (fit if nested else shrink)(i)


def straight_stubs(limbs, rise=0.3, emax=60.0):
    """Every limb off the trunk made straight from its start to its end,
    its forks carried along with the points they leave from. Where it would
    climb less than rise times its reach across the classic view, it climbs
    more steeply, up to emax degrees, and then turns back from the camera,
    so every stub reads upturned there as drawn."""
    S = np.array([kit.SHAPE["girth"], kit.SHAPE["girth"], kit.SHAPE["height"]])
    walk = subtrees(limbs)

    def lifts(hd, e):
        c = math.cos(e)
        return 16 * hd[1] * c + 8 * math.sin(e) >= rise * 16 * abs(hd[0]) * c - 1e-6

    for i, L in enumerate(limbs):
        if i == 0 or L["parent"] is None or len(L["P"]) < 2:
            continue
        P = L["P"]
        d = (P[-1] - P[0]) * S
        n = float(np.linalg.norm(d))
        if n < 1e-6:
            continue
        hl = float(np.linalg.norm(d[:2]))
        if hl > 1e-6:
            hd = d[:2] / hl
            e = math.atan2(d[2], hl)
            if not lifts(hd, e):
                for e in np.linspace(e, max(e, math.radians(emax)), 24):
                    if lifts(hd, e):
                        break
                d = n * np.array([hd[0] * math.cos(e), hd[1] * math.cos(e), math.sin(e)])
        for _ in range(4):
            up, across = 16 * d[1] + 8 * d[2], 16 * abs(d[0])
            if up >= rise * across - 1e-6:
                break
            d[1] = (rise * across - 8 * d[2]) / 16
            d = d / np.linalg.norm(d) * n
        seg = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])
        Q = P[0] + np.outer(seg / max(seg[-1], 1e-9), d / S)
        moved = Q - P
        L["P"] = Q
        for j, C in enumerate(limbs):
            if C["parent"] == i:
                k = int(np.argmin(np.linalg.norm(P - C["P"][0], axis=1)))
                for q in walk(j):
                    limbs[q]["P"] = limbs[q]["P"] + moved[k]


def limbs_geo(geo, limbs, bark_col, rng, *a, **kw):
    """The kit's limb tubes, the limbs first kept within the drawing's width
    and made straight where the tree asks for it. Under a convex foot the
    trunk is no thinner than the foot's 'trunk' (nor thicker than its
    'cap') up to the foot's top, eases into the drawn trunk above, and
    starts at the foot's top, where the foot takes over from its first
    ring. Roots drawn running down the picture are left to the foot's lobes."""
    TRUNK.clear()
    if LIMB.get("width"):
        keep_width(limbs, LIMB["spr"], LIMB["width"], LIMB.get("nested", False))
    if LIMB.get("straight"):
        straight_stubs(limbs, **LIMB["straight"])
    if not (FOOT.get("convex") and limbs and len(limbs[0]["P"]) > 4):
        return _LIMBS_GEO(geo, limbs, bark_col, rng, *a, **kw)
    g, h, rad = kit.SHAPE["girth"], kit.SHAPE["height"], kit.SHAPE["radius"]
    T = limbs[0]
    P = T["P"]
    rc = T["R"] * rad * g
    zc = P[:, 2] * h
    # the foot's top: a point put into the trunk at the foot's height,
    # unless one lies close above it
    above = np.nonzero(zc >= FOOT["h"])[0]
    k = int(np.clip(above[0] if len(above) else len(P) - 3, 1, len(P) - 3))
    if zc[k] - FOOT["h"] > 0.15 and zc[k - 1] < FOOT["h"]:
        u = (FOOT["h"] - zc[k - 1]) / (zc[k] - zc[k - 1])
        P = np.insert(P, k, P[k - 1] + (P[k] - P[k - 1]) * u, axis=0)
        rc = np.insert(rc, k, rc[k - 1] + (rc[k] - rc[k - 1]) * u)
        zc = P[:, 2] * h
    H = zc[k]
    if FOOT.get("taper"):
        rc = np.convolve(np.pad(rc, 2, mode="edge"), np.ones(5) / 5, mode="valid")
    w = np.clip(1 - (zc - H) / FOOT.get("ease", 1.5), 0, 1)
    rc = np.maximum(rc, FOOT["trunk"] * w * w * (3 - 2 * w))
    if FOOT.get("cap"):
        rc[:k + 1] = np.minimum(rc[:k + 1], FOOT["cap"])
    walk = subtrees(limbs)
    if FOOT.get("taper"):
        # a slender snag: the trunk tapering all the way up, its path above
        # the foot evened out with its limbs carried along, and no limb
        # thicker where it leaves than 0.7 of the trunk there
        for j in range(k + 1, len(rc)):
            rc[j] = min(rc[j], rc[j - 1])
        Q = P.copy()
        for c in (0, 1):
            sm = np.convolve(np.pad(P[:, c], 3, mode="edge"), np.ones(7) / 7, mode="valid")
            wt = np.clip((np.arange(len(P)) - k) / 3.0, 0, 1)
            Q[:, c] = P[:, c] + (sm - P[:, c]) * wt
        for i, L in enumerate(limbs):
            if L["parent"] == 0:
                j = int(np.argmin(np.linalg.norm(P - L["P"][0], axis=1)))
                for q in walk(i):
                    limbs[q]["P"] = limbs[q]["P"] + (Q[j] - P[j])
                L["R"] = np.minimum(L["R"], 0.7 * rc[j] / (rad * g))
        P = Q
    ztop = float(P[k, 2])
    drop = set()
    for i, L in enumerate(limbs):
        if L["parent"] == 0 and L["P"][-1, 2] < 0.15 and L["P"][:, 2].max() <= ztop * 1.2:
            drop.update(walk(i))
    # a limb leaving the trunk inside the foot or just above it leaves 'fork'
    # cells above the foot instead, from the trunk's middle, no thicker
    # there than 0.8 of the trunk, so it never cuts through the foot
    zf = H + FOOT.get("fork", 0.4)
    axis = [np.interp(zf, zc, P[:, c]) for c in range(3)]
    rf = float(np.interp(zf, zc, rc))
    for i, L in enumerate(limbs):
        if L["parent"] != 0 or i in drop or L["P"][0, 2] * h >= zf:
            continue
        up = np.nonzero(L["P"][:, 2] * h >= zf + 0.25)[0]
        if not len(up):
            drop.update(walk(i))
            continue
        j = int(up[0])
        old = L["P"]
        L["P"] = np.vstack([axis, old[j:]])
        L["R"] = np.minimum(np.concatenate([[L["R"][j]], L["R"][j:]]), 0.8 * rf / (rad * g))
        # forks off the part left out start from the nearest point kept
        for C in limbs:
            if C["parent"] == i and int(np.argmin(np.linalg.norm(old - C["P"][0], axis=1))) < j:
                C["P"] = C["P"].copy()
                C["P"][0] = L["P"][int(np.argmin(np.linalg.norm(L["P"] - C["P"][0], axis=1)))]
    T["P"], T["R"] = P[k:].copy(), rc[k:] / (rad * g)
    T["sides"] = FOOT.get("sides", 10)
    kept = [L for i, L in enumerate(limbs) if i not in drop]
    nb, nv0 = len(geo.v), geo.nv
    out = _LIMBS_GEO(geo, kept, bark_col, rng, *a, **kw)
    n = T["sides"]
    V = geo.v[nb]
    cap = nv0 + len(V) - 1   # the trunk's base cap, which the foot replaces
    geo.f = [f for f in geo.f if cap not in f]
    TRUNK.update(nv0=nv0, n=n, V=V[:n + 1].copy(), C=geo.c[nb][:n + 1].copy(), UV=geo.uv[nb][:n + 1].copy(),
                 P0=T["P"][0].copy(), dropped=len(drop))
    return out


def convex_foot(geo, base, tip, r0, r1, flare, nl, rng, col, sides=12, rings=9, hf=None, dark=0.8, phases=None):
    """The kit's trunk foot, for a FOOT spec marked convex: a sleeve from
    the ground up to the trunk's first ring, ending on that ring's own
    points, so foot and trunk are one surface with no rim. Its radius
    falls fast off the ground and levels into the trunk's with no ledge,
    and its root lobes fade out sooner than the radius does."""
    if not (FOOT.get("convex") and TRUNK):
        return _FLARED(geo, base, tip, r0, r1, flare, nl, rng, col, sides=sides, rings=rings, hf=hf, dark=dark,
                       phases=phases)
    g, h = kit.SHAPE["girth"], kit.SHAPE["height"]
    n, top, C0 = TRUNK["n"], TRUNK["V"], TRUNK["P0"]
    m = n + 1
    L = float(C0[2])
    foot0 = np.array([C0[0], C0[1], 0.0])
    off = top - C0
    ang = np.arctan2(off[:, 1], off[:, 0])
    rk = float(np.linalg.norm(off[:n, :2], axis=1).mean())
    rb = max(FOOT["base"] / g, rk)
    hz = FOOT["decay"] / h
    e_end = math.exp(-L / hz)
    nl = FOOT.get("n", nl)
    ph = rng.uniform(0, kit.TAU) + kit.TAU * np.arange(nl) / nl + rng.uniform(-0.3, 0.3, nl)
    amp = rng.uniform(0.6, 1.3, nl)

    def shade(z):
        return dark + (1 - dark) * min(1.0, max(z, 0.0) / 2.0)

    ts = np.linspace(0, 1, FOOT.get("rings", 9)) ** 1.5
    V, UV, CC = [], [], []
    for i, t in enumerate(ts[:-1]):
        z = t * L
        f = (math.exp(-z / hz) - e_end) / (1 - e_end) * (1 - t ** 4)
        s = 1 + (rb / rk - 1) * f
        F = FOOT.get("flare", flare) * f ** 1.3
        c = foot0 + (C0 - foot0) * t - (np.array([0, 0, 0.06]) if i == 0 else 0)
        for j in range(m):
            d = (ang[j] - ph + math.pi) % kit.TAU - math.pi
            lob = float((amp * np.exp(-(d / 0.5) ** 2)).max())
            sc = s * (1 + F * lob)
            V.append(c + off[j] * np.array([sc, sc, t]))
            UV.append((TRUNK["UV"][j][0], TRUNK["UV"][j][1] - (L - z) * kit.BARK_REP))
            CC.append(TRUNK["C"][j] * shade(z) / shade(L))
    q = len(ts) - 1
    b0 = geo.nv
    F_ = []
    for i in range(q):
        for j in range(n):
            a_, b_ = i * m + j, i * m + j + 1
            if i < q - 1:
                F_.append((a_, b_, b_ + m, a_ + m))
            else:
                # the last band closes on the trunk's own first ring
                F_.append((a_, b_, TRUNK["nv0"] + j + 1 - b0, TRUNK["nv0"] + j - b0))
    geo.add(np.array(V), F_, np.array(UV), np.array(CC))
    print("G5_FOOT base %.2f trunk %.2f up to %.2f cells, %d sides, %d roots left to the lobes"
          % (rb * g, rk * g, L * h, n, TRUNK["dropped"]))


kit.flared_trunk = convex_foot
kit.limbs_geo = limbs_geo


def lean_back(parts, spr, ln):
    """The tree kept on its anchor up to ln's 'from' cells and leaning back
    above them, the lean growing as the height above that to the power
    'p', just far enough that the classic view (the renders' camera, 63.4
    degrees down) reaches 'fill' of the drawing's height, and never more
    than 'max' cells. Returns the lean at the top, in cells."""
    co = []
    for ob in parts:
        a = np.empty(len(ob.data.vertices) * 3)
        ob.data.vertices.foreach_get("co", a)
        co.append(a.reshape(-1, 3))
    allc = np.concatenate(co)
    z0, top = ln.get("from", 1.5), float(allc[:, 2].max())

    def ramp(z):
        return np.clip((z - z0) / (top - z0), 0, 1) ** ln.get("p", 1.3)

    want = (spr.hy - spr.top - 0.5) * ln.get("fill", 0.95)
    e = math.atan(1.0 / s2.TILT)

    def reach(Y):
        return float((s2.CELL * (math.sin(e) * (allc[:, 1] + Y * ramp(allc[:, 2])) + math.cos(e) * allc[:, 2])).max())

    lo, hi = 0.0, ln.get("max", 2.2)
    for _ in range(40):
        mid = (lo + hi) / 2
        if reach(mid) < want:
            lo = mid
        else:
            hi = mid
    Y = lo
    for ob, a in zip(parts, co):
        a[:, 1] += Y * ramp(a[:, 2])
        ob.data.vertices.foreach_set("co", a.ravel())
        ob.data.update()
    return Y


def weld(ob, dist=1e-6):
    """The seams where each tube's rings close on themselves joined, so the
    shading runs on round them, and points no face uses dropped."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=dist)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def zmax(parts):
    return max(max((ob.matrix_world @ v.co).z for v in ob.data.vertices) for ob in parts)


def build_tree(name, render=True):
    spec = TREE_SPECS[name]
    spr = s2.Sprite(name)
    g, h, r = spec.get("sturdy", kit.STURDY["limbs"])
    target = spec["target"]
    kit.elev_depth = level_drawn_down
    FOOT.clear()
    FOOT.update(spec.get("foot") or {})
    LIMB.clear()
    LIMB.update(spr=spr, width=spec.get("width"), straight=spec.get("straight"), nested=spec.get("width_nested", False))
    for it in range(spec.get("passes", 4)):
        hk.reset()
        rng = np.random.default_rng(zlib.crc32(name.encode()) + spec.get("seed", 0))
        kit.SHAPE.update(girth=g, height=h, radius=r)
        parts, info = kit.build_limbs(spr, spec, rng)
        top = zmax(parts)
        print("G5_FIT", name, "pass", it, "height factor %.3f" % h, "top %.2f" % top, "target %.2f" % target)
        if abs(top / target - 1) < 0.01 or not 0.6 <= h * target / top <= spec.get("hmax", 1.2):
            break
        h *= target / top
    for p in parts:
        weld(p)
    if spec.get("lean"):
        print("G5_LEAN", name, "%.2f cells back at the top" % lean_back(parts, spr, spec["lean"]))
    for p in parts:
        ash_light(p, np.random.default_rng(zlib.crc32(name.encode()) + 7))
    cal = TB["calibrate"](parts, spr, spec, kit.CALIB_PARTS["limbs"])
    glb = os.path.join(OUT, name + ".glb")
    ob = hk.finish(parts, glb, {"feature": name, "family": "creon"})
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print("G5_BUILT", name, "tris", tris, "top %.2f" % top, info, "calib", cal)
    if render:
        hk._stage()
        TB["deep_alpha"]()
        hk.renders(ob, RENDERS, name, os.path.join(s2.SPRITES, name + ".png"), (spr.hx, spr.hy), scale=2)
        m = TB["overlap_means"](os.path.join(RENDERS, name + "_classic.png"), spr)
        if m is not None:
            print("G5_MATCH", name, "sprite", np.round(m[0], 3), "render", np.round(m[1], 3), "cover", round(m[2], 3),
                  "spill", round(m[3], 3))
        if VIEWS:
            turntable(ob, name)
    return tris


# ---------------------------------------------------------------- smudges

# the foot each smudge's dead tree leaves: kind, radius (cells) and the
# foot's centre on the ground, from the dead tree's model (CreTree07a to
# 09a's root balls meet the ground about 0.4 cells south of the anchor)
SMUDGES = {
    "CreTreesmudge01": {"foot": "poplar", "r": 0.32, "at": (0.0, 0.0)},
    "CreTreesmudge02": {"foot": "broad", "r": 0.62, "at": (0.0, 0.0)},
    "CreTreesmudge03": {"foot": "broad", "r": 0.6, "at": (0.0, 0.0)},
    "CreTreesmudge04": {"foot": "broad", "r": 0.62, "at": (0.0, 0.0)},
    "CreTreesmudge05": {"foot": "ball", "r": 0.85, "at": (0.05, -0.39), "lumps": 4},
    "CreTreesmudge06": {"foot": "ball", "r": 0.78, "at": (-0.12, -0.38), "lumps": 4},
    "CreTreesmudge07": {"foot": "ball", "r": 0.85, "at": (0.03, -0.38), "lumps": 4},
}
ASH = (0.157, 0.157, 0.149)       # #282826, the drawn ash
FLECK = (0.27, 0.30, 0.29)        # #454d4b, its grey-green flecks
COAL = (0.047, 0.047, 0.051)      # #0c0c0d, the charcoal
CHAR = (0.17, 0.14, 0.13)         # #2b2421, charred wood, warm and not quite black
VC = 0.25                         # the bed's vertex colour, its texture holds the rest


def _lin(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _srgb(c):
    c = np.clip(np.asarray(c, np.float64), 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def outline(spr, centre, n=64):
    """The patch's edge as a radius per direction round centre (cells), from
    the picture's coverage blurred over its flecks, smoothed round."""
    pad = 8
    D = kit._blur(np.pad(spr.mask.astype(np.float64), pad), 1.6, 1.6)
    cx, cy = centre
    rb = np.zeros(n)
    for k in range(n):
        a = 2 * math.pi * k / n
        best = 0.3
        for s in np.arange(0.05, 3.5, 1 / 32):
            x, y = cx + s * math.cos(a), cy + s * math.sin(a)
            col, row = spr.hx + x * 16, spr.hy - y * 16
            c, r_ = int(col) + pad, int(row) + pad
            if 0 <= r_ < D.shape[0] and 0 <= c < D.shape[1] and D[r_, c] > 0.42:
                best = s
        rb[k] = best
    # smoothed round, the narrow inlets and lone streaks evened out
    K = np.array([1, 2, 3, 2, 1], np.float64)
    K /= K.sum()
    rb = np.convolve(np.concatenate([rb[-2:], rb, rb[:2]]), K, mode="valid")
    return rb


def radius_at(rb, a):
    n = len(rb)
    u = (a % (2 * math.pi)) / (2 * math.pi) * n
    k = int(u) % n
    f = u - int(u)
    return rb[k] * (1 - f) + rb[(k + 1) % n] * f


def ash_ramp(spr, n=33):
    """The drawn ash's colours from darkest to lightest, n steps, linear:
    the mean colour of each brightness band of the picture."""
    lum = spr.lum[spr.mask]
    rgb = spr.rgb[spr.mask]
    order = np.argsort(lum)
    bands = np.array_split(order, n)
    return np.array([_lin(rgb[b].mean(0)) for b in bands])


def tex_bed(rb, centre, R, ramp, cell, rng, logs=(), lumps=(), size=512):
    """The ash bed's picture: noise at the drawing's grain (cell texels a
    cell) ranked onto the ash's own ramp of colours, from charcoal through
    grey ash to the grey-green flecks, darkest in broken smears under the
    logs and round the lumps lying on it, its alpha a ragged edge round the
    outline with loose flecks beyond it. Colours relative to the vertex
    colour VC. logs are (p0, p1, radius), lumps (centre, radius), in cells."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    x = (xx + 0.5) / size * 2 * R - R
    y = R - (yy + 0.5) / size * 2 * R
    ang = np.arctan2(y, x)
    d = np.hypot(x, y)
    rr = np.vectorize(lambda a: radius_at(rb, a))(ang)
    s = d / np.maximum(rr, 1e-3)
    px = cell / 16.0  # texels per pixel of the drawing
    fine = kit._norm01(kit._blur(rng.random((size, size)), 0.45 * px, 0.45 * px))
    mid = kit._norm01(kit._blur(rng.random((size, size)), 1.6 * px, 1.6 * px))
    big = kit._norm01(kit._blur(rng.random((size, size)), 5.0 * px, 5.0 * px))
    # charcoal smeared under each log, its width wandering with the noise,
    # and dust round each lump, broken up where ash has blown over it
    cx, cy = centre
    smear = np.zeros((size, size))
    for p0, p1, rad in logs:
        ax, ay = p0[0] - cx, p0[1] - cy
        bx, by = p1[0] - p0[0], p1[1] - p0[1]
        t = np.clip(((x - ax) * bx + (y - ay) * by) / max(bx * bx + by * by, 1e-9), -0.12, 1.12)
        dist = np.hypot(x - ax - t * bx, y - ay - t * by)
        w = rad * (1.5 + 1.6 * (mid - 0.5))
        smear = np.maximum(smear, np.clip(1 - dist / np.maximum(w, 1e-3), 0, 1))
    for c, r in lumps:
        dist = np.hypot(x - (c[0] - cx), y - (c[1] - cy))
        smear = np.maximum(smear, 0.7 * np.clip(1 - dist / (1.7 * r), 0, 1))
    smear *= np.clip((big - 0.25) / 0.2, 0, 1)
    v = 0.55 * fine + 0.3 * mid + 0.15 * big - 0.35 * smear + 0.08 * (1 - np.clip(s, 0, 1))
    rank = np.argsort(np.argsort(v.ravel())).reshape(size, size) / (v.size - 1.0)
    k = rank * (len(ramp) - 1)
    i0 = np.floor(k).astype(int).clip(0, len(ramp) - 2)
    f = (k - i0)[..., None]
    col = ramp[i0] * (1 - f) + ramp[i0 + 1] * f
    out = np.ones((size, size, 4))
    out[..., :3] = _srgb(col / VC)
    # a ragged rim: the edge wanders by noise, flecks scatter beyond it
    edge = 1.0 + 0.12 * (mid - 0.5) * 2 + 0.08 * (fine - 0.5) * 2
    inside = s < edge
    loose = (s < 1.24) & (fine > 0.62 + 0.3 * np.clip((s - 1.0) / 0.24, 0, 1))
    out[..., 3] = (inside | loose).astype(np.float64)
    return out


def tex_char(rng, size=128):
    """Charred wood: dark grain along v, checked cracks across it at uneven
    spacing, each one broken off partway round."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    grain = kit._norm01(kit._blur(rng.random((size, size)), 10, 1.2))
    v = (yy + 0.5) / size
    wob = kit._norm01(kit._blur(rng.random((size, size)), 6, 6))
    run = kit._norm01(kit._blur(rng.random((size, size)), 1.5, 12))
    checks = np.clip(1 - np.abs((v * 5 + 1.4 * wob) % 1 - 0.5) * 12, 0, 1) * (run > 0.4)
    g = np.clip(0.55 + 0.45 * grain - 0.5 * checks, 0.12, 1.0)
    out = np.ones((size, size, 4))
    for i in range(3):
        out[..., i] = g
    return out


def poly_ccw(q):
    q = np.asarray(q, np.float64)
    a = np.sum(q[:, 0] * np.roll(q[:, 1], -1) - np.roll(q[:, 0], -1) * q[:, 1])
    return q if a >= 0 else q[::-1].copy()


def poly_area(q):
    q = np.asarray(q, np.float64)
    return 0.5 * abs(np.sum(q[:, 0] * np.roll(q[:, 1], -1) - np.roll(q[:, 0], -1) * q[:, 1]))


def clip_half(q, n, c):
    """The part of convex polygon q on the side of the line through c that
    normal n points to."""
    out = []
    for i in range(len(q)):
        a, b = q[i], q[(i + 1) % len(q)]
        da, db = float(np.dot(a - c, n)), float(np.dot(b - c, n))
        if da >= 0:
            out.append(a)
        if (da >= 0) != (db >= 0):
            out.append(a + (b - a) * (da / (da - db)))
    return np.array(out)


def resample(q, step):
    """Polygon q with corners added along its edges at most step apart."""
    out = []
    for i in range(len(q)):
        a, b = q[i], q[(i + 1) % len(q)]
        m = max(1, int(math.ceil(float(np.linalg.norm(b - a)) / step)))
        out.extend(a + (b - a) * j / m for j in range(m))
    return np.array(out)


def unit2(rng):
    a = rng.uniform(0, 2 * math.pi)
    return np.array([math.cos(a), math.sin(a)])


def block(geo, q, ztop, zpeak, col, zbot=-0.03, wall_dark=0.75, uvs=1.2):
    """An angular block: anticlockwise polygon q (k, 2) standing from zbot
    to its corner heights ztop, its top a fan of facets up to a peak over
    the middle at zpeak. Walls and top are added apart, the walls with UVs
    round and up them, the top with flat-lying UVs."""
    k = len(q)
    run = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.roll(q, -1, 0) - q, axis=1))])
    V, UV = [], []
    for z in (None, 1):
        for i in range(k + 1):
            zz = zbot if z is None else ztop[i % k]
            V.append((q[i % k][0], q[i % k][1], zz))
            UV.append((run[i] * uvs, zz * uvs))
    F = [(i, i + 1, k + 2 + i, k + 1 + i) for i in range(k)]
    geo.add(np.array(V), F, np.array(UV), col * wall_dark)
    cen = q.mean(0)
    V = [(q[i][0], q[i][1], ztop[i]) for i in range(k)] + [(cen[0], cen[1], zpeak)]
    UV = np.array(V)[:, :2] * uvs
    F = [(i, (i + 1) % k, k) for i in range(k)]
    geo.add(np.array(V), F, UV, col)


def lump(geo, c, r, h, rng, col):
    """A charcoal chunk: an irregular prism with a broken, tilted top,
    now and then cracked in two with a gap between the halves."""
    n = int(rng.integers(4, 7))
    a0 = rng.uniform(0, 2 * math.pi)
    angs = a0 + 2 * math.pi * np.arange(n) / n + rng.uniform(-0.3, 0.3, n) * 2 * math.pi / n
    rad = r * rng.uniform(0.7, 1.15, n)
    q = np.stack([c[0] + np.cos(angs) * rad, c[1] + np.sin(angs) * rad], 1)
    pieces = [q]
    if rng.random() < 0.4:
        nrm = unit2(rng)
        cc = np.array(c[:2]) + nrm * rng.uniform(-0.2, 0.2) * r
        pieces = [clip_half(q, sg * nrm, cc) + sg * nrm * 0.012 for sg in (1, -1)]
    tilt = unit2(rng)
    for p in pieces:
        if len(p) < 3:
            continue
        p = poly_ccw(p)
        rel = (p - np.array(c[:2])) @ tilt / r
        zt = np.minimum(c[2] + h * (0.75 + 0.2 * rel) * rng.uniform(0.85, 1.05, len(p)), 0.15)
        rc = float((p.mean(0) - np.array(c[:2])) @ tilt / r)
        zp = min(c[2] + h * (rng.uniform(0.95, 1.1) + 0.1 * rc), 0.15)
        block(geo, p, zt, zp, col, zbot=-0.02, wall_dark=0.8)


def snapped(walls, cap, g, r, h, rng, col, sides=10, lean=0.0, splinters=2, top=0.3):
    """A snapped trunk on the ground point g: a short tapering stump whose
    broken rim runs unevenly round at about h, a few splinters standing
    about 0.1 cells above it (never over top), and a broken face across
    the top with flat-lying UVs, added to cap so it can shade flat."""
    a0 = rng.uniform(0, 2 * math.pi)
    angs = a0 + 2 * math.pi * np.arange(sides) / sides + rng.uniform(-0.25, 0.25, sides) * 2 * math.pi / sides
    ph = rng.uniform(0, 2 * math.pi, 3)
    lob = 1 + 0.1 * np.sin(2 * angs + ph[0]) + 0.06 * np.sin(3 * angs + ph[1]) + rng.uniform(-0.04, 0.04, sides)
    # the break slants, torn high on the side the trunk fell from, and the
    # splinters stand on that side, kept apart, torn down beside each one
    slant = np.cos(angs - ph[2])
    rim = h * (0.8 + 0.35 * slant + rng.uniform(-0.1, 0.1, sides))
    hi = float(rim.max())
    picked = []
    for i in np.argsort(-slant):
        if all(min((i - j) % sides, (j - i) % sides) > 1 for j in picked):
            picked.append(int(i))
        if len(picked) == splinters:
            break
    rr_rim = r * 0.86 * lob
    for i in picked:
        rim[i] = min(top, hi + rng.uniform(0.08, 0.11))
        rr_rim[i] *= 0.92
    for i in picked:
        for j in ((i - 1) % sides, (i + 1) % sides):
            if j not in picked:
                rim[j] *= 0.85
    rings = [(g[2] - 0.02, r * lob), (0.5 * float(rim.min()), r * 0.93 * lob), (rim, rr_rim)]
    urep = max(1, round(2 * math.pi * r * 1.5))
    V, UV, C = [], [], []
    for j, (z, rr) in enumerate(rings):
        z = np.broadcast_to(z, (sides,))
        for k in range(sides + 1):
            i = k % sides
            V.append((g[0] + math.cos(angs[i]) * rr[i] + lean * z[i], g[1] + math.sin(angs[i]) * rr[i], z[i]))
            UV.append((urep * k / sides, z[i]))
            C.append(col * (0.75 + 0.35 * j / 2))
    m = sides + 1
    F = [(j * m + k, j * m + k + 1, (j + 1) * m + k + 1, (j + 1) * m + k) for j in range(2) for k in range(sides)]
    walls.add(np.array(V), F, np.array(UV), np.array(C))
    # the break: the rim, an uneven inner ring on the slant, the heart low
    hz = h * 0.8 * rng.uniform(0.6, 0.75)
    off = rng.uniform(-0.1, 0.1, 2) * r
    V = [(g[0] + math.cos(angs[i]) * rr_rim[i] + lean * rim[i], g[1] + math.sin(angs[i]) * rr_rim[i], rim[i])
         for i in range(sides)]
    for i in range(sides):
        f = 0.45 * rng.uniform(0.8, 1.1)
        z = h * (0.8 + 0.35 * f * slant[i]) * rng.uniform(0.75, 0.95)
        V.append((g[0] + off[0] * 0.5 + math.cos(angs[i]) * rr_rim[i] * f + lean * z, g[1] + off[1] * 0.5 + math.sin(angs[i]) * rr_rim[i] * f, z))
    V.append((g[0] + off[0] + lean * hz, g[1] + off[1], hz))
    V = np.array(V)
    C = [col * 0.8] * sides + [col * 0.7] * sides + [col * 0.55]
    F = [(i, (i + 1) % sides, sides + (i + 1) % sides, sides + i) for i in range(sides)]
    F += [(sides + i, sides + (i + 1) % sides, 2 * sides) for i in range(sides)]
    cap.add(V, F, V[:, :2] * 1.3, np.array(C))


def root_ball(walls, cap, coal, g, r, rng, col, rb, centre):
    """The burnt-out root ball CreTree07a to 09a stand on: a low mound split
    by wandering cracks into three or four uneven slabs, heaved and tilted apart over
    a black core, and three to five root stubs of very different length and
    thickness, some snapped short above the ash, some lying flat half sunk
    in it, one or two arching down into it."""
    m = 14
    a0 = rng.uniform(0, 2 * math.pi)
    angs = a0 + 2 * math.pi * np.arange(m) / m + rng.uniform(-0.2, 0.2, m) * 2 * math.pi / m
    ph = rng.uniform(0, 2 * math.pi, 2)
    lob = 1 + 0.1 * np.sin(2 * angs + ph[0]) + 0.07 * np.sin(3 * angs + ph[1]) + rng.uniform(-0.05, 0.05, m)
    ax, ay = 0.8 * r, 0.75 * r
    g2 = np.array(g[:2])
    q = np.stack([g[0] + np.cos(angs) * ax * lob, g[1] + np.sin(angs) * ay * lob], 1)
    core = g2 + (q - g2) * 0.6
    V = [(p[0], p[1], 0.065) for p in core] + [(g[0], g[1], 0.075)]
    coal.add(np.array(V), [(i, (i + 1) % m, m) for i in range(m)], None, _lin(COAL) * 0.8)
    # two or three cracks, each across the biggest slab and off its middle,
    # so they meet in T and Y joints, each wandering along its length
    pieces, cracks = [q], []
    for j in range(int(rng.integers(2, 4))):
        k = int(np.argmax([poly_area(p) for p in pieces]))
        p = pieces[k]
        t = rng.uniform(0, math.pi)
        nrm = np.array([math.cos(t), math.sin(t)])
        cc = p.mean(0) + nrm * rng.uniform(0.1, 0.3) * rng.choice([-1, 1]) * math.sqrt(poly_area(p) / math.pi)
        halves = [clip_half(p, sg * nrm, cc) for sg in (1, -1)]
        if all(len(h_) >= 3 and poly_area(h_) > 0.04 for h_ in halves):
            pieces[k:k + 1] = halves
            cracks.append((nrm, cc, rng.uniform(0, 2 * math.pi)))
    hm = rng.uniform(0.17, 0.21)

    def mound(p):
        s2_ = ((p[..., 0] - g[0]) / ax) ** 2 + ((p[..., 1] - g[1]) / ay) ** 2
        return hm * np.clip(1 - s2_, 0, 1) ** 0.6

    for p in pieces:
        p = resample(p, 0.26)
        for nrm, cc, ph_ in cracks:
            on = np.abs((p - cc) @ nrm) < 1e-7
            along = (p - cc) @ np.array([-nrm[1], nrm[0]])
            p[on] += nrm[None, :] * (0.04 * np.sin(along[on] * 10 + ph_))[:, None]
        p = poly_ccw(p)
        cen = p.mean(0)
        to_c = cen - p
        dist = np.linalg.norm(to_c, axis=1, keepdims=True)
        p = p + to_c / np.maximum(dist, 1e-6) * np.minimum(0.03, dist * 0.3)
        heave = rng.uniform(-0.02, 0.03)
        tilt = rng.normal(size=2) * 0.06
        zt = np.clip(mound(p) + 0.015 + heave + (p - cen) @ tilt + rng.uniform(-0.012, 0.012, len(p)), 0.055, 0.27)
        zp = float(np.clip(mound(cen) + 0.03 + heave + rng.uniform(0, 0.02), 0.02, 0.28))
        block(cap, p, zt, zp, col * 0.7, zbot=-0.03, wall_dark=0.7)
    # the roots
    n = int(rng.integers(3, 6))
    gaps = rng.uniform(0.5, 1.5, n)
    angs = rng.uniform(0, 2 * math.pi) + np.cumsum(gaps) / gaps.sum() * 2 * math.pi
    kinds = ["lying", "snap"] + [str(k) for k in rng.choice(["lying", "snap", "arch"], n - 2)]
    rng.shuffle(kinds)
    G = np.array([g[0], g[1], 0.0])
    for a, kind in zip(angs, kinds):
        d = np.array([math.cos(a), math.sin(a), 0.0])
        sd = np.array([-d[1], d[0], 0.0])
        e = 0.78 * r
        t = rng.uniform(0.05, 0.15)
        start = G + d * e * 0.55 + [0, 0, 0.02]
        if kind == "snap":
            # a stub burnt off short, rising out of the mound's edge
            el = rng.uniform(0.25, 0.55)
            p1 = G + d * (e + 0.03) + [0, 0, 0.04]
            u = d * math.cos(el) + sd * rng.uniform(-0.3, 0.3) + np.array([0, 0, math.sin(el)])
            u /= np.linalg.norm(u)
            L = min(rng.uniform(0.12, 0.25), (0.25 - 0.8 * t) / max(u[2], 0.05) - 0.96 * t)
            P = [start, p1, p1 + u * max(L, 0.06)]
            R = [t, t * 0.9, t * 0.8]
        elif kind == "lying":
            # a root lying flat, half sunk in the ash
            L = reach_in(rb, centre, G, d, e + 0.2, e + rng.uniform(0.35, 0.85))
            bend = sd * rng.uniform(-0.25, 0.25)
            t = max(t, 0.07)
            P = [start, G + d * (e + 0.05) + [0, 0, 0.04], G + d * (0.5 * (e + L)) + bend * 0.6 + [0, 0, 0.03],
                 G + d * L + bend + [0, 0, 0.02]]
            R = [t, t * 0.9, t * 0.75, t * 0.55]
        else:
            # one arching out over the edge and down into the ash
            t = min(t, 0.11)
            L = reach_in(rb, centre, G, d, e + 0.15, e + rng.uniform(0.2, 0.45))
            P = [start, G + d * e * 0.95 + sd * 0.05 + [0, 0, 0.16], G + d * (0.5 * (e + L)) + sd * 0.1 + [0, 0, 0.1],
                 G + d * L + sd * 0.12 + [0, 0, -0.04]]
            R = [t, t * 0.85, t * 0.6, t * 0.4]
        kit.tube(walls, np.array(P), np.array(R), sides=5, col=col * rng.uniform(0.8, 1.1), cap_base=True, cap_tip=True,
                 vrep=0.35)


def broad_foot(walls, cap, g, r, rng, col, rb, centre):
    """A broadleaf's snapped trunk on three root flares of different
    thickness: one running long into the ash, one short, one snapped off
    above it. Each flare starts inside the stump under its broken top."""
    snapped(walls, cap, g, r, 0.17, rng, col, sides=12, splinters=3)
    gaps = rng.uniform(0.7, 1.3, 3)
    angs = rng.uniform(0, 2 * math.pi) + np.cumsum(gaps) / gaps.sum() * 2 * math.pi
    thick = rng.permutation([0.15, 0.11, 0.08])
    kinds = [str(k) for k in rng.permutation(["long", "mid", "snap"])]
    G = np.array([g[0], g[1], 0.0])
    for a, t, kind in zip(angs, thick, kinds):
        d = np.array([math.cos(a), math.sin(a), 0.0])
        sd = np.array([-d[1], d[0], 0.0]) * rng.uniform(-0.15, 0.15)
        P0, P1 = G + d * r * 0.6 + [0, 0, -0.02], G + d * (r + 0.02) + [0, 0, 0.05]
        if kind == "long":
            L = reach_in(rb, centre, G, d, r + 0.25, r + rng.uniform(0.4, 0.55))
            P = [P0, P1, G + d * (r + 0.5 * (L - r)) + sd + [0, 0, 0.035], G + d * L + sd * 1.5 + [0, 0, -0.03]]
            R = [t, t * 0.85, t * 0.6, t * 0.3]
        elif kind == "mid":
            L = reach_in(rb, centre, G, d, r + 0.12, r + rng.uniform(0.2, 0.3))
            P = [P0, P1, G + d * L + sd + [0, 0, -0.03]]
            R = [t, t * 0.8, t * 0.35]
        else:
            P = [P0, P1 + [0, 0, 0.01], G + d * (r + rng.uniform(0.1, 0.16)) + sd * 0.5 + [0, 0, 0.06]]
            R = [t, t * 0.92, t * 0.85]
        kit.tube(walls, np.array(P), np.array(R), sides=6, col=col * 0.9, cap_base=True, cap_tip=True, vrep=0.4)


def billet(geo, p0, p1, r, col, rng, sides=5):
    """A charred log end: an uneven prism from p0 to p1 with flat broken ends."""
    t = p1 - p0
    L = float(np.linalg.norm(t))
    t = t / L
    u, w = kit.frame(t)
    rings = []
    for e, p in enumerate((p0, p1)):
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides + rng.uniform(-0.2, 0.2)
            rr = r * rng.uniform(0.85, 1.1) * (1.0 if e == 0 else 0.9)
            ring.append(p + (math.cos(a) * u + math.sin(a) * w) * rr + t * rng.uniform(-0.3, 0.3) * r)
        rings.append(ring)
    V = np.array(rings[0] + rings[1])
    F = [(k, (k + 1) % sides, sides + (k + 1) % sides, sides + k) for k in range(sides)]
    F.append(tuple(range(sides - 1, -1, -1)))
    F.append(tuple(range(sides, 2 * sides)))
    uv = np.array([(k / sides, 0.0) for k in range(sides)] + [(k / sides, L) for k in range(sides)])
    cols = np.array([col] * sides + [col * 0.85] * sides)
    geo.add(V, F, uv, cols)


def inside_patch(rb, centre, p, share=0.9):
    q = np.array(p[:2]) - np.array(centre)
    return float(np.hypot(*q)) < radius_at(rb, math.atan2(q[1], q[0])) * share


def reach_in(rb, centre, base, d, lo, hi):
    """The longest run from base along d, between lo and hi cells, whose end
    stays inside the patch."""
    L = hi
    while L > lo and not inside_patch(rb, centre, base + d * L):
        L -= 0.05
    return max(L, lo)


def shade_flat(ob):
    ob.data.polygons.foreach_set("use_smooth", [False] * len(ob.data.polygons))


def build_smudge(name, render=True):
    sm = SMUDGES[name]
    spr = s2.Sprite(name)
    rng = np.random.default_rng(zlib.crc32(name.encode()))
    hk.reset()
    kit.SHAPE.update(girth=1.0, height=1.0, radius=1.0)
    ys, xs = np.nonzero(spr.mask)
    centre = ((xs.mean() + 0.5 - spr.hx) / 16, (spr.hy - ys.mean() - 0.5) / 16)
    rb = outline(spr, centre)
    R = float(rb.max()) * 1.3
    # the bed: a polar grid out past the edge, its alpha cutting the rim
    bed = kit.Geo()
    NA, RINGS = 24, (0.45, 0.8, 1.05, 1.28)
    V = [(centre[0], centre[1], 0.05)]
    for s in RINGS:
        for k in range(NA):
            a = 2 * math.pi * k / NA
            rr = radius_at(rb, a) * s
            x, y = centre[0] + math.cos(a) * rr, centre[1] + math.sin(a) * rr
            # a low mound, highest under the middle, 0.01 at the rim
            z = 0.012 + 0.035 * max(0.0, 1 - s * s) + 0.008 * rng.random()
            V.append((x, y, z))
    V = np.array(V)
    UV = np.stack([(V[:, 0] - centre[0]) / (2 * R) + 0.5, (V[:, 1] - centre[1]) / (2 * R) + 0.5], 1)
    F = [(0, 1 + k, 1 + (k + 1) % NA) for k in range(NA)]
    for i in range(len(RINGS) - 1):
        for k in range(NA):
            a, b = 1 + i * NA + k, 1 + i * NA + (k + 1) % NA
            F.append((a, a + NA, b + NA, b))
    bed.add(V, F, UV, (VC, VC, VC), normals=np.tile([0.0, 0.0, 1.0], (len(V), 1)))
    # a few charcoal chunks, spread apart and clear of the foot, the
    # darkest thing in the drawing (#0c0c0d to #1a1a1a)
    coal, wood, foot, cap = kit.Geo(), kit.Geo(), kit.Geo(), kit.Geo()
    fx, fy = sm["at"]
    g = np.array([fx, fy, 0.0])
    lumps = []
    for _ in range(120):
        if len(lumps) >= sm.get("lumps", 5):
            break
        a = rng.uniform(0, 2 * math.pi)
        rr = radius_at(rb, a) * rng.uniform(0.15, 0.85) ** 0.8
        s = rr / radius_at(rb, a)
        c = np.array([centre[0] + math.cos(a) * rr, centre[1] + math.sin(a) * rr, 0.012 + 0.035 * max(0.0, 1 - s * s)])
        if math.hypot(c[0] - fx, c[1] - fy) < sm["r"] + 0.15:
            continue
        if any(math.hypot(c[0] - o[0][0], c[1] - o[0][1]) < 0.35 for o in lumps):
            continue
        r_ = rng.uniform(0.07, 0.13)
        v = rng.uniform(0x0c, 0x1a) / 255.0
        lump(coal, c, r_, rng.uniform(0.05, 0.1), rng, _lin((v, v, v * 1.02)))
        lumps.append((c, r_))
    # charred log ends from the fallen limbs, lying out from the foot, one
    # across another here and there
    wcol = _lin(CHAR)
    logs = []
    for k in range(sm.get("logs", 3)):
        for _ in range(20):
            a = rng.uniform(0, 2 * math.pi)
            out = np.array([math.cos(a), math.sin(a), 0.0])
            swing = a + rng.uniform(-0.5, 0.5)
            d = np.array([math.cos(swing), math.sin(swing), 0.0])
            p0 = g + out * (sm["r"] + rng.uniform(0.1, 0.3))
            L = reach_in(rb, centre, p0, d, 0.3, rng.uniform(0.55, 0.95))
            if inside_patch(rb, centre, p0, 0.85) and inside_patch(rb, centre, p0 + d * L, 0.9):
                break
        rad = rng.uniform(0.055, 0.08) * (0.8 if sm["foot"] == "poplar" else 1.0)
        z0, z1 = rad + 0.02, rad + 0.02
        if k == 2:
            z1 += 0.06  # resting on another
        billet(wood, p0 + [0, 0, z0], p0 + d * L + [0, 0, z1], rad, wcol * rng.uniform(0.8, 1.2), rng)
        logs.append((p0, p0 + d * L, rad))
    # the foot of the tree that stood here
    fcol = _lin(CHAR)
    if sm["foot"] == "poplar":
        snapped(foot, cap, g, sm["r"], 0.155, rng, fcol, sides=10, lean=0.05, splinters=2)
    elif sm["foot"] == "broad":
        broad_foot(foot, cap, g, sm["r"], rng, fcol, rb, centre)
    else:
        root_ball(foot, cap, coal, g, sm["r"], rng, fcol, rb, centre)
    ctex = tex_char(np.random.default_rng(7))
    char = kit.material("char", kit.image("char", ctex), repeat=True, rough=0.95)
    R2 = 2 * R
    btex = tex_bed(rb, centre, R, ash_ramp(spr), 512 / R2, rng, logs, lumps)
    parts = [bed.to_object(name + "_bed", kit.material("ash", kit.image("ash", btex), cut=True, rough=1.0)),
             coal.to_object(name + "_coal", kit.material("coal", rough=0.9)),
             wood.to_object(name + "_wood", char),
             foot.to_object(name + "_foot", char),
             cap.to_object(name + "_cap", char)]
    for p in parts:
        if p.name.endswith(("_coal", "_cap")):
            shade_flat(p)
    parts = [p for p in parts if len(p.data.polygons)]
    for p in parts:
        # a flat stage sinks no deeper than its foundation allowance
        co = np.empty(len(p.data.vertices) * 3)
        p.data.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        co[:, 2] = np.maximum(co[:, 2], -0.05)
        p.data.vertices.foreach_set("co", co.ravel())
    for p in parts:
        p.visible_shadow = p.name.endswith(("_foot", "_wood", "_coal", "_cap"))
    cal = TB["calibrate"](parts, spr, {"calib": 1.0}, ("bed",))
    glb = os.path.join(OUT, name + ".glb")
    ob = hk.finish(parts, glb, {"feature": name, "family": "creon"})
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print("G5_BUILT", name, "tris", tris, "calib", cal)
    if render:
        hk.renders(ob, RENDERS, name, os.path.join(s2.SPRITES, name + ".png"), (spr.hx, spr.hy), scale=4)
        m = TB["overlap_means"](os.path.join(RENDERS, name + "_classic.png"), spr)
        if m is not None:
            print("G5_MATCH", name, "sprite", np.round(m[0], 3), "render", np.round(m[1], 3), "cover", round(m[2], 3),
                  "spill", round(m[3], 3))
        if VIEWS:
            turntable(ob, name)
    return tris


ALL = list(TREE_SPECS) + list(SMUDGES)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    render = "--norender" not in argv
    global VIEWS
    VIEWS = "--views" in argv
    names = [a for a in argv if not a.startswith("--")] or ALL
    os.makedirs(OUT, exist_ok=True)
    for n in names:
        t0 = time.time()
        try:
            (build_tree if n in TREE_SPECS else build_smudge)(n, render)
        except Exception as e:
            import traceback
            traceback.print_exc()
            print("G5_FAILED", n, repr(e))
        print("G5_TIME", n, "%.1fs" % (time.time() - t0))


main()
