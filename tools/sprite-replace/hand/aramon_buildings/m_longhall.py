"""The Aramon long hall: a hipped hall turned 24 degrees, a veranda on
posts along its front under a lean-to roof, a clerestory of small
windows between the two roofs, a gabled cross wing at its west end with
a hanging sign, and a low lean-to at its east end; and its ruins."""
import math
import os
import random

import kit
from kit import Mat, px
from m_halls import RoofGeom, tiled_body
from models import P, model

# walls a little lower and the cross wing broader than the pixel fit, so
# neither reads tall and thin from low down
HALL = P(cx=0.1, cy=1.66, yaw=24.0, L=11.6, W=6.1, H=3.8, R=1.5, over=0.25, over_end=0.5, hip=2.0, T=0.3)
# the cross wing's ridge runs back up the picture, its gable end at col 52 row 54
WING5 = P(cx=-5.0, cy=1.5, yaw=114.0, L=4.7, W=5.3, H=3.9, R=2.15, over=0.4, over_end=0.4, hip=-1, T=0.3)
VER = dict(u0=-6.8, u1=6.6, depth=5.1, z_wall=2.25, z_eave=1.75)


def hall_mats5(n, seed=131):
    return {"roof": Mat(n + "_roof", tex=kit.tex_tiles(n + "_roof", "#7a6658", rows=12, cols=4, var=0.12, lip=0.2,
                                                       moss="#4a4a30", moss_amt=0.12, seed=seed), uv=3.0),
            "wall": Mat(n + "_hwall", tex=kit.tex_planks(n + "_hwall", "#3a2c20", boards=8, seed=seed + 1), uv=2.0),
            "timber": Mat(n + "_timber", tex=kit.tex_planks(n + "_timber", "#1e1812", boards=2, seed=seed + 2),
                          uv=1.0),
            "cap": Mat(n + "_rcap", tex=kit.tex_planks(n + "_rcap", "#4a3a30", boards=2, seed=seed + 3), uv=1.0),
            "dark": Mat(n + "_hdark", "#120e0a"),
            "glass": Mat(n + "_glass", "#3e4c44", rough=0.3),
            "post": Mat(n + "_post", tex=kit.tex_planks(n + "_post", "#2a2018", boards=2, seed=seed + 4,
                                                        vertical=True), uv=1.0),
            "hay": Mat(n + "_hay", "#8a7a2a", both=True),
            "sign": Mat(n + "_sign", tex=kit.tex_planks(n + "_sign", "#4a3424", boards=3, seed=seed + 5), uv=0.8),
            "stone": Mat(n + "_hstone", tex=kit.tex_stone(n + "_hstone", "#5a5448", rows=7, seed=seed + 6), uv=1.5)}


def local(p, u, v, z=0.0):
    c, s = math.cos(math.radians(p["yaw"])), math.sin(math.radians(p["yaw"]))
    return (p["cx"] + u * c - v * s, p["cy"] + u * s + v * c, z)


def clerestory(B, m, p, z0, z1, u0, u1, n=8):
    """A row of small dark-glazed windows high in the front wall."""
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        y = -p["W"] / 2 - 0.03
        step = (u1 - u0) / n
        for i in range(n):
            u = u0 + (i + 0.5) * step
            B.box(m["glass"], u, y, z0 + 0.1, step * 0.62, 0.05, z1 - z0 - 0.2)
            B.box(m["timber"], u + step / 2, y - 0.02, z0, 0.14, 0.08, z1 - z0)
        B.box(m["timber"], (u0 + u1) / 2, y - 0.02, z0, u1 - u0, 0.1, 0.12)


