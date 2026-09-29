"""The Aramon smithy: a tall gabled house and a wide workshop wing in an L,
a yard walled in low stone with the anvil, the forge, its bellows and the
quench tub; and the smithy damaged and ransacked."""
import math

import kit
from kit import Mat, px
from m_halls import tiled_body
from models import P, model

# the tall house turned so its front gable faces the lower left, and the
# wing running off its right side toward the east
# the house stands two storeys to its eaves under a low roof, hipped over
# its front (local west) and gabled at the back where the chimney stands
# both are broader and a little lower than the pixel fit, so neither the
# tall house nor the steep wing looks thin from low down
HOUSE = P(cx=-4.75, cy=1.45, yaw=76.0, L=7.5, W=5.0, H=4.4, R=1.45, over=0.35, over_end=0.3, hip=0.3, T=0.3)
HOUSE_ENDS = ("hip", "gable")
WING = P(cx=2.03, cy=1.75, yaw=-15.6, L=10.4, W=5.1, H=2.3, R=3.9, over=0.4, over_end=0.3, hip=-1, T=0.3)

# yard wall runs as sprite pixels of their tops (col, row), 0.55 high
YARD = [[(73, 190), (68, 222), (124, 238)], [(237, 166), (213, 250), (154, 242)]]


def smithy_mats(n, seed=101):
    return {"roof": Mat(n + "_roof", tex=kit.tex_tiles(n + "_roof", "#4e4c32", rows=11, cols=3, var=0.1, lip=0.15,
                                                       moss="#556028", moss_amt=0.3, seed=seed), uv=3.0),
            "wall": Mat(n + "_pwall", tex=kit.tex_planks(n + "_pwall", "#665c46", boards=7, seed=seed + 1), uv=2.0),
            "timber": Mat(n + "_ptimber", tex=kit.tex_planks(n + "_ptimber", "#241c12", boards=2, seed=seed + 2),
                          uv=1.0),
            "cap": Mat(n + "_pcap", "#2a2618"),
            "dark": Mat(n + "_pdark", "#120e0a"),
            "shutter": Mat(n + "_shutter", tex=kit.tex_planks(n + "_shutter", "#5a3a1e", boards=3, seed=seed + 3,
                                                              vertical=True), uv=0.8),
            "stone": Mat(n + "_chim", tex=kit.tex_stone(n + "_chim", "#4a4a46", rows=7, seed=seed + 4), uv=1.5),
            "yard": Mat(n + "_yard", tex=kit.tex_stone(n + "_yard", "#46484c", rows=3, seed=seed + 5,
                                                       mortar="#222326"), uv=1.2, ref="#3a3a3c"),
            "iron": Mat(n + "_iron", "#26262a", rough=0.5, metal=0.6),
            "block": Mat(n + "_block", tex=kit.tex_planks(n + "_block", "#4a3a26", boards=3, seed=seed + 6), uv=0.8),
            "coal": Mat(n + "_coal", tex=kit.tex_mottle(n + "_coal", "#c45b1c", var=0.3, seed=seed + 8, speck="#2a0e06",
                                                        speck_amt=0.25), uv=0.6, emit=(0.7, 0.2, 0.03), strength=0.5),
            "water": Mat(n + "_water", "#3e4a48", rough=0.35),
            "tub": Mat(n + "_tub", tex=kit.tex_planks(n + "_tub", "#7a5228", boards=10, seed=seed + 7, vertical=True),
                       uv=1.0, ref="#7a5228"),
            "hoop": Mat(n + "_hoop", "#1e1a16", rough=0.5, metal=0.4),
            "leather": Mat(n + "_leather", "#553d20"),
            "glow": Mat(n + "_glow", "#6a5030", emit=(0.6, 0.35, 0.12), strength=0.6),
            "wood": Mat(n + "_wood", "#3a2c1a"),
            "sign": Mat(n + "_sign", tex=kit.tex_mottle(n + "_sign", "#8a7438", var=0.2, seed=seed + 9,
                                                        speck="#b0501c", speck_amt=0.12), uv=0.6)}


