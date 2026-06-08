// GridData.cs
// Purpose: Plain-C# logical grid for the TimeCraft prototype. Holds one cell per
//          terrain quad (height, walkability, occupancy) plus the cell<->local
//          mapping. This is simulation/spatial data, deliberately decoupled from
//          MonoBehaviours and rendering per the architecture principle, so it can
//          later move to DOTS and be snapshotted for time-rewind.
// Location: Assets/Scripts/World/GridData.cs
// Dependencies: UnityEngine for the Vector math types only (Vector3, Vector2Int).
//               No MonoBehaviour, no rendering, no scene references.
// Events: none. Owned and driven by GridManager.

using UnityEngine;

public struct GridCell
{
    public float Height;   // local-space Y of the cell centre (matches the terrain mesh)
    public bool Walkable;  // false if the cell is too steep to traverse
    public bool Occupied;  // reserved: set later when a building/resource claims the cell
}

public class GridData
{
    public int Width { get; private set; }
    public int Depth { get; private set; }
    public float CellSize { get; private set; }

    // Centering offsets, identical to the terrain mesh so the grid lines up with it.
    public float OffsetX { get; private set; }
    public float OffsetZ { get; private set; }

    public GridCell[,] Cells { get; private set; }

    // Builds the grid from the terrain's per-vertex local heights (already post
    // height-multiplier). 'vertexHeights' is sized (Width+1) x (Depth+1).
    // A cell is unwalkable if the height spread across its four corners exceeds
    // maxStepHeight (world units), which is how steep terrain is flagged.
    public void Build(float[,] vertexHeights, float cellSize, float maxStepHeight)
    {
        int vx = vertexHeights.GetLength(0);
        int vz = vertexHeights.GetLength(1);
        Width = vx - 1;
        Depth = vz - 1;
        CellSize = cellSize;
        OffsetX = Width * cellSize * 0.5f;
        OffsetZ = Depth * cellSize * 0.5f;

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

                Cells[x, z] = new GridCell
                {
                    Height = avg,
                    Walkable = (max - min) <= maxStepHeight,
                    Occupied = false
                };
            }
        }
    }

    public bool InBounds(int x, int z) => x >= 0 && x < Width && z >= 0 && z < Depth;

    // Local-space centre of a cell, relative to the grid/terrain object's transform.
    public Vector3 CellToLocal(int x, int z)
    {
        float h = InBounds(x, z) ? Cells[x, z].Height : 0f;
        return new Vector3((x + 0.5f) * CellSize - OffsetX, h, (z + 0.5f) * CellSize - OffsetZ);
    }

    // Local-space position -> the cell that contains it (may be out of bounds; check InBounds).
    public Vector2Int LocalToCell(Vector3 local)
    {
        int x = Mathf.FloorToInt((local.x + OffsetX) / CellSize);
        int z = Mathf.FloorToInt((local.z + OffsetZ) / CellSize);
        return new Vector2Int(x, z);
    }
}
