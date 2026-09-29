// OkuWaterCommon.hlsl - the sea's shared parts, for the water, the wake
// splats and the ground under the sea, in either pipeline: the swell (the
// same sum of Gerstner waves as WaterWaves.cs), the surface's finer waves,
// the sea's baked depth and shore distance, the ships' wakes, the sky it
// reflects, foam, the bed and caustics. WaterView sets every value here
// once a frame.
#ifndef OKU_WATER_COMMON_INCLUDED
#define OKU_WATER_COMMON_INCLUDED

sampler2D _OkuWaterNormals;     // rg slopes, b crests, a height, tiling
sampler2D _OkuWaterFoam;        // r foam, g blobs, b fine lace, tiling
sampler2D _OkuWaterCaustics;    // r caustic net, tiling
sampler2D _OkuWaterNoise;       // r slow value noise, tiling
sampler2D _OkuSeaData;          // r depth / 8, g 0.5 + shore distance / 32 cells, b widest water near / 8 cells
sampler2D _OkuWakeTex;          // r wake foam, g churned water
float4 _OkuWakeRect;            // x, z of the wake picture's corner, 1 / width, 1 / depth; 0 with no wakes
float4 _OkuSeaRect;             // x, z of the north-west corner, width, depth
float4 _OkuSeaCell;             // x cell size, y how far the sea reaches past the map, in cells
float _OkuSeaLevel;             // -1000 with no sea
float _OkuWaterTime;
float4 _OkuWaterWind;           // xy direction, z strength
float4 _OkuWaterWaves;          // x swell height, y roughness, z whitecaps, w rain
float4 _OkuWaterSigma;          // rgb absorption per unit, a caustics
float4 _OkuWaterScatter;        // rgb the water's colour near land in full light, a sun glint
float4 _OkuWaterDeep;           // rgb the open sea's colour far from land
float4 _OkuWaterBed;            // rgb sea bed colour, a how far the bed takes it
float4 _OkuWaterSky;            // rgb zenith, a reflection strength
float4 _OkuHaze;
float _OkuBedLuma;              // the painted ground's usual lightness under the sea
float4 _OkuSwellDirs[2];        // the four swell waves' directions, as xy and zw
float4 _OkuLayerTurns[2];       // cos and sin of the three wave layers' turns from +x, and the sparkle layer's
float4 _OkuSunDir, _OkuSunColor, _OkuAmbient;   // the sun and sky, for what lights itself

// Wavelength, turn from the wind in degrees, steepness.
static const float3 OKU_SWELL[4] =
{
    float3(8.7, 0, 0.045), float3(5.3, 23, 0.04), float3(3.1, -31, 0.035), float3(1.9, 57, 0.03)
};

// The surface's offset from a rest point, its slope and crest, summed
// over the first n waves, damped by damp (0 flat, 1 full).
float3 OkuSwell(float2 p, float damp, int n, out float2 slope, out float crest)
{
    float3 d = 0;
    slope = 0;
    crest = 0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        if (i < n)
        {
            float3 w = OKU_SWELL[i];
            float k = 6.2831853 / w.x;
            float s = w.z * _OkuWaterWaves.x * damp;
            float a = s / k;
            float4 pair = _OkuSwellDirs[i / 2];
            float2 dir = (i % 2) == 0 ? pair.xy : pair.zw;
            float f = k * dot(dir, p) - sqrt(9.8 * k) * 0.35 * _OkuWaterTime;
            float cf = cos(f), sf = sin(f);
            d += float3(dir.x * a * cf, a * sf, dir.y * a * cf);
            slope += dir * s * cf;
            crest += sf * w.z * damp;
        }
    }
    crest /= 0.15;
    return d;
}