def on_face(p, u, v, z):
    """A point of body p from its local frame to the world."""
    c, s = math.cos(math.radians(p["yaw"])), math.sin(math.radians(p["yaw"]))
    return (p["cx"] + u * c - v * s, p["cy"] + u * s + v * c, z)


def window(B, m, p, side, s, z0, w=0.8, h=0.8, open_=True):
    """A shuttered window on a long face (S or N) or an end (W or E) of
    body p: a dark opening with its two shutters swung open."""
    L, W = p["L"], p["W"]
    with B.at(p["cx"], p["cy"], yaw=p["yaw"]):
        if side in "WE":
            e = 1 if side == "E" else -1
            with B.at(e * L / 2, 0, 0, yaw=90 if e > 0 else -90):
                _window(B, m, s * (1 if e > 0 else -1), z0, w, h, open_)
        else:
            e = 1 if side == "N" else -1
            with B.at(0, e * W / 2, 0, yaw=0 if e < 0 else 180):
                _window(B, m, s * (1 if e < 0 else -1), z0, w, h, open_)


def _window(B, m, s, z0, w, h, open_):
    # in a frame whose -y faces out of the wall
    B.box(m["dark"], s, -0.03, z0, w, 0.06, h)
    B.box(m["timber"], s, -0.05, z0 - 0.08, w + 0.2, 0.1, 0.08)
    B.box(m["timber"], s, -0.05, z0 + h, w + 0.2, 0.1, 0.08)
    for e in (-1, 1):
        if open_:
            B.box(m["shutter"], s + e * (w / 2 + w / 4 + 0.05), -0.07, z0, w / 2, 0.05, h)
        else:
            B.box(m["shutter"], s + e * w / 4, -0.07, z0, w / 2 - 0.02, 0.05, h)


