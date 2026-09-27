"""NPCTHESH, the Thesh stand: a carved rosette pedestal of speckled
granite. A ring of radial blocks in pink granite makes the lower tier, a
short near-upright rim rising into a sloped shoulder, their joints deep
and dark; a dark column carries a round slab of cooler grey-brown granite
whose face is cut with fine, irregular, low radial flutes converging on a
soft darker hub.

    blender -b --factory-startup --python tools/sprite-replace/lodes/NPCTHESH.py
"""
import math
import os
import random
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import misckit as mk  # noqa: E402
from misckit import hk  # noqa: E402

NAME = "NPCTHESH"
PIC = r"D:\OKReplace\lodes\sprites\NPCTHESH.png"
HOT = (31, 45)

hk.reset()
# granite: saturated pink, rose, grey-green and deep red grains with
# near-black flecks, lifted to salmon and cream where the tops catch the
# light; the joints, grooves and column near-black
SALMON, CREAM = (228, 158, 118), (222, 212, 168)
PINK, ROSE, GREYGREEN, DKRED = (162, 70, 42), (112, 56, 36), (86, 88, 54), (80, 24, 14)
BROWN, OLIVE = (86, 56, 34), (72, 68, 40)
BLACK, SOOT = (17, 8, 5), (32, 16, 10)
GRAINS = (PINK, ROSE, BROWN, OLIVE, GREYGREEN, DKRED, BROWN)
# the slab's face: grey-brown with olive, grey, lavender-grey and cream
# grains, pink only as a speckle
T_BASE, T_DKBR, T_OLIVE, T_GREY = (104, 84, 62), (58, 46, 32), (108, 110, 76), (146, 146, 140)
T_LAV, T_CREAM, T_PINK, T_FLECK = (130, 126, 138), (196, 184, 144), (208, 134, 102), (36, 28, 20)
TOP_GRAINS = (T_BASE, T_BASE, T_BASE, T_DKBR, T_DKBR, T_OLIVE, T_OLIVE, T_GREY, T_LAV, T_CREAM)
GROOVE, GROOVE_LO = (86, 72, 52), (52, 42, 30)
HUB_C = (70, 58, 42)


def top_granite(seed, bias=None, k=0.0):
    """The slab face's granite: one grain per face, unblended, so the
    speckle stays crisp; dark flecks and a pink speckle among them."""
    r = random.Random(seed)
    cache = {}

    def fn(p, co, n, li):
        key = p.index
        if key not in cache:
            u = r.random()
            c = r.choice(TOP_GRAINS)
            if u < 0.1:
                c = T_FLECK
            elif u < 0.25:
                c = mk.mixc(c, T_PINK, r.uniform(0.6, 1.0))
            if bias is not None and u >= 0.1:
                c = mk.mixc(c, bias, k)
            cache[key] = c
        return cache[key]
    return fn
stone = mk.vmat("nt_stone", (240, 222, 190), rough=0.85)
dark = mk.vmat("nt_dark", (80, 56, 46), rough=0.95)
# matt granite: little sheen, so the joints and the shaded flank stay dark
for m in (stone, dark):
    m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.2

parts = []
rnd = random.Random(3)

Z_CAP0, Z_CAP1 = 2.52, 3.06  # the slab
R_RIM = 1.57  # the lower tier's outer rim, which sets the outline
Z_RIM = 0.48  # top of the upright rim
R_NECK, Z_SH = 0.82, 1.22  # where the shoulder meets the column, about 47 degrees up


def granite(seed, tones, crest=(SALMON, CREAM), lift=1.0, flecks=0.12, pale=0.05, speckle=None):
    """Per corner granite: each vertex a grain mixed from two of tones,
    some near-black flecks and a few pale ones, lifted toward a salmon or
    cream crest on faces turned up, darkened on faces turned down."""
    r = random.Random(seed)
    cache = {}

    def grain():
        u = r.random()
        c = mk.mixc(r.choice(tones), r.choice(tones), r.random())
        if u < flecks:
            c = mk.mixc(c, BLACK, r.uniform(0.65, 0.95))
        elif u > 1.0 - pale:
            c = mk.mixc(c, CREAM, 0.7)
        elif speckle and u < flecks + speckle[1]:
            c = mk.mixc(c, speckle[0], r.uniform(0.6, 0.9))
        return c

    def fn(p, co, n, li):
        key = (round(co.x, 4), round(co.y, 4), round(co.z, 4))
        if key not in cache:
            cache[key] = (grain(), r.random())
        g, u = cache[key]
        c = mk.mixc(g, grain(), 0.2)
        if crest and n.z > 0.45 and u > 0.12:
            c = mk.mixc(c, crest[0] if u < 0.72 else crest[1], min(1.0, (n.z - 0.45) * 1.6) * lift * (0.55 + 0.45 * u))
        elif crest and n.z < 0.3:
            c = mk.mixc(c, BLACK, 0.3 * (0.3 - n.z))  # the upright faces a shade darker
        if n.z < -0.3:
            c = mk.mixc(c, BLACK, 0.75)
        return c
    return fn


