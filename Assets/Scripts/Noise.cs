using System;
using Unity.Mathematics;
using UnityEngine;

public class Noise : MonoBehaviour
{
    static System.Random rng;
    static float minHeight;
    static float maxHeight;
    public ComputeShader noiseShader;
    public ComputeShader normalizationShader;
    public ComputeShader smoothShader;
    
    // Cached Strings : Speeds things up 
    private static readonly int OffsetBuffer = Shader.PropertyToID("_OffsetBuffer");
    private static readonly int RangeValues = Shader.PropertyToID("_RangeValues");
    private static readonly int HeightMapBuffer = Shader.PropertyToID("_HeightMapBuffer");
    private static readonly int Octaves = Shader.PropertyToID("octaves");
    private static readonly int ScaleFactor = Shader.PropertyToID("scaleFactor");
    private static readonly int Persistence = Shader.PropertyToID("persistence");
    private static readonly int Lacunarity = Shader.PropertyToID("lacunarity");
    private static readonly int NormalPrecision = Shader.PropertyToID("normalPrecision");
    private static readonly int MapWidth = Shader.PropertyToID("mapWidth");
    private static readonly int MapHeight = Shader.PropertyToID("mapHeight");
    private static readonly int NumVertices = Shader.PropertyToID("numVertices");
    private static readonly int MidPoint = Shader.PropertyToID("_MidPoint");
    private static readonly int NoiseType = Shader.PropertyToID("noiseType");
    private static readonly int WarpStrength = Shader.PropertyToID("warpStrength");
    private static readonly int WarpFrequency = Shader.PropertyToID("warpFrequency");
    private static readonly int OriginalHeightMap = Shader.PropertyToID("_OriginalHeightMap");
    private static readonly int SmoothedHeightMap = Shader.PropertyToID("_SmoothedHeightMap");


    // Version of this function where we offload work to the gpu
    public float[] ComputeHeightMap(int mapWidth, int mapHeight, int seed, float scale, int octaves,
        float persistence, float lacunarity, Vector2 offset, int noiseType, float warpStrength, float warpFreq, int smoothingPasses)
    {
        // Set seed so this will be consistent
        rng = new System.Random(seed);
        int mapLength = mapWidth * mapHeight;
        int normalPrecision = 10000;

        // Establish offsets for each point
        float2[] offsets = new float2[octaves];
        for (int i = 0; i < octaves; i++)
        {
            offsets[i] = new float2(rng.Next(-100000, 100000) + offset.x, rng.Next(-100000, 100000) + offset.y);
        }

        ComputeBuffer offsetBuffer = new(octaves, 8);
        offsetBuffer.SetData(offsets);
        noiseShader.SetBuffer(0, OffsetBuffer, offsetBuffer);

        // For normalization
        int[] ends = {Int32.MaxValue, Int32.MinValue};
        ComputeBuffer intRangeBuffer = new ComputeBuffer(2, 4);
        intRangeBuffer.SetData(ends);
        noiseShader.SetBuffer(0, RangeValues, intRangeBuffer);


        // For midpoint scaling
        float2[] midPoint = {new float2(mapWidth / 2.0f, mapHeight / 2.0f)};
        ComputeBuffer mid = new ComputeBuffer(1, 8);
        mid.SetData(midPoint);
        noiseShader.SetBuffer(0, MidPoint, mid);


        // Set Actual heightMap to buffer
        float[] map = new float[mapLength];
        ComputeBuffer heightMap = new ComputeBuffer(mapLength, 4);
        heightMap.SetData(map);
        noiseShader.SetBuffer(0, HeightMapBuffer, heightMap);


        // Set variables to shader
        noiseShader.SetInt(NumVertices, mapLength);
        noiseShader.SetInt(MapWidth, mapWidth);
        noiseShader.SetInt(Octaves, octaves);
        noiseShader.SetInt(NormalPrecision, normalPrecision);
        noiseShader.SetFloat(ScaleFactor, scale * mapWidth); // Scale is multiplied by mapWidth for consistency
        noiseShader.SetFloat(Persistence, persistence);
        noiseShader.SetFloat(Lacunarity, lacunarity);
        noiseShader.SetInt(NoiseType, noiseType);
        noiseShader.SetFloat(WarpStrength, warpStrength);
        noiseShader.SetFloat(WarpFrequency, warpFreq);


        // Dispatch
        noiseShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);

