using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
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
    public float2 offset;
    //public TerrainType[] regions;
    public AnimationCurve heightCurve;
    public NoiseType noiseType;
    private Unity.Mathematics.Random _random;
    [HideInInspector]
    public bool isDirty;
    private int mapLength;
    
    // Variables made to help with the async nature of the code
    private int[] dim;
    private bool isGenerating = false;
    
    // Object reference variables
    private Renderer textureRenderer;
    private Mesh mesh;
    
    // Mesh information
    private Vector3[] vertices;
    private Vector3[] normals;
    //private int[] heights;
    //private const int precision = 1000; // Precision to 3 decimal places
    private int[] indices;
    private Vector2[] uvs;
    private float[,] noiseMap;
    //private float[] heightMap;

    // Compute Shader Data
    public ComputeShader meshGenShader;
    public ComputeShader erosionShader;
    public ComputeShader normalShader;
    private ComputeBuffer heightMap;
    private ComputeBuffer vertexBuffer;

    
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
    private static readonly int Dimension = Shader.PropertyToID("dimension");
    private static readonly int Scale = Shader.PropertyToID("scale");
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
    //private static readonly int Precision = Shader.PropertyToID("precision");
    private static readonly int Seed = Shader.PropertyToID("_seed");

    #endregion

    // Using this for inline methods
    public List<Slider> sliders;
    private static readonly int VertexDataBuffer = Shader.PropertyToID("_VertexDataBuffer");

    void Start()
    {
        Camera.main!.depthTextureMode = DepthTextureMode.Depth;
        // Set reference for gameObject to use the mesh we create here
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
        textureRenderer = GetComponent<MeshRenderer>();
        
        // Hook up sliders to variables, I'm using inline functions because these are really simple and repetitive
        sliders[0].onValueChanged.AddListener(val => { mapWidth = (int)val; mapHeight = (int) val; isDirty = true; });
        sliders[1].onValueChanged.AddListener(val => { noiseType = (NoiseType)((int)val); isDirty = true; });
        sliders[2].onValueChanged.AddListener(val => { noiseScale = val / 10.0f; isDirty = true; });
        sliders[3].onValueChanged.AddListener(val => { heightMultiplier = val; isDirty = true; });
        sliders[4].onValueChanged.AddListener(val => { octaves = (int)val; isDirty = true; });
        sliders[5].onValueChanged.AddListener(val => { persistence = val; isDirty = true; });
        sliders[6].onValueChanged.AddListener(val => { lacunarity = val; isDirty = true; });
        sliders[7].onValueChanged.AddListener(val => { warpStrength = val; if (warpFrequency != 0) {isDirty = true;} });
        sliders[8].onValueChanged.AddListener(val => { warpFrequency = val; if (warpStrength != 0) {isDirty = true;} });
        sliders[9].onValueChanged.AddListener(val => { smoothingPasses = (int)val; isDirty = true; });
        sliders[10].onValueChanged.AddListener(val => { numRainDrops = (int)val; isDirty = true; });

        
        isDirty = true;
    }

    private void Update()
    {
        // Generates map using current information if the map isn't up-to-date, or already in the middle of generating
        if (isDirty && !isGenerating)
        {
            GenerateMap();
            isDirty = false;
        }
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
        isGenerating = true;
        
        // These will be used for all calls now on so that the player moving the slider won't affect late shader calls
        dim = new int[] {mapWidth, mapHeight};
        mapLength = (mapWidth + 1) * (mapHeight + 1);
        
        // TODO: Check of random is even used in meshGenerator anymore
        // Creating new random reference so that I can batch random values
        _random = new Unity.Mathematics.Random((uint)seed);
        
        
        // Step 1: Calculate a height map
        noise.ComputeHeightMap(dim[0] + 1, dim[1] + 1, _random, noiseScale, octaves, persistence,
            lacunarity, offset, (int) noiseType + 1, warpStrength, warpFrequency, smoothingPasses, (map) =>
            {
                // Saving the compute buffer to the class
                heightMap = map;
                vertexBuffer = new ComputeBuffer(mapLength, 12);
                
                // Set Shared variable
                meshGenShader.SetInt(NumVertices, mapLength);
                erosionShader.SetInt(NumVertices, mapLength);
                normalShader.SetInt(NumVertices, mapLength);
                meshGenShader.SetInts(Dimension, dim);
                erosionShader.SetInts(Dimension, dim);
                normalShader.SetInts(Dimension, dim);

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
                            isGenerating = false;
                        });
                    });
                });
            }
        );
    }

    // The only reason this would be called is if GenerateMap was stopped early
    void RecalculateNormals(Action callback)
    {
        // Now that all vertices are in their final positions, we want to calculate the normals of the mesh ourselves
        // This is because unity's innate RecalculateMeshNormals() isn't tuned for the sometimes steep slopes of 
        // terrain and causes visible artifacts. It's also likely faster to iterate over large meshes on the gpu.
        ComputeBuffer normalBuffer = new ComputeBuffer(mapLength, 12);
        normals = new Vector3[mapLength];
        normalBuffer.SetData(normals);
        
        normalShader.SetBuffer(0, VertexBuffer, vertexBuffer);
        normalShader.SetBuffer(0, NormalBuffer, normalBuffer);
            
        // Dispatch normalShader
        normalShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
            
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
                request.GetData<Vector3>().CopyTo(normals);
            }
            
            // Clean up and return
            callback?.Invoke();
            vertexBuffer.Release();
            normalBuffer.Release();
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

    [StructLayout(LayoutKind.Sequential)]
    struct VertexData
    {
        public Vector3 position;
        public Vector2 uv;
    }
    
    void CreateMeshGPU(Action callback)
    {
        // Pass distance between vertices to shader
        float[] scale = {1f / dim[0], 1f / dim[1]};
        meshGenShader.SetFloats(Scale, scale);
        
        
        // number of vertices/uvs
        VertexData[] data = new VertexData[mapLength];
        
        // This will hold vertices, uvs, and the modified heightmap
        ComputeBuffer vertexDataBuffer = new ComputeBuffer(mapLength, 20);
        vertexDataBuffer.SetData(data);
        meshGenShader.SetBuffer(0, VertexDataBuffer, vertexDataBuffer);
        meshGenShader.SetBuffer(0, HeightMap, heightMap);
        
        
        //vertices = new Vector3[size];
        //uvs = new Vector2[size];
        indices = new int[dim[0] * dim[1] * 6]; // 6 indices a square (two triangles)
        
        // TODO: Would it be better to pass this to noise compute shaders rather than mesh creation?
        // Pass Variables
        meshGenShader.SetFloat(HeightMultiplier, heightMultiplier);
        
        // Dispatch Shader
        meshGenShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
        
        // Normally I would do indices in the gpu, but now that this is async I need to limit it to only using one buffer
        // Indices don't perfectly map to vertices because of the edges, so I'm doing them in the cpu instead
        // Compute indices in cpu while gpu works
        int indexNum = 0;
        for (int i = 0; i < dim[1]; i++)
        {
            for (int j = 0; j < dim[0]; j++)
            {
                int rowOffset = i * (dim[0] + 1);
                // Top-left Triangle
                indices[indexNum]     = j + rowOffset;
                indices[indexNum + 1] = j + dim[0] + 1 + rowOffset;
                indices[indexNum + 2] = j + 1 + rowOffset;
                
                // Bottom-right Triangle
                indices[indexNum + 3] = j + 1 + rowOffset;
                indices[indexNum + 4] = j + dim[0] + 1 + rowOffset;
                indices[indexNum + 5] = j + dim[0] + 2 + rowOffset;

                indexNum += 6;
            }
        }
        
        AsyncGPUReadback.Request(vertexDataBuffer, request =>
        {
            if (request.hasError)
            {
                Debug.LogError("Height readBack failed");
            }
            else
            {
                request.GetData<VertexData>().CopyTo(data);

                vertices = data.Select(v => v.position).ToArray();
                uvs = data.Select(v => v.uv).ToArray();
                vertexBuffer.SetData(vertices);
            }
            
            // Continue to next step
            callback?.Invoke();
            
            // Clean up buffers
            vertexDataBuffer.Release();
            heightMap.Release();
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
        
        erosionShader.SetBuffer(0, HeightBuffer, heightMap);
        erosionShader.SetBuffer(0, VertexBuffer, vertexBuffer);
        
        // Set Variables
        erosionShader.SetFloat(Inertia, inertia);
        erosionShader.SetFloat(MaxSediment, sedimentMax);
        erosionShader.SetFloat(DepositionRate, depositionRate);
        erosionShader.SetFloat(EvaporationRate, evaporationRate);
        erosionShader.SetFloat(Softness, softness);
        erosionShader.SetFloat(Gravity,gravity);
        erosionShader.SetFloat(MinSlope, minSlope);
        erosionShader.SetInt(Radius, radius); // 0 would be normal square
        erosionShader.SetInt(Seed, _random.NextInt());
        //erosionShader.SetInt(Precision, precision);

        // Execute erosion shader
        erosionShader.Dispatch(0, Mathf.CeilToInt(numRainDrops / 64.0f), 1, 1);
        
        // Copy height data
        //heightBuffer.GetData(heights);
        
        AsyncGPUReadback.Request(vertexBuffer, request =>
        {
            if (request.hasError)
            {
                Debug.LogError("heightBuffer readBack failed");
            }
            else
            {
                request.GetData<Vector3>().CopyTo(vertices);
            }
            
            callback?.Invoke(); 
        });
    }
}
