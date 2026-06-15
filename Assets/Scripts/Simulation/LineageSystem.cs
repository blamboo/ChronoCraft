// LineageSystem.cs
// Version: 0.1 (Prototype v5 / v2 Phase F — the Lineage front end, GDD Ch.11)
// Purpose: The agent life cycle. Once per game-day it ages every agent, advances life
//          stages (Child -> Adult -> Elder), runs reproduction (pairing -> gestation ->
//          birth with skill-averaging at conception, Ch.11.1), and rolls death by old
//          age past the elder threshold. Every birth and age-death is written to the
//          True Log (Ch.4), giving the world its demographic history.
//          Deferred to Pre-Alpha (Ch.11 staging): dispositions/baseline pull, trait
//          pass-chances, the five-stage model with effects, the estate/inheritance system.
// Location: Assets/Scripts/Simulation/LineageSystem.cs
// Dependencies: System; System.Collections.Generic; Simulation; Agent; CivId. No UnityEngine.
// Events: subscribes Simulation.OnDayChanged. Emits Birth/Death via the sim (EmitAgentBorn,
//         KillAgent) and records them in the True Log.

using System;
using System.Collections.Generic;

public class LineageSystem
{
    private readonly Simulation sim;
    private readonly Random rng;

    // ── Pacing knobs (accelerated for the prototype; real values are Appendix E) ──
    public int   MaturationDays    = 8;    // Child -> Adult
    public int   ElderDays         = 60;   // Adult -> Elder
    public int   LifeExpectancyDays = 80;  // age past which death becomes likely
    public int   GestationDays     = 3;    // conception -> birth
    public int   ReproCooldownDays = 6;    // per-mother gap between pregnancies
    public int   MaxAgentsPerCiv   = 28;   // soft population cap (keeps the soak bounded)
    public float FedThreshold      = 70f;  // Hunger/Thirst below this to be fertile
    public float SkillJitter       = 8f;   // +/- mutation on inherited skills (Ch.11.1)
    public float AgeDeathChancePerDay = 0.06f; // per-day death roll once past expectancy

    private class Pregnancy { public Agent Mother; public Agent Father; public int DaysLeft; }

    private readonly List<Pregnancy> pregnancies = new List<Pregnancy>();
    private readonly Dictionary<int, int> lastBirthDay = new Dictionary<int, int>();

    public LineageSystem(Simulation sim, int seed)
    {
        this.sim = sim;
        rng = new Random(seed);
        sim.OnDayChanged += OnDay;
    }

    public void Dispose() { sim.OnDayChanged -= OnDay; }

    private void OnDay(int day)
    {
        if (sim.Ended) return;

        AgeAndStage();
        ResolveAgeDeaths(day);
        AdvancePregnancies();
        StartNewPregnancies(day);
    }

    // ── Aging + life-stage transitions (Ch.11.2) ─────────────────────────────────
    private void AgeAndStage()
    {
        var agents = sim.Agents;
        for (int i = 0; i < agents.Count; i++)
        {
            Agent a = agents[i];
            a.AgeDays += 1f;
            if (a.Stage == LifeStage.Child && a.AgeDays >= MaturationDays)
                a.Stage = LifeStage.Adult;
            else if (a.Stage == LifeStage.Adult && a.AgeDays >= ElderDays)
                a.Stage = LifeStage.Elder;
        }
    }

    // Death by age is a probability ramp past the elder/expectancy threshold (Ch.11.3).
    private void ResolveAgeDeaths(int day)
    {
        List<Agent> dying = null;
        var agents = sim.Agents;
        for (int i = 0; i < agents.Count; i++)
        {
            Agent a = agents[i];
            if (a.Stage != LifeStage.Elder || a.AgeDays < LifeExpectancyDays) continue;
            // Chance climbs the further past expectancy the agent is.
            float over = (a.AgeDays - LifeExpectancyDays) / 20f;
            float chance = AgeDeathChancePerDay * (1f + over);
            if (rng.NextDouble() < chance)
            {
                if (dying == null) dying = new List<Agent>();
                dying.Add(a);
            }
        }
        if (dying != null)
            for (int i = 0; i < dying.Count; i++)
                sim.KillAgent(dying[i], DeathCause.OldAge);
    }

