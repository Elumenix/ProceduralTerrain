using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
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
    [Range(0,5)]
    public int smoothingPasses;
    public int seed;
    public Vector2 offset;
    //public TerrainType[] regions;
    public AnimationCurve heightCurve;
    public NoiseType noiseType;
    
    // Variables made to help with the async nature of the code
    private int[] dim;
    private bool isGenerating = false;
    
    // Object reference variables
    private Renderer textureRenderer;
    private Mesh mesh;
    
    // Mesh information
    private Vector3[] vertices;
    private Vector3[] normals;
    private int[] heights;
    private const int precision = 100000; // Precision to 3 decimal places
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
    [Range(0, .3f)] 
    public float evaporationRate = .2f;
    [Range(0,1)]
    public float softness = .1f;
    [Range(0,10)] 
    public float gravity;
    [Range(1, 10)] 
    public int radius;
    [Range(0, 0.1f)]
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

    public List<Slider> sliders;

    void Start()
    {
        Camera.main!.depthTextureMode = DepthTextureMode.Depth;
        // Set reference for gameObject to use the mesh we create here
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
        textureRenderer = GetComponent<MeshRenderer>();
        
        // Hook up sliders to variables, I'm using inline functions because these are really simple and repetitive
        sliders[0].onValueChanged.AddListener(val => { mapWidth = (int)val; mapHeight = (int) val; GenerateMap(); });
        sliders[1].onValueChanged.AddListener(val => { noiseType = (NoiseType)((int)val); GenerateMap(); });
        sliders[2].onValueChanged.AddListener(val => { noiseScale = val / 10.0f; GenerateMap(); });
        sliders[3].onValueChanged.AddListener(val => { heightMultiplier = val; GenerateMap(); });
        sliders[4].onValueChanged.AddListener(val => { octaves = (int)val; GenerateMap(); });
        sliders[5].onValueChanged.AddListener(val => { persistence = val; GenerateMap(); });
        sliders[6].onValueChanged.AddListener(val => { lacunarity = val; GenerateMap(); });
        sliders[7].onValueChanged.AddListener(val => { warpStrength = val; GenerateMap(); });
        sliders[8].onValueChanged.AddListener(val => { warpFrequency = val; GenerateMap(); });
        sliders[9].onValueChanged.AddListener(val => { smoothingPasses = (int)val; GenerateMap(); });
        
        GenerateMap();
    }
    
    // This is mainly for testing in edit mode, It shouldn't be called at runtime
    /*private void OnValidate()
    {
        Camera.main!.depthTextureMode = DepthTextureMode.Depth;
        if (mapWidth < 1) mapWidth = 1;
        if (mapHeight < 1) mapHeight = 1;
        if (heightMultiplier < 0) heightMultiplier = 0;
        if (noiseScale <= 0) noiseScale = 0.0011f;
        if (lacunarity < 1) lacunarity = 1;
    }*/

    public void GenerateMap()
    {
        if (isGenerating) return;
        isGenerating = true;
        
        // This will be used for all mapWidth/mapHeight calls from now on to prevent changing parameters messing up the map
        dim = new int[] {mapWidth, mapHeight};
        
        // Step 1: Calculate a height map
        noise.ComputeHeightMap(dim[0] + 1, dim[1] + 1, seed, noiseScale, octaves, persistence,
            lacunarity, offset, (int) noiseType + 1, warpStrength, warpFrequency, smoothingPasses, (heightmap) =>
            {
                heightMap = heightmap;
                
                // safety
                try
                {
                    // Set up Shared Buffers
                    // Pass map dimensions to shaders
                    dimension = new ComputeBuffer(2, 4);
                    dimension.SetData(dim);
                    meshGenShader.SetBuffer(0, Dimension, dimension);
                    erosionShader.SetBuffer(0, Dimension, dimension);
                    normalShader.SetBuffer(0, Dimension, dimension);


                    // Set Shared variable
                    int numVertices = (dim[0] + 1) * (dim[1] + 1);
                    meshGenShader.SetInt(NumVertices, numVertices);
                    erosionShader.SetInt(NumVertices, numVertices);
                    normalShader.SetInt(NumVertices, numVertices);

                    // Step 2: Create the Mesh
                    CreateMeshGPU(() =>
                    {
                        // Step 3: Simulate Erosion
                        ComputeErosion(() =>
                        {
                            // Step 4: Recalculate normals to make sure lighting works correctly
                            RecalculateNormals(() =>
                            {
                                // Step 5: Update mesh parameters so it can display
                                UpdateMesh();
                                dimension?.Release();
                                isGenerating = false;
                            });
                        });
                    });
                }
                catch
                {
                    // Release Shared Buffer
                    dimension?.Release();
                    isGenerating = false;
                }
            });
    }

    // The only reason this would be called is if GenerateMap was stopped early
    void RecalculateNormals(Action callback)
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
        normalShader.Dispatch(0, Mathf.CeilToInt(vertices.Length / 64.0f), 1, 1);
            
        // Save normals
        //normalBuffer.GetData(normals);
        
        // Async operation is necessary for Unity WebGPU builds
        AsyncGPUReadback.Request(normalBuffer, request =>
        {
            if (request.hasError)
            {
                Debug.LogError("Normal calculation failed");
            }
            else
            {
                normals = request.GetData<Vector3>().ToArray();
            }
            
            // Clean up and return
            vertexBuffer.Release();
            normalBuffer.Release();
            callback?.Invoke();
        });
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
        //textureRenderer.sharedMaterial.SetFloat(MinHeight, 0);
        //textureRenderer.sharedMaterial.SetFloat(MaxHeight, heightMultiplier);
    }
    
    void CreateMeshGPU(Action callback)
    {
        // Pass distance between vertices to shader
        float[] scale = {1f / dim[0], 1f / dim[1]};
        ComputeBuffer mapScale = new ComputeBuffer(2, 4);
        mapScale.SetData(scale);
        meshGenShader.SetBuffer(0, Scale, mapScale);

        // Pass precalculated heightmap to shader
        ComputeBuffer mapHeights = new ComputeBuffer(heightMap.Length, 4);
        mapHeights.SetData(heightMap);
        meshGenShader.SetBuffer(0, HeightMap, mapHeights);
        
        // number of vertices/uvs
        int size = (dim[0] + 1) * (dim[1] + 1);
        
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
        indices = new int[dim[0] * dim[1] * 6]; // 6 indices a square (two triangles)
        ComputeBuffer indexBuffer = new ComputeBuffer(indices.Length, 4);
        indexBuffer.SetData(indices);
        meshGenShader.SetBuffer(0, IndexBuffer, indexBuffer);
        
        // Pass Variables
        meshGenShader.SetFloat(HeightMultiplier, heightMultiplier);
        
        // Dispatch Shader
        meshGenShader.Dispatch(0, Mathf.CeilToInt(size / 64.0f), 1, 1);

        // Retrieve data: UpdateMesh() will use these values
        //vertexBuffer.GetData(vertices);
        //uvBuffer.GetData(uvs);
        //indexBuffer.GetData(indices);
        
        // Everything below this is meant to represent the 3 commented lines above

        int buffersToRead = 3;
        void finishedReading()
        {
            buffersToRead--;
            
            // If all buffers are handled, we are finished here
            if (buffersToRead == 0)
            {
                mapScale.Release();
                mapHeights.Release();
                
                // Begin tracking heights after the mesh is created so that they can be used in erosion
                heights = new int[size];
                for (int i = 0; i < size; i++)
                {
                    // Capture vertex heights at intended precision so that they can be used in erosion and drawing
                    heights[i] = (int)(vertices[i][1] * precision);
                }
                
                callback?.Invoke();
            }
        }
        
        // GPU ReadBack is required for Unity WebGPU
        AsyncGPUReadback.Request(vertexBuffer, request =>
        {
            if (request.hasError)
            {
                Debug.LogError("vertex readBack failed");
            }
            else
            {
                vertices = request.GetData<Vector3>().ToArray();
            }

            vertexBuffer.Release();
            finishedReading();
        });

        AsyncGPUReadback.Request(uvBuffer, request =>
        {
            if (request.hasError)
            {
                Debug.LogError("UV readBack failed");
            }
            else
            {
                uvs = request.GetData<Vector2>().ToArray();
            }
            
            uvBuffer.Release();
            finishedReading();
        });

        AsyncGPUReadback.Request(indexBuffer, request =>
        {
            if (request.hasError)
            {
                Debug.LogError("Index readBack failed");
            }
            else
            {
                indices = request.GetData<int>().ToArray();
            }
            
            indexBuffer.Release();
            finishedReading();
        });
    }


    public void ComputeErosion(Action callback)
    {
        // Buffer will throw error if size 0 
        if (numRainDrops == 0 || skipErosion)
        {
            callback?.Invoke();
            return;
        }
        
        
        // Reapplying seed for somewhat consistent results
        Random.InitState(seed);
        
        // Generate raindrop positions
        int[] rd = new int[numRainDrops];
        int size = vertices.Length;
        for (int i = 0; i < numRainDrops; i++)
        {
            // prevent vertices on the right or bottom edges
            rd[i] = Random.Range(0, size);
            
            // Prevents raindrop directly on the right or bottom edge of the map
            if (rd[i] % (dim[0]+1) == dim[0]  || rd[i] / (dim[0]+1) == dim[1])
            {
                i--;
            }
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
        erosionShader.SetInt(Radius, radius); // 0 would be normal square
        erosionShader.SetInt(Precision, precision);

        // Execute erosion shader
        erosionShader.Dispatch(0, Mathf.CeilToInt(numRainDrops / 64.0f), 1, 1);
        
        // Copy height data
        //heightBuffer.GetData(heights);
        
        AsyncGPUReadback.Request(heightBuffer, request =>
        {
            if (request.hasError)
            {
                Debug.LogError("heightBuffer readBack failed");
            }
            else
            {
                heights = request.GetData<int>().ToArray();
                
                // Transfer height data to vertices so that the mesh displays properly
                for (int i = 0; i < size; i++)
                {
                    vertices[i].y = (float)heights[i] / precision;
                }
            }
            
            // Clean up and return
            heightBuffer.Release();
            rainDropBuffer.Release();
            callback?.Invoke(); 
        });
    }
}
