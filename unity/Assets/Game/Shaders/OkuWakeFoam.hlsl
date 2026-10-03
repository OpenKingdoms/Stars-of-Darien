// OkuWakeFoam.hlsl - how much foam a wake splat asks for, and how churned
// the water is, before the sea's own lace breaks it up. For the ribbon uv
// is (across -1 to 1, along in units from the stern, strength, age) and
// size (half its width here, the hull's half beam), both in units. For the
// hull's collar uv is (across, along) in units from the hull's middle,
// z how much foam its speed throws, w -1, and size its half beam and
// half length.
#ifndef OKU_WAKE_FOAM_INCLUDED
#define OKU_WAKE_FOAM_INCLUDED

float OkuWakeFoam(float4 uv, float2 size, out float churned)
{
    if (uv.w < 0)
    {
        // Round the hull at the waterline: heaped at the bow as the ship
        // gets under way, a little at the stern, fading a few tenths of a
        // unit out. d is the distance out from the hull's ellipse.
        float2 q = uv.xy / max(size, 0.05);
        float r = length(q);
        float2 grad = float2(uv.x / (size.x * size.x), uv.y / (size.y * size.y)) / max(r, 1e-3);
        float d = (r - 1) / max(length(grad), 1e-3);
        float bow = saturate(q.y), stern = saturate(-q.y);
        float reach = 0.2 + uv.z * (0.1 + 0.35 * bow * bow);
        float ring = smoothstep(-0.12, 0.02, d) * (1 - smoothstep(0.0, reach, d));
        float amount = ring * (0.3 + uv.z * (0.2 + 0.6 * bow * bow + 0.2 * stern));
        churned = ring * uv.z * 0.35;
        return saturate(amount);
    }
    // Lines along the arms of the Kelvin wedge, and the churned strip the
    // hull leaves, as wide as the hull, spreading and fading as it ages.
    float across = abs(uv.x) * size.x;
    float arm = 1 - smoothstep(0.0, 0.35, abs(size.x - 0.3 - across));
    // Never out to the ribbon's own edge, which would show straight.
    float spread = min(size.y * (1 + uv.w * 0.6), size.x * 0.8);
    float churn = (1 - smoothstep(spread * 0.3, spread, across)) * saturate(1 - uv.w / 3.5);
    float start = smoothstep(-0.3, 0.4, uv.y);
    // The arms' crests carry a little air too.
    churned = saturate(churn + arm * 0.25) * uv.z * start;
    // Foam along the churn's edges and the arms. The churn's bulk is left to
    // the churned channel, which the sea breaks into soft blobs.
    float rim = churn * (1 - churn) * 4;
    return saturate((arm * 0.55 + rim * 0.5 + churn * 0.25) * uv.z * start);
}

#endif
