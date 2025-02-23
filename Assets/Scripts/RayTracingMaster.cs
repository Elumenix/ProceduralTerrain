using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

struct Sphere
{
    public float3 position;
    public float radius;
    public float3 albedo;
    public float3 specular;
};

[ExecuteInEditMode]
public class RayTracingMaster : MonoBehaviour
{
    public ComputeShader RayTracingShader;
    public Texture SkyboxTexture;
    public Light DirectionalLight;
    [Range(1, 16)]
    public int NumReflections = 8;
    private RenderTexture _target;
    private uint _currentSample = 0;
    private Material _addMaterial;
    private List<Transform> _transforms;
    private Camera _camera;
    
    // Sphere variables
    public Vector2 SphereRadius = new Vector2(3.0f, 8.0f);
    public uint SpheresMax = 100;
    public float SpherePlacementRadius = 100.0f;
    private ComputeBuffer _sphereBuffer;

    
    // Cached strings
    private static readonly int Result = Shader.PropertyToID("Result");
    private static readonly int CameraToWorld = Shader.PropertyToID("_CameraToWorld");
    private static readonly int CameraInverseProjection = Shader.PropertyToID("_CameraInverseProjection");
    private static readonly int SkyboxTexture1 = Shader.PropertyToID("_SkyboxTexture");
    private static readonly int Sample = Shader.PropertyToID("_Sample");
    private static readonly int PixelOffset = Shader.PropertyToID("_PixelOffset");
    private static readonly int Reflections = Shader.PropertyToID("numReflections");
    private static readonly int DirectionalLight1 = Shader.PropertyToID("_DirectionalLight");
    private static readonly int Spheres = Shader.PropertyToID("_Spheres");

    private void Start()
    {
        _currentSample = 0;
        SetUpScene();
        
        _camera = Camera.main;
        _transforms = new List<Transform>()
        {
            transform,
            DirectionalLight.transform
        };
    }

    private void OnDisable()
    {
        if (_sphereBuffer != null)
        {
            _sphereBuffer.Release();
        }
    }

    private void SetUpScene()
    {
        List<Sphere> spheres = new List<Sphere>();
        
        // Add a number of random spheres
        for (int i = 0; i < SpheresMax; i++)
        {
            float radius = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);
            Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
            
            Sphere sphere = new Sphere
            {
                // Radius and position
                radius = radius,
                position = new Vector3(randomPos.x, radius, randomPos.y)
            };

            // Reject spheres that are intersecting other spheres
            foreach (Sphere other in spheres)
            {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist) goto SkipSphere;
            }

            // Albedo and Specular color
            Color color = Random.ColorHSV();
            bool metal = Random.value < 0.5f;
            sphere.albedo = metal ? Vector3.zero : new Vector3(color.r, color.g, color.b);
            sphere.specular = metal ? new Vector3(color.r, color.g, color.b) : Vector3.one * 0.04f;

            // Add the sphere to the list
            spheres.Add(sphere);


            SkipSphere:
            {
                continue;
            }
        }

        _sphereBuffer = new ComputeBuffer(spheres.Count, 40);
        _sphereBuffer.SetData(spheres);
    }

    private void Update()
    {
        foreach (Transform t in _transforms.Where(t => t.hasChanged))
        {
            _currentSample = 0;
            t.hasChanged = false;
        }
    }

    private void OnValidate()
    {
        // Make sure to redraw if a parameter changes in the inspector
        _currentSample = 0;
    }

    private void OnRenderObject()
    {
        // Lets the shader display in editor mode, antialiasing will display weird but the program will work
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        _currentSample = 0;
        SetShaderParameters();
        Render(null);
#endif
    }
    
    private void SetShaderParameters()
    {
        RayTracingShader.SetMatrix(CameraToWorld, _camera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix(CameraInverseProjection, _camera.projectionMatrix.inverse);
        RayTracingShader.SetTexture(0, SkyboxTexture1, SkyboxTexture);
        RayTracingShader.SetVector(PixelOffset, new Vector2(Random.value, Random.value));
        Vector3 l = DirectionalLight.transform.forward;
        RayTracingShader.SetVector(DirectionalLight1, new Vector4(l.x, l.y, l.z, DirectionalLight.intensity));
        RayTracingShader.SetInt(Reflections, NumReflections);
        RayTracingShader.SetBuffer(0, Spheres, _sphereBuffer);
    }
    
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        SetShaderParameters();
        Render(destination);
    }

    private void Render(RenderTexture destination)
    {
        InitRenderTexture();
        
        // Set target and dispatch
        RayTracingShader.SetTexture(0, Result, _target);
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        
        // Anti-aliasing
        if (_addMaterial == null)
        {
            _addMaterial = new Material(Shader.Find("Hidden/AddShader"));
        } 
        _addMaterial.SetFloat(Sample, _currentSample);
        
        // Pass texture to render target / screen
        Graphics.Blit(_target, destination, _addMaterial);
        _currentSample++;
    }

    private void InitRenderTexture()
    {
        if (_target != null && _target.width == Screen.width && _target.height == Screen.height) return;
        
        // Clear any current render texture
        if (_target != null)
        {
            _target.Release();
        }
        
        // Get new render target
        _target = new RenderTexture(Screen.width, Screen.height, 0,
            RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true
        };
                
        _target.Create();
        _currentSample = 0;
    }
}
