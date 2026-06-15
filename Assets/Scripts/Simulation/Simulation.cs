// Simulation.cs
// Version: 0.9 (Prototype v5: the TrueLog; OnStep/OnAgentBorn/OnAgentDied/OnEnded hooks;
//               agent Ids; central KillAgent; conquest end-state)
// Purpose: Plain-C# sim root. Owns Civs, Agents, ResourceNodes, StructureNodes,
//          StorageNodes, AgentBehaviors, and the TrueLog. Advances all per fixed step.
//          Provides the life-cycle spine the v5 systems hang off: OnStep (continuous,
//          for the raid/army movement), OnAgentBorn/OnAgentDied (so the Unity bridge can
//          spawn/despawn views), and KillAgent (the single death path — logs the Death
//          event, frees the home, removes the agent + its behavior, fires OnAgentDied).
// Location: Assets/Scripts/Simulation/Simulation.cs
// Dependencies: System; System.Collections.Generic; SimulationClock; Agent; CivId/CivState;
//               ResourceNode; StructureNode; StorageNode; AgentBehavior; TrueLog; GridData.
// Events emitted: OnTick; OnDayChanged(int); OnStep(float dt); OnAgentBorn(Agent);
//                 OnAgentDied(Agent); OnEnded.

using System;
using System.Collections.Generic;

public class Simulation
{
    public SimulationClock     Clock          { get; }
    public List<CivState>      Civs           { get; } = new List<CivState>();
    public List<Agent>         Agents         { get; } = new List<Agent>();
    public List<ResourceNode>  ResourceNodes  { get; } = new List<ResourceNode>();
    public List<StructureNode> StructureNodes { get; } = new List<StructureNode>();
    public List<StorageNode>   StorageNodes   { get; } = new List<StorageNode>();
    public List<AgentBehavior> AgentBehaviors { get; } = new List<AgentBehavior>();

    // The world's narrative record (Ch.4). Systems Record() events as agents act.
    public TrueLog Log { get; }

    public double SecondsPerTick { get; }

    public event Action OnTick;
    public event Action<int> OnDayChanged;
    // Per fixed-step hook (continuous dt) — the raid/army layer drives movement here.
    public event Action<float> OnStep;
    // Life-cycle hooks so presentation (Unity views) can react without polling.
    public event Action<Agent> OnAgentBorn;
    public event Action<Agent> OnAgentDied;
    // Fired once when the world reaches a conquest end-state (v2 exit criterion).
    public event Action OnEnded;

    public bool  Ended      { get; private set; }
    public CivId WinningCiv { get; private set; } = CivId.None;

    private double tickAccumulator;
    private int    nextAgentId = 1;

    public Simulation(int ticksPerDay, double secondsPerDay)
    {
        Clock = new SimulationClock(ticksPerDay);
        Log   = new TrueLog(Clock);
        SecondsPerTick = Math.Max(0.0001, secondsPerDay) / Math.Max(1, ticksPerDay);
    }

    public CivState RegisterCiv(CivId id, int anchorX, int anchorZ)
    {
        var c = new CivState(id, anchorX, anchorZ);
        Civs.Add(c);
        return c;
    }

    public Agent AddAgent(int startX, int startZ)
    {
        var a = new Agent(startX, startZ);
        a.Id = nextAgentId++;
        Agents.Add(a);
        return a;
    }

    // Raise OnAgentBorn for a child the LineageSystem has fully populated (civ, sex,
    // parents, skills). Kept separate from AddAgent so founder spawns do not fire births.
    public void EmitAgentBorn(Agent child) => OnAgentBorn?.Invoke(child);

    public ResourceNode AddResourceNode(ResourceType type, int cellX, int cellZ, int amount)
    {
        var n = new ResourceNode(type, cellX, cellZ, amount);
        ResourceNodes.Add(n);
        return n;
    }

    // StructureNode now requires a StructureType (Dwelling / Storage / Farm).
    public StructureNode AddStructureNode(StructureType type, CivId civ, int cellX, int cellZ,
                                          int woodRequired, float buildDurationSeconds)
    {
        var s = new StructureNode(type, civ, cellX, cellZ, woodRequired, buildDurationSeconds);
        StructureNodes.Add(s);
        return s;
    }

    public StorageNode AddStorageNode(CivId civ, int cellX, int cellZ)
    {
        var s = new StorageNode(civ, cellX, cellZ);
        StorageNodes.Add(s);
        return s;
    }

    public AgentBehavior AddAgentBehavior(Agent agent, GridData grid)
    {
        var b = new AgentBehavior(agent, this, grid);
        AgentBehaviors.Add(b);
        return b;
    }

    public void Advance(double dt)
    {
        for (int i = 0; i < Agents.Count; i++)
            Agents[i].Advance((float)dt);

        for (int i = 0; i < AgentBehaviors.Count; i++)
            AgentBehaviors[i].Update((float)dt);

        // Continuous-cadence systems (raid/army movement) run after the civilian brain.
        OnStep?.Invoke((float)dt);

        tickAccumulator += dt;
        while (tickAccumulator >= SecondsPerTick)
        {
            tickAccumulator -= SecondsPerTick;
            bool newDay = Clock.Advance();
            OnTick?.Invoke();
            if (newDay) OnDayChanged?.Invoke(Clock.Day);
        }
    }

    // ── Death: the single path every cause of death routes through ───────────────
    // Logs the Death event (schema v1), frees the agent's home slot, disposes and removes
    // its behavior, removes it from the roster, and fires OnAgentDied. killerCiv/causeEventId
    // attribute combat deaths to the slayer and the battle that caused them (Ch.4.4).
    public void KillAgent(Agent a, DeathCause cause,
                          CivId killerCiv = CivId.None, int causeEventId = 0)
    {
        if (a == null || !a.IsAlive) return;
        a.IsAlive = false;

        Log.Record(EventType.Death, EventSignificance.Personal,
                   DescribeAgent(a) + " " + DeathVerb(cause) + ".",
                   civA: a.Civ, civB: killerCiv,
                   actorId: a.Id, cellX: a.CellX, cellZ: a.CellZ,
                   cause: cause, causeEventId: causeEventId);

        if (a.Home != null) a.Home.RemoveResident();

        for (int i = AgentBehaviors.Count - 1; i >= 0; i--)
            if (AgentBehaviors[i].OwnerAgent == a)
            {
                AgentBehaviors[i].Dispose();
                AgentBehaviors.RemoveAt(i);
            }
        Agents.Remove(a);

        OnAgentDied?.Invoke(a);
    }

    // Declare the world conquered by victor (the v2 exit criterion). Idempotent.
    public void DeclareConquest(CivId victor)
    {
        if (Ended) return;
        Ended      = true;
        WinningCiv = victor;
        OnEnded?.Invoke();
    }

    public int CountLivingAgents(CivId civ)
    {
        int n = 0;
        for (int i = 0; i < Agents.Count; i++)
            if (Agents[i].Civ == civ && Agents[i].IsAlive) n++;
        return n;
    }

    public static string DescribeAgent(Agent a) =>
        a.Civ + " " + a.Job + " #" + a.Id;

    static string DeathVerb(DeathCause cause)
    {
        switch (cause)
        {
            case DeathCause.Starvation:  return "starved to death";
            case DeathCause.Dehydration: return "died of thirst";
            case DeathCause.OldAge:      return "died of old age";
            case DeathCause.Combat:      return "was slain in battle";
            default:                     return "died";
        }
    }
}
