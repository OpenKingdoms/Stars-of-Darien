// OkuLit.hlsl - the presentation's lighting under URP: the sun with its
// soft cascaded shadows, other lights, sky ambient from spherical
// harmonics, screen-space ambient occlusion and a faint sky rim.
#ifndef OKU_LIT_INCLUDED
#define OKU_LIT_INCLUDED

// Under Forward+ each pixel is lit from the clustered list of every light in
// view, so a battle's blasts can light more than four at once. A shader that
// includes this declares _CLUSTER_LIGHT_LOOP itself, as a pragma in an
// include does not make variants.
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
    // The light loop reads its clusters from these two.
    InputData inputData = (InputData)0;
    inputData.normalizedScreenSpaceUV = screenUV;
    inputData.positionWS = positionWS;
    uint count = GetAdditionalLightsCount();
#if USE_CLUSTER_LIGHT_LOOP
    // Forward+ keeps directional lights besides the sun ahead of the clusters.
    for (uint di = 0; di < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); di++)
    {
        Light d = GetAdditionalLight(di, positionWS);
        lit += d.color * saturate(dot(normalWS, d.direction)) * d.distanceAttenuation * d.shadowAttenuation;
    }
#endif
    LIGHT_LOOP_BEGIN(count)
        Light l = GetAdditionalLight(lightIndex, positionWS);
        lit += l.color * saturate(dot(normalWS, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
    LIGHT_LOOP_END
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
