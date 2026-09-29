"""Part lists for the Zhon temples and the Kandran tomb, read off the sprites.

Cells about the anchor, x east, y north, z up. The classic view shows only
tops and south faces: a sprite row is hy - 16y - 8z. The ruins are the
intact temple with blocks taken away, heaps of rubble and strewn ashlar.
"""


def P(name, **kw):
    return (name, kw)


def court(x0, x1, y0, y1, z, rim=1.1, ledge=0.6, spine=None, slots=(), zr=0.6, open_front=None):
    """The sunken court on top of the upper platform: a raised rim, an inner
    ledge, a spine running north-south and slots sunk a cell into the floor
    beside it, floored with dark stone."""
    out = []
    zt = z + zr
    front = [(x0, x1)] if open_front is None else [(x0, open_front[0]), (open_front[1], x1)]
    for a, c in front:
        out.append(P("fill", x0=a, x1=c, y0=y0, y1=y0 + rim, z0=z, z1=zt, bl=1.4, bd=rim, bh=1, moss=0.08))
    out += [P("fill", x0=x0, x1=x1, y0=y1 - rim, y1=y1, z0=z, z1=zt, bl=1.5, bd=rim, bh=1, moss=0.08),
            P("fill", x0=x0, x1=x0 + rim, y0=y0 + rim, y1=y1 - rim, z0=z, z1=zt, bl=rim, bd=1.5, bh=1, moss=0.1),
            P("fill", x0=x1 - rim, x1=x1, y0=y0 + rim, y1=y1 - rim, z0=z, z1=zt, bl=rim, bd=1.5, bh=1, moss=0.1)]
    xi0, xi1, yi0, yi1 = x0 + rim, x1 - rim, y0 + rim, y1 - rim
    zl = z + zr * 0.5
    out += [P("fill", x0=xi0, x1=xi0 + ledge, y0=yi0, y1=yi1, z0=z, z1=zl, bl=ledge, bd=1.2, bh=1, moss=0.15),
            P("fill", x0=xi1 - ledge, x1=xi1, y0=yi0, y1=yi1, z0=z, z1=zl, bl=ledge, bd=1.2, bh=1, moss=0.15),
            P("fill", x0=xi0 + ledge, x1=xi1 - ledge, y0=yi1 - ledge, y1=yi1, z0=z, z1=zl, bl=1.2, bd=ledge, bh=1,
              moss=0.15)]
    for sx0, sx1, sy0, sy1 in slots:
        out.append(P("pit", x0=sx0, x1=sx1, y0=sy0, y1=sy1, z=z, depth=1.0))
    if spine:
        sx0, sx1, sy0, sy1 = spine
        out.append(P("fill", x0=sx0, x1=sx1, y0=sy0, y1=sy1, z0=z, z1=zt, bl=(sx1 - sx0) / 2, bd=1.1, bh=1, moss=0.08))
    return out


def stepped(xin, xout, yback, yfront, ztop, zbot, n):
    """A stepped corner of the plinth: n steps falling from (xin side, yback)
    toward the front and outward edge, each a solid block."""
    out = []
    for k in range(n):
        t = k / n
        yf = yback + (yfront - yback) * (k + 1) / n
        z = ztop + (zbot - ztop) * t
        # each step reaches back to yback and in to xin; outward it stops short as it falls
        xo = xout + (xin - xout) * (k / n) * 0.8
        x0, x1 = min(xin, xo), max(xin, xo)
        out.append(P("fill", x0=x0, x1=x1, y0=yf, y1=yback, z0=0, z1=z, bl=1.3, bd=1.0, bh=0.9, moss=0.12))
    return out


# ---------------------------------------------------------------- ZonRuin15, the ancient temple

