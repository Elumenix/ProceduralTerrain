using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

public class Noise : MonoBehaviour
{
    //static System.Random rng;
    static float minHeight;
    static float maxHeight;
    public ComputeShader noiseShader;
    public ComputeShader reductionShader;
    public ComputeShader normalizationShader;
    public ComputeShader smoothShader;
    private static ComputeShader rangeShader;

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
    private static readonly int MidPoint = Shader.PropertyToID("midPoint");
    private static readonly int NoiseType = Shader.PropertyToID("noiseType");
    private static readonly int WarpStrength = Shader.PropertyToID("warpStrength");
    private static readonly int WarpFrequency = Shader.PropertyToID("warpFrequency");
    private static readonly int OriginalHeightMap = Shader.PropertyToID("_OriginalHeightMap");
    private static readonly int SmoothedHeightMap = Shader.PropertyToID("_SmoothedHeightMap");
    private static readonly int OctaveBuffer = Shader.PropertyToID("_OctaveBuffer");
    private static readonly int HeightMultiplier = Shader.PropertyToID("heightMultiplier");
    private static readonly int Resolution = Shader.PropertyToID("resolution");
    private static readonly int ReadBuffer = Shader.PropertyToID("_ReadBuffer");
    private static readonly int WriteBuffer = Shader.PropertyToID("_WriteBuffer");
    private static readonly int MinMax = Shader.PropertyToID("_MinMax");
    private static readonly int MinMaxInput = Shader.PropertyToID("_MinMaxInput");
    private static readonly int MinMaxResult = Shader.PropertyToID("_MinMaxResult");


    [StructLayout(LayoutKind.Sequential)]
    struct OctaveParams
    {
        public Vector2 offset;
        public float frequency;
        public float amplitude;
    }

    private void Awake()
    {
        rangeShader = reductionShader;
    }

    // Version of this function where we use the gpu asynchronously, which is required for Unity WebGPU beta
    public void ComputeHeightMap(int mapResolution, Unity.Mathematics.Random _random, float scale,
        int octaves, float persistence, float lacunarity, float2 offset, int noiseType, float warpStrength,
        float warpFreq, int smoothingPasses, float heightMultiplier, List<ComputeBuffer> pendingRelease,
        Action<ComputeBuffer> callback)
    {
        int mapLength = mapResolution * mapResolution;
        int threadGroups = Mathf.CeilToInt(mapResolution / 16.0f);

        // Set Actual heightMap to buffer
        ComputeBuffer heightMap = new ComputeBuffer(mapLength, 4);
        heightMap.SetData(new float[mapLength]);
        pendingRelease.Add(heightMap);


        // Precompute octave parameters to make shader more efficient
        OctaveParams[] octParams = new OctaveParams[octaves];
        for (int i = 0; i < octaves; i++)
        {
            octParams[i].offset = _random.NextFloat2(-100000, 100000) + offset;
            octParams[i].frequency = Mathf.Pow(lacunarity, i);
            octParams[i].amplitude = Mathf.Pow(persistence, i);
        }

        ComputeBuffer octaveBuffer = new ComputeBuffer(octaves, 16);
        octaveBuffer.SetData(octParams);
        pendingRelease.Add(octaveBuffer);

        // Set Buffers and variables
        noiseShader.SetBuffer(0, HeightMapBuffer, heightMap);
        noiseShader.SetBuffer(0, OctaveBuffer, octaveBuffer);
        //noiseShader.SetInt(NumVertices, mapLength);
        noiseShader.SetInt(MapWidth, mapResolution);
        noiseShader.SetInt(Octaves, octaves);
        noiseShader.SetFloat(ScaleFactor, scale * mapResolution); // Scale is multiplied by mapWidth for consistency
        noiseShader.SetFloat(WarpStrength, warpStrength);
        noiseShader.SetFloat(WarpFrequency, warpFreq);
        noiseShader.SetFloats(MidPoint, new float[] {mapResolution / 2.0f, mapResolution / 2.0f});

        // Set Noise Type
        noiseShader.DisableKeyword("_PERLIN");
        noiseShader.DisableKeyword("_SIMPLEX");
        noiseShader.DisableKeyword("_WORLEY");
        switch (noiseType)
        {
            case 1:
                noiseShader.EnableKeyword("_PERLIN");
                break;
            case 2:
                noiseShader.EnableKeyword("_SIMPLEX");
                break;
            default:
                noiseShader.EnableKeyword("_WORLEY");
                break;
        }
        noiseShader.Dispatch(0, threadGroups, threadGroups, 1);

        // Get the min/Max values in the mesh in order to perform normalization
        ComputeBuffer finalMinMax = PerformReductions(heightMap, pendingRelease, mapLength);
        
        // NORMALIZATION
        normalizationShader.SetBuffer(0, HeightMapBuffer, heightMap);
        normalizationShader.SetBuffer(0, RangeValues, finalMinMax);
        normalizationShader.SetInt(NumVertices, mapLength);
        normalizationShader.SetFloat(HeightMultiplier, heightMultiplier);
        normalizationShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
        
        // MESH SMOOTHING
        if (smoothingPasses > 0)
        {
            ComputeBuffer readBuffer = heightMap;
            ComputeBuffer writeBuffer = new ComputeBuffer(mapLength, 4);
            writeBuffer.SetData(new float[mapLength]);
            pendingRelease.Add(writeBuffer);

            smoothShader.SetInt(Resolution, mapResolution);
            
            while (smoothingPasses > 0)
            {
                smoothShader.SetBuffer(0, ReadBuffer, readBuffer);
                smoothShader.SetBuffer(0, WriteBuffer, writeBuffer);
                smoothShader.Dispatch(0, threadGroups, threadGroups, 1);

                // Swap Buffers so that readBuffer hold proper data (sets up next pass and return)
                // Buffers are essentially just pointers, so this is safe and not corrupting data
                (readBuffer, writeBuffer) = (writeBuffer, readBuffer);

                smoothingPasses--;
            }

            heightMap = readBuffer;
        }

        callback(heightMap);
    }

