"""Built things for the sprite-to-3D pipeline: walls, fences, houses, huts,
towers and tents, and their ruins cut from them. Carving cannot invent a
roof or a thin rail, so these get real architecture shaped to the
feature's footprint, height and sprite, and carve.paint() then paints the
original sprite onto it.

Same frame as carve.py: one Blender unit is one map cell, -Y toward the
classic camera, Z up, the origin at the feature's anchor on the ground.
"""
import math
import re

import bmesh
import bpy
from mathutils import Matrix, Vector

import carve
import frond

BROKEN_WORDS = ("ruin", "destroy", "damaged", "ransack", "rubble", "debris", "wreck", "burnt",
                "pillag", "sack", "plunder", "smoosh")
WALL_WORDS = ("wall", "palisade")
FENCE_WORDS = ("fence", "gate", "rail")
TOWER_WORDS = ("tower",)
TENT_WORDS = ("tent",)
HOUSE_WORDS = ("house", "hut", "hovel", "building", "barn", "shack", "cottage", "well", "temple", "church")
# kinds only shapes.json assigns, by looking at the sprite
HAND_KINDS = ("flat", "vault", "granary", "well", "site", "lode_ara", "lode_zon")
# the top of a lodestone site's plinth, in cells: lodestones stand on it
SITE_TOP = 0.3
# a ruin is cut from its intact sibling when that is built as one of these
RUIN_KINDS = ("house", "wall", "tower", "flat", "vault", "granary", "well")
# ruins left to carving until their intact pieces are built by hand
RUIN_LATER = ("tarprwall", "zonwall")
# how much of its intact sibling's height a ruin keeps at most; a broken
# wall or tower mostly still stands, and what the cut drops lies flat behind it
RUIN_TOP = 0.6
RUIN_TOP_STANDING = 1.0


def kind_of(r):
    words = (r["description"] + " " + r["category"]).lower()
    name = r["name"].lower()
    if any(w in words for w in BROKEN_WORDS):
        # a broken building, wall or tower is cut from its intact sibling
        if name.startswith(RUIN_LATER) or any(w in words for w in TENT_WORDS + FENCE_WORDS):
            return None
        if any(w in words for w in WALL_WORDS + TOWER_WORDS + HOUSE_WORDS):
            return "ruin"
        return None
    if any(w in words for w in TENT_WORDS) or name.startswith("zonhut"):
        return "tent"
    # "tow" alone would match the Ambtown sound emitters
    if any(w in words for w in TOWER_WORDS) or re.search(r"tow(er)?\d", name):
        return "tower"
    if any(w in words for w in FENCE_WORDS):
        return "fence"
    if any(w in words for w in WALL_WORDS):
        return "wall"
    if any(w in words for w in HOUSE_WORDS):
        return "house"
    return None


def sibling_name(name):
    """The intact piece a broken one was drawn from: the name less its a or b."""
    m = re.match(r"(.*\d)[ab]$", name, re.I)
    return m.group(1) if m else None


def _box(bm, cx, cy, z0, sx, sy, sz):
    m = bmesh.ops.create_cube(bm, size=1.0)
    for v in m["verts"]:
        v.co.x = cx + v.co.x * sx
        v.co.y = cy + v.co.y * sy
        v.co.z = z0 + (v.co.z + 0.5) * sz


def _cyl(bm, cx, cy, z0, r0, r1, h, seg=16):
    m = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r0, radius2=r1, depth=h)
    for v in m["verts"]:
        v.co.x += cx
        v.co.y += cy
        v.co.z += z0 + h / 2


def _lathe(bm, cx, cy, profile, seg=20):
    """A closed solid of revolution about a vertical axis: profile is
    (radius, z) from the bottom centre round to the top centre, and a
    radius of 0 is a point on the axis."""
    rings = []
    for rr, z in profile:
        if rr <= 0:
            rings.append([bm.verts.new((cx, cy, z))])
        else:
            rings.append([bm.verts.new((cx + math.cos(2 * math.pi * i / seg) * rr,
                                        cy + math.sin(2 * math.pi * i / seg) * rr, z)) for i in range(seg)])
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            q = [a[i % len(a)], a[j % len(a)], b[j % len(b)], b[i % len(b)]]
            bm.faces.new([v for k, v in enumerate(q) if v not in q[:k]])