def block(a0, a1, prof, sink=0.07, steps=8, name="block"):
    """One radial block of the lower tier: the closed (r, z) outline swept
    from a0 to a1 degrees, each outline point pulled in along its own
    outward normal toward the block's sides, so the top rounds over like a
    cushion and the joints open deep."""
    m = len(prof)
    nrm = []
    for j in range(m):
        (r0, z0), (r1, z1) = prof[j - 1], prof[(j + 1) % m]
        tr, tz = r1 - r0, z1 - z0
        L = math.hypot(tr, tz) or 1.0
        nrm.append((tz / L, -tr / L))  # outward, for an anticlockwise outline in (r, z)
    verts, faces = [], []
    for k in range(steps + 1):
        t = k / steps
        a = math.radians(a0 + (a1 - a0) * t)
        e = abs(2 * t - 1)
        pull = sink * (max(0.0, (e - 0.35) / 0.65) ** 1.6)
        for j, (r, z) in enumerate(prof):
            rr, zz = r - nrm[j][0] * pull, z - nrm[j][1] * pull
            if z < 0.01:
                zz = z
            verts.append((rr * math.cos(a), rr * math.sin(a), zz))
    for k in range(steps):
        c0, c1 = k * m, (k + 1) * m
        for j in range(m):
            j2 = (j + 1) % m
            faces.append((c0 + j, c1 + j, c1 + j2, c0 + j2))
    faces.append(tuple(range(m - 1, -1, -1)))
    faces.append(tuple(steps * m + j for j in range(m)))
    ob = mk.mesh(name, verts, faces, mat=stone)
    mk._fix_normals(ob)
    return ob


def tier_profile(h, reach):
    """A block's outline, anticlockwise in (r, z): up the short rim, over
    the rounded corner, up the shoulder to the column, down the inside and
    back along the ground."""
    zr = Z_RIM * h
    zs = Z_SH * h
    rs = [(R_RIM - 0.02, 0.0), (R_RIM + 0.01, zr * 0.35), (R_RIM, zr * 0.75), (R_RIM - 0.03, zr),
          (R_RIM - 0.1, zr + 0.08)]
    for f in (0.2, 0.42, 0.64, 0.84):
        rr = R_RIM - 0.1 + (R_NECK + reach - (R_RIM - 0.1)) * f
        zz = zr + 0.08 + (zs - zr - 0.08) * f + 0.03 * math.sin(math.pi * f)  # a slight swell
        rs.append((rr, zz))
    rs += [(R_NECK + reach, zs), (R_NECK - 0.02, zs - 0.06), (R_NECK - 0.04, 0.3), (1.2, 0.0)]
    return rs


# the lower tier: eighteen radial blocks of varied width
N_BLOCKS = 18
widths = [rnd.uniform(0.82, 1.18) for _ in range(N_BLOCKS)]
tot = sum(widths)
a = rnd.uniform(0, 20)
for k, w in enumerate(widths):
    span = 360.0 * w / tot
    gap = rnd.uniform(3.6, 5.0)
    prof = tier_profile(rnd.uniform(0.96, 1.05), rnd.uniform(0.0, 0.06))
    blk = block(a + gap / 2, a + span - gap / 2, prof, sink=rnd.uniform(0.1, 0.13), steps=8, name="block%d" % k)
    mk.jitter(blk, 0.012, seed=k, axes=(1, 1, 0.5))
    bias = rnd.choice(((PINK,), (ROSE,), (GREYGREEN,), (PINK,)))
    gfn = granite(100 + k, GRAINS + bias * 2, lift=rnd.uniform(0.9, 1.15))

    def blk_colour(p, co, n, li, gfn=gfn):
        # shade gathers at the foot of the rim and up against the column
        c = gfn(p, co, n, li)
        ang = math.atan2(co.y, co.x)
        side = abs(n.x * -math.sin(ang) + n.y * math.cos(ang))  # turned toward the next block
        c = mk.mixc(c, BLACK, min(0.9, max(0.0, side - 0.55) * 3.0))
        if n.z < 0.35:
            c = mk.mixc(c, BLACK, 0.25)  # the upright rim, below the lit shoulder
        c = mk.mixc(c, BLACK, 0.85 * max(0.0, 1.0 - co.z / 0.2) ** 0.8)
        # the right flank lies in the slab's shadow
        c = mk.mixc(c, BLACK, 0.68 * max(0.0, math.cos(ang - math.radians(30))) ** 1.5)
        return mk.mixc(c, BLACK, 0.6 * max(0.0, 1.0 - (co.xy.length - R_NECK) / 0.3))
    mk.vpaint(blk, blk_colour)
    parts.append(hk.smooth(blk, 50))
    a += span

