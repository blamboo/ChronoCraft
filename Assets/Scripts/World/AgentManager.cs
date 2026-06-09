// AgentManager.cs
// Version: 0.3 (per-agent speed; continuous smooth view sync)
// Purpose: Unity bridge for prototype agents. Spawns sim-side Agents into the running
//          Simulation, spawns one placeholder primitive per agent, and each frame snaps
//          it to the agent's CONTINUOUS position (sim -> render sync; smooth, no
//          teleport). Holds no sim logic -- the Agent walks itself; this only mirrors
//          position. Same adapter role GridManager plays for GridData.
// Location: Assets/Scripts/Simulation/AgentManager.cs
// Dependencies: UnityEngine; SimulationRunner (Simulation), GridManager (GridData +
//               terrain transform), Agent, Pathfinder.
// Events emitted: none. Events consumed: none (reads SimulationRunner.Sim each frame).
// Notes: The start<->target patrol is TEMPORARY test scaffolding to make movement
//        visible; it will be replaced by the job/behaviour system. Single agent for now;
//        a per-agent AgentView component is the scalable path once there are many.

using UnityEngine;

public class AgentManager : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("Drives the Simulation. Drag the Simulation GameObject here.")]
    [SerializeField] private SimulationRunner runner;
    [Tooltip("Owns the GridData + terrain transform. Drag the ProceduralTerrain object here.")]
    [SerializeField] private GridManager gridManager;

    [Header("Agent")]
    [Tooltip("NPC movement speed in cells per game-second. Independent of the tick/day rate.")]
    [Range(0.25f, 20f)]
    [SerializeField] private float agentSpeed = 3f;
    [Tooltip("Vertical offset so the capsule sits on the surface rather than through it.")]
    [SerializeField] private float yOffset = 1f;

    [Header("Test patrol (temporary scaffolding)")]
    [Tooltip("Approximate start cell (snapped to the nearest walkable cell).")]
    [SerializeField] private Vector2Int startCell = new Vector2Int(16, 16);
    [Tooltip("Approximate patrol target cell (snapped to the nearest walkable cell).")]
    [SerializeField] private Vector2Int targetCell = new Vector2Int(48, 48);

    private Agent agent;
    private Transform view;
    private Vector2Int patrolA, patrolB;
    private bool headingToB = true;
    private bool initialized;

    void Update()
    {
        if (!initialized)
        {
            TryInitialize();
            if (!initialized) return;
        }

        // TEMPORARY: on arrival, send the agent to the other patrol point. Replaced later
        // by job-driven targets.
        if (!agent.HasPath)
        {
            Vector2Int goal = headingToB ? patrolB : patrolA;
            var path = Pathfinder.FindPath(gridManager.Grid, new Vector2Int(agent.CellX, agent.CellZ), goal);
            if (path != null) agent.SetPath(path);
            headingToB = !headingToB;
        }

        SyncView();
    }

    // Lazy init: runs once both the grid (GridManager.Start) and the sim
    // (SimulationRunner.Awake) exist, so it is safe against scene start-order.
    void TryInitialize()
    {
        if (runner == null || gridManager == null) return;

        GridData grid = gridManager.Grid;
        Simulation sim = runner.Sim;
        if (grid == null || sim == null) return;

        patrolA = NearestWalkable(grid, startCell);
        patrolB = NearestWalkable(grid, targetCell);

        agent = sim.AddAgent(patrolA.x, patrolA.y);
        agent.Speed = agentSpeed;

        view = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
        view.name = "Agent (placeholder)";
        view.SetParent(gridManager.transform, worldPositionStays: false);
        Destroy(view.GetComponent<Collider>()); // not needed in the prototype

        initialized = true;
        SyncView();
    }

    void SyncView()
    {
        if (view == null) return;
        Vector3 local = gridManager.Grid.ContinuousToLocal(agent.PosX, agent.PosZ);
        Vector3 world = gridManager.transform.TransformPoint(local);
        view.position = world + Vector3.up * yOffset;
    }

    // Returns the requested cell if walkable, else the nearest walkable cell found by an
    // outward ring search. Falls back to the clamped cell if none is walkable.
    Vector2Int NearestWalkable(GridData grid, Vector2Int c)
    {
        c.x = Mathf.Clamp(c.x, 0, grid.Width - 1);
        c.y = Mathf.Clamp(c.y, 0, grid.Depth - 1);
        if (grid.Cells[c.x, c.y].Walkable) return c;

        int maxR = Mathf.Max(grid.Width, grid.Depth);
        for (int r = 1; r <= maxR; r++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue; // ring only
                    int x = c.x + dx, z = c.y + dz;
                    if (grid.InBounds(x, z) && grid.Cells[x, z].Walkable)
                        return new Vector2Int(x, z);
                }
            }
        }
        return c;
    }
}
