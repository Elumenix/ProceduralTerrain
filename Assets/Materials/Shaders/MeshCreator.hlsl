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

void FixMesh_float(float VertexIDf, out float3 Position, out float3 Normal, out float3 Tangent)
{
    uint index = (uint)VertexIDf;
    uint vertex = _IndexBuffer[index];
    VertexData v = _VertexDataBuffer[vertex];

    Position = v.position;
    Normal = v.normal;
    Tangent = v.tangent;
}

#endif
