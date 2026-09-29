"""The props family's parameters, one entry per feature, read off the
original pictures (sizes in cells, colours as picture sRGB, each colour
pair dark then light). Sizes are the pixel fit; kit.sturdy() then builds
the tall parts thicker and lower, with each entry's "sturdy" factors.
Only the two Aragon fountains paint parts from the picture: Arafount01 its
brick skirt, coping, pool, lip and foam, Arafount01a its brick paving and
the debris that lands on the picture (11 parts). They ship as okPaint materials."""

# ---------------------------------------------------------------- fountains

VER_FOUNTAIN_C = {
    "stone": ((150, 134, 116), (206, 192, 164)),
    "skirt": ((100, 92, 80), (160, 150, 132)),
    "lip": ((140, 132, 112), (200, 186, 158)),
    "water": ((150, 164, 172), (214, 220, 224)),
    "bronze": ((22, 22, 14), (92, 90, 72)),
    "foot": (57, 57, 49),
}
SERPENT = {
    "pedestal": (0.42, 1.0, 1.6),
    "radius": 0.32,
    "path": [(1.15, 0.45, 3.0), (1.05, 0.25, 2.3), (0.75, -0.2, 1.75), (0.15, -0.45, 1.62), (-0.45, -0.25, 1.68),
             (-0.6, 0.2, 1.88), (-0.2, 0.55, 2.08), (0.3, 0.45, 2.33), (0.35, 0.05, 2.68), (0.0, -0.1, 3.05),
             (-0.45, 0.05, 3.45), (-0.8, 0.2, 3.8), (-1.15, 0.15, 3.95)],
    "fallen_path": [(0.6, 0.5, 1.0), (0.2, 0.2, 1.05), (-0.5, 0.0, 0.95), (-1.4, -0.05, 0.75), (-2.3, -0.1, 0.6),
                    (-3.1, -0.2, 0.9), (-3.8, -0.45, 0.75), (-4.4, -0.7, 0.35)],
    "snout": 0.6,
}

VER_FOUNTAIN = {
    "kind": "fountain",
    "skirt": (4.1, 3.62, 0.5),
    "coping": (3.12, 3.66, 0.5, 0.78, 24),
    "pool_z": 0.34,
    "tiers": [(2.28, 2.55, 0.6, 0.52), (1.6, 1.85, 0.85, 0.78, None, ((130, 140, 146), (200, 206, 210))),
              (0.98, 1.2, 1.1, 1.03, None, ((20, 26, 22), (60, 66, 58)))],
    "centre": "serpent",
    "statue": SERPENT,
    "colours": VER_FOUNTAIN_C,
    "sturdy": {"girth": 1.18, "height": 0.85, "coil": 1.05},
}

# drained: dry stone steps under the lips, a dark pit where the statue stood
# with cracks running out from it; the serpent broke from its stub and lies
# over the left rim, its head on the ground outside and its tail along the rim
VER_FOUNTAIN_RUIN = dict(
    VER_FOUNTAIN,
    ruin=True, dry=True, seed=4,
    missing=((95, 140),),
    tumble={5: (0.12, 18, 0.05), 11: (-0.1, -10, 0.15), 17: (0.05, 12, 0.1), 21: (-0.15, 20, 0.2)},
    pool_z=0.14,
    tiers=[(2.28, 2.55, 0.55, 0.42, (160, 420)), (1.6, 1.85, 0.8, 0.66, (200, 470)), (1.12, 1.32, 1.05, 0.9, (-60, 150))],
    pit=(1.06, 0.5), pit_rubble=8, cracks=12, crack_w=0.15,
    centre="serpent_fallen",
    statue=dict(SERPENT, stub=False,
                fallen_path=[(-1.6, 0.45, 0.95), (-2.15, 0.42, 1.1), (-2.75, 0.32, 1.22), (-3.3, 0.12, 1.3),
                             (-3.8, -0.05, 1.15), (-4.1, -0.18, 0.8)],
                fallen_path_r=[0.42, 0.44, 0.45, 0.45, 0.42, 0.4],
                coil=[(-3.3, 0.12, 1.28), (-3.5, -0.35, 1.2), (-3.48, -0.85, 1.15), (-3.3, -1.3, 1.08),
                      (-3.0, -1.7, 1.0)],
                coil_r=[0.43, 0.42, 0.4, 0.3, 0.14],
                head=(0.5, (-4.3, -0.3, 0.45)), lit=(0.15, 0.45)),
    debris=((-3.75, -2.9, 0.35, "finial"), (-1.2, -4.25, 0.45), (-1.3, 3.7, 0.35), (2.7, -3.3, 0.3)),
    specks=10,
    colours=dict(VER_FOUNTAIN_C, stone=((120, 104, 92), (196, 180, 160)), skirt=((78, 72, 62), (146, 136, 120)),
                 dry=((58, 54, 48), (132, 126, 116)), lip=((124, 120, 112), (204, 198, 188)),
                 pit=((8, 8, 6), (34, 32, 27)), bronze=((20, 18, 14), (96, 88, 76)),
                 head=((36, 30, 24), (100, 82, 66)), crack=(40, 34, 30), finial=((46, 28, 22), (124, 80, 62))),
)