# the core under the blocks and the column above them, near-black so every
# joint opens into shadow; the column is necked at its foot and swells into
# a capital under the slab
core = mk.lathe([(0.0, 0.0), (R_RIM - 0.08, 0.0), (R_RIM - 0.1, Z_RIM - 0.04), (R_RIM - 0.3, Z_RIM + 0.14),
                 (R_NECK - 0.02, Z_SH - 0.1), (R_NECK - 0.02, Z_SH + 0.02), (R_NECK - 0.09, Z_SH + 0.08),
                 (R_NECK - 0.1, 1.7), (R_NECK - 0.07, 2.1), (R_NECK - 0.02, 2.3), (R_NECK + 0.08, Z_CAP0 - 0.08),
                 (R_NECK + 0.16, Z_CAP0 - 0.02), (R_NECK + 0.18, Z_CAP0 + 0.02), (0.0, Z_CAP0 + 0.02)],
                seg=36, mat=dark, name="core")
mk.jitter(core, 0.01, seed=9, axes=(1, 1, 0.3))
cr = random.Random(10)


def core_colour(p, co, n, li):
    # darkest under the slab, a little dark red granite showing lower down
    c = mk.mixc(BLACK, (70, 40, 32), cr.random() ** 2.5)
    return mk.mixc(c, BLACK, max(0.0, (co.z - 1.9) / 0.6))


mk.vpaint(core, core_colour)
parts.append(hk.smooth(core, 35))
# an astragal ring at the column's foot
for k, (z, rr) in enumerate(((Z_SH + 0.02, R_NECK + 0.0),)):
    ring = mk.lathe([(rr - 0.1, z - 0.05), (rr, z - 0.04), (rr + 0.03, z), (rr, z + 0.04), (rr - 0.1, z + 0.05)],
                    seg=36, mat=dark, name="astragal%d" % k)
    mk.vpaint(ring, lambda p, co, n, li: mk.mixc((22, 15, 12), (70, 42, 32), max(0.0, n.z) * 0.4 + cr.random() * 0.3))
    parts.append(hk.smooth(ring, 40))

# the slab: a thick round stone, its side speckled like the blocks, its top
# edge a smooth round
R_SLAB = 1.25
seg = 60
sverts, sfaces = [], []
rs = random.Random(7)
edge = [R_SLAB + rs.uniform(-0.008, 0.008) for _ in range(seg)]
rings = ((Z_CAP0, 0.9), (Z_CAP0 + 0.05, 0.97), (Z_CAP0 + 0.16, 1.0), (Z_CAP1 - 0.2, 1.01), (Z_CAP1 - 0.05, 1.0))
for (z, f) in rings:
    for j in range(seg):
        a = 2 * math.pi * j / seg
        rr = edge[j] * f
        sverts.append((rr * math.cos(a), rr * math.sin(a), z + rs.uniform(-0.012, 0.012)))
for i in range(len(rings) - 1):
    for j in range(seg):
        j2 = (j + 1) % seg
        sfaces.append((i * seg + j, i * seg + j2, (i + 1) * seg + j2, (i + 1) * seg + j))
# the slab's top between the flutes, dipping a touch to the hub, in close
# rings so its grain stays fine
Z_TOP = Z_CAP1 - 0.05
inner = []
for rf in (0.9, 0.8, 0.71, 0.62, 0.53, 0.44, 0.36, 0.28, 0.2, 0.13, 0.06):
    dz = -0.012 * max(0.0, 1.0 - rf / 0.45)
    base = len(sverts)
    for j in range(seg):
        a = 2 * math.pi * j / seg
        sverts.append((R_SLAB * rf * math.cos(a), R_SLAB * rf * math.sin(a), Z_TOP + dz))
    inner.append(base)
prev = (len(rings) - 1) * seg
for base in inner:
    for j in range(seg):
        j2 = (j + 1) % seg
        sfaces.append((prev + j, prev + j2, base + j2, base + j))
    prev = base
