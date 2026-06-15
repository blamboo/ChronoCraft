// AgentManager.cs
// Version: 0.17 (Prototype v5: Explorer agents (claim land -> intrusion war); SeparationSystem
//                (anti-overlap); auto territory-growth off by default; v0.16 perf intact)
// Purpose: Unity bridge that bootstraps the two-civ population, the NeedsSystem, and the
//          v5 life-cycle/conflict systems, and keeps capsule views in sync with births
//          and deaths in the plain-C# simulation.
// Location: Assets/Scripts/World/AgentManager.cs
// Dependencies: UnityEngine; SimulationRunner, GridManager, Agent, AgentBehavior,
//               AgentView, NeedsSystem, LineageSystem, CombatSystem, TerritoryGrowth,
//               TrueLog, CivId, JobRole, Sex, LifeStage.
// Events: subscribes Simulation.OnAgentBorn / OnAgentDied.

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
    [SerializeField] private Color civ1Color = new Color(0.30f, 0.55f, 0.95f);
    [SerializeField] private Color civ2Color = new Color(0.90f, 0.45f, 0.25f);

    [Header("Job counts per civ (must sum to <= agentsPerCiv)")]
    [Range(0, 20)] [SerializeField] private int loggerCount   = 3;
    [Range(0, 20)] [SerializeField] private int farmerCount   = 4;
    [Range(0, 20)] [SerializeField] private int minerCount    = 1;
    [Range(0, 20)] [SerializeField] private int builderCount  = 2;
    [Tooltip("Explorers claim a radius-5 area of unclaimed land as they roam; entering an " +
             "enemy's territory is what starts a war (Ch.8/Phase D).")]
    [Range(0, 20)] [SerializeField] private int explorerCount = 2;

    [Header("Agent")]
    [Range(0.25f, 20f)] [SerializeField] private float agentSpeed = 3f;
    [SerializeField]    private float yOffset = 1f;

    [Header("Needs (per tick, tuned to ticksPerDay 450)")]
    [Range(0.05f, 50f)] [SerializeField] private float hungerDrainPerTick   = 0.25f;
    [Range(0.05f, 50f)] [SerializeField] private float thirstDrainPerTick   = 0.40f;
    [Range(1f, 100f)]   [SerializeField] private float hungerThreshold       = 50f;
    [Tooltip("Hunger above this forces eating even at night (near-starvation override).")]
    [Range(1f, 100f)]   [SerializeField] private float hungerCriticalThreshold = 80f;
    [Range(1f, 100f)]   [SerializeField] private float thirstThreshold        = 50f;
    [Range(0f, 50f)]    [SerializeField] private float staminaDrainPerTick   = 0.30f;
    [Range(0f, 50f)]    [SerializeField] private float staminaRecoverPerTick = 0.60f;
    [Range(1f, 100f)]   [SerializeField] private float staminaRestThreshold  = 20f;
    [Range(1f, 100f)]   [SerializeField] private float staminaWakeThreshold  = 80f;

    [Header("Action durations (game-seconds)")]
    [Range(1f, 60f)] [SerializeField] private float harvestDurationSeconds = 10f;
    [Range(1f, 60f)] [SerializeField] private float drinkDurationSeconds   = 5f;
    [Range(1f, 60f)] [SerializeField] private float eatDurationSeconds     = 5f;

    [Header("Work")]
    [Tooltip("Seconds an agent waits before re-seeking when there is nothing to do.")]
    [Range(0.5f, 10f)] [SerializeField] private float idleCooldownSeconds = 3f;

    [Header("Prototype v5 — systems on/off")]
    [Tooltip("Reproduction, aging, and death by old age (Ch.11).")]
    [SerializeField] private bool enableLineage = true;
    [Tooltip("War parties, melee, and the conquest end-state (Ch.25/28). War starts when " +
             "an agent enters another civ's territory.")]
    [SerializeField] private bool enableConflict = true;
    [Tooltip("Keeps agents from standing on the same cell (melee, foraging, drinking).")]
    [SerializeField] private bool enableSeparation = true;
    [Tooltip("Automatic ring expansion. OFF by default — Explorers now expand territory " +
             "(Ch.8/Phase D). Turn on for a passive fallback.")]
    [SerializeField] private bool enableTerritoryGrowth = false;
    [Tooltip("Wild-food/resource respawn so the world doesn't starve (Phase C2). " +
             "Leave ON — finite nodes otherwise deplete and everyone dies.")]
    [SerializeField] private bool enableResourceRespawn = true;
    [Tooltip("Print every History Log event to the Console as it happens.")]
    [SerializeField] private bool logHistoryToConsole = true;
    [SerializeField] private int  lineageSeed = 12345;
    [SerializeField] private int  conflictSeed = 67890;

    [Header("Lineage pacing (game-days)")]
    [SerializeField] private int maturationDays     = 6;   // child -> adult
    [SerializeField] private int elderDays          = 40;  // adult -> elder
    [SerializeField] private int lifeExpectancyDays = 70;  // age past which death ramps
    [SerializeField] private int gestationDays      = 3;
    [SerializeField] private int maxAgentsPerCiv    = 24;  // soft population cap
    [Tooltip("Founder starting age range (game-days); they age, mature heirs, and die.")]
    [SerializeField] private float founderMinAgeDays    = 20f;
    [SerializeField] private float founderAgeSpreadDays = 22f;

    [Header("Conflict tuning")]
    [Range(1, 30)] [SerializeField] private int   raidIntervalDays  = 6;
    [Range(1, 12)] [SerializeField] private int   raidPartySize     = 4;
    [Tooltip("Melee damage per ~1s combat exchange.")]
    [Range(1f, 30f)] [SerializeField] private float baseDamagePerHit = 7f;
    [Range(0f, 100f)] [SerializeField] private float baseCombatSkill  = 28f;

    [Header("Resource respawn (per day)")]
    [Range(0, 50)] [SerializeField] private int foodRegenPerDay = 8;
    [Range(0, 50)] [SerializeField] private int woodRegenPerDay = 4;
    [Range(1, 200)] [SerializeField] private int resourceCap    = 60;

    [Header("Territory")]
    [Range(1, 20)] [SerializeField] private int territoryExpandIntervalDays = 2;

    [Header("Read-out (Play mode)")]
    [SerializeField] private int  civ1Agents;
    [SerializeField] private int  civ2Agents;
    [SerializeField] private int  day;
    [SerializeField] private bool night;
    [SerializeField] private int  logEvents;
    [SerializeField] private bool warOver;

    private class View { public Agent Agent; public Transform Tf; }
    private readonly List<View> views = new List<View>();
    private NeedsSystem      needs;
    private LineageSystem    lineage;
    private CombatSystem     combat;
    private TerritoryGrowth  territoryGrowth;
    private ResourceRespawn  resourceRespawn;
    private SeparationSystem separation;
    private System.Random    rng;
    private bool initialized;

    void Update()
    {
        if (!initialized) { TryInitialize(); if (!initialized) return; }
        SyncViews();
        var sim   = runner.Sim;
        var clock = sim.Clock;
        day       = clock.Day;
        night     = clock.IsNight;
        logEvents = sim.Log.Count;
        warOver   = sim.Ended;
    }

    void TryInitialize()
    {
        if (runner == null || gridManager == null) return;
        GridData   grid = gridManager.Grid;
        Simulation sim  = runner.Sim;
        if (grid == null || sim == null) return;

        // Seed before spawning — SpawnAgent draws founder sex/age/skill from rng.
        rng = new System.Random(lineageSeed ^ conflictSeed);

        int cz = grid.Depth / 2;
        civ1Agents = SpawnCiv(sim, grid, CivId.Civ1,
            new Vector2Int(edgeMargin, cz), civ1Color);
        civ2Agents = SpawnCiv(sim, grid, CivId.Civ2,
            new Vector2Int(grid.Width - 1 - edgeMargin, cz), civ2Color);

        needs = new NeedsSystem(sim, hungerDrainPerTick, thirstDrainPerTick,
                                staminaDrainPerTick, staminaRecoverPerTick);
        // Wire each behavior's tick-based food suppression counter.
        sim.OnTick += () => { for (int i = 0; i < sim.AgentBehaviors.Count; i++) sim.AgentBehaviors[i].OnTick(); };

        // ── Prototype-v5 systems: the life cycle, the war, and living borders ─────
        // Print the chronicle to the Console as it's written (no in-game UI until v6).
        if (logHistoryToConsole)
        {
            // Capturing a stack trace on every Debug.Log is the main editor lag spike at
            // high time scale; disable it for plain logs (warnings/errors keep traces).
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            sim.Log.OnRecord += e => Debug.Log($"[Day {e.Day}] {e.Summary}");
        }

        // Newborns get a brain + a view; the fallen lose their view.
        sim.OnAgentBorn += HandleAgentBorn;
        sim.OnAgentDied += HandleAgentDied;

        if (enableLineage)
        {
            lineage = new LineageSystem(sim, lineageSeed);
            lineage.MaturationDays     = maturationDays;
            lineage.ElderDays          = elderDays;
            lineage.LifeExpectancyDays = lifeExpectancyDays;
            lineage.GestationDays      = gestationDays;
            lineage.MaxAgentsPerCiv    = maxAgentsPerCiv;
        }
        if (enableConflict)
        {
            combat = new CombatSystem(sim, grid, conflictSeed);
            combat.RaidIntervalDays = raidIntervalDays;
            combat.RaidPartySize    = raidPartySize;
            combat.BaseDamagePerHit = baseDamagePerHit;
        }
        if (enableTerritoryGrowth)
        {
            territoryGrowth = new TerritoryGrowth(sim, grid);
            territoryGrowth.ExpandIntervalDays = territoryExpandIntervalDays;
        }
        if (enableSeparation) separation = new SeparationSystem(sim);
        if (enableResourceRespawn)
        {
            resourceRespawn = new ResourceRespawn(sim);
            resourceRespawn.FoodRegen = foodRegenPerDay;
            resourceRespawn.WoodRegen = woodRegenPerDay;
            resourceRespawn.FoodCap   = resourceCap;
            resourceRespawn.WoodCap   = resourceCap;
        }

        // Found each civ in the History Log (Ch.4) — the root of its story.
        foreach (CivState c in sim.Civs)
            if (c.Id != CivId.None)
                sim.Log.Record(EventType.Founding, EventSignificance.World,
                    c.Id + " is founded.", civA: c.Id, cellX: c.AnchorX, cellZ: c.AnchorZ);

        initialized = true;
        SyncViews();
    }

    int SpawnCiv(Simulation sim, GridData grid, CivId civ, Vector2Int anchor, Color color)
    {
        sim.RegisterCiv(civ, anchor.x, anchor.y);

        var jobQueue = new List<JobRole>();
        for (int i = 0; i < loggerCount;   i++) jobQueue.Add(JobRole.Logger);
        for (int i = 0; i < farmerCount;   i++) jobQueue.Add(JobRole.Farmer);
        for (int i = 0; i < minerCount;    i++) jobQueue.Add(JobRole.Miner);
        for (int i = 0; i < builderCount;  i++) jobQueue.Add(JobRole.Builder);
        for (int i = 0; i < explorerCount; i++) jobQueue.Add(JobRole.Explorer);
        while (jobQueue.Count < agentsPerCiv) jobQueue.Add(JobRole.Logger);

        int placed = 0;
        int maxR = Mathf.Max(grid.Width, grid.Depth);
        for (int r = 0; r <= maxR && placed < agentsPerCiv; r++)
        for (int dz = -r; dz <= r && placed < agentsPerCiv; dz++)
        for (int dx = -r; dx <= r && placed < agentsPerCiv; dx++)
        {
            if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
            int x = anchor.x + dx, z = anchor.y + dz;
            if (!grid.InBounds(x, z) || !grid.Cells[x, z].Walkable) continue;
            SpawnAgent(sim, grid, civ, new Vector2Int(x, z), color, jobQueue[placed]);
            placed++;
        }
        return placed;
    }

    void SpawnAgent(Simulation sim, GridData grid, CivId civ, Vector2Int cell,
                    Color color, JobRole job)
    {
        Agent agent = sim.AddAgent(cell.x, cell.y);
        agent.Speed = agentSpeed;
        agent.Civ   = civ;
        agent.Job   = job;

        // Founder life-cycle identity (Ch.9/11): an adult of a given sex and age, with a
        // starting combat skill. Aging, reproduction, and death take it from here.
        agent.Sex         = rng.Next(2) == 0 ? Sex.Male : Sex.Female;
        agent.Stage       = LifeStage.Adult;
        agent.AgeDays     = founderMinAgeDays + (float)rng.NextDouble() * founderAgeSpreadDays;
        agent.SkillCombat = Mathf.Clamp(baseCombatSkill + rng.Next(-6, 7), 0f, 100f);

        AttachAgent(sim, grid, agent, color);
    }

    // Builds an agent's behavior + capsule + view. Shared by founder spawns and births so
    // newborns (raised by the LineageSystem) get a brain and a body identically.
    void AttachAgent(Simulation sim, GridData grid, Agent agent, Color color)
    {
        AgentBehavior b = sim.AddAgentBehavior(agent, grid);
        b.HungerThreshold         = hungerThreshold;
        b.HungerCriticalThreshold = hungerCriticalThreshold;
        b.ThirstThreshold         = thirstThreshold;
        b.StaminaRestThreshold    = staminaRestThreshold;
        b.StaminaWakeThreshold    = staminaWakeThreshold;
        b.HarvestDurationSeconds  = harvestDurationSeconds;
        b.DrinkDurationSeconds    = drinkDurationSeconds;
        b.EatDurationSeconds      = eatDurationSeconds;
        b.IdleCooldownSeconds     = idleCooldownSeconds;

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = $"Agent ({agent.Civ} {agent.Job})";
        go.transform.SetParent(gridManager.transform, worldPositionStays: false);
        Destroy(go.GetComponent<Collider>());

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        go.GetComponent<Renderer>().SetPropertyBlock(mpb);

        go.AddComponent<AgentView>().Bind(agent, b);
        views.Add(new View { Agent = agent, Tf = go.transform });
    }

    // A child has been born (LineageSystem -> Simulation.EmitAgentBorn): give it a body.
    void HandleAgentBorn(Agent child)
    {
        AttachAgent(runner.Sim, gridManager.Grid, child, ColorFor(child.Civ));
    }

    // An agent has died: remove its capsule and drop it from the view list.
    void HandleAgentDied(Agent dead)
    {
        for (int i = views.Count - 1; i >= 0; i--)
        {
            if (views[i].Agent != dead) continue;
            if (views[i].Tf != null) Destroy(views[i].Tf.gameObject);
            views.RemoveAt(i);
        }
    }

    Color ColorFor(CivId civ) => civ == CivId.Civ2 ? civ2Color : civ1Color;

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

    void OnDestroy()
    {
        needs?.Dispose();
        lineage?.Dispose();
        combat?.Dispose();
        territoryGrowth?.Dispose();
        resourceRespawn?.Dispose();
        separation?.Dispose();
    }
}