def _dark_middle(r, spr, cx, rad, h):
    """Whether a round thing's drawn middle is darker than its rim, as an
    open shaft is and a lid is not."""
    import numpy as np
    hx, hy = r["sprite"]["hotspot"]
    yy, xx = np.mgrid[0:spr.h, 0:spr.w] + 0.5
    d = np.hypot(xx - (hx + 16 * cx), yy - (hy - 8 * h)) / (16 * rad)
    lum = spr.rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    a = np.array(spr.alpha)
    mid, rim = a & (d < 0.45), a & (d > 0.7) & (d < 1.0)
    return mid.sum() > 8 and rim.sum() > 8 and lum[mid].mean() < 0.8 * lum[rim].mean()


def _hip_roof(bm, sx, sy, z0, h, over, cy=0.0):
    hx, hy = sx / 2 + over, sy / 2 + over
    if sx >= sy:
        ridge = [(-(sx - sy) / 2, cy, z0 + h), ((sx - sy) / 2, cy, z0 + h)]
    else:
        ridge = [(0, cy - (sy - sx) / 2, z0 + h), (0, cy + (sy - sx) / 2, z0 + h)]
    c = [(-hx, cy - hy, z0), (hx, cy - hy, z0), (hx, cy + hy, z0), (-hx, cy + hy, z0)]
    v = [bm.verts.new(p) for p in c + ridge]
    a, b = v[4], v[5]
    if sx >= sy:
        faces = [(v[0], v[1], b, a), (v[1], v[2], b), (v[2], v[3], a, b), (v[3], v[0], a), (v[3], v[2], v[1], v[0])]
    else:
        faces = [(v[0], v[1], a), (v[1], v[2], b, a), (v[2], v[3], b), (v[3], v[0], a, b), (v[3], v[2], v[1], v[0])]
    for f in faces:
        try:
            bm.faces.new(f)
        except ValueError:
            pass


def _top_row(spr):
    for y in range(spr.h):
        if any(spr.alpha[y]):
            return y
    return 0


def _ring(r, spr, kind):
    """Centre offset and radius of a round thing, read off the widest
    row of the sprite at or above the anchor."""
    hx, hy = r["sprite"]["hotspot"]
    spans = [spr.row_span(y) for y in range(max(0, hy - 60), min(spr.h, hy + 1))]
    spans = [s for s in spans if s]
    if not spans:
        return 0.0, 0.5
    lo, hi = max(spans, key=lambda s: s[1] - s[0])
    rad = (hi - lo + 1) / 2 / 16.0 * (0.8 if kind == "well" else 0.95)
    return ((lo + hi + 1) / 2 - hx) / 16.0, max(0.3, rad)


def _disc_y(bm, cx, cz, r, thick, seg=20):
    """A round plate standing upright, facing south (-Y)."""
    rings = []
    for y in (-thick / 2, thick / 2):
        rings.append([bm.verts.new((cx + math.cos(2 * math.pi * i / seg) * r, y,
                                    cz + math.sin(2 * math.pi * i / seg) * r)) for i in range(seg)])
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[1])))


def _back(r, spr, kind):
    """How far north of the anchor the sprite's top row stands, in cells."""
    fx, fz = r["footprint"]
    if kind in ("house", "tent", "fence") or (kind == "wall" and max(fx, fz) > 2 * min(fx, fz)):
        return 0.0
    if kind == "flat" or (kind == "vault" and fz > fx):
        # the far edge of the roof is the highest thing drawn
        return fz / 2 * 0.85
    if kind == "vault":
        return 0.0
    if kind in ("well", "granary"):
        # the far rim is the highest thing drawn
        return _ring(r, spr, kind)[1]
    if kind == "tower":
        return max(0.4, min(fx, fz) / 2 * 0.9, spr.w / 32.0 * 0.85)
    return fz / 2 * 0.95


def drawn_height(r, spr, kind, back=None):
    """How tall the sprite shows the thing, in cells. The unit files'
    height is not the drawn height (walls say 12 cells and are drawn 1),
    so read it off the silhouette: the top row is the highest point, seen
    at the depth where that point stands."""
    hx, hy = r["sprite"]["hotspot"]
    back = _back(r, spr, kind) if back is None else back
    return max(0.4, (hy - back * 16.0 - _top_row(spr)) / 8.0)


