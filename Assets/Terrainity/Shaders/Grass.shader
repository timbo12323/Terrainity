Shader "Terrainity/Grass"
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
        _Cull("Cull", Float) = 2
        _Smoothness("Smoothness", Range(0,1)) = 0.15
        _BladeEdgeSoftness("Blade edge softness", Range(0,.25)) = 0
        [HideInInspector] _RibbonEdges("Ribbon edges", Float) = 1
        [Toggle] _GrassWindEnabled("Wind Zones", Float) = 1
        _GrassWindWave("Wind waves (size, travel, breakup, variation)", Vector) = (.6,.45,.65,.25)
        _GrassWindResponse("Wind response (sway, flutter)", Vector) = (1,1,0,0)
        [HideInInspector] _GrassPreview("Private preview wind", Float) = 0
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
        [HideInInspector] _PreviewWindDirection("Preview wind direction", Vector) = (0,0,0,0)
        [HideInInspector] _PreviewWindMotion("Preview wind motion", Vector) = (0,0,0,0)
        [HideInInspector] _PreviewWindWeights("Preview wind weights", Vector) = (0,0,0,0)
        [HideInInspector] _PreviewWindTime("Preview wind time", Float) = 0
        [HideInInspector] _PreviewGrassWindWave("Preview grass waves", Vector) = (.6,.45,.65,.25)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]
            ZWrite On
            AlphaToMask [_AlphaToMask]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #define TERRAINITY_GRASS_MATERIAL
            #include "TreeLighting.hlsl"
            #include "GrassSurface.hlsl"
            #include "GrassWind.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float2 tint:TEXCOORD1; float3 canopy:TEXCOORD2; float4 wind:TEXCOORD3; float4 blade:TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float2 tint:TEXCOORD3; half3 canopyWS:TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o);
                float3 rootWS = TransformObjectToWorld(float3(v.blade.x,0,v.blade.y));
                float4 blade = float4(rootWS.xz,v.blade.zw);
                float4 weights = v.wind;
                weights.xy *= length(TransformObjectToWorldDir(float3(0,1,0), false));
                float3 positionWS = TerrainityGrassSceneWindPosition(TransformObjectToWorld(v.positionOS.xyz),weights,blade,rootWS);
                VertexPositionInputs p = GetVertexPositionInputs(TransformWorldToObject(positionWS));
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.canopyWS = dot(v.canopy, v.canopy) > .00001 ? TransformObjectToWorldNormal(v.canopy) : float3(0,0,0);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap); o.tint = v.tint;
                return o;
            }
            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float coverage = 1;
                if (_AlphaClip > .5 && _AlphaToMask > .5 && _AlphaToMaskAvailable > .5)
                    coverage = TerrainityCoverage(tex.a, _Cutoff, _Feathering);
                else TerrainityClip(tex.a, _Cutoff, _Feathering, _AlphaClip, i.positionCS.xy);
                tex.a = coverage * TerrainityGrassEdgeCoverage(i.uv, _BladeEdgeSoftness, _RibbonEdges,
                    _AlphaToMask > .5 && _AlphaToMaskAvailable > .5, i.positionCS.xy);
                half4 color = TerrainityLitColor(tex, i.tint, i.positionWS, i.positionCS,
                    i.normalWS, i.canopyWS, IS_FRONT_VFACE(face, 1, -1), half3(1,1,1), false);
                return color;
            }
            ENDHLSL
        }
    }
    // Built-in rendering fallback.
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        CGPROGRAM
        #include "Assets/Terrainity/Shaders/TreeSurface.hlsl"
        #include "GrassSurface.hlsl"
        #include "GrassWind.hlsl"
        #pragma surface Surf TerrainityLeaf vertex:Vert addshadow
        #pragma multi_compile_instancing
        #pragma target 3.5
        #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
        #pragma shader_feature_local _GLOSSYREFLECTIONS_OFF
        sampler2D _BaseMap, _TintRamp;
        fixed4 _BaseColor;
        half _Cutoff, _AlphaClip, _Smoothness, _Feathering, _OcclusionStrength, _Transmission, _CanopySoftness, _AlphaToMask;
        half _BladeEdgeSoftness, _RibbonEdges;
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
        struct Input { float2 uv_BaseMap; float2 tint; float4 screenPos; float facing:VFACE;  };
        struct GrassVertex
        {
            float4 vertex:POSITION; float4 tangent:TANGENT; float3 normal:NORMAL;
            float4 texcoord:TEXCOORD0; float4 texcoord1:TEXCOORD1; float4 texcoord2:TEXCOORD2;
            float4 texcoord3:TEXCOORD3; float4 texcoord4:TEXCOORD4; fixed4 color:COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        void Vert(inout GrassVertex v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o); o.tint = v.texcoord1.xy;
            float3 rootWS = mul(unity_ObjectToWorld,float4(v.texcoord4.x,0,v.texcoord4.y,1)).xyz;
            float3 positionWS = mul(unity_ObjectToWorld,v.vertex).xyz;
            float4 weights = v.texcoord3; weights.xy *= length(mul((float3x3)unity_ObjectToWorld,float3(0,1,0)));
            v.vertex = mul(unity_WorldToObject,float4(TerrainityGrassSceneWindPosition(positionWS,weights,float4(rootWS.xz,v.texcoord4.zw),rootWS),1));
        }
        void Surf(Input i, inout SurfaceOutputStandard o)
        {
            fixed4 tex = tex2D(_BaseMap, i.uv_BaseMap);
            TerrainityClip(tex.a, _Cutoff, _Feathering, _AlphaClip, i.screenPos.xy / i.screenPos.w * _ScreenParams.xy);
            TerrainityGrassEdgeCoverage(i.uv_BaseMap,_BladeEdgeSoftness,_RibbonEdges,false,i.screenPos.xy / i.screenPos.w * _ScreenParams.xy);
            o.Albedo = tex.rgb * _BaseColor.rgb * tex2D(_TintRamp, float2(saturate(i.tint.x),0.5)).rgb;
            o.Normal = float3(0,0,i.facing >= 0 ? 1 : -1);
            o.Smoothness = _Smoothness; o.Alpha = 1; o.Occlusion = TerrainityOcclusion(i.tint.y, _OcclusionStrength);
        }
        ENDCG
    }
    Fallback Off
}
