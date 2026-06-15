// AgentBehavior.cs
// Version: 0.18 (Prototype v5: Explorer job — roams and claims a radius-5 area of unclaimed
//                land; OwnerAgent; conscription stand-down; CombatCooldown stand-and-fight)
// Purpose: Plain-C# per-agent decision controller.
//          Priority: Drinking > Eating (critical at night) > Resting > Eating (normal) > Working.
// Location: Assets/Scripts/Simulation/AgentBehavior.cs
// Dependencies: UnityEngine (Vector2Int/Mathf); Agent, Simulation, ResourceNode,
//               StructureNode, StorageNode, GridData, Pathfinder.
// Events: none.

using System.Collections.Generic;
using UnityEngine;

public class AgentBehavior
{
    public enum Intent { Working, Drinking, Eating, Resting }
    public Intent CurrentIntent { get; private set; } = Intent.Working;
    public string Action { get; private set; } = "Working";

    // Tuning knobs (set by AgentManager).
    public float ThirstThreshold        = 50f;
    public float HungerThreshold        = 50f;
    // Hunger above this overrides rest even at night (near-starvation).
    public float HungerCriticalThreshold = 80f;
    public float StaminaRestThreshold   = 20f;
    public float StaminaWakeThreshold   = 80f;
    public float HarvestDurationSeconds = 10f;
    public float DrinkDurationSeconds   = 5f;
    public float EatDurationSeconds     = 5f;
    // How long to idle before re-seeking when there's nothing to do.
    public float IdleCooldownSeconds    = 3f;

    private enum WorkState
    {
        Seek,
        MoveToStorage,  // Builder: walking to Storage to withdraw wood
        MoveToResource,
        Harvest,
        MoveToDeposit,
        Deposit,
        MoveToSite,
        Build,
        GoHome,
        Idle
    }
    private enum Sub { Seek, MoveTo, Act }

    private readonly Agent      agent;
    private readonly Simulation sim;
    private readonly GridData   grid;

    private WorkState work     = WorkState.Seek;
    private Sub       drinkSub = Sub.Seek;
    private Sub       eatSub   = Sub.Seek;
    private Sub       restSub  = Sub.Seek;

    private ResourceNode  currentNode;
    private StructureNode currentSite;
    private float actTimer;
    private float seekCooldown;
    private float idleTimer;
    // Tick-based food suppression. NOT reset in Abandon — food availability is world
    // state, not action state. Must survive intent switches (drink interrupting eat).
    private int   noFoodTicks;
    private const int NoFoodTickDuration = 113; // ~quarter-day at 450 ticks/day
    private const float SeekInterval = 0.5f;

    // Drink-point cache — avoids O(W*D) grid scan every seek cycle.
    private Vector2Int cachedDrinkPoint = new Vector2Int(-1, -1);
    private int        drinkCacheFromX  = int.MinValue;
    private int        drinkCacheFromZ  = int.MinValue;
    private const int  DrinkCacheRadius = 3;

    // Per-agent deterministic RNG (seeded by Id) for the Explorer's wandering.
    private readonly System.Random rng;

    // Explorer state: where it is roaming and where it last stamped a claim.
    private Vector2Int exploreTarget = new Vector2Int(-1, -1);
    private int lastClaimX = int.MinValue, lastClaimZ = int.MinValue;
    private const int ExploreClaimRadius = 5;   // GDD: claims a radius-5 area as it walks

    public AgentBehavior(Agent agent, Simulation sim, GridData grid)
    {
        this.agent = agent; this.sim = sim; this.grid = grid;
        rng = new System.Random(agent.Id * 9176 + 1);
    }

    // The agent this behavior drives — used by Simulation.KillAgent to find and remove it.
    public Agent OwnerAgent => agent;

    public void Dispose() { ReleaseNode(); }