def _split(col):
    """The row that best splits a run of colours into two flat tones."""
    import numpy as np
    n = len(col)
    c = np.cumsum(col, 0)
    c2 = np.cumsum((col ** 2).sum(1))
    k = np.arange(2, n - 2)
    a, b = c[k - 1], c[-1] - c[k - 1]
    sse = c2[k - 1] - (a ** 2).sum(1) / k + c2[-1] - c2[k - 1] - (b ** 2).sum(1) / (n - k)
    return int(k[np.argmin(sse)])


def _columns(spr):
    """(x, top, step, bottom, tiled) for each column 8 or more rows tall:
    step is where it changes from cap (roof or coping) to wall face, at the
    end of a red tile run when it starts with one, else at its best split."""
    import numpy as np
    a = np.array(spr.alpha)
    rgb = spr.rgb
    red = (rgb[..., 0] > 1.5 * rgb[..., 1]) & (rgb[..., 0] > 1.8 * rgb[..., 2]) & (rgb[..., 0] > 0.3) & a
    out = []
    for x in range(spr.w):
        ys = np.nonzero(a[:, x])[0]
        if len(ys) < 8:
            continue
        t, b = int(ys[0]), int(ys[-1]) + 1
        rr = red[t:b, x]
        if rr[:4].mean() > 0.5:
            # the tile run ends at 4 rows without red, not at a dark tile line
            gap = np.convolve(1.0 - rr, np.ones(4), "valid") >= 4
            out.append((x, t, t + (int(np.argmax(gap)) if gap.any() else len(rr)), b, True))
        else:
            out.append((x, t, t + _split(rgb[t:b, max(0, x - 1):x + 2].mean(1)), b, False))
    return out


def bands(spr, cols):
    """Median top, step and bottom rows over the middle half of the columns,
    the step None unless half of them agree on it within 3 rows, and whether
    the cap is roof tile. A box D deep and H tall shows its cap on 16 D rows
    and its front on 8 H rows."""
    import numpy as np
    xs = np.nonzero(np.array(spr.alpha).any(0))[0]
    q = (xs[-1] - xs[0]) // 4
    mid = [c for c in cols if xs[0] + q <= c[0] <= xs[-1] - q] or cols
    if not mid:
        return None, None, None, False
    steps = np.array([c[2] for c in mid])
    best = max(range(int(steps.min()), int(steps.max()) + 1), key=lambda s: int((abs(steps - s) <= 3).sum()))
    near = abs(steps - best) <= 3
    top, bot = (float(np.median([c[i] for c in mid])) for i in (1, 3))
    step = float(np.median(steps[near])) if near.mean() >= 0.5 else None
    return top, step, bot, float(np.mean([c[4] for c in mid])) > 0.6


def _hull_mask(pts, h, w):
    """Sprite pixels inside the convex hull of screen points."""
    import numpy as np
    from mathutils.geometry import convex_hull_2d
    hull = [pts[i] for i in convex_hull_2d([Vector(p) for p in pts])]
    yy, xx = np.mgrid[0:h, 0:w] + 0.5
    turn = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(hull, hull[1:] + hull[:1]))
    m = np.ones((h, w), bool)
    for (ax, ay), (bx, by) in zip(hull, hull[1:] + hull[:1]):
        m &= ((bx - ax) * (yy - ay) - (by - ay) * (xx - ax)) * turn >= 0
    return m