ARA_FOUNTAIN_C = {
    "stone": ((70, 72, 70), (136, 128, 120)),
    "skirt": ((60, 58, 58), (116, 108, 104)),
    "lip": ((150, 146, 140), (232, 232, 226)),
    "water": ((66, 84, 80), (112, 118, 112)),
    "water_in": ((150, 140, 126), (190, 180, 164)),
    "orb": ((48, 54, 18), (118, 122, 62)),
    "foot": (40, 40, 40),
}
ARA_FOUNTAIN = {
    "kind": "fountain",
    "skirt": (3.38, 3.05, 0.45),
    "coping": (2.45, 3.08, 0.45, 0.64, 28),
    "gap": 2.0,
    "pool_z": 0.24,
    "tiers": [(1.45, 1.7, 0.56, 0.48, None, ((150, 140, 126), (190, 180, 164)))],
    "centre": "orb",
    "orb": {"r": 0.38, "stand_z0": 0.4, "stand_z1": 0.95},
    "colours": ARA_FOUNTAIN_C,
    # brick paving and the foam ring are painted from the picture
    "coping_solid": True,
    "painted": ("skirt", "coping", "pool", "lip", "water"),
}
# the rim is chunky blocks with dark gaps and a notched outer edge, a few
# pushed out or tilted and four gone; the skirt stays inside them; the chunk
# off the picture's upper right is plain grey, not paint
ARA_FOUNTAIN_RUIN = dict(
    ARA_FOUNTAIN,
    ruin=True, seed=7,
    skirt=(2.85, 2.8, 0.4),
    coping=(2.45, 3.4, 0.4, 0.62, 18),
    gap=3.0, lump=0.22, notch=0.2, gap_rubble=2, shift=(0.2, 0.0),
    missing=((85, 95), (100, 128), (250, 262), (325, 335)),
    tumble={1: (0.05, 14, 0.22), 7: (-0.06, -15, 0.28), 9: (0.08, 16, 0.2), 13: (-0.06, 12, 0.3),
            15: (0.06, -18, 0.22)},
    pool_z=None,
    floor_z=0.16,
    tiers=[],
    centre="bowl_broken",
    orb={"r": 0.72, "a0": 30, "a1": 300, "tilt": (28, 0, 40), "loc": (0.05, 0.1, 0.12)},
    debris=((-0.6, 3.85, 0.3), (3.25, 2.45, 0.5, (56, 57, 59)), (-2.85, -2.75, 0.5), (1.9, -3.0, 0.14), (-3.3, 0.9, 0.12),
            (-2.2, 2.35, 0.36), (-3.05, 0.4, 0.4), (-2.6, -1.9, 0.38), (0.4, -3.0, 0.36), (3.0, 0.9, 0.4),
            (2.3, 2.2, 0.34), (2.5, -1.9, 0.36)),
    rough=0.08,
    # each block its own grey with a lighter top, the joints between them dark
    block_tone={"lum": (34, 88), "top": 1.3, "joint": (25, 25, 27), "edge": 0.6},
    gap_h=0.09,
    coping_solid=False,
    # the brick paving is painted, a fifth darker to sit at the picture's tone
    painted=("floor", "debris"),
    paint_tint=0.66,
    specks=26,
    colours=dict(ARA_FOUNTAIN_C, stone=((34, 34, 34), (100, 98, 96)), skirt=((40, 40, 40), (84, 82, 80)),
                 gap=(25, 25, 27),
                 floor=((74, 72, 70), (130, 126, 120)), orb=((50, 56, 18), (118, 122, 50)), crack=(30, 30, 30),
                 rubble=((44, 44, 42), (84, 84, 80))),
)

