#!/usr/bin/env python3
"""Lodestones as a work list for the sprite-to-3D pipeline.

    python tools/sprite-replace/lode.py <extracted data dir> <out dir>

A lodestone's 3DO is one card leaning back to face the classic camera,
painted with the stone. This finds that card in each lodestone model,
takes its texture from the player's own game files and turns it into the
picture the classic camera sees, in screen pixels with the anchor as the
hotspot, so batch.py can carve it like a feature sprite.

Writes <out dir>/sprites/<OBJECT>.png and <out dir>/catalog.json. The
images come from the original game and are never committed.
"""
import json
import os
import struct
import sys

from PIL import Image

import extract

# models that are painted cards leaning toward the classic camera
LODES = ["ARALODE", "TARLODE", "VERLODE", "ZONLODE", "ARAMANA", "TARMANA", "VERMANA", "ZONMANA",
         "ZONFIRE", "ZONGLYPH", "NPCTHESH"]
PALETTES = {"ara": "ara_textures.pcx", "tar": "tar_textures.pcx", "ver": "ver_textures.pcx",
            "zon": "zon_textures.pcx", "npc": "npc_textures.pcx"}


def find_texture(data_dir, name):
    """The texture archive holding a texture, and the texture's entry."""
    folder = os.path.join(data_dir, "data", "textures")
    for fn in sorted(os.listdir(folder)):
        if fn.lower().endswith(".gaf"):
            d, entries = extract.gaf_entries(os.path.join(folder, fn))
            for k, v in entries.items():
                if k.lower() == name.lower():
                    return d, v
    raise KeyError(name)


# built by hand where carving cannot find the shape
SHAPES = {"ARALODE": "lode_ara", "ZONLODE": "lode_zon"}


def cstr(b, o):
    return b[o:b.index(b"\0", o)].decode("latin-1")


def cards(b, off=0):
    """Every textured quad in a 3DO: (piece, texture, corners in 3DO units)."""
    out = []
    while True:
        _, nv, npr, _, _, _, _, oname, _, overt, oprim, osib, ochild = struct.unpack_from("<13i", b, off)
        verts = [struct.unpack_from("<3i", b, overt + 12 * i) for i in range(nv)]
        for i in range(npr):
            _, nidx, _, oidx, otex = struct.unpack_from("<5i", b, oprim + 32 * i)
            if otex and nidx == 4:
                idx = struct.unpack_from("<4h", b, oidx)
                out.append((cstr(b, oname), cstr(b, otex), [tuple(c / 65536 for c in verts[k]) for k in idx]))
        if ochild:
            out += cards(b, ochild)
        if not osib:
            return out
        off = osib


def main(data_dir, out_dir):
    os.makedirs(os.path.join(out_dir, "sprites"), exist_ok=True)
    rows = []
    for obj in LODES:
        pal_name = PALETTES[obj[:3].lower()]
        b = open(os.path.join(data_dir, "data", "objects3d", obj.lower() + ".3do"), "rb").read()
        # the stone is the tallest card; the logo plates and skulls are small
        piece, tex, quad = max(cards(b), key=lambda c: max(p[1] for p in c[2]) - min(p[1] for p in c[2]))
        d, f = find_texture(data_dir, tex)
        pal = extract.palette(os.path.join(data_dir, "data", "palettes", pal_name))
        idx = extract.decode(d, f)
        # textures mark their clear pixels with the colour in the corner
        clear = {idx[0], f["key"]}
        img = Image.new("RGBA", (f["w"], f["h"]))
        for i, v in enumerate(idx):
            if v not in clear:
                img.putpixel((i % f["w"], i // f["w"]), (pal[3 * v], pal[3 * v + 1], pal[3 * v + 2], 255))
        # the card on screen: x as it is, and a point at height y and depth
        # z lands z + y/2 above the anchor, so the card spans that tall
        xs = [p[0] for p in quad]
        top = max(quad, key=lambda p: p[1])
        bot = min(quad, key=lambda p: p[1])
        w = max(xs) - min(xs)
        span = (top[2] - bot[2]) + (top[1] - bot[1]) * 0.5
        pic = img.resize((max(1, round(w)), max(1, round(span))), Image.LANCZOS)
        hx = round(-min(xs))
        hy = round(span + bot[2] - bot[1] * 0.5)
        pic.save(os.path.join(out_dir, "sprites", obj + ".png"))
        rows.append({
            "name": obj, "world": obj[:3].lower(), "description": "Painted card", "category": "card",
            "piece": piece, "texture": tex, "footprint": [2, 2],
            "height": round(top[1]), "sprite": {"w": pic.width, "h": pic.height, "hotspot": [hx, hy]},
            "shape": SHAPES.get(obj, "lode"), "maps": 0, "status": "sprite",
        })
        print(obj, piece, tex, pic.size, "hotspot", (hx, hy), "height", round(top[1]))
    json.dump(rows, open(os.path.join(out_dir, "catalog.json"), "w"), indent=1)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