def _turn(r, spr, cols):
    """A wall drawn on the slant, as the oriented rectangle of its cap on the
    ground plane: (angle, centre x, centre y, length, width, height), or
    None. Only when the cap is over 1.5 times as long as wide and turned
    over 15 degrees, the base line slants the same way (the corner pieces
    that join a slanted run stand square), and the turned box outlines the
    sprite better than the sprite's own bounding box does."""
    import numpy as np
    hx, hy = r["sprite"]["hotspot"]
    a = np.array(spr.alpha)
    pts = [(x, y) for x, t, s, b, _ in cols for y in range(t, s) if a[y, x]]
    if len(pts) < 20:
        return None
    H = float(np.median([b - s for x, t, s, b, _ in cols])) / 8.0
    g = np.array([((x + 0.5 - hx) / 16.0, (hy - y - 0.5) / 16.0) for x, y in pts])
    c = g.mean(0)
    ev, vec = np.linalg.eigh(np.cov((g - c).T))
    th = math.atan2(vec[1, 1], vec[0, 1])
    off = math.degrees(th) % 90
    if ev[1] < 2.25 * ev[0] or min(off, 90 - off) < 15:
        return None
    xs = [x for x, t, s, b, _ in cols]
    mid = [(x, b) for x, t, s, b, _ in cols if xs[0] + (xs[-1] - xs[0]) / 5 <= x <= xs[-1] - (xs[-1] - xs[0]) / 5]
    if len(mid) < 2:
        return None
    rise = -math.degrees(math.atan(np.polyfit([m[0] for m in mid], [m[1] for m in mid], 1)[0]))
    if abs(rise) < 10 or (rise > 0) != (math.degrees(th) % 180 < 90):
        return None
    u, v = (g - c) @ vec[:, 1], (g - c) @ vec[:, 0]
    L = float(np.percentile(u, 98) - np.percentile(u, 2))
    W = float(np.percentile(v, 98) - np.percentile(v, 2))
    # the cap stands H up, so it is drawn H/2 north of where it stands
    cx, cy = float(c[0]), float(c[1]) - H / 2
    cs, sn = math.cos(th), math.sin(th)
    outline = [(hx + 16 * (cx + du * cs - dv * sn), hy - 16 * (cy + du * sn + dv * cs) - 8 * z)
               for du in (-L / 2, L / 2) for dv in (-W / 2, W / 2) for z in (0, H)]
    m = _hull_mask(outline, spr.h, spr.w)
    ys, xs = np.nonzero(a)
    bb = np.zeros_like(a)
    bb[ys.min():ys.max() + 1, xs.min():xs.max() + 1] = True
    if (m & a).sum() / (m | a).sum() < (bb & a).sum() / (bb | a).sum() + 0.05:
        return None
    return th, cx, cy, L, W, H


def _posts(r, spr):
    """An east-west fence as its sprite draws it: posts at the columns whose
    opaque run is at least 70 percent of the tallest, merged within 3 px,
    and rails at the rows where the other columns' pixels cluster. Returns
    (ground y, [(x, height)], [(x0, x1, z, height)]) in cells, or None when
    fewer than 2 posts are found."""
    import numpy as np
    hx, hy = r["sprite"]["hotspot"]
    a = np.array(spr.alpha)
    h = a.shape[0]
    has = a.any(0)
    top = np.where(has, a.argmax(0), h)
    bot = np.where(has, h - a[::-1].argmax(0), 0)
    run = bot - top
    groups = []
    for x in np.nonzero(run >= 0.7 * run.max())[0]:
        if groups and x - groups[-1][-1] <= 3:
            groups[-1].append(x)
        else:
            groups.append([x])
    if len(groups) < 2:
        return None
    ground = float(np.median([bot[g].max() for g in groups]))
    posts = [(((g[0] + g[-1] + 1) / 2 - hx) / 16.0, (ground - top[g].min()) / 8.0) for g in groups]
    between = has & (run < 0.7 * run.max())
    share = a[:, between].mean(1) if between.any() else np.zeros(h)
    rails, taken, zh = [], [], 0.12
    for y in np.argsort(-share, kind="stable"):
        # a rail at each peak of the rows the columns between posts draw
        if share[y] < 0.45 * share.max() or share.max() == 0:
            break
        if any(abs(y - q) < 3 for q in taken):
            continue
        taken.append(y)
        # the rail where it is drawn, broken where the sprite breaks it
        xs = np.nonzero(a[max(0, y - 1):y + 2].any(0))[0]
        for seg in np.split(xs, np.nonzero(np.diff(xs) > 4)[0] + 1):
            rails.append(((seg[0] - hx) / 16.0, (seg[-1] + 1 - hx) / 16.0, (ground - y - 0.5 - 4 * zh) / 8.0, zh))
    return (hy - ground) / 16.0, posts, rails


