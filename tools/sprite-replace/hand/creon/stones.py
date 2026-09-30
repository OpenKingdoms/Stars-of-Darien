"""Creon's rocks and standing stones, run inside Blender (kit.py has the
frame and the reading of the sprites).

A rock is one dome per piece of its outline, roughened by three octaves of
noise that fade toward the ground and collapsed to broad facets, in basalt
blotched with pale lichen.

A ring of standing stones is read top by top. A top is a lit region of the
drawing, its carvings closed over; the dark run under it, column by
column, is its south face, whose median gives the stone's height, so the
top's own outline drops to the ground plan and is extruded down to it. A
dark face with no top over it, or one named in 'slabs', is a slab seen
face on: a stone from henge.py's stone_mesh on the line of its foot,
leaning back when it would stand taller than 2.4 cells. A top named in
'raise' is a capstone on its support, with a prop hidden under its back.
Stones gain depth at the back and stand lower by twice what they gain, so
the drawn outline holds and they read sturdy. All are dark tuff, and every
stone bigger than 'carve_min' pixels wears the ring's carving (round
spirals, square meanders and rings) on its tops and south faces.
"""
import math
import zlib

import numpy as np

import kit


def pieces_of(spr, spec):
    """The outline's pieces, each with its pixel count; specks under
    spec 'min_px' dropped."""
    lab, n = kit.label(spr.mask)
    sizes = np.bincount(lab.ravel(), minlength=n + 1)
    keep = {p for p in range(1, n + 1) if sizes[p] >= spec.get("min_px", 4)}
    return lab, keep, sizes


def tint(base, spread, seed, freq=0.9):
    """A colour function: base (linear) with broad noise of spread in
    brightness, a little warmer or cooler by place."""
    from mathutils import Vector, noise
    off = Vector(((seed % 97) * 1.3, (seed % 89) * 2.1, 0.0))

    def colour(co, n):
        v = noise.noise(co * freq + off)
        w = noise.noise(co * 0.35 + off * 2)
        k = 1.0 + spread * v
        return base[0] * k * (1 + 0.05 * w), base[1] * k, base[2] * k * (1 - 0.05 * w)
    return colour


def build_rock(spr, spec):
    seed = zlib.crc32(spr.name.encode())
    lab, keep, sizes = pieces_of(spr, spec)
    f = kit.dome(spr, lab, keep, spec)
    kit.smooth_field(f, 5, 1, 1.5, depth_blur=False)
    ob = kit.field_mesh(f, spr.name + "_rock")
    # broad lumps, then knobs, then grain, then collapsed to broad facets
    kit.roughen(ob, spec.get("lumps", 0.22), 0.55, seed % 50, fade=0.7)
    kit.roughen(ob, spec.get("rough", 0.09), 1.6, seed % 50 + 5, fade=0.5)
    kit.roughen(ob, spec.get("grain", 0.025), 5.0, seed % 50 + 11, fade=0.3)
    kit.decimate(ob, spec.get("tris", 450))
    kit.shade(ob, spec.get("facet", 12))
    kit.box_uv(ob, 0.35)
    # basalt: the drawing's colour a little greyer, blotched by place
    m = spr.mean()
    grey = 0.7 * m + 0.3 * float(m @ kit.LUMA)
    base = kit.srgb_to_lin(spec.get("rgb", grey * spec.get("tone", 0.8)))
    kit.paint_points(ob, tint(base, 0.25, seed, freq=1.6))
    tex = kit.image(spr.name + "_basalt", kit.tex_stone("basalt", seed % 1000))
    ob.data.materials.append(kit.material("basalt", tex, rough=0.92))
    lo = np.array([min(v.co[i] for v in ob.data.vertices) for i in range(3)])
    hi = np.array([max(v.co[i] for v in ob.data.vertices) for i in range(3)])
    return [ob], {"pieces": len(keep), "size": np.round(hi - lo, 2).tolist()}


