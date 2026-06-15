// CombatSystem.cs
// Version: 0.4 (Prototype v5: war ignites from territorial intrusion (not a timer) — an
//                agent on enemy soil triggers the owner's response; steady combat cadence;
//                struck defenders stand and fight; A* re-path throttled)
// Purpose: The conflict layer. Periodically musters a small war party from one civ and
//          marches it at the rival (the embryo of Armies & Command, Ch.26); resolves
//          individual melee by adjacency, skill, and stamina (Ch.25.1); lets an outmatched
//          raider rout (Ch.25.4); and, when a civ is wiped out, declares conquest — flipping
//          the loser's territory to the victor and writing the BattleFought / Death /
//          TerritoryCaptured / CivConquered events that give the war its history (Ch.4).
//          Movement uses the A* Pathfinder (routes around water/hills) and a raider stops at
//          a walkable cell beside its target. Combat damage is applied on a fixed game-time
//          cadence (CombatTickSeconds) driven from OnStep, so fights resolve at a steady
//          pace regardless of how few day-ticks the runner uses; struck civilians get a
//          short engagement cooldown so they fight instead of wandering off to eat.
//          Deferred (Ch.25.2/28 staging): capture/Jail + captive fates, surrender, siege.
// Location: Assets/Scripts/Simulation/CombatSystem.cs
// Dependencies: System.Collections.Generic; UnityEngine (Vector2Int) + Pathfinder;
//               Simulation; Agent; GridData; CivId.
// Events: subscribes Simulation.OnDayChanged (muster), OnStep (march + fight + conquest),
//         OnEnded. Emits combat Deaths via Simulation.KillAgent.

using System.Collections.Generic;
using UnityEngine;

public class CombatSystem
{
    private readonly Simulation sim;
    private readonly GridData   grid;
    // Fully qualified: 'using UnityEngine' also defines a Random, so bare Random is ambiguous.
    private readonly System.Random rng;

    // ── Tuning (prototype values; balance is a later surface) ─────────────────────
    public int   RaidIntervalDays    = 3;     // a civ raids at most this often
    public int   RaidPartySize       = 4;     // soldiers per war party
    public int   MinAdultsToRaid     = 5;     // don't denude the home civ
    public int   MaxRaidDurationDays = 5;     // recall survivors after this long
    public float AttackRange         = 1.6f;  // melee adjacency (cells)
    public float PursuitRadius        = 30f;  // raiders hunt enemies within this, else march
    public float BaseDamagePerHit    = 7f;    // damage per combat cadence tick
    public float DefenderDamageScale  = 0.8f; // civilians swing a little weaker than raiders
    public float StaminaAttackCost   = 0.5f;
    public float FleeHealth           = 25f;  // rout threshold when losing badly
    public float SkillGrowthPerHit   = 0.4f;  // use-based combat growth (Ch.9.3)
    public float HomeReturnRange      = 2.5f;
    public float RepathInterval       = 0.5f; // min seconds between A* re-plans while chasing
    public float CombatTickSeconds    = 1.0f; // a melee exchange resolves this often
    public float CombatEngageSeconds  = 3.0f; // how long a struck civilian stays in the fight

    static readonly Vector2Int[] Dirs =
    {
        new Vector2Int(1, 0), new Vector2Int(0, 1),
        new Vector2Int(-1, 0), new Vector2Int(0, -1)
    };

    private class Raid
    {
        public CivId Attacker, Defender;
        public int   AttackerAnchorX, AttackerAnchorZ;
        public int   TargetX, TargetZ;
        public List<Agent> Party = new List<Agent>();
        public int   BattleEventId;   // logged on muster; roots the raid's causal chain
        public int   StartDay;
        public bool  Recalled;        // survivors marching home
    }

    private readonly List<Raid> raids = new List<Raid>();
    private readonly HashSet<Agent> routing = new HashSet<Agent>();
    private readonly Dictionary<CivId, int> lastRaidDay = new Dictionary<CivId, int>();
    private readonly Dictionary<CivId, int> lastBattleId = new Dictionary<CivId, int>();
    private readonly Dictionary<Agent, float> repathTimer = new Dictionary<Agent, float>();
    private readonly Dictionary<Agent, Vector2Int> pathTarget = new Dictionary<Agent, Vector2Int>();
    private float combatAccum;
    private bool  atWar;   // peace until an explorer/agent crosses into enemy territory

    public CombatSystem(Simulation sim, GridData grid, int seed)
    {
        this.sim = sim;
        this.grid = grid;
        rng = new System.Random(seed);
        sim.OnDayChanged += OnDay;
        sim.OnStep       += OnStep;
        sim.OnEnded      += FreeAllConscripts;
    }

