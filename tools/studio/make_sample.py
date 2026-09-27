#!/usr/bin/env python3
"""Write the studio's sample model, a small stone well, as a binary glTF.

    python tools/studio/make_sample.py [out.glb]

Everything is made here from numbers, with no game art: a round stone wall
with a painted stone texture, two posts and a gabled roof, a bucket on the
south side so the front is plain to see, and a small flag whose material is
named "team_flag" to show the studio's team colour. One unit is one map
cell, Y is up, +Z is south (the front), and the origin is the middle of the
base on the ground, as the game's drop-in models are.
"""
import json
import math
import os
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT = os.path.join(HERE, "..", "..", "unity", "Assets", "Game", "Studio", "Samples", "SampleWell.glb")


def png(w, h, pixel):
    rows = b"".join(b"\0" + bytes(c for x in range(w) for c in pixel(x, y)) for y in range(h))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def stone(x, y):
    # Courses of blocks with dark mortar and a little speckle.
    row = y // 16
    shift = 8 if row % 2 else 0
    mortar = y % 16 in (0, 1) or (x + shift) % 24 in (0, 1)
    n = ((x * 73856093) ^ (y * 19349663) ^ (row * 83492791)) % 29
    base = 70 if mortar else 150 + ((x + shift) // 24 * 37 + row * 11) % 30
    v = base + n - 14
    return (max(0, min(255, v + 6)), max(0, min(255, v)), max(0, min(255, v - 10)))


class Mesh:
    def __init__(self):
        self.p, self.n, self.uv, self.i = [], [], [], []

    def quad(self, a, b, c, d, uv=((0, 1), (1, 1), (1, 0), (0, 0))):
        """A flat quad a b c d, counter-clockwise seen from its front."""
        e1 = [b[k] - a[k] for k in range(3)]
        e2 = [d[k] - a[k] for k in range(3)]
        nx, ny, nz = e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]
        ln = math.sqrt(nx * nx + ny * ny + nz * nz) or 1
        base = len(self.p)
        for v, t in zip((a, b, c, d), uv):
            self.p.append(v)
            self.n.append((nx / ln, ny / ln, nz / ln))
            self.uv.append(t)
        self.i += [base, base + 1, base + 2, base, base + 2, base + 3]

    def tri(self, a, b, c):
        self.quad(a, b, c, c, ((0, 1), (1, 1), (0.5, 0), (0.5, 0)))

    def box(self, lo, hi):
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        self.quad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))
        self.quad((x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0))
        self.quad((x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1))
        self.quad((x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0))
        self.quad((x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0))
        self.quad((x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1))


def ring(m, r_out, r_in, h, sides=14):
    for s in range(sides):
        a0, a1 = 2 * math.pi * s / sides, 2 * math.pi * (s + 1) / sides
        o0 = (r_out * math.sin(a0), r_out * math.cos(a0))
        o1 = (r_out * math.sin(a1), r_out * math.cos(a1))
        i0 = (r_in * math.sin(a0), r_in * math.cos(a0))
        i1 = (r_in * math.sin(a1), r_in * math.cos(a1))
        u0, u1 = s / sides * 4, (s + 1) / sides * 4
        m.quad((o0[0], 0, o0[1]), (o1[0], 0, o1[1]), (o1[0], h, o1[1]), (o0[0], h, o0[1]), ((u0, 1), (u1, 1), (u1, 0.4), (u0, 0.4)))
        m.quad((i1[0], 0, i1[1]), (i0[0], 0, i0[1]), (i0[0], h, i0[1]), (i1[0], h, i1[1]), ((u1, 1), (u0, 1), (u0, 0.4), (u1, 0.4)))
        m.quad((o0[0], h, o0[1]), (o1[0], h, o1[1]), (i1[0], h, i1[1]), (i0[0], h, i0[1]), ((u0, 0.4), (u1, 0.4), (u1, 0.3), (u0, 0.3)))


