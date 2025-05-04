#ifndef GET_MIN_MAX
#define GET_MIN_MAX
#pragma target 5.0 // Needed for StructuredBuffer


StructuredBuffer<float2> _MinMaxBuffer; // This is always size 1

// This is the only real way to pass this to shaderGraph
// This isn't a part of MeshCreator.hlsl because that required an index during the vertex shader, while this is for the pixel shader
void GetMinMax_float(out float Min, out float Max)
{
    Min = _MinMaxBuffer[0][0];
    Max = _MinMaxBuffer[0][1];
}

#endif