# ---------------------------------------------------------------- braziers

LAMP_C = {
    "base": ((42, 42, 35), (126, 118, 102)),
    "base_moss": ((72, 82, 68), 0.2),
    "column": ((12, 12, 9), (72, 68, 58)),
    "bowl": ((46, 44, 42), (98, 94, 92)),
    "dish": ((40, 39, 40), (90, 88, 88)),
}
BROKEN_LAMP_C = dict(LAMP_C, collar=((78, 80, 70), (142, 126, 114)), shard=((14, 13, 10), (82, 78, 68)),
                     frag=((70, 70, 62), (132, 132, 118)))
# lamps are the tallest props: columns a quarter thicker than the picture's
# narrowest (still inside its knob) and swelling at the foot, the whole a
# fifth lower, the flame a sixth lower and wider with fuller side tongues
LAMP_STURDY = {"girth": 1.25, "height": 0.8, "knob": 1.15, "base": 1.12, "bowl": 1.06, "flame": (0.85, 1.3),
               "side": (0.3, 0.6)}
BROKEN_STURDY = {"girth": 1.18, "height": 0.85, "base": 1.12}
VER_LAMP1 = {"kind": "brazier", "base": (1.0, 0.62), "column_r": 0.34, "knob": (2.6, 0.5), "column_top": 6.5,
             "bowl": (7.45, 1.65, 0.55), "flame": (5.6, 0.36), "colours": LAMP_C, "seed": 5, "sturdy": LAMP_STURDY,
             "column_taper": 1.25}
VER_LAMP2 = {"kind": "brazier", "base": (0.75, 0.5), "column_r": 0.3, "knob": (2.2, 0.4), "column_top": 4.9,
             "bowl": (5.6, 1.28, 0.45), "flame": (4.2, 0.3), "colours": LAMP_C, "seed": 6, "coals": 10,
             "sturdy": LAMP_STURDY, "column_taper": 1.25}
# the break: (angle, height, outward lean) per shard, a splayed burst wider than the column
VER_LAMP1_BROKEN = {"kind": "brazier", "broken": True, "base": (1.0, 0.62), "column_r": 0.33, "knob": (2.5, 0.48),
                    "column_top": 4.3,
                    "shards": [(10, 0.4, 0.34), (58, 0.3, 0.25), (118, 0.32, 0.3), (165, 0.5, 0.42),
                               (200, 0.38, 0.3), (246, 0.55, 0.36), (292, 0.45, 0.32), (335, 0.6, 0.4)],
                    "bowl_frags": [(0.55, 0.28, 4.25, (20, -25, 30), 40, 0.8), (-0.2, -0.52, 0.5, (22, 0, -85), 45, 0.75)],
                    "colours": BROKEN_LAMP_C, "seed": 8, "sturdy": BROKEN_STURDY, "column_taper": 1.15}