def shift_or(m, r, op):
    """Square dilation (op=np.logical_or) or erosion (np.logical_and) by r."""
    H, W = m.shape
    pad = np.pad(m, r, constant_values=(op is np.logical_and))
    out = pad[r:r + H, r:r + W].copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            out = op(out, pad[r + dy:r + dy + H, r + dx:r + dx + W])
    return out


def slab(stands, spr, cols, feet, zs, thick, k):
    """A slab seen face on, as a stone: its foot the least-squares line
    through its columns' feet, as wide as its columns, as tall as their
    median. One that would stand taller than 2.4 cells leans back instead,
    which covers the same drawing lower down: a cell of its length leaning
    by a spans 8 cos a + 16 sin a pixels where upright it spans 8."""
    cols = np.asarray(cols, float)
    feet, zs = np.asarray(feet), np.asarray(zs)
    xs = (cols + 0.5 - spr.hx) / kit.CELL
    if len(cols) >= 3:
        a, b = np.polyfit(xs, feet, 1)
    else:
        a, b = 0.0, float(np.mean(feet))
    x0, x1 = xs.min() - 1 / 32, xs.max() + 1 / 32
    h = float(np.median(zs))
    lean = 0.0
    if h > 2.4:
        lean = 35.0 if h > 4.0 else 25.0
        la = math.radians(lean)
        h = h * 8 / (8 * math.cos(la) + 16 * math.sin(la))
    stands.append({"p1": (x0, a * x0 + b), "p2": (x1, a * x1 + b), "h": h, "t": thick, "k": k, "lean": lean})


