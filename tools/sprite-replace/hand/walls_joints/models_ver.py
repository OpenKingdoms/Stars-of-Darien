"""Veruna curtain walls, their joints and their ruins.

Every piece is one wall swept along a plan path and cut on grid lines:

  line    a straight run from (x0, y0) to (x1, y1), ends square
  diag    a run at ang degrees through (x, yf), cut east-west at yf and yb
  bend    two arms from the vertex (cx, cy): arm 1 leaves at a1 degrees and
          is cut by plane c1 (n1 names the side removed: x-, x+, y-, y+),
          arm 2 likewise; corners turn 90 degrees, bends 45

high is the side (L or R of the path's travel, arm 1 toward arm 2) where
the roof is high and the rafters show. Profile keys (zlo, zhi, foot ...)
override verwall.DEF for one piece.
"""
import math

import kit
import verwall as vw

T = {
    # east-west, high at the back (02, 05) or at the front (03, 08 with its gate)
    "VerWall02": dict(kind="line", x0=-2.13, y0=-0.12, x1=2.0, y1=-0.12, high="L"),
    "VerWall05": dict(kind="line", x0=-2.13, y0=-0.12, x1=2.06, y1=-0.12, high="L"),
    "VerWall03": dict(kind="line", x0=-2.0, y0=0.0, x1=2.13, y1=0.0, high="R"),
    "VerWall08": dict(kind="line", x0=-2.06, y0=0.0, x1=2.06, y1=0.0, high="R",
                      gate=(2.03, 0.82, 0.42, 0.45, 1.45, -1)),
    # north-south, high on the east (01, 06) or the west (04, 07)
    "VerWall01": dict(kind="line", x0=0.0, y0=-1.5, x1=0.0, y1=1.8, high="R"),
    "VerWall06": dict(kind="line", x0=0.0, y0=-1.5, x1=0.0, y1=1.8, high="R"),
    "VerWall04": dict(kind="line", x0=0.0, y0=-1.5, x1=0.0, y1=1.8, high="L"),
    "VerWall07": dict(kind="line", x0=0.0, y0=-1.5, x1=0.0, y1=1.8, high="L"),
    # diagonals, cut east-west at both ends
    "VerWall09": dict(kind="diag", x=-1.97, yf=-1.75, yb=1.44, ang=45.0, high="R"),
    "VerWall10": dict(kind="diag", x=-0.81, yf=-2.1, yb=0.6, ang=45.0, high="L"),
    "VerWall11": dict(kind="diag", x=1.22, yf=-2.3, yb=1.1, ang=135.0, high="R"),
    "VerWall12": dict(kind="diag", x=2.56, yf=-1.9, yb=1.4, ang=135.0, high="L"),
    # corners: arms leave the vertex, the roof high on the outside
    "VerJoint01": dict(kind="bend", cx=-0.09, cy=0.1, a1=270, c1=-1.5, n1="y-", a2=180, c2=-1.44, n2="x-",
                       high="R"),
    "VerJoint02": dict(kind="bend", cx=-0.22, cy=1.0, a1=90, c1=1.96, n1="y+", a2=0, c2=1.63, n2="x+",
                       high="R"),
    "VerJoint03": dict(kind="bend", cx=0.0, cy=0.34, a1=0, c1=1.31, n1="x+", a2=270, c2=-1.56, n2="y-",
                       high="R"),
    "VerJoint04": dict(kind="bend", cx=-0.3, cy=0.94, a1=180, c1=-1.56, n1="x-", a2=90, c2=1.83, n2="y+",
                       high="R"),
    # a north-south wall bending onto a diagonal
    "VerJoint05": dict(kind="bend", cx=-1.0, cy=0.56, a1=270, c1=-2.2, n1="y-", a2=45, c2=2.0, n2="y+",
                       high="R"),
    "VerJoint06": dict(kind="bend", cx=1.22, cy=-0.53, a1=225, c1=-2.0, n1="y-", a2=90, c2=2.46, n2="y+",
                       high="L"),
    "VerJoint07": dict(kind="bend", cx=-1.0, cy=-0.62, a1=315, c1=-2.0, n1="y-", a2=90, c2=2.46, n2="y+",
                       high="R"),
    "VerJoint08": dict(kind="bend", cx=1.5, cy=0.31, a1=270, c1=-2.25, n1="y-", a2=135, c2=1.93, n2="y+",
                       high="L"),
    # an east-west wall bending onto a diagonal
    "VerJoint09": dict(kind="bend", cx=1.15, cy=0.0, a1=225, c1=-2.4, n1="y-", a2=0, c2=3.6, n2="x+",
                       high="R"),
    "VerJoint10": dict(kind="bend", cx=-1.06, cy=0.0, a1=180, c1=-3.56, n1="x-", a2=315, c2=-2.4, n2="y-",
                       high="R"),
    "VerJoint11": dict(kind="bend", cx=-0.53, cy=-0.19, a1=180, c1=-3.5, n1="x-", a2=45, c2=1.96, n2="y+",
                       high="L"),
    "VerJoint12": dict(kind="bend", cx=0.56, cy=-0.19, a1=135, c1=1.9, n1="y+", a2=0, c2=3.56, n2="x+",
                       high="L"),
}

