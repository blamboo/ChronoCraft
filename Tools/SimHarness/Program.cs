// Program.cs — ChronoCraft headless verification harness (Mono / mcs).
// Builds a flat two-civ world, wires the real simulation systems (Needs+Health, Lineage,
// Combat, TerritoryGrowth) plus the existing civilian brain (AgentBehavior/Pathfinder),
// runs it for a span of game-days, and asserts the True Log recorded the full set of
// Prototype-v5 events: births, deaths, battles, buildings, territory flips, conquest.
// A "logistics caretaker" keeps civilians fed/watered (abstracting foraging) so the
// demographic + war systems can be observed deterministically; a separate focused check
// exercises the starvation/dehydration death path with the caretaker off.
//
// Run:  Tools/SimHarness/build.sh   (compiles + executes)

using System;
using System.Collections.Generic;

public static class Harness
{
    static int passed = 0, failed = 0;

    static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
        if (ok) passed++; else failed++;
    }

    public static int Main(string[] args)
    {
        Console.WriteLine("=== ChronoCraft — Prototype v5 verification ===\n");

        RunNeedsDeathCheck();
        Console.WriteLine();
        RunPathfinderCheck();
        Console.WriteLine();
        RunRespawnCheck();
        Console.WriteLine();
        RunSeparationCheck();
        Console.WriteLine();
        RunIntegration();

        Console.WriteLine("\n=== RESULT: " + passed + " passed, " + failed + " failed ===");
        return failed == 0 ? 0 : 1;
    }

    // ── Focused check: Health/death from need-failure (NeedsSystem) ───────────────
    static void RunNeedsDeathCheck()
    {
        Console.WriteLine("-- Health & death path (starvation / dehydration) --");
        var sim = new Simulation(300, 300.0);
        var needs = new NeedsSystem(sim, 0f, 0f, 0f, 0f); // no passive drain; we force needs
        needs.StarvationDamagePerTick = 5f;
        needs.DehydrationDamagePerTick = 5f;

        Agent starver = sim.AddAgent(0, 0);
        starver.Stage = LifeStage.Adult; starver.Hunger = 100f; // maxed -> starving

        Agent parched = sim.AddAgent(1, 0);
        parched.Stage = LifeStage.Adult; parched.Thirst = 100f; // maxed -> dehydrating

        // Health starts at 100; 5 dmg/tick -> dead within ~20 ticks. Run 60 days of ticks.
        for (int i = 0; i < 300 && sim.Agents.Count > 0; i++) sim.Advance(1.0);

        int starvDeaths = 0, thirstDeaths = 0;
        foreach (WorldEvent e in sim.Log.OfType(EventType.Death))
        {
            if (e.Cause == DeathCause.Starvation)  starvDeaths++;
            if (e.Cause == DeathCause.Dehydration) thirstDeaths++;
        }
        Check("starvation produces a Death(Starvation) event", starvDeaths == 1);
        Check("dehydration produces a Death(Dehydration) event", thirstDeaths == 1);
        Check("dead agents are removed from the roster", sim.Agents.Count == 0);
    }

    // ── Focused check: A* Pathfinder (binary-heap rewrite) ───────────────────────
    static void RunPathfinderCheck()
    {
        Console.WriteLine("-- Pathfinder (binary-heap A*) --");

        // Open grid: optimal 4-connected path length == Manhattan distance + 1 (inclusive).
        GridData open = FlatGrid(40, 40, null);
        var p = Pathfinder.FindPath(open, new UnityEngine.Vector2Int(2, 2),
                                          new UnityEngine.Vector2Int(35, 30));
        int manhattan = (35 - 2) + (30 - 2);
        Check("open-grid path is optimal length", p != null && p.Count == manhattan + 1);

        // Water wall across x=20 for z>=12, leaving a wide gap at the bottom: start and end
        // are at z=30 (behind the wall) so the path must detour through the gap (much longer
        // than the direct 30-cell run).
        GridData walled = FlatGrid(40, 40, (x, z) => x == 20 && z >= 12);
        var around = Pathfinder.FindPath(walled, new UnityEngine.Vector2Int(5, 30),
                                                 new UnityEngine.Vector2Int(35, 30));
        Check("routes around a barrier", around != null && around.Count > 45);

        // Full-height water wall, no gap: no path.
        GridData blocked = FlatGrid(40, 40, (x, z) => x == 20);
        var none = Pathfinder.FindPath(blocked, new UnityEngine.Vector2Int(5, 20),
                                                new UnityEngine.Vector2Int(35, 20));
        Check("returns null when fully blocked", none == null);
    }

    // Builds a flat all-walkable grid; cells where waterAt(x,z) is true become water (unwalkable).
    static GridData FlatGrid(int w, int d, Func<int, int, bool> waterAt)
    {
        var heights = new float[w + 1, d + 1];
        for (int x = 0; x <= w; x++)
            for (int z = 0; z <= d; z++)
                heights[x, z] = 1f;
        // Water is keyed per-cell, so depress all four of a cell's corners.
        if (waterAt != null)
            for (int x = 0; x < w; x++)
                for (int z = 0; z < d; z++)
                    if (waterAt(x, z))
                    { heights[x, z] = -5f; heights[x + 1, z] = -5f; heights[x, z + 1] = -5f; heights[x + 1, z + 1] = -5f; }
        var g = new GridData();
        g.Build(heights, 1f, 10f, 0f);
        return g;
    }

    // ── Focused check: wild-food respawn (ResourceRespawn) ────────────────────────
    static void RunRespawnCheck()
    {
        Console.WriteLine("-- Resource respawn (anti-starvation) --");
        var sim = new Simulation(60, 60.0);
        var respawn = new ResourceRespawn(sim);
        respawn.IntervalDays = 1; respawn.FoodRegen = 10; respawn.FoodCap = 40;

        ResourceNode food = sim.AddResourceNode(ResourceType.Food, 0, 0, 25);
        food.Harvest(25);                                  // fully deplete it
        Check("node is depleted after harvest", food.Depleted);

        for (int i = 0; i < 60; i++) sim.Advance(1.0);     // ~1 game-day of ticks
        Check("a depleted Food node regrows (no permanent starvation)", food.Amount > 0 && !food.Depleted);
        Check("regrowth respects the cap", food.Amount <= 40);
    }

    // ── Focused check: separation (no two agents on one cell) ─────────────────────
    static void RunSeparationCheck()
    {
        Console.WriteLine("-- Separation (anti-overlap) --");
        var sim = new Simulation(60, 60.0);
        var sep = new SeparationSystem(sim);
        sep.MinSeparation = 0.9f;

        Agent a = sim.AddAgent(10, 10);
        Agent b = sim.AddAgent(10, 10);   // exact overlap
        for (int i = 0; i < 300; i++) sim.Advance(0.1);

        float dx = a.PosX - b.PosX, dz = a.PosZ - b.PosZ;
        float d = (float)Math.Sqrt(dx * dx + dz * dz);
        Check("two agents on the same cell separate", d >= 0.8f);
    }

    // ── Integration: the living chronicle ────────────────────────────────────────
    static void RunIntegration()
    {
        Console.WriteLine("-- Two-civ living-world soak --");

        const int W = 64, D = 40, DayCap = 240;
        var rng = new Random(20260614);

        // Flat, all-walkable grid.
        var heights = new float[W + 1, D + 1];
        for (int x = 0; x <= W; x++)
            for (int z = 0; z <= D; z++)
                heights[x, z] = 1f;
        var grid = new GridData();
        grid.Build(heights, 1f, 10f, 0f);

        var sim = new Simulation(300, 300.0);

        var civ1 = sim.RegisterCiv(CivId.Civ1, 9, D / 2);
        var civ2 = sim.RegisterCiv(CivId.Civ2, W - 10, D / 2);
        StampTerritory(grid, civ1.AnchorX, civ1.AnchorZ, 7, CivId.Civ1);
        StampTerritory(grid, civ2.AnchorX, civ2.AnchorZ, 7, CivId.Civ2);

        sim.Log.Record(EventType.Founding, EventSignificance.World,
            "Civ1 is founded.", civA: CivId.Civ1, cellX: civ1.AnchorX, cellZ: civ1.AnchorZ);
        sim.Log.Record(EventType.Founding, EventSignificance.World,
            "Civ2 is founded.", civA: CivId.Civ2, cellX: civ2.AnchorX, cellZ: civ2.AnchorZ);

        // Systems (order matters: Needs drains, then the caretaker tops up).
        new NeedsSystem(sim, 0.25f, 0.40f, 0.30f, 0.60f);

        var lineage = new LineageSystem(sim, 12345);
        // Accelerated, mortal pacing for the demo (real values are Appendix E).
        lineage.MaturationDays = 6; lineage.ElderDays = 28; lineage.LifeExpectancyDays = 42;
        lineage.GestationDays = 3;  lineage.ReproCooldownDays = 5; lineage.MaxAgentsPerCiv = 26;
        lineage.AgeDeathChancePerDay = 0.08f;

        var combat = new CombatSystem(sim, grid, 67890);
        // War ignites on territorial contact (explorers), then both sides sustain it.
        combat.RaidIntervalDays = 4; combat.RaidPartySize = 3; combat.MinAdultsToRaid = 4;
        combat.MaxRaidDurationDays = 5; combat.BaseDamagePerHit = 2.0f; combat.DefenderDamageScale = 0.7f;

        // Territory now grows via Explorer agents (not the passive ring); keep agents apart.
        new SeparationSystem(sim);
        int neutralBefore = CountNeutral(grid);

        // Logistics caretaker: living agents never starve (foraging abstracted).
        sim.OnTick += () =>
        {
            for (int i = 0; i < sim.Agents.Count; i++)
            { sim.Agents[i].Hunger = 0f; sim.Agents[i].Thirst = 0f; }
        };

        // Give newborns a brain + a (modest) starting age so generations turn over.
        sim.OnAgentBorn += child => sim.AddAgentBehavior(child, grid);

        // Structures + resources + population for each civ.
        SetupCiv(sim, grid, civ1, CivId.Civ1, 12, 52f, rng);   // Civ1: seasoned fighters
        SetupCiv(sim, grid, civ2, CivId.Civ2,  9, 30f, rng);   // Civ2: fewer, greener

        // Run until conquest or the day cap.
        int lastDay = 0;
        double dt = 0.25;
        while (!sim.Ended && sim.Clock.Day <= DayCap)
        {
            sim.Advance(dt);
            lastDay = sim.Clock.Day;
        }

        // ── Report ───────────────────────────────────────────────────────────────
        var log = sim.Log;
        int births   = log.CountOf(EventType.Birth);
        int deaths    = log.CountOf(EventType.Death);
        int battles   = log.CountOf(EventType.BattleFought);
        int builds    = log.CountOf(EventType.StructureCompleted);
        int captures  = log.CountOf(EventType.TerritoryCaptured);
        int claimed   = neutralBefore - CountNeutral(grid);   // neutral land taken by explorers

        int combatDeaths = 0, ageDeaths = 0;
        foreach (WorldEvent e in log.OfType(EventType.Death))
        {
            if (e.Cause == DeathCause.Combat) combatDeaths++;
            if (e.Cause == DeathCause.OldAge) ageDeaths++;
        }

        Console.WriteLine("\nSimulated " + lastDay + " days. Log holds " + log.Count + " events.");
        Console.WriteLine("  Births: " + births + "   Deaths: " + deaths +
                          " (combat " + combatDeaths + ", age " + ageDeaths + ")");
        Console.WriteLine("  Battles mustered: " + battles + "   Buildings: " + builds +
                          "   Neutral cells claimed by explorers: " + claimed);
        Console.WriteLine("  Ended: " + sim.Ended + (sim.Ended ? "  Victor: " + sim.WinningCiv : ""));

        Console.WriteLine("\n----- World Chronicle (Town-tier and above) -----");
        Console.Write(log.Chronicle(EventSignificance.Town));
        Console.WriteLine("-------------------------------------------------");

        // A sample personal history: the first agent born, and its causal-chain demo.
        DemoCausalChain(log);

        // ── Assertions ─────────────────────────────────────────────────────────────
        Console.WriteLine();
        Check("the log recorded events", log.Count > 0);
        Check("births occurred (Lineage reproduction)", births > 0);
        Check("deaths occurred", deaths > 0);
        Check("age-deaths occurred (Lineage aging)", ageDeaths > 0);
        Check("war broke out from territorial intrusion (Phase D->E)", battles > 0);
        Check("combat deaths occurred", combatDeaths > 0);
        Check("buildings were completed (civilian brain)", builds > 0);
        Check("explorers expanded territory (Phase D)", claimed > 0);
        Check("a conquest end-state was reached (v2 exit criterion)", sim.Ended && captures > 0);
        Check("schema integrity: every event has id/day/summary", SchemaIntact(log));
    }

    static void DemoCausalChain(TrueLog log)
    {
        // Find a conquest and walk its causal chain back to the muster (Ch.4.4).
        WorldEvent conquest = null;
        foreach (WorldEvent e in log.OfType(EventType.CivConquered)) { conquest = e; break; }
        if (conquest == null) return;

        Console.WriteLine("\nCausal chain behind the conquest (Ch.4.4):");
        List<WorldEvent> chain = log.CausalChain(conquest);
        for (int i = 0; i < chain.Count; i++)
            Console.WriteLine("   " + (i == 0 ? "" : "<- ") + "Day " + chain[i].Day + ": " + chain[i].Summary);
    }

    static bool SchemaIntact(TrueLog log)
    {
        int prevId = 0;
        foreach (WorldEvent e in log.Events)
        {
            if (e.Id <= prevId) return false;     // ids strictly increasing
            if (e.Day < 0) return false;
            if (string.IsNullOrEmpty(e.Summary)) return false;
            prevId = e.Id;
        }
        return true;
    }

    // ── World-build helpers ──────────────────────────────────────────────────────
    static void StampTerritory(GridData grid, int ax, int az, int r, CivId civ)
    {
        for (int z = az - r; z <= az + r; z++)
            for (int x = ax - r; x <= ax + r; x++)
                if (grid.InBounds(x, z)) grid.SetOwner(x, z, civ);
    }

    static void SetupCiv(Simulation sim, GridData grid, CivState civ, CivId id,
                         int agents, float combatSkill, Random rng)
    {
        // One Storage + four Dwellings near the anchor.
        sim.AddStructureNode(StructureType.Storage, id, civ.AnchorX, civ.AnchorZ - 3, 6, 30f);
        sim.AddStorageNode(id, civ.AnchorX, civ.AnchorZ - 3);
        for (int k = 0; k < 4; k++)
            sim.AddStructureNode(StructureType.Dwelling, id,
                civ.AnchorX - 3 + k * 2, civ.AnchorZ + 3, 3, 20f);

        // Wood + food nodes within reach of the camp.
        for (int k = 0; k < 16; k++)
        {
            int wx = Clamp(civ.AnchorX + rng.Next(-10, 11), 0, grid.Width - 1);
            int wz = Clamp(civ.AnchorZ + rng.Next(-10, 11), 0, grid.Depth - 1);
            sim.AddResourceNode(ResourceType.Wood, wx, wz, 60);
        }
        for (int k = 0; k < 8; k++)
        {
            int fx = Clamp(civ.AnchorX + rng.Next(-8, 9), 0, grid.Width - 1);
            int fz = Clamp(civ.AnchorZ + rng.Next(-8, 9), 0, grid.Depth - 1);
            sim.AddResourceNode(ResourceType.Food, fx, fz, 9999);
        }

        // Population: adults, half male/half female, mixed trades, slight combat edge.
        JobRole[] jobs = { JobRole.Explorer, JobRole.Builder, JobRole.Logger,
                           JobRole.Farmer, JobRole.Builder, JobRole.Logger };
        int placed = 0;
        for (int r = 0; r <= 8 && placed < agents; r++)
            for (int dz = -r; dz <= r && placed < agents; dz++)
                for (int dx = -r; dx <= r && placed < agents; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
                    int x = civ.AnchorX + dx, z = civ.AnchorZ + dz;
                    if (!grid.InBounds(x, z) || !grid.Cells[x, z].Walkable) continue;

                    Agent a = sim.AddAgent(x, z);
                    a.Civ = id;
                    a.Job = jobs[placed % jobs.Length];
                    a.Sex = (placed % 2 == 0) ? Sex.Male : Sex.Female;
                    a.Stage = LifeStage.Adult;
                    a.AgeDays = 24f + rng.Next(0, 26);          // mortal adults (some die of age mid-soak)
                    a.SkillCombat = combatSkill + rng.Next(-6, 7);
                    sim.AddAgentBehavior(a, grid);
                    placed++;
                }
    }

    static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

    static int CountNeutral(GridData grid)
    {
        int n = 0;
        for (int z = 0; z < grid.Depth; z++)
            for (int x = 0; x < grid.Width; x++)
                if (grid.Cells[x, z].Owner == CivId.None) n++;
        return n;
    }
}
