"""Tarbuild03a and Tarbuild03b: the fortress with towers thrown down and
grey boulders heaped on its roof and spilled round its walls, where the
pictures show them."""
import math
import random

import kit as K
import fortress as F
from fortress import BODY, Z0

GREY = (72, 71, 69)
GREY_GAP = (10, 10, 10)
GREY_LIGHT = (124, 122, 118)
# how much darker the charred rubble is than the stone it fell from; the
# ruin's builder sets it
CHAR = 1.0


def _k(c, k=None):
    k = CHAR if k is None else k
    return tuple(int(v * k) for v in c)


def rubble_mat():
    return K.texmat("fo_boulders", lambda: K.tex_boulders(_k(GREY), (3, 3, 3), _k(GREY_LIGHT), n=256, count=90,
                                                          seed=67), rough=0.95)


def stones():
    return [K.mat("fo_stone_a", _k((80, 79, 77)), rough=0.9), K.mat("fo_stone_b", _k((54, 53, 52)), rough=0.9),
            K.mat("fo_stone_c", _k((104, 102, 99)), rough=0.9),
            K.texmat("fo_stone_char", lambda: K.tex_soot(_k((60, 59, 58)), (12, 12, 12), n=64, seed=68), rough=0.95)]


def soot_mats():
    return [K.mat("fo_soot_core", (12, 11, 11), rough=1.0),
            K.texmat("fo_soot", lambda: K.tex_soot((44, 42, 41), (14, 13, 13), n=128, seed=69), rough=1.0)]


def char_top():
    """Blackened tops for the walls and towers left standing."""
    return K.texmat("fo_char_top", lambda: K.tex_soot((40, 39, 38), (10, 10, 10), n=64, seed=70), rough=0.95)


def _into_body(cx, cy):
    """Pulls a point back along the ray to (cx, cy) until it is on the roof."""
    def clip(x, y):
        for k in range(12):
            if K.inside_poly(BODY, x, y):
                return x, y
            x, y = cx + (x - cx) * 0.85, cy + (y - cy) * 0.85
        return cx, cy
    return clip


TALUS = 1.4  # how far out from the walls spilled rubble reaches the ground


def _dist_out(x, y):
    d = 1e9
    for (ax, ay), (bx, by) in zip(BODY, BODY[1:] + BODY[:1]):
        dx, dy = bx - ax, by - ay
        t = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy)))
        d = min(d, math.hypot(x - ax - t * dx, y - ay - t * dy))
    return d


def ground(x, y):
    """The roof inside the walls; outside them a slope of spilled rubble
    running down to the ground, so a heap over a wall pours down its face."""
    if K.inside_poly(BODY, x, y):
        return Z0
    return max(0.0, Z0 * (1 - _dist_out(x, y) / TALUS))


def grit(specs, fn, seed=1, per=4):
    """A soft speckled spill of small stones round each heap's foot."""
    rnd = random.Random(seed)
    mats = [K.mat("fo_grit_a", (16, 16, 16), rough=1.0), K.mat("fo_grit_b", _k((92, 90, 88)), rough=1.0)]
    out = []
    for cx, cy, rx, ry, *_ in specs:
        for k in range(per):
            a = rnd.uniform(0, 2 * math.pi)
            r = rnd.uniform(0.95, 1.3)
            x, y = cx + rx * r * math.cos(a), cy + ry * r * math.sin(a)
            s = rnd.uniform(0.14, 0.28)
            out.append(K.rock((x, y, fn(x, y) + s * 0.2), (s, s * 0.8, s * 0.5), rnd.randrange(10 ** 6), mats[k % 2],
                              "grit"))
    return out


def ruin(heaps, keep_heap=None, seed=1, char=1.0, scorch=1.5, **damage):
    """The fortress with damage, charred boulder heaps (char scales their
    colour) on the roof and round the walls, a scorch round each heap on
    the roof, blackened wall tops, and, where the keep's small tower stood,
    a heap with red spire chunks in it."""
    global CHAR
    CHAR = char
    parts = F.fortress(seed=seed, **damage)
    ct = char_top()
    for p in parts:
        if p.name.startswith(("parapet", "tower_rim", "rim", "sawtooth", "tooth")):
            K.two_tone(p, ct)
    for i, sp in enumerate(heaps + ([keep_heap] if keep_heap else [])):
        if K.inside_poly(BODY, sp[0], sp[1]):
            parts += K.soot(sp[0], sp[1], sp[2] * scorch, sp[3] * scorch, ground, soot_mats(), seed=seed + 13 * i,
                            rot=sp[5] if len(sp) > 5 else 0.0, clip=_into_body(sp[0], sp[1]), seg=16)
    p, surf, ins = K.boulder_heaps(heaps, rubble_mat(), stones(), ground, seed=seed, per_area=0.5, size=(0.55, 1.05),
                                   rings=4, ring_seg=14)
    parts += p
    parts += grit(heaps, ground, seed + 3)
    if keep_heap is not None:
        p, ksurf, kins = K.boulder_heaps([keep_heap], rubble_mat(), stones() + [F.red_roof()],
                                         lambda x, y: Z0 + 0.4, seed=seed + 9, per_area=1.4, size=(0.45, 0.9),
                                         rings=4, ring_seg=14)
        parts += p
    return parts


