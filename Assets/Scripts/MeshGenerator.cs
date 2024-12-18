using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshGenerator : MonoBehaviour
{
    // Variables Changeable within the editor
    public int mapWidth;
    public int mapHeight;
    public float noiseScale;
    
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
        
        GenerateMap();
    }

    public void GenerateMap()
    {
        
        #if UNITY_EDITOR
        // The only case where this should happen
        if (!mesh)
        {
            mesh = new Mesh();
            GetComponent<MeshFilter>().mesh = mesh;
            textureRenderer = GetComponent<MeshRenderer>();
        }
        #endif
        
        if (mapWidth < 1) mapWidth = 1;
        if (mapHeight < 1) mapHeight = 1;
        if (noiseScale < 1) noiseScale = 1;
        
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
        float[,] noiseMap = Noise.GenerateNoiseMap(mapWidth, mapHeight, noiseScale);
        int width = noiseMap.GetLength(0);
        int height = noiseMap.GetLength(1);
        
        
        Texture2D texture = new(width, height);
        Color[] colorMap = new Color[width * height];

        // Trying to set the texture as perlin noise
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                colorMap[y * width + x] = Color.Lerp(Color.black, Color.white, noiseMap[x, y]);
            }
        }
        
        texture.SetPixels(colorMap);
        texture.Apply();


        textureRenderer.sharedMaterial.mainTexture = texture;
        textureRenderer.transform.localScale = new Vector3(width, 1, height);
    }
}
