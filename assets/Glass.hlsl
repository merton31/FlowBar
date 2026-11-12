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
};

float4 Fullscreen(uint id : SV_VertexID) : SV_Position
{
    float2 p = float2((id << 1) & 2, id & 2);
    return float4(p * float2(2, -2) + float2(-1, 1), 0, 1);
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
    float2 direction = float2(p.x < 0 ? -directionAtScreenEdge.x : directionAtScreenEdge.z,
                              p.y < 0 ? -directionAtScreenEdge.y : directionAtScreenEdge.w);
    float2 samplePosition = position.xy + offset + direction * normal * shift * edge * edge * lerp(1, edge, ios);
    float2 uv = samplePosition / sourceSize;
    float3 color = desktop.SampleLevel(linearClamp, uv, 0).rgb;
    if (blur > 0)
    {
        float2 step = blur / sourceSize;
        color = (color * 2
            + desktop.SampleLevel(linearClamp, uv + float2(step.x, 0), 0).rgb
            + desktop.SampleLevel(linearClamp, uv - float2(step.x, 0), 0).rgb
            + desktop.SampleLevel(linearClamp, uv + float2(0, step.y), 0).rgb
            + desktop.SampleLevel(linearClamp, uv - float2(0, step.y), 0).rgb) / 6;
    }
    return float4(color * alpha, alpha);
}

[numthreads(16, 16, 1)]
void CheckChanges(uint3 pixel : SV_DispatchThreadID, uint thread : SV_GroupIndex)
{
    if (thread == 0) tileChanged = 0;
    GroupMemoryBarrierWithGroupSync();

    uint width, height;
    desktop.GetDimensions(width, height);
    if (pixel.x < width && pixel.y < height &&
        any(desktop.Load(int3(pixel.xy, 0)).rgb != lastDrawn.Load(int3(pixel.xy, 0)).rgb))
        InterlockedOr(tileChanged, 1);

    GroupMemoryBarrierWithGroupSync();
    if (thread == 0 && tileChanged != 0) InterlockedOr(changed[0], 1);
}