        // Update Data
        intRangeBuffer.GetData(ends);
        ComputeBuffer floatRangeBuffer = new ComputeBuffer(2, 4);
        float[] preciseEnds = {ends[0] / (float) normalPrecision, ends[1] / (float) normalPrecision};
        floatRangeBuffer.SetData(preciseEnds); // I can just set this because name and size don't change

        // Set Data for height normalization shader
        normalizationShader.SetBuffer(0, RangeValues, floatRangeBuffer);
        normalizationShader.SetBuffer(0, HeightMapBuffer, heightMap);
        normalizationShader.SetInt(NumVertices, mapLength);


        // Dispatch then fetch data
        normalizationShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);

        if (smoothingPasses == 0)
        {
            heightMap.GetData(map);
        }


        ComputeBuffer resultBuffer = new ComputeBuffer(mapLength, 4);
        for (int i = 0; i < smoothingPasses; i++)
        {
            // Set input/output buffers and parameters
            smoothShader.SetBuffer(0, OriginalHeightMap, heightMap);
            smoothShader.SetBuffer(0, SmoothedHeightMap, resultBuffer);
            smoothShader.SetInt(MapWidth, mapWidth);
            smoothShader.SetInt(MapHeight, mapHeight);

            // Dispatch the compute shader
            smoothShader.Dispatch(0, Mathf.CeilToInt(mapWidth / 8f), Mathf.CeilToInt(mapHeight / 8f), 1);
            
            // Swap the buffers for the next pass
            if (i != smoothingPasses - 1)
            {
                // Used deconstruction to swap
                (heightMap, resultBuffer) = (resultBuffer, heightMap);
            }
        }
        
        resultBuffer.GetData(map);

        
        // Release Buffers
        offsetBuffer.Release();
        intRangeBuffer.Release();
        floatRangeBuffer.Release();
        heightMap.Release();
        mid.Release();
        resultBuffer.Release();

        
        return map;
    }
    
    
    // Version of this function that is completely on the cpu
    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, int seed, float scale, int octaves,
        float persistence, float lacunarity, Vector2 offset)
    {
        float[,] noiseMap = new float[mapWidth, mapHeight];
        rng = new System.Random(seed);
        
        Vector2[] offsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++)
        {
            offsets[i] = new Vector2(rng.Next(-100000, 100000) + offset.x, rng.Next(-100000, 100000) + offset.y);
        }

        Vector2 midPoint = new(mapWidth / 2.0f, mapHeight / 2.0f);
        
        // For normalization
        maxHeight = float.MinValue;
        minHeight = float.MaxValue;
        
        
        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;
                
                for (int i = 0; i < octaves; i++)
                {
                    // Offset parallaxes the noise
                    //float sampleX = (x-midPoint.x) / scale * frequency - offsets[i].x;
                    //float sampleY = (y-midPoint.y) / scale * frequency - offsets[i].y;
                    
                    // true offset
                    float sampleX = ((x-midPoint.x) / scale + offsets[i].x) * frequency;
                    float sampleY = ((y-midPoint.y) / scale + offsets[i].y) * frequency;
        
                    // Ranges from -1 to 1
                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;
                    
                    noiseHeight += perlinValue * amplitude;
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                // For normalization
                maxHeight = Mathf.Max(maxHeight, noiseHeight);
                minHeight = Mathf.Min(minHeight, noiseHeight);

                noiseMap[x, y] = noiseHeight;
            }
        }
        
        // Normalization: all values in map should be between 0 and 1
        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                noiseMap[x, y] = Mathf.InverseLerp(minHeight, maxHeight, noiseMap[x, y]);
            }
        }

        return noiseMap;
    }
}
