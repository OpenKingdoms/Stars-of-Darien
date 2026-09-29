"""The Aramon manor: a tall two-storey block whose ridge runs toward the
camera and ends in a hip over the front, a taller hipped tower block
behind it, and a long lean-to down its west side; and its ruins."""
import random

import kit
from kit import Mat, px
from m_halls import tiled_body
from models import P, model

# walls in cells; the main block's long axis runs back along +y. Both
# blocks are broader and lower than the pixel fit (the tower most), so the
# tower stands stout rather than thin from low down
MAIN = P(cx=2.3, cy=-0.55, yaw=90.0, L=10.6, W=6.9, H=4.5, R=2.6, over=0.4, over_end=0.4, hip=2.3, T=0.3)
TOWER = P(cx=3.75, cy=5.0, yaw=90.0, L=5.0, W=5.0, H=5.9, R=2.3, over=0.35, over_end=0.35, hip=2.5, T=0.3)
MAIN_ENDS = ("hip", "gable")  # hipped over the front, gabled at the back
LEAN = dict(x0=-6.1, x1=-1.15, y0=-6.1, y1=4.7, z_lo=2.4, z_hi=4.25)


def manor_mats(n, seed=161):
    return {"roof": Mat(n + "_roof", tex=kit.tex_tiles(n + "_roof", "#76645a", rows=12, cols=4, var=0.07, lip=0.2,
                                                       moss="#4a4638", moss_amt=0.1, seed=seed), uv=3.0),
            "wall": Mat(n + "_hwall", tex=kit.tex_mottle(n + "_hwall", "#86887a", var=0.1, seed=seed + 1), uv=1.5,
                        ref="#5b5648"),
            "boards": Mat(n + "_boards", tex=kit.tex_planks(n + "_boards", "#3a2c20", boards=6, seed=seed + 5,
                                                            vertical=True), uv=1.5),
            "timber": Mat(n + "_timber", tex=kit.tex_planks(n + "_timber", "#241a14", boards=2, seed=seed + 2),
                          uv=1.0),
            "cap": Mat(n + "_rcap", tex=kit.tex_planks(n + "_rcap", "#4a3c34", boards=2, seed=seed + 3), uv=1.0),
            "dark": Mat(n + "_hdark", "#120e0a"),
            "pane": Mat(n + "_pane", "#5a5448"),
            "hay": Mat(n + "_hay", "#7a6a26", both=True),
            "fence": Mat(n + "_fence", tex=kit.tex_planks(n + "_fence", "#3a2c1e", boards=2, seed=seed + 4), uv=1.0)}


def windows(B, m, p, side, n, z0, h, w=0.8):
    """Light panelled windows in a row on one face of a body."""
    L, W = p["L"], p["W"]
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        if side == "W":
            for i in range(n):
                s = -W / 2 + W * (i + 0.5) / n
                B.box(m["pane"], -L / 2 - 0.03, s, z0, 0.06, w, h)
                B.box(m["timber"], -L / 2 - 0.06, s, z0 - 0.08, 0.08, w + 0.2, 0.1)
                B.box(m["timber"], -L / 2 - 0.06, s, z0 + h / 2 - 0.04, 0.08, w, 0.08)


