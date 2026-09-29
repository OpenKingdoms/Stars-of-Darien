"""Aramon curtain walls and their ruins.

  line  a straight run from (x0, y0) to (x1, y1), ends square
  diag  a run at ang degrees through (x, yf), cut east-west at yf and yb

A buttress is (t, side, w0, w1, depth, height): t cells along the wall from
where its centreline crosses the front cut (from x0, y0 for a line), side
+1 on the left of the path's travel.
"""
import math

import arawall as aw
import kit

T = {
    "AraWall01": dict(kind="line", x0=-1.6, y0=-0.93, x1=1.6, y1=-0.93, H=3.8),
    "AraWall04": dict(kind="line", x0=-1.56, y0=-0.1, x1=1.6, y1=-0.1, H=3.8,
                      butt=[(1.65, -1, 2.1, 1.1, 0.95, 3.2)]),
    "AraWall05": dict(kind="diag", x=-1.25, yf=-1.8, yb=1.55, ang=45.0),
    "AraWall07": dict(kind="diag", x=-2.56, yf=-1.7, yb=2.1, ang=45.0,
                      butt=[(4.1, -1, 2.0, 1.1, 1.0, 3.3)]),
    "AraWall06": dict(kind="diag", x=2.1, yf=-1.45, yb=1.9, ang=135.0,
                      butt=[(4.0, 1, 2.0, 1.1, 1.0, 3.3)]),
    "AraWall08": dict(kind="diag", x=1.19, yf=-1.9, yb=1.4, ang=135.0),
}

PROFILE = ("D", "H", "ph", "mh")


def unit(deg):
    a = math.radians(deg)
    return math.cos(a), math.sin(a)


def sturdy(p):
    """The fitted piece made lower and thicker; buttresses follow."""
    q = dict(p)
    q["H"] = p.get("H", aw.DEF["H"]) * aw.STURDY_H
    q["D"] = p.get("D", aw.DEF["D"]) * aw.STURDY_D
    q["butt"] = [(b[0], b[1], b[2] * 1.1, b[3] * 1.1, b[4] * 1.1, b[5] * aw.STURDY_H) for b in p.get("butt", [])]
    return q


def piece(m, p, top=True):
    p = sturdy(p)
    prof = {k: p[k] for k in PROFILE if k in p}
    if p["kind"] == "line":
        a, b = (p["x0"], p["y0"]), (p["x1"], p["y1"])
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        d = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
        pts = [(a[0] - d[0] * 3, a[1] - d[1] * 3), (b[0] + d[0] * 3, b[1] + d[1] * 3)]
        m.clip(a[0], a[1], -d[0], -d[1])
        m.clip(b[0], b[1], d[0], d[1])
        ext = 3.0
    else:
        dx, dy = unit(p["ang"])
        pts = [(p["x"] - dx * 4, p["yf"] - dy * 4), (p["x"] + dx * 10, p["yf"] + dy * 10)]
        m.clip(0.0, p["yf"], 0.0, -1.0)
        m.clip(0.0, p["yb"], 0.0, 1.0)
        ext = 4.0
    butts = [(b[0] + ext,) + tuple(b[1:]) for b in p.get("butt", [])]
    return pts, aw.build(m, pts, buttresses=butts, top=top, **prof)


kit.FITDEF.update(aw.DEF)


def register(names, fit_keys):
    for n in names:
        kit.TABLES[n] = T
        kit.FITKEYS[n] = fit_keys

        @kit.model(n)
        def _b(m):
            p = kit.params(m.name, T)
            aw.palette(m)
            piece(m, p)


register([n for n, r in T.items() if r["kind"] == "line"],
         [("x0", 0.2), ("y0", 0.2), ("x1", 0.2), ("y1", 0.2), ("H", 0.3), ("D", 0.2)])
register([n for n, r in T.items() if r["kind"] == "diag"],
         [("x", 0.3), ("yf", 0.3), ("yb", 0.3), ("ang", 4.0), ("H", 0.3), ("D", 0.2)])
