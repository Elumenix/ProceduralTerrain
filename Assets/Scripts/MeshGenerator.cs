using System;
using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshGenerator : MonoBehaviour
{
    public enum DrawMode
    {
        noiseMap,
        colorMap,
        heightMap,
        coloredHeightMap
    };
    
    // Variables Changeable within the editor
    public DrawMode drawMode;
    public int mapWidth;
    public int mapHeight;
    public float noiseScale;
    [Range(1,6)]
    public int octaves;
    [Range(0,1)]
    public float persistence;
    [Range(1,3)]
    public float lacunarity;
    public int seed;
    public Vector2 offset;

    public TerrainType[] regions;
    
    // Object reference variables
    private Renderer textureRenderer;
    private Mesh mesh;
    
    // Mesh information
    private Vector3[] vertices;
    private int[] indices;
    private Vector2[] uvs;
    private float[,] noiseMap; 
    
    void Start()
    {
        // Set reference for gameObject to use the mesh we create here
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
        textureRenderer = GetComponent<MeshRenderer>();
        
        GenerateMap();
    }

    private void OnValidate()
    {
        if (mapWidth < 1) mapWidth = 1;
        if (mapHeight < 1) mapHeight = 1;
        if (noiseScale <= 0) noiseScale = 0.0011f;
        if (lacunarity < 1) lacunarity = 1;
    }

    public void GenerateMap()
    {
        
        #if UNITY_EDITOR
        // The only case where this should happen
        if (!mesh)
        {
            mesh = new Mesh();
            GetComponent<MeshFilter>().mesh = mesh;
            textureRenderer = GetComponent<MeshRenderer>();
        }
        #endif
        
        noiseMap = Noise.GenerateNoiseMap(mapWidth, mapHeight, seed, noiseScale, octaves, persistence, lacunarity, offset);
        CreateMesh();
        UpdateMesh();
    }


    void UpdateMesh()
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.uv = uvs;
        mesh.RecalculateNormals(); // Fixes Lighting
    }

    void CreateMesh()
    {
        // This was originally supposed to be two different methods, but it became much more efficient,
        // Albeit messy looking, to combine them to only loop through the map once
        
        // Variables for texturing
        int width = noiseMap.GetLength(0);
        int height = noiseMap.GetLength(1);
        Texture2D texture = new(width, height)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] colorMap = new Color[width * height];
        
        // Variables for mesh deformation
        float widthScale = 100.0f / mapWidth;
        float heightScale = 100.0f / mapHeight;
        vertices = new Vector3[(mapWidth + 1) * (mapHeight + 1)];
        uvs = new Vector2[(mapWidth + 1) * (mapHeight + 1)];
        indices = new int[mapWidth * mapHeight * 2 * 3];
        int num = 0;
        int indexNum = 0;

        // Trying to set the texture as perlin noise
        for (int x = 0; x <= width; x++)
        {
            for (int z = 0; z <= height; z++)
            {
                if (drawMode is DrawMode.heightMap or DrawMode.coloredHeightMap)
                {
                    vertices[num] = new Vector3(x * widthScale / 100,
                        Mathf.Max(noiseMap[Mathf.Min(x, mapWidth - 1), Mathf.Min(z, mapHeight - 1)], .4f) * 5,
                        z * heightScale / 100);
                }
                else
                {
                    vertices[num] = new Vector3(x * widthScale / 100, .4f, z * heightScale / 100);
                }

                uvs[num] = new Vector2(x * widthScale / 100, z * heightScale / 100);
                num++;
                
                if (x == width || z == height) continue;
                
                // We're forming a square here with vertices from the bottom left vertex
                // Top left triangle
                indices[indexNum] = x * (mapHeight + 1) + z;
                indices[indexNum + 1] = indices[indexNum] + 1; 
                indices[indexNum + 2] = (x+1) * (mapHeight + 1) + z + 1;
                    
                // Bottom right triangle
                indices[indexNum + 3] = indices[indexNum + 2];
                indices[indexNum + 4] = (x+1) * (mapHeight + 1) + z;
                indices[indexNum + 5] = indices[indexNum];
                indexNum += 6;


                // Determines whether the texture will be greyscale or colored
                switch (drawMode)
                {
                    case DrawMode.noiseMap:
                    case DrawMode.heightMap:
                        colorMap[z * width + x] = Color.Lerp(Color.black, Color.white, noiseMap[x, z]);
                        break;
                    case DrawMode.colorMap:
                    case DrawMode.coloredHeightMap:
                    {
                        float currentHeight = noiseMap[x, z];
                        for (int i = 0; i < regions.Length; i++)
                        {
                            if (!(currentHeight <= regions[i].height)) continue;
                            
                            colorMap[z * width + x] = regions[i].color;
                            break;
                        }
                        break;
                    }
                }
            }
        }
        
        texture.SetPixels(colorMap);
        texture.Apply();
        textureRenderer.sharedMaterial.mainTexture = texture;
    }
}
