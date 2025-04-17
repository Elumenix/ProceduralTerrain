using System;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

public class Noise : MonoBehaviour
{
    //static System.Random rng;
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
    private static readonly int OctaveBuffer = Shader.PropertyToID("_OctaveBuffer");


    [StructLayout(LayoutKind.Sequential)]
    struct OctaveParams
    {
        public Vector2 offset;
        public float frequency;
        public float amplitude;
    }
    
    // Version of this function where we use the gpu asynchronously, which is required for Unity WebGPU beta
    public void ComputeHeightMap(int mapWidth, int mapHeight, int seed, float scale, int octaves,
        float persistence, float lacunarity, Vector2 offset, int noiseType, float warpStrength, float warpFreq, int smoothingPasses, Action<float[]> callback)
    {
        // Set seed so this will be consistent
        Random.InitState(seed);
        int mapLength = mapWidth * mapHeight;
        //int normalPrecision = 10000;

        
        // Set Actual heightMap to buffer
        float[] map = new float[mapLength];
        ComputeBuffer heightMap = new ComputeBuffer(mapLength, 4);
        heightMap.SetData(map);
        noiseShader.SetBuffer(0, HeightMapBuffer, heightMap);
        
        // For midpoint scaling
        float2[] midPoint = {new float2(mapWidth / 2.0f, mapHeight / 2.0f)};
        ComputeBuffer mid = new ComputeBuffer(1, 8);
        mid.SetData(midPoint);
        noiseShader.SetBuffer(0, MidPoint, mid);
        
        // Precompute octave parameters to make shader more efficient
        OctaveParams[] octParams = new OctaveParams[octaves];
        for(int i = 0; i < octaves; i++)
        {
            octParams[i].offset = new float2(Random.Range(-100000, 100000) + offset.x, Random.Range(-100000, 100000) + offset.y);
            octParams[i].frequency = Mathf.Pow(lacunarity, i);
            octParams[i].amplitude = Mathf.Pow(persistence, i);
        }
        ComputeBuffer octaveBuffer = new ComputeBuffer(octaves, 16);
        octaveBuffer.SetData(octParams);
        noiseShader.SetBuffer(0, OctaveBuffer, octaveBuffer);


        // Set variables to shader
        noiseShader.SetInt(NumVertices, mapLength);
        noiseShader.SetInt(MapWidth, mapWidth);
        noiseShader.SetInt(Octaves, octaves);
        noiseShader.SetFloat(ScaleFactor, scale * mapWidth); // Scale is multiplied by mapWidth for consistency
        noiseShader.SetFloat(WarpStrength, warpStrength);
        noiseShader.SetFloat(WarpFrequency, warpFreq);
        
        noiseShader.DisableKeyword("_PERLIN");
        noiseShader.DisableKeyword("_SIMPLEX");
        noiseShader.DisableKeyword("_WORLEY");

        // Enable keyword of proper noise type for the shader
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

        if (warpStrength == 0 || warpFreq == 0)
        {
            noiseShader.DisableKeyword("_WARP_ENABLED");
        }
        else
        {
            noiseShader.EnableKeyword("_WARP_ENABLED");
        }


        // Dispatch
        noiseShader.Dispatch(0, Mathf.CeilToInt(mapLength / 128.0f), 1, 1);
        
        
        // Request ends after noise calculation
        AsyncGPUReadback.Request(heightMap, request =>
        {
            if (request.hasError)
            {
                Debug.LogError("intRangeBuffer failed");
                //intRangeBuffer.Release();
                octaveBuffer.Release();
                mid.Release();
                heightMap.Release();
                return;
            }

            // Successfully get data and continue
            //ends = request.GetData<int>().ToArray();
            //intRangeBuffer.Release();
            //Debug.Log(ends[0] + " " + ends[1]);
            
            // Retrieving the new heightmap and finding its range of values so we can normalize it
            map = request.GetData<float>().ToArray();
            float minF = float.MaxValue;
            float maxF = float.MinValue;
            foreach (float f in map)
            {
                minF = Mathf.Min(minF, f);
                maxF = Math.Max(maxF, f);
            }
            
            // Construct float buffer
            ComputeBuffer floatRangeBuffer = new ComputeBuffer(2, 4);
            //float[] preciseEnds = {ends[0] / (float) normalPrecision, ends[1] / (float) normalPrecision};
            float[] preciseEnds = {minF, maxF};
            floatRangeBuffer.SetData(preciseEnds); // I can just set this because name and size don't change

            // Set Data for height normalization shader
            normalizationShader.SetBuffer(0, RangeValues, floatRangeBuffer);
            normalizationShader.SetBuffer(0, HeightMapBuffer, heightMap);
            normalizationShader.SetInt(NumVertices, mapLength);


            // Dispatch then fetch data
            normalizationShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
            
            
            // No longer needed
            octaveBuffer.Release();
            mid.Release();


            AsyncGPUReadback.Request(heightMap, request2 =>
            {
                // We're done with this after normalization
                floatRangeBuffer.Release(); 

                if (request2.hasError)
                {
                    Debug.LogError("normalized heightmap failed");
                    heightMap.Release();
                    return;
                }
                
                // Retrieve
                map = request2.GetData<float>().ToArray();
                
                // Decide if we will do smoothing passes
                if (smoothingPasses == 0)
                {
                    // If not, we're essentially done
                    heightMap.Release();
                    callback(map);
                }
                else
                {
                    // This buffer will save the results of a smoothing pass
                    ComputeBuffer resultBuffer = new ComputeBuffer(mapLength, 4);

                    // Do all smoothing passes then do final return
                    SmoothMap(heightMap, resultBuffer, mapWidth, mapHeight, smoothingPasses, (smoothedMap) =>
                    {
                        heightMap.Release();
                        resultBuffer.Release();
                        callback(smoothedMap);
                    });
                }
            });
        });
    }
    
    // Helper function to do all smoothing passes on the map asynchronously, which is necessary for Unity WebGPU beta
    void SmoothMap(ComputeBuffer heightMap, ComputeBuffer resultBuffer, int mapWidth, int mapHeight, int remainingPasses, Action<float[]> callback)
    {
        // Set input/output buffers and parameters
        smoothShader.SetBuffer(0, OriginalHeightMap, heightMap);
        smoothShader.SetBuffer(0, SmoothedHeightMap, resultBuffer);
        smoothShader.SetInt(MapWidth, mapWidth);
        smoothShader.SetInt(MapHeight, mapHeight);

        // Dispatch the compute shader
        smoothShader.Dispatch(0, Mathf.CeilToInt(mapWidth / 8f), Mathf.CeilToInt(mapHeight / 8f), 1);


        if (remainingPasses > 1)
        {
            // A new request causes the program to wait until the previous one finishes, so this is safe
            AsyncGPUReadback.Request(resultBuffer, request =>
            {
                if (request.hasError)
                {
                    Debug.LogError("Smoothing pass failed");
                    heightMap.Release();
                    resultBuffer.Release();
                    return;
                }

                // Swap buffers and recurse
                SmoothMap(
                    resultBuffer,
                    heightMap, // Swap input/output
                    mapWidth,
                    mapHeight,
                    remainingPasses - 1,
                    callback
                );
            });
        }
        else
        {
            // Final ReadBack
            AsyncGPUReadback.Request(resultBuffer, request3 =>
            {
                if (request3.hasError)
                {
                    Debug.LogError("Final smoothing readBack failed");
                    heightMap.Release();
                    resultBuffer.Release();
                    return;
                }
                
                // Final height map data gets passed back
                callback(request3.GetData<float>().ToArray());
            });
        }
    }
    
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
}
