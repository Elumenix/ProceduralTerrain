using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshGenerator : MonoBehaviour
{
    // Object reference variables
    private Renderer textureRenderer;
    private Mesh mesh;
    
    // Mesh information
    private Vector3[] vertices;
    private int[] indices;
    private Vector2[] uvs;
    
    void Start()
    {
        // Set reference for gameObject to use the mesh we create here
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
        textureRenderer = GetComponent<MeshRenderer>();

        
        CreateShape();
        UpdateMesh();
        SetTexture();
    }

    void CreateShape()
    {
        vertices = new Vector3[]
        {
            new Vector3(0,0,0),
            new Vector3(0,0,10),
            new Vector3(10,0,0),
            new Vector3(10,0,10)
        };

        indices = new[]
        {
            0,1,2,2,1,3
        };
        
        uvs = new Vector2[]
        {
            new Vector2(0,0),
            new Vector2(0,1),
            new Vector2(1,0),
            new Vector2(1,1)
        };
    }

    void UpdateMesh()
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.uv = uvs;
        mesh.RecalculateNormals(); // Fixes Lighting
    }

    void SetTexture()
    {
        // Temp variables
        int width = 200;
        int height = 200;
        
        Texture2D texture = new Texture2D(width, height);
        Color[] colorMap = new Color[width * height];

        // Trying to set the texture as perlin noise
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float xCoord = (float)x / width * 3f; 
                float yCoord = (float)y / height * 3f; 
                float sample = Mathf.PerlinNoise(xCoord, yCoord); 
                colorMap[y * width + x] = Color.Lerp(Color.black, Color.white, sample);
            }
        }
        
        texture.SetPixels(colorMap);
        texture.Apply();

        textureRenderer.sharedMaterial.mainTexture = texture;
    }
}
