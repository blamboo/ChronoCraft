// GridData.cs
// Version: 0.8 (added Owner field + SetOwner for territory system)
// Purpose: Plain-C# logical grid for the TimeCraft prototype. Holds one cell per
//          terrain quad (height, walkability, occupancy, water) plus the cell<->local
//          mapping. Deliberately decoupled from MonoBehaviours and rendering per the
//          architecture principle; snapshot-friendly for time-rewind.
//          v0.5: cells at/below waterLevel are flagged IsWater and forced unwalkable;
//          IsWaterAdjacent(x,z) reports valid drink points.
//          v0.6: TryGetWalkableNeighbor(x,z,..) returns an adjacent walkable cell -- the
//          stand-on cell for harvesting an unwalkable node (stone on a hill, or a water
//          drink point). One helper for both access patterns.
// Location: Assets/Scripts/World/GridData.cs
// Dependencies: UnityEngine for Vector math types only. No MonoBehaviour, no rendering.
// Events: none. Owned and driven by GridManager.

using UnityEngine;

public struct GridCell
{
    public float Height;   // local-space Y of the cell centre (matches the terrain mesh)
    public bool Walkable;  // false if too steep, or water
    public bool Occupied;  // true when a building or resource node claims the cell
    public bool IsWater;   // true when the cell centre is at/below waterLevel
    public CivId Owner;    // None / Civ1 / Civ2 — territory ownership
}

public class GridData
{
    public int   Width    { get; private set; }
    public int   Depth    { get; private set; }
    public float CellSize { get; private set; }

    // Centering offsets, identical to the terrain mesh so the grid lines up with it.
    public float OffsetX { get; private set; }
    public float OffsetZ { get; private set; }

    public GridCell[,] Cells { get; private set; }

