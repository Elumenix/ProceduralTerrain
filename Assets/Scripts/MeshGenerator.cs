using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public class MeshGenerator : MonoBehaviour
{
    // Variables Changeable within the editor
    public Noise noise;
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
    public AnimationCurve heightCurve;
    public NoiseType noiseType;
    private Unity.Mathematics.Random _random;
    [HideInInspector]
    public bool isMeshDirty;
    [HideInInspector]
    public bool isErosionDirty;
    [HideInInspector]
    public float angle;
    private static readonly Vector3 rotOffset = new Vector3(50, 0, 50);
    private bool showNoiseMap;


    // Variables made to help with the async nature of the code
    private int dim;
    private bool isGenerating;
    private int mapLength;   
    
    // Object reference variables
    public Material meshCreator;
    public Material noiseMaterial;
    public Material waterMaterial;

    // Compute Shader Data
    public ComputeShader meshGenShader;
    public ComputeShader erosionShader;
    public ComputeShader copyComputeBuffer;
    private ComputeBuffer heightMap;
    private ComputeBuffer vertexDataBuffer;
    private ComputeBuffer indexBuffer;
    private ComputeBuffer minMaxBuffer;
    private ComputeBuffer savedHeightMap;
    private ComputeBuffer brushStencil;
    private List<ComputeBuffer> activeBuffers;
    private List<(int frame, ComputeBuffer buffer)> pendingRelease;
    private readonly Bounds meshBounds = new Bounds(new Vector3(50, 0, 50), Vector3.one * 100);
    private List<int2> brush;
    
    // Erosion Variables
    public bool skipErosion;
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
    [Range(0, 48)] 
    public int steps = 32;
    
    #region StringSearchOptimization
    // String search optimization for material shader properties
    private static readonly int MinMaxBuffer = Shader.PropertyToID("_MinMaxBuffer");
    private static readonly int MaxGrassHeight = Shader.PropertyToID("_MaxGrassHeight");
    private static readonly int Threshold = Shader.PropertyToID("_Threshold");
    private static readonly int BlendFactor = Shader.PropertyToID("_BlendFactor");
    private static readonly int Rotation = Shader.PropertyToID("_Rotation");
    private static readonly int WaterHeight = Shader.PropertyToID("_WaterHeight");
    private static readonly int Depth = Shader.PropertyToID("_Depth");
    private static readonly int WaterEnabled = Shader.PropertyToID("_WaterEnabled");
    private static readonly int Hide = Shader.PropertyToID("_Hide");
    
    // String search optimization for Mesh Creation
    private static readonly int NumVertices = Shader.PropertyToID("numVertices"); 
    private static readonly int VertexDataBuffer = Shader.PropertyToID("_VertexDataBuffer");
    private static readonly int IndexBuffer = Shader.PropertyToID("_IndexBuffer");
    private static readonly int QuadWidth = Shader.PropertyToID("quadWidth");
    private static readonly int NumQuads = Shader.PropertyToID("numQuads");
    private static readonly int Resolution = Shader.PropertyToID("resolution");
    private static readonly int Scale = Shader.PropertyToID("scale");
    private static readonly int HeightMap = Shader.PropertyToID("_HeightMap"); 
    private static readonly int HeightBuffer = Shader.PropertyToID("_HeightBuffer");
    private static readonly int SourceBuffer = Shader.PropertyToID("_SourceBuffer");
    private static readonly int DestinationBuffer = Shader.PropertyToID("_DestinationBuffer");
    
    // String search optimization for Erosion
    private static readonly int NumRainDrops = Shader.PropertyToID("numRainDrops");
    private static readonly int MaxSediment = Shader.PropertyToID("maxSediment");
    private static readonly int DepositionRate = Shader.PropertyToID("depositionRate");
    private static readonly int Softness = Shader.PropertyToID("softness");
    private static readonly int EvaporationRate = Shader.PropertyToID("evaporationRate");
    private static readonly int Inertia = Shader.PropertyToID("inertia");
    private static readonly int Radius = Shader.PropertyToID("radius");
    private static readonly int Gravity = Shader.PropertyToID("gravity");
    private static readonly int MinSlope = Shader.PropertyToID("minSlope");
    private static readonly int Seed = Shader.PropertyToID("_seed");
    private static readonly int BrushBuffer = Shader.PropertyToID("_BrushBuffer");
    private static readonly int BrushLength = Shader.PropertyToID("brushLength");
    private static readonly int ErosionSteps = Shader.PropertyToID("erosionSteps");

    #endregion

    // Using this for inline methods
    public List<Slider> sliders;
    public Toggle erosionToggle;
    public Toggle noiseMapToggle;
    public Toggle waterToggle;


    void Start()
    {
        Application.targetFrameRate = 30;
        activeBuffers = new List<ComputeBuffer>();
        pendingRelease = new List<(int frame, ComputeBuffer buffer)>();
        Camera.main!.depthTextureMode = DepthTextureMode.Depth;
        
        // Precomputing the area around a drop
        brush = new List<int2>();
        RecalculateBrushStencil(radius);
        
        // Default meshShader options (Needed because these change the actual material file)
        meshCreator.SetFloat(MaxGrassHeight, 1.0f);
        meshCreator.SetFloat(Threshold, .15f);
        meshCreator.SetFloat(BlendFactor, .75f);
        waterMaterial.SetFloat(WaterHeight, .3f);
        meshCreator.SetFloat(WaterHeight, .3f);
        waterMaterial.SetFloat(Depth, .6f);
        meshCreator.SetFloat(WaterEnabled, 1);
        waterMaterial.SetFloat(Hide, 0.0f);
        waterMaterial.SetFloat(Rotation, angle);
        
        
        // Hook up sliders to variables, I'm using inline functions because these are really simple and repetitive
        erosionToggle.onValueChanged.AddListener(val => { skipErosion = !val; isErosionDirty = true; });
        waterToggle.onValueChanged.AddListener(val => { meshCreator.SetFloat(WaterEnabled, val ? 1 : 0); });
        noiseMapToggle.onValueChanged.AddListener(val =>
        {
            showNoiseMap = val;
            waterMaterial.SetFloat(Hide, val ? 1.0f : 0.0f);
        });
        sliders[0].onValueChanged.AddListener(val => { resolution = (int)val; isMeshDirty = true; });
        sliders[1].onValueChanged.AddListener(val => { noiseType = (NoiseType)((int)val); isMeshDirty = true; });
        sliders[2].onValueChanged.AddListener(val => { noiseScale = val / 10.0f; isMeshDirty = true; });
        sliders[3].onValueChanged.AddListener(val => { heightMultiplier = val; isMeshDirty = true; });
        sliders[4].onValueChanged.AddListener(val => { octaves = (int)val; isMeshDirty = true; });
        sliders[5].onValueChanged.AddListener(val => { persistence = val; isMeshDirty = true; });
        sliders[6].onValueChanged.AddListener(val => { lacunarity = val; isMeshDirty = true; });
        sliders[7].onValueChanged.AddListener(val => { warpStrength = val; if (warpFrequency != 0) {isMeshDirty = true;} });
        sliders[8].onValueChanged.AddListener(val => { warpFrequency = val; if (warpStrength != 0) {isMeshDirty = true;} });
        sliders[9].onValueChanged.AddListener(val => { smoothingPasses = (int)val; isMeshDirty = true; });
        sliders[10].onValueChanged.AddListener(val => { numRainDrops = (int)val; isErosionDirty = true; });
        sliders[11].onValueChanged.AddListener(val => { inertia = val; isErosionDirty = true; });
        sliders[12].onValueChanged.AddListener(val => { sedimentMax = val; isErosionDirty = true; });
        sliders[13].onValueChanged.AddListener(val => { depositionRate = val; isErosionDirty = true; });
        sliders[14].onValueChanged.AddListener(val => { evaporationRate = val; isErosionDirty = true; });
        sliders[15].onValueChanged.AddListener(val => { softness = 1 - val; isErosionDirty = true; });
        sliders[16].onValueChanged.AddListener(val => { gravity = val; isErosionDirty = true; });
        sliders[17].onValueChanged.AddListener(val => { RecalculateBrushStencil((int)val); isErosionDirty = true; });
        sliders[18].onValueChanged.AddListener(val => { minSlope = val; isErosionDirty = true; });
        sliders[19].onValueChanged.AddListener(val => { meshCreator.SetFloat(MaxGrassHeight, val); });
        sliders[20].onValueChanged.AddListener(val => { meshCreator.SetFloat(Threshold, val); });
        sliders[21].onValueChanged.AddListener(val => { meshCreator.SetFloat(BlendFactor, val); });
        sliders[22].onValueChanged.AddListener(val => { waterMaterial.SetFloat(WaterHeight, val); meshCreator.SetFloat(WaterHeight, val); });
        sliders[23].onValueChanged.AddListener(val => { waterMaterial.SetFloat(Depth, 1.0f - val); });
        sliders[24].onValueChanged.AddListener(val => { steps = (int)val; isErosionDirty = true; });
        
        // Draw with current data on frame 1
        isMeshDirty = true;
    }
    
    private void Update()
    {
        // Generates map using current information if the map isn't up-to-date, or already in the middle of generating
        if (!isGenerating && (isMeshDirty || isErosionDirty))
        {
            GenerateMap();
        }

        // Set Rotation
        Matrix4x4 rotationMatrix = Matrix4x4.Translate(rotOffset) * Matrix4x4.Rotate(Quaternion.Euler(0, angle, 0)) *
                                   Matrix4x4.Translate(-rotOffset);
        
        Material currentMaterial = showNoiseMap ? noiseMaterial : meshCreator;
        currentMaterial.SetMatrix(Rotation, rotationMatrix);
        waterMaterial.SetFloat(Rotation, angle);
        
        // Draw Mesh to Screen
        Graphics.DrawProcedural(currentMaterial, meshBounds, MeshTopology.Triangles, indexBuffer.count);
    }

    private void LateUpdate()
    {
        // Release buffers after 4 frames : Should prevent swap-chain errors
        for (int i = pendingRelease.Count - 1; i >= 0; i--)
        {
            if (Time.frameCount - pendingRelease[i].frame < 4) continue; 
            
            // Release and remove Buffer from list
            pendingRelease[i].buffer.Release(); 
            pendingRelease.RemoveAt(i);
        }
    }

    private void OnApplicationQuit()
    {
        // This will be outside the buffer clear logic
        savedHeightMap?.Release();
        brushStencil?.Release();
        
        // Clean up all buffers to prevent memory leaks
        foreach (ComputeBuffer buffer in activeBuffers)
        {
            buffer.Release();
        }
        
        foreach ((int frame, ComputeBuffer buffer) buffer in pendingRelease)
        {
            buffer.buffer.Release();
        }
    }

    private void GenerateMap()
    {
        isGenerating = true;
        isErosionDirty = false;
        
        // Swap out old index and vertex buffers to be deleted
        foreach (ComputeBuffer buffer in activeBuffers)
        {
            pendingRelease.Add((Time.frameCount, buffer));
        }
        activeBuffers.Clear();
        
        // These will be used for all calls now on so that the player moving the slider won't affect late shader calls
        dim = resolution;
        mapLength = (resolution + 1) * (resolution + 1);
        
        // Set Shared variable
        meshGenShader.SetInt(NumVertices, mapLength);
        erosionShader.SetInt(NumVertices, mapLength);
        copyComputeBuffer.SetInt(NumVertices, mapLength);
        meshGenShader.SetInt(Resolution, dim + 1);
        erosionShader.SetInt(Resolution, dim);
        
        // Creating new random reference so that I can batch random values. For both noise and erosion
        _random = new Unity.Mathematics.Random((uint)seed);
        
        // Step 1: Get a Height Map
        GetHeightMap();
        isMeshDirty = false;
        
        // Step 2: Simulate Erosion
        // Raindrops will be simulated on the terrain. This directly modifies the heightMap
        ComputeErosion();
        
        // Step 3: Get Min and Max Vertex now that nothing else will change the heightmap
        minMaxBuffer = Noise.PerformReductions(heightMap, activeBuffers, pendingRelease, mapLength);
        
        // Step 4: Generate Indices
        // Needs to be done separate from mesh creation, and doesn't use heightMap, so it helps a bit with synchronization (Maybe, probably doesn't matter)
        GenerateIndices();
        
        // Step 5: Generate Mesh Data
        // Creates a new buffer to hold mesh data that we'll use in the drawing shader. Only Reads the heightMap
        CreateMeshGPU();
        
        // Step 6: Set Material Buffers
        meshCreator.SetBuffer(VertexDataBuffer, vertexDataBuffer);
        meshCreator.SetBuffer(IndexBuffer, indexBuffer);
        meshCreator.SetBuffer(MinMaxBuffer, minMaxBuffer);
        noiseMaterial.SetBuffer(VertexDataBuffer, vertexDataBuffer);
        noiseMaterial.SetBuffer(IndexBuffer, indexBuffer);
        noiseMaterial.SetBuffer(MinMaxBuffer, minMaxBuffer);
        waterMaterial.SetBuffer(MinMaxBuffer, minMaxBuffer);
        
        // Confirm that a new map can be generated next frame if dirty
        isGenerating = false;
    }

    private void GetHeightMap()
    {
        if (isMeshDirty)
        {
            savedHeightMap?.Release();
            
            // A new one will be calculated because mesh parameters changed
            heightMap = noise.ComputeHeightMap(dim + 1, _random, noiseScale, octaves, persistence, lacunarity, offset,
                (int) noiseType + 1, warpStrength, warpFrequency, smoothingPasses, heightMultiplier, heightCurve,
                pendingRelease);
            
            // savedHeightMap will copy HeightMap. The reason this is done instead of the other way around is
            // because ComputeHeightMap, by requirement, will have the newly generated heightmap in the pendingRelease list
            // heightMap should copy the savedHeightMap
            savedHeightMap = new ComputeBuffer(mapLength, sizeof(float));
            #if UNITY_EDITOR // Handled automatically be WebGPU
            savedHeightMap.SetData(new float[mapLength]);
            #endif
            copyComputeBuffer.SetBuffer(0, SourceBuffer, heightMap);
            copyComputeBuffer.SetBuffer(0, DestinationBuffer, savedHeightMap);
            copyComputeBuffer.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
        }
        else
        {
            // heightMap should copy the savedHeightMap
            heightMap = new ComputeBuffer(mapLength, sizeof(float));
            #if UNITY_EDITOR // Handled automatically be WebGPU
            heightMap.SetData(new float[mapLength]);
            #endif
            copyComputeBuffer.SetBuffer(0, SourceBuffer, savedHeightMap);
            copyComputeBuffer.SetBuffer(0, DestinationBuffer, heightMap);
            copyComputeBuffer.Dispatch(0, Mathf.CeilToInt(mapLength / 64.0f), 1, 1);
            pendingRelease.Add((Time.frameCount, heightMap));
        }
    }
    
    private void CreateMeshGPU()
    {
        // Pass distance between vertices to shader
        meshGenShader.SetFloat(Scale, 100.0f / dim);
        
        // This will hold vertices, uvs, and the modified heightmap
        vertexDataBuffer = new ComputeBuffer(mapLength, sizeof(float) * 9); // 3 float3's
        #if UNITY_EDITOR // Handled automatically be WebGPU
        vertexDataBuffer.SetData(new VertexData[mapLength]);
        #endif
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
        indexBuffer = new ComputeBuffer(numIndices, sizeof(uint));
        #if UNITY_EDITOR // Handled automatically be WebGPU
        indexBuffer.SetData(new int[numIndices]);
        #endif
        activeBuffers.Add(indexBuffer);
        
        // Set Shader data
        meshGenShader.SetBuffer(1, IndexBuffer, indexBuffer);
        meshGenShader.SetInt(QuadWidth, dim);
        meshGenShader.SetInt(NumQuads, numQuads);
        
        // Dispatch Shader so that the indexBuffer is available for UpdateMesh
        meshGenShader.Dispatch(1, Mathf.CeilToInt(numQuads / 64.0f), 1, 1);
    }

    private void ComputeErosion()
    {
        // Buffer will throw error if size 0 
        if (numRainDrops == 0 || steps == 0 || skipErosion)
        {
            return;
        }
        
        // Set Variables
        erosionShader.SetBuffer(0, HeightBuffer, heightMap);
        erosionShader.SetBuffer(0, BrushBuffer, brushStencil);
        erosionShader.SetFloat(Inertia, inertia);
        erosionShader.SetFloat(MaxSediment, sedimentMax);
        erosionShader.SetFloat(DepositionRate, depositionRate);
        erosionShader.SetFloat(EvaporationRate, 1.0f - evaporationRate);
        erosionShader.SetFloat(Softness, softness);
        erosionShader.SetFloat(Gravity,gravity);
        erosionShader.SetFloat(MinSlope, minSlope);
        erosionShader.SetInt(NumRainDrops, numRainDrops);
        erosionShader.SetInt(Radius, radius); 
        erosionShader.SetInt(BrushLength, brushStencil.count);
        erosionShader.SetInt(Seed, _random.NextInt());
        erosionShader.SetInt(ErosionSteps, steps);
        
        // Execute erosion shader
        erosionShader.Dispatch(0, Mathf.CeilToInt(numRainDrops / 64.0f), 1, 1);
    }

    private void RecalculateBrushStencil(int rad)
    {
        // Recalculating brush stencil
        brush.Clear();
        radius = rad;
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                if (Mathf.Sqrt(x * x + z * z) <= radius)
                {
                    brush.Add(new int2(x, z));
                }
            }
        }
            
        // Having a brush that I can iterate through on the gpu is much more efficient than a double loop on the gpu 
        brushStencil?.Release();
        brushStencil = new ComputeBuffer(brush.Count, sizeof(int) * 2);
        brushStencil.SetData(brush);
    }
}