def lean_to(B, m, q, hay=True, seed=0, roof=None):
    """The long lean-to down the west side: boarded at its back and its
    west side, open at the front between posts with a rail across, hay
    stacked inside and spilling out."""
    x0, x1, y0, y1, zl, zh = q["x0"], q["x1"], q["y0"], q["y1"], q["z_lo"], q["z_hi"]
    B.box(m["boards"], x0 + 0.45, (y0 + y1) / 2 + 0.2, 0, 0.12, y1 - y0 - 0.6, zl)
    B.box(m["boards"], (x0 + x1) / 2, y1 - 0.3, 0, x1 - x0 - 0.3, 0.12, zl)
    B.slab(roof or m["roof"], [(x0, y0, zl), (x1, y0, zh), (x1, y1, zh), (x0, y1, zl)], 0.2)
    # the timber gable over the open front
    B.slab(m["boards"], [(x0 + 0.3, y0 + 0.35, zl), (x1, y0 + 0.35, zl), (x1, y0 + 0.35, zh - 0.1)], 0.1)
    B.beam(m["timber"], (x0 + 0.3, y0 + 0.28, zl - 0.1), (x1, y0 + 0.28, zl - 0.1), 0.18)
    for i in range(4):
        x = x0 + 0.4 + (x1 - x0 - 0.6) * i / 3
        B.box(m["timber"], x, y0 + 0.28, 0, 0.24, 0.22, zl)
    B.beam(m["timber"], (x0 + 0.3, y0 + 0.28, 0.95), (x1, y0 + 0.28, 0.95), 0.16)
    if hay:
        import m_props
        body, cards = m_props.straw_mats(m["roof"].name + "_hay", seed=seed + 3)
        # a long low stack of hay under the roof, loose hay in front of it
        for k, (yc, ry) in enumerate(((y0 + 2.0, 1.6), (y0 + 5.0, 1.8), (y0 + 8.2, 1.6))):
            surf = kit.dome(B, body, (x0 + x1) / 2 + 0.2, yc, (x1 - x0) / 2 - 0.5, ry, 0.8,
                            prof=[(1.0, 0.0), (1.0, 0.3), (0.85, 0.7), (0.4, 0.95), (0.0, 1.0)], seg=14, jitter=0.08,
                            seed=seed + k)
            kit.strands(B, cards, surf, 30, length=(0.15, 0.3), seed=seed + 10 + k)
        # loose hay in front, low tufts of it and wisps
        tuft = [(x0 + 0.3, y0 - 1.0), (x1 - 0.3, y0 - 1.0), (x1 - 0.3, y0 + 0.5), (x0 + 0.3, y0 + 0.5)]
        rng = kit.random.Random(seed + 7)
        for _ in range(9):
            x, y = rng.uniform(x0 + 0.5, x1 - 0.5), rng.uniform(y0 - 0.8, y0 + 0.3)
            kit.dome(B, body, x, y, rng.uniform(0.3, 0.6), rng.uniform(0.25, 0.45), rng.uniform(0.12, 0.25), seg=8,
                     jitter=0.1, seed=seed + 30 + _)
        for _ in range(30):
            x, y = rng.uniform(x0 + 0.2, x1 - 0.2), rng.uniform(y0 - 1.0, y0 + 0.4)
            a = rng.uniform(0, 3.14)
            d = (0.2 * kit.math.cos(a), 0.2 * kit.math.sin(a))
            kit.card(B, rng.choice(cards), (x - d[0], y - d[1], 0.03), (x + d[0], y + d[1], rng.uniform(0.03, 0.1)),
                     0.07, kit.Vector((0, 0, 1)))


def manor(B, r, m, tower=True, hay=True, lean_roof=None):
    """Plastered walls in a dark timber frame, an X brace in each bay."""
    tiled_body(B, m, MAIN, ends=MAIN_ENDS, frame=dict(bays=3.2, xbrace=True, rail=0.55))
    kit.hip_caps(B, m["cap"], kit.RoofGeom.of(MAIN, ends=MAIN_ENDS), w=0.26, ridge=False)
    if tower:
        tiled_body(B, m, TOWER, frame=dict(bays=2.1, xbrace=True, rail=0.5))
        kit.hip_caps(B, m["cap"], kit.RoofGeom.of(TOWER), w=0.22, ridge=False)
    lean_to(B, m, LEAN, hay=hay, roof=lean_roof)
    windows(B, m, MAIN, "W", 4, 3.4, 1.0)


@model("Arabuild06")
def arabuild06(B, r):
    m = manor_mats(r["name"])
    manor(B, r, m)


# ---------------------------------------------------------------- the manor damaged and ransacked

def debris_mats(n, pic, seed=0, k=6):
    """Timbers, tile slabs, boards and stones in the colours the sprite
    draws its rubble in."""
    cols = kit.palette(pic, k=k, seed=seed)
    slab = kit.swatches(n + "_ds", cols, "mottle", seed=seed)
    wood = kit.swatches(n + "_dw", [c for c in cols if c[0] >= c[2]] or cols, "planks", seed=seed + 20)
    return {"slab": slab, "tile": slab, "stone": slab, "chunk": slab, "beam": wood, "board": wood}


