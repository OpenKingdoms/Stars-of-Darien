"""Per-model settings for the trees_plants family, read from each sprite.

kind picks the builder in kit.BUILDERS; the rest are its parameters.
Points are sprite pixels (col, row).
"""

MODELS = {}

# leafy trees (kit.build_crown); trunk colours are the lit bark sampled
# from the drawn trunks. Poplars: small sprig cards all round a spindle
# over a near-black core, gold flecks; Veruna ones fleck orange and cream
POPLAR = {"kind": "crown", "leaf": "poplar", "column": True, "card": 0.55, "shrink": 0.12, "spacing": 0.74, "front_step": 0.6,
          "flecks": [("bright", 0.9)], "fleck_gain": 1.4, "dark_gain": 0.5, "mid_gain": 0.75, "face_cam": 0.5, "core": "dark",
          "core_size": 0.9, "sprays": 0.2, "spray_size": 1.1, "sturdy": (1.16, 0.87, 1.15)}
# oaks: eight big clumps with dark crevices between them, sprays at the rim
OAK = {"kind": "crown", "leaf": "cluster", "card": 0.8, "spacing": 0.55, "clumps": 7, "clump_scale": 1.0, "crevice": 0.75,
       "crevice_band": 0.6, "core": "dark", "core_size": 0.9, "sprays": 0.6, "spray_size": 1.1,
       "flecks": [("bright", 0.35)], "cores": 24, "sturdy": (1.08, 0.92, 1.1)}
# Zhon maples: big leaves hanging in shingled tiers over a mid-green core
MAPLE = {"kind": "crown", "leaf": "maple", "card": 2.6, "shrink": 0.1, "spacing": 0.55, "front_step": 0.5, "droop": 35,
         "face_cam": 0.5, "core": "mid", "core_size": 0.93,
         "cores": 24, "sturdy": (1.12, 0.88, 1.1)}
# Zhon broadleaves: stars of pointed leaves, spiky sprays at the rim
ZMAPLE = {"kind": "crown", "leaf": "star", "edge": "spray", "card": 1.2, "spacing": 0.5, "front_step": 0.45, "core": "mid",
          "core_size": 0.92, "sprays": 0.6, "spray_size": 1.2, "up_bias": 1.0, "tilt_noise": 0.3, "flat_light": 0.8,
          "flecks": [("cream", 0.25)], "cores": 24, "sturdy": (1.1, 0.9, 1.1)}


def tree(base, **kw):
    d = dict(base)
    d.update(kw)
    return d


def trunk(rgb, r, flare=1.0, buttress=5, **kw):
    d = {"rgb": rgb, "r": r, "flare": flare, "buttress": buttress}
    d.update(kw)
    return d


# a spire's crown as a flame (kit.flame_lobes): width over the drawn, depth
# over its own width, the lower crown's rounding and where it is widest
FLAME = {"flame": True, "depth": 1.8, "top_depth": 0.8, "floor": 1.5, "width": 0.9, "round": 0.8, "round_p": 1.2,
         "widest": 0.48, "fit_min": 0.35, "leader": (0.45, 2.2), "straight": True}