def t15():
    Z, H = 5.4, 6.0    # the court floor and the rim of the upper platform
    out = [P("fill", x0=-4.1, x1=4.05, y0=-4.87, y1=4.0, z0=0, z1=Z, bl=1.65, bd=1.8, bh=1.08, moss=0.05)]
    out += court(-4.1, 4.05, -4.87, 4.0, Z, rim=1.1, ledge=0.7, zr=H - Z, spine=(-0.9, 1.3, -4.87, 2.5),
                 slots=[(-1.35, -0.9, -3.3, 2.4), (1.3, 2.25, -3.3, 2.4)], open_front=(-0.9, 1.3))
    out += [
        # the front flight between its cheeks and the two battered bastions
        P("stair", x0=-0.95, x1=1.2, y0=-9.06, y1=-4.87, z0=0, z1=H, n=34),
        P("cheek", x0=-1.3, x1=-0.95, y0=-9.06, y1=-4.87, z0=0, za=0.35, zb=H + 0.1, n=5, moss=0.05),
        P("cheek", x0=1.2, x1=1.55, y0=-9.06, y1=-4.87, z0=0, za=0.35, zb=H + 0.1, n=5, moss=0.05),
        P("talus", x0=-4.1, x1=-1.3, yf=-9.06, yb=-4.87, z0=0, z1=4.1, n=6, bl=1.45, moss=0.12),
        P("talus", x0=1.55, x1=4.05, yf=-9.06, yb=-4.87, z0=0, z1=4.1, n=6, bl=1.3, moss=0.12),
        # wings: a landing each side and a flight falling outward, west and east
        P("fill", x0=-7.2, x1=-4.1, y0=-0.5, y1=2.9, z0=0, z1=5.65, bl=1.0, bd=0.85, bh=1.13, moss=0.1,
          tones=(0.3, 0.5, 0.2)),
        P("stair", x0=-11.4, x1=-7.2, y0=-0.5, y1=2.9, z0=1.9, z1=5.65, n=16, axis="x", zb=0),
        P("fill", x0=4.05, x1=7.3, y0=-0.5, y1=3.2, z0=0, z1=5.65, bl=1.0, bd=0.9, bh=1.13, moss=0.3,
          tones=(0.3, 0.5, 0.2)),
        P("stair", x0=11.7, x1=7.3, y0=-0.5, y1=3.2, z0=1.9, z1=5.65, n=16, axis="x", zb=0),
    ]
    # the plinth steps down in the corners between the wings and the bastions
    out += stepped(-4.1, -7.2, -0.5, -4.87, 4.0, 1.1, 4)
    out += stepped(4.05, 7.1, -0.5, -4.87, 4.0, 1.1, 4)
    out += [
        # the two corner towers and the block between them at the back
        P("fill", x0=-7.2, x1=-3.9, y0=2.5, y1=5.5, z0=0, z1=7.6, bl=1.65, bd=1.5, bh=1.27, moss=0.12),
        P("fill", x0=3.85, x1=7.3, y0=2.5, y1=5.5, z0=0, z1=7.6, bl=1.72, bd=1.5, bh=1.27, moss=0.12),
        P("fill", x0=-2.0, x1=3.85, y0=4.0, y1=5.3, z0=0, z1=6.6, bl=1.45, bd=1.3, bh=1.1, moss=0.2),
        # the platform carried back between the west tower and the back wall
        P("fill", x0=-3.9, x1=-2.0, y0=4.0, y1=5.5, z0=0, z1=6.0, bl=1.9, bd=1.5, bh=1.2, moss=0.1),
        # a block fallen by the west corner
        P("loose", cx=-5.8, cy=-2.0, rx=0.2, ry=0.2, n=1, size=(0.9, 0.8, 0.8), seed=5),
    ]
    return out


# ---------------------------------------------------------------- ZonRuin16, the ancient temple

