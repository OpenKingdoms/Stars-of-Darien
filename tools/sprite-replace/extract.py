#!/usr/bin/env python3
"""Catalog every feature the original game draws as a sprite, from the
player's own game files, as the work list for 3D replacements.

    python tools/sprite-replace/extract.py <extracted data dir> <game dir> <out dir>

Writes, under <out dir> (keep it outside the repo: these are images from
the original game and are never committed):
  sprites/<feature>.png   the feature's first frame, transparent background
  catalog.json            one entry per feature: world, description,
                          category, footprint, height, sprite size and
                          hotspot, how many maps use it, status
  index.html              a gallery, most used first, with status

Status comes from the overrides folder: a feature with a model at
<repo>/unity/Assets/Overrides/Features/<feature>.glb is "3d".
"""
import html
import json
import os
import re
import struct
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
OVERRIDES = os.path.join(REPO, "unity", "Assets", "Overrides", "Features")

WORLD_PALETTE = {
    "aramon": "aramon_features.pcx", "veruna": "veruna_features.pcx",
    "zhon": "zhon_features.pcx", "taros": "taros_features.pcx",
    "all worlds": "aramon_features.pcx", "": "aramon_features.pcx",
}


def palette(path):
    d = open(path, "rb").read()
    return list(d[-768:])


def frame_header(d, off):
    w, h, ox, oy, key, comp, subs, _u, px = struct.unpack_from("<HHhhBBHII", d, off)
    return dict(w=w, h=h, ox=ox, oy=oy, key=key, comp=comp, subs=subs, px=px)


def decode(d, f):
    w, h = f["w"], f["h"]
    out = bytearray([f["key"]]) * (w * h)
    if f["subs"] > 0:
        for s in range(f["subs"]):
            sub = frame_header(d, struct.unpack_from("<I", d, f["px"] + 4 * s)[0])
            spx = decode(d, sub)
            dx, dy = f["ox"] - sub["ox"], f["oy"] - sub["oy"]
            for sy in range(sub["h"]):
                py = dy + sy
                if not 0 <= py < h:
                    continue
                for sx in range(sub["w"]):
                    px = dx + sx
                    v = spx[sy * sub["w"] + sx]
                    if 0 <= px < w and v != sub["key"]:
                        out[py * w + px] = v
        return out
    p = f["px"]
    if f["comp"] == 0:
        n = min(w * h, len(d) - p)
        out[:n] = d[p:p + n]
        return out
    for row in range(h):
        count = struct.unpack_from("<H", d, p)[0]
        p += 2
        end, x = p + count, 0
        while p < end:
            b = d[p]
            p += 1
            if b & 1:
                x += b >> 1
            elif b & 3 == 0:
                n = (b >> 2) + 1
                for i in range(n):
                    if x + i < w:
                        out[row * w + x + i] = d[p + i]
                p += n
                x += n
            elif b & 3 == 2:
                n = (b >> 2) + 1
                for i in range(n):
                    if x + i < w:
                        out[row * w + x + i] = d[p]
                p += 1
                x += n
        p = end
    return out


def gaf_entries(path):
    """entry name (lowercase) -> first frame header, from one GAF."""
    d = open(path, "rb").read()
    entries = {}
    for e in range(struct.unpack_from("<I", d, 4)[0]):
        eo = struct.unpack_from("<I", d, 12 + 4 * e)[0]
        name = d[eo + 8:eo + 40].split(b"\0")[0].decode("latin-1").lower()
        if struct.unpack_from("<H", d, eo)[0] > 0:
            entries[name] = frame_header(d, struct.unpack_from("<I", d, eo + 40)[0])
    return d, entries


def features(data_dir):
    rows = []
    root = os.path.join(data_dir, "data", "features")
    for dirpath, _, files in os.walk(root):
        for fn in files:
            if not fn.lower().endswith(".tdf"):
                continue
            s = open(os.path.join(dirpath, fn), encoding="latin-1").read()
            for m in re.finditer(r"\[([^\]]+)\][^\n{]*\s*\{(.*?)\n\s*\}", s, re.S):
                body = m.group(2)

                def g(k):
                    mm = re.search(r"\b" + k + r"\s*=\s*([^;]*);", body, re.I)
                    return mm.group(1).strip() if mm else ""
                if g("object") or not g("seqname"):
                    continue
                rows.append({
                    "name": m.group(1), "world": g("world").lower(), "description": g("description"),
                    "category": g("category"), "gaf": g("filename"), "seq": g("seqname"),
                    "footprint": [int(g("footprintx") or 1), int(g("footprintz") or 1)],
                    "height": int(g("height") or 0), "tdf": os.path.relpath(os.path.join(dirpath, fn), data_dir),
                })
    return rows


