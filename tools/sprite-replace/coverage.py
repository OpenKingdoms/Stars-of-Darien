#!/usr/bin/env python3
"""Which scenery the build draws in 3D. Walks every map in the player's
install (maps and kmap .tnt files in its archives and Maps/*.kmp), counts
each feature type's maps and placements, and sorts the types by how they
draw:

  hand-built       a model made by hand (some of these paint parts of
                   themselves at load too)
  painted at load  a carved model (okCarved, install.py) whose pictures
                   the game paints from the player's files (okPaint)
  engine 3DO       no model here, but the original draws it as a 3DO
  not scenery      waves (the game draws its own foam) and noise emitters
  flat             still the original's sprite on a card

It also walks every stage scenery breaks or burns into (featuredead and
featureburnt), placed or not, and lists those with no model, which the
game draws from an earlier stage's chunks.

    python tools/sprite-replace/coverage.py [--game <install>] [--models <folder>] [--out <flat list>]

--models is a built player's folder or a folder of <feature>.glb files,
by default unity/Assets/Overrides/Features beside these tools. A model
counts under the names the game tries: the feature's, its sprite
sequence's, then its 3DO's. The flat list goes to --out, most placed
first. It reads only names and counts from the game files.
"""
import argparse
import datetime
import json
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hpi  # noqa: E402

REPO = os.path.dirname(os.path.dirname(HERE))
FEATURES = os.path.join(REPO, "unity", "Assets", "Overrides", "Features")
DEFAULT_GAME = "C:/GOG Games/Total Annihilation Kingdoms"
ARCHIVES = (".hpi", ".ufo", ".ccx", ".kmp")
KINDS = ("hand-built", "painted at load", "engine 3DO", "not scenery", "flat", "unknown")


def archives(game):
    """The install's archives, base game first, then the expansion and
    patches, then downloaded maps, so a later one's file wins."""
    root = sorted(f for f in os.listdir(game) if f.lower().endswith(ARCHIVES))

    def rank(f):
        n = f.lower()
        return (2 if n.startswith("v") and "rocket" in n else 1 if n.startswith("ip") else 0, n)
    out = [os.path.join(game, f) for f in sorted(root, key=rank)]
    maps = os.path.join(game, "Maps")
    if os.path.isdir(maps):
        out += [os.path.join(maps, f) for f in sorted(os.listdir(maps)) if f.lower().endswith(ARCHIVES)]
    return out


def feature_defs(tdfs):
    """name (lower) -> def, from the features' .tdf text."""
    defs = {}
    for text in tdfs:
        for m in re.finditer(r"\[([^\]]+)\][^\n{]*\s*\{(.*?)\n\s*\}", text, re.S):
            body = m.group(2)

            def g(k):
                mm = re.search(r"\b" + k + r"\s*=\s*([^;]*);", body, re.I)
                return mm.group(1).strip() if mm else ""
            name = m.group(1).strip()
            defs[name.lower()] = {"name": name, "object": g("object"), "seq": g("seqname"),
                                  "category": g("category"), "world": g("world"), "description": g("description"),
                                  "dead": g("featuredead"), "burnt": g("featureburnt")}
    return defs


def tnt_features(blob):
    """Placements of each feature name in one .tnt."""
    w, h = struct.unpack_from("<II", blob, 4)
    off_layer, off_names, n_names = struct.unpack_from("<III", blob, 0x14)
    names = []
    for i in range(n_names):
        rec = blob[off_names + 132 * i + 4:off_names + 132 * (i + 1)]
        names.append(rec.split(b"\0")[0].decode("latin-1"))
    counts = {}
    if off_layer + 2 * w * h > len(blob):
        return counts
    for v in struct.unpack_from("<%dH" % (w * h), blob, off_layer):
        if v < 0xFFF0 and v < len(names):
            counts[names[v]] = counts.get(names[v], 0) + 1
    return counts


def model_folder(models):
    """The folder of feature models in a player build or a plain folder."""
    for sub in (("Stars of Darien_Data", "StreamingAssets", "Assets", "Overrides", "Features"),
                ("StreamingAssets", "Assets", "Overrides", "Features"), ("Assets", "Overrides", "Features")):
        p = os.path.join(models, *sub)
        if os.path.isdir(p):
            return p
    return models


def glb_kind(path):
    with open(path, "rb") as f:
        head = f.read(20)
        j = json.loads(f.read(struct.unpack_from("<I", head, 12)[0]))
    return "painted at load" if any("okCarved" in n.get("extras", {}) for n in j.get("nodes", [])) else "hand-built"


