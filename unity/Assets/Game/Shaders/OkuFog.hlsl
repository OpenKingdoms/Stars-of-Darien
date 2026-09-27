// OkuFog.hlsl - the fog of war as the engine reports it, one texel per
// height sample over the map: dark where never seen, dimmed where seen
// before, clear in sight. Shared by both pipelines' shaders, off unless
// the game sets _OkuFogOn.
#ifndef OKU_FOG_INCLUDED
#define OKU_FOG_INCLUDED

sampler2D _OkuFogTex;
float4 _OkuFogRect;     // x, z of the north-west corner, width, depth
float _OkuFogOn;

half OkuFogLight(float3 positionWS)
{
    if (_OkuFogOn < 0.5) return 1;
    float2 uv = float2((positionWS.x - _OkuFogRect.x) / _OkuFogRect.z, 1 + (positionWS.z - _OkuFogRect.y) / _OkuFogRect.w);
    // 0 never seen, 0.5 seen before, 1 in sight, filtered between texels.
    half f = tex2Dlod(_OkuFogTex, float4(saturate(uv), 0, 0)).r;
    return lerp(lerp(0.22, 0.6, saturate(f * 2)), 1, saturate(f * 2 - 1));
}

#endif