    public void Dispose()
    {
        sim.OnDayChanged -= OnDay;
        sim.OnStep       -= OnStep;
        sim.OnEnded      -= FreeAllConscripts;
    }

    // ── Muster: only once war has broken out; both sides then keep the pressure up ──
    private void OnDay(int day)
    {
        if (sim.Ended || !atWar) return;
        foreach (CivState civ in sim.Civs)
        {
            if (civ.Id == CivId.None) continue;
            CivState enemy = PickEnemy(civ.Id);
            if (enemy != null) TryMuster(civ, enemy, day, enemy.AnchorX, enemy.AnchorZ);
        }
    }

    // War starts organically: an agent standing on another civ's land is an incursion, and
    // the owner responds with force (GDD Phase D->E). The first incursion declares the war.
    private void DetectIntrusions()
    {
        int day = sim.Clock.Day;
        for (int i = 0; i < sim.Agents.Count; i++)
        {
            Agent a = sim.Agents[i];
            if (!a.IsAlive || !grid.InBounds(a.CellX, a.CellZ)) continue;
            CivId owner = grid.Cells[a.CellX, a.CellZ].Owner;
            if (owner == CivId.None || owner == a.Civ) continue;

            if (!atWar)
            {
                atWar = true;
                sim.Log.Record(EventType.BattleFought, EventSignificance.World,
                    "War breaks out: " + owner + " repels " + a.Civ + "'s incursion.",
                    civA: owner, civB: a.Civ, cellX: a.CellX, cellZ: a.CellZ);
            }

            CivState ownerState = FindCiv(owner), intruderState = FindCiv(a.Civ);
            if (ownerState != null && intruderState != null)
                TryMuster(ownerState, intruderState, day, a.CellX, a.CellZ); // march on the intruder
        }
    }

    // Muster a war party for 'attacker' against 'enemy', aimed at (tx,tz), respecting the
    // per-civ raid cooldown. Returns true if a party formed.
    private bool TryMuster(CivState attacker, CivState enemy, int day, int tx, int tz)
    {
        if (sim.CountLivingAgents(attacker.Id) < MinAdultsToRaid) return false;
        int last;
        if (lastRaidDay.TryGetValue(attacker.Id, out last) && day - last < RaidIntervalDays) return false;

        Raid raid = MusterParty(attacker, enemy, day);
        if (raid.Party.Count == 0) return false;
        raid.TargetX = tx; raid.TargetZ = tz;
        raids.Add(raid);
        lastRaidDay[attacker.Id] = day;

        WorldEvent muster = sim.Log.Record(EventType.BattleFought, EventSignificance.Civilization,
            attacker.Id + " musters a war party of " + raid.Party.Count + " against " + enemy.Id + ".",
            civA: attacker.Id, civB: enemy.Id, amount: raid.Party.Count, cellX: tx, cellZ: tz);
        raid.BattleEventId = muster.Id;          // roots this raid's causal chain
        lastBattleId[attacker.Id] = raid.BattleEventId;
        return true;
    }

    private CivState PickEnemy(CivId self)
    {
        foreach (CivState c in sim.Civs)
            if (c.Id != CivId.None && c.Id != self && sim.CountLivingAgents(c.Id) > 0)
                return c;
        return null;
    }

    private CivState FindCiv(CivId id)
    {
        foreach (CivState c in sim.Civs) if (c.Id == id) return c;
        return null;
    }

    private Raid MusterParty(CivState attacker, CivState enemy, int day)
    {
        var raid = new Raid
        {
            Attacker = attacker.Id, Defender = enemy.Id,
            AttackerAnchorX = attacker.AnchorX, AttackerAnchorZ = attacker.AnchorZ,
            TargetX = enemy.AnchorX, TargetZ = enemy.AnchorZ,
            StartDay = day
        };

        // Conscript adults nearest the enemy (best placed to strike). Explorers keep
        // exploring — they are the reason the war started, not its soldiers.
        var candidates = new List<Agent>();
        for (int i = 0; i < sim.Agents.Count; i++)
        {
            Agent a = sim.Agents[i];
            if (a.Civ != attacker.Id || !a.IsAlive || !a.IsAdult || a.Conscripted) continue;
            if (a.Stage == LifeStage.Elder || a.Job == JobRole.Explorer) continue;
            candidates.Add(a);
        }
        candidates.Sort((x, y) => DistSq(x, enemy.AnchorX, enemy.AnchorZ)
                                  .CompareTo(DistSq(y, enemy.AnchorX, enemy.AnchorZ)));
        int take = candidates.Count < RaidPartySize ? candidates.Count : RaidPartySize;
        for (int i = 0; i < take; i++)
        {
            candidates[i].Conscripted = true;
            raid.Party.Add(candidates[i]);
        }
        return raid;
    }

