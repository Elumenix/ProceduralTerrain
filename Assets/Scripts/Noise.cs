using UnityEngine;

public static class Noise
{
    public static System.Random rng;
    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, int seed, float scale, int octaves, float persistence, float lacunarity, Vector2 offset)
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
        float maxHeight = float.MinValue;
        float minHeight = float.MaxValue;
        
        
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
                    float sampleX = (x-midPoint.x) / scale * frequency - offsets[i].x;
                    float sampleY = (y-midPoint.y) / scale * frequency - offsets[i].y;
                    
                    // true offset
                    //float sampleX = ((x-midPoint.x) / scale - offsets[i].x)* frequency;
                    //float sampleY = ((y-midPoint.y) / scale - offsets[i].y) * frequency;
        
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