def tops_field(spr, spec):
    """The ring as flat-topped stones, each the drawing's own outline of a
    top the classic camera saw, extruded to the ground. A top is a lit
    region (its carvings closed over) and the dark run under it, column by
    column, its south face: its median, 8 pixels to the cell, is the
    stone's height, so each lit pixel drops to the ground plan by it. A
    dark face with no top over it is a slab seen face on, as tall as its
    columns less its unseen top. spec 'faces' lists pixels whose lit
    region is a south face in the light rather than a top. Every stone
    gains depth at the back up to 'min_thick' cells and stands lower by two
    cells for each it gains, so the drawn outline holds."""
    lit_ref = float(np.quantile(spr.lum[spr.mask], 0.8))
    lit = spr.mask & (spr.lum >= spec.get("dark", 0.45) * lit_ref)
    r = spec.get("close", 2)
    lit = shift_or(shift_or(lit, r, np.logical_or), r, np.logical_and) & spr.mask
    dark = spr.mask & ~lit
    # a dark region is a face only when it stands on the ground in the
    # drawing, clear below most of its columns; else it is a carving or a
    # crack in the top round it
    dl, dn = kit.label(dark)
    below = np.zeros_like(dark)
    below[:-1] = ~spr.mask[1:]
    below[-1] = True
    for k in range(1, dn + 1):
        reg = dl == k
        cols = np.nonzero(reg.any(0))[0]
        feet = sum(1 for c in cols if below[np.nonzero(reg[:, c])[0].max(), c])
        if feet < spec.get("ground_share", 0.5) * len(cols):
            lit |= reg
    dark = spr.mask & ~lit
    # and the other way: small lit carvings inside a dark face are that face
    dcl = shift_or(shift_or(dark, 2, np.logical_or), 2, np.logical_and) & spr.mask
    ll, ln = kit.label(lit)
    for k in range(1, ln + 1):
        reg = ll == k
        if reg.sum() < spec.get("speck", 60) and not (reg & ~dcl).any():
            lit &= ~reg
    dark = spr.mask & ~lit
    lab, n = kit.label(lit)
    faces = {int(lab[rr, cc]) for cc, rr in spec.get("faces", []) if lab[rr, cc]}
    raised = {int(lab[rr, cc]): (z, t) for cc, rr, z, t in spec.get("raise", []) if lab[rr, cc]}
    lifted = []
    # dark regions named as slabs of their own: no top above claims them
    dlab, _ = kit.label(dark)
    own = np.zeros_like(dark)
    for cc, rr in spec.get("slabs", []):
        if dlab[rr, cc]:
            own |= dlab == dlab[rr, cc]
    claimable = dark & ~own
    used = np.zeros_like(dark)
    stands = []
    min_thick = spec.get("min_thick", 0.45)
    todo, areas = [], {}
    H = spr.h
    for k in range(1, n + 1):
        top = lab == k
        px = int(top.sum())
        if px < spec.get("min_top", 4):
            continue
        cols = np.nonzero(top.any(0))[0]
        if k in raised:
            lifted.append((k, top, *raised[k]))
            areas[k] = px
            continue
        rise = {}
        for c in cols:
            rb = np.nonzero(top[:, c])[0].max()
            d = 0
            while rb + 1 + d < H and claimable[rb + 1 + d, c]:
                d += 1
            used[rb + 1:rb + 1 + d, c] = True
            rise[c] = d
        if k in faces:
            # a lit south face: a slab min_thick deep behind its foot
            feet, zs = [], []
            for c in cols:
                rr = np.nonzero(top[:, c])[0]
                s_px = rr.max() + rise[c] - rr.min() + 1
                zs.append(max((s_px - 16 * min_thick) / 8.0, 0.3))
                feet.append((spr.hy - rr.max() - rise[c] - 1) / kit.CELL)
            slab(stands, spr, cols, feet, zs, min_thick, k)
            areas[k] = px
            continue
        z = max(float(np.median(list(rise.values()))) / 8.0, spec.get("min_rise", 0.25))
        depth = {c: (np.nonzero(top[:, c])[0].max() - np.nonzero(top[:, c])[0].min() + 1) / 16.0 for c in cols}
        # standing stones gain depth at the back and lose twice it in height
        thin = float(np.median(list(depth.values())))
        gain = max(0.0, min(min_thick - thin, (z - 0.4) / 2)) if z > 1.2 * thin else 0.0
        z2 = z - 2 * gain
        # each edge evened over five columns, so a stone's sides don't
        # step from one pixel to the next
        lo_r = np.array([np.nonzero(top[:, c])[0].max() for c in cols], float)
        hi_r = np.array([np.nonzero(top[:, c])[0].min() for c in cols], float)
        if len(cols) >= 5:
            lo_r = np.array([np.median(lo_r[max(0, i - 2):i + 3]) for i in range(len(cols))])
            hi_r = np.array([np.median(hi_r[max(0, i - 2):i + 3]) for i in range(len(cols))])
        for c, rlo, rhi in zip(cols, lo_r, hi_r):
            y_front = (spr.hy - rlo - 1 - 8 * z) / kit.CELL
            y_back = (spr.hy - rhi - 8 * z) / kit.CELL + gain
            todo.append((c, y_front, y_back, z2, k))
        areas[k] = px + sum(rise.values())
    rest = dark & ~used
    lab2, n2 = kit.label(rest)
    for k2 in range(1, n2 + 1):
        face = lab2 == k2
        if face.sum() < spec.get("min_face", 12):
            continue
        k = n + k2
        cols = np.nonzero(face.any(0))[0]
        feet, zs = [], []
        for c in cols:
            rr = np.nonzero(face[:, c])[0]
            zs.append(max((rr.max() - rr.min() + 1 - 16 * min_thick) / 8.0, 0.3))
            feet.append((spr.hy - rr.max() - 1) / kit.CELL)
        slab(stands, spr, cols, feet, zs, min_thick, k)
        areas[k] = int(face.sum())
    if not todo:
        return None, areas, stands, lifted
    f = kit.Field(spr, min(t[1] for t in todo), max(t[2] for t in todo))
    for c, ya, yb, z, k in todo:
        f.fill(c, ya, yb, z, k)
    return f, areas, stands, lifted


