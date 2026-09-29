// OkuWaterShade.hlsl - the sea's colour at a point of its surface, the
// same in both pipelines. The includer defines OKU_SCENE_EYE(uv), the
// linear eye depth of what lies behind the water at a screen point,
// OKU_SCENE_COLOR(uv), its colour, and OKU_WATER_SHADOW(p, v, depth), how
// much sun the water under a point gets along the view ray.
#ifndef OKU_WATER_SHADE_INCLUDED
#define OKU_WATER_SHADE_INCLUDED

struct OkuSun
{
    float3 dir;
    float3 color;
    float shadow;
    float3 ambient;
};

// Where a vertex of the sea rides on the swell.
float3 OkuSeaVertex(float3 p)
{
    float3 sea = OkuSeaAt(p.xz);
    float2 slope;
    float crest;
    return p + OkuSwell(p.xz, OkuDampMesh(sea.x), 2, slope, crest);
}

float3 OkuWaterShade(float3 p, float3 cam, float2 suv, float surfEye, OkuSun sun)
{
    float3 toCam = cam - p;
    float dist = length(toCam);
    float3 v = toCam / dist;

    float3 sea = OkuSeaAt(p.xz);
    float depthBelow = sea.x, shore = sea.y, body = sea.z;
    float damp = OkuDamp(depthBelow);
    float2 wake = saturate(OkuWakeAt(p.xz));

    // The surface: the finer waves over a little of the swell, rougher
    // where the slow noise and the weather say, calmer in the shallows,
    // in wind slicks, far off and in the churn behind a hull, and rain
    // rings in the rain.
    OkuWave w = OkuWaveAt(p.xz, damp, dist);
    w.slope *= 1 - wake.y * 0.5;
    if (_OkuWaterWaves.w > 0.01) w.slope += OkuRain(p.xz, dist) * _OkuWaterWaves.w;
    float3 n = normalize(float3(-w.slope.x, 1, -w.slope.y));

    // What lies under the surface, bent by the waves where the water is
    // deep enough, never pulling in what stands above it.
    float sceneEye = OKU_SCENE_EYE(suv);
    float rayScale = dist / max(surfEye, 1e-3);
    float thick = max(sceneEye - surfEye, 0) * rayScale;
    float2 bend = n.xz * 0.06 * saturate(thick * 1.5) * saturate(12 / max(surfEye, 1));
    float refrEye = OKU_SCENE_EYE(suv + bend);
    if (refrEye < surfEye) { bend = 0; refrEye = sceneEye; }
    float path = max(refrEye - surfEye, 0) * rayScale;
    float under = path * v.y;
    float3 scene = OKU_SCENE_COLOR(suv + bend);
    // The water thins to nothing at its edge, and its sky and glints with it.
    float contact = smoothstep(0.0, 0.08, thick);

    // Light through the water: absorbed on the way down to the floor and
    // back up the view ray, and scattered back toward the eye, in the
    // shadow the water itself lies in, soft since it is taken through the
    // water's depth. The water's own colour deepens with the distance from
    // land as the true depth would, and varies a little in broad patches,
    // as wind and current leave a real sea.
    float shadow = lerp(1, OKU_WATER_SHADOW(p, v, max(under, 0.3)), 0.8);
    float3 lightIn = sun.color * shadow * saturate(sun.dir.y + 0.2) + sun.ambient;
    float far = smoothstep(2, 12, shore);
    float3 own = lerp(_OkuWaterScatter.rgb, _OkuWaterDeep.rgb, far);
    own *= lerp(float3(0.9, 0.97, 1.08), float3(1.1, 1.05, 0.92), w.tint) * lerp(0.92, 1.08, w.macro);
    float3 t = exp(-_OkuWaterSigma.rgb * (path + under));
    float3 inscatter = own * lightIn * (1 + w.crest * 0.3 * damp);
    inscatter += own * sun.color * shadow * saturate(w.crest) * 0.6 * damp;
    float3 water = scene * t + inscatter * (1 - t);
    // Air churned into the water behind a hull lightens it.
    water = lerp(water, (own * 5 + 0.03) * lightIn, wake.y * 0.45);

    // The sky off the surface, read off a steeper copy of the waves so it
    // moves on them as it does on a real sea seen from high up, a little
    // more of it in the glassy slicks.
    float3 nr = normalize(float3(-w.slope.x * 1.8, 1, -w.slope.y * 1.8));
    float3 r = reflect(-v, nr);
    r.y = abs(r.y);
    float fres = saturate(OkuFresnel(nr, v) * _OkuWaterSky.a * (1 + w.slick * 0.35)) * contact;
    float3 sky = OkuSky(r, sun.dir, sun.color);
    water = lerp(water, sky, fres);

    // The sun: points of light off a fine octave only the glint sees, over
    // a faint sheen along the sun's path. Fewer points in slicks and churn,
    // and broader, dimmer ones in rough weather.
    float2 sparkle = OkuSparkleSlope(p.xz) * (1 - w.slick * 0.5) * (1 - wake.y);
    float3 ng = normalize(float3(-(w.slope.x * 0.15 + sparkle.x), 1, -(w.slope.y * 0.15 + sparkle.y)));
    float sigma = 0.0045 + _OkuWaterWaves.y * 0.03 + dist * 0.00006;
    float sunLit = sun.shadow * _OkuWaterScatter.a * contact;
    float glint = OkuSparkle(ng, v, sun.dir, sigma) * 1.5;
    float sheen = OkuSheen(n, v, sun.dir) * 0.04;
    water += (sun.color * glint + (sky * 0.5 + sun.color * 0.25) * sheen) * sunLit;

    // Foam, as lace gathered into clumps. At the shore a band a fifth of a
    // cell to half a cell wide, thickest at the water's edge and thinning
    // to threads, broken along the shore by a slow surge and bands rolling
    // in, none round a pool. A faint broken ring round a post
    // in deeper water, whitecaps in a storm, and the wakes. The lace thins
    // out with distance, so a little more is asked for far off.
    float lace = OkuFroth(p.xz, OkuFoamLace(p.xz, 1));
    float farMore = lerp(1, 1.15, saturate(dist / 120));
    float surge = smoothstep(-0.45, 0.35, sin(tex2D(_OkuWaterNoise, p.xz / 23.0 + 0.61).r * 12.566 - _OkuWaterTime * 0.7));
    float width = lerp(0.15, 0.45, tex2D(_OkuWaterNoise, p.xz / 9.0 + 0.37).r);
    float core = smoothstep(-0.2, 0.0, shore) * (1 - smoothstep(0.0, width, shore));
    float edge = core * (0.35 + 0.5 * core) * surge;
    float wash = pow(saturate(sin(6.2831853 * shore / 1.4 - _OkuWaterTime * 1.2 + w.macro * 5)), 4) * exp(-max(shore, 0) / 0.6) * 0.4 * surge;
    float sized = smoothstep(2.0, 5.0, body);
    float post = (1 - smoothstep(0.0, 0.12, under)) * saturate((depthBelow - 0.4) / 0.4) * 0.3;
    float caps = saturate((w.crest - 0.25) * 4) * _OkuWaterWaves.z * lerp(0.6, 1.2, w.macro);
    // A sliver of water over a steep face takes none, or it would draw
    // the ground's facets.
    float foam = OkuFoamCover(lace, max(saturate(edge + wash) * sized, max(post, caps)) * farMore) * 0.85 * smoothstep(0.0, 0.05, thick);
    float wakeFoam = OkuFoamCover(lace, max(wake.x, wake.y * 0.7) * farMore) * 0.9 * smoothstep(0.0, 0.015, thick);
    foam = max(foam, wakeFoam);
    float3 foamLight = sun.color * (saturate(dot(n, sun.dir)) * shadow) + sun.ambient;
    water = lerp(water, foamLight * 0.85, foam * 0.92);

    // Past a land edge the sea melts into the haze as the land does. Past
    // a sea edge the sea runs on, hazing slowly, and toward the far end of
    // the water eases into the open sea's one colour, which the plain past
    // it carries on to the horizon.
    if (_OkuMapSize.x > 0)
    {
        float seaEdge;
        float cells = OkuPastEdge(p.xz, _OkuMapSize.xy, seaEdge);
        float3 land = lerp(water, _OkuHaze.rgb, smoothstep(0, 1, saturate(cells / 32)));
        float3 open = lerp(water, _OkuHaze.rgb, OkuSeaHaze(cells));
        open = lerp(open, OkuFarSea(cells, p, cam), smoothstep(0.3, 1, cells / max(_OkuSeaCell.y, 1)));
        water = lerp(land, open, seaEdge);
    }
    return water;
}

#endif
