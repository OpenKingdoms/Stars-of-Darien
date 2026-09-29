"""Parameters for each model of the Zhon structures family."""
MODELS = {}

MOUND = dict(body=(64, 54, 37), body2=(78, 66, 46), dark=(44, 37, 26), dark2=(58, 50, 35), cap=(118, 98, 68),
             sand=(88, 77, 52), sand2=(72, 63, 43), lean=0.03, fall=0.3, vary=0.75, sr=0.28, knobs=0.4, core=0.62, core_mat="body2", inner_knobs=0.3, flutes=0, flute_keep=0.85, ring=0.52, inner=0.88, flare=1.85, seg=6,
             apron=1.15)


def mound(name, **p):
    MODELS[name] = dict(kind="termite_mound", **dict(MOUND, **p))


mound("ZonTmound01", tone=0.88, h=9.2, r=1.25, cr=1.35, ar=1.84, ary=1.80, ah=1.0, n=48, sr=0.19, seg=5, knobs=0.5, clods=2, seed=11)
mound("ZonTmound02", tone=0.92, h=6.8, r=0.95, cr=1.05, cx=0.15, ar=1.4, ary=1.40, ah=0.8, n=30, sr=0.17, seg=5, knobs=0.5, seed=12)
mound("ZonTmound03", tone=0.94, h=8.1, r=1.1, cr=1.2, ar=1.6, ary=1.57, ah=0.9, n=34, sr=0.17, seg=5, knobs=0.5, seed=13)
mound("ZonTmound04", tone=0.86, h=7.75, r=1.15, cr=1.25, cx=-0.05, ar=1.55, ary=1.45, ah=0.9, n=34, sr=0.17, seg=5, knobs=0.5, seed=14)
mound("ZonTmound05", tone=0.88, h=10.1, r=1.4, cr=1.5, ar=1.9, ary=1.89, ah=1.1, n=50, sr=0.19, seg=5, knobs=0.5, clods=2, seed=15)
mound("ZonTmound06", tone=0.95, h=5.9, r=0.8, cr=0.9, cx=0.06, ar=1.2, ary=1.20, ah=0.7, n=27, sr=0.16, seg=5, knobs=0.5, core=0.58, seed=16)


HUT = dict(kind="thatch_hut", wall=(92, 78, 58), base=(72, 62, 48), straw=(88, 72, 27), straw2=(68, 55, 21),
           teeth=90, tooth=0.3)


def hut(name, **p):
    MODELS[name] = dict(HUT, **p)


hut("ZonHut09", rw=2.1, wh=1.9, wcy=-0.15, za=3.6, door=(0.0, 0.9),
    tiers=[(2.3, 1.85), (1.55, 2.55), (0.8, 3.1)], seed=9)
hut("ZonHut10", rw=2.0, wh=1.9, wcx=0.0, wcy=0.0, za=3.7, door=(-90.0, 0.9),
    tiers=[(2.35, 1.95), (1.55, 2.6), (0.8, 3.15)], seed=10)
hut("ZonHut11", rw=2.05, wh=1.9, wcy=-0.05, za=3.6, door=(180.0, 0.9),
    tiers=[(2.35, 1.95), (1.55, 2.6), (0.8, 3.1)], seed=11)


TENT = dict(kind="hide_tent", hide=(98, 76, 44), hide2=(84, 65, 38), rope=(62, 63, 62), bulge=0.32, dome=0.18,
            wrinkle=0.14)


def tent(name, **p):
    MODELS[name] = dict(TENT, **p)


tent("ZonHut06", ring=(0.55, 0.2, 2.2, 0.9),
     feet=[(0.38, 3.2), (-1.9, 2.1), (-2.2, -0.66), (0.06, -2.24), (2.3, -0.5), (2.25, 1.8)],
     porch=dict(between=(2, 3), apex=(-1.4, -2.0, 1.1), stakes=[(-2.6, -2.2), (-1.1, -3.5)]), seed=6)