VER = {"flecks": [("cream", 0.6), ("orange", 0.2)], "light_gain": 1.15}
MODELS.update({
    "AraTree01": tree(POPLAR, trunk=trunk((0.28, 0.27, 0.21), 0.09, 0.8, 4)),
    "AraTree02": tree(POPLAR, trunk=trunk((0.26, 0.25, 0.19), 0.09, 0.8, 4)),
    # the narrowest spires: a flame widest 40 percent up the crown and
    # tapering straight to the point, the bulk in depth and the drawn trunk
    # showing under it in classic
    "AraTree03": tree(POPLAR, trunk=trunk((0.27, 0.25, 0.2), 0.09, 0.8, 4), sturdy=(1.15, 0.8, 1.15), crown_bottom=120,
                      profile=dict(FLAME, floor=1.0, width=0.85, depth=2.2, straight=False), cores=14, spacing=0.8, spray_size=0.5),
    "VerTree01": tree(POPLAR, trunk=trunk((0.36, 0.35, 0.31), 0.1, 0.8, 4), sturdy=(1.15, 0.78, 1.15), crown_bottom=140,
                      profile=dict(FLAME, depth=2.3), cores=14, spacing=0.9, spray_size=0.5, **VER),
    "VerTree02": tree(POPLAR, trunk=trunk((0.38, 0.37, 0.31), 0.1, 0.8, 4), sturdy=(1.15, 0.8, 1.15), crown_bottom=118,
                      profile=dict(FLAME, width=0.8, depth=2.3), cores=14, spacing=0.8, spray_size=0.5, **VER),
    "VerTree03": tree(POPLAR, trunk=trunk((0.45, 0.43, 0.36), 0.1, 0.8, 4), sturdy=(1.15, 0.8, 1.15), crown_bottom=110,
                      profile=dict(FLAME, depth=2.3), cores=14, spacing=0.8, spray_size=0.5, **VER),
    "AraTree04": tree(OAK, trunk=trunk((0.5, 0.4, 0.2), 0.42, 1.5, 5)),
    "AraTree05": tree(OAK, trunk=trunk((0.52, 0.43, 0.25), 0.42, 1.5, 5)),
    # its lowest clumps slid a cell up their camera rays, so the trunk shows as on 04/05
    "AraTree06": tree(OAK, trunk=trunk((0.45, 0.36, 0.19), 0.42, 1.5, 5), lift_low=(2.0, 1.2)),
    "ZonTree04": tree(MAPLE, crown_bottom=123, trunk=trunk((0.36, 0.39, 0.37), 0.5, 1.4, 6)),
    "ZonTree05": tree(MAPLE, crown_bottom=117, trunk=trunk((0.44, 0.48, 0.38), 0.48, 1.4, 6)),
    "ZonTree06": tree(MAPLE, crown_bottom=112, trunk=trunk((0.42, 0.47, 0.37), 0.48, 1.4, 6)),
    "ZonTree201": tree(ZMAPLE, trunk=trunk((0.47, 0.44, 0.39), 0.35, 1.2, 4)),
    "ZonTree202": tree(ZMAPLE, trunk=trunk((0.48, 0.45, 0.4), 0.35, 1.2, 4)),
    "ZonTree203": tree(ZMAPLE, trunk=trunk((0.47, 0.44, 0.39), 0.35, 1.2, 4)),
})

