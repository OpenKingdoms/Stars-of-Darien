// OkuWaterCommon.hlsl - the sea's shared parts, for the water, the wakes
// and the ground under the sea, in either pipeline: the swell (the same sum
// of Gerstner waves as WaterWaves.cs), the surface's finer waves, the sea's
// baked depth and shore distance, the sky it reflects, foam, the bed and
// caustics. WaterView sets every value here once a frame.
#ifndef OKU_WATER_COMMON_INCLUDED
#define OKU_WATER_COMMON_INCLUDED

sampler2D _OkuWaterNormals;     // rg slopes, b crests, a height, tiling
sampler2D _OkuWaterFoam;        // r foam, g blobs, b fine lace, tiling
sampler2D _OkuWaterCaustics;    // r caustic net, tiling
sampler2D _OkuWaterNoise;       // r slow value noise, tiling
sampler2D _OkuSeaData;          // r depth / 8, g 0.5 + shore distance / 16 cells
float4 _OkuSeaRect;             // x, z of the north-west corner, width, depth
float4 _OkuSeaCell;             // x cell size
float _OkuSeaLevel;             // -1000 with no sea
float _OkuWaterTime;
float4 _OkuWaterWind;           // xy direction, z strength
float4 _OkuWaterWaves;          // x swell height, y roughness, z whitecaps, w rain
float4 _OkuWaterSigma;          // rgb absorption per unit, a caustics
float4 _OkuWaterScatter;        // rgb the deep water's colour in full light, a sun glint
float4 _OkuWaterBed;            // rgb sea bed colour, a how far the bed takes it
float4 _OkuWaterSky;            // rgb zenith, a reflection strength
float4 _OkuHaze;
float _OkuBedLuma;              // the painted ground's usual lightness under the sea

// Wavelength, turn from the wind in degrees, steepness.
static const float3 OKU_SWELL[4] =
{
    float3(8.7, 0, 0.045), float3(5.3, 23, 0.04), float3(3.1, -31, 0.035), float3(1.9, 57, 0.03)
};

float2 OkuWaveDir(int i)
{
    float a = atan2(_OkuWaterWind.y, _OkuWaterWind.x) + radians(OKU_SWELL[i].y);
    return float2(cos(a), sin(a));
}

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
            float2 dir = OkuWaveDir(i);
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
// distance in cells (negative on land). Deep open water off the data.
float2 OkuSeaAt(float2 xz)
{
    float2 uv = float2((xz.x - _OkuSeaRect.x) / _OkuSeaRect.z, 1 + (xz.y - _OkuSeaRect.y) / _OkuSeaRect.w);
    if (any(uv < 0) || any(uv > 1)) return float2(8, 8);
    float4 s = tex2Dlod(_OkuSeaData, float4(uv, 0, 0));
    return float2(s.r * 8, (s.g - 0.5) * 16);
}

float OkuDamp(float depth) { return smoothstep(0, 1.5, depth); }

