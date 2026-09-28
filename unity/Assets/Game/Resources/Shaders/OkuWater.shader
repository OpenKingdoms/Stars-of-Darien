// The sea. Under URP it draws the scene under it itself, from the opaque
// and depth textures: the sea floor bent by the waves and dimmed by the
// water it is seen through (red first, so shallows run turquoise and the
// deep turns blue-green), the sky and sun off the surface by Fresnel, and
// foam along the shore, round anything standing in the water and on the
// crests in a storm. The built-in pipeline gets the same surface over
// alpha blending. Everything it reads is set by WaterView.
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "../../Shaders/OkuWaterCommon.hlsl"
            #include "../../Shaders/OkuFog.hlsl"
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
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float2 sea = OkuSeaAt(p.xz);
                float2 slope;
                float crest;
                p += OkuSwell(p.xz, OkuDamp(sea.x), 2, slope, crest);
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.screen = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.positionWS;
                float3 cam = GetCameraPositionWS();
                float3 toCam = cam - p;
                float dist = length(toCam);
                float3 v = toCam / dist;
                float2 suv = i.screen.xy / i.screen.w;
                float surfEye = i.screen.w;

                float2 sea = OkuSeaAt(p.xz);
                float depthBelow = sea.x, shore = sea.y;
                float damp = OkuDamp(depthBelow);

                // The surface: the finer waves over a little of the swell,
                // rougher where the slow noise and the weather say, calmer
                // in the shallows and far off, and rain rings in the rain.
                OkuWave w = OkuWaveAt(p.xz, damp, dist);
                if (_OkuWaterWaves.w > 0.01) w.slope += OkuRain(p.xz, dist) * _OkuWaterWaves.w;
                float3 n = normalize(float3(-w.slope.x, 1, -w.slope.y));

                // What lies under the surface, bent by the waves where the
                // water is deep enough, never pulling in what stands above it.
                float rawScene = SampleSceneDepth(suv);
                float sceneEye = LinearEyeDepth(rawScene, _ZBufferParams);
                float rayScale = dist / max(surfEye, 1e-3);
                float thick = max(sceneEye - surfEye, 0) * rayScale;
                float2 bend = n.xz * 0.06 * saturate(thick * 1.5) * saturate(12 / max(surfEye, 1));
                float2 ruv = suv + bend;
                float refrEye = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (refrEye < surfEye) { ruv = suv; refrEye = sceneEye; }
                float path = max(refrEye - surfEye, 0) * rayScale;
                float under = path * v.y;
                float3 scene = SampleSceneColor(ruv);

                // Light through the water: absorbed on the way down to the
                // floor and back up the view ray, scattered back toward the
                // eye, a little lighter where the waves ride high and in
                // broad slow patches, as wind and current leave a real sea.
                Light sun = GetMainLight(TransformWorldToShadowCoord(p));
                float shadow = lerp(1, sun.shadowAttenuation, 0.75);
                float3 ambient = SampleSH(float3(0, 1, 0));
                float3 lightIn = sun.color * shadow * saturate(sun.direction.y + 0.2) + ambient;
                float3 t = exp(-_OkuWaterSigma.rgb * (path + under));
                float3 inscatter = _OkuWaterScatter.rgb * lightIn * (1 + w.crest * 0.3 * damp) * lerp(0.85, 1.15, w.macro);
                inscatter += _OkuWaterScatter.rgb * sun.color * shadow * saturate(w.crest) * 0.6 * damp;
                float3 water = scene * t + inscatter * (1 - t);

                // The sky and sun off the surface. The sky is read off a
                // steeper copy of the waves, so it moves on them as it does
                // on a real sea seen from high up.
                float3 nr = normalize(float3(-w.slope.x * 1.8, 1, -w.slope.y * 1.8));
                float3 r = reflect(-v, nr);
                r.y = abs(r.y);
                float fres = saturate(OkuFresnel(nr, v) * _OkuWaterSky.a);
                float3 sky = OkuSky(r, sun.direction, sun.color);
                water = lerp(water, sky, fres);
                float rough = max(0.05 + _OkuWaterWaves.y * 0.8, 0.03 + dist * 0.0012);
                // The weather's glint strength scales the clamped glint, so
                // haze dims every sparkle rather than thinning them out.
                float glint = min(OkuGlint(n, v, sun.direction, rough), 4) * sun.shadowAttenuation * _OkuWaterScatter.a;
                float sheen = min(OkuGlint(n, v, sun.direction, 0.35), 1) * sun.shadowAttenuation * _OkuWaterScatter.a;
                water += sun.color * (glint + sheen * 0.3);

                // Foam: a broken line where the water meets the shore, or a
                // hull or post standing in deeper water, but not across a flat
                // of wet sand just under the surface. Bands rolling in to the
                // shore, from the baked distance to it. Whitecaps in a storm.
                float lace = OkuFoamLace(p.xz, 1);
                float edge = (1 - smoothstep(0.0, 0.1, under)) * max(saturate(depthBelow / 0.4), saturate(1.5 - shore / 0.8)) * 0.9;
                float nearShore = saturate(1 - shore / 0.9) * step(0, shore + 0.5);
                float wash = pow(saturate(sin(6.2831853 * shore / 1.4 - _OkuWaterTime * 1.2 + w.macro * 5)), 4) * exp(-max(shore, 0) / 0.9);
                float caps = saturate((w.crest - 0.25) * 4) * _OkuWaterWaves.z * lerp(0.6, 1.2, w.macro);
                float foam = OkuFoamCover(lace, saturate(edge + nearShore * 0.3 + wash * 0.55 + caps) * 0.95) * 0.92;
                foam *= saturate(thick / 0.01);
                float3 foamLight = sun.color * (saturate(dot(n, sun.direction)) * shadow) + ambient;
                water = lerp(water, foamLight * 0.85, foam);

                // Past the map's edge the sea melts into the haze as the land does.
                if (_OkuMapSize.x > 0)
                {
                    float2 over = max(max(-float2(p.x, -p.z), float2(p.x, -p.z) - _OkuMapSize.xy), 0);
                    float away = length(over) / max(_OkuSeaCell.x, 1e-3) / 32;
                    water = lerp(water, _OkuHaze.rgb, smoothstep(0, 1, saturate(away)));
                }

                water = MixFog(water, i.fog);
                water *= OkuFogLight(p);
                return half4(water, 1);
            }
            ENDHLSL
        }
    }
    // The built-in pipeline: the same surface, blended over the ground.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
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
                float3 p = mul(unity_ObjectToWorld, v.vertex).xyz;
                float2 sea = OkuSeaAt(p.xz);
                float2 slope;
                float crest;
                p += OkuSwell(p.xz, OkuDamp(sea.x), 2, slope, crest);
                o.world = p;
                o.pos = mul(UNITY_MATRIX_VP, float4(p, 1));
                o.screen = ComputeScreenPos(o.pos);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 p = i.world;
                float3 toCam = _WorldSpaceCameraPos - p;
                float dist = length(toCam);
                float3 v = toCam / dist;
                float2 sea = OkuSeaAt(p.xz);
                float damp = OkuDamp(sea.x);
                OkuWave w = OkuWaveAt(p.xz, damp, dist);
                if (_OkuWaterWaves.w > 0.01) w.slope += OkuRain(p.xz, dist) * _OkuWaterWaves.w;
                float3 n = normalize(float3(-w.slope.x, 1, -w.slope.y));
                float sceneEye = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                float thick = max(sceneEye - i.screen.w, 0) * dist / max(i.screen.w, 1e-3);
                float under = thick * v.y;
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float3 lightIn = _LightColor0.rgb * saturate(l.y + 0.2) + ShadeSH9(float4(0, 1, 0, 1));
                // What shows of the ground is what the water lets through.
                float3 t = exp(-_OkuWaterSigma.rgb * (thick + under));
                float alpha = 1 - dot(t, float3(0.3, 0.59, 0.11));
                float3 water = _OkuWaterScatter.rgb * lightIn * (1 + w.crest * 0.3 * damp) * lerp(0.85, 1.15, w.macro);
                float3 nr = normalize(float3(-w.slope.x * 1.8, 1, -w.slope.y * 1.8));
                float3 r = reflect(-v, nr);
                r.y = abs(r.y);
                float fres = saturate(OkuFresnel(nr, v) * _OkuWaterSky.a);
                water = lerp(water, OkuSky(r, l, _LightColor0.rgb), fres);
                alpha = saturate(alpha + fres);
                float rough = max(0.05 + _OkuWaterWaves.y * 0.8, 0.03 + dist * 0.0012);
                float glint = (min(OkuGlint(n, v, l, rough), 4) + min(OkuGlint(n, v, l, 0.35), 1) * 0.3) * _OkuWaterScatter.a;
                water += _LightColor0.rgb * glint;
                float lace = OkuFoamLace(p.xz, 1);
                float edge = (1 - smoothstep(0.0, 0.1, under)) * max(saturate(sea.x / 0.4), saturate(1.5 - sea.y / 0.8)) * 0.9;
                float nearShore = saturate(1 - sea.y / 0.9) * step(0, sea.y + 0.5);
                float foam = OkuFoamCover(lace, saturate(edge + nearShore * 0.3) * 0.95) * 0.92;
                water = lerp(water, lightIn * 0.85, foam);
                alpha = saturate(max(alpha, foam) + glint * 0.2) * saturate(thick / 0.05);
                if (_OkuMapSize.x > 0)
                {
                    float2 over = max(max(-float2(p.x, -p.z), float2(p.x, -p.z) - _OkuMapSize.xy), 0);
                    float away = length(over) / max(_OkuSeaCell.x, 1e-3) / 32;
                    water = lerp(water, _OkuHaze.rgb, smoothstep(0, 1, saturate(away)));
                }
                fixed4 c = fixed4(water * OkuFogLight(p), alpha);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
