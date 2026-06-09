// Simulation.cs
// Version: 0.5 (added GathererBehaviors list; Update called in Advance)
// Purpose: Plain-C# sim root. Owns Agents, ResourceNodes, and GathererBehaviors.
//          Advances all three every fixed step. Movement is continuous; stat ticks
//          and harvesting are discrete (derived from accumulated game-time).
// Location: Assets/Scripts/Simulation/Simulation.cs
// Dependencies: System; System.Collections.Generic; SimulationClock; Agent;
//               ResourceNode; GathererBehavior; GridData.
// Events emitted: OnTick; OnDayChanged(int). Consumed: none.

using System;
using System.Collections.Generic;

public class Simulation
{
    public SimulationClock    Clock             { get; }
    public List<Agent>        Agents            { get; } = new List<Agent>();
    public List<ResourceNode> ResourceNodes     { get; } = new List<ResourceNode>();
    public List<GathererBehavior> GathererBehaviors { get; } = new List<GathererBehavior>();

    public double SecondsPerTick { get; }

    public event Action OnTick;
    public event Action<int> OnDayChanged;

    private double tickAccumulator;

    public Simulation(int ticksPerDay, double secondsPerDay)
    {
        Clock = new SimulationClock(ticksPerDay);
        SecondsPerTick = Math.Max(0.0001, secondsPerDay) / Math.Max(1, ticksPerDay);
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

    public GathererBehavior AddGathererBehavior(Agent agent, GridData grid,
                                                ResourceType targetType = ResourceType.Wood)
    {
        var b = new GathererBehavior(agent, this, grid, targetType);
        GathererBehaviors.Add(b);
        return b;
    }

    // Advance order within each fixed step:
    //   1. Agent movement (continuous).
    //   2. Behavior state-machine updates (arrival checks, seek throttle).
    //   3. Tick cadence (fires OnTick, which behavior.OnTick catches for harvesting).
    // This ordering means an agent that arrives at a node this step can harvest on the
    // very first tick that fires in the same step.
    public void Advance(double dt)
    {
        for (int i = 0; i < Agents.Count; i++)
            Agents[i].Advance((float)dt);

        for (int i = 0; i < GathererBehaviors.Count; i++)
            GathererBehaviors[i].Update((float)dt);

        tickAccumulator += dt;
        while (tickAccumulator >= SecondsPerTick)
        {
            tickAccumulator -= SecondsPerTick;
            bool newDay = Clock.Advance();
            OnTick?.Invoke();
            if (newDay)
                OnDayChanged?.Invoke(Clock.Day);
        }
    }
}