tent("ZonHut08", ring=(0.06, 0.4, 2.2, 0.9),
     feet=[(0.9, 3.2), (-1.66, 2.9), (-2.7, 1.0), (-1.0, -1.9), (1.4, -1.75), (2.56, 1.1)],
     porch=dict(between=(3, 4), apex=(0.4, -2.4, 1.1), stakes=[(-0.37, -3.4), (1.5, -3.1)]), seed=8)


PAL = dict(kind="palisade", r=0.44, stagger=0.35, post_blades=12, skull=0.3, gain=0.85, body=(17, 14, 11), head=(56, 38, 23),
           band=(62, 38, 26))


def pal(name, **p):
    MODELS[name] = dict(PAL, **p)


pal("TarPRWall01", axis="y", a0=-2.8, a1=2.8, c=0.1, n=8, h=4.65, seed=21, stagger=0.22,
    skulls=[(1, 2.0, -1), (3, 2.5, 1), (4, 1.8, -1), (4, 2.4, -1), (6, 2.9, 1)])


def stumps(n, seed, lo=0.34, hi=0.5, **special):
    """Per-post specs for a broken run: stumps keeping their heads, with
    special {index: (k, lean_x, lean_y)} for posts still standing or gone."""
    import random
    rng = random.Random(seed)
    out = [(rng.uniform(lo, hi), rng.uniform(-0.06, 0.06), rng.uniform(-0.06, 0.06)) for _ in range(n)]
    for k, v in special.items():
        out[int(k[1:])] = v
    return out


BROKEN = dict(stump_blades=2, blades=8, char=0.55, debris=(140, 3.8, 1.3))
# the long runs: stumps knocked askew, heads of every size, some splintered
RAGGED = dict(BROKEN, tip=(0.34, 5, 20), hvary=0.25, stump_blades=2, blades=6, debris=(160, 3.8, 1.5), char=0.5)

pal("TarPRWall02", axis="y", a0=-2.8, a1=2.8, c=0.2, n=8, h=4.1, seed=22, stagger=0.22,
    skulls=[(0, 2.0, -1), (0, 2.5, -1), (2, 1.9, -1), (3, 1.6, 1), (6, 2.3, -1)])
pal("TarPRWall03", axis="y", a0=-5.7, a1=5.7, c=0.1, n=15, h=4.75, seed=23, stagger=0.22,
    skulls=[(2, 2.2, 1), (5, 2.0, -1), (6, 2.5, -1), (8, 1.9, -1), (9, 2.3, 1), (12, 2.0, -1)])
pal("TarPRWall01a", axis="y", a0=-3.7, a1=2.7, c=0.0, n=8, h=5.0, seed=31, stagger=0.2,
    posts=stumps(8, 31), ground_skulls=[(-0.6, -0.3), (-0.75, -1.5), (0.6, 1.6)],
    skulls=[(4, 1.5, -1), (6, 1.7, 1)], splinter=(2, 5), **RAGGED)
pal("TarPRWall02a", axis="y", a0=-3.7, a1=2.5, c=0.0, n=8, h=5.0, seed=32, stagger=0.2,
    posts=stumps(8, 32, p7=(0.58, 0.0, 0.0)), ground_skulls=[(-0.55, 0.9), (-0.5, -1.9), (0.9, -2.8)],
    skulls=[(5, 1.6, -1), (7, 2.4, 1)], splinter=(1, 4), **dict(RAGGED, debris=(230, 3.8, 1.5)))
pal("TarPRWall03a", axis="y", a0=-7.1, a1=4.6, c=0.1, n=15, h=5.1, seed=33, stagger=0.2,
    posts=stumps(15, 33, p14=(0.95, 0.0, 0.0), p6=(-0.3, 0, 0)),
    ground_skulls=[(-0.6, 3.4), (-0.7, -3.4), (-0.4, -4.4), (-0.4, -6.6)],
    skulls=[(14, 3.4, -1), (4, 1.5, 1), (9, 1.6, 1)], splinter=(2, 7, 10, 12),
    fallen=[(-0.95, 3.1, -52, 2.8, 2.1)], **dict(RAGGED, debris=(260, 6.2, 1.5)))
