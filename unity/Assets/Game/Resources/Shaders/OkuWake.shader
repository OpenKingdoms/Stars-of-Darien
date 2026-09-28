// Ships' wakes on the sea: foam along the arms of the Kelvin wedge and in
// the churned water behind the stern, which the air in it lightens, and a
// collar at the hull with a bow wave. Lies on the same swell as the sea.
// Mesh from WaterWakes.
Shader "OpenKingdoms/Presentation/Wake"
{
    Properties
    {
        _Pad ("Unused", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-9" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../../Shaders/OkuWaterCommon.hlsl"
            #include "../../Shaders/OkuFog.hlsl"
            #include "../../Shaders/OkuWakeFoam.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Pad;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; float2 size : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 uv : TEXCOORD1; half fog : TEXCOORD2; float2 size : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float2 slope;
                float crest;
                p += OkuSwell(p.xz, OkuDamp(OkuSeaAt(p.xz).x), 2, slope, crest);
                p.y += 0.012;
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = v.uv;
                o.size = v.size;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float churned;
                float a = OkuWakeFoam(i.uv, i.size, i.positionWS.xz, churned);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 light = sun.color * (saturate(sun.direction.y) * lerp(0.35, 1, sun.shadowAttenuation)) + SampleSH(float3(0, 1, 0));
                // Foam over water with air in it, the sea's colour lifted.
                float3 aerated = (_OkuWaterScatter.rgb * 4 + 0.06) * light;
                float alpha = a + churned * (1 - a);
                float3 c = (light * 0.92 * a + aerated * churned * (1 - a)) / max(alpha, 1e-4);
                c = MixFog(c, i.fog) * OkuFogLight(i.positionWS);
                return half4(c * alpha, alpha);
            }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-9" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend One OneMinusSrcAlpha
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
            #include "../../Shaders/OkuWakeFoam.hlsl"
            struct appdata { float4 vertex : POSITION; float4 uv : TEXCOORD0; float2 size : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float4 uv : TEXCOORD1; UNITY_FOG_COORDS(2) float2 size : TEXCOORD3; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 p = mul(unity_ObjectToWorld, v.vertex).xyz;
                float2 slope;
                float crest;
                p += OkuSwell(p.xz, OkuDamp(OkuSeaAt(p.xz).x), 2, slope, crest);
                p.y += 0.012;
                o.world = p;
                o.pos = mul(UNITY_MATRIX_VP, float4(p, 1));
                o.uv = v.uv;
                o.size = v.size;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float churned;
                float a = OkuWakeFoam(i.uv, i.size, i.world.xz, churned);
                float3 light = _LightColor0.rgb * saturate(_WorldSpaceLightPos0.y) + ShadeSH9(float4(0, 1, 0, 1));
                float3 aerated = (_OkuWaterScatter.rgb * 4 + 0.06) * light;
                float alpha = a + churned * (1 - a);
                fixed4 c = fixed4((light * 0.92 * a + aerated * churned * (1 - a)) / max(alpha, 1e-4) * OkuFogLight(i.world), 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return fixed4(c.rgb * alpha, alpha);
            }
            ENDCG
        }
    }
}
