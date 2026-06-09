// AgentManager.cs
// Version: 0.6 (wired to AgentBehavior; hunger and state read-outs; patrol removed)
// Purpose: Unity bridge for the prototype NPC. Spawns one Agent, attaches an
//          AgentBehavior, and each frame syncs the placeholder capsule to the agent's
//          continuous position and mirrors state, inventory, and hunger to the Inspector.
// Location: Assets/Scripts/World/AgentManager.cs
// Dependencies: UnityEngine; SimulationRunner, GridManager, Agent, AgentBehavior.
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
    [Tooltip("Movement speed in cells per game-second (independent of tick rate).")]
    [Range(0.25f, 20f)]
    [SerializeField] private float agentSpeed = 3f;
    [Tooltip("Vertical offset so the capsule sits on the surface.")]
    [SerializeField] private float yOffset = 1f;
    [Tooltip("Spawn cell (snapped to nearest walkable).")]
    [SerializeField] private Vector2Int startCell = new Vector2Int(16, 16);

    [Header("Behavior tuning")]
    [Tooltip("Game-seconds spent at a resource node per gathering visit.")]
    [Range(1f, 60f)]
    [SerializeField] private float harvestDurationSeconds = 10f;
    [Tooltip("Hunger added per logical tick. Tick rate set on the Simulation object.")]
    [Range(1f, 50f)]
    [SerializeField] private float hungerDrainPerTick = 10f;
    [Tooltip("Hunger level (0-100) that triggers the agent to seek food.")]
    [Range(1f, 100f)]
    [SerializeField] private float hungerThreshold = 50f;

    [Header("Read-out (Play mode -- editing has no effect)")]
    [SerializeField] private string agentState;
    [SerializeField] private int    woodCarried;
    [SerializeField] private int    foodCarried;
    [SerializeField] private float  hunger;

    private Agent        agent;
    private AgentBehavior behavior;
    private Transform    view;
    private bool         initialized;

    void Update()
    {
        if (!initialized) { TryInitialize(); if (!initialized) return; }

        if (behavior != null)
        {
            agentState  = behavior.CurrentState.ToString();
            woodCarried = agent.WoodCarried;
            foodCarried = agent.FoodCarried;
            hunger      = agent.Hunger;
        }

        SyncView();
    }

    void TryInitialize()
    {
        if (runner == null || gridManager == null) return;
        GridData   grid = gridManager.Grid;
        Simulation sim  = runner.Sim;
        if (grid == null || sim == null) return;

        Vector2Int spawn = NearestWalkable(grid, startCell);
        agent       = sim.AddAgent(spawn.x, spawn.y);
        agent.Speed = agentSpeed;

        behavior = sim.AddAgentBehavior(agent, grid);
        behavior.HarvestDurationSeconds = harvestDurationSeconds;
        behavior.HungerDrainPerTick     = hungerDrainPerTick;
        behavior.HungerThreshold        = hungerThreshold;

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
        view.position = gridManager.transform.TransformPoint(local) + Vector3.up * yOffset;
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

    void OnDestroy() => behavior?.Dispose();
}