def jag(amp, base, seed):
    return lambda t: max(0.0, base + K.jagged(t, amp, seed, freq=2.0))


def tarbuild03a(name="Tarbuild03a"):
    heaps = [(-7.0, 5.6, 2.4, 1.9, 1.4, 10), (-5.1, 0.5, 1.4, 2.0, 1.0), (-3.3, -4.8, 1.9, 1.9, 1.3),
             (-0.3, -6.6, 1.5, 1.2, 1.1), (2.6, -3.3, 1.2, 1.0, 0.9), (-9.0, 0.8, 1.5, 3.4, 1.2, 0, (-0.3, 0.0)),
             (8.5, 0.3, 1.0, 3.0, 0.8), (-3.8, 6.8, 2.0, 0.9, 0.8, 0, (0.0, 0.4)), (0.8, -8.6, 1.4, 0.7, 0.6)]
    damage = dict(towers_broken={"back_left": "gone", "inner_left": "gone", "back_right": (1, 3)},
                  keep_tower=False, walks_gone=("cross_ul", "cross_ll"),
                  parapet_height=lambda nm: {"left": jag(0.35, 0.3, 3), "back_l": jag(0.3, 0.2, 4)}.get(nm),
                  arc_keep=lambda t: t > 0.2,
                  thorns=lambda side, i: side > 0,
                  finials_keep=lambda x, y: not (x < -3 and y > 0) and not (abs(x) < 3 and y < 0))
    return ruin(heaps, keep_heap=(0.1, 3.9, 1.9, 1.5, 1.5), seed=31, char=0.52, scorch=1.9, **damage)


def tarbuild03b(name="Tarbuild03b"):
    heaps = [(-6.0, 4.6, 3.4, 2.6, 1.8, 10), (3.9, 5.3, 3.3, 1.8, 1.6), (-7.3, 0.4, 2.3, 2.3, 1.6),
             (8.3, -2.2, 1.7, 3.6, 1.6), (4.8, -5.4, 2.2, 2.0, 1.7), (-4.0, -5.2, 2.4, 2.3, 1.8),
             (0.0, -7.0, 1.6, 1.2, 1.3), (8.3, -7.2, 1.6, 1.4, 1.4), (-9.1, -1.0, 1.5, 4.0, 1.3, 0, (-0.3, 0.0)),
             (10.4, -1.5, 0.9, 3.5, 1.0, 0, (0.4, 0.0)), (-2.0, 7.2, 3.5, 0.9, 1.0, 0, (0.0, 0.4)),
             (-3.1, 1.2, 1.5, 2.0, 1.4), (3.5, 0.6, 1.1, 1.8, 1.2), (-7.9, -3.8, 1.6, 1.3, 1.4),
             (6.9, 3.1, 1.4, 1.2, 1.3)]
    damage = dict(towers_broken={"back_left": "gone", "inner_left": "gone", "inner_right": "gone",
                                 "front_right": "gone", "back_right": (1, 3), "front_left": (1, 4)},
                  wells_broken={0}, keep_tower=False, stubs=False,
                  walks_gone=("cross_ul", "cross_ll", "cross_lr", "back_r"),
                  parapet_height=lambda nm: {"left": jag(0.4, 0.15, 5), "right": jag(0.35, 0.3, 6),
                                             "cross_ur": jag(0.3, 0.3, 7), "back_l": jag(0.4, 0.1, 8)}.get(nm),
                  arc_keep=lambda t: 0.4 < t < 0.8,
                  thorns=lambda side, i: i in (2, 3) and side < 0,
                  finials_keep=lambda x, y: x < -5 and y < 0)
    return ruin(heaps, keep_heap=(0.2, 3.9, 1.8, 1.5, 1.4), seed=41, char=0.5, scorch=1.7, **damage)
