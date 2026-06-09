// Pathfinder.cs
// Version: 0.2 (agent navigation slice)
// Purpose: Plain-C# A* pathfinder over the prototype's logical grid (GridData). Finds a
//          walkable, 4-connected path between two cells. Pure sim-side utility -- no
//          MonoBehaviour, no rendering -- consistent with the architecture principle.
//          Agents follow the returned path one cell per tick.
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

    // Returns a path from start to goal (inclusive of both), or null if no path exists
    // or either endpoint is out of bounds / unwalkable. 4-connected; uniform step cost.
    public static List<Vector2Int> FindPath(GridData grid, Vector2Int start, Vector2Int goal)
    {
        if (grid == null) return null;
        if (!IsWalkable(grid, start) || !IsWalkable(grid, goal)) return null;
        if (start == goal) return new List<Vector2Int> { start };

        var open = new List<Vector2Int> { start };
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, int> { [start] = 0 };
        var fScore = new Dictionary<Vector2Int, int> { [start] = Heuristic(start, goal) };

        // Linear-scan open set: fine for prototype grid sizes. Swap for a binary heap if
        // grids grow large (flagged as the scalable path, not needed now).
        while (open.Count > 0)
        {
            Vector2Int current = open[0];
            int bestF = fScore.TryGetValue(current, out var cf) ? cf : int.MaxValue;
            foreach (var node in open)
            {
                int f = fScore.TryGetValue(node, out var nf) ? nf : int.MaxValue;
                if (f < bestF) { bestF = f; current = node; }
            }

            if (current == goal) return Reconstruct(cameFrom, current);

            open.Remove(current);
            int currentG = gScore[current];

            foreach (var dir in Dirs)
            {
                Vector2Int next = current + dir;
                if (!IsWalkable(grid, next)) continue;

                int tentativeG = currentG + 1;
                if (gScore.TryGetValue(next, out var existing) && tentativeG >= existing) continue;

                cameFrom[next] = current;
                gScore[next] = tentativeG;
                fScore[next] = tentativeG + Heuristic(next, goal);
                if (!open.Contains(next)) open.Add(next);
            }
        }
        return null;
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
