// AgentBehavior.cs
// Version: 0.7 (resource reservation: agents claim a node so only one gathers it at a time)
// Purpose: Plain-C# state machine driving the prototype NPC through its full lifecycle:
//          gather wood -> walk to build site -> build shelter -> shelter inside ->
//          hunger drains each tick -> leave to gather food -> eat -> return home -> repeat.
//          Pure sim-side; no MonoBehaviour, no rendering.
//          v0.7: when seeking a resource the agent picks the nearest UNCLAIMED node,
//          reserves it (ResourceNode.TryClaim), and releases it when it finishes or
//          abandons it, so agents no longer clump on a single node.
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
    public float HungerDrainPerTick = 0.25f;
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

    public void Dispose()
    {
        ReleaseNode();          // never leave a node reserved after teardown
        sim.OnTick -= OnTick;
    }

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
                    ReleaseNode();                         // done with this node either way

                    if (agent.WoodCarried >= agent.CarryCapacity)
                        TryMoveToSite();
                    else
                        CurrentState = State.SeekWood;     // need more wood from another node
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
                    ReleaseNode();
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
        var node = FindNearestUnclaimed(type);
        if (node == null) return;                 // none free -> wait and retry
        var path = Pathfinder.FindPath(grid,
            new Vector2Int(agent.CellX, agent.CellZ),
            new Vector2Int(node.CellX,  node.CellZ));
        if (path == null) return;                 // unreachable -> do not claim
        node.TryClaim(agent);                     // reserve so no other agent takes it
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

    // Nearest non-depleted node of 'type' that is not reserved by another agent.
    ResourceNode FindNearestUnclaimed(ResourceType type)
    {
        ResourceNode best   = null;
        float        bestSq = float.MaxValue;
        foreach (var node in sim.ResourceNodes)
        {
            if (node.Type != type || node.Depleted) continue;
            if (node.IsClaimed && node.ClaimedBy != agent) continue; // taken by someone else
            float dx = node.CellX - agent.CellX;
            float dz = node.CellZ - agent.CellZ;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; best = node; }
        }
        return best;
    }

    void ReleaseNode()
    {
        if (currentNode != null) { currentNode.Release(agent); currentNode = null; }
    }
}
