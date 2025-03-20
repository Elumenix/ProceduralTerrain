using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
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
    //private float[] altitudes;
    private int[] indices;
    private Vector2[] uvs;
    private float[,] noiseMap;
    private float[] heightMap;

    // Compute Shader Data
    public ComputeShader meshGenShader;
    public ComputeShader erosionShader;
    public ComputeBuffer dimension;


    
    // Erosion Variables
    public int numRainDrops;
    [Range(0, 1)]
    public float inertia = 1.0f;
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
    private static readonly int IndexBuffer = Shader.PropertyToID("_IndexBuffer");
    private static readonly int HeightMultiplier = Shader.PropertyToID("heightMultiplier");
    
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
            lacunarity, offset, (int) noiseType + 1);
        
        try
        {
            // Set up Shared Buffers
            // Pass map dimensions to shaders
            int[] dim = {mapWidth, mapHeight};
            dimension = new ComputeBuffer(2, 4);
            dimension.SetData(dim);
            meshGenShader.SetBuffer(0, Dimension, dimension);
            erosionShader.SetBuffer(0, Dimension, dimension);

            // Set Shared variable
            int numVertices = (mapWidth + 1) * (mapHeight + 1);
            meshGenShader.SetInt(NumVertices, numVertices);
            erosionShader.SetInt(NumVertices, numVertices);

            // Create Mesh
            CreateMeshGPU();

            // Reapplying seed for somewhat consistent results
            Random rng = new Random(seed);

            // Erode
            for (int i = 0; i < 1; i++)
            {
                ComputeErosion(rng);
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

    void UpdateMesh()
    {
        mesh.Clear();
        mesh.indexFormat = IndexFormat.UInt32; // Allows larger meshes
        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.uv = uvs;
        mesh.RecalculateNormals(); // Fixes Lighting
        
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
        ComputeBuffer heights = new ComputeBuffer(heightMap.Length, 4);
        heights.SetData(heightMap);
        meshGenShader.SetBuffer(0, HeightMap, heights);
        
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
        heights.Release();
        vertexBuffer.Release();
        uvBuffer.Release();
        indexBuffer.Release();
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
        // Buffer will throw error if this goes through
        if (numRainDrops == 0) return;
        
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
        ComputeBuffer terrainBuffer = new(size, 12);
        terrainBuffer.SetData(vertices);
        ComputeBuffer rainDropBuffer = new(numRainDrops, 4);
        rainDropBuffer.SetData(rd);
        
        // Set Buffers
        erosionShader.SetBuffer(0, VertexBuffer, terrainBuffer);
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
        
        // Execute erosion shader
        erosionShader.Dispatch(0, Mathf.CeilToInt(numRainDrops / 1024f), 1, 1);
        
        // Copy new vertex data, will be used in update mesh
        terrainBuffer.GetData(vertices);
        
        // Clean up
        terrainBuffer.Release();
        rainDropBuffer.Release();
    }
    
    
    float3 GetVertexNormal(uint v)
    {
        // CPU variables
        int width = mapWidth + 1;
        uint x = (uint)(v % (width));
        uint y = (uint)((v - x) / (width));
        
        float3 normalSum = new float3(0.0f, 0.0f, 0.0f);
        float3 AB, AC, faceNormal;
        
        
        // Top Left triangle
        if (x > 0 && y > 0)
        {
            AB = vertices[v - 1] - vertices[v];
            AC = vertices[v - width] - vertices[v];
            faceNormal = Vector3.Normalize(Vector3.Cross(AB, AC));
            normalSum += faceNormal;
        }
        
        // Top Middle and Top Right triangles
        if (x < mapWidth && y > 0)
        {
            // Top Middle
            AB = vertices[v - width] - vertices[v];
            AC = vertices[v - width + 1] - vertices[v];
            faceNormal = Vector3.Normalize(Vector3.Cross(AB, AC));
            normalSum += faceNormal;
            
            // Top Right
            AB = vertices[v + 1] - vertices[v];
            faceNormal = Vector3.Normalize(Vector3.Cross(AC, AB));
            normalSum += faceNormal;
        }
        
        // Bottom Right triangle
        if (x < mapWidth && y < mapHeight)
        {
            AB = vertices[v + 1] - vertices[v];
            AC = vertices[v + width + 1] - vertices[v];
            faceNormal = Vector3.Normalize(Vector3.Cross(AB, AC));
            normalSum += faceNormal;
        }

        // Bottom Middle and Bottom Left triangles
        if (x > 0 && y < mapHeight)
        {
            // Bottom Middle
            AB = vertices[v + width] - vertices[v];
            AC = vertices[v + width - 1] - vertices[v];
            faceNormal = Vector3.Normalize(Vector3.Cross(AB, AC));
            normalSum += faceNormal;
            
            // Bottom Left
            AB = vertices[v - 1] - vertices[v];
            faceNormal = Vector3.Normalize(Vector3.Cross(AC, AB));
            normalSum += faceNormal;
        }

        
        // hlsl internally safeguards against division by 0
        return Vector3.Normalize(normalSum);
    }

    uint GetClosestVertex(float2 pos)
    {
        // Rounding takes us from face position to nearest vertex row/column
        int x = Mathf.RoundToInt(pos.x * mapWidth);
        int y = Mathf.RoundToInt(pos.y * mapHeight);

        // We're off the map
        if (x < 0 || x > mapWidth || y < 0 || y > mapHeight)
        {
            return UInt32.MaxValue;
        }

        // Correct vertex
        return (uint)(x * (mapHeight + 1) + y);
    }


    void SimulateDrop(uint v) 
    {
        // ReSharper disable once PossibleLossOfFraction
        float2 position = new float2(vertices[v].x, vertices[v].z);
        float sediment = 0;
        float volume = 1;
        float2 velocity = new float2(0.0f, 0.0f);
        
        
        while (volume > 0)
        {
            // Get vertex
            uint i = GetClosestVertex(position);
            
            // Early Out
            if (i == UInt32.MaxValue) break;
            
            // for debugging
            //points.Add(new Vector3(position.x * 100, vertices[i].y, position.y * 100));

            // Calculate normal
            float3 vertexNormal = GetVertexNormal(i);
            
            // Max Sediment a particle can hold
            float max = sedimentMax * math.length(velocity) * volume;
            //Debug.Log("Max: " + max);
            float3 vert = vertices[i];
            
            if (sediment > max)
            {
                // Deposit sediment to terrain
                float depositAmount = depositionRate * (sediment - max);
                sediment -= depositAmount;
                
                //Debug.Log("Depositing " + depositAmount);
                
                vert[1] += depositAmount;
            }
            else
            {
                // Take sediment from the terrain
                float erosionAmount = softness * (max - sediment);
                sediment += erosionAmount;
                
                //Debug.Log("Taking " + erosionAmount);
                
                vert[1] -= erosionAmount;
            }
            vertices[i] = vert;
            
            //Debug.Log("New Sediment Amount: " + sediment);
            
            // Evaporation
            volume -= .01f;

            // Update velocity based on normals
            float2 dir = new float2(-vertexNormal.x, -vertexNormal.z);
            velocity = velocity * inertia - dir * (1-inertia);

            // Update Position
            position += velocity;
        }
    }
}
