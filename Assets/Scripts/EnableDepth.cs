using System;
using UnityEngine;

public class EnableDepth : MonoBehaviour
{
    private void OnEnable()
    {
        // This is on the camera, so it should always work
        Camera.main!.depthTextureMode = DepthTextureMode.Depth;
    }
}