def manor_wreck(n, seed=171):
    import m_halls
    w = m_halls.wreck_mats(n, base="#34302c", light="#9a9088", beam="#34281e", plank="#4a3a2e", slab="#6e5e54",
                           seed=seed)
    w["tile"] = Mat(n + "_wtile", tex=kit.tex_tiles(n + "_wtile", "#6e5e54", rows=4, cols=2, var=0.08, seed=seed + 5),
                    uv=1.0)
    w["rafter"] = Mat(n + "_wrafter", tex=kit.tex_planks(n + "_wrafter", "#5a4a3e", boards=2, seed=seed + 6), uv=1.0)
    w["soot"] = Mat(n + "_soot", "#1a1614")
    return w


def tower_rubble(B, r, pic, m, dm, top=2.0, seed=0):
    """The tower brought down to a low heap round the stumps of its frame,
    staying under the main roof's line."""
    import m_halls
    p = TOWER
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        m_halls.broken_frame(B, m["timber"], p["L"], p["W"], 2.0, seed=seed, bays=1.6, lo=0.3, t=0.24)
    poly = kit.circle_px(r, p["cx"], p["cy"], 3.0, 0.8, k=12)
    h = kit.heap(B, m["ash"], r, kit.jag(poly, 2.0, 4.0, seed), top, z_at=0.8, cell=0.45, noise=0.05, seed=seed + 1,
                 edge=1.6, pic=pic)
    kit.cover(B, dm, r, kit.plan_of(r, poly, 0.8), h, 40, {"slab": 4, "beam": 3, "stone": 3}, pic=pic,
              size=(0.6, 1.1), length=(1.0, 2.2), seed=seed + 2, tilt=25, grow=1, jumble=0.2, stone=(0.3, 0.55))


def sooty_manor_roof(n, key, g, r, holes, seed=0, halo=1.6):
    halos = []
    for q in holes:
        h = kit.hole_centre(r, g, q)
        if h is not None:
            halos.append((h[0], h[1], h[2] * halo + 0.9, 0.95))
    return kit.roof_mat(n + key, g, "#76645a", course=0.26, tile_w=0.75, var=0.1, lip=0.2, moss="#4a4638",
                        moss_amt=0.08, soot=0.9, swirl=2.2, width=0.5, halos=halos, dark=0.9, seed=seed)


def lean_geom():
    q = LEAN
    return kit.RoofGeom(q["x1"], (q["y0"] + q["y1"]) / 2, q["y1"] - q["y0"], 2 * (q["x1"] - q["x0"]), q["z_lo"],
                        q["z_hi"] - q["z_lo"], 0.0, 0.0, yaw=90.0, kind="gable", T=0.2)


