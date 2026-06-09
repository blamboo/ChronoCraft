// GathererBehavior.cs
// Version: 0.5 (initial -- gather FSM)
// Purpose: Plain-C# state machine that drives one Agent through the gather loop:
//          seek the nearest reachable ResourceNode of the target type, walk to it,
//          harvest one unit per logical tick until the inventory is full or the node
//          is depleted, then seek again. Fully sim-side; no MonoBehaviour, no rendering.
//          Temporary: on inventory-full the stock is cleared in place of a real drop-off;
//          that phase is replaced in the construction slice.
// Location: Assets/Scripts/Simulation/GathererBehavior.cs
// Dependencies: System.Collections.Generic; UnityEngine for Vector2Int/Mathf only.
//               Agent, Simulation, ResourceNode, GridData, Pathfinder.
// Events emitted: none. Events consumed: Simulation.OnTick (for per-tick harvesting).

using System.Collections.Generic;
using UnityEngine;

public class GathererBehavior
{
    public enum State { Seeking, Moving, Harvesting }
    public State CurrentState { get; private set; } = State.Seeking;

    // Units harvested from the node per logical tick while in Harvesting state.
    public int UnitsPerTick = 1;

    private readonly Agent      agent;
    private readonly Simulation sim;
    private readonly GridData   grid;
    private readonly ResourceType targetType;

    private ResourceNode currentNode;

    // Throttle the pathfind search so it runs at most twice per game-second rather
    // than every fixed step (~60 Hz). Seeking is a scan over all nodes + one A*;
    // this keeps the cost negligible even with many agents in the future.
    private float seekCooldown;
    private const float SeekInterval = 0.5f;

    public GathererBehavior(Agent agent, Simulation sim, GridData grid, ResourceType targetType)
    {
        this.agent      = agent;
        this.sim        = sim;
        this.grid       = grid;
        this.targetType = targetType;
        sim.OnTick += OnTick;
    }

    // Call when this behavior is no longer needed.
    public void Dispose() => sim.OnTick -= OnTick;

    // Called every fixed sim step by Simulation.Advance. Handles movement-based
    // transitions; harvesting itself is driven by OnTick.
    public void Update(float dt)
    {
        switch (CurrentState)
        {
            case State.Seeking:
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;
                TrySeek();
                break;

            case State.Moving:
                if (!agent.HasPath)
                    CurrentState = State.Harvesting;
                break;

            case State.Harvesting:
                // Actual harvest happens in OnTick. Leave early if node is gone.
                if (currentNode == null || currentNode.Depleted)
                    CurrentState = State.Seeking;
                break;
        }
    }

    // ── private ────────────────────────────────────────────────────────────────

    void TrySeek()
    {
        currentNode = FindNearest(targetType);
        if (currentNode == null) return;

        var start = new Vector2Int(agent.CellX, agent.CellZ);
        var goal  = new Vector2Int(currentNode.CellX, currentNode.CellZ);
        var path  = Pathfinder.FindPath(grid, start, goal);
        if (path == null) { currentNode = null; return; } // unreachable; retry next interval

        agent.SetPath(path);
        CurrentState = State.Moving;
    }

    void OnTick()
    {
        if (CurrentState != State.Harvesting) return;
        if (currentNode == null || currentNode.Depleted)
        {
            CurrentState = State.Seeking;
            return;
        }

        int taken = currentNode.Harvest(UnitsPerTick);
        agent.AddResource(targetType, taken);

        if (agent.InventoryFull)
        {
            // TEMPORARY: clear inventory to keep the loop running. Slice 5 replaces
            // this with a walk-to-build-site phase where the wood is actually deposited.
            agent.ClearInventory();
            CurrentState = State.Seeking;
        }
    }

    // Returns the nearest non-depleted node of the target type by straight-line
    // distance in cell space. Does not guarantee reachability; TrySeek handles that.
    ResourceNode FindNearest(ResourceType type)
    {
        ResourceNode best   = null;
        float        bestSq = float.MaxValue;

        foreach (var node in sim.ResourceNodes)
        {
            if (node.Type != type || node.Depleted) continue;
            float dx = node.CellX - agent.CellX;
            float dz = node.CellZ - agent.CellZ;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; best = node; }
        }
        return best;
    }
}