    public void Update(float dt)
    {
        // Conscripted agents are driven by the Conflict system (Ch.26); the civilian
        // decision brain stands down while they are mustered.
        if (agent.Conscripted) { Action = "Soldiering"; return; }

        // Under attack: stand and fight back (the Conflict system trades blows) rather than
        // wandering off to satisfy needs mid-battle. Cooldown is refreshed on each strike.
        if (agent.CombatCooldown > 0f)
        {
            agent.CombatCooldown -= dt;
            agent.SetPath(null);
            agent.IsResting = false;
            Action = "Defending";
            return;
        }

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

    // Called once per sim tick by AgentManager (via Simulation.OnTick).
    public void OnTick()
    {
        if (noFoodTicks > 0) noFoodTicks--;
    }

    // ── Intent selection ──────────────────────────────────────────────────────
    // Thirst always wins.
    // Critical hunger (>=HungerCriticalThreshold) wins over rest even at night.
    // At night, normal hunger loses to rest — agent sleeps hungry rather than
    // roaming for food that may not exist.
    // Below critical, hunger is only acted on during the day.
    Intent ChooseIntent()
    {
        if (agent.Thirst >= ThirstThreshold) return Intent.Drinking;

        bool foodBlocked = noFoodTicks > 0;
        if (!foodBlocked && agent.Hunger >= HungerCriticalThreshold) return Intent.Eating;
        if (ShouldRest())                                             return Intent.Resting;
        if (!foodBlocked && agent.Hunger >= HungerThreshold)          return Intent.Eating;
        return Intent.Working;
    }

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
        work         = WorkState.Seek;
        drinkSub     = Sub.Seek;
        eatSub       = Sub.Seek;
        restSub      = Sub.Seek;
        actTimer     = 0f;
        seekCooldown = 0f;
        idleTimer    = 0f;
        // noFoodTicks intentionally NOT reset — survives intent switches.
        currentSite  = null;
    }

    // ── Drinking ──────────────────────────────────────────────────────────────
    void UpdateDrink(float dt)
    {
        switch (drinkSub)
        {
            case Sub.Seek:
                Action = "Drinking: seek water";
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;
                Vector2Int wcell = GetDrinkPoint();
                if (wcell.x >= 0)
                {
                    var p = Pathfinder.FindPath(grid,
                        new Vector2Int(agent.CellX, agent.CellZ), wcell);
                    if (p != null) { agent.SetPath(p); drinkSub = Sub.MoveTo; }
                }
                break;
            case Sub.MoveTo:
                Action = "Drinking: to water";
                if (!agent.HasPath) { actTimer = 0f; drinkSub = Sub.Act; }
                break;
            case Sub.Act:
                Action = "Drinking";
                actTimer += dt;
                if (actTimer >= DrinkDurationSeconds) { agent.Thirst = 0f; drinkSub = Sub.Seek; }
                break;
        }
    }

    Vector2Int GetDrinkPoint()
    {
        int dx = agent.CellX - drinkCacheFromX;
        int dz = agent.CellZ - drinkCacheFromZ;
        if ((dx * dx + dz * dz) > DrinkCacheRadius * DrinkCacheRadius || cachedDrinkPoint.x < 0)
        {
            grid.TryFindNearestDrinkPoint(agent.CellX, agent.CellZ, out cachedDrinkPoint);
            drinkCacheFromX = agent.CellX;
            drinkCacheFromZ = agent.CellZ;
        }
        return cachedDrinkPoint;
    }

    // ── Eating ────────────────────────────────────────────────────────────────
    void UpdateEat(float dt)
    {
        switch (eatSub)
        {
            case Sub.Seek:
                Action = "Eating: seek food";
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;
                var node = FindNearestUnclaimed(ResourceType.Food);
                if (node == null)
                {
                    noFoodTicks = NoFoodTickDuration;
                    return;
                }
                var path = Pathfinder.FindPath(grid,
                    new Vector2Int(agent.CellX, agent.CellZ),
                    new Vector2Int(node.CellX, node.CellZ));
                if (path == null) return;
                node.TryClaim(agent); currentNode = node;
                noFoodTicks = 0;  // food found — unblock hunger intent
                agent.SetPath(path); eatSub = Sub.MoveTo;
                break;
            case Sub.MoveTo:
                Action = "Eating: to food";
                if (!agent.HasPath) { actTimer = 0f; eatSub = Sub.Act; }
                break;
            case Sub.Act:
                Action = "Eating";
                actTimer += dt;
                if (actTimer >= EatDurationSeconds)
                {
                    currentNode?.Harvest(1);
                    ReleaseNode();
                    agent.Hunger = 0f;
                    eatSub = Sub.Seek;
                }
                break;
        }
    }

