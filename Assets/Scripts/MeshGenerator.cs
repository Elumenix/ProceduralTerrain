using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;
// ReSharper disable ArrangeObjectCreationWhenTypeEvident

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

    public enum NoiseType
    {
        Perlin,
        Simplex,
        Worley
    }
    
    // Variables Changeable within the editor
    public Noise noise;
    public DrawMode drawMode;
    public int mapWidth;
    public int mapHeight;
    public float heightMultiplier;
    public float noiseScale;
    [Range(1,10)]
    public int octaves;
    [Range(0,1)]
    public float persistence;
    [Range(1,3)]
    public float lacunarity;
    [Range(0,10)]
    public float warpStrength;
    [Range(0,5)]
    public float warpFrequency;
    public int seed;
    public Vector2 offset;
    //public TerrainType[] regions;
    public AnimationCurve heightCurve;
    public NoiseType noiseType;
    
    // Object reference variables
    private Renderer textureRenderer;
    private Mesh mesh;
    
    // Mesh information
    private Vector3[] vertices;
    private Vector3[] normals;
    private int[] heights;
    private const int precision = 1000; // Precision to 3 decimal places
    private int[] indices;
    private Vector2[] uvs;
    private float[,] noiseMap;
    private float[] heightMap;

    // Compute Shader Data
    public ComputeShader meshGenShader;
    public ComputeShader erosionShader;
    public ComputeShader normalShader;
    public ComputeBuffer dimension;

    
    // Erosion Variables
    public bool skipErosion = false;
    public int numRainDrops;
    [Range(0, .999f)]
    public float inertia = .999f;
    [Range(0,32)]
    public float sedimentMax = .1f;
    [Range(0,1)]
    public float depositionRate = .25f;
    [Range(.005f, .05f)] 
    public float evaporationRate = .2f;
    [Range(0,1)]
    public float softness = .1f;
    [Range(0,10)] 
    public float gravity;
    [Range(1, 10)] 
    public int radius;
    [Range(0, 1)]
    public float minSlope;

    #region StringSearchOptimization
    // String search optimization for material shader properties
    private static readonly int MinHeight = Shader.PropertyToID("_MinHeight");
    private static readonly int MaxHeight = Shader.PropertyToID("_MaxHeight");
    
    // String search optimization for Mesh Creation
    private static readonly int Dimension = Shader.PropertyToID("_Dimension");
    private static readonly int Scale = Shader.PropertyToID("_Scale");
    private static readonly int HeightMap = Shader.PropertyToID("_HeightMap");
    private static readonly int UVBuffer = Shader.PropertyToID("_UVBuffer");
    private static readonly int VertexBuffer = Shader.PropertyToID("_VertexBuffer");
    private static readonly int HeightBuffer = Shader.PropertyToID("_HeightBuffer");
    private static readonly int IndexBuffer = Shader.PropertyToID("_IndexBuffer");
    private static readonly int HeightMultiplier = Shader.PropertyToID("heightMultiplier");
    private static readonly int NormalBuffer = Shader.PropertyToID("_NormalBuffer");
    
    // String search optimization for Erosion
    private static readonly int NumVertices = Shader.PropertyToID("numVertices"); 
    private static readonly int RainDropBuffer = Shader.PropertyToID("_RainDropBuffer");
    private static readonly int MaxSediment = Shader.PropertyToID("maxSediment");
    private static readonly int DepositionRate = Shader.PropertyToID("depositionRate");
    private static readonly int Softness = Shader.PropertyToID("softness");
    private static readonly int EvaporationRate = Shader.PropertyToID("evaporationRate");
    private static readonly int Inertia = Shader.PropertyToID("inertia");
    private static readonly int Radius = Shader.PropertyToID("radius");
    private static readonly int Gravity = Shader.PropertyToID("gravity");
    private static readonly int MinSlope = Shader.PropertyToID("minSlope");
    private static readonly int Precision = Shader.PropertyToID("precision");

    #endregion

    void Start()
    {
        Camera.main!.depthTextureMode = DepthTextureMode.Depth;
        // Set reference for gameObject to use the mesh we create here
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
        textureRenderer = GetComponent<MeshRenderer>();
        StartCoroutine(GenerateMap());
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

    public IEnumerator GenerateMap()
    {
        // Update heightMap
        heightMap = noise.ComputeHeightMap(mapWidth + 1, mapHeight + 1, seed, noiseScale, octaves, persistence,
            lacunarity, offset, (int) noiseType + 1, warpStrength, warpFrequency);
        
        try
        {
            // Set up Shared Buffers
            // Pass map dimensions to shaders
            int[] dim = {mapWidth, mapHeight};
            dimension = new ComputeBuffer(2, 4);
            dimension.SetData(dim);
            meshGenShader.SetBuffer(0, Dimension, dimension);
            erosionShader.SetBuffer(0, Dimension, dimension);
            normalShader.SetBuffer(0, Dimension, dimension);


            // Set Shared variable
            int numVertices = (mapWidth + 1) * (mapHeight + 1);
            meshGenShader.SetInt(NumVertices, numVertices);
            erosionShader.SetInt(NumVertices, numVertices);
            normalShader.SetInt(NumVertices, numVertices);

            // Create Mesh
            CreateMeshGPU();

            // Reapplying seed for somewhat consistent results
            Random rng = new Random(seed);

            // Erode
            for (int i = 0; i < 1; i++)
            {
                ComputeErosion(rng);
                RecalculateNormals();
                UpdateMesh();
                yield return 0;
            }
        }
        finally
        {
            // Release Shared Buffer
            dimension?.Release();
        }
    }

    // The only reason this would be called is if GenerateMap was stopped early
    void RecalculateNormals()
    {
        // Now that all vertices are in their final positions, we want to calculate the normals of the mesh ourselves
        // This is because unity's innate RecalculateMeshNormals() isn't tuned for the sometimes steep slopes of 
        // terrain and causes visible artifacts. It's also likely faster to iterate over large meshes on the gpu.
        ComputeBuffer vertexBuffer = new ComputeBuffer(vertices.Length, 12);
        vertexBuffer.SetData(vertices);
        ComputeBuffer normalBuffer = new ComputeBuffer(vertices.Length, 12);
        normals = new Vector3[vertices.Length];
        normalBuffer.SetData(normals);
        
        normalShader.SetBuffer(0, VertexBuffer, vertexBuffer);
        normalShader.SetBuffer(0, NormalBuffer, normalBuffer);
            
        // Dispatch normalShader
        normalShader.Dispatch(0, Mathf.CeilToInt(vertices.Length / 1024.0f), 1, 1);
            
        // Save normals
        normalBuffer.GetData(normals);
            
        // Clean up for normalShader
        vertexBuffer.Release();
        normalBuffer.Release();
    }

    public void UpdateMesh()
    {
        mesh.Clear();
        mesh.indexFormat = IndexFormat.UInt32; // Allows larger meshes
        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.uv = uvs;
        //mesh.RecalculateNormals(); // Fixes Lighting
        mesh.normals = normals;
        
        // Update Materials
        textureRenderer.sharedMaterial.SetFloat(MinHeight, 0);
        textureRenderer.sharedMaterial.SetFloat(MaxHeight, heightMultiplier);
    }

    void CreateMeshCPU()
    {
        noiseMap = Noise.GenerateNoiseMap(mapWidth + 1, mapHeight + 1, seed, noiseScale, octaves, persistence,
            lacunarity, offset);
        
        // This was originally supposed to be two different methods, but it became much more efficient,
        // Albeit messy looking, to combine them to only loop through the map once
        
        // Variables for mesh deformation
        float widthScale = 1f / mapWidth;
        float heightScale = 1f / mapHeight;
        int size = (mapWidth + 1) * (mapHeight + 1);
        vertices = new Vector3[size];
        heights = new int[size];
        uvs = new Vector2[size];
        indices = new int[mapWidth * mapHeight * 2 * 3]; // What's needed to draw the mesh
        int indexNum = 0;
        int num = 0;

        
        // Trying to set the texture as perlin noise
        for (int x = 0; x <= mapWidth; x++)
        {
            for (int z = 0; z <= mapHeight; z++)
            {
                vertices[num] = new Vector3(x * widthScale,
                    heightCurve.Evaluate(noiseMap[x,z]) * heightMultiplier, z * heightScale);
                
                heights[num] = (int) (vertices[num][1] * precision); // Get y value at set precision
                
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
            }
        }
    }

    void CreateMeshGPU()
    {
        // Pass distance between vertices to shader
        float[] scale = {1f / mapWidth, 1f / mapHeight};
        ComputeBuffer mapScale = new ComputeBuffer(2, 4);
        mapScale.SetData(scale);
        meshGenShader.SetBuffer(0, Scale, mapScale);

        // Pass precalculated heightmap to shader
        ComputeBuffer mapHeights = new ComputeBuffer(heightMap.Length, 4);
        mapHeights.SetData(heightMap);
        meshGenShader.SetBuffer(0, HeightMap, mapHeights);
        
        // number of vertices/uvs
        int size = (mapWidth + 1) * (mapHeight + 1);
        
        // Create Buffer to hold vertices
        vertices = new Vector3[size];
        ComputeBuffer vertexBuffer = new ComputeBuffer(size, 12);
        vertexBuffer.SetData(vertices);
        meshGenShader.SetBuffer(0, VertexBuffer, vertexBuffer);
        
        // Create Buffer to hold UVs
        uvs = new Vector2[size];
        ComputeBuffer uvBuffer = new ComputeBuffer(size, 8);
        uvBuffer.SetData(uvs);
        meshGenShader.SetBuffer(0, UVBuffer, uvBuffer);
        
        // Create Buffer to hold indices
        indices = new int[mapWidth * mapHeight * 6]; // 6 indices a square (two triangles)
        ComputeBuffer indexBuffer = new ComputeBuffer(indices.Length, 4);
        indexBuffer.SetData(indices);
        meshGenShader.SetBuffer(0, IndexBuffer, indexBuffer);
        
        // Pass Variables
        meshGenShader.SetFloat(HeightMultiplier, heightMultiplier);
        
        // Dispatch Shader
        meshGenShader.Dispatch(0, Mathf.CeilToInt(size / 1024.0f), 1, 1);

        // Retrieve data: UpdateMesh() will use these values
        vertexBuffer.GetData(vertices);
        uvBuffer.GetData(uvs);
        indexBuffer.GetData(indices);
        
        // Release Buffers
        mapScale.Release();
        mapHeights.Release();
        vertexBuffer.Release();
        uvBuffer.Release();
        indexBuffer.Release();

        heights = new int[size];
        for (int i = 0; i < size; i++)
        {
            // Capture vertex heights at intended precision so that they can be used in erosion and drawing
            heights[i] = (int)(vertices[i][1] * precision);
        }
    }

    // Eventually refactor the following to be used to change draw modes. Leverage altitude list for this
    void DrawMesh()
    {
        // Variables for texturing
        /*Texture2D texture = new(mapWidth, mapHeight)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };*/
        //Color[] colorMap = new Color[mapWidth * mapHeight];
        
        
        
        
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
        
        
        //texture.SetPixels(colorMap);
        //texture.Apply();
        //textureRenderer.sharedMaterial.mainTexture = texture;
    }

    public void ComputeErosion(Random rng)
    {
        // Buffer will throw error if size 0 
        if (numRainDrops == 0 || skipErosion) return;
        
        // Generate raindrop positions
        int[] rd = new int[numRainDrops];
        int size = vertices.Length;
        for (int i = 0; i < numRainDrops; i++)
        {
            // prevent vertices on the right or bottom edges
            rd[i] = rng.Next(0, size - mapWidth);
            if (rd[i] % (mapWidth + 1) == mapWidth) i--;
        }
        
        // Manage Compute Buffers
        ComputeBuffer heightBuffer = new(size, 4);
        heightBuffer.SetData(heights);
        ComputeBuffer rainDropBuffer = new(numRainDrops, 4);
        rainDropBuffer.SetData(rd);
        
        // Set Buffers
        erosionShader.SetBuffer(0, HeightBuffer, heightBuffer);
        erosionShader.SetBuffer(0, RainDropBuffer, rainDropBuffer);
        
        // Set Variables
        erosionShader.SetFloat(Inertia, inertia);
        erosionShader.SetFloat(MaxSediment, sedimentMax);
        erosionShader.SetFloat(DepositionRate, depositionRate);
        erosionShader.SetFloat(EvaporationRate, evaporationRate);
        erosionShader.SetFloat(Softness, softness);
        erosionShader.SetFloat(Gravity,gravity);
        erosionShader.SetFloat(MinSlope, minSlope);
        erosionShader.SetInt(Radius, radius - 1); // 0 would be normal square
        erosionShader.SetInt(Precision, precision);
        
        // Execute erosion shader
        erosionShader.Dispatch(0, Mathf.CeilToInt(numRainDrops / 1024f), 1, 1);
        
        // Copy height data
        heightBuffer.GetData(heights);
        
        // Clean up
        heightBuffer.Release();
        rainDropBuffer.Release();
        
        // Transfer height data to vertices so that the mesh displays properly
        for (int i = 0; i < size; i++)
        {
            vertices[i].y = (float)heights[i] / precision;
        }
    }
}