VER_LAMP2_BROKEN = {"kind": "brazier", "broken": True, "base": (0.75, 0.5), "column_r": 0.33, "knob": (2.0, 0.38),
                    "column_top": 3.0,
                    "shards": [(82, 0.45, 0.2), (28, 0.22, 0.26), (145, 0.28, 0.22), (208, 0.35, 0.28),
                               (262, 0.2, 0.3), (320, 0.3, 0.24)],
                    "bowl_frags": [(0.32, 0.12, 3.02, (15, -20, 70), 35, 0.55)],
                    "colours": BROKEN_LAMP_C, "seed": 9, "sturdy": BROKEN_STURDY, "column_taper": 1.15}

# ---------------------------------------------------------------- bonfires

FIRE_C = {
    "stone": ((62, 54, 40), (110, 98, 72)),
    "earth": ((26, 18, 15), (104, 76, 50)),
    "bark": ((34, 25, 19), (120, 92, 62)),
    "char": (30, 22, 20),
    "end": (150, 116, 80),
}
# the flame a fifth lower and a third wider, its side tongues taller
FIRE_STURDY = {"flame": (0.82, 1.3), "side": (0.22, 0.55)}
ZON_FIRE1 = {"kind": "bonfire", "sturdy": FIRE_STURDY, "ring": (1.88, 24, 0.26), "logs": {"count": 12, "r_out": 1.5, "apex": 0.95, "r": 0.14},
             "flame": (5.0, 0.32), "colours": FIRE_C, "seed": 1, "sticks": 4}
ZON_FIRE2 = {"kind": "bonfire", "sturdy": FIRE_STURDY, "ring": (1.8, 23, 0.26), "logs": {"count": 12, "r_out": 1.45, "apex": 1.0, "r": 0.14},
             "flame": (5.8, 0.3), "colours": FIRE_C, "seed": 2, "sticks": 4, "centre": (0.0, 0.1)}
DEAD_C = dict(FIRE_C, bark=((24, 20, 20), (88, 74, 58)), char=(22, 18, 18), end=(70, 60, 50),
              ash=((70, 64, 58), (120, 112, 100)))
ZON_FIRE1_DEAD = {"kind": "bonfire", "ring": (1.88, 24, 0.26),
                  "logs": {"count": 12, "r_out": 1.5, "apex": 0.75, "r": 0.13, "char_from": 0.2},
                  "colours": DEAD_C, "seed": 3, "ash": 8}
ZON_FIRE2_DEAD = {"kind": "bonfire", "ring": (1.78, 23, 0.27),
                  "logs": {"count": 12, "r_out": 1.45, "apex": 0.85, "r": 0.14, "char_from": 0.15, "phase": 0.2},
                  "colours": DEAD_C, "seed": 4, "ash": 6}

# ---------------------------------------------------------------- altars

# each palette is (dark, light) and the blocks mottle widely between them
ALTAR_C = {
    "top": ((78, 68, 61), (178, 161, 150)),
    "side": ((62, 54, 48), (132, 118, 106)),
    "pier_top": ((90, 80, 72), (176, 160, 150)),
    "pier_side": ((72, 62, 55), (156, 140, 128)),
    "joint": (36, 31, 28),
    "moss": ((95, 110, 60), (110, 125, 80)),
    "pot": ((80, 50, 38), (130, 86, 64)),
    "basin": (46, 40, 36),
}
VER_ALTAR = {"kind": "altar", "size": (7.1, 1.45, 1.3), "pier_w": 0.75, "pier_x": 2.87, "pier_h": (2.6, 2.4),
             "pier_front": 0.3, "joints": ((0.14, 0.3, 0.47, 0.64, 0.8), (0.22, 0.38, 0.55, 0.72, 0.88)),
             "basin": 0.62, "basin_depth": 0.3, "tones": (0.88, 1.12), "colours": ALTAR_C, "seed": 2,
             # piers thicker and lower, the slab a little deeper and lower
             "sturdy": {"girth": 1.15, "height": 0.88, "depth": 1.08, "body": 0.94}}
