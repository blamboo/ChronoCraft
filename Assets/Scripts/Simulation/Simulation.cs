// Simulation.cs
// Version: 0.8 (added StorageNodes list + AddStorageNode; StructureNode now takes StructureType)
// Purpose: Plain-C# sim root. Owns Civs, Agents, ResourceNodes, StructureNodes,
//          StorageNodes, and AgentBehaviors. Advances all per fixed step.
// Location: Assets/Scripts/Simulation/Simulation.cs
// Dependencies: System; System.Collections.Generic; SimulationClock; Agent; CivId/CivState;
//               ResourceNode; StructureNode; StorageNode; AgentBehavior; GridData.
// Events emitted: OnTick; OnDayChanged(int).

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

    public double SecondsPerTick { get; }

    public event Action OnTick;
    public event Action<int> OnDayChanged;

    private double tickAccumulator;

    public Simulation(int ticksPerDay, double secondsPerDay)
    {
        Clock = new SimulationClock(ticksPerDay);
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
        Agents.Add(a);
        return a;
    }

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

        tickAccumulator += dt;
        while (tickAccumulator >= SecondsPerTick)
        {
            tickAccumulator -= SecondsPerTick;
            bool newDay = Clock.Advance();
            OnTick?.Invoke();
            if (newDay) OnDayChanged?.Invoke(Clock.Day);
        }
    }
}