def build():
    wall, wood, roof, bucket, flag = Mesh(), Mesh(), Mesh(), Mesh(), Mesh()
    ring(wall, 0.62, 0.45, 0.5)
    for x in (-0.56, 0.48):
        wood.box((x, 0.45, -0.04), (x + 0.08, 1.35, 0.04))
    wood.box((-0.6, 1.22, -0.03), (0.6, 1.28, 0.03))
    # A gabled roof, ridge running east to west.
    a, b, c, d = (-0.72, 1.3, 0.42), (0.72, 1.3, 0.42), (0.72, 1.62, 0), (-0.72, 1.62, 0)
    roof.quad(a, b, c, d)
    roof.quad((0.72, 1.3, -0.42), (-0.72, 1.3, -0.42), (-0.72, 1.62, 0), (0.72, 1.62, 0))
    roof.tri((0.72, 1.3, 0.42), (0.72, 1.3, -0.42), (0.72, 1.62, 0))
    roof.tri((-0.72, 1.3, -0.42), (-0.72, 1.3, 0.42), (-0.72, 1.62, 0))
    # The bucket hangs on the south side, the front.
    bucket.box((-0.1, 0.5, 0.66), (0.1, 0.72, 0.86))
    flag.box((0.53, 1.62, -0.015), (0.55, 2.0, 0.015))
    flag.quad((0.55, 1.78, 0), (0.85, 1.83, 0), (0.85, 1.98, 0), (0.55, 1.98, 0))
    flag.quad((0.85, 1.83, 0), (0.55, 1.78, 0), (0.55, 1.98, 0), (0.85, 1.98, 0))
    return [("wall", wall, 0), ("posts", wood, 1), ("roof", roof, 2), ("bucket", bucket, 1), ("flag", flag, 3)]


def write(path):
    parts = build()
    bin_ = bytearray()
    views, accessors, meshes, nodes = [], [], [], []

    def view(data):
        while len(bin_) % 4:
            bin_.append(0)
        views.append({"buffer": 0, "byteOffset": len(bin_), "byteLength": len(data)})
        bin_.extend(data)
        return len(views) - 1

    for name, m, mat in parts:
        pos = b"".join(struct.pack("<3f", *v) for v in m.p)
        lo = [min(v[k] for v in m.p) for k in range(3)]
        hi = [max(v[k] for v in m.p) for k in range(3)]
        accessors.append({"bufferView": view(pos), "componentType": 5126, "count": len(m.p), "type": "VEC3", "min": lo, "max": hi})
        accessors.append({"bufferView": view(b"".join(struct.pack("<3f", *v) for v in m.n)), "componentType": 5126, "count": len(m.n), "type": "VEC3"})
        accessors.append({"bufferView": view(b"".join(struct.pack("<2f", *v) for v in m.uv)), "componentType": 5126, "count": len(m.uv), "type": "VEC2"})
        accessors.append({"bufferView": view(b"".join(struct.pack("<H", i) for i in m.i)), "componentType": 5123, "count": len(m.i), "type": "SCALAR"})
        a = len(accessors) - 4
        meshes.append({"name": name, "primitives": [{"attributes": {"POSITION": a, "NORMAL": a + 1, "TEXCOORD_0": a + 2}, "indices": a + 3, "material": mat}]})
        nodes.append({"name": name, "mesh": len(meshes) - 1})
    image = view(png(64, 64, stone))
    nodes.append({"name": "SampleWell", "children": list(range(len(parts)))})
    doc = {
        "asset": {"version": "2.0", "generator": "OpenKingdoms tools/studio/make_sample.py"},
        "scene": 0, "scenes": [{"nodes": [len(nodes) - 1]}], "nodes": nodes, "meshes": meshes,
        "materials": [
            {"name": "stone", "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0, "roughnessFactor": 0.9}},
            {"name": "wood", "pbrMetallicRoughness": {"baseColorFactor": [0.42, 0.27, 0.15, 1], "metallicFactor": 0, "roughnessFactor": 0.8}},
            {"name": "roof", "pbrMetallicRoughness": {"baseColorFactor": [0.55, 0.18, 0.12, 1], "metallicFactor": 0, "roughnessFactor": 0.7}},
            {"name": "team_flag", "pbrMetallicRoughness": {"baseColorFactor": [0.92, 0.92, 0.9, 1], "metallicFactor": 0, "roughnessFactor": 0.9}, "doubleSided": True},
        ],
        "textures": [{"source": 0}], "images": [{"name": "stone", "bufferView": image, "mimeType": "image/png"}],
        "bufferViews": views, "accessors": accessors, "buffers": [{"byteLength": len(bin_)}],
    }
    js = json.dumps(doc, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    while len(bin_) % 4:
        bin_.append(0)
    out = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(bin_))
    out += struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(bin_), 0x004E4942) + bytes(bin_)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    open(path, "wb").write(out)
    print("%s: %d bytes, %d triangles" % (path, len(out), sum(len(m.i) // 3 for _, m, _ in parts)))


if __name__ == "__main__":
    write(sys.argv[1] if len(sys.argv) > 1 else DEFAULT)
