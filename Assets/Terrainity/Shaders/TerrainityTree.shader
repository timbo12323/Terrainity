Shader "Terrainity/Tree"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _TintRamp("Gradient", 2D) = "white" {}
        _Cutoff("Cutoff", Range(0,1)) = 0.5
        _Feathering("Cutoff feathering", Range(0,.5)) = 0
        _CanopySoftness("Canopy shading softness", Range(0,1)) = 0
        [Toggle] _AlphaToMask("Alpha-to-coverage (MSAA)", Float) = 0
        _OcclusionStrength("Ambient occlusion", Range(0,1)) = 0
        _AlphaClip("Alpha clip", Float) = 0
        _Transmission("Light transmission", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        _Smoothness("Smoothness", Range(0,1)) = 0.15
        [PerRendererData] _TreeInstanceColor("Terrain tree color", Color) = (1,1,1,1)
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "TreeLighting.hlsl"
        #include "TreeWind.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        UNITY_INSTANCING_BUFFER_START(TreeInstances)
            UNITY_DEFINE_INSTANCED_PROP(float4, _TreeInstanceColor)
        UNITY_INSTANCING_BUFFER_END(TreeInstances)
        struct Attributes
        {
            float4 positionOS:POSITION; float3 normalOS:NORMAL;
            float2 uv:TEXCOORD0; float2 tint:TEXCOORD1; float3 canopy:TEXCOORD2; float4 wind:TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0;
            half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float2 tint:TEXCOORD3; half3 canopyWS:TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 worldPosition = TransformObjectToWorld(v.positionOS.xyz);
            float3 deformedPosition = TransformWorldToObject(worldPosition + TerrainityWindOffset(worldPosition, v.wind));
            VertexPositionInputs p = GetVertexPositionInputs(deformedPosition);
            o.positionCS = p.positionCS; o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.canopyWS = dot(v.canopy, v.canopy) > .00001 ? TransformObjectToWorldNormal(v.canopy) : float3(0,0,0);
            o.uv = TRANSFORM_TEX(v.uv, _BaseMap); o.tint = v.tint;
            return o;
        }
        half4 SampleTree(Varyings i, bool coveragePass = false)
        {
            UNITY_SETUP_INSTANCE_ID(i);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
            if (coveragePass && _AlphaClip > .5 && _AlphaToMask > .5 && _AlphaToMaskAvailable > .5)
                tex.a = TerrainityCoverage(tex.a, _Cutoff, _Feathering);
            else { TerrainityClip(tex.a, _Cutoff, _Feathering, _AlphaClip, i.positionCS.xy); tex.a = 1; }
            return tex;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            AlphaToMask [_AlphaToMask]
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 tex = SampleTree(i, true);
                half4 color = TerrainityLitColor(tex, i.tint, i.positionWS, i.positionCS,
                    i.normalWS, i.canopyWS, IS_FRONT_VFACE(face, 1, -1),
                    UNITY_ACCESS_INSTANCED_PROP(TreeInstances, _TreeInstanceColor).rgb, true);
                color.rgb = MixFog(color.rgb, ComputeFogFactor(TransformWorldToHClip(i.positionWS).z));
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            Varyings ShadowVert(Attributes v)
            {
                Varyings o = Vert(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 direction = normalize(_LightPosition - o.positionWS);
                #else
                float3 direction = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, direction));
                o.positionCS = ApplyShadowClamping(o.positionCS);
                return o;
            }
            half4 DepthFrag(Varyings i):SV_Target { SampleTree(i); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            AlphaToMask [_AlphaToMask]
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            half4 DepthFrag(Varyings i):SV_Target { half4 tex = SampleTree(i, true); return half4(i.positionCS.zzz, tex.a); }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            AlphaToMask [_AlphaToMask]
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFrag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC):SV_Target
            {
                half4 tex = SampleTree(i, true);
                half3 normal = TerrainityCanopyNormal(normalize(i.normalWS) * IS_FRONT_VFACE(face, 1, -1), i.canopyWS, _CanopySoftness * _AlphaClip);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normal);
                    return half4(PackFloat2To888(saturate(oct * .5 + .5)), tex.a);
                #else
                    return half4(normal, tex.a);
                #endif
            }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        CGPROGRAM
        #include "Assets/Terrainity/Shaders/TreeSurface.hlsl"
        #include "Assets/Terrainity/Shaders/TreeWind.hlsl"
        #pragma surface Surf TerrainityLeaf fullforwardshadows addshadow vertex:Vert
        #pragma target 3.5
        #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
        #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
        #pragma shader_feature_local _GLOSSYREFLECTIONS_OFF
        sampler2D _BaseMap, _TintRamp;
        fixed4 _BaseColor;
        half _Cutoff, _AlphaClip, _Smoothness, _Feathering, _OcclusionStrength, _Transmission, _CanopySoftness, _AlphaToMask;
        UNITY_INSTANCING_BUFFER_START(TreeInstances)
            UNITY_DEFINE_INSTANCED_PROP(float4, _TreeInstanceColor)
        UNITY_INSTANCING_BUFFER_END(TreeInstances)
        #include "UnityPBSLighting.cginc"
        half4 LightingTerrainityLeaf(SurfaceOutputStandard s, half3 viewDir, UnityGI gi)
        {
            half4 color = LightingStandard(s, viewDir, gi);
            half back = saturate(-dot(s.Normal, gi.light.dir));
            half forwardScatter = pow(saturate(dot(-gi.light.dir, viewDir)), 4);
            color.rgb += s.Albedo * gi.light.color * back * lerp(.5h, 1.5h, forwardScatter) * _Transmission * _AlphaClip;
            return color;
        }
        void LightingTerrainityLeaf_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandard_GI(s, data, gi);
        }
        struct Input { float2 uv_BaseMap; float2 tint; float facing:VFACE; float4 screenPos;  };
        void Vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            o.tint = v.texcoord1.xy;
            float3 worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
            v.vertex = mul(unity_WorldToObject, float4(worldPosition + TerrainityWindOffset(worldPosition, v.texcoord3), 1));
            float3 blended = lerp(v.normal, v.texcoord2.xyz, _CanopySoftness * _AlphaClip);
            if (dot(blended, blended) > .00001) v.normal = normalize(blended);
        }
        void Surf(Input i, inout SurfaceOutputStandard o)
        {
            #if defined(LOD_FADE_CROSSFADE)
                UnityApplyDitherCrossFade(i.screenPos.xy / i.screenPos.w * _ScreenParams.xy);
            #endif
            fixed4 tex = tex2D(_BaseMap, i.uv_BaseMap);
            TerrainityClip(tex.a, _Cutoff, _Feathering, _AlphaClip, i.screenPos.xy / i.screenPos.w * _ScreenParams.xy);
            o.Albedo = tex.rgb * _BaseColor.rgb * tex2D(_TintRamp, float2(saturate(i.tint.x), .5)).rgb
                * UNITY_ACCESS_INSTANCED_PROP(TreeInstances, _TreeInstanceColor).rgb;
            o.Normal = float3(0,0,i.facing >= 0 ? 1 : -1);
            o.Smoothness = _Smoothness; o.Alpha = 1; o.Occlusion = TerrainityOcclusion(i.tint.y, _OcclusionStrength);
        }
        ENDCG
    }
    Fallback Off
}

