using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

public class Noise : MonoBehaviour
{
    static float minHeight;
    static float maxHeight;
    public ComputeShader noiseShader;
    public ComputeShader reductionShader;
    public ComputeShader normalizationShader;
    public ComputeShader smoothShader;
    private static ComputeShader rangeShader; 
    private static ComputeBuffer octaveBuffer;
    private static ComputeBuffer curveBuffer;
    private static ComputeBuffer minMax;
    private static ComputeBuffer writeBuffer;
    private static ComputeBuffer reductionBuffer;

    // Cached Strings : Speeds things up 
    private static readonly int RangeValues = Shader.PropertyToID("_RangeValues");
    private static readonly int HeightMapBuffer = Shader.PropertyToID("_HeightMapBuffer");
    private static readonly int Octaves = Shader.PropertyToID("octaves");
    private static readonly int ScaleFactor = Shader.PropertyToID("scaleFactor");
    private static readonly int MapWidth = Shader.PropertyToID("mapWidth");
    private static readonly int NumVertices = Shader.PropertyToID("numVertices");
    private static readonly int MidPoint = Shader.PropertyToID("midPoint");
    private static readonly int WarpStrength = Shader.PropertyToID("warpStrength");
    private static readonly int WarpFrequency = Shader.PropertyToID("warpFrequency");
    private static readonly int OctaveBuffer = Shader.PropertyToID("_OctaveBuffer");
    private static readonly int HeightMultiplier = Shader.PropertyToID("heightMultiplier");
    private static readonly int Resolution = Shader.PropertyToID("resolution");
    private static readonly int ReadBuffer = Shader.PropertyToID("_ReadBuffer");
    private static readonly int WriteBuffer = Shader.PropertyToID("_WriteBuffer");
    private static readonly int MinMax = Shader.PropertyToID("_MinMax");
    private static readonly int MinMaxInput = Shader.PropertyToID("_MinMaxInput");
    private static readonly int MinMaxResult = Shader.PropertyToID("_MinMaxResult");
    private static readonly int HeightCurveBuffer = Shader.PropertyToID("_HeightCurveBuffer");


    [StructLayout(LayoutKind.Sequential)]
    struct OctaveParams
    {
        public float2 offset;
        public float frequency;
        public float amplitude;
    }

    private void Awake()
    {
        // Because I need it to be static
        rangeShader = reductionShader;
        
        // Set up guaranteed persistent buffers
        octaveBuffer = new ComputeBuffer(10, sizeof(float) * 4); // float2 + float + float
        curveBuffer = new ComputeBuffer(128, sizeof(float));
        minMax = new ComputeBuffer(1, sizeof(float) * 2); // float2
    }

    // Version of this function where we use the gpu asynchronously, which is required for Unity WebGPU beta
    public void ComputeHeightMap(ref ComputeBuffer heightMap, int mapResolution, Unity.Mathematics.Random _random, float scale, int octaves,
        float persistence, float lacunarity, float2 offset, int noiseType, float warpStrength, float warpFreq,
        int smoothingPasses, float heightMultiplier)
    {
        int mapLength = mapResolution * mapResolution;
        int threadGroups = Mathf.CeilToInt(mapResolution / 16.0f);

        // Precompute octave parameters to make shader more efficient
        OctaveParams[] octParams = new OctaveParams[octaves];
        for (int i = 0; i < octaves; i++)
        {
            octParams[i].offset = _random.NextFloat2(-100000, 100000) + offset;
            octParams[i].frequency = Mathf.Pow(lacunarity, i);
            octParams[i].amplitude = Mathf.Pow(persistence, i);
        }
        octaveBuffer.SetData(octParams);

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
        PerformReductions(heightMap, mapLength);
        
        // NORMALIZATION & HEIGHT SCALE ADJUSTMENT
        normalizationShader.SetBuffer(0, HeightMapBuffer, heightMap);
        normalizationShader.SetBuffer(0, RangeValues, minMax);
        normalizationShader.SetBuffer(0, HeightCurveBuffer, curveBuffer);
        normalizationShader.SetInt(NumVertices, mapLength);
        normalizationShader.SetFloat(HeightMultiplier, heightMultiplier);
        normalizationShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
        
        // MESH SMOOTHING
        if (smoothingPasses > 0)
        {
            ComputeBuffer readBuffer = heightMap;
            smoothShader.SetInt(Resolution, mapResolution);
            
            // By relying on mapLength, this can't be completely persistent. We'll only remake it if map size changes
            if (writeBuffer == null || writeBuffer.count != mapLength)
            {
                writeBuffer?.Release();
                writeBuffer = new ComputeBuffer(mapLength, sizeof(float));
                #if UNITY_EDITOR // Handled automatically be WebGPU
                writeBuffer.SetData(new float[mapLength]);
                #endif
            }
            
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
    }

    // Essentially gets the min and max values of the mesh
    public static ComputeBuffer PerformReductions(ComputeBuffer heightMap, int mapLength)
    {
        // REDUCTION PART 1
        // Reduction refers to me iterating the mesh to find the min and max height values
        // This could have been done (with two lines) in the noise shader using atomics IF UNITY'S WEBGPU IMPLEMENTATION SUPPORTED IT
        int reductionGroups = Mathf.CeilToInt(mapLength / 256.0f);
        
        // Because this is size based, a new buffer will be needed if resolution changes
        if (reductionBuffer == null || reductionBuffer.count != reductionGroups)
        {
            reductionBuffer?.Release();
            reductionBuffer = new ComputeBuffer(reductionGroups, sizeof(float) * 2); // float2
            #if UNITY_EDITOR // Handled automatically be WebGPU
            reductionBuffer.SetData(new float2[reductionGroups]);
            #endif
        }
        
        // Set variables and dispatch
        rangeShader.SetBuffer(0, HeightMapBuffer, heightMap);
        rangeShader.SetBuffer(0, MinMax, reductionBuffer);
        rangeShader.SetInt(NumVertices, mapLength);
        rangeShader.Dispatch(0, reductionGroups, 1, 1);
        
        // REDUCTION PART 2
        // Now that there are up to 2048 reduction groups left, we can do them all in one pass
        // Note that this would not be the case if the map resolution were above 4096x4096, but that's far higher than the user can go
        minMax.SetData(new float2[1]);
        
        // Set Data
        rangeShader.SetInt(NumVertices, reductionGroups);
        rangeShader.SetBuffer(1, MinMaxInput, reductionBuffer);
        rangeShader.SetBuffer(1, MinMaxResult, minMax);
        
        // 1 group will handle all remaining elements (up to 2048)
        rangeShader.Dispatch(1,1,1,1);
        
        return minMax;
    }

    public static void ChangeCurve(AnimationCurve heightCurve)
    {
        float[] curve = new float[128];
        for (int i = 0; i < 128; i++)
        {
            curve[i] = heightCurve.Evaluate(i / 127.0f);
        }
        curveBuffer.SetData(curve);
    }

    private void OnApplicationQuit()
    {
        octaveBuffer.Release();
        curveBuffer.Release();
        minMax.Release();
        writeBuffer.Release();
        reductionBuffer.Release();
    }
    
}
