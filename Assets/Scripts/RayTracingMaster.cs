using System;
using UnityEditor.Searcher;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[ExecuteInEditMode]
public class RayTracingMaster : MonoBehaviour
{
    public ComputeShader RayTracingShader;
    private RenderTexture _target;
    
    // Cached strings
    private static readonly int Result = Shader.PropertyToID("Result");
    private static readonly int CameraToWorld = Shader.PropertyToID("_CameraToWorld");
    private static readonly int CameraInverseProjection = Shader.PropertyToID("_CameraInverseProjection");

    private void SetShaderParameters()
    {
        RayTracingShader.SetMatrix(CameraToWorld, Camera.current.cameraToWorldMatrix);
        RayTracingShader.SetMatrix(CameraInverseProjection, Camera.current.projectionMatrix.inverse);
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
        
        // Pass texture to render target / screen
        Graphics.Blit(_target, destination);
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
    }
}