def plan(r, kind, spr):
    """What build() makes of a feature: its height H, how far north its top
    row stands ('back'), and for walls, flat roofs and north-south vaults the
    box its cap and face bands give ('block': depth, centre y), a slanted
    wall's fit ('turn') and whether the cap is roof tile ('roofed')."""
    fx, fz = r["footprint"]
    tdf = (r["height"] or 32) / 16.0
    H = max(0.5, tdf)
    p = {"back": 0.0, "roofed": False}
    if kind in ("lode_ara", "lode_zon"):
        # a lodestone stands as tall as its 3DO card
        H = max(0.5, r["height"] / 16.0)
    elif spr is not None and kind != "site":
        p["back"] = _back(r, spr, kind)
        dh = drawn_height(r, spr, kind, p["back"])
        H = min(H, dh) if r["height"] else dh
    long_wall = kind == "wall" and max(fx, fz) > 2 * min(fx, fz)
    if spr is not None and (kind == "flat" or (kind == "vault" and fz > fx) or kind == "wall"):
        hx, hy = r["sprite"]["hotspot"]
        cols = _columns(spr)
        top, step, bot, p["roofed"] = bands(spr, cols)
        turn = _turn(r, spr, cols) if kind == "wall" else None
        if turn:
            p["turn"] = turn
            th, cx, cy, L, W, H = turn
            p["back"] = cy + (abs(L * math.sin(th)) + abs(W * math.cos(th))) / 2
        elif top is not None and not long_wall:
            D = (step - top) / 16.0 if step is not None else 0.0
            Hb = (bot - step) / 8.0 if step is not None else 0.0
            if not (0.3 <= D <= 1.3 * fz and 0.5 <= Hb <= tdf) and kind != "wall":
                # no clear step: a flat roof or vault at least half its height
                Hb = max(H, tdf / 2)
                D = max(fz * 0.85 / 2, (bot - top - 8 * Hb) / 16.0)
                Hb = (bot - top - 16 * D) / 8.0
            if 0.3 <= D <= 1.3 * fz and 0.5 <= Hb <= tdf:
                yc = (hy - bot) / 16.0 + D / 2
                p["block"], p["back"], H = (D, yc), yc + D / 2, Hb
    p["H"] = H
    return p


def _skyline(r, spr, xa, xb, yback, H):
    """A wall the sprite draws with a ragged top (stacked stones, stakes):
    [(x0, x1, height)], runs of columns as tall as their top pixel shows at
    the wall's back edge, or None when the top is level."""
    import numpy as np
    if spr is None:
        return None
    hx, hy = r["sprite"]["hotspot"]
    a = np.array(spr.alpha)
    has, top = a.any(0), a.argmax(0)
    cols = [x for x in range(spr.w) if has[x] and xa <= (x + 0.5 - hx) / 16.0 <= xb]
    if len(cols) < 8:
        return None
    z = np.array([min(H, max(0.25, (hy - 16.0 * yback - top[x]) / 8.0)) for x in cols])
    # a lone stray pixel is not a stone
    z = np.array([np.median(z[max(0, i - 1):i + 2]) for i in range(len(z))])
    if np.percentile(z, 95) - np.percentile(z, 5) < 0.75:
        return None
    runs = []
    for x, h in zip(cols, z):
        x0 = (x - hx) / 16.0
        if runs and abs(runs[-1][1] - x0) < 1e-6 and abs(runs[-1][2] - h) < 0.2:
            runs[-1][1] = x0 + 1 / 16.0
        else:
            runs.append([x0, x0 + 1 / 16.0, float(h)])
    return runs


def _wall_block(bm, cy, sx, sy, H, roofed):
    """A wall piece: a block under a coping, or under a hip roof when the
    sprite's cap is roof tile."""
    if roofed:
        over = sy * 0.05
        _box(bm, 0, cy, 0, sx, sy - 2 * over, H)
        _hip_roof(bm, sx, sy - 2 * over, H, 0.35 * min(sx, sy), over, cy)
    else:
        _box(bm, 0, cy, 0, sx, sy, H)
        _box(bm, 0, cy, H, sx * 1.06, sy * 1.06, 0.12)


