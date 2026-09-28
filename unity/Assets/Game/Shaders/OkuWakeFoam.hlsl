// OkuWakeFoam.hlsl - how much foam a wake vertex's uvs ask for, and how
// much the churned water behind a hull is lightened by the air in it. For
// the ribbon uv is (across -1 to 1, along in units from the stern,
// strength, age) and size (half its width here, the hull's half beam), both
// in units; for the hull's collar uv is (-1 to 1 across, -1 to 1 stern to
// bow, speed, -1).
#ifndef OKU_WAKE_FOAM_INCLUDED
#define OKU_WAKE_FOAM_INCLUDED

float OkuWakeFoam(float4 uv, float2 size, float2 xz, out float churned)
{
    float lace = OkuFoamLace(xz, 0.6);
    float amount;
    if (uv.w < 0)
    {
        // A thin collar just outside the hull, heaped up at the bow as
        // the ship gets under way.
        float r = length(uv.xy);
        float rim = smoothstep(0.66, 0.8, r) * (1 - smoothstep(0.82, 1.0, r));
        float bow = saturate(uv.y);
        amount = rim * (0.3 + uv.z * (0.25 + 0.55 * bow * bow));
        churned = rim * uv.z * 0.3;
    }
    else
    {
        // Thin lines along the arms of the Kelvin wedge, and the churned
        // strip the hull leaves, narrower than the hull, spreading and
        // fading as it ages.
        float across = abs(uv.x) * size.x;
        float arm = 1 - smoothstep(0.0, 0.3, abs(size.x - 0.25 - across));
        float spread = size.y * 0.75 * (1 + uv.w * 0.5);
        float churn = (1 - smoothstep(0.0, spread, across)) * saturate(1 - uv.w / 3.5);
        float start = smoothstep(-0.3, 0.4, uv.y);
        amount = (arm * 0.6 + churn * 1.2) * uv.z * start;
        churned = (churn * 0.5 + arm * 0.15) * uv.z * start;
    }
    return OkuFoamCover(lace, saturate(amount)) * 0.9;
}

#endif
