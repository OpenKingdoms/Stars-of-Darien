"""Per-model settings for the creon family: Creon's rocks and standing
stones, the Volcano sacred stones, the well and the palace.

kind picks the builder (stones.BUILDERS and the bespoke modules); the rest
are its parameters. 'tone' scales the drawing's mean colour for the
model's own: the Iron Plague's island is volcanic, so its stone is darker
than the drawing. Pixels named are sprite pixels (col, row).
"""

MODELS = {}

# Rocks: basalt boulders, a dome over each column, 85 percent of the
# feature's height at the tallest column
ROCK = {"kind": "rock", "tone": 0.8}
for n in ("CRERock01", "CRERock02", "CRERock03", "CRERock05", "CRERock06", "CRERock07", "CRERock08",
          "CRERock09", "CRERock10", "CRERock12", "CRERock13"):
    MODELS[n] = dict(ROCK)

# Standing stones: one ring style, dark tuff with the carving on every
# stone bigger than carve_min pixels
HENGE = {"kind": "henge", "tone": 0.85, "carve_min": 180, "min_thick": 0.55, "min_rise": 0.4}
for i in range(1, 24):
    MODELS["CREHenge%02d" % i] = dict(HENGE)

# The Volcano sacred stones: basalt rings round a star of lava (mana.py);
# their colours are set there, not matched to the drawing's
for n in ("CReMana01", "CREMana02", "CREMana03"):
    MODELS[n] = {"kind": "mana", "calib": False, "scale": 4}

# the well: a basin under a gable roof of planks (well.py)
MODELS["CreWell01"] = {"kind": "well", "calib": False, "scale": 4, "eave_row": 28, "ridge_col": 25}

# the palace: a marble terrace, corner towers, onion domes and a spire (palace.py)
MODELS["CreBuild01"] = {"kind": "palace", "calib": False, "scale": 2}

ORDER = tuple(MODELS)

# dark regions that are standing slabs of their own, not the face under a top
SLABS = {
    "CREHenge03": [(35, 60)],
    "CREHenge13": [(35, 80)],
    "CREHenge14": [(40, 50)],
}
for n, px in SLABS.items():
    MODELS[n] = dict(MODELS[n], slabs=px)

# capstones on their supports: a top pixel, the underside's height and the
# stone's thickness, in cells
RAISE = {
    "CREHenge13": [(40, 30, 2.6, 0.6)],
    "CREHenge14": [(30, 15, 2.8, 0.6)],
}
for n, caps in RAISE.items():
    MODELS[n] = dict(MODELS[n], **{"raise": caps})