def chamfer(xin, xout, y0, yc, ytop, z, n, **kw):
    """A wing top whose outer back corner is cut on the diagonal in n steps:
    from (xout, yc) back to (xin, ytop)."""
    out = [P("fill", x0=min(xin, xout), x1=max(xin, xout), y0=y0, y1=yc, z0=0, z1=z, bl=1.2, bd=1.3, bh=1.0, **kw)]
    for k in range(n):
        xa = xout + (xin - xout) * (k + 1) / n
        ya, yb = yc + (ytop - yc) * k / n, yc + (ytop - yc) * (k + 1) / n
        out.append(P("fill", x0=min(xin, xa), x1=max(xin, xa), y0=ya, y1=yb, z0=0, z1=z, bl=0.9, bd=yb - ya, bh=1.0,
                     **kw))
    return out


def bench(x0, x1, yf, ysplit, yb, zf, zb, legs, coursed=True, cols=3, rows=4, carried=2, bh=0.6, **kw):
    """A bastion of 16: a sloping slab, solid at the back, carried at the front
    on round feet. Coursed, it is laid as cols by rows sloped blocks, the
    front carried rows on the feet and the back rows on courses bh high."""
    out = []
    if coursed:
        def z(y):
            return zf + (zb - zf) * (y - yf) / (yb - yf)
        for j in range(rows):
            ya, yc = yf + (yb - yf) * j / rows, yf + (yb - yf) * (j + 1) / rows
            base = zf - 0.7 if j < carried else 0.0
            # the back rows stand on plain courses up to just under the slope
            nk = int((z(ya) - 0.3 - base) / bh) if j >= carried else 0
            # the joints wander a little from row to row
            cuts = [x0] + [x0 + (x1 - x0) * (i + (0.08, -0.06, 0.1, -0.1)[(i + j) % 4]) / cols
                           for i in range(1, cols)] + [x1]
            for a, c in zip(cuts, cuts[1:]):
                for k in range(nk):
                    out.append(P("block", x0=a, x1=c, y0=ya, y1=yc, z0=base + bh * k, z1=base + bh * (k + 1), **kw))
                out.append(P("block", x0=a, x1=c, y0=ya, y1=yc, z0=base + bh * nk, z1=max(z(ya), z(yc)),
                             wedge=("y", z(ya), z(yc)), moss_slope=True, **kw))
        for x in legs:
            out.append(P("drum", x=x, y=yf + 0.4, r=0.36, h=zf - 0.65, mat="dark"))
        return out
    mid = (x0 + x1) / 2
    for a, c in ((x0, mid), (mid, x1)):
        out.append(P("cheek", x0=a, x1=c, y0=ysplit, y1=yb, z0=0, za=zf + (zb - zf) * (ysplit - yf) / (yb - yf),
                     zb=zb, n=2, **kw))
        out.append(P("cheek", x0=a, x1=c, y0=yf, y1=ysplit, z0=zf - 0.7, za=zf,
                     zb=zf + (zb - zf) * (ysplit - yf) / (yb - yf), n=1, **kw))
    for x in legs:
        out.append(P("drum", x=x, y=yf + 0.4, r=0.36, h=zf - 0.65, mat="dark"))
    return out