    // ── March (every step) + resolve combat on a steady cadence ───────────────────
    private void OnStep(float dt)
    {
        if (sim.Ended) return;

        // Movement: smooth, A*-routed, throttled re-planning.
        for (int r = 0; r < raids.Count; r++)
        {
            Raid raid = raids[r];
            for (int i = 0; i < raid.Party.Count; i++)
            {
                Agent a = raid.Party[i];
                if (!a.IsAlive) continue;

                if (raid.Recalled || routing.Contains(a))
                {
                    EnsurePath(a, dt, WalkableNear(raid.AttackerAnchorX, raid.AttackerAnchorZ));
                    continue;
                }

                Agent prey = FindNearestEnemy(a, raid.Defender, PursuitRadius);
                if (prey != null && WithinRange(a, prey, AttackRange))
                {
                    a.SetPath(null);  // arrived — stand and fight (no overlap)
                    continue;
                }
                Vector2Int dest = prey != null
                    ? StandCellNear(prey, a)                        // stop beside the target
                    : WalkableNear(raid.TargetX, raid.TargetZ);     // else march on the town
                EnsurePath(a, dt, dest);
            }
        }

        // Combat: resolve melee at a fixed game-time cadence, independent of day-ticks.
        combatAccum += dt;
        int guard = 0;
        while (combatAccum >= CombatTickSeconds && guard++ < 8)
        {
            combatAccum -= CombatTickSeconds;
            CombatPass();
        }
    }

    private void CombatPass()
    {
        DetectIntrusions();   // a foot on enemy soil starts (and sustains) the war

        int day = sim.Clock.Day;
        for (int r = 0; r < raids.Count; r++)
        {
            Raid raid = raids[r];
            if (!raid.Recalled && day - raid.StartDay >= MaxRaidDurationDays)
                raid.Recalled = true;
            if (raid.Recalled) continue;

            for (int i = 0; i < raid.Party.Count; i++)
            {
                Agent a = raid.Party[i];
                if (!a.IsAlive || routing.Contains(a)) continue;
                Agent prey = FindNearestEnemy(a, raid.Defender, AttackRange);
                if (prey != null) ResolveStrike(raid, a, prey);
            }
        }
        CleanupRaids();
        CheckConquest();
    }

    // Re-plan an A* path toward 'dest' only when the destination cell changes (or the path
    // is exhausted), and at most every RepathInterval seconds — keeps A* calls rare.
    private void EnsurePath(Agent a, float dt, Vector2Int dest)
    {
        float t;
        repathTimer.TryGetValue(a, out t);
        t -= dt;

        Vector2Int prev;
        bool changed = !pathTarget.TryGetValue(a, out prev) || prev != dest;

        if (!a.HasPath || (changed && t <= 0f))
        {
            var path = Pathfinder.FindPath(grid, new Vector2Int(a.CellX, a.CellZ), dest);
            if (path != null) a.SetPath(path);
            pathTarget[a] = dest;
            t = RepathInterval;
        }
        repathTimer[a] = t;
    }

    private void ResolveStrike(Raid raid, Agent attacker, Agent target)
    {
        float aDmg = BaseDamagePerHit * (0.5f + attacker.SkillCombat / 100f)
                                      * Clamp(attacker.Stamina / 100f, 0.2f, 1f);
        float dDmg = BaseDamagePerHit * (0.5f + target.SkillCombat / 100f)
                                      * Clamp(target.Stamina / 100f, 0.2f, 1f) * DefenderDamageScale;

        target.Health   -= aDmg;
        attacker.Health -= dDmg;
        attacker.Stamina = Clamp(attacker.Stamina - StaminaAttackCost, 0f, 100f);
        target.CombatCooldown = CombatEngageSeconds;   // defender stands and fights
        Grow(attacker); Grow(target);

        if (target.Health <= 0f)
            sim.KillAgent(target, DeathCause.Combat, raid.Attacker, raid.BattleEventId);

        if (attacker.IsAlive && attacker.Health <= 0f)
            sim.KillAgent(attacker, DeathCause.Combat, raid.Defender, raid.BattleEventId);
        else if (attacker.IsAlive && attacker.Health < FleeHealth
                 && target.IsAlive && attacker.Health < target.Health)
            routing.Add(attacker); // losing badly and near death -> rout (Ch.25.4)
    }

    private void Grow(Agent a)
    {
        a.SkillCombat = Clamp(a.SkillCombat + SkillGrowthPerHit, 0f, 100f);
    }

