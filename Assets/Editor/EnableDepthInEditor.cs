using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class EnableDepth {
    static EnableDepth()
    {
        SceneView.duringSceneGui += OnSceneGui;
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        if (Camera.main != null)
        {
            Camera.main.depthTextureMode = DepthTextureMode.Depth;
        }
    }
}