def t16(coursed=True):
    Z, H = 5.4, 6.0
    out = [P("fill", x0=-3.9, x1=4.1, y0=-5.04, y1=3.7, z0=0, z1=Z, bl=1.6, bd=1.75, bh=1.08, moss=0.05)]
    out += court(-3.9, 4.1, -5.04, 3.7, Z, rim=1.1, ledge=0.75, zr=H - Z, spine=(-0.5, 2.0, -2.1, 2.6),
                 slots=[(-0.95, -0.5, -2.1, 2.5), (2.0, 2.5, -3.2, 2.6), (-0.5, 2.0, -3.2, -2.1)])
    out += [
        P("stair", x0=-2.25, x1=2.44, y0=-8.6, y1=-5.04, z0=0, z1=H, n=26),
        P("cheek", x0=-3.9, x1=-2.25, y0=-8.3, y1=-5.04, z0=0, za=0.8, zb=4.4, n=3, moss=0.2),
        P("cheek", x0=2.44, x1=3.4, y0=-8.3, y1=-5.04, z0=0, za=0.8, zb=4.4, n=3, moss=0.2),
        # wings: flights falling outward from broad landings cut back on the diagonal
        P("stair", x0=-11.6, x1=-7.6, y0=-4.3, y1=-1.2, z0=2.25, z1=5.8, n=14, axis="x", zb=0),
        P("stair", x0=11.6, x1=8.1, y0=-4.3, y1=-1.2, z0=2.25, z1=5.8, n=14, axis="x", zb=0),
    ]
    out += chamfer(-3.9, -7.6, -4.3, -1.2, 2.35, 5.8, 5, moss=0.25)
    out += chamfer(4.1, 8.1, -4.3, -1.2, 2.35, 5.8, 5, moss=0.25)
    out += bench(-7.25, -3.9, -7.7, -6.1, -5.04, 1.5, 3.7, (-6.8, -4.5), coursed=coursed, moss=0.3)
    out += bench(3.4, 7.6, -7.7, -6.1, -5.04, 1.5, 3.7, (5.0, 7.2), coursed=coursed, moss=0.3)
    out += [
        # the back block and a short flight climbing north over it
        P("fill", x0=-3.9, x1=4.1, y0=3.7, y1=5.2, z0=0, z1=6.2, bl=1.6, bd=1.5, bh=1.03, moss=0.15),
        P("stair", x0=-0.6, x1=0.9, y0=3.7, y1=5.6, z0=H, z1=7.0, n=6, zb=Z),
    ]
    return out


# ---------------------------------------------------------------- ZonRuin01, the Kandran tomb

def tomb():
    Z = 2.6
    stain = (2.3, -1.4, 1.0, 2.3)  # the soot stain over the east slab
    out = [
        # the plinth, stepped at its front corners
        P("fill", x0=-4.7, x1=5.0, y0=-4.9, y1=5.4, z0=0, z1=0.6, bl=1.6, bd=1.7, bh=1, tones=(0.5, 0.4, 0.1),
          moss=0.0),
        P("fill", x0=-4.7, x1=-2.4, y0=-6.2, y1=-4.9, z0=0, z1=0.6, bl=1.15, bd=1.3, bh=1, moss=0.0,
          mat="grey"),
        P("fill", x0=-4.3, x1=-2.4, y0=-5.5, y1=-4.9, z0=0.6, z1=1.2, bl=1.0, bd=0.6, bh=1, moss=0.0, mat="grey"),
        P("fill", x0=2.75, x1=5.0, y0=-6.2, y1=-4.9, z0=0, z1=0.6, bl=1.15, bd=1.3, bh=1, moss=0.0, mat="grey"),
        P("fill", x0=2.75, x1=4.6, y0=-5.5, y1=-4.9, z0=0.6, z1=1.2, bl=1.0, bd=0.6, bh=1, moss=0.0, mat="grey"),
        # the front flight: two courses of rough brown stones
        P("fill", x0=-2.4, x1=2.75, y0=-6.25, y1=-4.6, z0=0, z1=1.3, bl=0.95, bd=1.65, bh=1.3, mat=("brown", "brown2"),
          rough=0.05, jit=0.2),
        P("fill", x0=-2.4, x1=2.75, y0=-5.45, y1=-4.6, z0=1.3, z1=Z, bl=1.05, bd=0.85, bh=1.3,
          mat=("brown", "brown2"), rough=0.05, jit=0.2),
        # the body, battered in three courses
        P("fill", x0=-4.55, x1=4.9, y0=-4.9, y1=5.25, z0=0.6, z1=1.25, bl=1.7, bd=1.8, bh=1, moss=0.0,
          tones=(0.55, 0.35, 0.1)),
        P("fill", x0=-4.2, x1=4.55, y0=-4.75, y1=5.15, z0=1.25, z1=1.95, bl=1.6, bd=1.8, bh=1, moss=0.0,
          tones=(0.55, 0.35, 0.1)),
        P("fill", x0=-3.8, x1=4.15, y0=-4.6, y1=5.05, z0=1.95, z1=Z, bl=1.5, bd=1.8, bh=1, moss=0.0,
          tones=(0.6, 0.35, 0.05)),
        # the lid: a dark bed, three grey slabs lengthwise and red flagging between
        P("block", x0=-2.95, x1=3.45, y0=-3.4, y1=4.6, z0=Z, z1=Z + 0.09, mat="bed", gap=False),
        P("fill", x0=-2.95, x1=-1.5, y0=-3.4, y1=4.6, z0=Z, z1=Z + 0.28, bl=2, bd=4.0, bh=1, mat="grey"),
        P("fill", x0=-0.4, x1=0.95, y0=-3.4, y1=4.6, z0=Z, z1=Z + 0.28, bl=2, bd=4.0, bh=1, mat="grey"),
        P("fill", x0=2.1, x1=3.45, y0=-3.4, y1=4.6, z0=Z, z1=Z + 0.28, bl=2, bd=4.0, bh=1, mat="grey", stain=stain),
        P("flags", x0=-1.5, x1=-0.4, y0=-3.4, y1=4.6, z=Z + 0.09, size=0.62, seed=5),
        P("flags", x0=0.95, x1=2.1, y0=-3.4, y1=4.6, z=Z + 0.09, size=0.62, seed=6, stain=stain),
    ]
    return out


