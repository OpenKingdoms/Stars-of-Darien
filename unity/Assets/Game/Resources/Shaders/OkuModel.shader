// Models, features and sprites: texture times vertex colour times an
// instanced tint, alpha tested, lit, casting and taking shadows, with a
// faint rim of sky light and an optional self light (_Emission), which an
// instance may raise or lower (_PulseLift, a lodestone's breath). An
// instance may dither away or in (_OkuFade), and a broken chunk's material
// shades the faces seen from inside as its interior (_Interior). Scenery
// carries the marks fire and magic left on it (SceneryLook.cs): char with
// embers in its cracks, frost, withering, leaves lost, a golden sheen,
// wetness, a sway and a trunk split down the middle.
Shader "OpenKingdoms/Presentation/Model"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Glossiness ("Smoothness", Range(0, 1)) = 0.2
        _Rim ("Rim light", Range(0, 1)) = 0.3
        _Emission ("Self light", Range(0, 4)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _OffsetFactor ("Depth offset factor", Float) = 0
        _OffsetUnits ("Depth offset units", Float) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _EmissionMap ("Emission map", 2D) = "white" {}
        _Surface ("Blended", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 0
        _ZWrite ("Depth write", Float) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness, lit physically", Range(0, 1)) = 0.5
        _MetalRoughMap ("Metal (b) and roughness (g)", 2D) = "white" {}
        _ClearCoat ("Clear coat", Range(0, 1)) = 0
        _ClearCoatSmoothness ("Clear coat smoothness", Range(0, 1)) = 1
        _Interior ("Back faces show the interior", Float) = 0
        _InteriorColor ("Interior", Color) = (0.56, 0.53, 0.48, 1)
    }
    // URP: the same look, lit by OkuLit.hlsl.
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        TEXTURE2D(_MetalRoughMap); SAMPLER(sampler_MetalRoughMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Cutoff, _Glossiness, _Rim, _Cull, _Emission;
            half _OffsetFactor, _OffsetUnits;
            half4 _EmissionColor;
            half _Surface, _SrcBlend, _DstBlend, _ZWrite;
            half _Metallic, _Smoothness, _ClearCoat, _ClearCoatSmoothness;
            half _Interior;
            half4 _InteriorColor;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        // Something being built: solid up to the cut, a glowing edge just
        // under it, and above it a faint screen-door ghost in team colour.
        float _BuildCut;        // world height of the cut
        float _BuildBand;       // how deep the glow reaches under it
        float _BuildGlow;       // how strong the glow is
        half4 _BuildTint;       // the team colour
        float _OkuBuildGhost;   // how much of the ghost shows, 0 to 1
        // The self light is 1 + _PulseLift times its rest, per instance.
        UNITY_INSTANCING_BUFFER_START(OkuPulse)
            UNITY_DEFINE_INSTANCED_PROP(float, _PulseLift)
            UNITY_DEFINE_INSTANCED_PROP(float, _OkuFade)
            UNITY_DEFINE_INSTANCED_PROP(float4, _OkuMark)    // char, frost, withering, holy sheen
            UNITY_DEFINE_INSTANCED_PROP(float4, _OkuHeat)    // ember glow, leaves lost, wetness
            UNITY_DEFINE_INSTANCED_PROP(float4, _OkuBend)    // the top's swing in x and z, the split, its angle
            UNITY_DEFINE_INSTANCED_PROP(float4, _OkuFoot)    // where it stands, and its height
        UNITY_INSTANCING_BUFFER_END(OkuPulse)
        // A 4 by 4 ordered pattern, 0 to 1, by screen pixel.
        float OkuBayer(float2 px)
        {
            uint2 p = uint2(px) & 3;
            uint i = p.y * 4 + p.x;
            const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            return (m[i] + 0.5) / 16;
        }
        // An instance fading: above 0 it dithers away, gone at 1, and below
        // 0 it dithers in, whole at -1. The two cover each other's holes.
        void OkuFadeClip(float fade, float2 px)
        {
            if (fade > 0) clip(OkuBayer(px) - fade);
            else if (fade < 0) clip(-fade - OkuBayer(px));
        }
        // Scenery swayed or split at the top, its foot staying where it stands.
        // slit carries the distance from the split's plane before it opened,
        // and how wide the slit is there, for the fragment to cut.
        float3 OkuSceneryBend(float3 p, float4 bend, float4 foot, out float2 slit)
        {
            slit = float2(1, 0);
            if (bend.x == 0 && bend.y == 0 && bend.z == 0) return p;
            float k = saturate((p.y - foot.y) / max(foot.w, 0.1));
            float3 q = p;
            q.xz += bend.xy * k * k;
            if (bend.z > 0)
            {
                float2 n = float2(cos(bend.w), sin(bend.w));
                float side = dot(p.xz - foot.xz, n);
                float open = smoothstep(0.08, 0.85, k);
                q.xz += n * (side >= 0 ? 1 : -1) * bend.z * open;
                q.y -= bend.z * open * 0.25;
                slit = float2(side, bend.z * open * 0.45);
            }
            return q;
        }
        // Leaves by their colour: greener than they are red or blue.
        half OkuLeafy(half3 c)
        {
            half mx = max(c.r, max(c.g, c.b));
            return saturate((c.g - max(c.r, c.b)) / max(mx, 0.02) * 5);
        }
        float OkuMarkHash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1) * 17;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }
        float OkuMarkNoise(float3 x)
        {
            float3 i = floor(x), f = frac(x);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(lerp(OkuMarkHash(i), OkuMarkHash(i + float3(1, 0, 0)), f.x), lerp(OkuMarkHash(i + float3(0, 1, 0)), OkuMarkHash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(OkuMarkHash(i + float3(0, 0, 1)), OkuMarkHash(i + float3(1, 0, 1)), f.x), lerp(OkuMarkHash(i + float3(0, 1, 1)), OkuMarkHash(i + 1), f.x), f.y), f.z);
        }
        // Leaves that fire, magic or the wind took, and the slit of a split trunk, cut away.
        void OkuSceneryClip(half3 c, float3 p, float bare, float2 slit)
        {
            if (bare > 0) clip(OkuMarkNoise(p * 2.3) + (1 - OkuLeafy(c)) * 2 - bare * 1.05);
            if (slit.y > 0) clip(abs(slit.x) - slit.y);
        }
        // The marks on a surface: its colour, its gloss and the light it gives off.
        void OkuSceneryMarks(inout half3 c, inout half gloss, inout half3 emit, half3 n, half3 view, float3 p, float4 mark, float4 heat)
        {
            half leafy = OkuLeafy(c);
            half lum = dot(c, half3(0.3, 0.59, 0.11));
            float big = OkuMarkNoise(p * 1.7), fine = OkuMarkNoise(p * 6.3 + 11.1);
            // Withering: leaves grey, dark and faintly violet, wood a little darker.
            if (mark.z > 0)
            {
                // Dark leaves go ashen rather than darker still.
                half3 dead = max(lerp(half3(lum, lum, lum), c, 0.15) * half3(0.66, 0.6, 0.7), half3(0.12, 0.11, 0.13));
                c = lerp(c, dead, saturate(mark.z) * lerp(0.3, 0.95, leafy));
            }
            // Wet: darker and glossy.
            if (heat.z > 0)
            {
                c *= 1 - 0.4 * heat.z;
                gloss = lerp(gloss, 0.75, heat.z);
            }
            // Char creeps over it in patches, soot black where it took, with
            // embers glowing in its cracks while it is hot.
            if (mark.x > 0)
            {
                half took = saturate((mark.x * 1.3 - big * 0.85 - fine * 0.3) * 6);
                c = lerp(c, c * 0.08 + half3(0.016, 0.013, 0.011), took);
                gloss = lerp(gloss, 0.04, took);
                // Thin cracks where the grain's noise crosses its middle, and a few hot spots.
                half crack = 1 - smoothstep(0.0, 0.03 + 0.03 * heat.x, abs(fine - 0.5));
                half spot = smoothstep(0.8, 0.95, OkuMarkNoise(p * 3.1 + 5.3));
                half pulse = 0.7 + 0.3 * sin(_Time.y * 3.1 + big * 13);
                emit += half3(1.0, 0.28, 0.04) * heat.x * (crack * 0.8 + spot * 0.6) * took * (1 - 0.7 * leafy) * pulse * 2.2;
            }
            // Rime on what faces up and out, white and icy, glinting.
            if (mark.y > 0)
            {
                half rime = saturate((mark.y * 1.35 - (1 - saturate(n.y * 0.65 + 0.45)) * 0.6 - fine * 0.4) * 3);
                c = lerp(c, half3(0.8, 0.88, 0.97) * (0.9 + 0.2 * fine), rime * 0.9);
                gloss = lerp(gloss, 0.7, rime);
                emit += half3(0.6, 0.8, 1.0) * rime * smoothstep(0.92, 0.99, OkuMarkNoise(p * 23)) * 1.5;
            }
            // Holy light: a golden sheen, brightest at the edges it shows.
            if (mark.w > 0)
            {
                half rim = 1 - saturate(dot(n, view));
                emit += half3(1.0, 0.76, 0.32) * mark.w * (0.1 + 0.3 * lum + rim * rim * 1.3);
            }
        }
        // The inside of a broken chunk: its interior with some of the face's
        // own colour, grained so it reads as wood or stone.
        half3 OkuInterior(half3 face, float3 positionWS)
        {
            float g = frac(sin(dot(floor(positionWS * 7), float3(12.9898, 78.233, 37.719))) * 43758.5453);
            return lerp(face * 0.6, _InteriorColor.rgb, 0.6) * (0.8 + 0.4 * g);
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Offset [_OffsetFactor], [_OffsetUnits]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _OKU_BUILD
            // multi_compile, since the materials are made at run time and a
            // build keeps no shader_feature variant that no material asset uses.
            #pragma multi_compile_local _ _EMISSION
            #pragma multi_compile_local _ _OKU_PBR
            #pragma multi_compile_local _ _CLEARCOAT
            #include "../../Shaders/OkuLit.hlsl"
            #include "../../Shaders/OkuFog.hlsl"
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half4 color : COLOR; half fog : TEXCOORD3; nointerpolation float fade : TEXCOORD4; float2 slit : TEXCOORD5; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = OkuSceneryBend(TransformObjectToWorld(v.positionOS.xyz), UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuBend), UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuFoot), o.slit);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.fade = UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuFade);
                return o;
            }
            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                clip(c.a - _Cutoff);
                OkuFadeClip(i.fade, i.positionCS.xy);
                float4 mark = UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuMark);
                float4 heat = UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuHeat);
                OkuSceneryClip(c.rgb, i.positionWS, heat.y, i.slit);
                // A face seen from behind, where culling is off, is lit as
                // its other side.
                half3 n = normalize(i.normalWS) * (front ? 1 : -1);
                half gloss = _Glossiness;
                half3 marked = 0;
                if (any(mark != 0) || heat.x != 0 || heat.z != 0)
                    OkuSceneryMarks(c.rgb, gloss, marked, n, SafeNormalize(GetWorldSpaceViewDir(i.positionWS)), i.positionWS, mark, heat);
                bool inside = _Interior > 0.5 && !front;
                if (inside)
                {
                    // Lit as one flat face turned to the eye, it reads as the cut rather than a hollow.
                    c.rgb = OkuInterior(c.rgb, i.positionWS);
                    n = normalize(GetWorldSpaceViewDir(i.positionWS) + half3(0, 0.6, 0));
                }
            #if defined(_OKU_BUILD)
                float above = i.positionWS.y - _BuildCut;
                if (above > 0)
                {
                    clip(_OkuBuildGhost * 0.3 - OkuBayer(i.positionCS.xy));
                    half3 ghost = _BuildTint.rgb * (0.35 + 0.25 * saturate(n.y * 0.5 + 0.5));
                    return half4(MixFog(ghost * OkuFogLight(i.positionWS), i.fog), 1);
                }
            #endif
            #if defined(_OKU_PBR)
                // A drop-in model's own metal and roughness, lit as URP's Lit
                // is, with the sky's reflections in its metals and gems.
                half4 mr = SAMPLE_TEXTURE2D(_MetalRoughMap, sampler_MetalRoughMap, i.uv);
                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = n;
                input.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fog;
                input.bakedGI = SampleSH(n);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = 1;
                SurfaceData surf = (SurfaceData)0;
                surf.albedo = c.rgb;
                surf.metallic = inside ? 0 : _Metallic * mr.b;
                surf.smoothness = max(saturate(1 - (1 - _Smoothness) * mr.g), gloss - _Glossiness);
                surf.occlusion = 1;
                surf.alpha = c.a;
                surf.normalTS = half3(0, 0, 1);
                surf.clearCoatMask = _ClearCoat;
                surf.clearCoatSmoothness = _ClearCoatSmoothness;
                half3 rgb = UniversalFragmentPBR(input, surf).rgb;
            #else
                half3 rgb = OkuLight(c.rgb, i.positionWS, n, i.positionCS, gloss, _Rim);
            #endif
                rgb += marked;
                half breath = 1 + UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _PulseLift);
                rgb += c.rgb * _Emission * breath;
            #if defined(_OKU_BUILD)
                // The glow rides the walls at the cut, not floors lying in it.
                half edge = saturate(1 + above / max(_BuildBand, 1e-3));
                rgb += half3(1.6, 1.15, 0.55) * edge * edge * (1 - abs(n.y) * 0.85) * _BuildGlow * (0.6 + 0.4 * c.rgb);
            #endif
            #if defined(_EMISSION)
                // Light the surface gives off itself, bright enough to bloom.
                rgb += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb * breath;
            #endif
                rgb *= OkuFogLight(i.positionWS);
                return half4(MixFog(rgb, i.fog), _Surface > 0.5 ? c.a : 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_local _ _OKU_BUILD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float height : TEXCOORD1; nointerpolation float fade : TEXCOORD2; float2 slit : TEXCOORD3; nointerpolation float bare : TEXCOORD4; float3 positionWS : TEXCOORD5; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.fade = UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuFade);
                o.bare = UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuHeat).y;
                float3 p = OkuSceneryBend(TransformObjectToWorld(v.positionOS.xyz), UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuBend), UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuFoot), o.slit);
                o.positionWS = p;
                float3 n = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 dir = normalize(_LightPosition - p);
            #else
                float3 dir = _LightDirection;
            #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(p, n, dir));
            #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.height = p.y;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                clip(c.a - _Cutoff);
                OkuFadeClip(i.fade, i.positionCS.xy);
                OkuSceneryClip(c.rgb, i.positionWS, i.bare, i.slit);
            #if defined(_OKU_BUILD)
                clip(_BuildCut - i.height);
            #endif
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            Offset [_OffsetFactor], [_OffsetUnits]
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _OKU_BUILD
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float height : TEXCOORD1; nointerpolation float fade : TEXCOORD2; float2 slit : TEXCOORD3; nointerpolation float bare : TEXCOORD4; float3 positionWS : TEXCOORD5; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.fade = UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuFade);
                o.bare = UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuHeat).y;
                float3 p = OkuSceneryBend(TransformObjectToWorld(v.positionOS.xyz), UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuBend), UNITY_ACCESS_INSTANCED_PROP(OkuPulse, _OkuFoot), o.slit);
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.height = p.y;
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                clip(c.a - _Cutoff);
                OkuFadeClip(i.fade, i.positionCS.xy);
                OkuSceneryClip(c.rgb, i.positionWS, i.bare, i.slit);
            #if defined(_OKU_BUILD)
                if (i.height > _BuildCut) clip(_OkuBuildGhost * 0.3 - OkuBayer(i.positionCS.xy));
            #endif
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Cull [_Cull]
        Offset [_OffsetFactor], [_OffsetUnits]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow alphatest:_Cutoff
        #pragma multi_compile_instancing
        #pragma target 3.5
        #include "../../Shaders/OkuFog.hlsl"
        sampler2D _MainTex;
        half _Glossiness;
        half _Rim;
        half _Emission;
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 viewDir;
            float3 worldPos;
        };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * IN.color * UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
            o.Albedo = c.rgb;
            o.Alpha = c.a;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
            // A soft sky-coloured rim, so small models read against the ground.
            half rim = 1 - saturate(dot(normalize(IN.viewDir), o.Normal));
            half fog = OkuFogLight(IN.worldPos);
            o.Albedo *= fog;
            o.Emission = (unity_AmbientSky.rgb * pow(rim, 3) * _Rim * 2 + _Emission) * c.rgb * fog;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/Diffuse"
}
