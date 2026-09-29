"""The pictures carve.Sprite makes, for the engine test that the game paints
a model exactly as the pipeline did (PaintedModelEngineTests.cs).

    python tools/sprite-replace/paintref.py <extracted data dir> <catalog dir> <out dir> feature:<name> ... texture:<name>:<world> ...

A feature is read as extract.py wrote it, <catalog dir>/sprites/<name>.png.
A 3DO texture is decoded from the game's textures/*.gaf in its world's
palette and cleared where lode.py clears it. Then Blender (BLENDER, or
blender on the path) puts each through carve.Sprite, bleed and gain, and
opaque(), and writes it to <out dir> with refs.json listing them. These
are pictures from the original game: keep <out dir> outside the repo.
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)


def sources(data_dir, catalog_dir, out, args):
    from PIL import Image
    import extract
    import lode
    os.makedirs(os.path.join(out, "source"), exist_ok=True)
    refs = []
    for arg in args:
        kind, name, *rest = arg.split(":")
        if kind == "feature":
            src, world = os.path.join(catalog_dir, "sprites", name + ".png"), ""
        else:
            world = rest[0]
            src = os.path.join(out, "source", name + ".png")
            d, f = lode.find_texture(data_dir, name)
            pal = extract.palette(os.path.join(data_dir, "data", "palettes", lode.PALETTES[world[:3].lower()]))
            idx = extract.decode(d, f)
            clear = {idx[0], f["key"]}
            img = Image.new("RGBA", (f["w"], f["h"]))
            for i, v in enumerate(idx):
                if v not in clear:
                    img.putpixel((i % f["w"], i // f["w"]), (pal[3 * v], pal[3 * v + 1], pal[3 * v + 2], 255))
            img.save(src)
        refs.append({"kind": kind, "name": name, "world": world, "source": src,
                     "file": "%s_%s.png" % (kind, name)})
    json.dump(refs, open(os.path.join(out, "refs.json"), "w"), indent=1)


def carved(out):
    import carve
    refs = json.load(open(os.path.join(out, "refs.json")))
    for r in refs:
        spr = carve.Sprite(r["source"])
        spr.opaque()
        spr.img.filepath_raw = os.path.join(out, r["file"])
        spr.img.file_format = "PNG"
        spr.img.save()
        r.update(gain=carve.ALBEDO_GAIN, size=[spr.w, spr.h])
        print("PAINTREF", r["kind"], r["name"], spr.w, spr.h, flush=True)
    json.dump(refs, open(os.path.join(out, "refs.json"), "w"), indent=1)


if __name__ == "__main__":
    if "--" in sys.argv:
        carved(sys.argv[sys.argv.index("--") + 2])
    else:
        data_dir, catalog_dir, out = sys.argv[1:4]
        sources(data_dir, catalog_dir, out, sys.argv[4:])
        subprocess.check_call([os.environ.get("BLENDER", "blender"), "-b", "--factory-startup", "-P",
                               os.path.abspath(__file__), "--", "carve", out])
