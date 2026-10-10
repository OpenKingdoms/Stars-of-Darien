// OkuScar.hlsl - the battlefield's scars as the ground draws them, from the
// three textures ScarMap.cs keeps: char, soil, blight and stone, the dip,
// rim and cracks, and the seconds left of frost, wet, holy light and heat.
#ifndef OKU_SCAR_INCLUDED
#define OKU_SCAR_INCLUDED

sampler2D _OkuScarMarks;
sampler2D _OkuScarShape;
sampler2D _OkuScarFade;
float4 _OkuScarRect;    // x, z of the north-west corner, width, depth
float4 _OkuScarTexel;   // 1 / texels across, 1 / texels down, world units a texel spans across and down
float4 _OkuScarOn;      // on, lit through normals, dips, seconds since the fade texture last stepped
float4 _OkuScarSoil;    // the climate's soil, linear, and how wet a crater's floor lies
float4 _OkuScarSnow;    // what dug snow shows, and 1 on a snow map
sampler2D _OkuScarSnowed;   // the battle second each patch was last scarred (ScarSnow.cs)
float4 _OkuScarSnowing;     // now, when the snow began, seconds to cover a scar, and 1 while it snows

float2 OkuScarUv(float3 p)
{
    return float2((p.x - _OkuScarRect.x) / _OkuScarRect.z, 1 + (p.z - _OkuScarRect.y) / _OkuScarRect.w);
}

bool OkuScarInside(float2 uv) { return all(uv >= 0) && all(uv <= 1); }

// The drawn ground's change in world units from the dip and the rim,
// as ScarStamps.Height has it: the rim only where the ground is not dug.
float OkuScarHeightAt(float2 uv)
{
    float2 s = tex2Dlod(_OkuScarShape, float4(uv, 0, 0)).rg;
    float depthPx = s.r * 12, rimPx = s.g * 4;
    return (rimPx * (1 - saturate(depthPx * 0.5)) - depthPx) * 0.0625;
}

// How far the ground at a point dips, kept just above the water so a
// crater by the shore fills to the waterline.
float OkuScarDip(float3 p, float seaLevel)
{
    if (_OkuScarOn.z < 0.5) return 0;
    float2 uv = OkuScarUv(p);
    if (!OkuScarInside(uv)) return 0;
    return max(OkuScarHeightAt(uv), min(0, seaLevel + 0.05 - p.y));
}

float OkuScarHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float OkuScarNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(OkuScarHash(i), OkuScarHash(i + float2(1, 0)), f.x), lerp(OkuScarHash(i + float2(0, 1)), OkuScarHash(i + 1), f.x), f.y);
}

// A smooth mark made crisp: its middle value becomes an edge broken by
// noise, so marks read sharp up close though the map's texels are not.
half OkuScarCrisp(half v, half n, half middle, half breakup)
{
    return saturate((v - middle + (n - 0.5) * breakup) * 2.6 + 0.5) * step(0.02, v);
}

half OkuScarCrisp(half v, half n) { return OkuScarCrisp(v, n, 0.45, 0.5); }

struct OkuScarHere
{
    float4 marks;   // char, soil, blight, stone
    float4 shape;   // dip, rim, cracks
    float4 left;    // seconds of frost, wet, holy light and heat left
    float any;
};

OkuScarHere OkuScarRead(float3 p)
{
    OkuScarHere s;
    s.marks = 0; s.shape = 0; s.left = 0; s.any = 0;
    if (_OkuScarOn.x < 0.5) return s;
    float2 uv = OkuScarUv(p);
    if (!OkuScarInside(uv)) return s;
    s.marks = tex2Dlod(_OkuScarMarks, float4(uv, 0, 0));
    s.shape = tex2Dlod(_OkuScarShape, float4(uv, 0, 0));
    s.left = max(tex2Dlod(_OkuScarFade, float4(uv, 0, 0)) * 127.5 - _OkuScarOn.w, 0);
    s.any = step(0.004, dot(s.marks, 1) + dot(s.shape.rgb, 1) + dot(s.left, 1) * 0.01);
    return s;
}

