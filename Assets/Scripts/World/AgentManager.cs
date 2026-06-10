// AgentManager.cs
// Version: 0.9 (attaches an AgentView debug component to each capsule so per-agent
//               state is visible in the Inspector; v0.8 registers civ anchors)
// Purpose: Unity bridge that bootstraps the two-civ population. Spawns agentsPerCiv
//          agents for Civ1 and Civ2 at opposite map edges, tints each civ's placeholder
//          capsules, attaches an AgentBehavior to each (the v1 brain), and each frame
//          syncs every capsule to its agent's continuous position.
//          TRANSITIONAL (A3a): every agent still runs the v1 brain and targets the single
//          shared StructureManager structure and the shared resource nodes, so the two
//          civs will clump at one structure. Per-civ structures come in A3b; the
//          needs/jobs/schedule rewrite is Phase B-C.
// Location: Assets/Scripts/World/AgentManager.cs
// Dependencies: UnityEngine; System.Collections.Generic; SimulationRunner, GridManager,
//               Agent, AgentBehavior, CivId.
// Events: none.

using System.Collections.Generic;
using UnityEngine;

public class AgentManager : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("Drag the Simulation GameObject here.")]
    [SerializeField] private SimulationRunner runner;
    [Tooltip("Drag the ProceduralTerrain GameObject here.")]
    [SerializeField] private GridManager gridManager;

    [Header("Civ spawn")]
    [Tooltip("Agents spawned for EACH civ (so 12 here = 24 total).")]
    [Range(1, 50)]
    [SerializeField] private int agentsPerCiv = 12;
    [Tooltip("How many cells in from the left/right edge each civ's group clusters.")]
    [Range(1, 60)]
    [SerializeField] private int edgeMargin = 8;
    [Tooltip("Civ1 capsule tint (spawns on the west/low-X edge).")]
    [SerializeField] private Color civ1Color = new Color(0.30f, 0.55f, 0.95f); // blue
    [Tooltip("Civ2 capsule tint (spawns on the east/high-X edge).")]
    [SerializeField] private Color civ2Color = new Color(0.90f, 0.45f, 0.25f); // orange

    [Header("Agent")]
    [Tooltip("Movement speed in cells per game-second (independent of tick rate).")]
    [Range(0.25f, 20f)]
    [SerializeField] private float agentSpeed = 3f;
    [Tooltip("Vertical offset so the capsule sits on the surface.")]
    [SerializeField] private float yOffset = 1f;

    [Header("Behavior tuning")]
    [Tooltip("Game-seconds spent at a resource node per gathering visit.")]
    [Range(1f, 60f)]
    [SerializeField] private float harvestDurationSeconds = 10f;
    [Tooltip("Hunger added per logical tick. Tune to ticksPerDay: at 450 ticks/day, " +
             "~0.25 drains 0->100 over ~0.9 day. (Was 10, tuned for the old 24-tick day.)")]
    [Range(0.05f, 50f)]
    [SerializeField] private float hungerDrainPerTick = 0.25f;
    [Tooltip("Hunger level (0-100) that triggers the agent to seek food.")]
    [Range(1f, 100f)]
    [SerializeField] private float hungerThreshold = 50f;

    [Header("Read-out (Play mode -- editing has no effect)")]
    [SerializeField] private int civ1Agents;
    [SerializeField] private int civ2Agents;

    private class View { public Agent Agent; public Transform Tf; }
    private readonly List<View> views = new List<View>();
    private bool initialized;

    void Update()
    {
        if (!initialized) { TryInitialize(); if (!initialized) return; }
        SyncViews();
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

        initialized = true;
        SyncViews();
    }

    // Spawns up to agentsPerCiv agents on walkable cells, ringing outward from the anchor
    // so the group clusters. Returns the number actually placed.
    int SpawnCiv(Simulation sim, GridData grid, CivId civ, Vector2Int anchor, Color color)
    {
        // Record this civ's home anchor so StructureManager places its structure here.
        sim.RegisterCiv(civ, anchor.x, anchor.y);

        int placed = 0;
        int maxR = Mathf.Max(grid.Width, grid.Depth);
        for (int r = 0; r <= maxR && placed < agentsPerCiv; r++)
        {
            for (int dz = -r; dz <= r && placed < agentsPerCiv; dz++)
            for (int dx = -r; dx <= r && placed < agentsPerCiv; dx++)
            {
                if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue; // ring perimeter only
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
        Agent agent  = sim.AddAgent(cell.x, cell.y);
        agent.Speed  = agentSpeed;
        agent.Civ    = civ;

        AgentBehavior b = sim.AddAgentBehavior(agent, grid);
        b.HarvestDurationSeconds = harvestDurationSeconds;
        b.HungerDrainPerTick     = hungerDrainPerTick;
        b.HungerThreshold        = hungerThreshold;

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = $"Agent ({civ})";
        go.transform.SetParent(gridManager.transform, worldPositionStays: false);
        Destroy(go.GetComponent<Collider>());

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        go.GetComponent<Renderer>().SetPropertyBlock(mpb);

        // Live per-agent read-out: select the capsule to see its state in the Inspector.
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
}
