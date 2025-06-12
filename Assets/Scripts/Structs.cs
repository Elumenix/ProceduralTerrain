using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

[System.Serializable]
public struct TerrainType
{
    public string name;
    public float height;
    public Color color;
}

public enum DrawMode
{
    noiseMap,
    colorMap,
    heightMap,
    coloredHeightMap
};

public enum NoiseType
{
    Perlin,
    Simplex,
    Worley
}
    
[StructLayout(LayoutKind.Sequential)]
struct VertexData
{
    public float3 position;
    public float3 normal;
    public float3 tangent;
}

[StructLayout(LayoutKind.Sequential)]
struct OctaveParams
{
    public float2 offset;
    public float frequency;
    public float amplitude;
}
