// GridManager.cs
// Purpose: Unity adapter for the logical grid. Builds a GridData instance from the
//          TerrainGenerator's heights, owns it, and draws an editor-only gizmo overlay
//          so the otherwise-invisible grid can be inspected. All spatial state stays in
//          the plain-C# GridData object per the architecture principle; this component
//          is just the Unity-side glue and visualization.
// Location: Assets/Scripts/World/GridManager.cs
// Dependencies: UnityEngine; TerrainGenerator on the same GameObject (RequireComponent).
//               Reads TerrainGenerator.HeightAt(); shares the object's transform so the
//               grid aligns with the rendered terrain.
// Events emitted: none yet. Events consumed: none (grid is rebuilt manually for now).

using UnityEngine;

[RequireComponent(typeof(TerrainGenerator))]
public class GridManager : MonoBehaviour
{
    [Tooltip("Max height difference across a cell before it is marked unwalkable (world units).")]
    public float maxStepHeight = 1.5f;

    [Header("Editor gizmos")]
    public bool showGizmos = true;
    public bool onlyShowUnwalkable = false;

    public GridData Grid { get; private set; }

    TerrainGenerator terrain;

    void Awake()
    {
        terrain = GetComponent<TerrainGenerator>();
    }

    void Start()
    {
        BuildGrid();
    }

    [ContextMenu("Build Grid")]
    public void BuildGrid()
    {
        if (terrain == null) terrain = GetComponent<TerrainGenerator>();

        // Ensure the terrain heightfield exists (e.g. when building in edit mode).
        if (terrain.Heights == null) terrain.Generate();

        int vx = terrain.width + 1;
        int vz = terrain.depth + 1;
        float[,] vertexHeights = new float[vx, vz];
        for (int z = 0; z < vz; z++)
            for (int x = 0; x < vx; x++)
                vertexHeights[x, z] = terrain.HeightAt(x, z);

        Grid = new GridData();
        Grid.Build(vertexHeights, terrain.cellSize, maxStepHeight);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos || Grid == null) return;

        float size = Grid.CellSize * 0.35f;
        for (int z = 0; z < Grid.Depth; z++)
        {
            for (int x = 0; x < Grid.Width; x++)
            {
                bool walkable = Grid.Cells[x, z].Walkable;
                if (onlyShowUnwalkable && walkable) continue;

                Gizmos.color = walkable ? new Color(0f, 1f, 0f, 0.5f) : new Color(1f, 0f, 0f, 0.7f);
                Vector3 world = transform.TransformPoint(Grid.CellToLocal(x, z));
                Gizmos.DrawCube(world, new Vector3(size, 0.05f, size));
            }
        }
    }
}