    private void CleanupRaids()
    {
        for (int r = raids.Count - 1; r >= 0; r--)
        {
            Raid raid = raids[r];
            for (int i = raid.Party.Count - 1; i >= 0; i--)
            {
                Agent a = raid.Party[i];
                if (!a.IsAlive)
                {
                    Forget(a);
                    raid.Party.RemoveAt(i);
                }
                else if ((raid.Recalled || routing.Contains(a))
                         && DistSq(a, raid.AttackerAnchorX, raid.AttackerAnchorZ)
                            <= HomeReturnRange * HomeReturnRange)
                {
                    a.Conscripted = false;       // discharged — resume civilian life
                    a.SetPath(null);
                    routing.Remove(a);
                    Forget(a);
                    raid.Party.RemoveAt(i);
                }
            }
            if (raid.Party.Count == 0) raids.RemoveAt(r);
        }
    }

    private void Forget(Agent a) { repathTimer.Remove(a); pathTarget.Remove(a); }

    // ── Conquest end-state (Ch.28.3) ─────────────────────────────────────────────
    private void CheckConquest()
    {
        if (sim.Ended) return;
        CivId loser = CivId.None, winner = CivId.None;
        foreach (CivState c in sim.Civs)
        {
            if (c.Id == CivId.None) continue;
            if (sim.CountLivingAgents(c.Id) == 0) loser = c.Id;
            else winner = c.Id;
        }
        if (loser == CivId.None || winner == CivId.None) return;

        int cause;
        lastBattleId.TryGetValue(winner, out cause);

        int flipped = FlipTerritory(loser, winner);
        sim.Log.Record(EventType.TerritoryCaptured, EventSignificance.Civilization,
            winner + " seizes " + flipped + " cells of " + loser + "'s territory.",
            civA: winner, civB: loser, amount: flipped, causeEventId: cause);

        sim.Log.Record(EventType.CivConquered, EventSignificance.World,
            winner + " has conquered " + loser + " - the war is over.",
            civA: winner, civB: loser, causeEventId: cause);

        sim.DeclareConquest(winner);
    }

    private int FlipTerritory(CivId loser, CivId winner)
    {
        int flipped = 0;
        if (grid == null) return 0;
        for (int z = 0; z < grid.Depth; z++)
        for (int x = 0; x < grid.Width; x++)
            if (grid.Cells[x, z].Owner == loser)
            {
                grid.SetOwner(x, z, winner);
                flipped++;
            }
        return flipped;
    }

    private void FreeAllConscripts()
    {
        for (int i = 0; i < sim.Agents.Count; i++)
        {
            sim.Agents[i].Conscripted = false;
            sim.Agents[i].SetPath(null);
        }
        routing.Clear();
        repathTimer.Clear();
        pathTarget.Clear();
        raids.Clear();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────
    private Agent FindNearestEnemy(Agent from, CivId enemyCiv, float maxRange)
    {
        float maxSq = maxRange * maxRange;
        Agent best = null; float bestSq = float.MaxValue;
        for (int i = 0; i < sim.Agents.Count; i++)
        {
            Agent a = sim.Agents[i];
            if (a.Civ != enemyCiv || !a.IsAlive) continue;
            float dx = a.PosX - from.PosX, dz = a.PosZ - from.PosZ;
            float sq = dx * dx + dz * dz;
            if (sq <= maxSq && sq < bestSq) { bestSq = sq; best = a; }
        }
        return best;
    }

    private static bool WithinRange(Agent a, Agent b, float range)
    {
        float dx = a.PosX - b.PosX, dz = a.PosZ - b.PosZ;
        return dx * dx + dz * dz <= range * range;
    }

    // A walkable cell beside the target, chosen by attacker id so several attackers spread
    // around it (different sides) instead of stacking on the same cell.
    private Vector2Int StandCellNear(Agent prey, Agent attacker)
    {
        int off = attacker.Id & 3;
        for (int k = 0; k < Dirs.Length; k++)
        {
            Vector2Int d = Dirs[(off + k) & 3];
            int x = prey.CellX + d.x, z = prey.CellZ + d.y;
            if (grid.InBounds(x, z) && grid.Cells[x, z].Walkable) return new Vector2Int(x, z);
        }
        return new Vector2Int(prey.CellX, prey.CellZ);
    }

    // A walkable cell at (x,z) if possible, else a walkable neighbour, else (x,z) as-is.
    private Vector2Int WalkableNear(int x, int z)
    {
        if (grid.InBounds(x, z) && grid.Cells[x, z].Walkable) return new Vector2Int(x, z);
        Vector2Int n;
        if (grid.TryGetWalkableNeighbor(x, z, out n)) return n;
        return new Vector2Int(x, z);
    }

    private static float DistSq(Agent a, int x, int z)
    {
        float dx = a.PosX - x, dz = a.PosZ - z;
        return dx * dx + dz * dz;
    }

    private static float Clamp(float v, float lo, float hi)
    {
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }
}
