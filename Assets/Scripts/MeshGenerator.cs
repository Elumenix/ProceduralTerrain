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
        colorMap
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
        
        CreateShape();
        UpdateMesh();
        SetTexture();
    }

    void CreateShape()
    {
        //vertices = new Vector3[(mapWidth + 1) * (mapHeight + 1)];
        float widthScale = 100.0f / mapWidth;
        float heightScale = 100.0f / mapHeight;
        vertices = new Vector3[(mapWidth + 1) * (mapHeight + 1)];
        uvs = new Vector2[(mapWidth + 1) * (mapHeight + 1)];
        indices = new int[mapWidth * mapHeight * 2 * 3];
        float[,] noiseMap = Noise.GenerateNoiseMap(mapWidth, mapHeight, seed, noiseScale, octaves, persistence, lacunarity, offset);


        int num = 0;
        int indexNum = 0;
        
        // Row-major doesn't really matter here, so I'm traversing y first to make things easier
        for (int z = 0; z <= mapHeight; z++)
        {
            for (int x = 0; x <= mapWidth; x++)
            {
                vertices[num] = new Vector3(x * widthScale / 100,
                    Mathf.Max(noiseMap[Mathf.Min(x, mapWidth - 1), Mathf.Min(z, mapHeight - 1)], .4f) * 5,
                    z * heightScale / 100);
                uvs[num] = new Vector2(x * widthScale / 100, z * heightScale / 100);

                // We're forming a square here with vertices from the bottom left vertex
                if (x != mapWidth && z != mapHeight)
                {
                    // Top left triangle
                    indices[indexNum] = x + ((mapWidth + 1) * z);
                    indices[indexNum + 1] = x + ((mapWidth + 1) * (z + 1));
                    indices[indexNum + 2] = x + ((mapWidth + 1) * (z + 1)) + 1;
                    
                    // Bottom right triangle
                    indices[indexNum + 3] = indices[indexNum + 2];
                    indices[indexNum + 4] = indices[indexNum] + 1;
                    indices[indexNum + 5] = indices[indexNum];

                    indexNum += 6;
                }
                
                num++;
            }
        }
        
        
        /*vertices = new Vector3[]
        {
            new Vector3(0,0,0),
            new Vector3(0,0,1),
            new Vector3(.5f,0,0),
            new Vector3(.5f,0,1),
            new Vector3(1, 0, 0),
            new Vector3(1,0,1)
        }*/

        

        /*uvs = new Vector2[]
        {
            new Vector2(0,0),
            new Vector2(0,1),
            new Vector2(.5f,0),
            new Vector2(.5f,1),
            new Vector2(1,0),
            new Vector2(1, 1)
        };*/
    }

    void UpdateMesh()
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.uv = uvs;
        mesh.RecalculateNormals(); // Fixes Lighting
    }

    void SetTexture()
    {
        float[,] noiseMap = Noise.GenerateNoiseMap(mapWidth, mapHeight, seed, noiseScale, octaves, persistence, lacunarity, offset);
        int width = noiseMap.GetLength(0);
        int height = noiseMap.GetLength(1);
        
        
        Texture2D texture = new(width, height);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color[] colorMap = new Color[width * height];

        // Trying to set the texture as perlin noise
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // How to draw the map
                if (drawMode == DrawMode.noiseMap)
                {
                    colorMap[y * width + x] = Color.Lerp(Color.black, Color.white, noiseMap[x, y]);
                }
                else if (drawMode == DrawMode.colorMap)
                {
                    float currentHeight = noiseMap[x, y];
                    for (int i = 0; i < regions.Length; i++)
                    {
                        if (currentHeight <= regions[i].height)
                        {
                            colorMap[y * width + x] = regions[i].color;
                            break;
                        }
                    }
                }
            }
        }
        
        texture.SetPixels(colorMap);
        texture.Apply();


        textureRenderer.sharedMaterial.mainTexture = texture;
        //textureRenderer.transform.localScale = new Vector3(width, 1, height);
    }
}
