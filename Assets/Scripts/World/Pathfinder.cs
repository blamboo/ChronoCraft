// Pathfinder.cs
// Version: 0.3 (binary-heap open set — was a linear scan; large-grid performance fix)
// Purpose: Plain-C# A* pathfinder over the prototype's logical grid (GridData). Finds a
//          walkable, 4-connected path between two cells. Pure sim-side utility -- no
//          MonoBehaviour, no rendering -- consistent with the architecture principle.
//          v0.3 replaces the O(open) linear minimum scan with a binary min-heap + closed
//          set (lazy deletion), so cross-map paths on large grids stay cheap. This is the
//          scalable-path work flagged in the architecture doc, made necessary once armies
//          march across the full map.
// Location: Assets/Scripts/World/Pathfinder.cs
// Dependencies: System.Collections.Generic; UnityEngine for Vector2Int/Mathf only.
//               Reads GridData (InBounds, Cells[].Walkable).
// Events: none. Stateless; call FindPath().

using System.Collections.Generic;
using UnityEngine;

public static class Pathfinder
{
    static readonly Vector2Int[] Dirs =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    private struct HeapItem { public int F; public Vector2Int Cell; }

    // Returns a path from start to goal (inclusive of both), or null if no path exists
    // or either endpoint is out of bounds / unwalkable. 4-connected; uniform step cost.
    public static List<Vector2Int> FindPath(GridData grid, Vector2Int start, Vector2Int goal)
    {
        if (grid == null) return null;
        if (!IsWalkable(grid, start) || !IsWalkable(grid, goal)) return null;
        if (start == goal) return new List<Vector2Int> { start };

        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore   = new Dictionary<Vector2Int, int> { [start] = 0 };
        var closed   = new HashSet<Vector2Int>();
        var open     = new List<HeapItem>();           // binary min-heap by F
        Push(open, new HeapItem { F = Heuristic(start, goal), Cell = start });

        while (open.Count > 0)
        {
            Vector2Int current = Pop(open).Cell;
            if (current == goal) return Reconstruct(cameFrom, current);
            if (!closed.Add(current)) continue;        // already finalized (stale heap entry)

            int currentG = gScore[current];
            for (int d = 0; d < Dirs.Length; d++)
            {
                Vector2Int next = current + Dirs[d];
                if (closed.Contains(next) || !IsWalkable(grid, next)) continue;

                int tentativeG = currentG + 1;
                int existing;
                if (gScore.TryGetValue(next, out existing) && tentativeG >= existing) continue;

                cameFrom[next] = current;
                gScore[next]   = tentativeG;
                Push(open, new HeapItem { F = tentativeG + Heuristic(next, goal), Cell = next });
            }
        }
        return null;
    }

    // ── Binary min-heap on a List<HeapItem>, ordered by F ─────────────────────────
    static void Push(List<HeapItem> heap, HeapItem item)
    {
        heap.Add(item);
        int i = heap.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (heap[parent].F <= heap[i].F) break;
            HeapItem t = heap[parent]; heap[parent] = heap[i]; heap[i] = t;
            i = parent;
        }
    }

    static HeapItem Pop(List<HeapItem> heap)
    {
        HeapItem root = heap[0];
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);
        int n = heap.Count, i = 0;
        while (true)
        {
            int l = 2 * i + 1, r = 2 * i + 2, smallest = i;
            if (l < n && heap[l].F < heap[smallest].F) smallest = l;
            if (r < n && heap[r].F < heap[smallest].F) smallest = r;
            if (smallest == i) break;
            HeapItem t = heap[smallest]; heap[smallest] = heap[i]; heap[i] = t;
            i = smallest;
        }
        return root;
    }

    static bool IsWalkable(GridData grid, Vector2Int c) =>
        grid.InBounds(c.x, c.y) && grid.Cells[c.x, c.y].Walkable;

    // Manhattan distance, admissible for 4-connected uniform-cost movement.
    static int Heuristic(Vector2Int a, Vector2Int b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    static List<Vector2Int> Reconstruct(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        var path = new List<Vector2Int> { current };
        while (cameFrom.TryGetValue(current, out var prev))
        {
            current = prev;
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}
