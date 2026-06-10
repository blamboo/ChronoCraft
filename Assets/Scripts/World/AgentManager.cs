// AgentManager.cs
// Version: 0.11 (adds Stamina drain/recover + rest thresholds for B3 day/night; shows
//                Day + Night in the read-out; v0.10 created the NeedsSystem)
// Purpose: Unity bridge that bootstraps the two-civ population and the NeedsSystem.
//          Registers each civ's spawn anchor, spawns agentsPerCiv agents per civ at
//          opposite edges (tinted), attaches an AgentBehavior (decision controller) + an
//          AgentView (debug) to each, creates one NeedsSystem (drains Hunger/Thirst per
//          tick), and syncs all capsules each frame.
// Location: Assets/Scripts/World/AgentManager.cs
// Dependencies: UnityEngine; System.Collections.Generic; SimulationRunner, GridManager,
//               Agent, AgentBehavior, AgentView, NeedsSystem, CivId.
// Events: none.

using System.Collections.Generic;
using UnityEngine;

public class AgentManager : MonoBehaviour
{
    [Header("Scene references")]
    [SerializeField] private SimulationRunner runner;
    [SerializeField] private GridManager gridManager;

    [Header("Civ spawn")]
    [Range(1, 50)]   [SerializeField] private int agentsPerCiv = 12;
    [Range(1, 60)]   [SerializeField] private int edgeMargin   = 8;
    [SerializeField] private Color civ1Color = new Color(0.30f, 0.55f, 0.95f); // blue
    [SerializeField] private Color civ2Color = new Color(0.90f, 0.45f, 0.25f); // orange

    [Header("Agent")]
    [Range(0.25f, 20f)] [SerializeField] private float agentSpeed = 3f;
    [SerializeField] private float yOffset = 1f;

    [Header("Needs (drained per tick by NeedsSystem; tune to ticksPerDay 450)")]
    [Tooltip("Hunger added per tick. ~0.25 reaches the threshold over ~0.4 day.")]
    [Range(0.05f, 50f)] [SerializeField] private float hungerDrainPerTick = 0.25f;
    [Tooltip("Thirst added per tick. Thirst is more urgent, so drain it faster than hunger.")]
    [Range(0.05f, 50f)] [SerializeField] private float thirstDrainPerTick = 0.40f;
    [Range(1f, 100f)]  [SerializeField] private float hungerThreshold = 50f;
    [Range(1f, 100f)]  [SerializeField] private float thirstThreshold = 50f;
    [Tooltip("Stamina lost per tick while active (working/walking).")]
    [Range(0f, 50f)]   [SerializeField] private float staminaDrainPerTick = 0.30f;
    [Tooltip("Stamina regained per tick while resting at home.")]
    [Range(0f, 50f)]   [SerializeField] private float staminaRecoverPerTick = 0.60f;
    [Tooltip("Stamina at/below this triggers rest even during the day.")]
    [Range(1f, 100f)]  [SerializeField] private float staminaRestThreshold = 20f;
    [Tooltip("During the day, rest ends once Stamina climbs back to this.")]
    [Range(1f, 100f)]  [SerializeField] private float staminaWakeThreshold = 80f;

    [Header("Action durations (game-seconds)")]
    [Range(1f, 60f)] [SerializeField] private float harvestDurationSeconds = 10f;
    [Range(1f, 60f)] [SerializeField] private float drinkDurationSeconds   = 5f;
    [Range(1f, 60f)] [SerializeField] private float eatDurationSeconds     = 5f;

    [Header("Read-out (Play mode)")]
    [SerializeField] private int  civ1Agents;
    [SerializeField] private int  civ2Agents;
    [SerializeField] private int  day;
    [SerializeField] private bool night;

    private class View { public Agent Agent; public Transform Tf; }
    private readonly List<View> views = new List<View>();
    private NeedsSystem needs;
    private bool initialized;

    void Update()
    {
        if (!initialized) { TryInitialize(); if (!initialized) return; }
        SyncViews();

        var clock = runner.Sim.Clock;
        day   = clock.Day;
        night = clock.IsNight;
    }

    void TryInitialize()
    {
        if (runner == null || gridManager == null) return;
        GridData   grid = gridManager.Grid;
        Simulation sim  = runner.Sim;
        if (grid == null || sim == null) return;

        int cz = grid.Depth / 2;
        civ1Agents = SpawnCiv(sim, grid, CivId.Civ1, new Vector2Int(edgeMargin, cz), civ1Color);
        civ2Agents = SpawnCiv(sim, grid, CivId.Civ2, new Vector2Int(grid.Width - 1 - edgeMargin, cz), civ2Color);

        // One system drains everyone's needs each tick.
        needs = new NeedsSystem(sim, hungerDrainPerTick, thirstDrainPerTick,
                                staminaDrainPerTick, staminaRecoverPerTick);

        initialized = true;
        SyncViews();
    }

    int SpawnCiv(Simulation sim, GridData grid, CivId civ, Vector2Int anchor, Color color)
    {
        sim.RegisterCiv(civ, anchor.x, anchor.y);

        int placed = 0;
        int maxR = Mathf.Max(grid.Width, grid.Depth);
        for (int r = 0; r <= maxR && placed < agentsPerCiv; r++)
        {
            for (int dz = -r; dz <= r && placed < agentsPerCiv; dz++)
            for (int dx = -r; dx <= r && placed < agentsPerCiv; dx++)
            {
                if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                int x = anchor.x + dx, z = anchor.y + dz;
                if (!grid.InBounds(x, z) || !grid.Cells[x, z].Walkable) continue;
                SpawnAgent(sim, grid, civ, new Vector2Int(x, z), color);
                placed++;
            }
        }
        return placed;
    }

    void SpawnAgent(Simulation sim, GridData grid, CivId civ, Vector2Int cell, Color color)
    {
        Agent agent = sim.AddAgent(cell.x, cell.y);
        agent.Speed = agentSpeed;
        agent.Civ   = civ;

        AgentBehavior b = sim.AddAgentBehavior(agent, grid);
        b.HungerThreshold        = hungerThreshold;
        b.ThirstThreshold        = thirstThreshold;
        b.StaminaRestThreshold   = staminaRestThreshold;
        b.StaminaWakeThreshold   = staminaWakeThreshold;
        b.HarvestDurationSeconds = harvestDurationSeconds;
        b.DrinkDurationSeconds   = drinkDurationSeconds;
        b.EatDurationSeconds     = eatDurationSeconds;

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = $"Agent ({civ})";
        go.transform.SetParent(gridManager.transform, worldPositionStays: false);
        Destroy(go.GetComponent<Collider>());

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        go.GetComponent<Renderer>().SetPropertyBlock(mpb);

        go.AddComponent<AgentView>().Bind(agent, b);

        views.Add(new View { Agent = agent, Tf = go.transform });
    }

    void SyncViews()
    {
        for (int i = 0; i < views.Count; i++)
        {
            View v = views[i];
            if (v.Tf == null) continue;
            Vector3 local = gridManager.Grid.ContinuousToLocal(v.Agent.PosX, v.Agent.PosZ);
            v.Tf.position = gridManager.transform.TransformPoint(local) + Vector3.up * yOffset;
        }
    }

    void OnDestroy() { needs?.Dispose(); }
}
