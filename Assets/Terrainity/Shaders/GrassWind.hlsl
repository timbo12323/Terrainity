#ifndef TERRAINITY_GRASS_WIND_INCLUDED
#define TERRAINITY_GRASS_WIND_INCLUDED
#include "TreeWind.hlsl"

float4 _PreviewWindDirection;
float4 _PreviewWindMotion;
float4 _PreviewWindWeights;
float4 _PreviewGrassWindWave;
float _PreviewWindTime;
float _GrassPreview;
float _GrassWindEnabled;
float4 _GrassWindWave;
float4 _GrassWindResponse;
float _TerrainityWindTime;

float TerrainityGrassNoiseHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float TerrainityGrassNoise(float2 p)
{
    float2 cell = floor(p), f = frac(p);
    float2 blend = f * f * (3 - 2 * f);
    return lerp(lerp(TerrainityGrassNoiseHash(cell), TerrainityGrassNoiseHash(cell + float2(1,0)), blend.x),
        lerp(TerrainityGrassNoiseHash(cell + float2(0,1)), TerrainityGrassNoiseHash(cell + float2(1,1)), blend.x), blend.y);
}

// Metadata is constant across a blade: world-space root X/Z, blade height and seeded response.
// Weights contain height-scaled bend/flutter, seeded phase, and normalized root-to-tip distance.
float3 TerrainityGrassWindPosition(float3 positionWS, float4 weights, float4 blade,
    float4 windDirection, float4 motion, float4 multipliers, float4 wave, float time)
{
    if (windDirection.w <= .0001 || weights.x <= .0001) return positionWS;
    float2 direction = windDirection.xz;
    float2 across = float2(-direction.y, direction.x);
    float wavelength = max(.05, wave.x);
    float travel = wave.y * (.35 + motion.z * 3);
    float2 samplePosition = (blade.xy - direction * time * travel) / wavelength;
    float breakup = wave.z;
    float coarse = TerrainityGrassNoise(samplePosition * .7);
    // The travelling bands remain coherent across neighbours; smooth noise bends and breaks their fronts.
    float phase = dot(samplePosition, direction) * 6.2831853 + (coarse - .5) * breakup * 5;
    float band = .5 + .5 * sin(phase);
    band = band * band * (3 - 2 * band);
    float gust = lerp(band, band * (.45 + .55 * TerrainityGrassNoise(samplePosition * 1.9 + 17.3)), breakup);
    float response = 1 + (blade.w * 2 - 1) * wave.w;
    float pressure = windDirection.w * (.2 + .8 * gust) + motion.y * .45 * gust;
    float bend = min(.7, pressure * .18 * multipliers.x * response);
    float turbulence = (TerrainityGrassNoise(samplePosition * 3.1 + across * time * .65 + weights.z * 2) - .5)
        * clamp(motion.x,0,5) * .045 * clamp(multipliers.x,0,2);
    float flutter = sin(time * (8.7 + motion.z * 3) + weights.z * 6.2831853 + phase)
        * clamp(motion.x,0,5) * .025 * clamp(multipliers.y,0,2);
    float2 offset = direction * (weights.x * bend + weights.y * flutter) + across * weights.x * turbulence;
    return positionWS + float3(offset.x, 0, offset.y);
}

float3 TerrainityGrassPreviewWindPosition(float3 positionWS, float4 weights, float4 blade)
{
    return TerrainityGrassWindPosition(positionWS,weights,blade,_PreviewWindDirection,
        _PreviewWindMotion,_PreviewWindWeights,_PreviewGrassWindWave,_PreviewWindTime);
}

float3 TerrainityGrassSceneWindPosition(float3 positionWS, float4 weights, float4 blade, float3 rootWS)
{
    if (_GrassPreview > .5) return TerrainityGrassPreviewWindPosition(positionWS,weights,blade);
    if (_GrassWindEnabled < .5 || weights.x <= .0001) return positionWS;
    float3 force = _TerrainityWindDirection.xyz * _TerrainityWindDirection.w;
    float4 motion = _TerrainityWindMotion;
    [unroll] for (int i = 0; i < 4; i++)
    {
        if (i >= (int)_TerrainityWindSphereCount) break;
        float3 radial = rootWS - _TerrainityWindSphere[i].xyz;
        float distance = length(radial);
        float falloff = saturate(1 - distance / max(.01,_TerrainityWindSphere[i].w));
        force += radial / max(distance,.001) * _TerrainityWindSphereMotion[i].x * falloff;
        motion.xy += _TerrainityWindSphereMotion[i].yz * falloff;
        if (falloff > 0) motion.z = max(motion.z,_TerrainityWindSphereMotion[i].w);
    }
    float strength = length(force.xz);
    if (strength <= .0001) return positionWS;
    float2 direction = force.xz / strength;
    return TerrainityGrassWindPosition(positionWS,weights,blade,float4(direction.x,0,direction.y,strength),
        motion,_GrassWindResponse,_GrassWindWave,_TerrainityWindTime);
}

#endif