// The sea's baked data at a world point: x depth below the sea, y shore
// distance in cells (negative on land), z the widest water within a few
// cells, in cells, small in a pool. Deep open water off the data.
float3 OkuSeaAt(float2 xz)
{
    float2 uv = float2((xz.x - _OkuSeaRect.x) / _OkuSeaRect.z, 1 + (xz.y - _OkuSeaRect.y) / _OkuSeaRect.w);
    if (any(uv < 0) || any(uv > 1)) return float3(8, 16, 8);
    float4 s = tex2Dlod(_OkuSeaData, float4(uv, 0, 0));
    return float3(s.r * 8, (s.g - 0.5) * 32, s.b * 8);
}

// Past the map's edge: how far out a point lies, in cells, and how much of
// the edge beside it is sea, 0 to 1, read over a stretch that widens with
// the distance so a coast at the edge parts sea from haze softly.
float OkuSeaIsAt(float2 xz) { return saturate(OkuSeaAt(xz).x / 0.75); }
float OkuPastEdge(float2 xz, float2 mapSize, out float seaEdge)
{
    float2 edge = float2(clamp(xz.x, 0, mapSize.x), clamp(xz.y, -mapSize.y, 0));
    float2 off = xz - edge;
    float d = length(off);
    seaEdge = 0;
    if (_OkuSeaLevel > -999 && d > 0)
    {
        float2 along = float2(-off.y, off.x) * 0.15;
        float2 lo = float2(0, -mapSize.y), hi = float2(mapSize.x, 0);
        seaEdge = (OkuSeaIsAt(edge) + OkuSeaIsAt(clamp(edge + along, lo, hi)) + OkuSeaIsAt(clamp(edge - along, lo, hi))
            + OkuSeaIsAt(clamp(edge + along * 2, lo, hi)) + OkuSeaIsAt(clamp(edge - along * 2, lo, hi))) / 5;
    }
    return d / max(_OkuSeaCell.x, 1e-3);
}

// The ships' wakes at a world point: x foam, y churned water.
float2 OkuWakeAt(float2 xz)
{
    float2 uv = (xz - _OkuWakeRect.xy) * _OkuWakeRect.zw;
    if (_OkuWakeRect.z <= 0 || any(uv < 0) || any(uv > 1)) return 0;
    return tex2Dlod(_OkuWakeTex, float4(uv, 0, 0)).rg;
}

float OkuDamp(float depth) { return smoothstep(0, 1.5, depth); }

// The swell the mesh carries keeps a little of itself over the shore, so
// the water's edge moves up and down the beach.
float OkuDampMesh(float depth) { return lerp(0.3, 1, smoothstep(0, 1.5, depth)); }