def veranda(B, m, p, v, posts=True, roof=True, gap=None):
    """The lean-to along the front: its roof from the wall down to the
    posts, the west end hipped back to the wall; gap (u0, u1) leaves out
    the posts and front beam where a section has fallen."""
    u0, u1, D = v["u0"], v["u1"], v["depth"]
    zw, ze = v["z_wall"], v["z_eave"]
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        y0 = -p["W"] / 2
        y1 = y0 - D
        if roof:
            vr = m.get("vroof", m["roof"])
            B.slab(vr, [(u0 + 1.6, y1, ze), (u1, y1, ze), (u1, y0 + 0.2, zw), (u0 + 0.4, y0 + 0.2, zw)], 0.2)
            B.slab(vr, [(u0 + 1.6, y1, ze), (u0 + 0.4, y0 + 0.2, zw), (u0 - 0.2, y0 + 0.2, ze - 0.1)], 0.18)
        if posts:
            k = 6
            for i in range(k + 1):
                u = u0 + 1.8 + (u1 - 0.2 - u0 - 1.8) * i / k
                if gap and gap[0] < u < gap[1]:
                    continue
                B.box(m["post"], u, y1 + 0.3, 0, 0.28, 0.28, ze - 0.05)
            spans = [(u0 + 1.6, gap[0]), (gap[1], u1 - 0.1)] if gap else [(u0 + 1.6, u1 - 0.1)]
            for a, b in spans:
                if b - a > 0.3:
                    B.beam(m["timber"], (a, y1 + 0.3, ze - 0.2), (b, y1 + 0.3, ze - 0.2), 0.22)


def hay_tufts(B, m, r, spots, seed=0):
    g = random.Random(seed)
    for c, rw, k in spots:
        x, y = px(r, c, rw, 0.0)
        for _ in range(k):
            a = g.uniform(0, math.pi)
            L = g.uniform(0.3, 0.6)
            cx, cy = x + g.uniform(-0.5, 0.5), y + g.uniform(-0.4, 0.4)
            d = (math.cos(a) * L / 2, math.sin(a) * L / 2)
            kit.card(B, m["hay"], (cx - d[0], cy - d[1], 0.03), (cx + d[0], cy + d[1], g.uniform(0.05, 0.3)), 0.08,
                     kit.Vector((0, 0, 1)))


def long_hall(B, r, m, mw=None, gap=None):
    tiled_body(B, m, HALL, frame=dict(bays=2.0, braces=False))
    tiled_body(B, mw or m, WING5, frame=dict(bays=2.0, braces=False))
    clerestory(B, m, HALL, VER["z_wall"] + 0.1, HALL["H"] - 0.15, -4.8, 5.8)
    veranda(B, m, HALL, VER, gap=gap)
    # a low lean-to on the east end, cols 232-270
    with B.at(*local(HALL, HALL["L"] / 2 + 1.0, -0.6)[:2], yaw=HALL["yaw"]):
        B.box(m["wall"], 0, 0, 0, 1.6, 4.0, 2.4)
        B.slab(m.get("vroof", m["roof"]), [(-0.9, -2.3, 3.3), (1.1, -2.3, 2.4), (1.1, 2.3, 2.4), (-0.9, 2.3, 3.3)],
               0.18)
    # the sign on its beam out of the wing's west wall, cols 0-25 rows 100-125
    a = local(WING5, -0.4, WING5["W"] / 2, 3.1)
    b = local(WING5, -0.4, WING5["W"] / 2 + 2.0, 3.1)
    B.beam(m["timber"], a, b, 0.2)
    with B.at(b[0], b[1], 2.2, yaw=WING5["yaw"] + 90):
        B.box(m["sign"], -0.55, 0, 0, 0.9, 0.08, 0.7)
        for e in (-0.8, -0.3):
            B.beam(m["timber"], (e, 0, 0.7), (e, 0, 0.9), 0.03)


@model("Arabuild05")
def arabuild05(B, r):
    m = hall_mats5(r["name"])
    long_hall(B, r, m)
    hay_tufts(B, m, r, [(255, 205, 14), (120, 250, 12), (100, 240, 6)], seed=3)


# ---------------------------------------------------------------- the long hall damaged and ransacked

def hall_geoms():
    """The hall's hipped roof, the wing's gable and the veranda's slope
    (the front half of a gable whose ridge runs along the hall's wall)."""
    g = kit.RoofGeom.of(HALL)
    gw = kit.RoofGeom.of(WING5)
    v = VER
    cx, cy, _ = local(HALL, (v["u0"] + v["u1"]) / 2, -HALL["W"] / 2)
    gv = kit.RoofGeom(cx, cy, v["u1"] - v["u0"], 2 * v["depth"], v["z_eave"], v["z_wall"] - v["z_eave"], 0.0, 0.0,
                      yaw=HALL["yaw"], kind="gable", T=0.2)
    return g, gw, gv


