// AgentManager.cs
// Version: 0.5 (patrol replaced by GathererBehavior; added inventory read-outs)
// Purpose: Unity bridge for prototype agents. Spawns one Agent, attaches a
//          GathererBehavior to drive it, and each frame snaps the placeholder capsule
//          to the agent's continuous position and mirrors state/inventory to the
//          Inspector for live observation. No sim logic lives here.
// Location: Assets/Scripts/Simulation/AgentManager.cs
// Dependencies: UnityEngine; SimulationRunner, GridManager, Agent, GathererBehavior.
// Events: none.

using UnityEngine;

public class AgentManager : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("Drag the Simulation GameObject here.")]
    [SerializeField] private SimulationRunner runner;
    [Tooltip("Drag the ProceduralTerrain GameObject here.")]
    [SerializeField] private GridManager gridManager;

    [Header("Agent")]
    [Tooltip("NPC movement speed in cells per game-second. Independent of tick rate.")]
    [Range(0.25f, 20f)]
    [SerializeField] private float agentSpeed = 3f;
    [Tooltip("Vertical offset so the capsule sits on the surface.")]
    [SerializeField] private float yOffset = 1f;
    [Tooltip("Spawn cell (snapped to nearest walkable).")]
    [SerializeField] private Vector2Int startCell = new Vector2Int(16, 16);
    [Tooltip("Which resource type this agent gathers.")]
    [SerializeField] private ResourceType targetResourceType = ResourceType.Wood;

    [Header("Read-out (Play mode -- editing has no effect)")]
    [SerializeField] private string agentState;
    [SerializeField] private int    woodCarried;
    [SerializeField] private int    foodCarried;

    private Agent            agent;
    private GathererBehavior behavior;
    private Transform        view;
    private bool             initialized;

    void Update()
    {
        if (!initialized) { TryInitialize(); if (!initialized) return; }

        // Mirror sim state to the Inspector for live observation.
        if (behavior != null)
        {
            agentState  = behavior.CurrentState.ToString();
            woodCarried = agent.WoodCarried;
            foodCarried = agent.FoodCarried;
        }

        SyncView();
    }

    void TryInitialize()
    {
        if (runner == null || gridManager == null) return;
        GridData   grid = gridManager.Grid;
        Simulation sim  = runner.Sim;
        if (grid == null || sim == null) return;

        Vector2Int spawnCell = NearestWalkable(grid, startCell);
        agent           = sim.AddAgent(spawnCell.x, spawnCell.y);
        agent.Speed     = agentSpeed;
        behavior        = sim.AddGathererBehavior(agent, grid, targetResourceType);

        view      = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
        view.name = "Agent (placeholder)";
        view.SetParent(gridManager.transform, worldPositionStays: false);
        Destroy(view.GetComponent<Collider>());

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

    void OnDestroy()
    {
        behavior?.Dispose();
    }
}