TOMB_STONE = dict(light=(152, 136, 112), mid=(136, 118, 94), dark=(118, 98, 74), grey=(136, 126, 112),
                  flag=(128, 64, 46), flag2=(114, 57, 41), flag3=(140, 74, 52), brown=(106, 82, 58),
                  brown2=(88, 68, 47), moss=(96, 94, 60), stain=(40, 38, 34), stain_mid=(70, 66, 58),
                  stain_light=(102, 96, 86))


# ---------------------------------------------------------------- the ruins
# Heaps and strewn blocks rest on whatever is left standing under them.

def ruin15a():
    return [
        # the east half of the court has fallen in and the east tower is down
        P("collapse", cx=3.0, cy=-0.6, rx=2.2, ry=4.3, zkeep=3.5, rise=1.6, jag=0.6, tip=0.45),
        P("collapse", cx=5.6, cy=4.0, rx=2.2, ry=2.0, zkeep=3.5, rise=2.0, jag=0.8, tip=0.3),
        # the foot of the west flight, the stepped corner and the west bastion have slumped
        P("collapse", cx=-9.6, cy=-0.6, rx=2.2, ry=1.4, zkeep=1.2, rise=1.5, jag=0.4),
        P("collapse", cx=-5.8, cy=-2.8, rx=2.2, ry=2.4, zkeep=0.5, rise=1.5, jag=0.4),
        P("collapse", cx=-2.7, cy=-7.4, rx=1.6, ry=2.0, zkeep=0.4, rise=1.6, jag=0.3),
        P("knock", cx=-10.0, cy=1.2, rx=1.6, ry=2.2, share=0.35, seed=19),
        P("heap", cx=2.9, cy=-0.5, rx=1.3, ry=2.4, h=1.4, big=8, chips=34, seed=11),
        P("heap", cx=5.4, cy=4.6, rx=1.5, ry=1.2, h=1.6, big=3, chips=30, seed=12),
        P("heap", cx=-9.5, cy=-0.3, rx=1.4, ry=1.2, h=1.4, z0=0, big=1, chips=31, seed=13),
        P("heap", cx=-5.6, cy=-2.8, rx=1.4, ry=1.4, h=1.3, z0=0, big=4, chips=30, seed=14),
        P("heap", cx=-2.7, cy=-7.6, rx=1.2, ry=1.1, h=1.1, z0=0, big=1, chips=30, seed=15),
        P("heap", cx=4.4, cy=-7.6, rx=0.9, ry=0.9, h=0.7, z0=0, chips=30, seed=16),
        P("loose", cx=9.3, cy=1.0, rx=0.8, ry=0.6, n=3, size=(0.6, 0.45, 0.35), seed=17),
        P("loose", cx=-0.4, cy=-6.4, rx=0.6, ry=0.8, n=3, size=(0.55, 0.4, 0.3), seed=18),
        P("loose", cx=-7.4, cy=-3.2, rx=3.0, ry=2.4, n=6, seed=21),
        P("loose", cx=8.3, cy=-3.3, rx=2.6, ry=2.4, n=5, seed=23),
        P("loose", cx=7.6, cy=4.6, rx=1.2, ry=1.0, n=3, seed=24),
        P("pebbles", cx=-1.0, cy=-4.0, rx=10.5, ry=5.5, n=26, seed=25),
    ]