float2 OkuRotate(float2 p, float a)
{
    float c = cos(a), s = sin(a);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

// The finer waves: three layers of the baked wave texture at unrelated
// sizes, each turned its own way from the wind and drifting at its own
// speed, the whole bent by slow noise, so no tiling, lattice or single
// wave shows. Returns the slope, and crest the height, about -0.5 to 0.5.
float2 OkuDetailSlope(float2 xz, out float crest)
{
    float wa = atan2(_OkuWaterWind.y, _OkuWaterWind.x);
    float t = _OkuWaterTime;
    float2 w = _OkuWaterWind.xy;
    // Slow noise bends the two larger layers. The smallest, which would
    // repeat soonest, is bent again on a scale near its own.
    float2 warp = float2(tex2D(_OkuWaterNoise, xz / 29.0).r, tex2D(_OkuWaterNoise, xz / 29.0 + 0.5).r) - 0.5;
    float2 fine = float2(tex2D(_OkuWaterNoise, xz / 7.3 + 0.25).r, tex2D(_OkuWaterNoise, xz / 7.3 + 0.75).r) - 0.5;
    float a1 = wa + 0.23, a2 = wa - 0.41, a3 = wa + 0.77;
    float2 uv1 = OkuRotate(xz - w * t * 0.21, -a1) / 13.7 + warp * 0.21;
    float2 uv2 = OkuRotate(xz - w * t * 0.37, -a2) / 5.3 - warp * 0.33 + 0.37;
    float2 uv3 = OkuRotate(xz - w * t * 0.58, -a3) / 2.1 + warp.yx * 0.5 + fine * 0.35 + 0.71;
    float4 n1 = tex2D(_OkuWaterNormals, uv1);
    float4 n2 = tex2D(_OkuWaterNormals, uv2);
    float4 n3 = tex2D(_OkuWaterNormals, uv3);
    crest = (n1.b - 0.5) * 0.4 + (n2.b - 0.5) * 0.35 + (n3.b - 0.5) * 0.25;
    return OkuRotate(n1.rg * 2 - 1, a1) * 0.10 + OkuRotate(n2.rg * 2 - 1, a2) * 0.12 + OkuRotate(n3.rg * 2 - 1, a3) * 0.08;
}

// A slow noise over tens of units, to vary roughness and foam.
float OkuMacro(float2 xz)
{
    return tex2D(_OkuWaterNoise, xz / 61.0 + _OkuWaterWind.xy * _OkuWaterTime * 0.002).r;
}

// The surface at a point: its slope, how high it rides (crest, about -1
// to 1) and the slow noise there. The swell's own regular waves add only a
// little to the slope, or they would show as stripes.
struct OkuWave
{
    float2 slope;
    float crest;
    float macro;
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
    float strength = lerp(0.6, 1.4, o.macro) * (0.35 + _OkuWaterWaves.y * 4.0) * lerp(0.3, 1, damp);
    strength *= lerp(1, 0.55, saturate(dist / 150));
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
    float3 sky = lerp(_OkuHaze.rgb * 1.15, _OkuWaterSky.rgb, pow(up, 0.6));
    float sd = saturate(dot(r, sunDir));
    return sky + sunColor * (pow(sd, 24) * 0.25);
}

// Sun glint: GGX with Schlick's fresnel for water.
float OkuGlint(float3 n, float3 v, float3 l, float rough)
{
    float3 h = normalize(v + l);
    float nh = saturate(dot(n, h)), nl = saturate(dot(n, l)), nv = saturate(dot(n, v)) + 1e-3;
    float a = rough * rough, a2 = a * a;
    float d = nh * nh * (a2 - 1) + 1;
    float D = a2 / (3.14159 * d * d);
    float k = a * 0.5 + 1e-4;
    float G = (nl / (nl * (1 - k) + k)) * (nv / (nv * (1 - k) + k));
    float F = 0.02 + 0.98 * pow(1 - saturate(dot(v, h)), 5);
    return D * G * F / (4 * nv);
}

// Schlick's fresnel with a touch more at the head-on angle than water has,
// so the sky still shows on the waves from an RTS camera.
float OkuFresnel(float3 n, float3 v)
{
    return 0.035 + 0.965 * pow(1 - saturate(dot(n, v)), 5);
}

// Foam lace, two layers drifting apart, 0 to 1.
float OkuFoamLace(float2 xz, float scale)
{
    float t = _OkuWaterTime;
    float2 w = _OkuWaterWind.xy;
    float a = tex2D(_OkuWaterFoam, xz / (3.3 * scale) + w * t * 0.013).r;
    float b = tex2D(_OkuWaterFoam, OkuRotate(xz, 1.1) / (2.1 * scale) - w * t * 0.021).r;
    return saturate(max(a, b) * 0.75 + min(a, b) * 0.45);
}

// How much of the lace shows for an amount of foam from 0 to 1: a little
// shows as its brightest threads, a lot as a solid sheet.
float OkuFoamCover(float lace, float amount)
{
    return smoothstep(1 - amount, 1.3 - amount, lace) * saturate(amount * 1.5);
}

// The painted ground under the sea, turned toward the sea bed's own
// colour with its lightness kept, so reefs and rocks still read.
half3 OkuBed(half3 c, float3 p)
{
    float depth = _OkuSeaLevel - p.y;
    if (depth <= 0) return c;
    half lum = dot(c, half3(0.3, 0.59, 0.11));
    half rel = clamp(lum / max(_OkuBedLuma, 0.03), 0.45, 1.6);
    return lerp(c, _OkuWaterBed.rgb * rel, saturate(depth / 0.4) * _OkuWaterBed.a);
}

// How glossy the ground is: never under the sea.
half OkuBedGloss(half gloss, float3 p)
{
    return gloss * saturate(1 - (_OkuSeaLevel - p.y) / 0.1);
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
