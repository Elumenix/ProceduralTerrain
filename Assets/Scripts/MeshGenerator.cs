using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

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
    private float[] altitudes;
    private int[] indices;
    private Vector2[] uvs;
    private float[,] noiseMap;

    // Compute Shader Data
    public ComputeShader erosionShader;
    private ComputeBuffer terrainBuffer;
    private ComputeBuffer altitudeBuffer;
    private ComputeBuffer rainDropBuffer;
    
    // Erosion Variables
    public int numRainDrops;

    private List<Vector3> points;

    [Range(0, 10)]
    public float accelMultiplier = 1.0f;
    [Range(0,1)]
    public float frictionMultiplier = .9f;
    
    // String search optimization
    private static readonly int MinHeight = Shader.PropertyToID("_MinHeight");
    private static readonly int MaxHeight = Shader.PropertyToID("_MaxHeight");
    private static readonly int VertexBuffer = Shader.PropertyToID("_VertexBuffer");
    private static readonly int NumVertices = Shader.PropertyToID("numVertices");
    private static readonly int Width = Shader.PropertyToID("width");
    private static readonly int AltitudeBuffer = Shader.PropertyToID("_AltitudeBuffer");
    private static readonly int RainDropBuffer = Shader.PropertyToID("RainDropBuffer");

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
        mesh.indexFormat = IndexFormat.UInt32; // Allows larger meshes
        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.uv = uvs;
        mesh.RecalculateNormals(); // Fixes Lighting
    }

    void CreateMesh()
    {
        // This was originally supposed to be two different methods, but it became much more efficient,
        // Albeit messy looking, to combine them to only loop through the map once
        
        // Variables for mesh deformation
        float widthScale = 1f / mapWidth;
        float heightScale = 1f / mapHeight;
        int size = (mapWidth + 1) * (mapHeight + 1);
        vertices = new Vector3[size];
        altitudes = new float[size];
        uvs = new Vector2[size];
        indices = new int[mapWidth * mapHeight * 2 * 3]; // What's needed to draw the mesh
        int num = 0;
        int indexNum = 0;

        // Trying to set the texture as perlin noise
        for (int x = 0; x <= mapWidth; x++)
        {
            for (int z = 0; z <= mapHeight; z++)
            {
                vertices[num] = new Vector3(x * widthScale,
                    heightCurve.Evaluate(noiseMap[x, z]) * heightMultiplier, z * heightScale);
                
                altitudes[num] = vertices[num][1];
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
        textureRenderer.sharedMaterial.SetFloat(MinHeight, 0);
        textureRenderer.sharedMaterial.SetFloat(MaxHeight, heightMultiplier);
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

    public void ComputeErosion()
    {
        points = new List<Vector3>();
        
        // PreDetermine raindrop positions
        uint[] rd = new uint[numRainDrops];
        int size = vertices.Length;
        for (int i = 0; i < numRainDrops; i++)
        {
            rd[i] = (uint)Random.Range(0, size);
        }
        
        /*
        // Manage Compute Buffers
        terrainBuffer = new ComputeBuffer(size, 12);
        terrainBuffer.SetData(vertices);
        altitudeBuffer = new ComputeBuffer(size, 4);
        altitudeBuffer.SetData(altitudes);
        rainDropBuffer = new ComputeBuffer(numRainDrops, 4);
        rainDropBuffer.SetData(rd);
        
        // Set necessary Data
        erosionShader.SetBuffer(0, VertexBuffer, terrainBuffer);
        erosionShader.SetBuffer(0, AltitudeBuffer, altitudeBuffer);
        erosionShader.SetBuffer(0, RainDropBuffer, rainDropBuffer);
        erosionShader.SetInt(NumVertices, size);
        erosionShader.SetInt(Width, mapWidth);
        erosionShader.SetVector("deltaParams",
            new Vector4(transform.localScale.x / mapWidth, transform.localScale.z / mapHeight, transform.localScale.x,
            transform.localScale.z));
        
        // Execute erosion shader
        erosionShader.Dispatch(0, Mathf.CeilToInt(numRainDrops / 1024f), 1, 1);
        
        // Copy new vertex data and apply
        terrainBuffer.GetData(vertices);
        altitudeBuffer.GetData(altitudes);
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        
        // Clean up
        terrainBuffer.Release();
        terrainBuffer.Release();
        altitudeBuffer.Release();
        rainDropBuffer.Release();*/

        foreach (uint i in rd)
        {
            SimulateDrop(i);
        }
        
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
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
        //float sediment = 0;
        float volume = 1;
        float2 velocity = new float2(0.0f, 0.0f);
        
        
        while (volume > 0)
        {
            // Get vertex
            uint i = GetClosestVertex(position);
            
            // Early Out
            if (i == UInt32.MaxValue) break;
            
            // for debugging
            points.Add(new Vector3(position.x * 100, vertices[i].y, position.y * 100));

            // Calculate normal
            float3 vertexNormal = GetVertexNormal(i);

            // Test erosion
            /*float3 vert = vertices[i];
            vert[1] -= .03f;
            vertices[i] = vert;*/
            volume -= .1f;

            // Acceleration
            velocity += new float2(vertexNormal.x,vertexNormal.z) * accelMultiplier;
            // Friction
            velocity *= (1 - frictionMultiplier);

            // Update Position
            position += velocity;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        if (points != null)
        {
            for (int i = 1; i < points.Count; i++)
            {
                Gizmos.DrawLine(points[i - 1], points[i]);
            }
        }
    }
}