@model("Arabuild06a")
def arabuild06a(B, r):
    import m_halls
    n = r["name"]
    pic = kit.Picture(r)
    m = manor_mats(n)
    m["ash"] = Mat(n + "_ash", tex=kit.tex_grit(n + "_ash", "#1e1814", light="#7a7066", dark="#080605", uv=2.0,
                                                 chips=0.06, darks=0.3, seed=3), uv=2.0)
    m["char"] = Mat(n + "_char", tex=kit.tex_grit(n + "_char", "#261e18", light="#8a8076", dark="#0a0806", uv=2.0,
                                                   chips=0.1, darks=0.25, seed=4), uv=2.0)
    dm = debris_mats(n, pic)
    g = kit.RoofGeom.of(MAIN, ends=MAIN_ENDS)
    gl = lean_geom()
    hole_main = [(98, 44), (138, 40), (148, 78), (140, 120), (104, 126), (96, 90)]
    hole_lean = [(64, 54), (92, 50), (94, 116), (72, 120), (62, 90)]
    hole_front = [(128, 170), (176, 162), (194, 192), (184, 208), (134, 208)]
    # where the tower came down on the back of the main roof
    hole_back = [(150, 14), (214, 14), (214, 70), (186, 80), (152, 74)]
    mm = dict(m, roof=sooty_manor_roof(n, "_mroof", g, r, [hole_main, hole_front, hole_back], seed=1))
    lean_roof = sooty_manor_roof(n, "_lroof", gl, r, [hole_lean], seed=2, halo=1.2)
    mm["wall"] = Mat(n + "_hwall", tex=kit.tex_mottle(n + "_hwall", "#4a463c", var=0.2, seed=5), uv=1.5)
    manor(B, r, mm, tower=False, lean_roof=lean_roof, hay=False)
    obs = B.objects()
    holes = [kit.jag(q, 2.5, 4.0, i) for i, q in enumerate((hole_main, hole_front, hole_lean, hole_back))]
    kit.cut_view([o for o in obs if o.name.endswith(("_mroof", "_rcap"))], [holes[0], holes[1], holes[3]], r)
    kit.cut_view([o for o in obs if o.name.endswith("_lroof")], [holes[2]], r)
    rafter = Mat(n + "_wrafter", tex=kit.tex_planks(n + "_wrafter", "#4a3a2e", boards=2, seed=6), uv=1.0)
    # rafters and battens left bare in the holes, charred debris lying in them
    m_halls.roof_frame(B, rafter, r, g, [hole_main], spacing=0.6, lath=0.7)
    m_halls.roof_frame(B, rafter, r, g, [hole_front], spacing=0.7, lath=20.0)
    for gg, q, sag, k in ((g, holes[0], 0.8, 19), (g, holes[1], 0.7, 17), (gl, holes[2], 0.6, 11),
                          (g, holes[3], 1.2, 21)):
        fill, Pp = kit.sag_fill(B, m["char"], r, gg, q, sag=sag, cell=0.42, noise=0.05, seed=k, edge=0.9)
        if len(Pp) >= 3:
            kit.cover(B, dm, r, Pp, fill, k, {"beam": 3, "board": 2, "slab": 3, "stone": 2}, pic=pic,
                      size=(0.3, 0.7), length=(0.8, 1.8), seed=k + 1, lift=0.04, tilt=20, grow=0,
                      stone=(0.2, 0.4), jumble=0.15)
    # the front wall's right half is hidden by what fell from the hip above
    front = [(136, 206), (196, 204), (204, 236), (190, 252), (150, 250), (132, 232)]
    h = kit.heap(B, m["ash"], r, kit.jag(front, 2.0, 4.0, 9), 1.6, z_at=0.8, cell=0.4, noise=0.05, seed=9,
                 edge=1.0, pic=pic)
    kit.cover(B, dm, r, kit.plan_of(r, front, 0.8), h, 28, {"slab": 3, "beam": 3, "board": 2, "stone": 2}, pic=pic,
              size=(0.5, 1.0), length=(0.8, 2.0), seed=10, tilt=25, grow=1, jumble=0.2)
    tower_rubble(B, r, pic, m, dm, top=1.8, seed=7)
    # small debris scattered round the edges, as the sprite has it
    flat = lambda x, y: 0.0  # noqa: E731
    for i, q in enumerate(([(0, 30), (18, 30), (18, 250), (0, 250)], [(200, 60), (223, 60), (223, 250), (200, 250)],
                           [(20, 238), (200, 238), (200, 263), (20, 263)])):
        kit.cover(B, dm, r, kit.plan_of(r, q, 0.0), flat, 11, {"stone": 3, "slab": 2, "beam": 1, "board": 1},
                  pic=pic, size=(0.25, 0.5), length=(0.5, 1.0), stone=(0.2, 0.35), seed=20 + i, tilt=20, grow=0,
                  tries=300)
    return obs