sfaces.append(tuple(prev + j for j in range(seg)))
sfaces.append(tuple(range(seg - 1, -1, -1)))
slab = mk.mesh("slab", sverts, sfaces, mat=stone)
mk._fix_normals(slab)
slab_side = granite(200, GRAINS, crest=None, flecks=0.14)
gr = random.Random(201)


groove_c = {}


def slab_colour(p, co, n, li):
    if n.z > 0.7:
        # the grooves between the flutes, mid-dark brown, one grain a face;
        # the hub a soft, darker brown where they converge
        if p.index not in groove_c:
            c = mk.mixc(GROOVE_LO, GROOVE, gr.random() ** 0.7)
            u = gr.random()
            if u < 0.15:
                c = mk.mixc(c, T_OLIVE, 0.6)
            elif u < 0.22:
                c = mk.mixc(c, T_PINK, 0.4)
            groove_c[p.index] = c
        return mk.mixc(groove_c[p.index], HUB_C, max(0.0, 1.0 - p.center.xy.length / 0.5) * 0.7)
    c = slab_side(p, co, n, li)
    return mk.mixc(c, BLACK, 0.6 * max(0.0, 1.0 - (co.z - Z_CAP0) / 0.22))


mk.vpaint(slab, slab_colour)
parts.append(hk.smooth(slab, 40))

# the flutes: many fine, low radial ridges of uneven width, spacing and
# length, some starting further out, none running over the slab's edge
ARCH = [(-1.0, 0.0), (-0.86, 0.3), (-0.68, 0.62), (-0.42, 0.88), (-0.14, 1.0), (0.14, 1.0), (0.42, 0.88),
        (0.68, 0.62), (0.86, 0.3), (1.0, 0.0)]
n_ribs = 33
fl = random.Random(21)
spacing = [fl.uniform(0.6, 1.4) for _ in range(n_ribs)]
ang = fl.uniform(0, 2 * math.pi)
for k in range(n_ribs):
    ang += 2 * math.pi * spacing[k] / sum(spacing)
    ca, sa = math.cos(ang), math.sin(ang)
    px, py = -sa, ca
    r0 = fl.uniform(0.2, 0.32) if fl.random() < 0.7 else fl.uniform(0.4, 0.7)
    r1 = R_SLAB - fl.uniform(0.04, 0.08)
    h = fl.uniform(0.03, 0.05)
    w0, w1 = fl.uniform(0.024, 0.036), fl.uniform(0.055, 0.095)
    bend = fl.uniform(-0.04, 0.04)
    path, scales = [], []
    for i in range(13):
        t = i / 12
        r = r0 + (r1 - r0) * t
        off = bend * math.sin(math.pi * t) + 0.008 * math.sin(7 * t + k)
        # swelling out of the hub, sinking back into the slab at the edge
        rise = min(1.0, t / 0.15) * min(1.0, (1.0 - t) / 0.12 + 0.15)
        path.append((r * ca + off * px, r * sa + off * py, Z_TOP - 0.006))
        scales.append((w0 + (w1 - w0) * t ** 0.8, h * rise * fl.uniform(0.85, 1.1) + 0.006))
    rib = mk.sweep(path, ARCH, side=(0, 0, 1), scales=scales, mat=stone, name="rib%d" % k)
    mk.jitter(rib, 0.004, seed=400 + k)
    # each flute leans to a tone of its own, so the face shows broad
    # streaks of grey, cream, olive and a few pink ones
    tone = fl.choice((T_GREY, T_CREAM, T_OLIVE, T_OLIVE, T_LAV, T_DKBR, T_DKBR, T_PINK, None))
    rfn = top_granite(420 + k, tone, fl.uniform(0.3, 0.5))

    def rib_colour(p, co, n, li, rfn=rfn, px=px, py=py):
        # the flute's flanks fall into the brown grooves either side
        c = rfn(p, co, n, li)
        return mk.mixc(c, GROOVE_LO, min(0.5, max(0.0, abs(n.x * px + n.y * py) - 0.4) * 1.4))
    mk.vpaint(rib, rib_colour)
    parts.append(hk.smooth(rib, 55))

mk.tidy(parts)
mk.whiten(parts)
parts[0].name = NAME  # the joined object, and the glTF node, take this name
ob = hk.finish(parts, r"D:\OKReplace\lodes\hand\models\NPCTHESH.glb",
               {"replacesTexture": "theshstand", "replacesPiece": "base"})
print("MISCKIT_TRIS", NAME, mk.tris(ob))
# plain display transform, as for the other misc models
bpy.context.scene.view_settings.view_transform = "Standard"
hk.renders(ob, r"D:\OKReplace\lodes\hand\renders", NAME, PIC, HOT, scale=4)
