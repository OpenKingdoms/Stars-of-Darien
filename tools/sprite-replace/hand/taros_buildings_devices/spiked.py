"""Tarbuild02 and its ruins: a long flat-roofed Taros hall. Its roof is
laid in red-veined panels between pale stone beams, a row of horned spires
and silver horns stands along the back, blade-like fins slope from both
ends down to the ground, and leaning struts prop the front."""
import math
import random

from mathutils import Matrix, Vector

import kit as K

PANEL = (70, 64, 63)
VEIN = (112, 22, 20)
PANEL_DARK = (40, 12, 10)
BEAM = (70, 62, 60)
BEAM_DOT = (30, 26, 24)
PALE = (112, 104, 94)
WALL = (20, 20, 19)
FIN = (38, 38, 38)
IRON = (58, 58, 60)
SILVER = (200, 202, 206)
RUBBLE_TAN = (104, 86, 78)
OCHRE = (132, 110, 76)


def panel():
    return K.texmat("sp_panel", lambda: K.tex_cobble(PANEL, VEIN, n=256, count=46, seed=51, var=0.25, vein_w=2.4,
                                                      dark=PANEL_DARK), rough=0.85)


def beam():
    return K.texmat("sp_beam", lambda: K.tex_speckle(BEAM, [(BEAM_DOT, 0.7), ((104, 96, 90), 0.5)], n=128, seed=52,
                                                      density=0.6, size=2), rough=0.9)


def pale():
    return K.texmat("sp_pale", lambda: K.tex_mottle(PALE, (80, 72, 64), (136, 132, 126), n=128, seed=53), rough=0.85)


def wall():
    return K.texmat("sp_wall", lambda: K.tex_courses(WALL, (8, 8, 8), n=128, rows=6, per_row=3, seed=54, var=0.3,
                                                      jw=2), rough=0.9)


def fin_mat():
    return K.texmat("sp_fin", lambda: K.tex_mottle(FIN, (18, 18, 18), (58, 58, 58), n=128, seed=55), rough=0.8)


def iron():
    return K.mat("sp_iron", IRON, rough=0.55, metal=0.5, spec=0.4)


def silver():
    return K.mat("sp_silver", SILVER, rough=0.25, metal=0.9, spec=0.6)


def tan_rubble():
    return K.texmat("sp_rubble", lambda: K.tex_rubble(RUBBLE_TAN, (20, 15, 13), (150, 128, 118), n=256, count=40,
                                                       seed=56), rough=1.0)


def tan_stones():
    return [K.mat("sp_stone_a", (128, 104, 96), rough=0.95), K.mat("sp_stone_b", (80, 64, 58), rough=0.95),
            panel(), beam()]


# ---- the hall ------------------------------------------------------------------------

HALL = dict(cx=0.05, cy=-0.25, W=16.7, D=6.9, H=6.9)
SPIRES = [-7.7, -5.0, -2.5, 0.1, 2.8, 5.5, 8.25]
HORNS = [-4.7, 0.1, 4.7]
FINS = [3.4, 1.3, -0.5, -2.6]
STRUTS = [-8.1, -5.5, -2.8, 2.4, 5.0, 7.6]


def spire(x, y, z, h=3.7 * K.RISE, mt=None, broken=None, g=K.GIRTH * 1.1):
    """A dark iron finial: a square shaft in stepped collars with two
    swept flanges, drawn to a point, built g times thicker and lower than
    the picture's. broken leaves a stump: a positive height snapped off
    flat under a collar, a negative one jagged."""
    mt = mt or iron()
    parts = [K.block(0.7 * g, 0.7 * g, 0.5, x, y, z, 45, mt, "spire_foot", top=(0.55 * g, 0.55 * g))]
    if broken is not None:
        if broken < 0:
            parts.append(K.block(0.45 * g, 0.45 * g, -broken * K.RISE, x, y, z + 0.5, 45, mt, "spire_stump",
                                 top=(0.3 * g, 0.12 * g)))
        else:
            b = broken * K.RISE
            parts.append(K.block(0.44 * g, 0.44 * g, b, x, y, z + 0.5, 45, mt, "spire_stump", top=(0.38 * g, 0.38 * g)))
            parts.append(K.block(0.56 * g, 0.56 * g, 0.16, x, y, z + 0.5 + b * 0.55, 45, mt, "spire_collar"))
            parts.append(K.loft([K.rect(x, y, 0.38 * g, 0.38 * g, 45, z + 0.5 + b),
                                 K.rect(x, y, 0.3 * g, 0.3 * g, 45, z + 0.62 + b)], mt, "spire_break"))
        return parts
    parts.append(K.loft([K.rect(x, y, 0.42 * g, 0.42 * g, 45, z + 0.5), K.rect(x, y, 0.32 * g, 0.32 * g, 45, z + h * 0.55),
                         K.rect(x, y, 0.5 * g, 0.5 * g, 45, z + h * 0.6), K.rect(x, y, 0.22 * g, 0.22 * g, 45, z + h * 0.75),
                         [(x, y, z + h)]], mt, "spire"))
    for s in (-1, 1):
        parts.append(K.tube([(x, y, z + h * 0.35), (x + s * 0.45 * g, y, z + h * 0.42), (x + s * 0.6 * g, y, z + h * 0.62)],
                            [0.12 * g, 0.08 * g, 0.0], seg=4, sub=2, mt=mt, name="flange"))
    return parts


