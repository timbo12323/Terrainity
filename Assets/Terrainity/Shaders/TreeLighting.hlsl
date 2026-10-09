#ifndef TERRAINITY_TREE_LIGHTING_INCLUDED
#define TERRAINITY_TREE_LIGHTING_INCLUDED

#include "TreeTransmission.hlsl"
#include "TreeSurface.hlsl"

// Preview and exported trees use the same material layout and foliage lighting.
TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_TintRamp); SAMPLER(sampler_TintRamp);
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Cutoff, _AlphaClip, _Cull, _Smoothness, _Feathering, _OcclusionStrength, _Transmission, _CanopySoftness, _AlphaToMask;
    #if defined(TERRAINITY_GRASS_MATERIAL)
        half _BladeEdgeSoftness, _RibbonEdges;
    #endif
CBUFFER_END

half4 TerrainityLitColor(half4 tex, float2 tintCoordinate, float3 positionWS, float4 positionCS,
    half3 normalWS, half3 canopyWS, half faceSign, half3 instanceTint, bool receiveShadows)
{
    half3 tint = SAMPLE_TEXTURE2D(_TintRamp, sampler_TintRamp, float2(saturate(tintCoordinate.x), .5)).rgb;
    SurfaceData surface = (SurfaceData)0;
    surface.albedo = tex.rgb * _BaseColor.rgb * tint * instanceTint;
    surface.smoothness = _Smoothness;
    surface.occlusion = TerrainityOcclusion(tintCoordinate.y, _OcclusionStrength);
    surface.alpha = 1;
    surface.normalTS = half3(0,0,1);
    InputData input = (InputData)0;
    input.positionWS = positionWS;
    input.positionCS = positionCS;
    input.normalWS = TerrainityCanopyNormal(normalize(normalWS) * faceSign, canopyWS, _CanopySoftness * _AlphaClip);
    input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    input.bakedGI = SampleSH(input.normalWS);
    input.shadowCoord = receiveShadows ? TransformWorldToShadowCoord(positionWS) : float4(0,0,0,0);
    input.shadowMask = half4(1,1,1,1);
    input.vertexLighting = VertexLighting(positionWS, input.normalWS);
    input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    half4 color = UniversalFragmentPBR(input, surface);
    color.rgb += TerrainityTransmission(input, surface.albedo, _Transmission * _AlphaClip);
    color.a = tex.a;
    return color;
}
#endif
