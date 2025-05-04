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
                meshGen.isMeshDirty = true;
            }
            
            if (GUILayout.Button("Reset"))
            {
                meshGen.isMeshDirty = true;
            }
        }
    }
}