MODELS.update({
    "AraTree07": {"kind": "fir", "reach": 1.2, "close": 3, "spray": 1.3, "droop": 0.3},
    "AraTree08": {"kind": "fir", "reach": 1.2, "close": 3, "spray": 1.3, "droop": 0.3},
    "AraTree09": {"kind": "fir", "reach": 1.2, "close": 3, "spray": 1.3, "droop": 0.3},
    "AraTree10": {"kind": "fir", "reach": 1.2, "close": 3, "spray": 1.3, "droop": 0.3},
    # dead firs: stiff spurs up a column, lowest limbs reaching forward to
    # the drawn foot below the anchor
    # ragged: whorl and skirt heights jittered, spurs drooping at varied
    # lengths and thinned, so the classic view shows no rungs; single spurs
    # by turns on the skirt limbs, short and partly upturned ones above
    # half height, so the crown is a see-through skeleton
    "AraTree07a": {"kind": "fir", "dead": True, "close": 2, "reach": 1.0, "dz": 0.5, "spacing": 0.35, "gain": 1.0, "depth": 0.85, "skirt_top": 0.6,
                   "ragged": {"jit": 0.3, "droop": (15, 30), "len": (0.6, 1.0), "keep": 0.7, "up_len": (0.4, 0.7), "alternate": True, "skirt_extra": ((-75, -105), (-68, -112)), "extra_len": 0.7, "skirt_cap": 0.9}},
    "AraTree08a": {"kind": "fir", "dead": True, "close": 2, "reach": 1.0, "dz": 0.5, "spacing": 0.35, "gain": 1.0, "depth": 0.85, "skirt_top": 0.6,
                   "ragged": {"jit": 0.3, "droop": (15, 30), "len": (0.6, 1.0), "keep": 0.7, "thin_above": 0.5, "up_len": (0.4, 0.7), "alternate": True, "skirt_extra": ((-75, -105), (-68, -112)), "extra_len": 0.7, "skirt_cap": 0.9}},
    "AraTree09a": {"kind": "fir", "dead": True, "close": 2, "reach": 1.0, "dz": 0.5, "spacing": 0.35, "gain": 1.0, "depth": 0.85, "skirt_top": 0.6,
                   "ragged": {"jit": 0.3, "droop": (15, 30), "len": (0.6, 1.0), "keep": 0.7, "thin_above": 0.5, "up_len": (0.4, 0.7), "alternate": True, "skirt_extra": ((-75, -105), (-68, -112)), "extra_len": 0.7, "skirt_cap": 0.9}},
    "AraTree10a": {"kind": "fir", "dead": True, "close": 2, "reach": 1.0, "dz": 0.5, "spacing": 0.35, "gain": 1.0, "depth": 0.85, "skirt_top": 0.6,
                   "ragged": {"jit": 0.3, "droop": (15, 30), "len": (0.6, 1.0), "keep": 0.7, "thin_above": 0.5, "up_len": (0.4, 0.7), "alternate": True, "skirt_extra": ((-75, -105), (-68, -112)), "extra_len": 0.7, "skirt_cap": 0.9}},
    # Veruna grass tufts: a blade up every drawn stroke, blades strewn over
    # the clumps; calibrated over the render's own pixels
    "VerGrass01": {"kind": "grass", "height": 0.7, "density": 0.6, "width": 0.08, "calib_own": True, "cast": False},
    "VerGrass02": {"kind": "grass", "height": 0.7, "density": 0.5, "width": 0.08, "calib_own": True, "cast": False},
    "VerGrass03": {"kind": "grass", "height": 0.6, "density": 0.45, "width": 0.08, "calib_own": True, "cast": False},
    "VerGrass04": {"kind": "grass", "height": 0.7, "density": 0.6, "width": 0.08, "calib_own": True, "cast": False},
    "VerGrass05": {"kind": "grass", "height": 0.7, "density": 0.75, "width": 0.09, "calib_own": True, "cast": False},
    "VerGrass06": {"kind": "grass", "height": 0.7, "density": 0.75, "width": 0.09, "calib_own": True, "cast": False},
    # Zhon rosettes: narrow pointed leaves spiralling out of a dark heart
    "ZonPlant07": {"kind": "rosette", "leaves": 70, "segments": 5, "height": 0.85, "width": 0.34},
    "ZonPlant07a": {"kind": "rosette", "dead": True, "fragments": 70},
    "ZonPlant08": {"kind": "rosette", "leaves": 110, "segments": 5, "height": 1.0},
    "ZonPlant09": {"kind": "rosette", "leaves": 110, "segments": 5, "height": 1.0},
    # Taros bulbs: a ribbed dome with a skirt of spines, a red flower on top
    # trailing pale tendrils; bulb is the dome ground centre (col, row) and radius px
    "TarTree07": {"kind": "bulb", "bulb": (40, 62, 27), "flower": (48, 30), "spines": 80},
    "TarTree08": {"kind": "bulb", "bulb": (42, 67, 27), "flower": (37, 32), "spines": 80},
    "TarTree09": {"kind": "bulb", "bulb": (55, 72, 29), "flower": (37, 25), "spines": 80},
})

# dead trees (kit.build_limbs). Points are sprite pixels; "ground" lists the
# tips of legs and roots that rest on the ground, "body" a fibrous trunk as a
# ball ((col, row) of the centre, depth y, radii) with an optional neck,
# "balls" spiked root masses, "cut" circles left out of the skeleton, "twigs"
# the brush crowns. "sturdy" is (girth, height, limb radius) over the
# pixel-exact fit: tall ones widen and shorten most, wide ones little.
# snags: an upright trunk, its limbs swung toward and away from the camera
# along their pixels' rays so they spread all round
# limbs climb 30-45 degrees on their pixels' rays, mostly away from the
# camera, every other one mirrored toward it and all reaching 1.4 times
# their drawn length, and a few twigs added front and back, so the crown
# spreads wide all round over a heavy flared foot
SNAG = {"kind": "limbs", "bark": "speckle", "prune": (1.2, 0.6), "sides_min": 4, "sturdy": (1.2, 0.75, 1.2),
        "foot": {"r": 0.38, "flare": 0.5, "h": 1.2},
        "extra": {"front": 2, "back": 2, "z": (0.35, 0.8), "len": (1.0, 1.6), "elev": (30, 45), "r": 0.07},
        "lift": {"mode": "swing", "trunk": [0], "psi": (40, 65), "rscale": 1.05, "elev": (30, 45), "mirror": 0.6, "reach": 1.4}}