# ---------------------------------------------------------------- the ring

def mesh_stone(dims, seed, standing, **o):
    """henge.py's stone in its own frame: width on x, thickness on y and
    length up z from its base, with its rolling surface and broken ends."""
    import henge
    W, T, L = dims
    s = {"pose": "rubble" if o.get("rubble") else ("upright" if standing else "lying"), "width": W, "thickness": T,
         "height": L, "taper": o.get("taper", 0.92), "top_shape": o.get("top", "broken"), "break_depth": o.get("brk", 0.1),
         "chip": o.get("chip", 0.12), "p": o.get("p", 6.5), "round": o.get("round", 0.06),
         "base_shape": o.get("base", "broken")}
    return henge.stone_mesh(s, seed, standing, 0.3 if standing else 0.0)


def place(bm, R, t, z0=0.0, clip=True):
    """Turns the stone by R, moves it by t, and seats its lowest point at z0,
    cut off at the ground when it rests on it."""
    import henge
    from mathutils import Matrix
    bm.transform(Matrix.Translation(t) @ R)
    zmin = min(v.co.z for v in bm.verts)
    sink = 0.06 if z0 == 0 else 0.0
    bm.transform(Matrix.Translation((0.0, 0.0, z0 - zmin - sink)))
    if clip and z0 == 0:
        henge.clip_ground(bm)


def stand_stone(st, seed):
    """A face-on slab from slab() as a bmesh in the model's frame."""
    from mathutils import Matrix, Vector
    p1, p2 = np.array(st["p1"]), np.array(st["p2"])
    v = p2 - p1
    W = float(np.hypot(*v))
    th = math.atan2(v[1], v[0])
    n = np.array([-math.sin(th), math.cos(th)])
    if n[1] < 0:
        n = -n
    mid = (p1 + p2) / 2 + n * st["t"] / 2
    bm = mesh_stone((W, st["t"], max(st["h"], 0.4)), seed, True, top="broken", taper=0.9, chip=0.15)
    R = Matrix.Rotation(th, 4, "Z")
    if st.get("lean"):
        # its top toward its back, the compass heading of n
        L = math.atan2(n[0], n[1])
        R = Matrix.Rotation(math.radians(st["lean"]), 4, Vector((-math.cos(L), math.sin(L), 0.0))) @ R
    place(bm, R, Vector((mid[0], mid[1], 0.0)))
    return bm


