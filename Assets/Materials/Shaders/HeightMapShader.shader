Shader "Unlit/HeightMapShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0 // Needed for StructuredBuffer
            #include "UnityCG.cginc"


            // For Mapping Vertices/Indices
            struct VertexData {
                float3 position;
                float3 normal;
                float3 tangent;
            };
            
            StructuredBuffer<VertexData> _VertexDataBuffer;
            StructuredBuffer<uint> _IndexBuffer;
            StructuredBuffer<float2> _MinMaxBuffer; // This is always size 1
            float4x4 _Rotation;

            struct VertexInput
            {
                uint vertexID : SV_VertexID; // ONLY NEED THIS
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float heightValue : TEXCOORD0;
            };

            v2f vert (VertexInput v)
            {
                v2f o;
                // Get actual vertex index from index buffer
                uint index = _IndexBuffer[v.vertexID];
                VertexData data = _VertexDataBuffer[index];

                // Calculate height value FIRST before any transformations
                float minVal = _MinMaxBuffer[0].x;
                float maxVal = _MinMaxBuffer[0].y;
                o.heightValue = (data.position.y - minVal) / (maxVal - minVal);

                // Apply transformations
                float4 worldPos = mul(unity_ObjectToWorld, float4(data.position, 1));
                float4 rotatedPos = mul(_Rotation, worldPos);
                float4 finalPos = mul(unity_WorldToObject, rotatedPos);
                
                // Flatten the mesh in object space
                finalPos.y = 0;

                // Properly transform to clip space
                o.vertex = UnityObjectToClipPos(finalPos.xyz);
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(i.heightValue.xxx, 1);
            }
            ENDCG
        }
    }
}
