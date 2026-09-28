// The sea. It draws the scene under it itself: the sea floor bent by the
// waves and dimmed by the water it is seen through (red first, so shallows
// run turquoise and the deep turns blue-green), the sky and sun off the
// surface, foam along the shore and in the ships' wakes. URP gives it the
// opaque and depth textures, the built-in pipeline a grab of the screen and
// the depth texture. The shading is OkuWaterShade.hlsl, and everything it
// reads is set by WaterView.
Shader "OpenKingdoms/Presentation/Water"
{
    Properties
    {
        _Pad ("Unused", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend Off
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            // One hardware-filtered tap of the sun's shadow is soft enough
            // on water, so the soft-shadow keywords are left out.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "../../Shaders/OkuWaterCommon.hlsl"
            #include "../../Shaders/OkuFog.hlsl"
            // The sun's shadow in the water, taken at three depths down the
            // view ray and averaged, so a shadow on the sea is soft.
            float OkuWaterShadow(float3 p, float3 v, float depth)
            {
                float s = 0;
                [unroll] for (int k = 0; k < 3; k++)
                {
                    float d = min(depth, 0.35 + k * 0.8);
                    s += MainLightRealtimeShadow(TransformWorldToShadowCoord(p - v * (d / max(v.y, 0.2))));
                }
                return s / 3;
            }
            #define OKU_SCENE_EYE(uv) LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams)
            #define OKU_SCENE_COLOR(uv) SampleSceneColor(uv)
            #define OKU_WATER_SHADOW(p, v, depth) OkuWaterShadow(p, v, depth)
            #include "../../Shaders/OkuWaterShade.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Pad;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screen : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = OkuSeaVertex(TransformObjectToWorld(v.positionOS.xyz));
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.screen = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                OkuSun sun;
                sun.dir = light.direction;
                sun.color = light.color;
                sun.shadow = light.shadowAttenuation;
                sun.ambient = SampleSH(float3(0, 1, 0));
                float3 water = OkuWaterShade(i.positionWS, GetCameraPositionWS(), i.screen.xy / i.screen.w, i.screen.w, sun);
                water = MixFog(water, i.fog);
                water *= OkuFogLight(i.positionWS);
                return half4(water, 1);
            }
            ENDHLSL
        }
    }
    // The built-in pipeline: the same sea over a grab of the screen.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }
        GrabPass { "_OkuWaterGrab" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend Off
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "../../Shaders/OkuWaterCommon.hlsl"
            #include "../../Shaders/OkuFog.hlsl"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler2D _OkuWaterGrab;
            // The grab is upside down to the screen on some platforms.
            float2 OkuGrabUv(float2 uv)
            {
            #if UNITY_UV_STARTS_AT_TOP
                float s = -_ProjectionParams.x;
            #else
                float s = _ProjectionParams.x;
            #endif
                return float2(uv.x, s > 0 ? uv.y : 1 - uv.y);
            }
            #define OKU_SCENE_EYE(uv) LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv))
            #define OKU_SCENE_COLOR(uv) tex2D(_OkuWaterGrab, OkuGrabUv(uv)).rgb
            #define OKU_WATER_SHADOW(p, v, depth) 1
            #include "../../Shaders/OkuWaterShade.hlsl"

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float4 screen : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 p = OkuSeaVertex(mul(unity_ObjectToWorld, v.vertex).xyz);
                o.world = p;
                o.pos = mul(UNITY_MATRIX_VP, float4(p, 1));
                o.screen = ComputeScreenPos(o.pos);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                OkuSun sun;
                sun.dir = normalize(_WorldSpaceLightPos0.xyz);
                sun.color = _LightColor0.rgb;
                sun.shadow = 1;
                sun.ambient = ShadeSH9(float4(0, 1, 0, 1));
                float3 water = OkuWaterShade(i.world, _WorldSpaceCameraPos, i.screen.xy / i.screen.w, i.screen.w, sun);
                fixed4 c = fixed4(water * OkuFogLight(i.world), 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