    // ── Resting ───────────────────────────────────────────────────────────────
    void UpdateRest(float dt)
    {
        switch (restSub)
        {
            case Sub.Seek:
                Action = "Resting: go home";
                agent.IsResting = false;
                var home = Home();
                if (home == null) { agent.SetPath(null); restSub = Sub.Act; break; }
                var rpath = Pathfinder.FindPath(grid,
                    new Vector2Int(agent.CellX, agent.CellZ),
                    new Vector2Int(home.CellX, home.CellZ));
                if (rpath != null) { agent.SetPath(rpath); restSub = Sub.MoveTo; }
                else restSub = Sub.Act;
                break;
            case Sub.MoveTo:
                Action = "Resting: to home";
                agent.IsResting = false;
                if (!agent.HasPath) restSub = Sub.Act;
                break;
            case Sub.Act:
                Action = sim.Clock.IsNight ? "Resting (night)" : "Resting";
                agent.IsResting = agent.Home != null && agent.Home.IsBuilt;
                break;
        }
    }

    // ── Work dispatch ─────────────────────────────────────────────────────────
    void UpdateWork(float dt)
    {
        switch (agent.Job)
        {
            case JobRole.Logger:   UpdateGather(dt, ResourceType.Wood);  break;
            case JobRole.Farmer:   UpdateGather(dt, ResourceType.Food);  break;
            case JobRole.Miner:    UpdateGather(dt, ResourceType.Stone); break;
            case JobRole.Builder:  UpdateBuilder(dt);                    break;
            case JobRole.Explorer: UpdateExplorer(dt);                   break;
        }
    }

    // ── Explorer (Ch.8 / Phase D) ───────────────────────────────────────────────
    // Roams the map and claims a radius-5 area of UNCLAIMED land for its civ as it goes.
    // It does not seize owned cells by walking — but stepping into an enemy's territory is
    // what the Conflict system reads as an incursion (that is how wars start).
    void UpdateExplorer(float dt)
    {
        Action = "Exploring";
        ClaimAround();

        if (!agent.HasPath)
        {
            seekCooldown -= dt;
            if (seekCooldown > 0f) return;
            seekCooldown = SeekInterval;

            if (exploreTarget.x < 0 ||
                (agent.CellX == exploreTarget.x && agent.CellZ == exploreTarget.y))
                exploreTarget = PickExploreTarget();

            var path = Pathfinder.FindPath(grid,
                new Vector2Int(agent.CellX, agent.CellZ), exploreTarget);
            if (path != null) agent.SetPath(path);
            else exploreTarget = PickExploreTarget();
        }
    }

