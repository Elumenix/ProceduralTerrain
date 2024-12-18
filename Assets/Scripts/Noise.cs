using UnityEngine;

public static class Noise
{
    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, float scale)
    {
        float[,] noiseMap = new float[mapWidth, mapHeight];
        
        if (scale <= 0)
        {
            scale = 0.0001f;
        }
        
        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                // Perlin noise is the same at even values, so it needs to be scaled by some float
                noiseMap[x, y] = Mathf.PerlinNoise(x / scale, y / scale);
            }
        }

        return noiseMap;
    }
}