    // Essentially gets the min and max values of the mesh
    public static ComputeBuffer PerformReductions(ComputeBuffer heightMap, List<ComputeBuffer> pendingRelease, int mapLength)
    {
        // Reduction refers to me iterating the mesh to find the min and max height values
        // This could have been done in two lines in the noise shader using atomics IF UNITY'S WEBGPU IMPLEMENTATION SUPPORTED IT
        // REDUCTION PART 1
        int reductionGroups = Mathf.CeilToInt(mapLength / 256.0f);
        ComputeBuffer minMaxBuffer = new ComputeBuffer(reductionGroups, 8); // 8 bytes per float2
        minMaxBuffer.SetData(new float2[reductionGroups]);
        pendingRelease.Add(minMaxBuffer);

        rangeShader.SetBuffer(0, HeightMapBuffer, heightMap);
        rangeShader.SetBuffer(0, MinMax, minMaxBuffer);
        rangeShader.SetInt(NumVertices, mapLength);
        rangeShader.Dispatch(0, reductionGroups, 1, 1);

        // REDUCTION PART 2
        // Now that there are less than 256 groups, we can reduce that down to one
        // Note that this would not be the case if the map resolution were above 4096, but that's far higher than the user can go
        ComputeBuffer finalMinMax = new ComputeBuffer(1, 8);
        finalMinMax.SetData(new float2[1]);
        pendingRelease.Add(finalMinMax);
        
        rangeShader.SetBuffer(1, MinMaxInput, minMaxBuffer);
        rangeShader.SetBuffer(1, MinMaxResult, finalMinMax);
        rangeShader.Dispatch(1,1,1,1);

        return finalMinMax;
    }


    #region Depreciated

    // Version of this function where we offload work to the gpu
    public float[] ComputeHeightMap(int mapWidth, int mapHeight, int seed, float scale, int octaves,
    float persistence, float lacunarity, Vector2 offset, int noiseType, float warpStrength, float warpFreq, int smoothingPasses)
    {
        // Set seed so this will be consistent
        Random.InitState(seed);
        int mapLength = mapWidth * mapHeight;
        int normalPrecision = 10000;

        // Establish offsets for each point
        float2[] offsets = new float2[octaves];
        for (int i = 0; i < octaves; i++)
        {
            offsets[i] = new float2(Random.Range(-100000, 100000) + offset.x, Random.Range(-100000, 100000) + offset.y);
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
        else
        {
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
            resultBuffer.Release();
        }


        // Release Buffers
        offsetBuffer.Release();
        intRangeBuffer.Release();
        floatRangeBuffer.Release();
        heightMap.Release();
        mid.Release();


        return map;
    }


    // Version of this function that is completely on the cpu
    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, int seed, float scale, int octaves,
    float persistence, float lacunarity, Vector2 offset)
    {
    float[,] noiseMap = new float[mapWidth, mapHeight];
    Random.InitState(seed);

    Vector2[] offsets = new Vector2[octaves];
    for (int i = 0; i < octaves; i++)
    {
        offsets[i] = new Vector2(Random.Range(-100000, 100000) + offset.x, Random.Range(-100000, 100000) + offset.y);
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

    #endregion
}