def horn(x, y, z, h=3.4 * K.RISE, lean=0.35, g=K.GIRTH * 1.1):
    """A silver horn standing on the roof edge, curving back a little,
    thicker and lower than the picture's."""
    return K.tube([(x, y, z), (x, y + lean * 0.3, z + h * 0.45), (x, y + lean, z + h)], [0.22 * g, 0.13 * g, 0.0],
                  seg=8, sub=3, mt=silver(), name="horn")


def ring_blade(x, y, z):
    """An iron ring on the roof with a silver blade hanging from it."""
    ring = K.torus(0.55, 0.1, seg=16, rseg=4, mt=iron(), name="roof_ring")
    K.place(ring, Matrix.Translation((x, y, z + 0.15)))
    blade = K.loft([[(x - 0.22, y - 0.35, z + 0.05), (x + 0.22, y - 0.35, z + 0.05), (x + 0.22, y - 0.35, z + 0.25),
                     (x - 0.22, y - 0.35, z + 0.25)], [(x, y - 1.6, z + 0.12)]], silver(), "roof_blade")
    return [ring, blade]


def fin(x, y, z_top, out, side, t=0.6, spikes=3, keep=1.0):
    """A blade-like fin standing out from an end wall: attached along the
    wall from the ground to z_top, its sloped edge running out `out`
    cells to the ground. keep < 1 snaps it off part way down the slope."""
    s = side
    x1 = x + s * out
    pts = [(x, z_top), (x + s * 0.7, z_top), (x1 - s * 0.15, 0.25), (x1, -0.05), (x, -0.05)]
    if keep < 1.0:
        k = keep
        pts = [(x, z_top * k), (x + s * out * (1 - k) * 0.9, z_top * k * 0.55), (x1, -0.05), (x, -0.05)]
    prof = [(px, y - t / 2, pz) for px, pz in pts]
    back = [(px, y + t / 2, pz) for px, pz in pts]
    ob = K.loft([prof, back], fin_mat(), "fin")
    K.uv_box(ob, 0.5)
    parts = [ob]
    for i in range(spikes):
        f = (i + 0.7) / (spikes + 0.4)
        if keep < 1.0 and f < 1 - keep:
            continue
        px = x + s * (0.7 + (out - 0.7) * f)
        pz = z_top * (1 - f) + 0.2
        parts.append(K.tube([(px, y - t / 2 + 0.05, pz), (px + s * 0.25, y - t / 2 - 0.35, pz + 0.2)],
                            [0.12, 0.0], seg=4, sub=1, mt=silver(), name="fin_spike"))
    return parts


def strut(x, y0, y1, z1, broken=None):
    """A leaning prop from the ground in front up to the roof edge, with a
    pale ornament near its head."""
    a = Vector((x, y0, -0.3))
    b = Vector((x, y1, z1))
    if broken is not None:
        b = a.lerp(b, broken)
    parts = [K.board(a, b, 0.55, 0.55, iron(), "strut", up=(0, -1, 0))]
    K.uv_box(parts[0], 0.5)
    if broken is None:
        c = a.lerp(b, 0.86)
        parts.append(K.board(c - Vector((0, 0.15, 0.4)), c + Vector((0, 0.15, 0.4)), 0.7, 0.62, pale(), "plaque",
                             up=(0, -1, 0)))
        parts.append(K.pyramid(x, b.y, b.z, 0.6, 0.6, 0.8, 45, iron(), "strut_cap"))
    return parts


