#ifndef TERRAINITY_PREVIEW_WIND_INCLUDED
#define TERRAINITY_PREVIEW_WIND_INCLUDED
#include "../Shaders/TreeWind.hlsl"
#include "../Shaders/GrassWind.hlsl"

float3 TerrainityPreviewWindPosition(float3 worldPosition, float4 weights)
{
    if (weights.x <= .0001) return worldPosition;
    weights.xy *= _PreviewWindWeights.xy;
    return worldPosition + TerrainityWindMotionOffset(worldPosition, weights,
        _PreviewWindDirection.xyz * _PreviewWindDirection.w,
        _PreviewWindMotion.x, _PreviewWindMotion.y, _PreviewWindMotion.z, _PreviewWindTime);
}

float3 TerrainityPreviewWindPosition(float3 worldPosition, float4 weights, float4 blade)
{
    if (_PreviewWindWeights.z > .5) return TerrainityGrassPreviewWindPosition(worldPosition, weights, blade);
    return TerrainityPreviewWindPosition(worldPosition, weights);
}
#endif
