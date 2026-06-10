// AgentBehavior.cs
// Version: 0.10 (added Resting intent + day/night schedule: rest at home at night or when
//                exhausted; v0.9 = continuous Drink>Eat>Work decision controller)
// Purpose: Plain-C# per-agent decision controller (the DecisionSystem, one per agent).
//          Each step it picks an INTENT by priority and executes it:
//            1 Drinking  (Thirst >= threshold)        -> walk to nearest water, drink.
//            2 Eating    (Hunger >= threshold)         -> walk to nearest food, eat.
//            3 Resting   (night, or Stamina exhausted) -> go home, recover Stamina.
//            4 Working   (daytime, nothing pressing)   -> gather wood, build the civ's
//                                                         structure, then idle at home.
//          Survival (Drink/Eat) preempts everything; Work never runs at night. Needs are
//          drained by NeedsSystem; this controller sets Agent.IsResting for Stamina.
// Location: Assets/Scripts/Simulation/AgentBehavior.cs
// Dependencies: System.Collections.Generic; UnityEngine (Vector2Int/Mathf only).
//               Agent, Simulation (Clock), ResourceNode, StructureNode, GridData, Pathfinder.
// Events: none.

using System.Collections.Generic;
using UnityEngine;

public class AgentBehavior
{
    public enum Intent { Working, Drinking, Eating, Resting }
    public Intent CurrentIntent { get; private set; } = Intent.Working;

    public string Action { get; private set; } = "Working";

    // Tuning (set by AgentManager).
    public float ThirstThreshold        = 50f;
    public float HungerThreshold        = 50f;
    public float StaminaRestThreshold   = 20f;  // drop to this -> rest even by day
    public float StaminaWakeThreshold   = 80f;  // recover to this -> stop a daytime rest
    public float HarvestDurationSeconds = 10f;
    public float DrinkDurationSeconds   = 5f;
    public float EatDurationSeconds     = 5f;

    private enum Work { SeekWood, MoveToWood, HarvestWood, MoveToSite, Building, GoHome, Idle }
    private enum Sub  { Seek, MoveTo, Act }

    private readonly Agent      agent;
    private readonly Simulation sim;
    private readonly GridData   grid;

    private Work work = Work.SeekWood;
    private Sub  sub  = Sub.Seek;

    private ResourceNode  currentNode;
    private StructureNode currentStructure;
    private float actTimer;
    private float seekCooldown;
    private const float SeekInterval = 0.5f;

    public AgentBehavior(Agent agent, Simulation sim, GridData grid)
    {
        this.agent = agent; this.sim = sim; this.grid = grid;
    }

    public void Dispose() { ReleaseNode(); }

    public void Update(float dt)
    {
        Intent desired = ChooseIntent();
        if (desired != CurrentIntent) { Abandon(); CurrentIntent = desired; }

        switch (CurrentIntent)
        {
            case Intent.Drinking: UpdateDrink(dt); break;
            case Intent.Eating:   UpdateEat(dt);   break;
            case Intent.Resting:  UpdateRest(dt);  break;
            case Intent.Working:  UpdateWork(dt);  break;
        }
    }

    Intent ChooseIntent()
    {
        if (agent.Thirst >= ThirstThreshold) return Intent.Drinking;
        if (agent.Hunger >= HungerThreshold) return Intent.Eating;
        if (ShouldRest())                    return Intent.Resting;
        return Intent.Working;
    }

    // Rest at night, or when exhausted. Hysteresis: once resting, keep going until it's
    // daytime AND Stamina has climbed back to the wake threshold.
    bool ShouldRest()
    {
        bool night = sim.Clock.IsNight;
        if (CurrentIntent == Intent.Resting)
            return night || agent.Stamina < StaminaWakeThreshold;
        return night || agent.Stamina <= StaminaRestThreshold;
    }

    void Abandon()
    {
        ReleaseNode();
        agent.SetPath(null);
        agent.IsResting = false;
        sub = Sub.Seek;
        work = Work.SeekWood;
        actTimer = 0f;
        seekCooldown = 0f;
    }

    // ── Drinking ──────────────────────────────────────────────────────────────
    void UpdateDrink(float dt)
    {
        switch (sub)
        {
            case Sub.Seek:
                Action = "Drinking: seek water";
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;
                if (grid.TryFindNearestDrinkPoint(agent.CellX, agent.CellZ, out Vector2Int wcell))
                {
                    var path = Pathfinder.FindPath(grid,
                        new Vector2Int(agent.CellX, agent.CellZ), wcell);
                    if (path != null) { agent.SetPath(path); sub = Sub.MoveTo; }
                }
                break;
            case Sub.MoveTo:
                Action = "Drinking: to water";
                if (!agent.HasPath) { actTimer = 0f; sub = Sub.Act; }
                break;
            case Sub.Act:
                Action = "Drinking";
                actTimer += dt;
                if (actTimer >= DrinkDurationSeconds) { agent.Thirst = 0f; sub = Sub.Seek; }
                break;
        }
    }

    // ── Eating ────────────────────────────────────────────────────────────────
    void UpdateEat(float dt)
    {
        switch (sub)
        {
            case Sub.Seek:
                Action = "Eating: seek food";
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;
                var node = FindNearestUnclaimed(ResourceType.Food);
                if (node == null) return; // no food (Phase C: farms)
                var path = Pathfinder.FindPath(grid,
                    new Vector2Int(agent.CellX, agent.CellZ),
                    new Vector2Int(node.CellX, node.CellZ));
                if (path == null) return;
                node.TryClaim(agent); currentNode = node;
                agent.SetPath(path); sub = Sub.MoveTo;
                break;
            case Sub.MoveTo:
                Action = "Eating: to food";
                if (!agent.HasPath) { actTimer = 0f; sub = Sub.Act; }
                break;
            case Sub.Act:
                Action = "Eating";
                actTimer += dt;
                if (actTimer >= EatDurationSeconds)
                {
                    if (currentNode != null) currentNode.Harvest(1);
                    ReleaseNode();
                    agent.Hunger = 0f;
                    sub = Sub.Seek;
                }
                break;
        }
    }