float2 OkuRotate(float2 p, float a)
{
    float c = cos(a), s = sin(a);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

// Turns p by the angle whose cosine and sine are cs.
float2 OkuTurn(float2 p, float2 cs) { return float2(cs.x * p.x - cs.y * p.y, cs.y * p.x + cs.x * p.y); }
float2 OkuUnturn(float2 p, float2 cs) { return float2(cs.x * p.x + cs.y * p.y, -cs.y * p.x + cs.x * p.y); }

// The finer waves: three layers of the baked wave texture at unrelated
// sizes, each turned its own way from the wind and drifting at its own
// speed, the whole bent by slow noise, so no tiling, lattice or single
// wave shows. Returns the slope, and crest the height, about -0.5 to 0.5.
float2 OkuDetailSlope(float2 xz, out float crest)
{
    float t = _OkuWaterTime;
    float2 w = _OkuWaterWind.xy;
    // Slow noise bends the two larger layers. The smallest, which would
    // repeat soonest, is bent again on a scale near its own.
    float2 warp = float2(tex2D(_OkuWaterNoise, xz / 29.0).r, tex2D(_OkuWaterNoise, xz / 29.0 + 0.5).r) - 0.5;
    float2 fine = float2(tex2D(_OkuWaterNoise, xz / 7.3 + 0.25).r, tex2D(_OkuWaterNoise, xz / 7.3 + 0.75).r) - 0.5;
    float2 c1 = _OkuLayerTurns[0].xy, c2 = _OkuLayerTurns[0].zw, c3 = _OkuLayerTurns[1].xy;
    float2 uv1 = OkuUnturn(xz - w * t * 0.21, c1) / 13.7 + warp * 0.21;
    float2 uv2 = OkuUnturn(xz - w * t * 0.37, c2) / 5.3 - warp * 0.33 + 0.37;
    float2 uv3 = OkuUnturn(xz - w * t * 0.58, c3) / 2.1 + warp.yx * 0.5 + fine * 0.35 + 0.71;
    float4 n1 = tex2D(_OkuWaterNormals, uv1);
    float4 n2 = tex2D(_OkuWaterNormals, uv2);
    float4 n3 = tex2D(_OkuWaterNormals, uv3);
    crest = (n1.b - 0.5) * 0.4 + (n2.b - 0.5) * 0.35 + (n3.b - 0.5) * 0.25;
    return OkuTurn(n1.rg * 2 - 1, c1) * 0.10 + OkuTurn(n2.rg * 2 - 1, c2) * 0.12 + OkuTurn(n3.rg * 2 - 1, c3) * 0.08;
}

// A fine octave, wavelengths a fifth of a unit to most of one, that only
// the sun's glint sees, so the glint breaks into points.
float2 OkuSparkleSlope(float2 xz)
{
    float2 cs = _OkuLayerTurns[1].zw;
    float2 w = _OkuWaterWind.xy;
    float t = _OkuWaterTime;
    float2 a = tex2D(_OkuWaterNormals, OkuUnturn(xz - w * t * 0.83, cs) / 1.7 + 0.43).rg * 2 - 1;
    float2 b = tex2D(_OkuWaterNormals, OkuUnturn(xz + w.yx * t * 0.51, float2(cs.y, cs.x)) / 1.1 + 0.19).rg * 2 - 1;
    return OkuTurn(a, cs) * 0.035 + OkuTurn(b, float2(cs.y, cs.x)) * 0.025;
}

// A slow noise over tens of units, to vary roughness and foam.
float OkuMacro(float2 xz)
{
    return tex2D(_OkuWaterNoise, xz / 61.0 + _OkuWaterWind.xy * _OkuWaterTime * 0.002).r;
}

// The surface at a point: its slope, how high it rides (crest, about -1
// to 1), the slow noise there, wind slicks (1 in a slick, where the sea is
// glassier) and the sea's broad tint (0 to 1). The swell's own regular
// waves add only a little to the slope, or they would show as stripes.
struct OkuWave
{
    float2 slope;
    float crest;
    float macro;
    float slick;
    float tint;
};

OkuWave OkuWaveAt(float2 xz, float damp, float dist)
{
    OkuWave o;
    float2 swell;
    float swellCrest;
    OkuSwell(xz, damp, 4, swell, swellCrest);
    float detailCrest;
    float2 detail = OkuDetailSlope(xz, detailCrest);
    o.macro = OkuMacro(xz);
    // Broad patches, some tens of units across, drifting with the wind.
    float2 drift = _OkuWaterWind.xy * _OkuWaterTime * 0.0005;
    o.slick = smoothstep(0.55, 0.8, tex2D(_OkuWaterNoise, xz / 140.0 + drift + 0.31).r);
    o.tint = saturate((tex2D(_OkuWaterNoise, OkuRotate(xz, 0.7) / 110.0 - drift + 0.67).r - 0.5) * 2.2 + 0.5);
    float strength = lerp(0.6, 1.4, o.macro) * (0.35 + _OkuWaterWaves.y * 4.0) * lerp(0.3, 1, damp);
    strength *= lerp(1, 0.55, saturate(dist / 150)) * (1 - o.slick * 0.55);
    o.slope = swell * 0.15 + detail * strength;
    o.crest = detailCrest * 2 * lerp(0.5, 1, damp) + swellCrest * 0.06;
    return o;
}

// Rain: rings on two offset grids of cells, each starting at its own time.
float2 OkuRain(float2 xz, float dist)
{
    float2 slope = 0;
    [unroll] for (int g = 0; g < 2; g++)
    {
        float2 q = xz * 2.3 + g * float2(0.37, 0.61);
        float2 cell = floor(q);
        float2 local = frac(q) - 0.5;
        float h = frac(sin(dot(cell + g * 17.0, float2(12.9898, 78.233))) * 43758.5453);
        float2 jitter = float2(frac(h * 7.13), frac(h * 3.71)) - 0.5;
        float2 d = local - jitter * 0.5;
        float age = frac(_OkuWaterTime * 1.1 + h);
        float r = length(d);
        float ring = sin((r - age * 0.42) * 46) * saturate(1 - abs(r - age * 0.42) * 12) * (1 - age);
        slope += (d / max(r, 1e-3)) * ring;
    }
    return slope * 0.12 * saturate(1 - dist / 90);
}

// The sky a ray off the water sees: the haze at the horizon, deepening to
// the zenith, and the sun.
float3 OkuSky(float3 r, float3 sunDir, float3 sunColor)
{
    float up = saturate(r.y);
    float3 sky = lerp(_OkuHaze.rgb * 1.05, _OkuWaterSky.rgb, pow(up, 0.45));
    float sd = saturate(dot(r, sunDir));
    return sky + sunColor * (pow(sd, 24) * 0.25);
}

// The sun off one facet of the sea: a sharp lobe with no long tail, so
// only facets turned almost exactly to the sun light up, as points.
float OkuSparkle(float3 n, float3 v, float3 l, float sigma)
{
    float nh = saturate(dot(n, normalize(v + l)));
    return exp(-(1 - nh) * 2 / (sigma * sigma)) * saturate(dot(n, l) * 4);
}

// A broad faint lobe round the sun's path, 0 to 1.
float OkuSheen(float3 n, float3 v, float3 l)
{
    float nh = saturate(dot(n, normalize(v + l)));
    return pow(nh, 160) * saturate(dot(n, l) * 4);
}

// Schlick's fresnel with a touch more at the head-on angle than water has,
// so the sky still shows on the waves from an RTS camera.
float OkuFresnel(float3 n, float3 v)
{
    return 0.035 + 0.965 * pow(1 - saturate(dot(n, v)), 5);
}

// How much haze lies over the sea this many cells past a sea edge: a
// little over the ring, all of it toward the horizon.
float OkuSeaHaze(float cells) { return 1 - exp(-cells / 600); }

// The open sea far past a sea edge seen from cam, one look for the water
// and the plain beyond it alike: the deep water in the day's light with
// the sky off its flat surface, into the haze.
float3 OkuFarSea(float cells, float3 p, float3 cam)
{
    float3 v = normalize(cam - p), up = float3(0, 1, 0);
    float3 lightIn = _OkuSunColor.rgb * saturate(_OkuSunDir.y + 0.2) + _OkuAmbient.rgb * 1.1;
    float3 sea = _OkuWaterDeep.rgb * lightIn;
    sea = lerp(sea, OkuSky(reflect(-v, up), _OkuSunDir.xyz, _OkuSunColor.rgb), saturate(OkuFresnel(up, v) * _OkuWaterSky.a));
    return lerp(sea, _OkuHaze.rgb, OkuSeaHaze(cells));
}

// Foam lace, two layers drifting apart, 0 to 1, read a touch blurred so
// its threads are soft rather than cracks.
float OkuFoamLace(float2 xz, float scale)
{
    float t = _OkuWaterTime;
    float2 w = _OkuWaterWind.xy;
    float a = tex2Dbias(_OkuWaterFoam, float4(xz / (3.3 * scale) + w * t * 0.013, 0, 0.5)).r;
    float b = tex2Dbias(_OkuWaterFoam, float4(OkuRotate(xz, 1.1) / (2.1 * scale) - w * t * 0.021, 0, 0.5)).r;
    return saturate(max(a, b) * 0.75 + min(a, b) * 0.45);
}

// How much of the lace shows for an amount of foam from 0 to 1: a little
// shows as its brightest threads, a lot as a solid sheet.
float OkuFoamCover(float lace, float amount)
{
    return smoothstep(1 - amount, 1.4 - amount, lace) * saturate(amount * 1.5);
}

// The lace gathered into clumps by soft blobs, as foam on water gathers.
float OkuFroth(float2 xz, float lace)
{
    float blob = tex2D(_OkuWaterFoam, xz / 7.9 + _OkuWaterWind.xy * _OkuWaterTime * 0.006 + 0.3).g;
    return saturate(lace * 0.75 + blob * 0.45);
}

// The painted ground under the sea, turned toward the sea bed's own
// colour. Its light and dark carry through in the shallows, so reefs and
// rocks still read, and fade to a narrow range a little over half a unit down.
half3 OkuBed(half3 c, float3 p)
{
    float depth = _OkuSeaLevel - p.y;
    if (depth <= 0) return c;
    half lum = dot(c, half3(0.3, 0.59, 0.11));
    half k = saturate(depth / 0.6);
    half rel = clamp(lum / max(_OkuBedLuma, 0.03), lerp(0.45, 0.75, k), lerp(1.6, 1.25, k));
    return lerp(c, _OkuWaterBed.rgb * rel, saturate(depth / 0.4) * _OkuWaterBed.a);
}

// How glossy the ground is: never under the sea.
half OkuBedGloss(half gloss, float3 p)
{
    return gloss * saturate(1 - (_OkuSeaLevel - p.y) / 0.1);
}

// How far the ground lies under the sea, 0 above it.
float OkuUnder(float3 p) { return max(_OkuSeaLevel - p.y, 0); }

// Under the sea the light is diffuse: the bed's facets and the shadows cast
// on it fade with depth, and its picture softens.
half OkuBedFlat(float3 p) { return saturate(OkuUnder(p) / 1.2) * 0.85; }
half OkuBedShadow(half shadow, float3 p) { return lerp(shadow, 1, saturate(OkuUnder(p) / 1.5)); }
float OkuBedBlur(float3 p) { return saturate(OkuUnder(p) / 1.5) * 1.5; }

// Just above the water an uneven band the waves keep wet, darker than
// the ground above it, so the water's edge is not a drawn line.
half OkuWetBand(float3 p)
{
    float above = p.y - _OkuSeaLevel;
    float wobble = 0.5 + 0.25 * (sin(p.x * 1.7 + sin(p.z * 1.3) * 2) + sin(p.z * 2.3 + sin(p.x * 0.9) * 2));
    float reach = 0.1 * lerp(0.35, 1.6, wobble);
    return lerp(1, 0.8, (1 - smoothstep(0, reach, above)) * smoothstep(-0.04, 0, above));
}

// Light the waves focus onto the bed, 0 to about 1, from the sun's
// direction, fading with depth. Multiplied by the sun's light by the caller.
// gx and gy are the ground point's screen derivatives (ddx, ddy of p.xz),
// taken outside any branch.
half OkuCaustics(float3 p, float3 sunDir, float2 gx, float2 gy)
{
    float depth = _OkuSeaLevel - p.y;
    float2 q = p.xz + sunDir.xz / max(sunDir.y, 0.25) * depth;
    float t = _OkuWaterTime;
    float2 w = _OkuWaterWind.xy;
    half a = tex2Dgrad(_OkuWaterCaustics, q / 3.7 + w * t * 0.021, gx / 3.7, gy / 3.7).r;
    half b = tex2Dgrad(_OkuWaterCaustics, OkuRotate(q, 0.6) / 2.9 - w * t * 0.017 + 0.5, OkuRotate(gx, 0.6) / 2.9, OkuRotate(gy, 0.6) / 2.9).r;
    half c = min(a, b);
    return c * c * 2.5 * saturate(1 - depth / 1.6) * smoothstep(0.02, 0.2, depth) * _OkuWaterSigma.a;
}

#endif
