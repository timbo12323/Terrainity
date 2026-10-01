Shader "Terrainity/Rock Triplanar"
{
    Properties
    {
        [MainTexture] _BaseMap("Base texture", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal map", 2D) = "bump" {}
        _BumpScale("Normal strength", Range(0,3)) = 1
        _Smoothness("Smoothness", Range(0,1)) = .15
        _BlendSharpness("Projection blend", Range(1,16)) = 4
        [ToggleOff] _SpecularHighlights("Specular highlights", Float) = 1
        [ToggleOff] _EnvironmentReflections("Environment reflections", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Cutoff("Cutoff", Float) = .5
        [HideInInspector] _AlphaClip("Alpha clip", Float) = 0
        [HideInInspector] _MainTex("Legacy base texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale, _Smoothness, _BlendSharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                half3 normalOS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 3);
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs position = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = position.positionCS;
                o.positionWS = position.positionWS;
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                OUTPUT_LIGHTMAP_UV(v.lightmapUV, unity_LightmapST, o.lightmapUV);
                OUTPUT_SH(TransformObjectToWorldNormal(v.normalOS), o.vertexSH);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
                #endif
                half3 n = normalize(i.normalOS);
                half3 weights = pow(abs(n), _BlendSharpness);
                weights /= max(weights.x + weights.y + weights.z, .00001h);
                float3 directions = sign(n);
                float2 uvX = float2(-i.positionOS.z * directions.x, i.positionOS.y) * _BaseMap_ST.xy + _BaseMap_ST.zw;
                float2 uvY = float2(i.positionOS.x, -i.positionOS.z * directions.y) * _BaseMap_ST.xy + _BaseMap_ST.zw;
                float2 uvZ = float2(i.positionOS.x * directions.z, i.positionOS.y) * _BaseMap_ST.xy + _BaseMap_ST.zw;
                half4 baseX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX);
                half4 baseY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY);
                half4 baseZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ);
                half3 albedo = (baseX.rgb * weights.x + baseY.rgb * weights.y + baseZ.rgb * weights.z) * _BaseColor.rgb;
                #if defined(_NORMALMAP)
                half3 bumpX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _BumpScale);
                half3 bumpY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _BumpScale);
                half3 bumpZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _BumpScale);
                half3 normalX = n * bumpX.z + half3(0,0,-directions.x) * bumpX.x + half3(0,1,0) * bumpX.y;
                half3 normalY = n * bumpY.z + half3(1,0,0) * bumpY.x + half3(0,0,-directions.y) * bumpY.y;
                half3 normalZ = n * bumpZ.z + half3(directions.z,0,0) * bumpZ.x + half3(0,1,0) * bumpZ.y;
                n = normalize(normalX * weights.x + normalY * weights.y + normalZ * weights.z);
                #endif
                half3 normalWS = normalize(TransformObjectToWorldNormal(n));
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.metallic = 0;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.normalTS = half3(0,0,1);
                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = normalWS;
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.bakedGI = SAMPLE_GI(i.lightmapUV, i.vertexSH, normalWS);
                input.vertexLighting = VertexLighting(i.positionWS, normalWS);
                input.shadowMask = half4(1,1,1,1);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.fogCoord = ComputeFogFactor(i.positionCS.z);
                half4 color = UniversalFragmentPBR(input, surface);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        CGPROGRAM
        #pragma surface Surf Standard fullforwardshadows vertex:Vert
        #pragma target 3.5
        sampler2D _BaseMap;
        fixed4 _BaseColor;
        half _Smoothness, _BlendSharpness;
        float4 _BaseMap_ST;
        struct Input { float3 positionOS; float3 normalOS; };
        void Vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.positionOS = v.vertex.xyz;
            o.normalOS = v.normal;
        }
        void Surf(Input i, inout SurfaceOutputStandard o)
        {
            half3 n = normalize(i.normalOS);
            half3 weights = pow(abs(n), _BlendSharpness);
            weights /= max(weights.x + weights.y + weights.z, .00001h);
            float3 directions = sign(n);
            float2 uvX = float2(-i.positionOS.z * directions.x, i.positionOS.y) * _BaseMap_ST.xy + _BaseMap_ST.zw;
            float2 uvY = float2(i.positionOS.x, -i.positionOS.z * directions.y) * _BaseMap_ST.xy + _BaseMap_ST.zw;
            float2 uvZ = float2(i.positionOS.x * directions.z, i.positionOS.y) * _BaseMap_ST.xy + _BaseMap_ST.zw;
            o.Albedo = (tex2D(_BaseMap, uvX).rgb * weights.x + tex2D(_BaseMap, uvY).rgb * weights.y + tex2D(_BaseMap, uvZ).rgb * weights.z) * _BaseColor.rgb;
            o.Smoothness = _Smoothness;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback Off
}
