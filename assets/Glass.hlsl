Texture2D<float4> desktop : register(t0);
Texture2D<float4> lastDrawn : register(t1);
SamplerState linearClamp : register(s0);
RWBuffer<uint> changed : register(u0);
groupshared uint tileChanged;

cbuffer Lens : register(b0)
{
    float2 size;
    float2 offset;
    float2 sourceSize;
    float radius;
    float band;
    float shift;
    float blur;
    float ios;
    float opacity;
    float4 directionAtScreenEdge;
    float liveRim;
};

float4 Fullscreen(uint id : SV_VertexID) : SV_Position
{
    float2 p = float2((id << 1) & 2, id & 2);
    return float4(p * float2(2, -2) + float2(-1, 1), 0, 1);
}

float3 BlurSample(float2 sampleAt, float2 low, float2 high)
{
    float3 color = desktop.SampleLevel(linearClamp, clamp(sampleAt, low, high) / sourceSize, 0).rgb;
    if (blur > 0)
    {
        float2 x = float2(blur, 0), y = float2(0, blur);
        color = (color * 2
            + desktop.SampleLevel(linearClamp, clamp(sampleAt + x, low, high) / sourceSize, 0).rgb
            + desktop.SampleLevel(linearClamp, clamp(sampleAt - x, low, high) / sourceSize, 0).rgb
            + desktop.SampleLevel(linearClamp, clamp(sampleAt + y, low, high) / sourceSize, 0).rgb
            + desktop.SampleLevel(linearClamp, clamp(sampleAt - y, low, high) / sourceSize, 0).rgb) / 6;
    }
    return color;
}

float4 Refract(float4 position : SV_Position) : SV_Target
{
    float2 p = position.xy - size * 0.5;
    float2 q = abs(p) - size * 0.5 + radius;
    float2 outer = max(q, 0);
    float distance = length(outer) + min(max(q.x, q.y), 0) - radius;
    float alpha = saturate(0.5 - distance) * opacity;
    float edge = saturate(1 + distance / band);
    float2 normal = any(outer) ? normalize(outer) : (q.x >= q.y ? float2(1, 0) : float2(0, 1));
    float displacement = shift * edge * edge * lerp(1, edge, ios);
    if (liveRim != 0)
    {
        if (alpha == 0 || edge == 0) return 0;
        float2 side = float2(p.x < 0 ? -1 : 1, p.y < 0 ? -1 : 1);
        float2 boundary = offset + float2(p.x < 0 ? -0.5 : size.x + 0.5,
                                          p.y < 0 ? -0.5 : size.y + 0.5);
        float2 low = 0.5, high = sourceSize - 0.5;
        float2 weights = normal * float2(boundary.x >= low.x && boundary.x <= high.x ? 1 : 0,
                                          boundary.y >= low.y && boundary.y <= high.y ? 1 : 0);
        float weight = weights.x + weights.y;
        if (weight == 0) return 0;
        float2 sampleAt = position.xy + offset + side * normal * displacement;
        float3 color = 0;
        if (weights.x > 0)
            color += weights.x * BlurSample(float2(boundary.x + side.x * displacement, sampleAt.y),
                float2(side.x < 0 ? low.x : boundary.x, low.y),
                float2(side.x < 0 ? boundary.x : high.x, high.y));
        if (weights.y > 0)
            color += weights.y * BlurSample(float2(sampleAt.x, boundary.y + side.y * displacement),
                float2(low.x, side.y < 0 ? low.y : boundary.y),
                float2(high.x, side.y < 0 ? boundary.y : high.y));
        alpha *= 0.55 * edge * saturate(shift / 4) * max(weights.x, weights.y);
        return float4(color * alpha / weight, alpha);
    }
    float2 direction = float2(p.x < 0 ? -directionAtScreenEdge.x : directionAtScreenEdge.z,
                              p.y < 0 ? -directionAtScreenEdge.y : directionAtScreenEdge.w);
    float2 samplePosition = position.xy + offset + direction * normal * displacement;
    float3 color = BlurSample(samplePosition, 0.5, sourceSize - 0.5);
    return float4(color * alpha, alpha);
}

[numthreads(16, 16, 1)]
void CheckChanges(uint3 pixel : SV_DispatchThreadID, uint thread : SV_GroupIndex)
{
    if (thread == 0) tileChanged = 0;
    GroupMemoryBarrierWithGroupSync();

    uint width, height;
    desktop.GetDimensions(width, height);
    float2 local = float2(pixel.xy) - offset;
    bool outside = any(local < 0) || any(local >= size);
    if (pixel.x < width && pixel.y < height && (liveRim == 0 || outside) &&
        any(desktop.Load(int3(pixel.xy, 0)).rgb != lastDrawn.Load(int3(pixel.xy, 0)).rgb))
        InterlockedOr(tileChanged, 1);

    GroupMemoryBarrierWithGroupSync();
    if (thread == 0 && tileChanged != 0) InterlockedOr(changed[0], 1);
}
