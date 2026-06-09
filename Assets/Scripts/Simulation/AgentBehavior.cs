// AgentBehavior.cs
// Version: 0.6 (initial -- full NPC lifecycle: wood → build → shelter → food loop)
// Purpose: Plain-C# state machine driving the prototype NPC through its full lifecycle:
//          gather 3 wood → walk to build site → build shelter (20 s) → shelter inside →
//          hunger drains each tick → leave to gather food → eat → return home → repeat.
//          Replaces GathererBehavior (delete that file from your project).
//          Pure sim-side; no MonoBehaviour, no rendering.
// Location: Assets/Scripts/Simulation/AgentBehavior.cs
// Dependencies: System.Collections.Generic; UnityEngine (Vector2Int/Mathf only).
//               Agent, Simulation, ResourceNode, StructureNode, GridData, Pathfinder.
// Events consumed: Simulation.OnTick (hunger drain).

using System.Collections.Generic;
using UnityEngine;

public class AgentBehavior
{
    public enum State
    {
        SeekWood, MoveToWood, HarvestWood,   // gather phase
        MoveToSite, Building,                 // construction phase
        InHome,                               // shelter phase
        SeekFood, MoveToFood, HarvestFood,   // feed phase
        ReturnHome                            // return after eating
    }

    public State CurrentState { get; private set; } = State.SeekWood;

    // Time the agent spends at a resource node per visit (game-seconds).
    public float HarvestDurationSeconds = 10f;
    // Hunger added each logical tick. Stat drain is tick-based per design.
    public float HungerDrainPerTick = 10f;
    // Hunger level that triggers the feed phase.
    public float HungerThreshold = 50f;

    private readonly Agent      agent;
    private readonly Simulation sim;
    private readonly GridData   grid;

    private ResourceNode  currentNode;
    private StructureNode currentStructure;

    private float harvestTimer;
    private float seekCooldown;
    private const float SeekInterval = 0.5f; // max 2 seek-attempts per game-second

    public AgentBehavior(Agent agent, Simulation sim, GridData grid)
    {
        this.agent = agent;
        this.sim   = sim;
        this.grid  = grid;
        sim.OnTick += OnTick;
    }

    public void Dispose() => sim.OnTick -= OnTick;

    // Called every fixed sim step by Simulation.Advance (after agent movement).
    public void Update(float dt)
    {
        switch (CurrentState)
        {
            // ── Wood gather ────────────────────────────────────────────────────
            case State.SeekWood:
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;
                TrySeekResource(ResourceType.Wood, State.MoveToWood);
                break;

            case State.MoveToWood:
                if (!agent.HasPath) { harvestTimer = 0f; CurrentState = State.HarvestWood; }
                break;

            case State.HarvestWood:
                harvestTimer += dt;
                if (harvestTimer >= HarvestDurationSeconds)
                {
                    int need  = agent.CarryCapacity - agent.WoodCarried;
                    int taken = currentNode != null ? currentNode.Harvest(need) : 0;
                    agent.AddResource(ResourceType.Wood, taken);

                    if (agent.WoodCarried >= agent.CarryCapacity)
                        TryMoveToSite();
                    else
                    {
                        currentNode  = null;
                        CurrentState = State.SeekWood; // need more wood from another node
                    }
                }
                break;

            // ── Construction ──────────────────────────────────────────────────
            case State.MoveToSite:
                if (!agent.HasPath)
                {
                    currentStructure.DepositWood(agent.WoodCarried);
                    agent.ClearInventory();
                    CurrentState = State.Building;
                }
                break;

            case State.Building:
                currentStructure.AdvanceBuild(dt);
                if (currentStructure.IsBuilt) CurrentState = State.InHome;
                break;

            // ── Shelter ───────────────────────────────────────────────────────
            case State.InHome:
                if (agent.Hunger >= HungerThreshold)
                {
                    seekCooldown = 0f;
                    CurrentState = State.SeekFood;
                }
                break;

            // ── Food gather ───────────────────────────────────────────────────
            case State.SeekFood:
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;
                TrySeekResource(ResourceType.Food, State.MoveToFood);
                break;

            case State.MoveToFood:
                if (!agent.HasPath) { harvestTimer = 0f; CurrentState = State.HarvestFood; }
                break;

            case State.HarvestFood:
                harvestTimer += dt;
                if (harvestTimer >= HarvestDurationSeconds)
                {
                    if (currentNode != null) currentNode.Harvest(1);
                    agent.Hunger = 0f; // ate -- hunger reset
                    TryReturnHome();
                }
                break;

            // ── Return ────────────────────────────────────────────────────────
            case State.ReturnHome:
                if (!agent.HasPath) CurrentState = State.InHome;
                break;
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    void OnTick()
    {
        // Hunger drains every logical tick regardless of current state.
        agent.Hunger = Mathf.Min(100f, agent.Hunger + HungerDrainPerTick);
    }

    void TrySeekResource(ResourceType type, State moveState)
    {
        var node = FindNearest(type);
        if (node == null) return;
        var path = Pathfinder.FindPath(grid,
            new Vector2Int(agent.CellX, agent.CellZ),
            new Vector2Int(node.CellX,  node.CellZ));
        if (path == null) return;
        currentNode  = node;
        agent.SetPath(path);
        CurrentState = moveState;
    }

    void TryMoveToSite()
    {
        var structure = sim.StructureNodes.Find(n => !n.IsBuilt);
        if (structure == null)
        {
            // Structure not placed yet; drop wood and re-gather once it exists.
            agent.ClearInventory();
            CurrentState = State.SeekWood;
            return;
        }
        var path = Pathfinder.FindPath(grid,
            new Vector2Int(agent.CellX,    agent.CellZ),
            new Vector2Int(structure.CellX, structure.CellZ));
        if (path == null) { agent.ClearInventory(); CurrentState = State.SeekWood; return; }
        currentStructure = structure;
        agent.SetPath(path);
        CurrentState = State.MoveToSite;
    }

    void TryReturnHome()
    {
        var structure = sim.StructureNodes.Find(n => n.IsBuilt);
        if (structure == null) { CurrentState = State.InHome; return; }
        var path = Pathfinder.FindPath(grid,
            new Vector2Int(agent.CellX,    agent.CellZ),
            new Vector2Int(structure.CellX, structure.CellZ));
        currentStructure = structure;
        if (path != null) agent.SetPath(path);
        CurrentState = State.ReturnHome;
    }

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