// The ground's normal with the scars' dips and rims, from the slopes of
// the dent between neighbouring texels.
half3 OkuScarNormal(half3 n, float3 p)
{
    if (_OkuScarOn.y < 0.5) return n;
    float2 uv = OkuScarUv(p);
    float2 du = float2(_OkuScarTexel.x, 0), dv = float2(0, _OkuScarTexel.y);
    float gx = (OkuScarHeightAt(uv + du) - OkuScarHeightAt(uv - du)) / (2 * _OkuScarTexel.z);
    float gz = (OkuScarHeightAt(uv + dv) - OkuScarHeightAt(uv - dv)) / (2 * _OkuScarTexel.w);
    return normalize(n + half3(-gx, 0, -gz) * n.y);
}

// The nearest feature point of a jittered grid of cells, for pebbles and
// cracks: x the distance to it, y to the second nearest, z its own hash.
float3 OkuScarCells(float2 q)
{
    float2 c = floor(q);
    float d1 = 9, d2 = 9, h1 = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 cell = c + float2(x, y);
        float h = OkuScarHash(cell);
        float2 at = cell + float2(h, OkuScarHash(cell + 17.3)) * 0.8 + 0.1;
        float d = distance(q, at);
        if (d < d1) { d2 = d1; d1 = d; h1 = h; }
        else if (d < d2) d2 = d;
    }
    return float3(d1, d2, h1);
}