    // ── Gestation -> birth (Ch.11.1) ─────────────────────────────────────────────
    private void AdvancePregnancies()
    {
        // Collect births first (spawning mutates sim.Agents) then apply.
        List<Pregnancy> delivered = null;
        for (int i = pregnancies.Count - 1; i >= 0; i--)
        {
            Pregnancy p = pregnancies[i];
            if (p.Mother == null || !p.Mother.IsAlive) { pregnancies.RemoveAt(i); continue; }
            p.DaysLeft--;
            if (p.DaysLeft <= 0)
            {
                if (delivered == null) delivered = new List<Pregnancy>();
                delivered.Add(p);
                pregnancies.RemoveAt(i);
            }
        }
        if (delivered != null)
            for (int i = 0; i < delivered.Count; i++)
                GiveBirth(delivered[i]);
    }

    private void GiveBirth(Pregnancy p)
    {
        Agent mother = p.Mother;
        Agent father = p.Father; // may have died during gestation; skills fall back to mother

        Agent child = sim.AddAgent(mother.CellX, mother.CellZ);
        child.Civ       = mother.Civ;
        child.Sex       = rng.Next(2) == 0 ? Sex.Male : Sex.Female;
        child.Stage     = LifeStage.Child;
        child.AgeDays   = 0f;
        child.BirthTick = (int)sim.Clock.TotalTicks;
        child.MotherId  = mother.Id;
        child.FatherId  = father != null ? father.Id : 0;

        // Skill-averaging at conception + mutation jitter (Ch.11.1). Family-trade tendency
        // (Ch.12.4): the child takes a parent's trade as a default, not a rule.
        child.SkillFarming = Inherit(mother.SkillFarming, father != null ? father.SkillFarming : mother.SkillFarming);
        child.SkillCombat  = Inherit(mother.SkillCombat,  father != null ? father.SkillCombat  : mother.SkillCombat);
        child.Job          = (father != null && rng.Next(2) == 0) ? father.Job : mother.Job;

        sim.Log.Record(EventType.Birth, EventSignificance.Personal,
            mother.Civ + " child #" + child.Id + " born to #" + mother.Id +
            (father != null ? " and #" + father.Id : ""),
            civA: child.Civ, actorId: mother.Id, subjectId: child.Id,
            cellX: child.CellX, cellZ: child.CellZ);

        sim.EmitAgentBorn(child); // the bridge/harness gives the child a behavior + view
    }

    private float Inherit(float a, float b)
    {
        float avg = (a + b) * 0.5f;
        float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * SkillJitter;
        float v = avg + jitter;
        if (v < 0f) v = 0f; else if (v > 100f) v = 100f;
        return v;
    }

    // ── Pairing (Ch.11.1 / 12) ───────────────────────────────────────────────────
    private void StartNewPregnancies(int day)
    {
        foreach (CivState civ in sim.Civs)
        {
            if (civ.Id == CivId.None) continue;
            if (sim.CountLivingAgents(civ.Id) >= MaxAgentsPerCiv) continue;

            Agent mother = FindFertile(civ.Id, Sex.Female, day);
            if (mother == null) continue;
            Agent father = FindFertile(civ.Id, Sex.Male, day, exclude: mother);
            if (father == null) continue;

            // Same camp: parents must be near each other to pair.
            float dx = mother.PosX - father.PosX, dz = mother.PosZ - father.PosZ;
            if (dx * dx + dz * dz > 40f * 40f) continue;

            pregnancies.Add(new Pregnancy { Mother = mother, Father = father, DaysLeft = GestationDays });
            lastBirthDay[mother.Id] = day; // cooldown starts at conception
        }
    }

    private Agent FindFertile(CivId civ, Sex sex, int day, Agent exclude = null)
    {
        var agents = sim.Agents;
        for (int i = 0; i < agents.Count; i++)
        {
            Agent a = agents[i];
            if (a == exclude || a.Civ != civ || a.Sex != sex || !a.IsAdult || !a.IsAlive) continue;
            if (a.Stage == LifeStage.Elder) continue;
            if (a.Conscripted) continue;
            if (a.Hunger >= FedThreshold || a.Thirst >= FedThreshold) continue;
            if (IsPregnant(a)) continue;
            int last;
            if (lastBirthDay.TryGetValue(a.Id, out last) && day - last < ReproCooldownDays) continue;
            return a;
        }
        return null;
    }

    private bool IsPregnant(Agent a)
    {
        for (int i = 0; i < pregnancies.Count; i++)
            if (pregnancies[i].Mother == a) return true;
        return false;
    }
}
