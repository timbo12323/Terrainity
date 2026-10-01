#ifndef TERRAINITY_TREE_TRANSMISSION_INCLUDED
#define TERRAINITY_TREE_TRANSMISSION_INCLUDED

half3 TerrainityTransmitLight(Light light, half3 normal, half3 viewDirection)
{
    half back = saturate(-dot(normal, light.direction));
    half forwardScatter = pow(saturate(dot(-light.direction, viewDirection)), 4);
    return light.color * light.distanceAttenuation * light.shadowAttenuation
        * back * lerp(.5h, 1.5h, forwardScatter);
}

half3 TerrainityTransmission(InputData inputData, half3 albedo, half amount)
{
    if (amount <= 0) return 0;
    half4 shadowMask = inputData.shadowMask;
    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, shadowMask);
    half3 transmitted = TerrainityTransmitLight(mainLight, inputData.normalWS, inputData.viewDirectionWS);
    #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
        #if USE_CLUSTER_LIGHT_LOOP
        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
            transmitted += TerrainityTransmitLight(light, inputData.normalWS, inputData.viewDirectionWS);
        }
        #endif
        uint pixelLightCount = GetAdditionalLightsCount();
        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
            transmitted += TerrainityTransmitLight(light, inputData.normalWS, inputData.viewDirectionWS);
        LIGHT_LOOP_END
    #endif
    return albedo * transmitted * saturate(amount);
}
#endif
