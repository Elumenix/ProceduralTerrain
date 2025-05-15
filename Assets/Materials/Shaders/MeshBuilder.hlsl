#if !defined(UNIVERSAL_LIGHTING_INCLUDED)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#endif

#ifndef FIX_MESH_CODE
#define FIX_MESH_CODE
#pragma target 5.0 // Needed for StructuredBuffer

// The entire point of this class is to match indices to vertex/normal/tangent in a shaderGraph
// This script should be added as a custom node to the shaderGraph to be used
struct VertexData {
    float3 position;
    float3 normal;
    float3 tangent;
};

StructuredBuffer<VertexData> _VertexDataBuffer;
StructuredBuffer<uint> _IndexBuffer;

// float4x4 Rotation is a Pure Rotation Matrix : Has pivot baked in
// OrigPosition is unused, so it's commented out. This actually represents where the Uv's would be if I needed them
void FixMesh_float(float VertexIDf, float4x4 Rotation, out float3 Position, out float3 Normal, out float3 Tangent/*, out float3 origPosition*/)
{
    uint index = (uint)VertexIDf;
    uint vertex = _IndexBuffer[index];
    VertexData v = _VertexDataBuffer[vertex];

    // Vertices are translated int world space, then rotated, then translated back into object space
    float4 worldPos = mul(unity_ObjectToWorld, float4(v.position, 1));
    float3 worldNormal = mul((float3x3)unity_ObjectToWorld, v.normal);
    float3 worldTangent = mul((float3x3)unity_ObjectToWorld, v.tangent);
    
    Position = mul(unity_WorldToObject, mul(Rotation, worldPos)).xyz;
    Normal = mul((float3x3)unity_WorldToObject, mul((float3x3)Rotation, worldNormal));
    Tangent = mul((float3x3)unity_WorldToObject, mul((float3x3)Rotation, worldTangent));
    /*origPosition = v.position;*/
}

#endif