# sandy tops with moss on about a sixth of them, a dark front and plinth,
# grey-pink piers
ZON_ALTAR = dict(VER_ALTAR, seed=3, moss=0.12, joints=((0.12, 0.33, 0.5, 0.66, 0.84), (0.2, 0.42, 0.58, 0.76, 0.9)),
                 offerings=((-0.05, -0.95, 0.22),),
                 colours=dict(ALTAR_C, top=((118, 106, 82), (200, 184, 148)), side=((44, 40, 32), (94, 88, 76)),
                              pier_top=((96, 88, 82), (168, 154, 144)), pier_side=((70, 64, 58), (140, 128, 118)),
                              joint=(40, 36, 28)))

# ---------------------------------------------------------------- carts

CART_C = {
    "wood": ((40, 33, 26), (120, 102, 80)),
    "end": (60, 50, 40),
    "iron": ((22, 18, 14), (58, 50, 42)),
    "hay": ((56, 50, 16), (140, 130, 30)),
    "hay_hi": (186, 172, 46),
    "sack": ((66, 56, 40), (126, 108, 80)),
    "sack_hi": (164, 148, 88),
    "grass": ((28, 52, 16), (72, 122, 36)),
}
# stakes, beams, shafts and wheels a fifth thicker, rails, boards and load lower
CART_STURDY = {"girth": 1.18, "height": 0.9}
ARA_CART1 = {"kind": "cart", "sturdy": CART_STURDY, "bed": (3.1, 1.8), "wheel": 0.55, "shafts": 1.5, "shaft_gap": 1.15, "rails": (1,),
             "posts": 6, "rail_h": 0.8, "boards": (1,), "board_h": 0.36, "board_span": {1: (0.0, 0.65)},
             "yaw": 31, "at": (-0.85, -0.25), "axle_x": 0.1, "beam": 0.14, "shaft_w": 0.2, "shaft_t": 0.12,
             "colours": CART_C, "seed": 1}
ARA_CART2 = {"kind": "cart", "sturdy": CART_STURDY, "bed": (3.5, 1.6), "wheel": 0.55, "shafts": 1.5, "shaft_gap": 1.2, "yaw": 128,
             "at": (0.25, -0.1), "beam": 0.14, "shaft_w": 0.18, "shaft_t": 0.12,
             "hub_off": 0.2,
             # a long low loaf from over the shaft roots to past the tail, full width
             # with blunt ends, nudged to the near side so the far rail shows; its
             # hay a sixth darker than the other carts'
             "hay": {"h": 0.94, "L": 4.13, "over": (1.0, 0.9), "x": 0.15, "y": 0.1, "blunt": 0.45, "lying": 700, "strands": 120},
             "colours": dict(CART_C, hay=((48, 43, 14), (119, 111, 26)), hay_hi=(158, 146, 39)), "seed": 2}
# a heap of five khaki sacks on the front-left two-thirds of the bed against
# the left wall, one lying on the others, the back-right third bare boards;
# thin dark seams where the sacks meet; the shafts run parallel from under
# the bed's front, and the left wheel shows past the box
ARA_CART3 = {"kind": "cart", "sturdy": CART_STURDY, "bed": (3.0, 2.0), "wheel": 0.55, "shafts": 1.5, "shaft_gap": 0.7,
             "shaft_root": 0.9, "yaw": -63,
             "at": (-0.1, 0.24), "boards": (-1, 1), "board_h": 0.45, "head": 0.45, "tail": 0.45, "hub_off": 0.2,
             "beam": 0.14, "shaft_w": 0.2, "shaft_t": 0.12,
             "sacks": ((0.85, -0.45, 80, (0.5, 0.44, 0.22), (0, 0)), (0.0, -0.47, 95, (0.52, 0.44, 0.22), (0, 0)),
                       (-0.85, -0.47, 85, (0.5, 0.42, 0.22), (0, 0)), (0.9, 0.4, 100, (0.5, 0.42, 0.22), (0, 0)),
                       (0.3, 0.0, 10, (0.58, 0.4, 0.2), (6, 0), 0.12)),
             "colours": dict(CART_C, sack=((36, 30, 23), (112, 99, 72)), sack_hi=(132, 122, 66),
                             sack_crease=(20, 17, 13), crease_w=0.06, fold_z=0.15), "seed": 3}