# the round-4 snags: limbs off the drawn rays (mirrored toward the camera,
# and all of them on 03) rebuilt as dead wood, climbing 30-45 degrees and
# bending at most 30 per step, their twigs forking 30-45 degrees toward the
# tip; limbs beyond the trunk at their own radius, tapering to half; a few
# more twigs all round in the upper half
STRAIGHT = {"elev": (30, 45), "bend": 30, "fork": (30, 45), "stretch": 1.3}
SNAG4 = dict(SNAG, lift=dict(SNAG["lift"], straighten=STRAIGHT), limb_taper={"radius": 1.0, "tip": 0.5},
             extra=[SNAG["extra"], {"az": [(35, 65), (-145, -115), (-100, -70), (150, 190)], "z": (0.5, 0.85),
                                    "len": (1.3, 1.5), "elev": (30, 40), "elev_post": True, "r": 0.06}])
# the Zhon forked snags keep their drawn forks near the screen plane
# buttress roots at the foot, the fork opened in depth above trunk_top
# buttress roots out of a widened foot, the prongs ending in splinters
ZSNAG = {"kind": "limbs", "bark": "mottle", "prune": (2.0, 1.0), "sturdy": (1.25, 0.78, 1.1),
         "roots": {"n": 6, "reach": 1.1, "buttress": True, "flare": 0.5},
         "lift": {"alpha": 0.4, "beta": 0.3, "rscale": 1.0}}
TENT = {"kind": "limbs", "bark": "camo", "rough": 1.0, "sturdy": (1.06, 0.94, 1.12), "close": 2, "sides_min": 6,
        "bole": {"smooth": True, "r": 0.55, "flare": 0.35, "knots": 3},
        "lift": {"mode": "balanced", "root_z": 1.5, "rise": 0.35, "p": 1.4, "rscale": 1.0, "rmax": 0.28, "taper": True}}
TENTACLE = {"kind": "limbs", "bark": "camo", "stem": 1.6, "sturdy": (1.06, 0.94, 1.12),
            "lift": {"mode": "rise", "trunk": [], "root_z": 1.6, "phi": (55, 10), "rmax": 0.45}}
HUB = {"kind": "limbs", "bark": "speckle", "sides_min": 6, "sturdy": (1.1, 0.9, 1.12), "smooth_r": True, "bulb_shade": 0.35, "spike_shade": 0.28,
       "lift": {"mode": "rise", "trunk": [0], "phi": (45, 15), "root_level": 2.0, "root_phi": (-15, -5),
                "rmax": 0.4}}
STILT = {"kind": "limbs", "bark": "speckle", "calib_parts": ("limbs", "twigs"), "sturdy": (1.1, 0.9, 1.12),
         "lift": {"mode": "rise", "trunk": [], "root_z": 1.5, "phi": (55, 30)}}
# the Zhon bulb trees: a fibrous bottle of a trunk in one piece, knobbly
# roots wandering to thin tips, thin crooked arms, a pale tuft seated in the top
BULB = {"kind": "limbs", "bark": "speckle", "sides_min": 4, "sturdy": (1.08, 0.92, 1.12), "arm_r": 0.13, "calib_parts": ("limbs",),
        "gnarl": {"knobs": 0.5, "wiggle": 0.14, "tip_thin": 0.6},
        "lift": {"mode": "rise", "trunk": [], "root_z": 1.2, "phi": (50, 20), "rmax": 0.5}}
DARK_TWIGS = (0.12, 0.11, 0.1)
PALE_TWIGS = (0.66, 0.66, 0.63)
TUFT_TWIGS = (0.6, 0.6, 0.57)
TUFT_DARK = (0.1, 0.1, 0.1)


def lifted(base, bulbs=None, twigs=None, body=None, balls=None, cut=None, **kw):
    d = dict(base)
    d["lift"] = dict(base["lift"])
    d["lift"].update(kw)
    for k, v in (("bulbs", bulbs), ("twigs", twigs), ("body", body), ("balls", balls), ("cut", cut)):
        if v:
            d[k] = v
    return d


def tuft(n=24, length=1.6, up=1.6, cards=40, **kw):
    return [dict({"seat": True, "n": n, "len": length, "rgb": TUFT_TWIGS, "dark": TUFT_DARK, "up": up, "r": 1.6,
                  "cards": cards}, **kw)]


