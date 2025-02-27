using Unity.Mathematics;
using UnityEngine;

public class Noise : MonoBehaviour
{
    static System.Random rng;
    static float minHeight;
    static float maxHeight;
    public ComputeShader noiseShader;
    public ComputeShader normalizationShader;
    
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
    private static readonly int NumVertices = Shader.PropertyToID("numVertices");
    private static readonly int MidPoint = Shader.PropertyToID("_MidPoint");
    private static readonly int NoiseType = Shader.PropertyToID("noiseType");


    // Version of this function where we offload work to the gpu
    public float[] ComputeHeightMap(int mapWidth, int mapHeight, int seed, float scale, int octaves,
        float persistence, float lacunarity, Vector2 offset, int noiseType)
    {
        // Set seed so this will be consistent
        rng = new System.Random(seed);
        int mapLength = mapWidth * mapHeight;
        int normalPrecision = 1000 * octaves;
        
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
        int[] ends = {normalPrecision, -normalPrecision};
        ComputeBuffer normalization = new ComputeBuffer(2, 4);
        normalization.SetData(ends);
        noiseShader.SetBuffer(0, RangeValues, normalization);
        
        
        // For midpoint scaling
        float[] midPoint = {mapWidth / 2.0f, mapHeight / 2.0f};
        ComputeBuffer mid = new ComputeBuffer(2, 4);
        mid.SetData(midPoint);
        noiseShader.SetBuffer(0, MidPoint, mid);
        
        
        // Set Actual heightMap to buffer
        float[] map = new float[mapLength];
        ComputeBuffer heightMap = new ComputeBuffer(mapWidth * mapHeight, 4);
        heightMap.SetData(map);
        noiseShader.SetBuffer(0, HeightMapBuffer, heightMap);
        
        
        // Set variables to shader
        noiseShader.SetInt(NumVertices, mapLength);
        noiseShader.SetInt(MapWidth, mapWidth);
        noiseShader.SetInt(Octaves, octaves);
        noiseShader.SetInt(NormalPrecision, normalPrecision);
        noiseShader.SetFloat(ScaleFactor, scale);
        noiseShader.SetFloat(Persistence, persistence);
        noiseShader.SetFloat(Lacunarity, lacunarity);
        noiseShader.SetInt(NoiseType, noiseType);
        
        
        // Dispatch
        noiseShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
        
        // Update Data
        normalization.GetData(ends);
        float[] preciseEnds = {ends[0] / (float) normalPrecision, ends[1] / (float) normalPrecision};
        normalization.SetData(preciseEnds); // I can just set this because name and size don't change
        
        // Set Data for height normalization shader
        normalizationShader.SetBuffer(0, RangeValues, normalization);
        normalizationShader.SetBuffer(0, HeightMapBuffer, heightMap);
        normalizationShader.SetInt(NumVertices, mapLength);
        
        
        // Dispatch then fetch data
        normalizationShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
        heightMap.GetData(map);

        
        // Release Buffers
        offsetBuffer.Release();
        normalization.Release();
        heightMap.Release();
        mid.Release();
        
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
