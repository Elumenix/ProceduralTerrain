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
                //meshGen.StopAllCoroutines();
                meshGen.isDirty = true;
            }

            /*if (GUILayout.Button("Erode"))
            {
                meshGen.ComputeErosion();
            }*/
            
            if (GUILayout.Button("Reset"))
            {
                meshGen.isDirty = true;
            }
        }
    }
}