ARA_CART4 = {"kind": "cart", "sturdy": CART_STURDY, "bed": (3.2, 2.0), "wheel": 0.55, "shafts": 1.4, "shaft_gap": 1.2, "yaw": 160,
             "at": (0.5, 0.15), "boards": (-1,), "board_h": 0.3, "board_n": 1, "head": 0.7, "beam": 0.14,
             "shaft_w": 0.2, "shaft_t": 0.12, "colours": CART_C, "seed": 4}
WRECK_C = dict(CART_C, wood=((32, 27, 21), (112, 96, 76)))
# the wreck: seven slats with ground between them, racked into a leaning
# parallelogram, a cross beam at the far end; the wheel still on the axle
# leans 40 degrees off upright with the cart's roll, a tall tilted ellipse
ARA_CART5 = {"kind": "cart", "sturdy": CART_STURDY, "bed": (3.3, 2.3), "wheel": 0.52, "shafts": 0, "yaw": 90, "at": (0.1, 0.35),
             "pitch": -3, "roll": 12, "lift": -0.33, "wheels": (1,), "wheel_lean": {1: 28}, "wheel_yaw": {1: -20}, "hub_off": 0.35, "axle_x": 0.35,
             "planks": 7, "plank_gap": (0.08, 0.12), "stagger": 0.15, "shear": 17, "cross": (0.78, 4),
             "splay": 0.02, "warp": 6, "short": {1: 0.75, 4: 0.88}, "beam": 0.14, "shaft_w": 0.2, "shaft_t": 0.12,
             "loose_wheels": ((1.72, 0.0, 0.09, -90, 0, 20),),
             "loose_planks": (((-0.75, 1.5, 0.45), (0.95, 1.05, 0.45), 0.24, 0.07),),
             "loose_shafts": (((0.0, 1.65, 0.4), (-0.25, 3.0, 0.06)), ((1.3, 1.35, 0.06), (1.62, 2.45, 0.06))),
             "tufts": ((-1.1, 1.4, 8), (-0.35, -0.6, 9), (1.5, 1.7, 6), (1.25, 0.4, 6), (2.15, -0.4, 5)),
             "colours": WRECK_C, "seed": 5}

# ---------------------------------------------------------------- corn

CORN_C = {
    # leaves listed dark to light: deep green with lime highlights
    "soil": ((24, 18, 11), (50, 38, 24)),
    "stalk": ((20, 36, 8), (56, 80, 26)),
    "leaf": ((12, 30, 5), (32, 52, 14), (75, 94, 37), (100, 125, 50)),
    "tassel": ((128, 121, 94), (156, 148, 122)),
    # the sunlit upper leaves, the picture's lime-yellow to its khaki highlights
    "lime": ((90, 114, 40), (155, 148, 74)),
    "ear": ((90, 100, 40), (140, 130, 70)),
}


def corn_rows(hy, bands, h, lower=0.88):
    """Row positions from the picture: each pale band is one row's tassels,
    their tops at the built (sturdy) height, seen by the true tilted camera
    that draws a cell 0.894 of the oblique rule's 16 and 8 pixels."""
    k = 0.894
    return [round(((hy - b) / k - 8 * (h * lower + 0.1)) / 16.0, 3) for b in bands]


# compact upright clumps of short leaves spread along each row, so the rows
# read as hedges with dark soil between them and behind the back row; the
# leaves above half height sunlit lime to khaki, the lower ones in the rows'
# shade; each row's tassels stand up into one pale band; the soil pulled in
# under the outer leaves; stalks a third thicker and the plants an eighth lower
CORN = {"kind": "corn", "sturdy": {"girth": 1.3, "height": 0.88}, "leaves": 6, "leaf_w": 0.11, "leaf_len": 0.48,
        "leaf_pts": 3, "rise": (0.3, 0.55), "leaf_mix": (0.6, 0.9), "canopy": 0.85, "matte": True,
        "lit": (0.35, 1.0), "lit_to": 0.55, "along": 0.4, "ears": 0.25, "edge_out": 0.5, "edge_leaves": 3,
        "edge_len": 0.8, "tassel": 0.24, "tassel_w": 0.1, "tassel_n": 4, "tassel_up": (0.85, 1.0),
        "soil_back": 0.6, "soil_front": 0.2, "soil_rag": 0.35, "soil_side": 0.0, "colours": CORN_C}