def build_tops(spr, spec, seed):
    import bmesh
    import bpy
    f, areas, stands, lifted = tops_field(spr, spec)
    parts = []
    for i, (k, top, z, t) in enumerate(lifted):
        rows, cols = np.nonzero(top)
        ys = (spr.hy - rows - 0.5 - 8 * (z + t)) / kit.CELL
        g = kit.Field(spr, ys.min() - 1 / 16, ys.max() + 1 / 16)
        for rr, c, y in zip(rows, cols, ys):
            g.fill(int(c), y - 1 / 32, y + 1 / 32, t, k)
        ob = kit.field_mesh(g, "%s_cap%d" % (spr.name, i), bottom=True)
        kit.modifier(ob, "LAPLACIANSMOOTH", lambda_factor=spec.get("even", 1.5), iterations=6, use_z=False,
                     use_volume_preserve=False, use_normalized=True)
        kit.roughen(ob, spec.get("rough", 0.035), 2.2, seed % 50 + i, keep_ground=False)
        kit.decimate(ob, spec.get("tris", 6000) // 2)
        for v in ob.data.vertices:
            v.co.z += z
        parts.append((ob, k))
        # a prop under its back half, out of the classic camera's sight
        xs = (cols + 0.5 - spr.hx) / kit.CELL
        yb = float(np.quantile(ys, 0.8))
        stands.append({"p1": (float(np.quantile(xs, 0.3)), yb), "p2": (float(np.quantile(xs, 0.7)), yb),
                       "h": z + 0.1, "t": 0.6, "k": k, "lean": 0.0})
    if f is not None:
        ob = kit.field_mesh(f, spr.name + "_tops")
        # outlines evened in plan alone, so the sides lose the pixel steps
        # and the tops stay flat
        kit.modifier(ob, "LAPLACIANSMOOTH", lambda_factor=spec.get("even", 1.5), iterations=6, use_z=False,
                     use_volume_preserve=False, use_normalized=True)
        # the noise fades toward the ground, so feet stay put
        kit.roughen(ob, spec.get("rough", 0.035), 2.2, seed % 50, fade=0.3)
        kit.roughen(ob, spec.get("grain", 0.012), 7.0, seed % 50 + 3, fade=0.3)
        kit.decimate(ob, spec.get("tris", 6000))
        parts.append((ob, None))
    for i, st in enumerate(stands):
        bm = stand_stone(st, zlib.crc32(("%s:s%d" % (spr.name, i)).encode()))
        me = bpy.data.meshes.new("%s_slab%d" % (spr.name, i))
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(me.name, me)
        bpy.context.collection.objects.link(ob)
        parts.append((ob, st["k"]))
    carved = {k for k, a in areas.items() if a >= spec.get("carve_min", 180)}
    carved |= set(spec.get("carve", []))
    R, C = (f.h.shape if f is not None else (0, 0))
    for ob, k in parts:
        me = ob.data
        me.materials.append(kit.material("tuff", kit.image(spr.name + "_tuff", kit.tex_stone("tuff", seed % 1000)), rough=0.9))
        me.materials.append(kit.material("tuff_carved", kit.image(spr.name + "_carving", kit.tex_carved(seed % 1000)), rough=0.9))
        for p in me.polygons:
            kk = k
            if kk is None:
                c = p.center
                col = int(np.clip(round(c.x * kit.CELL + spr.hx - 0.5), 0, C - 1))
                row = int(np.clip(round((c.y - f.y0) * kit.CELL - 0.5), 0, R - 1))
                kk = f.piece[row, col]
                if kk == 0 and p.normal.y < -0.5:
                    kk = f.piece[min(row + 1, R - 1), col]
            facing = p.normal.z > 0.6 or p.normal.y < -0.6
            p.material_index = 1 if (kk in carved and facing) else 0
    obs = [ob for ob, _ in parts]
    bpy.ops.object.select_all(action="DESELECT")
    for ob in obs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    # joining keeps each part's own copies of the two materials: fold them
    me = ob.data
    first = {}
    for i, m in enumerate(me.materials):
        first.setdefault(m.name.split(".")[0], i)
    remap = [first[m.name.split(".")[0]] for m in me.materials]
    for p in me.polygons:
        p.material_index = remap[p.material_index]
    keep = sorted(set(first.values()))
    for i in reversed(range(len(me.materials))):
        if i not in keep:
            for p in me.polygons:
                if p.material_index > i:
                    p.material_index -= 1
            me.materials.pop(index=i)
    kit.shade(ob, spec.get("facet", 40))
    kit.box_uv(ob, spec.get("uv", 0.42))
    base = kit.srgb_to_lin(spec.get("rgb", spr.mean() * spec.get("tone", 0.8)))
    kit.paint_points(ob, tint(base, 0.1, seed))
    lo = np.array([min(v.co[i] for v in me.vertices) for i in range(3)])
    hi = np.array([max(v.co[i] for v in me.vertices) for i in range(3)])
    return [ob], {"tops": len(areas) - len(stands), "slabs": len(stands), "carved": len(carved),
                  "size": np.round(hi - lo, 2).tolist()}


def build_henge(spr, spec):
    return build_tops(spr, spec, zlib.crc32(spr.name.encode()))


BUILDERS = {"rock": build_rock, "henge": build_henge}