def on_veranda(r, gv, poly, v_max=-0.05):
    """Plan points of the veranda roof the sprite draws at poly, kept on
    the veranda's own slope."""
    out = []
    for c, rw in poly:
        h = gv.hit(r, c, rw)
        if h is None:
            continue
        u, v = gv.local(h[0], h[1])
        x, y, _ = gv.world(u, min(v, v_max), 0)
        out.append((x, y))
    return out


def rim_halos(r, g, poly, core=0.8, fade=0.5, step=4.0, closed=True, seed=0):
    """Soot haloes every step px along an outline traced on the sprite, on
    roof g: near black for core cells out from the rim, fading to the
    tiles over fade more."""
    rng = random.Random(seed)
    pts = list(poly) + [poly[0]] if closed else list(poly)
    out = []
    for (c0, r0), (c1, r1) in zip(pts, pts[1:]):
        k = max(1, int(math.hypot(c1 - c0, r1 - r0) / step))
        for j in range(k):
            h = g.hit(r, c0 + (c1 - c0) * j / k, r0 + (r1 - r0) * j / k)
            if h is None:
                continue
            w, f = core * rng.uniform(0.9, 1.1), fade * rng.uniform(0.8, 1.2)
            out.append((h[0], h[1], w + f, ((w + f) / f) ** 0.8))
    return out


def burnt_roof(n, key, g, halos, seed, **kw):
    """Clean tiles with near-black soot only where haloes put it."""
    d = dict(course=0.26, tile_w=0.7, var=0.12, lip=0.2, moss="#2a221c", moss_amt=0.1, dark=0.9, soot=0.0,
             soot_col="#1a1612")
    d.update(kw)
    return matte(kit.roof_mat(n + key, g, "#7a665a", halos=halos, seed=seed, **d))


def matte(mat, spec=0.1):
    """Soot and charcoal hardly shine: a low specular, so a sooted tile
    goes near black in the sun instead of greying."""
    mat.m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = spec
    return mat


