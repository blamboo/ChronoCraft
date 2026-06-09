// StructureManager.cs
// Version: 0.6 (initial)
// Purpose: Unity bridge for the prototype structure. Picks a build-site cell, registers
//          a StructureNode in the simulation, marks the cell occupied, and spawns a
//          placeholder cube whose height and colour animate as build progress advances.
//          Holds no sim logic -- it only mirrors StructureNode state into the scene.
// Location: Assets/Scripts/Simulation/StructureManager.cs
// Dependencies: UnityEngine; SimulationRunner, GridManager, StructureNode.
// Events: none.

using UnityEngine;

public class StructureManager : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("Drag the Simulation GameObject here.")]
    [SerializeField] private SimulationRunner runner;
    [Tooltip("Drag the ProceduralTerrain GameObject here.")]
    [SerializeField] private GridManager gridManager;

    [Header("Build site")]
    [Tooltip("Approximate build-site cell (snapped to nearest walkable).")]
    [SerializeField] private Vector2Int buildSiteCell = new Vector2Int(32, 32);
    [Tooltip("Wood units the agent must deliver before construction can begin.")]
    [Range(1, 10)]
    [SerializeField] private int woodRequired = 3;
    [Tooltip("Game-seconds to complete construction once wood is deposited.")]
    [Range(1f, 120f)]
    [SerializeField] private float buildDurationSeconds = 20f;

    [Header("Read-out (Play mode)")]
    [SerializeField] private float buildProgress;

    private StructureNode structureNode;
    private Transform     view;
    private Renderer      viewRenderer;
    private bool          initialized;

    // Read by AgentBehavior via Simulation.StructureNodes -- no direct reference needed.

    void Update()
    {
        if (!initialized) { TryInitialize(); return; }
        if (structureNode == null) return;

        buildProgress = structureNode.BuildProgress;

        // Animate placeholder: flat slab → house height as build progresses.
        float t      = structureNode.BuildProgress;
        float yScale = Mathf.Lerp(0.1f, 1.5f, t);
        view.localScale = new Vector3(0.9f, yScale, 0.9f);

        // Colour: grey foundation → warm tan when built.
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", Color.Lerp(
            new Color(0.55f, 0.55f, 0.55f),   // grey foundation
            new Color(0.65f, 0.45f, 0.25f),   // warm tan (built)
            t));
        viewRenderer.SetPropertyBlock(mpb);
    }

    void TryInitialize()
    {
        if (runner == null || gridManager == null) return;
        GridData   grid = gridManager.Grid;
        Simulation sim  = runner.Sim;
        if (grid == null || sim == null) return;

        Vector2Int cell = NearestWalkable(grid, buildSiteCell);

        structureNode = sim.AddStructureNode(cell.x, cell.y, woodRequired, buildDurationSeconds);
        grid.SetOccupied(cell.x, cell.y, true);

        // Spawn placeholder at the build-site cell.
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = "Structure (placeholder)";
        obj.transform.SetParent(gridManager.transform, worldPositionStays: false);
        Destroy(obj.GetComponent<Collider>());

        Vector3 local = gridManager.Grid.CellToLocal(cell.x, cell.y);
        obj.transform.localPosition = new Vector3(local.x, local.y + 0.05f, local.z);
        obj.transform.localScale    = new Vector3(0.9f, 0.1f, 0.9f);

        viewRenderer = obj.GetComponent<Renderer>();
        view         = obj.transform;

        initialized = true;
    }

    Vector2Int NearestWalkable(GridData grid, Vector2Int c)
    {
        c.x = Mathf.Clamp(c.x, 0, grid.Width  - 1);
        c.y = Mathf.Clamp(c.y, 0, grid.Depth  - 1);
        if (grid.Cells[c.x, c.y].Walkable) return c;

        int maxR = Mathf.Max(grid.Width, grid.Depth);
        for (int r = 1; r <= maxR; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
            int x = c.x + dx, z = c.y + dz;
            if (grid.InBounds(x, z) && grid.Cells[x, z].Walkable)
                return new Vector2Int(x, z);
        }
        return c;
    }
}
