"""Veruna corner towers: one rounded outer corner each (vertower.py)."""
import kit
import vertower as vt
import verwall as vw

T = {
    "VerTow01": dict(corner="SW", x0=-0.9, x1=2.0, y0=-1.0, y1=1.6),
    "VerTow02": dict(corner="NW", x0=-0.94, x1=2.0, y0=-2.15, y1=0.14),
    "VerTow03": dict(corner="NE", x0=-1.94, x1=0.94, y0=-2.1, y1=0.14),
    "VerTow04": dict(corner="SE", x0=-1.94, x1=1.0, y0=-0.86, y1=1.64),
}

GROW = {"VerTow03": 0.5}

kit.FITDEF.update(vt.DEF)
for _n in T:
    kit.TABLES[_n] = T
    kit.FITKEYS[_n] = [("x0", 0.2), ("x1", 0.2), ("y0", 0.2), ("y1", 0.2), ("Hd", 0.3), ("bat", 0.2)]

    @kit.model(_n)
    def _b(m):
        p = kit.params(m.name, T)
        # sturdy: the two outer sides pushed out, the inner ones kept on the
        # grid lines where the walls meet, the deck at the shared wall top
        sx, sy = vt.CORNERS[p["corner"]]
        g = GROW.get(m.name, 0.45)
        if sx < 0:
            p["x0"] -= g
        else:
            p["x1"] += g
        if sy < 0:
            p["y0"] -= g
        else:
            p["y1"] += g
        p["Hd"] = vt.DEF["Hd"]
        p["bat"] = max(0.5, p.get("bat", vt.DEF["bat"]))
        vw.palette(m)
        m.col("deck", m.sample(0, 0, m.w, m.h // 2, pick=lambda r, g, b: r > 150 and r > b * 1.15), 0.9)
        vt.build(m, p)