def ruin15b():
    return [
        # the whole court, both towers and the back have fallen in
        P("collapse", cx=0.8, cy=0.3, rx=4.6, ry=4.8, zkeep=2.2, rise=2.0, jag=0.8, tip=0.35),
        P("collapse", cx=-5.5, cy=4.0, rx=2.2, ry=2.2, zkeep=2.5, rise=2.0, jag=0.8, tip=0.3),
        P("collapse", cx=5.6, cy=4.0, rx=2.2, ry=2.2, zkeep=2.5, rise=2.0, jag=0.8, tip=0.3),
        P("collapse", cx=1.0, cy=4.7, rx=3.2, ry=1.0, zkeep=3.5, rise=1.0, jag=0.6),
        # the west flight's foot, both stepped corners and both bastions are heaps
        P("collapse", cx=-10.2, cy=-0.6, rx=1.6, ry=1.4, zkeep=1.0, rise=1.4, jag=0.4),
        P("collapse", cx=-5.8, cy=-3.0, rx=2.4, ry=2.4, zkeep=0.4, rise=1.6, jag=0.4),
        P("collapse", cx=5.8, cy=-2.8, rx=2.2, ry=2.2, zkeep=0.5, rise=1.6, jag=0.4),
        P("collapse", cx=-2.7, cy=-7.2, rx=1.8, ry=2.6, zkeep=0.3, rise=1.4, jag=0.3),
        P("collapse", cx=3.0, cy=-6.8, rx=1.7, ry=2.6, zkeep=0.3, rise=1.4, jag=0.3),
        # treads knocked out of the lower flights
        P("knock", cx=-10.0, cy=1.2, rx=2.4, ry=2.4, share=0.5, seed=46),
        P("knock", cx=10.2, cy=1.3, rx=2.4, ry=2.4, share=0.5, seed=47),
        P("knock", cx=0.1, cy=-8.0, rx=1.6, ry=1.6, share=0.45, seed=48),
        P("heap", cx=0.9, cy=0.2, rx=2.7, ry=2.9, h=1.8, big=10, chips=30, seed=31),
        P("heap", cx=-4.6, cy=6.1, rx=2.3, ry=1.4, h=2.2, big=3, chips=30, seed=32),
        P("heap", cx=3.2, cy=6.1, rx=2.6, ry=1.4, h=2.3, big=2, chips=31, seed=33),
        # rubble spilled over the lower flights
        P("heap", cx=-9.9, cy=0.9, rx=1.8, ry=1.7, h=1.3, chips=30, follow=True, seed=35),
        P("heap", cx=10.1, cy=1.3, rx=1.7, ry=1.7, h=1.2, chips=30, follow=True, seed=44),
        # piles either side of the front flight, spilling over its foot
        P("heap", cx=-1.4, cy=-7.9, rx=1.7, ry=1.5, h=1.2, chips=37, follow=True, seed=38),
        P("heap", cx=1.9, cy=-7.5, rx=1.6, ry=1.6, h=1.2, chips=37, follow=True, seed=39),
        P("loose", cx=9.4, cy=0.6, rx=0.9, ry=0.8, n=3, size=(0.6, 0.45, 0.35), seed=40),
        P("loose", cx=-7.0, cy=1.5, rx=2.4, ry=1.2, n=4, size=(1.0, 0.7, 0.5), seed=41),
        P("loose", cx=-6.4, cy=-3.4, rx=3.0, ry=2.2, n=10, size=(1.6, 1.1, 0.75), seed=42),
        P("loose", cx=6.6, cy=-3.0, rx=2.8, ry=2.2, n=10, size=(1.6, 1.1, 0.75), seed=43),
        P("drum", x=-6.9, y=4.6, r=0.42, h=1.0, z0=2.2),
        P("drum", x=4.3, y=5.0, r=0.38, h=0.8, z0=2.6),
        P("pebbles", cx=0.0, cy=-3.5, rx=11.0, ry=6.0, n=10, seed=45),
    ]