MODELS.update({
    # Aramon dead oaks: alternate limbs swung toward and away from the
    # camera along their pixels' rays, so the crown spreads in depth
    # every drawn limb rising 10-40 degrees on its rays, plus rising limbs
    # toward and away from the camera so the crown spreads all round
    "AraTree04a": {"kind": "limbs", "bark": "speckle", "sides_min": 5, "sturdy": (1.08, 0.92, 1.0),
                   "lift": {"mode": "swing", "trunk": [0], "trunk_top": 7.5, "elev": (10, 40), "rmax": 0.8},
                   "extra": {"front": 3, "back": 3, "z": (0.45, 0.7), "len": (1.3, 2.0), "elev": (15, 35), "r": 0.2}},
    # main limbs behind, back-left and back-right, so it spreads in depth;
    # none toward the camera, where they read as a collar of stubs
    "AraTree06a": {"kind": "limbs", "bark": "speckle", "sides_min": 5, "sturdy": (1.08, 0.92, 1.0),
                   "lift": {"mode": "swing", "trunk": [0], "trunk_top": 6.5, "elev": (25, 40), "rmax": 0.8},
                   "extra": [{"az": [(138, 150), (140, 150)], "zabs": (3.4, 4.4), "len": (2.0, 2.2), "elev": (20, 28),
                              "elev_post": True, "kink": (18, 30), "fork_len": 0.55, "r": 0.25},
                             {"az": [(30, 35)], "zabs": (3.0, 3.2), "len": (2.6, 2.6), "elev": (20, 21), "elev_post": True, "kink": (18, 30), "fork_len": 0.55,
                              "r": 0.25},
                             {"az": [(38, 44)], "zabs": (3.3, 3.6), "len": (2.4, 2.5), "elev": (22, 25), "elev_post": True, "kink": (18, 30), "fork_len": 0.55,
                              "r": 0.24},
                             {"az": [(40, 48)], "zabs": (4.2, 4.5), "len": (1.7, 1.9), "elev": (28, 32), "elev_post": True, "kink": (18, 30), "fork_len": 0.55,
                              "r": 0.2}]},
    # Veruna snags
    "VerTreeDead01": dict(SNAG4),
    "VerTreeDead02": dict(SNAG),
    # its drawn roots running down become flares sloping into the ground
    "VerTreeDead03": dict(SNAG4, lift=dict(SNAG["lift"], straighten=dict(STRAIGHT, all=True), root_slope=0.8)),
    # Veruna stilt trees: three legs to the ground, a trunk up to a brush of twigs
    "VerTreeDead07": lifted(STILT, ground=[(11, 76), (57, 78), (57, 25)],
                            twigs=[{"at": (22, 12), "y": 0.0, "n": 30, "len": 1.2, "rgb": DARK_TWIGS, "r": 1.3}]),
    "VerTreeDead08": lifted(STILT, ground=[(2, 54), (52, 70), (13, 14)],
                            twigs=[{"at": (48, 12), "y": 0.0, "n": 30, "len": 1.2, "rgb": DARK_TWIGS, "r": 1.3}]),
    "VerTreeDead09": lifted(STILT, ground=[(1, 55), (55, 39), (35, 79)],
                            twigs=[{"at": (24, 10), "y": 0.0, "n": 30, "len": 1.2, "rgb": DARK_TWIGS, "r": 1.3}]),
    # Zhon bulb trees: a swollen trunk on root legs, a pale brush of twigs on top
    "ZonTreeDead01": lifted(BULB, ground=[(2, 83), (64, 111), (115, 69)], cut=[(68, 27, 17)],
                            body={"ball": ((62, 44), 0.0, (1.35, 1.25, 1.75))}, twigs=tuft(24, 1.6, 1.4)),
    "ZonTreeDead02": lifted(BULB, ground=[(2, 53), (24, 94), (84, 92)], cut=[(68, 20, 18)],
                            body={"ball": ((47, 41), 0.0, (1.35, 1.2, 1.15)),
                                  "neck": [((58, 30), 0.0, 0.85), ((64, 23), 0.0, 0.6)]}, twigs=tuft(24, 1.7, 1.4)),
    # a tuft the size of 01/02's on a neck run on a little higher
    "ZonTreeDead03": lifted(BULB, ground=[(1, 64), (66, 117), (102, 74)], cut=[(48, 18, 20)],
                            body={"ball": ((54, 55), 0.0, (1.2, 1.1, 1.3)),
                                  "neck": [((52, 40), 0.0, 0.8), ((50, 30), 0.0, 0.6), ((49, 27), 0.0, 0.5)]},
                            twigs=tuft(24, 1.7, 1.5, 40, trim=0.85, sink=0.1)),
    # Zhon forked snags
    # prongs refitted so their classic tips land on the drawn ones while
    # they stand f of the drawn height, leaning back a little
    "ZonTreeDead04": dict(lifted(ZSNAG, trunk_top=4.6), prong_fit={"branches": [0, 4], "f": 0.8, "x": 0.9, "r": 0.72, "keep": 0.6},
                          splinter={"branches": [0, 4, 6, 7]}),
    "ZonTreeDead05": dict(lifted(ZSNAG, trunk_top=5.0), prong_fit={"branches": [0, 4], "f": 0.75, "x": 0.9, "r": 0.72, "keep": 0.6},
                          splinter={"branches": [0, 4, 7, 10]}),
    # the thick side branch eased into the trunk over a cell
    "ZonTreeDead06": dict(lifted(ZSNAG, trunk_top=5.4, branch={3: {"thick": 1.5, "blend": 1.0}}),
                          prong_fit={"branches": [0, 3], "f": 0.85, "x": 0.9, "r": 0.72, "keep": 0.2}, splinter={"branches": [0, 3, 6]}),
    # Zhon root-ball trees: grey limbs fanning from the top of a trunk over a
    # black ball of roots
    "ZonTreeDead201": lifted(HUB, trunk_top=4.5, balls=[{"at": ((38, 74), -0.4, (1.3, 1.25, 1.25)), "spikes": 34, "spike_len": 1.35}]),
    "ZonTreeDead202": lifted(HUB, trunk_top=4.5, balls=[{"at": ((52, 78), -0.4, (1.3, 1.2, 1.2)), "spikes": 34, "spike_len": 1.35}]),
    "ZonTreeDead203": lifted(HUB, trunk_top=4.5, balls=[{"at": ((55, 77), -0.4, (1.25, 1.15, 1.15)), "spikes": 34, "spike_len": 1.35}]),
    # Taros tentacle trees: round arms spreading evenly all round from the
    # top of a knotted bole at a low rise, their tips curling up
    "TarTree01": tree(TENT, lift=dict(TENT["lift"], R=4.5)),
    "TarTree02": tree(TENT, lift=dict(TENT["lift"], R=5.0)),
    "TarTree03": tree(TENT, lift=dict(TENT["lift"], R=5.0)),
    # the bleached ones: finer arms, grained like driftwood
    "TarTree04": tree(lifted(TENTACLE, root_z=2.5, phi=(55, 5), rmax=0.3), bark="streak"),
    "TarTree05": tree(lifted(TENTACLE, root_z=2.5, phi=(55, 5), rmax=0.3), bark="streak"),
    "TarTree06": tree(lifted(TENTACLE, root_z=2.5, phi=(55, 5), rmax=0.3), bark="streak"),
})


