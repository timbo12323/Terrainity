#ifndef TERRAINITY_TREE_SURFACE_INCLUDED
#define TERRAINITY_TREE_SURFACE_INCLUDED

void TerrainityClip(float alpha, float cutoff, float feather, float enabled, float2 pixel)
{
    if (enabled < .5) return;
    if (feather <= .0001) { clip(alpha - cutoff); return; }
    // Keep fully transparent texels empty, including at the lowest cutoff.
    clip(alpha - .00001);
    float coverage = smoothstep(max(0, cutoff - feather * .5), min(1, cutoff + feather * .5), alpha);
    float threshold = frac(52.9829189 * frac(dot(floor(pixel), float2(.06711056, .00583715))));
    clip(coverage - max(.00001, threshold));
}

float TerrainityOcclusion(float amount, float strength)
{
    return saturate(1 - saturate(amount) * saturate(strength));
}

float3 TerrainityCanopyNormal(float3 card, float3 canopy, float softness)
{
    if (softness <= 0) return normalize(card);
    if (dot(canopy, canopy) < .00001) return normalize(card);
    float3 blended = lerp(normalize(card), normalize(canopy), saturate(softness));
    return dot(blended, blended) > .00001 ? normalize(blended) : normalize(canopy);
}

float TerrainityCoverage(float alpha, float cutoff, float feather)
{
    clip(alpha - .00001);
    float width = max(fwidth(alpha), max(feather, .0001));
    float coverage = saturate((alpha - cutoff) / width + .5);
    clip(coverage - .00001);
    return coverage;
}
#endif