ARA_CORN7 = dict(CORN, **{"h": 1.75, "rows": corn_rows(37, (4, 15, 24, 34, 43, 54), 1.75), "span": (-1.75, 1.8),
             "per_row": 8, "strays": ((-1.85, -0.3, 185, 0.6),), "seed": 7})
ARA_CORN8 = dict(CORN, **{"h": 1.7, "rows": corn_rows(32, (3, 15, 24, 33, 43, 52), 1.7), "span": (-1.85, 1.85),
             "spans": [(-1.95, 1.8), (-1.85, 1.9), (-1.9, 1.75), (-1.85, 1.85), (-1.95, 2.1), (-1.8, 1.95)],
             "per_row": 8, "seed": 8})
# the patch's left edge steps in at the second and third rows, as the picture's does
ARA_CORN9 = dict(CORN, **{"h": 1.65, "rows": corn_rows(23, (2, 13, 22, 31.5), 1.65),
             "spans": [(-1.25, 1.5), (-1.05, 1.4), (-1.05, 1.3), (-1.17, 1.3)], "per_row": 7,
             "strays": ((-1.2, -1.25, 190, 0.8),), "no_edge": ((1, -1), (2, -1)), "seed": 9})
ARA_CORN10 = dict(CORN, **{"h": 1.65, "rows": corn_rows(24, (4, 14, 23, 32.5), 1.65), "span": (-1.35, 1.2),
              "per_row": 7, "strays": ((1.15, -0.5, 5, 0.65),), "seed": 10})
# the front row runs further left, into the picture's lower-left corner
ARA_CORN11 = dict(CORN, **{"h": 1.5, "rows": corn_rows(27, (4, 15, 26, 37.5), 1.5),
              "spans": [(-1.1, 1.05), (-1.1, 1.05), (-1.1, 1.05), (-1.3, 1.05)],
              "spacing": 0.7, "per_row": 6, "seed": 11})
ARA_CORN12 = dict(CORN, **{"h": 1.55, "rows": corn_rows(27, (2, 15, 27, 37), 1.55), "span": (-1.15, 1.25),
              "spacing": 0.75, "per_row": 6, "seed": 12})

MODELS = {
    "VerFountain01": VER_FOUNTAIN,
    "VerFountain01a": VER_FOUNTAIN_RUIN,
    "Arafount01": ARA_FOUNTAIN,
    "Arafount01a": ARA_FOUNTAIN_RUIN,
    "VerLamp01": VER_LAMP1,
    "VerLamp01a": VER_LAMP1_BROKEN,
    "VerLamp02": VER_LAMP2,
    "VerLamp02a": VER_LAMP2_BROKEN,
    "ZonFire01": ZON_FIRE1,
    "ZonFire02": ZON_FIRE2,
    "ZonFireD01": ZON_FIRE1_DEAD,
    "ZonFireD02": ZON_FIRE2_DEAD,
    "VerAlt01": VER_ALTAR,
    "ZonAlt01": ZON_ALTAR,
    "Aracart01": ARA_CART1,
    "Aracart02": ARA_CART2,
    "Aracart03": ARA_CART3,
    "Aracart04": ARA_CART4,
    "Aracart05": ARA_CART5,
    "Aracrop07": ARA_CORN7,
    "Aracrop08": ARA_CORN8,
    "Aracrop09": ARA_CORN9,
    "Aracrop10": ARA_CORN10,
    "Aracrop11": ARA_CORN11,
    "Aracrop12": ARA_CORN12,
}
