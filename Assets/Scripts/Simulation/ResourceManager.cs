// ResourceManager.cs
// Version: 0.5 (added Stone/ore nodes scattered on hill cells; reuses wood/food scatter)
// Purpose: Unity bridge for prototype resource nodes. Scatters sim-side ResourceNodes
//          (seed-based, reproducible) and spawns a placeholder primitive per node:
//            - Wood / Food on walkable cells (marked occupied so structures avoid them).
//            - Stone (ore) on UNWALKABLE hill cells (not water) that have at least one
//              walkable neighbour, so a future Miner can stand adjacent to harvest it
//              (same access pattern as drinking beside water).
//          Holds no sim logic -- nodes are passive data.
// Location: Assets/Scripts/Simulation/ResourceManager.cs
// Dependencies: UnityEngine; SimulationRunner, GridManager, ResourceNode, GridData.
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

    [Header("Stone (ore) nodes -- spawn on hills")]
    [Range(0, 50)]
    [SerializeField] private int stoneCount = 8;

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
        SpawnStoneNodes(sim, grid, rng, stoneCount);

        initialized = true;
    }

    // Wood / Food: walkable, unoccupied cells.
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

            sim.AddResourceNode(type, cell.x, cell.y, amountPerNode);
            grid.SetOccupied(cell.x, cell.y, true);

            SpawnPlaceholder(grid, type, cell, color, primitiveType, scale, yOff);
        }
    }

    // Stone: unwalkable hill cells (not water) with a walkable neighbour to mine from.
    void SpawnStoneNodes(Simulation sim, GridData grid, System.Random rng, int count)
    {
        Color color = new Color(0.5f, 0.5f, 0.52f);              // grey
        Vector3 scale = new Vector3(0.7f, 0.7f, 0.7f);
        float yOff = 0.4f;

        for (int i = 0; i < count; i++)
        {
            Vector2Int cell = RandomHillCell(grid, rng);
            if (cell.x < 0) break; // no suitable hill cell found

            sim.AddResourceNode(ResourceType.Stone, cell.x, cell.y, amountPerNode);
            grid.SetOccupied(cell.x, cell.y, true);

            SpawnPlaceholder(grid, ResourceType.Stone, cell, color,
                             PrimitiveType.Cube, scale, yOff);
        }
    }

    void SpawnPlaceholder(GridData grid, ResourceType type, Vector2Int cell, Color color,
                          PrimitiveType primitiveType, Vector3 scale, float yOff)
    {
        var obj = GameObject.CreatePrimitive(primitiveType);
        obj.name = $"{type} Node ({cell.x},{cell.y})";
        obj.transform.SetParent(gridManager.transform, worldPositionStays: false);
        Destroy(obj.GetComponent<Collider>()); // not needed in the prototype

        Vector3 local = grid.CellToLocal(cell.x, cell.y);
        obj.transform.localPosition = new Vector3(local.x, local.y + yOff, local.z);
        obj.transform.localScale = scale;

        // Tint via MaterialPropertyBlock so no extra material instances are created.
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        obj.GetComponent<Renderer>().SetPropertyBlock(mpb);
    }

    // Random walkable, unoccupied cell; falls back to a linear scan. (-1,-1) if full.
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

    // Random unwalkable, non-water, unoccupied hill cell that has a walkable neighbour
    // (so it is reachable for mining). Falls back to a linear scan. (-1,-1) if none.
    Vector2Int RandomHillCell(GridData grid, System.Random rng)
    {
        const int maxAttempts = 1000;
        for (int i = 0; i < maxAttempts; i++)
        {
            int x = rng.Next(0, grid.Width);
            int z = rng.Next(0, grid.Depth);
            if (IsMineableHill(grid, x, z)) return new Vector2Int(x, z);
        }
        for (int z = 0; z < grid.Depth; z++)
            for (int x = 0; x < grid.Width; x++)
                if (IsMineableHill(grid, x, z)) return new Vector2Int(x, z);
        return new Vector2Int(-1, -1);
    }

    bool IsMineableHill(GridData grid, int x, int z)
    {
        GridCell c = grid.Cells[x, z];
        return !c.Walkable && !c.IsWater && !c.Occupied
               && grid.TryGetWalkableNeighbor(x, z, out _);
    }
}