def build(r, kind, spr=None, H=None):
    """The kind's architecture for a feature; H, when given, is the height
    to build it at instead of its own (a ruin's)."""
    fx, fz = r["footprint"]
    # the drawn width, which often overhangs the footprint
    drawn = r["sprite"]["w"] / 16.0
    p = plan(r, kind, spr)
    H = p["H"] if H is None else H
    bm = bmesh.new()
    if kind == "wall" and "turn" in p:
        th, cx, cy, L, W, _ = p["turn"]
        _wall_block(bm, 0.0, L, W, H, p["roofed"])
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(th, 3, "Z"), verts=bm.verts)
        bmesh.ops.translate(bm, vec=(cx, cy, 0), verts=bm.verts)
    elif kind in ("wall", "fence"):
        along_x = fx >= fz
        length = max(fx, fz)
        fence = _posts(r, spr) if kind == "fence" and along_x and spr is not None else None
        if kind == "wall" and max(fx, fz) <= 2 * min(fx, fz):
            # walls are laid a piece at a time: a squarish piece is a block
            sx = min(fx * 0.95, drawn * 0.95)
            sy, yc = p.get("block", (fz * 0.9, 0.0))
            sky = None if p["roofed"] else _skyline(r, spr, -sx / 2, sx / 2, yc + sy / 2, H)
            for x0, x1, h in sky or ():
                _box(bm, (x0 + x1) / 2, yc, 0, x1 - x0, sy, h)
            if not sky:
                _wall_block(bm, yc, sx, sy, H, p["roofed"])
        elif kind == "wall":
            thick = min(0.8 * min(fx, fz), 0.35 + 0.15 * min(fx, fz))
            if not along_x:
                # a north-south run is as thick as it is drawn
                thick = min(max(thick, drawn * 0.9), min(fx, fz) * 0.9)
            sky = _skyline(r, spr, -length / 2, length / 2, thick / 2, H) if along_x and not p["roofed"] else None
            for x0, x1, h in sky or ():
                _box(bm, (x0 + x1) / 2, 0, 0, x1 - x0, thick, h)
            if not sky and along_x:
                _box(bm, 0, 0, 0, length, thick, H)
                # a coping along the top
                _box(bm, 0, 0, H, length, thick * 1.2, 0.12)
            elif not sky:
                _box(bm, 0, 0, 0, thick, length, H)
                _box(bm, 0, 0, H, thick * 1.2, length, 0.12)
        elif fence:
            # posts and rails where the sprite draws them
            yf, posts, rails = fence
            for px, ph in posts:
                _box(bm, px, yf, 0, 0.12, 0.12, ph)
            if not rails:
                ph = max(ph for _, ph in posts)
                x0, x1 = min(px for px, _ in posts), max(px for px, _ in posts)
                rails = [(x0, x1, ph * 0.45 - 0.05, 0.1), (x0, x1, ph * 0.85 - 0.05, 0.1)]
            for x0, x1, z, zh in rails:
                _box(bm, (x0 + x1) / 2, yf, z, x1 - x0, 0.1, zh)
        else:
            posts = max(2, int(round(length)) + 1)
            for i in range(posts):
                t = -length / 2 + i * length / (posts - 1)
                px, py = (t, 0) if along_x else (0, t)
                _box(bm, px, py, 0, 0.12, 0.12, H)
            for zr in (H * 0.45, H * 0.85):
                if along_x:
                    _box(bm, 0, 0, zr - 0.05, length, 0.07, 0.1)
                else:
                    _box(bm, 0, 0, zr - 0.05, 0.07, length, 0.1)
    elif kind == "tower":
        rad = max(0.4, min(fx, fz) / 2 * 0.9, drawn / 2 * 0.85)
        shaft = H * 0.85
        _cyl(bm, 0, 0, 0, rad * 1.05, rad * 0.95, shaft)
        _cyl(bm, 0, 0, shaft, rad * 1.1, rad * 1.1, H * 0.05)
        for i in range(10):
            a = 2 * math.pi * i / 10
            _box(bm, math.cos(a) * rad, math.sin(a) * rad, shaft + H * 0.05, rad * 0.35, rad * 0.35, H * 0.1)
    elif kind == "tent":
        rad = max(0.5, min(fx, fz) / 2 * 0.95)
        stretch = max(fx, fz) / min(fx, fz)
        if stretch > 1.3 and spr is not None:
            # a long pavilion: an oval base, its peak where the canvas
            # starts rather than at the tip of its pole
            hy = r["sprite"]["hotspot"][1]
            canvas = next((y for y in range(spr.h) if sum(spr.alpha[y]) >= 8), 0)
            H = min(H, max(0.4, (hy - canvas) / 8.0))
        m = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=rad, radius2=0.05, depth=H)
        for v in m["verts"]:
            v.co.z += H / 2
            if stretch > 1.3:
                v.co[0 if fx > fz else 1] *= stretch
    elif kind == "flat":
        sx = fx * 0.85
        sy, yc = p.get("block", (fz * 0.85, 0.0))
        _box(bm, 0, yc, 0, sx, sy, H)
        t, ph = 0.12, 0.18
        _box(bm, 0, yc - sy / 2 + t / 2, H, sx, t, ph)
        _box(bm, 0, yc + sy / 2 - t / 2, H, sx, t, ph)
        _box(bm, -sx / 2 + t / 2, yc, H, t, sy, ph)
        _box(bm, sx / 2 - t / 2, yc, H, t, sy, ph)
    elif kind == "vault":
        sx = fx * 0.85
        sy, yc = p.get("block", (fz * 0.85, 0.0))
        along_y = fz > fx
        span = sx if along_y else sy
        length = sy if along_y else sx
        rise = min(span / 2, H * 0.45)
        wall_h = H - rise
        _box(bm, 0, yc, 0, sx, sy, wall_h)
        seg = 12
        ends = []
        for e in (-1, 1):
            ring = []
            for i in range(seg + 1):
                a = math.pi * i / seg
                u, z = math.cos(a) * span / 2, wall_h + math.sin(a) * rise
                ring.append(bm.verts.new((u, yc + e * length / 2, z) if along_y else (e * length / 2, yc + u, z)))
            ends.append(ring)
        for i in range(seg):
            bm.faces.new((ends[0][i], ends[0][i + 1], ends[1][i + 1], ends[1][i]))
        for ring in ends:
            bm.faces.new(ring)
    elif kind == "granary":
        cx, rad = _ring(r, spr, kind)
        roof = min(H * 0.25, rad * 0.6)
        shaft = H - roof
        _cyl(bm, cx, 0, 0, rad, rad * 0.97, shaft, seg=20)
        _cyl(bm, cx, 0, shaft, rad * 1.1, 0.05, roof, seg=20)
    elif kind == "lode_ara":
        # a six-sided plinth and a tall crystal on it
        rad = min(fx, fz) / 2 * 0.8
        _cyl(bm, 0, 0, 0, rad, rad * 0.92, H * 0.18, seg=6)
        _cyl(bm, 0, 0, H * 0.18, rad * 0.3, rad * 0.3, H * 0.12, seg=6)
        _cyl(bm, 0, 0, H * 0.3, rad * 0.3, 0.02, H * 0.7, seg=6)
    elif kind == "lode_zon":
        # two posts, a beam and the gong hanging between them
        half = min(fx, fz) / 2 * 0.85
        for e in (-1, 1):
            _cyl(bm, e * half, 0, 0, 0.16, 0.13, H, seg=10)
        _box(bm, 0, 0, H * 0.88, half * 2 + 0.3, 0.16, 0.14)
        _disc_y(bm, 0, H * 0.45, min(half * 0.7, H * 0.36), 0.12)
    elif kind == "site":
        # a lodestone site: a two-step round plinth, its top the site art
        rad = max(min(fx, fz) / 2 * 0.95, spr.w / 32.0)
        _cyl(bm, 0, 0, 0, rad * 1.12, rad * 1.08, SITE_TOP * 0.45, seg=32)
        _cyl(bm, 0, 0, SITE_TOP * 0.45, rad, rad, SITE_TOP * 0.55, seg=32)
        # the art, spr.w / 32 across, is scaled out to cover the whole top
        site_art = spr.w / 32.0 / rad
    elif kind == "well":
        cx, rad = _ring(r, spr, kind)
        ring_h = min(H, 0.5)
        # a ring of stone round a dark shaft is sunk in the middle: a shallow
        # funnel, steep enough to be painted as the classic camera saw it
        if _dark_middle(r, spr, cx, rad, ring_h):
            floor = ring_h * 0.6
            _lathe(bm, cx, 0, [(0, 0), (rad, 0), (rad, ring_h), (rad * 0.62, ring_h),
                               (rad * 0.62 - (ring_h - floor), floor), (0, floor)])
        else:
            _cyl(bm, cx, 0, 0, rad, rad, ring_h, seg=16)
        if H > 1.2:
            # the frame that holds the bucket
            for e in (-1, 1):
                _box(bm, cx + e * rad * 0.9, 0, 0, 0.1, 0.1, H * 0.9)
            _box(bm, cx, 0, H * 0.9 - 0.08, rad * 2, 0.08, 0.08)
    else:  # house
        # as wide as its roof is drawn, which often overhangs the footprint
        sx, sy = max(fx, drawn) * 0.85, fz * 0.85
        wall_h = H * 0.42
        _box(bm, 0, 0, 0, sx, sy, wall_h)
        _hip_roof(bm, sx, sy, wall_h, H - wall_h, over=0.15 * min(sx, sy) / 2 + 0.1)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(r["name"])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(r["name"], me)
    bpy.context.collection.objects.link(ob)
    # enough faces for the painting to follow, then smooth shading off:
    # architecture keeps its hard edges
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    sub = ob.modifiers.new("sub", "SUBSURF")
    sub.subdivision_type = "SIMPLE"
    sub.levels = 3
    bpy.ops.object.convert(target="MESH")
    lo = [min(v.co[i] for v in me.vertices) for i in range(2)]
    hi = [max(v.co[i] for v in me.vertices) for i in range(2)]
    ob["box"] = (lo[0], hi[0], lo[1], hi[1])
    if kind in ("lode_ara", "lode_zon") and spr is not None:
        # the painting was drawn for a card leaning back: stretch it down
        hx0, hy0 = r["sprite"]["hotspot"]
        ob["zscale"] = max(1.0, (hy0 - _top_row(spr)) / 8.0 / H)
    if kind == "site":
        ob["site_top"] = SITE_TOP
        ob["standTop"] = SITE_TOP
        ob["site_art"] = site_art
    return ob


