// TerrainGenerator.cs
// Version: 0.7 (added waterLevel + central basin so a lake biases toward map centre)
// Purpose: Procedural terrain proof-of-concept for the TimeCraft prototype.
//          Generates a heightfield from layered Perlin noise (plain data) and builds
//          a display mesh from it. The heightfield is the single source of truth for
//          terrain height; the logical grid reads this same data (via HeightAt) so
//          pathfinding/placement and rendering can never diverge.
//          v0.7: a smooth central basin is subtracted from the heightfield, and a
//          waterLevel (local-Y) is exposed. GridData classifies cells at/below
//          waterLevel as water (unwalkable); GridManager renders a water plane there.
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

    [Header("Water")]
    [Tooltip("Local-Y height of the water surface. Cells whose centre is at or below " +
             "this are water (unwalkable). Raise to flood more, lower to drain. Tune " +
             "live while watching the blue water plane in the Game view.")]
    public float waterLevel = -1.5f;

    [Header("Central basin (biases a lake toward the map centre)")]
    [Tooltip("How far the map centre is pushed DOWN, in raw noise units (these get " +
             "multiplied by Height Multiplier in the final mesh). 0 = no basin. Raise " +
             "to dig a deeper central lake.")]
    [Range(0f, 4f)] public float basinStrength = 1.0f;
    [Tooltip("Basin radius as a fraction of half the map (1 = reaches the edges). " +
             "Larger = wider lake.")]
    [Range(0.1f, 1.5f)] public float basinRadius = 0.7f;

    // Single source of truth for terrain height. Sized (width+1) x (depth+1) to match
    // the mesh vertex grid. Stores RAW noise values (with the basin already applied);
    // HeightAt() applies the multiplier so mesh and grid stay in lock-step.
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

                // Central basin: subtract a smooth dome (max at centre, 0 past basinRadius)
                // so the middle dips below waterLevel and forms a contestable central lake.
                // Applied in raw space so the mesh, HeightAt(), and the grid all see it.
                if (basinStrength > 0f && basinRadius > 0f)
                {
                    float nx = (vx > 1) ? (x / (float)(vx - 1)) * 2f - 1f : 0f; // -1..1
                    float nz = (vz > 1) ? (z / (float)(vz - 1)) * 2f - 1f : 0f;
                    float dist = Mathf.Sqrt(nx * nx + nz * nz);
                    float t = Mathf.Clamp01(1f - dist / basinRadius);
                    float dome = t * t * (3f - 2f * t); // smoothstep
                    value -= basinStrength * dome;
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