def draw_kind(d, glbs):
    """How a def draws, and its model when it has one."""
    for n in (d["name"], d["seq"], d["object"]):
        if n and n.lower() in glbs:
            return glb_kind(glbs[n.lower()]), glbs[n.lower()]
    if d["object"]:
        return "engine 3DO", None
    if d["category"].lower() in ("waves", "noise") or not d["seq"]:
        return "not scenery", None
    return "flat", None


def stage_rows(defs, glbs):
    """Every stage another def breaks or burns into, with how it draws."""
    keys = {d[k].lower() for d in defs.values() for k in ("dead", "burnt") if d[k]}
    rows = []
    for key in sorted(keys):
        d = defs.get(key)
        if d:
            kind, _ = draw_kind(d, glbs)
            rows.append(dict(name=d["name"], kind=kind, world=d["world"], category=d["category"],
                             description=d["description"]))
    return rows


def survey(game, models):
    tdfs, maps = [], {}
    for path in archives(game):
        try:
            a = hpi.Archive(path)
        except (ValueError, OSError, struct.error):
            continue
        for f in a.files:
            if f.startswith("features/") and f.endswith(".tdf"):
                tdfs.append(a.read(f).decode("latin-1"))
            elif f.endswith(".tnt") and (f.startswith("maps/") or f.startswith("kmap/")):
                maps[os.path.basename(f)[:-4]] = (a, f)
    defs = feature_defs(tdfs)
    folder = model_folder(models)
    glbs = {f[:-4].lower(): os.path.join(folder, f) for f in os.listdir(folder) if f.lower().endswith(".glb")} \
        if os.path.isdir(folder) else {}
    used = {}
    for name, (a, f) in sorted(maps.items()):
        try:
            counts = tnt_features(a.read(f))
        except (ValueError, struct.error):
            continue
        for feat, n in counts.items():
            u = used.setdefault(feat.lower(), {"name": feat, "maps": 0, "placements": 0})
            u["maps"] += 1
            u["placements"] += n
    rows = []
    for key, u in used.items():
        d = defs.get(key)
        kind = draw_kind(d, glbs)[0] if d else "unknown"
        rows.append(dict(u, kind=kind, world=d["world"] if d else "", category=d["category"] if d else "",
                         description=d["description"] if d else ""))
    return len(maps), folder, rows, stage_rows(defs, glbs)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--game", default=os.environ.get("OK_GAME_DIR") or DEFAULT_GAME)
    ap.add_argument("--models", default=FEATURES)
    ap.add_argument("--out")
    ap.add_argument("--title", default="Stars of Darien")
    a = ap.parse_args(argv)
    if not os.path.isdir(a.game):
        print("COVERAGE no game install at %s" % a.game)
        return 1
    nmaps, folder, rows, stages = survey(a.game, a.models)
    print("COVERAGE %d maps in %s use %d feature types; models from %s" % (nmaps, a.game, len(rows), folder))
    for k in KINDS:
        sel = [r for r in rows if r["kind"] == k]
        print("COVERAGE %-16s %4d types, %6d placements" % (k, len(sel), sum(r["placements"] for r in sel)))
    flat = sorted((r for r in rows if r["kind"] == "flat"), key=lambda r: (-r["placements"], r["name"].lower()))
    print("COVERAGE FLAT %d feature types still draw flat" % len(flat))
    bare = sorted((r for r in stages if r["kind"] == "flat"), key=lambda r: r["name"].lower())
    print("COVERAGE STAGES %d stages scenery breaks or burns into, %s" % (len(stages), ", ".join(
        "%d %s" % (sum(r["kind"] == k for r in stages), k) for k in KINDS if any(r["kind"] == k for r in stages))))
    print("COVERAGE NO MODEL %d stages have no model%s" % (len(bare), ": " + " ".join(r["name"] for r in bare) if bare else ""))
    if a.out:
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            f.write("# Features that still draw flat in %s, %s\n" % (a.title, datetime.date.today().isoformat()))
            f.write("# %d types over %d maps in %s. Models from %s.\n" % (len(flat), nmaps, a.game, folder))
            f.write("# placements  maps  name  world  category  description\n")
            for r in flat:
                f.write("%6d %4d  %s  %s  %s  %s\n" % (r["placements"], r["maps"], r["name"], r["world"],
                                                        r["category"], r["description"]))
            f.write("# %d stages with no model, drawn from an earlier stage's chunks\n" % len(bare))
            for r in bare:
                f.write("stage  %s  %s  %s  %s\n" % (r["name"], r["world"], r["category"], r["description"]))
        print("COVERAGE flat list in %s" % a.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