    // Builds the grid from the terrain's per-vertex local heights (post height-multiplier).
    // 'vertexHeights' is sized (Width+1) x (Depth+1).
    // A cell is unwalkable if it is water (centre at/below waterLevel) OR the height
    // spread across its four corners exceeds maxStepHeight (world units).
    public void Build(float[,] vertexHeights, float cellSize, float maxStepHeight, float waterLevel)
    {
        int vx = vertexHeights.GetLength(0);
        int vz = vertexHeights.GetLength(1);
        Width    = vx - 1;
        Depth    = vz - 1;
        CellSize = cellSize;
        OffsetX  = Width * cellSize * 0.5f;
        OffsetZ  = Depth * cellSize * 0.5f;

        Cells = new GridCell[Width, Depth];

        for (int z = 0; z < Depth; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                float h00 = vertexHeights[x, z];
                float h10 = vertexHeights[x + 1, z];
                float h01 = vertexHeights[x, z + 1];
                float h11 = vertexHeights[x + 1, z + 1];

                float min = Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11));
                float max = Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11));
                float avg = (h00 + h10 + h01 + h11) * 0.25f;

                bool isWater = avg <= waterLevel;

                Cells[x, z] = new GridCell
                {
                    Height   = avg,
                    Walkable = !isWater && (max - min) <= maxStepHeight,
                    Occupied = false,
                    IsWater  = isWater,
                    Owner    = CivId.None
                };
            }
        }
    }

    public bool InBounds(int x, int z) => x >= 0 && x < Width && z >= 0 && z < Depth;

    // Sets the Occupied flag without exposing the struct read-modify-write to callers.
    public void SetOccupied(int x, int z, bool occupied)
    {
        if (!InBounds(x, z)) return;
        var cell     = Cells[x, z];
        cell.Occupied = occupied;
        Cells[x, z]  = cell;
    }

    // Sets the territory Owner of a cell.
    public void SetOwner(int x, int z, CivId owner)
    {
        if (!InBounds(x, z)) return;
        var cell    = Cells[x, z];
        cell.Owner  = owner;
        Cells[x, z] = cell;
    }

    // True if (x,z) is a valid drink point: walkable and 4-adjacent to a water cell.
    // The Thirst need (Phase B) routes agents to the nearest such cell.
    public bool IsWaterAdjacent(int x, int z)
    {
        if (!InBounds(x, z) || !Cells[x, z].Walkable) return false;
        if (InBounds(x + 1, z) && Cells[x + 1, z].IsWater) return true;
        if (InBounds(x - 1, z) && Cells[x - 1, z].IsWater) return true;
        if (InBounds(x, z + 1) && Cells[x, z + 1].IsWater) return true;
        if (InBounds(x, z - 1) && Cells[x, z - 1].IsWater) return true;
        return false;
    }

    // Nearest drink point (walkable cell touching water) to (fromX, fromZ), by squared
    // grid distance. O(Width*Depth) scan -- called occasionally (when an agent gets
    // thirsty), not per frame. Returns false if the map has no water at all.
    public bool TryFindNearestDrinkPoint(int fromX, int fromZ, out Vector2Int cell)
    {
        cell = new Vector2Int(-1, -1);
        float best = float.MaxValue;
        for (int z = 0; z < Depth; z++)
        for (int x = 0; x < Width; x++)
        {
            if (!IsWaterAdjacent(x, z)) continue;
            float dx = x - fromX, dz = z - fromZ;
            float sq = dx * dx + dz * dz;
            if (sq < best) { best = sq; cell = new Vector2Int(x, z); }
        }
        return cell.x >= 0;
    }

    // Returns the first walkable 4-neighbour of (x,z) -- the cell an agent stands on to
    // harvest an unwalkable node (stone on a hill cell, or to drink beside water).
    // 'neighbor' is (-1,-1) and the method returns false if no walkable neighbour exists.
    public bool TryGetWalkableNeighbor(int x, int z, out Vector2Int neighbor)
    {
        if (InBounds(x + 1, z) && Cells[x + 1, z].Walkable) { neighbor = new Vector2Int(x + 1, z); return true; }
        if (InBounds(x - 1, z) && Cells[x - 1, z].Walkable) { neighbor = new Vector2Int(x - 1, z); return true; }
        if (InBounds(x, z + 1) && Cells[x, z + 1].Walkable) { neighbor = new Vector2Int(x, z + 1); return true; }
        if (InBounds(x, z - 1) && Cells[x, z - 1].Walkable) { neighbor = new Vector2Int(x, z - 1); return true; }
        neighbor = new Vector2Int(-1, -1);
        return false;
    }

    // Local-space centre of a cell, relative to the terrain object's transform.
    public Vector3 CellToLocal(int x, int z)
    {
        float h = InBounds(x, z) ? Cells[x, z].Height : 0f;
        return new Vector3((x + 0.5f) * CellSize - OffsetX, h,
                           (z + 0.5f) * CellSize - OffsetZ);
    }

    // Local-space position for a CONTINUOUS grid coordinate (gx, gz in cell units),
    // used by smooth agent movement. Height uses the nearest cell (bilinear deferred --
    // see the architecture doc's deferred-technical note re: smooth vertical movement).
    public Vector3 ContinuousToLocal(float gx, float gz)
    {
        int cx = Mathf.Clamp(Mathf.RoundToInt(gx), 0, Width  - 1);
        int cz = Mathf.Clamp(Mathf.RoundToInt(gz), 0, Depth  - 1);
        float h = Cells[cx, cz].Height;
        return new Vector3((gx + 0.5f) * CellSize - OffsetX, h,
                           (gz + 0.5f) * CellSize - OffsetZ);
    }

    // Local-space position -> the cell that contains it (may be out of bounds; check InBounds).
    public Vector2Int LocalToCell(Vector3 local)
    {
        int x = Mathf.FloorToInt((local.x + OffsetX) / CellSize);
        int z = Mathf.FloorToInt((local.z + OffsetZ) / CellSize);
        return new Vector2Int(x, z);
    }
}