    // Claim every unclaimed, walkable cell within ExploreClaimRadius of the explorer.
    // Throttled to once per cell entered (radius scan is otherwise per-frame).
    void ClaimAround()
    {
        if (agent.CellX == lastClaimX && agent.CellZ == lastClaimZ) return;
        lastClaimX = agent.CellX; lastClaimZ = agent.CellZ;

        int r = ExploreClaimRadius, r2 = r * r;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dz * dz > r2) continue;
            int x = agent.CellX + dx, z = agent.CellZ + dz;
            if (grid.InBounds(x, z) && grid.Cells[x, z].Walkable &&
                grid.Cells[x, z].Owner == CivId.None)
                grid.SetOwner(x, z, agent.Civ);
        }
    }

    Vector2Int PickExploreTarget()
    {
        for (int tries = 0; tries < 16; tries++)
        {
            int x = rng.Next(grid.Width), z = rng.Next(grid.Depth);
            if (grid.Cells[x, z].Walkable) return new Vector2Int(x, z);
        }
        return new Vector2Int(agent.CellX, agent.CellZ);
    }

    // ── Gatherer ──────────────────────────────────────────────────────────────
    void UpdateGather(float dt, ResourceType resourceType)
    {
        StorageNode storage   = OwnStorage();
        bool        storageReady = storage != null && storage.IsBuilt;

        switch (work)
        {
            case WorkState.Seek:
                Action = $"Seeking {resourceType}";
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;

                var rnode = FindNearestUnclaimed(resourceType);
                if (rnode == null) { EnterIdle(); return; }

                Vector2Int targetCell;
                if (resourceType == ResourceType.Stone)
                {
                    if (!grid.TryGetWalkableNeighbor(rnode.CellX, rnode.CellZ, out targetCell))
                    { EnterIdle(); return; }
                }
                else targetCell = new Vector2Int(rnode.CellX, rnode.CellZ);

                var rpath = Pathfinder.FindPath(grid,
                    new Vector2Int(agent.CellX, agent.CellZ), targetCell);
                if (rpath == null) return;

                rnode.TryClaim(agent); currentNode = rnode;
                agent.SetPath(rpath); work = WorkState.MoveToResource;
                break;

            case WorkState.MoveToResource:
                Action = $"To {resourceType}";
                if (!agent.HasPath) { actTimer = 0f; work = WorkState.Harvest; }
                break;

            case WorkState.Harvest:
                Action = $"Harvesting {resourceType}";
                actTimer += dt;
                if (actTimer >= HarvestDurationSeconds)
                {
                    int need  = agent.CarryCapacity - CarriedOf(resourceType);
                    int taken = currentNode != null ? currentNode.Harvest(need) : 0;
                    agent.AddResource(resourceType, taken);
                    ReleaseNode();
                    work = CarriedOf(resourceType) > 0 ? WorkState.MoveToDeposit : WorkState.Seek;
                }
                break;

            case WorkState.MoveToDeposit:
                Action = storageReady ? "Hauling to Storage" : "Hauling to site";
                {
                    Vector2Int dest;
                    if (storageReady)
                    {
                        dest = new Vector2Int(storage.CellX, storage.CellZ);
                    }
                    else
                    {
                        if (resourceType != ResourceType.Wood)
                        { agent.ClearInventory(); EnterIdle(); return; }
                        currentSite = NearestUnbuiltStructure();
                        if (currentSite == null) { agent.ClearInventory(); EnterIdle(); return; }
                        dest = new Vector2Int(currentSite.CellX, currentSite.CellZ);
                    }
                    var dpath = Pathfinder.FindPath(grid,
                        new Vector2Int(agent.CellX, agent.CellZ), dest);
                    if (dpath == null) { agent.ClearInventory(); work = WorkState.Seek; return; }
                    agent.SetPath(dpath);
                    work = storageReady ? WorkState.Deposit : WorkState.MoveToSite;
                }
                break;

            case WorkState.Deposit:
                Action = "Depositing";
                if (!agent.HasPath)
                {
                    storage?.Deposit(resourceType, CarriedOf(resourceType));
                    agent.ClearInventory();
                    work = WorkState.Seek;
                }
                break;

            case WorkState.MoveToSite:
                Action = "Delivering wood";
                if (!agent.HasPath)
                {
                    if (currentSite != null && !currentSite.IsBuilt)
                        currentSite.DepositWood(agent.WoodCarried);
                    agent.ClearInventory();
                    currentSite = null;
                    work = WorkState.Seek;
                }
                break;

            case WorkState.Idle:
                Action = "Idle";
                idleTimer -= dt;
                if (idleTimer <= 0f) work = WorkState.Seek;
                break;

            default:
                work = WorkState.Seek;
                break;
        }
    }

    // ── Builder ───────────────────────────────────────────────────────────────
    void UpdateBuilder(float dt)
    {
        StorageNode storage   = OwnStorage();
        bool        storageReady = storage != null && storage.IsBuilt;

        switch (work)
        {
            case WorkState.Seek:
                Action = "Builder: seek site";
                seekCooldown -= dt;
                if (seekCooldown > 0f) return;
                seekCooldown = SeekInterval;

                currentSite = NextBuildTarget();
                if (currentSite == null) { EnterIdle(); return; }

                if (storageReady)
                {
                    // Post-Storage: withdraw wood from Storage, then walk to site.
                    int stillNeeded = currentSite.WoodRequired - currentSite.WoodDeposited;
                    if (stillNeeded <= 0)
                    {
                        // Wood already deposited by a previous trip — just go build.
                        RouteToBuildSite();
                        work = WorkState.MoveToSite;
                        return;
                    }
                    int canCarry = agent.CarryCapacity - agent.WoodCarried;
                    int toFetch  = Mathf.Min(stillNeeded, canCarry);
                    if (toFetch <= 0) { RouteToBuildSite(); work = WorkState.MoveToSite; return; }

                    // Walk to Storage first to pick up wood.
                    var sp = Pathfinder.FindPath(grid,
                        new Vector2Int(agent.CellX, agent.CellZ),
                        new Vector2Int(storage.CellX, storage.CellZ));
                    if (sp == null) { EnterIdle(); return; }
                    agent.SetPath(sp);
                    work = WorkState.MoveToStorage;
                }
                else
                {
                    // Pre-Storage: gather wood ourselves if we have none.
                    if (agent.WoodCarried == 0)
                    {
                        var wnode = FindNearestUnclaimed(ResourceType.Wood);
                        if (wnode == null) { EnterIdle(); return; }
                        var wp = Pathfinder.FindPath(grid,
                            new Vector2Int(agent.CellX, agent.CellZ),
                            new Vector2Int(wnode.CellX, wnode.CellZ));
                        if (wp == null) return;
                        wnode.TryClaim(agent); currentNode = wnode;
                        agent.SetPath(wp); work = WorkState.MoveToResource;
                    }
                    else { RouteToBuildSite(); work = WorkState.MoveToSite; }
                }
                break;

            // Post-Storage: arrived at Storage — withdraw wood, then route to site.
            case WorkState.MoveToStorage:
                Action = "Builder: to storage";
                if (!agent.HasPath)
                {
                    if (storage != null && currentSite != null)
                    {
                        int stillNeeded = currentSite.WoodRequired - currentSite.WoodDeposited;
                        int toFetch     = Mathf.Min(stillNeeded, agent.CarryCapacity - agent.WoodCarried);
                        int taken       = storage.WithdrawWood(Mathf.Max(0, toFetch));
                        agent.AddResource(ResourceType.Wood, taken);
                    }
                    if (agent.WoodCarried > 0) { RouteToBuildSite(); work = WorkState.MoveToSite; }
                    else { EnterIdle(); } // storage was empty
                }
                break;

            case WorkState.MoveToResource:
                Action = "Builder: to wood";
                if (!agent.HasPath) { actTimer = 0f; work = WorkState.Harvest; }
                break;

            case WorkState.Harvest:
                Action = "Builder: harvest wood";
                actTimer += dt;
                if (actTimer >= HarvestDurationSeconds)
                {
                    int need  = agent.CarryCapacity - agent.WoodCarried;
                    int taken = currentNode != null ? currentNode.Harvest(need) : 0;
                    agent.AddResource(ResourceType.Wood, taken);
                    ReleaseNode();
                    if (currentSite != null && agent.WoodCarried > 0)
                    { RouteToBuildSite(); work = WorkState.MoveToSite; }
                    else work = WorkState.Seek;
                }
                break;

            case WorkState.MoveToSite:
                Action = "Builder: to site";
                if (!agent.HasPath)
                {
                    if (currentSite != null && !currentSite.IsBuilt)
                    {
                        currentSite.DepositWood(agent.WoodCarried);
                        agent.ClearInventory();
                        // If still needs more wood, go back for another load.
                        if (!currentSite.HasEnoughWood) { work = WorkState.Seek; return; }
                        work = WorkState.Build;
                    }
                    else { agent.ClearInventory(); currentSite = null; work = WorkState.Seek; }
                }
                break;

            case WorkState.Build:
                Action = "Building";
                if (currentSite == null) { work = WorkState.Seek; break; }
                currentSite.AdvanceBuild(dt);
                if (currentSite.IsBuilt)
                {
                    // An agent finishing a building is a Town-tier History Log event (Ch.4/15).
                    // The latch keeps it to one entry when several builders share a site.
                    if (!currentSite.CompletionLogged)
                    {
                        currentSite.CompletionLogged = true;
                        sim.Log.Record(EventType.StructureCompleted, EventSignificance.Town,
                            agent.Civ + " completed a " + currentSite.Type + ".",
                            civA: currentSite.Civ, actorId: agent.Id,
                            cellX: currentSite.CellX, cellZ: currentSite.CellZ);
                    }
                    currentSite = null; work = WorkState.Seek;
                }
                break;

            case WorkState.Idle:
                Action = "Builder: idle";
                idleTimer -= dt;
                if (idleTimer <= 0f) work = WorkState.Seek;
                break;

            default:
                work = WorkState.Seek;
                break;
        }
    }

    // ── Idle helper ───────────────────────────────────────────────────────────
    void EnterIdle()
    {
        idleTimer = IdleCooldownSeconds;
        work      = WorkState.Idle;
    }

    // ── Home assignment ───────────────────────────────────────────────────────
    StructureNode Home()
    {
        if (agent.Home == null) AssignHome();
        return agent.Home;
    }

    void AssignHome()
    {
        StructureNode best = null; float bestSq = float.MaxValue;
        foreach (var s in sim.StructureNodes)
        {
            if (s.Civ != agent.Civ || s.Type != StructureType.Dwelling || !s.HasFreeSlot) continue;
            float dx = s.CellX - agent.CellX, dz = s.CellZ - agent.CellZ;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; best = s; }
        }
        if (best != null && best.TryAddResident()) agent.Home = best;
    }

    // ── Sim helpers ───────────────────────────────────────────────────────────
    StorageNode OwnStorage()
    {
        foreach (var s in sim.StorageNodes)
            if (s.Civ == agent.Civ) return s;
        return null;
    }

    StructureNode NextBuildTarget()
    {
        foreach (var s in sim.StructureNodes)
            if (s.Civ == agent.Civ && s.Type == StructureType.Storage && !s.IsBuilt) return s;
        foreach (var s in sim.StructureNodes)
            if (s.Civ == agent.Civ && s.Type == StructureType.Dwelling && !s.IsBuilt) return s;
        return null;
    }

    StructureNode NearestUnbuiltStructure()
    {
        StructureNode best = null; float bestSq = float.MaxValue;
        foreach (var s in sim.StructureNodes)
        {
            if (s.Civ != agent.Civ || s.IsBuilt) continue;
            float dx = s.CellX - agent.CellX, dz = s.CellZ - agent.CellZ;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; best = s; }
        }
        return best;
    }

    void RouteToBuildSite()
    {
        if (currentSite == null) return;
        var p = Pathfinder.FindPath(grid,
            new Vector2Int(agent.CellX, agent.CellZ),
            new Vector2Int(currentSite.CellX, currentSite.CellZ));
        if (p != null) agent.SetPath(p);
    }

    int CarriedOf(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Wood:  return agent.WoodCarried;
            case ResourceType.Food:  return agent.FoodCarried;
            case ResourceType.Stone: return agent.StoneCarried;
        }
        return 0;
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

    void ReleaseNode() { currentNode?.Release(agent); currentNode = null; }
}
