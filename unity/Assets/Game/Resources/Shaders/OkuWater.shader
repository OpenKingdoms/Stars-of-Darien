// The sea: gentle Gerstner waves, deep and shallow colour from the
// water's depth over the ground, foam where the ground comes close to
// the surface, sky reflection by Fresnel and a sun glint. The depth comes
// from a picture of the sea floor baked from the height grid, so it needs
// no camera depth texture.
Shader "OpenKingdoms/Presentation/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.1, 0.36, 0.4, 0.55)
        _Deep ("Deep", Color) = (0.01, 0.07, 0.13, 0.94)
        _Sky ("Sky", Color) = (0.62, 0.74, 0.86, 1)
        _Foam ("Foam", Color) = (0.95, 0.97, 1, 1)
        _DepthScale ("Depth to deep", Float) = 3
        _FoamWidth ("Foam width", Float) = 0.45
        _WaveHeight ("Wave height", Float) = 0.07
        _WaveLength ("Wave length", Float) = 7
        _WaveSpeed ("Wave speed", Float) = 1.2
        _Wind ("Wind direction", Vector) = (1, 0, 0.4, 0)
        _DepthTex ("Depth under the surface / 4", 2D) = "white" {}
        _MapRect ("Map x, z, width, depth", Vector) = (0, -64, 64, 64)
    }
    // URP: the same sea, lit by the main light.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_DepthTex); SAMPLER(sampler_DepthTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Deep, _Sky, _Foam;
                float _DepthScale, _FoamWidth, _WaveHeight, _WaveLength, _WaveSpeed;
                float4 _Wind, _MapRect, _DepthTex_ST;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; half fog : TEXCOORD2; };

            float3 Wave(float2 dir, float len, float amp, float speed, float3 p, inout float3 tangent, inout float3 binormal)
            {
                float k = 6.28318 / len;
                float f = k * (dot(dir, p.xz) - speed * _Time.y);
                float steep = 0.25;
                tangent += float3(-dir.x * dir.x * steep * sin(f), dir.x * amp * k * cos(f), -dir.x * dir.y * steep * sin(f));
                binormal += float3(-dir.x * dir.y * steep * sin(f), dir.y * amp * k * cos(f), -dir.y * dir.y * steep * sin(f));
                return float3(dir.x * amp * cos(f), amp * sin(f), dir.y * amp * cos(f));
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float2 w = normalize(_Wind.xz + 1e-4);
                float2 w2 = float2(w.y, -w.x) * 0.6 + w * 0.8;
                float3 t = float3(1, 0, 0), b = float3(0, 0, 1);
                float3 d = Wave(w, _WaveLength, _WaveHeight, _WaveSpeed, p, t, b)
                         + Wave(normalize(w2), _WaveLength * 0.53, _WaveHeight * 0.5, _WaveSpeed * 1.3, p, t, b)
                         + Wave(normalize(float2(-w.y, w.x)), _WaveLength * 0.31, _WaveHeight * 0.25, _WaveSpeed * 1.7, p, t, b);
                p += d;
                o.world = p;
                o.normal = normalize(cross(b, t));
                o.positionCS = TransformWorldToHClip(p);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 duv = (i.world.xz - _MapRect.xy) / _MapRect.zw;
                float inside = step(0, duv.x) * step(duv.x, 1) * step(0, duv.y) * step(duv.y, 1);
                float vdepth = lerp(4, SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, saturate(duv)).r * 4, inside);
                float3 view = SafeNormalize(GetWorldSpaceViewDir(i.world));
                float3 n = normalize(i.normal);
                half4 water = lerp(_Shallow, _Deep, saturate(vdepth / _DepthScale));
                float fresnel = pow(1 - saturate(dot(n, view)), 4);
                water.rgb = lerp(water.rgb, _Sky.rgb, fresnel * 0.6);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.world));
                float3 h = normalize(sun.direction + view);
                float spec = pow(saturate(dot(n, h)), 160) * 1.2 * sun.shadowAttenuation;
                water.rgb *= 0.55 + 0.45 * saturate(dot(n, sun.direction)) * sun.color * lerp(0.6, 1, sun.shadowAttenuation);
                water.rgb += spec * sun.color;
                float band = 1 - saturate(vdepth / _FoamWidth);
                float ripple = sin(i.world.x * 1.7 + _Time.y * 1.3) * sin(i.world.z * 2.1 - _Time.y * 0.9) * 0.5 + 0.5;
                float foam = saturate(band * (0.6 + 0.8 * ripple) - 0.25) * 1.4;
                water = lerp(water, _Foam, saturate(foam));
                water.a = saturate(max(water.a, foam) + spec);
                water.a *= saturate(vdepth / 0.06);
                water.rgb = MixFog(water.rgb, i.fog);
                return water;
            }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Shallow, _Deep, _Sky, _Foam;
            float _DepthScale, _FoamWidth, _WaveHeight, _WaveLength, _WaveSpeed;
            float4 _Wind;
            sampler2D _DepthTex;
            float4 _MapRect;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float4 screen : TEXCOORD2;
                float eyeDepth : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            // One Gerstner wave: displacement, and its share of the normal.
            float3 Wave(float2 dir, float len, float amp, float speed, float3 p, inout float3 tangent, inout float3 binormal)
            {
                float k = 6.28318 / len;
                float f = k * (dot(dir, p.xz) - speed * _Time.y);
                float steep = 0.25;
                float a = amp;
                tangent += float3(-dir.x * dir.x * steep * sin(f), dir.x * a * k * cos(f), -dir.x * dir.y * steep * sin(f));
                binormal += float3(-dir.x * dir.y * steep * sin(f), dir.y * a * k * cos(f), -dir.y * dir.y * steep * sin(f));
                return float3(dir.x * a * cos(f), a * sin(f), dir.y * a * cos(f));
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 p = mul(unity_ObjectToWorld, v.vertex).xyz;
                float2 w = normalize(_Wind.xz + 1e-4);
                float2 w2 = float2(w.y, -w.x) * 0.6 + w * 0.8;
                float3 t = float3(1, 0, 0), b = float3(0, 0, 1);
                float3 d = Wave(w, _WaveLength, _WaveHeight, _WaveSpeed, p, t, b)
                         + Wave(normalize(w2), _WaveLength * 0.53, _WaveHeight * 0.5, _WaveSpeed * 1.3, p, t, b)
                         + Wave(normalize(float2(-w.y, w.x)), _WaveLength * 0.31, _WaveHeight * 0.25, _WaveSpeed * 1.7, p, t, b);
                p += d;
                o.world = p;
                o.normal = normalize(cross(b, t));
                o.pos = mul(UNITY_MATRIX_VP, float4(p, 1));
                o.screen = ComputeScreenPos(o.pos);
                o.eyeDepth = -mul(UNITY_MATRIX_V, float4(p, 1)).z;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 duv = (i.world.xz - _MapRect.xy) / _MapRect.zw;
                float inside = step(0, duv.x) * step(duv.x, 1) * step(0, duv.y) * step(duv.y, 1);
                float vdepth = lerp(4, tex2D(_DepthTex, saturate(duv)).r * 4, inside);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float3 n = normalize(i.normal);
                fixed4 water = lerp(_Shallow, _Deep, saturate(vdepth / _DepthScale));
                float fresnel = pow(1 - saturate(dot(n, view)), 4);
                water.rgb = lerp(water.rgb, _Sky.rgb, fresnel * 0.6);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float3 h = normalize(l + view);
                float spec = pow(saturate(dot(n, h)), 160) * 1.2;
                water.rgb *= 0.55 + 0.45 * saturate(dot(n, l)) * _LightColor0.rgb;
                water.rgb += spec * _LightColor0.rgb;
                // Foam at the shore, broken up by moving noise.
                float band = 1 - saturate(vdepth / _FoamWidth);
                float ripple = sin(i.world.x * 1.7 + _Time.y * 1.3) * sin(i.world.z * 2.1 - _Time.y * 0.9) * 0.5 + 0.5;
                float foam = saturate(band * (0.6 + 0.8 * ripple) - 0.25) * 1.4;
                water = lerp(water, _Foam, saturate(foam));
                water.a = saturate(max(water.a, foam) + spec);
                // Fade in over the first touch of the ground, no hard edge.
                water.a *= saturate(vdepth / 0.06);
                UNITY_APPLY_FOG(i.fogCoord, water);
                return water;
            }
            ENDCG
        }
    }
}