PROFILE = ("foot", "zb", "zt", "W", "zlo", "zhi", "ov", "ovl", "loops", "cope")
NORMALS = {"x-": (-1, 0), "x+": (1, 0), "y-": (0, -1), "y+": (0, 1)}


def unit(deg):
    a = math.radians(deg)
    return math.cos(a), math.sin(a)


PLINTH = ("stone", "stone2", "stone3", "mortar")


def cut(m, n, c, p):
    """An end cut on a grid line, the plinth battered back from it."""
    nx, ny = NORMALS[n] if isinstance(n, str) else n
    x, y = (c, 0.0) if isinstance(n, str) and nx else (0.0, c) if isinstance(n, str) else c
    # only a cut facing the camera shows its plinth leaning out; the others stay square
    bat = max(0.0, p.get("ebat", 0.55)) if ny < -0.9 else 0.0
    m.clip(x, y, nx, ny, batter=bat, zb=p.get("zb", vw.DEF["zb"]), keys=PLINTH)


def frame(m, p):
    """The piece's plan path (run long) with its end cuts set on m; the
    gate's distance along the path, if it has one."""
    if p["kind"] == "line":
        a, b = (p["x0"], p["y0"]), (p["x1"], p["y1"])
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        d = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
        # run past both ends and cut square there
        pts = [(a[0] - d[0] * 3, a[1] - d[1] * 3), (b[0] + d[0] * 3, b[1] + d[1] * 3)]
        cut(m, (-d[0], -d[1]), a, p)
        cut(m, d, b, p)
        gate = p.get("gate")
        if gate:
            gate = (gate[0] + 3,) + tuple(gate[1:])
        return pts, gate
    if p["kind"] == "diag":
        dx, dy = unit(p["ang"])
        # the centreline crosses the front cut at (x, yf); run it well past both cuts
        pts = [(p["x"] - dx * 4, p["yf"] - dy * 4), (p["x"] + dx * 10, p["yf"] + dy * 10)]
        cut(m, "y-", p["yf"], p)
        cut(m, "y+", p["yb"], p)
        return pts, None
    # a bend: from arm 1's far end, through the vertex, out along arm 2
    d1, d2 = unit(p["a1"]), unit(p["a2"])
    cx, cy = p["cx"], p["cy"]
    pts = [(cx + d1[0] * 6, cy + d1[1] * 6), (cx, cy), (cx + d2[0] * 6, cy + d2[1] * 6)]
    cut(m, p["n1"], p["c1"], p)
    cut(m, p["n2"], p["c2"], p)
    return pts, None


def profile(p, **extra):
    prof = {k: p[k] for k in PROFILE if k in p}
    prof.update(extra)
    return prof


def piece(m, p, **extra):
    """Builds the piece described by p; returns the path and profile."""
    pts, gate = frame(m, p)
    return pts, vw.build(m, pts, high=p["high"], gate=gate, **profile(p, **extra))


kit.FITDEF.update(vw.DEF)
kit.FITDEF["ebat"] = 0.55


def register(names, fit_keys):
    for n in names:
        kit.TABLES[n] = T
        kit.FITKEYS[n] = fit_keys

        @kit.model(n)
        def _b(m):
            p = kit.params(m.name, T)
            vw.palette(m)
            if p.get("gate"):
                m.col("arch", m.sample(17, 83, 26, 97, pick=lambda r, g, b: r > g * 1.05), 0.9)
                m.shade("panel", "stone", 1.3, 0.95)
            piece(m, p)


# the heights are shared by every piece (verwall.DEF), only placement is fitted
register([n for n, r in T.items() if r["kind"] == "line"],
         [("x0", 0.2), ("y0", 0.2), ("x1", 0.2), ("y1", 0.2), ("ebat", 0.2)])
register([n for n, r in T.items() if r["kind"] == "diag"],
         [("x", 0.3), ("yf", 0.3), ("yb", 0.3), ("ang", 4.0), ("ebat", 0.2)])
register([n for n, r in T.items() if r["kind"] == "bend"],
         [("cx", 0.3), ("cy", 0.3), ("c1", 0.3), ("c2", 0.3), ("ebat", 0.2)])
