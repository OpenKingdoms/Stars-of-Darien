// Models, features and sprites: texture times vertex colour times an
// instanced tint, alpha tested, lit, casting and taking shadows, with a
// faint rim of sky light and an optional self light (_Emission).
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
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        // Something being built: solid up to the cut, a glowing edge just
        // under it, and above it a faint screen-door ghost in team colour.
        float _BuildCut;        // world height of the cut
        float _BuildBand;       // how deep the glow reaches under it
        float _BuildGlow;       // how strong the glow is
        half4 _BuildTint;       // the team colour
        float _OkuBuildGhost;   // how much of the ghost shows, 0 to 1
        // A 4 by 4 ordered pattern, 0 to 1, by screen pixel.
        float OkuBayer(float2 px)
        {
            uint2 p = uint2(px) & 3;
            uint i = p.y * 4 + p.x;
            const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            return (m[i] + 0.5) / 16;
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _OKU_BUILD
            #pragma shader_feature_local _EMISSION
            #pragma shader_feature_local _OKU_PBR
            #pragma shader_feature_local _CLEARCOAT
            #include "../../Shaders/OkuLit.hlsl"
            #include "../../Shaders/OkuFog.hlsl"
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half4 color : COLOR; half fog : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                clip(c.a - _Cutoff);
                // A face seen from behind, where culling is off, is lit as
                // its other side.
                half3 n = normalize(i.normalWS) * (front ? 1 : -1);
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
                surf.metallic = _Metallic * mr.b;
                surf.smoothness = saturate(1 - (1 - _Smoothness) * mr.g);
                surf.occlusion = 1;
                surf.alpha = c.a;
                surf.normalTS = half3(0, 0, 1);
                surf.clearCoatMask = _ClearCoat;
                surf.clearCoatSmoothness = _ClearCoatSmoothness;
                half3 rgb = UniversalFragmentPBR(input, surf).rgb;
            #else
                half3 rgb = OkuLight(c.rgb, i.positionWS, n, i.positionCS, _Glossiness, _Rim);
            #endif
                rgb += c.rgb * _Emission;
            #if defined(_OKU_BUILD)
                // The glow rides the walls at the cut, not floors lying in it.
                half edge = saturate(1 + above / max(_BuildBand, 1e-3));
                rgb += half3(1.6, 1.15, 0.55) * edge * edge * (1 - abs(n.y) * 0.85) * _BuildGlow * (0.6 + 0.4 * c.rgb);
            #endif
            #if defined(_EMISSION)
                // Light the surface gives off itself, bright enough to bloom.
                rgb += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
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
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float height : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
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
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.color.a * _Color.a - _Cutoff);
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
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float height : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(p);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.height = p.y;
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.color.a * _Color.a - _Cutoff);
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