def ochre():
    return K.texmat("sp_ochre", lambda: K.tex_mottle(OCHRE, (80, 66, 46), (160, 136, 96), n=128, seed=57, amt=0.8),
                    rough=0.95)


def crater_floor():
    return K.texmat("sp_crater", lambda: K.tex_rubble((96, 80, 58), (22, 16, 12), (150, 126, 90), n=256, count=60,
                                                       seed=58), rough=1.0)


def crack_mat():
    return K.mat("sp_crack", (16, 10, 8), rough=1.0)


def hall(cx, cy, W, D, H, holes=(), spires=None, horns=None, fins=None, struts=None, rings=True, seed=1,
         floor=3.0, craters=(), walls="fblr", door_prop=True, ragged=None):
    """The hall. holes are roof polygons [(x, y), ...] left open onto a
    hollow shell; the shell's sides not named in walls (f, b, l, r) break
    down round the holes. craters [(x, y, rx, ry, depth, seed), ...] are
    shallow ragged dents in the roof: its panels sag and tilt into them
    over a broken floor, an ochre rim round each and cracks running out
    across the panels. Each of spires/horns/fins/struts may be a dict of
    index -> damage. ragged maps a broken side to the (low, high) share of
    its height its torn top keeps."""
    rnd = random.Random(seed)
    x0, x1, y0, y1 = cx - W / 2, cx + W / 2, cy - D / 2, cy + D / 2
    zr = H - 0.3
    parts = []
    edges = [K.outline_fn(cs, 0.2) for *_, cs in craters]

    def open_at(x, y):
        return any(K.inside_poly(h, x, y) for h in holes)

    def crater_at(x, y):
        """How far down a crater the roof is here (0..1), and which."""
        for k, (ccx, ccy, crx, cry, cd, cs) in enumerate(craters):
            u, v = (x - ccx) / crx, (y - ccy) / cry
            r = math.hypot(u, v) / edges[k](math.atan2(v, u))
            if r < 1:
                return 1 - r * r, k
        return 0.0, None

    if not holes and not craters:
        body = K.block(W, D, H - 0.3, cx, cy, 0, 0, wall(), "body")
        K.uv_box(body, 0.5)
        parts.append(body)
    else:
        if craters and not holes:
            floor = zr - 1.1
        body = K.block(W, D, floor, cx, cy, 0, 0, wall(), "body")
        K.uv_box(body, 0.5)
        parts.append(body)
        t = 0.6
        for side, ((ax, ay), (bx, by)) in zip("frbl", (((x0, y0 + t / 2), (x1, y0 + t / 2)),
                                                        ((x1 - t / 2, y0), (x1 - t / 2, y1)),
                                                        ((x1, y1 - t / 2), (x0, y1 - t / 2)),
                                                        ((x0 + t / 2, y1), (x0 + t / 2, y0)))):
            inx, iny = (cx - (ax + bx) / 2), (cy - (ay + by) / 2)
            n = math.hypot(inx, iny)
            inx, iny = inx / n, iny / n

            def hfn(u, ax=ax, ay=ay, bx=bx, by=by, inx=inx, iny=iny, side=side):
                px, py = ax + (bx - ax) * u + inx * 1.0, ay + (by - ay) * u + iny * 1.0
                full = H - 0.3 - floor
                lo, hi = (ragged or {}).get(side, (0.35, 0.8))
                return full * rnd.uniform(lo, hi) if (side not in walls and open_at(px, py)) else full
            sh = K.wall_run((ax, ay), (bx, by), floor, hfn if holes else H - 0.3 - floor, t, wall(), seg=0.8,
                            name="shell")
            for p in sh:
                K.uv_box(p, 0.5)
            parts += sh

    # the roof: panels between beams, each a tile so holes can be left out
    beams_x = [-4.85, -0.15, 4.6]
    band_y = (-1.6, 0.1)
    bw = 1.4
    tiles = []
    xs = [x0 + 0.6] + sum([[bx - bw / 2, bx + bw / 2] for bx in beams_x], []) + [x1 - 0.6]
    ys = [y0 + 0.6, band_y[0], band_y[1], y1 - 0.6]

    def roof_top(x, y):
        on_beam = any(abs(x - bx) < bw / 2 for bx in beams_x) or band_y[0] < y < band_y[1]
        return zr + (0.42 if on_beam else 0.3)

    for i in range(len(xs) - 1):
        for j in range(len(ys) - 1):
            is_beam = (i % 2 == 1) or j == 1
            tiles.append((xs[i], xs[i + 1], ys[j], ys[j + 1], is_beam, j))
    for tx0, tx1, ty0, ty1, is_beam, j in tiles:
        # cut each tile into cells so a hole or a crater can take part of it
        near = any(tx0 < c[0] + c[2] * 1.3 and tx1 > c[0] - c[2] * 1.3 and ty0 < c[1] + c[3] * 1.3
                   and ty1 > c[1] - c[3] * 1.3 for c in craters)
        step = 0.7 if near else 0.8
        nx, ny = max(1, int((tx1 - tx0) / step)), max(1, int((ty1 - ty0) / step))
        for a in range(nx):
            for b in range(ny):
                ux0, ux1 = tx0 + (tx1 - tx0) * a / nx, tx0 + (tx1 - tx0) * (a + 1) / nx
                uy0, uy1 = ty0 + (ty1 - ty0) * b / ny, ty0 + (ty1 - ty0) * (b + 1) / ny
                mx, my = (ux0 + ux1) / 2, (uy0 + uy1) / 2
                if open_at(mx, my):
                    continue
                if is_beam:
                    mt = pale() if (j == 2 and (tx1 - tx0) < 2) else beam()
                    th = 0.42
                else:
                    mt, th = panel(), 0.3
                s, k = crater_at(mx, my)
                if k is None:
                    ob = K.block(ux1 - ux0, uy1 - uy0, th, mx, my, zr, 0, mt, "beam" if is_beam else "panel")
                    K.uv_box(ob, 0.3)
                    parts.append(ob)
                    continue
                # a broken panel sagging into the crater, tipped toward its middle
                ccx, ccy, crx, cry, cd, cs = craters[k]
                ob = K.block((ux1 - ux0) * 0.9, (uy1 - uy0) * 0.9, th, 0, 0, -th, 0, mt, "sag_panel")
                K.uv_box(ob, 0.3, rnd.random(), rnd.random())
                dx, dy = mx - ccx, my - ccy
                dl = math.hypot(dx, dy) + 1e-6
                slope = math.atan(2 * cd * math.sqrt(1 - s) / ((crx + cry) / 2)) + math.radians(rnd.uniform(-8, 8))
                K.place(ob, Matrix.Translation((mx, my, zr + th - cd * s)) @ Matrix.Rotation(
                    math.radians(rnd.uniform(-10, 10)), 4, "Z") @ Matrix.Rotation(-slope, 4, Vector((-dy / dl, dx / dl, 0))))
                parts.append(ob)
    for k, (ccx, ccy, crx, cry, cd, cs) in enumerate(craters):
        # the broken floor under the sagging panels
        ob, fn, ins = K.heap(ccx, ccy, crx * 1.02, cry * 1.02, -cd - 0.1, crater_floor(), lambda x, y: zr + 0.25,
                             seed=cs, amp=0.2, lump=0.4, power=1.0, sink=-0.02, name="crater_floor", rings=5, seg=22)
        parts.append(ob)
        # the ochre rim: exposed stone and dust round the edge
        e = edges[k]
        seg = 28
        rim_rings = []
        for rr, dz in ((0.82, -cd * 0.33), (0.96, 0.1), (1.08, 0.08), (1.2, 0.03)):
            ring = []
            for q in range(seg):
                a = 2 * math.pi * q / seg
                f = rr * e(a) * (1 + 0.06 * math.sin(7 * a + cs))
                px, py = ccx + crx * f * math.cos(a), ccy + cry * f * math.sin(a)
                ring.append((px, py, roof_top(px, py) + dz))
            rim_rings.append(ring)
        rim = K.loft(rim_rings, ochre(), "crater_rim", cap0=False, cap1=False, smooth=50)
        for p in rim.data.polygons:
            if p.normal.z < 0:
                p.flip()
        K.uv_box(rim, 0.5)
        parts.append(rim)
        # cracks running out across the neighbouring panels
        cr = random.Random(cs)
        for q in range(6):
            a = cr.uniform(0, 2 * math.pi)
            f = 1.15 * e(a)
            px, py = ccx + crx * f * math.cos(a), ccy + cry * f * math.sin(a)
            pts = [(px, py)]
            for _ in range(3):
                a += cr.uniform(-0.6, 0.6)
                L = cr.uniform(0.4, 0.9)
                px, py = px + L * math.cos(a), py + L * math.sin(a)
                pts.append((px, py))
            pts = [(max(x0 + 0.7, min(x1 - 0.7, x)), max(y0 + 0.7, min(y1 - 0.7, y))) for x, y in pts]
            parts.append(K.tube([(x, y, roof_top(x, y) + 0.01) for x, y in pts], [0.07, 0.06, 0.05, 0.0], seg=4,
                                sub=1, mt=crack_mat(), name="crack", flat=0.25))
    # the cornice round the roof edge, higher along the back
    for (ax, ay), (bx, by), h, w in (((x0, y0), (x1, y0), 0.5, 0.6), ((x1, y0), (x1, y1), 0.5, 0.6),
                                     ((x1, y1), (x0, y1), 0.9, 0.7), ((x0, y1), (x0, y0), 0.5, 0.6)):
        ins = Vector((cx - (ax + bx) / 2, cy - (ay + by) / 2)).normalized() * (w / 2 - 0.05)

        def hfn(t, ax=ax, ay=ay, bx=bx, by=by, h=h, ins=ins):
            gone = open_at(ax + (bx - ax) * t + ins.x * 3, ay + (by - ay) * t + ins.y * 3)
            return 0.0 if gone else h
        run = Vector((bx - ax, by - ay)).normalized() * 0.35
        parts += K.wall_run((ax + ins.x - run.x, ay + ins.y - run.y), (bx + ins.x + run.x, by + ins.y + run.y), zr,
                            hfn if holes else h, w, beam(), seg=0.8, name="cornice")
    for p in parts[-40:]:
        if p.name.startswith("cornice"):
            K.uv_box(p, 0.3)
    spires = {} if spires is None else spires
    for i, sx in enumerate(SPIRES):
        d = spires.get(i, "ok")
        if d == "gone":
            continue
        parts += spire(sx, y1 - 0.35, zr + 0.9, broken=None if d == "ok" else d)
    horns = {} if horns is None else horns
    for i, hx_ in enumerate(HORNS):
        if horns.get(i, "ok") == "gone":
            continue
        parts.append(horn(hx_, y1 - 1.1, zr + 0.4))
    if rings:
        for bx in beams_x:
            if not open_at(bx, -0.75):
                s, k = crater_at(bx, -0.75)
                parts += ring_blade(bx, 0.1 - 0.1, zr + 0.42 - (craters[k][4] * s if k is not None else 0))
    fins = {} if fins is None else fins
    for side, xe in ((-1, x0), (1, x1)):
        for i, fy in enumerate(FINS):
            k = fins.get((side, i), 1.0)
            if k <= 0:
                continue
            parts += fin(xe, fy, 5.4, 3.9, side, keep=k)
    struts = {} if struts is None else struts
    for i, sx in enumerate(STRUTS):
        d = struts.get(i, None)
        if d == "gone":
            continue
        parts += strut(sx, y0 - 3.1, y0 - 0.1, zr - 0.2, broken=d)
    # the door and, above it, a short prop with its plaque
    door = K.block(1.3, 0.2, 1.9, 0.0 - 0.2, y0 - 0.05, 0, 0, K.mat("sp_door", (40, 30, 24), rough=0.9), "door")
    arch = K.torus(0.65, 0.12, seg=12, rseg=4, mt=iron(), name="door_arch", axis="Y")
    K.place(arch, Matrix.Translation((-0.2, y0 - 0.12, 1.9)))
    parts += [door, arch]
    if door_prop:
        parts.append(K.board((-0.2, y0 - 0.3, 2.6), (-0.2, y0 - 0.1, zr - 0.2), 0.5, 0.5, iron(), "door_prop",
                             up=(0, -1, 0)))
        parts.append(K.board((-0.2, y0 - 0.45, zr - 1.8), (-0.2, y0 - 0.25, zr - 0.8), 0.7, 0.6, pale(), "plaque",
                             up=(0, -1, 0)))
    return parts


def tarbuild02(name="Tarbuild02"):
    return hall(**HALL)
