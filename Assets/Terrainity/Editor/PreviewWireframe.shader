Shader "Hidden/Terrainity/Preview Wireframe"
{
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
            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                float4 position = UnityObjectToClipPos(vertex);
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
