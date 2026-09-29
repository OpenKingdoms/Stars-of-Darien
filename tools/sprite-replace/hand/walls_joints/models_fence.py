"""Veruna split-rail fences, knocked about: posts standing or leaning,
rails between them, others fallen so they splay or cross.

Each piece is read off the drawing: an end at sprite pixel (col, row)
standing z cells up is the point X(col), Y(row, z), z. Rails are split
timbers (a five-sided rod), posts squared timbers with a pale cut top.
"""
import math

import kit

# (col, row, z) ends; posts are (col, row at the foot, height, lean_col, lean_row) with the top read off too
T = {
    "VerFence08": dict(
        posts=[((48, 12.5, 0.0), (48, 2.5, None)), ((92, 12.5, 0.0), (92, 1.0, None)),
               # the left post leans over toward the fence
               ((2, 13, 0.0), (10, 4, None))],
        rails=[((49, 4.5, None), (91, 3.5, None)), ((49, 9.5, None), (91, 9.0, None)),
               ((4, 9.5, None), (47, 9.5, None)),
               # the fallen top rail: one end still on the post, the other on the ground
               ((46, 5.0, None), (13, 17.0, 0.06))],
        line=-0.15),
    "VerFence11": dict(
        posts=[((4.5, 8, 0.0), (4.5, 0.5, None))],
        rails=[((5, 4, None), (17, 78, 0.07)), ((5, 4, None), (30, 76, 0.07)),
               ((3, 42, 0.3), (26, 36, 0.3)), ((8, 80, 0.07), (35, 79, 0.07))],
        line=None),
    "VerFence12": dict(
        posts=[((21.5, 84.5, 0.0), (21.5, 70, None))],
        rails=[((21, 9, 0.07), (21, 70, None)), ((5, 9, 0.07), (21, 70, None)),
               # the short broken rail lies on the ground where it crosses the others
               ((1, 3, 0.07), (28, 5, 0.07)), ((8, 30, 0.07), (27, 42, 0.07))],
        line=None),
}


# the owner's sturdy rule: posts a little lower than the drawing, and thick
LOW = 0.9


def build(m, p):
    wood = m.sample(0, 0, m.w, m.h, pick=lambda r, g, b: r > 110 and r > b * 1.15)
    m.col("wood", tuple(v * 0.95 for v in wood), 0.9)
    m.shade("wood2", "wood", 0.78, 0.9)
    m.shade("endgrain", "wood", 1.2, 0.9)
    posts = []

    def pt(c, r, z, y=None):
        if y is None:
            y = m.Y(r, z)
        return (m.X(c), y, z)
    for (c0, r0, z0), (c1, r1, _) in p["posts"]:
        base = pt(c0, r0, z0)
        # the top stands in the post's own ground line: its height follows from its row
        y = base[1]
        z1 = (m.hy - 16 * y - r1) / 8.0 * LOW
        top = (m.X(c1), y, z1)
        posts.append((base, top))
        # a touch more girth than the drawing's two pixels, so a post is not a stick
        m.beam("wood2", base, top, 0.26, 0.26)
        d = [top[i] - base[i] for i in range(3)]
        L = math.sqrt(sum(v * v for v in d))
        cap = [top[i] + d[i] / L * 0.015 for i in range(3)]
        m.beam("endgrain", top, cap, 0.24, 0.24)
    for i, (a, b) in enumerate(p["rails"]):
        ends = []
        for c, r, z in (a, b):
            if z is None:
                # an end on a post: on the post's line, at the height its row gives
                near = min(posts, key=lambda q: abs(q[0][0] - m.X(c)))
                y = near[0][1] + 0.12
                z = (m.hy - 16 * y - r) / 8.0 * LOW
                ends.append((m.X(c), y, z))
            else:
                ends.append(pt(c, r, z))
        rail(m, "wood" if i % 2 == 0 else "wood2", ends[0], ends[1], 0.1)


def rail(m, key, a, b, r):
    """A split rail: a rough five-sided timber from a to b."""
    m.rod(key, a, b, r, seg=5)


for _n in T:
    kit.TABLES[_n] = T
    kit.FITKEYS[_n] = []

    @kit.model(_n)
    def _b(m):
        build(m, kit.params(m.name, T))