    // ── Resting (go home; recover Stamina; home-only) ─────────────────────────
    void UpdateRest(float dt)
    {
        switch (sub)
        {
            case Sub.Seek:
            {
                Action = "Resting: go home";
                agent.IsResting = false;
                var home = OwnBuiltStructure();
                if (home == null) { agent.SetPath(null); sub = Sub.Act; break; } // no home yet
                var path = Pathfinder.FindPath(grid,
                    new Vector2Int(agent.CellX, agent.CellZ),
                    new Vector2Int(home.CellX, home.CellZ));
                if (path != null) { agent.SetPath(path); sub = Sub.MoveTo; }
                else sub = Sub.Act;
                break;
            }
            case Sub.MoveTo:
                Action = "Resting: to home";
                agent.IsResting = false;
                if (!agent.HasPath) sub = Sub.Act;
                break;
            case Sub.Act:
                Action = sim.Clock.IsNight ? "Resting (night)" : "Resting";
                // Stamina recovers only while actually at a home structure.
                agent.IsResting = OwnBuiltStructure() != null;
                break;
        }
    }

    // ── Working (gather wood -> build civ structure -> idle at home) ──────────
    void UpdateWork(float dt)
    {
        switch (work)
        {
            case Work.SeekWood:
                Action = "Working: seek wood";
                if (OwnUnbuiltStructure() == null) { work = Work.GoHome; return; }
                if (agent.WoodCarried >= agent.CarryCapacity) { BeginDeliver(); return; }
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;
                var node = FindNearestUnclaimed(ResourceType.Wood);
                if (node == null) { work = Work.GoHome; return; }
                var path = Pathfinder.FindPath(grid,
                    new Vector2Int(agent.CellX, agent.CellZ),
                    new Vector2Int(node.CellX, node.CellZ));
                if (path == null) return;
                node.TryClaim(agent); currentNode = node;
                agent.SetPath(path); work = Work.MoveToWood;
                break;

            case Work.MoveToWood:
                Action = "Working: to wood";
                if (!agent.HasPath) { actTimer = 0f; work = Work.HarvestWood; }
                break;

            case Work.HarvestWood:
                Action = "Working: harvest wood";
                actTimer += dt;
                if (actTimer >= HarvestDurationSeconds)
                {
                    int need  = agent.CarryCapacity - agent.WoodCarried;
                    int taken = currentNode != null ? currentNode.Harvest(need) : 0;
                    agent.AddResource(ResourceType.Wood, taken);
                    ReleaseNode();
                    if (agent.WoodCarried >= agent.CarryCapacity) BeginDeliver();
                    else work = Work.SeekWood;
                }
                break;

            case Work.MoveToSite:
                Action = "Working: to site";
                if (!agent.HasPath)
                {
                    if (currentStructure != null && !currentStructure.IsBuilt)
                    {
                        currentStructure.DepositWood(agent.WoodCarried);
                        agent.ClearInventory();
                        work = Work.Building;
                    }
                    else { agent.ClearInventory(); work = Work.GoHome; }
                }
                break;

            case Work.Building:
                Action = "Working: building";
                if (currentStructure == null) { work = Work.SeekWood; break; }
                currentStructure.AdvanceBuild(dt);
                if (currentStructure.IsBuilt) work = Work.GoHome;
                break;

            case Work.GoHome:
                Action = "Working: go home";
                {
                    var home = OwnBuiltStructure();
                    if (home != null)
                    {
                        var p = Pathfinder.FindPath(grid,
                            new Vector2Int(agent.CellX, agent.CellZ),
                            new Vector2Int(home.CellX, home.CellZ));
                        if (p != null) agent.SetPath(p);
                    }
                    work = Work.Idle;
                }
                break;

            case Work.Idle:
                Action = agent.HasPath ? "Working: go home" : "Idle (home)";
                if (!agent.HasPath && OwnUnbuiltStructure() != null) work = Work.SeekWood;
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    StructureNode OwnUnbuiltStructure() =>
        sim.StructureNodes.Find(n => n.Civ == agent.Civ && !n.IsBuilt);
    StructureNode OwnBuiltStructure() =>
        sim.StructureNodes.Find(n => n.Civ == agent.Civ && n.IsBuilt);

    void BeginDeliver()
    {
        var s = OwnUnbuiltStructure();
        if (s == null) { agent.ClearInventory(); work = Work.GoHome; return; }
        var path = Pathfinder.FindPath(grid,
            new Vector2Int(agent.CellX, agent.CellZ),
            new Vector2Int(s.CellX, s.CellZ));
        if (path == null) { agent.ClearInventory(); work = Work.SeekWood; return; }
        currentStructure = s; agent.SetPath(path); work = Work.MoveToSite;
    }

    ResourceNode FindNearestUnclaimed(ResourceType type)
    {
        ResourceNode best = null; float bestSq = float.MaxValue;
        foreach (var node in sim.ResourceNodes)
        {
            if (node.Type != type || node.Depleted) continue;
            if (node.IsClaimed && node.ClaimedBy != agent) continue;
            float dx = node.CellX - agent.CellX, dz = node.CellZ - agent.CellZ;
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
