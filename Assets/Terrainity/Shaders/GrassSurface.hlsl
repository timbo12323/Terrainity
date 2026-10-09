#ifndef TERRAINITY_GRASS_SURFACE_INCLUDED
#define TERRAINITY_GRASS_SURFACE_INCLUDED

float TerrainityGrassEdgeCoverage(float2 uv, float softness, float ribbon, bool multisampled, float2 pixel)
{
    if (ribbon < .5) return 1;
    float width = max(softness, multisampled ? fwidth(uv.x) : 0);
    if (width <= .0001) return 1;
    float alpha = saturate(min(uv.x, 1 - uv.x) / width);
    if (multisampled) return alpha;
    TerrainityClip(alpha, .5, .5, 1, pixel);
    return 1;
}

#endif
