"""Bark tone for the bespoke Zhon snags, run inside Blender.

The texture is a colour ramp of the drawing's own colours from its darkest
to its lightest, laid on a blotchy pattern, so the pale grey-green lights
and the near-black cracks come back in their drawn share. fit() then sets
the ramp's contrast and the vertex level until the classic render's
luminance quartiles meet the drawing's, and nudges the hue to its mean.
"""
import math
import os

import numpy as np

import common
import kit

LUMA = np.array([0.299, 0.587, 0.114])


def _lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


class Bark:
    """The ramp texture: pattern ranks, the drawing's colours by rank, and
    the Blender image they are written to."""

    def __init__(self, spr, seed=11, size=128, n=48, top=0.97):
        rng = np.random.default_rng(seed)
        blob = kit._norm01(kit._blur(rng.random((size, size)), 7, 5))
        mid = kit._norm01(kit._blur(rng.random((size, size)), 3, 2.5))
        fine = kit._norm01(kit._blur(rng.random((size, size)), 1.2, 1.2))
        v = (0.5 * blob + 0.35 * mid + 0.15 * fine).ravel()
        self.rank = (np.argsort(np.argsort(v)) / (v.size - 1)).reshape(size, size)
        lum, rgb = spr.lum[spr.mask], spr.rgb[spr.mask]
        o = np.argsort(lum)
        lum, rgb = lum[o], rgb[o]
        self.q = np.linspace(0.01, 0.99, n)
        idx = (self.q * (len(lum) - 1)).astype(int)
        win = max(2, len(lum) // (2 * n))
        self.C = _lin(np.array([rgb[max(0, i - win):i + win + 1].mean(0) for i in idx]))
        L = self.C @ LUMA
        self.Lrel = np.maximum(L / max(float(np.interp(top, self.q, L)), 1e-4), 1e-3)
        self.size = size
        self.g = 1.0
        self.tint = np.ones((n, 3))
        self.img = kit.image("bark", self.pixels(1.0))

    def pixels(self, g):
        C = self.C * self.tint / (self.C * self.tint @ LUMA)[:, None] * (self.Lrel ** g)[:, None]
        a = np.ones((self.size, self.size, 4))
        for ch in range(3):
            a[..., ch] = _srgb(np.interp(self.rank, self.q, C[:, ch]))
        return a

    def set(self, g):
        """A fresh image at contrast g in every node that held the last one
        (a packed image edited in place can render from its old pixels)."""
        import bpy
        self.g = g
        old, self.img = self.img, kit.image("bark", self.pixels(g))
        for m in bpy.data.materials:
            for n in (m.node_tree.nodes if m.node_tree else ()):
                if n.type == "TEX_IMAGE" and n.image == old:
                    n.image = self.img
        bpy.data.images.remove(old)


def _bands(rgb, k=4):
    """Mean colour over luminance of each quarter of the pixels, darkest first."""
    L = rgb @ LUMA
    e = np.quantile(L, np.linspace(0, 1, k + 1))
    out = []
    for a, b in zip(e[:-1], e[1:]):
        c = rgb[(L >= a) & (L <= b)].mean(0)
        out.append(c / max(float(c @ LUMA), 1e-4))
    return np.array(out)


def measure(path, spr, bands=False):
    """Luminance quartiles (25/50/75/90, 0-255) and mean sRGB of the classic
    render over its opaque pixels, averaged to the drawing's pixel size;
    with bands, the colour of each luminance quarter too."""
    import bpy
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    a = a.reshape(h, w, 4)[::-1]
    k = w // spr.w
    a = a[:spr.h * k, :spr.w * k].reshape(spr.h, k, spr.w, k, 4).mean((1, 3))
    sel = a[..., 3] > 0.5
    rgb = a[..., :3][sel] / a[..., 3][sel][:, None]
    if bands:
        return np.percentile(rgb @ LUMA * 255, [25, 50, 75, 90]), rgb.mean(0), _bands(rgb)
    return np.percentile(rgb @ LUMA * 255, [25, 50, 75, 90]), rgb.mean(0)


def fit(parts, spr, bark, which=("wood",), rounds=7, g0=1.1, spec=0.1):
    """Sets the ramp's contrast g and the parts' vertex level so the classic
    render's median and 90th percentile meet the drawing's. Scaling the
    vertex colours scales the render's light evenly, so the level step is
    exact and the 90th over the median depends on g alone. Dead bark is
    matte: at the default specular its sheen puts a floor under the dark
    tones the drawing's cracks go well below, so it is turned down."""
    for p in parts:
        for m in p.data.materials:
            m.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = spec
    T = np.percentile(spr.lum[spr.mask] * 255, [25, 50, 75, 90])
    ms = spr.rgb[spr.mask].mean(0)
    path = os.path.join(common.WORK, "calib", spr.name + "_tone.png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tl = math.log(T[3] / T[1])
    g, seen, log = g0, [], []
    for it in range(rounds):
        bark.set(g)
        common.classic(spr, path, scale=2)
        R, mr = measure(path, spr)
        rl = math.log(max(R[3], 1) / max(R[1], 1))
        log.append((round(g, 2), R.round(1).tolist()))
        seen.append((g, rl))
        ratio_ok = abs(rl - tl) < 0.05
        if (ratio_ok and abs(R[1] - T[1]) < 1.5) or it == rounds - 1:
            break
        tint = np.clip((ms / ms.mean()) / (mr / max(mr.mean(), 1e-4)), 0.92, 1.1)
        common.scale_colours(parts, which, np.clip(tint * T[1] / max(R[1], 1), 0.4, 2.5) ** 2.2)
        if ratio_ok:
            continue
        if len(seen) > 1 and abs(seen[-1][1] - seen[-2][1]) > 1e-3 and seen[-1][0] != seen[-2][0]:
            (ga, ra), (gb, rb) = seen[-2], seen[-1]
            g = gb + (tl - rb) * (gb - ga) / (rb - ra)
        else:
            g = g * (tl / max(rl, 1e-3)) ** 1.5
        g = float(np.clip(g, 0.5, 3.0))
    # the ramp's hue by luminance quarter, so the lights come out the
    # drawing's grey-green rather than the sky's cyan, then the level again
    S = _bands(spr.rgb[spr.mask])
    for _ in range(2):
        R, mr, B = measure(path, spr, bands=True)
        fix = np.clip(S / np.maximum(B, 1e-4), 0.85, 1.18)
        bark.tint *= np.array([np.interp(bark.q, [0.125, 0.375, 0.625, 0.875], fix[:, ch]) for ch in range(3)]).T
        bark.set(g)
        common.classic(spr, path, scale=2)
        R, mr = measure(path, spr)
        common.scale_colours(parts, which, np.clip(T[1] / max(R[1], 1), 0.8, 1.25) ** 2.2)
        common.classic(spr, path, scale=2)
        R, mr = measure(path, spr)
        log.append(("hue", R.round(1).tolist(), fix.round(2).tolist()))
    return {"target": T.round(1).tolist(), "tone": log}