def resample(poly, k):
    """k points evenly along an open polyline."""
    L = [0.0]
    for a, b in zip(poly, poly[1:]):
        L.append(L[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
    out = []
    for i in range(k):
        t = L[-1] * i / (k - 1)
        j = max(0, min(len(poly) - 2, next((q for q in range(len(L) - 1) if L[q + 1] >= t), len(L) - 2)))
        f = (t - L[j]) / max(1e-9, L[j + 1] - L[j])
        out.append((poly[j][0] + (poly[j + 1][0] - poly[j][0]) * f, poly[j][1] + (poly[j + 1][1] - poly[j][1]) * f))
    return out


def screen_strip(B, mat, r, outer, inner, zfn, t, across=3):
    """A strip between two polylines traced on the sprite (as many points
    each), its top at zfn(i, f, col, row) and t thick, so the classic
    camera sees it exactly over the traced band."""
    n = len(outer)
    V, top = [], {}
    for i in range(n):
        for j in range(across + 1):
            f = j / across
            c = outer[i][0] + (inner[i][0] - outer[i][0]) * f
            rw = outer[i][1] + (inner[i][1] - outer[i][1]) * f
            z = zfn(i, f, c, rw)
            x, y = px(r, c, rw, z)
            top[i, j] = len(V)
            V.extend([(x, y, z), (x, y, z - t)])
    F = []
    for i in range(n - 1):
        for j in range(across):
            q = [top[i, j], top[i + 1, j], top[i + 1, j + 1], top[i, j + 1]]
            F.append(tuple(q))
            F.append(tuple(k + 1 for k in reversed(q)))
    rim = [(i, 0) for i in range(n)] + [(n - 1, j) for j in range(1, across + 1)] + \
        [(i, across) for i in range(n - 2, -1, -1)] + [(0, j) for j in range(across - 1, 0, -1)]
    for a, b in zip(rim, rim[1:] + rim[:1]):
        F.append((top[a], top[a] + 1, top[b] + 1, top[b]))
    B.solid(mat, V, F)


def seat_islands(ob, ground, P, above=0.2, sink=0.03):
    """Lower each loose piece of ob inside plan polygon P whose lowest
    corner stands more than above over ground(x, y) onto it."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    seen, moved = set(), 0
    for v0 in bm.verts:
        if v0.index in seen:
            continue
        isl, todo = [], [v0]
        seen.add(v0.index)
        while todo:
            v = todo.pop()
            isl.append(v)
            for e in v.link_edges:
                w = e.other_vert(v)
                if w.index not in seen:
                    seen.add(w.index)
                    todo.append(w)
        cx = sum(v.co.x for v in isl) / len(isl)
        cy = sum(v.co.y for v in isl) / len(isl)
        if not kit.inside_poly(P, cx, cy):
            continue
        gap = min(v.co.z - ground(v.co.x, v.co.y) for v in isl)
        if gap > above:
            for v in isl:
                v.co.z -= gap + sink
            moved += 1
    bm.to_mesh(ob.data)
    bm.free()
    return moved


def grown(poly, k=1.0):
    """An outline traced on the sprite pushed k px out from its middle."""
    c = sum(p[0] for p in poly) / len(poly)
    rw = sum(p[1] for p in poly) / len(poly)
    out = []
    for x, y in poly:
        d = max(1e-6, math.hypot(x - c, y - rw))
        out.append((x + (x - c) * k / d, y + (y - rw) * k / d))
    return out


def proud(ob, p, d=0.01, eps=1e-3):
    """Push a body's timbers that end flush with its end or side walls
    (body p) d further out, so no timber face lies in a wall's plane."""
    c, s = math.cos(math.radians(p["yaw"])), math.sin(math.radians(p["yaw"]))
    for v in ob.data.vertices:
        dx, dy = v.co.x - p["cx"], v.co.y - p["cy"]
        u, w = dx * c + dy * s, -dx * s + dy * c
        if abs(abs(u) - p["L"] / 2) < eps:
            u += math.copysign(d, u)
        if abs(abs(w) - p["W"] / 2) < eps:
            w += math.copysign(d, w)
        v.co.x, v.co.y = p["cx"] + u * c - w * s, p["cy"] + u * s + w * c


@model("Arabuild05a")
def arabuild05a(B, r):
    import m_halls
    n = r["name"]
    pic = kit.Picture(r)
    g, gw, gv = hall_geoms()
    hx_, hy_ = r["sprite"]["hotspot"]
    # the holes the fire broke: the middle of the hall roof and its back
    # hip, the tip of the wing, the veranda's right end
    main = [(128, 72), (160, 64), (196, 70), (206, 100), (196, 124), (160, 130), (130, 122), (122, 96)]
    back = [(172, 6), (214, 4), (232, 20), (228, 44), (196, 48), (176, 34)]
    tip = [(14, 30), (56, 26), (66, 40), (42, 50), (16, 46)]
    ver_r = [(228, 128), (268, 126), (282, 150), (276, 176), (244, 180), (226, 160)]
    # the wing's front end broken open, cols 30-100 rows 110-175
    wfront = [(30, 116), (52, 108), (80, 110), (100, 126), (102, 150), (96, 172), (70, 178), (42, 172), (28, 150)]
    # the hall's west hip inside the junction fell with the wing
    jn0 = [(58, 126), (100, 124), (104, 150), (96, 172), (70, 178), (58, 160)]
    # the veranda's front section slumped as one slab, cols 133-200 rows
    # 167-235, its tile courses level to the camera; the cut round it is
    # wider at the sides for the dark creases and takes the torn corner to
    # its right, where the sprite shows ground
    slab = [(134, 181), (140, 175), (150, 170), (165, 167), (180, 167), (193, 170), (199, 176), (200, 190),
            (197, 202), (192, 214), (189, 224), (188, 233), (182, 236), (176, 232), (169, 236), (161, 233),
            (154, 237), (147, 233), (140, 236), (134, 233), (133, 215), (133, 196)]
    cut = [(128, 256), (128, 196), (131, 180), (139, 170), (150, 164), (165, 161), (181, 161), (195, 164),
           (203, 172), (207, 186), (206, 197), (214, 199), (224, 202), (229, 214), (226, 224), (200, 246),
           (196, 256)]
    # the horseshoe of soot round it, from the front eave over the arch
    shoe = [(127, 236), (127, 196), (130, 180), (138, 169), (150, 162), (165, 159), (181, 159), (196, 162),
            (205, 171), (209, 186), (209, 198)]
    m = hall_mats5(n)
    m["roof"] = burnt_roof(n, "_roof", g, rim_halos(r, g, main, seed=1) + rim_halos(r, g, back, seed=2), 1)
    m["vroof"] = burnt_roof(n, "_vroof", gv, rim_halos(r, gv, wfront, seed=4) + rim_halos(r, gv, ver_r, seed=5) +
                            rim_halos(r, gv, shoe, core=0.8, fade=0.5, closed=False, seed=6), 2)
    m["char"] = Mat(n + "_char", tex=kit.tex_grit(n + "_char", "#1e1814", light="#6a6056", dark="#080605", uv=2.0,
                                                   chips=0.06, darks=0.3, seed=4), uv=2.0)
    m["wall"] = Mat(n + "_hwall", tex=kit.tex_planks(n + "_hwall", "#2a2018", boards=8, seed=5), uv=2.0)
    # the wing has its own walls, timbers and cap, so its front can be cut
    mw = dict(m, roof=burnt_roof(n, "_wroof", gw, rim_halos(r, gw, wfront, core=0.6, fade=0.4, seed=8), 3),
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
    J = {k: kit.jag(q, 2.5, 4.0, i) for i, (k, q) in enumerate((("main", main), ("back", back), ("tip", tip),
                                                                  ("wfront", wfront), ("ver_r", ver_r)))}
    jn = kit.jag(jn0, 2.5, 4.0, 9)
    # caps and timbers cut a pixel wider than the roof and walls under them,
    # so no cut face lies in another's plane
    kit.cut_view([o for o in obs if o.name.endswith("_roof")], [J["main"], J["back"], jn], r)
    kit.cut_view([o for o in obs if o.name.endswith("_rcap")], [grown(q) for q in (J["main"], J["back"], jn)], r)
    kit.cut_view([o for o in obs if o.name.endswith("_wroof")], [J["tip"], J["wfront"]], r)
    kit.cut_view([o for o in obs if o.name.endswith("_wwall")], [wfront], r)
    kit.cut_view([o for o in obs if o.name.endswith(("_wtimber", "_wcap"))], [grown(wfront)], r)
    kit.cut_view([o for o in obs if o.name.endswith("_vroof")], [J["wfront"], J["ver_r"], kit.jag(cut, 1.5, 4.0, 5)],
                 r)
    dm = kit.debris_mats(n, pic, seed=6, k=7)
    rafter = Mat(n + "_wrafter", tex=kit.tex_planks(n + "_wrafter", "#3a2e24", boards=2, seed=7), uv=1.0)
    m_halls.roof_frame(B, rafter, r, g, [main], spacing=1.3, lath=20.0, w=0.18)
    # charred floors sagging under the holes with debris lying in them
    kit.hole_insides(B, r, pic, m["char"], dm, g, [J["main"], J["back"]], sag=1.0, n=18, seed=10,
                     size=(0.4, 0.8), cell=0.5)
    kit.hole_insides(B, r, pic, m["char"], dm, gw, [J["tip"]], sag=0.6, n=6, seed=20, cell=0.5)
    kit.hole_insides(B, r, pic, m["char"], dm, g, [jn], sag=1.3, n=14, seed=24, size=(0.4, 0.8), cell=0.5)
    flat = lambda x, y: 0.0  # noqa: E731
    # under the right veranda hole the fallen boards and tiles lie on the
    # ground, heaped below its roof line
    h = kit.heap(B, m["char"], r, kit.jag(ver_r, 2.0, 4.0, 31), 1.1, z_at=0.6, cell=0.5, noise=0.06, seed=32,
                 edge=1.0, pic=pic)
    kit.cover(B, dm, r, kit.plan_of(r, ver_r, 0.6), kit.ground_of(h, flat), 16, {"slab": 3, "beam": 3, "board": 2},
              pic=pic, size=(0.4, 0.8), length=(1.0, 2.2), seed=41, tilt=30, grow=1, jumble=0.2, stick=0.25)
    # the wing's front end: a heap of its roof and gable filling it, broken
    # rafters hanging from what is left, spilling onto the veranda and ground
    hw = kit.heap(B, m["char"], r, J["wfront"], 2.3, z_at=0.2, cell=0.55, noise=0.12, seed=33, edge=1.4, pic=pic)
    west = kit.ground_of(hw, flat)
    # its loose pieces and the spill below it are seated on the heap after,
    # none left hanging over it
    pre = B.objects()
    kit.cover(B, dm, r, kit.plan_of(r, wfront, 0.2), west, 30,
              {"slab": 3, "beam": 3, "board": 2, "stone": 1}, pic=pic, size=(0.4, 0.9), length=(1.0, 2.4), seed=44,
              tilt=35, grow=1, jumble=0.3, stick=0.3)
    kit.cover(B, dm, r, kit.plan_of(r, [(36, 150), (110, 160), (116, 206), (60, 200)], 0.0), flat, 8,
              {"slab": 3, "beam": 2, "stone": 2}, pic=pic, size=(0.3, 0.6), length=(0.6, 1.4), seed=50, tilt=20,
              grow=1, tries=300)
    wp = B.objects()
    Pw = kit.plan_of(r, [(10, 96), (116, 96), (124, 214), (10, 214)], 0.5)
    for ob in wp:
        k = seat_islands(ob, west, Pw)
        if k and os.environ.get("AB_TRIS"):
            print("AB_SEAT", ob.name, k, flush=True)
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
        """The slab plane (lifted dz) where the sprite draws (c, rw)."""
        y = (hy_ - rw - 8 * (ZF + dz) + 8 * sl * yf) / (16 + 8 * sl)
        return ((c - hx_) / 16.0, y, ZF + (y - yf) * sl + dz)
    Ps = [s_at(c, rw) for c, rw in slab]
    xs = [p[0] for p in Ps]
    D = yb - yf + 0.4
    gs = kit.RoofGeom((min(xs) + max(xs)) / 2, yb + 0.1, max(xs) - min(xs) + 0.6, 2 * D, ZF - 0.3 * sl, sl * D, 0.0,
                      yaw=0.0, kind="gable", T=0.16)
    edge = [s_at(c, rw) for c, rw in ((135, 232), (134, 214), (134, 196), (136, 182))]
    ms = burnt_roof(n, "_sroof", gs, [(x, y, 0.45, 1.4) for x, y, _ in edge], 11, course=0.37, tile_w=1.2, dark=0.72)
    area = sum(Ps[i][0] * Ps[(i + 1) % len(Ps)][1] - Ps[(i + 1) % len(Ps)][0] * Ps[i][1] for i in range(len(Ps)))
    B.slab(ms, Ps if area > 0 else list(reversed(Ps)), 0.16)
    # a dark crease down its left side, between it and the standing roof
    char2 = matte(Mat(n + "_char2", tex=kit.tex_grit(n + "_char2", "#2c2722", light="#6a6258", dark="#100e0c", uv=1.2,
                                                chips=0.05, darks=0.3, seed=48), uv=1.2))
    lo = resample([(128, 236), (128, 214), (128, 196), (131, 186)], 4)
    li = resample([(134, 236), (133, 214), (133, 196), (136, 186)], 4)
    screen_strip(B, char2, r, lo, li, lambda i, f, c, rw: s_at(c, rw)[2] - 0.1, 0.08, across=1)
    # charcoal rubble heaped in a raised rim along the break, from the
    # standing roof's torn edge down onto the slab's top
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
    # the broken posts it came down on, and what fell through lies below
    for u, hgt, tilt in ((-3.1, 0.45, 14), (-1.2, None, 5), (0.7, 0.5, -16)):
        x, y, _ = local(HALL, u, -HALL["W"] / 2 - VER["depth"] + 0.3)
        top = hgt if hgt is not None else ZF + (y - yf) * sl - 0.17
        B.box(m["post"], x, y, 0, 0.28, 0.28, top, yaw=HALL["yaw"], pitch=tilt)
    kit.cover(B, dm, r, kit.plan_of(r, [(136, 236), (192, 234), (196, 250), (134, 252)], 0.0), flat, 6,
              {"slab": 3, "beam": 2, "stone": 2}, pic=None, size=(0.3, 0.6), length=(0.6, 1.4), seed=53, tilt=20)
    # small spills along the broken edges, and a torn piece of the veranda
    # roof on the ground, cols 205-235
    for i, q in enumerate(([(236, 176), (286, 170), (286, 196), (240, 204)],
                           [(100, 196), (132, 196), (132, 214), (100, 214)])):
        kit.cover(B, dm, r, kit.plan_of(r, q, 0.0), flat, 8, {"slab": 3, "beam": 2, "stone": 2}, pic=pic,
                  size=(0.3, 0.6), length=(0.6, 1.4), seed=51 + i, tilt=20, grow=1, tries=300)
    x, y = px(r, 220, 222, 0.3)
    with B.at(x, y, 0.25, yaw=30, pitch=12, roll=-8):
        B.slab(ms, [(-1.0, -0.6, 0), (1.0, -0.7, 0), (0.8, 0.6, 0), (-0.9, 0.5, 0)], 0.15)
    hay_tufts(B, m, r, [(165, 242, 6)], seed=5)
    return obs + pre + wp


@model("Arabuild05b")
def arabuild05b(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    m = hall_mats5(n)
    ash = Mat(n + "_ash", tex=kit.tex_grit(n + "_ash", "#221e1a", light="#a09c94", dark="#0a0806", uv=1.5,
                                            chips=0.12, darks=0.35, seed=3), uv=1.5)
    # a low field of fallen tiles, stones and timbers over grey ash, the
    # ground showing through its gaps and past its ragged edges
    field = [(4, 48), (40, 40), (60, 58), (96, 44), (128, 30), (150, 6), (200, 4), (218, 30), (238, 54), (250, 92),
             (252, 132), (246, 170), (226, 196), (190, 214), (150, 226), (110, 232), (70, 222), (50, 190), (30, 170),
             (10, 130)]
    mounds = [([(96, 50), (200, 20), (236, 80), (220, 170), (130, 190), (70, 150), (80, 80)], 1.8),
              ([(140, 8), (200, 6), (216, 40), (170, 56), (140, 40)], 1.2)]
    hs = [kit.bed(B, ash, r, pic, field, 0.35, z_at=0.2, cell=1.0, edge=1.2, seed=5)]
    for i, (q, t) in enumerate(mounds):
        hs.append(kit.bed(B, ash, r, pic, q, t, z_at=0.5, cell=0.8, edge=2.4, seed=6 + i, gaps=False))
    ground = kit.ground_of(*hs)
    cols = kit.palette(pic, k=7, seed=1, lit=0.3)
    tile = kit.swatches(n + "_tl", [c for c in cols if c[0] > c[2] + 6] or cols, "mottle", seed=10, sat=1.4, gain=1.3)
    stone = kit.swatches(n + "_sn", cols, "mottle", seed=20, sat=1.0, gain=1.3)
    wood = kit.swatches(n + "_wd", ["#3a2a20", "#4a3424", "#2a201a"], "planks", seed=30, sat=1.0)
    dm = {"slab": tile, "tile": tile, "stone": stone, "chunk": stone, "beam": wood, "board": wood}
    P = kit.plan_of(r, field, 0.3)
    kit.cover(B, dm, r, P, ground, 330, {"slab": 5, "stone": 4, "beam": 3}, pic=pic, size=(0.6, 1.3),
              length=(1.2, 2.6), width=(0.14, 0.22), stone=(0.35, 0.7), seed=11, tilt=30, grow=0, jumble=0.4,
              stick=0.06, q=0.7, tries=200)
    # loose pieces past the edges
    flat = lambda x, y: 0.0  # noqa: E731
    for i, q in enumerate(([(0, 120), (40, 130), (60, 200), (20, 196)],
                           [(230, 60), (276, 60), (276, 190), (240, 190)],
                           [(80, 216), (200, 216), (200, 250), (80, 250)])):
        kit.cover(B, dm, r, kit.plan_of(r, q, 0.0), flat, 16, {"stone": 2, "beam": 2, "slab": 2}, pic=pic,
                  size=(0.3, 0.5), length=(0.6, 1.4), stone=(0.2, 0.35), seed=40 + i, tilt=15, grow=1, tries=300)
    # the stub of the cross wing's west gable wall at the left end
    stub = Mat(n + "_stub", tex=kit.tex_planks(n + "_stub", "#2e2620", boards=4, seed=7, vertical=True), uv=1.2)
    top = Mat(n + "_stubtop", "#6a5e52")
    kit.ruin_walls(B, stub, top, WING5, {"N": [(0, 1.2), (0.3, 1.8), (0.6, 1.3), (1, 0.9)],
                                         "E": [(0, 0.6), (0.5, 0.4), (1, 0.8)]}, t=0.24, seed=8, jitter=0.2)
    # a few veranda posts and frame stubs still stand
    for c, rw, h in ((150, 250, 1.3), (196, 236, 1.0), (244, 214, 1.4), (36, 60, 1.6)):
        x, y = px(r, c, rw, 0)
        B.box(m["post"], x, y, 0, 0.28, 0.28, h, pitch=6)
