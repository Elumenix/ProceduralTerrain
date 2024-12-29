using System;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

[ExecuteInEditMode]
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
    public float heightMultiplier;
    public float noiseScale;
    [Range(1,6)]
    public int octaves;
    [Range(0,1)]
    public float persistence;
    [Range(1,3)]
    public float lacunarity;
    public int seed;
    public Vector2 offset;
    //public TerrainType[] regions;
    public AnimationCurve heightCurve;
    
    // Object reference variables
    private Renderer textureRenderer;
    private Mesh mesh;
    
    // Mesh information
    private Vector3[] vertices;
    private int[] indices;
    private Vector2[] uvs;
    private float[,] noiseMap;
    
    // String search optimization
    private static readonly int MinHeight = Shader.PropertyToID("_MinHeight");
    private static readonly int MaxHeight = Shader.PropertyToID("_MaxHeight");

    void Start()
    {
        Camera.main!.depthTextureMode = DepthTextureMode.Depth;
        // Set reference for gameObject to use the mesh we create here
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
        textureRenderer = GetComponent<MeshRenderer>();
        
        GenerateMap();
    }

    private void OnValidate()
    {
        Camera.main!.depthTextureMode = DepthTextureMode.Depth;
        if (mapWidth < 1) mapWidth = 1;
        if (mapHeight < 1) mapHeight = 1;
        if (heightMultiplier < 0) heightMultiplier = 0;
        if (noiseScale <= 0) noiseScale = 0.0011f;
        if (lacunarity < 1) lacunarity = 1;
    }

    private void Update()
    {
        // I couldn't find a better way to reload the shader after shader graph saves
        if (!Application.isPlaying)
        {
            GenerateMap();
        }
    }

    public void GenerateMap()
    {
        noiseMap = Noise.GenerateNoiseMap(mapWidth + 1, mapHeight + 1, seed, noiseScale, octaves, persistence,
            lacunarity, offset);
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
        /*Texture2D texture = new(mapWidth, mapHeight)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };*/
        //Color[] colorMap = new Color[mapWidth * mapHeight];
        
        // Variables for mesh deformation
        float widthScale = 1f / mapWidth;
        float heightScale = 1f / mapHeight;
        vertices = new Vector3[(mapWidth + 1) * (mapHeight + 1)];
        uvs = new Vector2[(mapWidth + 1) * (mapHeight + 1)];
        indices = new int[mapWidth * mapHeight * 2 * 3];
        int num = 0;
        int indexNum = 0;

        // Trying to set the texture as perlin noise
        for (int x = 0; x <= mapWidth; x++)
        {
            for (int z = 0; z <= mapHeight; z++)
            {
                if (drawMode is DrawMode.heightMap or DrawMode.coloredHeightMap)
                {
                    vertices[num] = new Vector3(x * widthScale,
                        heightCurve.Evaluate(noiseMap[x, z]) * heightMultiplier, z * heightScale);
                }
                else
                {
                    vertices[num] = new Vector3(x * widthScale, 0, z * heightScale);
                }

                uvs[num] = new Vector2(x * widthScale, z * heightScale);
                num++;
                
                
                // Indices and color do not need to be updated for outer vertices
                if (x == mapWidth || z == mapHeight) continue;
                
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
                /*switch (drawMode)
                {
                    case DrawMode.noiseMap:
                    case DrawMode.heightMap:
                        //colorMap[z * mapWidth + x] = Color.Lerp(Color.black, Color.white, noiseMap[x, z]);
                        break;
                    /*case DrawMode.colorMap:
                    case DrawMode.coloredHeightMap:
                    {
                        float currentHeight = noiseMap[x, z];
                        for (int i = 0; i < regions.Length; i++)
                        {
                            if (!(currentHeight <= regions[i].height)) continue;
                            
                            colorMap[z * mapWidth + x] = regions[i].color;
                            break;
                        }
                        break;
                    }
                }*/
            }
        }
        
        //texture.SetPixels(colorMap);
        //texture.Apply();
        //textureRenderer.sharedMaterial.mainTexture = texture;
        textureRenderer.sharedMaterial.SetFloat(MinHeight, 0);
        textureRenderer.sharedMaterial.SetFloat(MaxHeight, heightMultiplier);
    }
}