def build_ruin(r, spr, sib_spr):
    """A broken building, wall or tower: its intact sibling's shape at the
    ruin's own drawn height (at most RUIN_TOP of the intact one, never the
    unit file's), cut to the ruin's outline, over a flat skirt of its
    rubble. Returns the solid, the skirt, and the sprite pixels the skirt
    keeps: those the solid does not cover from the classic camera."""
    import numpy as np
    from mathutils.bvhtree import BVHTree
    sib, kind = r["sibling"], r["sibling_kind"]
    p = plan(sib, kind, sib_spr)
    hx, hy = r["sprite"]["hotspot"]
    top = RUIN_TOP_STANDING if kind in ("wall", "tower") else RUIN_TOP
    H = min(max(0.4, (hy - 16.0 * p["back"] - _top_row(spr)) / 8.0), top * p["H"])
    if kind == "well":
        # the ring alone: a ruined well's frame is down
        H = min(H, 1.2)
    ob = build(sib, kind, sib_spr, H)
    ob.name = ob.data.name = r["name"]
    solid = ob.data.copy()
    bpy.ops.object.select_all(action="DESELECT")
    # every point the ruin's sprite covers, from a little under the ground
    hull = carve.carve(r, spr, hull=H + 2.0)[0]
    hull.location.z = -0.1
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    cut = ob.modifiers.new("cut", "BOOLEAN")
    cut.operation, cut.solver, cut.object = "INTERSECT", "EXACT", hull
    # the shapes are overlapping closed parts (a roof on a box, crenels on a drum)
    cut.use_self = True
    try:
        bpy.ops.object.modifier_apply(modifier=cut.name)
    except RuntimeError:
        ob.modifiers.clear()
    me = bpy.data.meshes[hull.data.name]
    bpy.data.objects.remove(hull, do_unlink=True)
    bpy.data.meshes.remove(me)
    if len(ob.data.polygons) < 4:
        # the cut failed: the plain sibling at the ruin's height
        print("RUIN_FALLBACK", r["name"], flush=True)
        ob.data = solid
    me = ob.data
    # the cut leaves an empty material slot that paint() would paint past,
    # and now and then faces turned inward
    me.materials.clear()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    for q in me.polygons:
        q.material_index = 0
    bvh = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [tuple(q.vertices) for q in me.polygons])
    keep = np.array(spr.alpha)
    look = Vector((0.0, 1.0, -2.0)).normalized()
    z0 = H + 3.0
    for py, px in zip(*np.nonzero(keep)):
        start = Vector(((px + 0.5 - hx) / 16.0, (hy - py - 0.5 - 8.0 * z0) / 16.0, z0))
        if bvh.ray_cast(start, look)[0] is not None:
            keep[py, px] = False
    skirt = frond.build_decal(r, spr)
    skirt.name = r["name"] + "_skirt"
    return ob, skirt, keep
