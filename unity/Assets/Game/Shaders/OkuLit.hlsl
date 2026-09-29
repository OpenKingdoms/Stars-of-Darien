// OkuLit.hlsl - the presentation's lighting under URP: the sun with its
// soft cascaded shadows, other lights, sky ambient from spherical
// harmonics, screen-space ambient occlusion and a faint sky rim.
#ifndef OKU_LIT_INCLUDED
#define OKU_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// shadowLift from 0 (the sun's shadow as cast) to 1 (none), and the
// shadow used, for callers that light more by the sun. ambientGain scales
// the sky's light, as steep ground takes more of it.
half3 OkuLight(half3 albedo, float3 positionWS, half3 normalWS, float4 positionCS, half gloss, half rim, half shadowLift, half ambientGain, out half shadow)
{
    float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);
    half ao = 1;
    half directAo = 1;
#if defined(_SCREEN_SPACE_OCCLUSION)
    AmbientOcclusionFactor aof = GetScreenSpaceAmbientOcclusion(screenUV);
    ao = aof.indirectAmbientOcclusion;
    directAo = aof.directAmbientOcclusion;
#endif
    half3 viewWS = SafeNormalize(GetWorldSpaceViewDir(positionWS));
    Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS));
    sun.shadowAttenuation = lerp(sun.shadowAttenuation, 1, shadowLift);
    shadow = sun.shadowAttenuation;
    half ndl = saturate(dot(normalWS, sun.direction));
    half3 lit = sun.color * (ndl * sun.shadowAttenuation * sun.distanceAttenuation * directAo);
    half3 h = SafeNormalize(sun.direction + viewWS);
    half3 spec = sun.color * pow(saturate(dot(normalWS, h)), lerp(8, 128, gloss)) * gloss * 0.5 * ndl * sun.shadowAttenuation;
#if defined(_ADDITIONAL_LIGHTS)
    uint count = GetAdditionalLightsCount();
    for (uint li = 0; li < count; li++)
    {
        Light l = GetAdditionalLight(li, positionWS);
        lit += l.color * saturate(dot(normalWS, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
    }
#endif
    half3 ambient = SampleSH(normalWS) * ao * ambientGain;
    half rimTerm = pow(1 - saturate(dot(normalWS, viewWS)), 3) * rim * 2;
    return albedo * (lit + ambient) + spec + albedo * ambient * rimTerm;
}

half3 OkuLight(half3 albedo, float3 positionWS, half3 normalWS, float4 positionCS, half gloss, half rim, half shadowLift, out half shadow)
{
    return OkuLight(albedo, positionWS, normalWS, positionCS, gloss, rim, shadowLift, 1, shadow);
}

half3 OkuLight(half3 albedo, float3 positionWS, half3 normalWS, float4 positionCS, half gloss, half rim)
{
    half shadow;
    return OkuLight(albedo, positionWS, normalWS, positionCS, gloss, rim, 0, 1, shadow);
}

#endif
