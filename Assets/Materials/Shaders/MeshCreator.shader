Shader "Custom/MeshCreator"
{
    
    Properties
    {
        //_Color ("Color", Color) = (1,1,1,1)
        //_MainTex("InputTex", 2D) = "white" {}
     }
     SubShader
     {
         Tags{ "RenderType" = "Opaque" }
         Pass
         {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0 // Needed for StructuredBuffer
            #include "UnityCG.cginc"

            struct VertexData {
                float3 position;
                float u;
                float3 normal;
                float v;
            };

            StructuredBuffer<VertexData> _VertexDataBuffer;
            StructuredBuffer<uint> _IndexBuffer; 

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            v2f vert(uint id : SV_VertexID) {
                // Fetch index from the index buffer
                uint index = _IndexBuffer[id];
                
                // Fetch vertex data using the index
                VertexData v = _VertexDataBuffer[index];
                
                v2f o;
                o.pos = UnityObjectToClipPos(float3(v.position.x * 100, v.position.y, v.position.z * 100));
                o.uv = float2(v.u, v.v);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                return fixed4(i.normal * 0.5 + 0.5, 1);
                //return fixed4(1,1,1,1);
            }
            ENDCG
        }
    }
}
