Shader "Hidden/Terrainity/Preview Wireframe"
{
    Properties
    {
        [HideInInspector] _PreviewWindDirection("Preview wind direction", Vector) = (0,0,0,0)
        [HideInInspector] _PreviewWindMotion("Preview wind motion", Vector) = (0,0,0,0)
        [HideInInspector] _PreviewWindWeights("Preview wind weights", Vector) = (0,0,0,0)
        [HideInInspector] _PreviewWindTime("Preview wind time", Float) = 0
        [HideInInspector] _PreviewGrassWindWave("Preview grass waves", Vector) = (.6,.45,.65,.25)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "PreviewWind.hlsl"
            struct Attributes { float4 vertex : POSITION; float4 wind : TEXCOORD3; float4 blade : TEXCOORD4; };
            float4 vert(Attributes v) : SV_POSITION
            {
                float3 rootWS = mul(unity_ObjectToWorld,float4(v.blade.x,0,v.blade.y,1)).xyz;
                float3 positionWS = TerrainityPreviewWindPosition(mul(unity_ObjectToWorld, v.vertex).xyz, v.wind,float4(rootWS.xz,v.blade.zw));
                float4 position = mul(UNITY_MATRIX_VP, float4(positionWS, 1));
                #if defined(UNITY_REVERSED_Z)
                    position.z += .000005 * position.w;
                #else
                    position.z -= .000005 * position.w;
                #endif
                return position;
            }
            half4 frag() : SV_Target { return half4(.2, .95, 1, .8); }
            ENDHLSL
        }
    }
}
