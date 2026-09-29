"""Debug overlays for the 2D reading, run with the system Python.

    python debug2d.py skel <out.png> <scale> <Name> [<Name> ...]
    python debug2d.py lobes <out.png> <scale> <Name> [<Name> ...]
"""
import colorsys
import sys

from PIL import Image, ImageDraw

import sprite2d as s2


def panel(name, scale, mode):
    spr = s2.Sprite(name)
    im = Image.open("%s/%s.png" % (s2.SPRITES, name)).convert("RGBA")
    im = im.resize((spr.w * scale, spr.h * scale), Image.NEAREST)
    bg = Image.new("RGBA", im.size, (84, 104, 64, 255))
    faded = im.copy()
    faded.putalpha(im.getchannel("A").point(lambda a: a // 3))
    bg.alpha_composite(faded)
    d = ImageDraw.Draw(bg)
    try:
        import models
        spec = models.MODELS.get(name, {})
    except Exception:
        spec = {}
    if mode == "skel":
        root = spec.get("root", (spr.hx, spr.hy))
        br = s2.skeleton_tree(spr.mask, root, prune=spec.get("prune", (3.0, 1.2)))
        for i, b in enumerate(br):
            rgb = colorsys.hsv_to_rgb((i * 0.618) % 1.0, 0.9, 1.0)
            col = tuple(int(255 * v) for v in rgb) + (255,)
            pts = [((c + 0.5) * scale, (r + 0.5) * scale) for c, r in b["px"]]
            d.line(pts, fill=col, width=2)
            d.text(pts[-1], str(i), fill=(255, 255, 255, 255))
        d.text((2, 2), "%s %d br" % (name, len(br)), fill=(255, 255, 255, 255))
    else:
        mask = spr.mask
        for c, r, rad in s2.pack_circles(mask):
            d.ellipse(((c + 0.5 - rad) * scale, (r + 0.5 - rad) * scale, (c + 0.5 + rad) * scale,
                       (r + 0.5 + rad) * scale), outline=(255, 255, 0, 255))
    x, y = (spr.hx + 0.5) * scale, (spr.hy + 0.5) * scale
    d.line((x - 6, y, x + 6, y), fill=(255, 0, 0, 255))
    d.line((x, y - 6, x, y + 6), fill=(255, 0, 0, 255))
    return bg


if __name__ == "__main__":
    mode, out, scale = sys.argv[1], sys.argv[2], int(sys.argv[3])
    ps = [panel(n, scale, mode) for n in sys.argv[4:]]
    W = sum(p.width for p in ps) + 8 * len(ps)
    sheet = Image.new("RGBA", (W, max(p.height for p in ps)), (40, 40, 40, 255))
    x = 0
    for p in ps:
        sheet.alpha_composite(p, (x, 0))
        x += p.width + 8
    sheet.save(out)
    print(out)
