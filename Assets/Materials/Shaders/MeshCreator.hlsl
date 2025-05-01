#ifndef FIX_MESH_CODE
#define FIX_MESH_CODE
#pragma target 5.0 // Needed for StructuredBuffer

struct VertexData {
    float3 position;
    float u;
    float3 normal;
    float v;
    float3 tangent;
};

StructuredBuffer<VertexData> _VertexDataBuffer;
StructuredBuffer<uint> _IndexBuffer;

void FixMesh_float(float VertexIDf, out float3 Position, out float3 Normal, out float3 Tangent)
{
    uint index = (uint)VertexIDf;
    uint vertex = _IndexBuffer[index];
    VertexData v = _VertexDataBuffer[vertex];

    Position = float3(v.position.x * 100, v.position.y, v.position.z * 100);
    Normal = v.normal;
    Tangent = v.tangent;
}

#endif