pal("TarPRWall04a", axis="x", r=0.38, xs=[-2.6, -1.7, -0.9, 0.06, 0.91, 1.94, 2.63], c=-0.3, n=7, h=6.2, seed=34,
    posts=[(0.36, 0, 0), (0.31, 0, 0), (0.41, 0, 0), (1.0, 0, 0), (0.43, 0, 0), (0.32, 0, 0), (0.85, 0, 0)],
    skulls=[(3, 3.9, -1), (3, 3.3, -1)], ground_skulls=[(-2.75, -1.0), (1.56, -1.0)], **BROKEN)
pal("TarPRWall05a", axis="x", r=0.38, xs=[-2.6, -1.66, -0.85, 0.06, 1.75, 2.63], c=-0.3, n=6, h=6.2, seed=35,
    posts=[(0.3, 0, 0), (0.44, 0, 0), (0.25, 0, 0), (0.74, -0.1, 0.0), (0.31, 0, 0), (0.38, 0, 0)],
    fallen=[(-1.7, -1.0, 5, 1.8), (0.3, -1.1, -5, 1.6)], ground_skulls=[(-2.0, -1.0), (2.2, -0.9)], **BROKEN)
# the west end leans over, pointed heads still on; short blades line the foot
pal("TarPRWall06a", axis="x", r=0.38, a0=-5.5, a1=6.2, c=-0.3, n=16, h=6.2, seed=36,
    posts=stumps(16, 36, lo=0.29, hi=0.43, p0=(0.4, -0.47, 0.0), p1=(0.44, -0.58, 0.02), p2=(0.42, -0.4, 0.0),
                 p3=(0.4, -0.36, 0.0), p12=(1.0, 0.0, 0.0), p5=None, p9=(-0.3, 0, 0)),
    skulls=[(12, 3.6, -1)], ground_skulls=[(-4.9, -0.6), (-2.3, -0.5), (0.0, 0.3)],
    fallen=[(-2.6, -0.2, 0, 2.2)], tip=(0.4, 4, 12), hvary=0.3, stump_blades=0, post_blades=4, blades=0,
    base_blades=28, char=0.55, debris=(260, 7.5, 1.4))


# a darker earth bed and dark stakes: the sprite's gaps between the stones are near black
PILE = dict(kind="barricade_remains", bed=(56, 52, 36), wood2=(36, 26, 18))


def pile(name, **p):
    MODELS[name] = dict(PILE, **p)


for i, n in enumerate(("ZonWall01a", "ZonWall02a", "ZonWall03a", "ZonWall04a", "ZonWall05a", "ZonWall06a", "ZonWall07a",
                       "ZonWall08a")):
    pile(n, seed=40 + i)
# a dense dark tangle: faceted boulders packed in layers, stakes crossing the
# middle and pale straw over the earth
MODELS["ZonWall08a"].update(pack=0.18, stone=((38, 38, 34), (74, 72, 64)), cross_stakes=7, heap=0.5,
                            bed=(46, 40, 29), wood=(66, 44, 30), straw=(150, 130, 90), grass=(150, 130, 90), straw_bits=60, tufts=26,
                            tuft_blades=2, chips=40,
                            add_boulders=[dict(x=0.2, y=0.3, r=0.36, lum=50), dict(x=-0.4, y=-0.4, r=0.32, lum=50),
                                          dict(x=0.7, y=-0.6, r=0.3, lum=50), dict(x=-0.1, y=0.9, r=0.3, lum=50),
                                          dict(x=1.4, y=-0.5, r=0.28, lum=50)])


import temples  # noqa: E402

MODELS.update(temples.models())

# The sturdiness pass (girth, height) against the pixel-exact fit: the classic
# picture draws height at half scale, so tall things fitted to it stand
# spindly in 3D. Temples, the tomb and the barricade heaps are broad and low
# and keep their fitted size.
STURDY = {"termite_mound": (1.18, 0.86), "palisade": (1.16, 0.88), "thatch_hut": (1.03, 0.9),
          "hide_tent": (1.0, 0.92)}
for _p in MODELS.values():
    _p.setdefault("sturdy", STURDY.get(_p["kind"], (1.0, 1.0)))