def map_usage(game_dir, data_dir, names):
    """How many map files mention each feature name (TNT feature tables)."""
    lower = {n.lower().encode("latin-1"): n for n in names}
    counts = {n: 0 for n in names}
    for base in (data_dir, game_dir):
        for dirpath, _, files in os.walk(base):
            for fn in files:
                if fn.lower().endswith(".tnt"):
                    blob = open(os.path.join(dirpath, fn), "rb").read().lower()
                    for k, n in lower.items():
                        if k + b"\0" in blob:
                            counts[n] += 1
    return counts


def main(data_dir, game_dir, out_dir):
    os.makedirs(os.path.join(out_dir, "sprites"), exist_ok=True)
    rows = features(data_dir)
    pals, gafs = {}, {}
    for r in rows:
        gaf_path = os.path.join(data_dir, "data", "anims", r["gaf"].lower() + ".gaf")
        if not os.path.exists(gaf_path):
            r["sprite"] = None
            continue
        if gaf_path not in gafs:
            gafs[gaf_path] = gaf_entries(gaf_path)
        d, entries = gafs[gaf_path]
        f = entries.get(r["seq"].lower())
        if not f or not f["w"] or not f["h"]:
            r["sprite"] = None
            continue
        pal_name = WORLD_PALETTE.get(r["world"], "aramon_features.pcx")
        if pal_name not in pals:
            pals[pal_name] = palette(os.path.join(data_dir, "data", "palettes", pal_name))
        pal = pals[pal_name]
        idx = decode(d, f)
        img = Image.new("RGBA", (f["w"], f["h"]))
        px = img.load()
        for i, v in enumerate(idx):
            if v != f["key"]:
                px[i % f["w"], i // f["w"]] = (pal[3 * v], pal[3 * v + 1], pal[3 * v + 2], 255)
        img.save(os.path.join(out_dir, "sprites", r["name"] + ".png"))
        r["sprite"] = {"w": f["w"], "h": f["h"], "hotspot": [f["ox"], f["oy"]]}
    usage = map_usage(game_dir, data_dir, [r["name"] for r in rows])
    for r in rows:
        r["maps"] = usage[r["name"]]
        r["status"] = "3d" if os.path.exists(os.path.join(OVERRIDES, r["name"] + ".glb")) else "sprite"
    rows.sort(key=lambda r: (-r["maps"], r["name"].lower()))
    json.dump(rows, open(os.path.join(out_dir, "catalog.json"), "w"), indent=1)

    done = sum(1 for r in rows if r["status"] == "3d")
    cards = []
    for r in rows:
        img = ('<img src="sprites/%s.png">' % html.escape(r["name"])) if r["sprite"] else "<i>no sprite</i>"
        cards.append('<div class="c %s">%s<b>%s</b><span>%s</span><span>%d x %d cells, h %d, %d maps</span></div>'
                     % (r["status"], img, html.escape(r["name"]), html.escape(r["description"] or r["category"]),
                        r["footprint"][0], r["footprint"][1], r["height"], r["maps"]))
    page = """<!doctype html><meta charset="utf-8"><title>Sprite features</title>
<style>body{font:13px sans-serif;background:#1d1f24;color:#ddd;margin:16px}
.g{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:8px}
.c{background:#2a2d34;padding:6px;display:flex;flex-direction:column;gap:2px;border-left:4px solid #b85}
.c.3d{border-left-color:#5b8}.c img{max-width:100%%;image-rendering:pixelated;background:#556}
span{color:#9aa;font-size:11px}</style>
<h1>Sprite features: %d of %d replaced with 3D</h1><div class="g">%s</div>""" % (done, len(rows), "".join(cards))
    open(os.path.join(out_dir, "index.html"), "w", encoding="utf-8").write(page)
    print("%d sprite features, %d with sprites, %d replaced; %s" %
          (len(rows), sum(1 for r in rows if r["sprite"]), done, os.path.join(out_dir, "index.html")))


if __name__ == "__main__":
    main(*sys.argv[1:4])
