// Writes stamps into the scar map, a pass a texture, keeping the stronger of
// what is there and what a stamp leaves. The dip and rim follow ScarStamps.cs
// exactly. The last pass takes a step off every texel, for marks that fade.
Shader "Hidden/OpenKingdoms/ScarStamp"
{
    Properties
    {
        _Step ("Step", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "UnityCG.cginc"
        float _Step;
        float _TexelUnits;      // world units a texel spans

        // local: the stamp's frame in world units (y along it), seed and kind.
        // a: reach, swath length, dent radius, floor. b: dip, rim, cracks, stone.
        // c: char, soil, blight. d: frost, wet, holy and heat left.
        struct appdata { float4 vertex : POSITION; float4 local : TEXCOORD0; float4 a : TEXCOORD1; float4 b : TEXCOORD2; float4 c : TEXCOORD3; float4 d : TEXCOORD4; };
        struct v2f { float4 pos : SV_POSITION; float4 local : TEXCOORD0; nointerpolation float4 a : TEXCOORD1; nointerpolation float4 b : TEXCOORD2; nointerpolation float4 c : TEXCOORD3; nointerpolation float4 d : TEXCOORD4; };

        v2f vert(appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.local = v.local; o.a = v.a; o.b = v.b; o.c = v.c; o.d = v.d;
            return o;
        }

        #define K_GUNPOWDER 1
        #define K_SIEGE 2
        #define K_IMPACT 3
        #define K_FIRE 4
        #define K_BREATH 5
        #define K_LIGHTNING 6
        #define K_FROST 7
        #define K_DARK 8
        #define K_WATER 9
        #define K_HOLY 10
        #define K_EARTH 11
        #define K_DUST 12

        float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + 1), f.x), f.y);
        }
        // About 0 to 0.94, centred near 0.47.
        float Fbm(float2 p) { return Noise(p) * 0.5 + Noise(p * 2.03 + 1.7) * 0.25 + Noise(p * 4.07 + 3.1) * 0.125 + Noise(p * 8.11 + 5.3) * 0.0625; }

        // ScarStamps.DepthAt, RimAt and Lumps.
        float DepthAt(float x, float floorShare) { return 1 - smoothstep(floorShare, 1, x); }
        float Lumps(float ang, float seed) { return 0.75 + 0.25 * sin(3 * ang + frac(seed * 0.1234) * 6.2831853) * cos(2 * ang + frac(seed * 0.5678) * 6.2831853); }
        float RimAt(float x, float ang, float seed)
        {
            float t = (x - 1.05) / 0.45;
            if (abs(t) >= 1) return 0;
            float b = 1 - t * t;
            return b * b * Lumps(ang, seed);
        }

        // Noise round a circle, so rays meet themselves where the angle wraps.
        float Rays(float ang, float seed, float count) { return Noise(float2(cos(ang), sin(ang)) * count + seed * 1.37); }

        // The angle a jagged branch runs at, r out from where it starts.
        float Wobble(float r, float a0, float s) { return a0 + (Noise(float2(r * 2.5, s)) - 0.5) * 0.9 + (Noise(float2(r * 7, s + 3.1)) - 0.5) * 0.25; }

        // How far p lies from a jagged branch leaving o at angle a0.
        float Branch(float2 p, float2 o, float a0, float len, float s)
        {
            float2 d = p - o;
            float r = length(d);
            if (r > len) return 1e3;
            float da = atan2(d.y, d.x) - Wobble(r, a0, s);
            da -= 6.2831853 * floor((da + 3.14159265) / 6.2831853);
            return abs(da) * r;
        }

        // Branching scorch round a strike: five main branches, each with a side one.
        float Fork(float2 p, float reach, float seed, float wide)
        {
            float best = 0;
            [unroll] for (int i = 0; i < 5; i++)
            {
                float a0 = seed * 0.731 + i * 1.2566 + (Hash21(float2(seed, i)) - 0.5) * 0.8;
                float len = reach * (0.6 + 0.4 * Hash21(float2(i, seed + 7)));
                float s = seed + i * 13.1;
                float r = length(p);
                float w = lerp(wide * 1.6, wide * 0.6, saturate(r / len));
                best = max(best, 1 - smoothstep(w, w + wide * 0.6, Branch(p, 0, a0, len, s)));
                float r0 = len * (0.35 + 0.2 * Hash21(float2(i + 3, seed)));
                float aw = Wobble(r0, a0, s);
                float2 o = r0 * float2(cos(aw), sin(aw));
                float side = aw + (Hash21(float2(i, seed + 11)) > 0.5 ? 0.7 : -0.7);
                float len2 = (len - r0) * 0.7;
                float w2 = lerp(wide, wide * 0.5, saturate(length(p - o) / len2));
                best = max(best, (1 - smoothstep(w2, w2 + wide * 0.5, Branch(p, o, side, len2, s + 2.7))) * 0.85);
            }
            return best;
        }

        // Long fissures out from an earth spell's centre.
        float Fissures(float2 p, float reach, float seed, float wide)
        {
            float best = 0;
            [unroll] for (int i = 0; i < 4; i++)
            {
                float a0 = seed * 0.37 + i * 1.5708 + (Hash21(float2(seed + 5, i)) - 0.5) * 0.9;
                float len = reach * (0.7 + 0.3 * Hash21(float2(i + 9, seed)));
                float r = length(p);
                float w = lerp(wide, wide * 0.3, saturate(r / len));
                best = max(best, 1 - smoothstep(w, w + wide, Branch(p, 0, a0, len, seed + i * 7.7)));
            }
            return best;
        }

        // How far p lies across a swath, 0 on its line and 1 at its edge.
        float Across(float2 p, float halfWidth, float len)
        {
            float2 q = float2(0, clamp(p.y, -len, 0));
            return distance(p, q) / max(halfWidth, 1e-3);
        }

        // How far out a fragment lies, 1 at the stamp's reach, and a fade past
        // it so ragged edges never meet the quad's own.
        float Spread(v2f i, float2 p) { return i.a.y > 0 ? Across(p, i.a.x, i.a.y) : length(p) / max(i.a.x, 1e-3); }
        float Outer(v2f i, float2 p) { return 1 - smoothstep(1.25, 1.38, Spread(i, p)); }

        // Where char lies for a stamp, 0 to 1 before its strength.
        float CharShape(v2f i, float2 p, float r, float seed, int kind)
        {
            float reach = i.a.x, dent = max(i.a.z, 1e-3);
            float ragged = Fbm(p * 1.7 + seed) - 0.47;
            if (kind == K_GUNPOWDER || kind == K_IMPACT)
            {
                if (i.a.z <= 0) return 1 - smoothstep(0.4, 1.0, r / reach + ragged * 0.6);
                float x = r / dent;
                float spread = kind == K_IMPACT ? 1.45 : 1.2;
                float core = 1 - smoothstep(0.45, spread - 0.15, x + ragged * 0.6);
                float streaks = smoothstep(0.5, 0.8, Rays(atan2(p.y, p.x), seed, 1.6)) * (1 - smoothstep(1.0, 2.0, x + ragged));
                return max(core, streaks * 0.45);
            }
            if (kind == K_FIRE) return 1 - smoothstep(0.55, 1.0, r / reach + ragged * 0.7);
            if (kind == K_BREATH)
            {
                float across = Across(p, reach, i.a.y);
                return (1 - smoothstep(0.5, 1.0, across + ragged * 0.6)) * (0.75 + 0.25 * Noise(float2(p.x * 5, p.y * 0.6 + seed)));
            }
            if (kind == K_LIGHTNING)
            {
                float fork = Fork(p, reach, seed, max(0.09, _TexelUnits * 0.7));
                float star = 1 - smoothstep(0.1, 0.4, r + ragged * 0.2);
                return max(fork, star);
            }
            return 0;
        }

        float4 fragMarks(v2f i) : SV_Target
        {
            float2 p = i.local.xy;
            float seed = i.local.z, r = length(p), reach = i.a.x;
            int kind = (int)round(i.local.w);
            float ragged = Fbm(p * 1.7 + seed) - 0.47;
            float charred = i.c.x * CharShape(i, p, r, seed, kind);
            float soil = 0, blight = 0, stone = 0;
            if (kind == K_GUNPOWDER || kind == K_IMPACT || kind == K_SIEGE)
            {
                float dent = max(i.a.z, 1e-3);
                float x = r / dent, outer = reach / dent;
                float inner = i.a.z > 0 ? 1 - smoothstep(1.1, 1.55, x + ragged * 0.5) : 1 - smoothstep(0.4, 1.0, r / reach + ragged * 0.5);
                // Earth flung out in rays past the rim, in clods.
                float rays = smoothstep(0.38, 0.72, Rays(atan2(p.y, p.x), seed + 4.1, kind == K_SIEGE ? 1.1 : 1.8));
                float clods = 0.55 + 0.45 * smoothstep(0.3, 0.65, Noise(p * 3.1 + seed * 2.3));
                float thrown = i.a.z > 0 ? rays * clods * (1 - smoothstep(1.3, outer, x + ragged * 0.6)) : 0;
                soil = i.c.y * max(inner, thrown);
                if (kind == K_SIEGE) stone = i.b.w * (1 - smoothstep(0.5, outer, x + ragged)) * smoothstep(0.3, 0.55, Noise(p * 1.9 + seed));
            }
            else if (kind == K_EARTH)
            {
                soil = i.c.y * Fissures(p, reach, seed, 0.6);
            }
            else if (kind == K_DUST)
            {
                soil = i.c.y * (1 - smoothstep(0.3, 1.0, r / reach + ragged * 0.6));
            }
            else if (kind == K_DARK)
            {
                float patch = 1 - smoothstep(0.45, 1.0, r / reach + ragged * 0.9);
                float veins = (1 - smoothstep(0.03, 0.08, abs(Noise(p * 2.2 + seed) - 0.5))) * (1 - smoothstep(0.6, 1.3, r / reach));
                blight = i.c.z * max(patch, veins * 0.8);
            }
            return saturate(float4(charred, soil, blight, stone) * Outer(i, p));
        }

        float4 fragShape(v2f i) : SV_Target
        {
            float2 p = i.local.xy;
            float seed = i.local.z, r = length(p), reach = i.a.x;
            int kind = (int)round(i.local.w);
            float depth = 0, rim = 0, crack = 0;
            if (i.a.z > 0)
            {
                float x = r / i.a.z;
                depth = i.b.x * DepthAt(x, i.a.w);
                rim = i.b.y * RimAt(x, atan2(p.y, p.x), seed);
            }
            if (kind == K_FROST) crack = i.b.z * (1 - smoothstep(0.35, 0.75, r / reach + (Fbm(p * 1.3 + seed) - 0.47) * 0.4));
            if (kind == K_EARTH) crack = i.b.z * Fissures(p, reach, seed, 0.5);
            return saturate(float4(depth, rim, crack * Outer(i, p), 0));
        }

        float4 fragFade(v2f i) : SV_Target
        {
            float2 p = i.local.xy;
            float seed = i.local.z, r = length(p), reach = i.a.x;
            int kind = (int)round(i.local.w);
            float ragged = Fbm(p * 1.3 + seed) - 0.47;
            // Lasting marks last longest at their heart, so their edges go first.
            float spread = kind == K_WATER && i.a.y > 0 ? Across(p, reach, i.a.y) : r / reach;
            float patch = (1 - smoothstep(0.6, 1.0, spread + ragged * 0.5)) * lerp(0.5, 1, saturate(1 - spread));
            float frost = i.d.x * patch, wet = i.d.y * patch;
            float ring = 1 - smoothstep(0.0, 0.22, abs(r / reach - 0.8));
            float holy = i.d.z * max(ring, 0.45 * (1 - smoothstep(0.5, 0.85, r / reach)));
            float heat = i.d.w * CharShape(i, p, r, seed, kind);
            if (kind == K_GUNPOWDER && i.a.z > 0) heat *= 1 - smoothstep(0.2, 0.8, r / i.a.z);
            return saturate(float4(frost, wet, holy, heat) * Outer(i, p));
        }
        ENDHLSL

        Pass
        {
            Name "Marks"
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragMarks
            ENDHLSL
        }
        Pass
        {
            Name "Shape"
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragShape
            ENDHLSL
        }
        Pass
        {
            Name "Fade"
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragFade
            ENDHLSL
        }
        Pass
        {
            Name "Step"
            Blend One One
            BlendOp RevSub
            HLSLPROGRAM
            #pragma vertex vertStep
            #pragma fragment fragStep
            float4 vertStep(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 fragStep() : SV_Target { return _Step; }
            ENDHLSL
        }
    }
}
