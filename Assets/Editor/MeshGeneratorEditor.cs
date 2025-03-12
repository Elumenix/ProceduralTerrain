using UnityEditor;
using UnityEngine;

namespace Editor
{
    [CustomEditor(typeof(MeshGenerator))]
    public class MeshGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            MeshGenerator meshGen = (MeshGenerator)target;

            // Automatically redraws if something in the inspector is changed
            if (DrawDefaultInspector())
            {
                meshGen.StopAllCoroutines();
                meshGen.dimension?.Release(); // Prevent Leak
                meshGen.StartCoroutine(meshGen.GenerateMap());
            }

            /*if (GUILayout.Button("Erode"))
            {
                meshGen.ComputeErosion();
            }*/
            
            if (GUILayout.Button("Reset"))
            {
                meshGen.StartCoroutine(meshGen.GenerateMap());
            }
        }
    }
}
