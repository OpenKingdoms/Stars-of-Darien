// The fog of war over the finished URP picture. Each pixel's point comes
// from the depth buffer, and its brightness on screen is scaled by the
// fog there: 0 never seen, SeenBefore seen before, 1 in sight. Past the
// map it takes the nearest edge's fog and lets it go beyond the edge
// ring, and the sky takes none.
Shader "Hidden/OpenKingdoms/FogOfWar"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "FogOfWar"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X_FLOAT(_OkuDepth);
            sampler2D _OkuFogTex;
            float4 _OkuFogRect;     // x, z of the north-west corner, width, depth
            float4x4 _OkuInvViewProj;
            float _OkuSeenBefore;   // screen brightness where seen before
            float _OkuFogReach;     // past the map, where the fog lets go
            float4 _OkuFogEye;      // the camera's position
            float _OkuSeaLevel;     // -1000 with no sea

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, i.texcoord);
                float raw = SAMPLE_TEXTURE2D_X(_OkuDepth, sampler_PointClamp, i.texcoord).r;
            #if UNITY_REVERSED_Z
                if (raw <= 0) return c;
                float z = 1 - 2 * raw;
            #else
                if (raw >= 1) return c;
                float z = 2 * raw - 1;
            #endif
                float4 p = mul(_OkuInvViewProj, float4(i.texcoord * 2 - 1, z, 1));
                float3 w = p.xyz / p.w;
                // The sea draws no depth, so under it the depth holds the
                // floor. The fog belongs where the eye meets the surface.
                if (w.y < _OkuSeaLevel && _OkuFogEye.y > _OkuSeaLevel)
                {
                    float3 ray = w - _OkuFogEye.xyz;
                    w = _OkuFogEye.xyz + ray * ((_OkuSeaLevel - _OkuFogEye.y) / ray.y);
                }
                float2 uv = float2((w.x - _OkuFogRect.x) / _OkuFogRect.z, 1 + (w.z - _OkuFogRect.y) / _OkuFogRect.w);
                float f = tex2Dlod(_OkuFogTex, float4(saturate(uv), 0, 0)).r;
                float light = f < 0.5 ? f * 2 * _OkuSeenBefore : lerp(_OkuSeenBefore, 1, f * 2 - 1);
                float2 out2 = max(max(-uv, uv - 1), 0) * _OkuFogRect.zw;
                light = lerp(light, 1, saturate((max(out2.x, out2.y) - _OkuFogReach) / 16));
                if (light >= 0.999) return c;
                c.rgb = SRGBToLinear(LinearToSRGB(saturate(c.rgb)) * light);
                return c;
            }
            ENDHLSL
        }
    }
}