def ruin16a():
    return [
        # the west side of the court has burst, down into a dark hole
        P("collapse", cx=-3.4, cy=0.2, rx=2.0, ry=3.6, zkeep=3.4, rise=1.5, jag=0.6, tip=0.5),
        P("block", x0=-4.8, x1=-2.9, y0=-2.4, y1=0.0, z0=2.2, z1=3.0, mat="shade", gap=False),
        # the north-west corner of the west landing is thrown down, and the east landing's middle
        P("collapse", cx=-6.4, cy=1.6, rx=2.2, ry=2.0, zkeep=2.5, rise=2.0, jag=0.6),
        P("collapse", cx=7.0, cy=0.8, rx=2.0, ry=1.8, zkeep=4.4, rise=1.0, jag=0.5),
        # the east bastion has fallen apart
        P("collapse", cx=5.6, cy=-6.2, rx=2.4, ry=2.0, zkeep=0.2, rise=1.2, jag=0.3),
        P("knock", cx=-10.0, cy=-2.8, rx=1.8, ry=2.0, share=0.3, seed=57),
        P("heap", cx=-3.3, cy=-1.0, rx=1.0, ry=1.1, h=0.9, big=3, chips=30, seed=51),
        P("heap", cx=6.9, cy=0.6, rx=1.4, ry=1.4, h=2.0, z0=3.8, big=1, chips=44, seed=52),
        P("heap", cx=-10.3, cy=-1.6, rx=1.0, ry=0.9, h=0.8, chips=30, seed=53),
        P("heap", cx=-10.2, cy=-4.3, rx=1.0, ry=0.9, h=0.8, chips=30, seed=54),
        P("loose", cx=10.4, cy=-2.6, rx=1.0, ry=1.0, n=4, size=(0.6, 0.45, 0.35), seed=55),
        P("loose", cx=-1.0, cy=-6.1, rx=1.0, ry=1.2, n=4, size=(0.55, 0.4, 0.3), seed=56),
        P("loose", cx=-7.0, cy=3.4, rx=2.2, ry=2.2, n=7, size=(1.2, 0.8, 0.6), seed=61),
        P("loose", cx=9.5, cy=0.4, rx=1.8, ry=1.6, n=4, seed=62),
        P("loose", cx=5.8, cy=-6.4, rx=2.2, ry=1.8, n=8, tilt=0.5, seed=63),
        P("drum", x=4.6, y=-7.8, r=0.4, h=0.7),
        P("drum", x=5.6, y=-8.5, r=0.35, h=0.6),
        P("pebbles", cx=0.0, cy=-3.0, rx=11.5, ry=5.5, n=24, seed=64),
    ]


