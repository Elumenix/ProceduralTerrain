using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

//[ExecuteInEditMode]
//[RequireComponent(typeof(MeshFilter))]
//[RequireComponent(typeof(MeshRenderer))]
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
    public int resolution;
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
    private int dim;
    private bool isGenerating = false;
    
    // Object reference variables
    //private Renderer textureRenderer;
    //private Mesh mesh;
    public Material meshCreator;

    // Compute Shader Data
    public ComputeShader meshGenShader;
    public ComputeShader erosionShader;
    public ComputeShader indexShader;
    private ComputeBuffer heightMap;
    private ComputeBuffer vertexDataBuffer;
    private ComputeBuffer indexBuffer;
    private List<ComputeBuffer> activeBuffers;
    private List<ComputeBuffer> pendingRelease;

    
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
    private static readonly int VertexDataBuffer = Shader.PropertyToID("_VertexDataBuffer");
    private static readonly int IndexBuffer = Shader.PropertyToID("_IndexBuffer");
    private static readonly int QuadWidth = Shader.PropertyToID("quadWidth");
    private static readonly int NumQuads = Shader.PropertyToID("numQuads");
    private static readonly int Resolution = Shader.PropertyToID("resolution");
    private static readonly int Scale = Shader.PropertyToID("scale");
    private static readonly int HeightMap = Shader.PropertyToID("_HeightMap"); 
    private static readonly int HeightBuffer = Shader.PropertyToID("_HeightBuffer");
    
    // String search optimization for Erosion
    private static readonly int NumVertices = Shader.PropertyToID("numVertices"); 
    private static readonly int MaxSediment = Shader.PropertyToID("maxSediment");
    private static readonly int DepositionRate = Shader.PropertyToID("depositionRate");
    private static readonly int Softness = Shader.PropertyToID("softness");
    private static readonly int EvaporationRate = Shader.PropertyToID("evaporationRate");
    private static readonly int Inertia = Shader.PropertyToID("inertia");
    private static readonly int Radius = Shader.PropertyToID("radius");
    private static readonly int Gravity = Shader.PropertyToID("gravity");
    private static readonly int MinSlope = Shader.PropertyToID("minSlope");
    private static readonly int Seed = Shader.PropertyToID("_seed");

    #endregion

    // Using this for inline methods
    public List<Slider> sliders;
    public Toggle erosionToggle;

    void Start()
    {
        activeBuffers = new List<ComputeBuffer>();
        pendingRelease = new List<ComputeBuffer>();
        Camera.main!.depthTextureMode = DepthTextureMode.Depth;
        // Set reference for gameObject to use the mesh we create here
        /*mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
        textureRenderer = GetComponent<MeshRenderer>();

        // Some Mesh Optimization
        mesh.MarkDynamic();
        mesh.indexFormat = IndexFormat.UInt32; // Allows larger meshes
        */
        
        // Hook up sliders to variables, I'm using inline functions because these are really simple and repetitive
        erosionToggle.onValueChanged.AddListener(val => { skipErosion = !val; isDirty = true; });
        sliders[0].onValueChanged.AddListener(val => { resolution = (int)val; isDirty = true; });
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
        sliders[11].onValueChanged.AddListener(val => { inertia = val; isDirty = true; });
        sliders[12].onValueChanged.AddListener(val => { sedimentMax = val; isDirty = true; });
        sliders[13].onValueChanged.AddListener(val => { depositionRate = val; isDirty = true; });
        sliders[14].onValueChanged.AddListener(val => { evaporationRate = val; isDirty = true; });
        sliders[15].onValueChanged.AddListener(val => { softness = 1 - val; isDirty = true; });
        sliders[16].onValueChanged.AddListener(val => { gravity = val; isDirty = true; });
        sliders[17].onValueChanged.AddListener(val => { radius = (int)val; isDirty = true; });
        sliders[18].onValueChanged.AddListener(val => { minSlope = val; isDirty = true; });

        
        // Draw with current data on frame 1
        isDirty = true;
    }

    private void Update()
    {
        // Generates map using current information if the map isn't up-to-date, or already in the middle of generating
        if (isDirty && !isGenerating)
        {
            GenerateMap();
        }
    }

    private void OnRenderObject()
    {
        if (vertexDataBuffer == null || indexBuffer == null) return;
        
        meshCreator.SetBuffer(VertexDataBuffer, vertexDataBuffer);
        meshCreator.SetBuffer(IndexBuffer, indexBuffer);
        meshCreator.SetPass(0);
        Graphics.DrawProceduralNow(MeshTopology.Triangles, indexBuffer.count, 1);
    }
    

    private void OnApplicationQuit()
    {
        // Clean up all buffers to prevent memory leaks
        foreach (ComputeBuffer buffer in activeBuffers)
        {
            buffer.Release();
        }
        
        foreach (ComputeBuffer buffer in pendingRelease)
        {
            buffer.Release();
        }
    }



    
    // Uv is being split up to facilitate proper byte alignment
    // Sends 32 bytes instead of 48, which is huge for how large this buffer will be
    [StructLayout(LayoutKind.Sequential)]
    struct VertexData
    {
        public Vector3 position;
        public float u;
        public Vector3 normal;
        public float v;
    }

    private void GenerateMap()
    {
        isGenerating = true;
        isDirty = false;
        
        // These will be used for all calls now on so that the player moving the slider won't affect late shader calls
        dim = resolution;
        mapLength = (resolution + 1) * (resolution + 1);
        
        // TODO: Check of random is even used in meshGenerator anymore
        // Creating new random reference so that I can batch random values
        _random = new Unity.Mathematics.Random((uint)seed);
        
        // Swap out old index and vertex buffers to be deleted
        pendingRelease.AddRange(activeBuffers);
        activeBuffers.Clear();
        
        // Step 1: Calculate a height map
        noise.ComputeHeightMap(dim + 1, _random, noiseScale, octaves, persistence,
            lacunarity, offset, (int) noiseType + 1, warpStrength, warpFrequency, smoothingPasses, heightMultiplier, pendingRelease, (map) =>
            {
                // Saving the compute buffer to the class
                heightMap = map;
                
                // Set Shared variable
                meshGenShader.SetInt(NumVertices, mapLength);
                erosionShader.SetInt(NumVertices, mapLength);
                meshGenShader.SetInt(Resolution, dim + 1);
                erosionShader.SetInt(Resolution, dim);
                
                // Step 2: Simulate Erosion
                // Raindrops will be simulated on the terrain. This directly modifies the heightMap
                ComputeErosion();
                
                // Step 3: Generate Indices
                // Needs to be done separate from mesh creation, and doesn't use heightMap, so it helps a bit with synchronization
                GenerateIndices();
                
                
                // Step 4: Generate Mesh Data
                // Creates a new buffer to hold mesh data that we'll use in the drawing shader. Only Reads the heightMap
                CreateMeshGPU();
        
                // Release all buffers that no longer need to be used
                foreach (ComputeBuffer buffer in pendingRelease)
                {
                    buffer.Release();
                }
                pendingRelease.Clear();
        
                // Confirm that a new map can be generated next frame if dirty
                isGenerating = false;
            }
        );
    }
    
    private void CreateMeshGPU()
    {
        // Pass distance between vertices to shader
        meshGenShader.SetFloat(Scale, 1.0f / dim);
        
        // This will hold vertices, uvs, and the modified heightmap
        vertexDataBuffer = new ComputeBuffer(mapLength, 32);
        vertexDataBuffer.SetData(new VertexData[mapLength]);
        activeBuffers.Add(vertexDataBuffer);
        
        meshGenShader.SetBuffer(0, VertexDataBuffer, vertexDataBuffer);
        meshGenShader.SetBuffer(0, HeightMap, heightMap);
        
        // Dispatch Shader
        meshGenShader.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
    }

    void GenerateIndices()
    {
        // Setup
        int numQuads = dim * dim;
        int numIndices = numQuads * 6;
        indexBuffer = new ComputeBuffer(numIndices, 4);
        indexBuffer.SetData(new int[numIndices]);
        activeBuffers.Add(indexBuffer);
        
        // Set Shader data
        indexShader.SetBuffer(0, IndexBuffer, indexBuffer);
        indexShader.SetInt(QuadWidth, dim);
        indexShader.SetInt(NumQuads, numQuads);
        
        // Dispatch Shader so that the indexBuffer is available for UpdateMesh
        indexShader.Dispatch(0, Mathf.CeilToInt(numQuads / 64.0f), 1, 1);
    }

    private void ComputeErosion()
    {
        // Buffer will throw error if size 0 
        if (numRainDrops == 0 || skipErosion)
        {
            return;
        }
        
        // Set Variables
        erosionShader.SetBuffer(0, HeightBuffer, heightMap);
        erosionShader.SetFloat(Inertia, inertia);
        erosionShader.SetFloat(MaxSediment, sedimentMax);
        erosionShader.SetFloat(DepositionRate, depositionRate);
        erosionShader.SetFloat(EvaporationRate, evaporationRate);
        erosionShader.SetFloat(Softness, softness);
        erosionShader.SetFloat(Gravity,gravity);
        erosionShader.SetFloat(MinSlope, minSlope);
        erosionShader.SetInt(Radius, radius); // 0 would be normal square
        erosionShader.SetInt(Seed, _random.NextInt());
        
        // Execute erosion shader
        erosionShader.Dispatch(0, Mathf.CeilToInt(numRainDrops / 64.0f), 1, 1);
    }
}