@model("Arabuild06b")
def arabuild06b(B, r):
    import m_halls
    n = r["name"]
    pic = kit.Picture(r)
    m = manor_mats(n)
    ash = Mat(n + "_ash", tex=kit.tex_grit(n + "_ash", "#221e1a", light="#a09c94", dark="#0a0806", uv=1.5,
                                            chips=0.13, darks=0.35, seed=3), uv=1.5)
    dm = kit.debris_mats(n, pic, seed=2, k=7, gain=1.3)
    # the block's stone walls stand low round it, their broken tops pale
    stub = Mat(n + "_stub", tex=kit.tex_stone(n + "_stub", "#46423a", rows=4, seed=3, mortar="#22201c"), uv=1.5)
    top = Mat(n + "_stubtop", tex=kit.tex_mottle(n + "_stubtop", "#5e5a52", var=0.25, seed=4), uv=1.0)
    kit.ruin_walls(B, stub, top, MAIN, {"N": [(0, 0.8), (0.3, 1.0), (0.6, 0.6), (1, 0.9)],
                                        "S": [(0, 0.6), (0.4, 0.8), (0.7, 0.5), (1, 0.7)],
                                        "E": [(0, 1.0), (0.5, 0.7), (1, 1.1)]}, t=0.34, seed=5, jitter=0.18,
                   notch=0.35)
    kit.ruin_walls(B, stub, top, TOWER, {"N": [(0, 1.7), (0.5, 1.2), (1, 1.5)], "E": [(0, 1.5), (1, 1.1)],
                                         "S": [(0, 1.1), (0.6, 0.6), (1, 0.9)]}, t=0.38, seed=6, jitter=0.12,
                   notch=0.15)
    # the front frames of both storeys stand at the front, snapped off
    with B.at(MAIN["cx"], MAIN["cy"], yaw=MAIN["yaw"]):
        m_halls.broken_frame(B, m["timber"], MAIN["L"], MAIN["W"], 2.0, keep="W", seed=3, bays=1.7, lo=0.15,
                             xbrace="W", t=0.24, sides="W")
    for i in range(4):
        x = LEAN["x0"] + 0.4 + (LEAN["x1"] - LEAN["x0"] - 0.6) * i / 3
        B.box(m["timber"], x, LEAN["y0"] + 0.28, 0, 0.24, 0.22, LEAN["z_lo"] * 0.5)
    B.beam(m["timber"], (LEAN["x0"] + 0.3, LEAN["y0"] + 0.28, 0.9), (LEAN["x1"], LEAN["y0"] + 0.28, 0.9), 0.18)
    # a low field of ash, tiles, timbers and stones, the ground showing
    # through its gaps, sparse over the lean-to, a heap where the tower was
    main = [(98, 26), (134, 20), (188, 40), (200, 90), (198, 236), (100, 240), (96, 130)]
    lean = [(16, 44), (96, 34), (98, 150), (20, 150), (8, 100)]
    tower = kit.circle_px(r, TOWER["cx"], TOWER["cy"], 2.8, 0.8, k=12)
    hs = [kit.bed(B, ash, r, pic, main, 0.7, z_at=0.35, cell=0.6, edge=1.4, seed=8),
          kit.bed(B, ash, r, pic, lean, 0.2, z_at=0.1, cell=0.6, edge=0.5, seed=9, gaps=False),
          kit.bed(B, ash, r, pic, tower, 1.5, z_at=0.8, cell=0.6, edge=1.6, seed=10, gaps=False)]
    ground = kit.ground_of(*hs)
    for i, (q, k, z) in enumerate(((main, 130, 0.35), (lean, 60, 0.1), (tower, 34, 0.8))):
        kit.cover(B, dm, r, kit.plan_of(r, q, z), ground, k, {"slab": 4, "beam": 3, "board": 1, "stone": 3},
                  pic=pic, size=(0.6, 1.2), length=(1.2, 2.6), seed=20 + i, tilt=30, grow=0, stone=(0.35, 0.7),
                  jumble=0.35, stick=0.05)
    # a sooty piece of the lean-to roof lies at the front left, cols 10-95
    # rows 150-225, and a smaller one beside it
    g = kit.RoofGeom(-3.6, -4.2, 5.0, 4.0, 0.3, 0.9, 0.0, kind="gable", T=0.18)
    burnt = kit.roof_mat(n + "_broof", g, "#5e4e46", course=0.26, tile_w=0.7, var=0.1, lip=0.2, soot=0.6,
                         swirl=2.0, width=0.45, dark=0.85, seed=12)
    with B.at(-3.6, -4.2, 0):
        B.slab(burnt, [(-2.5, -1.7, 0.3), (1.9, -1.8, 0.9), (2.4, 0.2, 1.4), (2.0, 1.7, 1.1), (-2.4, 1.6, 0.5)], 0.18)
    x, y = px(r, 110, 170, 0.5)
    with B.at(x, y, 0.3, yaw=-10, pitch=15):
        B.slab(burnt, [(-0.9, -0.8, 0), (0.9, -0.9, 0), (1.0, 0.8, 0), (-0.8, 0.9, 0)], 0.15)
    # stones and timbers scattered off the edges
    flat = lambda x, y: 0.0  # noqa: E731
    for i, q in enumerate(([(0, 30), (22, 30), (22, 250), (0, 250)], [(196, 40), (223, 40), (223, 250), (196, 250)],
                           [(20, 236), (200, 236), (200, 263), (20, 263)])):
        kit.cover(B, dm, r, kit.plan_of(r, q, 0.0), flat, 10, {"stone": 3, "slab": 2, "beam": 2}, pic=pic,
                  size=(0.25, 0.5), length=(0.5, 1.2), stone=(0.2, 0.35), seed=30 + i, tilt=20, grow=0, tries=300)