def ruin16b():
    return [
        # the court is a pile of slabs and the back has come down over it
        P("collapse", cx=0.0, cy=0.0, rx=4.4, ry=4.8, zkeep=2.4, rise=2.0, jag=0.8, tip=0.4),
        P("collapse", cx=-0.8, cy=4.5, rx=3.6, ry=1.2, zkeep=3.5, rise=1.2, jag=0.6),
        P("collapse", cx=-6.4, cy=1.6, rx=2.2, ry=2.0, zkeep=2.5, rise=2.0, jag=0.6),
        P("collapse", cx=7.0, cy=0.8, rx=2.0, ry=1.8, zkeep=4.4, rise=1.0, jag=0.5),
        P("collapse", cx=5.6, cy=-6.2, rx=2.4, ry=2.0, zkeep=0.2, rise=1.2, jag=0.3),
        P("collapse", cx=-5.4, cy=-6.5, rx=2.0, ry=1.9, zkeep=0.6, rise=1.2, jag=0.3, tip=0.5),
        # treads knocked out of the east flight and the foot of the front flight
        P("knock", cx=9.9, cy=-2.75, rx=2.4, ry=2.0, share=0.5, seed=87),
        P("knock", cx=0.1, cy=-7.6, rx=2.6, ry=1.5, share=0.45, seed=88),
        P("heap", cx=0.6, cy=0.3, rx=2.3, ry=2.7, h=1.8, big=10, chips=30, seed=71),
        P("heap", cx=-2.2, cy=-0.5, rx=1.4, ry=1.0, h=1.0, z0=3.0, chips=30, seed=72),
        P("heap", cx=-0.9, cy=5.4, rx=2.3, ry=1.1, h=2.0, big=3, chips=30, seed=73),
        P("heap", cx=6.9, cy=0.6, rx=1.4, ry=1.4, h=2.0, z0=3.8, big=1, chips=30, seed=74),
        # rubble over the east flight
        P("heap", cx=9.8, cy=-2.8, rx=1.9, ry=1.7, h=1.2, chips=34, follow=True, seed=79),
        P("heap", cx=-10.25, cy=-2.9, rx=1.1, ry=2.0, h=0.8, chips=34, seed=75),
        P("loose", cx=10.4, cy=-2.6, rx=1.0, ry=1.0, n=3, size=(0.6, 0.45, 0.35), seed=77),
        P("heap", cx=-0.5, cy=-7.4, rx=2.1, ry=2.0, h=1.2, big=1, chips=39, follow=True, seed=78),
        P("loose", cx=-7.0, cy=2.4, rx=2.4, ry=1.8, n=5, size=(1.2, 0.8, 0.6), seed=81),
        P("loose", cx=-5.8, cy=4.6, rx=2.4, ry=1.6, n=4, size=(1.2, 0.8, 0.6), seed=86),
        P("loose", cx=9.4, cy=-0.2, rx=1.8, ry=1.6, n=3, seed=82),
        P("loose", cx=5.8, cy=-6.4, rx=2.2, ry=1.8, n=6, tilt=0.5, seed=83),
        P("heap", cx=-5.5, cy=-6.6, rx=1.7, ry=1.4, h=1.0, z0=0, big=3, chips=30, seed=84),
        P("drum", x=3.9, y=-7.6, r=0.4, h=0.7),
        P("drum", x=5.6, y=-8.2, r=0.35, h=0.6),
    ]


def models():
    base = dict(kind="temple", seed=1, moss=0.12)
    out = {
        "ZonRuin15": dict(base, parts=t15(), seed=15, moss=0.2, darken=0.96),
        "ZonRuin15a": dict(base, parts=t15(), ruin=ruin15a(), seed=15, moss=0.2, darken=0.86),
        "ZonRuin15b": dict(base, parts=t15(), ruin=ruin15b(), seed=15, moss=0.2, darken=0.8),
        "ZonRuin16": dict(base, parts=t16(), seed=16, moss=0.25, darken=0.9),
        # the burst west half of the court dirty and thick with moss
        "ZonRuin16a": dict(base, parts=t16(), ruin=ruin16a(), seed=16, moss=0.25, darken=0.75,
                           dirt_zones=[(-3.0, 0.0, 2.8, 4.4, 0.7)], moss_zones=[(-4.5, 0.0, 4.5, 5.5, 0.3)]),
        # its benches are down, so it keeps the plain slabs it passed review with
        "ZonRuin16b": dict(base, parts=t16(coursed=False), ruin=ruin16b(), seed=16, moss=0.25, darken=0.72),
        "ZonRuin01": dict(base, parts=tomb(), seed=1, moss=0.0, stone=TOMB_STONE, tones=(0.5, 0.35, 0.15),
                          darken=0.9),
    }
    return out
