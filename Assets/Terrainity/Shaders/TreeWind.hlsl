#ifndef TERRAINITY_TREE_WIND_INCLUDED
#define TERRAINITY_TREE_WIND_INCLUDED

float4 _TerrainityWindDirection;
float4 _TerrainityWindMotion;
float4 _TerrainityWindSphere[4];
float4 _TerrainityWindSphereMotion[4];
float _TerrainityWindSphereCount;

float3 TerrainityWindOffset(float3 worldPosition, float4 weights)
{
    if (weights.x <= .0001) return 0;
    // Broad sway stays coherent across each tree; individual card phase only affects flutter.
    float phase = worldPosition.x * .06 + worldPosition.z * .05;
    float3 force = _TerrainityWindDirection.xyz * _TerrainityWindDirection.w;
    float turbulence = _TerrainityWindMotion.x;
    float pulse = _TerrainityWindMotion.y;
    float frequency = _TerrainityWindMotion.z;
    [unroll] for (int i = 0; i < 4; i++)
    {
        if (i >= (int)_TerrainityWindSphereCount) break;
        float3 fromZone = worldPosition - _TerrainityWindSphere[i].xyz;
        float distance = length(fromZone);
        float falloff = saturate(1 - distance / _TerrainityWindSphere[i].w);
        force += fromZone / max(distance, .001) * (_TerrainityWindSphereMotion[i].x * falloff);
        turbulence += _TerrainityWindSphereMotion[i].y * falloff;
        pulse += _TerrainityWindSphereMotion[i].z * falloff;
        frequency = max(frequency, _TerrainityWindSphereMotion[i].w * falloff);
    }
    float strength = length(force);
    if (strength <= .0001) return 0;
    float3 direction = force / strength;
    float time = _Time.y;
    float slow = sin(time * (1.1 + frequency * 6) + phase);
    float gust = sin(time * (2.3 + frequency * 9) + phase * 1.7);
    float pressure = clamp(strength * (.55 + .45 * slow) + pulse * .25 * gust, -3, 3);
    float3 bend = direction * pressure * (.28 * weights.x);
    float flutter = weights.y * turbulence * .028
        * sin(time * 8.7 + phase * 2.1 + worldPosition.y * 1.7 + weights.z * 6.28318);
    return bend + (direction + float3(0, .35, 0)) * flutter;
}

#endif
