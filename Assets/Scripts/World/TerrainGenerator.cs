// TerrainGenerator.cs
// Purpose: Procedural terrain proof-of-concept for the TimeCraft prototype.
//          Generates a heightfield from layered Perlin noise (plain data) and builds
//          a display mesh from it. The heightfield is the single source of truth for
//          terrain height; the logical grid reads this same data (via HeightAt) so
//          pathfinding/placement and rendering can never diverge.
// Location: Assets/Scripts/World/TerrainGenerator.cs
// Dependencies: UnityEngine. Requires MeshFilter + MeshRenderer (auto-added).
// Events emitted: none. Events consumed: none.
// Notes: Static once generated (no terrain degradation). Uses the simple Mesh API,
//        which is fine for a small static PoC. If terrain later becomes large or
//        streamed, move generation to the advanced MeshData API and/or the Jobs
//        system. Flagged as the scalable path, not required now.

using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainGenerator : MonoBehaviour
{
    [Header("Grid size (cells)")]
    public int width = 64;
    public int depth = 64;

    [Header("Cell size (world units)")]
    public float cellSize = 1f;

    [Header("Noise")]
    public float noiseScale = 20f;          // larger = smoother, broader features
    [Range(1, 8)] public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f; // amplitude falloff per octave
    public float lacunarity = 2f;           // frequency growth per octave
    public float heightMultiplier = 6f;     // vertical scale
    public int seed = 0;

    // Single source of truth for terrain height. Sized (width+1) x (depth+1) to match
    // the mesh vertex grid. Stores RAW noise values; HeightAt() applies the multiplier.
    public float[,] Heights { get; private set; }

    void Start()
    {
        Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        Heights = BuildHeightfield();
        Mesh mesh = BuildMesh(Heights);
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    // Local-space Y used by the mesh for vertex (x, z): raw noise height * heightMultiplier.
    // Keeping this transform in one place is what lets the grid sample the same heights the
    // mesh renders, so the two never drift apart (single source of truth).
    public float HeightAt(int x, int z)
    {
        if (Heights == null) Generate();
        return Heights[x, z] * heightMultiplier;
    }

    float[,] BuildHeightfield()
    {
        int vx = width + 1;
        int vz = depth + 1;
        float[,] h = new float[vx, vz];

        // Per-octave random offsets so different seeds give different terrain.
        System.Random prng = new System.Random(seed);
        Vector2[] offsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++)
            offsets[i] = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));

        float scale = Mathf.Max(0.0001f, noiseScale);

        for (int z = 0; z < vz; z++)
        {
            for (int x = 0; x < vx; x++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float value = 0f;

                for (int o = 0; o < octaves; o++)
                {
                    float sx = (x / scale) * frequency + offsets[o].x;
                    float sz = (z / scale) * frequency + offsets[o].y;
                    float sample = Mathf.PerlinNoise(sx, sz) * 2f - 1f; // remap 0..1 -> -1..1
                    value += sample * amplitude;
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                h[x, z] = value;
            }
        }
        return h;
    }

    Mesh BuildMesh(float[,] h)
    {
        int vx = width + 1;
        int vz = depth + 1;

        float offsetX = width * cellSize * 0.5f;
        float offsetZ = depth * cellSize * 0.5f;

        Vector3[] vertices = new Vector3[vx * vz];
        Vector2[] uv = new Vector2[vx * vz];
        int[] triangles = new int[width * depth * 6];

        for (int z = 0; z < vz; z++)
        {
            for (int x = 0; x < vx; x++)
            {
                int i = z * vx + x;
                vertices[i] = new Vector3(x * cellSize - offsetX, h[x, z] * heightMultiplier, z * cellSize - offsetZ);
                uv[i] = new Vector2((float)x / width, (float)z / depth);
            }
        }

        int t = 0;
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int bl = z * vx + x;   // bottom-left vertex of this cell
                int br = bl + 1;       // bottom-right
                int tl = bl + vx;      // top-left
                int tr = bl + vx + 1;  // top-right

                // Winding chosen so face normals point +Y (visible from the top-down camera).
                triangles[t++] = bl;
                triangles[t++] = tl;
                triangles[t++] = br;

                triangles[t++] = br;
                triangles[t++] = tl;
                triangles[t++] = tr;
            }
        }

        Mesh mesh = new Mesh { name = "ProceduralTerrain" };
        if (vertices.Length > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