def yard_walls(B, m, r, runs, h=0.55, t=0.4, broken=None, seed=0):
    """Low stone walls along runs of sprite points (tops drawn at h)."""
    import random
    g = random.Random(seed)
    for run in runs:
        pts = [px(r, c, rw, h) for c, rw in run]
        for a, b in zip(pts, pts[1:]):
            if broken:
                prof = [(f, h * g.uniform(0.3, 1.0)) for f in (0, 0.3, 0.6, 1.0)]
                kit.broken_wall(B, m["yard"], m["yard"], a, b, t, prof, seed=g.randint(0, 999), jitter=0.12,
                                step=0.35)
            else:
                d = (b[0] - a[0], b[1] - a[1])
                L = math.hypot(*d)
                yaw = math.degrees(math.atan2(d[1], d[0]))
                B.box(m["yard"], (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, 0, L + t, t, h, yaw=yaw)
                B.box(m["yard"], (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, h, L + t + 0.06, t + 0.08, 0.08, yaw=yaw)


def anvil(B, m, x, y, yaw=0.0):
    with B.at(x, y, yaw=yaw):
        B.box(m["block"], 0, 0, 0, 0.55, 0.55, 0.55)
        B.box(m["iron"], 0, 0, 0.55, 0.35, 0.3, 0.2)
        B.box(m["iron"], 0, 0, 0.75, 0.8, 0.34, 0.16)
        B.cyl(m["iron"], 0.4, 0, 0.83, 0.12, 0.45, r_top=0.01, seg=8, axis=(1, 0, 0))


def forge(B, m, x, y, yaw=0.0, lit=True):
    with B.at(x, y, yaw=yaw):
        B.box(m["yard"], 0, 0, 0, 2.0, 1.6, 0.8)
        B.box(m["coal"] if lit in (True, "lit", "embers") else m.get("cinder", m["dark"]), 0, 0, 0.8, 1.6, 1.2, 0.06)
        for e in (-1, 1):
            B.box(m["yard"], 0, e * 0.73, 0.8, 2.0, 0.14, 0.14)
        for e in (-1, 1):
            B.box(m["yard"], e * 0.93, 0, 0.8, 0.14, 1.6, 0.14)


def bellows(B, m, x, y, yaw=0.0):
    with B.at(x, y, 0.35, yaw=yaw):
        B.slab(m["leather"], [(-0.45, -0.3, 0.0), (0.35, -0.15, 0.0), (0.35, 0.15, 0.0), (-0.45, 0.3, 0.0)], 0.1)
        B.slab(m["leather"], [(-0.45, -0.3, 0.3), (0.35, -0.15, 0.05), (0.35, 0.15, 0.05), (-0.45, 0.3, 0.3)], 0.08)
        B.cyl(m["iron"], 0.35, 0, 0.05, 0.05, 0.4, seg=6, axis=(1, 0, 0))
        B.box(m["wood"], 0, 0, -0.35, 0.3, 0.3, 0.35)


def tub(B, m, x, y, r=0.8, h=0.45, water=True):
    """The open quench tub with water standing in it, or burnt out."""
    kit.open_tub(B, m["tub"], m["hoop"], m["water"], x, y, r=r, h=h, burnt=None if water else m["dark"], seg=14)


def chimney_top(r):
    """The chimney stands on the house's back hip where the intact sprite
    draws its top at col 73 row 7; the same place for the damaged ones."""
    x, y, _ = on_face(HOUSE, HOUSE["L"] / 2 - 0.5, 0, 0)
    # a little shorter than the sprite's row gives, so the stack is stout
    return x, y, (149 - 7 - 16 * y) / 8.0 - 0.6


def smithy(B, r, m, mh=None, mw=None, chimney_mat=None):
    """The intact buildings; the damaged ones cut them afterwards. mh and
    mw give the house and the wing their own materials (sooty roofs)."""
    tiled_body(B, mh or m, HOUSE, ends=HOUSE_ENDS, frame=dict(bays=2.2, braces=False, rail=0.5))
    tiled_body(B, mw or m, WING, frame=dict(bays=2.4, braces=False))
    # the chimney at the back of the house's ridge, its top drawn at row 7
    x, y, zt = chimney_top(r)
    zr = HOUSE["H"] + HOUSE["R"]
    st = chimney_mat or m["stone"]
    B.box(st, x, y, zr - 2.4, 1.15, 1.15, zt - zr + 2.4 - 0.15)
    B.box(st, x, y, zt - 0.15, 1.32, 1.32, 0.15)
    B.box(m["dark"], x, y, zt, 0.75, 0.75, 0.02)
    # shuttered windows in both storeys of the house's front, and down the wing
    window(B, m, HOUSE, "W", -0.9, 0.7, 0.8, 0.8)
    window(B, m, HOUSE, "W", 1.0, 0.7, 0.8, 0.8)
    window(B, m, HOUSE, "W", -0.9, 2.6, 0.7, 0.7, open_=False)
    window(B, m, HOUSE, "W", 1.0, 2.6, 0.7, 0.7, open_=False)
    for s in (-3.4, 2.0, 4.2):
        window(B, m, WING, "S", s, 0.9, 0.8, 0.7)
    # the smithy door, open, the forge glow inside, col 140
    with B.at(WING["cx"], WING["cy"], yaw=WING["yaw"]):
        s = -1.2
        y = -WING["W"] / 2
        B.box(m["glow"], s, y - 0.02, 0.0, 1.2, 0.05, 1.8)
        B.box(m["timber"], s, y - 0.06, 1.8, 1.45, 0.12, 0.14)
        for e in (-1, 1):
            B.box(m["timber"], s + e * 0.66, y - 0.06, 0.0, 0.14, 0.12, 1.9)
        B.box(m["shutter"], s + 0.62 + 0.55, y - 0.5, 0.0, 0.06, 1.1, 1.75, yaw=-35)


def shift(r, pts):
    """Sprite points of the intact smithy moved into feature r's picture,
    whose anchor sits elsewhere in it."""
    hx, hy = r["sprite"]["hotspot"]
    return [(c - 132 + hx, rw - 149 + hy) for c, rw in pts]


def yard(B, r, m, fire="lit", tub_state="water", broken=False, sign=True):
    """The walled yard: anvil, forge (fire lit, embers or cold), bellows,
    the quench tub (water or burnt), tongs and the hanging sign."""
    P = lambda c, rw, z: px(r, *shift(r, [(c, rw)])[0], z)  # noqa: E731
    yard_walls(B, m, r, [shift(r, run) for run in YARD], broken=broken)
    anvil(B, m, *P(110, 212, 0.9), yaw=-12)
    forge(B, m, *P(190, 204, 0.8), yaw=-12, lit=fire)
    bellows(B, m, *P(210, 200, 0.5), yaw=160)
    tub(B, m, *P(192, 226, 0.42), r=0.78, h=0.42, water=tub_state == "water")
    tub(B, m, *P(171, 236, 0.3), r=0.28, h=0.4, water=tub_state == "water")
    # tongs by the forge
    a, b = P(168, 212, 0.1), P(178, 206, 0.1)
    B.beam(m["iron"], (a[0], a[1], 0.08), (b[0], b[1], 0.1), 0.06)
    if not sign:
        return
    # the smith's sign, a wheel hung from a bracket off the house's front
    # corner: the bracket drawn from the corner to col 100 row 168, the
    # wheel round col 89 row 181
    a = on_face(HOUSE, -HOUSE["L"] / 2 + 0.15, -HOUSE["W"] / 2, 2.7)
    bx, by = P(100, 168, 2.7)
    B.beam(m["sign"], (a[0], a[1], 2.7), (bx, by, 2.7), 0.14)
    x, y = P(89, 181, 1.8)
    with B.at(x, y, 1.8, yaw=-14):
        kit.wheel(B, m["wood"], m["wood"], 0, 0, 0, 0.5, axis=(0, 1, 0), spokes=8)
    for e in (-0.35, 0.35):
        B.beam(m["iron"], (x + e, y, 2.25), (x + e * 0.8, y, 2.68), 0.03)


@model("Arabuild04")
def arabuild04(B, r):
    m = smithy_mats(r["name"])
    smithy(B, r, m)
    yard(B, r, m)


# ---------------------------------------------------------------- the smithy damaged and ransacked

def geom(p, ends=None):
    return kit.RoofGeom.of(p, kind="gable" if p["hip"] < 0 else "hip", ends=ends)


def char_mats(n, m, seed=111):
    """The fire's work on the smithy: charred boards and timbers, a charred
    floor for the holes, embers still glowing in the forge, a burnt tub."""
    m = dict(m)
    m["wall"] = Mat(n + "_pwall", tex=kit.tex_planks(n + "_pwall", "#3a3428", boards=7, seed=seed), uv=2.0)
    m["timber"] = Mat(n + "_ptimber", tex=kit.tex_planks(n + "_ptimber", "#18120c", boards=2, seed=seed + 1), uv=1.0)
    m["shutter"] = Mat(n + "_shutter", tex=kit.tex_planks(n + "_shutter", "#2e2014", boards=3, seed=seed + 2,
                                                          vertical=True), uv=0.8)
    m["cap"] = Mat(n + "_pcap", "#1a1812")
    m["glow"] = Mat(n + "_glow", "#2a1c12", emit=(0.5, 0.18, 0.05), strength=0.25)
    m["coal"] = Mat(n + "_coal", tex=kit.tex_grit(n + "_coal", "#2a1a12", light="#c8581a", dark="#0c0806", uv=1.0,
                                                   chips=0.16, darks=0.35, seed=seed + 3), uv=1.0, glow=0.8)
    m["cinder"] = Mat(n + "_cinder", tex=kit.tex_grit(n + "_cinder", "#241410", light="#6a2a14", dark="#0c0806",
                                                       uv=1.0, chips=0.2, darks=0.3, seed=seed + 8), uv=1.0)
    m["tub"] = Mat(n + "_tub", tex=kit.tex_planks(n + "_tub", "#3a2618", boards=10, seed=seed + 4, vertical=True),
                   uv=1.0)
    m["dark"] = Mat(n + "_pdark", "#16120e")
    m["yard"] = Mat(n + "_yard", tex=kit.tex_stone(n + "_yard", "#3e4046", rows=3, seed=seed + 5, mortar="#1c1d20"),
                    uv=1.2)
    m["char"] = Mat(n + "_char", tex=kit.tex_grit(n + "_char", "#241e16", light="#8a8478", dark="#0a0806", uv=2.0,
                                                   chips=0.12, darks=0.25, seed=seed + 6), uv=2.0)
    m["ash"] = Mat(n + "_ash", tex=kit.tex_grit(n + "_ash", "#1a140e", light="#6a6258", dark="#060504", uv=2.0,
                                                 chips=0.04, darks=0.3, seed=seed + 7), uv=2.0)
    return m


def sooty_roof(n, key, g, r, holes, base="#524c2a", dark=0.85, seed=0):
    """The smithy's shingles darkened, streaked with soot and haloed black
    round the holes the fire broke through them."""
    halos = []
    for q in holes:
        h = kit.hole_centre(r, g, q)
        if h is not None:
            halos.append((h[0], h[1], h[2] * 1.6 + 0.9, 0.95))
    return kit.roof_mat(n + key, g, base, course=0.27, tile_w=0.9, var=0.18, lip=0.15, moss="#4a5224",
                        moss_amt=0.22, soot=0.9, swirl=2.5, width=0.45, halos=halos, dark=dark, seed=seed)


def hole_insides(B, r, pic, m, dm, g, holes, sag=0.8, n=18, seed=0):
    """Each hole's charred inside sagging below the roof plane with charred
    timbers, boards and slipped shingles lying in it."""
    for i, q in enumerate(holes):
        fill, Pp = kit.sag_fill(B, m["char"], r, g, q, sag=sag, cell=0.42, noise=0.05, seed=seed + i, edge=0.9)
        if len(Pp) >= 3:
            kit.cover(B, dm, r, Pp, fill, n, {"beam": 3, "board": 2, "slab": 3, "stone": 2}, pic=pic,
                      size=(0.3, 0.7), length=(0.8, 1.8), seed=seed + 10 + i, lift=0.04, tilt=20, grow=0,
                      stone=(0.2, 0.4), jumble=0.15)


def debris_mats(n, pic, seed=0, box=None, k=6):
    """Timbers, shingle slabs, boards and stones in the colours the sprite
    draws its rubble in."""
    cols = kit.palette(pic, k=k, seed=seed, box=box)
    slab = kit.swatches(n + "_ds", cols, "mottle", seed=seed)
    wood = kit.swatches(n + "_dw", [c for c in cols if c[0] >= c[2]] or cols, "planks", seed=seed + 20)
    return {"slab": slab, "tile": slab, "stone": slab, "chunk": slab, "beam": wood, "board": wood}


@model("Arabuild04a")
def arabuild04a(B, r):
    import m_halls
    n = r["name"]
    pic = kit.Picture(r)
    m = char_mats(n, smithy_mats(n))
    gh, gw = geom(HOUSE, HOUSE_ENDS), geom(WING)
    house_holes = [[(62, 38), (96, 40), (100, 66), (82, 76), (62, 64)],
                   [(24, 110), (44, 102), (56, 122), (50, 144), (30, 148), (20, 134)]]
    wing_holes = [[(124, 70), (166, 72), (184, 96), (178, 128), (142, 134), (122, 108)],
                  [(198, 88), (238, 88), (264, 102), (258, 122), (222, 124), (198, 110)]]
    mh = dict(m, roof=sooty_roof(n, "_hroof", gh, r, house_holes, seed=1))
    mw = dict(m, roof=sooty_roof(n, "_wroof", gw, r, wing_holes, seed=2))
    smithy(B, r, m, mh=mh, mw=mw)
    obs = B.objects()
    jagged = [kit.jag(q, 2.0, 4.0, 2 + i) for i, q in enumerate(house_holes + wing_holes)]
    kit.cut_view([o for o in obs if o.name.endswith(("_hroof", "_wroof", "_pcap"))], jagged, r)
    dm = debris_mats(n, pic, seed=5)
    hole_insides(B, r, pic, m, dm, gh, jagged[:2], sag=0.7, n=18, seed=20)
    hole_insides(B, r, pic, m, dm, gw, jagged[2:], sag=0.9, n=24, seed=30)
    # the sign's bracket and a rafter fallen off the house front, cols 88-112
    kit.leaning(B, m["timber"], r, [(88, 166, 1.2, 112, 172, 0.1), (86, 176, 0.8, 106, 186, 0.05)], w=0.16)
    # a few charred rafters still span the holes
    rafter = Mat(n + "_wrafter", tex=kit.tex_planks(n + "_wrafter", "#3a2e22", boards=2, seed=6), uv=1.0)
    m_halls.roof_frame(B, rafter, r, gh, house_holes, spacing=1.1, lath=20.0)
    m_halls.roof_frame(B, rafter, r, gw, wing_holes, spacing=1.3, lath=20.0)
    # the house front: a small spill of its charred boards at the left corner
    front = [(6, 168), (26, 160), (46, 172), (44, 192), (16, 196), (4, 184)]
    h = kit.heap(B, m["char"], r, kit.jag(front, 2.0, 4.0, 7), 0.45, z_at=0.2, cell=0.35, noise=0.08, seed=7,
                 edge=0.6, pic=pic)
    kit.cover(B, dm, r, kit.plan_of(r, front, 0.2), h, 18, {"beam": 3, "board": 3, "slab": 2}, pic=pic,
              length=(0.8, 2.0), seed=8, tilt=20, grow=2)
    # boards and shingles fallen along the wing's front and into the yard
    flat = lambda x, y: 0.0  # noqa: E731
    for i, q in enumerate(([(110, 146), (256, 146), (262, 176), (118, 186)],
                           [(96, 196), (250, 196), (250, 250), (96, 250)])):
        kit.cover(B, dm, r, kit.plan_of(r, q, 0.1), flat, 16, {"beam": 3, "board": 2, "slab": 2, "stone": 2},
                  pic=pic, length=(0.5, 1.2), size=(0.3, 0.6), seed=40 + i, tilt=15, grow=1, tries=300)
    yard(B, r, m, fire="embers", tub_state="burnt", broken=True, sign=False)
    return obs


@model("Arabuild04b")
def arabuild04b(B, r):
    n = r["name"]
    pic = kit.Picture(r)
    m = char_mats(n, smithy_mats(n), seed=121)
    # brown timbers, mossy roof slabs and grey stones in the sprite's colours
    cols = kit.palette(pic, k=8, seed=9, lit=0.3)
    slab = kit.swatches(n + "_ds", cols, "mottle", seed=9, sat=1.35, gain=1.3)
    moss = kit.swatches(n + "_dm", ["#4e5230", "#5e6034", "#3e4226"], "mottle", seed=15, sat=1.2)
    wood = kit.swatches(n + "_dw", ["#4a3422", "#5a4028", "#3a2a1c", "#6a5034"], "planks", seed=29, sat=1.0)
    dm = {"slab": slab + moss, "stone": slab, "chunk": slab, "beam": wood, "board": wood}
    # the ground-floor walls stand as charred stubs along both outlines, the
    # house's side walls highest
    stub = Mat(n + "_stub", tex=kit.tex_planks(n + "_stub", "#2a2016", boards=3, seed=3), uv=1.2)
    top = Mat(n + "_stubtop", "#1a140e")
    kit.ruin_walls(B, stub, top, HOUSE, {"N": [(0, 1.6), (0.3, 2.0), (0.6, 1.7), (1, 2.1)],
                                         "S": [(0, 1.4), (0.4, 1.8), (0.7, 1.3), (1, 1.6)],
                                         "W": [(0, 0.8), (0.5, 0.5), (1, 0.9)],
                                         "E": [(0, 1.3), (0.5, 1.5), (1, 1.2)]}, t=0.3, seed=3, jitter=0.2, step=0.5)
    kit.ruin_walls(B, stub, top, WING, {"N": [(0, 0.9), (0.3, 1.2), (0.6, 0.8), (1, 1.1)],
                                        "S": [(0, 0.6), (0.3, 0.9), (0.6, 0.5), (1, 0.8)],
                                        "E": [(0, 0.8), (1, 1.0)]}, t=0.3, seed=5, jitter=0.2, step=0.5)
    # a piece of the house's mossy roof still leans on its east wall
    g = geom(HOUSE, HOUSE_ENDS)
    roof = kit.roof_mat(n + "_hroof", g, "#4e4c32", course=0.27, tile_w=0.9, var=0.18, lip=0.15, moss="#4a5224",
                        moss_amt=0.3, soot=0.6, swirl=2.5, width=0.45, dark=0.85, seed=4)
    a0, a1 = on_face(HOUSE, -1.8, HOUSE["W"] / 2 - 0.2, 2.0), on_face(HOUSE, 1.8, HOUSE["W"] / 2 - 0.2, 2.0)
    b0, b1 = on_face(HOUSE, -1.5, 0.2, 3.3), on_face(HOUSE, 1.6, 0.4, 3.1)
    B.slab(roof, [a0, a1, b1, b0], 0.22)
    # the fallen roofs and floors: a low bed of ash under timbers, mossy
    # slabs and stones, tracing both buildings, the house's reaching forward
    ash = Mat(n + "_ash", tex=kit.tex_grit(n + "_ash", "#221e1a", light="#a09c94", dark="#0a0806", uv=2.0,
                                            chips=0.12, darks=0.35, seed=7), uv=2.0)
    house = [(40, 48), (74, 30), (100, 34), (120, 62), (122, 150), (104, 200), (60, 204), (18, 198), (4, 180),
             (14, 130), (28, 80)]
    wing = [(108, 64), (160, 66), (230, 92), (268, 104), (268, 168), (240, 178), (160, 164), (112, 152)]
    hs = [kit.bed(B, ash, r, pic, house, 1.1, z_at=0.5, cell=0.7, edge=1.8, seed=11, gaps=False),
          kit.bed(B, ash, r, pic, wing, 0.6, z_at=0.3, cell=0.7, edge=1.4, seed=12)]
    ground = kit.ground_of(*hs)
    for i, (q, k, z) in enumerate(((house, 165, 0.5), (wing, 85, 0.3))):
        kit.cover(B, dm, r, kit.plan_of(r, q, z), ground, k, {"slab": 6, "beam": 3, "board": 1, "stone": 2},
                  pic=pic, size=(0.7, 1.3), length=(1.2, 2.6), seed=20 + i, tilt=28, stick=0.06, grow=0,
                  stone=(0.35, 0.65), q=0.7, jumble=0.25)
    # the chimney stack stands on alone in dark stone: broader and lower than
    # the sprite draws it, with rubble banked round its foot, so it reads as
    # a stout stack and not a thin tower from low down
    x, y, zt = chimney_top(r)
    zt -= 1.3
    chim = Mat(n + "_chim", tex=kit.tex_stone(n + "_chim", "#30323a", rows=7, seed=4, mortar="#141418"), uv=1.5)
    B.box(chim, x, y, 0.0, 2.0, 2.0, zt - 0.2)
    B.box(chim, x, y, zt - 0.2, 2.2, 2.2, 0.2)
    B.box(m["dark"], x, y, zt, 1.1, 1.1, 0.02)
    foot = kit.circle_px(r, x, y, 2.2, 0.8, k=10)
    hf = kit.heap(B, ash, r, kit.jag(foot, 1.5, 4.0, 5), 1.9, z_at=0.8, cell=0.5, noise=0.05, seed=6, edge=1.2)
    kit.cover(B, dm, r, kit.plan_of(r, foot, 0.8), hf, 12, {"slab": 4, "beam": 2, "stone": 2}, pic=pic,
              size=(0.6, 1.1), length=(1.0, 2.0), seed=7, tilt=30, grow=1, jumble=0.2, stone=(0.35, 0.6))
    flat = lambda x, y: 0.0  # noqa: E731
    kit.cover(B, dm, r, kit.plan_of(r, [(96, 150), (250, 150), (250, 250), (96, 250)], 0.1), flat, 18,
              {"beam": 3, "board": 2, "slab": 2, "stone": 2}, pic=pic, length=(0.6, 1.6), size=(0.3, 0.6), seed=40,
              tilt=15, grow=0, tries=300)
    yard(B, r, m, fire="cold", tub_state="burnt", broken=True, sign=False)
