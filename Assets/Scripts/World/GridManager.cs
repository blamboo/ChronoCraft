// GridManager.cs
// Version: 0.8 (water plane: on-demand RebuildWaterPlane context menu, edit-mode safe,
//               clears stale planes, sizes from terrain, logs creation)
// Purpose: Unity adapter for the logical grid. Builds a GridData instance from the
//          TerrainGenerator's heights, owns it, and draws an editor-only gizmo overlay
//          so the otherwise-invisible grid can be inspected. All spatial state stays in
//          the plain-C# GridData object per the architecture principle; this component
//          is just the Unity-side glue and visualization.
//          v0.8: water plane creation moved into RebuildWaterPlane(), exposed as a
//          [ContextMenu] so it can be (re)spawned on demand in edit mode or Play; old
//          "WaterPlane" children are cleared first; sizing reads TerrainGenerator
//          directly so it works even before the grid is built; logs on creation.
// Location: Assets/Scripts/World/GridManager.cs
// Dependencies: UnityEngine; TerrainGenerator on the same GameObject (RequireComponent).
//               Reads TerrainGenerator.HeightAt(), width/depth/cellSize, waterLevel.
// Events emitted: none yet. Events consumed: none (grid is rebuilt manually for now).

using UnityEngine;

[RequireComponent(typeof(TerrainGenerator))]
public class GridManager : MonoBehaviour
{
    [Tooltip("Max height difference across a cell before it is marked unwalkable (world units).")]
    public float maxStepHeight = 1.5f;

    [Header("Editor gizmos (Scene view only)")]
    public bool showGizmos = true;
    public bool onlyShowUnwalkable = false;

    [Header("Water plane")]
    [Tooltip("Spawn a flat blue plane at the terrain's waterLevel so the lake is visible " +
             "in the Game view. Created on Play; use the component's context menu " +
             "(Rebuild Water Plane) to spawn or refresh it in edit mode too.")]
    public bool showWaterPlane = true;

    public GridData Grid { get; private set; }

    TerrainGenerator terrain;

    void Awake()
    {
        terrain = GetComponent<TerrainGenerator>();
    }

    void Start()
    {
        BuildGrid();
        if (showWaterPlane) RebuildWaterPlane();
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
        Grid.Build(vertexHeights, terrain.cellSize, maxStepHeight, terrain.waterLevel);
    }

    // Spawns (or refreshes) a flat plane at local-Y = waterLevel, centred on the terrain
    // like the mesh. Works in edit mode and Play. Safe to call repeatedly.
    [ContextMenu("Rebuild Water Plane")]
    public void RebuildWaterPlane()
    {
        if (terrain == null) terrain = GetComponent<TerrainGenerator>();

        // Clear any previous water plane (handles repeated calls and references lost
        // across a domain reload / recompile).
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c != null && c.name == "WaterPlane") SafeDestroy(c.gameObject);
        }

        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "WaterPlane";

        Collider col = plane.GetComponent<Collider>();
        if (col != null) SafeDestroy(col);

        plane.transform.SetParent(transform, worldPositionStays: false);

        // Unity's built-in Plane is 10x10 world units at scale 1. Size from the terrain
        // directly so this is correct even before BuildGrid() has run.
        float mapW = terrain.width * terrain.cellSize;
        float mapD = terrain.depth * terrain.cellSize;
        plane.transform.localScale    = new Vector3(mapW / 10f, 1f, mapD / 10f);
        plane.transform.localPosition = new Vector3(0f, terrain.waterLevel, 0f);
        plane.transform.localRotation = Quaternion.identity;

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", new Color(0.15f, 0.35f, 0.7f, 1f)); // water blue
        plane.GetComponent<Renderer>().SetPropertyBlock(mpb);

        Debug.Log($"[GridManager] WaterPlane rebuilt at localY={terrain.waterLevel}, " +
                  $"size {mapW}x{mapD}.");
    }

    // Destroy that works in both edit mode and Play mode.
    void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos || Grid == null) return;

        float size = Grid.CellSize * 0.35f;
        for (int z = 0; z < Grid.Depth; z++)
        {
            for (int x = 0; x < Grid.Width; x++)
            {
                GridCell cell = Grid.Cells[x, z];
                if (onlyShowUnwalkable && cell.Walkable) continue;

                if (cell.IsWater)
                    Gizmos.color = new Color(0.15f, 0.35f, 0.85f, 0.6f); // water blue
                else if (cell.Walkable)
                    Gizmos.color = new Color(0f, 1f, 0f, 0.5f);          // walkable green
                else
                    Gizmos.color = new Color(1f, 0f, 0f, 0.7f);          // unwalkable red

                Vector3 world = transform.TransformPoint(Grid.CellToLocal(x, z));
                Gizmos.DrawCube(world, new Vector3(size, 0.05f, size));
            }
        }
    }
}
