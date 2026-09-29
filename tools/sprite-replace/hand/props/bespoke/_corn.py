"""The bespoke corn patch builder, run inside Blender, for the six Aramon
corn fields (Aracrop07 to 12). Each field's script gives its own rows,
spans and soil from its picture; this holds the plant and the colouring.

The plants are as the table built them (compact clumps along each row, a
tassel band per row). The colouring is the picture's: the lower leaves and
stalks deep saturated green, the middle leaves leaf green, lime and khaki
only on the sunlit tips of the upper leaves, pinkish-beige tassels and
brown earth, dark red-brown in the furrows.
"""
import math
import random

from _common import kit, mk
from mathutils import Vector, noise

TAU = 2 * math.pi

# the six pictures share one palette (sampled 2026-09-29), dark to light
DEEP = ((10, 24, 4), (16, 33, 6), (16, 41, 6), (17, 54, 5), (41, 77, 14))
GREEN = ((41, 77, 14), (51, 79, 23), (51, 93, 35), (71, 107, 41), (65, 118, 43))
LIME = ((74, 94, 25), (105, 107, 32), (115, 107, 29))
KHAKI = ((133, 127, 67), (146, 147, 49))
TASSEL = ((108, 82, 66), (123, 99, 87), (123, 108, 91), (134, 124, 107), (148, 136, 121))
SOIL = ((33, 24, 16), (16, 8, 8))


def rows_from_bands(hy, bands, h):
    """Row positions from the picture's pale bands (each row's tassels), their
    tops at the built height, seen by the true tilted camera that draws a
    cell at 0.894 of the oblique rule's 16 and 8 pixels."""
    return [round(((hy - b) / 0.894 - 8 * (h + 0.1)) / 16.0, 3) for b in bands]


def ramp(cols, t):
    """A colour along a list of colours, t from 0 to 1."""
    t = min(1.0, max(0.0, t)) * (len(cols) - 1)
    i = min(len(cols) - 2, int(t))
    return kit.mix(cols[i], cols[i + 1], t - i)