// The ground's colour with its scars, its normal and gloss, and the light
// that fresh char and holy marks give off. near is 1 up close and 0 far.
half3 OkuScarColour(half3 c, OkuScarHere s, float3 p, half near, inout half3 n, inout half gloss, out half3 emit)
{
    emit = 0;
    half3 ground = c;
    half lum = dot(c, half3(0.3, 0.59, 0.11));
    half mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
    half sat = (mx - mn) / max(mx, 0.001);
    // Snow is bright and grey, grass is greener than it is red or blue.
    half snowy = max(_OkuScarSnow.w * 0.5, smoothstep(0.3, 0.55, lum) * (1 - smoothstep(0.1, 0.28, sat)));
    half green = saturate((c.g - max(c.r, c.b)) / max(mx, 0.001) * 4);
    half grain = lerp(0.5, OkuScarNoise(p.xz * 3.1) * 0.6 + OkuScarNoise(p.xz * 9.7) * 0.4, near);
    half fine = lerp(0.5, OkuScarNoise(p.xz * 17.3 + 4.1), near);

    // Earth dug and thrown: the climate's soil tinted by the ground, slush on snow.
    half3 soil = lerp(_OkuScarSoil.rgb, c * 0.55, 0.25);
    soil = lerp(soil, _OkuScarSnow.rgb, snowy * 0.7);
    half dug = s.shape.r;
    // Broad patches of darker and lighter earth, seen from any distance, so
    // a field of craters is never one even colour, and dry thrown earth
    // lighter on the rims than the dug floors.
    half patch = OkuScarNoise(p.xz * 0.45) * 0.6 + OkuScarNoise(p.xz * 1.3 + 7.7) * 0.4;
    soil *= lerp(0.78, 1.18, patch);
    soil = lerp(soil, soil * 1.3 + 0.008, saturate(s.shape.g * 2.5) * (1 - saturate(dug * 3)));
    c = lerp(c, soil * (0.7 + 0.6 * fine), OkuScarCrisp(s.marks.g, grain) * lerp(0.74, 0.94, OkuScarNoise(p.xz * 0.8 + 3.3)));
    c *= 1 - saturate(dug * 1.5) * 0.3;
    // Dug earth is rough, its clods catching the light up close.
    half rough = OkuScarCrisp(s.marks.g, grain) * near;
    if (rough > 0)
    {
        float2 q = p.xz * 9;
        half gx = OkuScarNoise(q + float2(0.07, 0)) - OkuScarNoise(q - float2(0.07, 0));
        half gz = OkuScarNoise(q + float2(0, 0.07)) - OkuScarNoise(q - float2(0, 0.07));
        n = normalize(n - half3(gx, 0, gz) * 3 * rough);
    }
    half pool = _OkuScarSoil.w * saturate(dug * 4);
    c *= 1 - pool * 0.45;
    gloss = lerp(gloss, 0.8, pool);

    // Scattered stone, pebble by pebble up close.
    if (s.marks.a > 0.02 && near > 0)
    {
        float3 cell = OkuScarCells(p.xz / 0.45);
        half size = 0.25 + 0.2 * frac(cell.z * 7.1);
        half on = step(cell.z, s.marks.a * 0.75) * (1 - smoothstep(size - 0.06, size, cell.x)) * near;
        half3 stone = lerp(half3(0.2, 0.18, 0.15), half3(lum, lum, lum) * 1.2, 0.3) * (0.7 + 0.6 * frac(cell.z * 13.7));
        c = lerp(c, stone, on);
        gloss = lerp(gloss, 0.08, on);
    }

    // Blight withers what grows, grey and faintly violet.
    half3 withered = lerp(half3(lum, lum, lum), c, 0.15) * half3(0.78, 0.72, 0.84);
    c = lerp(c, withered, OkuScarCrisp(s.marks.b, grain, 0.35, 0.6) * lerp(0.6, 1, green));

    // Char, black soot while it is fresh, settling to dark burnt soil, with
    // embers glowing in spots while it is hot.
    // Char keeps a lower edge, so a lightning fork's thin lines hold together.
    half burnt = OkuScarCrisp(s.marks.r, 1 - grain, 0.28, 0.3);
    half3 charred = lerp(soil * 0.3, half3(0.013, 0.010, 0.008), 0.6) * (0.75 + 0.5 * fine);
    c = lerp(c, charred, burnt * 0.94);
    half fresh = saturate(s.left.w / 20);
    c = lerp(c, half3(0.005, 0.0045, 0.004), fresh * burnt * 0.8);
    half embers = smoothstep(0.62, 0.9, fine * 0.6 + grain * 0.4) * burnt;
    emit += half3(1.0, 0.3, 0.05) * fresh * fresh * embers * 1.6;

    // Cracks: the edges of a coarse grid of cells, where the cracks reach.
    if (s.shape.b > 0.02 && near > 0)
    {
        float3 cell = OkuScarCells(p.xz / 0.55);
        half seam = 1 - smoothstep(0.02, 0.06 + 0.04 * s.shape.b, cell.y - cell.x);
        c *= 1 - seam * saturate(s.shape.b * 1.5) * 0.75 * near;
    }

    // Rime, bright and icy, melting from its edges as its seconds run out.
    half frost = OkuScarCrisp(saturate(s.left.x / 12), fine * 0.6 + grain * 0.4);
    c = lerp(c, half3(0.62, 0.72, 0.84) * (0.7 + 0.6 * fine), frost * (0.75 + 0.2 * grain));
    gloss = lerp(gloss, 0.55, frost);

    // Wet ground, darker and glossy until it dries.
    half wet = smoothstep(0, 30, s.left.y) * (0.8 + 0.4 * grain);
    c *= lerp(1, 0.6, wet);
    gloss = lerp(gloss, 0.45, wet);

    // Holy light, a pale glow that fades.
    half holy = saturate(s.left.z / 25);
    c = lerp(c, c * 1.15 + 0.03, holy);
    emit += half3(1.0, 0.87, 0.58) * holy * holy * 1.5;

    // Snow settling over old scars while it snows, a fresh one dark through it.
    if (_OkuScarSnowing.w > 0.5)
    {
        float since = tex2Dlod(_OkuScarSnowed, float4(OkuScarUv(p), 0, 0)).r;
        half cover = saturate((_OkuScarSnowing.x - max(since, _OkuScarSnowing.y)) / _OkuScarSnowing.z);
        half scarred = saturate(dot(s.marks, 1) * 2 + s.shape.r * 4 + s.shape.b + s.left.w * 0.1);
        half lay = cover * scarred * (0.7 + 0.3 * grain);
        // Buried as the ground round it lies: white on snowy ground, and on
        // green ground back to the ground's own colour under a light dusting.
        half3 snowLook = half3(0.84, 0.87, 0.92) * (0.9 + 0.2 * fine);
        c = lerp(c, lerp(lerp(ground, snowLook, 0.2), snowLook, snowy), lay);
        gloss = lerp(gloss, 0.3, lay);
        emit *= 1 - lay;
    }
    return c;
}

#endif