# Round 5 builds AraTree03, VerTree01-03, AraTree07a, AraTree08a and
# ZonTreeDead04-06 from their own scripts in bespoke/, which build.py hands
# them to; their settings above are the kit's round-4 ones, kept for reference.

# the family in the order of the job list, for the contact sheets
ORDER = (
    "AraTree07a", "AraTree08a", "AraTree09a", "AraTree10a", "VerTreeDead07", "VerTreeDead08", "VerTreeDead09",
    "ZonTreeDead01", "ZonTreeDead02", "ZonTreeDead03", "AraTree04a", "AraTree06a", "VerTreeDead01", "VerTreeDead02",
    "VerTreeDead03", "ZonTreeDead04", "ZonTreeDead05", "ZonTreeDead06", "ZonTreeDead201", "ZonTreeDead202",
    "ZonTreeDead203", "AraTree01", "AraTree02", "AraTree03", "AraTree04", "AraTree05", "AraTree06", "AraTree07",
    "AraTree08", "AraTree09", "AraTree10", "VerTree01", "VerTree02", "VerTree03", "ZonTree04", "ZonTree05", "ZonTree06",
    "ZonTree201", "ZonTree202", "ZonTree203", "ZonPlant07", "ZonPlant07a", "ZonPlant08", "ZonPlant09", "VerGrass01",
    "VerGrass02", "VerGrass03", "VerGrass04", "VerGrass05", "VerGrass06", "TarTree01", "TarTree02", "TarTree03",
    "TarTree04", "TarTree05", "TarTree06", "TarTree07", "TarTree08", "TarTree09")
