// Simulation.cs
// Version: 0.4 (added ResourceNodes list and AddResourceNode)
// Purpose: Plain-C# root of the TimeCraft prototype simulation. Runs on a single
//          continuous game-time source advanced in fixed steps. Two INDEPENDENT cadences
//          derive from it: continuous agent movement (every step) and a discrete
//          day/stat tick (every SecondsPerTick of game-time) for over-time stat drains.
//          Owns Agents and ResourceNodes as plain sim state.
//          Contains no MonoBehaviour and no UnityEngine beyond value types.
// Location: Assets/Scripts/Simulation/Simulation.cs
// Dependencies: System (Action); System.Collections.Generic; SimulationClock; Agent;
//               ResourceNode.
// Events emitted: OnTick (each derived logical tick); OnDayChanged(int newDay).
// Events consumed: none. Advanced externally by SimulationRunner in fixed steps.

using System;
using System.Collections.Generic;

public class Simulation
{
    public SimulationClock Clock { get; }

    public List<Agent>        Agents        { get; } = new List<Agent>();
    public List<ResourceNode> ResourceNodes { get; } = new List<ResourceNode>();

    // Game-seconds per logical tick = secondsPerDay / ticksPerDay.
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
        var agent = new Agent(startX, startZ);
        Agents.Add(agent);
        return agent;
    }

    public ResourceNode AddResourceNode(ResourceType type, int cellX, int cellZ, int amount)
    {
        var node = new ResourceNode(type, cellX, cellZ, amount);
        ResourceNodes.Add(node);
        return node;
    }

    // Advances the sim by dt game-seconds. Movement is continuous (every step); logical
    // ticks fire whenever SecondsPerTick of game-time has accumulated.
    public void Advance(double dt)
    {
        // Continuous systems.
        for (int i = 0; i < Agents.Count; i++)
            Agents[i].Advance((float)dt);

        // Discrete day/stat tick cadence.
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
