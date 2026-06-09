// ResourceManager.cs
// Version: 0.4 (initial)
// Purpose: Unity bridge for prototype resource nodes. Scatters sim-side ResourceNodes
//          onto walkable grid cells (seed-based, reproducible), marks those cells
//          occupied so structures cannot be placed there later, and spawns a placeholder
//          primitive per node. Holds no sim logic -- nodes are passive data.
// Location: Assets/Scripts/Simulation/ResourceManager.cs
// Dependencies: UnityEngine; SimulationRunner, GridManager, ResourceNode.
// Events emitted: none. Events consumed: none.

using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("Drag the Simulation GameObject here.")]
    [SerializeField] private SimulationRunner runner;
    [Tooltip("Drag the ProceduralTerrain GameObject here.")]
    [SerializeField] private GridManager gridManager;

    [Header("Wood nodes")]
    [Range(1, 50)]
    [SerializeField] private int woodCount = 10;

    [Header("Food nodes")]
    [Range(1, 50)]
    [SerializeField] private int foodCount = 10;

    [Header("Per-node stock")]
    [Tooltip("Harvest actions available before the node is depleted.")]
    [Range(1, 20)]
    [SerializeField] private int amountPerNode = 5;

    [Header("Placement")]
    [Tooltip("Seed for reproducible node placement.")]
    [SerializeField] private int seed = 42;

    private bool initialized;

    void Update()
    {
        if (!initialized) TryInitialize();
    }

    void TryInitialize()
    {
        if (runner == null || gridManager == null) return;
        GridData grid = gridManager.Grid;
        Simulation sim = runner.Sim;
        if (grid == null || sim == null) return;

        var rng = new System.Random(seed);
        SpawnNodes(sim, grid, rng, ResourceType.Wood, woodCount);
        SpawnNodes(sim, grid, rng, ResourceType.Food, foodCount);

        initialized = true;
    }

    void SpawnNodes(Simulation sim, GridData grid, System.Random rng,
                   ResourceType type, int count)
    {
        bool isWood = type == ResourceType.Wood;
        Color color = isWood ? new Color(0.4f, 0.25f, 0.1f)      // brown
                             : new Color(0.2f, 0.7f,  0.2f);      // green
        var primitiveType = isWood ? PrimitiveType.Cube : PrimitiveType.Sphere;
        Vector3 scale = isWood ? new Vector3(0.5f, 1.2f, 0.5f)
                               : new Vector3(0.6f, 0.6f, 0.6f);
        float yOff = isWood ? 0.6f : 0.3f;

        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = RandomWalkableCell(grid, rng);
            if (cell.x < 0) break; // no walkable cell found

            // Register in the simulation. (cell.y = Z in our Vector2Int convention.)
            sim.AddResourceNode(type, cell.x, cell.y, amountPerNode);

            // Mark the cell occupied so structures cannot be placed here.
            grid.SetOccupied(cell.x, cell.y, true);

            // Spawn placeholder primitive.
            var obj = GameObject.CreatePrimitive(primitiveType);
            obj.name = $"{type} Node ({cell.x},{cell.y})";
            obj.transform.SetParent(gridManager.transform, worldPositionStays: false);
            Destroy(obj.GetComponent<Collider>()); // not needed in the prototype

            Vector3 local = gridManager.Grid.CellToLocal(cell.x, cell.y);
            obj.transform.localPosition = new Vector3(local.x, local.y + yOff, local.z);
            obj.transform.localScale = scale;

            // Tint via MaterialPropertyBlock so no extra material instances are created.
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            obj.GetComponent<Renderer>().SetPropertyBlock(mpb);
        }
    }

    // Returns a random walkable, unoccupied cell; falls back to a linear scan.
    // Returns (-1, -1) if the grid is full.
    Vector2Int RandomWalkableCell(GridData grid, System.Random rng)
    {
        const int maxAttempts = 1000;
        for (int i = 0; i < maxAttempts; i++)
        {
            int x = rng.Next(0, grid.Width);
            int z = rng.Next(0, grid.Depth);
            if (grid.Cells[x, z].Walkable && !grid.Cells[x, z].Occupied)
                return new Vector2Int(x, z);
        }
        for (int z = 0; z < grid.Depth; z++)
            for (int x = 0; x < grid.Width; x++)
                if (grid.Cells[x, z].Walkable && !grid.Cells[x, z].Occupied)
                    return new Vector2Int(x, z);
        return new Vector2Int(-1, -1);
    }
}