def field(p):
    rnd = random.Random(p["seed"])
    parts = []
    soil_lit, soil_dark = p.get("soil", SOIL)
    soil_m = kit.matte(kit.mat("soil", kit.brightest(soil_lit, soil_dark), rough=1.0))
    stalk_m = kit.matte(kit.mat("stalk", DEEP[-1], rough=0.8))
    leaf_m = kit.matte(kit.mat("leaf", tuple(min(255.0, v * p.get("sun", 1.0)) for v in kit.brightest(*(GREEN + LIME + KHAKI))),
                                rough=0.75))
    tassel_m = kit.mat("tassel", TASSEL[-1], rough=0.9)
    ys, h = p["rows"], p["h"]
    spans = p.get("spans") or [p["span"]] * len(ys)
    sp = p.get("spacing", 0.6)

    # hilled earth following each row's own ends, ragged at its edges
    y_lo, y_hi = min(ys) - p.get("soil_front", 0.2), max(ys) + p.get("soil_back", 0.6)
    nx, ny = 10, max(6, 3 * len(ys))
    order = sorted(zip(ys, spans))

    def span_at(y):
        if y <= order[0][0]:
            return order[0][1]
        for (y0, s0), (y1, s1) in zip(order, order[1:]):
            if y <= y1:
                return s0 if y - y0 < y1 - y else s1
        return order[-1][1]
    verts, faces, ridge_of = [], [], []
    for j in range(ny + 1):
        y = y_lo + (y_hi - y_lo) * j / ny
        xa, xb = span_at(y)
        for i in range(nx + 1):
            x = xa + (xb - xa) * i / nx
            inner = min(i, nx - i, j, ny - j) > 0
            ridge = max(math.cos(math.pi * (y - r) / sp) ** 2 if abs(y - r) < sp / 2 else 0.0 for r in ys)
            z = (0.02 + 0.06 * ridge + rnd.uniform(-0.01, 0.01)) if inner else 0.0
            yy = y
            if not inner:
                x += (0.12 if i == 0 else -0.12 if i == nx else 0.0) * rnd.uniform(0.2, 1.0)
                rag = p.get("soil_rag", 0.35)
                yy += (rag if j == 0 else -rag if j == ny else 0.0) * rnd.uniform(0.2, 1.0)
            verts.append((x + (rnd.uniform(-0.05, 0.05) if inner else 0.0), yy, z))
            ridge_of.append(ridge)
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            faces.append((a, a + 1, a + nx + 2, a + nx + 1))
    soil = mk.mesh("soil", verts, faces, soil_m)
    # the hills brown, the furrows between them the picture's red-black
    furrow = p.get("furrow", 0.6)
    kit.paint(soil, lambda pp, co, nn, li: kit.mix(soil_dark, soil_lit, (1.0 - furrow) + furrow * min(
        1.0, max(0.0, (co.z - 0.02) / 0.05)) + 0.25 * noise.noise(co * 3.0)))
    parts.append(soil)

    per = p["per_row"]
    y_first, y_last = min(ys), max(ys)
    no_edge = {tuple(e) for e in p.get("no_edge", ())}
    up_share = p.get("khaki", 0.35)
    for ri, (y0, (xa, xb)) in enumerate(zip(ys, spans)):
        n = max(2, int(round(per * (xb - xa) / (spans[0][1] - spans[0][0]))))
        for k in range(n):
            out = Vector(((k == n - 1) - (k == 0), (y0 == y_last) - (y0 == y_first), 0))
            x = xa + (xb - xa) * k / (n - 1) + rnd.uniform(-0.07, 0.07)
            y = y0 + rnd.uniform(-0.07, 0.07)
            hh = h * rnd.uniform(0.84, 1.06)
            base = Vector((x, y, 0.03))
            topv = base + Vector((rnd.uniform(-0.08, 0.08), rnd.uniform(-0.08, 0.08), hh))
            st = kit.rod(tuple(base), tuple(topv), p["stalk_r"][0], p["stalk_r"][1], seg=3, mat=stalk_m, name="stalk")
            kit.paint(st, lambda pp, co, nn, li: ramp(DEEP[1:], co.z / h))
            parts.append(st)
            phi = rnd.choice((0.0, math.pi)) + rnd.uniform(-0.4, 0.4)
            if out.length:
                phi = math.atan2(out.y, out.x) + rnd.uniform(-0.5, 0.5)
            nl = p.get("leaves", 6)
            ne = p.get("edge_leaves", 3) if out.length and (ri, int(out.x)) not in no_edge else 0
            for q in range(nl + ne):
                if q < nl:
                    t = 0.18 + 0.62 * q / (nl - 1) + rnd.uniform(-0.04, 0.04)
                    a = phi + math.pi * q + rnd.uniform(-0.6, 0.6)
                    far = 1.0
                else:
                    t = rnd.uniform(0.2, 0.55)
                    a = math.atan2(out.y, out.x) + rnd.uniform(-1.0, 1.0)
                    far = p.get("edge_len", 0.8)
                node = base + (topv - base) * t
                d = Vector((math.cos(a), math.sin(a), 0))
                ln = p.get("leaf_len", 0.48) * (1.1 - 0.45 * t) * rnd.uniform(0.8, 1.15) * far
                rise = rnd.uniform(0.3, 0.55)
                wl = p.get("leaf_w", 0.11)
                pts = [node, node + d * ln * 0.45 + Vector((0, 0, ln * rise * 1.1)),
                       node + d * ln + Vector((0, 0, ln * (rise * 0.3 - 0.25)))]
                lf = kit.ribbon([tuple(v) for v in pts], [wl * 0.55, wl, wl * 0.12], mat=leaf_m, name="leaf")
                hf = (node.z - base.z) / h  # how high on the plant the leaf springs
                r = rnd.random()
                g0 = rnd.uniform(*p.get("g0", (0.0, 0.5)))

                def col(pp, co, nn, li, n0=node, ln=ln, hf=hf, r=r, g0=g0):
                    s = min(1.0, (co - n0).length / ln)  # along the leaf, 0 at the stalk
                    z = co.z / h
                    if hf < p.get("deep_to", 0.42):
                        # the lower leaves in the rows' shade
                        return ramp(DEEP, 0.15 + 0.55 * z + 0.25 * s + 0.2 * g0)
                    c = ramp(GREEN, g0 + 0.5 * s + 0.3 * (z - 0.5))
                    if hf > p.get("lit_from", 0.55) and r < p.get("lime", 0.55):
                        # sunlit upper leaves: lime along their length, khaki at
                        # the tips of the faces turned to the sky
                        c = kit.mix(c, ramp(LIME, g0 * 2.0), min(1.0, 0.3 + s))
                        if r < up_share and abs(nn.z) > 0.55 and s > 0.55:
                            c = kit.mix(c, ramp(KHAKI, g0 * 2.0), min(1.0, (s - 0.55) * 3.0))
                    if hf > p.get("lit_from", 0.55):
                        c = tuple(min(255.0, v * p.get("sun", 1.0)) for v in c)
                    return c
                kit.paint(lf, col)
                parts.append(lf)
            # tassel: pinkish-beige spikes fanning up from the top
            tv, tf = [], []
            tw, nt = p.get("tassel_w", 0.13), p.get("tassel_n", 5)
            for q in range(nt):
                a = phi + TAU * q / nt + rnd.uniform(-0.4, 0.4)
                d = Vector((math.cos(a), math.sin(a), 0))
                s = Vector((-d.y, d.x, 0))
                tl = p.get("tassel", 0.3) * rnd.uniform(0.8, 1.2)
                up = rnd.uniform(0.8, 1.0)
                b0 = topv - Vector((0, 0, 0.06))
                i0 = len(tv)
                tv += [tuple(b0 - s * tw / 2), tuple(b0 + s * tw / 2),
                       tuple(b0 + d * tl * (1.0 - up * 0.5) + Vector((0, 0, tl * up)))]
                tf.append((i0, i0 + 1, i0 + 2))
            ts = mk.mesh("tassel", tv, tf, tassel_m)
            tcols = [ramp(TASSEL, rnd.uniform(0.25, 1.0)) for _ in range(nt)]
            kit.paint(ts, lambda pp, co, nn, li, tc=tcols, b=topv.z: kit.mix(TASSEL[0], tc[pp.index], min(
                1.0, 0.4 + (co.z - b + 0.06) / 0.2)))
            parts.append(ts)
            if rnd.random() < p.get("ears", 0.25):
                a = phi + math.pi / 2 + rnd.uniform(-0.5, 0.5)
                d = Vector((math.cos(a), math.sin(a), 0))
                e0 = base + (topv - base) * rnd.uniform(0.45, 0.6)
                ear = kit.rod(tuple(e0), tuple(e0 + d * 0.12 + Vector((0, 0, 0.26))), 0.05, 0.015, seg=4,
                              mat=leaf_m, name="ear")
                kit.paint(ear, lambda pp, co, nn, li: ramp(LIME, rnd.uniform(0.0, 1.0)))
                parts.append(kit.smooth(ear, 60))
    for k, (x, y, a, ln) in enumerate(p.get("strays", ())):
        d = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))
        node = Vector((x, y, 0.35))
        pts = [node, node + d * ln * 0.3 + Vector((0, 0, 0.12)), node + d * ln * 0.7 + Vector((0, 0, 0.05)),
               node + d * ln + Vector((0, 0, -0.2))]
        lf = kit.ribbon([tuple(v) for v in pts], [0.06, 0.1, 0.08, 0.015], mat=leaf_m, name="stray%d" % k)
        kit.paint(lf, lambda pp, co, nn, li: GREEN[2])
        parts.append(lf)
    return parts
