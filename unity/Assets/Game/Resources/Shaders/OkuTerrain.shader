// The ground: its picture, lit, taking shadows, with detail and bumps up
// close, rock laid on three planes over cliffs and more of the sky's light
// on them, a wet darkening near the water line, and under the sea a sandy
// bed with caustics, lit more evenly and softer the deeper it lies.
Shader "OpenKingdoms/Presentation/Terrain"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SeaLevel ("Sea level", Float) = -100
        _Glossiness ("Smoothness", Range(0, 1)) = 0.05
        _OkuDip ("Dips by the scar map", Float) = 0
    }
    // URP: the same look, lit by OkuLit.hlsl.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _SeaLevel;
            half _Glossiness;
            float _OkuDip;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        #include "../../Shaders/OkuScar.hlsl"
        // Where a vertex of the ground lies in the world, dipped by the scar
        // map in every pass alike, so shadows and depth follow the craters.
        float3 OkuGroundWS(float3 positionOS)
        {
            float3 p = TransformObjectToWorld(positionOS);
            if (_OkuDip > 0.5) p.y += OkuScarDip(p, _SeaLevel);
            return p;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "../../Shaders/OkuLit.hlsl"
            #include "../../Shaders/OkuFog.hlsl"
            #include "../../Shaders/OkuWaterCommon.hlsl"
            TEXTURE2D(_OkuDetail); SAMPLER(sampler_OkuDetail);
            float4 _OkuDetailParams;    // tile size, fade start and end, on
            TEXTURE2D(_OkuRock); SAMPLER(sampler_OkuRock);
            float4 _OkuRockParams;      // tile size, sky light added at the steepest, bump strength
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half4 color : COLOR; half fog : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = OkuGroundWS(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = 1;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 c = SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, i.uv, OkuBedBlur(i.positionWS));
                half3 n = normalize(i.normalWS);
                half steep = 0;
                if (_OkuDetailParams.w > 0)
                {
                    float s = 1 / _OkuDetailParams.x;
                    float dist = distance(GetCameraPositionWS(), i.positionWS);
                    // Past about 40 degrees the picture smears down the face,
                    // so there rock laid on three planes carries the detail
                    // over the picture's own colour, and its bumps catch the
                    // light. From afar more of the painting shows through, so
                    // the classic view keeps the original's painted cliffs.
                    steep = smoothstep(0.23, 0.45, 1 - n.y);
                    if (steep > 0)
                    {
                        half near = saturate((60 - dist) / 40);
                        half3 macro = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv, 4).rgb;
                        float r = 1 / max(_OkuRockParams.x, 0.1);
                        float3 w = pow(abs(n), 4);
                        w /= w.x + w.y + w.z;
                        half4 rx = SAMPLE_TEXTURE2D(_OkuRock, sampler_OkuRock, i.positionWS.zy * r);
                        half4 ry = SAMPLE_TEXTURE2D(_OkuRock, sampler_OkuRock, i.positionWS.xz * r);
                        half4 rz = SAMPLE_TEXTURE2D(_OkuRock, sampler_OkuRock, i.positionWS.xy * r);
                        half light = rx.r * w.x + ry.r * w.y + rz.r * w.z;
                        half3 painted = lerp(macro, c.rgb, lerp(0.45, 0.2, near));
                        c.rgb = lerp(c.rgb, painted * lerp(1, light * 2, lerp(0.6, 1, near)), steep);
                        // Each plane's slopes along its own two axes.
                        half3 bump = half3(0, rx.b - 0.5, rx.g - 0.5) * w.x + half3(ry.g - 0.5, 0, ry.b - 0.5) * w.y + half3(rz.g - 0.5, rz.b - 0.5, 0) * w.z;
                        n = normalize(n - bump * 2 * steep * _OkuRockParams.z);
                    }
                    // Up close, lightness and bumps around the picture's own,
                    // from two scales so the tiling does not show.
                    half fade = saturate((_OkuDetailParams.z - dist) / (_OkuDetailParams.z - _OkuDetailParams.y));
                    if (fade > 0)
                    {
                        float2 p = i.positionWS.xz * s;
                        half4 d = (SAMPLE_TEXTURE2D(_OkuDetail, sampler_OkuDetail, p) + SAMPLE_TEXTURE2D(_OkuDetail, sampler_OkuDetail, p * 0.37 + 0.21)) * 0.5;
                        c.rgb *= lerp(1, d.r * 2, fade);
                        n = normalize(n - half3(d.g * 2 - 1, 0, d.b * 2 - 1) * fade * 0.8);
                    }
                }
                // The battle's scars: dug and thrown earth, stone, blight,
                // char, cracks, frost, wet and holy light, lit through their dips.
                half gloss = _Glossiness;
                half3 emit = 0;
                OkuScarHere scar = OkuScarRead(i.positionWS);
                if (scar.any > 0)
                {
                    half near = saturate((140 - distance(GetCameraPositionWS(), i.positionWS)) / 70);
                    n = OkuScarNormal(n, i.positionWS);
                    c.rgb = OkuScarColour(c.rgb, scar, i.positionWS, near, n, gloss, emit);
                }
                half wet = saturate(1 - (i.positionWS.y - _SeaLevel) / 0.6);
                c.rgb *= lerp(1, 0.72, wet) * OkuWetBand(i.positionWS);
                c.rgb = OkuBed(c.rgb, i.positionWS);
                n = normalize(lerp(n, half3(0, 1, 0), OkuBedFlat(i.positionWS)));
                half shadow;
                half3 rgb = OkuLight(c.rgb, i.positionWS, n, i.positionCS, OkuBedGloss(lerp(gloss, 0.6, wet), i.positionWS), 0,
                    saturate(OkuUnder(i.positionWS) / 1.5), 1 + steep * _OkuRockParams.y, shadow) + emit;
                float2 gx = ddx(i.positionWS.xz), gy = ddy(i.positionWS.xz);
                if (i.positionWS.y < _OkuSeaLevel - 0.02 && _OkuWaterSigma.a > 0)
                    rgb += c.rgb * _MainLightColor.rgb * (OkuCaustics(i.positionWS, _MainLightPosition.xyz, gx, gy) * shadow);
                rgb *= OkuFogLight(i.positionWS) * OkuEdgeBand(i.positionWS);
                return half4(MixFog(rgb, i.fog), 1);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 p = OkuGroundWS(v.positionOS.xyz);
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
                o.uv = 0;
                o.color = 1;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(OkuGroundWS(v.positionOS.xyz));
                o.uv = 0;
                o.color = 1;
                return o;
            }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.5
        #include "../../Shaders/OkuFog.hlsl"
        #include "../../Shaders/OkuWaterCommon.hlsl"
        sampler2D _MainTex;
        float _SeaLevel;
        half _Glossiness;
        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2Dbias(_MainTex, float4(IN.uv_MainTex, 0, OkuBedBlur(IN.worldPos)));
            float wet = saturate(1 - (IN.worldPos.y - _SeaLevel) / 0.6);
            c.rgb *= lerp(1, 0.72, wet) * OkuWetBand(IN.worldPos);
            c.rgb = OkuBed(c.rgb, IN.worldPos);
            o.Albedo = c.rgb * OkuFogLight(IN.worldPos) * OkuEdgeBand(IN.worldPos);
            o.Smoothness = OkuBedGloss(lerp(_Glossiness, 0.6, wet), IN.worldPos);
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
